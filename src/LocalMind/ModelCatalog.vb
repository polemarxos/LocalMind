Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Net.Http
Imports System.Reflection
Imports System.Text.RegularExpressions
Imports System.Threading
Imports System.Threading.Tasks
Imports Newtonsoft.Json
Imports Newtonsoft.Json.Linq

Public Enum FitLevel
    Fits          ' entirely in VRAM — fast
    MoeOffload    ' MoE with experts in RAM — still quick
    PartialOffload    ' split GPU/CPU — slower
    TooBig
End Enum

''' <summary>One model repository in the catalog (compact JSON keys match catalog.json).</summary>
Public Class CatalogEntry
    <JsonProperty("repo")> Public Property Repo As String
    <JsonProperty("name")> Public Property Name As String
    <JsonProperty("author")> Public Property Author As String
    <JsonProperty("params")> Public Property Params As Long
    <JsonProperty("arch")> Public Property Arch As String
    <JsonProperty("ctx")> Public Property ContextLength As Long
    <JsonProperty("dl")> Public Property Downloads As Long
    <JsonProperty("likes")> Public Property Likes As Long
    <JsonProperty("pipe")> Public Property Pipeline As String
    <JsonProperty("lic")> Public Property License As String
    <JsonProperty("upd")> Public Property Updated As String
    <JsonProperty("tags")> Public Property Tags As New List(Of String)()

    <JsonIgnore> Public ReadOnly Property ParamsB As Double
        Get
            Return Params / 1.0E+9
        End Get
    End Property

    <JsonIgnore> Public ReadOnly Property IsVision As Boolean
        Get
            Return Pipeline = "image-text-to-text" OrElse Pipeline = "any-to-any" OrElse
                   Tags.Contains("image-text-to-text") OrElse Tags.Contains("any-to-any") OrElse
                   If(Arch, "").Contains("vl")
        End Get
    End Property

    <JsonIgnore> Public ReadOnly Property ActiveParamsB As Double
        Get
            Dim m = Regex.Match(If(Name, ""), "[-_]A(\d+(?:\.\d+)?)B", RegexOptions.IgnoreCase)
            If m.Success Then Return Double.Parse(m.Groups(1).Value, Globalization.CultureInfo.InvariantCulture)
            Return 0
        End Get
    End Property

    <JsonIgnore> Public ReadOnly Property IsMoE As Boolean
        Get
            Return If(Arch, "").Contains("moe") OrElse Tags.Contains("moe") OrElse ActiveParamsB > 0 OrElse
                   Regex.IsMatch(If(Arch, ""), "^(gpt-oss|deepseek|glm4moe|mixtral|kimi|qwen3next|qwen4exp|llama4|dots1|ernie4_5-moe)")
        End Get
    End Property

    <JsonIgnore> Public ReadOnly Property IsCoder As Boolean
        Get
            Return Regex.IsMatch(Name & " " & String.Join(" ", Tags), "coder|code|devstral|codestral|starcoder|codegemma", RegexOptions.IgnoreCase)
        End Get
    End Property

    <JsonIgnore> Public ReadOnly Property IsReasoning As Boolean
        Get
            Return Regex.IsMatch(If(Name, ""), "(^|[-_])R1|think|reason|qwq|gpt-oss|magistral|phi-4-mini-flash|deepseek-v3\.[1-9]|qwen3(?!-.*instruct)|qwen3\.\d|nemotron|glm-[45]", RegexOptions.IgnoreCase)
        End Get
    End Property

    <JsonIgnore> Public ReadOnly Property IsUncensored As Boolean
        Get
            Return Regex.IsMatch(If(Name, ""), "uncensored|uncencored|abliterated|heretic|nsfw", RegexOptions.IgnoreCase)
        End Get
    End Property

    <JsonIgnore> Public ReadOnly Property Badges As String
        Get
            Dim b As New List(Of String)
            If IsVision Then b.Add("👁 Vision")
            If IsReasoning Then b.Add("🧠 Think")
            If IsCoder Then b.Add("💻 Code")
            If IsMoE Then b.Add("⚡ MoE" & If(ActiveParamsB > 0, $" {ActiveParamsB:0.#}B active", ""))
            If IsUncensored Then b.Add("🔓 Uncensored")
            Return String.Join("  ", b)
        End Get
    End Property

    <JsonIgnore> Public ReadOnly Property SizeClass As String
        Get
            Dim p = ParamsB
            If p < 4 Then Return "Tiny (<4B)"
            If p < 10 Then Return "Small (4–10B)"
            If p < 20 Then Return "Medium (10–20B)"
            If p < 40 Then Return "Large (20–40B)"
            If p < 130 Then Return "Very large (40–130B)"
            Return "Huge (130B+)"
        End Get
    End Property
End Class

''' <summary>One downloadable quantization (may be several split files).</summary>
Public Class QuantOption
    Public Property Label As String
    Public Property Files As New List(Of RepoFile)()
    Public ReadOnly Property TotalBytes As Long
        Get
            Return Files.Sum(Function(f) f.Size)
        End Get
    End Property
    Public Overrides Function ToString() As String
        Return Label & "  (" & Downloader.FormatBytes(TotalBytes) & ")"
    End Function
