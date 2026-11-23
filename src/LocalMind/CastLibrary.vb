Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.IO
Imports System.Linq
Imports System.Text.RegularExpressions
Imports System.Threading
Imports System.Threading.Tasks
Imports Newtonsoft.Json

''' <summary>
''' The cast: characters and locations you reuse in pictures and videos. Each has a name, a description and
''' reference pictures. In a prompt, write @Name (or just pick them): the description is added to the prompt and
''' the pictures are given to models that accept reference pictures, so the same faces and places come back.
''' </summary>
Public Module CastLibrary

    Public Class CastMember
        Public Property Id As String = Guid.NewGuid().ToString("N").Substring(0, 10)
        ''' <summary>character | location</summary>
        Public Property Kind As String = "character"
        Public Property Name As String = ""
        Public Property Description As String = ""
        ''' <summary>File names inside this member's folder.</summary>
        Public Property Images As New List(Of String)()

        <JsonIgnore> Public ReadOnly Property IsLocation As Boolean
            Get
                Return Kind = "location"
            End Get
        End Property

        Public Overrides Function ToString() As String
            Return If(IsLocation, "📍 ", "🎭 ") & Name
        End Function
    End Class

    Public ReadOnly Property Folder As String
        Get
            Dim f = Path.Combine(AppPaths.Roaming, "Cast")
            Directory.CreateDirectory(f)
            Return f
        End Get
    End Property

    Private ReadOnly Property FilePath As String
        Get
            Return Path.Combine(Folder, "cast.json")
        End Get
    End Property

    Public Function Load() As List(Of CastMember)
        Try
            If File.Exists(FilePath) Then Return If(JsonConvert.DeserializeObject(Of List(Of CastMember))(File.ReadAllText(FilePath)), New List(Of CastMember))
        Catch
        End Try
        Return New List(Of CastMember)()
    End Function

    Public Sub Save(list As List(Of CastMember))
        File.WriteAllText(FilePath, JsonConvert.SerializeObject(list, Formatting.Indented))
    End Sub

    Public Function MemberFolder(m As CastMember) As String
        Dim f = Path.Combine(Folder, m.Id)
        Directory.CreateDirectory(f)
        Return f
    End Function

    Public Function ImagePaths(m As CastMember) As List(Of String)
        Return m.Images.Select(Function(i) Path.Combine(MemberFolder(m), i)).Where(AddressOf File.Exists).ToList()
    End Function

    ''' <summary>Copies a picture into the member's folder (so it stays even if the original moves).</summary>
    Public Function AddImage(m As CastMember, source As String) As String
        Dim name = $"{m.Images.Count + 1:00}-{Guid.NewGuid().ToString("N").Substring(0, 6)}{Path.GetExtension(source).ToLowerInvariant()}"
        File.Copy(source, Path.Combine(MemberFolder(m), name), True)
        m.Images.Add(name)
        Return name
    End Function

    Public Sub Delete(list As List(Of CastMember), m As CastMember)
        list.Remove(m)
        Try
            Directory.Delete(Path.Combine(Folder, m.Id), True)
        Catch
        End Try
        Save(list)
    End Sub

    ''' <summary>
    ''' The prompt with the cast in it: "@Anna" becomes "Anna (a woman in her 30s, red hair…)", and picked members
    ''' that the prompt doesn't mention are added at the end.
    ''' </summary>
    Public Function ComposePrompt(prompt As String, picked As IEnumerable(Of CastMember)) As String
        Dim p = If(prompt, "")
        Dim all = Load()
        Dim used As New HashSet(Of String)
        ' @Name for anyone in the cast (even if not ticked)
        For Each m In all.OrderByDescending(Function(x) x.Name.Length)
            If String.IsNullOrWhiteSpace(m.Name) Then Continue For
            Dim rx As New Regex("@" & Regex.Escape(m.Name) & "\b", RegexOptions.IgnoreCase)
            If rx.IsMatch(p) Then
                p = rx.Replace(p, Describe(m), 1)
                p = rx.Replace(p, m.Name)
                used.Add(m.Id)
            End If
        Next
        Dim rest = picked.Where(Function(m) Not used.Contains(m.Id)).ToList()
        Dim chars = rest.Where(Function(m) Not m.IsLocation).ToList()
        Dim places = rest.Where(Function(m) m.IsLocation).ToList()
        If chars.Count > 0 Then p = p.TrimEnd(" "c, "."c) & ". " & If(chars.Count = 1, "Character: ", "Characters: ") & String.Join("; ", chars.Select(AddressOf Describe)) & "."
        If places.Count > 0 Then p = p.TrimEnd(" "c, "."c) & ". " & "Location: " & String.Join("; ", places.Select(AddressOf Describe)) & "."
        Return p.Trim()
    End Function

    Private Function Describe(m As CastMember) As String
        Return If(String.IsNullOrWhiteSpace(m.Description), m.Name, $"{m.Name} ({m.Description.Trim().TrimEnd("."c)})")
    End Function

    ''' <summary>Cast members picked for a job, plus anyone written as @Name in the prompt.</summary>
    Public Function Involved(prompt As String, picked As IEnumerable(Of CastMember)) As List(Of CastMember)
        Dim l = picked.ToList()
        For Each m In Load()
            If l.Any(Function(x) x.Id = m.Id) OrElse String.IsNullOrWhiteSpace(m.Name) Then Continue For
            If Regex.IsMatch(If(prompt, ""), "@" & Regex.Escape(m.Name) & "\b", RegexOptions.IgnoreCase) Then l.Add(m)
        Next
        Return l
    End Function

    ''' <summary>
    ''' One reference picture with everyone in it, side by side on white (characters first, the location last):
    ''' what Wan VACE expects for "reference to video", and a good start for picture models.
    ''' </summary>
    Public Function ReferenceSheet(members As IEnumerable(Of CastMember), width As Integer, height As Integer) As String
        Dim pics = members.OrderBy(Function(m) If(m.IsLocation, 1, 0)).
                           Select(Function(m) ImagePaths(m).FirstOrDefault()).Where(Function(f) f IsNot Nothing).ToList()
        If pics.Count = 0 Then Return Nothing
        Dim outPath = Path.Combine(Path.GetTempPath(), $"localmind-cast-{Guid.NewGuid():N}.png")
        Using sheet As New Bitmap(width, height)
            Using g = Graphics.FromImage(sheet)
                g.Clear(Color.White)
                g.InterpolationMode = Drawing2D.InterpolationMode.HighQualityBicubic
                Dim cellW = width / pics.Count
                For i = 0 To pics.Count - 1
                    Using img = ImageTools.Load(pics(i))
                        Dim k = Math.Min(cellW / img.Width, height / img.Height) * 0.96
                        Dim w = CInt(img.Width * k), h = CInt(img.Height * k)
                        g.DrawImage(img, CInt(i * cellW + (cellW - w) / 2), CInt((height - h) / 2), w, h)
                    End Using
                Next
            End Using
            sheet.Save(outPath, Imaging.ImageFormat.Png)
        End Using
        Return outPath
    End Function

    ''' <summary>The members' pictures (up to max), characters first: for models that take several reference pictures.</summary>
    Public Function ReferencePictures(members As IEnumerable(Of CastMember), max As Integer) As List(Of String)
        Return members.OrderBy(Function(m) If(m.IsLocation, 1, 0)).
                       Select(Function(m) ImagePaths(m).FirstOrDefault()).Where(Function(f) f IsNot Nothing).Take(max).ToList()
    End Function

    ''' <summary>Writes a short description of a cast picture with a chat model that can see (if one is installed).</summary>
    Public Async Function DescribeAsync(picture As String, isLocation As Boolean, ct As CancellationToken) As Task(Of String)
        Dim vision = AppCore.Library.All().FirstOrDefault(Function(m) Not String.IsNullOrEmpty(m.MmprojFile))
        If AppCore.Settings.Backend = BackendKind.BuiltIn AndAlso vision Is Nothing Then
            Throw New Exception(Localizer.T("This needs a chat model that can see pictures (marked 👁 in the Model Manager)."))
        End If
        Dim what = If(isLocation,
            "Describe this place for an image generator in one English sentence (under 40 words): type of place, key objects, colors, light, mood.",
            "Describe this person or character for an image generator in one English sentence (under 40 words): apparent age, gender, face, hair, eyes, build, clothing, distinctive features. No names.")
        Dim msgs As New List(Of ChatMessage) From {
            New ChatMessage With {.Role = "user", .Content = what, .Images = New List(Of String) From {Convert.ToBase64String(File.ReadAllBytes(picture))}}}
        Dim opts As New ChatRequestOptions With {.Model = If(vision?.DisplayName, AppCore.Settings.LastModel), .Temperature = 0.3, .MaxTokens = 200, .EnableThinking = False}
        Dim backend = AppCore.Backend
        Dim res = Await Task.Run(Function() backend.StreamChatAsync(msgs, opts, Sub(t) Return, Sub(t) Return, ct))
        Return If(res?.Content, "").Trim().Trim(""""c)
    End Function
End Module
