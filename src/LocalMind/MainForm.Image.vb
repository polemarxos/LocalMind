Imports System
Imports System.IO
Imports System.Linq
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports Newtonsoft.Json

' 🖼 Image switch: the message becomes a picture, made by the image model, right inside the chat.
' On consumer GPUs the chat model first (optionally) polishes the prompt, then it is unloaded,
' the image model runs, and the chat model reloads on the next text message.
Partial Class MainForm

    Private _btnImage As ToolStripButton
    Private _miImageChat As ToolStripMenuItem

    Private Sub BuildImageUi(btnStudio As ToolStripButton, miStudio As ToolStripMenuItem)
        _btnImage = New ToolStripButton("🖼 Image") With {.CheckOnClick = True,
            .ToolTipText = "Turn your next messages into pictures (choose the model in Tools → Image in chat)"}
        toolTop.Items.Insert(toolTop.Items.IndexOf(btnStudio), _btnImage)
        AddHandler _btnImage.CheckedChanged, Sub()
                                                 If _btnImage.Checked AndAlso ChatImageRecipe() Is Nothing Then
                                                     _btnImage.Checked = False
                                                     If Localizer.Box(Me, "No image model is downloaded yet. Open the Image & video studio to get one?",
                                                                      "Image", MessageBoxButtons.YesNo, MessageBoxIcon.Question) = DialogResult.Yes Then OpenStudio()
                                                 End If
                                                 SetStatus(If(_btnImage.Checked, "Image mode: your message will be turned into a picture", "Chat mode"))
                                             End Sub

        Dim miImage As New ToolStripMenuItem("Image in chat")
        _miImageChat = miImage
        AddHandler miImage.DropDownOpening, Sub() FillImageMenu(miImage)
        miImage.DropDownItems.Add("-")   ' placeholder so the arrow shows
        menuTools.DropDownItems.Insert(menuTools.DropDownItems.IndexOf(miStudio) + 1, miImage)
    End Sub

    Private Sub FillImageMenu(mi As ToolStripMenuItem)
        mi.DropDownItems.Clear()
        Dim current = ChatImageRecipe()
        Dim installed = MediaStudio.Recipes().Where(Function(r) Not r.IsVideo AndAlso Not r.IsUpscaler AndAlso MediaStudio.InstalledVariant(r) IsNot Nothing).ToList()
        If installed.Count = 0 Then
            mi.DropDownItems.Add(New ToolStripMenuItem(Localizer.T("(no image model downloaded)")) With {.Enabled = False})
        End If
        For Each r In installed
            Dim rr = r
            Dim it As New ToolStripMenuItem($"{r.Name}  [{MediaStudio.InstalledVariant(r).Label}]") With {.Checked = current Is r}
            AddHandler it.Click, Sub()
                                     Settings.ChatImageModel = rr.Id
                                     AppCore.SaveSettings()
                                 End Sub
            mi.DropDownItems.Add(it)
        Next
        Dim getMore As New ToolStripMenuItem(Localizer.T("Get image models…"))
        AddHandler getMore.Click, Sub() OpenStudio()
        mi.DropDownItems.Add(getMore)
        mi.DropDownItems.Add(New ToolStripSeparator())

        Dim improve As New ToolStripMenuItem(Localizer.T("Improve my prompt with the chat model first")) With {.Checked = Settings.ImproveImagePrompt,
            .ToolTipText = Localizer.T("The chat model writes a detailed English prompt (works for Greek messages too), then it is unloaded")}
        AddHandler improve.Click, Sub()
                                      Settings.ImproveImagePrompt = Not Settings.ImproveImagePrompt
                                      AppCore.SaveSettings()
                                  End Sub
        mi.DropDownItems.Add(improve)

        Dim gpu As New ToolStripMenuItem(Localizer.T("Graphics card memory"))
        Dim labels = {"Unload the chat model only when needed (recommended)", "Always unload the chat model", "Keep the chat model loaded"}
        For i = 0 To 2
            Dim k = i
            Dim it As New ToolStripMenuItem(Localizer.T(labels(i))) With {.Checked = Settings.ImageGpuSharing = i}
            AddHandler it.Click, Sub()
                                     Settings.ImageGpuSharing = k
                                     AppCore.SaveSettings()
                                 End Sub
            gpu.DropDownItems.Add(it)
        Next
        mi.DropDownItems.Add(gpu)
    End Sub

    ''' <summary>The chosen image model if downloaded, otherwise the best downloaded image model.</summary>
    Private Function ChatImageRecipe(Optional withPhoto As Boolean = False) As MediaRecipe
        Return ChatAgent.PickImageRecipe(withPhoto)
    End Function


    Private Const PromptWriter As String =
        "You write prompts for an image generator. Rewrite the user's request as one detailed English image prompt: " &
        "subject, setting, style, lighting, composition. Keep every detail the user asked for, including any text " &
        "that must appear in the picture (keep that text exactly). Reply with the prompt only, no quotes, at most 80 words."

    Private Const EditWriter As String =
        "You translate picture-editing instructions for an image editor. Rewrite the user's request as one short, clear English " &
        "instruction describing only the change (for example 'make the sky orange at sunset'). Keep any text that must appear exactly. " &
        "Reply with the instruction only, no quotes."

    Private Async Function GenerateImageAsync(text As String, userMsg As ChatMessage) As Task
        If _isGenerating Then Return
        Dim r = ChatImageRecipe(userMsg.Images.Count > 0)
        If r Is Nothing Then
            _btnImage.Checked = False
            Return
        End If

        _isGenerating = True
        btnSend.Enabled = False
        btnStop.Enabled = True
        btnRegenerate.Enabled = False
        _cts = New CancellationTokenSource()
        Dim ct = _cts.Token
        Dim msgId = "msg-" & Guid.NewGuid().ToString("N").Substring(0, 8)
        _streamMsgId = msgId
        Await webChat.ExecuteScriptAsync($"appendAssistantStart('{msgId}')")
        Dim status = Sub(t As String)
                         If InvokeRequired Then
                             BeginInvoke(Sub() ShowImageStatus(msgId, t))
                         Else
                             ShowImageStatus(msgId, t)
                         End If
                     End Sub

        Dim content As String
        Dim prompt = text
        Dim inputFile As String = Nothing
        Dim sw = Diagnostics.Stopwatch.StartNew()
        Try
            ' 1) Let the chat model write a better prompt (it is loaded anyway for chat).
            Dim editing = userMsg.Images.Count > 0
            If Settings.ImproveImagePrompt AndAlso Not String.IsNullOrWhiteSpace(_session.Model) AndAlso text.Length > 0 Then
                status("Writing the image prompt…")
                Try
                    Dim payload As New List(Of ChatMessage) From {
                        New ChatMessage With {.Role = "system", .Content = If(editing, EditWriter, PromptWriter)},
                        New ChatMessage With {.Role = "user", .Content = text}}
                    Dim opts As New ChatRequestOptions With {.Model = _session.Model, .Temperature = 0.6, .MaxTokens = 300, .EnableThinking = False}
                    Dim backend = AppCore.Backend
                    Dim res = Await Task.Run(Function() backend.StreamChatAsync(payload, opts, Sub(t) Return, Sub(t) Return, ct))
                    Dim better = If(res?.Content, "").Trim().Trim(""""c, "'"c, ChrW(&H201C), ChrW(&H201D)).Trim()
                    If better.Length >= 8 Then prompt = better
                Catch ex As OperationCanceledException
                    Throw
                Catch
                    ' keep the user's own words
                End Try
            End If
            If prompt.Length = 0 Then prompt = "a beautiful picture"

            ' 2) An attached picture becomes the starting / reference image.
            If userMsg.Images.Count > 0 Then
                inputFile = Path.Combine(Path.GetTempPath(), $"localmind-input-{Guid.NewGuid():N}.png")
                File.WriteAllBytes(inputFile, Convert.FromBase64String(userMsg.Images(0)))
            End If

            ' 3) Free the GPU (handled inside the studio) and draw.
            Dim picture = Await ChatAgent.MakePictureAsync(prompt, inputFile, status, ct)
            content = picture & $" · {sw.Elapsed.TotalSeconds:0}s" &
                      If(prompt <> text, vbCrLf & vbCrLf & "> " & prompt.Replace(vbLf, " "), "")
        Catch oce As OperationCanceledException
            content = "_[stopped]_"
        Catch ex As Exception
            content = "⚠ Error: " & ex.Message
        Finally
            _isGenerating = False
            btnSend.Enabled = True
            btnStop.Enabled = False
            btnRegenerate.Enabled = True
            If inputFile IsNot Nothing Then Try : File.Delete(inputFile) : Catch : End Try
        End Try

        Dim msg As New ChatMessage With {.Role = "assistant", .Content = content, .Model = r.Name}
        Await webChat.ExecuteScriptAsync(
            $"finalizeAssistant('{msgId}', {JsonConvert.SerializeObject(MarkdownRenderer.ToHtml(content))}, """", {JsonConvert.SerializeObject(MetaText(msg))});")
        _session.Messages.Add(msg)
        _session.UpdatedAt = DateTime.Now
        AppCore.History.Save(_session)
        RefreshChatList()
        SetStatus("Done")
    End Function

    Private Sub ShowImageStatus(msgId As String, t As String)
        SetStatus(t)
        webChat.ExecuteScriptAsync($"setStatusText('{msgId}', {JsonConvert.SerializeObject(Localizer.T(t))});")
    End Sub

End Class
