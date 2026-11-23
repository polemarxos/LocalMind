Imports System
Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.IO
Imports System.Linq
Imports System.Reflection
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Threading
Imports System.Threading.Tasks
Imports Newtonsoft.Json

'════════════════════════════════════════════════════════════════════
'  Image & video generation with stable-diffusion.cpp (GGUF + INT8 ConvRot)
'════════════════════════════════════════════════════════════════════

Public Class MediaFile
    <JsonProperty("repo")> Public Property Repo As String
    <JsonProperty("path")> Public Property Path As String
    <JsonProperty("size")> Public Property Size As Long
End Class

Public Class MediaPart
    Inherits MediaFile
    <JsonProperty("role")> Public Property Role As String     ' vae | llm | llm_vision | t5xxl | clip_l | audio-vae | embeddings-connectors
    ''' <summary>Only needed for editing (e.g. the vision part of the text encoder); downloaded on first edit.</summary>
    <JsonProperty("edit")> Public Property Edit As Boolean
    ''' <summary>Only needed for pose / layout / outline control (a ControlNet); downloaded on first use.</summary>
    <JsonProperty("control")> Public Property Control As Boolean
End Class

''' <summary>A LoRA file stored in Models\Loras under a fixed name (e.g. a Lightning speed LoRA).</summary>
Public Class MediaLora
    Inherits MediaFile
    <JsonProperty("name")> Public Property Name As String
    <JsonProperty("high")> Public Property High As Boolean     ' applies to the high-noise expert (Wan 2.2 A14B)
    <JsonProperty("weight")> Public Property Weight As Double = 1.0
End Class

''' <summary>Turbo / Lightning mode: a distillation LoRA with its own steps and CFG.</summary>
Public Class MediaSpeed
    <JsonProperty("label")> Public Property Label As String
    <JsonProperty("steps")> Public Property Steps As Integer
    <JsonProperty("highSteps")> Public Property HighSteps As Integer
    <JsonProperty("cfg")> Public Property Cfg As Double
    <JsonProperty("extra")> Public Property Extra As String
    <JsonProperty("loras")> Public Property Loras As New List(Of MediaLora)()
End Class

Public Class MediaVariant
    Inherits MediaFile
    <JsonProperty("label")> Public Property Label As String
    <JsonProperty("format")> Public Property Format As String ' gguf | convrot | checkpoint | safetensors
    <JsonProperty("high")> Public Property High As MediaFile  ' Wan 2.2 A14B high-noise expert
    <JsonProperty("size_total")> Public Property SizeTotal As Long

    <JsonIgnore> Public ReadOnly Property Total As Long
        Get
            Return If(SizeTotal > 0, SizeTotal, Size)
        End Get
    End Property

    ''' <summary>Largest single block loaded on the GPU at once (experts run one after another).</summary>
    <JsonIgnore> Public ReadOnly Property Peak As Long
        Get
            Return Math.Max(Size, If(High?.Size, 0L))
        End Get
    End Property

    Public Overrides Function ToString() As String
        Return $"{Label}  ({Downloader.FormatBytes(Total)})"
    End Function
End Class

Public Class MediaRecipe
    <JsonProperty("id")> Public Property Id As String
    <JsonProperty("name")> Public Property Name As String
    <JsonProperty("kind")> Public Property Kind As String       ' image | video
    <JsonProperty("family")> Public Property Family As String
    <JsonProperty("about")> Public Property About As String
    <JsonProperty("steps")> Public Property Steps As Integer
    <JsonProperty("highSteps")> Public Property HighSteps As Integer
    <JsonProperty("cfg")> Public Property Cfg As Double
    <JsonProperty("flowShift")> Public Property FlowShift As Double
    <JsonProperty("width")> Public Property Width As Integer
    <JsonProperty("height")> Public Property Height As Integer
    <JsonProperty("frames")> Public Property Frames As Integer
    <JsonProperty("negative")> Public Property Negative As String
    <JsonProperty("sampler")> Public Property Sampler As String
    <JsonProperty("input")> Public Property Input As String     ' none | optional | required
    ''' <summary>create, restyle, edit, combine, inpaint, animate, video-edit, upscale</summary>
    <JsonProperty("tasks")> Public Property Tasks As New List(Of String)()
    <JsonProperty("refs")> Public Property Refs As Integer       ' reference pictures for edit / combine
    <JsonProperty("extra")> Public Property Extra As String     ' extra sd-cli arguments
    <JsonProperty("fps")> Public Property Fps As Integer
    <JsonProperty("speed")> Public Property Speed As MediaSpeed
    ''' <summary>No content filter; listed under "🔓 Uncensored" and kept out of the default list.</summary>
    <JsonProperty("uncensored")> Public Property Uncensored As Boolean
    ''' <summary>Hugging Face downloads (popularity, for sorting).</summary>
    <JsonProperty("dl")> Public Property Downloads As Long
    ''' <summary>Where the model comes from (Hugging Face repo).</summary>
    <JsonProperty("repo")> Public Property Repo As String
    ''' <summary>A community model found on Hugging Face (settings copied from its family).</summary>
    <JsonProperty("auto")> Public Property Auto As Boolean
    ''' <summary>Text put in front of every prompt (e.g. Pony's quality tags).</summary>
    <JsonProperty("prefix")> Public Property Prefix As String
    ''' <summary>LoRAs that are part of the model (always downloaded and applied).</summary>
    <JsonProperty("loras")> Public Property Loras As New List(Of MediaLora)()
    <JsonProperty("variants")> Public Property Variants As New List(Of MediaVariant)()
    <JsonProperty("parts")> Public Property Parts As New List(Of MediaPart)()

    <JsonIgnore> Public ReadOnly Property IsVideo As Boolean
        Get
            Return Kind = "video"
        End Get
    End Property

    <JsonIgnore> Public ReadOnly Property IsUpscaler As Boolean
        Get
            Return Kind = "upscale"
        End Get
    End Property

    Public Function Can(task As String) As Boolean
        Return Tasks IsNot Nothing AndAlso Tasks.Contains(task)
    End Function

    Public Overrides Function ToString() As String
        Return Name
    End Function
End Class

