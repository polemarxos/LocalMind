Imports System
Imports System.IO
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms

''' <summary>💾 Saves a copy of a result. Songs and voice clips can be saved as MP3, as MP4 (with a moving waveform) or as WAV.</summary>
Public Module MediaExport
    Private ReadOnly AudioExt As String() = {".wav", ".mp3", ".flac", ".ogg", ".m4a"}

    Public Function IsAudio(f As String) As Boolean
        Return AudioExt.Contains(Path.GetExtension(If(f, "")).ToLowerInvariant())
    End Function

    ''' <summary>Asks where (and for sound, in which format) and saves. Returns the saved file, or Nothing.</summary>
    Public Async Function ExportAsync(owner As IWin32Window, f As String, status As Action(Of String)) As Task(Of String)
        Dim ext = Path.GetExtension(f).ToLowerInvariant()
        Dim audio = IsAudio(f)
        Using d As New SaveFileDialog()
            d.FileName = Path.GetFileNameWithoutExtension(f) & If(audio, ".mp3", ext)
            If audio Then
                d.Filter = Localizer.T("MP3 sound") & " (*.mp3)|*.mp3|" & Localizer.T("MP4 video with a waveform") & " (*.mp4)|*.mp4|" & Localizer.T("WAV sound (original)") & " (*.wav)|*.wav"
                d.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)
            Else
                d.Filter = $"*{ext}|*{ext}|*.*|*.*"
                d.InitialDirectory = Environment.GetFolderPath(If(ext = ".mp4" OrElse ext = ".webm", Environment.SpecialFolder.MyVideos, Environment.SpecialFolder.MyPictures))
            End If
            If d.ShowDialog(owner) <> DialogResult.OK Then Return Nothing
            Dim target = d.FileName
            If audio Then
                ' (the format picked in the list wins over a typed extension that doesn't match it)
                Dim wanted = {".mp3", ".mp4", ".wav"}(Math.Max(0, Math.Min(2, d.FilterIndex - 1)))
                If Path.GetExtension(target).ToLowerInvariant() <> wanted Then target = Path.ChangeExtension(target, wanted)
                Await ConvertAsync(f, target, status, CancellationToken.None)
            Else
                File.Copy(f, target, True)
            End If
            status?.Invoke(Localizer.T("Saved to") & " " & target)
            Return target
        End Using
    End Function

    ''' <summary>Sound → MP3 (VBR ~190 kbps), MP4 (1280×720 waveform video, AAC) or WAV.</summary>
    Public Async Function ConvertAsync(source As String, target As String, status As Action(Of String), ct As CancellationToken) As Task
        Dim ext = Path.GetExtension(target).ToLowerInvariant()
        Dim tmp = target & ".part" & ext
        Select Case ext
            Case ".mp3"
                status?.Invoke(Localizer.T("Making the MP3…"))
                Await VideoTools.FfmpegAsync($"-hide_banner -loglevel error -y -i ""{source}"" -vn -c:a libmp3lame -q:a 2 ""{tmp}""", ct)
            Case ".mp4"
                status?.Invoke(Localizer.T("Making the MP4…"))
                Await VideoTools.FfmpegAsync($"-hide_banner -loglevel error -y -i ""{source}"" -filter_complex " &
                    """[0:a]showwaves=s=1280x720:mode=cline:rate=30:colors=0x3b82f6|0x8b5cf6:scale=sqrt,format=yuv420p[v]"" " &
                    $"-map ""[v]"" -map 0:a -c:v libx264 -preset veryfast -crf 23 -c:a aac -b:a 192k -shortest -movflags +faststart ""{tmp}""", ct)
            Case Else
                If Path.GetExtension(source).ToLowerInvariant() = ".wav" Then
                    File.Copy(source, tmp, True)
                Else
                    Await VideoTools.FfmpegAsync($"-hide_banner -loglevel error -y -i ""{source}"" -vn -c:a pcm_s16le ""{tmp}""", ct)
                End If
        End Select
        If Not File.Exists(tmp) Then Throw New Exception(Localizer.T("Could not convert the file."))
        File.Move(tmp, target, True)
    End Function
End Module
