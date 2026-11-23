Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Net.Http
Imports System.Net.Http.Headers
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks
Imports Newtonsoft.Json
Imports Newtonsoft.Json.Linq

''' <summary>
''' Streaming client for any OpenAI-compatible /v1/chat/completions server.
''' Used for the built-in llama.cpp engine and for external servers
''' (LM Studio, vLLM, Jan, another PC running this app with LAN sharing on…).
''' </summary>
Public Class OpenAiCompatService
    Implements IChatBackend

    Friend Shared ReadOnly Http As HttpClient = CreateClient()

    Public Property BaseUrl As String
    Public Property ApiKey As String
    Public Property Name As String = "OpenAI-compatible"
    ''' <summary>Send llama.cpp-specific fields (thinking toggle). Strict servers may reject them.</summary>
    Public Property SendLlamaExtras As Boolean = True

    Public Sub New(baseUrl As String, Optional apiKey As String = Nothing)
        Me.BaseUrl = NormalizeBase(baseUrl)
        Me.ApiKey = apiKey
    End Sub

    Private Shared Function CreateClient() As HttpClient
        Dim handler As New SocketsHttpHandler() With {
            .AutomaticDecompression = Net.DecompressionMethods.All,
            .PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2)
        }
        Return New HttpClient(handler) With {.Timeout = Timeout.InfiniteTimeSpan}
    End Function

    ''' <summary>Accepts "http://host:port", ".../v1" or ".../v1/" and returns ".../v1".</summary>
    Public Shared Function NormalizeBase(url As String) As String
        Dim u = If(url, "").Trim().TrimEnd("/"c)
        If u.Length = 0 Then Return u
        If Not u.StartsWith("http", StringComparison.OrdinalIgnoreCase) Then u = "http://" & u
        If Not u.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) Then u &= "/v1"
        Return u
    End Function

    Public ReadOnly Property DisplayName As String Implements IChatBackend.DisplayName
        Get
            Return Name & " (" & BaseUrl & ")"
        End Get
    End Property

    Private Sub AddAuth(req As HttpRequestMessage)
        If Not String.IsNullOrWhiteSpace(ApiKey) Then
            req.Headers.Authorization = New AuthenticationHeaderValue("Bearer", ApiKey.Trim())
        End If
    End Sub

    Public Async Function IsOnlineAsync() As Task(Of Boolean) Implements IChatBackend.IsOnlineAsync
        Try
            Using cts As New CancellationTokenSource(TimeSpan.FromSeconds(3))
                Using req As New HttpRequestMessage(HttpMethod.Get, BaseUrl & "/models")
                    AddAuth(req)
                    Using r = Await Http.SendAsync(req, cts.Token).ConfigureAwait(False)
                        Return r.IsSuccessStatusCode
                    End Using
                End Using
            End Using
        Catch
            Return False
        End Try
    End Function

    Public Async Function ListModelsAsync() As Task(Of List(Of String)) Implements IChatBackend.ListModelsAsync
        Dim list As New List(Of String)
        Try
            Using cts As New CancellationTokenSource(TimeSpan.FromSeconds(10))
                Using req As New HttpRequestMessage(HttpMethod.Get, BaseUrl & "/models")
                    AddAuth(req)
                    Using r = Await Http.SendAsync(req, cts.Token).ConfigureAwait(False)
                        If Not r.IsSuccessStatusCode Then Return list
                        Dim o = JObject.Parse(Await r.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(False))
                        Dim data = TryCast(o("data"), JArray)
                        If data IsNot Nothing Then
                            For Each m In data
                                Dim id = m("id")?.ToString()
                                If Not String.IsNullOrEmpty(id) Then list.Add(id)
                            Next
                        End If
                    End Using
                End Using
            End Using
        Catch
        End Try
        Return list
    End Function

    ''' <summary>Builds the OpenAI "messages" array, converting base64 images to data URLs.</summary>
    Public Shared Function BuildMessages(messages As List(Of ChatMessage)) As JArray
        Dim arr As New JArray()
        For Each m In messages
            Dim mo As New JObject()
            mo("role") = m.Role
            If m.Images IsNot Nothing AndAlso m.Images.Count > 0 Then
                Dim parts As New JArray()
                If Not String.IsNullOrEmpty(m.Content) Then
                    parts.Add(New JObject From {{"type", "text"}, {"text", m.Content}})
                End If
                For Each img In m.Images
                    parts.Add(New JObject From {
                        {"type", "image_url"},
                        {"image_url", New JObject From {{"url", ImageUtil.ToDataUrl(img)}}}
                    })
                Next
                mo("content") = parts
            Else
                mo("content") = If(m.Content, "")
            End If
            arr.Add(mo)
        Next
        Return arr
    End Function

    Public Async Function StreamChatAsync(messages As List(Of ChatMessage),
                                         opts As ChatRequestOptions,
                                         onThinking As Action(Of String),
                                         onToken As Action(Of String),
                                         ct As CancellationToken) As Task(Of ChatResult) Implements IChatBackend.StreamChatAsync
        Dim payload As New JObject()
        payload("model") = If(opts.Model, "default")
        payload("stream") = True
        payload("temperature") = opts.Temperature
        payload("top_p") = opts.TopP
        If opts.MaxTokens > 0 Then payload("max_tokens") = opts.MaxTokens
        payload("messages") = BuildMessages(messages)
        payload("stream_options") = New JObject From {{"include_usage", True}}
        If opts.Tools IsNot Nothing AndAlso opts.Tools.Count > 0 Then
            payload("tools") = opts.Tools
            payload("tool_choice") = "auto"
        End If
        If SendLlamaExtras Then
            ' Hybrid-thinking models (Qwen3.x, GLM, DeepSeek…) read this flag from their chat template.
            payload("chat_template_kwargs") = New JObject From {{"enable_thinking", opts.EnableThinking}}
            If Not opts.EnableThinking Then payload("reasoning_effort") = "low"
        End If

        Dim result As New ChatResult()
        Dim content As New StringBuilder()
        Dim thinking As New StringBuilder()
        Dim count = 0

        Dim splitter As New ThinkTagSplitter(
            Sub(t)
                thinking.Append(t)
                onThinking(t)
            End Sub,
            Sub(t)
                content.Append(t)
                onToken(t)
            End Sub)

        Using req As New HttpRequestMessage(HttpMethod.Post, BaseUrl & "/chat/completions")
            AddAuth(req)
            req.Content = New StringContent(payload.ToString(Formatting.None), Encoding.UTF8, "application/json")
            Using resp = Await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(False)
                If Not resp.IsSuccessStatusCode Then
                    Dim body = Await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(False)
                    Throw New HttpRequestException($"Server returned {CInt(resp.StatusCode)}: {ExtractError(body)}")
                End If

                Using stream = Await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(False)
                    Using reader As New StreamReader(stream, Encoding.UTF8)
                        Do
                            Dim line = Await reader.ReadLineAsync(ct).ConfigureAwait(False)
                            If line Is Nothing Then Exit Do
                            If Not line.StartsWith("data:", StringComparison.Ordinal) Then Continue Do
                            Dim data = line.Substring(5).Trim()
                            If data = "[DONE]" Then Exit Do

                            Dim o As JObject
                            Try
                                o = JObject.Parse(data)
                            Catch
                                Continue Do
                            End Try

                            If o("error") IsNot Nothing Then
                                Throw New HttpRequestException(If(o("error")("message")?.ToString(), o("error").ToString()))
                            End If

                            Dim choices = TryCast(o("choices"), JArray)
                            If choices IsNot Nothing AndAlso choices.Count > 0 Then
                                Dim delta = choices(0)("delta")
                                If delta IsNot Nothing AndAlso delta.Type = JTokenType.Object Then
                                    Dim r = If(delta("reasoning_content")?.ToString(), delta("reasoning")?.ToString())
                                    If Not String.IsNullOrEmpty(r) Then
                                        thinking.Append(r)
                                        onThinking(r)
                                    End If
                                    Dim tcs = TryCast(delta("tool_calls"), JArray)
                                    If tcs IsNot Nothing Then
                                        For Each tc In tcs
                                            Dim idx = If(tc.Value(Of Integer?)("index"), result.ToolCalls.Count)
                                            While result.ToolCalls.Count <= idx
                                                result.ToolCalls.Add(New ToolCall())
                                            End While
                                            Dim fn = tc("function")
                                            If fn IsNot Nothing Then
                                                result.ToolCalls(idx).Name &= If(fn.Value(Of String)("name"), "")
                                                result.ToolCalls(idx).Arguments &= If(fn.Value(Of String)("arguments"), "")
                                            End If
                                        Next
                                    End If
                                    Dim c = delta("content")
                                    If c IsNot Nothing AndAlso c.Type = JTokenType.String Then
                                        Dim s = c.ToString()
                                        If s.Length > 0 Then
                                            count += 1
                                            splitter.Feed(s)
                                        End If
                                    End If
                                End If
                            End If

                            Dim usage = o("usage")
                            If usage IsNot Nothing AndAlso usage.Type = JTokenType.Object Then
                                Dim ct2 = usage("completion_tokens")
                                If ct2 IsNot Nothing Then count = CInt(ct2)
                            End If
                            ' llama.cpp extension: exact generation speed
                            Dim timings = o("timings")
                            If timings IsNot Nothing AndAlso timings.Type = JTokenType.Object Then
                                Dim pps = timings("predicted_per_second")
                                If pps IsNot Nothing Then result.EngineTokensPerSecond = CDbl(pps)
                                Dim prps = timings("prompt_per_second")
                                If prps IsNot Nothing Then result.PromptTokensPerSecond = CDbl(prps)
                            End If
                        Loop
                    End Using
                End Using
            End Using
        End Using

        splitter.Flush()
        result.Content = content.ToString()
        result.Thinking = thinking.ToString()
        result.Tokens = count
        Return result
    End Function

    Friend Shared Function ExtractError(body As String) As String
        Try
            Dim o = JObject.Parse(body)
            Dim e = o("error")
            If e Is Nothing Then Return body
            If e.Type = JTokenType.Object Then Return If(e("message")?.ToString(), e.ToString())
            Return e.ToString()
        Catch
            Return If(body?.Length > 400, body.Substring(0, 400), body)
        End Try
    End Function
