<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class MainForm
    Inherits System.Windows.Forms.Form

    <System.Diagnostics.DebuggerNonUserCode()>
    Protected Overrides Sub Dispose(disposing As Boolean)
        Try
            If disposing AndAlso components IsNot Nothing Then
                components.Dispose()
            End If
        Finally
            MyBase.Dispose(disposing)
        End Try
    End Sub

    Private components As System.ComponentModel.IContainer

    <System.Diagnostics.DebuggerStepThrough()>
    Private Sub InitializeComponent()
        Me.components = New System.ComponentModel.Container()

        Me.menuMain = New System.Windows.Forms.MenuStrip()
        Me.menuFile = New System.Windows.Forms.ToolStripMenuItem()
        Me.miNewChat = New System.Windows.Forms.ToolStripMenuItem()
        Me.miSave = New System.Windows.Forms.ToolStripMenuItem()
        Me.miExport = New System.Windows.Forms.ToolStripMenuItem()
        Me.miImport = New System.Windows.Forms.ToolStripMenuItem()
        Me.miSepFile = New System.Windows.Forms.ToolStripSeparator()
        Me.miExit = New System.Windows.Forms.ToolStripMenuItem()
        Me.menuHelp = New System.Windows.Forms.ToolStripMenuItem()
        Me.miAbout = New System.Windows.Forms.ToolStripMenuItem()
        Me.miTips = New System.Windows.Forms.ToolStripMenuItem()
        Me.menuModels = New System.Windows.Forms.ToolStripMenuItem()
        Me.miModelManager = New System.Windows.Forms.ToolStripMenuItem()
        Me.miOpenModels = New System.Windows.Forms.ToolStripMenuItem()
        Me.miUnload = New System.Windows.Forms.ToolStripMenuItem()
        Me.menuTools = New System.Windows.Forms.ToolStripMenuItem()
        Me.miSettings = New System.Windows.Forms.ToolStripMenuItem()
        Me.miLan = New System.Windows.Forms.ToolStripMenuItem()
        Me.miEngineLog = New System.Windows.Forms.ToolStripMenuItem()
        Me.btnGetModels = New System.Windows.Forms.ToolStripButton()
        Me.btnLan = New System.Windows.Forms.ToolStripButton()
        Me.lblDownloads = New System.Windows.Forms.ToolStripStatusLabel()

        Me.toolTop = New System.Windows.Forms.ToolStrip()
        Me.lblModelTag = New System.Windows.Forms.ToolStripLabel()
        Me.cmbModels = New System.Windows.Forms.ToolStripComboBox()
        Me.sepTop1 = New System.Windows.Forms.ToolStripSeparator()
        Me.lblPersonaTag = New System.Windows.Forms.ToolStripLabel()
        Me.cmbPersonas = New System.Windows.Forms.ToolStripComboBox()
        Me.btnEditPersona = New System.Windows.Forms.ToolStripButton()
        Me.sepTop2 = New System.Windows.Forms.ToolStripSeparator()
        Me.btnAttachDoc = New System.Windows.Forms.ToolStripButton()
        Me.btnAttachImage = New System.Windows.Forms.ToolStripButton()
        Me.btnAttachAny = New System.Windows.Forms.ToolStripButton()
        Me.btnThinking = New System.Windows.Forms.ToolStripButton()
        Me.sepTop3 = New System.Windows.Forms.ToolStripSeparator()
        Me.lblConnection = New System.Windows.Forms.ToolStripLabel()

        Me.statusStrip = New System.Windows.Forms.StatusStrip()
        Me.lblStatus = New System.Windows.Forms.ToolStripStatusLabel()
        Me.lblTokens = New System.Windows.Forms.ToolStripStatusLabel()

        Me.splitMain = New System.Windows.Forms.SplitContainer()
        Me.btnNewChat = New System.Windows.Forms.Button()
        Me.txtSearch = New System.Windows.Forms.TextBox()
        Me.lstChats = New System.Windows.Forms.ListBox()

        Me.splitContent = New System.Windows.Forms.SplitContainer()
        Me.webChat = New Microsoft.Web.WebView2.WinForms.WebView2()
        Me.pnlInput = New System.Windows.Forms.Panel()
        Me.inputToolbar = New System.Windows.Forms.FlowLayoutPanel()
        Me.btnSend = New System.Windows.Forms.Button()
        Me.btnStop = New System.Windows.Forms.Button()
        Me.btnRegenerate = New System.Windows.Forms.Button()
        Me.btnClear = New System.Windows.Forms.Button()
        Me.flpAttachments = New System.Windows.Forms.FlowLayoutPanel()
        Me.txtInput = New System.Windows.Forms.RichTextBox()

        CType(Me.splitMain, System.ComponentModel.ISupportInitialize).BeginInit()
        Me.splitMain.Panel1.SuspendLayout()
        Me.splitMain.Panel2.SuspendLayout()
        Me.splitMain.SuspendLayout()
        CType(Me.splitContent, System.ComponentModel.ISupportInitialize).BeginInit()
        Me.splitContent.Panel1.SuspendLayout()
        Me.splitContent.Panel2.SuspendLayout()
        Me.splitContent.SuspendLayout()
        CType(Me.webChat, System.ComponentModel.ISupportInitialize).BeginInit()
        Me.pnlInput.SuspendLayout()
        Me.inputToolbar.SuspendLayout()
        Me.SuspendLayout()

        '
        ' menuMain
        '
        Me.menuMain.BackColor = System.Drawing.Color.FromArgb(37, 37, 38)
        Me.menuMain.ForeColor = System.Drawing.Color.FromArgb(212, 212, 212)
        Me.menuMain.Items.AddRange(New System.Windows.Forms.ToolStripItem() {Me.menuFile, Me.menuModels, Me.menuTools, Me.menuHelp})
        Me.menuMain.Location = New System.Drawing.Point(0, 0)
        Me.menuMain.Name = "menuMain"
        Me.menuMain.Size = New System.Drawing.Size(1280, 24)
        Me.menuMain.TabIndex = 0
        '
        ' menuFile
        '
        Me.menuFile.DropDownItems.AddRange(New System.Windows.Forms.ToolStripItem() {
            Me.miNewChat, Me.miSave, Me.miExport, Me.miImport,
            Me.miSepFile, Me.miExit})
        Me.menuFile.Text = "&File"
        Me.miNewChat.Text = "New Chat"
        Me.miSave.Text = "Save Chat"
        Me.miExport.Text = "Export Chat…"
        Me.miImport.Text = "Import Chat…"
        Me.miExit.Text = "Exit"
        '
        ' menuHelp
        '
        Me.menuHelp.DropDownItems.AddRange(New System.Windows.Forms.ToolStripItem() {Me.miTips, Me.miAbout})
        Me.menuHelp.Text = "&Help"
        Me.miTips.Text = "Tips && shortcuts"
        Me.miAbout.Text = "About"
        '
        ' menuModels
        '
        Me.menuModels.DropDownItems.AddRange(New System.Windows.Forms.ToolStripItem() {Me.miModelManager, Me.miOpenModels, Me.miUnload})
        Me.menuModels.Text = "&Models"
        Me.miModelManager.Text = "Model Manager…"
        Me.miModelManager.ShortcutKeys = System.Windows.Forms.Keys.Control Or System.Windows.Forms.Keys.M
        Me.miOpenModels.Text = "Open models folder"
        Me.miUnload.Text = "Unload model (free VRAM)"
        '
        ' menuTools
        '
        Me.menuTools.DropDownItems.AddRange(New System.Windows.Forms.ToolStripItem() {Me.miSettings, Me.miLan, Me.miEngineLog})
        Me.menuTools.Text = "&Tools"
        Me.miSettings.Text = "Settings…"
        Me.miSettings.ShortcutKeys = System.Windows.Forms.Keys.Control Or System.Windows.Forms.Keys.Oemcomma
        Me.miSettings.ShortcutKeyDisplayString = "Ctrl+,"
        Me.miLan.Text = "Share on network (phones, other PCs)…"
        Me.miEngineLog.Text = "Engine log"
        '
        ' toolTop
        '
        Me.toolTop.BackColor = System.Drawing.Color.FromArgb(45, 45, 48)
        Me.toolTop.ForeColor = System.Drawing.Color.FromArgb(212, 212, 212)
        Me.toolTop.GripStyle = System.Windows.Forms.ToolStripGripStyle.Hidden
        Me.toolTop.Items.AddRange(New System.Windows.Forms.ToolStripItem() {
            Me.lblModelTag, Me.cmbModels, Me.btnGetModels,
            Me.sepTop1,
            Me.lblPersonaTag, Me.cmbPersonas, Me.btnEditPersona,
            Me.sepTop2,
            Me.btnAttachDoc, Me.btnAttachImage, Me.btnAttachAny, Me.btnThinking,
            Me.sepTop3, Me.lblConnection, Me.btnLan})
        Me.toolTop.Location = New System.Drawing.Point(0, 24)
        Me.toolTop.Name = "toolTop"
        Me.toolTop.Size = New System.Drawing.Size(1280, 27)
        Me.toolTop.Padding = New System.Windows.Forms.Padding(6, 2, 6, 2)
        '
        Me.lblModelTag.Text = "Model:"
        Me.cmbModels.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList
        Me.cmbModels.Size = New System.Drawing.Size(280, 23)
        Me.cmbModels.DropDownWidth = 420
        Me.btnGetModels.Text = "⬇ Get models"
        Me.btnGetModels.ToolTipText = "Browse and download models that fit your GPU (Ctrl+M)"
        Me.btnLan.Text = "📡 Share"
        Me.btnLan.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right
        Me.btnLan.ToolTipText = "Use this AI from your phone or other PCs on the network"
        Me.lblDownloads.Text = ""
        Me.lblDownloads.Visible = False
        Me.lblPersonaTag.Text = "Persona:"
        Me.cmbPersonas.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList
        Me.cmbPersonas.Size = New System.Drawing.Size(170, 23)
        Me.btnEditPersona.Text = "Edit"
        Me.btnEditPersona.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text
        Me.btnEditPersona.ToolTipText = "Create, edit, or delete personas"
        Me.btnAttachDoc.Text = "📄 Doc"
        Me.btnAttachImage.Text = "🖼 Image"
        Me.btnAttachAny.Text = "📎 Any"
        Me.btnThinking.Text = "💭 Think"
        Me.btnThinking.CheckOnClick = True
        Me.btnThinking.Checked = False
        Me.btnThinking.ToolTipText = "Ask thinking-capable models to show their reasoning"
        Me.lblConnection.Text = "● Checking…"
        Me.lblConnection.ForeColor = System.Drawing.Color.Gray
        '
        ' statusStrip
        '
        Me.statusStrip.BackColor = System.Drawing.Color.FromArgb(0, 122, 204)
        Me.statusStrip.ForeColor = System.Drawing.Color.White
        Me.statusStrip.Items.AddRange(New System.Windows.Forms.ToolStripItem() {Me.lblStatus, Me.lblDownloads, Me.lblTokens})
        Me.statusStrip.Location = New System.Drawing.Point(0, 739)
        Me.statusStrip.Name = "statusStrip"
        Me.statusStrip.Size = New System.Drawing.Size(1280, 22)
        Me.lblStatus.Spring = True
        Me.lblStatus.TextAlign = System.Drawing.ContentAlignment.MiddleLeft
        Me.lblStatus.Text = "Ready"
        Me.lblTokens.Text = "— tok/s"
        '
        ' splitMain
        '
        Me.splitMain.Dock = System.Windows.Forms.DockStyle.Fill
        Me.splitMain.FixedPanel = System.Windows.Forms.FixedPanel.Panel1
        Me.splitMain.Location = New System.Drawing.Point(0, 51)
        Me.splitMain.Name = "splitMain"
        Me.splitMain.Panel1.Controls.Add(Me.lstChats)
        Me.splitMain.Panel1.Controls.Add(Me.txtSearch)
        Me.splitMain.Panel1.Controls.Add(Me.btnNewChat)
        Me.splitMain.Panel1MinSize = 180
        Me.splitMain.Panel2.Controls.Add(Me.splitContent)
        Me.splitMain.Size = New System.Drawing.Size(1280, 688)
        Me.splitMain.SplitterDistance = 250
        Me.splitMain.TabIndex = 3
        '
        ' btnNewChat
        '
        Me.btnNewChat.Dock = System.Windows.Forms.DockStyle.Top
        Me.btnNewChat.FlatStyle = System.Windows.Forms.FlatStyle.Flat
        Me.btnNewChat.BackColor = System.Drawing.Color.FromArgb(0, 122, 204)
        Me.btnNewChat.ForeColor = System.Drawing.Color.White
        Me.btnNewChat.FlatAppearance.BorderSize = 0
        Me.btnNewChat.Height = 36
        Me.btnNewChat.Text = "＋  New Chat"
        Me.btnNewChat.Name = "btnNewChat"
        '
        ' txtSearch
        '
        Me.txtSearch.Dock = System.Windows.Forms.DockStyle.Top
        Me.txtSearch.BackColor = System.Drawing.Color.FromArgb(45, 45, 48)
        Me.txtSearch.ForeColor = System.Drawing.Color.FromArgb(200, 200, 200)
        Me.txtSearch.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle
        Me.txtSearch.Font = New System.Drawing.Font("Segoe UI", 9.5!)
        Me.txtSearch.Name = "txtSearch"
        '
        ' lstChats
        '
        Me.lstChats.Dock = System.Windows.Forms.DockStyle.Fill
        Me.lstChats.BackColor = System.Drawing.Color.FromArgb(37, 37, 38)
        Me.lstChats.ForeColor = System.Drawing.Color.FromArgb(212, 212, 212)
        Me.lstChats.BorderStyle = System.Windows.Forms.BorderStyle.None
        Me.lstChats.Font = New System.Drawing.Font("Segoe UI", 9.5!)
        Me.lstChats.IntegralHeight = False
        Me.lstChats.ItemHeight = 22
        Me.lstChats.Name = "lstChats"
        '
        ' splitContent
        '
        Me.splitContent.Dock = System.Windows.Forms.DockStyle.Fill
        Me.splitContent.Orientation = System.Windows.Forms.Orientation.Horizontal
        Me.splitContent.Location = New System.Drawing.Point(0, 0)
        Me.splitContent.Name = "splitContent"
        Me.splitContent.Panel1.Controls.Add(Me.webChat)
        Me.splitContent.Panel2.Controls.Add(Me.pnlInput)
        Me.splitContent.Panel2MinSize = 120
        Me.splitContent.Size = New System.Drawing.Size(1026, 688)
        Me.splitContent.SplitterDistance = 490
        Me.splitContent.TabIndex = 0
        '
        ' webChat
        '
        Me.webChat.Dock = System.Windows.Forms.DockStyle.Fill
        Me.webChat.Location = New System.Drawing.Point(0, 0)
        Me.webChat.Name = "webChat"
        Me.webChat.Size = New System.Drawing.Size(1026, 490)
        Me.webChat.TabIndex = 0
        Me.webChat.AllowExternalDrop = False
        '
        ' pnlInput
        '
        Me.pnlInput.BackColor = System.Drawing.Color.FromArgb(37, 37, 38)
        Me.pnlInput.Dock = System.Windows.Forms.DockStyle.Fill
        Me.pnlInput.Name = "pnlInput"
        Me.pnlInput.Padding = New System.Windows.Forms.Padding(8)
        Me.pnlInput.Controls.Add(Me.txtInput)
        Me.pnlInput.Controls.Add(Me.flpAttachments)
        Me.pnlInput.Controls.Add(Me.inputToolbar)
        '
        ' inputToolbar
        '
        Me.inputToolbar.Dock = System.Windows.Forms.DockStyle.Top
        Me.inputToolbar.FlowDirection = System.Windows.Forms.FlowDirection.LeftToRight
        Me.inputToolbar.AutoSize = False
        Me.inputToolbar.Height = 34
        Me.inputToolbar.BackColor = System.Drawing.Color.FromArgb(37, 37, 38)
        Me.inputToolbar.Name = "inputToolbar"
        Me.inputToolbar.Padding = New System.Windows.Forms.Padding(0, 2, 0, 2)
        Me.inputToolbar.Controls.AddRange(New System.Windows.Forms.Control() {
            Me.btnSend, Me.btnStop, Me.btnRegenerate, Me.btnClear})
        Me.btnSend.Text = "Send  (Ctrl+Enter)"
        Me.btnSend.Width = 130
        Me.btnSend.Height = 26
        Me.btnSend.FlatStyle = System.Windows.Forms.FlatStyle.Flat
        Me.btnSend.BackColor = System.Drawing.Color.FromArgb(0, 122, 204)
        Me.btnSend.ForeColor = System.Drawing.Color.White
        Me.btnSend.FlatAppearance.BorderSize = 0
        Me.btnStop.Text = "Stop"
        Me.btnStop.Width = 60
        Me.btnStop.Height = 26
        Me.btnStop.Enabled = False
        Me.btnStop.FlatStyle = System.Windows.Forms.FlatStyle.Flat
        Me.btnStop.BackColor = System.Drawing.Color.FromArgb(60, 60, 60)
        Me.btnStop.ForeColor = System.Drawing.Color.White
        Me.btnStop.FlatAppearance.BorderSize = 0
        Me.btnRegenerate.Text = "Regenerate"
        Me.btnRegenerate.Width = 90
        Me.btnRegenerate.Height = 26
        Me.btnRegenerate.FlatStyle = System.Windows.Forms.FlatStyle.Flat
        Me.btnRegenerate.BackColor = System.Drawing.Color.FromArgb(60, 60, 60)
        Me.btnRegenerate.ForeColor = System.Drawing.Color.White
        Me.btnRegenerate.FlatAppearance.BorderSize = 0
        Me.btnClear.Text = "Clear"
        Me.btnClear.Width = 60
        Me.btnClear.Height = 26
        Me.btnClear.FlatStyle = System.Windows.Forms.FlatStyle.Flat
        Me.btnClear.BackColor = System.Drawing.Color.FromArgb(60, 60, 60)
        Me.btnClear.ForeColor = System.Drawing.Color.White
        Me.btnClear.FlatAppearance.BorderSize = 0
        '
        ' flpAttachments
        '
        Me.flpAttachments.Dock = System.Windows.Forms.DockStyle.Top
        Me.flpAttachments.AutoSize = False
        Me.flpAttachments.Height = 0
        Me.flpAttachments.BackColor = System.Drawing.Color.FromArgb(37, 37, 38)
        Me.flpAttachments.WrapContents = False
        Me.flpAttachments.AutoScroll = True
        Me.flpAttachments.Name = "flpAttachments"
        '
        ' txtInput
        '
        Me.txtInput.Dock = System.Windows.Forms.DockStyle.Fill
        Me.txtInput.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle
        Me.txtInput.BackColor = System.Drawing.Color.FromArgb(30, 30, 30)
        Me.txtInput.ForeColor = System.Drawing.Color.FromArgb(220, 220, 220)
        Me.txtInput.Font = New System.Drawing.Font("Cascadia Code", 10.0!, System.Drawing.FontStyle.Regular)
        Me.txtInput.AcceptsTab = True
        Me.txtInput.AllowDrop = True
        Me.txtInput.DetectUrls = False
        Me.txtInput.Name = "txtInput"
        '
        ' MainForm
        '
        Me.AutoScaleDimensions = New System.Drawing.SizeF(7.0!, 15.0!)
        Me.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font
        Me.ClientSize = New System.Drawing.Size(1280, 761)
        Me.BackColor = System.Drawing.Color.FromArgb(30, 30, 30)
        Me.ForeColor = System.Drawing.Color.FromArgb(212, 212, 212)
        Me.Controls.Add(Me.splitMain)
        Me.Controls.Add(Me.toolTop)
        Me.Controls.Add(Me.menuMain)
        Me.Controls.Add(Me.statusStrip)
        Me.MainMenuStrip = Me.menuMain
        Me.MinimumSize = New System.Drawing.Size(900, 600)
        Me.Name = "MainForm"
        Me.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen
        Me.Text = "LocalMind"
        Me.AllowDrop = True

        Me.splitMain.Panel1.ResumeLayout(False)
        Me.splitMain.Panel2.ResumeLayout(False)
        CType(Me.splitMain, System.ComponentModel.ISupportInitialize).EndInit()
        Me.splitMain.ResumeLayout(False)
        Me.splitContent.Panel1.ResumeLayout(False)
        Me.splitContent.Panel2.ResumeLayout(False)
        CType(Me.splitContent, System.ComponentModel.ISupportInitialize).EndInit()
        Me.splitContent.ResumeLayout(False)
        CType(Me.webChat, System.ComponentModel.ISupportInitialize).EndInit()
        Me.pnlInput.ResumeLayout(False)
        Me.inputToolbar.ResumeLayout(False)
        Me.ResumeLayout(False)
        Me.PerformLayout()
    End Sub

    Friend WithEvents menuMain As System.Windows.Forms.MenuStrip
    Friend WithEvents menuFile As System.Windows.Forms.ToolStripMenuItem
    Friend WithEvents miNewChat As System.Windows.Forms.ToolStripMenuItem
    Friend WithEvents miSave As System.Windows.Forms.ToolStripMenuItem
    Friend WithEvents miExport As System.Windows.Forms.ToolStripMenuItem
    Friend WithEvents miImport As System.Windows.Forms.ToolStripMenuItem
    Friend WithEvents miSepFile As System.Windows.Forms.ToolStripSeparator
    Friend WithEvents miExit As System.Windows.Forms.ToolStripMenuItem
    Friend WithEvents menuHelp As System.Windows.Forms.ToolStripMenuItem
    Friend WithEvents miAbout As System.Windows.Forms.ToolStripMenuItem
    Friend WithEvents miTips As System.Windows.Forms.ToolStripMenuItem
    Friend WithEvents menuModels As System.Windows.Forms.ToolStripMenuItem
    Friend WithEvents miModelManager As System.Windows.Forms.ToolStripMenuItem
    Friend WithEvents miOpenModels As System.Windows.Forms.ToolStripMenuItem
    Friend WithEvents miUnload As System.Windows.Forms.ToolStripMenuItem
    Friend WithEvents menuTools As System.Windows.Forms.ToolStripMenuItem
    Friend WithEvents miSettings As System.Windows.Forms.ToolStripMenuItem
    Friend WithEvents miLan As System.Windows.Forms.ToolStripMenuItem
    Friend WithEvents miEngineLog As System.Windows.Forms.ToolStripMenuItem
    Friend WithEvents btnGetModels As System.Windows.Forms.ToolStripButton
    Friend WithEvents btnLan As System.Windows.Forms.ToolStripButton
    Friend WithEvents lblDownloads As System.Windows.Forms.ToolStripStatusLabel

    Friend WithEvents toolTop As System.Windows.Forms.ToolStrip
    Friend WithEvents lblModelTag As System.Windows.Forms.ToolStripLabel
    Friend WithEvents cmbModels As System.Windows.Forms.ToolStripComboBox
    Friend WithEvents sepTop1 As System.Windows.Forms.ToolStripSeparator
    Friend WithEvents lblPersonaTag As System.Windows.Forms.ToolStripLabel
    Friend WithEvents cmbPersonas As System.Windows.Forms.ToolStripComboBox
    Friend WithEvents btnEditPersona As System.Windows.Forms.ToolStripButton
    Friend WithEvents sepTop2 As System.Windows.Forms.ToolStripSeparator
    Friend WithEvents btnAttachDoc As System.Windows.Forms.ToolStripButton
    Friend WithEvents btnAttachImage As System.Windows.Forms.ToolStripButton
    Friend WithEvents btnAttachAny As System.Windows.Forms.ToolStripButton
    Friend WithEvents btnThinking As System.Windows.Forms.ToolStripButton
    Friend WithEvents sepTop3 As System.Windows.Forms.ToolStripSeparator
    Friend WithEvents lblConnection As System.Windows.Forms.ToolStripLabel

    Friend WithEvents statusStrip As System.Windows.Forms.StatusStrip
    Friend WithEvents lblStatus As System.Windows.Forms.ToolStripStatusLabel
    Friend WithEvents lblTokens As System.Windows.Forms.ToolStripStatusLabel

    Friend WithEvents splitMain As System.Windows.Forms.SplitContainer
    Friend WithEvents btnNewChat As System.Windows.Forms.Button
    Friend WithEvents txtSearch As System.Windows.Forms.TextBox
    Friend WithEvents lstChats As System.Windows.Forms.ListBox

    Friend WithEvents splitContent As System.Windows.Forms.SplitContainer
    Friend WithEvents webChat As Microsoft.Web.WebView2.WinForms.WebView2
    Friend WithEvents pnlInput As System.Windows.Forms.Panel
    Friend WithEvents inputToolbar As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents btnSend As System.Windows.Forms.Button
    Friend WithEvents btnStop As System.Windows.Forms.Button
    Friend WithEvents btnRegenerate As System.Windows.Forms.Button
    Friend WithEvents btnClear As System.Windows.Forms.Button
    Friend WithEvents flpAttachments As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents txtInput As System.Windows.Forms.RichTextBox
End Class