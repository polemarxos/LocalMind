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

            Console.WriteLine("== Portable folders")
            Check(AppPaths.IsPortable AndAlso AppPaths.Roaming = Path.Combine(AppPaths.AppDir, "Data"), "settings/chats in Data\ next to exe", AppPaths.Roaming)
            Check(AppPaths.EngineFolder = Path.Combine(AppPaths.AppDir, "Engine") AndAlso AppPaths.DefaultModelsFolder = Path.Combine(AppPaths.AppDir, "Models"), "Engine\ and Models\ next to exe")

            Console.WriteLine("== Math rendering")
            Dim mh = MarkdownRenderer.ToHtml("Area \(\pi r^2\) and" & vbLf & "\[\frac{a}{b} = \sqrt{x^2+1}\]" & vbLf & "Keep `\(code\)` and $5 price." & vbLf & "```" & vbLf & "\[not math\]" & vbLf & "```")
            File.WriteAllText("/tmp/math.html", mh)
            Check(mh.Contains("<span class=""math"">") AndAlso mh.Contains("<div class=""math"">") AndAlso mh.Contains("<code>\(code\)</code>") AndAlso mh.Contains("\[not math\]"),
                  "LaTeX → math blocks, code untouched", mh.Replace(vbLf, " "))

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
                Check(best.Count >= 5 AndAlso best.Count <= 400 AndAlso allFit, $"{prof.Item1:0}GB VRAM: {best.Count} shown", "largest: " & String.Join(", ", sample))
            Next

            For Each vram In {8.0, 16.0}
                Dim h = Hw(vram, 32)
                Dim unc = ModelCatalog.BestThatFit(emb, h, 16384, uncensored:=True)
                Dim names = emb.Where(Function(e) unc.Contains(e.Repo)).Select(Function(e) e.Name).Take(6)
                Check(unc.Count >= 3 AndAlso emb.Where(Function(e) unc.Contains(e.Repo)).All(Function(e) e.IsUncensored),
                      $"{vram:0}GB VRAM uncensored best: {unc.Count} shown", String.Join(", ", names))
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

            Console.WriteLine("== Auto-update check (GitHub Releases)")
            Dim upd = Await Updater.CheckAsync(CancellationToken.None)
            Check(upd IsNot Nothing AndAlso upd.ZipUrl IsNot Nothing AndAlso upd.Version > Updater.CurrentVersion,
                  "finds newer release than this build", $"current {Updater.CurrentVersion} → {upd?.Tag} ({upd?.ZipUrl})")

            Dim gf = Await ModelCatalog.GetRepoFilesAsync("unsloth/gemma-4-12b-it-GGUF", Nothing, CancellationToken.None)
            Check(gf.Mtp IsNot Nothing AndAlso gf.Mtp.Path.Contains("mtp-") AndAlso gf.Quants.All(Function(q) q.Files.All(Function(f) Not f.Path.Contains("mtp-"))),
                  "MTP speed-up file detected and kept out of the quant list", $"{gf.Mtp?.Path} ({Downloader.FormatBytes(If(gf.Mtp?.Size, 0L))})")

            Console.WriteLine("== Auto quant on real files")
            For Each repoName In {"unsloth/gemma-4-12b-it-GGUF", "unsloth/Qwen3.6-35B-A3B-GGUF", "bartowski/Meta-Llama-3.1-8B-Instruct-GGUF"}
                Dim e = live.FirstOrDefault(Function(x) x.Repo = repoName)
                If e Is Nothing Then e = ModelCatalog.LoadEmbedded().First(Function(x) x.Repo = repoName)
                Dim files = Await ModelCatalog.GetRepoFilesAsync(repoName, Nothing, CancellationToken.None)
                Dim line = repoName & ": " & files.Quants.Count & " quants, mmproj=" & If(files.Mmproj?.Path, "none") & vbLf
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

        If only = "media" Then
            Console.WriteLine("== Image & video catalog")
            Dim recs = MediaStudio.Recipes()
            Check(recs.Count >= 20 AndAlso recs.All(Function(r) r.Variants.Count > 0 AndAlso (r.Parts.Count > 0 OrElse r.IsUpscaler OrElse r.Kind = "tool" OrElse r.Variants.All(Function(v) v.Format = "checkpoint"))) AndAlso
                  recs.All(Function(r) r.Tasks.Count > 0), "recipes load", recs.Count & " models, " & recs.Where(Function(r) r.Speed IsNot Nothing).Count() & " with ⚡ speed LoRAs")
            For Each prof In {(0.0, 16.0), (8.0, 32.0), (12.0, 32.0), (16.0, 64.0), (24.0, 64.0)}
                Dim h = Hw(prof.Item1, prof.Item2)
                Dim best = MediaStudio.BestForGpu(h)
                Dim picks = best.Select(Function(r) $"{r.Name} [{MediaStudio.PickVariant(r, h).Label}]")
                Check(prof.Item1 = 0 OrElse best.All(Function(r) MediaStudio.Fit(r, MediaStudio.PickVariant(r, h), h) = FitLevel.Fits),
                      $"{prof.Item1:0}GB VRAM/{prof.Item2:0}GB RAM: {best.Count} shown", String.Join(", ", picks))
            Next
            Dim anima = recs.First(Function(r) r.Id = "anima")
            Dim small = anima.Variants.OrderBy(Function(v) v.Size).First()
            If Environment.GetEnvironmentVariable("LM_CONVROT") = "1" Then small = anima.Variants.First(Function(v) v.Label.Contains("ConvRot"))
            Await MediaStudio.DownloadAsync(anima, small, Sub(p, t) Return, CancellationToken.None)
            Check(MediaStudio.IsInstalled(anima, small), "download Anima " & small.Label & " + VAE + text encoder",
                  String.Join(", ", MediaStudio.FilesFor(anima, small).Select(Function(f) IO.Path.GetFileName(MediaStudio.LocalPath(f)))))
            If ExternalTools.FindExe(New ExternalTools.ToolPackage With {.Name = "sd", .Flavor = "cpu", .ExeName = "sd-cli.exe"}) IsNot Nothing Then
                Dim last = ""
                Dim sw = Diagnostics.Stopwatch.StartNew()
                Dim out = Await MediaStudio.GenerateAsync(New MediaStudio.GenerateRequest With {
                    .Recipe = anima, .Variant = small, .Prompt = "a red apple on a wooden table, illustration", .Negative = anima.Negative,
                    .Width = 384, .Height = 384, .Steps = 6, .Cfg = anima.Cfg, .Seed = 42}, Sub(p, t) last = $"{p}% {t}", CancellationToken.None)
                Dim ok = File.Exists(out) AndAlso New FileInfo(out).Length > 10000
                Check(ok, "generated an image with sd.cpp", $"{out} ({If(ok, New FileInfo(out).Length \ 1024, 0)} KB, {sw.Elapsed.TotalSeconds:0}s, last progress: {last})")
                If ok Then File.Copy(out, "/tmp/anima-test.png", True)
            Else
                Console.WriteLine("  SKIP generation (no sd-cli in Engine/sd/cpu)")
            End If
        End If

        If only = "edit" Then
            Console.WriteLine("== Picture editing with sd.cpp")
            Dim recs = MediaStudio.Recipes()
            Dim src = "/tmp/anima-test.png"
            Dim Run = Async Function(rid As String, label As String, req As MediaStudio.GenerateRequest) As Task(Of String)
                          Dim r = recs.First(Function(x) x.Id = rid)
                          Dim v = If(label Is Nothing, r.Variants.OrderBy(Function(x) x.Size).First(), r.Variants.First(Function(x) x.Label = label))
                          Await MediaStudio.DownloadAsync(r, v, Sub(p, t) Return, CancellationToken.None)
                          req.Recipe = r : req.Variant = v
                          If req.Steps = 0 Then req.Steps = r.Steps
                          If req.Cfg = 0 Then req.Cfg = r.Cfg
                          If req.Width = 0 Then req.Width = 384 : req.Height = 384
                          req.Seed = 7
                          Dim sw = Diagnostics.Stopwatch.StartNew()
                          Dim last = ""
                          Dim out = Await MediaStudio.GenerateAsync(req, Sub(p, t) last = t, CancellationToken.None)
                          Console.WriteLine($"     {rid}: {sw.Elapsed.TotalSeconds:0}s, last: {last}")
                          Return out
                      End Function
            Dim want = If(Environment.GetEnvironmentVariable("LM_EDIT"), "upscale,inpaint,edit,combine").Split(","c)

            If want.Contains("upscale") Then
                Dim up = Await Run("upscale-realesrgan", Nothing, New MediaStudio.GenerateRequest With {.InputImages = {src}.ToList()})
                Dim hdr = File.ReadAllBytes(up).Skip(16).Take(8).ToArray()
                Dim w = (CInt(hdr(0)) << 24) Or (CInt(hdr(1)) << 16) Or (CInt(hdr(2)) << 8) Or hdr(3)
                Dim h = (CInt(hdr(4)) << 24) Or (CInt(hdr(5)) << 16) Or (CInt(hdr(6)) << 8) Or hdr(7)
                Check(w = 1536 AndAlso h = 1536, "upscale 4×", $"{up} → {w}×{h}")
                File.Copy(up, "/tmp/edit-upscale.png", True)
            End If
            If want.Contains("inpaint") Then
                Dim mask = "/tmp/mask.png"
                Dim ffm = Diagnostics.Process.Start("ffmpeg", "-y -loglevel error -f lavfi -i color=black:s=384x384 -vf ""drawbox=x=96:y=60:w=200:h=270:color=white:t=fill"" -frames:v 1 " & mask)
                ffm.WaitForExit()
                Dim inp = Await Run("anima", "Q3_K_L", New MediaStudio.GenerateRequest With {.Task = "inpaint", .InputImages = {src}.ToList(), .MaskImage = mask,
                    .Strength = 1.0, .Prompt = "a green pear on a wooden table, illustration", .Steps = 8})
                Check(File.Exists(inp), "inpaint (brush area)", inp)
                File.Copy(inp, "/tmp/edit-inpaint.png", True)
            End If
            If want.Contains("edit") Then
                Dim ed = Await Run("flux2-klein-4b", "Q4_0", New MediaStudio.GenerateRequest With {.Task = "edit", .InputImages = {src}.ToList(),
                    .Prompt = "make the apple blue"})
                Check(File.Exists(ed), "edit with a reference picture (FLUX.2 klein)", ed)
                File.Copy(ed, "/tmp/edit-edit.png", True)
            End If
            If want.Contains("combine") Then
                Dim cb = Await Run("flux2-klein-4b", "Q4_0", New MediaStudio.GenerateRequest With {.Task = "combine", .InputImages = {src, "/tmp/edit-inpaint.png"}.ToList(),
                    .Prompt = "the apple from image 1 and the pear from image 2 side by side on a table"})
                Check(File.Exists(cb), "combine two pictures (FLUX.2 klein)", cb)
                File.Copy(cb, "/tmp/edit-combine.png", True)
            End If
            If want.Contains("frames") Then
                Dim vid = "/tmp/src-video.mp4"
                Diagnostics.Process.Start("ffmpeg", "-y -loglevel error -f lavfi -i testsrc2=s=320x240:d=3:r=25 -c:v libx264 -pix_fmt yuv420p " & vid).WaitForExit()
                Dim dir = Await VideoTools.ExtractFramesAsync(vid, 16, 256, 256, 33, Nothing, CancellationToken.None)
                Dim n = Directory.GetFiles(dir, "*.png").Length
                Check(n = 33, "video read into frames for the video model", $"{n} frames at 16 fps, 256×256")
                Directory.Delete(dir, True)
            End If
            If want.Contains("video") Then
                Dim vid = "/tmp/src-video.mp4"
                Diagnostics.Process.Start("ffmpeg", "-y -loglevel error -f lavfi -i testsrc2=s=320x240:d=2:r=16 -c:v libx264 -pix_fmt yuv420p " & vid).WaitForExit()
                Dim ve = Await Run("wan2.1-vace-1.3b", Nothing, New MediaStudio.GenerateRequest With {.Task = "video-edit", .InputVideo = vid,
                    .Prompt = "colorful stained glass window, animated", .Negative = "", .Width = 256, .Height = 256, .Frames = 9, .Steps = 4, .Strength = 1.0})
                Check(File.Exists(ve) AndAlso New FileInfo(ve).Length > 5000, "video edit (Wan VACE)", ve)
                Dim mp4 = Await VideoTools.ToMp4Async(ve, Nothing, CancellationToken.None)
                Check(File.Exists(mp4), "converted to MP4", mp4)
                File.Copy(mp4, "/tmp/edit-video.mp4", True)
            End If
        End If

        If only = "audio" Then
            Console.WriteLine("== Voice & music (llama-tts)")
            AppCore.Settings.Backend = BackendKind.BuiltIn
            Check(AudioStudio.Models().Count >= 7, "audio catalog", String.Join(", ", AudioStudio.Models().Select(Function(m) m.Id)))
            Check(AudioStudio.TtsExe() IsNot Nothing, "llama-tts found next to the engine", AudioStudio.TtsExe())
            Dim want = If(Environment.GetEnvironmentVariable("LM_AUDIO"), "speech,clone").Split(","c)
            Dim cb = AudioStudio.Models().First(Function(m) m.Id = "chatterbox-ml")
            Dim sw = Diagnostics.Stopwatch.StartNew()
            Dim first As String = Nothing
            If want.Contains("speech") OrElse want.Contains("clone") Then
                first = Await AudioStudio.GenerateAsync(New AudioStudio.AudioRequest With {.Model = cb, .Language = "el", .Seed = 1,
                    .Text = "Καλημέρα! Αυτή είναι μια δοκιμή της ελληνικής φωνής του LocalMind."}, Sub(p, t) Return, CancellationToken.None)
                Check(File.Exists(first) AndAlso New FileInfo(first).Length > 20000, "Greek speech (Chatterbox Multilingual)", $"{first} ({New FileInfo(first).Length \ 1024} KB, {sw.Elapsed.TotalSeconds:0}s)")
                File.Copy(first, "/tmp/tts-greek.wav", True)
            End If
            If want.Contains("clone") Then
                sw.Restart()
                Dim cl = Await AudioStudio.GenerateAsync(New AudioStudio.AudioRequest With {.Model = cb, .Language = "en", .Seed = 2, .SpeakerFile = first,
                    .Text = "Hello, this sentence is spoken in a cloned voice."}, Sub(p, t) Return, CancellationToken.None)
                Check(File.Exists(cl) AndAlso New FileInfo(cl).Length > 20000, "voice cloning from a recording", $"{cl} ({sw.Elapsed.TotalSeconds:0}s)")
                File.Copy(cl, "/tmp/tts-clone.wav", True)
            End If
            If want.Contains("phone") Then
                AppCore.Settings.ReadAloudModel = "chatterbox-ml"
                Dim wavP = Await Voice.SpeakToFileAsync("Hello there. This answer is read for the phone. It has three sentences.", CancellationToken.None)
                Dim lenP = New FileInfo(wavP).Length
                Check(lenP > 40000, "answer spoken into one WAV for the phone (AI voice)", $"{lenP \ 1024} KB")
                AppCore.Settings.ReadAloudModel = ""
            End If
            If want.Contains("music") Then
                sw.Restart()
                Dim yue = AudioStudio.Models().First(Function(m) m.Id = "yue2-3b")
                Dim song = Await AudioStudio.GenerateAsync(New AudioStudio.AudioRequest With {.Model = yue, .Seed = 3, .Seconds = 8,
                    .Text = "acoustic pop, happy, guitar, female vocals", .Lyrics = "[verse]" & vbLf & "Sun is shining over the sea"}, Sub(p, t) Return, CancellationToken.None)
                Check(File.Exists(song), "song (YuE2)", $"{song} ({sw.Elapsed.TotalSeconds:0}s)")
                File.Copy(song, "/tmp/song.wav", True)
            End If
        End If

        If only = "lora" Then
            Console.WriteLine("== LoRAs & Civitai")
            ' Metadata reading from a hand-made safetensors header (kohya style).
            Dim fake = Path.Combine(Path.GetTempPath(), "my_style_sdxl.safetensors")
            Dim hdr = "{""__metadata__"":{""ss_base_model_version"":""sdxl_base_v1-0"",""ss_tag_frequency"":""{\""10_mystyle\"":{\""mystyle\"":40,\""1girl\"":12,\""outdoors\"":5}}""},""w"":{""dtype"":""F32"",""shape"":[1],""data_offsets"":[0,4]}}"
            Dim hb = Text.Encoding.UTF8.GetBytes(hdr)
            Using fs = File.Create(fake)
                fs.Write(BitConverter.GetBytes(CLng(hb.Length)))
                fs.Write(hb)
                fs.Write(New Byte(3) {})
            End Using
            Dim meta = LoraLibrary.ReadMetadata(fake)
            Check(meta.Family = "SDXL" AndAlso meta.Triggers.StartsWith("mystyle"), "trigger words + family from LoRA metadata", $"{meta.Family}: {meta.Triggers}")

            Dim res = Await Civitai.SearchAsync("", True, {"Anima"}, False, "Most Downloaded", Nothing, CancellationToken.None)
            Check(res.Items.Count > 0 AndAlso res.Items.All(Function(m) m.Versions(0).BaseModel = "Anima"), "Civitai search (LoRAs for Anima)",
                  $"{res.Items.Count} results, e.g. {res.Items(0).Name}")
            Dim ck = Await Civitai.SearchAsync("", False, {"SDXL 1.0"}, False, "Most Downloaded", Nothing, CancellationToken.None)
            Check(ck.Items.Count > 0 AndAlso ck.Items.All(Function(m) Not m.IsLora), "Civitai search (SDXL checkpoints)", $"{ck.Items.Count} results, e.g. {ck.Items(0).Name}")
            Dim pick = res.Items.First(Function(m) m.Name.Contains("Turbo"))
            Dim authMsg = ""
            Try
                Dim needsLogin = res.Items.First(Function(m) m.Versions(0).Files.Any(Function(f) f.DownloadUrl.Contains("3078972")))
                Await Civitai.InstallLoraAsync(needsLogin, needsLogin.Versions(0), Sub(p, t) Return, CancellationToken.None)
            Catch ex As Exception
                authMsg = ex.Message
            End Try
            Check(authMsg.Contains("API key"), "download that needs a Civitai account asks for the API key", authMsg)
            Dim l = Await Civitai.InstallLoraAsync(pick, pick.Versions(0), Sub(p, t) Return, CancellationToken.None)
            Check(l IsNot Nothing AndAlso File.Exists(l.FullPath) AndAlso l.Family = "Anima" AndAlso l.Enabled, "LoRA installed from Civitai",
                  $"{l?.Name} ({New FileInfo(l.FullPath).Length \ 1024} KB), triggers: '{l?.Triggers}'")
            If ExternalTools.FindExe(New ExternalTools.ToolPackage With {.Name = "sd", .Flavor = "cpu", .ExeName = "sd-cli.exe"}) IsNot Nothing Then
                Dim anima = MediaStudio.Recipes().First(Function(x) x.Id = "anima")
                Dim v = anima.Variants.First(Function(x) x.Label = "Q3_K_L")
                Dim log As New List(Of String)
                Dim out = Await MediaStudio.GenerateAsync(New MediaStudio.GenerateRequest With {.Recipe = anima, .Variant = v, .Prompt = "a cat in a garden",
                    .Width = 384, .Height = 384, .Steps = 8, .Cfg = anima.Cfg, .Seed = 5, .Loras = {l}.ToList()}, Sub(p, t) log.Add(t), CancellationToken.None)
                Check(File.Exists(out), "generated with the Civitai LoRA", out)
                File.Copy(out, "/tmp/lora-test.png", True)
            End If
        End If

        If only = "v26" Then
            Console.WriteLine("== 2.6 helpers")
            ' Files from renamed Hugging Face repos are found instead of downloaded again.
            Dim anima = MediaStudio.Recipes().First(Function(x) x.Id = "anima")
            Dim vae = anima.Parts.First(Function(p) p.Role = "vae")
            Dim target = MediaStudio.LocalPath(vae)
            If File.Exists(target) Then
                Dim oldDir = Path.Combine(MediaStudio.Folder, "Comfy-Org__Old_Name_Repack")
                Directory.CreateDirectory(oldDir)
                File.Move(target, Path.Combine(oldDir, Path.GetFileName(target)), True)
                Check(MediaStudio.IsPresent(vae) AndAlso File.Exists(target), "file in an old repo folder is reused, not downloaded again", target)
            End If
            ' Several pictures at once + saved settings for "Reuse".
            Dim v = anima.Variants.First(Function(x) x.Label = "Q3_K_L")
            If MediaStudio.IsInstalled(anima, v) AndAlso ExternalTools.FindExe(New ExternalTools.ToolPackage With {.Name = "sd", .Flavor = "cpu", .ExeName = "sd-cli.exe"}) IsNot Nothing Then
                Dim req As New MediaStudio.GenerateRequest With {.Recipe = anima, .Variant = v, .Prompt = "a blue bird", .Width = 256, .Height = 256, .Steps = 4, .Cfg = 6, .Count = 2}
                Await MediaStudio.GenerateAsync(req, Sub(p, t) Return, CancellationToken.None)
                Dim s0 = MediaStudio.LoadSettingsFor(req.Outputs(0))
                Check(req.Outputs.Count = 2 AndAlso req.Outputs.All(AddressOf File.Exists), "2 variations in one run", String.Join(", ", req.Outputs.Select(AddressOf Path.GetFileName)))
                Check(s0 IsNot Nothing AndAlso s0.Prompt = "a blue bird" AndAlso s0.Seed > 0 AndAlso MediaStudio.LoadSettingsFor(req.Outputs(1)).Seed = s0.Seed + 1,
                      "settings saved per picture (seed, prompt, model)", $"seed {s0?.Seed}, {s0?.Recipe} {s0?.Variant}")
            End If
            ' Memory estimate shown before download.
            Dim w22 = MediaStudio.Recipes().First(Function(x) x.Id = "wan2.2-t2v-a14b")
            Dim need = MediaStudio.Requirement(w22, MediaStudio.PickVariant(w22, Hw(8, 16)), Hw(8, 16))
            Check(need.RamGB > 10 AndAlso MediaStudio.Fit(w22, w22.Variants.Last(), Hw(8, 16)) = FitLevel.TooBig, "memory needs shown before downloading big models",
                  $"Wan 2.2 A14B on 8 GB GPU + 16 GB RAM: needs ~{need.VramGB} GB GPU + {need.RamGB} GB RAM")
            ' Joining speech pieces for the phone.
            Dim tts = Directory.GetFiles(MediaStudio.OutputFolder, "*chatterbox*.wav").Take(2).ToList()
            If tts.Count = 2 Then
                Dim joined = Path.Combine(Path.GetTempPath(), "joined.wav")
                Voice.JoinWavs(tts, joined)
                Dim expect = tts.Sum(Function(f) New FileInfo(f).Length - 44)
                Check(Math.Abs(New FileInfo(joined).Length - 44 - expect) < 200, "speech pieces joined into one WAV", $"{New FileInfo(joined).Length \ 1024} KB")
            End If
            Check(Voice.Chunks("First sentence. Second one! " & New String("x"c, 300) & ". Last.").Count >= 2, "long answers are read sentence by sentence")
        End If

        If only = "v27" Then
            Console.WriteLine("== 2.6.1: uncensored pictures & video, videos from the chat")
            Dim unc = MediaStudio.Recipes().Where(Function(r) r.Uncensored).ToList()
            Check(unc.Where(Function(r) Not r.IsVideo).Count() >= 5 AndAlso unc.Where(Function(r) r.IsVideo).Count() >= 3, "uncensored picture and video models in the catalog",
                  String.Join(", ", unc.Select(Function(r) r.Id)))
            Dim h16 = Hw(16, 32)
            Dim normal = MediaStudio.BestForGpu(h16)
            Dim uncFit = MediaStudio.BestForGpu(h16, uncensored:=True)
            Check(Not normal.Any(Function(r) r.Uncensored) AndAlso uncFit.Count >= 4 AndAlso uncFit.All(Function(r) r.Uncensored OrElse r.IsUpscaler OrElse r.Kind = "tool"),
                  "16 GB GPU: uncensored list has its own best picks", String.Join(", ", uncFit.Where(Function(r) r.Uncensored).Select(Function(r) r.Id)))
            Dim wn = unc.First(Function(r) r.Id = "wan2.2-i2v-a14b-nsfw")
            Dim files = MediaStudio.FilesFor(wn, wn.Variants.First())
            Check(files.OfType(Of MediaLora)().Count() = 2 AndAlso files.OfType(Of MediaLora)().All(Function(l) l.Name.StartsWith(LoraLibrary.BuiltInPrefix)),
                  "uncensored Wan 2.2 downloads its two built-in LoRAs (hidden from My LoRAs)", String.Join(", ", files.OfType(Of MediaLora)().Select(Function(l) l.Name)))
            Dim baseI2v = MediaStudio.Recipes().First(Function(r) r.Id = "wan2.2-i2v-a14b")
            Check(wn.Variants.First().Path = baseI2v.Variants.First().Path AndAlso wn.Variants.First().Repo = baseI2v.Variants.First().Repo,
                  "it reuses the normal Wan 2.2 I2V files (no second download)")
            Dim pony = unc.First(Function(r) r.Id = "pony-v6-xl")
            Check(pony.Prefix.StartsWith("score_9") AndAlso pony.Extra.Contains("--clip-skip 2"), "Pony gets its quality tags and clip skip")

            AppCore.Settings.AgentTools = True
            AppCore.Settings.EnableImages = True
            Dim tl = ChatAgent.Tools("any-model", False)
            Dim names = tl.Select(Function(t) t("function").Value(Of String)("name")).ToList()
            Check(names.Contains("generate_video") AndAlso names.Contains("generate_image"), "chat offers picture and video tools", String.Join(", ", names))
            For Each c In {("generate me a video", "generate_video"), ("Generate me an image", "generate_image"),
                           ("make a short video of a cat surfing", "generate_video"), ("draw me a dragon", "generate_image"),
                           ("can you create a picture of a sunset over Santorini?", "generate_image"),
                           ("animate this photo into a video", "generate_video"),
                           ("φτιάξε μου ένα βίντεο με μια γάτα", "generate_video"), ("φτιάξε μου μια εικόνα με ένα σκύλο", "generate_image"),
                           ("how do I make a video in Premiere?", CStr(Nothing)), ("πώς φτιάχνω βίντεο στο κινητό;", CStr(Nothing)),
                           ("what is a good video card for gaming?", CStr(Nothing)), ("tell me a joke", CStr(Nothing))}
                Dim got = ChatAgent.RequestedTool(c.Item1, tl)
                Check(got = c.Item2, $"'{c.Item1}' → {If(c.Item2, "normal answer")}", If(got, "(none)"))
            Next
            ' Bigger model list, Ollama-style names
            Dim emb2 = ModelCatalog.LoadEmbedded()
            Check(emb2.Count >= 6000, "model list is about 10x bigger", emb2.Count & " models")
            For Each c In {("Qwen3.8-27B-Uncensored:iq4_xs", "Qwen3.8-27B-Uncensored", "IQ4_XS"),
                           ("hf.co/bartowski/Some-Model-GGUF:Q4_K_M", "bartowski/Some-Model-GGUF", "Q4_K_M"),
                           ("ollama run gemma-4-12b-it:q8_0", "gemma-4-12b-it", "Q8_0"),
                           ("qwen coder", "qwen coder", CStr(Nothing))}
                Dim sq = ModelCatalog.SplitQuery(c.Item1)
                Check(sq.Name = c.Item2 AndAlso sq.Quant = c.Item3, $"'{c.Item1}' → {sq.Name} + {If(sq.Quant, "Auto")}")
            Next
            Check(emb2.Any(Function(e) e.Repo.Contains("Qwen3.8-27B-Uncensored")), "Qwen3.8-27B-Uncensored is in the list")
            For Each c In {("mradermacher/Foo-7B-i1-GGUF", "Foo-7B"), ("RichardErkhov/org_-_Bar-8B-gguf", "Bar-8B"),
                           ("DevQuasar/Qwen.Qwen3-8B-GGUF", "Qwen3-8B"), ("Lewdiculous/L3-8B-Stheno-v3.2-GGUF-IQ-Imatrix", "L3-8B-Stheno-v3.2")}
                Check(ModelCatalog.NormalizeName(c.Item1) = c.Item2, $"name of {c.Item1}", ModelCatalog.NormalizeName(c.Item1))
            Next
            Dim h16b = Hw(16, 32)
            Dim best16 = ModelCatalog.BestThatFit(emb2, h16b, 16384)
            Check(best16.Count >= 150, "16 GB GPU: many more best picks", best16.Count & " models")
            Dim sw = Diagnostics.Stopwatch.StartNew()
            Dim live = Await ModelCatalog.FetchLiveAsync(CancellationToken.None)
            Check(live.Count >= 6000, "live list from Hugging Face", $"{live.Count} models in {sw.Elapsed.TotalSeconds:0} s: " &
                  String.Join(", ", live.GroupBy(Function(x) x.Author).OrderByDescending(Function(g) g.Count()).Select(Function(g) $"{g.Key} {g.Count()}")))
            ' Personas: built-ins are added once, user personas are kept
            Dim ps As New PersonaService()
            If IO.File.Exists(ps.FilePath) Then IO.File.Delete(ps.FilePath)
            Dim seenFile = IO.Path.Combine(IO.Path.GetDirectoryName(ps.FilePath), "personas-builtin.json")
            If IO.File.Exists(seenFile) Then IO.File.Delete(seenFile)
            IO.File.WriteAllText(ps.FilePath, "[{""Name"":""Helpful Assistant"",""SystemPrompt"":""x""},{""Name"":""My own"",""SystemPrompt"":""y""}]")
            Dim pl = ps.Load()
            Check(pl.Count = PersonaService.BuiltIns().Count + 1 AndAlso pl.Any(Function(x) x.Name = "My own"), "built-in personas added, own ones kept, no duplicate assistant", pl.Count & " personas")
            pl.RemoveAll(Function(x) x.Name.Contains("Chef"))
            ps.Save(pl)
            Check(Not ps.Load().Any(Function(x) x.Name.Contains("Chef")), "a deleted built-in persona does not come back")
            ' Many more picture and video models
            Dim sw2 = Diagnostics.Stopwatch.StartNew()
            Dim allMedia = MediaStudio.Recipes()
            Dim installedCount = allMedia.Where(Function(r) MediaStudio.InstalledVariant(r) IsNot Nothing).Count()
            Dim t1 = sw2.Elapsed.TotalSeconds
            Dim bestMedia = MediaStudio.BestForGpu(Hw(16, 32))
            Check(allMedia.Count >= 700, "picture & video models: about 20x more", $"{allMedia.Count} models ({allMedia.Where(Function(r) r.IsVideo).Count()} video, {allMedia.Where(Function(r) r.Uncensored).Count()} uncensored), {installedCount} installed, checked in {t1:0.0} s")
            Check(t1 < 5, "checking what is installed stays fast", $"{t1:0.00} s")
            Check(allMedia.Where(Function(r) r.Auto).All(Function(r) r.Variants.Count > 0 AndAlso Not String.IsNullOrEmpty(r.Family)), "every community model has files and a family")
            Check(allMedia.Any(Function(r) r.Variants.Any(Function(v) v.Path.Contains("qwen_image_edit_2511_int8_convrot"))) AndAlso
                  allMedia.Any(Function(r) r.Variants.Any(Function(v) v.Path.ToLowerInvariant().Contains("krea2-turbo-int8"))), "ConvRot models like qwen_image_edit_2511 and Krea2-Turbo are listed")
            Check(bestMedia.Count >= 20 AndAlso bestMedia.Count < 400, "Studio 'best for my GPU' stays a short list", bestMedia.Count & " models")
            ' Model pages: description, sample pictures, settings
            For Each id In {"realvisxl-v5", "flux1-dev", "hf-rundiffusion-juggernaut-xl-v9", "wan2.2-i2v-a14b"}
                Dim rr = MediaStudio.Recipes().FirstOrDefault(Function(x) x.Id = id)
                If rr Is Nothing Then Continue For
                Dim inf = Await ModelDetails.GetAsync(rr, False, CancellationToken.None)
                Dim firstImg = False
                If inf.Samples.Count > 0 Then
                    Try
                        firstImg = (Await Downloader.Http.GetByteArrayAsync(inf.Samples(0).Url)).Length > 1000
                    Catch
                    End Try
                End If
                Check(inf.Description.Length > 50 AndAlso (firstImg OrElse rr.IsVideo OrElse rr.Id = "flux1-dev"), $"model page for {rr.Name}",
                      $"{inf.Samples.Count} pictures (first loads: {firstImg}), {inf.Description.Length} chars, likes {inf.Likes}, " &
                      $"civitai: {If(inf.CivitaiUrl, "")} {inf.CivitaiSteps} | author: {inf.AuthorSettings} | {String.Join(" / ", ModelDetails.SettingsLines(rr).Take(2))}")
            Next
            Dim msg = Await ChatAgent.MakeVideoAsync("a cat", Nothing, Sub(t) Return, CancellationToken.None)
            Check(msg.Contains("video model"), "no video model yet: a helpful note instead of an error", msg)
        End If

        If only = "v211" Then
            Console.WriteLine("== 2.11: storyboard, first → last, talking characters, extend, enhance, cast in chat")
            Check(VideoMaker.FramesFor(5, 16, 81) = 81 AndAlso VideoMaker.FramesFor(1.2, 16, 81) = 21 AndAlso VideoMaker.FramesFor(0.05, 16, 81) = 5 AndAlso
                  VideoMaker.FramesFor(9, 16, 81) = 81, "frame counts are 4n+1 and capped",
                  $"{VideoMaker.FramesFor(5, 16, 81)} {VideoMaker.FramesFor(1.2, 16, 81)} {VideoMaker.FramesFor(0.05, 16, 81)}")
            Dim shots = VideoMaker.SplitShots("1. a cat wakes up" & vbCrLf & "- it stretches" & vbLf & "Shot 3: it jumps" & vbCrLf & vbCrLf & "  2) it runs away  " & vbLf & "Πλάνο 5: τέλος")
            Check(shots.SequenceEqual({"a cat wakes up", "it stretches", "it jumps", "it runs away", "τέλος"}), "storyboard shots: one per line, numbers and bullets removed", String.Join(" | ", shots))
            Dim s2v = MediaStudio.Recipes().FirstOrDefault(Function(r) r.Id = "wan2.2-s2v-14b")
            Check(s2v IsNot Nothing AndAlso s2v.IsVideo AndAlso s2v.Can("talk") AndAlso s2v.Parts.Any(Function(p) p.Role = "audio-encoder") AndAlso s2v.Variants.Count >= 5,
                  "talking-video model (Wan 2.2 S2V) with its voice encoder", If(s2v Is Nothing, "missing", String.Join(", ", s2v.Variants.Select(Function(v) v.Label))))
            Dim flf = MediaStudio.Recipes().FirstOrDefault(Function(r) r.Id = "wan2.1-flf2v-14b-720p")
            Check(flf IsNot Nothing AndAlso flf.Can("first-last") AndAlso flf.Parts.Any(Function(p) p.Role = "clip_vision") AndAlso
                  MediaStudio.Recipes().First(Function(r) r.Id = "wan2.2-i2v-a14b").Can("first-last"), "first → last frame models (Wan 2.1 FLF2V, Wan 2.2 I2V)")
            Check(Not MediaStudio.Recipes().Any(Function(r) r.Can("talk") AndAlso (r.Can("create") OrElse r.Can("animate"))), "the talking model is only offered for talking")
            Check(ChatAgent.PickVideoRecipe(False) Is Nothing OrElse Not ChatAgent.PickVideoRecipe(False).Can("talk"), "the chat never picks the talking model for a plain video")

            ' Video helpers on real files (ffmpeg)
            Dim tmp = Path.Combine(Path.GetTempPath(), "lm-v211")
            If Directory.Exists(tmp) Then Directory.Delete(tmp, True)
            Directory.CreateDirectory(tmp)
            Dim ff = Await VideoTools.EnsureFfmpegAsync(Nothing, CancellationToken.None)
            Dim mk = Sub(cmd As String)
                         Using p = Diagnostics.Process.Start(New Diagnostics.ProcessStartInfo(ff, "-hide_banner -loglevel error -y " & cmd) With {.UseShellExecute = False, .RedirectStandardError = True})
                             p.StandardError.ReadToEnd()
                             p.WaitForExit()
                         End Using
                     End Sub
            Dim a = Path.Combine(tmp, "a.webm"), b = Path.Combine(tmp, "b.webm"), snd = Path.Combine(tmp, "voice.wav")
            mk($"-f lavfi -i testsrc=size=64x48:rate=16:duration=2 -c:v libvpx-vp9 -b:v 200k ""{a}""")
            mk($"-f lavfi -i testsrc2=size=96x64:rate=16:duration=1 -c:v libvpx-vp9 -b:v 200k ""{b}""")
            mk($"-f lavfi -i sine=frequency=440:duration=7 -ar 44100 ""{snd}""")
            Dim ia = Await VideoTools.InfoAsync(a, CancellationToken.None)
            Check(ia.Width = 64 AndAlso ia.Height = 48 AndAlso Math.Abs(ia.Fps - 16) < 0.01 AndAlso Math.Abs(ia.Seconds - 2) < 0.2 AndAlso Not ia.HasAudio,
                  "video size, frame rate and length are read", $"{ia.Width}x{ia.Height} {ia.Fps} fps {ia.Seconds:0.00}s audio={ia.HasAudio}")
            Dim lastPng = Await VideoTools.LastFrameAsync(a, CancellationToken.None)
            Check(File.Exists(lastPng) AndAlso New FileInfo(lastPng).Length > 100, "last frame saved (start of the next shot)", lastPng)
            Dim joined = Path.Combine(tmp, "joined.mp4")
            Await VideoTools.JoinAsync({a, b}, joined, True, Nothing, CancellationToken.None)
            Dim ij = Await VideoTools.InfoAsync(joined, CancellationToken.None)
            Check(ij.Width = 64 AndAlso ij.Height = 48 AndAlso Math.Abs(ij.Seconds - 47 / 16.0) < 0.15, "clips of different sizes joined (repeated first frame dropped)",
                  $"{ij.Width}x{ij.Height} {ij.Seconds:0.000}s (expected {47 / 16.0:0.000}s)")
            Dim piece = Path.Combine(tmp, "piece.wav")
            Await VideoTools.CutAudioAsync(snd, 5, 5, piece, CancellationToken.None)
            Dim ip = Await VideoTools.InfoAsync(piece, CancellationToken.None)
            Check(Math.Abs(ip.Seconds - 2) < 0.1, "last 2 s of a 7 s recording cut for the talking model", $"{ip.Seconds:0.00}s")
            Dim enh = Path.Combine(tmp, "enh.mp4")
            Await VideoTools.EnhanceAsync(a, Nothing, True, True, enh, Nothing, CancellationToken.None)
            Dim ie = Await VideoTools.InfoAsync(enh, CancellationToken.None)
            Check(ie.Width = 128 AndAlso ie.Height = 96 AndAlso Math.Abs(ie.Fps - 32) < 0.01, "enhance: twice the size and twice the frame rate", $"{ie.Width}x{ie.Height} {ie.Fps} fps {ie.Seconds:0.00}s")
            ' with sound: the sound is kept
            Dim withSound = Path.Combine(tmp, "withsound.mp4")
            Await VideoTools.AddAudioAsync(joined, snd, False, 1.0, withSound, Nothing, CancellationToken.None)
            Dim enh2 = Path.Combine(tmp, "enh2.mp4")
            Await VideoTools.EnhanceAsync(withSound, Nothing, False, True, enh2, Nothing, CancellationToken.None)
            Dim ie2 = Await VideoTools.InfoAsync(enh2, CancellationToken.None)
            Check(ie2.HasAudio AndAlso ie2.Width = 64 AndAlso Math.Abs(ie2.Fps - 32) < 0.01, "smoother only: same size, sound kept", $"{ie2.Width}x{ie2.Height} {ie2.Fps} fps audio={ie2.HasAudio}")

            ' AI enlarger frame by frame (Real-ESRGAN with sd-cli)
            Dim up = MediaStudio.Recipes().FirstOrDefault(Function(r) r.Id = "upscale-realesrgan")
            If up IsNot Nothing Then
              Try
                Await MediaStudio.DownloadAsync(up, up.Variants(0), Sub(p, t) Return, CancellationToken.None)
                Dim tiny = Path.Combine(tmp, "tiny.webm")
                mk($"-f lavfi -i testsrc=size=64x48:rate=8:duration=0.5 -c:v libvpx-vp9 -b:v 200k ""{tiny}""")
                Dim outAi = Await VideoMaker.EnhanceAsync(tiny, up, True, False, Sub(p, t) Return, CancellationToken.None)
                Dim iai = Await VideoTools.InfoAsync(outAi, CancellationToken.None)
                Check(iai.Width = 128 AndAlso iai.Height = 96 AndAlso iai.Seconds > 0.3, "enhance with the AI enlarger, frame by frame", $"{outAi} {iai.Width}x{iai.Height} {iai.Seconds:0.00}s")
                Check(Not Directory.GetFiles(MediaStudio.OutputFolder, "up_*.png").Any(), "no frame files left in the outputs folder")
              Catch ex As Exception
                Console.WriteLine("  SKIP AI enlarger (engine not available here): " & ex.Message)
              End Try
            End If

            ' Cast in the chat: @Name gets the description; no picture model → a note, not an error
            Dim castList = CastLibrary.Load()
            castList.RemoveAll(Function(m) m.Name = "Zoe")
            Dim zoe As New CastLibrary.CastMember With {.Name = "Zoe", .Description = "a girl with curly black hair and a yellow raincoat"}
            castList.Add(zoe)
            CastLibrary.Save(castList)
            CastLibrary.AddImage(zoe, lastPng)
            CastLibrary.Save(castList)
            Dim inv = CastLibrary.Involved("draw @Zoe in the rain", New List(Of CastLibrary.CastMember)())
            Check(inv.Count = 1 AndAlso CastLibrary.ComposePrompt("@Zoe jumps in a puddle", inv).Contains("yellow raincoat"), "@Name in a chat request brings the character's look",
                  CastLibrary.ComposePrompt("@Zoe jumps in a puddle", inv))
            Check(CastLibrary.ComposePrompt("Zoe jumps in a puddle", inv).Contains("Character: Zoe (a girl with curly"), "even when the chat model drops the @",
                  CastLibrary.ComposePrompt("Zoe jumps in a puddle", inv))
            Dim note = Await ChatAgent.MakePictureAsync("@Zoe in the rain", Nothing, Sub(t) Return, CancellationToken.None, inv)
            Check(note.Length > 0, "picture request with the cast runs", note.Split({vbLf}, StringSplitOptions.None)(0))
            castList.RemoveAll(Function(m) m.Name = "Zoe")
            CastLibrary.Save(castList)
        End If

        If only = "v215" Then
            Console.WriteLine("== 2.15: voice cloning from the chat")
            Dim tools As New Newtonsoft.Json.Linq.JArray(
                New Newtonsoft.Json.Linq.JObject From {{"type", "function"}, {"function", New Newtonsoft.Json.Linq.JObject From {{"name", "clone_voice"}}}},
                New Newtonsoft.Json.Linq.JObject From {{"type", "function"}, {"function", New Newtonsoft.Json.Linq.JObject From {{"name", "generate_video"}}}})
            For Each q In {"clone this voice and say: hello there", "Use this voice to say good morning", "say it in her voice",
                           "κλωνοποίησε τη φωνή και πες: καλημέρα", "πες το με αυτή τη φωνή", "voice clone: welcome to the show"}
                Check(ChatAgent.RequestedTool(q, tools) = "clone_voice", "asks to clone a voice", q)
            Next
            For Each q In {"make a video of a cat", "what is a voice actor?", "how do I clone a voice?"}
                Check(ChatAgent.RequestedTool(q, tools) <> "clone_voice", "not a cloning request", q)
            Next
            Check(ChatAgent.WordsToSay("clone this voice and say: Good morning, everyone!" & vbCrLf & vbCrLf & "[Attached voice recording: a.mp3]") = "Good morning, everyone!",
                  "words after the colon", ChatAgent.WordsToSay("clone this voice and say: Good morning, everyone!"))
            Check(ChatAgent.WordsToSay("use this voice to say ""I'll be back""") = "I'll be back", "words in quotes")
            Check(ChatAgent.WordsToSay("use this voice and say hello world") = "hello world", "words after say")
            Check(ChatAgent.WordsToSay("με αυτή τη φωνή πες καλημέρα σε όλους") = "καλημέρα σε όλους", "Greek: words after πες")
            Check(ChatAgent.IsAudioFile("C:\x\Voice.MP3") AndAlso ChatAgent.IsAudioFile("a.wav") AndAlso ChatAgent.IsAudioFile("b.m4a") AndAlso Not ChatAgent.IsAudioFile("c.png"),
                  "sound files are recognized")
            Dim rec = Path.Combine(Path.GetTempPath(), "lm-v215.wav")
            File.WriteAllBytes(rec, {0, 1, 2})
            Dim kept = ChatAgent.KeepChatAudio(rec)
            Check(File.Exists(kept) AndAlso Path.GetFileName(kept).Substring(23) = "lm-v215.wav", "the attached recording is kept with the chat", kept)
            Dim payload As New List(Of ChatMessage) From {
                New ChatMessage With {.Role = "user", .Content = "hi", .AudioFiles = New List(Of String) From {kept}},
                New ChatMessage With {.Role = "assistant", .Content = "hello"},
                New ChatMessage With {.Role = "user", .Content = "now say it in this voice"}}
            Check(ChatAgent.LastRecording(payload) = kept, "an earlier recording in the conversation is used")
            Dim s As New ChatSession()
            s.Messages.AddRange(payload)
            Dim built = AppCore.BuildPayload(s, Nothing)
            Check(ChatAgent.LastRecording(built) = kept, "the recording reaches the chat agent")
            Dim json = Newtonsoft.Json.JsonConvert.SerializeObject(New ChatMessage With {.Role = "user", .Content = "x"})
            Check(Not json.Contains("AudioFiles"), "messages without sound save as before", json)
            Dim msg = Await ChatAgent.CloneVoiceAsync("hello", kept, Nothing, Nothing, Nothing, CancellationToken.None)
            Check(msg.StartsWith("(") OrElse msg.Contains("🗣"), "without a voice model: a clear note, no crash", msg)
            File.Delete(kept)
        End If

        If only = "v212" Then
            Console.WriteLine("== 2.12: film editor, conversations, cast voices/LoRAs, history, subtitles, notes")
            Dim tmp = Path.Combine(Path.GetTempPath(), "lm-v212")
            If Directory.Exists(tmp) Then Directory.Delete(tmp, True)
            Directory.CreateDirectory(tmp)
            Dim ff = Await VideoTools.EnsureFfmpegAsync(Nothing, CancellationToken.None)
            Dim mk = Sub(cmd As String)
                         Using p = Diagnostics.Process.Start(New Diagnostics.ProcessStartInfo(ff, "-hide_banner -loglevel error -y " & cmd) With {.UseShellExecute = False, .RedirectStandardError = True})
                             p.StandardError.ReadToEnd()
                             p.WaitForExit()
                         End Using
                     End Sub
            Dim v1 = Path.Combine(tmp, "v1.webm"), v2 = Path.Combine(tmp, "v2.mp4"), pic = Path.Combine(tmp, "pic.png")
            Dim music = Path.Combine(tmp, "music.wav"), vo = Path.Combine(tmp, "vo.wav")
            mk($"-f lavfi -i testsrc=size=320x240:rate=16:duration=2 -c:v libvpx-vp9 -b:v 300k ""{v1}""")
            mk($"-f lavfi -i testsrc2=size=480x270:rate=30:duration=1.5 -f lavfi -i sine=frequency=300:duration=1.5 -shortest -c:v libx264 -c:a aac ""{v2}""")
            mk($"-f lavfi -i color=c=orange:size=400x400 -frames:v 1 ""{pic}""")
            mk($"-f lavfi -i sine=frequency=500:duration=2 ""{music}""")
            mk($"-f lavfi -i sine=frequency=800:duration=1 ""{vo}""")

            ' Film: video without sound, video with sound, picture; music looped, voice-over at 1 s
            Dim film As New FilmProject With {.Name = "Test film", .Width = 640, .Height = 360, .Fades = True, .KeepClipSound = True, .Music = music, .MusicVolume = 0.3}
            film.Clips.Add(New FilmProject.Clip With {.File = v1})
            film.Clips.Add(New FilmProject.Clip With {.File = v2})
            film.Clips.Add(New FilmProject.Clip With {.File = pic, .Seconds = 2.5})
            film.VoiceOvers.Add(New FilmProject.VoiceOver With {.File = vo, .Start = 1})
            Dim filmOut = Await film.ExportAsync(Sub(p, t) Return, CancellationToken.None)
            Dim fi = Await VideoTools.InfoAsync(filmOut, CancellationToken.None)
            Check(fi.Width = 640 AndAlso fi.Height = 360 AndAlso Math.Abs(fi.Seconds - 6) < 0.3 AndAlso fi.HasAudio AndAlso Math.Abs(fi.Fps - 24) < 0.1,
                  "film exported: 3 clips (2 s + 1.5 s + picture 2.5 s), music and voice-over", $"{fi.Width}x{fi.Height} {fi.Fps} fps {fi.Seconds:0.00}s audio={fi.HasAudio}")
            Dim film2 As New FilmProject With {.Fades = False, .KeepClipSound = False}
            film2.Clips.Add(New FilmProject.Clip With {.File = v2})
            film2.Clips.Add(New FilmProject.Clip With {.File = pic, .Seconds = 1})
            Dim filmOut2 = Await film2.ExportAsync(Sub(p, t) Return, CancellationToken.None)
            Dim fi2 = Await VideoTools.InfoAsync(filmOut2, CancellationToken.None)
            Check(fi2.Width = 480 AndAlso fi2.Height = 270 AndAlso Math.Abs(fi2.Seconds - 2.5) < 0.3, "film at the size of the first clip, no music", $"{fi2.Width}x{fi2.Height} {fi2.Seconds:0.00}s")
            Dim saved = Path.Combine(tmp, "f.lmfilm")
            film.Save(saved)
            Dim back = FilmProject.Load(saved)
            Check(back.Clips.Count = 3 AndAlso back.Clips(2).IsPicture AndAlso back.VoiceOvers(0).Start = 1 AndAlso back.Music = music, "film saved and opened again")
            File.Delete(filmOut2)

            ' Talking pieces joined with their sound
            Dim t1 = Path.Combine(tmp, "t1.mp4"), t2 = Path.Combine(tmp, "t2.mp4")
            mk($"-f lavfi -i testsrc=size=320x240:rate=16:duration=1 -f lavfi -i sine=frequency=400:duration=1:sample_rate=24000 -shortest -c:v libx264 -c:a aac ""{t1}""")
            mk($"-f lavfi -i testsrc2=size=320x240:rate=16:duration=1.5 -f lavfi -i sine=frequency=600:duration=1.5:sample_rate=44100 -shortest -c:v libx264 -c:a aac ""{t2}""")
            Dim conv = Path.Combine(tmp, "conv.mp4")
            Await VideoTools.JoinWithSoundAsync({t1, t2}, conv, CancellationToken.None)
            Dim ci = Await VideoTools.InfoAsync(conv, CancellationToken.None)
            Check(ci.HasAudio AndAlso Math.Abs(ci.Seconds - 2.5) < 0.2, "conversation pieces (different sound rates) joined with their voices", $"{ci.Seconds:0.00}s audio={ci.HasAudio}")

            ' Subtitles from an SRT: burned and as a track
            Dim srt = Path.Combine(tmp, "subs.srt")
            File.WriteAllText(srt, "1" & vbLf & "00:00:00,000 --> 00:00:01,000" & vbLf & "Hello there" & vbLf & vbLf & "2" & vbLf & "00:00:01,000 --> 00:00:02,400" & vbLf & "Γεια σου" & vbLf)
            Dim burned = Path.Combine(tmp, "burned.mp4"), track = Path.Combine(tmp, "track.mp4")
            Await VideoTools.AddSubtitlesAsync(conv, srt, True, "eng", burned, CancellationToken.None)
            Await VideoTools.AddSubtitlesAsync(conv, srt, False, "eng", track, CancellationToken.None)
            Dim probe = Function(f As String) As String
                            Using p = Diagnostics.Process.Start(New Diagnostics.ProcessStartInfo(ff, $"-hide_banner -i ""{f}""") With {.UseShellExecute = False, .RedirectStandardError = True})
                                Dim e = p.StandardError.ReadToEnd()
                                p.WaitForExit()
                                Return e
                            End Using
                        End Function
            Check(File.Exists(burned) AndAlso New FileInfo(burned).Length > 1000 AndAlso Not probe(burned).Contains("Subtitle:"), "subtitles burned into the picture")
            Check(probe(track).Contains("Subtitle: mov_text") AndAlso probe(track).Contains("(eng)"), "subtitles as a track players can switch")

            ' Dialogue lines, cast voice / LoRA, training set
            Dim castList = CastLibrary.Load()
            castList.RemoveAll(Function(m) m.Name = "Nina" OrElse m.Name = "Leo")
            Dim nina As New CastLibrary.CastMember With {.Name = "Nina", .Description = "young woman, braids", .Voice = "NinaVoice", .Lora = "nina_lora"}
            Dim leo As New CastLibrary.CastMember With {.Name = "Leo", .Description = "old fisherman"}
            castList.Add(nina) : castList.Add(leo)
            CastLibrary.AddImage(nina, pic)
            CastLibrary.AddImage(nina, pic)
            CastLibrary.Save(castList)
            Dim dlg = CastLibrary.ParseDialogue("@Nina: Hello, Leo!" & vbCrLf & "How are you?" & vbCrLf & "Leo: Fine, thanks." & vbCrLf & "Narrator: (not in the cast)")
            Check(dlg.Count = 2 AndAlso dlg(0).Member.Name = "Nina" AndAlso dlg(0).Words = "Hello, Leo! How are you?" AndAlso dlg(1).Member.Name = "Leo" AndAlso
                  dlg(1).Words.StartsWith("Fine, thanks.") AndAlso dlg(1).Words.Contains("Narrator"), "conversation lines: who says what",
                  String.Join(" | ", dlg.Select(Function(d) $"{d.Member?.Name}: {d.Words}")))
            Check(CastLibrary.Load().First(Function(m) m.Name = "Nina").Voice = "NinaVoice", "a cast member keeps their voice")
            Dim set1 = CastLibrary.ExportTrainingSet(nina, tmp, "nina_lm")
            Check(Directory.GetFiles(set1, "*.png").Length = 2 AndAlso Directory.GetFiles(set1, "*.txt").Length = 3 AndAlso
                  File.ReadAllText(Directory.GetFiles(set1, "Nina_001.txt")(0)) = "nina_lm, young woman, braids", "LoRA training set: pictures + captions + README")
            ' a LoRA for the member, used only with its family
            Directory.CreateDirectory(LoraLibrary.Folder)
            File.WriteAllBytes(Path.Combine(LoraLibrary.Folder, "nina_lora.safetensors"), New Byte(100) {})
            Dim lib0 = LoraLibrary.All()
            If Not lib0.Any(Function(l) l.Name = "nina_lora") Then
                Dim imp = LoraLibrary.Import(Path.Combine(LoraLibrary.Folder, "nina_lora.safetensors"))
            End If
            Dim nl = LoraLibrary.All().FirstOrDefault(Function(l) l.Name = "nina_lora")
            If nl IsNot Nothing Then
                nl.Family = "SDXL"
                LoraLibrary.Save()
                Check(CastLibrary.LorasFor({nina}, "SDXL").Count = 1 AndAlso CastLibrary.LorasFor({nina}, "Wan").Count = 0, "the member's LoRA is used with its model family only")
            Else
                Check(False, "LoRA registered")
            End If

            ' History: search and favourites
            Dim found = History.Find("Test_film", False, 50)
            Check(found.Any(Function(f) f = filmOut), "history finds the film by name", String.Join(", ", found.Select(AddressOf Path.GetFileName)))
            Dim st = History.ToggleFavorite(filmOut)
            Check(st AndAlso History.Find("", True, 50).Contains(filmOut) AndAlso Not History.ToggleFavorite(filmOut) AndAlso Not History.Find("", True, 50).Contains(filmOut),
                  "★ favourite on and off")

            ' Notes for the phone
            Dim before = Notifier.LastId
            Notifier.Notify("LocalMind studio", "3 done")
            Check(Notifier.Since(before).Count = 1 AndAlso Notifier.Since(before)(0).Text = "3 done", "job messages reach the phone page")

            ' Whisper subtitles (needs the engine download; skipped where it is blocked)
            Try
                Dim speech = Path.Combine(tmp, "speech.mp4")
                mk($"-f lavfi -i testsrc=size=320x240:rate=16:duration=2 -f lavfi -i sine=frequency=300:duration=2 -shortest -c:v libx264 -c:a aac ""{speech}""")
                Dim r = Await VideoMaker.SubtitlesAsync(speech, "en", "", True, Sub(p, t) Return, CancellationToken.None)
                Check(File.Exists(r.Video) AndAlso File.Exists(r.Srt), "subtitles with Whisper", r.Video)
            Catch ex As Exception
                Console.WriteLine("  SKIP Whisper subtitles (engine not available here): " & ex.Message)
            End Try
            castList = CastLibrary.Load()
            castList.RemoveAll(Function(m) m.Name = "Nina" OrElse m.Name = "Leo")
            CastLibrary.Save(castList)
            File.Delete(filmOut)
        End If

        If only = "agent" Then
            Console.WriteLine("== Chat: plain requests for a picture / video use the tool")
            AppCore.Settings.AgentTools = True
            AppCore.Settings.EnableImages = True
            Dim seen As New List(Of String)
            For Each ask In {"make me a picture of a red apple", "generate a video of the sea", "φτιάξε μου μια εικόνα με γάτα", "how do I make a video?"}
                Dim payload As New List(Of ChatMessage) From {New ChatMessage With {.Role = "user", .Content = ask}}
                Dim opts As New ChatRequestOptions With {.Model = "test"}
                Dim sent As String = Nothing
                Dim streamOnce = Function(o As ChatRequestOptions) As Task(Of ChatResult)
                                     sent = If(o.Tools Is Nothing, "-", String.Join(",", o.Tools.Select(Function(t) t("function")?.Value(Of String)("name"))) & "|" & o.ToolChoice)
                                     Return Task.FromResult(New ChatResult With {.Content = "ok"})
                                 End Function
                Dim err As String = Nothing
                Try
                    Dim r = Await ChatAgent.AnswerAsync(streamOnce, payload, opts, False, Sub(t) Return, Sub() Return, CancellationToken.None)
                Catch ex As Exception
                    err = ex.Message
                End Try
                Check(err Is Nothing, "no error: " & ask, If(err, sent))
                seen.Add(sent)
            Next
            Check(seen(0) = "generate_image|required" AndAlso seen(1) = "generate_video|required" AndAlso seen(2) = "generate_image|required" AndAlso Not seen(3).Contains("required"),
                  "the asked-for tool is required (and only offered tool)", String.Join(" ; ", seen))
            ' one system message, at the start (chat templates reject others)
            Dim msgs As New List(Of ChatMessage) From {
                New ChatMessage With {.Role = "system", .Content = "You are a pirate."},
                New ChatMessage With {.Role = "system", .Content = "Answer briefly."},
                New ChatMessage With {.Role = "user", .Content = "hi"},
                New ChatMessage With {.Role = "assistant", .Content = "Arr!"},
                New ChatMessage With {.Role = "system", .Content = "Web results: sunny, 24°C"},
                New ChatMessage With {.Role = "user", .Content = "weather in Athens?"}}
            Dim arr = OpenAiCompatService.BuildMessages(msgs)
            Dim roles = String.Join(",", arr.Select(Function(m) m.Value(Of String)("role")))
            Check(roles = "system,user,assistant,user" AndAlso arr(0).Value(Of String)("content").Contains("Answer briefly") AndAlso
                  arr(3).Value(Of String)("content").StartsWith("Web results") AndAlso arr(3).Value(Of String)("content").EndsWith("weather in Athens?"),
                  "one system message at the start; later notes go with the next question", roles)
            Dim only1 = OpenAiCompatService.BuildMessages(New List(Of ChatMessage) From {New ChatMessage With {.Role = "user", .Content = "x"}, New ChatMessage With {.Role = "system", .Content = "Search query: y"}})
            Check(String.Join(",", only1.Select(Function(m) m.Value(Of String)("role"))) = "system,user", "a note after the last question becomes the first message")
        End If

        If only = "community" Then
            Console.WriteLine("== A community model (downloaded and used like the hand-made ones)")
            Dim r = MediaStudio.Recipes().First(Function(x) x.Id = "hf-gsdf-counterfeit-v2-5")
            Dim v = r.Variants(0)
            Await MediaStudio.DownloadAsync(r, v, Sub(p, t) Return, CancellationToken.None)
            Check(MediaStudio.InstalledVariant(r) IsNot Nothing, "downloaded", MediaStudio.LocalPath(v))
            Dim req As New MediaStudio.GenerateRequest With {.Recipe = r, .Variant = v, .Prompt = "a red apple on a wooden table", .Negative = r.Negative,
                .Width = 256, .Height = 256, .Steps = 6, .Cfg = r.Cfg, .Seed = 3}
            Dim out = Await MediaStudio.GenerateAsync(req, Sub(p, t) Return, CancellationToken.None)
            Check(IO.File.Exists(out) AndAlso New IO.FileInfo(out).Length > 10000, "picture made with a community SD 1.5 model", out)
            If IO.File.Exists(out) Then IO.File.Copy(out, "/tmp/community-test.png", True)
        End If

        If only = "docs" Then
            Console.WriteLine("== Chat with documents (real embedding model)")
            Dim dir = Path.Combine(Path.GetTempPath(), "lm-docs")
            Directory.CreateDirectory(dir)
            File.WriteAllText(Path.Combine(dir, "pets.md"), "# Our cat Milo" & vbLf & "Milo is an orange cat. He eats salmon pate twice a day and sleeps on the blue sofa. His vet is Dr. Papadopoulos in Kifisia.")
            File.WriteAllText(Path.Combine(dir, "car.txt"), "The car is a 2019 Toyota Yaris hybrid. Next service is due in March at 60,000 km. Tyre pressure: 2.3 bar front, 2.2 bar rear.")
            File.WriteAllText(Path.Combine(dir, "trip.html"), "<html><head><script>var x=1;</script></head><body><nav>menu</nav><article><h1>Crete trip</h1><p>We fly to Chania on 14 June and stay in Rethymno for six nights. Ferry back from Heraklion.</p></article></body></html>")
            Knowledge.AddSource(dir)
            Dim summary = Await Knowledge.IndexAsync(Sub(t) Return, CancellationToken.None)
            Check(Knowledge.Stats().Docs = 3, "indexed folder", summary)
            For Each q In {("What food does Milo eat?", "pets.md"), ("When is the car service?", "car.txt"), ("Where do we stay in Crete?", "trip.html"), ("Ποιος είναι ο κτηνίατρος της γάτας;", "pets.md")}
                Dim hits = Await Knowledge.SearchAsync(q.Item1, 3, CancellationToken.None)
                Check(hits.Count > 0 AndAlso hits(0).Title = q.Item2, "search: " & q.Item1, $"top = {hits(0).Title} ({hits(0).Score:0.00})")
            Next
            Dim payload As New List(Of ChatMessage) From {New ChatMessage With {.Role = "user", .Content = "What is the tyre pressure?"}}
            Dim srcs = Await AppCore.AugmentAsync(payload, True, False, Nothing, CancellationToken.None)
            Check(payload.Count = 2 AndAlso payload(0).Role = "system" AndAlso payload(0).Content.Contains("2.3 bar") AndAlso payload(1).Role = "user", "prompt augmented before the question", $"{srcs.Count} sources")
            Check(AppCore.SourcesMarkdown(srcs).Contains("car.txt"), "sources list")
            Knowledge.StopEmbedder()

            Console.WriteLine("== Web search")
            Check(WebSearch.HtmlToText("<html><script>bad()</script><nav>x</nav><main><p>Hello &amp; welcome</p><p>Line two</p></main></html>").StartsWith("Hello & welcome"), "HTML to text")
            Dim sample = "<div class=""result results_links web-result""><div class=""links_main result__body""><h2 class=""result__title""><a rel=""nofollow"" class=""result__a"" href=""//duckduckgo.com/l/?uddg=https%3A%2F%2Fgithub.com%2Fggml-org%2Fllama.cpp&amp;rut=abc"">ggml-org/<b>llama.cpp</b>: LLM inference</a></h2>" &
                         "<a class=""result__snippet"" href=""//duckduckgo.com/l/?uddg=x"">LLM inference in <b>C/C++</b> &amp; more</a></div></div>" &
                         "<div class=""result""><h2><a rel=""nofollow"" class=""result__a"" href=""https://duckduckgo.com/y.js?ad=1"">Ad</a></h2></div>" &
                         "<div class=""result""><h2><a rel=""nofollow"" class=""result__a"" href=""//duckduckgo.com/l/?uddg=https%3A%2F%2Fen.wikipedia.org%2Fwiki%2FLlama&amp;rut=z"">Llama - Wikipedia</a></h2><a class=""result__snippet"" href=""#"">The llama is a South American camelid</a></div>"
            Dim parsed = WebSearch.ParseDuckDuckGo(sample)
            Check(parsed.Count = 2 AndAlso parsed(0).Url = "https://github.com/ggml-org/llama.cpp" AndAlso parsed(0).Title = "ggml-org/llama.cpp: LLM inference" AndAlso parsed(0).Snippet = "LLM inference in C/C++ & more" AndAlso parsed(1).Url.Contains("wikipedia"),
                  "DuckDuckGo result parsing (ads skipped, redirect links decoded)", String.Join(" | ", parsed.Select(Function(p) p.Title & " → " & p.Url)))
            Try
                Dim web = Await WebSearch.SearchAndReadAsync("llama.cpp github", CancellationToken.None)
                Check(web.Count > 0, "DuckDuckGo search + page reading", $"{web.Count} results, first: {web(0).Title} — {web(0).Location} ({web(0).Text.Length} chars)")
            Catch ex As Exception
                Console.WriteLine("  SKIP web search from this sandbox: " & ex.Message)
            End Try
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

            Check(AppCore.Engine.LoadedContext > 0, "auto context read from engine", AppCore.Engine.LoadedContext & " tokens")

            Console.WriteLine("== Sharing the GPU with image generation")
            AppCore.Settings.ImageGpuSharing = 2
            Using lease = Await AppCore.Engine.ReserveGpuAsync(4L << 30, Nothing, CancellationToken.None)
                Check(AppCore.Engine.CurrentModel IsNot Nothing, "keep-loaded mode leaves the chat model", AppCore.Engine.CurrentModel?.DisplayName)
            End Using
            AppCore.Settings.ImageGpuSharing = 1
            Dim msgs As New List(Of String)
            Dim leaseG = Await AppCore.Engine.ReserveGpuAsync(4L << 30, Sub(t) msgs.Add(t), CancellationToken.None)
            Check(AppCore.Engine.CurrentModel Is Nothing AndAlso msgs.Any(Function(m) m.StartsWith("Unloading")), "chat model unloaded for the image", String.Join(" | ", msgs))
            Dim waiting = backend.StreamChatAsync(
                New List(Of ChatMessage) From {New ChatMessage With {.Role = "user", .Content = "Say hi."}},
                New ChatRequestOptions With {.Model = qwen.DisplayName, .MaxTokens = 20, .EnableThinking = False},
                Sub(t) Return, Sub(t) Return, CancellationToken.None)
            Await Task.Delay(1500)
            Check(Not waiting.IsCompleted, "chat waits while the image is drawn", "")
            leaseG.Dispose()
            Dim after = Await waiting
            Check(after.Content.Length > 0 AndAlso AppCore.Engine.CurrentModel IsNot Nothing, "chat model reloads afterwards", after.Content.Trim().Replace(vbLf, " "))
            AppCore.Settings.ImageGpuSharing = 0
            Console.WriteLine("== Speed test")
            Dim sp = Await SpeedTest.RunAsync(smol, CancellationToken.None)
            Check(sp.GenTokensPerSecond > 0 AndAlso sp.PromptTokensPerSecond > 0 AndAlso SpeedTest.GetResult(smol) IsNot Nothing,
                  "benchmark stored", $"{sp.GenTokensPerSecond} tok/s gen, {sp.PromptTokensPerSecond} tok/s prompt, load {sp.LoadSeconds}s, ctx {sp.ContextTokens}")

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

            Console.WriteLine("== Phone extras")
            Dim fi = JObject.Parse(Await http.GetStringAsync("/api/info"))
            Check(fi("features") IsNot Nothing, "info lists phone features", fi("features").ToString(Newtonsoft.Json.Formatting.None))
            Dim someOut = Directory.GetFiles(MediaStudio.OutputFolder, "*.png").FirstOrDefault()
            If someOut IsNot Nothing Then
                Dim ob = Await http.GetByteArrayAsync("/api/outputs/" & Uri.EscapeDataString(Path.GetFileName(someOut)))
                Check(ob.Length = New FileInfo(someOut).Length, "phone can load pictures made on the PC", Path.GetFileName(someOut))
                Dim badOut = Await http.GetAsync("/api/outputs/..%2F..%2Fsecret.txt")
                Check(CInt(badOut.StatusCode) = 404, "outputs: no path tricks")
            End If
            If AudioStudio.Models().Any(Function(m) m.Id = "chatterbox-ml" AndAlso AudioStudio.IsInstalled(m)) Then
                AppCore.Settings.ReadAloudModel = "chatterbox-ml"
                Dim spk = Await http.PostAsync("/api/speak", New StringContent("{""text"":""Hello there. This is the phone speaking. Two sentences.""}", Encoding.UTF8, "application/json"))
                Dim wavBytes = Await spk.Content.ReadAsByteArrayAsync()
                Check(spk.IsSuccessStatusCode AndAlso wavBytes.Length > 20000 AndAlso Encoding.ASCII.GetString(wavBytes, 0, 4) = "RIFF", "phone read-aloud with the AI voice", $"{wavBytes.Length \ 1024} KB WAV")
                AppCore.Settings.ReadAloudModel = ""
            End If

            Console.WriteLine("== AI uses tools by itself")
            AppCore.Settings.AgentTools = True
            Dim toolOpts As New ChatRequestOptions With {.Model = qwen.DisplayName, .MaxTokens = 400, .EnableThinking = False,
                .Tools = ChatAgent.Tools(qwen.DisplayName, True)}
            Dim tr = Await backend.StreamChatAsync(New List(Of ChatMessage) From {
                New ChatMessage With {.Role = "user", .Content = "Search the web: who won the most recent Eurovision Song Contest?"}},
                toolOpts, Sub(t) Return, Sub(t) Return, CancellationToken.None)
            Check(tr.ToolCalls.Count > 0 AndAlso tr.ToolCalls(0).Name = "web_search", "model calls the web_search tool",
                  String.Join("; ", tr.ToolCalls.Select(Function(c) c.Name & " " & c.Arguments)))

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