End Class

Public Module ImageUtil
    ''' <summary>Detects the image type from its magic bytes and returns a data: URL.</summary>
    Public Function ToDataUrl(base64 As String) As String
        If base64 Is Nothing Then Return ""
        If base64.StartsWith("data:", StringComparison.OrdinalIgnoreCase) Then Return base64
        Return "data:" & MimeFromBase64(base64) & ";base64," & base64
    End Function

    Public Function MimeFromBase64(b64 As String) As String
        If b64.StartsWith("iVBOR") Then Return "image/png"
        If b64.StartsWith("/9j/") Then Return "image/jpeg"
        If b64.StartsWith("R0lGOD") Then Return "image/gif"
        If b64.StartsWith("UklGR") Then Return "image/webp"
        If b64.StartsWith("Qk") Then Return "image/bmp"
        Return "image/png"
    End Function

    ''' <summary>Strips a "data:...;base64," prefix if present.</summary>
    Public Function StripDataUrl(s As String) As String
        If s Is Nothing Then Return Nothing
        Dim i = s.IndexOf(";base64,", StringComparison.OrdinalIgnoreCase)
        If s.StartsWith("data:", StringComparison.OrdinalIgnoreCase) AndAlso i > 0 Then Return s.Substring(i + 8)
        Return s
    End Function
End Module
