Imports System
Imports System.Diagnostics
Imports System.IO
Imports System.Linq
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks

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

    Private Async Function RunAsync(exe As String, args As String, ct As CancellationToken) As Task
        Dim psi As New ProcessStartInfo(exe, args) With {.UseShellExecute = False, .CreateNoWindow = True,
                                                         .RedirectStandardError = True, .RedirectStandardOutput = True}
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
