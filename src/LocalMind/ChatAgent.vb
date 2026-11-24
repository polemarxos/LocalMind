Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Text.RegularExpressions
Imports Newtonsoft.Json.Linq

''' <summary>
''' Lets the chat model use LocalMind's abilities by itself (draw a picture, compose a song, search the web),
''' for the PC window and the phone page alike. Also the shared "make a picture / song for the chat" helpers.
''' </summary>
Public Module ChatAgent
    ''' <summary>Models whose chat template rejected tools (no retries with tools for them).</summary>
    Public ReadOnly NoToolModels As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

    Private Function Tool(name As String, description As String, props As JObject, required As String()) As JObject
        Return New JObject From {
            {"type", "function"},
            {"function", New JObject From {
                {"name", name}, {"description", description},
                {"parameters", New JObject From {{"type", "object"}, {"properties", props}, {"required", New JArray(required)}}}}}}
    End Function

    ' Plain requests for a video or a picture (English and Greek). "How do I make a video…" questions are not requests.
    Private ReadOnly AskVideo As New Regex("\b(generate|make|create|render|produce|animate|film)\b[^.?!\n]{0,60}\b(video|clip|animation|movie)s?\b|" &
                                           "(φτιά[χξ]|δημιούργ|κάνε|κάν'|βγάλε|παρήγαγε|ζωντάνεψε)[^.?!;\n]{0,60}(βίντεο|κλιπ|κινούμεν)",
                                           RegexOptions.IgnoreCase Or RegexOptions.Compiled)
    Private ReadOnly AskPicture As New Regex("\b(generate|make|create|draw|paint|render|design)\b[^.?!\n]{0,60}\b(image|picture|photo|drawing|painting|illustration|logo|wallpaper|portrait)s?\b|" &
                                             "^\s*(please\s+)?(draw|paint)\b|" &
                                             "(φτιά[χξ]|δημιούργ|ζωγράφισ|κάνε|σχεδίασ)[^.?!;\n]{0,60}(εικόνα|φωτογραφ|ζωγραφι|σκίτσο|λογότυπο|πορτρέτο)",
                                             RegexOptions.IgnoreCase Or RegexOptions.Compiled)
    Private ReadOnly AskClone As New Regex("\b(clone|copy|mimic|imitate|use)\b[^.?!\n]{0,40}\bvoice\b|\b(this|that|his|her|the same|my)\s+voice\b|\bvoice\s+(clone|cloning)\b|" &
                                           "(κλών|κλωνοπ|αντέγραψ|μιμήσ|μιμου)[^.?!;\n]{0,40}φων|(αυτή|ίδια|τη|την|του|της|μου)\s+(τη\s+)?φωνή",
                                           RegexOptions.IgnoreCase Or RegexOptions.Compiled)
    Private ReadOnly HowTo As New Regex("\bhow (do|to|can|should|would)\b|\bπώς\b|\bπως\b|\btutorial\b", RegexOptions.IgnoreCase Or RegexOptions.Compiled)

    ''' <summary>The tool the user plainly asked for in their latest message ("generate me a video"), if offered.</summary>
    Public Function RequestedTool(text As String, tools As JArray) As String
        If tools Is Nothing OrElse String.IsNullOrWhiteSpace(text) OrElse HowTo.IsMatch(text) Then Return Nothing
        Dim offered = tools.Select(Function(t) t("function")?.Value(Of String)("name")).ToList()
        If AskClone.IsMatch(text) AndAlso offered.Contains("clone_voice") Then Return "clone_voice"
        If AskVideo.IsMatch(text) AndAlso offered.Contains("generate_video") Then Return "generate_video"
        If AskPicture.IsMatch(text) AndAlso offered.Contains("generate_image") Then Return "generate_image"
        Return Nothing
    End Function

    ''' <summary>Tools offered to the model, depending on the abilities that are switched on and installed.</summary>
    Public Function Tools(model As String, offerWebSearch As Boolean) As JArray
        Dim s = AppCore.Settings
        If Not s.AgentTools Then Return Nothing
        If NoToolModels.Contains(If(model, "")) Then Return Nothing
        Dim list As New JArray()
        If s.EnableImages Then
            list.Add(Tool("generate_image", "Create a picture with the local image model. Use it only when the user asks for a picture, drawing, photo, logo or illustration.",
                New JObject From {{"prompt", New JObject From {{"type", "string"}, {"description", "Detailed English description of the picture: subject, setting, style, lighting. Keep any text that must appear exactly. Keep every @Name exactly as the user wrote it (saved characters and places)."}}}},
                {"prompt"}))
            list.Add(Tool("generate_video", "Create a short video clip (a few seconds) with the local video model. If the user attached a photo, it is animated. Use it only when the user asks for a video, clip or animation.",
                New JObject From {{"prompt", New JObject From {{"type", "string"}, {"description", "Detailed English description of the video: subject, action and motion, camera movement, setting, style, lighting. Keep every @Name exactly as the user wrote it (saved characters and places)."}}}},
                {"prompt"}))
        End If
        If s.EnableAudio AndAlso AudioStudio.Models().Any(Function(m) m.IsMusic AndAlso AudioStudio.IsInstalled(m)) Then
            list.Add(Tool("make_song", "Compose a song with vocals (or an instrumental) with the local music model. Use it only when the user asks for a song or music.",
                New JObject From {
                    {"style", New JObject From {{"type", "string"}, {"description", "Genre, mood, instruments, voice, tempo, in English"}}},
                    {"lyrics", New JObject From {{"type", "string"}, {"description", "Lyrics with [verse] and [chorus] tags, in the user's language; empty for instrumental"}}}},
                {"style"}))
        End If
        If s.EnableAudio AndAlso CanCloneVoice() Then
            Dim saved = String.Join(", ", VoiceLibrary.All().Select(Function(v) v.Name))
            list.Add(Tool("clone_voice", "Speak text in a copied voice: the voice of the recording the user attached (MP3, WAV…) or a saved voice. " &
                          "Use it when the user asks to clone / copy / use a voice, or to say something in someone's voice.",
                New JObject From {
                    {"text", New JObject From {{"type", "string"}, {"description", "The exact words to speak, in the user's language, as the user wrote them"}}},
                    {"voice_name", New JObject From {{"type", "string"}, {"description", "A saved voice to use when no recording is attached" & If(saved <> "", " (saved voices: " & saved & ")", "")}}},
                    {"save_as", New JObject From {{"type", "string"}, {"description", "Name to save the attached voice under, only if the user gave one"}}}},
                {"text"}))
        End If
        If offerWebSearch Then
            list.Add(Tool("web_search", "Search the web for current or unknown information (news, prices, recent events, facts you are unsure about).",
                New JObject From {{"query", New JObject From {{"type", "string"}, {"description", "Search words"}}}}, {"query"}))
        End If
        Return If(list.Count > 0, list, Nothing)
    End Function

    ''' <summary>
    ''' Streams an answer with tools offered; falls back to a plain answer if the model's template has no tool support.
    ''' Runs any tools the model calls. Returns the final result plus extra Markdown (pictures, songs) and web sources.
    ''' </summary>
    Public Async Function AnswerAsync(streamOnce As Func(Of ChatRequestOptions, Task(Of ChatResult)), payload As List(Of ChatMessage),
                                      opts As ChatRequestOptions, offerWebSearch As Boolean, status As Action(Of String), beforeFollowup As Action,
                                      ct As CancellationToken) As Task(Of (Result As ChatResult, Markdown As String, Sources As List(Of SourceRef)))
        opts.Tools = Tools(opts.Model, offerWebSearch)
        ' A plain "make me a video / picture": make sure it happens (small models often answer "I can't")
        ' by offering only that tool and requiring the call; the model still writes the English prompt.
        Dim userText = payload?.LastOrDefault(Function(m) m.Role = "user")?.Content
        Dim forced = RequestedTool(userText, opts.Tools)
        If forced IsNot Nothing Then
            ' (Enumerable.First: JArray's own "First" property would index the JSON with the lambda)
            opts.Tools = New JArray(Enumerable.First(opts.Tools, Function(t As JToken) t("function")?.Value(Of String)("name") = forced))
            opts.ToolChoice = "required"
        End If
        Dim result As ChatResult = Nothing
        Dim failed = False
        Try
            result = Await streamOnce(opts)
        Catch ex As Net.Http.HttpRequestException When opts.Tools IsNot Nothing
            failed = True
        End Try
        If failed Then
            NoToolModels.Add(If(opts.Model, ""))
            opts.Tools = Nothing
            opts.ToolChoice = Nothing
            result = Await streamOnce(opts)
        End If
        opts.ToolChoice = Nothing
        If result.ToolCalls.Count = 0 AndAlso forced IsNot Nothing Then
            ' The model could not call the tool: run it with the user's own words.
            result.ToolCalls.Add(New ToolCall With {.Name = forced, .Arguments = New JObject From {{"prompt", userText}}.ToString()})
        End If
        If result.ToolCalls.Count = 0 Then Return (result, "", Nothing)

        Dim md As New Text.StringBuilder()
        Dim sources As List(Of SourceRef) = Nothing
        ' 🎭 Cast: characters and places the user wrote as @Name keep their look (description + pictures)
        Dim cast = CastLibrary.Involved(userText, New List(Of CastLibrary.CastMember)())
        For Each tc In result.ToolCalls
            Dim args As JObject
            Try
                args = If(String.IsNullOrWhiteSpace(tc.Arguments), New JObject(), JObject.Parse(tc.Arguments))
            Catch
                args = New JObject()
            End Try
            Try
                Select Case tc.Name
                    Case "generate_image"
                        md.AppendLine().AppendLine().Append(Await MakePictureAsync(If(args.Value(Of String)("prompt"), ""), Nothing, status, ct, cast))
                    Case "generate_video"
                        md.AppendLine().AppendLine().Append(Await MakeVideoAsync(If(args.Value(Of String)("prompt"), ""), LastPhoto(payload), status, ct, cast))
                    Case "clone_voice"
                        Dim words = If(args.Value(Of String)("text"), args.Value(Of String)("prompt"))
                        ' (a forced call carries the whole message: keep only the words to say)
                        If String.IsNullOrWhiteSpace(words) OrElse words = userText Then words = WordsToSay(userText)
                        md.AppendLine().AppendLine().Append(Await CloneVoiceAsync(words, LastRecording(payload), args.Value(Of String)("voice_name"),
                                                                                  args.Value(Of String)("save_as"), status, ct))
                    Case "make_song"
                        md.AppendLine().AppendLine().Append(Await MakeSongAsync(If(args.Value(Of String)("style"), ""), args.Value(Of String)("lyrics"), status, ct))
                    Case "web_search"
                        status?.Invoke("Searching the web…")
                        Dim q = args.Value(Of String)("query")
                        If Not String.IsNullOrWhiteSpace(q) Then payload.Insert(payload.Count - 1, New ChatMessage With {.Role = "system", .Content = "Search query: " & q})
                        sources = Await AppCore.AugmentAsync(payload, False, True, status, ct)
                        opts.Tools = Nothing
                        beforeFollowup?.Invoke()
                        result = Await streamOnce(opts)
                End Select
            Catch ex As OperationCanceledException
                Throw
            Catch ex As Exception
                Dim ls = ex.Message.Split({vbCr, vbLf}, StringSplitOptions.RemoveEmptyEntries)
                md.AppendLine().AppendLine().Append("⚠ " & String.Join("  " & vbLf & "⚠ ", ls.Skip(Math.Max(0, ls.Length - 4))))
            End Try
        Next
        Return (result, md.ToString(), sources)
    End Function

    '──────────────── pictures and songs for the chat ────────────────
    ''' <summary>The chosen chat image model if downloaded, else the best one: with a photo one that edits or restyles, else one that creates.</summary>
    Public Function PickImageRecipe(withPhoto As Boolean) As MediaRecipe
        Dim all = MediaStudio.Recipes().Where(Function(r) Not r.IsVideo AndAlso Not r.IsUpscaler AndAlso r.Kind <> "tool" AndAlso MediaStudio.UsableVariant(r) IsNot Nothing).
                  Where(Function(r) If(withPhoto, r.Can("edit") OrElse r.Can("restyle"), r.Can("create"))).ToList()
        Dim chosen = all.FirstOrDefault(Function(r) r.Id = AppCore.Settings.ChatImageModel)
        If chosen IsNot Nothing Then Return chosen
        Dim hw = HardwareInfo.Current()
        Return all.OrderBy(Function(r) If(withPhoto AndAlso r.Can("edit"), 0, 1)).
                   ThenBy(Function(r) If(MediaStudio.Fit(r, MediaStudio.UsableVariant(r), hw) = FitLevel.Fits, 0, 1)).
                   ThenBy(Function(r) r.Steps).FirstOrDefault()
    End Function

    Public Function EscapeAlt(s As String) As String
        Dim t = If(s, "").Replace("[", "(").Replace("]", ")").Replace(vbCr, " ").Replace(vbLf, " ")
        Return If(t.Length > 120, t.Substring(0, 120) & "…", t)
    End Function

    ''' <summary>Makes a picture (or edits the photo) and returns Markdown that shows it.</summary>
    Public Async Function MakePictureAsync(prompt As String, photoFile As String, status As Action(Of String), ct As CancellationToken,
                                           Optional cast As List(Of CastLibrary.CastMember) = Nothing) As Task(Of String)
        cast = If(cast, New List(Of CastLibrary.CastMember)())
        If cast.Count > 0 Then prompt = CastLibrary.ComposePrompt(prompt, cast)
        Dim castPics = If(photoFile Is Nothing, CastLibrary.ReferencePictures(cast, 3), New List(Of String)())
        Dim r = PickImageRecipe(photoFile IsNot Nothing)
        Dim combine = False
        If castPics.Count > 0 Then
            ' a model that takes reference pictures puts the same faces and places in the picture
            Dim c = VideoMaker.CastSceneRecipe()
            If c IsNot Nothing Then
                r = If(r IsNot Nothing AndAlso r.Can("combine"), r, c)
                combine = True
            End If
        End If
        If r Is Nothing Then Return Localizer.T("(No picture model is downloaded yet — open 🎨 Create to get one.)")
        Dim v = MediaStudio.UsableVariant(r)
        Dim fast = AppCore.Settings.MediaFast AndAlso r.Speed IsNot Nothing
        status?.Invoke(If(photoFile IsNot Nothing, "Editing the picture…", "Drawing…"))
        Dim req As New MediaStudio.GenerateRequest With {
            .Recipe = r, .Variant = v, .Prompt = If(String.IsNullOrWhiteSpace(prompt), "a beautiful picture", prompt), .Negative = r.Negative,
            .Task = If(combine, "combine", MediaStudio.GuessTask(r, photoFile IsNot Nothing, False, False)),
            .InputImages = If(combine, castPics.Take(Math.Max(1, r.Refs)).ToList(), New List(Of String)()),
            .Width = If(r.Width > 0, r.Width, 1024), .Height = If(r.Height > 0, r.Height, 1024),
            .Steps = If(fast, r.Speed.Steps, r.Steps), .Cfg = If(fast, r.Speed.Cfg, r.Cfg), .Fast = fast, .Strength = 0.6,
            .InputImage = photoFile, .Loras = LoraLibrary.ForFamily(r.Family).Where(Function(l) l.Enabled).ToList()}
        Dim out = Await MediaStudio.GenerateAsync(req, Sub(p, t) status?.Invoke(If(p > 0, $"{t} ({p}%)", t)), ct)
        Return $"![{EscapeAlt(prompt)}](https://outputs.localmind/{Uri.EscapeDataString(Path.GetFileName(out))})" & vbCrLf & vbCrLf & $"_{r.Name} · {v.Label}_"
    End Function

    ''' <summary>A downloaded video model: with a photo one that animates it (else one that creates from text).</summary>
    Public Function PickVideoRecipe(withPhoto As Boolean) As MediaRecipe
        Dim hw = HardwareInfo.Current()
        Dim all = MediaStudio.Recipes().Where(Function(r) r.IsVideo AndAlso MediaStudio.UsableVariant(r) IsNot Nothing).ToList()
        Dim ok = all.Where(Function(r) If(withPhoto, r.Can("animate"), r.Can("create") AndAlso r.Input <> "required")).ToList()
        If ok.Count = 0 AndAlso withPhoto Then ok = all.Where(Function(r) r.Can("create") AndAlso r.Input <> "required").ToList()
        ' the default the user chose (if it can do this kind of video)
        Dim chosen = ok.FirstOrDefault(Function(r) r.Id = AppCore.Settings.ChatVideoModel)
        If chosen IsNot Nothing Then Return chosen
        Return ok.OrderBy(Function(r) If(MediaStudio.Fit(r, MediaStudio.UsableVariant(r), hw) = FitLevel.Fits, 0, 1)).
                  ThenBy(Function(r) If(r.Uncensored, 1, 0)).
                  ThenBy(Function(r) If(r.Speed IsNot Nothing AndAlso AppCore.Settings.MediaFast, r.Speed.Steps, r.Steps)).FirstOrDefault()
    End Function

    ''' <summary>The photo attached to the user's latest message (saved to a temporary file), if any.</summary>
    Private Function LastPhoto(payload As List(Of ChatMessage)) As String
        Dim m = payload?.LastOrDefault(Function(x) x.Role = "user")
        Dim b64 = m?.Images?.FirstOrDefault()
        If String.IsNullOrEmpty(b64) Then Return Nothing
        Try
            Dim f = Path.Combine(Path.GetTempPath(), "localmind-photo-" & Guid.NewGuid().ToString("N") & ".png")
            File.WriteAllBytes(f, Convert.FromBase64String(b64))
            Return f
        Catch
            Return Nothing
        End Try
    End Function

    ''' <summary>Makes a short video (animates the photo if one is given) and returns Markdown with a player.</summary>
    Public Async Function MakeVideoAsync(prompt As String, photoFile As String, status As Action(Of String), ct As CancellationToken,
                                         Optional cast As List(Of CastLibrary.CastMember) = Nothing) As Task(Of String)
        cast = If(cast, New List(Of CastLibrary.CastMember)())
        If cast.Count > 0 AndAlso photoFile Is Nothing Then
            ' with the cast: first the opening scene with their pictures, then it is animated
            Dim animator = PickVideoRecipe(True)
            If animator IsNot Nothing AndAlso animator.Can("animate") Then
                Dim scene = VideoMaker.CastSceneRequest(prompt, cast, If(animator.Width > 0, animator.Width, 832), If(animator.Height > 0, animator.Height, 480))
                If scene IsNot Nothing Then
                    status?.Invoke("🎭 Making the opening scene with the cast…")
                    photoFile = Await MediaStudio.GenerateAsync(scene, Sub(p, t) status?.Invoke(If(p > 0, $"{t} ({p}%)", t)), ct)
                End If
            End If
        End If
        If cast.Count > 0 Then prompt = CastLibrary.ComposePrompt(prompt, cast)
        Dim r = PickVideoRecipe(photoFile IsNot Nothing)
        If r Is Nothing Then
            Dim animOnly = MediaStudio.Recipes().Any(Function(x) x.IsVideo AndAlso MediaStudio.InstalledVariant(x) IsNot Nothing)
            Return Localizer.T(If(animOnly, "(Your video model needs a photo — attach one and ask again.)",
                                            "(No video model is downloaded yet — open 🎨 Create, pick a video task and get one.)"))
        End If
        Dim v = MediaStudio.InstalledVariant(r)
        Dim fast = AppCore.Settings.MediaFast AndAlso r.Speed IsNot Nothing
        Dim animate = photoFile IsNot Nothing AndAlso r.Can("animate")
        status?.Invoke(If(animate, "Animating the photo…", "Making the video…"))
        Dim req As New MediaStudio.GenerateRequest With {
            .Recipe = r, .Variant = v, .Prompt = If(String.IsNullOrWhiteSpace(prompt), "a beautiful scene, gentle camera movement", prompt), .Negative = r.Negative,
            .Task = If(animate, "animate", "create"), .InputImage = If(animate, photoFile, Nothing),
            .Width = If(r.Width > 0, r.Width, 832), .Height = If(r.Height > 0, r.Height, 480), .Frames = Math.Max(1, r.Frames),
            .Steps = If(fast, r.Speed.Steps, r.Steps), .Cfg = If(fast, r.Speed.Cfg, r.Cfg), .Fast = fast,
            .Loras = LoraLibrary.ForFamily(r.Family).Where(Function(l) l.Enabled).ToList()}
        Dim out = Await MediaStudio.GenerateAsync(req, Sub(p, t) status?.Invoke(If(p > 0, $"{t} ({p}%)", t)), ct)
        ' MP4 plays everywhere (iPhone too); keep the original if ffmpeg is not available.
        If out.EndsWith(".webm", StringComparison.OrdinalIgnoreCase) Then
            Try
                status?.Invoke("Converting to MP4…")
                out = Await VideoTools.ToMp4Async(out, status, ct)
            Catch ex As OperationCanceledException
                Throw
            Catch
            End Try
        End If
        Return $"[🎬 {EscapeAlt(prompt)}](https://outputs.localmind/{Uri.EscapeDataString(Path.GetFileName(out))})" & vbCrLf & vbCrLf & $"_{r.Name} · {v.Label}_"
    End Function

    Public Async Function MakeSongAsync(style As String, lyrics As String, status As Action(Of String), ct As CancellationToken) As Task(Of String)
        Dim m = AudioStudio.Models().FirstOrDefault(Function(x) x.IsMusic AndAlso AudioStudio.IsInstalled(x))
        If m Is Nothing Then Return Localizer.T("(No music model is downloaded yet — open 🎵 Music & voice to get one.)")
        status?.Invoke("Composing…")
        Dim wav = Await AudioStudio.GenerateAsync(New AudioStudio.AudioRequest With {.Model = m, .Text = style, .Lyrics = lyrics},
                                                  Sub(p, t) status?.Invoke(If(p > 0, $"{t} ({p}%)", t)), ct)
        Return $"[🎵 {EscapeAlt(style)}](https://outputs.localmind/{Uri.EscapeDataString(Path.GetFileName(wav))})"
    End Function

    '──────────────── voice cloning in the chat ────────────────
    Public ReadOnly AudioExtensions As String() = {".mp3", ".wav", ".m4a", ".ogg", ".oga", ".opus", ".flac", ".aac", ".wma", ".webm", ".mp4", ".mov", ".mkv"}

    Public Function IsAudioFile(path As String) As Boolean
        Return AudioExtensions.Contains(IO.Path.GetExtension(If(path, "")).ToLowerInvariant())
    End Function

    ''' <summary>Where sound files attached in the chat are kept (they stay with the conversation).</summary>
    Public ReadOnly Property ChatAudioFolder As String
        Get
            Dim d = Path.Combine(AppPaths.Roaming, "ChatAudio")
            Directory.CreateDirectory(d)
            Return d
        End Get
    End Property

    ''' <summary>Copies a sound file the user attached into the chat audio folder and returns the copy.</summary>
    Public Function KeepChatAudio(source As String) As String
        Dim dest = Path.Combine(ChatAudioFolder, $"{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N").Substring(0, 6)}-{Path.GetFileName(source)}")
        File.Copy(source, dest, True)
        Return dest
    End Function

    ''' <summary>A voice model that can copy a voice is installed.</summary>
    Public Function CanCloneVoice() As Boolean
        Return AudioStudio.Models().Any(Function(m) Not m.IsMusic AndAlso m.Clone AndAlso AudioStudio.IsInstalled(m))
    End Function

    ''' <summary>The newest sound file attached in the conversation (latest message first).</summary>
    Public Function LastRecording(payload As List(Of ChatMessage)) As String
        If payload Is Nothing Then Return Nothing
        For i = payload.Count - 1 To 0 Step -1
            Dim a = payload(i).AudioFiles
            If a IsNot Nothing Then
                For j = a.Count - 1 To 0 Step -1
                    If File.Exists(a(j)) Then Return a(j)
                Next
            End If
        Next
        Return Nothing
    End Function

    ''' <summary>What to say when the chat model did not fill in the text: the words after "say" / ":" or in quotes.</summary>
    Public Function WordsToSay(userText As String) As String
        Dim t = If(userText, "")
        t = Regex.Replace(t, "\[Attached [^\]]*\]", "").Trim()
        Dim q = Regex.Match(t, "[""" & ChrW(&H201C) & "«](.+?)[""" & ChrW(&H201D) & "»]", RegexOptions.Singleline)
        If q.Success Then Return q.Groups(1).Value.Trim()
        Dim c = t.IndexOf(":"c)
        If c >= 0 AndAlso c < t.Length - 1 Then Return t.Substring(c + 1).Trim()
        Dim s = Regex.Match(t, "\b(say|says|saying|speak|read)\b\s+(.+)$|(πες|λέει|να πει|διάβασε)\s+(.+)$", RegexOptions.IgnoreCase Or RegexOptions.Singleline)
        If s.Success Then Return If(s.Groups(2).Success AndAlso s.Groups(2).Value <> "", s.Groups(2).Value, s.Groups(4).Value).Trim()
        Return t
    End Function

    ''' <summary>
    ''' Speaks the words in the voice of the attached recording (or of a saved voice). The recording is cut to a clean
    ''' 15-second sample, its words are written down when the model works better with them, and it is kept in
    ''' the voice library ("saveAs", or "Last chat voice"), so later messages can use it by name.
    ''' </summary>
    Public Async Function CloneVoiceAsync(words As String, recording As String, voiceName As String, saveAs As String,
                                          status As Action(Of String), ct As CancellationToken) As Task(Of String)
        Dim m = AudioStudio.Models().Where(Function(x) Not x.IsMusic AndAlso x.Clone AndAlso AudioStudio.IsInstalled(x)).
                    OrderBy(Function(x) If(x.Id = AppCore.Settings.ReadAloudModel, 0, 1)).FirstOrDefault()
        If m Is Nothing Then Return Localizer.T("(No voice model that can copy a voice is downloaded yet — get Qwen3-TTS, Chatterbox or VoxCPM2 in Models → 🎵 Music & voice models.)")
        If String.IsNullOrWhiteSpace(words) Then Return Localizer.T("(Tell me what the voice should say, e.g. clone this voice and say: Good morning!)")

        Dim name As String
        If String.IsNullOrEmpty(recording) Then
            Dim saved = If(String.IsNullOrWhiteSpace(voiceName), Nothing, VoiceLibrary.Find(voiceName))
            If saved Is Nothing Then saved = If(String.IsNullOrWhiteSpace(saveAs), Nothing, VoiceLibrary.Find(saveAs))
            If saved Is Nothing Then
                Dim names = VoiceLibrary.All().Select(Function(v) v.Name).ToList()
                Return Localizer.T("(Attach a recording of the voice (MP3, WAV, M4A…) to your message and ask again.)") &
                       If(names.Count > 0, "  " & Localizer.T("Saved voices") & ": " & String.Join(", ", names), "")
            End If
            name = saved.Name
        Else
            ' a clean sample: 15 seconds, mono, from just after the start
            status?.Invoke(Localizer.T("Preparing the voice sample…"))
            Dim info = Await VideoTools.InfoAsync(recording, ct)
            Dim start = If(info.Seconds > 20, 1.0, 0.0)
            Dim len = Math.Min(15.0, Math.Max(1.0, info.Seconds - start))
            Dim inv = Globalization.CultureInfo.InvariantCulture
            Dim sample = Path.Combine(Path.GetTempPath(), $"localmind-voice-{Guid.NewGuid():N}.wav")
            Await VideoTools.FfmpegAsync($"-hide_banner -loglevel error -y -ss {start.ToString("0.###", inv)} -t {len.ToString("0.###", inv)} -i ""{recording}"" -vn -ac 1 -ar 24000 -c:a pcm_s16le ""{sample}""", ct)
            If Not File.Exists(sample) Then Throw New Exception(Localizer.T("Could not read the sound in the attached file."))
            Dim spoken = ""
            If m.Transcript Then
                Try
                    Dim w16 = Path.Combine(Path.GetTempPath(), $"localmind-voice16-{Guid.NewGuid():N}.wav")
                    Await VideoTools.CutAudioAsync(sample, 0, len, w16, ct)
                    status?.Invoke(Localizer.T("Writing down the words of the recording…"))
                    spoken = Await Voice.TranscribeAsync(w16, Sub(t) status?.Invoke(Localizer.T(t)), ct)
                    Try : File.Delete(w16) : Catch : End Try
                Catch ex As OperationCanceledException
                    Throw
                Catch
                    spoken = ""          ' (cloning still works without the words, a little less close)
                End Try
            End If
            name = If(String.IsNullOrWhiteSpace(saveAs), Localizer.T("Last chat voice"), saveAs.Trim())
            VoiceLibrary.Add(name, sample, If(spoken, "").Trim(), If(Voice.IsGreek(words), "el", "en"))
            Try : File.Delete(sample) : Catch : End Try
        End If

        Dim wav = Await VideoMaker.SpeakAsync(words, name, Sub(p, t) status?.Invoke(If(p > 0, $"{t} ({p}%)", t)), ct)
        Dim note = $"_{Localizer.T("Voice")}: {name} · {m.Name}_"
        If Not String.IsNullOrWhiteSpace(saveAs) Then note &= "  " & String.Format(Localizer.T("(saved — say ""with {0}'s voice"" next time)"), name)
        Return $"[🗣 {EscapeAlt(words)}](https://outputs.localmind/{Uri.EscapeDataString(Path.GetFileName(wav))})" & vbCrLf & vbCrLf & note
    End Function
End Module
