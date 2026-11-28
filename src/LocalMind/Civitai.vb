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
        ''' <summary>All pictures of this version (videos left out), with whether each is NSFW.</summary>
        Public Property Images As New List(Of (Url As String, Nsfw As Boolean))()

        ''' <summary>The file to download: the primary model file, safetensors preferred (never pickle files).</summary>
        Public Function BestFile() As CivFile
            Dim safe = Files.Where(Function(f) IsUsableFile(f)).ToList()
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
        ("MiniMax H3 video", {"MiniMax H3"}, "MiniMax-H3", "minimax-h3"),
        ("LTX-2.5 video", {"LTXV 2.5"}, "LTX-2.5", "ltx-2.5-distilled")}

    Public Function IsVideoGroup(g As (Label As String, BaseModels As String(), Family As String, Template As String)) As Boolean
        Return g.Family = "MiniMax-H3" OrElse g.Family = "LTX-2.5"
    End Function

    ' Weight formats the engine can't run (4-bit float / mixed formats made for other apps).
    Private ReadOnly Unusable As New Regex("nvfp4|mxfp|[-_. ]fp4|w4a8|w4a16|int4|svdq|nunchaku|awq|gptq", RegexOptions.IgnoreCase)

    Public Function IsUsableFile(f As CivFile) As Boolean
        Return f.Format <> "PickleTensor" AndAlso Not Unusable.IsMatch(If(f.Name, "")) AndAlso
               (f.Name.EndsWith(".safetensors", StringComparison.OrdinalIgnoreCase) OrElse f.Name.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase))
    End Function

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
                        ' pictures, and a still frame of each preview video (many video models only have videos)
                        For Each i In If(TryCast(v("images"), JArray), New JArray())
                            Dim u = i.Value(Of String)("url")
                            If String.IsNullOrEmpty(u) Then Continue For
                            If i.Value(Of String)("type") = "video" Then u = StillUrl(u)
                            cv.Images.Add((u, i.Value(Of Integer?)("nsfwLevel").GetValueOrDefault() > 1))
                        Next
                        Dim imgs = If(TryCast(v("images"), JArray), New JArray())
                        Dim img = If(imgs.FirstOrDefault(Function(i) i.Value(Of String)("type") <> "video"), imgs.FirstOrDefault())
                        If img IsNot Nothing AndAlso Not String.IsNullOrEmpty(img.Value(Of String)("url")) Then
                            cv.ImageUrl = If(img.Value(Of String)("type") = "video", StillUrl(img.Value(Of String)("url")), img.Value(Of String)("url"))
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
        If url.Contains("anim=false") Then Return Regex.Replace(url, "width=\d+", $"width={width}")
        Return Regex.Replace(url, "/(original=true|width=\d+)/", $"/width={width}/")
    End Function

    ''' + "'''" + ''' <summary>A JPEG still frame of a Civitai preview video (their image CDN makes it: anim=false).</summary>
    Public Function StillUrl(videoUrl As String) As String
        Dim m = Regex.Match(videoUrl, "^(https://[^/]+/[^/]+/[^/]+)/[^/]+/([^/?]+?)(\.\w+)?$")
        If Not m.Success Then Return videoUrl
        Return $"{m.Groups(1).Value}/anim=false,transcode=true,width=450,optimized=true/{m.Groups(2).Value}.jpeg"
    End Function

    Private Function SafeFileName(name As String) As String
        Dim n = String.Concat(name.Select(Function(c) If(Path.GetInvalidFileNameChars().Contains(c) OrElse c = " "c, "_"c, c)))
        Return n
    End Function

    ''' <summary>True when the file can only be downloaded with a signed-in account (checked without downloading it).</summary>
    Public Async Function NeedsLoginAsync(f As CivFile, ct As CancellationToken) As Task(Of Boolean)
        Dim h As New HttpClientHandler() With {.AllowAutoRedirect = False}
        Using c As New HttpClient(h) With {.Timeout = TimeSpan.FromSeconds(15)}
            Using req As New HttpRequestMessage(HttpMethod.Get, f.DownloadUrl)
                req.Headers.UserAgent.ParseAdd("LocalMind/" & Updater.CurrentVersion.ToString())
                Dim key = AppCore.Settings.CivitaiApiKey
                If Not String.IsNullOrWhiteSpace(key) Then req.Headers.Authorization = New Headers.AuthenticationHeaderValue("Bearer", key.Trim())
                Using resp = Await c.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct)
                    Return resp.StatusCode = Net.HttpStatusCode.Unauthorized OrElse resp.StatusCode = Net.HttpStatusCode.Forbidden
                End Using
            End Using
        End Using
    End Function

    ''' <summary>
    ''' Finds LoRAs that were added from a file on Civitai by their SHA-256 (Civitai's lookup by file hash) and fills in
    ''' where they came from: the page, the download link (so an exported setup can get them again), the base model and
    ''' the trigger words when none were set. Each file is looked up once. Returns how many were found.
    ''' </summary>
    Public Async Function IdentifyLorasAsync(ct As CancellationToken) As Task(Of Integer)
        Dim found = 0
        For Each l In LoraLibrary.All().Where(Function(x) String.IsNullOrEmpty(x.Url) AndAlso Not x.LookedUp AndAlso File.Exists(x.FullPath)).ToList()
            ct.ThrowIfCancellationRequested()
            Dim hash As String
            Using fs As New FileStream(l.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 20, FileOptions.SequentialScan)
                hash = Convert.ToHexString(Await System.Security.Cryptography.SHA256.HashDataAsync(fs, ct))
            End Using
            Dim v As JObject = Nothing
            Dim failed = False
            For Each red In {False, True}
                Try
                    v = Await ByHashAsync(hash, red, ct)
                    If v IsNot Nothing Then Exit For
                Catch ex As OperationCanceledException
                    Throw
                Catch
                    failed = True        ' (offline: try again next time)
                End Try
            Next
            If v Is Nothing Then
                If Not failed Then l.LookedUp = True
                Continue For
            End If
            Dim modelId = v.Value(Of Long)("modelId")
            Dim modelName = If(v("model")?.Value(Of String)("name"), "")
            l.Source = $"Civitai #{modelId} · {modelName} · {v.Value(Of String)("name")}"
            l.PageUrl = $"https://civitai.com/models/{modelId}?modelVersionId={v.Value(Of Long)("id")}"
            Dim match = TryCast(v("files"), JArray)?.OfType(Of JObject)().FirstOrDefault(Function(f) String.Equals(f("hashes")?.Value(Of String)("SHA256"), hash, StringComparison.OrdinalIgnoreCase))
            l.Url = If(match?.Value(Of String)("downloadUrl"), v.Value(Of String)("downloadUrl"))
            l.BaseModel = v.Value(Of String)("baseModel")
            Dim g = GroupFor(If(l.BaseModel, ""))
            If String.IsNullOrEmpty(l.Family) AndAlso g.Family IsNot Nothing Then l.Family = g.Family
            Dim words = TryCast(v("trainedWords"), JArray)
            If String.IsNullOrWhiteSpace(l.Triggers) AndAlso words IsNot Nothing AndAlso words.Count > 0 Then l.Triggers = String.Join(", ", words.Select(Function(w) w.ToString()))
            l.LookedUp = True
            found += 1
        Next
        LoraLibrary.Save()
        Return found
    End Function

    ''' <summary>The Civitai model version with this file hash, or Nothing when Civitai doesn't know it.</summary>
    Private Async Function ByHashAsync(hash As String, red As Boolean, ct As CancellationToken) As Task(Of JObject)
        Using req As New HttpRequestMessage(HttpMethod.Get, ApiBase(red) & "/model-versions/by-hash/" & hash)
            req.Headers.UserAgent.ParseAdd("LocalMind/" & Updater.CurrentVersion.ToString())
            Dim key = AppCore.Settings.CivitaiApiKey
            If Not String.IsNullOrWhiteSpace(key) Then req.Headers.Authorization = New Headers.AuthenticationHeaderValue("Bearer", key.Trim())
            Using resp = Await Downloader.Http.SendAsync(req, ct)
                If resp.StatusCode = Net.HttpStatusCode.NotFound Then Return Nothing
                resp.EnsureSuccessStatusCode()
                Dim o = JObject.Parse(Await resp.Content.ReadAsStringAsync(ct))
                Return If(o("modelId") Is Nothing, Nothing, o)
            End Using
        End Using
    End Function

    ''' <summary>Downloads a LoRA into Models\Loras and fills in its family and trigger words.</summary>
    Public Async Function InstallLoraAsync(m As CivModel, v As CivVersion, progress As Action(Of Integer, String), ct As CancellationToken) As Task(Of UserLora)
        Dim f = v.BestFile()
        If f Is Nothing Then Throw New Exception(Localizer.T("This version has no safetensors file to download."))
        Dim dest = Path.Combine(LoraLibrary.Folder, SafeFileName(f.Name))
        Await AppCore.Downloads.RunAsync(m.Name & " · " & v.Name, "LoRA", "civitai:" & v.Id, Function(p, c) DownloadAsync(f, dest, p, c), ct)
        Dim l = LoraLibrary.All().FirstOrDefault(Function(x) x.Name.Equals(Path.GetFileNameWithoutExtension(dest), StringComparison.OrdinalIgnoreCase))
        If l IsNot Nothing Then
            Dim g = GroupFor(v.BaseModel)
            If g.Family IsNot Nothing Then l.Family = g.Family
            If v.TrainedWords.Count > 0 Then l.Triggers = String.Join(", ", v.TrainedWords)
            l.Enabled = True
            l.Source = $"Civitai #{m.Id} · {m.Name} · {v.Name}"
            l.PageUrl = $"https://civitai.com/models/{m.Id}?modelVersionId={v.Id}"
            l.LookedUp = True
            l.BaseModel = v.BaseModel
            l.Url = f.DownloadUrl
            LoraLibrary.Save()
        End If
        Return l
    End Function

    ''' <summary>Downloads a checkpoint into Models\Images\civitai and adds it as a model in the studio.</summary>
    Public Async Function InstallCheckpointAsync(m As CivModel, v As CivVersion, progress As Action(Of Integer, String), ct As CancellationToken) As Task(Of MediaStudio.UserModel)
        Dim g = GroupFor(v.BaseModel)
        If g.Template Is Nothing Then Throw New Exception(Localizer.T("LocalMind can't run checkpoints of this type yet:") & " " & v.BaseModel)
        Dim f = v.BestFile()
        If f Is Nothing Then Throw New Exception(Localizer.T("This version has no file LocalMind can run (only formats like NVFP4, INT4 or W4A8). Try another version."))
        Dim fileName = SafeFileName(f.Name)
        Dim dest = Path.Combine(MediaStudio.UserModelFolder, fileName)
        Await AppCore.Downloads.RunAsync(m.Name & " · " & v.Name, If(IsVideoGroup(g), "Video", "Picture"), "civitai:" & v.Id, Function(p, c) DownloadAsync(f, dest, p, c), ct)
        Dim allInOne = g.Template = "sdxl" OrElse g.Template = "sd15"
        Dim um As New MediaStudio.UserModel With {
            .Name = m.Name & If(m.Versions.Count > 1, " · " & v.Name, ""), .Template = g.Template, .File = fileName,
            .Size = New FileInfo(dest).Length, .Format = If(allInOne, "checkpoint", If(fileName.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase), "gguf", "safetensors")),
            .BaseModel = v.BaseModel, .Source = $"Civitai #{m.Id}"}
        ' INT8 (ConvRot) files get the same small repair as the built-in ones.
        If um.Format = "safetensors" AndAlso Regex.IsMatch(fileName, "int8|convrot", RegexOptions.IgnoreCase) Then um.Format = "convrot"
        Dim what = (m.Name & " " & v.Name & " " & fileName).ToLowerInvariant()
        ' FLUX.1 dev fine-tunes need dev settings, not the schnell template's 4 steps.
        If v.BaseModel = "MiniMax H3" Then
            ' Ref2VA (people from pictures) or first/last-frame model; turbo / 8-step versions need 8 steps
            If what.Contains("ref2va") Then um.Template = "minimax-h3-ref2va"
            If Regex.IsMatch(what, "turbo|8[- ]?step|flash|distill") Then um.Steps = 8 : um.Cfg = 1.0
        ElseIf v.BaseModel = "LTXV 2.5" Then
            If what.Contains("dev") AndAlso Not what.Contains("distill") Then um.Template = "ltx-2.5-dev"
        ElseIf v.BaseModel = "Flux.1 D" OrElse v.BaseModel = "Flux.1 Krea" Then
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

    ''' <summary>A LoRA from a saved download link (recommended setups): file, family and trigger words.</summary>
    Public Async Function InstallLoraFromUrlAsync(url As String, fileName As String, family As String, triggers As String,
                                                  ct As CancellationToken) As Task(Of UserLora)
        Dim dest = Path.Combine(LoraLibrary.Folder, SafeFileName(fileName))
        If Not File.Exists(dest) Then
            Dim f As New CivFile With {.Name = fileName, .DownloadUrl = url}
            Await AppCore.Downloads.RunAsync(Path.GetFileNameWithoutExtension(fileName), "LoRA", "lora-url:" & url, Function(p, c) DownloadAsync(f, dest, p, c), ct)
        End If
        Dim l = LoraLibrary.All().FirstOrDefault(Function(x) x.Name.Equals(Path.GetFileNameWithoutExtension(dest), StringComparison.OrdinalIgnoreCase))
        If l IsNot Nothing Then
            If Not String.IsNullOrEmpty(family) Then l.Family = family
            If Not String.IsNullOrEmpty(triggers) Then l.Triggers = triggers
            l.Url = url
            LoraLibrary.Save()
        End If
        Return l
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
