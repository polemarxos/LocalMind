Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Text.RegularExpressions
Imports System.Threading
Imports System.Threading.Tasks

''' <summary>
''' Longer video jobs made of several model runs: storyboards (shot after shot, each starting where the last one
''' ended), extending a video, talking characters (voice → lip movement, in 5-second pieces), making videos
''' sharper and smoother, and a prompt helper. Used by the studio and by the chat.
''' </summary>
Public Module VideoMaker

    Private Function NewOutput(suffix As String) As String
        Return Path.Combine(MediaStudio.OutputFolder, $"{DateTime.Now:yyyyMMdd-HHmmss}-{suffix}")
    End Function

    ''' <summary>Wan wants 4n+1 frames: enough for the given seconds, at most <paramref name="max"/>.</summary>
    Public Function FramesFor(seconds As Double, fps As Integer, max As Integer) As Integer
        Dim n = CInt(Math.Ceiling(Math.Max(0.25, seconds) * Math.Max(1, fps)))
        Dim f = ((Math.Max(1, n - 1) + 3) \ 4) * 4 + 1
        Return Math.Max(5, Math.Min(max, f))
    End Function

    Private Function Scaled(progress As Action(Of Integer, String), part As Integer, parts As Integer, label As String) As Action(Of Integer, String)
        Return Sub(p, t) progress?.Invoke(CInt((part * 100 + Math.Max(0, Math.Min(100, p))) / Math.Max(1, parts)), label & " — " & Localizer.T(t))
    End Function

    '──────────────── cast scene (pictures with the cast, for videos that start from a picture) ────────────────
    ''' <summary>An installed picture model that can combine reference pictures (puts the cast into one scene).</summary>
    Public Function CastSceneRecipe() As MediaRecipe
        Dim hw = HardwareInfo.Current()
        Return MediaStudio.Recipes().Where(Function(x) Not x.IsVideo AndAlso x.Can("combine") AndAlso MediaStudio.InstalledVariant(x) IsNot Nothing).
                                     OrderBy(Function(x) If(MediaStudio.Fit(x, MediaStudio.InstalledVariant(x), hw) = FitLevel.Fits, 0, 1)).
                                     ThenBy(Function(x) If(x.Speed IsNot Nothing AndAlso AppCore.Settings.MediaFast, x.Speed.Steps, x.Steps)).FirstOrDefault()
    End Function

    ''' <summary>The request that makes an opening scene with the cast, or Nothing when no picture model can combine pictures.</summary>
    Public Function CastSceneRequest(prompt As String, cast As IEnumerable(Of CastLibrary.CastMember), width As Integer, height As Integer) As MediaStudio.GenerateRequest
        Dim pics = CastLibrary.ReferencePictures(cast, 3)
        Dim maker = CastSceneRecipe()
        If maker Is Nothing OrElse pics.Count = 0 Then Return Nothing
        Dim fast = AppCore.Settings.MediaFast AndAlso maker.Speed IsNot Nothing
        Return New MediaStudio.GenerateRequest With {
            .Recipe = maker, .Variant = MediaStudio.InstalledVariant(maker), .Task = "combine",
            .Prompt = CastLibrary.ComposePrompt(prompt, cast) & ", opening frame of a film scene, cinematic",
            .Width = If(width > 0, width, 832), .Height = If(height > 0, height, 480),
            .Steps = If(fast, maker.Speed.Steps, maker.Steps), .Cfg = If(fast, maker.Speed.Cfg, maker.Cfg), .Fast = fast,
            .InputImages = pics.Take(Math.Max(1, maker.Refs)).ToList()}
    End Function

    '──────────────── storyboard ────────────────
    ''' <summary>Splits the text into shots: one per line (numbers and bullets at the start are ignored).</summary>
    Public Function SplitShots(text As String) As List(Of String)
        Return If(text, "").Split({vbCr, vbLf}, StringSplitOptions.RemoveEmptyEntries).
                    Select(Function(l) Regex.Replace(l, "^\s*(?:[-*•]\s*|(?:shot|scene|πλάνο|σκηνή)?\s*\d+\s*[\.\):\-–]\s*)", "", RegexOptions.IgnoreCase).Trim()).
                    Where(Function(l) l.Length > 0).ToList()
    End Function

    ''' <summary>
    ''' Makes every shot and joins them into one MP4. Each shot starts from the last frame of the one before (when the
    ''' model animates pictures), so people and places carry on. <paramref name="reference"/> (a cast sheet for Wan VACE)
    ''' is used for every shot instead. Returns the joined video first, then the shots.
    ''' </summary>
    Public Async Function StoryboardAsync(template As MediaStudio.GenerateRequest, shots As IList(Of String), startPicture As String,
                                          reference As String, opening As MediaStudio.GenerateRequest,
                                          progress As Action(Of Integer, String), ct As CancellationToken) As Task(Of List(Of String))
        Dim r = template.Recipe
        If shots.Count = 0 Then Throw New Exception(Localizer.T("Write the shots first: one line per shot."))
        Dim stamp = $"{DateTime.Now:yyyyMMdd-HHmmss}"
        Dim clips As New List(Of String)
        Dim made As New List(Of String)
        Dim chained = reference Is Nothing AndAlso r.Can("animate")
        Dim parts = shots.Count + If(opening IsNot Nothing AndAlso startPicture Is Nothing, 1, 0)
        Dim part = 0
        Dim temps As New List(Of String)
        Try
            For i = 0 To shots.Count - 1
                ct.ThrowIfCancellationRequested()
                Dim label = Localizer.T("Shot") & $" {i + 1}/{shots.Count}"
                Dim start As String = Nothing
                If i = 0 Then
                    start = startPicture
                    If start Is Nothing AndAlso opening IsNot Nothing AndAlso reference Is Nothing Then
                        Dim o = opening.Clone()
                        o.OutputPath = Path.Combine(MediaStudio.OutputFolder, $"{stamp}-shot1-start.png")
                        Await MediaStudio.GenerateAsync(o, Scaled(progress, part, parts, Localizer.T("🎭 Opening scene with the cast")), ct)
                        part += 1
                        start = o.OutputPath
                        made.Add(start)
                    End If
                ElseIf chained Then
                    start = Await VideoTools.LastFrameAsync(clips.Last(), ct)
                    temps.Add(start)
                End If
                Dim req = template.Clone()
                req.Prompt = shots(i)
                req.OutputPath = Path.Combine(MediaStudio.OutputFolder, $"{stamp}-shot{i + 1}.webm")
                If reference IsNot Nothing Then
                    req.Task = "animate" : req.InputImages = New List(Of String) From {reference}
                ElseIf start IsNot Nothing AndAlso r.Can("animate") Then
                    req.Task = "animate" : req.InputImages = New List(Of String) From {start}
                ElseIf r.Can("create") AndAlso r.Input <> "required" Then
                    req.Task = "create" : req.InputImages = New List(Of String)()
                Else
                    Throw New Exception(Localizer.T("This model starts from a picture: choose a start picture, or tick cast members and download a picture model that combines pictures (Qwen-Image Edit, FLUX.2 klein or Qwen-Image 2.1)."))
                End If
                Await MediaStudio.GenerateAsync(req, Scaled(progress, part, parts, label), ct)
                part += 1
                clips.Add(req.OutputPath)
                made.Add(req.OutputPath)
            Next
            Dim final = NewOutput("storyboard.mp4")
            progress?.Invoke(99, Localizer.T("Joining the shots…"))
            Await VideoTools.JoinAsync(clips, final, chained, Nothing, ct)
            made.Insert(0, final)
            Return made
        Finally
            For Each f In temps
                Try : File.Delete(f) : Catch : End Try
            Next
        End Try
    End Function

    '──────────────── extend ────────────────
    ''' <summary>Continues a video: a new clip starts from its last frame and is joined after it. Returns the joined video.</summary>
    Public Async Function ExtendAsync(template As MediaStudio.GenerateRequest, video As String,
                                      progress As Action(Of Integer, String), ct As CancellationToken) As Task(Of String)
        If Not template.Recipe.Can("animate") Then Throw New Exception(Localizer.T("Choose a model that animates pictures (photo → video)."))
        Dim last = Await VideoTools.LastFrameAsync(video, ct)
        Try
            Dim req = template.Clone()
            req.Task = "animate"
            req.InputImages = New List(Of String) From {last}
            req.OutputPath = NewOutput("next.webm")
            Await MediaStudio.GenerateAsync(req, Sub(p, t) progress?.Invoke(Math.Min(97, p), Localizer.T("⏩ Making the next part") & " — " & Localizer.T(t)), ct)
            Dim final = NewOutput("extended.mp4")
            progress?.Invoke(98, Localizer.T("Joining the clips…"))
            Await VideoTools.JoinAsync({video, req.OutputPath}, final, True, Nothing, ct)
            Return final
        Finally
            Try : File.Delete(last) : Catch : End Try
        End Try
    End Function

    '──────────────── talking characters ────────────────
    ''' <summary>A downloaded voice model (one that clones voices when a saved voice is wanted).</summary>
    Public Function SpeechModel(wantClone As Boolean) As AudioModel
        Dim ok = AudioStudio.Models().Where(Function(m) Not m.IsMusic AndAlso AudioStudio.IsInstalled(m)).ToList()
        Return ok.OrderBy(Function(m) If(wantClone AndAlso Not m.Clone, 1, 0)).
                  ThenBy(Function(m) If(m.Id = AppCore.Settings.ReadAloudModel, 0, 1)).FirstOrDefault()
    End Function

    ''' <summary>Speaks the words (with a saved voice if given) into one WAV in the outputs folder.</summary>
    Public Async Function SpeakAsync(words As String, voiceName As String, progress As Action(Of Integer, String), ct As CancellationToken) As Task(Of String)
        Dim saved = If(String.IsNullOrEmpty(voiceName), Nothing, VoiceLibrary.Find(voiceName))
        Dim m = SpeechModel(saved IsNot Nothing)
        If m Is Nothing Then Throw New Exception(Localizer.T("Download a voice model first (🎵 Music & voice)."))
        Dim clean = Voice.CleanForSpeech(words)
        Dim lang = If(Voice.IsGreek(clean), "el", If(saved?.Language, "en"))
        Dim pieces = Voice.Chunks(clean)
        Dim wavs As New List(Of String)
        For i = 0 To pieces.Count - 1
            Dim k = i
            wavs.Add(Await AudioStudio.GenerateAsync(New AudioStudio.AudioRequest With {
                .Model = m, .Text = pieces(i), .Language = lang,
                .SpeakerFile = If(m.Clone, saved?.FullPath, Nothing), .SpeakerText = saved?.Words},
                Sub(p, t) progress?.Invoke(CInt((k * 100 + p) / pieces.Count), Localizer.T("🗣 Speaking the words") & " — " & Localizer.T(t)), ct))
        Next
        Dim out = NewOutput("voice.wav")
        Voice.JoinWavs(wavs, out)
        For Each w In wavs
            Try : File.Delete(w) : Catch : End Try
        Next
        Return out
    End Function

    ''' <summary>
    ''' Makes the person in the picture talk to the recording (Wan S2V): 5-second pieces, each starting from the last
    ''' frame of the one before, joined, with the recording as the sound. Returns the MP4.
    ''' </summary>
    Public Async Function TalkAsync(template As MediaStudio.GenerateRequest, picture As String, audio As String,
                                    progress As Action(Of Integer, String), ct As CancellationToken) As Task(Of String)
        Dim info = Await VideoTools.InfoAsync(audio, ct)
        Dim secs = info.Seconds
        If secs <= 0.1 Then Throw New Exception(Localizer.T("The recording is empty."))
        Const Piece = 5.0
        Dim count = CInt(Math.Ceiling(secs / Piece - 0.05))
        count = Math.Max(1, count)
        Dim fps = If(template.Recipe.Fps > 0, template.Recipe.Fps, 16)
        Dim clips As New List(Of String)
        Dim temps As New List(Of String)
        Try
            Dim start = picture
            For k = 0 To count - 1
                ct.ThrowIfCancellationRequested()
                Dim from = k * Piece
                Dim length = Math.Min(Piece, secs - from)
                If length <= 0.05 Then Exit For
                Dim wav = Path.Combine(Path.GetTempPath(), $"localmind-talk-{Guid.NewGuid():N}.wav")
                temps.Add(wav)
                Await VideoTools.CutAudioAsync(audio, from, length, wav, ct)
                If k > 0 Then
                    start = Await VideoTools.LastFrameAsync(clips.Last(), ct)
                    temps.Add(start)
                End If
                Dim req = template.Clone()
                req.Task = "talk"
                req.InputImages = New List(Of String) From {start}
                req.AudioFile = wav
                req.Frames = FramesFor(length, fps, 81)
                req.OutputPath = Path.Combine(Path.GetTempPath(), $"localmind-talk-{Guid.NewGuid():N}.webm")
                temps.Add(req.OutputPath)
                Await MediaStudio.GenerateAsync(req, Scaled(progress, k, count, Localizer.T("🗣 Talking") & $" {k + 1}/{count}"), ct)
                clips.Add(req.OutputPath)
            Next
            Dim joined = Path.Combine(Path.GetTempPath(), $"localmind-talk-{Guid.NewGuid():N}.mp4")
            temps.Add(joined)
            progress?.Invoke(99, Localizer.T("Adding the voice…"))
            Await VideoTools.JoinAsync(clips, joined, True, Nothing, ct)
            Dim final = NewOutput("talking.mp4")
            Await VideoTools.AddAudioAsync(joined, audio, False, 1.0, final, Nothing, ct)
            Return final
        Finally
            For Each f In temps
                Try : File.Delete(f) : Catch : End Try
            Next
        End Try
    End Function

    '──────────────── enhance ────────────────
    ''' <summary>An installed picture enlarger (Real-ESRGAN etc.) for sharper video frames, if any.</summary>
    Public Function InstalledUpscaler() As MediaRecipe
        Return MediaStudio.Recipes().FirstOrDefault(Function(r) r.IsUpscaler AndAlso MediaStudio.InstalledVariant(r) IsNot Nothing)
    End Function

    ''' <summary>
    ''' Twice the size (frame by frame with the AI enlarger when one is given, else a sharp plain enlargement) and / or
    ''' twice the frame rate (smoother motion). Returns the new MP4.
    ''' </summary>
    Public Async Function EnhanceAsync(video As String, upscaler As MediaRecipe, upscale As Boolean, smooth As Boolean,
                                      progress As Action(Of Integer, String), ct As CancellationToken) As Task(Of String)
        If Not upscale AndAlso Not smooth Then Throw New Exception(Localizer.T("Tick 'Sharper' or 'Smoother' (or both)."))
        Dim framesDir As String = Nothing
        Try
            Dim v = If(upscaler Is Nothing, Nothing, MediaStudio.InstalledVariant(upscaler))
            If upscale AndAlso v IsNot Nothing Then
                progress?.Invoke(0, Localizer.T("Reading the video…"))
                framesDir = Await VideoTools.AllFramesAsync(video, ct)
                Dim frames = Directory.GetFiles(framesDir, "frame_*.png").OrderBy(Function(f) f).ToList()
                For i = 0 To frames.Count - 1
                    ct.ThrowIfCancellationRequested()
                    Dim req As New MediaStudio.GenerateRequest With {
                        .Recipe = upscaler, .Variant = v, .Task = "upscale", .InputImages = New List(Of String) From {frames(i)},
                        .OutputPath = Path.Combine(framesDir, $"up_{i + 1:00000}.png")}
                    Dim k = i
                    Await MediaStudio.GenerateAsync(req, Sub(p, t) progress?.Invoke(CInt(k * 90 / frames.Count),
                                                        Localizer.T("🔍 Sharpening frame") & $" {k + 1}/{frames.Count}"), ct)
                    Try : File.Delete(frames(i)) : Catch : End Try
                Next
            End If
            Dim out = NewOutput("enhanced.mp4")
            Await VideoTools.EnhanceAsync(video, framesDir, upscale, smooth, out, Sub(t) progress?.Invoke(92, Localizer.T(t)), ct)
            Return out
        Finally
            If framesDir IsNot Nothing Then Try : Directory.Delete(framesDir, True) : Catch : End Try
        End Try
    End Function

    '──────────────── prompt helper ────────────────
    Private ReadOnly TagFamilies As String() = {"SD1.5", "SDXL"}

    ''' <summary>
    ''' A chat model rewrites the idea as a good prompt for the chosen model and task (tags for SDXL / SD 1.5,
    ''' sentences for FLUX / Qwen / Z-Image, motion and camera for video). For a storyboard it writes one shot per line.
    ''' @Names stay as they are.
    ''' </summary>
    Public Async Function ImprovePromptAsync(idea As String, r As MediaRecipe, job As String, shotCount As Integer, ct As CancellationToken) As Task(Of String)
        Dim model = AppCore.Settings.LastModel
        If AppCore.Settings.Backend = BackendKind.BuiltIn Then
            Dim all = AppCore.Library.All()
            If all.Count = 0 Then Throw New Exception(Localizer.T("This needs a chat model: download one in the Model Manager first."))
            If String.IsNullOrEmpty(model) OrElse Not all.Any(Function(m) m.DisplayName = model) Then model = all(0).DisplayName
        End If
        Dim video = r IsNot Nothing AndAlso r.IsVideo
        Dim how As String
        Select Case job
            Case "storyboard"
                how = $"Turn the idea into {Math.Max(2, shotCount)} consecutive shots of one short film. Write exactly one shot per line: no numbers, no titles, no empty lines. " &
                      "Each line is a complete video prompt of 2-3 sentences: the action and how it moves, the camera movement, the setting, the lighting. " &
                      "Repeat the key look of each character and place in every line so they stay the same from shot to shot."
            Case "talk"
                how = "Describe the person talking: their expression, small head and hand gestures, the setting and the framing (for example a medium close-up). One or two sentences."
            Case "first-last"
                how = "Describe how the scene moves from the first picture to the last one: the motion, the camera movement and the mood. Two or three sentences."
            Case Else
                If video Then
                    how = "Write 2-4 sentences for a video model: who or what, the action and how it moves over time, the camera movement (for example slow dolly-in, pan, tracking shot), the setting, lighting, mood and style. Describe one continuous shot."
                ElseIf r IsNot Nothing AndAlso TagFamilies.Contains(r.Family) Then
                    how = "Write it as comma-separated tags and short phrases: subject, appearance, action, setting, composition, lighting, style, quality words (for example masterpiece, best quality, highly detailed)."
                ElseIf job = "edit" OrElse job = "inpaint" Then
                    how = "Write one clear instruction for a picture-editing model: what to change and what to keep the same."
                Else
                    how = "Write one detailed paragraph (2-4 sentences): subject, appearance, action, setting, composition, lighting, colors and style; for photos the camera and lens. Put any text that must appear in the picture in quotes."
                End If
        End Select
        Dim sys = "You improve prompts for a local AI " & If(video, "video", "picture") & " generator" & If(r IsNot Nothing, $" ({r.Name})", "") & ". " &
                  "Keep the user's idea and subject. Keep every word that starts with @ exactly as written: they are saved characters and places. " &
                  "Write in English even if the idea is in another language. " & how & " Reply with the result only: no explanations, no quotes, no headings."
        Dim msgs As New List(Of ChatMessage) From {
            New ChatMessage With {.Role = "system", .Content = sys},
            New ChatMessage With {.Role = "user", .Content = If(String.IsNullOrWhiteSpace(idea), "Surprise me with a beautiful idea.", idea.Trim())}}
        Dim opts As New ChatRequestOptions With {.Model = model, .Temperature = 0.7, .MaxTokens = If(job = "storyboard", 900, 350), .EnableThinking = False}
        Dim backend = AppCore.Backend
        Dim res = Await Task.Run(Function() backend.StreamChatAsync(msgs, opts, Sub(t) Return, Sub(t) Return, ct))
        Dim text = If(res?.Content, "")
        text = Regex.Replace(text, "<think>.*?</think>", "", RegexOptions.Singleline).Trim()
        text = Regex.Replace(text, "^(?:improved\s+)?prompt\s*:\s*", "", RegexOptions.IgnoreCase).Trim().Trim(""""c)
        If job = "storyboard" Then text = String.Join(Environment.NewLine, SplitShots(text))
        Return text
    End Function
End Module
