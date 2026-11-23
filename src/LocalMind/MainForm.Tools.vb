Imports System
Imports System.Drawing
Imports System.IO
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

        Dim miDocs As New ToolStripMenuItem("My documents…")
        AddHandler miDocs.Click, Sub() OpenKnowledge()
        _miAutoRead = New ToolStripMenuItem("Read answers aloud") With {.CheckOnClick = True, .Checked = Settings.AutoReadAnswers}
        AddHandler _miAutoRead.CheckedChanged, Sub()
                                                   Settings.AutoReadAnswers = _miAutoRead.Checked
                                                   AppCore.SaveSettings()
                                                   If Not _miAutoRead.Checked Then Voice.StopSpeaking()
                                               End Sub
        Dim miStudio As New ToolStripMenuItem("Image & video studio…")
        AddHandler miStudio.Click, Sub() OpenStudio()
        menuTools.DropDownItems.Insert(0, miStudio)
        Dim btnStudio As New ToolStripButton("🎨 Create") With {.ToolTipText = "Create images and videos (Image & video studio)"}
        AddHandler btnStudio.Click, Sub() OpenStudio()
        toolTop.Items.Insert(toolTop.Items.IndexOf(_btnWeb) + 1, btnStudio)
        BuildImageUi(btnStudio, miStudio)
        Dim miCompare As New ToolStripMenuItem("Compare models…")
        AddHandler miCompare.Click, Sub() OpenCompare()
        menuTools.DropDownItems.Insert(0, miCompare)
        menuTools.DropDownItems.Insert(0, New ToolStripSeparator())
        menuTools.DropDownItems.Insert(0, _miAutoRead)
        menuTools.DropDownItems.Insert(0, miDocs)
    End Sub

    Private _studio As StudioForm

    Private Sub OpenStudio()
        If _studio Is Nothing OrElse _studio.IsDisposed Then
            _studio = New StudioForm()
            _studio.Show(Me)
        Else
            _studio.Activate()
        End If
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

    Private Sub SpeakAnswer(text As String)
        Try
            If Voice.IsSpeaking Then
                Voice.StopSpeaking()
            Else
                Voice.Speak(StripSources(text))
            End If
        Catch ex As Exception
            SetStatus(ex.Message)
        End Try
    End Sub

    Private Shared Function StripSources(text As String) As String
        If text Is Nothing Then Return ""
        Dim i = text.LastIndexOf(vbLf & "---" & vbLf & "**Sources**", StringComparison.Ordinal)
        Return If(i > 0, text.Substring(0, i), text)
    End Function
End Class