Public Module MediaStudio

    Private _recipes As List(Of MediaRecipe)

    Private _catalogJson As String

    Public Function Recipes() As List(Of MediaRecipe)
        If _recipes Is Nothing Then
            If _catalogJson Is Nothing Then
                Using s = Assembly.GetExecutingAssembly().GetManifestResourceStream("media_catalog.json")
                    Using r As New StreamReader(s)
                        _catalogJson = r.ReadToEnd()
                    End Using
                End Using
            End If
            Dim list = JsonConvert.DeserializeObject(Of List(Of MediaRecipe))(_catalogJson)
            list.AddRange(UserRecipes(list))
            _recipes = list
        End If
        Return _recipes
    End Function

    '──────────────── your own checkpoints (e.g. from Civitai) ────────────────
    Public Class UserModel
        Public Property Id As String
        Public Property Name As String
        ''' <summary>Built-in recipe it is based on (supplies VAE, text encoder, settings).</summary>
        Public Property Template As String
        ''' <summary>File name inside Models\Images\civitai.</summary>
        Public Property File As String
        Public Property Size As Long
        ''' <summary>checkpoint (all-in-one, e.g. SD 1.5 / SDXL) or safetensors / gguf (diffusion model only).</summary>
        Public Property Format As String
        Public Property BaseModel As String
        Public Property Steps As Integer
        Public Property Cfg As Double
        Public Property Extra As String
        Public Property Source As String
    End Class

    Private ReadOnly Property UserModelsFile As String
        Get
            Return Path.Combine(AppPaths.Roaming, "user_models.json")
        End Get
    End Property

    Public ReadOnly Property UserModelFolder As String
        Get
            Dim d = Path.Combine(Folder, "civitai")
            Directory.CreateDirectory(d)
            Return d
        End Get
    End Property

    Public Function LoadUserModels() As List(Of UserModel)
        Try
            If File.Exists(UserModelsFile) Then Return If(JsonConvert.DeserializeObject(Of List(Of UserModel))(File.ReadAllText(UserModelsFile)), New List(Of UserModel)())
        Catch
        End Try
        Return New List(Of UserModel)()
    End Function

    Public Sub SaveUserModels(list As List(Of UserModel))
        File.WriteAllText(UserModelsFile, JsonConvert.SerializeObject(list, Formatting.Indented))
        _recipes = Nothing          ' rebuild the model list with the change
    End Sub

    Public Sub AddUserModel(m As UserModel)
        Dim list = LoadUserModels()
        list.RemoveAll(Function(x) x.File.Equals(m.File, StringComparison.OrdinalIgnoreCase))
        If String.IsNullOrEmpty(m.Id) Then m.Id = "user-" & Guid.NewGuid().ToString("N").Substring(0, 8)
        list.Add(m)
        SaveUserModels(list)
    End Sub

    ''' <summary>Each of your checkpoints becomes a model in the studio, built from its family's recipe.</summary>
    Private Function UserRecipes(catalog As List(Of MediaRecipe)) As List(Of MediaRecipe)
        Dim result As New List(Of MediaRecipe)()
        For Each u In LoadUserModels()
            Dim t = catalog.FirstOrDefault(Function(x) x.Id = u.Template)
            If t Is Nothing Then Continue For
            Dim r = JsonConvert.DeserializeObject(Of MediaRecipe)(JsonConvert.SerializeObject(t))
            r.Id = u.Id
            r.Name = "⭐ " & u.Name
            r.About = Localizer.T("Your model") & $" ({u.BaseModel}" & If(String.IsNullOrEmpty(u.Source), "", ", " & u.Source) & "). " &
                      Localizer.T("Uses the VAE and text encoder of") & " " & t.Name & "."
            If u.Steps > 0 Then r.Steps = u.Steps
            If u.Cfg > 0 Then r.Cfg = u.Cfg
            If u.Extra IsNot Nothing Then r.Extra = u.Extra
            r.Variants = New List(Of MediaVariant) From {
                New MediaVariant With {.Label = If(u.Format = "checkpoint", "Checkpoint", "Model"), .Format = u.Format, .Repo = "local", .Path = u.File, .Size = u.Size}}
            ' An all-in-one checkpoint brings its own text encoder; keep only a separate VAE if the template has one.
            If u.Format = "checkpoint" Then r.Parts = r.Parts.Where(Function(p) p.Role = "vae" OrElse p.Control).ToList()
            result.Add(r)
        Next
        Return result
    End Function

    '──────────────── files on disk ────────────────
    Public ReadOnly Property Folder As String
        Get
            Dim d = Path.Combine(AppCore.Library.Folder, "Images")
            Directory.CreateDirectory(d)
            Return d
        End Get
    End Property

    Public ReadOnly Property OutputFolder As String
        Get
            Dim d = If(AppPaths.IsPortable, Path.Combine(AppPaths.AppDir, "Outputs"),
                       Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "LocalMind"))
            Directory.CreateDirectory(d)
            Return d
        End Get
    End Property

    Public Function LocalPath(f As MediaFile) As String
        If f.Repo = "local" Then Return Path.Combine(UserModelFolder, f.Path)
        Dim l = TryCast(f, MediaLora)
        If l IsNot Nothing Then Return Path.Combine(LoraLibrary.Folder, l.Name & Path.GetExtension(l.Path))
        Return Path.Combine(Folder, f.Repo.Replace("/", "__"), IO.Path.GetFileName(f.Path))
    End Function

    ''' <summary>
    ''' A file may already be on disk under an older folder name (Hugging Face renames repos, e.g.
    ''' Wan_2.1_ComfyUI_repackaged → Wan-2.1). Find it by name and size and move it, instead of downloading it again.
    ''' </summary>
    Private _fileIndex As Dictionary(Of String, List(Of String))
    Private _fileIndexTime As DateTime
    Private ReadOnly _fileIndexLock As New Object()

    ''' <summary>Files under a folder by name (rebuilt at most every few seconds).</summary>
    Private Function FilesNamed(root As String, name As String) As List(Of String)
        SyncLock _fileIndexLock
            If _fileIndex Is Nothing OrElse (DateTime.Now - _fileIndexTime).TotalSeconds > 5 Then
                _fileIndex = New Dictionary(Of String, List(Of String))(StringComparer.OrdinalIgnoreCase)
                For Each scanDir In {Folder, LoraLibrary.Folder}.Distinct()
                    If Not Directory.Exists(scanDir) Then Continue For
                    For Each f In Directory.EnumerateFiles(scanDir, "*", SearchOption.AllDirectories)
                        Dim n = Path.GetFileName(f)
                        Dim l As List(Of String) = Nothing
                        If Not _fileIndex.TryGetValue(n, l) Then
                            l = New List(Of String)
                            _fileIndex(n) = l
                        End If
                        l.Add(f)
                    Next
                Next
                _fileIndexTime = DateTime.Now
            End If
            Dim hit As List(Of String) = Nothing
            If Not _fileIndex.TryGetValue(name, hit) Then Return New List(Of String)
            Return hit.Where(Function(x) x.StartsWith(root, StringComparison.OrdinalIgnoreCase)).ToList()
        End SyncLock
    End Function

    Private Sub Relocate(f As MediaFile, target As String)
        Try
            Dim name = Path.GetFileName(target)
            Dim root = If(TypeOf f Is MediaLora, LoraLibrary.Folder, Folder)
            If Not Directory.Exists(root) Then Return
            For Each c In FilesNamed(root, name)
                If Not File.Exists(c) Then Continue For
                If c.Equals(target, StringComparison.OrdinalIgnoreCase) Then Continue For
                If f.Size > 0 AndAlso ConvRotFix.OriginalLength(c) <> f.Size Then Continue For
                Directory.CreateDirectory(Path.GetDirectoryName(target))
                File.Move(c, target)
                If File.Exists(ConvRotFix.MarkerPath(c)) Then File.Move(ConvRotFix.MarkerPath(c), ConvRotFix.MarkerPath(target), True)
                Return
            Next
        Catch
        End Try
    End Sub

    Public Function SpeedReady(r As MediaRecipe) As Boolean
        Return r.Speed IsNot Nothing AndAlso r.Speed.Loras.All(AddressOf IsPresent)
    End Function

    Public Function IsPresent(f As MediaFile) As Boolean
        Dim p = LocalPath(f)
        If Not File.Exists(p) Then Relocate(f, p)
        Return File.Exists(p) AndAlso (f.Size <= 0 OrElse ConvRotFix.OriginalLength(p) = f.Size)
    End Function

    Public Function FilesFor(r As MediaRecipe, v As MediaVariant, Optional forEdit As Boolean = False, Optional withSpeed As Boolean = False) As List(Of MediaFile)
        Dim l As New List(Of MediaFile) From {v}
        If v.High IsNot Nothing Then l.Add(v.High)
        l.AddRange(r.Parts.Where(Function(p) (forEdit OrElse Not p.Edit) AndAlso Not p.Control))
        If r.Loras IsNot Nothing Then l.AddRange(r.Loras)
        If withSpeed AndAlso r.Speed IsNot Nothing Then l.AddRange(r.Speed.Loras)
        Return l
    End Function

    Public Function IsInstalled(r As MediaRecipe, v As MediaVariant) As Boolean
        Return FilesFor(r, v).All(AddressOf IsPresent)
    End Function

    Public Function InstalledVariant(r As MediaRecipe) As MediaVariant
        Return r.Variants.Where(Function(v) IsInstalled(r, v)).OrderByDescending(Function(v) v.Total).FirstOrDefault()
    End Function

    Public Function BytesToDownload(r As MediaRecipe, v As MediaVariant, Optional withSpeed As Boolean = False) As Long
        Return FilesFor(r, v, False, withSpeed).Where(Function(f) Not IsPresent(f)).Sum(Function(f) f.Size)
    End Function

    Private ReadOnly _gates As New System.Collections.Concurrent.ConcurrentDictionary(Of String, SemaphoreSlim)(StringComparer.OrdinalIgnoreCase)

    ''' <summary>One download at a time per file (two models that share a part never write it together).</summary>
    Friend Function FileGate(path As String) As SemaphoreSlim
        Return _gates.GetOrAdd(path, Function(k) New SemaphoreSlim(1, 1))
    End Function

    Public Async Function DownloadAsync(r As MediaRecipe, v As MediaVariant, progress As Action(Of Integer, String), ct As CancellationToken,
                                        Optional forEdit As Boolean = False, Optional withSpeed As Boolean = False) As Task
        Dim todo = FilesFor(r, v, forEdit, withSpeed).Where(Function(f) Not IsPresent(f)).ToList()
        Dim total = Math.Max(1L, todo.Sum(Function(f) f.Size))
        Dim before As Long = 0
        For Each f In todo
            Dim url = $"https://huggingface.co/{f.Repo}/resolve/main/" & String.Join("/", f.Path.Split("/"c).Select(AddressOf Uri.EscapeDataString))
            Dim b = before
            ' Several models can download at the same time; a shared part (VAE, text encoder) is fetched once.
            Dim gate = FileGate(LocalPath(f))
            Await gate.WaitAsync(ct)
            Try
                If Not IsPresent(f) Then
                    Await Downloader.DownloadFileAsync(url, LocalPath(f), AppCore.Settings.HuggingFaceToken,
                        Sub(done, size, speed) progress?.Invoke(CInt((b + done) * 100 \ total),
                            $"{IO.Path.GetFileName(f.Path)}  {Downloader.FormatBytes(b + done)} / {Downloader.FormatBytes(total)}  ·  {Downloader.FormatBytes(CLng(speed))}/s"),
                        ct, f.Size)
                End If
            Finally
                gate.Release()
            End Try
            before += f.Size
        Next
        If v.Format = "convrot" Then
            For Each f In {CType(v, MediaFile), v.High}.Where(Function(x) x IsNot Nothing)
                ConvRotFix.EnsurePatched(LocalPath(f))
            Next
        End If
        progress?.Invoke(100, "Ready")
    End Function

    ''' <summary>Deletes the downloaded variant (shared parts used by another installed model are kept).</summary>
    Public Sub DeleteVariant(r As MediaRecipe, v As MediaVariant)
        If r.Id.StartsWith("user-") Then
            Dim list = LoadUserModels()
            list.RemoveAll(Function(x) x.Id = r.Id)
            Try : File.Delete(LocalPath(v)) : Catch : End Try
            SaveUserModels(list)
            Return
        End If
        Dim keep = Recipes().Where(Function(x) x IsNot r).SelectMany(Function(x) x.Variants.Where(Function(y) IsInstalled(x, y)).SelectMany(Function(y) FilesFor(x, y))).
                   Select(Function(f) LocalPath(f)).ToHashSet(StringComparer.OrdinalIgnoreCase)
        For Each f In FilesFor(r, v, True)
            Dim p = LocalPath(f)
            If Not keep.Contains(p) AndAlso File.Exists(p) Then File.Delete(p)
            If Not keep.Contains(p) AndAlso File.Exists(ConvRotFix.MarkerPath(p)) Then File.Delete(ConvRotFix.MarkerPath(p))
        Next
    End Sub

    '──────────────── fit to this PC ────────────────
    Private Function Overhead(r As MediaRecipe) As Long
        If r.IsUpscaler Then Return 512L * 1024 * 1024
        Return CLng(If(r.IsVideo, 5.0, 2.5) * 1024 * 1024 * 1024)
    End Function

    ''' <summary>
    ''' Rough memory needs: graphics memory for full speed, and system RAM (parts that do not fit on the GPU
    ''' wait in RAM, and every file passes through RAM while it loads).
    ''' </summary>
    Public Function Requirement(r As MediaRecipe, v As MediaVariant, hw As HardwareInfo) As (VramGB As Double, RamGB As Double)
        Dim gb = 1024.0 * 1024 * 1024
        Dim files = FilesFor(r, v)
        Dim all = files.Sum(Function(f) f.Size)
        Dim vram = (v.Peak + Overhead(r)) / gb
        Dim onGpu = hw.VramBytes > 0 AndAlso all <= hw.VramBytes * 0.85
        Dim ram = If(onGpu, files.Max(Function(f) f.Size) / gb + 2, all / gb + 2 - If(hw.VramBytes > 0, Math.Min(all, hw.VramBytes * 0.5) / gb, 0))
        Return (Math.Round(vram, 1), Math.Round(Math.Max(2, ram), 1))
    End Function

    ''' <summary>One line for the UI: "Needs ≈ 9 GB graphics memory + 12 GB RAM · this PC: 8 + 32".</summary>
    Public Function RequirementText(r As MediaRecipe, v As MediaVariant, hw As HardwareInfo) As String
        Dim q = Requirement(r, v, hw)
        Dim fit = MediaStudio.Fit(r, v, hw)
        Dim mark = If(fit = FitLevel.Fits, "✔", If(fit = FitLevel.PartialOffload, "⚠", "✖"))
        Return $"{mark} {Localizer.T("Needs about")} {q.VramGB:0.#} GB {Localizer.T("graphics memory")} + {q.RamGB:0.#} GB RAM  ·  " &
               $"{Localizer.T("this PC")}: {hw.VramGB:0.#} + {hw.RamBytes / (1024.0 * 1024 * 1024):0} GB"
    End Function

    Public Function Fit(r As MediaRecipe, v As MediaVariant, hw As HardwareInfo) As FitLevel
        Dim vram = hw.VramBytes
        Dim ram = hw.RamBytes
        Dim allBytes = FilesFor(r, v).Sum(Function(f) f.Size)
        If allBytes > ram * 0.9 + vram Then Return FitLevel.TooBig
        ' Parts are offloaded to RAM and brought to the GPU one at a time.
        If vram > 0 AndAlso v.Peak + Overhead(r) <= vram * 0.95 Then Return FitLevel.Fits
        Return If(vram > 0 AndAlso v.Peak <= vram * 1.6, FitLevel.PartialOffload, If(vram = 0 AndAlso Not r.IsVideo AndAlso v.Peak < 8L * 1024 * 1024 * 1024, FitLevel.PartialOffload, FitLevel.TooBig))
    End Function

    Private Function ConvRotFast(hw As HardwareInfo) As Boolean
        Return hw.Gpus.Any(Function(g) g.Vendor = GpuVendor.Nvidia AndAlso (g.ComputeCap = 0 OrElse g.ComputeCap >= 7.5))
    End Function

    ''' <summary>Best variant for this PC: INT8 ConvRot on modern NVIDIA when it fits, else the largest GGUF that fits.</summary>
    Public Function PickVariant(r As MediaRecipe, hw As HardwareInfo) As MediaVariant
        Dim fits = r.Variants.Where(Function(v) Fit(r, v, hw) = FitLevel.Fits AndAlso v.Label <> "BF16" AndAlso v.Label <> "F16").ToList()
        If ConvRotFast(hw) Then
            Dim cr = fits.FirstOrDefault(Function(v) v.Format = "convrot")
            If cr IsNot Nothing Then Return cr
        End If
        Dim best = fits.Where(Function(v) v.Format <> "convrot" OrElse ConvRotFast(hw)).OrderByDescending(Function(v) v.Total).FirstOrDefault()
        If best IsNot Nothing Then Return best
        ' Nothing fits fully: smallest variant ≥ Q4 that still runs with offloading, else the smallest.
        Dim runnable = r.Variants.Where(Function(v) Fit(r, v, hw) <> FitLevel.TooBig).OrderBy(Function(v) v.Total).ToList()
        Return If(runnable.FirstOrDefault(Function(v) v.Label.StartsWith("Q4") OrElse v.Label.StartsWith("Q5")),
                  If(runnable.FirstOrDefault(), r.Variants.OrderBy(Function(v) v.Total).First()))
    End Function

    ''' <summary>Default Studio list: recipes whose best variant runs entirely on this GPU.</summary>
    Public Function BestForGpu(hw As HardwareInfo, Optional uncensored As Boolean = False) As List(Of MediaRecipe)
        Return Recipes().Where(Function(r)
                                   ' community models only when popular (or already downloaded)
                                   If r.Auto AndAlso r.Downloads < 2000 AndAlso InstalledVariant(r) Is Nothing Then Return False
                                   If r.Uncensored <> uncensored AndAlso Not r.IsUpscaler AndAlso r.Kind <> "tool" Then Return False
                                   Dim v = PickVariant(r, hw)
                                   Dim f = Fit(r, v, hw)
                                   Return f = FitLevel.Fits OrElse (hw.VramBytes = 0 AndAlso f <> FitLevel.TooBig)
                               End Function).ToList()
    End Function

    '──────────────── engine (stable-diffusion.cpp) ────────────────
    ''' <summary>Updates the image engine if a newer build exists (only when it is installed and idle).</summary>
    Public Async Function UpdateEngineAsync(progress As Action(Of String), ct As CancellationToken) As Task
        Dim pkg = EnginePackage()
        If ExternalTools.FindExe(pkg) Is Nothing OrElse (_proc IsNot Nothing AndAlso Not _proc.HasExited) Then Return
        Await ExternalTools.InstallAsync(pkg, progress, ct, onlyIfNewer:=True)
    End Function

    Private Function EnginePackage() As ExternalTools.ToolPackage
        Dim v = AppCore.Engine.EffectiveVariant
        Dim flavor = If(v.StartsWith("cuda"), "cuda12", If(v = "cpu", "cpu", "vulkan"))
        Dim pats As String() = If(flavor = "cuda12",
            {"^sd-.+-bin-win-cuda12-x64\.zip$", "^cudart-sd-bin-win-cu12-x64\.zip$"},
            If(flavor = "vulkan", {"^sd-.+-bin-win-vulkan-x64\.zip$"}, {"^sd-.+-bin-win-(cpu|avx2)-x64\.zip$"}))
        Return New ExternalTools.ToolPackage With {.Name = "sd", .Flavor = flavor, .Repo = "leejet/stable-diffusion.cpp",
                                                   .ExeName = "sd-cli.exe", .AssetPatterns = pats}
    End Function

    Public Class GenerateRequest
        Public Property Recipe As MediaRecipe
        Public Property [Variant] As MediaVariant
        Public Property Prompt As String
        Public Property Negative As String
        Public Property Width As Integer
        Public Property Height As Integer
        Public Property Steps As Integer
        Public Property Cfg As Double
        Public Property Seed As Long = -1
        Public Property Frames As Integer
        Public Property InputImage As String
        ''' <summary>create, restyle, edit, combine, inpaint, animate, video-edit, upscale (empty = guess from inputs).</summary>
        Public Property Task As String
        Public Property InputImages As New List(Of String)()
        Public Property MaskImage As String
        Public Property InputVideo As String
        Public Property Strength As Double = 0.75
        ''' <summary>Use the model's Turbo / Lightning LoRA (its steps and CFG must already be in Steps / Cfg).</summary>
        Public Property Fast As Boolean
        ''' <summary>How many pictures to make at once (variations with consecutive seeds).</summary>
        Public Property Count As Integer = 1
        ''' <summary>All files made (filled in by GenerateAsync).</summary>
        Public Property Outputs As New List(Of String)()
        ''' <summary>The user's own LoRAs to apply (their trigger words are added to the prompt).</summary>
        Public Property Loras As New List(Of UserLora)()
        ''' <summary>first-last: the picture the video ends on (or picture 2 of InputImages).</summary>
        Public Property EndImage As String
        ''' <summary>talk: the voice recording (WAV) that drives the mouth and face.</summary>
        Public Property AudioFile As String
        ''' <summary>Where to save the result (for steps of a longer job); empty = a new file in Outputs.</summary>
        Public Property OutputPath As String

        ''' <summary>A copy with its own lists (for the shots of a storyboard, the pieces of a talking video…).</summary>
        Public Function Clone() As GenerateRequest
            Dim c = CType(MemberwiseClone(), GenerateRequest)
            c.InputImages = New List(Of String)(InputImages)
            c.Loras = New List(Of UserLora)(Loras)
            c.Outputs = New List(Of String)()
            Return c
        End Function
    End Class

    Private _proc As Process

    Public ReadOnly Property IsBusy As Boolean
        Get
            Dim p = _proc
            Return p IsNot Nothing AndAlso Not p.HasExited
        End Get
    End Property

    ''' <summary>Runs sd-cli and returns the output file (PNG or WebM).</summary>
    Public Async Function GenerateAsync(req As GenerateRequest, progress As Action(Of Integer, String), ct As CancellationToken) As Task(Of String)
        Dim r = req.Recipe
        Dim v = req.Variant
        Dim hw = HardwareInfo.Current()
        If r.Kind = "tool" Then Return Await RunToolAsync(req, progress, ct)
        Dim pkg = EnginePackage()
        If ExternalTools.FindExe(pkg) Is Nothing Then Await ExternalTools.InstallAsync(pkg, Sub(t) progress?.Invoke(0, t), ct)
        Dim exe = ExternalTools.FindExe(pkg)
        If v.Format = "convrot" Then
            Await Task.Run(Sub()
                               ConvRotFix.EnsurePatched(LocalPath(v))
                               If v.High IsNot Nothing Then ConvRotFix.EnsurePatched(LocalPath(v.High))
                           End Sub, ct)
        End If

        ' Inputs and task
        Dim imgs = req.InputImages.Where(Function(f) Not String.IsNullOrEmpty(f) AndAlso File.Exists(f)).ToList()
        If imgs.Count = 0 AndAlso Not String.IsNullOrEmpty(req.InputImage) AndAlso File.Exists(req.InputImage) Then imgs.Add(req.InputImage)
        Dim job = If(String.IsNullOrEmpty(req.Task), GuessTask(r, imgs.Count > 0, Not String.IsNullOrEmpty(req.MaskImage), Not String.IsNullOrEmpty(req.InputVideo)), req.Task)
        ' Guided tasks: a ControlNet where the model has one, otherwise an instruction for an editing model.
        Dim promptPrefix = ""
        Dim controlType As String = Nothing
        Dim controlNet = r.Parts.FirstOrDefault(Function(p) p.Control AndAlso p.Role = "control-net")
        Select Case job
            Case "pose"
                promptPrefix = PosePrompt : job = "combine"
            Case "layout", "outline"
                If controlNet IsNot Nothing Then
                    controlType = If(job = "layout", "depth", "canny") : job = "control"
                Else
                    promptPrefix = If(job = "layout", LayoutPrompt, OutlinePrompt) : job = "edit"
                End If
            Case "restore"
                promptPrefix = RestorePrompt : job = "edit"
        End Select
        Dim usesRefs = (job = "edit" OrElse job = "combine") AndAlso r.Refs > 0
        If job = "control" AndAlso Not IsPresent(controlNet) Then
            progress?.Invoke(0, "Downloading the ControlNet…")
            Await DownloadFilesAsync({CType(controlNet, MediaFile)}.ToList(), progress, ct)
        End If
        If usesRefs AndAlso FilesFor(r, v, True).Any(Function(f) Not IsPresent(f)) Then
            Await DownloadAsync(r, v, progress, ct, forEdit:=True)       ' e.g. the vision part needed for editing
        End If

        Dim isVideoOut = r.IsVideo
        Dim stamp = $"{DateTime.Now:yyyyMMdd-HHmmss}-{r.Id}"
        Dim out = If(String.IsNullOrEmpty(req.OutputPath), Path.Combine(OutputFolder, stamp & If(isVideoOut, ".webm", ".png")), req.OutputPath)
        Dim a As New StringBuilder()
        Dim framesDir As String = Nothing

        If r.IsUpscaler Then
            If imgs.Count = 0 Then Throw New Exception(Localizer.T("Choose a picture to enlarge first."))
            a.Append($"-M upscale --upscale-model {Q(LocalPath(v))} -i {Q(imgs(0))} ")
        Else
            If r.IsVideo Then a.Append("-M vid_gen ")
            If v.Format = "checkpoint" Then
                a.Append($"-m {Q(LocalPath(v))} ")
            Else
                a.Append($"--diffusion-model {Q(LocalPath(v))} ")
                If v.High IsNot Nothing Then a.Append($"--high-noise-diffusion-model {Q(LocalPath(v.High))} ")
            End If
            For Each p In r.Parts.Where(Function(x) (Not x.Edit OrElse usesRefs) AndAlso Not x.Control AndAlso Not x.Role.StartsWith("x-"))
                a.Append($"--{p.Role} {Q(LocalPath(p))} ")
            Next
            ' LoRAs: the user's own (with trigger words) and the Turbo / Lightning one.
            Dim prompt = promptPrefix & req.Prompt
            If Not String.IsNullOrEmpty(r.Prefix) AndAlso Not req.Prompt.StartsWith(r.Prefix.Trim().TrimEnd(","c), StringComparison.OrdinalIgnoreCase) Then prompt = r.Prefix & prompt
            Dim tags As New StringBuilder()
            For Each l In If(r.Loras, New List(Of MediaLora)())
                tags.Append(FormattableString.Invariant($"<lora:{If(l.High, "|high_noise|", "")}{l.Name}:{l.Weight}>"))
            Next
            Dim speedOn = req.Fast AndAlso r.Speed IsNot Nothing
            If speedOn Then
                If Not SpeedReady(r) Then
                    progress?.Invoke(0, "Downloading the speed-up LoRA…")
                    Await DownloadAsync(r, v, progress, ct, withSpeed:=True)
                End If
                For Each l In r.Speed.Loras
                    tags.Append(FormattableString.Invariant($"<lora:{If(l.High, "|high_noise|", "")}{l.Name}:{l.Weight}>"))
                Next
            End If
            For Each u In req.Loras.Where(Function(x) x IsNot Nothing AndAlso File.Exists(x.FullPath))
                If Not String.IsNullOrWhiteSpace(u.Triggers) AndAlso prompt.IndexOf(u.Triggers.Trim(), StringComparison.OrdinalIgnoreCase) < 0 Then
                    prompt = u.Triggers.Trim().TrimEnd(","c) & ", " & prompt
                End If
                tags.Append(FormattableString.Invariant($"<lora:{u.Name}:{u.Weight}>"))
                If v.High IsNot Nothing Then tags.Append(FormattableString.Invariant($"<lora:|high_noise|{u.Name}:{u.Weight}>"))
            Next
            If tags.Length > 0 Then a.Append($"--lora-model-dir {Q(LoraLibrary.Folder)} ")
            a.Append($"-p {Q(prompt & tags.ToString())} ")
            If Not String.IsNullOrWhiteSpace(req.Negative) Then a.Append($"-n {Q(req.Negative)} ")
            a.Append(FormattableString.Invariant($"--cfg-scale {req.Cfg} --steps {req.Steps} --sampling-method {If(r.Sampler, "euler")} "))
            Dim highSteps = If(speedOn AndAlso r.Speed.HighSteps > 0, r.Speed.HighSteps, r.HighSteps)
            If highSteps > 0 Then a.Append($"--high-noise-steps {highSteps} ")
            If r.FlowShift > 0 Then a.Append(FormattableString.Invariant($"--flow-shift {r.FlowShift} "))
            If req.Seed < 0 Then req.Seed = Random.Shared.Next(1, Integer.MaxValue)      ' known seed, so the result can be repeated
            a.Append($"-W {req.Width} -H {req.Height} --seed {req.Seed} ")
            If Not r.IsVideo AndAlso req.Count > 1 Then a.Append($"-b {Math.Min(8, req.Count)} ")
            If r.IsVideo Then
                a.Append($"--video-frames {Math.Max(1, req.Frames)} ")
                If r.Fps > 0 Then a.Append($"--fps {r.Fps} ")
            End If
            Dim extra = If(speedOn AndAlso Not String.IsNullOrWhiteSpace(r.Speed.Extra), r.Speed.Extra, r.Extra)
            If Not String.IsNullOrWhiteSpace(extra) Then a.Append(extra.Trim() & " ")

            Select Case job
                Case "edit", "combine"
                    For Each f In imgs.Take(Math.Max(1, r.Refs))
                        a.Append($"-r {Q(f)} ")
                    Next
                Case "inpaint"
                    If imgs.Count = 0 OrElse String.IsNullOrEmpty(req.MaskImage) Then Throw New Exception(Localizer.T("Paint the area to change first."))
                    a.Append($"-i {Q(imgs(0))} --mask {Q(req.MaskImage)} ")
                    a.Append(FormattableString.Invariant($"--strength {Math.Clamp(req.Strength, 0.05, 1.0)} "))
                Case "control"
                    If imgs.Count = 0 Then Throw New Exception(Localizer.T("Choose the picture to copy from first."))
                    progress?.Invoke(0, If(controlType = "depth", "Reading the layout and depth…", "Finding the outlines…"))
                    Dim map = Path.Combine(Path.GetTempPath(), $"localmind-control-{Guid.NewGuid():N}.png")
                    If controlType = "depth" Then
                        If DepthEstimator Is Nothing Then Throw New Exception("Depth reading is not available here.")
                        Await DepthEstimator(imgs(0), map, progress, ct)
                    Else
                        If EdgeDetector Is Nothing Then Throw New Exception("Outline reading is not available here.")
                        EdgeDetector.Invoke(imgs(0), map)
                    End If
                    a.Append($"--control-net {Q(LocalPath(controlNet))} --control-image {Q(map)} --control-type {controlType} ")
                    a.Append(FormattableString.Invariant($"--control-strength {Math.Clamp(req.Strength, 0.1, 1.5)} "))
                Case "restyle"
                    If imgs.Count > 0 Then a.Append($"-i {Q(imgs(0))} " & FormattableString.Invariant($"--strength {Math.Clamp(req.Strength, 0.05, 1.0)} "))
                Case "animate"
                    If imgs.Count > 0 Then a.Append($"-i {Q(imgs(0))} ")
                Case "reference"
                    If imgs.Count = 0 Then Throw New Exception(Localizer.T("Choose the reference pictures (who and what to show)."))
                    For Each f In imgs.Take(Math.Max(1, r.Refs))
                        a.Append($"-r {Q(f)} ")
                    Next
                Case "first-last"
                    Dim lastPic = If(Not String.IsNullOrEmpty(req.EndImage) AndAlso File.Exists(req.EndImage), req.EndImage, imgs.Skip(1).FirstOrDefault())
                    If imgs.Count = 0 OrElse lastPic Is Nothing Then Throw New Exception(Localizer.T("Choose the first and the last picture."))
                    a.Append($"-i {Q(imgs(0))} --end-img {Q(lastPic)} ")
                Case "talk"
                    If imgs.Count = 0 Then Throw New Exception(Localizer.T("Choose the picture of the person who talks."))
                    If String.IsNullOrEmpty(req.AudioFile) OrElse Not File.Exists(req.AudioFile) Then Throw New Exception(Localizer.T("Choose or make the voice recording first."))
                    a.Append($"-i {Q(imgs(0))} --audio {Q(req.AudioFile)} ")
                Case "video-edit"
                    If String.IsNullOrEmpty(req.InputVideo) OrElse Not File.Exists(req.InputVideo) Then Throw New Exception(Localizer.T("Choose a video to edit first."))
                    progress?.Invoke(0, "Reading the video…")
                    framesDir = Await VideoTools.ExtractFramesAsync(req.InputVideo, If(r.Fps > 0, r.Fps, 16), req.Width, req.Height,
                                                                   Math.Max(1, req.Frames), Sub(t) progress?.Invoke(0, t), ct)
                    a.Append($"--control-video {Q(framesDir)} ")
                    If imgs.Count > 0 Then a.Append($"-i {Q(imgs(0))} ")
                    a.Append(FormattableString.Invariant($"--vace-strength {Math.Clamp(req.Strength, 0.1, 2.0)} "))
            End Select
            ' Memory: keep parts in RAM and move them to the GPU as needed; tile the VAE on smaller GPUs.
            If FilesFor(r, v).Sum(Function(f) f.Size) > hw.VramBytes * 0.85 OrElse r.IsVideo Then a.Append("--offload-to-cpu ")
            If hw.VramGB < 12 Then a.Append("--vae-tiling ")
            If pkg.Flavor = "cuda12" Then a.Append("--diffusion-fa ")
        End If
        a.Append($"-o {Q(out)} -v")

        ' Make room on the GPU: the chat model is unloaded when both would not fit, and reloads on the next message.
        Dim lease As IDisposable = Nothing
        If AppCore.Settings.Backend = BackendKind.BuiltIn Then
            lease = Await AppCore.Engine.ReserveGpuAsync(v.Peak + Overhead(r), Sub(t) progress?.Invoke(0, t), ct)
        End If
        Try
            progress?.Invoke(0, "Loading model…")
            Dim log As New StringBuilder()
            Dim psi As New ProcessStartInfo(exe, a.ToString()) With {
                .UseShellExecute = False, .CreateNoWindow = True, .RedirectStandardOutput = True, .RedirectStandardError = True,
                .WorkingDirectory = Path.GetDirectoryName(exe), .StandardOutputEncoding = Encoding.UTF8, .StandardErrorEncoding = Encoding.UTF8}
            Dim stepRx As New Regex("(\d+)/(\d+)\s*-\s*([\d.]+)\s*(s/it|it/s)")
            Dim hsTotal = If(req.Fast AndAlso r.Speed IsNot Nothing AndAlso r.Speed.HighSteps > 0, r.Speed.HighSteps, r.HighSteps)
            Dim totalSteps = req.Steps + If(hsTotal > 0, hsTotal, 0)
            Dim stage = 0
            Dim lastI = 0
            Dim onLine = Sub(line As String)
                             If line Is Nothing Then Return
                             SyncLock log
                                 log.AppendLine(line)
                             End SyncLock
                             Dim m = stepRx.Match(line)
                             If m.Success Then
                                 Dim i = CInt(m.Groups(1).Value), n = CInt(m.Groups(2).Value)
                                 If i < lastI Then stage += 1   ' second expert (Wan A14B)
                                 lastI = i
                                 Dim doneSteps = If(stage > 0 AndAlso hsTotal > 0, hsTotal + i, i)
                                 Dim pct = CInt(Math.Min(97, doneSteps * 95 / Math.Max(1, If(hsTotal > 0, totalSteps, n))))
                                 progress?.Invoke(pct, $"Step {doneSteps}/{If(hsTotal > 0, totalSteps, n)} · {m.Groups(3).Value} {m.Groups(4).Value}")
                             ElseIf line.Contains("decod", StringComparison.OrdinalIgnoreCase) Then
                                 progress?.Invoke(97, If(r.IsVideo, "Decoding video…", "Decoding image…"))
                             ElseIf line.Contains("loading", StringComparison.OrdinalIgnoreCase) AndAlso lastI = 0 Then
                                 progress?.Invoke(0, "Loading model…")
                             End If
                         End Sub
            _proc = New Process() With {.StartInfo = psi}
            AddHandler _proc.OutputDataReceived, Sub(s, e) onLine(e.Data)
            AddHandler _proc.ErrorDataReceived, Sub(s, e) onLine(e.Data)
            _proc.Start()
            AppCore.Engine.AssignToJob(_proc)   ' sd-cli closes together with LocalMind
            _proc.BeginOutputReadLine()
            _proc.BeginErrorReadLine()
            Try
                Await _proc.WaitForExitAsync(ct)
            Catch ex As OperationCanceledException
                Try : _proc.Kill(True) : Catch : End Try
                Throw
            End Try
            Dim code = _proc.ExitCode
            _proc = Nothing
            ' Several pictures at once are saved as name_0.png, name_1.png…
            Dim stem = Path.Combine(Path.GetDirectoryName(out), Path.GetFileNameWithoutExtension(out))
            If Not File.Exists(out) AndAlso File.Exists(stem & "_0.png") Then
                req.Outputs = Enumerable.Range(0, 8).Select(Function(i) $"{stem}_{i}.png").Where(AddressOf File.Exists).ToList()
                out = req.Outputs(0)
            Else
                req.Outputs = New List(Of String) From {out}
            End If
            If code <> 0 OrElse Not File.Exists(out) Then
                Dim tail = String.Join(Environment.NewLine, log.ToString().Split({vbLf}, StringSplitOptions.RemoveEmptyEntries).Reverse().Take(15).Reverse())
                Dim hint = If(tail.ToLowerInvariant().Contains("out of memory") OrElse tail.Contains("alloc"),
                              "Not enough memory: try a smaller size, fewer frames, or a smaller quality level." & Environment.NewLine & Environment.NewLine, "")
                Throw New Exception(hint & tail)
            End If
        Finally
            lease?.Dispose()
            If framesDir IsNot Nothing Then Try : Directory.Delete(framesDir, True) : Catch : End Try
        End Try
        If job = "inpaint" AndAlso InpaintComposer IsNot Nothing Then
            For Each o In req.Outputs
                Try
                    InpaintComposer.Invoke(imgs(0), o, req.MaskImage)    ' smooth edges, untouched pixels stay exact
                Catch
                End Try
            Next
        End If
        ' Remember how each result was made, so it can be made again or tweaked ("Reuse settings").
        If String.IsNullOrEmpty(req.OutputPath) Then
            For i = 0 To req.Outputs.Count - 1
                SaveSettingsFor(req.Outputs(i), req, job, req.Seed + i)
            Next
        End If
        progress?.Invoke(100, "Done")
        Return out
    End Function

    '──────────────── settings of each result ────────────────
    Public Class SavedSettings
        Public Property Recipe As String
        Public Property [Variant] As String
        Public Property Task As String
        Public Property Prompt As String
        Public Property Negative As String
        Public Property Width As Integer
        Public Property Height As Integer
        Public Property Steps As Integer
        Public Property Cfg As Double
        Public Property Seed As Long
        Public Property Frames As Integer
        Public Property Strength As Double
        Public Property Fast As Boolean
        Public Property Loras As New List(Of String)()
        Public Property Inputs As New List(Of String)()
        Public Property Created As DateTime
    End Class

    Public Function SettingsFile(output As String) As String
        Return output & ".json"
    End Function

    Private Sub SaveSettingsFor(output As String, req As GenerateRequest, job As String, seed As Long)
        Try
            Dim s As New SavedSettings With {
                .Recipe = req.Recipe.Id, .Variant = req.Variant.Label, .Task = job, .Prompt = req.Prompt, .Negative = req.Negative,
                .Width = req.Width, .Height = req.Height, .Steps = req.Steps, .Cfg = req.Cfg, .Seed = seed, .Frames = req.Frames,
                .Strength = req.Strength, .Fast = req.Fast, .Loras = req.Loras.Select(Function(l) $"{l.Name}:{l.Weight}").ToList(),
                .Inputs = req.InputImages.ToList(), .Created = DateTime.Now}
            File.WriteAllText(SettingsFile(output), JsonConvert.SerializeObject(s, Formatting.Indented))
        Catch
        End Try
    End Sub

    ''' <summary>Remembers how a result of a longer job was made (storyboards, talking videos…): for search and reuse.</summary>
    Public Sub SaveNote(output As String, req As GenerateRequest, job As String)
        If output Is Nothing OrElse req Is Nothing OrElse req.Recipe Is Nothing OrElse req.Variant Is Nothing Then Return
        SaveSettingsFor(output, req, job, req.Seed)
    End Sub

    Public Function LoadSettingsFor(output As String) As SavedSettings
        Try
            Dim f = SettingsFile(output)
            If File.Exists(f) Then Return JsonConvert.DeserializeObject(Of SavedSettings)(File.ReadAllText(f))
        Catch
        End Try
        Return Nothing
    End Function

    Private Const PosePrompt As String = "Show the subject of picture 1 in exactly the same pose, body position and camera angle as in picture 2. " &
        "Keep the identity, face, clothes and style of picture 1; use picture 2 only for the pose. "
    Private Const LayoutPrompt As String = "Keep the exact layout, composition, perspective and depth of this picture, but change its content and style to: "
    Private Const OutlinePrompt As String = "Turn this sketch or line drawing into a finished picture that follows its outlines exactly: "
    Private Const RestorePrompt As String = "Restore this photo: remove noise, scratches and blur, sharpen and fix the faces naturally while keeping " &
        "each person's identity and expression, and improve the colors. "

    ''' <summary>Set by the app (Windows): (model file, picture, output) → transparent PNG.</summary>
    Public Property BackgroundRemover As Func(Of String, String, String, Task)
    ''' <summary>Set by the app (Windows): (picture, output depth map, progress, ct).</summary>
    Public Property DepthEstimator As Func(Of String, String, Action(Of Integer, String), CancellationToken, Task)
    ''' <summary>Set by the app (Windows): (picture, output edge map).</summary>
    Public Property EdgeDetector As Action(Of String, String)

    ''' <summary>Non-diffusion tools (background removal).</summary>
    Private Async Function RunToolAsync(req As GenerateRequest, progress As Action(Of Integer, String), ct As CancellationToken) As Task(Of String)
        Dim r = req.Recipe, v = req.Variant
        Dim pic = req.InputImages.FirstOrDefault(Function(f) Not String.IsNullOrEmpty(f) AndAlso File.Exists(f))
        If pic Is Nothing Then Throw New Exception(Localizer.T("Choose the picture first (click the picture box)."))
        If Not IsInstalled(r, v) Then Await DownloadAsync(r, v, progress, ct)
        If BackgroundRemover Is Nothing Then Throw New Exception("Background removal is not available here.")
        progress?.Invoke(30, "Removing the background…")
        Dim out = Path.Combine(OutputFolder, $"{DateTime.Now:yyyyMMdd-HHmmss}-cutout.png")
        Await BackgroundRemover(LocalPath(v), pic, out)
        req.Outputs = New List(Of String) From {out}
        SaveSettingsFor(out, req, "remove-bg", 0)
        progress?.Invoke(100, "Done")
        Return out
    End Function

    Private Async Function DownloadFilesAsync(files As List(Of MediaFile), progress As Action(Of Integer, String), ct As CancellationToken) As Task
        Dim total = Math.Max(1L, files.Sum(Function(f) f.Size))
        Dim before As Long = 0
        For Each f In files.Where(Function(x) Not IsPresent(x))
            Dim url = $"https://huggingface.co/{f.Repo}/resolve/main/" & String.Join("/", f.Path.Split("/"c).Select(AddressOf Uri.EscapeDataString))
            Dim b = before
            Await Downloader.DownloadFileAsync(url, LocalPath(f), AppCore.Settings.HuggingFaceToken,
                Sub(done, size, speed) progress?.Invoke(CInt((b + done) * 100 \ total),
                    $"{IO.Path.GetFileName(f.Path)}  {Downloader.FormatBytes(b + done)} / {Downloader.FormatBytes(total)}  ·  {Downloader.FormatBytes(CLng(speed))}/s"),
                ct, f.Size)
            before += f.Size
        Next
    End Function

    ''' <summary>Set by the app (Windows): blends an inpainted result back into the original through a softened mask.</summary>
    Public Property InpaintComposer As Action(Of String, String, String)

    ''' <summary>What to do when the caller did not say: decided by the inputs and what the model can do.</summary>
    Public Function GuessTask(r As MediaRecipe, hasImage As Boolean, hasMask As Boolean, hasVideo As Boolean) As String
        If r.IsUpscaler Then Return "upscale"
        If hasVideo AndAlso r.Can("video-edit") Then Return "video-edit"
        If hasImage AndAlso hasMask AndAlso r.Can("inpaint") Then Return "inpaint"
        If hasImage Then
            If r.IsVideo Then Return If(Not r.Can("animate") AndAlso r.Can("reference"), "reference", "animate")
            If r.Can("edit") Then Return "edit"
            If r.Can("restyle") Then Return "restyle"
        End If
        Return "create"
    End Function

    Private Function Q(s As String) As String
        Return """" & If(s, "").Replace("""", "\""") & """"
    End Function

    Public Sub Cancel()
        Try
            If _proc IsNot Nothing AndAlso Not _proc.HasExited Then _proc.Kill(True)
        Catch
        End Try
    End Sub
End Module
