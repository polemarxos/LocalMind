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
        ''' <summary>Saved (cloned) voice the character speaks with (talking videos).</summary>
        Public Property Voice As String = ""
        ''' <summary>The member's own LoRA (name in My LoRAs), used whenever the member appears.</summary>
        Public Property Lora As String = ""

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
    '──────────────── voices, LoRAs, dialogue, more pictures, training sets ────────────────
    ''' <summary>The members' own LoRAs that suit the model family (their trigger words are added by the studio).</summary>
    Public Function LorasFor(members As IEnumerable(Of CastMember), family As String) As List(Of UserLora)
        Dim all = LoraLibrary.All()
        Return members.Where(Function(m) Not String.IsNullOrEmpty(m.Lora)).
                       Select(Function(m) all.FirstOrDefault(Function(l) l.Name.Equals(m.Lora, StringComparison.OrdinalIgnoreCase))).
                       Where(Function(l) l IsNot Nothing AndAlso File.Exists(l.FullPath) AndAlso LoraLibrary.FamilyMatches(l.Family, family)).ToList()
    End Function

    ''' <summary>A member by name (case-insensitive, with or without the @).</summary>
    Public Function Find(name As String) As CastMember
        Dim n = If(name, "").Trim().TrimStart("@"c).Trim()
        Return Load().FirstOrDefault(Function(m) m.Name.Equals(n, StringComparison.OrdinalIgnoreCase))
    End Function

    ''' <summary>
    ''' Lines like "@Anna: Hello!" (or "Anna: Hello!") → who says what. Lines without a known name belong to the
    ''' speaker before them (or to nobody).
    ''' </summary>
    Public Function ParseDialogue(text As String) As List(Of (Member As CastMember, Words As String))
        Dim result As New List(Of (Member As CastMember, Words As String))
        For Each raw In If(text, "").Split({vbCr, vbLf}, StringSplitOptions.RemoveEmptyEntries)
            Dim line = raw.Trim()
            If line.Length = 0 Then Continue For
            Dim m = Regex.Match(line, "^@?([^:@]{1,40}?)\s*:\s*(.+)$")
            Dim who = If(m.Success, Find(m.Groups(1).Value), Nothing)
            If who IsNot Nothing Then
                result.Add((who, m.Groups(2).Value.Trim()))
            ElseIf result.Count > 0 Then
                Dim last = result(result.Count - 1)
                result(result.Count - 1) = (last.Member, last.Words & " " & line)
            Else
                result.Add((Nothing, line))
            End If
        Next
        Return result
    End Function

    Private ReadOnly CharacterViews As String() = {
        "close-up portrait, looking at the camera, neutral background",
        "three-quarter view portrait, soft daylight",
        "side profile, studio light",
        "full body, standing, simple background",
        "smiling, upper body, outdoors",
        "serious expression, upper body, evening light",
        "walking, full body, city street",
        "sitting, medium shot, indoors"}
    Private ReadOnly PlaceViews As String() = {
        "wide shot, daytime", "wide shot, at night", "different angle, morning light", "close view of the details",
        "rainy weather", "golden hour", "from above", "empty, quiet"}

    ''' <summary>
    ''' More pictures of a member in different views (with a picture model that combines pictures and the member's
    ''' pictures as references): a richer set for consistent results, or for training a LoRA. Returns the new files.
    ''' </summary>
    Public Async Function MakeMorePicturesAsync(m As CastMember, count As Integer, progress As Action(Of Integer, String), ct As CancellationToken) As Task(Of List(Of String))
        Dim refs = ImagePaths(m)
        If refs.Count = 0 Then Throw New Exception(Localizer.T("Add a picture first."))
        Dim maker = VideoMaker.CastSceneRecipe()
        If maker Is Nothing Then Throw New Exception(Localizer.T("This needs a picture model that combines pictures (Qwen-Image Edit, FLUX.2 klein or Qwen-Image 2.1)."))
        Dim v = MediaStudio.InstalledVariant(maker)
        Dim fast = AppCore.Settings.MediaFast AndAlso maker.Speed IsNot Nothing
        Dim views = If(m.IsLocation, PlaceViews, CharacterViews)
        Dim made As New List(Of String)
        For i = 0 To count - 1
            ct.ThrowIfCancellationRequested()
            Dim view = views(i Mod views.Length)
            Dim what = If(m.IsLocation, "the same place as in the picture", "the same person as in the picture, same face, hair and clothes")
            Dim req As New MediaStudio.GenerateRequest With {
                .Recipe = maker, .Variant = v, .Task = "combine",
                .Prompt = $"{what}: {If(String.IsNullOrWhiteSpace(m.Description), m.Name, m.Description)}. {view}, photorealistic, high detail",
                .Width = 1024, .Height = 1024, .Steps = If(fast, maker.Speed.Steps, maker.Steps), .Cfg = If(fast, maker.Speed.Cfg, maker.Cfg), .Fast = fast,
                .InputImages = refs.Take(Math.Max(1, maker.Refs)).ToList(),
                .OutputPath = Path.Combine(Path.GetTempPath(), $"localmind-castpic-{Guid.NewGuid():N}.png")}
            Dim k = i
            Await MediaStudio.GenerateAsync(req, Sub(p, t) progress?.Invoke(CInt((k * 100 + p) / count), Localizer.T("📸 Picture") & $" {k + 1}/{count} — " & Localizer.T(t)), ct)
            If File.Exists(req.OutputPath) Then
                AddImage(m, req.OutputPath)
                made.Add(req.OutputPath)
            End If
        Next
        Return made
    End Function

    ''' <summary>
    ''' A folder ready for LoRA training (OneTrainer, kohya_ss, ai-toolkit or Civitai's trainer): every picture with a
    ''' caption file "trigger, description". Returns the folder.
    ''' </summary>
    Public Function ExportTrainingSet(m As CastMember, parent As String, trigger As String) As String
        Dim safe = Regex.Replace(If(m.Name, "cast"), "[^\w\-]+", "_")
        Dim dir = Path.Combine(parent, $"{safe}-lora-dataset")
        Directory.CreateDirectory(dir)
        Dim caption = trigger & If(String.IsNullOrWhiteSpace(m.Description), "", ", " & m.Description.Trim().TrimEnd("."c))
        Dim i = 0
        For Each f In ImagePaths(m)
            i += 1
            Dim name = $"{safe}_{i:000}"
            File.Copy(f, Path.Combine(dir, name & Path.GetExtension(f).ToLowerInvariant()), True)
            File.WriteAllText(Path.Combine(dir, name & ".txt"), caption)
        Next
        File.WriteAllText(Path.Combine(dir, "README.txt"),
            $"Training set for '{m.Name}' made by LocalMind ({i} pictures).{Environment.NewLine}" &
            $"Trigger word: {trigger}{Environment.NewLine}{Environment.NewLine}" &
            "Train a LoRA with OneTrainer, kohya_ss, ai-toolkit, or online with Civitai's LoRA trainer (upload this folder as a zip)." & Environment.NewLine &
            "15-30 varied pictures work best (different angles, expressions, light). Then in LocalMind: Studio → LoRAs → Add, and pick it" & Environment.NewLine &
            "as this cast member's LoRA in the Cast window. It is used automatically whenever the member is in a picture or video.")
        Return dir
    End Function
End Module
