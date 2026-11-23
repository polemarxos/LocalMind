Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports Newtonsoft.Json

''' <summary>
''' A film: clips and pictures in order, music and voice-overs on top, exported as one MP4 (FFmpeg).
''' The film being worked on is kept in Data\Films\current.json; films can also be saved as .lmfilm files.
''' </summary>
Public Class FilmProject
    Public Class Clip
        Public Property File As String
        ''' <summary>How long a picture is shown (videos play to the end).</summary>
        Public Property Seconds As Double = 3
        <JsonIgnore> Public ReadOnly Property IsPicture As Boolean
            Get
                Dim e = Path.GetExtension(If(File, "")).ToLowerInvariant()
                Return e = ".png" OrElse e = ".jpg" OrElse e = ".jpeg" OrElse e = ".webp" OrElse e = ".bmp"
            End Get
        End Property
    End Class

    Public Class VoiceOver
        Public Property File As String
        ''' <summary>Second of the film where it starts.</summary>
        Public Property Start As Double
    End Class

    Public Property Name As String = "My film"
    Public Property Clips As New List(Of Clip)()
    Public Property Music As String = ""
    Public Property MusicVolume As Double = 0.35
    Public Property KeepClipSound As Boolean = True
    Public Property Fades As Boolean = True
    ''' <summary>0 = the size of the first clip.</summary>
    Public Property Width As Integer
    Public Property Height As Integer
    Public Property VoiceOvers As New List(Of VoiceOver)()

    Public Shared ReadOnly Property Folder As String
        Get
            Dim d = Path.Combine(AppPaths.Roaming, "Films")
            Directory.CreateDirectory(d)
            Return d
        End Get
    End Property

    Public Shared ReadOnly Property CurrentFile As String
        Get
            Return Path.Combine(Folder, "current.json")
        End Get
    End Property

    Public Shared Function Load(file As String) As FilmProject
        Try
            If IO.File.Exists(file) Then Return If(JsonConvert.DeserializeObject(Of FilmProject)(IO.File.ReadAllText(file)), New FilmProject())
        Catch
        End Try
        Return New FilmProject()
    End Function

    Public Shared Function LoadCurrent() As FilmProject
        Return Load(CurrentFile)
    End Function

    Public Sub Save(Optional file As String = Nothing)
        IO.File.WriteAllText(If(file, CurrentFile), JsonConvert.SerializeObject(Me, Formatting.Indented))
    End Sub

    ''' <summary>Adds a clip or picture to the end of the film being worked on (from the studio's gallery).</summary>
    Public Shared Sub AddToCurrent(file As String)
        Dim f = LoadCurrent()
        f.Clips.Add(New Clip With {.File = file})
        f.Save()
    End Sub

    ''' <summary>
    ''' Makes the film: every clip made uniform (size, 24 fps, sound), joined, then music and voice-overs mixed in.
    ''' Returns the MP4 in the outputs folder.
    ''' </summary>
    Public Async Function ExportAsync(progress As Action(Of Integer, String), ct As CancellationToken) As Task(Of String)
        Dim items = Clips.Where(Function(c) IO.File.Exists(c.File)).ToList()
        If items.Count = 0 Then Throw New Exception(Localizer.T("Add clips or pictures to the film first."))
        Dim w = Width, h = Height
        If w <= 0 OrElse h <= 0 Then
            If items(0).IsPicture Then
                Using img = ImageTools.Load(items(0).File)
                    w = img.Width : h = img.Height
                End Using
            Else
                Dim info = Await VideoTools.InfoAsync(items(0).File, ct)
                w = info.Width : h = info.Height
            End If
            If w <= 0 OrElse h <= 0 Then w = 1280 : h = 720
        End If
        w = Math.Max(2, (w \ 2) * 2) : h = Math.Max(2, (h \ 2) * 2)        ' H.264 wants even sizes
        Dim tmp = Path.Combine(Path.GetTempPath(), "localmind-film-" & Guid.NewGuid().ToString("N").Substring(0, 8))
        Directory.CreateDirectory(tmp)
        Try
            Dim pieces As New List(Of String)
            Dim total = 0.0
            For i = 0 To items.Count - 1
                ct.ThrowIfCancellationRequested()
                progress?.Invoke(i * 80 \ items.Count, Localizer.T("Preparing clip") & $" {i + 1}/{items.Count}…")
                Dim seg = Path.Combine(tmp, $"seg{i:0000}.mp4")
                total += Await VideoTools.FilmSegmentAsync(items(i).File, items(i).IsPicture, If(items(i).Seconds > 0, items(i).Seconds, 3), w, h, KeepClipSound, Fades, seg, ct)
                pieces.Add(seg)
            Next
            progress?.Invoke(82, Localizer.T("Joining the clips…"))
            Dim joined = Path.Combine(tmp, "joined.mp4")
            Await VideoTools.ConcatSameAsync(pieces, joined, ct)
            Dim safe = Text.RegularExpressions.Regex.Replace(If(Name, "film"), "[^\w\-]+", "_").Trim("_"c)
            Dim out = Path.Combine(MediaStudio.OutputFolder, $"{DateTime.Now:yyyyMMdd-HHmmss}-film-{If(safe.Length > 0, safe, "film")}.mp4")
            Dim vos = VoiceOvers.Where(Function(v) IO.File.Exists(v.File)).Select(Function(v) (v.File, v.Start)).ToList()
            If (Not String.IsNullOrEmpty(Music) AndAlso IO.File.Exists(Music)) OrElse vos.Count > 0 Then
                progress?.Invoke(90, Localizer.T("Mixing music and voice-overs…"))
                Await VideoTools.MixFilmSoundAsync(joined, total, Music, MusicVolume, KeepClipSound, vos, out, ct)
            Else
                IO.File.Copy(joined, out, True)
            End If
            progress?.Invoke(100, Localizer.T("Done"))
            Return out
        Finally
            Try : Directory.Delete(tmp, True) : Catch : End Try
        End Try
    End Function
End Class