End Class

Public Class RepoFile
    Public Property Path As String
    Public Property Size As Long
    Public Property Sha256 As String
End Class

Public Class RepoFiles
    Public Property Quants As New List(Of QuantOption)()
    Public Property Mmproj As RepoFile
    ''' <summary>Multi-token-prediction head for faster generation (llama.cpp --spec-type draft-mtp).</summary>
    Public Property Mtp As RepoFile
End Class

Public Module ModelCatalog

    ''' <summary>
    ''' Where the model list comes from: GGUF quantizers and AI labs on Hugging Face, best first
    ''' (the same model from an earlier source wins). Pages of 1000 models, and the minimum downloads
    ''' for a model to be listed. Kept in step with tools/build_catalog.py.
    ''' </summary>
    Public ReadOnly Sources As (Author As String, Pages As Integer, MinDownloads As Integer)() = {
        ("unsloth", 1, 0), ("bartowski", 3, 0), ("lmstudio-community", 1, 0), ("ggml-org", 1, 0),
        ("Qwen", 1, 0), ("google", 1, 0), ("mistralai", 1, 0), ("microsoft", 1, 0), ("ibm-granite", 1, 0),
        ("LiquidAI", 1, 0), ("nvidia", 1, 0), ("NousResearch", 1, 0), ("openbmb", 1, 0), ("allenai", 1, 0),
        ("tiiuae", 1, 0), ("internlm", 1, 0), ("HuggingFaceTB", 1, 0), ("zai-org", 1, 0), ("janhq", 1, 0),
        ("Menlo", 1, 0), ("huihui-ai", 1, 0), ("mlabonne", 1, 0),
        ("MaziyarPanahi", 1, 100), ("QuantFactory", 1, 100), ("mradermacher", 3, 100), ("second-state", 1, 100),
        ("Mungert", 1, 100), ("TheBloke", 1, 100), ("DavidAU", 1, 100), ("TheDrummer", 1, 50), ("Lewdiculous", 1, 100),
        ("gabriellarson", 1, 100), ("prithivMLmods", 1, 100), ("DevQuasar", 1, 100), ("tensorblock", 1, 100),
        ("RichardErkhov", 2, 100)}

    ''' <summary>Sources up to mlabonne are the main quantizers and AI labs.</summary>
    Public Const TopTierCount As Integer = 22

    Public ReadOnly TrustedAuthors As String() = Sources.Select(Function(s) s.Author.ToLowerInvariant()).ToArray()

    ''' <summary>Approximate bits per weight of common llama.cpp quantizations (incl. overhead).</summary>
    Public ReadOnly QuantBits As New Dictionary(Of String, Double)(StringComparer.OrdinalIgnoreCase) From {
        {"Q8_0", 8.5}, {"Q6_K", 6.6}, {"Q5_K_M", 5.7}, {"Q5_K_S", 5.5}, {"Q4_K_M", 4.85}, {"Q4_K_S", 4.6},
        {"IQ4_XS", 4.3}, {"IQ4_NL", 4.5}, {"Q4_0", 4.55}, {"Q3_K_L", 4.3}, {"Q3_K_M", 3.9}, {"Q3_K_S", 3.5},
        {"IQ3_M", 3.7}, {"IQ3_XXS", 3.1}, {"Q2_K", 3.0}, {"Q2_K_L", 3.3}, {"IQ2_M", 2.7}, {"IQ2_XXS", 2.2},
        {"IQ1_M", 1.9}, {"MXFP4", 4.25}, {"MXFP4_MOE", 4.25}, {"BF16", 16}, {"F16", 16}
    }

    ''' <summary>Preference order when picking a quant automatically (best quality first).</summary>
    Public ReadOnly AutoOrder As String() = {"Q8_0", "Q6_K", "Q5_K_M", "Q4_K_M", "IQ4_XS", "Q3_K_M", "IQ3_M", "IQ3_XXS", "Q2_K", "IQ2_M"}

    Private ReadOnly QuantRx As New Regex(
        "(?:^|[-_.])((?:UD-)?(?:I?Q\d(?:_[A-Z0-9]+)*|BF16|F16|F32|MXFP4(?:_MOE)?))(?:-\d{5}-of-\d{5})?\.gguf$",
        RegexOptions.IgnoreCase)

    Public Function ParseQuant(fileName As String) As String
        Dim m = QuantRx.Match(IO.Path.GetFileName(If(fileName, "")))
        Return If(m.Success, m.Groups(1).Value.ToUpperInvariant(), "")
    End Function

    ''' <summary>"UD-Q4_K_XL" → "Q4_K_M"-ish base used for size estimates.</summary>
    Private Function BaseQuant(label As String) As String
        Dim l = label.ToUpperInvariant().Replace("UD-", "")
        If l.EndsWith("_XL") Then l = l.Substring(0, l.Length - 3) & "_M"
        Return l
    End Function

    Public Function BitsFor(label As String) As Double
        Dim b As Double
        If QuantBits.TryGetValue(BaseQuant(label), b) Then Return b
        Dim m = Regex.Match(label, "Q(\d)")
        If m.Success Then Return CInt(m.Groups(1).Value) + 0.8
        Return 4.85
    End Function

    '────────────────────────────────────────────────────────────
    '  Memory estimates
    '────────────────────────────────────────────────────────────
    Public Function EstimateFileBytes(e As CatalogEntry, quant As String) As Long
        Return CLng(e.Params * BitsFor(quant) / 8.0)
    End Function

    ''' <summary>
    ''' Rough KV-cache + runtime overhead. Deliberately conservative: modern GQA/hybrid models
    ''' usually need less, so "fits" here really means fits.
    ''' </summary>
    Public Function EstimateOverheadBytes(paramsB As Double, contextTokens As Integer) As Long
        ' Calibrated on GQA models (Llama 3 8B ≈ 0.7 GB @8K). Slight under-estimates are harmless:
        ' llama.cpp's --fit moves the overflow to system RAM instead of failing.
        Dim kvGB = 0.25 * Math.Sqrt(Math.Max(0.5, paramsB)) * (contextTokens / 8192.0)
        Return CLng((0.6 + kvGB) * 1024 * 1024 * 1024)
    End Function

    Public Function Fit(e As CatalogEntry, fileBytes As Long, hw As HardwareInfo, contextTokens As Integer) As FitLevel
        Dim need = fileBytes + EstimateOverheadBytes(e.ParamsB, contextTokens)
        Dim vram = hw.VramBytes
        Dim ram = hw.RamBytes
        If vram > 0 AndAlso need <= vram * 0.95 Then Return FitLevel.Fits
        ' Keep ~6 GB of RAM for Windows and other apps.
        Dim freeRam = Math.Max(0, ram - 6L * 1024 * 1024 * 1024)
        If need <= vram + freeRam * 0.85 Then
            If e.IsMoE AndAlso vram > 0 Then Return FitLevel.MoeOffload
            Return FitLevel.PartialOffload
        End If
        Return FitLevel.TooBig
    End Function

    Public Function FitText(f As FitLevel, Optional cpuOnly As Boolean = False) As String
        Select Case f
            Case FitLevel.Fits : Return Localizer.T("✔ Fits in VRAM")
            Case FitLevel.MoeOffload : Return Localizer.T("◐ MoE offload (good)")
            Case FitLevel.PartialOffload : Return Localizer.T(If(cpuOnly, "◐ Runs on CPU", "◐ GPU+CPU (slower)"))
            Case Else : Return Localizer.T("✖ Too big")
        End Select
    End Function

    ''' <summary>Best quant for this PC using estimated sizes (before the real file list is known).</summary>
    Public Function AutoQuant(e As CatalogEntry, hw As HardwareInfo, ctx As Integer) As String
        Dim virtualFiles As New RepoFiles()
        For Each q In AutoOrder
            virtualFiles.Quants.Add(New QuantOption With {.Label = q, .Files = New List(Of RepoFile) From {
                New RepoFile With {.Path = q, .Size = EstimateFileBytes(e, q)}}})
        Next
        Dim pick = PickQuant(e, virtualFiles, "Auto", hw, ctx)
        Return If(pick?.Label, "Q4_K_M")
    End Function

    ''' <summary>
    ''' Chooses the file to download. Policy:
    '''  • dense models: best quality that fits fully in VRAM (≥ ~3 bits), else a Q3–Q5 that runs with CPU help;
    '''  • MoE models: only a few billion parameters work per token, so a Q4-class file with experts in RAM
    '''    beats squeezing a 2-bit file into VRAM — prefer ≥ 4 bits.
    ''' </summary>
    Public Function PickQuant(e As CatalogEntry, files As RepoFiles, wanted As String, hw As HardwareInfo, ctx As Integer) As QuantOption
        If files.Quants.Count = 0 Then Return Nothing
        If Not String.IsNullOrEmpty(wanted) AndAlso Not wanted.StartsWith("Auto", StringComparison.OrdinalIgnoreCase) Then
            Dim exact = files.Quants.FirstOrDefault(Function(q) q.Label.Equals(wanted, StringComparison.OrdinalIgnoreCase))
            If exact Is Nothing Then exact = files.Quants.FirstOrDefault(Function(q) BaseQuant(q.Label).Equals(wanted, StringComparison.OrdinalIgnoreCase))
            If exact IsNot Nothing Then Return exact
            ' Closest size to what was asked for.
            Dim target = EstimateFileBytes(e, wanted)
            Return files.Quants.Where(Function(q) Not IsFullPrecision(q.Label)).
                OrderBy(Function(q) Math.Abs(q.TotalBytes - target)).FirstOrDefault()
        End If

        Dim candidates = files.Quants.Where(Function(q) Not IsFullPrecision(q.Label)).OrderByDescending(Function(q) q.TotalBytes).ToList()
        If candidates.Count = 0 Then candidates = files.Quants.OrderByDescending(Function(q) q.TotalBytes).ToList()
        Dim fitOf = Function(q As QuantOption) Fit(e, q.TotalBytes, hw, ctx)
        Dim bits = Function(q As QuantOption) EffectiveBits(e, q)

        Dim fitting = candidates.Where(Function(q) fitOf(q) = FitLevel.Fits AndAlso bits(q) <= 8.7 AndAlso bits(q) >= 3.0).ToList()
        If e.IsMoE Then
            Dim strong = fitting.Where(Function(q) bits(q) >= 4.0).ToList()
            If strong.Count > 0 Then Return PreferStandard(strong)
            Dim moe = candidates.Where(Function(q) fitOf(q) <> FitLevel.TooBig AndAlso bits(q) >= 3.5 AndAlso bits(q) <= 5.2).ToList()
            If moe.Count > 0 Then Return PreferStandard(moe)
        End If
        If fitting.Count > 0 Then Return PreferStandard(fitting)
        Dim offload = candidates.Where(Function(q) fitOf(q) <> FitLevel.TooBig AndAlso bits(q) >= 3.0 AndAlso bits(q) <= 5.2).ToList()
        If offload.Count > 0 Then Return PreferStandard(offload)
        ' Nothing runs comfortably: smallest usable file (≥ 2 bits) so the user can at least try.
        Dim smallest = candidates.Where(Function(q) bits(q) >= 2.0 AndAlso fitOf(q) <> FitLevel.TooBig).LastOrDefault()
        If smallest IsNot Nothing Then Return smallest
        Return candidates.OrderBy(Function(q) Math.Abs(bits(q) - 4.85)).First()
    End Function

    ''' <summary>
    ''' The default Model Manager view: only strong, current, popular models that run entirely
    ''' in this PC's VRAM at good quality (≥ ~4-bit). One or two per family/size so near-duplicates
    ''' (e.g. three re-uploads of the same 8B) don't crowd the list.
    ''' With no usable GPU it falls back to the best small models that run well on the CPU.
    ''' </summary>
    Public Function BestThatFit(all As IEnumerable(Of CatalogEntry), hw As HardwareInfo, ctx As Integer,
                                Optional uncensored As Boolean = False) As HashSet(Of String)
        Dim cpuOnly = hw.VramBytes = 0
        ' Uncensored fine-tunes are a separate list (never mixed into the normal picks) and are
        ' much less downloaded than the originals, so they get a lower popularity bar.
        Dim minDownloads = If(uncensored, 1000, 5000)
        Dim cutoff = DateTime.Today.AddMonths(If(uncensored, -36, -18)).ToString("yyyy-MM-dd")
        Dim pool = all.Where(Function(e)
                                 If e.Params <= 0 OrElse e.IsUncensored <> uncensored Then Return False
                                 ' The normal picks come from the main quantizers and AI labs (not community fine-tunes).
                                 If Not uncensored AndAlso Array.IndexOf(TrustedAuthors, If(e.Author, "").ToLowerInvariant()) >= TopTierCount Then Return False
                                 If e.Downloads < minDownloads Then Return False
                                 If Not String.IsNullOrEmpty(e.Updated) AndAlso String.CompareOrdinal(e.Updated, cutoff) < 0 Then Return False
                                 Dim q = AutoQuant(e, hw, ctx)
                                 If BitsFor(q) < 4.2 Then Return False            ' quality floor: IQ4_XS / Q4_K_M or better
                                 Dim f = Fit(e, EstimateFileBytes(e, q), hw, ctx)
                                 If cpuOnly Then Return f <> FitLevel.TooBig AndAlso (e.ParamsB <= 9 OrElse (e.IsMoE AndAlso e.ActiveParamsB <= 4))
                                 Return f = FitLevel.Fits
                             End Function).ToList()

        ' Score: popularity, newer is better, bigger is smarter (it already fits, so bigger = better).
        Dim score = Function(e As CatalogEntry)
                        Dim ageDays = 365.0
                        Dim d As DateTime
                        If DateTime.TryParse(e.Updated, Globalization.CultureInfo.InvariantCulture, Globalization.DateTimeStyles.None, d) Then ageDays = (DateTime.Today - d).TotalDays
                        Return Math.Log10(Math.Max(10, e.Downloads)) + 1.5 * Math.Log10(Math.Max(0.5, e.ParamsB)) - ageDays / 365.0
                    End Function

        ' Keep the top 2 per architecture × size bucket (removes near-duplicate uploads and old versions).
        Dim bucket = Function(e As CatalogEntry) If(e.Arch, "") & "|" & e.SizeClass & "|" & If(e.IsVision, "v", "") & If(e.IsCoder, "c", "")
        Dim best = pool.GroupBy(bucket).
                        SelectMany(Function(g) g.OrderByDescending(score).Take(If(uncensored, 6, 4))).
                        OrderByDescending(score).Take(If(uncensored, 200, 300)).
                        Select(Function(e) e.Repo)
        Return New HashSet(Of String)(best, StringComparer.OrdinalIgnoreCase)
    End Function

    ''' <summary>Real bits per weight from the file size (falls back to the quant name).</summary>
    Private Function EffectiveBits(e As CatalogEntry, q As QuantOption) As Double
        If e.Params > 0 AndAlso q.TotalBytes > 0 Then Return q.TotalBytes * 8.0 / e.Params
        Return BitsFor(q.Label)
    End Function

    ''' <summary>list is ordered largest-first: take the largest, unless it is a legacy format
    ''' (Q4_0/Q4_1/Q5_0/Q5_1) and a modern K/IQ quant of nearly the same size exists.</summary>
    Private Function PreferStandard(list As List(Of QuantOption)) As QuantOption
        Dim best = list(0)
        If IsLegacy(best.Label) Then
            Dim alt = list.Skip(1).FirstOrDefault(Function(q) Not IsLegacy(q.Label) AndAlso q.TotalBytes >= best.TotalBytes * 0.9)
            If alt IsNot Nothing Then Return alt
        End If
        Return best
    End Function

    Private Function IsLegacy(label As String) As Boolean
        Dim l = label.ToUpperInvariant()
        Return l = "Q4_0" OrElse l = "Q4_1" OrElse l = "Q5_0" OrElse l = "Q5_1"
    End Function

    Private Function IsFullPrecision(label As String) As Boolean
        Dim l = label.ToUpperInvariant()
        Return l = "BF16" OrElse l = "F16" OrElse l = "F32"
    End Function

    '────────────────────────────────────────────────────────────
    '  Catalog loading
    '────────────────────────────────────────────────────────────
    Private ReadOnly Property CachePath As String
        Get
            Return IO.Path.Combine(AppPaths.Roaming, "catalog-cache.json")
        End Get
    End Property

    Public Function LoadEmbedded() As List(Of CatalogEntry)
        Using s = Assembly.GetExecutingAssembly().GetManifestResourceStream("catalog.json")
            If s Is Nothing Then Return New List(Of CatalogEntry)()
            Using r As New StreamReader(s)
                Return JsonConvert.DeserializeObject(Of List(Of CatalogEntry))(r.ReadToEnd())
            End Using
        End Using
    End Function

    ''' <summary>Cached live catalog if fresh enough, else the one compiled into the app.</summary>
    Public Function LoadCachedOrEmbedded(ByRef ageText As String) As List(Of CatalogEntry)
        Try
            If File.Exists(CachePath) Then
                Dim list = JsonConvert.DeserializeObject(Of List(Of CatalogEntry))(File.ReadAllText(CachePath))
                If list IsNot Nothing AndAlso list.Count > 50 Then
                    Dim age = DateTime.Now - File.GetLastWriteTime(CachePath)
                    ageText = If(age.TotalHours < 1, "updated just now", $"updated {age.TotalHours:0}h ago")
                    Return ModelFamilies.KeepChat(list, Installed)
                End If
            End If
        Catch
        End Try
        ageText = "built-in list"
        Return ModelFamilies.KeepChat(LoadEmbedded(), Installed)
    End Function

    ''' <summary>Set by the app: whether a catalog model is already downloaded (it stays listed).</summary>
    Public Installed As Func(Of CatalogEntry, Boolean) = Nothing

    Public Function CacheIsStale() As Boolean
        Return Not File.Exists(CachePath) OrElse (DateTime.Now - File.GetLastWriteTime(CachePath)).TotalHours > 24
    End Function

    Private ReadOnly NonChatArch As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
        "flux", "qwen_image", "lumina2", "wan", "ltxv", "hyvid", "moss_image", "gemma-embedding", "bert",
        "nomic-bert", "sd3", "chroma", "hidream", "t5", "t5encoder", "clip", "jina-bert-v2", "xlm-roberta"}
    Private ReadOnly NonChatPipe As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
        "text-to-image", "image-to-image", "text-to-video", "image-to-video", "image-text-to-video",
        "feature-extraction", "sentence-similarity", "text-to-speech", "automatic-speech-recognition", "text-to-audio"}

    Private Const Expand As String = "&expand[]=gguf&expand[]=downloads&expand[]=pipeline_tag&expand[]=tags&expand[]=likes&expand[]=lastModified"

    ''' <summary>Downloads the current top GGUF chat models from trusted quantizers.</summary>
    Public Async Function FetchLiveAsync(ct As CancellationToken, Optional perAuthor As Integer = 1000) As Task(Of List(Of CatalogEntry))
        ' A few at a time: Hugging Face rate-limits bursts.
        Dim gate As New SemaphoreSlim(3)
        Dim tasks = Sources.Select(Async Function(src)
                                       Await gate.WaitAsync(ct)
                                       Try
                                           Dim all As New List(Of JToken)
                                           Dim url = $"https://huggingface.co/api/models?author={src.Author}&filter=gguf&sort=downloads&limit={Math.Min(1000, perAuthor)}{Expand}"
                                           For page = 1 To src.Pages
                                               Dim res = Await FetchPageAsync(url, ct)
                                               If res.Items Is Nothing Then Exit For
                                               all.AddRange(res.Items.Where(Function(m) src.MinDownloads = 0 OrElse If(m("downloads")?.ToObject(Of Long?)(), 0L) >= src.MinDownloads))
                                               If res.NextUrl Is Nothing Then Exit For
                                               url = res.NextUrl
                                           Next
                                           Return New JArray(all)
                                       Catch ex As OperationCanceledException When ct.IsCancellationRequested
                                           Throw
                                       Catch
                                           Return New JArray()
                                       Finally
                                           gate.Release()
                                       End Try
                                   End Function).ToList()
        Dim arrays = Await Task.WhenAll(tasks)
        Dim list = Dedupe(arrays.SelectMany(Function(a) ParseEntries(a)))
        If list.Count > 50 Then
            File.WriteAllText(CachePath, JsonConvert.SerializeObject(list))
        End If
        Return list
    End Function

    ''' <summary>Free-text search across ALL of Hugging Face (any author).</summary>
    Public Async Function SearchHubAsync(query As String, ct As CancellationToken) As Task(Of List(Of CatalogEntry))
        Dim url = $"https://huggingface.co/api/models?search={Uri.EscapeDataString(query)}&filter=gguf&sort=downloads&limit=100{Expand}"
        Dim arr = Await FetchJsonAsync(url, ct)
        Return ParseEntries(arr).OrderByDescending(Function(e) e.Downloads).ToList()
    End Function

    ''' <summary>One page of the Hugging Face model list (retries when rate-limited) and the next page's address.</summary>
    Private Async Function FetchPageAsync(url As String, ct As CancellationToken) As Task(Of (Items As JArray, NextUrl As String))
        For attempt = 1 To 5
            Using cts = CancellationTokenSource.CreateLinkedTokenSource(ct)
                cts.CancelAfter(TimeSpan.FromSeconds(90))
                Using resp = Await Downloader.Http.GetAsync(url, cts.Token)
                    If CInt(resp.StatusCode) = 429 OrElse CInt(resp.StatusCode) >= 500 Then
                        Await Task.Delay(TimeSpan.FromSeconds(10 * attempt), ct)
                        Continue For
                    End If
                    If Not resp.IsSuccessStatusCode Then Return (Nothing, Nothing)
                    Dim nextUrl As String = Nothing
                    Dim links As IEnumerable(Of String) = Nothing
                    If resp.Headers.TryGetValues("Link", links) Then
                        Dim m = Regex.Match(String.Join(",", links), "<([^>]+)>;\s*rel=.next.")
                        If m.Success Then nextUrl = m.Groups(1).Value
                    End If
                    Return (JArray.Parse(Await resp.Content.ReadAsStringAsync(cts.Token)), nextUrl)
                End Using
            End Using
        Next
        Return (Nothing, Nothing)
    End Function

    Private Async Function FetchJsonAsync(url As String, ct As CancellationToken) As Task(Of JArray)
        Using cts = CancellationTokenSource.CreateLinkedTokenSource(ct)
            cts.CancelAfter(TimeSpan.FromSeconds(60))
            Dim s = Await Downloader.Http.GetStringAsync(url, cts.Token)
            Return JArray.Parse(s)
        End Using
    End Function

    Private Function ParseEntries(arr As JArray) As IEnumerable(Of CatalogEntry)
        Dim result As New List(Of CatalogEntry)
        For Each m In arr
            Dim g = TryCast(m("gguf"), JObject)
            Dim params As Long = If(g IsNot Nothing, CLng(If(g("total")?.ToObject(Of Double?)(), 0)), 0)
            Dim arch = If(g?("architecture")?.ToString(), "").ToLowerInvariant()
            Dim pipe = If(m("pipeline_tag")?.ToString(), "")
            Dim id = m("id")?.ToString()
            If id Is Nothing Then Continue For
            Dim low = id.ToLowerInvariant()
            If params < 100000000L Then Continue For
            If NonChatArch.Contains(arch) OrElse NonChatPipe.Contains(pipe) Then Continue For
            If {"test-", "models-moved", "embed", "reranker", "-mtp-gguf", "-mtp", "tts", "whisper", "imatrix-dataset", "stable-diffusion", "flux", "qwen-image", "sdxl"}.Any(Function(x) low.Contains(x)) Then Continue For

            Dim tags = If(TryCast(m("tags"), JArray)?.Select(Function(t) t.ToString()).ToList(), New List(Of String))
            Dim keep = {"moe", "conversational", "image-text-to-text", "any-to-any", "reasoning", "code", "uncensored", "abliterated"}
            result.Add(New CatalogEntry With {
                .Repo = id,
                .Name = NormalizeName(id),
                .Author = id.Split("/"c)(0),
                .Params = params,
                .Arch = arch,
                .ContextLength = If(g?("context_length")?.ToObject(Of Long?)(), 0L),
                .Downloads = If(m("downloads")?.ToObject(Of Long?)(), 0L),
                .Likes = If(m("likes")?.ToObject(Of Long?)(), 0L),
                .Pipeline = pipe,
                .License = If(tags.FirstOrDefault(Function(t) t.StartsWith("license:"))?.Substring(8), ""),
                .Updated = Left10(m("lastModified")?.ToString()),
                .Tags = tags.Where(Function(t) keep.Contains(t)).ToList()
            })
        Next
        Return result
    End Function

    Private Function Left10(s As String) As String
        If s Is Nothing Then Return ""
        Dim d As DateTime
        If DateTime.TryParse(s, Globalization.CultureInfo.InvariantCulture, Globalization.DateTimeStyles.RoundtripKind, d) Then Return d.ToString("yyyy-MM-dd")
        Return If(s.Length >= 10, s.Substring(0, 10), s)
    End Function

    ''' <summary>
    ''' Splits an Ollama-style name: "Qwen3.8-27B-Uncensored:iq4_xs" → ("Qwen3.8-27B-Uncensored", "IQ4_XS").
    ''' Also accepts "hf.co/bartowski/Some-Model-GGUF:Q4_K_M" and "ollama run …".
    ''' </summary>
    Public Function SplitQuery(text As String) As (Name As String, Quant As String)
        Dim t = If(text, "").Trim()
        t = Regex.Replace(t, "^(ollama\s+(run|pull)\s+)", "", RegexOptions.IgnoreCase)
        t = Regex.Replace(t, "^(https?://)?(hf\.co|huggingface\.co)/", "", RegexOptions.IgnoreCase)
        Dim i = t.LastIndexOf(":"c)
        If i > 0 AndAlso i < t.Length - 1 Then
            Dim q = t.Substring(i + 1).Trim().ToUpperInvariant()
            If Regex.IsMatch(q, "^(UD-)?(I?Q\d(_[A-Z0-9]+)*|BF16|F16|F32|MXFP4(_MOE)?)$") Then Return (t.Substring(0, i).Trim(), q)
        End If
        Return (t, Nothing)
    End Function

    Public Function NormalizeName(repo As String) As String
        Dim parts = repo.Split({"/"c}, 2)
        Dim author = parts(0).ToLowerInvariant()
        Dim n = parts.Last()
        n = Regex.Replace(n, "(-GGUF-IQ-Imatrix|-GGUF|_GGUF)$", "", RegexOptions.IgnoreCase)
        n = Regex.Replace(n, "-i1$", "")
        If author = "richarderkhov" AndAlso n.Contains("_-_") Then
            n = n.Substring(n.IndexOf("_-_") + 3)
        ElseIf (author = "bartowski" OrElse author = "tensorblock") AndAlso n.Contains("_") Then
            n = n.Split({"_"c}, 2)(1)
        ElseIf author = "devquasar" AndAlso n.Contains(".") Then
            n = n.Split({"."c}, 2)(1)
        End If
        Return n
    End Function

    ''' <summary>One entry per model name, preferring unsloth › bartowski › lmstudio › ggml-org.</summary>
    Private Function Dedupe(items As IEnumerable(Of CatalogEntry)) As List(Of CatalogEntry)
        Dim pref = Function(a As String) Array.IndexOf(TrustedAuthors, a.ToLowerInvariant()) + If(Array.IndexOf(TrustedAuthors, a.ToLowerInvariant()) < 0, 99, 0)
        Dim best As New Dictionary(Of String, CatalogEntry)(StringComparer.OrdinalIgnoreCase)
        For Each e In items
            Dim cur As CatalogEntry = Nothing
            If Not best.TryGetValue(e.Name, cur) Then
                best(e.Name) = e
            ElseIf e.Downloads > cur.Downloads * 3 OrElse (pref(e.Author) < pref(cur.Author) AndAlso cur.Downloads < e.Downloads * 3) Then
                e.Downloads = Math.Max(e.Downloads, cur.Downloads)
                best(e.Name) = e
            Else
                cur.Downloads = Math.Max(cur.Downloads, e.Downloads)
            End If
        Next
        Return best.Values.OrderByDescending(Function(e) e.Downloads).ToList()
    End Function

    '────────────────────────────────────────────────────────────
    '  Repo file listing
    '────────────────────────────────────────────────────────────
    Private ReadOnly _filesCache As New Concurrent.ConcurrentDictionary(Of String, RepoFiles)(StringComparer.OrdinalIgnoreCase)

    Public Async Function GetRepoFilesAsync(repo As String, token As String, ct As CancellationToken) As Task(Of RepoFiles)
        Dim cached As RepoFiles = Nothing
        If _filesCache.TryGetValue(repo, cached) Then Return cached

        Dim url = $"https://huggingface.co/api/models/{repo}/tree/main?recursive=true"
        Using req As New HttpRequestMessage(HttpMethod.Get, url)
            If Not String.IsNullOrWhiteSpace(token) Then req.Headers.Authorization = New Headers.AuthenticationHeaderValue("Bearer", token.Trim())
            Using cts = CancellationTokenSource.CreateLinkedTokenSource(ct)
                cts.CancelAfter(TimeSpan.FromSeconds(45))
                Using resp = Await Downloader.Http.SendAsync(req, cts.Token)
                    If resp.StatusCode = Net.HttpStatusCode.NotFound Then Throw New Exception("Repository not found on Hugging Face: " & repo)
                    If CInt(resp.StatusCode) = 401 OrElse CInt(resp.StatusCode) = 403 Then Throw New Exception("This repository is gated/private. Accept its license on huggingface.co and set your token in Settings.")
                    resp.EnsureSuccessStatusCode()
                    Dim arr = JArray.Parse(Await resp.Content.ReadAsStringAsync(cts.Token))
                    Dim result = BuildRepoFiles(arr)
                    _filesCache(repo) = result
                    Return result
                End Using
            End Using
        End Using
    End Function

    Private Function BuildRepoFiles(arr As JArray) As RepoFiles
        Dim r As New RepoFiles()
        Dim groups As New Dictionary(Of String, QuantOption)(StringComparer.OrdinalIgnoreCase)
        Dim mmprojs As New List(Of RepoFile)
        Dim mtps As New List(Of RepoFile)

        For Each f In arr
            If f.Value(Of String)("type") <> "file" Then Continue For
            Dim p = f.Value(Of String)("path")
            If p Is Nothing OrElse Not p.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase) Then Continue For
            Dim fileName = IO.Path.GetFileName(p).ToLowerInvariant()
            Dim rf As New RepoFile With {
                .Path = p,
                .Size = If(f("lfs")?("size")?.ToObject(Of Long?)(), If(f("size")?.ToObject(Of Long?)(), 0L)),
                .Sha256 = f("lfs")?("oid")?.ToString()
            }
            If fileName.Contains("mmproj") Then
                mmprojs.Add(rf)
                Continue For
            End If
            If fileName.StartsWith("mtp-") OrElse p.StartsWith("MTP/", StringComparison.OrdinalIgnoreCase) Then
                If fileName.StartsWith("mtp-") Then mtps.Add(rf)
                Continue For
            End If
            If fileName.Contains("imatrix") Then Continue For

            Dim q = ParseQuant(fileName)
            If q = "" Then q = IO.Path.GetFileNameWithoutExtension(fileName)
            Dim opt As QuantOption = Nothing
            If Not groups.TryGetValue(q, opt) Then
                opt = New QuantOption With {.Label = q}
                groups(q) = opt
            End If
            opt.Files.Add(rf)
        Next

        For Each g In groups.Values
            g.Files = g.Files.OrderBy(Function(x) x.Path, StringComparer.OrdinalIgnoreCase).ToList()
        Next
        r.Quants = groups.Values.OrderBy(Function(q) q.TotalBytes).ToList()
        r.Mmproj = mmprojs.OrderBy(Function(m) ModelLibrary.MmprojRank(m.Path)).FirstOrDefault()
        ' Smallest MTP head (usually Q8_0) is plenty for drafting.
        r.Mtp = mtps.OrderBy(Function(m) m.Size).FirstOrDefault()
        Return r
    End Function
End Module
