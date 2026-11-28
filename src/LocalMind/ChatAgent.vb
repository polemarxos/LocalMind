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
    ' "extend the video by 6 seconds", "continue it", "συνέχισε το βίντεο"
    Private ReadOnly AskExtend As New Regex("\b(extend|continue|lengthen|prolong)\b[^.?!\n]{0,50}\b(video|clip|it|animation)\b|\bmake (it|the video|the clip) longer\b|" &
                                            "(επέκτεινε|συνέχισε|μάκρυνε)[^.?!;\n]{0,50}(βίντεο|κλιπ|το)", RegexOptions.IgnoreCase Or RegexOptions.Compiled)
    ' "change the sky in that picture", "edit the image: …"
    Private ReadOnly AskEditPicture As New Regex("\b(edit|change|modify|recolou?r|retouch|fix|remove|replace|add)\b[^.?!\n]{0,50}\b(that|this|the|last|previous|my)\s+(image|picture|photo|drawing)\b|" &
                                                 "\b(edit|retouch)\b\s+(it|the (image|picture|photo))\b|" &
                                                 "(άλλαξε|επεξεργάσου|διόρθωσε|αφαίρεσε|πρόσθεσε)[^.?!;\n]{0,50}(εικόνα|φωτογραφία)", RegexOptions.IgnoreCase Or RegexOptions.Compiled)
    Private ReadOnly HowTo As New Regex("\bhow (do|to|can|should|would)\b|\bπώς\b|\bπως\b|\btutorial\b", RegexOptions.IgnoreCase Or RegexOptions.Compiled)

    ''' <summary>The tool the user plainly asked for in their latest message ("generate me a video"), if offered.</summary>
    Public Function RequestedTool(text As String, tools As JArray) As String
        If tools Is Nothing OrElse String.IsNullOrWhiteSpace(text) OrElse HowTo.IsMatch(text) Then Return Nothing
        Dim offered = tools.Select(Function(t) t("function")?.Value(Of String)("name")).ToList()
        ' The request that comes first in the message ("a picture … then a video of it" starts with the picture);
        ' on a tie: voice, then video, then picture.
        Dim best As String = Nothing, bestAt = Integer.MaxValue
        For Each c In {("clone_voice", AskClone), ("extend_video", AskExtend), ("generate_video", AskVideo), ("generate_image", AskEditPicture), ("generate_image", AskPicture)}
            Dim m = c.Item2.Match(text)
            If m.Success AndAlso offered.Contains(c.Item1) AndAlso m.Index < bestAt Then best = c.Item1 : bestAt = m.Index
        Next
        Return best
    End Function

    ''' <summary>Tools offered to the model, depending on the abilities that are switched on and installed.</summary>
    Public Function Tools(model As String, offerWebSearch As Boolean) As JArray
        Dim s = AppCore.Settings
        If Not s.AgentTools Then Return Nothing
        If NoToolModels.Contains(If(model, "")) Then Return Nothing
        Dim list As New JArray()
        If s.EnableImages Then
            list.Add(Tool("generate_image", "Create a picture with the local image model, or change one: a photo the user attached, or (edit_last_picture) the last picture made in this chat. " &
                          "Use it when the user asks for a picture, drawing, photo, logo or illustration, or asks to edit / change a picture.",
                New JObject From {{"prompt", New JObject From {{"type", "string"}, {"description", "Detailed English description of the picture: subject, setting, style, lighting. Keep any text that must appear exactly. Keep every @Name exactly as the user wrote it (saved characters and places)."}}},
                                  {"megapixels", New JObject From {{"type", "number"}, {"description", "Picture size in megapixels when the user asked for one (e.g. 0.5, 1, 2)"}}},
                                  {"edit_last_picture", New JObject From {{"type", "boolean"}, {"description", "true to change the last picture made in this chat (the prompt then says only what to change)"}}},
                                  {"persona", PersonaArg()}},
                {"prompt"}))
            list.Add(Tool("generate_video", "Create a short video clip (a few seconds, with sound on models that make it) with the local video model. If the user attached a photo, it is animated; " &
                          "use_last_picture animates a picture made earlier in the chat. Use it when the user asks for a video, clip or animation, or asks a saved @character to say or do something " &
                          "(a talking video: put the exact spoken words in quotes in the prompt; their saved voice is used).",
                New JObject From {{"prompt", New JObject From {{"type", "string"}, {"description", "Detailed English description of the video: subject, action and motion, camera movement, setting, style, lighting. Keep every @Name exactly as the user wrote it (saved characters and places)."}}},
                                  {"use_last_picture", New JObject From {{"type", "boolean"}, {"description", "true to start the video from the picture made earlier in this chat (the user says 'use that image', 'animate it'…)"}}},
                                  {"seconds", New JObject From {{"type", "number"}, {"description", "Length in seconds when the user asked for one"}}},
                                  {"megapixels", New JObject From {{"type", "number"}, {"description", "Frame size in megapixels when the user asked for one (e.g. 0.2, 0.4)"}}},
                                  {"voice", New JObject From {{"type", "string"}, {"description", "A saved voice (@name) the people in the video speak with, or 'attached' for the recording the user attached. Write the spoken words in the prompt."}}},
                                  {"persona", PersonaArg()}},
                {"prompt"}))
        End If
        If s.EnableAudio AndAlso AudioStudio.Models().Any(Function(m) m.IsMusic AndAlso AudioStudio.IsInstalled(m)) Then
            list.Add(Tool("make_song", "Compose a song with vocals (or an instrumental) with the local music model. Use it only when the user asks for a song or music.",
                New JObject From {
                    {"style", New JObject From {{"type", "string"}, {"description", "Genre, mood, instruments, voice, tempo, in English"}}},
                    {"lyrics", New JObject From {{"type", "string"}, {"description", "Lyrics with [verse] and [chorus] tags, in the user's language; empty for instrumental"}}},
                    {"persona", PersonaArg()}},
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
        If s.EnableImages Then
            list.Add(Tool("extend_video", "Continue the last video made in this chat: a new clip that starts where it ends (the user can then merge both into one video). " &
                          "Use it when the user asks to extend / continue / make longer the video.",
                New JObject From {
                    {"prompt", New JObject From {{"type", "string"}, {"description", "Detailed English description of what happens next in the continuation"}}},
                    {"seconds", New JObject From {{"type", "number"}, {"description", "How many seconds to add"}}}},
                {"prompt"}))
        End If
        list.Add(Tool("set_persona", "Change your persona (role) for the rest of this chat, when the user says 'your persona is …', 'be a …', 'act as …' for the conversation (not for a single picture/video step: use that tool's persona field for that).",
            New JObject From {{"name", New JObject From {{"type", "string"}, {"description", "The persona the user named, e.g. 'Simple Teacher' or 'image prompt writer'"}}}}, {"name"}))
        If offerWebSearch Then
            list.Add(Tool("web_search", "Search the web for current or unknown information (news, prices, recent events, facts you are unsure about).",
                New JObject From {{"query", New JObject From {{"type", "string"}, {"description", "Search words"}}}}, {"query"}))
        End If
        Return If(list.Count > 0, list, Nothing)
    End Function

    Private Function PersonaArg() As JObject
        Dim names = String.Join(", ", AppCore.Personas.Load().Select(Function(p) p.Name).Take(20))
        Return New JObject From {{"type", "string"}, {"description",
            "The persona (role) the user asked to write this step's prompt, e.g. 'image prompt writer' or 'video prompt writer'. Leave empty when the user named none." &
            If(names <> "", " Saved personas: " & names, "")}}
    End Function

    ''' <summary>Raised when the chat asks to switch persona ("your persona is …"); the window picks or creates it.</summary>
    Public Event PersonaRequested As Action(Of Persona)

    ' "then", "after that", "μετά"… : the message asks for several steps in a row
    Private ReadOnly MultiStep As New Regex("\b(then|after (that|you|this|it)|afterwards|next|and also|once (done|finished|you))\b|\bpersona\b|" &
                                            "(μετά|έπειτα|ύστερα|στη συνέχεια|αφού|περσόνα)", RegexOptions.IgnoreCase Or RegexOptions.Compiled)
    Private ReadOnly PersonaIn As New Regex("(?:persona|role)\s+(?:is|=|:|to|will be)\s+(?:an?\s+|the\s+)?([^.,;:!?\n]{2,40}?)(?=\s+(?:and|then|to|for|who|that|which)\b|[.,;:!?\n]|$)|" &
                                            "(?:act|be|become)\s+(?:as\s+)?(?:an?|the)\s+([^.,;:!?\n]{2,40}?)(?=\s+(?:and|then|to|for)\b|[.,;:!?\n]|$)|" &
                                            "περσόνα\s+(?:σου\s+)?(?:είναι|θα είναι)\s+(?:ο\s+|η\s+|ένας\s+|μια\s+)?([^.,;:!?\n]{2,40}?)(?=\s+(?:και|μετά)\b|[.,;:!?\n]|$)",
                                            RegexOptions.IgnoreCase Or RegexOptions.Compiled)
    Private ReadOnly ThatPicture As New Regex("\b(that|this|the|same|previous|last)\s+(image|picture|photo|drawing)\b|\busing it\b|\banimate it\b|" &
                                              "(αυτή|ίδια|την)\s+(την\s+)?(εικόνα|φωτογραφία)", RegexOptions.IgnoreCase Or RegexOptions.Compiled)
    Private ReadOnly OutputLink As New Regex("https://outputs\.localmind/([^)\s""']+?\.(?:png|jpg|jpeg|webp))", RegexOptions.IgnoreCase Or RegexOptions.Compiled)

    ''' <summary>Every persona named in the message, in order ("your persona is image prompt writer … then your persona is video prompt writer").</summary>
    Public Function PersonasNamed(text As String) As List(Of String)
        Dim list As New List(Of String)()
        If String.IsNullOrEmpty(text) Then Return list
        For Each m As Match In PersonaIn.Matches(text)
            Dim g = If(m.Groups(1).Success, m.Groups(1).Value, If(m.Groups(2).Success, m.Groups(2).Value, m.Groups(3).Value)).Trim()
            If g.Length > 1 Then list.Add(g)
        Next
        Return list
    End Function

    ''' <summary>A saved persona with this name (emoji and case ignored, partial names too) or a new one made for the role.</summary>
    Public Function FindOrMakePersona(name As String, ByRef isNew As Boolean) As Persona
        isNew = False
        Dim want = PlainName(name)
        If want = "" Then Return Nothing
        Dim all = AppCore.Personas.Load()
        Dim p = all.FirstOrDefault(Function(x) String.Equals(PlainName(x.Name), want, StringComparison.OrdinalIgnoreCase))
        If p Is Nothing Then p = all.FirstOrDefault(Function(x) PlainName(x.Name).IndexOf(want, StringComparison.OrdinalIgnoreCase) >= 0 OrElse
                                                                 (PlainName(x.Name).Length > 3 AndAlso want.IndexOf(PlainName(x.Name), StringComparison.OrdinalIgnoreCase) >= 0))
        If p IsNot Nothing Then Return p
        isNew = True
        Dim title = Char.ToUpper(want(0)) & want.Substring(1)
        Return New Persona With {.Name = "🎭 " & title, .Temperature = 0.7,
            .SystemPrompt = $"You are an expert {want}. Stay in this role: do the work a skilled {want} would do, with their knowledge, vocabulary and standards. Answer in the user's language."}
    End Function

    Private Function PlainName(n As String) As String
        Dim t = If(n, "").Trim()
        Dim i = 0
        While i < t.Length AndAlso Not Char.IsLetterOrDigit(t(i))
            i += 1
        End While
        t = t.Substring(i).Trim()
        If t.StartsWith("a ", StringComparison.OrdinalIgnoreCase) Then t = t.Substring(2)
        If t.StartsWith("an ", StringComparison.OrdinalIgnoreCase) Then t = t.Substring(3)
        If t.StartsWith("the ", StringComparison.OrdinalIgnoreCase) Then t = t.Substring(4)
        Return t.Trim()
    End Function

    ''' <summary>Lets a persona write the prompt of one step (picture, video or song) and returns it.</summary>
    Private Async Function WriteWithPersonaAsync(personaName As String, kind As String, draft As String, userText As String, earlier As String,
                                                 model As String, status As Action(Of String), ct As CancellationToken) As Task(Of (Prompt As String, Name As String))
        Dim isNew As Boolean
        Dim p = FindOrMakePersona(personaName, isNew)
        If p Is Nothing Then Return (draft, Nothing)
        status?.Invoke("🎭 " & PlainName(p.Name) & "…")
        Dim msgs As New List(Of ChatMessage) From {
            New ChatMessage With {.Role = "system", .Content = p.SystemPrompt & vbLf & vbLf &
                $"Your task now: write the prompt for a {kind} model. Reply with the prompt only — one paragraph in English, no title, no quotes, no explanations."},
            New ChatMessage With {.Role = "user", .Content = "The user's request: " & userText & vbLf & vbLf &
                If(String.IsNullOrWhiteSpace(earlier), "", "Already done: " & earlier & vbLf & vbLf) &
                $"Write the {kind} prompt for this step: " & If(String.IsNullOrWhiteSpace(draft), "(see the request)", draft)}}
        Try
            Dim r = Await AppCore.Backend.StreamChatAsync(msgs, New ChatRequestOptions With {.Model = model, .Temperature = p.Temperature, .EnableThinking = False},
                                                          Nothing, Nothing, ct)
            Dim t = If(r?.Content, "").Trim().Trim(ChrW(34), ChrW(&H201C), ChrW(&H201D)).Trim()
            Return (If(t.Length >= 8, t, draft), PlainName(p.Name))
        Catch ex As OperationCanceledException
            Throw
        Catch
            Return (draft, Nothing)
        End Try
    End Function

    Private ReadOnly VideoLink As New Regex("https://outputs\.localmind/([^)\s""']+?\.(?:mp4|webm))", RegexOptions.IgnoreCase Or RegexOptions.Compiled)

    ''' <summary>The last video made in this chat (this answer first, then earlier ones), if any.</summary>
    Public Function LastMadeVideo(md As String, payload As List(Of ChatMessage)) As String
        Dim texts As New List(Of String) From {md}
        If payload IsNot Nothing Then texts.AddRange(payload.Where(Function(m) m.Role = "assistant").Reverse().Select(Function(m) m.Content))
        For Each txt As String In texts
            Dim ms = VideoLink.Matches(If(txt, ""))
            For i = ms.Count - 1 To 0 Step -1
                Dim f = Path.Combine(MediaStudio.OutputFolder, Uri.UnescapeDataString(ms(i).Groups(1).Value))
                If File.Exists(f) Then Return f
            Next
        Next
        Return Nothing
    End Function

    ''' <summary>
    ''' ➕ Continues the last video: its last frame starts a new clip. The answer shows the new clip and a button that
    ''' joins both into one video.
    ''' </summary>
    Public Async Function ExtendVideoAsync(prompt As String, size As MediaSize, md As String, payload As List(Of ChatMessage),
                                           status As Action(Of String), ct As CancellationToken) As Task(Of String)
        Dim first = LastMadeVideo(md, payload)
        If first Is Nothing Then Return Localizer.T("(There is no video in this chat to extend yet — make one first.)")
        status?.Invoke("Taking the last frame of the video…")
        Dim frame = Path.Combine(Path.GetTempPath(), $"localmind-lastframe-{Guid.NewGuid():N}.png")
        Await VideoTools.FfmpegAsync($"-hide_banner -loglevel error -y -sseof -0.25 -i ""{first}"" -update 1 -frames:v 1 ""{frame}""", ct)
        If Not File.Exists(frame) Then Throw New Exception(Localizer.T("Could not read the last frame of the video."))
        Dim original = MediaStudio.LoadSettingsFor(first)
        Dim text = prompt
        If original IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(original.Prompt) Then
            text = prompt.Trim().TrimEnd("."c) & ". Continue the same scene, same look and style as before."
        End If
        Dim made = Await MakeVideoAsync(text, frame, status, ct, Nothing, size)
        Try : File.Delete(frame) : Catch : End Try
        Dim m = VideoLink.Match(made)
        If Not m.Success Then Return made
        Dim second = Uri.UnescapeDataString(m.Groups(1).Value)
        Return made & vbCrLf & vbCrLf &
               $"[🔗 {Localizer.T("Merge both into one video")}](https://action.localmind/merge?a={Uri.EscapeDataString(Path.GetFileName(first))}&b={Uri.EscapeDataString(second)})"
    End Function

    ''' <summary>The last picture made in this chat (by this answer or an earlier one) as a file, if any.</summary>
    Private Function LastMadePicture(md As String, payload As List(Of ChatMessage)) As String
        Dim texts As New List(Of String) From {md}
        If payload IsNot Nothing Then texts.AddRange(payload.Where(Function(m) m.Role = "assistant").Reverse().Select(Function(m) m.Content))
        For Each txt As String In texts
            Dim ms = OutputLink.Matches(If(txt, ""))
            If ms.Count = 0 Then Continue For
            Dim f = Path.Combine(MediaStudio.OutputFolder, Uri.UnescapeDataString(ms(ms.Count - 1).Groups(1).Value))
            If File.Exists(f) Then Return f
        Next
        Return Nothing
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
            ' "required" makes the engine force the tool call from the first word, which cuts a thinking model's
            ' reasoning off halfway. With thinking on, the model may think to the end and then calls the tool (the only
            ' one offered); if it still answers in words, the tool runs anyway below.
            opts.ToolChoice = If(opts.EnableThinking, "auto", "required")
            If opts.EnableThinking AndAlso payload IsNot Nothing AndAlso payload.Count > 0 Then
                payload.Insert(payload.Count - 1, New ChatMessage With {.Role = "system",
                    .Content = $"The user's next message asks for something the {forced} tool makes. Think it through as long as you need, then call {forced} (do not answer in words instead)."})
            End If
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
        Dim multi = MultiStep.IsMatch(If(userText, ""))
        Dim named = PersonasNamed(userText)
        If result.ToolCalls.Count = 0 AndAlso forced IsNot Nothing Then
            ' The model could not call the tool: run it with the user's own words.
            Dim a As New JObject From {{"prompt", userText}}
            If named.Count > 0 Then a("persona") = named(0)
            result.ToolCalls.Add(New ToolCall With {.Name = forced, .Arguments = a.ToString()})
        End If
        If result.ToolCalls.Count = 0 Then Return (result, "", Nothing)

        Dim md As New Text.StringBuilder()
        Dim said As New Text.StringBuilder()
        Dim sources As List(Of SourceRef) = Nothing
        ' 🎭 Cast: characters and places the user wrote as @Name keep their look (description + pictures)
        Dim cast = CastLibrary.Involved(userText, New List(Of CastLibrary.CastMember)())
        Dim doneSteps As New List(Of String)()
        Dim asked = payload.ToList()   ' (the photo / recording the user attached stay with their message as more steps follow)
        Dim seen As New HashSet(Of String)()
        Dim stepNo = 0
        Do
            Dim searched = False
            Dim stepsThisRound As New List(Of String)()
            For Each tc In result.ToolCalls
                Dim args As JObject
                Try
                    args = If(String.IsNullOrWhiteSpace(tc.Arguments), New JObject(), JObject.Parse(tc.Arguments))
                Catch
                    args = New JObject()
                End Try
                ' the same step twice (small models repeat themselves) runs once
                If Not seen.Add(tc.Name & "|" & args.ToString(Newtonsoft.Json.Formatting.None)) Then Continue For
                Try
                    ' 🎭 a persona the user named writes this step's prompt
                    Dim pname = args.Value(Of String)("persona")
                    If String.IsNullOrWhiteSpace(pname) AndAlso named.Count > 0 AndAlso tc.Name <> "set_persona" AndAlso tc.Name <> "web_search" Then
                        pname = named(Math.Min(stepNo, named.Count - 1))
                    End If
                    Dim byLine = ""
                    Select Case tc.Name
                        Case "generate_image", "generate_video"
                            Dim prompt = If(args.Value(Of String)("prompt"), "")
                            If Not String.IsNullOrWhiteSpace(pname) Then
                                Dim w = Await WriteWithPersonaAsync(pname, If(tc.Name = "generate_image", "picture", "video"), prompt, userText,
                                                                    String.Join("; ", doneSteps), opts.Model, status, ct)
                                prompt = w.Prompt
                                If w.Name IsNot Nothing Then byLine = $"_🎭 {w.Name}:_ {EscapeAlt(prompt)}"
                            End If
                            If byLine <> "" Then md.AppendLine().AppendLine().Append(byLine)
                            Dim size = MediaSize.From(args, userText)
                            If tc.Name = "generate_image" Then
                                ' ✏ change a picture: the attached one, or the last one made in the chat when asked
                                Dim toEdit = LastPhoto(asked)
                                If toEdit Is Nothing AndAlso (args.Value(Of Boolean?)("edit_last_picture") = True OrElse AskEditPicture.IsMatch(If(userText, ""))) Then
                                    toEdit = LastMadePicture(md.ToString(), payload)
                                End If
                                md.AppendLine().AppendLine().Append(Await MakePictureAsync(prompt, toEdit, status, ct, cast, size))
                                stepsThisRound.Add(If(toEdit IsNot Nothing, "picture changed (", "picture made (") & EscapeAlt(prompt) & ")")
                            Else
                                Dim photo = LastPhoto(asked)
                                If photo Is Nothing AndAlso (args.Value(Of Boolean?)("use_last_picture") = True OrElse
                                                             ThatPicture.IsMatch(If(userText, ""))) Then
                                    photo = LastMadePicture(md.ToString(), payload)
                                End If
                                Dim voices = VoicesFor(args.Value(Of String)("voice"), userText, asked)
                                md.AppendLine().AppendLine().Append(Await MakeVideoAsync(prompt, photo, status, ct, cast, size, voices))
                                stepsThisRound.Add("video made" & If(photo IsNot Nothing, " from the picture", "") & " (" & EscapeAlt(prompt) & ")")
                            End If
                        Case "extend_video"
                            Dim prompt = If(args.Value(Of String)("prompt"), userText)
                            md.AppendLine().AppendLine().Append(Await ExtendVideoAsync(prompt, MediaSize.From(args, userText), md.ToString(), payload, status, ct))
                            stepsThisRound.Add("video extended")
                        Case "clone_voice"
                            Dim words = If(args.Value(Of String)("text"), args.Value(Of String)("prompt"))
                            ' (a forced call carries the whole message: keep only the words to say)
                            If String.IsNullOrWhiteSpace(words) OrElse words = userText Then words = WordsToSay(userText)
                            md.AppendLine().AppendLine().Append(Await CloneVoiceAsync(words, LastRecording(asked), args.Value(Of String)("voice_name"),
                                                                                      args.Value(Of String)("save_as"), status, ct))
                            stepsThisRound.Add("voice clip made")
                        Case "make_song"
                            Dim style = If(args.Value(Of String)("style"), "")
                            If Not String.IsNullOrWhiteSpace(pname) Then
                                Dim w = Await WriteWithPersonaAsync(pname, "music (style description)", style, userText, String.Join("; ", doneSteps), opts.Model, status, ct)
                                style = w.Prompt
                                If w.Name IsNot Nothing Then md.AppendLine().AppendLine().Append($"_🎭 {w.Name}:_ {EscapeAlt(style)}")
                            End If
                            md.AppendLine().AppendLine().Append(Await MakeSongAsync(style, args.Value(Of String)("lyrics"), status, ct))
                            stepsThisRound.Add("song made")
                        Case "set_persona"
                            Dim isNew As Boolean
                            Dim p = FindOrMakePersona(args.Value(Of String)("name"), isNew)
                            If p IsNot Nothing Then
                                RaiseEvent PersonaRequested(p)
                                md.AppendLine().AppendLine().Append("_🎭 " & Localizer.T("Persona") & ": " & p.Name & "_")
                                stepsThisRound.Add("persona is now " & p.Name)
                                ' the rest of this answer already speaks as the new persona
                                Dim sys = payload.FirstOrDefault(Function(m) m.Role = "system")
                                If sys IsNot Nothing Then sys.Content = p.SystemPrompt & vbLf & vbLf & sys.Content Else payload.Insert(0, New ChatMessage With {.Role = "system", .Content = p.SystemPrompt})
                            End If
                        Case "web_search"
                            status?.Invoke("Searching the web…")
                            Dim q = args.Value(Of String)("query")
                            If Not String.IsNullOrWhiteSpace(q) Then payload.Insert(payload.Count - 1, New ChatMessage With {.Role = "system", .Content = "Search query: " & q})
                            sources = Await AppCore.AugmentAsync(payload, False, True, status, ct)
                            opts.Tools = Nothing
                            beforeFollowup?.Invoke()
                            result = Await streamOnce(opts)
                            searched = True
                    End Select
                Catch ex As OperationCanceledException
                    Throw
                Catch ex As Exception
                    Dim ls = ex.Message.Split({vbCr, vbLf}, StringSplitOptions.RemoveEmptyEntries)
                    md.AppendLine().AppendLine().Append("⚠ " & String.Join("  " & vbLf & "⚠ ", ls.Skip(Math.Max(0, ls.Length - 4))))
                    stepsThisRound.Add(tc.Name & " failed")
                End Try
                stepNo += 1
            Next
            doneSteps.AddRange(stepsThisRound)
            ' One step at a time: "picture, then a video of it" → the model is asked for the next step after each one.
            Dim more = (multi OrElse result.ToolCalls.Any(Function(t) t.Name = "set_persona")) AndAlso Not searched AndAlso
                       stepsThisRound.Count > 0 AndAlso stepNo < 6
            If Not more Then Exit Do
            If Not String.IsNullOrWhiteSpace(result.Content) Then said.AppendLine(result.Content.Trim()).AppendLine()
            ' the "call this tool" note was for the first step only
            payload.RemoveAll(Function(m) m.Role = "system" AndAlso m.Content.StartsWith("The user's next message asks for something the ", StringComparison.Ordinal))
            payload.Add(New ChatMessage With {.Role = "assistant", .Content = If(result.Content, "").Trim() & If(String.IsNullOrWhiteSpace(result.Content), "", vbLf) & "[Done: " & String.Join("; ", stepsThisRound) & "]"})
            payload.Add(New ChatMessage With {.Role = "user", .Content =
                "(LocalMind) Done so far: " & String.Join("; ", doneSteps) & ". If my request above has a next step that is not done yet, do it now by calling its tool " &
                "(with the persona I named for that step, and use_last_picture=true if it should use the picture). If everything is done, reply with one short sentence and no tool call."})
            opts.Tools = Tools(opts.Model, offerWebSearch)
            opts.ToolChoice = Nothing
            beforeFollowup?.Invoke()
            result = Await streamOnce(opts)
            If result.ToolCalls.Count = 0 Then Exit Do
        Loop
        If said.Length > 0 Then result.Content = said.ToString() & If(result.Content, "")
        Return (result, md.ToString(), sources)
    End Function

    '──────────────── pictures and songs for the chat ────────────────
    ''' <summary>The chosen chat image model if downloaded, else the best one: with a photo one that edits or restyles, else one that creates.</summary>
    Public Function PickImageRecipe(withPhoto As Boolean) As MediaRecipe
        Dim all = MediaStudio.Recipes().Where(Function(r) Not r.IsVideo AndAlso Not r.IsUpscaler AndAlso r.Kind <> "tool" AndAlso MediaStudio.UsableVariant(r) IsNot Nothing).
                  Where(Function(r) If(withPhoto, r.Can("edit") OrElse r.Can("restyle"), r.Can("create"))).ToList()
        ' the default picture (or edit) profile's model first, then the default model
        Dim prof = If(withPhoto, ProfileLibrary.ForChatEdit(), ProfileLibrary.ForChat(False))
        Dim chosen = If(If(prof IsNot Nothing, all.FirstOrDefault(Function(r) r.Id = prof.Model), Nothing),
                        all.FirstOrDefault(Function(r) r.Id = AppCore.Settings.ChatImageModel))
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
                                           Optional cast As List(Of CastLibrary.CastMember) = Nothing,
                                           Optional size As MediaSize = Nothing) As Task(Of String)
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
        If r Is Nothing Then Return Localizer.T("(No picture model is downloaded yet — open ⬇ Get models → Picture to get one.)")
        Dim v = MediaStudio.UsableVariant(r)
        ' 🧩 the default picture (or edit) profile: its LoRAs, trigger words and prompts
        Dim prof = If(photoFile IsNot Nothing, ProfileLibrary.ForChatEdit(), ProfileLibrary.ForChat(False))
        If prof IsNot Nothing AndAlso prof.Model <> r.Id Then prof = Nothing
        Dim fast = If(prof IsNot Nothing, prof.Fast, AppCore.Settings.MediaFast) AndAlso r.Speed IsNot Nothing
        Dim composed = ProfileLibrary.Compose(prof, prompt, r.Negative)
        Dim loras = If(prof IsNot Nothing, ProfileLibrary.LorasOf(prof), LoraLibrary.ForFamily(r.Family).Where(Function(l) l.Enabled).ToList())
        loras.AddRange(CastLibrary.LorasFor(cast, r.Family).Where(Function(l) Not loras.Any(Function(x) x.Name = l.Name)))
        Dim wh = If(size, New MediaSize()).Dims(If(r.Width > 0, r.Width, 1024), If(r.Height > 0, r.Height, 1024))
        status?.Invoke(If(photoFile IsNot Nothing, "Editing the picture…", "Drawing…") & If(prof IsNot Nothing, " 🧩 " & prof.Name, ""))
        Dim req As New MediaStudio.GenerateRequest With {
            .Recipe = r, .Variant = v, .Prompt = If(String.IsNullOrWhiteSpace(composed.Prompt), "a beautiful picture", composed.Prompt), .Negative = composed.Negative,
            .Task = If(combine, "combine", MediaStudio.GuessTask(r, photoFile IsNot Nothing, False, False)),
            .InputImages = If(combine, castPics.Take(Math.Max(1, r.Refs)).ToList(), New List(Of String)()),
            .Width = wh.W, .Height = wh.H,
            .Steps = If(fast, r.Speed.Steps, r.Steps), .Cfg = If(fast, r.Speed.Cfg, r.Cfg), .Fast = fast, .Strength = 0.6,
            .InputImage = photoFile, .Loras = loras}
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
        Dim prof = ProfileLibrary.ForChat(True)
        Dim chosen = If(If(prof IsNot Nothing, ok.FirstOrDefault(Function(r) r.Id = prof.Model), Nothing),
                        ok.FirstOrDefault(Function(r) r.Id = AppCore.Settings.ChatVideoModel))
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
                                         Optional cast As List(Of CastLibrary.CastMember) = Nothing,
                                         Optional size As MediaSize = Nothing,
                                         Optional voices As List(Of (Who As String, File As String)) = Nothing) As Task(Of String)
        cast = If(cast, New List(Of CastLibrary.CastMember)())
        size = If(size, New MediaSize())
        ' 🗣 Voices: the characters' own voices and any voice asked for → a model that takes reference pictures and
        ' voices (MiniMax H3 Ref2VA) makes the people look and sound like them.
        Dim audios As New List(Of (Who As String, File As String))()
        For Each m In cast.Where(Function(x) Not x.IsLocation AndAlso Not String.IsNullOrWhiteSpace(x.Voice))
            Dim sv = VoiceLibrary.Find(m.Voice)
            If sv IsNot Nothing Then audios.Add((m.Name, sv.FullPath))
        Next
        If voices IsNot Nothing Then audios.AddRange(voices.Where(Function(x) Not audios.Any(Function(a) a.File = x.File)))
        If audios.Count > 0 OrElse cast.Count > 0 Then
            Dim refModel = PickReferenceVideoRecipe()
            If refModel IsNot Nothing Then Return Await MakeReferenceVideoAsync(refModel, prompt, photoFile, cast, audios, size, status, ct)
        End If
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
                                            "(No video model is downloaded yet — open ⬇ Get models → Video to get one.)"))
        End If
        Dim v = MediaStudio.UsableVariant(r)
        Dim prof = ProfileLibrary.ForChat(True)
        If prof IsNot Nothing AndAlso prof.Model <> r.Id Then prof = Nothing
        Dim fast = If(prof IsNot Nothing, prof.Fast, AppCore.Settings.MediaFast) AndAlso r.Speed IsNot Nothing
        Dim composed = ProfileLibrary.Compose(prof, prompt, r.Negative)
        Dim loras = If(prof IsNot Nothing, ProfileLibrary.LorasOf(prof), LoraLibrary.ForFamily(r.Family).Where(Function(l) l.Enabled).ToList())
        Dim animate = photoFile IsNot Nothing AndAlso r.Can("animate")
        Dim wh = size.Dims(If(r.Width > 0, r.Width, 832), If(r.Height > 0, r.Height, 480))
        Dim frames = size.FramesFor(r)
        status?.Invoke(If(animate, "Animating the photo…", "Making the video…") & $" ({wh.W}×{wh.H}, {size.SecondsText(frames, r)})" &
                       If(prof IsNot Nothing, " 🧩 " & prof.Name, ""))
        Dim req As New MediaStudio.GenerateRequest With {
            .Recipe = r, .Variant = v, .Prompt = If(String.IsNullOrWhiteSpace(composed.Prompt), "a beautiful scene, gentle camera movement", composed.Prompt), .Negative = composed.Negative,
            .Task = If(animate, "animate", "create"), .InputImage = If(animate, photoFile, Nothing),
            .Width = wh.W, .Height = wh.H, .Frames = frames,
            .Steps = If(fast, r.Speed.Steps, r.Steps), .Cfg = If(fast, r.Speed.Cfg, r.Cfg), .Fast = fast,
            .Loras = loras}
        Dim out = Await MediaStudio.GenerateAsync(req, Sub(p, t) status?.Invoke(If(p > 0, $"{t} ({p}%)", t)), ct)
        out = Await ToMp4Async(out, status, ct)
        Return $"[🎬 {EscapeAlt(prompt)}](https://outputs.localmind/{Uri.EscapeDataString(Path.GetFileName(out))})" & vbCrLf & vbCrLf & $"_{r.Name} · {v.Label}_"
    End Function

    ''' <summary>A downloaded video model that takes reference pictures and voices (the video profile's model first).</summary>
    Public Function PickReferenceVideoRecipe() As MediaRecipe
        Dim ok = MediaStudio.Recipes().Where(Function(r) r.IsVideo AndAlso r.Can("reference") AndAlso MediaStudio.UsableVariant(r) IsNot Nothing).ToList()
        If ok.Count = 0 Then Return Nothing
        Dim prof = ProfileLibrary.ForChat(True)
        Dim chosen = If(prof IsNot Nothing, ok.FirstOrDefault(Function(r) r.Id = prof.Model), Nothing)
        If chosen IsNot Nothing Then Return chosen
        Dim hw = HardwareInfo.Current()
        Return ok.OrderBy(Function(r) If(MediaStudio.Fit(r, MediaStudio.UsableVariant(r), hw) = FitLevel.Fits, 0, 1)).
                  ThenBy(Function(r) If(r.Uncensored, 1, 0)).FirstOrDefault()
    End Function

    ''' <summary>
    ''' Video from reference pictures (characters, places, an attached or earlier picture) and reference voices:
    ''' MiniMax H3 Ref2VA sees them as &lt;Picture n&gt; and &lt;Audio n&gt;.
    ''' </summary>
    Private Async Function MakeReferenceVideoAsync(r As MediaRecipe, prompt As String, photoFile As String, cast As List(Of CastLibrary.CastMember),
                                                   audios As List(Of (Who As String, File As String)), size As MediaSize,
                                                   status As Action(Of String), ct As CancellationToken) As Task(Of String)
        Dim v = MediaStudio.UsableVariant(r)
        Dim pics As New List(Of String)()
        If photoFile IsNot Nothing Then pics.Add(photoFile)
        pics.AddRange(CastLibrary.ReferencePictures(cast, 3))
        pics = pics.Distinct().Take(Math.Max(1, If(r.Refs > 0, r.Refs, 3))).ToList()
        Dim wavs As New List(Of String)()
        Dim lines As New List(Of String)()
        For Each a In audios.Take(3)
            status?.Invoke("Preparing the voice…")
            Dim wav = Path.Combine(Path.GetTempPath(), $"localmind-voice-{Guid.NewGuid():N}.wav")
            Await VideoTools.ToRefWavAsync(a.File, wav, ct)
            wavs.Add(wav)
            lines.Add(If(String.IsNullOrWhiteSpace(a.Who), $"The speaking voice is the voice in <Audio {wavs.Count}>.",
                         $"{a.Who} speaks with the voice in <Audio {wavs.Count}>."))
        Next
        Dim text = CastLibrary.ComposePrompt(prompt, cast)
        If pics.Count > 0 Then text &= " Keep the people and places looking exactly like the reference pictures."
        If lines.Count > 0 Then text &= " " & String.Join(" ", lines)
        Dim prof = ProfileLibrary.ForChat(True)
        If prof IsNot Nothing AndAlso prof.Model <> r.Id Then prof = Nothing
        Dim composed = ProfileLibrary.Compose(prof, text, r.Negative)
        Dim fast = If(prof IsNot Nothing, prof.Fast, AppCore.Settings.MediaFast) AndAlso r.Speed IsNot Nothing
        Dim wh = size.Dims(If(r.Width > 0, r.Width, 832), If(r.Height > 0, r.Height, 480))
        Dim frames = size.FramesFor(r)
        status?.Invoke($"Making the video with {pics.Count} picture(s) and {wavs.Count} voice(s)… ({wh.W}×{wh.H}, {size.SecondsText(frames, r)})")
        Dim req As New MediaStudio.GenerateRequest With {
            .Recipe = r, .Variant = v, .Prompt = composed.Prompt, .Negative = composed.Negative, .Task = "reference",
            .InputImages = pics, .RefAudios = wavs, .Width = wh.W, .Height = wh.H, .Frames = frames,
            .Steps = If(fast, r.Speed.Steps, r.Steps), .Cfg = If(fast, r.Speed.Cfg, r.Cfg), .Fast = fast,
            .Loras = If(prof IsNot Nothing, ProfileLibrary.LorasOf(prof), New List(Of UserLora)())}
        Dim out As String
        Try
            out = Await MediaStudio.GenerateAsync(req, Sub(p, t) status?.Invoke(If(p > 0, $"{t} ({p}%)", t)), ct)
        Finally
            For Each w In wavs
                Try : File.Delete(w) : Catch : End Try
            Next
        End Try
        out = Await ToMp4Async(out, status, ct)
        Return $"[🎬 {EscapeAlt(prompt)}](https://outputs.localmind/{Uri.EscapeDataString(Path.GetFileName(out))})" & vbCrLf & vbCrLf & $"_{r.Name} · {v.Label}_"
    End Function

    ''' <summary>MP4 plays everywhere (iPhone too); keeps the original if ffmpeg is not available. The prompt file moves along.</summary>
    Private Async Function ToMp4Async(out As String, status As Action(Of String), ct As CancellationToken) As Task(Of String)
        If Not out.EndsWith(".webm", StringComparison.OrdinalIgnoreCase) Then Return out
        Try
            status?.Invoke("Converting to MP4…")
            Dim mp4 = Await VideoTools.ToMp4Async(out, status, ct)
            MediaStudio.MoveSettings(out, mp4)
            Return mp4
        Catch ex As OperationCanceledException
            Throw
        Catch
            Return out
        End Try
    End Function

    ''' <summary>Voices for a video: a saved voice named in the tool call or as @name in the message, or the attached recording.</summary>
    Public Function VoicesFor(voiceArg As String, userText As String, payload As List(Of ChatMessage)) As List(Of (Who As String, File As String))
        Dim list As New List(Of (Who As String, File As String))()
        Dim names As New List(Of String)()
        If Not String.IsNullOrWhiteSpace(voiceArg) Then names.Add(voiceArg.Trim().TrimStart("@"c))
        For Each sv In VoiceLibrary.All()
            If Regex.IsMatch(If(userText, ""), "@" & Regex.Escape(sv.Name) & "\b", RegexOptions.IgnoreCase) Then names.Add(sv.Name)
        Next
        For Each n In names.Distinct(StringComparer.OrdinalIgnoreCase)
            If n.Equals("attached", StringComparison.OrdinalIgnoreCase) Then
                Dim rec = LastRecording(payload)
                If rec IsNot Nothing Then list.Add(("", rec))
                Continue For
            End If
            Dim sv = VoiceLibrary.Find(n)
            If sv IsNot Nothing Then list.Add(("", sv.FullPath))
        Next
        ' a recording attached with "use this voice / with my voice" and no saved one named
        If list.Count = 0 AndAlso AskClone.IsMatch(If(userText, "")) Then
            Dim rec = LastRecording(payload)
            If rec IsNot Nothing Then list.Add(("", rec))
        End If
        Return list
    End Function

    Public Async Function MakeSongAsync(style As String, lyrics As String, status As Action(Of String), ct As CancellationToken) As Task(Of String)
        ' 🎵 the chat's music profile: its model, default style and the words always added
        Dim prof = ProfileLibrary.ForChatAudio("music")
        Dim m = If(prof IsNot Nothing, AudioStudio.Models().FirstOrDefault(Function(x) x.Id = prof.Model), Nothing)
        If m Is Nothing Then m = AudioStudio.Models().FirstOrDefault(Function(x) x.IsMusic AndAlso AudioStudio.IsInstalled(x))
        If m Is Nothing Then Return Localizer.T("(No music model is downloaded yet — open ⬇ Get models → Music to get one.)")
        If prof IsNot Nothing Then style = ProfileLibrary.Compose(prof, style, "").Prompt
        status?.Invoke("Composing…" & If(prof IsNot Nothing, " 🧩 " & prof.Name, ""))
        Dim wav = Await AudioStudio.GenerateAsync(New AudioStudio.AudioRequest With {.Model = m, .Text = style, .Lyrics = lyrics},
                                                  Sub(p, t) status?.Invoke(If(p > 0, $"{t} ({p}%)", t)), ct)
        MediaStudio.SaveInfo(wav, "music", m.Name, style, lyrics)
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
        ' 🗣 the chat's voice profile: its model, and its saved voice when no recording is attached
        Dim vprof = ProfileLibrary.ForChatAudio("voice")
        Dim m = AudioStudio.Models().Where(Function(x) Not x.IsMusic AndAlso x.Clone AndAlso AudioStudio.IsInstalled(x)).
                    OrderBy(Function(x) If(vprof IsNot Nothing AndAlso x.Id = vprof.Model, 0, If(x.Id = AppCore.Settings.ReadAloudModel, 1, 2))).FirstOrDefault()
        If vprof IsNot Nothing Then
            If String.IsNullOrEmpty(recording) AndAlso String.IsNullOrWhiteSpace(voiceName) AndAlso String.IsNullOrWhiteSpace(saveAs) Then voiceName = vprof.Voice
            If String.IsNullOrWhiteSpace(words) Then words = vprof.DefaultPrompt
        End If
        If m Is Nothing Then Return Localizer.T("(No voice model that can copy a voice is downloaded yet — get Qwen3-TTS, Chatterbox or VoxCPM2 in ⬇ Get models → Voice.)")
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
        MediaStudio.SaveInfo(wav, "voice", m.Name & " · " & name, words, Nothing)
        Dim note = $"_{Localizer.T("Voice")}: {name} · {m.Name}_"
        If Not String.IsNullOrWhiteSpace(saveAs) Then note &= "  " & String.Format(Localizer.T("(saved — say ""with {0}'s voice"" next time)"), name)
        Return $"[🗣 {EscapeAlt(words)}](https://outputs.localmind/{Uri.EscapeDataString(Path.GetFileName(wav))})" & vbCrLf & vbCrLf & note
    End Function
End Module
