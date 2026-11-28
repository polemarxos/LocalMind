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

''' <summary>A speech (voice cloning) or music model run by llama.cpp's llama-tts.</summary>
Public Class AudioModel
    <JsonProperty("id")> Public Property Id As String
    <JsonProperty("name")> Public Property Name As String
    <JsonProperty("kind")> Public Property Kind As String          ' voice | music
    <JsonProperty("about")> Public Property About As String
    <JsonProperty("model")> Public Property Model As MediaFile
    <JsonProperty("mmproj")> Public Property Mmproj As MediaFile
    <JsonProperty("clone")> Public Property Clone As Boolean
    <JsonProperty("langs")> Public Property Langs As New List(Of String)()
    <JsonProperty("transcript")> Public Property Transcript As Boolean ' cloning is closer with the words of the recording
    <JsonProperty("dl")> Public Property Downloads As Long
    <JsonProperty("design")> Public Property Design As Boolean         ' "(voice description)Text" designs a voice
    ''' <summary>Added by the user from Hugging Face (🌐 Browse), not from LocalMind's list.</summary>
    <JsonProperty("user")> Public Property IsUser As Boolean
    ''' <summary>Hugging Face repo the files come from (for the list's "From" column).</summary>
    <JsonIgnore> Public ReadOnly Property Source As String
        Get
            Return If(Model?.Repo, "")
        End Get
    End Property

    <JsonIgnore> Public ReadOnly Property IsMusic As Boolean
        Get
            Return Kind = "music"
        End Get
    End Property

    <JsonIgnore> Public ReadOnly Property Size As Long
        Get
            Return Model.Size + Mmproj.Size
        End Get
    End Property

    Public Overrides Function ToString() As String
        Return Name
    End Function
End Class

''' <summary>
''' Voice cloning / text-to-speech and song generation with the llama.cpp engine LocalMind already uses
''' (llama-tts). Models go to Models\Audio, results to the Outputs folder as WAV.
''' </summary>
Public Module AudioStudio
    Private _models As List(Of AudioModel)

    Public Function Models() As List(Of AudioModel)
        If _models Is Nothing Then
            Dim list As List(Of AudioModel)
            Using s = Assembly.GetExecutingAssembly().GetManifestResourceStream("audio_catalog.json")
                Using r As New StreamReader(s)
                    list = JsonConvert.DeserializeObject(Of List(Of AudioModel))(r.ReadToEnd())
                End Using
            End Using
            ' + the ones the user added from Hugging Face
            Try
                If File.Exists(UserFile) Then
                    For Each u In If(JsonConvert.DeserializeObject(Of List(Of AudioModel))(File.ReadAllText(UserFile)), New List(Of AudioModel)())
                        If u?.Model Is Nothing OrElse u.Mmproj Is Nothing OrElse list.Any(Function(x) x.Id = u.Id) Then Continue For
                        u.IsUser = True
                        list.Add(u)
                    Next
                End If
            Catch
            End Try
            _models = list
        End If
        Return _models
    End Function

    Private ReadOnly Property UserFile As String
        Get
            Return Path.Combine(AppPaths.Roaming, "audio_models.json")
        End Get
    End Property

    ''' <summary>Adds a music or voice model found on Hugging Face (kept in audio_models.json).</summary>
    Public Function AddUserModel(m As AudioModel) As AudioModel
        m.IsUser = True
        Dim same = Models().FirstOrDefault(Function(x) x.Model.Repo = m.Model.Repo AndAlso x.Model.Path = m.Model.Path AndAlso
                                                       x.Mmproj.Repo = m.Mmproj.Repo AndAlso x.Mmproj.Path = m.Mmproj.Path)
        If same IsNot Nothing Then Return same
        Models().Add(m)
        SaveUserModels()
        Return m
    End Function

    Public Sub RemoveUserModel(m As AudioModel)
        If m Is Nothing OrElse Not m.IsUser Then Return
        Models().Remove(m)
        SaveUserModels()
    End Sub

    Private Sub SaveUserModels()
        Try
            File.WriteAllText(UserFile, JsonConvert.SerializeObject(Models().Where(Function(x) x.IsUser).ToList(), Formatting.Indented))
        Catch
        End Try
    End Sub

    Public ReadOnly Property Folder As String
        Get
            Dim d = Path.Combine(AppCore.Library.Folder, "Audio")
            Directory.CreateDirectory(d)
            Return d
        End Get
    End Property

    Public Function LocalPath(f As MediaFile) As String
        Return Path.Combine(Folder, f.Repo.Replace("/", "__"), Path.GetFileName(f.Path))
    End Function

    Private Function Present(f As MediaFile) As Boolean
        Dim p = LocalPath(f)
        Return File.Exists(p) AndAlso (f.Size <= 0 OrElse New FileInfo(p).Length = f.Size)
    End Function

    Public Function IsInstalled(m As AudioModel) As Boolean
        Return Present(m.Model) AndAlso Present(m.Mmproj)
    End Function

    Public Function BytesToDownload(m As AudioModel) As Long
        Return {m.Model, m.Mmproj}.Where(Function(f) Not Present(f)).Sum(Function(f) f.Size)
    End Function

    ''' <summary>Downloads a voice or music model through the one download list (Models → Downloads).</summary>
    Public Async Function DownloadAsync(m As AudioModel, progress As Action(Of Integer, String), ct As CancellationToken) As Task
        If IsInstalled(m) Then Return
        progress?.Invoke(0, Localizer.T("Downloading — see Models → Downloads"))
        Await AppCore.Downloads.RunAsync(m.Name, If(m.IsMusic, "Music", "Voice"), "audio:" & m.Id, Function(p, c) DownloadCoreAsync(m, p, c), ct)
    End Function

    Private Async Function DownloadCoreAsync(m As AudioModel, progress As Action(Of Integer, String), ct As CancellationToken) As Task
        Dim todo = {m.Model, m.Mmproj}.Where(Function(f) Not Present(f)).ToList()
        Dim total = Math.Max(1L, todo.Sum(Function(f) f.Size))
        Dim before As Long = 0
        For Each f In todo
            Dim url = $"https://huggingface.co/{f.Repo}/resolve/main/" & String.Join("/", f.Path.Split("/"c).Select(AddressOf Uri.EscapeDataString))
            Dim b = before
            Await Downloader.DownloadFileAsync(url, LocalPath(f), AppCore.Settings.HuggingFaceToken,
                Sub(done, size, speed) progress?.Invoke(CInt((b + done) * 100 \ total),
                    $"{Path.GetFileName(f.Path)}  {Downloader.FormatBytes(b + done)} / {Downloader.FormatBytes(total)}  ·  {Downloader.FormatBytes(CLng(speed))}/s"),
                ct, f.Size)
            before += f.Size
        Next
        progress?.Invoke(100, "Ready")
    End Function

    Public Sub Delete(m As AudioModel)
        ' files another installed voice / music model shares are kept
        Dim keep = Models().Where(Function(x) x IsNot m AndAlso IsInstalled(x)).SelectMany(Function(x) {LocalPath(x.Model), LocalPath(x.Mmproj)}).
                            ToHashSet(StringComparer.OrdinalIgnoreCase)
        For Each f In {m.Model, m.Mmproj}
            Dim p = LocalPath(f)
            If keep.Contains(p) Then Continue For
            For Each extra In {p, p & ".part"}
                If File.Exists(extra) Then File.Delete(extra)
            Next
            Try
                Dim d = Path.GetDirectoryName(p)
                If Directory.Exists(d) AndAlso Not Directory.EnumerateFileSystemEntries(d).Any() Then Directory.Delete(d)
            Catch
            End Try
        Next
    End Sub

    Public Function Fit(m As AudioModel, hw As HardwareInfo) As FitLevel
        Dim gb = 1024L * 1024 * 1024
        Dim need = m.Size + 1536L * 1024 * 1024
        Dim extra = If(m.IsMusic, 4L, 2L) * gb                 ' context + audio decoding in system RAM
        If hw.VramBytes > 0 AndAlso need <= hw.VramBytes * 0.95 AndAlso hw.RamBytes >= extra + 2 * gb Then Return FitLevel.Fits
        If hw.VramBytes = 0 AndAlso hw.RamBytes * 0.85 < m.Size + extra Then Return FitLevel.TooBig
        If need + extra <= hw.RamBytes * 0.85 + hw.VramBytes Then Return FitLevel.PartialOffload
        Return FitLevel.TooBig
    End Function

    Public Function RequirementText(m As AudioModel, hw As HardwareInfo) As String
        Dim gb = 1024.0 * 1024 * 1024
        Dim vram = (m.Size + 1536L * 1024 * 1024) / gb
        Dim ram = m.Size / gb + If(m.IsMusic, 4, 2)          ' songs keep a long context in memory
        Dim f = Fit(m, hw)
        Dim mark = If(f = FitLevel.Fits, "✔", If(f = FitLevel.PartialOffload, "⚠", "✖"))
        Return $"{mark} {Localizer.T("Needs about")} {vram:0.#} GB {Localizer.T("graphics memory")} + {ram:0.#} GB RAM  ·  " &
               $"{Localizer.T("this PC")}: {hw.VramGB:0.#} + {hw.RamBytes / gb:0} GB"
    End Function

    Public Function TtsExe() As String
        Dim server = AppCore.Engine.ServerExe()
        If server Is Nothing Then Return Nothing
        Dim name = If(OperatingSystem.IsWindows(), "llama-tts.exe", "llama-tts")
        Dim f = Path.Combine(Path.GetDirectoryName(server), name)
        Return If(File.Exists(f), f, Nothing)
    End Function

    Public Class AudioRequest
        Public Property Model As AudioModel
        ''' <summary>Text to speak, or for music the style / description.</summary>
        Public Property Text As String
        ''' <summary>Music only: the lyrics with [verse] / [chorus] tags.</summary>
        Public Property Lyrics As String
        Public Property Language As String
        ''' <summary>Recording of the voice to clone (WAV / MP3, a few seconds up to 30 s).</summary>
        Public Property SpeakerFile As String
        ''' <summary>Words spoken in the recording (optional, makes cloning closer on some models).</summary>
        Public Property SpeakerText As String
        Public Property Seed As Integer = -1
        ''' <summary>Music: song length in seconds (0 = let the model decide).</summary>
        Public Property Seconds As Integer
    End Class

    Private _proc As Process

    ''' <summary>What the running job makes (for the status bar): a song, else speech.</summary>
    Public Property CurrentIsMusic As Boolean

    Public ReadOnly Property IsBusy As Boolean
        Get
            Dim p = _proc
            Return p IsNot Nothing AndAlso Not p.HasExited
        End Get
    End Property

    ''' <summary>Runs llama-tts and returns the WAV file.</summary>
    Public Async Function GenerateAsync(req As AudioRequest, progress As Action(Of Integer, String), ct As CancellationToken) As Task(Of String)
        Dim m = req.Model
        Dim total = Stopwatch.StartNew()
        If Not AppCore.Engine.IsInstalled() Then
            progress?.Invoke(0, "Installing the AI engine…")
            Await AppCore.Engine.InstallAsync(New Progress(Of (Text As String, Percent As Integer))(Sub(p) progress?.Invoke(p.Percent, p.Text)), ct)
        End If
        Dim exe = TtsExe()
        If exe Is Nothing Then Throw New Exception(Localizer.T("This engine build has no llama-tts. Update the engine in Settings → Built-in engine."))
        If Not IsInstalled(m) Then Await DownloadAsync(m, progress, ct)

        Dim out = Path.Combine(MediaStudio.OutputFolder, $"{DateTime.Now:yyyyMMdd-HHmmss}-{m.Id}.wav")
        Dim a As New StringBuilder()
        a.Append($"-m {Q(LocalPath(m.Model))} -mm {Q(LocalPath(m.Mmproj))} ")
        Dim promptFile As String = Nothing
        If m.IsMusic Then
            ' Song request in the reference format: [Tags] description, [Lyrics] text.
            Dim sb As New StringBuilder()
            sb.AppendLine("[Tags]").AppendLine(If(req.Text, "").Trim())
            sb.AppendLine("[Lyrics]").AppendLine(If(String.IsNullOrWhiteSpace(req.Lyrics), "[instrumental]", req.Lyrics.Trim()))
            promptFile = Path.Combine(Path.GetTempPath(), $"localmind-song-{Guid.NewGuid():N}.txt")
            File.WriteAllText(promptFile, sb.ToString(), New UTF8Encoding(False))
            a.Append($"-f {Q(promptFile)} ")
            If req.Seconds > 0 Then a.Append($"-n {req.Seconds * 25} ")       ' 25 frames per second (40 ms)
            If m.Id.StartsWith("minimax") Then a.Append("-c 20480 ")
        Else
            a.Append($"-p {Q(req.Text)} ")
            If Not String.IsNullOrEmpty(req.Language) AndAlso m.Langs.Count > 1 Then a.Append($"--tts-lang {req.Language} ")
            If m.Clone AndAlso Not String.IsNullOrEmpty(req.SpeakerFile) AndAlso File.Exists(req.SpeakerFile) Then
                a.Append($"--tts-speaker-file {Q(req.SpeakerFile)} ")
                If m.Transcript AndAlso Not String.IsNullOrWhiteSpace(req.SpeakerText) Then a.Append($"--tts-speaker-text {Q(req.SpeakerText.Trim())} ")
            End If
            If m.Id = "s2-pro" Then a.Append("-n 2000 ")
            ' Speech needs a few thousand positions; the default (the model's 128K) would reserve many GB for nothing.
            a.Append("-c 8192 ")
        End If
        If req.Seed >= 0 Then a.Append($"--seed {req.Seed} ")
        Dim hw = HardwareInfo.Current()
        a.Append(If(hw.VramBytes > 0, "-ngl 999 ", "-ngl 0 "))
        a.Append($"--output {Q(out)}")

        Dim lease As IDisposable = Nothing
        If AppCore.Settings.Backend = BackendKind.BuiltIn Then
            lease = Await AppCore.Engine.ReserveGpuAsync(m.Size + 1024L * 1024 * 1024, Sub(t) progress?.Invoke(0, t), ct)
        End If
        Dim log As New StringBuilder()
        Try
            progress?.Invoke(0, "Loading model…")
            Dim psi As New ProcessStartInfo(exe, a.ToString()) With {
                .UseShellExecute = False, .CreateNoWindow = True, .RedirectStandardOutput = True, .RedirectStandardError = True,
                .WorkingDirectory = Path.GetDirectoryName(exe), .StandardOutputEncoding = Encoding.UTF8, .StandardErrorEncoding = Encoding.UTF8}
            ' (some models print no progress for minutes: the time spent keeps showing so it doesn't look stuck)
            Dim clock = Stopwatch.StartNew()
            Dim lastPct = 0, lastText = ""
            Dim report = Sub(p As Integer, t As String)
                             lastPct = p : lastText = t
                             progress?.Invoke(p, $"{t} · {CInt(clock.Elapsed.TotalMinutes)}:{clock.Elapsed.Seconds:00}")
                         End Sub
            Dim frameRx As New Regex("(\d+)\s*/\s*(\d+)\s*(frames|codes|tokens)", RegexOptions.IgnoreCase)
            Dim onLine = Sub(line As String)
                             If line Is Nothing Then Return
                             SyncLock log
                                 log.AppendLine(line)
                             End SyncLock
                             Dim mm = frameRx.Match(line)
                             If mm.Success Then
                                 Dim i = CDbl(mm.Groups(1).Value), n = Math.Max(1.0, CDbl(mm.Groups(2).Value))
                                 report(CInt(Math.Min(95, i * 95 / n)), $"{If(m.IsMusic, "Composing", "Speaking")}… {mm.Value}")
                             ElseIf line.Contains("decod", StringComparison.OrdinalIgnoreCase) OrElse line.Contains("vocoder", StringComparison.OrdinalIgnoreCase) Then
                                 report(96, "Making the audio…")
                             End If
                         End Sub
            CurrentIsMusic = m.IsMusic
            _proc = New Process() With {.StartInfo = psi}
            AddHandler _proc.OutputDataReceived, Sub(s, e) onLine(e.Data)
            AddHandler _proc.ErrorDataReceived, Sub(s, e) onLine(e.Data)
            _proc.Start()
            AppCore.Engine.AssignToJob(_proc)
            _proc.BeginOutputReadLine()
            _proc.BeginErrorReadLine()
            report(2, If(m.IsMusic, "Composing…", "Speaking…"))
            Dim beatStop As New CancellationTokenSource()
            Dim beat = Task.Run(Async Function()
                                    While Not beatStop.IsCancellationRequested
                                        Try : Await Task.Delay(1000, beatStop.Token) : Catch : Exit While : End Try
                                        report(lastPct, lastText)
                                    End While
                                End Function)
            Try
                Await _proc.WaitForExitAsync(ct)
            Catch ex As OperationCanceledException
                Try : _proc.Kill(True) : Catch : End Try
                Throw
            Finally
                beatStop.Cancel()
            End Try
            Dim code = _proc.ExitCode
            _proc = Nothing
            If code <> 0 OrElse Not File.Exists(out) Then
                Dim tail = String.Join(Environment.NewLine, log.ToString().Split({vbLf}, StringSplitOptions.RemoveEmptyEntries).Reverse().Take(12).Reverse())
                Dim hint = ""
                If tail.Contains("unknown projector") OrElse tail.Contains("unknown model architecture") Then
                    hint = Localizer.T("This model needs a newer AI engine: Settings → Built-in engine → Install / Update.") & Environment.NewLine & Environment.NewLine
                ElseIf tail.Contains("out of memory", StringComparison.OrdinalIgnoreCase) OrElse tail.Contains("failed to allocate") Then
                    hint = Localizer.T("Not enough memory for this model. Try a smaller model or close other programs.") & Environment.NewLine & Environment.NewLine
                End If
                Throw New Exception(hint & tail)
            End If
        Finally
            lease?.Dispose()
            If promptFile IsNot Nothing Then Try : File.Delete(promptFile) : Catch : End Try
        End Try
        progress?.Invoke(100, "Done")
        Try
            JobTimes.Record("audio:" & m.Id, total.Elapsed.TotalSeconds, If(m.IsMusic AndAlso req.Seconds > 0, $"{req.Seconds} s song", ""))
        Catch
        End Try
        Return out
    End Function

    Public Sub Cancel()
        Try
            If _proc IsNot Nothing AndAlso Not _proc.HasExited Then _proc.Kill(True)
        Catch
        End Try
    End Sub

    Private Function Q(s As String) As String
        Return """" & If(s, "").Replace("""", "\""") & """"
    End Function
End Module
