Imports System
Imports System.Diagnostics
Imports System.IO
Imports System.Linq
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Collections.Generic

''' <summary>
''' FFmpeg helpers for video editing: reads any video (MP4, MOV, MKV…) into frames for the video model,
''' and converts results to MP4. FFmpeg is downloaded into Engine\ffmpeg on first use.
''' </summary>
Public Module VideoTools

    Private Function Package() As ExternalTools.ToolPackage
        Return New ExternalTools.ToolPackage With {.Name = "ffmpeg", .Flavor = "win64", .Repo = "BtbN/FFmpeg-Builds",
                                                   .ExeName = "ffmpeg.exe", .AssetPatterns = {"^ffmpeg-master-latest-win64-gpl\.zip$"}}
    End Function

    ''' <summary>Path to ffmpeg, installing it when missing (on non-Windows the system ffmpeg is used).</summary>
    Public Async Function EnsureFfmpegAsync(progress As Action(Of String), ct As CancellationToken) As Task(Of String)
        Dim p = Package()
        Dim exe = ExternalTools.FindExe(p)
        If exe IsNot Nothing Then Return exe
        If Not OperatingSystem.IsWindows() Then
            For Each d In (If(Environment.GetEnvironmentVariable("PATH"), "")).Split(Path.PathSeparator)
                Dim f = Path.Combine(d, "ffmpeg")
                If File.Exists(f) Then Return f
            Next
        End If
        Await ExternalTools.InstallAsync(p, progress, ct)
        Return ExternalTools.FindExe(p)
    End Function

    Public Async Function UpdateAsync(progress As Action(Of String), ct As CancellationToken) As Task
        If Not FfmpegInstalled() OrElse Not OperatingSystem.IsWindows() Then Return
        Await ExternalTools.InstallAsync(Package(), progress, ct, onlyIfNewer:=True)
    End Function

    Public Function FfmpegInstalled() As Boolean
        Return ExternalTools.FindExe(Package()) IsNot Nothing
    End Function

    ''' <summary>
    ''' Writes up to <paramref name="maxFrames"/> frames at <paramref name="fps"/>, scaled and centre-cropped to
    ''' width × height, into a new temporary folder (frame_0001.png …). The caller deletes the folder.
    ''' </summary>
    Public Async Function ExtractFramesAsync(video As String, fps As Integer, width As Integer, height As Integer, maxFrames As Integer,
                                             progress As Action(Of String), ct As CancellationToken) As Task(Of String)
        Dim ff = Await EnsureFfmpegAsync(progress, ct)
        Dim dir = Path.Combine(Path.GetTempPath(), "localmind-frames-" & Guid.NewGuid().ToString("N").Substring(0, 8))
        Directory.CreateDirectory(dir)
        Dim vf = $"fps={fps},scale={width}:{height}:force_original_aspect_ratio=increase,crop={width}:{height}"
        Await RunAsync(ff, $"-hide_banner -loglevel error -y -i ""{video}"" -vf ""{vf}"" -frames:v {maxFrames} ""{Path.Combine(dir, "frame_%04d.png")}""", ct)
        If Not Directory.EnumerateFiles(dir, "*.png").Any() Then
            Try : Directory.Delete(dir, True) : Catch : End Try
            Throw New Exception(Localizer.T("Could not read any frames from the video."))
        End If
        Return dir
    End Function

    ''' <summary>Converts a WebM result to MP4 (H.264 + AAC) next to it and returns the new path.</summary>
    Public Async Function ToMp4Async(webm As String, progress As Action(Of String), ct As CancellationToken) As Task(Of String)
        Dim ff = Await EnsureFfmpegAsync(progress, ct)
        Dim mp4 = Path.ChangeExtension(webm, ".mp4")
        Await RunAsync(ff, $"-hide_banner -loglevel error -y -i ""{webm}"" -c:v libx264 -pix_fmt yuv420p -crf 18 -c:a aac -movflags +faststart ""{mp4}""", ct)
        Return mp4
    End Function

    ''' <summary>Any recording (m4a, mp3, webm, wav…) → 16 kHz mono WAV for Whisper.</summary>
    Public Async Function ToSpeechWavAsync(input As String, output As String, ct As CancellationToken) As Task
        Dim ff = Await EnsureFfmpegAsync(Nothing, ct)
        Await RunAsync(ff, $"-hide_banner -loglevel error -y -i ""{input}"" -ar 16000 -ac 1 -c:a pcm_s16le ""{output}""", ct)
    End Function

    ''' <summary>Length of a video or audio file in seconds (0 if unknown).</summary>
    Public Async Function DurationAsync(file As String, ct As CancellationToken) As Task(Of Double)
        Dim ff = Await EnsureFfmpegAsync(Nothing, ct)
        Dim psi As New ProcessStartInfo(ff, $"-hide_banner -i ""{file}""") With {.UseShellExecute = False, .CreateNoWindow = True, .RedirectStandardError = True}
        Using p = Process.Start(psi)
            Dim err = Await p.StandardError.ReadToEndAsync(ct)
            Await p.WaitForExitAsync(ct)
            Dim m = Text.RegularExpressions.Regex.Match(err, "Duration:\s*(\d+):(\d+):(\d+(?:\.\d+)?)")
            If Not m.Success Then Return 0
            Return CInt(m.Groups(1).Value) * 3600 + CInt(m.Groups(2).Value) * 60 + Double.Parse(m.Groups(3).Value, Globalization.CultureInfo.InvariantCulture)
        End Using
    End Function

    ''' <summary>
    ''' Puts a soundtrack (music or a voice-over) under a video and saves an MP4. With <paramref name="keepOriginal"/>
    ''' the video's own sound is mixed in (quieter). The result is as long as the video.
    ''' </summary>
    Public Async Function AddAudioAsync(video As String, audio As String, keepOriginal As Boolean, volume As Double,
                                        output As String, progress As Action(Of String), ct As CancellationToken) As Task
        Dim ff = Await EnsureFfmpegAsync(progress, ct)
        Dim vol = volume.ToString("0.##", Globalization.CultureInfo.InvariantCulture)
        Dim filter = If(keepOriginal,
            $"[1:a]volume={vol},apad[s];[0:a]volume=0.5[o];[o][s]amix=inputs=2:duration=first:dropout_transition=0[a]",
            $"[1:a]volume={vol},apad[a]")
        Dim args = $"-hide_banner -loglevel error -y -i ""{video}"" -i ""{audio}"" -filter_complex ""{filter}"" -map 0:v -map ""[a]"" " &
                   $"-c:v libx264 -pix_fmt yuv420p -crf 18 -c:a aac -shortest -movflags +faststart ""{output}"""
        Dim retry = False
        Try
            Await RunAsync(ff, args, ct)
        Catch When keepOriginal
            retry = True        ' the video has no sound of its own: use only the new soundtrack
        End Try
        If retry Then Await AddAudioAsync(video, audio, False, volume, output, progress, ct)
    End Function

    ''' <summary>Size, frame rate and length of a video (0 when unknown).</summary>
    Public Async Function InfoAsync(file As String, ct As CancellationToken) As Task(Of (Width As Integer, Height As Integer, Fps As Double, Seconds As Double, HasAudio As Boolean))
        Dim ff = Await EnsureFfmpegAsync(Nothing, ct)
        Dim psi As New ProcessStartInfo(ff, $"-hide_banner -i ""{file}""") With {.UseShellExecute = False, .CreateNoWindow = True, .RedirectStandardError = True}
        Using p = Process.Start(psi)
            Dim err = Await p.StandardError.ReadToEndAsync(ct)
            Await p.WaitForExitAsync(ct)
            Dim inv = Globalization.CultureInfo.InvariantCulture
            Dim w = 0, h = 0, fps = 0.0, secs = 0.0
            Dim vline = err.Split({vbLf}, StringSplitOptions.None).FirstOrDefault(Function(l) l.Contains("Video:"))
            If vline IsNot Nothing Then
                Dim m = Text.RegularExpressions.Regex.Match(vline, ",\s*(\d{2,5})x(\d{2,5})")
                If m.Success Then w = CInt(m.Groups(1).Value) : h = CInt(m.Groups(2).Value)
                Dim f = Text.RegularExpressions.Regex.Match(vline, "([\d.]+)\s*fps")
                If Not f.Success Then f = Text.RegularExpressions.Regex.Match(vline, "([\d.]+)\s*tbr")
                If f.Success Then Double.TryParse(f.Groups(1).Value, Globalization.NumberStyles.Float, inv, fps)
            End If
            Dim d = Text.RegularExpressions.Regex.Match(err, "Duration:\s*(\d+):(\d+):(\d+(?:\.\d+)?)")
            If d.Success Then secs = CInt(d.Groups(1).Value) * 3600 + CInt(d.Groups(2).Value) * 60 + Double.Parse(d.Groups(3).Value, inv)
            Return (w, h, If(fps > 0, fps, 16), secs, err.Contains("Audio:"))
        End Using
    End Function

    ''' <summary>Saves the last frame of a video as a PNG (the start of the next shot).</summary>
    Public Async Function LastFrameAsync(video As String, ct As CancellationToken) As Task(Of String)
        Dim ff = Await EnsureFfmpegAsync(Nothing, ct)
        Dim png = Path.Combine(Path.GetTempPath(), $"localmind-last-{Guid.NewGuid():N}.png")
        Try
            Await RunAsync(ff, $"-hide_banner -loglevel error -y -sseof -1 -i ""{video}"" -update 1 ""{png}""", ct)
        Catch ex As OperationCanceledException
            Throw
        Catch
        End Try
        If Not File.Exists(png) Then
            ' very short clips: read the whole clip and keep the last frame
            Await RunAsync(ff, $"-hide_banner -loglevel error -y -i ""{video}"" -update 1 ""{png}""", ct)
        End If
        If Not File.Exists(png) Then Throw New Exception(Localizer.T("Could not read any frames from the video."))
        Return png
    End Function

    ''' <summary>
    ''' Joins clips into one MP4 (no sound), at the size and frame rate of the first one. With
    ''' <paramref name="dropFirstFrame"/> the first frame of every following clip is left out: it repeats the
    ''' last frame of the clip before (each shot starts from it).
    ''' </summary>
    Public Async Function JoinAsync(clips As IList(Of String), output As String, dropFirstFrame As Boolean,
                                    progress As Action(Of String), ct As CancellationToken) As Task
        Dim ff = Await EnsureFfmpegAsync(progress, ct)
        If clips.Count = 0 Then Throw New Exception("Nothing to join.")
        Dim first = Await InfoAsync(clips(0), ct)
        Dim w = If(first.Width > 0, first.Width, 832), h = If(first.Height > 0, first.Height, 480)
        Dim fps = first.Fps.ToString("0.###", Globalization.CultureInfo.InvariantCulture)
        Dim inputs As New StringBuilder()
        Dim filter As New StringBuilder()
        For i = 0 To clips.Count - 1
            inputs.Append($"-i ""{clips(i)}"" ")
            Dim trim = If(dropFirstFrame AndAlso i > 0, "trim=start_frame=1,", "")
            filter.Append($"[{i}:v]{trim}scale={w}:{h}:force_original_aspect_ratio=decrease:force_divisible_by=2,pad={w}:{h}:(ow-iw)/2:(oh-ih)/2,setsar=1,fps={fps},setpts=PTS-STARTPTS[v{i}];")
        Next
        For i = 0 To clips.Count - 1
            filter.Append($"[v{i}]")
        Next
        filter.Append($"concat=n={clips.Count}:v=1:a=0[v]")
        progress?.Invoke("Joining the clips…")
        Await RunAsync(ff, $"-hide_banner -loglevel error -y {inputs}-filter_complex ""{filter}"" -map ""[v]"" -c:v libx264 -pix_fmt yuv420p -crf 18 -movflags +faststart ""{output}""", ct)
    End Function

    ''' <summary>A piece of a recording as 16 kHz mono WAV (what the talking-video model reads).</summary>
    Public Async Function CutAudioAsync(audio As String, start As Double, seconds As Double, output As String, ct As CancellationToken) As Task
        Dim ff = Await EnsureFfmpegAsync(Nothing, ct)
        Dim inv = Globalization.CultureInfo.InvariantCulture
        Await RunAsync(ff, $"-hide_banner -loglevel error -y -ss {start.ToString("0.###", inv)} -t {seconds.ToString("0.###", inv)} -i ""{audio}"" -ar 16000 -ac 1 -c:a pcm_s16le ""{output}""", ct)
    End Function

    ''' <summary>Reads every frame of a video into a new temporary folder (frame_00001.png …). The caller deletes it.</summary>
    Public Async Function AllFramesAsync(video As String, ct As CancellationToken) As Task(Of String)
        Dim ff = Await EnsureFfmpegAsync(Nothing, ct)
        Dim dir = Path.Combine(Path.GetTempPath(), "localmind-frames-" & Guid.NewGuid().ToString("N").Substring(0, 8))
        Directory.CreateDirectory(dir)
        Await RunAsync(ff, $"-hide_banner -loglevel error -y -i ""{video}"" ""{Path.Combine(dir, "frame_%05d.png")}""", ct)
        Return dir
    End Function

    ''' <summary>
    ''' Sharper and / or smoother video: frames enlarged by an AI model (up_00001.png … in framesDir) or a plain 2×
    ''' enlargement, and motion interpolation to twice the frame rate. Keeps the sound. Writes an MP4.
    ''' </summary>
    Public Async Function EnhanceAsync(video As String, framesDir As String, upscale As Boolean, smooth As Boolean,
                                       output As String, progress As Action(Of String), ct As CancellationToken) As Task
        Dim ff = Await EnsureFfmpegAsync(progress, ct)
        Dim info = Await InfoAsync(video, ct)
        Dim inv = Globalization.CultureInfo.InvariantCulture
        Dim fps = info.Fps.ToString("0.###", inv)
        Dim w2 = Math.Max(16, info.Width * 2), h2 = Math.Max(16, info.Height * 2)
        Dim aiFrames = upscale AndAlso framesDir IsNot Nothing
        Dim chain As New List(Of String)
        Dim interp = $"minterpolate=fps={(info.Fps * 2).ToString("0.###", inv)}:mi_mode=mci:mc_mode=aobmc:me_mode=bidir:vsbmc=1"
        ' cheapest order: AI frames (4×) are brought down to 2× first; plain videos get the new frames before enlarging
        If aiFrames Then chain.Add($"scale={w2}:{h2}:flags=lanczos")
        If smooth Then chain.Add(interp)
        If upscale AndAlso Not aiFrames Then chain.Add($"scale={w2}:{h2}:flags=lanczos,unsharp=5:5:0.6:5:5:0.0")
        chain.Add("format=yuv420p")
        Dim src = If(aiFrames, $"-framerate {fps} -i ""{Path.Combine(framesDir, "up_%05d.png")}"" -i ""{video}""", $"-i ""{video}""")
        Dim audio = If(info.HasAudio, If(aiFrames, "-map 1:a? ", "-map 0:a? ") & "-c:a aac ", "")
        progress?.Invoke(If(smooth, "Making the motion smoother…", "Sharpening the video…"))
        Await RunAsync(ff, $"-hide_banner -loglevel error -y {src} -vf ""{String.Join(",", chain)}"" -map 0:v {audio}-c:v libx264 -crf 17 -preset veryfast -movflags +faststart ""{output}""", ct)
    End Function

    ''' <summary>Runs ffmpeg with the given arguments (optionally in a folder, so files can be named without a path).</summary>
    Public Async Function FfmpegAsync(args As String, ct As CancellationToken, Optional workDir As String = Nothing) As Task
        Dim ff = Await EnsureFfmpegAsync(Nothing, ct)
        Await RunAsync(ff, args, ct, workDir)
    End Function

    ''' <summary>Joins clips that all have sound (e.g. talking pieces) into one MP4, picture and sound.</summary>
    Public Async Function JoinWithSoundAsync(clips As IList(Of String), output As String, ct As CancellationToken) As Task
        Dim ff = Await EnsureFfmpegAsync(Nothing, ct)
        If clips.Count = 0 Then Throw New Exception("Nothing to join.")
        Dim first = Await InfoAsync(clips(0), ct)
        Dim w = If(first.Width > 0, first.Width, 832), h = If(first.Height > 0, first.Height, 480)
        Dim fps = first.Fps.ToString("0.###", Globalization.CultureInfo.InvariantCulture)
        Dim inputs As New StringBuilder()
        Dim filter As New StringBuilder()
        Dim pairs As New StringBuilder()
        For i = 0 To clips.Count - 1
            Dim info = Await InfoAsync(clips(i), ct)
            inputs.Append($"-i ""{clips(i)}"" ")
            filter.Append($"[{i}:v]scale={w}:{h}:force_original_aspect_ratio=decrease:force_divisible_by=2,pad={w}:{h}:(ow-iw)/2:(oh-ih)/2,setsar=1,fps={fps},setpts=PTS-STARTPTS[v{i}];")
            If info.HasAudio Then
                filter.Append($"[{i}:a]aresample=44100,aformat=channel_layouts=stereo,asetpts=PTS-STARTPTS[a{i}];")
            Else
                filter.Append($"anullsrc=r=44100:cl=stereo,atrim=duration={info.Seconds.ToString("0.###", Globalization.CultureInfo.InvariantCulture)}[a{i}];")
            End If
            pairs.Append($"[v{i}][a{i}]")
        Next
        filter.Append(pairs).Append($"concat=n={clips.Count}:v=1:a=1[v][a]")
        Await RunAsync(ff, $"-hide_banner -loglevel error -y {inputs}-filter_complex ""{filter}"" -map ""[v]"" -map ""[a]"" -c:v libx264 -pix_fmt yuv420p -crf 18 -c:a aac -movflags +faststart ""{output}""", ct)
    End Function

    ''' <summary>
    ''' One piece of a film, made uniform (size, 24 fps, stereo sound — silent when the clip has none or its sound is
    ''' not wanted), with optional fades. Pictures are shown for <paramref name="seconds"/>.
    ''' </summary>
    Public Async Function FilmSegmentAsync(source As String, isPicture As Boolean, seconds As Double, width As Integer, height As Integer,
                                           keepSound As Boolean, fades As Boolean, output As String, ct As CancellationToken) As Task(Of Double)
        Dim ff = Await EnsureFfmpegAsync(Nothing, ct)
        Dim inv = Globalization.CultureInfo.InvariantCulture
        Dim dur = seconds
        Dim hasSound = False
        If Not isPicture Then
            Dim info = Await InfoAsync(source, ct)
            dur = If(info.Seconds > 0, info.Seconds, 3)
            hasSound = info.HasAudio AndAlso keepSound
        End If
        dur = Math.Max(0.5, dur)
        Dim d = dur.ToString("0.###", inv)
        Dim vf = $"scale={width}:{height}:force_original_aspect_ratio=decrease:force_divisible_by=2,pad={width}:{height}:(ow-iw)/2:(oh-ih)/2,setsar=1,fps=24,format=yuv420p"
        If fades Then
            Dim f = Math.Min(0.4, dur / 4).ToString("0.###", inv)
            vf &= $",fade=t=in:st=0:d={f},fade=t=out:st={(dur - Math.Min(0.4, dur / 4)).ToString("0.###", inv)}:d={f}"
        End If
        Dim src = If(isPicture, $"-loop 1 -t {d} -i ""{source}""", $"-i ""{source}""")
        Dim audio = If(hasSound, "[0:a]aresample=44100,aformat=channel_layouts=stereo,apad[a]", "[1:a]anull[a]")
        Await RunAsync(ff, $"-hide_banner -loglevel error -y {src} -f lavfi -t {d} -i anullsrc=r=44100:cl=stereo " &
                           $"-filter_complex ""[0:v]{vf}[v];{audio}"" -map ""[v]"" -map ""[a]"" -t {d} " &
                           $"-c:v libx264 -crf 18 -preset veryfast -c:a aac -ar 44100 -ac 2 ""{output}""", ct)
        Return dur
    End Function

    ''' <summary>Joins uniform pieces (same size, rate and sound format) without re-encoding.</summary>
    Public Async Function ConcatSameAsync(pieces As IList(Of String), output As String, ct As CancellationToken) As Task
        Dim ff = Await EnsureFfmpegAsync(Nothing, ct)
        Dim list = Path.Combine(Path.GetTempPath(), $"localmind-concat-{Guid.NewGuid():N}.txt")
        File.WriteAllLines(list, pieces.Select(Function(p) "file '" & p.Replace("\", "/").Replace("'", "'\''") & "'"))
        Try
            Await RunAsync(ff, $"-hide_banner -loglevel error -y -f concat -safe 0 -i ""{list}"" -c copy -movflags +faststart ""{output}""", ct)
        Finally
            Try : File.Delete(list) : Catch : End Try
        End Try
    End Function

    ''' <summary>
    ''' Puts music (looped to the length, faded out at the end) and voice-overs (each at its start second) under a
    ''' film; the film's own sound stays (or is muted).
    ''' </summary>
    Public Async Function MixFilmSoundAsync(film As String, filmSeconds As Double, music As String, musicVolume As Double, keepFilmSound As Boolean,
                                           voiceOvers As IList(Of (File As String, Start As Double)), output As String, ct As CancellationToken) As Task
        Dim ff = Await EnsureFfmpegAsync(Nothing, ct)
        Dim inv = Globalization.CultureInfo.InvariantCulture
        Dim inputs As New StringBuilder($"-i ""{film}"" ")
        Dim filter As New StringBuilder($"[0:a]volume={If(keepFilmSound, "1", "0")}[a0];")
        Dim mixIn As New StringBuilder("[a0]")
        Dim n = 1
        If Not String.IsNullOrEmpty(music) AndAlso File.Exists(music) Then
            inputs.Append($"-stream_loop -1 -i ""{music}"" ")
            Dim fadeAt = Math.Max(0, filmSeconds - 2).ToString("0.###", inv)
            filter.Append($"[{n}:a]aresample=44100,aformat=channel_layouts=stereo,volume={musicVolume.ToString("0.##", inv)},afade=t=out:st={fadeAt}:d=2[m];")
            mixIn.Append("[m]")
            n += 1
        End If
        Dim k = 0
        For Each vo In voiceOvers.Where(Function(x) File.Exists(x.File))
            inputs.Append($"-i ""{vo.File}"" ")
            Dim ms = CLng(Math.Max(0, vo.Start) * 1000)
            filter.Append($"[{n}:a]aresample=44100,aformat=channel_layouts=stereo,adelay={ms}|{ms}[vo{k}];")
            mixIn.Append($"[vo{k}]")
            n += 1 : k += 1
        Next
        filter.Append(mixIn).Append($"amix=inputs={n}:duration=first:normalize=0[a]")
        Await RunAsync(ff, $"-hide_banner -loglevel error -y {inputs}-filter_complex ""{filter}"" -map 0:v -map ""[a]"" -c:v copy -c:a aac -b:a 192k -movflags +faststart ""{output}""", ct)
    End Function

    ''' <summary>
    ''' Adds subtitles to a video: burned into the picture (always visible), or as a subtitle track players can turn
    ''' on and off. Writes an MP4.
    ''' </summary>
    Public Async Function AddSubtitlesAsync(video As String, srt As String, burn As Boolean, language As String, output As String, ct As CancellationToken) As Task
        Dim ff = Await EnsureFfmpegAsync(Nothing, ct)
        Dim info = Await InfoAsync(video, ct)
        Dim audio = If(info.HasAudio, "-c:a aac ", "")
        If burn Then
            ' run in the subtitle's folder: the filter then needs no Windows path (drive letters and \ break it)
            Dim style = "FontName=Segoe UI,FontSize=20,Outline=2,Shadow=0,MarginV=24"
            Await RunAsync(ff, $"-hide_banner -loglevel error -y -i ""{video}"" -vf ""subtitles={Path.GetFileName(srt)}:force_style='{style}'"" " &
                               $"-c:v libx264 -crf 18 -preset veryfast -pix_fmt yuv420p {audio}-movflags +faststart ""{output}""", ct, Path.GetDirectoryName(srt))
        Else
            Dim lang = If(String.IsNullOrWhiteSpace(language), "und", language)
            Await RunAsync(ff, $"-hide_banner -loglevel error -y -i ""{video}"" -i ""{srt}"" -map 0:v -map 0:a? -map 1:s " &
                               $"-c:v libx264 -crf 18 -preset veryfast -pix_fmt yuv420p {audio}-c:s mov_text -metadata:s:s:0 language={lang} -movflags +faststart ""{output}""", ct)
        End If
    End Function

    Private Async Function RunAsync(exe As String, args As String, ct As CancellationToken, Optional workDir As String = Nothing) As Task
        Dim psi As New ProcessStartInfo(exe, args) With {.UseShellExecute = False, .CreateNoWindow = True,
                                                         .RedirectStandardError = True, .RedirectStandardOutput = True}
        If workDir IsNot Nothing Then psi.WorkingDirectory = workDir
        Using p As New Process() With {.StartInfo = psi}
            Dim err As New StringBuilder()
            AddHandler p.ErrorDataReceived, Sub(s, e)
                                                If e.Data IsNot Nothing Then SyncLock err : err.AppendLine(e.Data) : End SyncLock
                                            End Sub
            p.Start()
            AppCore.Engine.AssignToJob(p)
            p.BeginErrorReadLine()
            p.BeginOutputReadLine()
            Try
                Await p.WaitForExitAsync(ct)
            Catch ex As OperationCanceledException
                Try : p.Kill(True) : Catch : End Try
                Throw
            End Try
            If p.ExitCode <> 0 Then Throw New Exception("ffmpeg: " & err.ToString().Trim())
        End Using
    End Function
End Module
