Imports System
Imports System.Drawing
Imports System.Linq
Imports System.Windows.Forms

' The main window's look: a clean top bar (model, persona, the big features),
' a sidebar with chats, and a message "card" that holds every option for the next message.
Partial Class MainForm

    Private _composer As ToolStrip
    Private _attachMenu As ToolStripDropDownButton
    Private _inputCard As RoundedPanel
    Private _inputHint As Label
    Private _btnTalk As Button
    Private ReadOnly _tips As New ToolTip() With {.InitialDelay = 400, .ReshowDelay = 100}

    ''' <summary>Text for a toolbar item ("&" would otherwise underline the next letter).</summary>
    Private Shared Function ItemText(english As String) As String
        Return Localizer.T(english).Replace("&", "&&")
    End Function

    Private Sub ApplyModernLayout()
        SuspendLayout()
        BackColor = Theme.CBack
        Font = Theme.UiFont

        ' ── menu ──
        menuMain.BackColor = Theme.CBack
        menuMain.ForeColor = Theme.CText
        menuMain.Padding = New Padding(8, 3, 0, 3)

        ' ── top bar: what the AI is (model, persona) on the left, the big features on the right ──
        toolTop.Tag = "bar"
        toolTop.AutoSize = False
        toolTop.Height = 46
        toolTop.Padding = New Padding(10, 0, 10, 0)
        toolTop.BackColor = Theme.CPanel
        toolTop.Font = Theme.UiFont
        toolTop.ShowItemToolTips = True

        lblModelTag.Text = Localizer.T("Model")
        lblModelTag.ForeColor = Theme.CDim
        StyleStripCombo(cmbModels, 330)
        cmbModels.ToolTipText = Localizer.T("The AI model that answers. Models marked 👁 can also read pictures.")
        btnGetModels.Text = ItemText("⬇ Get models")
        btnGetModels.Tag = Theme.PrimaryTag
        btnGetModels.Margin = New Padding(6, 0, 4, 0)
        lblPersonaTag.Text = Localizer.T("Persona")
        lblPersonaTag.ForeColor = Theme.CDim
        StyleStripCombo(cmbPersonas, 200)
        cmbPersonas.ToolTipText = Localizer.T("How the AI behaves: its role, tone and instructions")
        btnEditPersona.Text = "✎"
        btnEditPersona.ToolTipText = Localizer.T("Create, edit or delete personas")

        btnLan.Text = LanButtonText()
        btnLan.ToolTipText = Localizer.T("Use this AI from your phone or other PCs on your network")
        If _btnStudio IsNot Nothing Then
            _btnStudio.Text = ItemText("🎨 Pictures & video")
            _btnStudio.ToolTipText = Localizer.T("Create and edit pictures and videos")
        End If
        If _btnAudio IsNot Nothing Then
            _btnAudio.Text = ItemText("🎵 Music & voice")
            _btnAudio.ToolTipText = Localizer.T("Make songs, speech and cloned voices")
        End If

        ' rebuild the bar in a fixed order
        Dim right As New List(Of ToolStripItem) From {btnLan}
        If _btnAudio IsNot Nothing Then right.Add(_btnAudio)
        If _btnStudio IsNot Nothing Then right.Add(_btnStudio)
        Dim sepRight As New ToolStripSeparator() With {.Alignment = ToolStripItemAlignment.Right}
        right.Add(sepRight)
        right.Add(lblConnection)
        toolTop.Items.Clear()
        toolTop.Items.AddRange({lblModelTag, cmbModels, btnGetModels, sepTop1, lblPersonaTag, cmbPersonas, btnEditPersona})
        For Each it In right
            it.Alignment = ToolStripItemAlignment.Right
            it.Margin = New Padding(2, 0, 2, 0)
            toolTop.Items.Add(it)
        Next
        lblConnection.Margin = New Padding(4, 0, 6, 0)
        Theme.DarkCombo(cmbModels.ComboBox)
        Theme.DarkCombo(cmbPersonas.ComboBox)
        AddHandler toolTop.Resize, Sub() FitTopBar()
        FitTopBar()
        FitToScreen()

        ' ── sidebar ──
        BuildSidebar()

        ' ── message card ──
        BuildComposer()

        ' ── status bar ──
        statusStrip.BackColor = Theme.CBack
        statusStrip.ForeColor = Theme.CDim
        statusStrip.SizingGrip = False
        statusStrip.Padding = New Padding(10, 2, 10, 2)
        For Each it As ToolStripItem In statusStrip.Items
            it.ForeColor = Theme.CDim
        Next

        ' ── splitters: a thin line next to the sidebar, the chat flows into the message card ──
        splitMain.BackColor = Theme.CBorder
        splitMain.SplitterWidth = 1
        splitMain.SplitterDistance = 260
        splitContent.BackColor = Theme.CBack
        splitContent.Panel1.BackColor = Theme.CBack
        splitContent.Panel2.BackColor = Theme.CBack
        splitContent.SplitterWidth = 6
        splitContent.Panel2MinSize = 130
        Try
            splitContent.SplitterDistance = Math.Max(200, splitContent.Height - 165)
        Catch
        End Try
        ResumeLayout(True)
    End Sub

    Private _compactBar As Boolean? = Nothing

    ''' <summary>Narrow windows: smaller boxes and short button names so nothing disappears.</summary>
    Private Sub FitTopBar()
        Dim w = toolTop.Width
        Dim compact = w < 1240
        cmbModels.Width = CInt(Math.Max(170, Math.Min(360, w * If(compact, 0.21, 0.24))))
        cmbPersonas.Width = CInt(Math.Max(120, Math.Min(220, w * If(compact, 0.13, 0.14))))
        If _compactBar.HasValue AndAlso _compactBar.Value = compact Then Return
        _compactBar = compact
        If _btnStudio IsNot Nothing Then _btnStudio.Text = ItemText(If(compact, "🎨 Create", "🎨 Pictures & video"))
        If _btnAudio IsNot Nothing Then _btnAudio.Text = ItemText(If(compact, "🎵 Music", "🎵 Music & voice"))
        btnLan.Text = LanButtonText()
    End Sub

    Private Function LanButtonText() As String
        If AppCore.Lan IsNot Nothing AndAlso AppCore.Lan.IsRunning Then
            Return String.Format(ItemText("📱 On phone ({0} connected)"), AppCore.Lan.ActiveClients)
        End If
        Return ItemText(If(_compactBar.GetValueOrDefault(), "📱 Phone", "📱 Use on phone"))
    End Function

    ''' <summary>Never open bigger than the screen (small laptops).</summary>
    Private Sub FitToScreen()
        Dim wa = Screen.FromControl(Me).WorkingArea
        If Width > wa.Width OrElse Height > wa.Height Then
            Size = New Size(Math.Min(Width, wa.Width), Math.Min(Height, wa.Height))
            Location = New Point(wa.X + (wa.Width - Width) \ 2, wa.Y + (wa.Height - Height) \ 2)
        End If
    End Sub

    Private Sub StyleStripCombo(c As ToolStripComboBox, width As Integer)
        c.AutoSize = False
        c.Width = width
        c.FlatStyle = FlatStyle.Flat
        c.BackColor = Theme.CRaised
        c.ForeColor = Theme.CText
        c.Font = Theme.UiFont
        c.Margin = New Padding(6, 0, 2, 0)
    End Sub

    '──────────────── sidebar ────────────────
    Private Sub BuildSidebar()
        Dim side = splitMain.Panel1
        side.SuspendLayout()
        side.BackColor = Theme.CPanel
        side.Padding = New Padding(10, 10, 10, 6)

        btnNewChat.Text = Localizer.T("＋  New chat")
        btnNewChat.Height = 38
        btnNewChat.Font = Theme.BoldFont
        Theme.StyleFlat(btnNewChat, primary:=True)
        _tips.SetToolTip(btnNewChat, Localizer.T("Start a new conversation (Ctrl+N)"))

        ' search box without the bright border
        Dim searchHost As New Panel() With {.Dock = DockStyle.Top, .Height = 32, .BackColor = Theme.CRaised, .Padding = New Padding(10, 8, 8, 0)}
        txtSearch.BorderStyle = BorderStyle.None
        txtSearch.BackColor = Theme.CRaised
        txtSearch.Dock = DockStyle.Fill
        searchHost.Controls.Add(txtSearch)
        AddHandler searchHost.Click, Sub() txtSearch.Focus()

        If _cmbFolder IsNot Nothing Then
            _cmbFolder.BackColor = Theme.CRaised
            _cmbFolder.ForeColor = Theme.CText
            _cmbFolder.FlatStyle = FlatStyle.Flat
            Theme.DarkCombo(_cmbFolder)
            _tips.SetToolTip(_cmbFolder, Localizer.T("Show the chats of one folder"))
        End If

        Dim header As New Label() With {.Dock = DockStyle.Top, .Height = 26, .Text = Localizer.T("RECENT CHATS"),
                                        .ForeColor = Theme.CDim, .Font = Theme.SmallFont, .TextAlign = ContentAlignment.BottomLeft,
                                        .Padding = New Padding(2, 0, 0, 3)}

        lstChats.BackColor = Theme.CPanel
        lstChats.ForeColor = Theme.CText
        lstChats.DrawMode = DrawMode.OwnerDrawFixed
        lstChats.ItemHeight = 34
        AddHandler lstChats.DrawItem, AddressOf DrawChatItem

        ' Docked controls are laid out from the last added: add the list (fill) first, the button last.
        side.Controls.Clear()
        side.Controls.Add(lstChats)
        side.Controls.Add(header)
        If _cmbFolder IsNot Nothing Then
            side.Controls.Add(Spacer(6))
            side.Controls.Add(_cmbFolder)
        End If
        side.Controls.Add(Spacer(6))
        side.Controls.Add(searchHost)
        side.Controls.Add(Spacer(10))
        side.Controls.Add(btnNewChat)
        side.ResumeLayout(True)
    End Sub

    Private Shared Function Spacer(h As Integer) As Panel
        Return New Panel() With {.Dock = DockStyle.Top, .Height = h, .BackColor = Color.Transparent}
    End Function

    Private Sub DrawChatItem(sender As Object, e As DrawItemEventArgs)
        If e.Index < 0 Then Return
        Dim selected = (e.State And DrawItemState.Selected) = DrawItemState.Selected
        Dim g = e.Graphics
        Using b As New SolidBrush(Theme.CPanel)
            g.FillRectangle(b, e.Bounds)
        End Using
        Dim r As New Rectangle(e.Bounds.X, e.Bounds.Y + 1, e.Bounds.Width - 1, e.Bounds.Height - 2)
        If selected Then
            g.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias
            Using path = Theme.Rounded(r, 6), b As New SolidBrush(Theme.CControl)
                g.FillPath(b, path)
            End Using
            g.SmoothingMode = Drawing2D.SmoothingMode.Default
            Using b As New SolidBrush(Theme.CAccent)
                g.FillRectangle(b, r.X, r.Y + 8, 3, r.Height - 16)
            End Using
        End If
        Dim text = lstChats.Items(e.Index).ToString()
        TextRenderer.DrawText(g, text, Theme.UiFont, New Rectangle(r.X + 12, r.Y, r.Width - 16, r.Height),
                              If(selected, Color.White, Theme.CText),
                              TextFormatFlags.VerticalCenter Or TextFormatFlags.EndEllipsis Or TextFormatFlags.SingleLine Or TextFormatFlags.NoPrefix)
    End Sub

    '──────────────── message card ────────────────
    Private Sub BuildComposer()
        pnlInput.SuspendLayout()
        pnlInput.BackColor = Theme.CBack
        pnlInput.Padding = New Padding(18, 4, 18, 12)
        pnlInput.Controls.Clear()

        _inputCard = New RoundedPanel() With {.Dock = DockStyle.Fill, .BackColor = Theme.CRaised, .Padding = New Padding(12, 10, 8, 6)}
        pnlInput.Controls.Add(_inputCard)

        ' bottom row: options for this message (left) and the actions (right)
        Dim bottom As New Panel() With {.Dock = DockStyle.Bottom, .Height = 38, .BackColor = Theme.CRaised}
        _composer = New ToolStrip() With {.Dock = DockStyle.Fill, .GripStyle = ToolStripGripStyle.Hidden, .BackColor = Theme.CRaised,
                                          .Padding = New Padding(0), .Font = Theme.UiFont, .ShowItemToolTips = True, .CanOverflow = True}

        _attachMenu = New ToolStripDropDownButton(ItemText("📎 Attach")) With {
            .ToolTipText = Localizer.T("Add a document, picture or file to your message (you can also paste or drag files here)")}
        Dim miDoc = _attachMenu.DropDownItems.Add(ItemText("📄 Document (PDF, Word, text)…"))
        AddHandler miDoc.Click, Sub() btnAttachDoc_Click(Nothing, EventArgs.Empty)
        Dim miPic = _attachMenu.DropDownItems.Add(ItemText("🖼 Picture…"))
        AddHandler miPic.Click, Sub() btnAttachImage_Click(Nothing, EventArgs.Empty)
        Dim miAny = _attachMenu.DropDownItems.Add(ItemText("📁 Any file…"))
        AddHandler miAny.Click, Sub() btnAttachAny_Click(Nothing, EventArgs.Empty)
        For Each it In {btnAttachDoc, btnAttachImage, btnAttachAny}
            toolTop.Items.Remove(it)
        Next

        btnThinking.Text = ItemText("💭 Think first")
        btnThinking.ToolTipText = Localizer.T("The AI thinks step by step before answering (slower, smarter). Works with thinking models.")
        Dim items As New List(Of ToolStripItem) From {_attachMenu}
        If _btnPrompts IsNot Nothing Then
            _btnPrompts.Text = ItemText("📝 Prompts")
            _btnPrompts.ToolTipText = Localizer.T("Insert one of your saved prompts")
            items.Add(_btnPrompts)
        End If
        items.Add(New ToolStripSeparator())
        items.Add(btnThinking)
        If _btnWeb IsNot Nothing Then
            _btnWeb.Text = ItemText("🌐 Web search")
            _btnWeb.ToolTipText = Localizer.T("Search the internet for up-to-date answers")
            items.Add(_btnWeb)
        End If
        If _btnDocs IsNot Nothing Then
            _btnDocs.Text = ItemText("📚 My documents")
            _btnDocs.ToolTipText = Localizer.T("Answer from your own files (Tools → My documents)")
            items.Add(_btnDocs)
        End If
        If _btnImage IsNot Nothing Then
            _btnImage.Text = ItemText("🖼 Picture mode")
            _btnImage.ToolTipText = Localizer.T("Your next messages become pictures instead of text answers")
            items.Add(_btnImage)
        End If
        For Each it In items
            it.Margin = New Padding(0, 0, 4, 0)
            _composer.Items.Add(it)       ' moves it out of the top bar
        Next

        ' actions on the right: Send (or Stop while the AI writes), talk, speak, retry, clear
        Dim actions = inputToolbar
        actions.Dock = DockStyle.Right
        actions.AutoSize = True
        actions.AutoSizeMode = AutoSizeMode.GrowAndShrink
        actions.WrapContents = False
        actions.FlowDirection = FlowDirection.RightToLeft
        actions.BackColor = Theme.CRaised
        actions.Padding = New Padding(0, 3, 0, 0)
        _btnTalk = actions.Controls.OfType(Of Button)().FirstOrDefault(Function(b) b IsNot btnSend AndAlso b IsNot btnStop AndAlso b IsNot btnRegenerate AndAlso
                                                                                b IsNot btnClear AndAlso b IsNot _btnMic)
        StyleAction(btnSend, ItemTextPlain("Send  ➤"), 96, Localizer.T("Send the message (Ctrl+Enter)"), primary:=True)
        StyleAction(btnStop, ItemTextPlain("■  Stop"), 90, Localizer.T("Stop the answer (Esc)"), danger:=True)
        If _btnTalk IsNot Nothing Then StyleAction(_btnTalk, ItemTextPlain("🎙 Talk"), 78, Localizer.T("Talk with the AI hands-free: it answers out loud and listens again"))
        If _btnMic IsNot Nothing Then StyleAction(_btnMic, "🎤", 38, Localizer.T("Speak your message instead of typing (press again to stop)"))
        StyleAction(btnRegenerate, "↻", 38, Localizer.T("Write the last answer again"))
        StyleAction(btnClear, "🗑", 38, Localizer.T("Clear this conversation"))
        Dim order As New List(Of Control) From {btnSend, btnStop}
        If _btnTalk IsNot Nothing Then order.Add(_btnTalk)
        If _btnMic IsNot Nothing Then order.Add(_btnMic)
        order.Add(btnRegenerate)
        order.Add(btnClear)
        For i = 0 To order.Count - 1
            actions.Controls.SetChildIndex(order(i), i)
        Next
        ' Send and Stop share one place: Stop shows only while the AI is answering.
        btnStop.Visible = btnStop.Enabled
        AddHandler btnStop.EnabledChanged, Sub()
                                               btnStop.Visible = btnStop.Enabled
                                               btnSend.Visible = Not btnStop.Enabled
                                           End Sub

        bottom.Controls.Add(_composer)
        bottom.Controls.Add(actions)
        AddHandler bottom.Resize, Sub() FitComposer(bottom.Width)

        ' attachments (chips) above the text, the text fills the rest
        flpAttachments.BackColor = Theme.CRaised
        txtInput.BorderStyle = BorderStyle.None
        txtInput.BackColor = Theme.CRaised
        txtInput.ForeColor = Theme.CText
        txtInput.Font = Theme.InputFont
        txtInput.Dock = DockStyle.Fill
        _inputHint = New Label() With {.AutoSize = True, .Location = New Point(1, 1), .BackColor = Theme.CRaised, .ForeColor = Color.FromArgb(120, 122, 132),
                                       .Font = Theme.InputFont, .Cursor = Cursors.IBeam,
                                       .Text = Localizer.T("Message LocalMind…   (Ctrl+Enter to send · paste or drop files to attach)")}
        txtInput.Controls.Add(_inputHint)
        AddHandler _inputHint.Click, Sub() txtInput.Focus()
        AddHandler txtInput.TextChanged, Sub() _inputHint.Visible = txtInput.TextLength = 0
        AddHandler txtInput.Enter, Sub()
                                       _inputCard.Focused_ = True
                                       _inputCard.Invalidate()
                                   End Sub
        AddHandler txtInput.Leave, Sub()
                                       _inputCard.Focused_ = False
                                       _inputCard.Invalidate()
                                   End Sub

        _inputCard.Controls.Add(txtInput)
        _inputCard.Controls.Add(flpAttachments)
        _inputCard.Controls.Add(bottom)
        pnlInput.ResumeLayout(True)
    End Sub

    Private _compactComposer As Boolean? = Nothing

    ''' <summary>Narrow message card: short names for the options so none of them hides.</summary>
    Private Sub FitComposer(width As Integer)
        Dim compact = width < 1100
        If _compactComposer.HasValue AndAlso _compactComposer.Value = compact Then Return
        _compactComposer = compact
        _attachMenu.Text = ItemText(If(compact, "📎", "📎 Attach"))
        If _btnPrompts IsNot Nothing Then _btnPrompts.Text = ItemText(If(compact, "📝", "📝 Prompts"))
        btnThinking.Text = ItemText(If(compact, "💭 Think", "💭 Think first"))
        ' narrow windows: icons only (the tooltips still say what each one does)
        If _btnWeb IsNot Nothing Then _btnWeb.Text = ItemText(If(compact, "🌐", "🌐 Web search"))
        If _btnDocs IsNot Nothing Then _btnDocs.Text = ItemText(If(compact, "📚", "📚 My documents"))
        If _btnImage IsNot Nothing Then _btnImage.Text = ItemText(If(compact, "🖼", "🖼 Picture mode"))
        If _btnTalk IsNot Nothing Then
            _btnTalk.Text = If(compact, "🎙", Localizer.T("🎙 Talk"))
            _btnTalk.Width = If(compact, 38, 78)
        End If
    End Sub

    Private Shared Function ItemTextPlain(english As String) As String
        Return Localizer.T(english)
    End Function

    Private Sub StyleAction(b As Button, text As String, width As Integer, tip As String, Optional primary As Boolean = False, Optional danger As Boolean = False)
        b.Text = text
        b.AutoSize = False
        b.Width = Math.Max(width, TextRenderer.MeasureText(text, If(primary OrElse danger, Theme.BoldFont, Theme.UiFont)).Width + 26)
        b.Height = 30
        b.Margin = New Padding(4, 0, 0, 0)
        b.Font = If(primary OrElse danger, Theme.BoldFont, Theme.UiFont)
        Theme.StyleFlat(b, primary, danger)
        If Not primary AndAlso Not danger Then b.BackColor = Theme.CControl
        _tips.SetToolTip(b, tip)
    End Sub
End Class
