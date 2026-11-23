Imports System
Imports System.IO
Imports System.Linq
Imports Newtonsoft.Json

Public Enum BackendKind
    BuiltIn = 0      ' bundled llama.cpp engine
    OpenAiCompatible = 2  ' LM Studio, vLLM, another PC running this app, etc.
End Enum

Public Class AppSettings
    ' ── Chat / UI ──
    Public Property LastModel As String
    Public Property LastPersona As String
    Public Property ShowThinking As Boolean = True
    Public Property HistoryMessages As Integer = 30
    Public Property KeepImagesInLastMessages As Integer = 4
    Public Property MaxTokens As Integer = 0            ' 0 = unlimited
    Public Property TopP As Double = 0.9
    Public Property DefaultTemperature As Double = 0.7

    ' ── Backend ──
    Public Property Backend As BackendKind = BackendKind.BuiltIn
    Public Property OpenAiUrl As String = "http://localhost:1234"
    Public Property OpenAiKey As String = ""

    ' ── Built-in engine (llama.cpp) ──
    Public Property EngineVariant As String = "auto"   ' auto | cuda12 | cuda13 | vulkan | cpu | rocm
    Public Property ModelsFolder As String = ""        ' empty = default under LocalAppData
    Public Property ContextSize As Integer = 16384
    ''' <summary>Let llama.cpp use the largest context that fits in VRAM (at least MinAutoContext).</summary>
    Public Property AutoContext As Boolean = True
    Public Property MinAutoContext As Integer = 8192
    Public Property GpuLayers As String = "auto"       ' auto | all | number
    Public Property ParallelSlots As Integer = 1
    Public Property KvCacheType As String = "f16"      ' f16 | q8_0 | q4_0
    Public Property ExtraEngineArgs As String = ""
    Public Property UnloadAfterIdleMinutes As Integer = 0  ' 0 = never
    ''' <summary>Image generation: 0 = unload the chat model only when both do not fit, 1 = always, 2 = never.</summary>
    Public Property ImageGpuSharing As Integer = 0
    ''' <summary>Image model used by the 🎨 Image switch in chat (recipe id; empty = best installed).</summary>
    Public Property ChatImageModel As String = ""
    Public Property ImproveImagePrompt As Boolean = True
    ''' <summary>Optional abilities. When off, their buttons are hidden and nothing for them is ever downloaded.</summary>
    Public Property EnableImages As Boolean = False
    Public Property EnableAudio As Boolean = False
    ''' <summary>The "which abilities do you want?" question was answered.</summary>
    Public Property FeaturesAsked As Boolean = False
    ''' <summary>Use Turbo / Lightning LoRAs when a model has one.</summary>
    Public Property MediaFast As Boolean = True
    ''' <summary>Keep llama.cpp, stable-diffusion.cpp and FFmpeg up to date automatically (checked every few days).</summary>
    Public Property AutoUpdateEngines As Boolean = True
    ''' <summary>Let the chat model use tools by itself (draw, compose, search the web) when you ask for it.</summary>
    Public Property AgentTools As Boolean = True
    Public Property LastEngineCheck As DateTime = DateTime.MinValue
    ''' <summary>Civitai API key (optional; some downloads need a signed-in account).</summary>
    Public Property CivitaiApiKey As String = ""
    ''' <summary>Search civitai.red (full catalogue, 18+) instead of civitai.com.</summary>
    Public Property CivitaiRed As Boolean = False
    Public Property CivitaiRedConfirmed As Boolean = False
    Public Property HuggingFaceToken As String = ""
    Public Property VerifyDownloads As Boolean = True
    Public Property AutoResumeDownloads As Boolean = True

    ' ── LAN sharing ──
    Public Property LanEnabled As Boolean = False
    Public Property LanPort As Integer = 8484
    Public Property LanPin As String = ""
    Public Property LanRequirePin As Boolean = True
    Public Property LanOpenAiApi As Boolean = True

    ' ── Misc ──
    Public Property FirstRunDone As Boolean = False
    Public Property CheckForUpdates As Boolean = True
    ''' <summary>Show uncensored / NSFW picture and video models (and their sample pictures unblurred).</summary>
    Public Property ShowNsfw As Boolean = False
    ''' <summary>Model Manager: list only models that run fully on the graphics card.</summary>
    Public Property OnlyFitGpu As Boolean = False
    ''' <summary>Download and install LocalMind updates by themselves (used from the next start).</summary>
    Public Property AutoInstallUpdates As Boolean = True
    ''' <summary>The look (Settings → Appearance), one of ThemePresets.All.</summary>
    Public Property ThemeName As String = "Midnight"
    ''' <summary>The loading screen at start (checks updates, updates the model list; the chat model is not loaded).</summary>
    Public Property ShowStartupScreen As Boolean = True
    Public Property MinimizeToTray As Boolean = True
    Public Property CloseToTray As Boolean = False
    Public Property QuickAskHotkey As String = "Ctrl+Alt+Space"

    ' ── Documents / web / voice ──
    Public Property UseDocuments As Boolean = False
    Public Property UseWeb As Boolean = False
    Public Property WebSearchProvider As String = "duckduckgo"   ' duckduckgo | brave | searxng
    Public Property BraveApiKey As String = ""
    Public Property SearxngUrl As String = ""
    Public Property WebPagesToRead As Integer = 3
    Public Property VoiceModel As String = "auto"     ' auto | small-q5_1 | large-v3-turbo-q5_0 | …
    Public Property VoiceLanguage As String = "auto"  ' auto | el | en | …
    Public Property TtsVoice As String = ""
    ''' <summary>Read-aloud with an AI voice: model id from the voice catalog (empty = Windows voices).</summary>
    Public Property ReadAloudModel As String = ""
    ''' <summary>Saved voice to use for AI read-aloud (empty = the model's default voice).</summary>
    Public Property ReadAloudVoice As String = ""
    Public Property AutoReadAnswers As Boolean = False

    ' ── Speed / language ──
    ''' <summary>auto = MTP head if the model has one, otherwise n-gram drafting; mtp; ngram; off.</summary>
    Public Property SpeedUpMode As String = "auto"
    Public Property DownloadSpeedUpFiles As Boolean = True
    Public Property Language As String = "auto"   ' auto | en | el
    Public Property LastUpdateCheck As DateTime

    <JsonIgnore>
    Public ReadOnly Property EffectiveModelsFolder As String
        Get
            If Not String.IsNullOrWhiteSpace(ModelsFolder) Then Return ModelsFolder
            Return AppPaths.DefaultModelsFolder
        End Get
    End Property
