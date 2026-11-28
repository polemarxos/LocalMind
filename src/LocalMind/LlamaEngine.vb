Imports System
Imports System.Collections.Concurrent
Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.IO
Imports System.IO.Compression
Imports System.Linq
Imports System.Net
Imports System.Net.Http
Imports System.Net.Sockets
Imports System.Runtime.InteropServices
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Threading
Imports System.Threading.Tasks
Imports Newtonsoft.Json.Linq

Public Enum EngineState
    NotInstalled
    Stopped
    Installing
    Loading
    Ready
    [Error]
End Enum

''' <summary>
''' Owns the bundled llama.cpp server (llama-server.exe): installs/updates it from the
''' official GitHub releases, starts it with the selected GGUF model, and exposes it on
''' 127.0.0.1 only. The app's own LAN server is what other devices talk to.
''' </summary>
Public Class LlamaEngine
    Implements IDisposable

    Public Const ReleasesApi As String = "https://api.github.com/repos/ggml-org/llama.cpp/releases?per_page=20"

    Private ReadOnly _settings As Func(Of AppSettings)
    Private _proc As Process
    Private _port As Integer
    Private ReadOnly _switchLock As New SemaphoreSlim(1, 1)
    Private ReadOnly _log As New ConcurrentQueue(Of String)()
    Private _activeRequests As Integer
    Private _lastActivity As DateTime = DateTime.Now
    Private ReadOnly _idleTimer As Timer
    Private _job As IntPtr = IntPtr.Zero

    Public Event StateChanged(state As EngineState, message As String)
    Public Event LogLine(line As String)

    Public ReadOnly Property State As EngineState = EngineState.Stopped
    Public ReadOnly Property StateMessage As String = ""
    Public ReadOnly Property CurrentModel As LocalModel
    Public ReadOnly Property Port As Integer
        Get
            Return _port
        End Get
    End Property

    Public ReadOnly Property BaseUrl As String
        Get
            Return $"http://127.0.0.1:{_port}"
        End Get
    End Property

    Public Sub New(settings As Func(Of AppSettings))
        _settings = settings
        _idleTimer = New Timer(AddressOf IdleCheck, Nothing, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30))
        CreateKillOnCloseJob()
        KillOrphans()
        If Not IsInstalled() Then SetState(EngineState.NotInstalled, "Engine not installed")
    End Sub

    '════════════════════════════════════════════════════════════
    '  Variant / install locations
    '════════════════════════════════════════════════════════════
    Public Shared ReadOnly Property EngineRoot As String
        Get
            Return AppPaths.EngineFolder
        End Get
    End Property

    ''' <summary>Picks the best llama.cpp build for this PC.</summary>
    Public Shared Function AutoVariant(hw As HardwareInfo) As String
        Select Case hw.PrimaryVendor
            Case GpuVendor.Nvidia
                Dim nv = hw.Gpus.Where(Function(g) g.Vendor = GpuVendor.Nvidia).ToList()
                Dim allTuring = nv.Count > 0 AndAlso nv.All(Function(g) g.ComputeCap = 0 OrElse g.ComputeCap >= 7.5)
                If hw.CudaDriverVersion >= 13 AndAlso allTuring Then Return "cuda13"
                If hw.CudaDriverVersion = 0 OrElse hw.CudaDriverVersion >= 12.4 Then Return "cuda12"
                Return "vulkan"   ' driver too old for the CUDA builds
            Case GpuVendor.Amd, GpuVendor.Intel, GpuVendor.Other
                Return "vulkan"
            Case Else
                Return "cpu"
        End Select
    End Function

    Public ReadOnly Property EffectiveVariant As String
        Get
            Dim v = If(_settings().EngineVariant, "auto").ToLowerInvariant()
            If v = "auto" OrElse v = "" Then v = AutoVariant(HardwareInfo.Current())
            Return v
        End Get
    End Property

    Public Shared Function VariantLabel(v As String) As String
        Select Case v
            Case "cuda12" : Return "NVIDIA CUDA 12"
            Case "cuda13" : Return "NVIDIA CUDA 13"
            Case "vulkan" : Return "Vulkan (AMD / Intel / NVIDIA)"
            Case "rocm" : Return "AMD ROCm"
            Case "cpu" : Return "CPU only"
            Case Else : Return v
        End Select
    End Function

    Public Function VariantDir(Optional v As String = Nothing) As String
        Return Path.Combine(EngineRoot, If(v, EffectiveVariant))
    End Function

    Public Function ServerExe(Optional v As String = Nothing) As String
        Dim d = VariantDir(v)
        If Not Directory.Exists(d) Then Return Nothing
        Return Directory.EnumerateFiles(d, ServerFileName, SearchOption.AllDirectories).FirstOrDefault()
    End Function

    Private Shared ReadOnly Property ServerFileName As String
        Get
            Return If(OperatingSystem.IsWindows(), "llama-server.exe", "llama-server")
        End Get
    End Property

    Public Function IsInstalled(Optional v As String = Nothing) As Boolean
        Return ServerExe(v) IsNot Nothing
    End Function

    Private Shared ReadOnly AllVariants As String() = {"cuda12", "cuda13", "vulkan", "rocm", "cpu"}

    ''' <summary>Deletes the engine builds of the other GPU backends (and unfinished downloads of them).</summary>
    Public Sub RemoveOtherVariants(keep As String)
        For Each o In AllVariants.Where(Function(x) Not String.Equals(x, keep, StringComparison.OrdinalIgnoreCase))
            For Each d In {VariantDir(o), VariantDir(o) & ".new"}
                Try
                    If Directory.Exists(d) Then Directory.Delete(d, True)
                Catch
                    ' (still in use: removed next time)
                End Try
            Next
        Next
    End Sub

    Public Function InstalledVersion(Optional v As String = Nothing) As String
        Dim f = Path.Combine(VariantDir(v), "version.txt")
        Return If(File.Exists(f), File.ReadAllText(f).Trim(), Nothing)
    End Function

    '════════════════════════════════════════════════════════════
    '  Install / update from GitHub releases
    '════════════════════════════════════════════════════════════
    Private Shared Function AssetPatterns(v As String) As (Main As Regex, Runtime As Regex)
        Select Case v
            Case "cuda12"
                Return (New Regex("^llama-.+-bin-win-cuda-12[\d.]*-x64\.zip$", RegexOptions.IgnoreCase),
                        New Regex("^cudart-llama-bin-win-cuda-12[\d.]*-x64\.zip$", RegexOptions.IgnoreCase))
            Case "cuda13"
                Return (New Regex("^llama-.+-bin-win-cuda-13[\d.]*-x64\.zip$", RegexOptions.IgnoreCase),
                        New Regex("^cudart-llama-bin-win-cuda-13[\d.]*-x64\.zip$", RegexOptions.IgnoreCase))
            Case "vulkan"
                Return (New Regex("^llama-.+-bin-win-vulkan-x64\.zip$", RegexOptions.IgnoreCase), Nothing)
            Case "rocm"
                Return (New Regex("^llama-.+-bin-win-(rocm|hip)[\w.\-]*-x64\.zip$", RegexOptions.IgnoreCase), Nothing)
            Case Else
                Return (New Regex("^llama-.+-bin-win-(cpu|avx2)-x64\.zip$", RegexOptions.IgnoreCase), Nothing)
        End Select
    End Function

    Public Class ReleaseInfo
        Public Tag As String
        Public MainUrl As String
        Public MainName As String
        Public MainSize As Long
        Public RuntimeUrl As String
        Public RuntimeSize As Long
    End Class

    ''' <summary>Finds the newest release that contains a build for the variant.</summary>
    Public Shared Async Function FindLatestReleaseAsync(v As String, ct As CancellationToken) As Task(Of ReleaseInfo)
        Dim pats = AssetPatterns(v)
        Using req As New HttpRequestMessage(HttpMethod.Get, ReleasesApi)
            req.Headers.UserAgent.ParseAdd("LocalMind/2.0")
            req.Headers.Accept.ParseAdd("application/vnd.github+json")
            Using resp = Await Downloader.Http.SendAsync(req, ct)
                If resp.StatusCode = HttpStatusCode.Forbidden Then
                    Throw New Exception("GitHub API rate limit reached — try again in an hour.")
                End If
                resp.EnsureSuccessStatusCode()
                Dim arr = JArray.Parse(Await resp.Content.ReadAsStringAsync(ct))
                For Each rel In arr
                    If rel.Value(Of Boolean)("draft") Then Continue For
                    Dim assets = TryCast(rel("assets"), JArray)
                    If assets Is Nothing Then Continue For
                    Dim main = assets.FirstOrDefault(Function(a) pats.Main.IsMatch(a.Value(Of String)("name")))
                    If main Is Nothing Then Continue For
                    Dim info As New ReleaseInfo With {
                        .Tag = rel.Value(Of String)("tag_name"),
                        .MainUrl = main.Value(Of String)("browser_download_url"),
                        .MainName = main.Value(Of String)("name"),
                        .MainSize = main.Value(Of Long)("size")
                    }
                    If pats.Runtime IsNot Nothing Then
                        ' Match the runtime to the exact CUDA version of the main build.
                        Dim cv = Regex.Match(info.MainName, "cuda-([\d.]+)-x64").Groups(1).Value
                        Dim rt = assets.FirstOrDefault(Function(a) a.Value(Of String)("name").Equals($"cudart-llama-bin-win-cuda-{cv}-x64.zip", StringComparison.OrdinalIgnoreCase))
                        If rt Is Nothing Then rt = assets.FirstOrDefault(Function(a) pats.Runtime.IsMatch(a.Value(Of String)("name")))
                        If rt Is Nothing Then Continue For
                        info.RuntimeUrl = rt.Value(Of String)("browser_download_url")
                        info.RuntimeSize = rt.Value(Of Long)("size")
                    End If
                    Return info
                Next
            End Using
        End Using
        Throw New Exception("No Windows build for '" & VariantLabel(v) & "' was found in the latest llama.cpp releases.")
    End Function

    ''' <summary>Downloads and installs (or updates) the engine. Safe to call while a model is loaded.</summary>
    Public Async Function InstallAsync(progress As IProgress(Of (Text As String, Percent As Integer)),
                                       ct As CancellationToken,
                                       Optional v As String = Nothing,
                                       Optional reloadModel As Boolean = True) As Task(Of String)
        v = If(v, EffectiveVariant)
        SetState(EngineState.Installing, "Checking llama.cpp releases…")
        Try
            Dim rel = Await FindLatestReleaseAsync(v, ct)
            If InstalledVersion(v) = rel.Tag AndAlso IsInstalled(v) Then
                SetState(If(_proc IsNot Nothing, EngineState.Ready, EngineState.Stopped), "Engine up to date (" & rel.Tag & ")")
                Return rel.Tag
            End If

            Dim staging = VariantDir(v) & ".new"
            If Directory.Exists(staging) Then Directory.Delete(staging, True)
            Directory.CreateDirectory(staging)

            Dim total = rel.MainSize + rel.RuntimeSize
            Dim doneBefore As Long = 0
            For Each item In {(rel.MainUrl, rel.MainSize, "llama.cpp " & rel.Tag), (rel.RuntimeUrl, rel.RuntimeSize, "CUDA runtime")}
                If item.Item1 Is Nothing Then Continue For
                Dim zip = Path.Combine(staging, Path.GetFileName(New Uri(item.Item1).LocalPath))
                Dim before = doneBefore
                Await Downloader.DownloadFileAsync(item.Item1, zip, Nothing,
                    Sub(done As Long, size As Long, speed As Double)
                        Dim pct = If(total > 0, CInt((before + done) * 100 \ total), 0)
                        progress?.Report(($"Downloading {item.Item3}… {Downloader.FormatBytes(before + done)} / {Downloader.FormatBytes(total)}", Math.Min(99, pct)))
                    End Sub, ct)
                progress?.Report(("Extracting " & item.Item3 & "…", CInt((doneBefore + item.Item2) * 100 \ Math.Max(1, total))))
                ZipFile.ExtractToDirectory(zip, staging, True)
                File.Delete(zip)
                doneBefore += item.Item2
            Next

            If Not Directory.EnumerateFiles(staging, ServerFileName, SearchOption.AllDirectories).Any() Then
                Throw New Exception("The downloaded package does not contain llama-server.exe.")
            End If
            File.WriteAllText(Path.Combine(staging, "version.txt"), rel.Tag)

            ' Swap in the new build (the running server must release its DLLs first).
            If v = EffectiveVariant AndAlso Volatile.Read(_activeRequests) > 0 Then
                Throw New InvalidOperationException("A chat is still running — try the update again when it has finished.")
            End If
            Dim wasModel = _CurrentModel
            If v = EffectiveVariant Then Await StopAsync()
            Dim target = VariantDir(v)
            If Directory.Exists(target) Then Directory.Delete(target, True)
            Directory.Move(staging, target)
            ' Switched backend (e.g. CUDA 12 → CUDA 13): the other builds are removed, they are hundreds of MB each.
            If v = EffectiveVariant Then RemoveOtherVariants(v)

            progress?.Report(("Installed llama.cpp " & rel.Tag, 100))
            SetState(EngineState.Stopped, "Engine installed (" & rel.Tag & ")")
            If reloadModel AndAlso wasModel IsNot Nothing AndAlso v = EffectiveVariant Then
                Await EnsureModelAsync(wasModel, ct)
            End If
            Return rel.Tag
        Catch ex As OperationCanceledException
            SetState(If(IsInstalled(), EngineState.Stopped, EngineState.NotInstalled), "Install cancelled")
            Throw
        Catch ex As Exception
            SetState(EngineState.Error, "Install failed: " & ex.Message)
            Throw
        End Try
    End Function

    '════════════════════════════════════════════════════════════
    '  Start / stop / model switching
    '════════════════════════════════════════════════════════════
    ''' <summary>
    ''' Loads the model if needed and registers a running request in one atomic step, so no other
    ''' request (e.g. a phone on the LAN asking for a different model) can switch models mid-answer.
    ''' Dispose the result when the answer is finished.
    ''' </summary>
    Public Async Function AcquireAsync(model As LocalModel, ct As CancellationToken) As Task(Of IDisposable)
        If model Is Nothing Then Throw New ArgumentNullException(NameOf(model))
        Await _switchLock.WaitAsync(ct)
        Try
            Await EnsureLoadedLockedAsync(model, ct)
            Interlocked.Increment(_activeRequests)
            Touch()
            Return New RequestScope(Me)
        Finally
            _switchLock.Release()
        End Try
    End Function

    ''' <summary>
    ''' Makes room on the GPU for another program (image / video generation). Waits until running answers
    ''' finish, then unloads the chat model if both would not fit together (or always / never, per settings).
    ''' Chat requests wait until the returned lease is disposed; the chat model reloads on the next message.
    ''' </summary>
    Public Async Function ReserveGpuAsync(needBytes As Long, status As Action(Of String), ct As CancellationToken) As Task(Of IDisposable)
        Await _switchLock.WaitAsync(ct)
        Try
            If Volatile.Read(_activeRequests) > 0 Then status?.Invoke("Waiting for the chat answer to finish…")
            While Volatile.Read(_activeRequests) > 0
                Await Task.Delay(200, ct)
            End While
            Dim m = _CurrentModel
            If _proc IsNot Nothing AndAlso m IsNot Nothing Then
                Dim mode = _settings().ImageGpuSharing   ' 0 auto, 1 always unload, 2 keep loaded
                Dim vram = HardwareInfo.Current().VramBytes
                Dim chatBytes = CLng(m.SizeBytes * 1.15 + 600L * 1024 * 1024)
                ' (auto: always — a chat model left loaded also holds system RAM, which big video models need;
                '  it loads again by itself for the next message)
                Dim unload = mode <> 2
                If unload Then
                    status?.Invoke("Unloading the chat model to free the GPU…")
                    AppendLog($"Unloading {m.DisplayName} to make room for image generation")
                    Await StopAsync()
                End If
            End If
            ' the document-search helper holds memory too (it starts again by itself when needed)
            Knowledge.StopEmbedder()
            ' the chat model (or the last picture / video / sound job) may have just closed: wait until the card
            ' has really given its memory back, so the next model sees all of it
            status?.Invoke("Freeing memory…")
            Await HardwareInfo.WaitForVramReleaseAsync(ct)
            Await HardwareInfo.WaitForRamReleaseAsync(ct)
        Catch
            _switchLock.Release()
            Throw
        End Try
        Return New GpuLease(Me)
    End Function

    Private Class GpuLease
        Implements IDisposable
        Private _e As LlamaEngine
        Public Sub New(e As LlamaEngine)
            _e = e
        End Sub
        Public Sub Dispose() Implements IDisposable.Dispose
            If _e Is Nothing Then Return
            _e._switchLock.Release()
            _e = Nothing
        End Sub
    End Class

    ''' <summary>Pre-loads a model (e.g. when it is picked in the toolbar) without starting a request.</summary>
    Public Async Function EnsureModelAsync(model As LocalModel, ct As CancellationToken) As Task
        If model Is Nothing Then Throw New ArgumentNullException(NameOf(model))
        Await _switchLock.WaitAsync(ct)
        Try
            Await EnsureLoadedLockedAsync(model, ct)
        Finally
            _switchLock.Release()
        End Try
    End Function

    ''' <summary>Caller holds _switchLock.</summary>
    Private Async Function EnsureLoadedLockedAsync(model As LocalModel, ct As CancellationToken) As Task
        If _proc IsNot Nothing AndAlso Not _proc.HasExited AndAlso
           State = EngineState.Ready AndAlso SameModel(_CurrentModel, model) Then
            Touch()
            Return
        End If

        If Not IsInstalled() Then
            Await InstallAsync(Nothing, ct, reloadModel:=False)   ' we hold the lock: no nested reload
        End If

        ' Wait for running answers (other chats / LAN users) before swapping the model out.
        Dim waited = 0
        While Volatile.Read(_activeRequests) > 0
            If waited = 0 Then SetState(EngineState.Loading, "Waiting for another chat to finish before switching model…")
            Await Task.Delay(250, ct)
            waited += 1
            If waited > 4 * 60 * 10 Then Throw New TimeoutException("Another chat has been using the engine for over 10 minutes.")
        End While

        Await StopAsync()
        Await StartAsync(model, ct)
    End Function

    Private Shared Function SameModel(a As LocalModel, b As LocalModel) As Boolean
        Return a IsNot Nothing AndAlso b IsNot Nothing AndAlso
               String.Equals(a.MainFile, b.MainFile, StringComparison.OrdinalIgnoreCase)
    End Function

    Private Async Function StartAsync(model As LocalModel, ct As CancellationToken) As Task
        Dim exe = ServerExe()
        If exe Is Nothing Then Throw New Exception("The llama.cpp engine is not installed.")
        If Not File.Exists(model.MainFile) Then Throw New FileNotFoundException("Model file not found.", model.MainFile)

        Dim s = _settings()
        _port = FreePort()
        Dim slots = Math.Max(1, s.ParallelSlots)

        Dim args As New StringBuilder()
        args.Append($"-m ""{model.MainFile}"" --host 127.0.0.1 --port {_port}")
        If s.AutoContext Then
            ' llama.cpp picks the biggest context that still fits in VRAM (never below the minimum).
            args.Append($" --fit-ctx {Math.Max(2048, s.MinAutoContext) * slots} -np {slots} --no-webui --jinja")
        Else
            args.Append($" -c {s.ContextSize * slots} -np {slots} --no-webui --jinja")
        End If
        If Not String.IsNullOrEmpty(model.MmprojFile) AndAlso File.Exists(model.MmprojFile) Then
            args.Append($" --mmproj ""{model.MmprojFile}""")
        End If
        Dim ngl = If(s.GpuLayers, "auto").Trim().ToLowerInvariant()
        If EffectiveVariant = "cpu" Then
            args.Append(" -ngl 0")
        ElseIf ngl <> "auto" AndAlso ngl <> "" Then
            args.Append(" -ngl " & ngl)
        End If
        If Not String.IsNullOrWhiteSpace(s.KvCacheType) AndAlso s.KvCacheType <> "f16" Then
            args.Append($" -ctk {s.KvCacheType} -ctv {s.KvCacheType}")
        End If
        ' Faster generation: speculative decoding (same output, produced faster).
        Dim speed = If(s.SpeedUpMode, "auto").ToLowerInvariant()
        Dim hasMtp = Not String.IsNullOrEmpty(model.MtpFile) AndAlso File.Exists(model.MtpFile)
        If hasMtp AndAlso (speed = "auto" OrElse speed = "mtp") Then
            args.Append($" --spec-type draft-mtp --spec-draft-model ""{model.MtpFile}""")
        ElseIf speed = "auto" OrElse speed = "ngram" Then
            args.Append(" --spec-type ngram-mod")
        End If
        If Not String.IsNullOrWhiteSpace(s.ExtraEngineArgs) Then args.Append(" " & s.ExtraEngineArgs.Trim())

        AppendLog("> llama-server " & args.ToString())
        SetState(EngineState.Loading, "Loading " & model.DisplayName & "…")

        Dim psi As New ProcessStartInfo(exe, args.ToString()) With {
            .UseShellExecute = False,
            .CreateNoWindow = True,
            .RedirectStandardOutput = True,
            .RedirectStandardError = True,
            .WorkingDirectory = Path.GetDirectoryName(exe),
            .StandardOutputEncoding = Encoding.UTF8,
            .StandardErrorEncoding = Encoding.UTF8
        }
        ' 🧠 Less system RAM (set through the environment, so engine builds that don't know a setting just ignore it):
        '  - the prompt cache kept in RAM between messages: 8 GB by default → 1–2 GB;
        '  - a model that fits on the graphics card is read into it without memory-mapping the file, so its copy in
        '    RAM is released after loading instead of staying mapped;
        '  - "Reduce system RAM usage": the conversation memory (KV cache) at 8 bits instead of 16 (half the size).
        Dim hwi = HardwareInfo.Current()
        Dim lowRam = s.LowRamActive(hwi.VramGB)
        psi.Environment("LLAMA_ARG_CACHE_RAM") = If(lowRam, "1024", "2048")
        ' the automatic fit leaves this much graphics memory free (1 GB by default): a little more keeps Windows from
        ' spilling into system RAM when the chat window, pictures or videos need some too
        If lowRam Then psi.Environment("LLAMA_ARG_FIT_TARGET") = "1536"
        Dim modelBytes = model.SizeBytes
        If EffectiveVariant <> "cpu" AndAlso hwi.VramBytes > 0 AndAlso modelBytes > 0 AndAlso modelBytes < hwi.VramBytes * 0.8 Then
            psi.Environment("LLAMA_ARG_LOAD_MODE") = "none"
            psi.Environment("LLAMA_ARG_NO_MMAP") = "1"
        End If
        If lowRam AndAlso EffectiveVariant.StartsWith("cuda", StringComparison.Ordinal) AndAlso
           (String.IsNullOrWhiteSpace(s.KvCacheType) OrElse s.KvCacheType = "f16") Then
            psi.Environment("LLAMA_ARG_CACHE_TYPE_K") = "q8_0"
            psi.Environment("LLAMA_ARG_CACHE_TYPE_V") = "q8_0"
        End If
        Dim p As New Process() With {.StartInfo = psi, .EnableRaisingEvents = True}
        AddHandler p.OutputDataReceived, Sub(o, e) If e.Data IsNot Nothing Then AppendLog(e.Data)
        AddHandler p.ErrorDataReceived, Sub(o, e) If e.Data IsNot Nothing Then AppendLog(e.Data)
        AddHandler p.Exited, Sub(o, e) OnProcessExited(p)
        p.Start()
        AssignToJob(p)
        p.BeginOutputReadLine()
        p.BeginErrorReadLine()
        _proc = p
        _CurrentModel = model

        ' Wait for /health → 200 (503 while loading).
        Dim sw = Stopwatch.StartNew()
        Do
            ct.ThrowIfCancellationRequested()
            If p.HasExited Then
                _proc = Nothing
                _CurrentModel = Nothing
                Dim tail = String.Join(Environment.NewLine, _log.Reverse().Take(25).Reverse())
                SetState(EngineState.Error, "Engine stopped while loading the model")
                Throw New Exception("The engine exited while loading the model." & Environment.NewLine &
                                    Environment.NewLine & FriendlyHint(tail) & Environment.NewLine & Environment.NewLine & tail)
            End If
            Try
                Using cts As New CancellationTokenSource(TimeSpan.FromSeconds(2))
                    Using r = Await OpenAiCompatService.Http.GetAsync(BaseUrl & "/health", cts.Token)
                        If r.IsSuccessStatusCode Then Exit Do
                    End Using
                End Using
            Catch ex As OperationCanceledException When Not ct.IsCancellationRequested
            Catch ex As HttpRequestException
            End Try
            If sw.Elapsed > TimeSpan.FromMinutes(20) Then
                Await StopAsync()
                Throw New TimeoutException("The model took more than 20 minutes to load.")
            End If
            Await Task.Delay(300, ct)
        Loop

        Touch()
        _LoadedContext = Await ReadContextAsync(slots)
        _LastLoadSeconds = sw.Elapsed.TotalSeconds
        SetState(EngineState.Ready, model.DisplayName & $" loaded in {sw.Elapsed.TotalSeconds:0.0}s" &
                 If(_LoadedContext > 0, $" · {_LoadedContext \ 1024}K context", ""))
    End Function

    ''' <summary>Context per chat actually chosen by the engine (from /props).</summary>
    Public ReadOnly Property LoadedContext As Integer
    Public ReadOnly Property LastLoadSeconds As Double

    Private Async Function ReadContextAsync(slots As Integer) As Task(Of Integer)
        Try
            Using cts As New CancellationTokenSource(3000)
                Dim o = JObject.Parse(Await OpenAiCompatService.Http.GetStringAsync(BaseUrl & "/props", cts.Token))
                Dim n = o("default_generation_settings")?("n_ctx")?.ToObject(Of Integer?)()
                Return If(n, 0)
            End Using
        Catch
            Return 0
        End Try
    End Function

    Private Shared Function FriendlyHint(log As String) As String
        Dim l = log.ToLowerInvariant()
        If l.Contains("out of memory") OrElse l.Contains("failed to allocate") OrElse l.Contains("cudamalloc failed") Then
            Return "Hint: not enough VRAM/RAM. Lower the context size, pick a smaller quantization, or set KV cache to q8_0 in Settings."
        End If
        If l.Contains("unknown model architecture") OrElse l.Contains("unknown architecture") Then
            Return "Hint: this model is newer than the installed engine. Use Settings → Engine → Install / Update."
        End If
        If l.Contains("invalid argument") OrElse l.Contains("unknown argument") OrElse l.Contains("error: invalid") Then
            Return "Hint: check 'Extra engine arguments' in Settings."
        End If
        If l.Contains("cuda") AndAlso (l.Contains("driver") OrElse l.Contains("no cuda-capable")) Then
            Return "Hint: CUDA driver problem. Update the NVIDIA driver or switch the engine to Vulkan in Settings."
        End If
        Return "Engine log (last lines):"
    End Function

    Private Sub OnProcessExited(p As Process)
        If p IsNot _proc Then Return
        _proc = Nothing
        _CurrentModel = Nothing
        If State = EngineState.Ready Then SetState(EngineState.Error, "Engine stopped unexpectedly — it will restart on the next message")
    End Sub

    Public Async Function StopAsync() As Task
        Dim p = _proc
        _proc = Nothing
        _CurrentModel = Nothing
        If p IsNot Nothing Then
            Try
                If Not p.HasExited Then
                    p.Kill(True)
                    Await p.WaitForExitAsync(New CancellationTokenSource(5000).Token)
                End If
            Catch
            End Try
            p.Dispose()
        End If
        If State <> EngineState.Installing Then
            SetState(If(IsInstalled(), EngineState.Stopped, EngineState.NotInstalled), "Engine idle (no model loaded)")
        End If
    End Function

    '════════════════════════════════════════════════════════════
    '  Request tracking + idle unload
    '════════════════════════════════════════════════════════════
    ''' <summary>Starts another llama-server (e.g. the embedding model) tied to the app's lifetime.</summary>
    Public Function LaunchHelper(args As String) As Process
        Dim exe = ServerExe()
        If exe Is Nothing Then Throw New Exception("The llama.cpp engine is not installed.")
        Dim psi As New ProcessStartInfo(exe, args) With {
            .UseShellExecute = False, .CreateNoWindow = True, .RedirectStandardOutput = True, .RedirectStandardError = True,
            .WorkingDirectory = Path.GetDirectoryName(exe)}
        Dim p As New Process() With {.StartInfo = psi}
        AddHandler p.OutputDataReceived, Sub(o, e) If e.Data IsNot Nothing Then AppendLog("[helper] " & e.Data)
        AddHandler p.ErrorDataReceived, Sub(o, e) If e.Data IsNot Nothing Then AppendLog("[helper] " & e.Data)
        AppendLog("> llama-server " & args)
        p.Start()
        AssignToJob(p)
        p.BeginOutputReadLine()
        p.BeginErrorReadLine()
        Return p
    End Function

    Public ReadOnly Property ActiveRequests As Integer
        Get
            Return Volatile.Read(_activeRequests)
        End Get
    End Property

    Private Class RequestScope
        Implements IDisposable
        Private _e As LlamaEngine
        Public Sub New(e As LlamaEngine)
            _e = e
        End Sub
        Public Sub Dispose() Implements IDisposable.Dispose
            If _e Is Nothing Then Return
            Interlocked.Decrement(_e._activeRequests)
            _e.Touch()
            _e = Nothing
        End Sub
    End Class

    Private Sub Touch()
        _lastActivity = DateTime.Now
    End Sub

    Private Sub IdleCheck(state As Object)
        Try
            Dim mins = _settings().UnloadAfterIdleMinutes
            If mins <= 0 OrElse _proc Is Nothing Then Return
            If Volatile.Read(_activeRequests) > 0 Then Return
            If (DateTime.Now - _lastActivity).TotalMinutes >= mins Then
                AppendLog($"Unloading model after {mins} idle minutes")
                StopAsync().Wait()
            End If
        Catch
        End Try
    End Sub

    '════════════════════════════════════════════════════════════
    '  Logging / state
    '════════════════════════════════════════════════════════════
    Public Function GetLog() As String
        Return String.Join(Environment.NewLine, _log)
    End Function

    Private Sub AppendLog(line As String)
        _log.Enqueue(line)
        Dim dummy As String = Nothing
        While _log.Count > 3000 AndAlso _log.TryDequeue(dummy)
        End While
        RaiseEvent LogLine(line)
    End Sub

    Private Sub SetState(s As EngineState, msg As String)
        _State = s
        _StateMessage = msg
        RaiseEvent StateChanged(s, msg)
    End Sub

    Private Shared Function FreePort() As Integer
        Dim l As New TcpListener(IPAddress.Loopback, 0)
        l.Start()
        Dim p = CType(l.LocalEndpoint, IPEndPoint).Port
        l.Stop()
        Return p
    End Function

    ''' <summary>Kills llama-server processes left behind by a previous crash of this app.</summary>
    Private Sub KillOrphans()
        Try
            Dim root = EngineRoot
            For Each p In Process.GetProcessesByName("llama-server")
                Try
                    Dim path = p.MainModule?.FileName
                    If path IsNot Nothing AndAlso path.StartsWith(root, StringComparison.OrdinalIgnoreCase) Then p.Kill(True)
                Catch
                Finally
                    p.Dispose()
                End Try
            Next
        Catch
        End Try
    End Sub

    '════════════════════════════════════════════════════════════
    '  Windows Job Object: the engine dies with the app, even on a crash
    '════════════════════════════════════════════════════════════
    <StructLayout(LayoutKind.Sequential)>
    Private Structure JOBOBJECT_BASIC_LIMIT_INFORMATION
        Public PerProcessUserTimeLimit As Long
        Public PerJobUserTimeLimit As Long
        Public LimitFlags As UInteger
        Public MinimumWorkingSetSize As UIntPtr
        Public MaximumWorkingSetSize As UIntPtr
        Public ActiveProcessLimit As UInteger
        Public Affinity As UIntPtr
        Public PriorityClass As UInteger
        Public SchedulingClass As UInteger
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Private Structure IO_COUNTERS
        Public ReadOperationCount As ULong
        Public WriteOperationCount As ULong
        Public OtherOperationCount As ULong
        Public ReadTransferCount As ULong
        Public WriteTransferCount As ULong
        Public OtherTransferCount As ULong
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Private Structure JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        Public BasicLimitInformation As JOBOBJECT_BASIC_LIMIT_INFORMATION
        Public IoInfo As IO_COUNTERS
        Public ProcessMemoryLimit As UIntPtr
        Public JobMemoryLimit As UIntPtr
        Public PeakProcessMemoryUsed As UIntPtr
        Public PeakJobMemoryUsed As UIntPtr
    End Structure

    <DllImport("kernel32.dll", CharSet:=CharSet.Unicode, SetLastError:=True)>
    Private Shared Function CreateJobObject(lpJobAttributes As IntPtr, lpName As String) As IntPtr
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Shared Function SetInformationJobObject(hJob As IntPtr, infoClass As Integer,
                                                    ByRef info As JOBOBJECT_EXTENDED_LIMIT_INFORMATION,
                                                    cbInfo As UInteger) As Boolean
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Shared Function AssignProcessToJobObject(hJob As IntPtr, hProcess As IntPtr) As Boolean
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Shared Function CloseHandle(h As IntPtr) As Boolean
    End Function

    Private Sub CreateKillOnCloseJob()
        Try
            If Not OperatingSystem.IsWindows() Then Return
            _job = CreateJobObject(IntPtr.Zero, Nothing)
            If _job = IntPtr.Zero Then Return
            Dim info As New JOBOBJECT_EXTENDED_LIMIT_INFORMATION()
            info.BasicLimitInformation.LimitFlags = &H2000UI   ' JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
            SetInformationJobObject(_job, 9, info, CUInt(Marshal.SizeOf(GetType(JOBOBJECT_EXTENDED_LIMIT_INFORMATION))))
        Catch
            _job = IntPtr.Zero
        End Try
    End Sub

    Friend Sub AssignToJob(p As Process)
        Try
            If _job <> IntPtr.Zero Then AssignProcessToJobObject(_job, p.Handle)
        Catch
        End Try
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Try
            _idleTimer.Dispose()
            StopAsync().Wait(5000)
            If _job <> IntPtr.Zero Then CloseHandle(_job)
            _job = IntPtr.Zero
        Catch
        End Try
    End Sub
End Class
