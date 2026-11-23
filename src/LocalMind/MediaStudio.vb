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
    <JsonProperty("role")> Public Property Role As String     ' vae | llm | t5xxl | clip_l
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
    <JsonProperty("variants")> Public Property Variants As New List(Of MediaVariant)()
    <JsonProperty("parts")> Public Property Parts As New List(Of MediaPart)()

    <JsonIgnore> Public ReadOnly Property IsVideo As Boolean
        Get
            Return Kind = "video"
        End Get
    End Property

    ''' <summary>FLUX.2 / Qwen-Image edit with a reference image (-r); others use img2img / first frame (-i).</summary>
    <JsonIgnore> Public ReadOnly Property InputIsReference As Boolean
        Get
            Return Family = "FLUX.2" OrElse Family = "Qwen-Image"
        End Get
    End Property

    Public Overrides Function ToString() As String
        Return Name
    End Function
End Class

Public Module MediaStudio

    Private _recipes As List(Of MediaRecipe)

    Public Function Recipes() As List(Of MediaRecipe)
        If _recipes Is Nothing Then
            Using s = Assembly.GetExecutingAssembly().GetManifestResourceStream("media_catalog.json")
                Using r As New StreamReader(s)
                    _recipes = JsonConvert.DeserializeObject(Of List(Of MediaRecipe))(r.ReadToEnd())
                End Using
            End Using
        End If
        Return _recipes
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
        Return Path.Combine(Folder, f.Repo.Replace("/", "__"), IO.Path.GetFileName(f.Path))
    End Function

    Private Function IsPresent(f As MediaFile) As Boolean
        Dim p = LocalPath(f)
        Return File.Exists(p) AndAlso (f.Size <= 0 OrElse ConvRotFix.OriginalLength(p) = f.Size)
    End Function

    Public Function FilesFor(r As MediaRecipe, v As MediaVariant) As List(Of MediaFile)
        Dim l As New List(Of MediaFile) From {v}
        If v.High IsNot Nothing Then l.Add(v.High)
        l.AddRange(r.Parts)
        Return l
    End Function

    Public Function IsInstalled(r As MediaRecipe, v As MediaVariant) As Boolean
        Return FilesFor(r, v).All(AddressOf IsPresent)
    End Function

    Public Function InstalledVariant(r As MediaRecipe) As MediaVariant
        Return r.Variants.Where(Function(v) IsInstalled(r, v)).OrderByDescending(Function(v) v.Total).FirstOrDefault()
    End Function

    Public Function BytesToDownload(r As MediaRecipe, v As MediaVariant) As Long
        Return FilesFor(r, v).Where(Function(f) Not IsPresent(f)).Sum(Function(f) f.Size)
    End Function

    Public Async Function DownloadAsync(r As MediaRecipe, v As MediaVariant, progress As Action(Of Integer, String), ct As CancellationToken) As Task
        Dim todo = FilesFor(r, v).Where(Function(f) Not IsPresent(f)).ToList()
        Dim total = Math.Max(1L, todo.Sum(Function(f) f.Size))
        Dim before As Long = 0
        For Each f In todo
            Dim url = $"https://huggingface.co/{f.Repo}/resolve/main/" & String.Join("/", f.Path.Split("/"c).Select(AddressOf Uri.EscapeDataString))
            Dim b = before
            Await Downloader.DownloadFileAsync(url, LocalPath(f), AppCore.Settings.HuggingFaceToken,
                Sub(done, size, speed) progress?.Invoke(CInt((b + done) * 100 \ total),
                    $"{IO.Path.GetFileName(f.Path)}  {Downloader.FormatBytes(b + done)} / {Downloader.FormatBytes(total)}  ·  {Downloader.FormatBytes(CLng(speed))}/s"),
                ct, f.Size)
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
        Dim keep = Recipes().Where(Function(x) x IsNot r).SelectMany(Function(x) x.Variants.Where(Function(y) IsInstalled(x, y)).SelectMany(Function(y) FilesFor(x, y))).
                   Select(Function(f) LocalPath(f)).ToHashSet(StringComparer.OrdinalIgnoreCase)
        For Each f In FilesFor(r, v)
            Dim p = LocalPath(f)
            If Not keep.Contains(p) AndAlso File.Exists(p) Then File.Delete(p)
            If Not keep.Contains(p) AndAlso File.Exists(ConvRotFix.MarkerPath(p)) Then File.Delete(ConvRotFix.MarkerPath(p))
        Next
    End Sub

    '──────────────── fit to this PC ────────────────
    Private Function Overhead(r As MediaRecipe) As Long
        Return CLng(If(r.IsVideo, 5.0, 2.5) * 1024 * 1024 * 1024)
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
    Public Function BestForGpu(hw As HardwareInfo) As List(Of MediaRecipe)
        Return Recipes().Where(Function(r)
                                   Dim v = PickVariant(r, hw)
                                   Dim f = Fit(r, v, hw)
                                   Return f = FitLevel.Fits OrElse (hw.VramBytes = 0 AndAlso f <> FitLevel.TooBig)
                               End Function).ToList()
    End Function

    '──────────────── engine (stable-diffusion.cpp) ────────────────
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
    End Class

    Private _proc As Process

    ''' <summary>Runs sd-cli and returns the output file (PNG or WebM).</summary>
    Public Async Function GenerateAsync(req As GenerateRequest, progress As Action(Of Integer, String), ct As CancellationToken) As Task(Of String)
        Dim pkg = EnginePackage()
        If ExternalTools.FindExe(pkg) Is Nothing Then Await ExternalTools.InstallAsync(pkg, Sub(t) progress?.Invoke(0, t), ct)
        Dim exe = ExternalTools.FindExe(pkg)
        Dim r = req.Recipe
        Dim v = req.Variant
        Dim hw = HardwareInfo.Current()
        If v.Format = "convrot" Then
            Await Task.Run(Sub()
                               ConvRotFix.EnsurePatched(LocalPath(v))
                               If v.High IsNot Nothing Then ConvRotFix.EnsurePatched(LocalPath(v.High))
                           End Sub, ct)
        End If

        Dim out = Path.Combine(OutputFolder, $"{DateTime.Now:yyyyMMdd-HHmmss}-{r.Id}" & If(r.IsVideo, ".webm", ".png"))
        Dim a As New StringBuilder()
        If r.IsVideo Then a.Append("-M vid_gen ")
        If v.Format = "checkpoint" Then
            a.Append($"-m {Q(LocalPath(v))} ")
        Else
            a.Append($"--diffusion-model {Q(LocalPath(v))} ")
            If v.High IsNot Nothing Then a.Append($"--high-noise-diffusion-model {Q(LocalPath(v.High))} ")
        End If
        For Each p In r.Parts
            a.Append($"--{p.Role} {Q(LocalPath(p))} ")
        Next
        a.Append($"-p {Q(req.Prompt)} ")
        If Not String.IsNullOrWhiteSpace(req.Negative) Then a.Append($"-n {Q(req.Negative)} ")
        a.Append(FormattableString.Invariant($"--cfg-scale {req.Cfg} --steps {req.Steps} --sampling-method {If(r.Sampler, "euler")} "))
        If r.HighSteps > 0 Then a.Append($"--high-noise-steps {r.HighSteps} ")
        If r.FlowShift > 0 Then a.Append(FormattableString.Invariant($"--flow-shift {r.FlowShift} "))
        a.Append($"-W {req.Width} -H {req.Height} --seed {req.Seed} ")
        If r.IsVideo Then a.Append($"--video-frames {Math.Max(1, req.Frames)} ")
        If Not String.IsNullOrEmpty(req.InputImage) AndAlso File.Exists(req.InputImage) Then
            If r.InputIsReference AndAlso Not r.IsVideo Then
                a.Append($"-r {Q(req.InputImage)} ")
            Else
                a.Append($"-i {Q(req.InputImage)} ")
                If Not r.IsVideo Then a.Append("--strength 0.75 ")
            End If
        End If
        ' Memory: keep parts in RAM and move them to the GPU as needed; tile the VAE on smaller GPUs.
        If FilesFor(r, v).Sum(Function(f) f.Size) > hw.VramBytes * 0.85 OrElse r.IsVideo Then a.Append("--offload-to-cpu ")
        If hw.VramGB < 12 Then a.Append("--vae-tiling ")
        If pkg.Flavor = "cuda12" Then a.Append("--diffusion-fa ")
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
            Dim totalSteps = req.Steps + If(r.HighSteps > 0, r.HighSteps, 0)
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
                                 Dim doneSteps = If(stage > 0 AndAlso r.HighSteps > 0, r.HighSteps + i, i)
                                 Dim pct = CInt(Math.Min(97, doneSteps * 95 / Math.Max(1, If(r.HighSteps > 0, totalSteps, n))))
                                 progress?.Invoke(pct, $"Step {doneSteps}/{If(r.HighSteps > 0, totalSteps, n)} · {m.Groups(3).Value} {m.Groups(4).Value}")
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
            If code <> 0 OrElse Not File.Exists(out) Then
                Dim tail = String.Join(Environment.NewLine, log.ToString().Split({vbLf}, StringSplitOptions.RemoveEmptyEntries).Reverse().Take(15).Reverse())
                Dim hint = If(tail.ToLowerInvariant().Contains("out of memory") OrElse tail.Contains("alloc"),
                              "Not enough memory: try a smaller size, fewer frames, or a smaller quality level." & Environment.NewLine & Environment.NewLine, "")
                Throw New Exception(hint & tail)
            End If
        Finally
            lease?.Dispose()
        End Try
        progress?.Invoke(100, "Done")
        Return out
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
