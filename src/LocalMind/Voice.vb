Imports System
Imports System.Diagnostics
Imports System.IO
Imports System.Linq
Imports System.Runtime.InteropServices
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Threading
Imports System.Threading.Tasks

''' <summary>
''' Voice input (microphone → whisper.cpp, fully offline) and voice output (Windows' built-in
''' SAPI voices, including Greek when installed).
''' </summary>
Public Module Voice

    '════════════════ Recording (winmm MCI: no extra libraries) ════════════════
    <DllImport("winmm.dll", CharSet:=CharSet.Unicode)>
    Private Function mciSendString(command As String, buffer As StringBuilder, bufferSize As Integer, hwndCallback As IntPtr) As Integer
    End Function

    Private _recording As Boolean
    Public ReadOnly Property IsRecording As Boolean
        Get
            Return _recording
        End Get
    End Property

    Private Sub Mci(cmd As String)
        Dim err = mciSendString(cmd, Nothing, 0, IntPtr.Zero)
        If err <> 0 Then Throw New Exception("Microphone error " & err & " (" & cmd.Split(" "c)(0) & "). Check that a microphone is connected and allowed in Windows privacy settings.")
    End Sub

    Public Sub StartRecording()
        If _recording Then Return
        Try : mciSendString("close lmrec", Nothing, 0, IntPtr.Zero) : Catch : End Try
        Mci("open new type waveaudio alias lmrec")
        ' 16 kHz mono 16-bit = what Whisper expects.
        Mci("set lmrec time format ms bitspersample 16 channels 1 samplespersec 16000 bytespersec 32000 alignment 2")
        Mci("record lmrec")
        _recording = True
    End Sub

    ''' <summary>Stops recording and returns the WAV file path.</summary>
    Public Function StopRecording() As String
        If Not _recording Then Return Nothing
        _recording = False
        Dim wav = Path.Combine(Path.GetTempPath(), $"localmind-voice-{DateTime.Now:HHmmss}.wav")
        Try
            Mci("stop lmrec")
            Mci($"save lmrec ""{wav}""")
        Finally
            mciSendString("close lmrec", Nothing, 0, IntPtr.Zero)
        End Try
        Return wav
    End Function

    '════════════════ Whisper ════════════════
    Private Function WhisperPackage(flavor As String) As ExternalTools.ToolPackage
        If flavor = "cuda13" Then
            ' whisper.cpp has no Windows x64 CUDA 13 build: LocalMind builds its own (engines-cuda13 release)
            Return New ExternalTools.ToolPackage With {.Name = "whisper", .Flavor = "cuda13", .Repo = Updater.Repo, .ExeName = "whisper-cli.exe",
                                                       .AssetPatterns = {"^whisper-.+-bin-win-cuda13-x64\.zip$"}}
        End If
        Return New ExternalTools.ToolPackage With {
            .Name = "whisper", .Flavor = flavor, .Repo = "ggml-org/whisper.cpp", .ExeName = "whisper-cli.exe",
            .AssetPatterns = If(flavor = "cuda12",
                                {"^whisper-(cublas-12[\d.]*-bin-x64|bin-win-cuda-12[\d.]*-x64)\.zip$"},
                                {"^whisper-(blas-bin-x64|bin-x64)\.zip$"})}
    End Function

    ''' <summary>Updates whisper.cpp builds that are installed, when a newer one exists.</summary>
    Public Async Function UpdateWhisperAsync(progress As Action(Of String), ct As CancellationToken) As Task
        If Not OperatingSystem.IsWindows() Then Return
        For Each flavor In {"cuda13", "cuda12", "cpu"}
            Dim p = WhisperPackage(flavor)
            If ExternalTools.FindExe(p) IsNot Nothing Then Await ExternalTools.InstallAsync(p, progress, ct, onlyIfNewer:=True)
        Next
    End Function

    Private Function PreferredFlavor() As String
        If HardwareInfo.Current().PrimaryVendor <> GpuVendor.Nvidia Then Return "cpu"
        Return If(AppCore.Engine.EffectiveVariant = "cuda13", "cuda13", "cuda12")
    End Function

    ''' <summary>Whisper model by hardware: large-v3-turbo on NVIDIA, small on other PCs.</summary>
    Public Function ModelFile() As String
        Dim chosen = If(AppCore.Settings.VoiceModel, "auto")
        If chosen = "auto" Then
            Dim hw = HardwareInfo.Current()
            chosen = If(hw.PrimaryVendor = GpuVendor.Nvidia AndAlso hw.VramGB >= 4, "large-v3-turbo-q5_0", "small-q5_1")
        End If
        Return "ggml-" & chosen & ".bin"
    End Function

    ''' <summary>Installs whisper.cpp and the speech model on first use; returns the package to run and the model.</summary>
    Private Async Function WhisperReadyAsync(progress As Action(Of String), ct As CancellationToken) As Task(Of (Pkg As ExternalTools.ToolPackage, Model As String))
        Dim flavor = PreferredFlavor()
        Dim pkg = WhisperPackage(flavor)
        If ExternalTools.FindExe(pkg) Is Nothing Then
            Dim gpuInstallFailed = False
            Try
                Await ExternalTools.InstallAsync(pkg, progress, ct)
            Catch ex As Exception When flavor <> "cpu"
                gpuInstallFailed = True
            End Try
            If gpuInstallFailed AndAlso flavor = "cuda13" Then
                ' CUDA 13 build not published yet: the CUDA 12 build
                gpuInstallFailed = False
                flavor = "cuda12"
                pkg = WhisperPackage(flavor)
                Try
                    If ExternalTools.FindExe(pkg) Is Nothing Then Await ExternalTools.InstallAsync(pkg, progress, ct)
                Catch ex As Exception
                    gpuInstallFailed = True
                End Try
            End If
            If gpuInstallFailed Then
                pkg = WhisperPackage("cpu")
                If ExternalTools.FindExe(pkg) Is Nothing Then Await ExternalTools.InstallAsync(pkg, progress, ct)
            End If
        End If
        If pkg.Flavor = "cuda12" Then ExternalTools.ShareCudaRuntime(ExternalTools.ToolDir(pkg))
        Dim model = Path.Combine(AppCore.Library.Folder, "Voice", ModelFile())
        If Not File.Exists(model) Then
            Await Downloader.DownloadFileAsync($"https://huggingface.co/ggerganov/whisper.cpp/resolve/main/{Path.GetFileName(model)}", model, Nothing,
                Sub(d, t, s) progress?.Invoke($"Downloading speech model… {If(t > 0, d * 100 \ t, 0)}%"), ct)
        End If
        Return (pkg, model)
    End Function

    ''' <summary>Transcribes a WAV file. Installs whisper.cpp and the model on first use.</summary>
    Public Async Function TranscribeAsync(wav As String, progress As Action(Of String), ct As CancellationToken) As Task(Of String)
        Dim ready = Await WhisperReadyAsync(progress, ct)
        Dim pkg = ready.Pkg, model = ready.Model
        progress?.Invoke("Transcribing…")
        Dim text = Await RunWhisperAsync(ExternalTools.FindExe(pkg), model, wav, ct)
        If text Is Nothing AndAlso pkg.Flavor <> "cpu" Then
            ' CUDA build could not start (driver/DLLs) → fall back to the CPU build once.
            Dim cpu = WhisperPackage("cpu")
            If ExternalTools.FindExe(cpu) Is Nothing Then Await ExternalTools.InstallAsync(cpu, progress, ct)
            text = Await RunWhisperAsync(ExternalTools.FindExe(cpu), model, wav, ct)
        End If
        If text Is Nothing Then Throw New Exception("Speech recognition failed.")
        Return text
    End Function

    ''' <summary>
    ''' Subtitles with timings (SRT) from a 16 kHz WAV. <paramref name="language"/> = spoken language ("auto" to
    ''' detect); <paramref name="toEnglish"/> makes Whisper write them in English. Returns the SRT file.
    ''' </summary>
    Public Async Function TranscribeSrtAsync(wav As String, language As String, toEnglish As Boolean, progress As Action(Of String), ct As CancellationToken) As Task(Of String)
        Dim ready = Await WhisperReadyAsync(progress, ct)
        progress?.Invoke("Writing subtitles…")
        For Each pkg In {ready.Pkg, WhisperPackage("cpu")}.Distinct()
            Dim exe = ExternalTools.FindExe(pkg)
            If exe Is Nothing Then
                Await ExternalTools.InstallAsync(pkg, progress, ct)
                exe = ExternalTools.FindExe(pkg)
            End If
            If exe Is Nothing Then Continue For
            Dim outBase = Path.Combine(Path.GetTempPath(), "localmind-srt-" & Guid.NewGuid().ToString("N"))
            Dim lang = If(String.IsNullOrWhiteSpace(language), "auto", language)
            Dim psi As New ProcessStartInfo(exe, $"-m ""{ready.Model}"" -f ""{wav}"" -l {lang}{If(toEnglish, " -tr", "")} -t {Math.Max(2, Environment.ProcessorCount - 2)} -np -osrt -of ""{outBase}""") With {
                .UseShellExecute = False, .CreateNoWindow = True, .RedirectStandardOutput = True, .RedirectStandardError = True,
                .WorkingDirectory = Path.GetDirectoryName(exe)}
            Using p = Process.Start(psi)
                Dim o = p.StandardOutput.ReadToEndAsync()
                Dim e = p.StandardError.ReadToEndAsync()
                Await p.WaitForExitAsync(ct)
                Await Task.WhenAll(o, e)
                If p.ExitCode = 0 AndAlso File.Exists(outBase & ".srt") Then Return outBase & ".srt"
            End Using
        Next
        Throw New Exception("Speech recognition failed.")
    End Function

    Private Async Function RunWhisperAsync(exe As String, model As String, wav As String, ct As CancellationToken) As Task(Of String)
        If exe Is Nothing Then Return Nothing
        Dim outBase = Path.Combine(Path.GetTempPath(), "localmind-stt-" & Guid.NewGuid().ToString("N"))
        Dim lang = If(String.IsNullOrWhiteSpace(AppCore.Settings.VoiceLanguage), "auto", AppCore.Settings.VoiceLanguage)
        Dim psi As New ProcessStartInfo(exe, $"-m ""{model}"" -f ""{wav}"" -l {lang} -t {Math.Max(2, Environment.ProcessorCount - 2)} -nt -np -otxt -of ""{outBase}""") With {
            .UseShellExecute = False, .CreateNoWindow = True, .RedirectStandardOutput = True, .RedirectStandardError = True,
            .WorkingDirectory = Path.GetDirectoryName(exe)}
        Using p = Process.Start(psi)
            Dim o = p.StandardOutput.ReadToEndAsync()
            Dim e = p.StandardError.ReadToEndAsync()
            Await p.WaitForExitAsync(ct)
            Await Task.WhenAll(o, e)
            Dim txtFile = outBase & ".txt"
            If p.ExitCode <> 0 OrElse Not File.Exists(txtFile) Then Return Nothing
            Dim text = File.ReadAllText(txtFile).Trim()
            File.Delete(txtFile)
            Return Regex.Replace(text, "\[(BLANK_AUDIO|Music|MUSIC)\]", "").Trim()
        End Using
    End Function

    '════════════════ Text to speech (SAPI) ════════════════
    Private _voice As Object
    Private _speaking As Boolean

    Public ReadOnly Property IsSpeaking As Boolean
        Get
            Return _speaking
        End Get
    End Property

    ''' <summary>Installed Windows voices ("Microsoft Stefanos - Greek", …).</summary>
    Public Function VoiceNames() As String()
        Try
            Dim v = GetVoice()
            Dim tokens = v.GetVoices()
            Dim names As New Collections.Generic.List(Of String)
            For i = 0 To CInt(tokens.Count) - 1
                names.Add(CStr(tokens.Item(i).GetDescription()))
            Next
            Return names.ToArray()
        Catch
            Return Array.Empty(Of String)()
        End Try
    End Function

    Private Function GetVoice() As Object
        If _voice Is Nothing Then _voice = CreateObject("SAPI.SpVoice")
        Return _voice
    End Function

    <DllImport("winmm.dll", CharSet:=CharSet.Unicode)>
    Private Function PlaySound(pszSound As String, hmod As IntPtr, fdwSound As UInteger) As Boolean
    End Function
    Private Const SND_SYNC As UInteger = 0, SND_FILENAME As UInteger = &H20000, SND_NODEFAULT As UInteger = 2

    Private _aiCts As CancellationTokenSource

    ''' <summary>Text made speakable: no code blocks, Markdown symbols or citation marks.</summary>
    Public Function CleanForSpeech(text As String) As String
        Dim clean = Regex.Replace(If(text, ""), "```[\s\S]*?```", " (code) ")
        clean = Regex.Replace(clean, "!\[[^\]]*\]\([^)]*\)", " ")
        clean = Regex.Replace(clean, "\[([^\]]+)\]\([^)]*\)", "$1")
        clean = Regex.Replace(clean, "[#*_`>|]+", " ")
        clean = Regex.Replace(clean, "\[(\w?\d+)\]", "")
        Return Regex.Replace(clean, "\s+", " ").Trim()
    End Function

    Public Function IsGreek(text As String) As Boolean
        Return Regex.Matches(If(text, ""), "[Ͱ-Ͽ]").Count > If(text, "").Length * 0.2
    End Function

    ''' <summary>Splits text into sentence groups of up to ~240 characters (short pieces start playing sooner).</summary>
    Public Function Chunks(text As String) As List(Of String)
        Dim parts = Regex.Split(text, "(?<=[.!?;·…])\s+")
        Dim list As New Collections.Generic.List(Of String)
        Dim cur As New StringBuilder()
        For Each p In parts
            If cur.Length > 0 AndAlso cur.Length + p.Length > 240 Then
                list.Add(cur.ToString().Trim())
                cur.Clear()
            End If
            cur.Append(p).Append(" "c)
        Next
        If cur.ToString().Trim().Length > 0 Then list.Add(cur.ToString().Trim())
        Return list
    End Function

    ''' <summary>
    ''' Reads text with an AI voice (Settings: ReadAloudModel + ReadAloudVoice): the next sentence is made while the
    ''' current one plays, so long answers start quickly.
    ''' </summary>
    Public Function SpeakAiAsync(text As String, Optional onStatus As Action(Of String) = Nothing) As Task
        StopSpeaking()
        Dim m = AudioStudio.Models().FirstOrDefault(Function(x) x.Id = AppCore.Settings.ReadAloudModel AndAlso Not x.IsMusic)
        If m Is Nothing Then Return Task.CompletedTask
        Dim cts As New CancellationTokenSource()
        _aiCts = cts
        Dim ct = cts.Token
        Dim clean = CleanForSpeech(text)
        Dim saved = VoiceLibrary.Find(AppCore.Settings.ReadAloudVoice)
        Dim lang = If(IsGreek(clean), "el", If(saved?.Language, "en"))
        _speaking = True
        Return Task.Run(Async Function()
                            Try
                                Dim pieces = Chunks(clean)
                                Dim makeOne = Function(s As String) AudioStudio.GenerateAsync(New AudioStudio.AudioRequest With {
                                                  .Model = m, .Text = s, .Language = lang, .SpeakerFile = saved?.FullPath, .SpeakerText = saved?.Words},
                                                  Sub(p, t) onStatus?.Invoke(t), ct)
                                Dim nextWav = If(pieces.Count > 0, makeOne(pieces(0)), Nothing)
                                For i = 0 To pieces.Count - 1
                                    Dim wav = Await nextWav
                                    nextWav = If(i + 1 < pieces.Count, makeOne(pieces(i + 1)), Nothing)
                                    If ct.IsCancellationRequested Then Exit For
                                    PlaySound(wav, IntPtr.Zero, SND_FILENAME Or SND_SYNC Or SND_NODEFAULT)
                                    Try : IO.File.Delete(wav) : Catch : End Try
                                Next
                            Catch ex As OperationCanceledException
                            Catch ex As Exception
                                onStatus?.Invoke("⚠ " & ex.Message.Split({vbCr, vbLf}, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault())
                            Finally
                                _speaking = False
                            End Try
                        End Function)
    End Function

    ''' <summary>
    ''' Speaks text into a WAV file (for the phone): the AI voice from Settings if chosen (sentence by sentence,
    ''' joined), otherwise a Windows voice.
    ''' </summary>
    Public Async Function SpeakToFileAsync(text As String, ct As CancellationToken) As Task(Of String)
        Dim clean = CleanForSpeech(text)
        Dim out = Path.Combine(Path.GetTempPath(), $"localmind-speak-{Guid.NewGuid():N}.wav")
        Dim m = AudioStudio.Models().FirstOrDefault(Function(x) x.Id = AppCore.Settings.ReadAloudModel AndAlso Not x.IsMusic)
        If m IsNot Nothing Then
            Dim saved = VoiceLibrary.Find(AppCore.Settings.ReadAloudVoice)
            Dim lang = If(IsGreek(clean), "el", If(saved?.Language, "en"))
            Dim parts As New Collections.Generic.List(Of String)
            For Each piece In Chunks(clean)
                parts.Add(Await AudioStudio.GenerateAsync(New AudioStudio.AudioRequest With {
                    .Model = m, .Text = piece, .Language = lang, .SpeakerFile = saved?.FullPath, .SpeakerText = saved?.Words}, Nothing, ct))
            Next
            JoinWavs(parts, out)
            For Each p In parts
                Try : IO.File.Delete(p) : Catch : End Try
            Next
            Return out
        End If
        Await Task.Run(Sub()
                           Dim v = CreateObject("SAPI.SpVoice")
                           Dim fs = CreateObject("SAPI.SpFileStream")
                           fs.Format.Type = 22                 ' 22 kHz 16-bit mono
                           fs.Open(out, 3, False)              ' create for write
                           v.AudioOutputStream = fs
                           If IsGreek(clean) Then
                               Dim tokens = v.GetVoices()
                               For i = 0 To CInt(tokens.Count) - 1
                                   If CStr(tokens.Item(i).GetDescription()).Contains("Greek") Then v.Voice = tokens.Item(i) : Exit For
                               Next
                           End If
                           v.Speak(clean, 0)
                           fs.Close()
                       End Sub, ct)
        Return out
    End Function

    ''' <summary>Joins PCM WAV files of the same format into one.</summary>
    Public Sub JoinWavs(files As Collections.Generic.List(Of String), output As String)
        Dim header As Byte() = Nothing
        Using ms As New MemoryStream()
            For Each f In files
                Dim b = IO.File.ReadAllBytes(f)
                ' Find the "data" chunk.
                Dim i = 12
                While i + 8 <= b.Length
                    Dim id = Encoding.ASCII.GetString(b, i, 4)
                    Dim size = BitConverter.ToInt32(b, i + 4)
                    If id = "data" Then
                        If header Is Nothing Then header = b.Take(i + 8).ToArray()
                        ms.Write(b, i + 8, Math.Min(size, b.Length - i - 8))
                        Exit While
                    End If
                    i += 8 + size + (size And 1)
                End While
            Next
            If header Is Nothing Then Throw New Exception("No audio was made.")
            Dim data = ms.ToArray()
            Array.Copy(BitConverter.GetBytes(header.Length - 8 + data.Length), 0, header, 4, 4)
            Array.Copy(BitConverter.GetBytes(data.Length), 0, header, header.Length - 4, 4)
            Using o = IO.File.Create(output)
                o.Write(header, 0, header.Length)
                o.Write(data, 0, data.Length)
            End Using
        End Using
    End Sub

    ''' <summary>Reads text aloud (async): the chosen AI voice, or Windows voices (Greek voice for Greek text when installed).</summary>
    Public Sub Speak(text As String)
        If Not String.IsNullOrEmpty(AppCore.Settings.ReadAloudModel) AndAlso
           AudioStudio.Models().Any(Function(x) x.Id = AppCore.Settings.ReadAloudModel) Then
            Dim unused = SpeakAiAsync(text)
            Return
        End If
        Try
            Dim v = GetVoice()
            StopSpeaking()
            Dim clean = Regex.Replace(If(text, ""), "```[\s\S]*?```", " (code) ")
            clean = Regex.Replace(clean, "[#*_`>|]+", " ")
            clean = Regex.Replace(clean, "\[(\w?\d+)\]", "")
            Dim isGreek = Regex.Matches(clean, "[Ͱ-Ͽ]").Count > clean.Length * 0.2
            Dim wanted = AppCore.Settings.TtsVoice
            Dim tokens = v.GetVoices()
            For i = 0 To CInt(tokens.Count) - 1
                Dim t = tokens.Item(i)
                Dim desc = CStr(t.GetDescription())
                Dim lang = ""
                Try
                    lang = CStr(t.GetAttribute("Language"))
                Catch
                End Try
                Dim greekVoice = lang.Split(";"c).Any(Function(x) x.Trim().Equals("408", StringComparison.OrdinalIgnoreCase)) OrElse desc.Contains("Greek")
                If (isGreek AndAlso greekVoice) OrElse (Not isGreek AndAlso Not String.IsNullOrEmpty(wanted) AndAlso desc = wanted) Then
                    v.Voice = t
                    Exit For
                End If
            Next
            _speaking = True
            v.Speak(clean, 1)   ' SVSFlagsAsync
            Task.Run(Sub()
                         Try
                             v.WaitUntilDone(-1)
                         Catch
                         End Try
                         _speaking = False
                     End Sub)
        Catch ex As Exception
            _speaking = False
            Throw New Exception("Text-to-speech is not available: " & ex.Message)
        End Try
    End Sub

    Public Sub StopSpeaking()
        Try
            _aiCts?.Cancel()            ' also stops its llama-tts run
            PlaySound(Nothing, IntPtr.Zero, 0)
        Catch
        End Try
        Try
            If _voice IsNot Nothing Then _voice.Speak("", 2)   ' SVSFPurgeBeforeSpeak
        Catch
        End Try
        _speaking = False
    End Sub
End Module
