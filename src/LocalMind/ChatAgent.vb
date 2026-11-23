Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
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

    ''' <summary>Tools offered to the model, depending on the abilities that are switched on and installed.</summary>
    Public Function Tools(model As String, offerWebSearch As Boolean) As JArray
        Dim s = AppCore.Settings
        If Not s.AgentTools Then Return Nothing
        If NoToolModels.Contains(If(model, "")) Then Return Nothing
        Dim list As New JArray()
        If s.EnableImages AndAlso PickImageRecipe(False) IsNot Nothing Then
            list.Add(Tool("generate_image", "Create a picture with the local image model. Use it only when the user asks for a picture, drawing, photo, logo or illustration.",
                New JObject From {{"prompt", New JObject From {{"type", "string"}, {"description", "Detailed English description of the picture: subject, setting, style, lighting. Keep any text that must appear exactly."}}}},
                {"prompt"}))
        End If
        If s.EnableAudio AndAlso AudioStudio.Models().Any(Function(m) m.IsMusic AndAlso AudioStudio.IsInstalled(m)) Then
            list.Add(Tool("make_song", "Compose a song with vocals (or an instrumental) with the local music model. Use it only when the user asks for a song or music.",
                New JObject From {
                    {"style", New JObject From {{"type", "string"}, {"description", "Genre, mood, instruments, voice, tempo, in English"}}},
                    {"lyrics", New JObject From {{"type", "string"}, {"description", "Lyrics with [verse] and [chorus] tags, in the user's language; empty for instrumental"}}}},
                {"style"}))
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
            result = Await streamOnce(opts)
        End If
        If result.ToolCalls.Count = 0 Then Return (result, "", Nothing)

        Dim md As New Text.StringBuilder()
        Dim sources As List(Of SourceRef) = Nothing
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
                        md.AppendLine().AppendLine().Append(Await MakePictureAsync(If(args.Value(Of String)("prompt"), ""), Nothing, status, ct))
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
                md.AppendLine().AppendLine().Append("⚠ " & ex.Message.Split({vbCr, vbLf}, StringSplitOptions.RemoveEmptyEntries).LastOrDefault())
            End Try
        Next
        Return (result, md.ToString(), sources)
    End Function

    '──────────────── pictures and songs for the chat ────────────────
    ''' <summary>The chosen chat image model if downloaded, else the best one: with a photo one that edits or restyles, else one that creates.</summary>
    Public Function PickImageRecipe(withPhoto As Boolean) As MediaRecipe
        Dim all = MediaStudio.Recipes().Where(Function(r) Not r.IsVideo AndAlso Not r.IsUpscaler AndAlso r.Kind <> "tool" AndAlso MediaStudio.InstalledVariant(r) IsNot Nothing).
                  Where(Function(r) If(withPhoto, r.Can("edit") OrElse r.Can("restyle"), r.Can("create"))).ToList()
        Dim chosen = all.FirstOrDefault(Function(r) r.Id = AppCore.Settings.ChatImageModel)
        If chosen IsNot Nothing Then Return chosen
        Dim hw = HardwareInfo.Current()
        Return all.OrderBy(Function(r) If(withPhoto AndAlso r.Can("edit"), 0, 1)).
                   ThenBy(Function(r) If(MediaStudio.Fit(r, MediaStudio.InstalledVariant(r), hw) = FitLevel.Fits, 0, 1)).
                   ThenBy(Function(r) r.Steps).FirstOrDefault()
    End Function

    Public Function EscapeAlt(s As String) As String
        Dim t = If(s, "").Replace("[", "(").Replace("]", ")").Replace(vbCr, " ").Replace(vbLf, " ")
        Return If(t.Length > 120, t.Substring(0, 120) & "…", t)
    End Function

    ''' <summary>Makes a picture (or edits the photo) and returns Markdown that shows it.</summary>
    Public Async Function MakePictureAsync(prompt As String, photoFile As String, status As Action(Of String), ct As CancellationToken) As Task(Of String)
        Dim r = PickImageRecipe(photoFile IsNot Nothing)
        If r Is Nothing Then Return Localizer.T("(No picture model is downloaded yet — open 🎨 Create to get one.)")
        Dim v = MediaStudio.InstalledVariant(r)
        Dim fast = AppCore.Settings.MediaFast AndAlso r.Speed IsNot Nothing
        status?.Invoke(If(photoFile IsNot Nothing, "Editing the picture…", "Drawing…"))
        Dim req As New MediaStudio.GenerateRequest With {
            .Recipe = r, .Variant = v, .Prompt = If(String.IsNullOrWhiteSpace(prompt), "a beautiful picture", prompt), .Negative = r.Negative,
            .Task = MediaStudio.GuessTask(r, photoFile IsNot Nothing, False, False),
            .Width = If(r.Width > 0, r.Width, 1024), .Height = If(r.Height > 0, r.Height, 1024),
            .Steps = If(fast, r.Speed.Steps, r.Steps), .Cfg = If(fast, r.Speed.Cfg, r.Cfg), .Fast = fast, .Strength = 0.6,
            .InputImage = photoFile, .Loras = LoraLibrary.ForFamily(r.Family).Where(Function(l) l.Enabled).ToList()}
        Dim out = Await MediaStudio.GenerateAsync(req, Sub(p, t) status?.Invoke(If(p > 0, $"{t} ({p}%)", t)), ct)
        Return $"![{EscapeAlt(prompt)}](https://outputs.localmind/{Uri.EscapeDataString(Path.GetFileName(out))})" & vbCrLf & vbCrLf & $"_{r.Name} · {v.Label}_"
    End Function

    Public Async Function MakeSongAsync(style As String, lyrics As String, status As Action(Of String), ct As CancellationToken) As Task(Of String)
        Dim m = AudioStudio.Models().FirstOrDefault(Function(x) x.IsMusic AndAlso AudioStudio.IsInstalled(x))
        If m Is Nothing Then Return Localizer.T("(No music model is downloaded yet — open 🎵 Music & voice to get one.)")
        status?.Invoke("Composing…")
        Dim wav = Await AudioStudio.GenerateAsync(New AudioStudio.AudioRequest With {.Model = m, .Text = style, .Lyrics = lyrics},
                                                  Sub(p, t) status?.Invoke(If(p > 0, $"{t} ({p}%)", t)), ct)
        Return $"[🎵 {EscapeAlt(style)}](https://outputs.localmind/{Uri.EscapeDataString(Path.GetFileName(wav))})"
    End Function
End Module
