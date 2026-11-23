Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Net.Http
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks
Imports Newtonsoft.Json
Imports Newtonsoft.Json.Linq

''' <summary>Ollama backend (optional — the app no longer requires Ollama).</summary>
Public Class OllamaService
    Implements IChatBackend

    Private Shared ReadOnly _http As HttpClient = CreateClient()
    Public Property BaseUrl As String

    Public Sub New(Optional baseUrl As String = "http://localhost:11434")
        Me.BaseUrl = If(baseUrl, "http://localhost:11434").TrimEnd("/"c)
    End Sub

    Private Shared Function CreateClient() As HttpClient
        Dim handler As New SocketsHttpHandler() With {
            .AutomaticDecompression = Net.DecompressionMethods.All,
            .PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2)
        }
        Return New HttpClient(handler) With {.Timeout = Timeout.InfiniteTimeSpan}
    End Function

    Public ReadOnly Property DisplayName As String Implements IChatBackend.DisplayName
        Get
            Return "Ollama (" & BaseUrl & ")"
        End Get
    End Property

    Public Async Function ListModelsAsync() As Task(Of List(Of String)) Implements IChatBackend.ListModelsAsync
        Dim result As New List(Of String)
        Try
            Using cts As New CancellationTokenSource(TimeSpan.FromSeconds(10))
                Dim json = Await _http.GetStringAsync(BaseUrl & "/api/tags", cts.Token).ConfigureAwait(False)
                Dim resp = JsonConvert.DeserializeObject(Of OllamaTagsResponse)(json)
                If resp?.models IsNot Nothing Then
                    For Each m In resp.models
                        result.Add(If(m.name, m.model))
                    Next
                End If
            End Using
        Catch
        End Try
        Return result
    End Function

    Public Async Function IsOnlineAsync() As Task(Of Boolean) Implements IChatBackend.IsOnlineAsync
        Try
            Using cts As New CancellationTokenSource(TimeSpan.FromSeconds(3))
                Using r = Await _http.GetAsync(BaseUrl & "/api/tags", cts.Token).ConfigureAwait(False)
                    Return r.IsSuccessStatusCode
                End Using
            End Using
        Catch
            Return False
        End Try
    End Function

    Public Async Function StreamChatAsync(messages As List(Of ChatMessage),
                                         opts As ChatRequestOptions,
                                         onThinking As Action(Of String),
                                         onToken As Action(Of String),
                                         ct As CancellationToken) As Task(Of ChatResult) Implements IChatBackend.StreamChatAsync
        Dim payload = New JObject()
        payload("model") = opts.Model
        payload("stream") = True
        payload("think") = opts.EnableThinking
        Dim options As New JObject From {
            {"temperature", opts.Temperature},
            {"top_p", opts.TopP}
        }
        If opts.MaxTokens > 0 Then options("num_predict") = opts.MaxTokens
        payload("options") = options

        Dim msgArray As New JArray()
        For Each m In messages
            Dim mo As New JObject()
            mo("role") = m.Role
            mo("content") = If(m.Content, "")
            If m.Images IsNot Nothing AndAlso m.Images.Count > 0 Then
                mo("images") = New JArray(m.Images.ToArray())
            End If
            msgArray.Add(mo)
        Next
        payload("messages") = msgArray

        Dim result As New ChatResult()
        Dim full As New StringBuilder()
        Dim thinkingFull As New StringBuilder()
        Dim tokenCount = 0

        Using req As New HttpRequestMessage(HttpMethod.Post, BaseUrl & "/api/chat")
            req.Content = New StringContent(payload.ToString(Formatting.None), Encoding.UTF8, "application/json")
            Using resp = Await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(False)
                If Not resp.IsSuccessStatusCode Then
                    Dim body = Await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(False)
                    Throw New HttpRequestException($"Ollama returned {CInt(resp.StatusCode)}: {ExtractError(body)}")
                End If
                Using stream = Await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(False)
                    Using reader As New StreamReader(stream)
                        Do
                            Dim line = Await reader.ReadLineAsync(ct).ConfigureAwait(False)
                            If line Is Nothing Then Exit Do
                            If String.IsNullOrWhiteSpace(line) Then Continue Do

                            Dim chunk As OllamaChatChunk
                            Try
                                chunk = JsonConvert.DeserializeObject(Of OllamaChatChunk)(line)
                            Catch
                                Continue Do
                            End Try
                            If chunk Is Nothing Then Continue Do

                            Dim thinkTok = chunk.message?.Thinking
                            If Not String.IsNullOrEmpty(thinkTok) Then
                                thinkingFull.Append(thinkTok)
                                onThinking(thinkTok)
                            End If

                            Dim tok = chunk.message?.Content
                            If Not String.IsNullOrEmpty(tok) Then
                                full.Append(tok)
                                tokenCount += 1
                                onToken(tok)
                            End If

                            If chunk.done Then
                                If chunk.eval_count > 0 Then tokenCount = chunk.eval_count
                                If chunk.eval_duration > 0 Then
                                    result.EngineTokensPerSecond = chunk.eval_count / (chunk.eval_duration / 1.0E+9)
                                End If
                                Exit Do
                            End If
                        Loop
                    End Using
                End Using
            End Using
        End Using

        result.Content = full.ToString()
        result.Thinking = thinkingFull.ToString()
        result.Tokens = tokenCount
        Return result
    End Function

    Private Shared Function ExtractError(body As String) As String
        Try
            Dim o = JObject.Parse(body)
            Return If(o("error")?.ToString(), body)
        Catch
            Return body
        End Try
    End Function
End Class
