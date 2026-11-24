Imports System
Imports System.Collections.Concurrent
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Net
Imports System.Net.Http
Imports System.Text.RegularExpressions
Imports System.Threading
Imports System.Threading.Tasks
Imports Newtonsoft.Json.Linq

''' <summary>
''' More about a picture/video model, like a Civitai page: description, sample pictures (with the settings they
''' were made with when Civitai has them), likes, license, base model. From the Hugging Face model card, and
''' from Civitai when the model came from there or Hugging Face has no pictures.
''' </summary>
Public Module ModelDetails

    Public Class Sample
        Public Property Url As String
        Public Property Nsfw As Boolean
        Public Property Prompt As String
        Public Property Settings As String     ' e.g. "30 steps · CFG 7 · Euler a · 832×1216"
    End Class

    Public Class Info
        Public Property Description As String = ""
        Public Property Samples As New List(Of Sample)()
        Public Property Likes As Long
        Public Property License As String = ""
        Public Property BaseModel As String = ""
        Public Property Updated As String = ""
        Public Property Tags As New List(Of String)()
        Public Property PageUrl As String = ""
        Public Property CivitaiUrl As String = ""
        Public Property TriggerWords As New List(Of String)()
        Public Property CivitaiSteps As String = ""      ' typical settings seen on Civitai sample pictures
        Public Property AuthorSettings As String = ""    ' what the author recommends (read from the description)
    End Class

    Private ReadOnly _cache As New ConcurrentDictionary(Of String, Info)(StringComparer.OrdinalIgnoreCase)

    Private Async Function GetJsonAsync(url As String, ct As CancellationToken) As Task(Of JToken)
        Using req As New HttpRequestMessage(HttpMethod.Get, url)
            req.Headers.UserAgent.ParseAdd("LocalMind/" & Updater.CurrentVersion.ToString())
            If url.Contains("civitai.") AndAlso Not String.IsNullOrWhiteSpace(AppCore.Settings.CivitaiApiKey) Then
                req.Headers.Authorization = New Headers.AuthenticationHeaderValue("Bearer", AppCore.Settings.CivitaiApiKey.Trim())
            End If
            Using cts = CancellationTokenSource.CreateLinkedTokenSource(ct)
                cts.CancelAfter(TimeSpan.FromSeconds(25))
                Using resp = Await Downloader.Http.SendAsync(req, cts.Token)
                    If Not resp.IsSuccessStatusCode Then Return Nothing
                    Dim text = Await resp.Content.ReadAsStringAsync(cts.Token)
                    Return JToken.Parse(text)
                End Using
            End Using
        End Using
    End Function

    Private Async Function GetTextAsync(url As String, ct As CancellationToken) As Task(Of String)
        Try
            Using cts = CancellationTokenSource.CreateLinkedTokenSource(ct)
                cts.CancelAfter(TimeSpan.FromSeconds(20))
                Using resp = Await Downloader.Http.GetAsync(url, cts.Token)
                    If Not resp.IsSuccessStatusCode Then Return ""
                    Return Await resp.Content.ReadAsStringAsync(cts.Token)
                End Using
            End Using
        Catch
            Return ""
        End Try
    End Function

    ''' <summary>Plain text from Markdown / HTML (for the description box).</summary>
    Public Function PlainText(s As String, Optional maxLen As Integer = 2500) As String
        If String.IsNullOrEmpty(s) Then Return ""
        Dim t = Regex.Replace(s, "^---[\s\S]*?\n---\s*", "")                 ' YAML front matter
        t = Regex.Replace(t, "<(script|style|video|audio|iframe|object|noscript|svg)\b[\s\S]*?</\1\s*>", "", RegexOptions.IgnoreCase)
        t = Regex.Replace(t, "<(video|audio|source|track|iframe|embed)\b[^>]*>", "", RegexOptions.IgnoreCase)
        t = Regex.Replace(t, "!\[[^\]]*\]\([^)]*\)", "")                       ' images
        t = Regex.Replace(t, "\[([^\]]+)\]\([^)]*\)", "$1")                    ' links → text
        t = Regex.Replace(t, "<br\s*/?>|</p>|</li>|</h\d>", vbLf, RegexOptions.IgnoreCase)
        t = Regex.Replace(t, "<[^>]+>", "")
        t = WebUtility.HtmlDecode(t)
        t = Regex.Replace(t, "Your browser does not support (the )?(video|audio)( tag| element)?\.?", "", RegexOptions.IgnoreCase)
        t = Regex.Replace(t, "^#{1,6}\s*", "", RegexOptions.Multiline)
        t = Regex.Replace(t, "[*_`]{1,3}", "")
        t = Regex.Replace(t, "\r", "")
        t = Regex.Replace(t, "[ \t\u00A0]+(?=\n)|(?<=\n)[ \t\u00A0]+", "")      ' stray indentation
        t = Regex.Replace(t, "\n{3,}", vbLf & vbLf).Trim()
        If t.Length > maxLen Then t = t.Substring(0, maxLen).TrimEnd() & "…"
        Return t.Replace(vbLf, vbCrLf)
    End Function

    Private Function IsImageFile(p As String) As Boolean
        Return Regex.IsMatch(p, "\.(png|jpe?g|webp|gif)$", RegexOptions.IgnoreCase)
    End Function

    ''' <summary>Model page details (cached). The repo is the Hugging Face repo; name is used to look it up on Civitai.</summary>
    Public Async Function GetAsync(r As MediaRecipe, allowNsfw As Boolean, ct As CancellationToken) As Task(Of Info)
        Dim repo = If(r.Repo, r.Variants.FirstOrDefault()?.Repo)
        Dim key = If(repo, r.Id) & "|" & allowNsfw.ToString()
        Dim cached As Info = Nothing
        If _cache.TryGetValue(key, cached) Then Return cached
        Dim info As New Info()
        If Not String.IsNullOrEmpty(repo) Then
            info.PageUrl = "https://huggingface.co/" & repo
            Try
                Dim m = Await GetJsonAsync($"https://huggingface.co/api/models/{repo}", ct)
                If m IsNot Nothing Then
                    info.Likes = If(m.Value(Of Long?)("likes"), 0)
                    info.Updated = Left(If(m.Value(Of String)("lastModified"), ""), 10)
                    Dim card = TryCast(m("cardData"), JObject)
                    info.License = If(card?("license")?.ToString(), "")
                    Dim bm = card?("base_model")
                    If bm IsNot Nothing Then info.BaseModel = If(TypeOf bm Is JArray, String.Join(", ", bm.Select(Function(x) x.ToString())), bm.ToString())
                    info.Tags = If(TryCast(m("tags"), JArray)?.Select(Function(t) t.ToString()).Where(Function(t) Not t.Contains(":")).Take(12).ToList(), New List(Of String))
                    Dim nsfwRepo = r.Uncensored OrElse info.Tags.Contains("not-for-all-audiences")
                    ' pictures from the model card's examples (widget) and pictures in the repo
                    For Each w In If(TryCast(card?("widget"), JArray), New JArray())
                        Dim u = w("output")?.Value(Of String)("url")
                        If String.IsNullOrEmpty(u) Then Continue For
                        If Not u.StartsWith("http") Then u = $"https://huggingface.co/{repo}/resolve/main/{u.TrimStart("/"c)}"
                        info.Samples.Add(New Sample With {.Url = u, .Nsfw = nsfwRepo, .Prompt = w.Value(Of String)("text")})
                    Next
                    For Each s In If(TryCast(m("siblings"), JArray), New JArray())
                        Dim p = s.Value(Of String)("rfilename")
                        If p IsNot Nothing AndAlso IsImageFile(p) AndAlso info.Samples.Count < 12 Then
                            Dim u = $"https://huggingface.co/{repo}/resolve/main/{Uri.EscapeDataString(p).Replace("%2F", "/")}"
                            If Not info.Samples.Any(Function(x) x.Url = u) Then info.Samples.Add(New Sample With {.Url = u, .Nsfw = nsfwRepo})
                        End If
                    Next
                End If
                Dim readme = Await GetTextAsync($"https://huggingface.co/{repo}/raw/main/README.md", ct)
                If readme.Length > 0 Then
                    info.Description = PlainText(readme)
                    ' pictures linked from the model card
                    For Each mm As Match In Regex.Matches(readme, "!\[[^\]]*\]\(([^)\s]+)|<img[^>]+src=[""']([^""']+)", RegexOptions.IgnoreCase)
                        Dim u = If(mm.Groups(1).Success, mm.Groups(1).Value, mm.Groups(2).Value)
                        If String.IsNullOrEmpty(u) OrElse u.Contains("shields.io") OrElse u.Contains("badge") Then Continue For
                        If Not u.StartsWith("http") Then u = $"https://huggingface.co/{repo}/resolve/main/{u.TrimStart("."c, "/"c)}"
                        If Not IsImageFile(u.Split("?"c)(0)) AndAlso Not u.Contains("cdn-uploads") AndAlso Not u.Contains("civitai") Then Continue For
                        If info.Samples.Count < 12 AndAlso Not info.Samples.Any(Function(x) x.Url = u) Then info.Samples.Add(New Sample With {.Url = u, .Nsfw = r.Uncensored})
                    Next
                End If
            Catch ex As OperationCanceledException When ct.IsCancellationRequested
                Throw
            Catch
            End Try
        End If

        If info.Samples.Count = 0 AndAlso Not String.IsNullOrEmpty(info.BaseModel) AndAlso Not info.BaseModel.Contains(","c) Then
            Try
                Dim baseRepo = info.BaseModel.Trim()
                Dim bm = Await GetJsonAsync($"https://huggingface.co/api/models/{baseRepo}", ct)
                Dim gated = bm?("gated") IsNot Nothing AndAlso bm("gated").Type <> JTokenType.Boolean OrElse (bm?("gated")?.Type = JTokenType.Boolean AndAlso bm("gated").Value(Of Boolean)())
                For Each sb In If(If(gated, Nothing, TryCast(bm?("siblings"), JArray)), New JArray())
                    Dim p = sb.Value(Of String)("rfilename")
                    If p IsNot Nothing AndAlso IsImageFile(p) AndAlso info.Samples.Count < 8 Then
                        info.Samples.Add(New Sample With {.Url = $"https://huggingface.co/{baseRepo}/resolve/main/{Uri.EscapeDataString(p).Replace("%2F", "/")}", .Nsfw = r.Uncensored})
                    End If
                Next
                If info.Description.Length < 300 Then
                    Dim baseReadme = Await GetTextAsync($"https://huggingface.co/{baseRepo}/raw/main/README.md", ct)
                    If baseReadme.Length > 0 Then info.Description = (info.Description & vbCrLf & vbCrLf & PlainText(baseReadme)).Trim()
                End If
            Catch ex As OperationCanceledException When ct.IsCancellationRequested
                Throw
            Catch
            End Try
        End If

        ' Civitai: most SDXL / SD 1.5 / FLUX community models started there, with example pictures and their settings.
        If (r.Auto OrElse r.Family = "SDXL" OrElse r.Family = "SD1.5") AndAlso Not r.IsVideo AndAlso
           (info.Samples.Count < 4 OrElse r.Family = "SDXL" OrElse r.Family = "SD1.5") Then
            Try
                Await AddCivitaiAsync(r, info, allowNsfw, ct)
            Catch ex As OperationCanceledException When ct.IsCancellationRequested
                Throw
            Catch
            End Try
        End If
        Dim more As String = Nothing
        _extraText.TryRemove(info, more)
        info.AuthorSettings = Recommended(info.Description & vbLf & more)
        _cache(key) = info
        Return info
    End Function

    Private ReadOnly _extraText As New ConcurrentDictionary(Of Info, String)()

    ''' <summary>Steps / CFG / sampler the author recommends, read from the description ("Steps: 25-30, CFG 5").</summary>
    Public Function Recommended(text As String) As String
        If String.IsNullOrWhiteSpace(text) Then Return ""
        Dim parts As New List(Of String)
        Dim st = Regex.Match(text, "(?i)(?<!hires\s)(\d{1,3})(?:\s*(?:-|–|~|to)\s*(\d{1,3}))?(\+?)\s*(?:sampling\s+|inference\s+)?steps\b")
        If Not st.Success Then st = Regex.Match(text, "(?i)(?<!hires\s)\bsteps?\b\s*[:=]?\s*(\d{1,3})(?:\s*(?:-|–|~|to)\s*(\d{1,3}))?(\+?)")
        If st.Success Then parts.Add(Localizer.T("Steps") & " " & st.Groups(1).Value & If(st.Groups(2).Success, "–" & st.Groups(2).Value, "") & st.Groups(3).Value)
        Dim cfg = Regex.Match(text, "(?i)\b(?:cfg(?:\s*scale)?|guidance(?:\s*scale)?)\b\s*[:=]?\s*(\d{1,2}(?:\.\d)?)(?:\s*(?:-|–|~|to)\s*(\d{1,2}(?:\.\d)?))?")
        If cfg.Success Then parts.Add("CFG " & cfg.Groups(1).Value & If(cfg.Groups(2).Success, "–" & cfg.Groups(2).Value, ""))
        Dim smp = Regex.Match(text, "(?i)\b(DPM\+\+ ?SDE(?: Karras)?|DPM\+\+ ?[23]M(?: SDE)?(?: Karras)?|DPM\+\+ ?2S a|Euler a(?:ncestral)?|Euler|LCM|DDIM|UniPC|Heun|res_multistep|er_sde)\b")
        If smp.Success Then parts.Add(smp.Value)
        Dim cs = Regex.Match(text, "(?i)clip\s*skip[^\d\n]{0,6}(\d)")
        If cs.Success Then parts.Add("CLIP skip " & cs.Groups(1).Value)
        Return String.Join(" · ", parts)
    End Function

    Private Function SearchName(r As MediaRecipe) As String
        Dim n = r.Name.Split("·"c)(0)
        n = Regex.Replace(n, "\(.*?\)", "")
        n = Regex.Replace(n, "(?i)\b(gguf|int8|convrot|fp16|fp32|bf16|q\d_k(_[ms])?|q8_0|sdxl|safetensors)\b", "")
        n = Regex.Replace(n, "[-_]+", " ").Trim()
        Return n
    End Function

    Private Async Function AddCivitaiAsync(r As MediaRecipe, info As Info, allowNsfw As Boolean, ct As CancellationToken) As Task
        Dim q = SearchName(r)
        If q.Length < 3 Then Return
        Dim baseUrl = If(allowNsfw, "https://civitai.red/api/v1", "https://civitai.com/api/v1")
        Dim res = Await GetJsonAsync($"{baseUrl}/models?limit=5&types=Checkpoint&query={Uri.EscapeDataString(q)}&nsfw={allowNsfw.ToString().ToLowerInvariant()}", ct)
        Dim items = TryCast(res?("items"), JArray)
        If items Is Nothing OrElse items.Count = 0 Then Return
        ' the result whose name matches best
        ' name words without version numbers ("Juggernaut XL v9" → juggernaut, xl)
        Dim words = q.ToLowerInvariant().Split(" "c, StringSplitOptions.RemoveEmptyEntries).
                    Where(Function(w) w.Length > 1 AndAlso Not Regex.IsMatch(w, "^v?\d+(\.\d+)*[a-z]?$")).ToArray()
        If words.Length = 0 Then Return
        Dim best = items.OrderByDescending(Function(i) words.Count(Function(w) If(i.Value(Of String)("name"), "").ToLowerInvariant().Contains(w))).First()
        If words.Count(Function(w) If(best.Value(Of String)("name"), "").ToLowerInvariant().Contains(w)) < Math.Max(1, CInt(Math.Ceiling(words.Length * 0.7))) Then Return
        info.CivitaiUrl = If(allowNsfw, "https://civitai.red/models/", "https://civitai.com/models/") & best.Value(Of Long)("id")
        If String.IsNullOrEmpty(info.Description) OrElse info.Description.Length < 80 Then info.Description = PlainText(best.Value(Of String)("description"))
        info.Likes = Math.Max(info.Likes, If(best("stats")?.Value(Of Long?)("thumbsUpCount"), 0))
        Dim ver = TryCast(best("modelVersions"), JArray)?.FirstOrDefault()
        If ver Is Nothing Then Return
        If String.IsNullOrEmpty(info.BaseModel) Then info.BaseModel = ver.Value(Of String)("baseModel")
        info.TriggerWords = If(TryCast(ver("trainedWords"), JArray)?.Select(Function(t) t.ToString()).Where(Function(t) t <> "").ToList(), New List(Of String))
        _extraText(info) = PlainText(ver.Value(Of String)("description"), 4000)
        Dim stepsSeen As New List(Of Integer)
        For Each img In If(TryCast(ver("images"), JArray), New JArray())
            If img.Value(Of String)("type") = "video" Then Continue For
            Dim lvl = img.Value(Of Integer?)("nsfwLevel").GetValueOrDefault()
            Dim meta = TryCast(img("meta"), JObject)
            Dim parts As New List(Of String)
            Dim st = meta?.Value(Of Integer?)("steps")
            If st.HasValue Then
                parts.Add(st.Value & " steps")
                stepsSeen.Add(st.Value)
            End If
            Dim cfg = meta?.Value(Of Double?)("cfgScale")
            If cfg.HasValue Then parts.Add("CFG " & cfg.Value.ToString("0.#", Globalization.CultureInfo.InvariantCulture))
            Dim smp = meta?.Value(Of String)("sampler")
            If Not String.IsNullOrEmpty(smp) Then parts.Add(smp)
            Dim w = img.Value(Of Integer?)("width"), h = img.Value(Of Integer?)("height")
            If w.HasValue AndAlso h.HasValue Then parts.Add($"{w}×{h}")
            If info.Samples.Count >= 16 Then Exit For
            info.Samples.Add(New Sample With {
                .Url = Civitai.ThumbUrl(img.Value(Of String)("url"), 450), .Nsfw = lvl > 1 OrElse (lvl = 0 AndAlso (r.Uncensored OrElse best.Value(Of Boolean?)("nsfw").GetValueOrDefault())),
                .Prompt = meta?.Value(Of String)("prompt"), .Settings = String.Join(" · ", parts)})
        Next
        If stepsSeen.Count > 0 Then
            stepsSeen.Sort()
            info.CivitaiSteps = $"{stepsSeen(stepsSeen.Count \ 2)} steps (typical on Civitai, range {stepsSeen.First()}–{stepsSeen.Last()})"
        End If
    End Function

    Private Function Inv(d As Double) As String
        Return d.ToString("0.##", Globalization.CultureInfo.InvariantCulture)
    End Function

    ''' <summary>The settings LocalMind uses for this model, in plain words.</summary>
    Public Function SettingsLines(r As MediaRecipe) As List(Of String)
        Dim l As New List(Of String)
        If r.IsUpscaler Then
            l.Add(Localizer.T("Makes pictures 4× bigger (no prompt needed)."))
            Return l
        End If
        If r.Kind = "tool" Then Return l
        Dim steps = If(r.HighSteps > 0, $"{r.HighSteps} + {r.Steps} ({Localizer.T("two experts")})", r.Steps.ToString())
        l.Add($"{Localizer.T("Steps")}: {steps}" & If(r.Speed IsNot Nothing, $"   ·   ⚡ {Localizer.T("fast mode")}: {If(r.Speed.HighSteps > 0, r.Speed.HighSteps & " + ", "")}{r.Speed.Steps} ({r.Speed.Label.TrimStart("⚡"c, " "c)})", ""))
        l.Add($"{Localizer.T("Guidance (CFG)")}: {Inv(r.Cfg)}" & If(r.Speed IsNot Nothing, $"   ·   ⚡ {Inv(r.Speed.Cfg)}", "") &
              $"   ·   {Localizer.T("Sampler")}: {If(r.Sampler, "euler")}" & If(r.FlowShift > 0, $"   ·   {Localizer.T("Flow shift")}: {Inv(r.FlowShift)}", ""))
        If r.Width > 0 Then l.Add($"{Localizer.T("Size")}: {r.Width} × {r.Height}")
        If r.IsVideo AndAlso r.Frames > 0 Then
            Dim fps = If(r.Fps > 0, r.Fps, 16)
            l.Add($"{Localizer.T("Video")}: {r.Frames} {Localizer.T("frames")} @ {fps} fps ≈ {Inv(r.Frames / fps)} s")
        End If
        Dim extra = If(r.Extra, "")
        If extra.Contains("--clip-skip 2") Then l.Add(Localizer.T("CLIP skip: 2"))
        If extra.Contains("--prediction v") Then l.Add(Localizer.T("V-prediction model"))
        If Not String.IsNullOrEmpty(r.Prefix) Then l.Add($"{Localizer.T("Added to every prompt")}: {r.Prefix.Trim()}")
        If Not String.IsNullOrEmpty(r.Negative) Then l.Add($"{Localizer.T("Negative prompt")}: {If(r.Negative.Length > 110, r.Negative.Substring(0, 110) & "…", r.Negative)}")
        If r.Input = "required" Then l.Add(Localizer.T("Needs a picture to start from (photo → video / edit)."))
        Return l
    End Function

    '──────────────── pictures ────────────────
    Private ReadOnly _imgCache As New ConcurrentDictionary(Of String, Byte())()

    ''' <summary>Downloads a sample picture (kept in memory while LocalMind runs).</summary>
    Public Async Function GetImageAsync(url As String, ct As CancellationToken) As Task(Of Drawing.Image)
        Dim bytes As Byte() = Nothing
        If Not _imgCache.TryGetValue(url, bytes) Then
            Using cts = CancellationTokenSource.CreateLinkedTokenSource(ct)
                cts.CancelAfter(TimeSpan.FromSeconds(30))
                Using resp = Await Downloader.Http.GetAsync(url, cts.Token)
                    If Not resp.IsSuccessStatusCode Then Return Nothing
                    bytes = Await resp.Content.ReadAsByteArrayAsync(cts.Token)
                End Using
            End Using
            If bytes.Length > 25000000 Then Return Nothing
            _imgCache(url) = bytes
        End If
        Try
            Using ms As New MemoryStream(bytes)
                Using img = Drawing.Image.FromStream(ms)
                    Return New Drawing.Bitmap(img)
                End Using
            End Using
        Catch
            Return Nothing      ' webp and other formats GDI+ can't read
        End Try
    End Function

    ''' <summary>A strongly blurred copy (for NSFW pictures while "Show NSFW" is off).</summary>
    Public Function Blur(img As Drawing.Image) As Drawing.Image
        Dim small As New Drawing.Bitmap(Math.Max(1, img.Width \ 24), Math.Max(1, img.Height \ 24))
        Using g = Drawing.Graphics.FromImage(small)
            g.InterpolationMode = Drawing.Drawing2D.InterpolationMode.HighQualityBilinear
            g.DrawImage(img, 0, 0, small.Width, small.Height)
        End Using
        Dim big As New Drawing.Bitmap(img.Width, img.Height)
        Using g = Drawing.Graphics.FromImage(big)
            g.InterpolationMode = Drawing.Drawing2D.InterpolationMode.HighQualityBicubic
            g.DrawImage(small, 0, 0, big.Width, big.Height)
        End Using
        small.Dispose()
        Return big
    End Function
End Module
