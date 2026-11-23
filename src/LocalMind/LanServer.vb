Imports System
Imports System.Collections.Concurrent
Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.IO
Imports System.Linq
Imports System.Net
Imports System.Net.Http
Imports System.Net.NetworkInformation
Imports System.Net.Sockets
Imports System.Reflection
Imports System.Security.Cryptography
Imports System.Text
Imports System.Threading
Imports System.Threading.Channels
Imports System.Threading.Tasks
Imports Microsoft.AspNetCore.Builder
Imports Microsoft.AspNetCore.Hosting
Imports Microsoft.AspNetCore.Http
Imports Microsoft.Extensions.Logging
Imports Newtonsoft.Json
Imports Newtonsoft.Json.Linq

''' <summary>
''' Lets phones, tablets and other PCs on the same network use this app:
'''   • "/"      – mobile-friendly chat page (shares chat history with the desktop app)
'''   • "/api/*" – JSON + Server-Sent-Events API used by that page
'''   • "/v1/*"  – OpenAI-compatible API (for other apps: Continue, Open WebUI, scripts…)
''' Everything except the page itself requires the PIN.
''' </summary>
Public Class LanServer

    Private _app As WebApplication
    Private ReadOnly _failures As New ConcurrentDictionary(Of String, (Count As Integer, LockedUntil As DateTime))()
    Private _activeClients As Integer

    Public Event Activity(text As String)

    Public ReadOnly Property IsRunning As Boolean
        Get
            Return _app IsNot Nothing
        End Get
    End Property

    Public ReadOnly Property Port As Integer
    Public ReadOnly Property ActiveClients As Integer
        Get
            Return _activeClients
        End Get
    End Property

    '════════════════════════════════════════════════════════════
    '  Start / stop
    '════════════════════════════════════════════════════════════
    Public Async Function StartAsync(port As Integer) As Task
        If _app IsNot Nothing Then Await StopAsync()

        Dim builder = WebApplication.CreateSlimBuilder(New WebApplicationOptions With {
            .ContentRootPath = AppContext.BaseDirectory, .Args = Array.Empty(Of String)()})
        builder.Logging.ClearProviders()
        builder.WebHost.ConfigureKestrel(
            Sub(o)
                o.ListenAnyIP(port)
                o.Limits.MaxRequestBodySize = 80L * 1024 * 1024   ' photos from phones
                o.AddServerHeader = False
            End Sub)

        Dim app = builder.Build()
        RunExtensions.Run(app, New RequestDelegate(AddressOf HandleAsync))
        Dim startError As Exception = Nothing
        Try
            Await app.StartAsync()
        Catch ex As Exception
            startError = ex
        End Try
        If startError IsNot Nothing Then
            Await app.DisposeAsync()
            If TypeOf startError Is IOException Then
                Throw New Exception($"Port {port} is already in use. Choose another port.", startError)
            End If
            Throw startError
        End If
        _app = app
        _Port = port
        Log($"LAN server started on port {port}")
    End Function

    Public Async Function StopAsync() As Task
        Dim app = _app
        _app = Nothing
        If app Is Nothing Then Return
        Try
            Await app.StopAsync(New CancellationTokenSource(3000).Token)
        Catch
        End Try
        Await app.DisposeAsync()
        Log("LAN server stopped")
    End Function

    Private Sub Log(text As String)
        RaiseEvent Activity(DateTime.Now.ToString("HH:mm:ss") & "  " & text)
    End Sub

    '════════════════════════════════════════════════════════════
    '  Addresses / firewall
    '════════════════════════════════════════════════════════════
    ''' <summary>The URLs other devices on the network can use, best first.</summary>
    Public Shared Function LocalUrls(port As Integer) As List(Of String)
        Dim ips As New List(Of IPAddress)
        Try
            For Each nic In NetworkInterface.GetAllNetworkInterfaces()
                If nic.OperationalStatus <> OperationalStatus.Up Then Continue For
                If nic.NetworkInterfaceType = NetworkInterfaceType.Loopback OrElse
                   nic.NetworkInterfaceType = NetworkInterfaceType.Tunnel Then Continue For
                Dim desc = (nic.Description & " " & nic.Name).ToLowerInvariant()
                If desc.Contains("virtual") OrElse desc.Contains("vethernet") OrElse desc.Contains("hyper-v") OrElse
                   desc.Contains("vmware") OrElse desc.Contains("virtualbox") OrElse desc.Contains("wsl") Then Continue For
                For Each ua In nic.GetIPProperties().UnicastAddresses
                    If ua.Address.AddressFamily <> AddressFamily.InterNetwork Then Continue For
                    Dim b = ua.Address.GetAddressBytes()
                    If b(0) = 169 AndAlso b(1) = 254 Then Continue For
                    ips.Add(ua.Address)
                Next
            Next
        Catch
        End Try
        Dim rank = Function(ip As IPAddress)
                       Dim b = ip.GetAddressBytes()
                       If b(0) = 192 AndAlso b(1) = 168 Then Return 0
                       If b(0) = 10 Then Return 1
                       If b(0) = 172 AndAlso b(1) >= 16 AndAlso b(1) <= 31 Then Return 2
                       Return 3
                   End Function
        Return ips.Distinct().OrderBy(rank).Select(Function(ip) $"http://{ip}:{port}/").ToList()
    End Function

    ''' <summary>Adds an inbound Windows Firewall rule for the port (asks for admin rights).</summary>
    Public Shared Function AddFirewallRule(port As Integer) As Boolean
        Try
            Const RuleName = "LocalMind LAN"
            Dim cmd = $"/c netsh advfirewall firewall delete rule name=""{RuleName}"" >nul 2>&1 & " &
                      $"netsh advfirewall firewall add rule name=""{RuleName}"" dir=in action=allow protocol=TCP localport={port} profile=private,domain"
            Dim psi As New ProcessStartInfo("cmd.exe", cmd) With {
                .UseShellExecute = True, .Verb = "runas", .WindowStyle = ProcessWindowStyle.Hidden}
            Using p = Process.Start(psi)
                p.WaitForExit(20000)
                Return p.ExitCode = 0
            End Using
        Catch
            Return False   ' user declined the UAC prompt
        End Try
    End Function

    '════════════════════════════════════════════════════════════
    '  Request routing
    '════════════════════════════════════════════════════════════
    Private Async Function HandleAsync(ctx As HttpContext) As Task
        Dim path = If(ctx.Request.Path.Value, "/")
        Dim method = ctx.Request.Method.ToUpperInvariant()
        ctx.Response.Headers("X-Content-Type-Options") = "nosniff"
        Dim failure As Exception = Nothing

        ' CORS for /v1 so browser-based clients on the LAN can use the API.
        If path.StartsWith("/v1/", StringComparison.OrdinalIgnoreCase) Then
            ctx.Response.Headers("Access-Control-Allow-Origin") = "*"
            ctx.Response.Headers("Access-Control-Allow-Headers") = "Authorization, Content-Type"
            ctx.Response.Headers("Access-Control-Allow-Methods") = "GET, POST, OPTIONS"
            If method = "OPTIONS" Then
                ctx.Response.StatusCode = 204
                Return
            End If
        End If

        Try
            Select Case True
                Case path = "/" OrElse path = "/index.html"
                    Await ServeResourceAsync(ctx, "webui.html", "text/html; charset=utf-8")
                    Return
                Case path = "/assets/hljs.js"
                    Await ServeResourceAsync(ctx, "hljs.min.js", "application/javascript; charset=utf-8", cache:=True)
                    Return
                Case path = "/assets/katex.js"
                    Await ServeResourceAsync(ctx, "katex.min.js", "application/javascript; charset=utf-8", cache:=True)
                    Return
                Case path = "/assets/katex.css"
                    Await ServeResourceAsync(ctx, "katex.min.css", "text/css; charset=utf-8", cache:=True)
                    Return
                Case path = "/assets/hljs.css"
                    Await ServeResourceAsync(ctx, "hljs-dark.min.css", "text/css; charset=utf-8", cache:=True)
                    Return
                Case path = "/api/login" AndAlso method = "POST"
                    If Await CheckAuthAsync(ctx) Then Await WriteJsonAsync(ctx, New JObject From {{"ok", True}})
                    Return
            End Select

            If path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) Then
                If Not Await CheckAuthAsync(ctx) Then Return
                Await HandleApiAsync(ctx, path, method)
                Return
            End If

            If path.StartsWith("/v1/", StringComparison.OrdinalIgnoreCase) Then
                If Not AppCore.Settings.LanOpenAiApi Then
                    Await WriteErrorAsync(ctx, 404, "The OpenAI-compatible API is disabled on this PC.")
                    Return
                End If
                If Not Await CheckAuthAsync(ctx) Then Return
                Await HandleOpenAiAsync(ctx, path, method)
                Return
            End If

            Await WriteErrorAsync(ctx, 404, "Not found")
            Return
        Catch ex As OperationCanceledException
            Return   ' client disconnected
        Catch ex As Exception
            failure = ex
        End Try
        If failure IsNot Nothing AndAlso Not ctx.Response.HasStarted Then
            Await WriteErrorAsync(ctx, 500, failure.Message)
        End If
    End Function

    '════════════════════════════════════════════════════════════
    '  Auth (PIN) with brute-force lockout
    '════════════════════════════════════════════════════════════
    Private Async Function CheckAuthAsync(ctx As HttpContext) As Task(Of Boolean)
        Dim s = AppCore.Settings
        If Not s.LanRequirePin Then Return True

        Dim ip = If(ctx.Connection.RemoteIpAddress?.ToString(), "?")
        Dim f As (Count As Integer, LockedUntil As DateTime) = Nothing
        If _failures.TryGetValue(ip, f) AndAlso f.LockedUntil > DateTime.Now Then
            Await WriteErrorAsync(ctx, 429, "Too many wrong PINs. Try again in a few minutes.")
            Return False
        End If

        Dim given As String = ctx.Request.Headers("X-Pin").ToString()
        If String.IsNullOrEmpty(given) Then
            Dim auth = ctx.Request.Headers("Authorization").ToString()
            If auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) Then given = auth.Substring(7).Trim()
        End If
        If String.IsNullOrEmpty(given) Then given = ctx.Request.Query("pin").ToString()

        Dim expected = Encoding.UTF8.GetBytes(If(s.LanPin, ""))
        Dim actual = Encoding.UTF8.GetBytes(If(given, ""))
        If expected.Length > 0 AndAlso CryptographicOperations.FixedTimeEquals(expected, actual) Then
            _failures.TryRemove(ip, f)
            Return True
        End If

        Dim n = If(_failures.TryGetValue(ip, f), f.Count, 0) + 1
        _failures(ip) = (n, If(n >= 8, DateTime.Now.AddMinutes(5), DateTime.MinValue))
        If n >= 8 Then Log($"Blocked {ip} for 5 minutes after {n} wrong PINs")
        Await WriteErrorAsync(ctx, 401, "Wrong or missing PIN.")
        Return False
    End Function

    '════════════════════════════════════════════════════════════
    '  /api — used by the built-in web page
    '════════════════════════════════════════════════════════════
    Private Async Function HandleApiAsync(ctx As HttpContext, path As String, method As String) As Task
        If path = "/api/info" Then
            Dim models As New JArray()
            If AppCore.Settings.Backend = BackendKind.BuiltIn Then
                For Each m In AppCore.Library.All()
                    models.Add(New JObject From {{"name", m.DisplayName}, {"vision", m.IsVision}})
                Next
            Else
                For Each name In Await AppCore.Backend.ListModelsAsync()
                    models.Add(New JObject From {{"name", name}, {"vision", True}})
                Next
            End If
            Dim personas As New JArray(AppCore.Personas.Load().Select(Function(p) p.Name).ToArray())
            Await WriteJsonAsync(ctx, New JObject From {
                {"app", "LocalMind"},
                {"backend", AppCore.Backend.DisplayName},
                {"models", models},
                {"current", If(AppCore.Settings.LastModel, "")},
                {"persona", If(AppCore.Settings.LastPersona, "")},
                {"personas", personas},
                {"thinking", AppCore.Settings.ShowThinking},
                {"features", New JObject From {
                    {"images", AppCore.Settings.EnableImages AndAlso ChatAgent.PickImageRecipe(False) IsNot Nothing},
                    {"voiceIn", OperatingSystem.IsWindows()},
                    {"speak", True}}}
            })
            Return
        End If

        ' Pictures, songs and videos made on the PC (shown in the phone's chat).
        If path.StartsWith("/api/outputs/") AndAlso method = "GET" Then
            Dim name = Uri.UnescapeDataString(path.Substring("/api/outputs/".Length))
            Dim file = IO.Path.Combine(MediaStudio.OutputFolder, IO.Path.GetFileName(name))
            If name.Contains("/"c) OrElse name.Contains("\"c) OrElse Not IO.File.Exists(file) Then
                Await WriteErrorAsync(ctx, 404, "Not found")
                Return
            End If
            Dim ext = IO.Path.GetExtension(file).ToLowerInvariant()
            ctx.Response.ContentType = If(ext = ".png", "image/png", If(ext = ".wav", "audio/wav", If(ext = ".mp4", "video/mp4", If(ext = ".webm", "video/webm", "application/octet-stream"))))
            ctx.Response.Headers("Cache-Control") = "private, max-age=86400"
            ctx.Response.Headers("Accept-Ranges") = "bytes"
            Await ctx.Response.SendFileAsync(file, ctx.RequestAborted)
            Return
        End If

        ' Voice input from the phone: any recording → text (Whisper on the PC).
        If path = "/api/transcribe" AndAlso method = "POST" Then
            Dim ext = If(If(ctx.Request.ContentType, "").Contains("wav"), ".wav", If(If(ctx.Request.ContentType, "").Contains("mpeg"), ".mp3", ".m4a"))
            Dim raw = IO.Path.Combine(IO.Path.GetTempPath(), $"localmind-phone-{Guid.NewGuid():N}{ext}")
            Dim wav = IO.Path.ChangeExtension(raw, ".16k.wav")
            Try
                Using fs = IO.File.Create(raw)
                    Await ctx.Request.Body.CopyToAsync(fs, ctx.RequestAborted)
                End Using
                Await VideoTools.ToSpeechWavAsync(raw, wav, ctx.RequestAborted)
                Dim text = Await Voice.TranscribeAsync(wav, Nothing, ctx.RequestAborted)
                Await WriteJsonAsync(ctx, New JObject From {{"text", If(text, "").Trim()}})
            Finally
                Try : IO.File.Delete(raw) : Catch : End Try
                Try : IO.File.Delete(wav) : Catch : End Try
            End Try
            Return
        End If

        ' Read an answer aloud for the phone (AI voice if chosen in Settings, else a Windows voice).
        If path = "/api/speak" AndAlso method = "POST" Then
            Dim b = JObject.Parse(Await ReadBodyAsync(ctx))
            Dim wav = Await Voice.SpeakToFileAsync(If(b("text")?.ToString(), ""), ctx.RequestAborted)
            Try
                ctx.Response.ContentType = "audio/wav"
                Await ctx.Response.SendFileAsync(wav, ctx.RequestAborted)
            Finally
                Try : IO.File.Delete(wav) : Catch : End Try
            End Try
            Return
        End If

        If path = "/api/chats" AndAlso method = "GET" Then
            Dim arr As New JArray()
            For Each s In AppCore.History.ListAll().Take(300)
                arr.Add(New JObject From {
                    {"id", s.Id}, {"title", s.Title}, {"model", If(s.Model, "")},
                    {"updated", s.UpdatedAt.ToString("o")}, {"count", s.Messages.Count}})
            Next
            Await WriteJsonAsync(ctx, arr)
            Return
        End If

        If path.StartsWith("/api/chats/") Then
            Dim id = path.Substring("/api/chats/".Length)
            If method = "DELETE" Then
                AppCore.History.Delete(id)
                Await WriteJsonAsync(ctx, New JObject From {{"ok", True}})
                Return
            End If
            Dim s = AppCore.History.Load(id)
            If s Is Nothing Then
                Await WriteErrorAsync(ctx, 404, "Chat not found")
                Return
            End If
            Await WriteJsonAsync(ctx, SessionToJson(s))
            Return
        End If

        If path = "/api/chat" AndAlso method = "POST" Then
            Await StreamChatAsync(ctx)
            Return
        End If

        Await WriteErrorAsync(ctx, 404, "Unknown API call")
    End Function

    Private Shared Function SessionToJson(s As ChatSession) As JObject
        Dim msgs As New JArray()
        For Each m In s.Messages
            Dim o As New JObject From {{"role", m.Role}}
            If m.Role = "assistant" Then
                o("html") = MarkdownRenderer.ToHtml(m.Content)
                If Not String.IsNullOrEmpty(m.Thinking) Then o("thinkingHtml") = MarkdownRenderer.ToHtml(m.Thinking)
            Else
                o("content") = m.Content
                If m.Images IsNot Nothing AndAlso m.Images.Count > 0 Then
                    o("images") = New JArray(m.Images.Select(Function(i) ImageUtil.ToDataUrl(i)).ToArray())
                End If
            End If
            msgs.Add(o)
        Next
        Return New JObject From {{"id", s.Id}, {"title", s.Title}, {"model", If(s.Model, "")},
                                 {"persona", If(s.PersonaName, "")}, {"messages", msgs}}
    End Function

    ''' <summary>POST /api/chat → Server-Sent Events: thinking/token/done/error.</summary>
    Private Async Function StreamChatAsync(ctx As HttpContext) As Task
        Dim body = JObject.Parse(Await ReadBodyAsync(ctx))
        Dim text = If(body("message")?.ToString(), "").Trim()
        Dim images = If(TryCast(body("images"), JArray)?.Select(Function(i) ImageUtil.StripDataUrl(i.ToString())).
                        Where(Function(i) Not String.IsNullOrEmpty(i)).ToList(), New List(Of String)())
        Dim model = If(body("model")?.ToString(), AppCore.Settings.LastModel)
        If text.Length = 0 AndAlso images.Count = 0 Then
            Await WriteErrorAsync(ctx, 400, "Empty message")
            Return
        End If
        If String.IsNullOrEmpty(model) Then
            Await WriteErrorAsync(ctx, 400, "No model selected")
            Return
        End If

        Dim chatId = body("chatId")?.ToString()
        Dim session = If(String.IsNullOrEmpty(chatId), Nothing, AppCore.History.LoadCopy(chatId))
        If session Is Nothing Then session = New ChatSession()
        Dim persona = AppCore.FindPersona(body("persona")?.ToString())

        session.Messages.Add(New ChatMessage With {.Role = "user", .Content = text, .Images = images})
        If session.Messages.Count = 1 Then session.Title = If(text.Length > 0, Truncate(text, 40), "Photo chat")
        session.Model = model
        session.PersonaName = If(persona?.Name, "")
        session.UpdatedAt = DateTime.Now

        Dim opts As New ChatRequestOptions With {
            .Model = model,
            .Temperature = If(persona?.Temperature, AppCore.Settings.DefaultTemperature),
            .TopP = AppCore.Settings.TopP,
            .MaxTokens = AppCore.Settings.MaxTokens,
            .EnableThinking = If(body("think")?.ToObject(Of Boolean?)(), AppCore.Settings.ShowThinking)
        }
        Dim payload = AppCore.BuildPayload(session, persona)
        Dim useDocs = If(body("docs")?.ToObject(Of Boolean?)(), False)
        Dim useWeb = If(body("web")?.ToObject(Of Boolean?)(), False)
        Dim imageMode = If(body("image")?.ToObject(Of Boolean?)(), False) AndAlso AppCore.Settings.EnableImages

        ctx.Response.ContentType = "text/event-stream; charset=utf-8"
        ctx.Response.Headers("Cache-Control") = "no-cache"
        ctx.Response.Headers("X-Accel-Buffering") = "no"
        Await ctx.Response.StartAsync()

        Dim ch = Channel.CreateUnbounded(Of String)(New UnboundedChannelOptions With {.SingleReader = True})
        Dim send = Sub(o As JObject) ch.Writer.TryWrite("data: " & o.ToString(Formatting.None) & vbLf & vbLf)
        send(New JObject From {{"type", "start"}, {"chatId", session.Id}, {"title", session.Title}})

        Dim who = ctx.Connection.RemoteIpAddress?.ToString()
        Interlocked.Increment(_activeClients)
        Log($"{who} → {model}: {Truncate(text, 60)}")

        Dim ct = ctx.RequestAborted
        Dim sw = Stopwatch.StartNew()
        Dim sources As List(Of SourceRef) = Nothing
        Dim extraMd = ""
        Dim status = Sub(t As String) send(New JObject From {{"type", "status"}, {"t", Localizer.T(t)}})
        Dim genTask = Task.Run(
            Async Function() As Task(Of ChatResult)
                Dim photo As String = Nothing
                Try
                    If imageMode Then
                        ' 🖼 mode: the message becomes a picture (an attached photo is edited).
                        If images.Count > 0 Then
                            photo = IO.Path.Combine(IO.Path.GetTempPath(), $"localmind-phone-{Guid.NewGuid():N}.png")
                            IO.File.WriteAllBytes(photo, Convert.FromBase64String(images(0)))
                        End If
                        extraMd = Await ChatAgent.MakePictureAsync(text, photo, status, ct)
                        Return New ChatResult()
                    End If
                    If useDocs OrElse useWeb Then
                        sources = Await AppCore.AugmentAsync(payload, useDocs, useWeb, Nothing, ct)
                    End If
                    Dim streamOnce = Function(o As ChatRequestOptions) AppCore.Backend.StreamChatAsync(payload, o,
                        Sub(t) send(New JObject From {{"type", "thinking"}, {"t", t}}),
                        Sub(t) send(New JObject From {{"type", "token"}, {"t", t}}),
                        ct)
                    Dim answer = Await ChatAgent.AnswerAsync(streamOnce, payload, opts, Not useWeb, status,
                                                             Sub() send(New JObject From {{"type", "reset"}}), ct)
                    If answer.Sources IsNot Nothing Then sources = answer.Sources
                    extraMd = answer.Markdown
                    Return answer.Result
                Finally
                    If photo IsNot Nothing Then Try : IO.File.Delete(photo) : Catch : End Try
                    ch.Writer.TryComplete()
                End Try
            End Function)

        ' Pump events to the client while generating.
        Try
            Await foreach_reader(ch.Reader, ctx.Response, ct)
        Catch ex As OperationCanceledException
        Catch ex As IOException
        End Try

        Dim result As ChatResult = Nothing
        Dim errorText As String = Nothing
        Try
            result = Await genTask
        Catch ex As OperationCanceledException
            errorText = "stopped"
        Catch ex As Exception
            errorText = ex.Message
        Finally
            Interlocked.Decrement(_activeClients)
        End Try

        Dim content = If(result?.Content, "") & extraMd & If(result IsNot Nothing, AppCore.SourcesMarkdown(sources), "")
        If errorText = "stopped" Then
            content &= vbCrLf & vbCrLf & "_[stopped]_"
        ElseIf errorText IsNot Nothing Then
            content = "⚠ Error: " & errorText
        End If

        Dim tps = If(result IsNot Nothing AndAlso result.EngineTokensPerSecond > 0, result.EngineTokensPerSecond,
                     If(result IsNot Nothing AndAlso sw.Elapsed.TotalSeconds > 0, result.Tokens / sw.Elapsed.TotalSeconds, 0))
        session.Messages.Add(New ChatMessage With {
            .Role = "assistant", .Content = content, .Thinking = If(result?.Thinking, ""),
            .Model = model, .TokensPerSecond = Math.Round(tps, 1)})
        session.UpdatedAt = DateTime.Now
        AppCore.History.Save(session)

        If Not ct.IsCancellationRequested Then
            Try
                Dim done As New JObject From {
                    {"type", If(errorText IsNot Nothing AndAlso errorText <> "stopped", "error", "done")},
                    {"chatId", session.Id}, {"title", session.Title},
                    {"html", MarkdownRenderer.ToHtml(content)},
                    {"thinkingHtml", If(String.IsNullOrEmpty(result?.Thinking), "", MarkdownRenderer.ToHtml(result.Thinking))},
                    {"tps", Math.Round(tps, 1)}, {"tokens", If(result?.Tokens, 0)},
                    {"message", If(errorText, "")}}
                Await ctx.Response.WriteAsync("data: " & done.ToString(Formatting.None) & vbLf & vbLf, ct)
                Await ctx.Response.Body.FlushAsync(ct)
            Catch
            End Try
        End If
    End Function

    Private Shared Async Function foreach_reader(reader As ChannelReader(Of String), resp As HttpResponse, ct As CancellationToken) As Task
        Dim sb As New StringBuilder()
        While Await reader.WaitToReadAsync(ct)
            sb.Clear()
            Dim item As String = Nothing
            ' Batch whatever is queued into one write (fewer packets on slow Wi-Fi).
            While reader.TryRead(item)
                sb.Append(item)
            End While
            Await resp.WriteAsync(sb.ToString(), ct)
            Await resp.Body.FlushAsync(ct)
        End While
    End Function

    '════════════════════════════════════════════════════════════
    '  /v1 — OpenAI-compatible proxy
    '════════════════════════════════════════════════════════════
    Private Async Function HandleOpenAiAsync(ctx As HttpContext, path As String, method As String) As Task
        Dim s = AppCore.Settings

        If path.Equals("/v1/models", StringComparison.OrdinalIgnoreCase) AndAlso s.Backend = BackendKind.BuiltIn Then
            Dim data As New JArray()
            For Each m In AppCore.Library.All()
                data.Add(New JObject From {{"id", m.DisplayName}, {"object", "model"}, {"owned_by", "local"},
                                           {"created", New DateTimeOffset(m.DownloadedAt).ToUnixTimeSeconds()}})
            Next
            Await WriteJsonAsync(ctx, New JObject From {{"object", "list"}, {"data", data}})
            Return
        End If

        Dim bodyBytes As Byte() = Nothing
        If method = "POST" Then
            Using ms As New MemoryStream()
                Await ctx.Request.Body.CopyToAsync(ms, ctx.RequestAborted)
                bodyBytes = ms.ToArray()
            End Using
        End If

        Dim target As String
        Dim auth As String = Nothing
        Select Case s.Backend
            Case BackendKind.BuiltIn
                ' Load the requested model (or the desktop's current one) before forwarding.
                Dim modelName As String = Nothing
                If bodyBytes IsNot Nothing AndAlso bodyBytes.Length > 0 Then
                    Try
                        modelName = JObject.Parse(Encoding.UTF8.GetString(bodyBytes))("model")?.ToString()
                    Catch
                    End Try
                End If
                Dim model = If(AppCore.Library.FindByName(modelName),
                            If(AppCore.Engine.CurrentModel, AppCore.Library.FindByName(s.LastModel)))
                If model Is Nothing Then model = AppCore.Library.All().FirstOrDefault()
                If model Is Nothing Then
                    Await WriteErrorAsync(ctx, 400, "No models are downloaded on this PC yet.")
                    Return
                End If
                Using Await AppCore.Engine.AcquireAsync(model, ctx.RequestAborted)
                    Log($"{ctx.Connection.RemoteIpAddress} → API {path} ({model.DisplayName})")
                    Await ForwardAsync(ctx, AppCore.Engine.BaseUrl & path, bodyBytes, Nothing)
                End Using
                Return
            Case Else
                target = OpenAiCompatService.NormalizeBase(s.OpenAiUrl) & path.Substring(3)
                auth = s.OpenAiKey
        End Select
        Log($"{ctx.Connection.RemoteIpAddress} → API {path}")
        Await ForwardAsync(ctx, target, bodyBytes, auth)
    End Function

    Private Shared Async Function ForwardAsync(ctx As HttpContext, url As String, body As Byte(), bearer As String) As Task
        Using req As New HttpRequestMessage(New HttpMethod(ctx.Request.Method), url)
            If body IsNot Nothing Then
                req.Content = New ByteArrayContent(body)
                req.Content.Headers.TryAddWithoutValidation("Content-Type", If(ctx.Request.ContentType, "application/json"))
            End If
            If Not String.IsNullOrWhiteSpace(bearer) Then req.Headers.TryAddWithoutValidation("Authorization", "Bearer " & bearer)

            Using resp = Await OpenAiCompatService.Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ctx.RequestAborted)
                ctx.Response.StatusCode = CInt(resp.StatusCode)
                Dim cType2 = resp.Content.Headers.ContentType?.ToString()
                If cType2 IsNot Nothing Then ctx.Response.ContentType = cType2
                Using src = Await resp.Content.ReadAsStreamAsync(ctx.RequestAborted)
                    Dim buf(16383) As Byte
                    Do
                        Dim n = Await src.ReadAsync(buf.AsMemory(0, buf.Length), ctx.RequestAborted)
                        If n = 0 Then Exit Do
                        Await ctx.Response.Body.WriteAsync(buf.AsMemory(0, n), ctx.RequestAborted)
                        Await ctx.Response.Body.FlushAsync(ctx.RequestAborted)
                    Loop
                End Using
            End Using
        End Using
    End Function

    '════════════════════════════════════════════════════════════
    '  Helpers
    '════════════════════════════════════════════════════════════
    Private Shared Async Function ReadBodyAsync(ctx As HttpContext) As Task(Of String)
        Using r As New StreamReader(ctx.Request.Body, Encoding.UTF8)
            Return Await r.ReadToEndAsync(ctx.RequestAborted)
        End Using
    End Function

    Private Shared ReadOnly _resCache As New ConcurrentDictionary(Of String, Byte())()

    Private Shared Async Function ServeResourceAsync(ctx As HttpContext, name As String, contentType As String,
                                                     Optional cache As Boolean = False) As Task
        Dim bytes = _resCache.GetOrAdd(name, Function(n)
                                                 Using s = Assembly.GetExecutingAssembly().GetManifestResourceStream(n)
                                                     If s Is Nothing Then Return Array.Empty(Of Byte)()
                                                     Using ms As New MemoryStream()
                                                         s.CopyTo(ms)
                                                         Return ms.ToArray()
                                                     End Using
                                                 End Using
                                             End Function)
        ctx.Response.ContentType = contentType
        ctx.Response.Headers("Cache-Control") = If(cache, "public, max-age=86400", "no-cache")
        Await ctx.Response.Body.WriteAsync(bytes, ctx.RequestAborted)
    End Function

    Private Shared Async Function WriteJsonAsync(ctx As HttpContext, token As JToken) As Task
        ctx.Response.ContentType = "application/json; charset=utf-8"
        Await ctx.Response.WriteAsync(token.ToString(Formatting.None), ctx.RequestAborted)
    End Function

    Private Shared Async Function WriteErrorAsync(ctx As HttpContext, status As Integer, message As String) As Task
        If ctx.Response.HasStarted Then Return
        ctx.Response.StatusCode = status
        Await WriteJsonAsync(ctx, New JObject From {{"error", New JObject From {{"message", message}, {"code", status}}}})
    End Function

    Private Shared Function Truncate(s As String, n As Integer) As String
        If s Is Nothing Then Return ""
        If s.Length <= n Then Return s
        Return s.Substring(0, n) & "…"
    End Function
End Class
