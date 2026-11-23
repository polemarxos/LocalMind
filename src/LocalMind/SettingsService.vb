Imports System
Imports System.IO
Imports Newtonsoft.Json

Public Enum BackendKind
    BuiltIn = 0      ' bundled llama.cpp engine — no Ollama needed
    Ollama = 1
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
    Public Property OllamaUrl As String = "http://localhost:11434"
    Public Property OpenAiUrl As String = "http://localhost:1234"
    Public Property OpenAiKey As String = ""

    ' ── Built-in engine (llama.cpp) ──
    Public Property EngineVariant As String = "auto"   ' auto | cuda12 | cuda13 | vulkan | cpu | rocm
    Public Property ModelsFolder As String = ""        ' empty = default under LocalAppData
    Public Property ContextSize As Integer = 16384
    Public Property GpuLayers As String = "auto"       ' auto | all | number
    Public Property ParallelSlots As Integer = 1
    Public Property KvCacheType As String = "f16"      ' f16 | q8_0 | q4_0
    Public Property ExtraEngineArgs As String = ""
    Public Property UnloadAfterIdleMinutes As Integer = 0  ' 0 = never
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

    ''' <summary>True when settings.json came from v1 (Ollama-only) — used to offer the built-in engine once.</summary>
    <JsonIgnore> Public Property UpgradedFromV1 As Boolean

    <JsonIgnore>
    Public ReadOnly Property EffectiveModelsFolder As String
        Get
            If Not String.IsNullOrWhiteSpace(ModelsFolder) Then Return ModelsFolder
            Return Path.Combine(AppPaths.LocalData, "models")
        End Get
    End Property
End Class

''' <summary>Central place for every on-disk location the app uses.</summary>
Public Module AppPaths
    Public Const AppFolderName As String = "LocalMind"
    Private Const LegacyFolderName As String = "OllamaChat"   ' name used before the rename

    Private _roaming As String
    Private _local As String
    Private ReadOnly _lock As New Object()

    ''' <summary>Settings, chats, personas (%AppData%\LocalMind).</summary>
    Public ReadOnly Property Roaming As String
        Get
            SyncLock _lock
                If _roaming Is Nothing Then _roaming = Resolve(Environment.SpecialFolder.ApplicationData)
                Return _roaming
            End SyncLock
        End Get
    End Property

    ''' <summary>Large, machine-specific data: engine binaries, models (%LocalAppData%\LocalMind) — not roamed.</summary>
    Public ReadOnly Property LocalData As String
        Get
            SyncLock _lock
                If _local Is Nothing Then _local = Resolve(Environment.SpecialFolder.LocalApplicationData)
                Return _local
            End SyncLock
        End Get
    End Property

    ''' <summary>
    ''' Returns the LocalMind folder, moving the old "OllamaChat" folder there on first run so
    ''' settings, chats, the engine and downloaded models carry over. A move on the same drive is
    ''' instant even for many GB of models. If the move is impossible (e.g. files in use), the old
    ''' folder keeps being used rather than copying gigabytes.
    ''' </summary>
    Private Function Resolve(root As Environment.SpecialFolder) As String
        Dim baseDir = Environment.GetFolderPath(root)
        Dim current = Path.Combine(baseDir, AppFolderName)
        Dim legacy = Path.Combine(baseDir, LegacyFolderName)
        If Not Directory.Exists(current) AndAlso Directory.Exists(legacy) Then
            Try
                Directory.Move(legacy, current)
            Catch
                Return legacy
            End Try
        End If
        Directory.CreateDirectory(current)
        Return current
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
                If s IsNot Nothing Then
                    If Not json.Contains("""Backend""") Then
                        ' v1 settings: keep Ollama working exactly as before until the user decides.
                        s.UpgradedFromV1 = True
                        s.Backend = BackendKind.Ollama
                    End If
                    Return Sanitize(s)
                End If
            End If
        Catch
        End Try
        Return Sanitize(New AppSettings())
    End Function

    Private Shared Function Sanitize(s As AppSettings) As AppSettings
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