End Class

''' <summary>Central place for every on-disk location the app uses.</summary>
Public Module AppPaths
    Public Const AppFolderName As String = "LocalMind"

    Private _roaming As String
    Private _local As String
    Private ReadOnly _lock As New Object()

    ''' <summary>
    ''' Settings, chats, personas, prompts: &lt;exe folder&gt;\Data (portable), or %AppData%\LocalMind
    ''' when the exe folder is read-only.
    ''' </summary>
    Public ReadOnly Property Roaming As String
        Get
            SyncLock _lock
                If _roaming Is Nothing Then
                    _roaming = If(IsPortable, PortableData(), Resolve(Environment.SpecialFolder.ApplicationData))
                End If
                Return _roaming
            End SyncLock
        End Get
    End Property

    ''' <summary>True when LocalMind can keep everything in its own folder.</summary>
    Public ReadOnly Property IsPortable As Boolean
        Get
            Return CanWrite(AppDir)
        End Get
    End Property

    ''' <summary>WebView2 browser cache for the chat view.</summary>
    Public ReadOnly Property WebViewFolder As String
        Get
            Return If(IsPortable, Path.Combine(Roaming, "WebView"), Path.Combine(LocalData, "webview"))
        End Get
    End Property

    ''' <summary>&lt;exe folder&gt;\Data — first run copies settings/chats from %AppData%\LocalMind.</summary>
    Private Function PortableData() As String
        Dim target = Path.Combine(AppDir, "Data")
        If Not Directory.Exists(target) Then
            Dim appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
            Dim old = Path.Combine(appData, AppFolderName)
            If Directory.Exists(old) AndAlso Directory.EnumerateFileSystemEntries(old).Any() Then
                Try
                    CopyTree(old, target)   ' small (text files) — copy, leave the original as a backup
                Catch
                End Try
            End If
        End If
        Directory.CreateDirectory(target)
        Return target
    End Function

    Private Sub CopyTree(src As String, dst As String)
        Directory.CreateDirectory(dst)
        For Each f In Directory.GetFiles(src)
            File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), False)
        Next
        For Each d In Directory.GetDirectories(src)
            Dim name = Path.GetFileName(d)
            If name.Equals("webview", StringComparison.OrdinalIgnoreCase) Then Continue For   ' browser cache: rebuilds itself
            CopyTree(d, Path.Combine(dst, name))
        Next
    End Sub

    ''' <summary>Large, machine-specific data: engine binaries, models (%LocalAppData%\LocalMind) — not roamed.</summary>
    Public ReadOnly Property LocalData As String
        Get
            SyncLock _lock
                If _local Is Nothing Then _local = Resolve(Environment.SpecialFolder.LocalApplicationData)
                Return _local
            End SyncLock
        End Get
    End Property

    ''' <summary>The LocalMind folder under %AppData% / %LocalAppData% (used when the exe's folder is read-only).</summary>
    Private Function Resolve(root As Environment.SpecialFolder) As String
        Dim current = Path.Combine(Environment.GetFolderPath(root), AppFolderName)
        Directory.CreateDirectory(current)
        Return current
    End Function
    '────────────────────────────────────────────────────────────
    '  Portable folders next to LocalMind.exe
    '────────────────────────────────────────────────────────────
    Private _engineDir As String
    Private _modelsDir As String

    ''' <summary>Folder that contains LocalMind.exe.</summary>
    Public ReadOnly Property AppDir As String
        Get
            Dim p = Environment.ProcessPath
            Return If(String.IsNullOrEmpty(p), AppContext.BaseDirectory, Path.GetDirectoryName(p))
        End Get
    End Property

    ''' <summary>&lt;exe folder&gt;\Engine — the llama.cpp engine.</summary>
    Public ReadOnly Property EngineFolder As String
        Get
            SyncLock _lock
                If _engineDir Is Nothing Then _engineDir = PortableFolder("Engine", "engine")
                Return _engineDir
            End SyncLock
        End Get
    End Property

    ''' <summary>&lt;exe folder&gt;\Models — default place for downloaded models.</summary>
    Public ReadOnly Property DefaultModelsFolder As String
        Get
            SyncLock _lock
                If _modelsDir Is Nothing Then _modelsDir = PortableFolder("Models", "models")
                Return _modelsDir
            End SyncLock
        End Get
    End Property

    ''' <summary>
    ''' Uses &lt;exe folder&gt;\name. Data from the previous location (%LocalAppData%\LocalMind\legacyName)
    ''' is moved over when it is on the same drive (instant). If the exe folder is read-only
    ''' (e.g. Program Files), falls back to %LocalAppData%\LocalMind\legacyName.
    ''' </summary>
    Private Function PortableFolder(name As String, legacyName As String) As String
        Dim fallback = Path.Combine(LocalData, legacyName)
        Dim target = Path.Combine(AppDir, name)
        If Not CanWrite(AppDir) Then
            Directory.CreateDirectory(fallback)
            Return fallback
        End If
        If Not Directory.Exists(target) AndAlso Directory.Exists(fallback) AndAlso
           Directory.EnumerateFileSystemEntries(fallback).Any() Then
            If String.Equals(Path.GetPathRoot(target), Path.GetPathRoot(fallback), StringComparison.OrdinalIgnoreCase) Then
                Try
                    Directory.Move(fallback, target)
                Catch
                    Return fallback   ' in use — keep working from the old place
                End Try
            ElseIf legacyName = "models" Then
                Return fallback       ' don't copy many GB across drives behind the user's back
            End If
        End If
        Directory.CreateDirectory(target)
        Return target
    End Function

    Private _canWriteCache As Boolean?

    Private Function CanWrite(dir As String) As Boolean
        If dir = AppDir AndAlso _canWriteCache.HasValue Then Return _canWriteCache.Value
        Dim ok = ProbeWrite(dir)
        If dir = AppDir Then _canWriteCache = ok
        Return ok
    End Function

    Private Function ProbeWrite(dir As String) As Boolean
        Try
            Dim probe = Path.Combine(dir, ".write-test-" & Guid.NewGuid().ToString("N"))
            File.WriteAllText(probe, "")
            File.Delete(probe)
            Return True
        Catch
            Return False
        End Try
    End Function
End Module


Public Class SettingsService

    Public ReadOnly FilePath As String

    Public Sub New()
        FilePath = Path.Combine(AppPaths.Roaming, "settings.json")
    End Sub

    Public Function Load() As AppSettings
        Try
            If File.Exists(FilePath) Then
                Dim json = File.ReadAllText(FilePath)
                Dim s = JsonConvert.DeserializeObject(Of AppSettings)(json)
                If s IsNot Nothing Then Return Sanitize(s)
            End If
        Catch
        End Try
        Return Sanitize(New AppSettings())
    End Function

    Private Shared Function Sanitize(s As AppSettings) As AppSettings
        If Not [Enum].IsDefined(s.Backend) Then s.Backend = BackendKind.BuiltIn     ' an option that no longer exists
        If String.IsNullOrWhiteSpace(s.LanPin) Then
            s.LanPin = Random.Shared.Next(100000, 999999).ToString()
        End If
        If s.HistoryMessages < 2 Then s.HistoryMessages = 30
        If s.ContextSize < 512 Then s.ContextSize = 16384
        If s.ParallelSlots < 1 Then s.ParallelSlots = 1
        If s.LanPort < 1 OrElse s.LanPort > 65535 Then s.LanPort = 8484
        Return s
    End Function

    Public Sub Save(settings As AppSettings)
        Try
            ' Write-then-replace so a crash mid-write never corrupts settings.
            Dim tmp = FilePath & ".tmp"
            File.WriteAllText(tmp, JsonConvert.SerializeObject(settings, Formatting.Indented))
            File.Move(tmp, FilePath, True)
        Catch
        End Try
    End Sub
End Class
