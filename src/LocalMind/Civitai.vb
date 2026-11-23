Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Net.Http
Imports System.Text.RegularExpressions
Imports System.Threading
Imports System.Threading.Tasks
Imports Newtonsoft.Json.Linq

''' <summary>
''' Search Civitai (civitai.com, or civitai.red for the full catalogue) for LoRAs and checkpoints and install them
''' where LocalMind uses them: LoRAs go to Models\Loras with their trigger words, checkpoints to
''' Models\Images\civitai as a new model in the studio. Uses the public REST API (same API on both domains).
''' </summary>
Public Module Civitai

    Public Class CivFile
        Public Property Name As String
        Public Property SizeBytes As Long
        Public Property Primary As Boolean
        Public Property Format As String         ' SafeTensor, GGUF, PickleTensor…
        Public Property Fp As String
        Public Property DownloadUrl As String
        Public Property Sha256 As String
    End Class

    Public Class CivVersion
        Public Property Id As Long
        Public Property Name As String
        Public Property BaseModel As String
        Public Property TrainedWords As New List(Of String)()
        Public Property Files As New List(Of CivFile)()
        Public Property ImageUrl As String
        Public Property ImageNsfw As Boolean

        ''' <summary>The file to download: the primary model file, safetensors preferred (never pickle files).</summary>
        Public Function BestFile() As CivFile
            Dim safe = Files.Where(Function(f) f.Format <> "PickleTensor" AndAlso
                                               (f.Name.EndsWith(".safetensors", StringComparison.OrdinalIgnoreCase) OrElse f.Name.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase))).ToList()
            Return If(safe.FirstOrDefault(Function(f) f.Primary), safe.OrderBy(Function(f) f.SizeBytes).FirstOrDefault())
        End Function

        Public Overrides Function ToString() As String
            Return $"{Name}  ·  {BaseModel}"
        End Function
    End Class

    Public Class CivModel
        Public Property Id As Long
        Public Property Name As String
        Public Property Type As String           ' LORA, LoCon, DoRA, Checkpoint
        Public Property Nsfw As Boolean
        Public Property Creator As String
        Public Property Downloads As Long
        Public Property Likes As Long
        Public Property Versions As New List(Of CivVersion)()
        Public Property Tags As New List(Of String)()

        Public ReadOnly Property IsLora As Boolean
            Get
                Return Type IsNot Nothing AndAlso (Type.Equals("LORA", StringComparison.OrdinalIgnoreCase) OrElse Type.Equals("LoCon", StringComparison.OrdinalIgnoreCase) OrElse Type.Equals("DoRA", StringComparison.OrdinalIgnoreCase))
            End Get
        End Property
    End Class

    ''' <summary>Base-model groups LocalMind can run: Civitai names, the LoRA family and the recipe a checkpoint is built on.</summary>
    Public ReadOnly Groups As (Label As String, BaseModels As String(), Family As String, Template As String)() = {
        ("SDXL · Pony · Illustrious · NoobAI", {"SDXL 1.0", "Pony", "Illustrious", "NoobAI"}, "SDXL", "sdxl"),
        ("SD 1.5", {"SD 1.5"}, "SD1.5", "sd15"),
        ("FLUX.1 dev / Krea", {"Flux.1 D", "Flux.1 Krea"}, "FLUX.1", "flux1-schnell"),
        ("FLUX.1 schnell", {"Flux.1 S"}, "FLUX.1", "flux1-schnell"),
        ("FLUX.1 Kontext", {"Flux.1 Kontext"}, "FLUX.1-Kontext", "flux1-kontext"),
        ("FLUX.2 klein 4B", {"Flux.2 Klein 4B"}, "FLUX.2", "flux2-klein-4b"),
        ("FLUX.2 klein 9B", {"Flux.2 Klein 9B"}, "FLUX.2", "flux2-klein-9b"),
        ("Z-Image", {"ZImageTurbo", "ZImageBase"}, "Z-Image", "z-image-turbo"),
        ("Qwen-Image", {"Qwen"}, "Qwen-Image", "qwen-image-2512"),
        ("Qwen-Image 2.1", {"Qwen 2.1"}, "Qwen-Image", "qwen-image-2.1"),
        ("Krea 2", {"Krea 2", "Krea 2 Turbo"}, "Krea", "krea2-turbo"),
        ("Anima", {"Anima"}, "Anima", "anima"),
        ("Wan video", {"Wan Video 2.2 T2V-A14B", "Wan Video 2.2 I2V-A14B", "Wan Video 2.2 TI2V-5B", "Wan Video 14B t2v", "Wan Video 1.3B t2v"}, "Wan", Nothing),
        ("LTX video", {"LTXV 2.3", "LTXV2"}, "LTX", Nothing)}

    Public Function GroupFor(baseModel As String) As (Label As String, BaseModels As String(), Family As String, Template As String)
        For Each g In Groups
            If g.BaseModels.Any(Function(b) b.Equals(baseModel, StringComparison.OrdinalIgnoreCase)) Then Return g
        Next
        Return (Nothing, Nothing, Nothing, Nothing)
    End Function

    Private Function ApiBase(red As Boolean) As String
        Return If(red, "https://civitai.red/api/v1", "https://civitai.com/api/v1")
    End Function

    ''' <summary>One page of results. Pass the returned cursor to get the next page.</summary>
    Public Async Function SearchAsync(query As String, loras As Boolean, baseModels As IEnumerable(Of String), red As Boolean,
                                      sort As String, cursor As String, ct As CancellationToken) As Task(Of (Items As List(Of CivModel), NextCursor As String))
        Dim q As New List(Of String) From {"limit=30", "sort=" & Uri.EscapeDataString(If(sort, "Most Downloaded"))}
        If Not String.IsNullOrWhiteSpace(query) Then q.Add("query=" & Uri.EscapeDataString(query.Trim()))
        If loras Then
            q.AddRange({"types=LORA", "types=LoCon", "types=DoRA"})
        Else
            q.Add("types=Checkpoint")
        End If
        For Each b In If(baseModels, Enumerable.Empty(Of String)())
            q.Add("baseModels=" & Uri.EscapeDataString(b))
        Next
        q.Add("nsfw=" & If(red, "true", "false"))
        If Not String.IsNullOrEmpty(cursor) Then q.Add("cursor=" & Uri.EscapeDataString(cursor))
        Dim url = ApiBase(red) & "/models?" & String.Join("&", q)

        Using req As New HttpRequestMessage(HttpMethod.Get, url)
            req.Headers.UserAgent.ParseAdd("LocalMind/" & Updater.CurrentVersion.ToString())
            Dim key = AppCore.Settings.CivitaiApiKey
            If Not String.IsNullOrWhiteSpace(key) Then req.Headers.Authorization = New Headers.AuthenticationHeaderValue("Bearer", key.Trim())
            Using resp = Await Downloader.Http.SendAsync(req, ct)
                resp.EnsureSuccessStatusCode()
                Dim root = JObject.Parse(Await resp.Content.ReadAsStringAsync(ct))
                Dim items As New List(Of CivModel)()
                For Each m In CType(root("items"), JArray)
                    Dim cm As New CivModel With {
                        .Id = m.Value(Of Long)("id"), .Name = m.Value(Of String)("name"), .Type = m.Value(Of String)("type"),
                        .Nsfw = If(m.Value(Of Boolean?)("nsfw"), False), .Creator = m("creator")?.Value(Of String)("username"),
                        .Downloads = If(m("stats")?.Value(Of Long?)("downloadCount"), 0), .Likes = If(m("stats")?.Value(Of Long?)("thumbsUpCount"), 0)}
                    If m("tags") IsNot Nothing Then cm.Tags = m("tags").Select(Function(t) t.ToString()).ToList()
                    For Each v In If(CType(m("modelVersions"), JArray), New JArray())
                        Dim cv As New CivVersion With {.Id = v.Value(Of Long)("id"), .Name = v.Value(Of String)("name"), .BaseModel = v.Value(Of String)("baseModel")}
                        If v("trainedWords") IsNot Nothing Then cv.TrainedWords = v("trainedWords").Select(Function(t) t.ToString().Trim()).Where(Function(t) t <> "").ToList()
                        For Each f In If(CType(v("files"), JArray), New JArray())
                            If f.Value(Of String)("type") <> "Model" Then Continue For
                            cv.Files.Add(New CivFile With {
                                .Name = f.Value(Of String)("name"), .SizeBytes = CLng(If(f.Value(Of Double?)("sizeKB"), 0) * 1024),
                                .Primary = If(f.Value(Of Boolean?)("primary"), False), .Format = f("metadata")?.Value(Of String)("format"),
                                .Fp = f("metadata")?.Value(Of String)("fp"), .DownloadUrl = f.Value(Of String)("downloadUrl"),
                                .Sha256 = f("hashes")?.Value(Of String)("SHA256")})
                        Next
                        Dim img = TryCast(v("images"), JArray)?.FirstOrDefault(Function(i) i.Value(Of String)("type") <> "video")
                        If img IsNot Nothing Then
                            cv.ImageUrl = img.Value(Of String)("url")
                            cv.ImageNsfw = img.Value(Of Integer?)("nsfwLevel").GetValueOrDefault() > 1
                        End If
                        If cv.Files.Count > 0 Then cm.Versions.Add(cv)
                    Next
                    If cm.Versions.Count > 0 Then items.Add(cm)
                Next
                Dim nextCursor = root("metadata")?.Value(Of String)("nextCursor")
                Return (items, nextCursor)
            End Using
        End Using
    End Function

    ''' <summary>A small preview picture (Civitai's image CDN can resize: width=…).</summary>
    Public Function ThumbUrl(url As String, width As Integer) As String
        If String.IsNullOrEmpty(url) Then Return Nothing
        Return Regex.Replace(url, "/(original=true|width=\d+)/", $"/width={width}/")
    End Function

    Private Function SafeFileName(name As String) As String
        Dim n = String.Concat(name.Select(Function(c) If(Path.GetInvalidFileNameChars().Contains(c) OrElse c = " "c, "_"c, c)))
        Return n
    End Function

    Private Function DownloadUrlFor(f As CivFile) As String
        Return f.DownloadUrl
    End Function

    ''' <summary>Downloads a LoRA into Models\Loras and fills in its family and trigger words.</summary>
    Public Async Function InstallLoraAsync(m As CivModel, v As CivVersion, progress As Action(Of Integer, String), ct As CancellationToken) As Task(Of UserLora)
        Dim f = v.BestFile()
        If f Is Nothing Then Throw New Exception(Localizer.T("This version has no safetensors file to download."))
        Dim dest = Path.Combine(LoraLibrary.Folder, SafeFileName(f.Name))
        Await DownloadAsync(f, dest, progress, ct)
        Dim l = LoraLibrary.All().FirstOrDefault(Function(x) x.Name.Equals(Path.GetFileNameWithoutExtension(dest), StringComparison.OrdinalIgnoreCase))
        If l IsNot Nothing Then
            Dim g = GroupFor(v.BaseModel)
            If g.Family IsNot Nothing Then l.Family = g.Family
            If v.TrainedWords.Count > 0 Then l.Triggers = String.Join(", ", v.TrainedWords)
            l.Enabled = True
            l.Source = $"Civitai #{m.Id} · {m.Name} · {v.Name}"
            l.BaseModel = v.BaseModel
            LoraLibrary.Save()
        End If
        Return l
    End Function

    ''' <summary>Downloads a checkpoint into Models\Images\civitai and adds it as a model in the studio.</summary>
    Public Async Function InstallCheckpointAsync(m As CivModel, v As CivVersion, progress As Action(Of Integer, String), ct As CancellationToken) As Task(Of MediaStudio.UserModel)
        Dim g = GroupFor(v.BaseModel)
        If g.Template Is Nothing Then Throw New Exception(Localizer.T("LocalMind can't run checkpoints of this type yet:") & " " & v.BaseModel)
        Dim f = v.BestFile()
        If f Is Nothing Then Throw New Exception(Localizer.T("This version has no safetensors file to download."))
        Dim fileName = SafeFileName(f.Name)
        Dim dest = Path.Combine(MediaStudio.UserModelFolder, fileName)
        Await DownloadAsync(f, dest, progress, ct)
        Dim allInOne = g.Template = "sdxl" OrElse g.Template = "sd15"
        Dim um As New MediaStudio.UserModel With {
            .Name = m.Name & If(m.Versions.Count > 1, " · " & v.Name, ""), .Template = g.Template, .File = fileName,
            .Size = New FileInfo(dest).Length, .Format = If(allInOne, "checkpoint", If(fileName.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase), "gguf", "safetensors")),
            .BaseModel = v.BaseModel, .Source = $"Civitai #{m.Id}"}
        ' FLUX.1 dev fine-tunes need dev settings, not the schnell template's 4 steps.
        If v.BaseModel = "Flux.1 D" OrElse v.BaseModel = "Flux.1 Krea" Then
            um.Steps = 24 : um.Cfg = 1.0 : um.Extra = "--guidance 3.5"
        ElseIf v.BaseModel = "ZImageBase" Then
            um.Template = "z-image"
        ElseIf v.BaseModel = "Krea 2" Then
            um.Steps = 28 : um.Cfg = 4.0
        ElseIf v.BaseModel = "Pony" OrElse v.BaseModel = "Illustrious" OrElse v.BaseModel = "NoobAI" Then
            um.Steps = 28 : um.Cfg = 6.0
        End If
        MediaStudio.AddUserModel(um)
        Return um
    End Function

    Private Async Function DownloadAsync(f As CivFile, dest As String, progress As Action(Of Integer, String), ct As CancellationToken) As Task
        Dim key = AppCore.Settings.CivitaiApiKey
        Try
            Await Downloader.DownloadFileAsync(f.DownloadUrl, dest, If(String.IsNullOrWhiteSpace(key), Nothing, key.Trim()),
                Sub(done, size, speed) progress?.Invoke(CInt(done * 100 \ Math.Max(1L, If(size > 0, size, f.SizeBytes))),
                    $"{f.Name}  {Downloader.FormatBytes(done)} / {Downloader.FormatBytes(If(size > 0, size, f.SizeBytes))}  ·  {Downloader.FormatBytes(CLng(speed))}/s"), ct)
        Catch ex As HttpRequestException When ex.StatusCode = Net.HttpStatusCode.Unauthorized OrElse ex.StatusCode = Net.HttpStatusCode.Forbidden
            Throw New Exception(Localizer.T("Civitai asks you to sign in for this download. Paste your Civitai API key (civitai.com → Account settings → API keys) in the box at the top and try again."))
        End Try
        ' Civitai serves HTML (a login page) instead of the file when the key is missing.
        If New FileInfo(dest).Length < 100_000 AndAlso File.ReadAllText(dest).TrimStart().StartsWith("<") Then
            File.Delete(dest)
            Throw New Exception(Localizer.T("Civitai asks you to sign in for this download. Paste your Civitai API key (civitai.com → Account settings → API keys) in the box at the top and try again."))
        End If
        progress?.Invoke(100, "Ready")
    End Function
End Module
