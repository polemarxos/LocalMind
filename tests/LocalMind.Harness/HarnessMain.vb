Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Net.Http
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks
Imports Newtonsoft.Json.Linq

Module HarnessMain
    Private _fails As Integer
    Private Sub Check(cond As Boolean, name As String, Optional detail As String = "")
        Console.WriteLine(If(cond, "  PASS ", "  FAIL ") & name & If(detail <> "", "  → " & detail, ""))
        If Not cond Then _fails += 1
    End Sub

    Function Hw(vramGB As Double, ramGB As Double) As HardwareInfo
        Dim h As New HardwareInfo() With {.RamBytes = CLng(ramGB * 1024 ^ 3)}
        If vramGB > 0 Then h.Gpus.Add(New GpuDevice With {.Name = "Test GPU", .Vendor = GpuVendor.Nvidia, .VramBytes = CLng(vramGB * 1024 ^ 3)})
        Return h
    End Function

    Sub Main(args As String())
        RunAsync(args).GetAwaiter().GetResult()
        Console.WriteLine()
        Console.WriteLine(If(_fails = 0, "ALL TESTS PASSED", $"{_fails} FAILURE(S)"))
        Environment.Exit(If(_fails = 0, 0, 1))
    End Sub

    Async Function RunAsync(args As String()) As Task
        Dim only = If(args.Length > 0, args(0), "all")

        If only = "all" OrElse only = "unit" Then
            Console.WriteLine("== ThinkTagSplitter")
            Dim th As New StringBuilder(), co As New StringBuilder()
            Dim sp As New ThinkTagSplitter(Sub(t) th.Append(t), Sub(t) co.Append(t))
            For Each c In {"<thi", "nk>abc", "</th", "ink>", vbLf & "Hello <think> x <", "/b>"}
                sp.Feed(c)
            Next
            sp.Flush()
            Check(th.ToString() = "abc", "thinking extracted", th.ToString())
            Check(co.ToString() = "Hello <think> x </b>", "content keeps later tags", co.ToString().Replace(vbLf, "\n"))

            Console.WriteLine("== ParseQuant")
            For Each pair In {("gemma-4-12b-it-Q4_K_M.gguf", "Q4_K_M"), ("Qwen3.6-35B-A3B-UD-Q4_K_XL.gguf", "UD-Q4_K_XL"),
                              ("Qwen3.6-35B-A3B-BF16-00001-of-00002.gguf", "BF16"), ("model.IQ3_XXS.gguf", "IQ3_XXS"),
                              ("gpt-oss-20b-MXFP4.gguf", "MXFP4"), ("Qwen3.6-35B-A3B-MXFP4_MOE.gguf", "MXFP4_MOE"),
                              ("Meta-Llama-3.1-8B-Instruct-Q8_0.gguf", "Q8_0"), ("model-q5_k_s.gguf", "Q5_K_S")}
                Check(ModelCatalog.ParseQuant(pair.Item1) = pair.Item2, pair.Item1, ModelCatalog.ParseQuant(pair.Item1))
            Next

            Console.WriteLine("== Markdown safety")
            Dim html = MarkdownRenderer.ToHtml("Hi <script>alert(1)</script> <img src=x onerror=alert(2)>")
            Check(Not html.Contains("<script>") AndAlso Not html.Contains("<img"), "raw HTML disabled", html.Trim())

            Console.WriteLine("== Embedded catalog")
            Dim emb = ModelCatalog.LoadEmbedded()
            Check(emb.Count > 500, "embedded catalog loads", emb.Count & " entries")
            Dim q = emb.First(Function(e) e.Repo = "unsloth/Qwen3.6-35B-A3B-GGUF")
            Check(q.IsMoE AndAlso q.IsVision AndAlso q.ActiveParamsB = 3, "Qwen3.6-35B-A3B flags", q.Badges)
            Dim fits8 = emb.Where(Function(e) ModelCatalog.Fit(e, ModelCatalog.EstimateFileBytes(e, ModelCatalog.AutoQuant(e, Hw(8, 16), 16384)), Hw(8, 16), 16384) <> FitLevel.TooBig).Count()
            Dim fits24 = emb.Where(Function(e) ModelCatalog.Fit(e, ModelCatalog.EstimateFileBytes(e, ModelCatalog.AutoQuant(e, Hw(24, 64), 16384)), Hw(24, 64), 16384) <> FitLevel.TooBig).Count()
            Check(fits8 > 100 AndAlso fits24 > fits8, "fit filter scales with hardware", $"8GB/16GB: {fits8} runnable, 24GB/64GB: {fits24}")

            Console.WriteLine("== Default view: best models that fit the GPU")
            For Each prof In {(0.0, 16.0), (6.0, 16.0), (8.0, 32.0), (12.0, 32.0), (16.0, 32.0), (24.0, 64.0)}
                Dim h = Hw(prof.Item1, prof.Item2)
                Dim best = ModelCatalog.BestThatFit(emb, h, 16384)
                Dim sample = emb.Where(Function(e) best.Contains(e.Repo)).OrderByDescending(Function(e) e.Params).Take(6).Select(Function(e) $"{e.Name}[{ModelCatalog.AutoQuant(e, h, 16384)}]")
                Dim allFit = emb.Where(Function(e) best.Contains(e.Repo)).All(Function(e) prof.Item1 = 0 OrElse ModelCatalog.Fit(e, ModelCatalog.EstimateFileBytes(e, ModelCatalog.AutoQuant(e, h, 16384)), h, 16384) = FitLevel.Fits)
                Check(best.Count >= 5 AndAlso best.Count <= 60 AndAlso allFit, $"{prof.Item1:0}GB VRAM: {best.Count} shown", "largest: " & String.Join(", ", sample))
            Next

            Console.WriteLine("== Payload building")
            AppCore.Settings.HistoryMessages = 4
            AppCore.Settings.KeepImagesInLastMessages = 2
            Dim s As New ChatSession()
            For i = 1 To 5
                s.Messages.Add(New ChatMessage With {.Role = "user", .Content = "q" & i, .Images = New List(Of String) From {"iVBORimg" & i}})
                s.Messages.Add(New ChatMessage With {.Role = "assistant", .Content = "a" & i})
            Next
            Dim p = AppCore.BuildPayload(s, New Persona With {.Name = "x", .SystemPrompt = "SYS"})
            Check(p(0).Role = "system" AndAlso p(1).Role = "user", "system first, history starts on user", String.Join(",", p.Select(Function(m) m.Role & ":" & m.Content & ":" & m.Images.Count)))
            Check(p.Last().Content = "a5" AndAlso p.Count = 5, "history window (system + last 4)", p.Count.ToString())
        End If

        If only = "all" OrElse only = "live" Then
            Console.WriteLine("== Live Hugging Face catalog")
            Dim live = Await ModelCatalog.FetchLiveAsync(CancellationToken.None)
            Check(live.Count > 500, "live catalog", live.Count & " entries")
            Dim hits = Await ModelCatalog.SearchHubAsync("mistral", CancellationToken.None)
            Check(hits.Count > 5, "hub search 'mistral'", hits.Count & " results, e.g. " & String.Join(", ", hits.Take(3).Select(Function(h) h.Repo)))

            Console.WriteLine("== Auto quant on real files")
            For Each repo In {"unsloth/gemma-4-12b-it-GGUF", "unsloth/Qwen3.6-35B-A3B-GGUF", "bartowski/Meta-Llama-3.1-8B-Instruct-GGUF"}
                Dim e = live.FirstOrDefault(Function(x) x.Repo = repo)
                If e Is Nothing Then e = ModelCatalog.LoadEmbedded().First(Function(x) x.Repo = repo)
                Dim files = Await ModelCatalog.GetRepoFilesAsync(repo, Nothing, CancellationToken.None)
                Dim line = repo & ": " & files.Quants.Count & " quants, mmproj=" & If(files.Mmproj?.Path, "none") & vbLf
                For Each prof In {(0.0, 16.0), (8.0, 16.0), (12.0, 32.0), (16.0, 32.0), (24.0, 64.0)}
                    Dim h = Hw(prof.Item1, prof.Item2)
                    Dim pick = ModelCatalog.PickQuant(e, files, "Auto", h, 16384)
                    line &= $"        {prof.Item1:0}GB VRAM/{prof.Item2:0}GB RAM → {pick.Label} ({Downloader.FormatBytes(pick.TotalBytes)}, {ModelCatalog.FitText(ModelCatalog.Fit(e, pick.TotalBytes, h, 16384))})" & vbLf
                Next
                Check(files.Quants.Count > 5, line.TrimEnd())
            Next
        End If

        If only = "all" OrElse only = "download" Then
            Console.WriteLine("== Download manager (real download, cancel + resume, SHA-256)")
            AppCore.Settings.ModelsFolder = "/tmp/oc-models"
            AppCore.Settings.VerifyDownloads = True
            Dim entry As New CatalogEntry With {.Repo = "bartowski/SmolLM2-135M-Instruct-GGUF", .Name = "SmolLM2-135M-Instruct", .Params = 134515008}
            Dim job = AppCore.Downloads.Enqueue(entry, "Q8_0")
            ' cancel shortly after it starts downloading, then resume
            Dim sw = Diagnostics.Stopwatch.StartNew()
            While job.Status <> DownloadStatus.Downloading AndAlso sw.Elapsed.TotalSeconds < 60
                Await Task.Delay(50)
            End While
            Await Task.Delay(400)
            Dim before = job.BytesDone
            AppCore.Downloads.Cancel(job)
            While job.Status = DownloadStatus.Downloading
                Await Task.Delay(50)
            End While
            Dim partFile = Directory.GetFiles(job.TargetDir, "*.part").FirstOrDefault()
            Dim partLen = If(partFile IsNot Nothing, New FileInfo(partFile).Length, 0)
            Check(job.Status = DownloadStatus.Cancelled AndAlso partLen > 0, "cancel keeps partial file", $"{Downloader.FormatBytes(partLen)} saved of {Downloader.FormatBytes(job.BytesTotal)}")
            AppCore.Downloads.Retry(job)
            sw.Restart()
            While job.Status <> DownloadStatus.Completed AndAlso job.Status <> DownloadStatus.Failed AndAlso sw.Elapsed.TotalSeconds < 300
                Await Task.Delay(200)
            End While
            Check(job.Status = DownloadStatus.Completed, "resume completes + checksum verified", job.StatusText)
            AppCore.Library.Refresh()
            Dim m = AppCore.Library.All().FirstOrDefault()
            Check(m IsNot Nothing AndAlso m.DisplayName = "SmolLM2-135M-Instruct · Q8_0", "model registered in library", m?.DisplayName)

            ' a reasoning model for the thinking test + a pending-job resume test
            Dim qwen As New CatalogEntry With {.Repo = "unsloth/Qwen3-0.6B-GGUF", .Name = "Qwen3-0.6B", .Params = 596049920}
            Dim j2 = AppCore.Downloads.Enqueue(qwen, "Q4_K_M")
            sw.Restart()
            While j2.Status <> DownloadStatus.Completed AndAlso j2.Status <> DownloadStatus.Failed AndAlso sw.Elapsed.TotalSeconds < 300
                Await Task.Delay(200)
            End While
            Check(j2.Status = DownloadStatus.Completed, "second model", j2.StatusText)
        End If

        If only = "server" Then
            Console.WriteLine("== OpenAI-compatible client vs real llama-server")
            AppCore.Settings.Backend = BackendKind.OpenAiCompatible
            AppCore.Settings.OpenAiUrl = "http://127.0.0.1:18080"
            AppCore.Settings.OpenAiKey = ""
            AppCore.ResetBackend()
            Dim be = AppCore.Backend
            Check(Await be.IsOnlineAsync(), "online", be.DisplayName)
            Dim names = Await be.ListModelsAsync()
            Check(names.Count > 0, "list models", String.Join(",", names))
            Dim mdl = names(0)
            Dim thinkBuf As New StringBuilder(), tokBuf As New StringBuilder()
            Dim r1 = Await be.StreamChatAsync(New List(Of ChatMessage) From {New ChatMessage With {.Role = "user", .Content = "What is 17+25? Answer briefly."}},
                New ChatRequestOptions With {.Model = mdl, .MaxTokens = 3000, .Temperature = 0.6, .EnableThinking = True}, Sub(t) thinkBuf.Append(t), Sub(t) tokBuf.Append(t), CancellationToken.None)
            Check(r1.Thinking.Length > 20 AndAlso r1.Content.Contains("42") AndAlso thinkBuf.ToString() = r1.Thinking AndAlso tokBuf.ToString() = r1.Content,
                  "thinking + answer streamed separately", $"{r1.Thinking.Length} thinking chars, answer '{r1.Content.Trim()}', {r1.Tokens} tok @ {r1.EngineTokensPerSecond:0} tok/s")
            Dim r2 = Await be.StreamChatAsync(New List(Of ChatMessage) From {New ChatMessage With {.Role = "user", .Content = "What is 17+25? Answer briefly."}},
                New ChatRequestOptions With {.Model = mdl, .MaxTokens = 200, .EnableThinking = False}, Sub(t) Return, Sub(t) Return, CancellationToken.None)
            Check(r2.Thinking.Length = 0 AndAlso r2.Content.Contains("42"), "thinking toggle off", r2.Content.Trim())

            Dim cts0 As New CancellationTokenSource(700)
            Dim cancelled = False
            Try
                Await be.StreamChatAsync(New List(Of ChatMessage) From {New ChatMessage With {.Role = "user", .Content = "Write a 2000 word story."}},
                    New ChatRequestOptions With {.Model = mdl, .EnableThinking = False}, Sub(t) Return, Sub(t) Return, cts0.Token)
            Catch ex As OperationCanceledException
                cancelled = True
            End Try
            Check(cancelled, "Stop button cancels a stream")

            Console.WriteLine("== LAN server (phone page API + OpenAI proxy)")
            AppCore.Settings.LastModel = mdl
            AppCore.Settings.LanPin = "424242"
            AppCore.Settings.LanRequirePin = True
            AppCore.Settings.LanOpenAiApi = True
            AppCore.Lan = New LanServer()
            Await AppCore.Lan.StartAsync(18484)
            Dim http As New HttpClient() With {.BaseAddress = New Uri("http://127.0.0.1:18484")}
            Dim page = Await http.GetStringAsync("/")
            Check(page.Contains("LocalMind") AndAlso page.Contains("/api/chat"), "web UI served", page.Length & " bytes")
            Dim js = Await http.GetAsync("/assets/hljs.js")
            Check(js.IsSuccessStatusCode AndAlso (Await js.Content.ReadAsByteArrayAsync()).Length > 100000, "highlight.js served offline")
            Check(CInt((Await http.GetAsync("/api/info")).StatusCode) = 401, "API rejects missing PIN")
            Check(CInt((Await http.GetAsync("/v1/models")).StatusCode) = 401, "/v1 rejects missing key")
            Dim authed As New HttpClient() With {.BaseAddress = New Uri("http://127.0.0.1:18484")}
            authed.DefaultRequestHeaders.Add("X-Pin", "424242")
            Dim info = JObject.Parse(Await authed.GetStringAsync("/api/info"))
            Check(CType(info("models"), JArray).Count >= 1, "API info", info("backend").ToString())

            Dim req As New HttpRequestMessage(HttpMethod.Post, "/api/chat") With {
                .Content = New StringContent(New JObject From {{"model", mdl}, {"message", "What is 6*7? Think, then answer."}, {"think", True}}.ToString(), Encoding.UTF8, "application/json")}
            Dim resp = Await authed.SendAsync(req, HttpCompletionOption.ResponseHeadersRead)
            Check(resp.Content.Headers.ContentType.MediaType = "text/event-stream", "SSE content type")
            Dim body = Await resp.Content.ReadAsStringAsync()
            Dim evs = body.Split({vbLf & vbLf}, StringSplitOptions.RemoveEmptyEntries).Where(Function(l) l.StartsWith("data: ")).Select(Function(l) JObject.Parse(l.Substring(6))).ToList()
            Dim types = evs.Select(Function(e) e("type").ToString()).Distinct().ToList()
            Dim done = evs.LastOrDefault()
            Check(types.SequenceEqual({"start", "thinking", "token", "done"}), "SSE events start→thinking→token→done", String.Join(",", types) & $" ({evs.Count} events, {done?("tps")} tok/s)")
            Check(done("html").ToString().Contains("42"), "server-rendered answer", done("html").ToString().Trim())
            Dim chatId = done("chatId").ToString()
            Dim saved = AppCore.History.Load(chatId)
            Check(saved IsNot Nothing AndAlso saved.Messages.Count = 2 AndAlso saved.Messages(1).Thinking.Length > 0, "chat saved to shared history (with thinking)", saved?.Title)

            Dim req2 As New HttpRequestMessage(HttpMethod.Post, "/api/chat") With {
                .Content = New StringContent(New JObject From {{"chatId", chatId}, {"model", mdl}, {"message", "Now add 8 to that."}, {"think", False},
                                                               {"images", New JArray("data:image/png;base64,iVBORw0KGgo=")}}.ToString(), Encoding.UTF8, "application/json")}
            Dim b2 = Await (Await authed.SendAsync(req2)).Content.ReadAsStringAsync()
            Dim s2 = AppCore.History.Load(chatId)
            Check(s2.Messages.Count = 4 AndAlso s2.Messages(2).Images.Count = 1 AndAlso s2.Messages(2).Images(0) = "iVBORw0KGgo=", "follow-up in same chat, photo stored", s2.Messages(3).Content.Trim().Replace(vbLf, " "))
            Dim chat = JObject.Parse(Await authed.GetStringAsync("/api/chats/" & chatId))
            Check(CType(chat("messages"), JArray).Count = 4 AndAlso chat("messages")(2)("images")(0).ToString().StartsWith("data:image/png;base64,"), "chat fetch for phone (images as data URLs)")
            Dim list = JArray.Parse(Await authed.GetStringAsync("/api/chats"))
            Check(list.Any(Function(x) x("id").ToString() = chatId), "chat list")
            Check(CInt((Await authed.GetAsync("/api/chats/..%2F..%2Fsettings")).StatusCode) = 404, "path traversal blocked")

            Dim models = JObject.Parse(Await authed.GetStringAsync("/v1/models"))
            Check(CType(models("data"), JArray).Count >= 1, "/v1/models proxied")
            Dim oai As New OpenAiCompatService("http://127.0.0.1:18484", "424242")
            Dim r4 = Await oai.StreamChatAsync(New List(Of ChatMessage) From {New ChatMessage With {.Role = "user", .Content = "Reply with the word OK."}},
                                              New ChatRequestOptions With {.Model = mdl, .MaxTokens = 50}, Sub(t) Return, Sub(t) Return, CancellationToken.None)
            Check(r4.Content.Length > 0, "another PC using this one via /v1 (streamed through proxy)", r4.Content.Trim())

            Dim cts As New CancellationTokenSource()
            ' Behave like a browser closing the tab: drop the socket, don't drain the response for reuse.
            Dim phone As New HttpClient(New SocketsHttpHandler With {.MaxResponseDrainSize = 0}) With {.BaseAddress = New Uri("http://127.0.0.1:18484")}
            phone.DefaultRequestHeaders.Add("X-Pin", "424242")
            Dim req5 As New HttpRequestMessage(HttpMethod.Post, "/api/chat") With {
                .Content = New StringContent(New JObject From {{"model", mdl}, {"message", "Write a very long story about the sea."}, {"think", False}}.ToString(), Encoding.UTF8, "application/json")}
            Dim resp5 = Await phone.SendAsync(req5, HttpCompletionOption.ResponseHeadersRead, cts.Token)
            Dim st = Await resp5.Content.ReadAsStreamAsync()
            Dim buf(1023) As Byte
            Await st.ReadAsync(buf, 0, buf.Length)
            Await Task.Delay(500)
            Check(AppCore.Lan.ActiveClients = 1, "phone generating", "active=" & AppCore.Lan.ActiveClients)
            cts.Cancel()
            st.Dispose()
            resp5.Dispose()
            phone.Dispose()
            Await Task.Delay(1500)
            Check(AppCore.Lan.ActiveClients = 0, "closing the phone page stops generation", "active=" & AppCore.Lan.ActiveClients)

            Dim bad As New HttpClient() With {.BaseAddress = New Uri("http://127.0.0.1:18484")}
            bad.DefaultRequestHeaders.Add("X-Pin", "000000")
            Dim codes As New List(Of Integer)
            For i = 1 To 9
                codes.Add(CInt((Await bad.GetAsync("/api/info")).StatusCode))
            Next
            Check(codes.Last() = 429 AndAlso CInt((Await authed.GetAsync("/api/info")).StatusCode) = 429, "PIN brute-force lockout (per IP)", String.Join(",", codes))
            Await AppCore.Lan.StopAsync()
            Check(Not AppCore.Lan.IsRunning, "LAN server stops cleanly")
        End If

        If only = "all" OrElse only = "engine" Then
            Console.WriteLine("== Built-in engine (real llama-server)")
            AppCore.Settings.Backend = BackendKind.BuiltIn
            AppCore.ResetBackend()
            AppCore.Settings.ModelsFolder = "/tmp/oc-models"
            AppCore.Settings.ContextSize = 4096
            AppCore.Settings.ParallelSlots = 2
            AppCore.Settings.KvCacheType = "q8_0"
            AppCore.Library.Refresh()
            Check(AppCore.Engine.IsInstalled(), "engine found", AppCore.Engine.ServerExe())
            Dim smol = AppCore.Library.All().First(Function(x) x.DisplayName.StartsWith("SmolLM2"))
            Dim backend As New BuiltInBackend()
            Dim tokens As New StringBuilder()
            Dim res = Await backend.StreamChatAsync(
                New List(Of ChatMessage) From {New ChatMessage With {.Role = "user", .Content = "Say hello in five words."}},
                New ChatRequestOptions With {.Model = smol.DisplayName, .Temperature = 0.2, .MaxTokens = 40},
                Sub(t) Return, Sub(t) tokens.Append(t), CancellationToken.None)
            Check(res.Content.Length > 0 AndAlso res.Content = tokens.ToString(), "streamed answer", res.Content.Replace(vbLf, " ") & $"  [{res.Tokens} tok, {res.EngineTokensPerSecond:0} tok/s]")
            Check(AppCore.Engine.State = EngineState.Ready, "engine ready", AppCore.Engine.StateMessage)

            Dim qwen = AppCore.Library.All().First(Function(x) x.DisplayName.StartsWith("Qwen3-0.6B"))
            Dim thinkBuf As New StringBuilder()
            Dim r2 = Await backend.StreamChatAsync(
                New List(Of ChatMessage) From {New ChatMessage With {.Role = "user", .Content = "What is 17+25? Answer briefly."}},
                New ChatRequestOptions With {.Model = qwen.DisplayName, .Temperature = 0.6, .MaxTokens = 3000, .EnableThinking = True},
                Sub(t) thinkBuf.Append(t), Sub(t) Return, CancellationToken.None)
            Check(AppCore.Engine.CurrentModel.DisplayName = qwen.DisplayName, "switched model", AppCore.Engine.StateMessage)
            Check(r2.Thinking.Length > 0 AndAlso Not r2.Content.Contains("<think>"), "thinking separated", $"thinking {r2.Thinking.Length} chars; answer: {r2.Content.Trim().Replace(vbLf, " ")}")
            Dim r3 = Await backend.StreamChatAsync(
                New List(Of ChatMessage) From {New ChatMessage With {.Role = "user", .Content = "What is 17+25? Answer briefly."}},
                New ChatRequestOptions With {.Model = qwen.DisplayName, .Temperature = 0.6, .MaxTokens = 200, .EnableThinking = False},
                Sub(t) Return, Sub(t) Return, CancellationToken.None)
            Check(r3.Thinking.Trim().Length = 0, "thinking off honoured", $"thinking {r3.Thinking.Length} chars; answer: {r3.Content.Trim().Replace(vbLf, " ")}")

            Console.WriteLine("== Concurrent requests on different models")
            Dim aText As New StringBuilder()
            Dim tA = backend.StreamChatAsync(New List(Of ChatMessage) From {New ChatMessage With {.Role = "user", .Content = "Count from 1 to 400, comma separated, no other text."}},
                New ChatRequestOptions With {.Model = smol.DisplayName, .MaxTokens = 1500, .Temperature = 0.1}, Sub(t) Return, Sub(t) aText.Append(t), CancellationToken.None)
            Await Task.Delay(1500)
            Dim stateDuringA = AppCore.Engine.CurrentModel?.DisplayName
            Dim tB = backend.StreamChatAsync(New List(Of ChatMessage) From {New ChatMessage With {.Role = "user", .Content = "Say hi."}},
                New ChatRequestOptions With {.Model = qwen.DisplayName, .MaxTokens = 50, .EnableThinking = False}, Sub(t) Return, Sub(t) Return, CancellationToken.None)
            Await Task.Delay(800)
            Check(AppCore.Engine.CurrentModel?.DisplayName = smol.DisplayName AndAlso Not tA.IsCompleted, "B waits while A is answering", AppCore.Engine.StateMessage)
            Dim rA = Await tA
            Dim rB = Await tB
            Check(rA.Content.Length > 20 AndAlso rB.Content.Length > 0, "both answers complete, no model swapped mid-answer", $"A: {rA.Content.Length} chars, B: '{rB.Content.Trim()}'")
            Check(AppCore.Engine.CurrentModel?.DisplayName = qwen.DisplayName AndAlso AppCore.Engine.ActiveRequests = 0, "engine on B's model afterwards, no leaked request count")

            Console.WriteLine("== LAN server")
            AppCore.Settings.LastModel = smol.DisplayName
            AppCore.Settings.LanPin = "424242"
            AppCore.Settings.LanRequirePin = True
            AppCore.Lan = New LanServer()
            Await AppCore.Lan.StartAsync(18484)
            Dim http As New HttpClient() With {.BaseAddress = New Uri("http://127.0.0.1:18484")}
            Dim page = Await http.GetStringAsync("/")
            Check(page.Contains("LocalMind") AndAlso page.Contains("/api/chat"), "web UI served")
            Dim js = Await http.GetAsync("/assets/hljs.js")
            Check(js.IsSuccessStatusCode AndAlso (Await js.Content.ReadAsByteArrayAsync()).Length > 100000, "highlight.js served")
            Dim noPin = Await http.GetAsync("/api/info")
            Check(CInt(noPin.StatusCode) = 401, "API rejects missing PIN")
            http.DefaultRequestHeaders.Add("X-Pin", "424242")
            Dim info = JObject.Parse(Await http.GetStringAsync("/api/info"))
            Check(CType(info("models"), JArray).Count >= 2, "API info with PIN", info("models").ToString(Newtonsoft.Json.Formatting.None))

            ' SSE chat as the phone page does it
            Dim req As New HttpRequestMessage(HttpMethod.Post, "/api/chat") With {
                .Content = New StringContent(New JObject From {{"model", smol.DisplayName}, {"message", "Name three colors."}, {"think", False}}.ToString(), Encoding.UTF8, "application/json")}
            Dim resp = Await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead)
            Dim body = Await resp.Content.ReadAsStringAsync()
            Dim evs = body.Split({vbLf & vbLf}, StringSplitOptions.RemoveEmptyEntries).Where(Function(l) l.StartsWith("data: ")).Select(Function(l) JObject.Parse(l.Substring(6))).ToList()
            Dim types = evs.Select(Function(e) e("type").ToString()).Distinct().ToList()
            Dim done = evs.LastOrDefault()
            Check(types.Contains("start") AndAlso types.Contains("token") AndAlso done?("type")?.ToString() = "done", "SSE stream start/token/done", String.Join(",", types) & $" ({evs.Count} events, {done?("tps")} tok/s)")
            Dim chatId = done("chatId").ToString()
            Dim saved = AppCore.History.Load(chatId)
            Check(saved IsNot Nothing AndAlso saved.Messages.Count = 2, "LAN chat saved to shared history", saved?.Title)

            ' continue same chat
            Dim req2 As New HttpRequestMessage(HttpMethod.Post, "/api/chat") With {
                .Content = New StringContent(New JObject From {{"chatId", chatId}, {"model", smol.DisplayName}, {"message", "And one more?"}}.ToString(), Encoding.UTF8, "application/json")}
            Dim b2 = Await (Await http.SendAsync(req2)).Content.ReadAsStringAsync()
            Check(AppCore.History.Load(chatId).Messages.Count = 4, "follow-up appended to same chat")
            Dim chat = JObject.Parse(Await http.GetStringAsync("/api/chats/" & chatId))
            Check(CType(chat("messages"), JArray).Count = 4 AndAlso chat("messages")(1)("html") IsNot Nothing, "chat fetch renders HTML")

            ' OpenAI-compatible proxy
            Dim models = JObject.Parse(Await http.GetStringAsync("/v1/models"))
            Check(CType(models("data"), JArray).Count >= 2, "/v1/models", String.Join(", ", models("data").Select(Function(x) x("id").ToString())))
            Dim oai As New OpenAiCompatService("http://127.0.0.1:18484", "424242")
            Dim r4 = Await oai.StreamChatAsync(New List(Of ChatMessage) From {New ChatMessage With {.Role = "user", .Content = "Say OK."}},
                                              New ChatRequestOptions With {.Model = smol.DisplayName, .MaxTokens = 10}, Sub(t) Return, Sub(t) Return, CancellationToken.None)
            Check(r4.Content.Length > 0, "/v1/chat/completions streamed through proxy (= another PC using this one)", r4.Content.Trim())

            ' brute-force lockout
            Dim bad As New HttpClient() With {.BaseAddress = New Uri("http://127.0.0.1:18484")}
            bad.DefaultRequestHeaders.Add("X-Pin", "000000")
            Dim last = 0
            For i = 1 To 9
                last = CInt((Await bad.GetAsync("/api/info")).StatusCode)
            Next
            Check(last = 429, "PIN brute-force lockout", "status " & last)

            ' client disconnect cancels generation
            Dim cts As New CancellationTokenSource()
            ' Behave like a browser closing the tab: drop the socket, don't drain the response for reuse.
            Dim phone As New HttpClient(New SocketsHttpHandler With {.MaxResponseDrainSize = 0}) With {.BaseAddress = New Uri("http://127.0.0.1:18484")}
            phone.DefaultRequestHeaders.Add("X-Pin", "424242")
            Dim req5 As New HttpRequestMessage(HttpMethod.Post, "/api/chat") With {
                .Content = New StringContent(New JObject From {{"model", smol.DisplayName}, {"message", "Write a very long story about the sea."}}.ToString(), Encoding.UTF8, "application/json")}
            Dim resp5 = Await http.SendAsync(req5, HttpCompletionOption.ResponseHeadersRead, cts.Token)
            Dim st = Await resp5.Content.ReadAsStreamAsync()
            Dim buf(255) As Byte
            Await st.ReadAsync(buf, 0, buf.Length)
            cts.Cancel()
            resp5.Dispose()
            Await Task.Delay(1500)
            Check(AppCore.Lan.ActiveClients = 0, "disconnect stops generation", "active=" & AppCore.Lan.ActiveClients)

            Await AppCore.Lan.StopAsync()
            Await AppCore.Engine.StopAsync()
            Check(AppCore.Engine.State = EngineState.Stopped, "engine stopped")
        End If
    End Function
End Module
