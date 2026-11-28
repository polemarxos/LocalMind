Imports System
Imports System.Drawing
Imports System.IO
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms

' 📚 documents, 🌐 web search, 🎤 voice input, 🔊 read aloud.
Partial Class MainForm

    Private _btnDocs As ToolStripButton
    Private _btnWeb As ToolStripButton
    Private _btnMic As Button
    Private _miAutoRead As ToolStripMenuItem

    Private Sub BuildToolsUi()
        _btnDocs = New ToolStripButton("📚 Docs") With {.CheckOnClick = True, .Checked = Settings.UseDocuments,
            .ToolTipText = "Answer from your own documents (Tools → My documents)"}
        _btnWeb = New ToolStripButton("🌐 Web") With {.CheckOnClick = True, .Checked = Settings.UseWeb,
            .ToolTipText = "Search the web for up-to-date answers"}
        Dim at = toolTop.Items.IndexOf(btnThinking) + 1
        toolTop.Items.Insert(at, _btnWeb)
        toolTop.Items.Insert(at, _btnDocs)
        AddHandler _btnDocs.CheckedChanged, Sub()
                                                Settings.UseDocuments = _btnDocs.Checked
                                                AppCore.SaveSettings()
                                                If _btnDocs.Checked AndAlso Knowledge.Stats().Docs = 0 Then OpenKnowledge()
                                            End Sub
        AddHandler _btnWeb.CheckedChanged, Sub()
                                               Settings.UseWeb = _btnWeb.Checked
                                               AppCore.SaveSettings()
                                           End Sub

        _btnMic = New Button() With {.Text = "🎤 Speak", .Width = 90, .Height = 26, .FlatStyle = FlatStyle.Flat,
                                     .BackColor = Color.FromArgb(60, 60, 60), .ForeColor = Color.White}
        _btnMic.FlatAppearance.BorderSize = 0
        inputToolbar.Controls.Add(_btnMic)
        AddHandler _btnMic.Click, AddressOf MicClicked
        Dim btnTalk As New Button() With {.Text = "🎙 Talk", .Width = 80, .Height = 26, .FlatStyle = FlatStyle.Flat,
                                          .BackColor = Color.FromArgb(60, 60, 60), .ForeColor = Color.White}
        btnTalk.FlatAppearance.BorderSize = 0
        inputToolbar.Controls.Add(btnTalk)
        AddHandler btnTalk.Click, Sub() OpenVoiceChat()
        BuildMemoryMonitor()

        Dim miDocs As New ToolStripMenuItem("My documents…")
        AddHandler miDocs.Click, Sub() OpenKnowledge()
        _miAutoRead = New ToolStripMenuItem("Read answers aloud") With {.CheckOnClick = True, .Checked = Settings.AutoReadAnswers}
        AddHandler _miAutoRead.CheckedChanged, Sub()
                                                   Settings.AutoReadAnswers = _miAutoRead.Checked
                                                   AppCore.SaveSettings()
                                                   If Not _miAutoRead.Checked Then Voice.StopSpeaking()
                                               End Sub
        Dim miStudio As New ToolStripMenuItem("Library…")
        AddHandler miStudio.Click, Sub() OpenLibrary()
        menuTools.DropDownItems.Insert(0, miStudio)
        Dim btnStudio As New ToolStripButton("🖼 Library") With {.ToolTipText = "Everything you made, with its prompt"}
        AddHandler btnStudio.Click, Sub() OpenLibrary()
        toolTop.Items.Insert(toolTop.Items.IndexOf(_btnWeb) + 1, btnStudio)
        _btnStudio = btnStudio
        _miStudio = miStudio
        BuildImageUi(btnStudio, miStudio)
        _miAudio = New ToolStripMenuItem("Characters…")
        AddHandler _miAudio.Click, Sub() OpenCharacters()
        menuTools.DropDownItems.Insert(menuTools.DropDownItems.IndexOf(miStudio) + 1, _miAudio)
        _btnAudio = New ToolStripButton("🎭 Characters") With {.ToolTipText = "People, places and voices for @Name in the chat"}
        AddHandler _btnAudio.Click, Sub() OpenCharacters()
        toolTop.Items.Insert(toolTop.Items.IndexOf(btnStudio) + 1, _btnAudio)
        ApplyFeatures()
        Dim miCompare As New ToolStripMenuItem("Compare models…")
        AddHandler miCompare.Click, Sub() OpenCompare()
        menuTools.DropDownItems.Insert(0, miCompare)
        menuTools.DropDownItems.Insert(0, New ToolStripSeparator())
        menuTools.DropDownItems.Insert(0, _miAutoRead)
        menuTools.DropDownItems.Insert(0, miDocs)
    End Sub

    Private _btnStudio, _btnAudio As ToolStripButton
    Private _miStudio, _miAudio As ToolStripMenuItem
    Private _characters As CastForm
    Private _library As LibraryForm

    ''' <summary>Shows only the abilities the user chose (Settings → Chat → Extra abilities).</summary>
    Private Sub ApplyFeatures()
        If _btnStudio Is Nothing Then Return
        Dim img = Settings.EnableImages, aud = Settings.EnableAudio
        _btnStudio.Visible = img OrElse aud
        _miStudio.Visible = img OrElse aud
        _btnImage.Visible = img
        _miImageChat.Visible = img OrElse aud
        If Not img Then _btnImage.Checked = False
        _btnAudio.Visible = img OrElse aud
        _miAudio.Visible = img OrElse aud
    End Sub

    ''' <summary>🎭 Characters: people, places and voices used as @Name in the chat.</summary>
    Private Sub OpenCharacters()
        If _characters Is Nothing OrElse _characters.IsDisposed Then
            _characters = New CastForm()
            _characters.Show(Me)
        Else
            _characters.Activate()
        End If
    End Sub

    Private _lblMem As ToolStripStatusLabel
    Private ReadOnly _memTimer As New System.Windows.Forms.Timer() With {.Interval = 3000}

    ''' <summary>Status-bar memory monitor: GPU / RAM use and what is loaded; click to free memory.</summary>
    Private Sub BuildMemoryMonitor()
        _lblMem = New ToolStripStatusLabel() With {.IsLink = False, .ToolTipText = Localizer.T("Click to free memory")}
        statusStrip.Items.Add(_lblMem)
        Dim hw = HardwareInfo.Current()
        AddHandler _memTimer.Tick, Sub()
                                       Try
                                           _lblMem.Text = MemoryMonitor.Summary(hw)
                                           If Settings.Backend = BackendKind.BuiltIn Then ShowEngineState(AppCore.Engine.State, AppCore.Engine.StateMessage)
                                       Catch
                                       End Try
                                   End Sub
        _memTimer.Start()
        Dim menu As New ContextMenuStrip()
        Dim miChat = menu.Items.Add(Localizer.T("Unload the chat model"))
        AddHandler miChat.Click, Async Sub()
                                     If AppCore.Engine.ActiveRequests = 0 Then Await AppCore.Engine.StopAsync()
                                 End Sub
        Dim miMedia = menu.Items.Add(Localizer.T("Stop picture, video and voice work"))
        AddHandler miMedia.Click, Sub()
                                      MediaStudio.Cancel()
                                      AudioStudio.Cancel()
                                      Voice.StopSpeaking()
                                  End Sub
        Dim miAll = menu.Items.Add(Localizer.T("Free everything"))
        AddHandler miAll.Click, Async Sub()
                                    MediaStudio.Cancel()
                                    AudioStudio.Cancel()
                                    Voice.StopSpeaking()
                                    If _isGenerating Then _cts?.Cancel()
                                    Await Task.Delay(300)
                                    Await AppCore.Engine.StopAsync()
                                    GC.Collect()
                                End Sub
        AddHandler _lblMem.Click, Sub() menu.Show(Cursor.Position)
    End Sub

    Private _voiceChat As VoiceChatForm
    Private _inVoiceChat As Boolean

    Private Sub OpenVoiceChat()
        If cmbModels.SelectedIndex < 0 Then
            Localizer.Box(Me, "Choose a chat model first.", "Voice conversation")
            Return
        End If
        If _voiceChat Is Nothing OrElse _voiceChat.IsDisposed Then
            _voiceChat = New VoiceChatForm(AddressOf AskByVoiceAsync)
            _voiceChat.Show(Me)
        Else
            _voiceChat.Activate()
        End If
    End Sub

    ''' <summary>Sends spoken text as a normal chat message (it appears in the chat) and returns the answer.</summary>
    Private Async Function AskByVoiceAsync(text As String) As Task(Of String)
        While _isGenerating
            Await Task.Delay(200)
        End While
        _inVoiceChat = True
        Try
            txtInput.Text = text
            Await SendCurrentInputAsync()
            Dim last = _session.Messages.LastOrDefault(Function(m) m.Role = "assistant")
            Return StripSources(If(last?.Content, ""))
        Finally
            _inVoiceChat = False
        End Try
    End Function

    ''' <summary>🖼 Library: everything made, with its prompt.</summary>
    Private Sub OpenLibrary()
        If _library Is Nothing OrElse _library.IsDisposed Then
            _library = New LibraryForm()
            AddHandler _library.ReuseRequested, Sub(prompt, file)
                                                    If Not String.IsNullOrWhiteSpace(prompt) Then txtInput.Text = prompt
                                                    If Not String.IsNullOrEmpty(file) Then AttachFiles({file})
                                                    Activate()
                                                    txtInput.Focus()
                                                    txtInput.SelectionStart = txtInput.TextLength
                                                End Sub
            _library.Show(Me)
        Else
            _library.Activate()
        End If
    End Sub

    ''' <summary>The Model Manager opened on a category (picture, video, lora…) or on "profiles".</summary>
    Private Sub OpenModelManagerAt(where As String)
        Using f As New ModelManagerForm() With {.StartAt = where}
            f.ShowDialog(Me)
        End Using
        _modelsDirty = True
    End Sub

    Private Sub OpenKnowledge()
        Using f As New KnowledgeForm()
            f.ShowDialog(Me)
        End Using
    End Sub

    Private _micBusy As Boolean

    Private Async Sub MicClicked(sender As Object, e As EventArgs)
        If _micBusy Then Return
        Try
            If Not Voice.IsRecording Then
                Voice.StartRecording()
                _btnMic.Text = "⏹ Stop"
                _btnMic.BackColor = Color.FromArgb(161, 38, 13)
                SetStatus("Listening… press Stop when you're done.")
                Return
            End If
            _micBusy = True
            _btnMic.Text = "…"
            _btnMic.BackColor = Color.FromArgb(60, 60, 60)
            Dim wav = Voice.StopRecording()
            Dim text = Await Voice.TranscribeAsync(wav, Sub(t) SetStatus(t), CancellationToken.None)
            Try
                File.Delete(wav)
            Catch
            End Try
            If String.IsNullOrWhiteSpace(text) Then
                SetStatus("Didn't catch that — try again a bit closer to the microphone.")
            Else
                txtInput.AppendText(If(txtInput.TextLength > 0, " ", "") & text)
                txtInput.Focus()
                SetStatus("Speech added — press Ctrl+Enter to send.")
            End If
        Catch ex As Exception
            SetStatus("🎤 " & ex.Message)
            Localizer.Box(Me, ex.Message, "Voice input", MessageBoxButtons.OK, MessageBoxIcon.Warning)
        Finally
            _micBusy = False
            _btnMic.Text = "🎤 Speak"
            _btnMic.BackColor = Color.FromArgb(60, 60, 60)
        End Try
    End Sub

    Private _speakProblemShown As Boolean

    Private Sub SpeakAnswer(text As String)
        Try
            If Voice.IsSpeaking Then
                Voice.StopSpeaking()
            ElseIf Not String.IsNullOrEmpty(Settings.ReadAloudModel) Then
                SetStatus("Preparing the AI voice…")
                Dim unused = Voice.SpeakAiAsync(StripSources(text), Sub(t) SetStatus(t))
            Else
                Voice.Speak(StripSources(text))
            End If
        Catch ex As Exception
            SetStatus(ex.Message)
            ' (e.g. no Greek voice installed: said once, plainly)
            If Not _speakProblemShown Then
                _speakProblemShown = True
                Localizer.Box(Me, ex.Message, "Read aloud", MessageBoxButtons.OK, MessageBoxIcon.Information)
            End If
        End Try
    End Sub

    Private Shared Function StripSources(text As String) As String
        If text Is Nothing Then Return ""
        Dim i = text.LastIndexOf(vbLf & "---" & vbLf & "**Sources**", StringComparison.Ordinal)
        Return If(i > 0, text.Substring(0, i), text)
    End Function
End Class
