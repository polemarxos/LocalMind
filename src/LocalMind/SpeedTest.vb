Imports System
Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.IO
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks
Imports Newtonsoft.Json

Public Class SpeedResult
    Public Property GenTokensPerSecond As Double
    Public Property PromptTokensPerSecond As Double
    Public Property LoadSeconds As Double
    Public Property ContextTokens As Integer
    Public Property TestedAt As DateTime
    Public Property Hardware As String

    Public Overrides Function ToString() As String
        Return $"{GenTokensPerSecond:0.0} tok/s"
    End Function
End Class

''' <summary>Measures how fast each installed model runs on this PC (built-in engine).</summary>
Public Module SpeedTest

    Private ReadOnly _lock As New Object()
    Private _results As Dictionary(Of String, SpeedResult)

    Private ReadOnly Property FilePath As String
        Get
            Return Path.Combine(AppPaths.Roaming, "speedtests.json")
        End Get
    End Property

    Private Function Key(m As LocalModel) As String
        Return Path.GetFileName(m.MainFile).ToLowerInvariant()
    End Function

    Public Function GetResult(m As LocalModel) As SpeedResult
        SyncLock _lock
            Load()
            Dim r As SpeedResult = Nothing
            Return If(_results.TryGetValue(Key(m), r), r, Nothing)
        End SyncLock
    End Function

    Private Sub Load()
        If _results IsNot Nothing Then Return
        Try
            If File.Exists(FilePath) Then _results = JsonConvert.DeserializeObject(Of Dictionary(Of String, SpeedResult))(File.ReadAllText(FilePath))
        Catch
        End Try
        If _results Is Nothing Then _results = New Dictionary(Of String, SpeedResult)()
    End Sub

    Private Sub Save()
        Try
            File.WriteAllText(FilePath, JsonConvert.SerializeObject(_results, Formatting.Indented))
        Catch
        End Try
    End Sub

    ''' <summary>Loads the model (timed), feeds a ~500-token prompt and generates 128 tokens.</summary>
    Public Async Function RunAsync(m As LocalModel, ct As CancellationToken) As Task(Of SpeedResult)
        Dim sw = Stopwatch.StartNew()
        Dim alreadyLoaded = AppCore.Engine.CurrentModel IsNot Nothing AndAlso
                            String.Equals(AppCore.Engine.CurrentModel.MainFile, m.MainFile, StringComparison.OrdinalIgnoreCase)
        Using Await AppCore.Engine.AcquireAsync(m, ct)
            Dim loadSecs = If(alreadyLoaded, AppCore.Engine.LastLoadSeconds, sw.Elapsed.TotalSeconds)

            Dim text As New StringBuilder("Summarize the following notes in three bullet points." & vbLf)
            For i = 1 To 20
                text.Append($"Note {i}: the quarterly review covered budgets, hiring plans, customer feedback, product delays and the new office move. ")
            Next
            Dim client As New OpenAiCompatService(AppCore.Engine.BaseUrl) With {.SendLlamaExtras = True}
            Dim r = Await client.StreamChatAsync(
                New List(Of ChatMessage) From {New ChatMessage With {.Role = "user", .Content = text.ToString()}},
                New ChatRequestOptions With {.Model = m.DisplayName, .Temperature = 0, .MaxTokens = 128, .EnableThinking = False},
                Sub(t) Return, Sub(t) Return, ct)

            Dim res As New SpeedResult With {
                .GenTokensPerSecond = Math.Round(r.EngineTokensPerSecond, 1),
                .PromptTokensPerSecond = Math.Round(r.PromptTokensPerSecond, 0),
                .LoadSeconds = Math.Round(loadSecs, 1),
                .ContextTokens = AppCore.Engine.LoadedContext,
                .TestedAt = DateTime.Now,
                .Hardware = HardwareInfo.Current().Summary
            }
            SyncLock _lock
                Load()
                _results(Key(m)) = res
                Save()
            End SyncLock
            Return res
        End Using
    End Function
End Module

''' <summary>How long the last pictures, videos and songs took per model, and the chat speed seen in real answers.</summary>
Public Module JobTimes
    Public Class Entry
        Public Property Seconds As Double
        Public Property Detail As String
        Public Property At As DateTime
    End Class

    Private ReadOnly _lock As New Object()
    Private _items As Dictionary(Of String, Entry)

    Private ReadOnly Property FilePath As String
        Get
            Return Path.Combine(AppPaths.Roaming, "jobtimes.json")
        End Get
    End Property

    Private Sub Load()
        If _items IsNot Nothing Then Return
        Try
            If File.Exists(FilePath) Then _items = JsonConvert.DeserializeObject(Of Dictionary(Of String, Entry))(File.ReadAllText(FilePath))
        Catch
        End Try
        If _items Is Nothing Then _items = New Dictionary(Of String, Entry)(StringComparer.OrdinalIgnoreCase)
    End Sub

    ''' <summary>Remembers one run (key: "media:&lt;id&gt;", "audio:&lt;id&gt;" or "chat:&lt;model&gt;").</summary>
    Public Sub Record(key As String, seconds As Double, detail As String)
        SyncLock _lock
            Load()
            _items(key) = New Entry With {.Seconds = Math.Round(seconds, 1), .Detail = detail, .At = DateTime.Now}
            Try
                File.WriteAllText(FilePath, JsonConvert.SerializeObject(_items, Formatting.Indented))
            Catch
            End Try
        End SyncLock
    End Sub

    Public Function GetEntry(key As String) As Entry
        SyncLock _lock
            Load()
            Dim e As Entry = Nothing
            Return If(_items.TryGetValue(key, e), e, Nothing)
        End SyncLock
    End Function

    Public Function FormatSeconds(s As Double) As String
        If s >= 3600 Then Return $"{CInt(Math.Floor(s / 3600))}h {CInt(Math.Floor(s Mod 3600 / 60))}m"
        If s >= 60 Then Return $"{CInt(Math.Floor(s / 60))}m {CInt(Math.Floor(s Mod 60)):00}s"
        Return If(s < 10, $"{s:0.0}s", $"{CInt(Math.Floor(s))}s")
    End Function
End Module
