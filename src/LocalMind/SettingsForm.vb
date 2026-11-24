Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.IO
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms

Public Class SettingsForm
    Inherits Form

    Private ReadOnly _s As AppSettings
    Private _installCts As CancellationTokenSource

    ' Backend
    Private rbBuiltIn, rbOpenAi As RadioButton
    Private txtOpenAiUrl, txtOpenAiKey As TextBox
    ' Engine
    Private cmbVariant As ComboBox
    Private lblEngine As Label
    Private btnInstall As Button
    Private prgInstall As ProgressBar
    Private txtModels As TextBox
    Private numCtx, numSlots, numIdle, numMinCtx As NumericUpDown
    Private cmbTheme As ComboBox
    Private chkSplash As CheckBox
    Private chkAutoCtx, chkUpdates, chkMinTray, chkCloseTray, chkImages, chkAudio, chkEngUpd, chkAgent, chkAutoInstall As CheckBox
    Private cmbLang As ComboBox
    Private txtHotkey As TextBox
    Private cmbNgl, cmbKv, cmbSpeed As ComboBox
    Private chkMtp As CheckBox
    Private txtExtra, txtHfToken As TextBox
    Private chkVerify, chkResume As CheckBox
    ' Chat
    Private numHistory, numImages, numMaxTok As NumericUpDown
    Private numTemp, numTopP As NumericUpDown

    Public Sub New()
        _s = AppCore.Settings
        Text = "Settings"
        Size = New Size(760, 640)
        MinimumSize = New Size(700, 600)
        StartPosition = FormStartPosition.CenterParent
        Theme.ApplyForm(Me)

        Dim tabs As New TabControl() With {.Dock = DockStyle.Fill}
        Dim tBackend As New TabPage("Backend")
        Dim tEngine As New TabPage("Built-in engine")
        Dim tChat As New TabPage("Chat")
        Dim tTools As New TabPage("Web && voice")
        Dim tLook As New TabPage("Appearance")
        tabs.TabPages.AddRange({tBackend, tEngine, tChat, tTools, tLook})
        Theme.StyleTabs(tabs)

        BuildBackend(tBackend)
        BuildEngine(tEngine)
        BuildChat(tChat)
        BuildTools(tTools)
        BuildLook(tLook)

        Dim bottom As New FlowLayoutPanel() With {.Dock = DockStyle.Bottom, .Height = 46, .FlowDirection = FlowDirection.RightToLeft, .Padding = New Padding(8)}
        Dim btnOk = Theme.MakeButton("Save", primary:=True, width:=100)
        Dim btnCancel = Theme.MakeButton("Cancel", width:=100)
        AddHandler btnOk.Click, AddressOf SaveAndClose
        AddHandler btnCancel.Click, Sub() Close()
        bottom.Controls.AddRange({btnOk, btnCancel})
        AcceptButton = btnOk
        CancelButton = btnCancel

        Controls.Add(tabs)
        Controls.Add(bottom)
    End Sub

    '──────────────── layout helpers ────────────────
    Private Function Grid(page As TabPage) As TableLayoutPanel
        Dim t As New TableLayoutPanel() With {.Dock = DockStyle.Fill, .ColumnCount = 2, .AutoScroll = True, .Padding = New Padding(14)}
        t.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 210))
        t.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        page.Controls.Add(t)
        Return t
    End Function

    Private Sub Row(t As TableLayoutPanel, label As String, c As Control, Optional hint As String = Nothing)
        Dim l = Theme.MakeLabel(label)
        t.Controls.Add(l)
        If hint IsNot Nothing Then
            Dim p As New FlowLayoutPanel() With {.AutoSize = True, .WrapContents = True, .Dock = DockStyle.Fill, .Margin = New Padding(0)}
            p.Controls.Add(c)
            p.Controls.Add(Theme.MakeLabel(hint, dim_:=True))
            t.Controls.Add(p)
        Else
            t.Controls.Add(c)
        End If
        If TypeOf c Is TextBox OrElse TypeOf c Is ComboBox OrElse TypeOf c Is NumericUpDown Then Theme.StyleInput(c)
    End Sub

    Private Sub Wide(t As TableLayoutPanel, c As Control)
        t.Controls.Add(c)
        t.SetColumnSpan(c, 2)
    End Sub

    Private Shared Function Num(min As Decimal, max As Decimal, val As Decimal, Optional dec As Integer = 0, Optional inc As Decimal = 1) As NumericUpDown
        Return New NumericUpDown() With {.Minimum = min, .Maximum = max, .DecimalPlaces = dec, .Increment = inc,
                                         .Value = Math.Max(min, Math.Min(max, val)), .Width = 110}
    End Function

    '──────────────── Appearance tab ────────────────
    Private Sub BuildLook(page As TabPage)
        Dim t = Grid(page)
        cmbTheme = New ComboBox() With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 260, .MaxDropDownItems = 16}
        For i = 0 To ThemePresets.All.Count - 1
            cmbTheme.Items.Add($"{i + 1}. {ThemePresets.All(i).Name}" & If(ThemePresets.All(i).Dark, "", "  ☀"))
        Next
        cmbTheme.SelectedIndex = Math.Max(0, ThemePresets.All.FindIndex(Function(p) p.Name = ThemePresets.Find(_s.ThemeName).Name))
        Row(t, "Look", cmbTheme, "Colors and font of every window and the chat. Used after a restart.")
        Dim swatch As New Panel() With {.Width = 420, .Height = 120, .Margin = New Padding(3, 8, 3, 3)}
        AddHandler swatch.Paint, Sub(s, e)
                                     Dim p = ThemePresets.All(Math.Max(0, cmbTheme.SelectedIndex))
                                     Dim g = e.Graphics
                                     g.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias
                                     Using b As New SolidBrush(p.Panel) : g.FillRectangle(b, 0, 0, swatch.Width, swatch.Height) : End Using
                                     Using b As New SolidBrush(p.Raised) : g.FillRectangle(b, 0, 0, swatch.Width, 28) : End Using
                                     Using b As New SolidBrush(p.Back) : g.FillRectangle(b, 12, 40, 250, 66) : End Using
                                     Dim fam = ThemePresets.FontOrDefault(p.Font)
                                     Using f As New Font(fam, 9.5!), fb As New Font(fam, 9.5!, FontStyle.Bold)
                                         TextRenderer.DrawText(g, "LocalMind", fb, New Point(10, 6), p.Text)
                                         TextRenderer.DrawText(g, "Hello! How can I help?", f, New Point(22, 50), p.Text)
                                         TextRenderer.DrawText(g, "dim details · 7B · Q4_K_M", f, New Point(22, 76), p.Dimmed)
                                         Using b As New SolidBrush(p.Accent) : g.FillRectangle(b, 280, 44, 120, 28) : End Using
                                         TextRenderer.DrawText(g, "Generate", fb, New Rectangle(280, 44, 120, 28), If(ThemePresets.Lum(p.Accent) > 0.55, Color.Black, Color.White),
                                                               TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
                                         Using b As New SolidBrush(p.Control) : g.FillRectangle(b, 280, 78, 120, 28) : End Using
                                         TextRenderer.DrawText(g, "Cancel", f, New Rectangle(280, 78, 120, 28), p.Text, TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
                                     End Using
                                 End Sub
        AddHandler cmbTheme.SelectedIndexChanged, Sub() swatch.Invalidate()
        Row(t, "Preview", swatch)
        chkSplash = New CheckBox() With {.Text = "Show the loading screen at start (updates, model list)", .Checked = _s.ShowStartupScreen, .AutoSize = True}
        Row(t, "Start", chkSplash)
    End Sub

    '──────────────── Backend tab ────────────────
    Private Sub BuildBackend(page As TabPage)
        Dim t = Grid(page)
        rbBuiltIn = New RadioButton() With {.Text = "Built-in engine (llama.cpp) — runs models on this PC  ★ recommended", .AutoSize = True, .Checked = _s.Backend = BackendKind.BuiltIn}
        rbOpenAi = New RadioButton() With {.Text = "OpenAI-compatible server (LM Studio, vLLM, Jan, another PC running this app…)", .AutoSize = True, .Checked = _s.Backend = BackendKind.OpenAiCompatible}
        txtOpenAiUrl = New TextBox() With {.Text = _s.OpenAiUrl, .Width = 360}
        txtOpenAiKey = New TextBox() With {.Text = _s.OpenAiKey, .Width = 360, .UseSystemPasswordChar = True}

        Wide(t, Theme.MakeLabel("Where should the AI run?", bold:=True))
        Wide(t, rbBuiltIn)
        Wide(t, rbOpenAi)
        Row(t, "   Server URL", txtOpenAiUrl, "e.g. http://localhost:1234 (LM Studio) or http://192.168.1.20:8484 (this app on another PC)")
        Row(t, "   API key / PIN", txtOpenAiKey, "For another PC running this app, enter its LAN PIN")
        Wide(t, Theme.MakeLabel(""))
        Wide(t, Theme.MakeLabel("Tip: with LAN sharing on, a weak laptop can use the models of a strong desktop by choosing " &
                                "'OpenAI-compatible server' and entering the desktop's address and PIN.", dim_:=True))
    End Sub

    '──────────────── Engine tab ────────────────
    Private Sub BuildEngine(page As TabPage)
        Dim t = Grid(page)
        Dim hw = HardwareInfo.Current()

        Wide(t, Theme.MakeLabel(hw.Summary, bold:=True))

        cmbVariant = New ComboBox() With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 330}
        Dim autoV = LlamaEngine.AutoVariant(hw)
        cmbVariant.Items.AddRange({$"auto  → {LlamaEngine.VariantLabel(autoV)}", "cuda12  (NVIDIA, most compatible)",
                                   "cuda13  (NVIDIA RTX 20xx and newer)", "vulkan  (AMD / Intel / any GPU)", "rocm  (AMD, experimental)", "cpu  (no GPU)"})
        cmbVariant.SelectedIndex = Math.Max(0, Array.IndexOf({"auto", "cuda12", "cuda13", "vulkan", "rocm", "cpu"}, If(_s.EngineVariant, "auto")))
        Row(t, "GPU backend", cmbVariant)

        lblEngine = Theme.MakeLabel("", dim_:=True)
        btnInstall = Theme.MakeButton("Install / Update engine", primary:=True)
        Dim btnLog = Theme.MakeButton("View engine log")
        prgInstall = New ProgressBar() With {.Width = 220, .Height = 18, .Visible = False, .Margin = New Padding(3, 10, 3, 3)}
        Dim p As New FlowLayoutPanel() With {.AutoSize = True, .Dock = DockStyle.Fill, .Margin = New Padding(0)}
        p.Controls.AddRange({btnInstall, btnLog, prgInstall, lblEngine})
        Row(t, "Engine", p)
        AddHandler btnInstall.Click, AddressOf InstallClicked
        AddHandler btnLog.Click, Sub()
                                     Dim f As New EngineLogForm()
                                     f.Show(Me)
                                 End Sub
        AddHandler cmbVariant.SelectedIndexChanged, Sub() UpdateEngineLabel()
        UpdateEngineLabel()

        txtModels = New TextBox() With {.Text = _s.EffectiveModelsFolder, .Width = 360}
        Dim btnBrowse = Theme.MakeButton("…", width:=34)
        Dim pm As New FlowLayoutPanel() With {.AutoSize = True, .Dock = DockStyle.Fill, .Margin = New Padding(0)}
        Theme.StyleInput(txtModels)
        pm.Controls.AddRange({txtModels, btnBrowse})
        AddHandler btnBrowse.Click, Sub()
                                        Using d As New FolderBrowserDialog() With {.SelectedPath = txtModels.Text, .Description = "Folder for downloaded models (default: the Models folder next to LocalMind.exe)"}
                                            If d.ShowDialog(Me) = DialogResult.OK Then txtModels.Text = d.SelectedPath
                                        End Using
                                    End Sub
        Row(t, "Models folder", pm)

        chkAutoCtx = New CheckBox() With {.Text = "Auto: use the largest context that fits in VRAM", .Checked = _s.AutoContext, .AutoSize = True}
        Row(t, "Context size", chkAutoCtx)
        numMinCtx = Num(2048, 262144, _s.MinAutoContext, inc:=1024)
        Row(t, "   Minimum (auto)", numMinCtx, "Never go below this, even on small GPUs")
        numCtx = Num(1024, 1048576, _s.ContextSize, inc:=1024)
        Row(t, "   Fixed size (tokens)", numCtx, "Used when Auto is off, and for fit estimates in the Model Manager")
        AddHandler chkAutoCtx.CheckedChanged, Sub()
                                                  numMinCtx.Enabled = chkAutoCtx.Checked
                                              End Sub
        numMinCtx.Enabled = chkAutoCtx.Checked
        cmbNgl = New ComboBox() With {.DropDownStyle = ComboBoxStyle.DropDown, .Width = 110, .Text = _s.GpuLayers}
        cmbNgl.Items.AddRange({"auto", "all", "0", "16", "24", "32", "48"})
        Row(t, "GPU layers", cmbNgl, "auto = fit as much as possible into VRAM (recommended)")
        cmbKv = New ComboBox() With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 110}
        cmbKv.Items.AddRange({"f16", "q8_0", "q4_0"})
        cmbKv.SelectedItem = If(cmbKv.Items.Contains(_s.KvCacheType), _s.KvCacheType, "f16")
        Row(t, "KV cache type", cmbKv, "q8_0 halves context memory with almost no quality loss")
        numSlots = Num(1, 16, _s.ParallelSlots)
        Row(t, "Parallel chats", numSlots, "How many people (e.g. LAN users) can chat at the same time")
        numIdle = Num(0, 1440, _s.UnloadAfterIdleMinutes)
        Row(t, "Unload after idle (min)", numIdle, "0 = keep loaded. Frees VRAM for games etc.")
        cmbSpeed = New ComboBox() With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 330}
        cmbSpeed.Items.AddRange({"auto  (MTP file if the model has one, else n-gram)", "mtp  (only with an MTP file)", "ngram  (no extra file)", "off"})
        cmbSpeed.SelectedIndex = Math.Max(0, Array.IndexOf({"auto", "mtp", "ngram", "off"}, If(_s.SpeedUpMode, "auto")))
        Row(t, "Faster generation", cmbSpeed, "Speculative decoding: same answers, up to ~2x faster")
        chkMtp = New CheckBox() With {.Text = "Download MTP speed-up files with models (when available)", .Checked = _s.DownloadSpeedUpFiles, .AutoSize = True}
        Row(t, "", chkMtp)
        txtExtra = New TextBox() With {.Text = _s.ExtraEngineArgs, .Width = 360}
        Row(t, "Extra engine arguments", txtExtra, "Advanced llama-server flags, e.g. --n-cpu-moe 20")
        txtHfToken = New TextBox() With {.Text = _s.HuggingFaceToken, .Width = 360, .UseSystemPasswordChar = True}
        Row(t, "Hugging Face token", txtHfToken, "Only needed for gated models (Llama, some Gemma)")
        Dim btnCivKey = Theme.MakeButton(CivitaiKeyForm.ButtonText())
        btnCivKey.UseMnemonic = False
        AddHandler btnCivKey.Click, Sub()
                                        CivitaiKeyForm.Edit(Me)
                                        btnCivKey.Text = CivitaiKeyForm.ButtonText()
                                    End Sub
        Row(t, "Civitai", btnCivKey, "For Civitai downloads that need an account")
        chkVerify = New CheckBox() With {.Text = "Verify downloads with SHA-256", .Checked = _s.VerifyDownloads, .AutoSize = True}
        chkResume = New CheckBox() With {.Text = "Resume unfinished downloads when the app starts", .Checked = _s.AutoResumeDownloads, .AutoSize = True}
        Row(t, "Downloads", chkVerify)
        Row(t, "", chkResume)
    End Sub

    Private Function SelectedVariant() As String
        Return cmbVariant.SelectedItem.ToString().Split(" "c)(0)
    End Function

    Private Sub UpdateEngineLabel()
        Dim v = SelectedVariant()
        If v = "auto" Then v = LlamaEngine.AutoVariant(HardwareInfo.Current())
        Dim ver = AppCore.Engine.InstalledVersion(v)
        lblEngine.Text = If(AppCore.Engine.IsInstalled(v), $"{LlamaEngine.VariantLabel(v)}: installed ({If(ver, "?")})",
                                                          $"{LlamaEngine.VariantLabel(v)}: not installed (~50–700 MB, auto-installs on first use)")
    End Sub

    Private Async Sub InstallClicked(sender As Object, e As EventArgs)
        If _installCts IsNot Nothing Then
            _installCts.Cancel()
            Return
        End If
        Dim v = SelectedVariant()
        If v = "auto" Then v = LlamaEngine.AutoVariant(HardwareInfo.Current())
        _installCts = New CancellationTokenSource()
        btnInstall.Text = "Cancel"
        prgInstall.Visible = True
        Try
            Dim prog As New Progress(Of (Text As String, Percent As Integer))(
                Sub(p)
                    lblEngine.Text = p.Text
                    prgInstall.Value = Math.Max(0, Math.Min(100, p.Percent))
                End Sub)
            Dim tag = Await AppCore.Engine.InstallAsync(prog, _installCts.Token, v)
            lblEngine.Text = $"{LlamaEngine.VariantLabel(v)}: installed ({tag})"
        Catch ex As OperationCanceledException
            UpdateEngineLabel()
        Catch ex As Exception
            Localizer.Box(Me, ex.Message, "Engine install", MessageBoxButtons.OK, MessageBoxIcon.Error)
            UpdateEngineLabel()
        Finally
            _installCts = Nothing
            btnInstall.Text = "Install / Update engine"
            prgInstall.Visible = False
        End Try
    End Sub

    '──────────────── Chat tab ────────────────
    Private Sub BuildChat(page As TabPage)
        Dim t = Grid(page)
        numHistory = Num(2, 500, _s.HistoryMessages)
        Row(t, "Messages sent as history", numHistory, "Older messages are left out to save context")
        numImages = Num(1, 50, _s.KeepImagesInLastMessages)
        Row(t, "Keep images for last N msgs", numImages, "Older images are replaced by a note (saves a lot of context)")
        numTemp = Num(0, 2, CDec(_s.DefaultTemperature), dec:=2, inc:=0.05D)
        Row(t, "Default temperature", numTemp, "Used when no persona is selected")
        numTopP = Num(0, 1, CDec(_s.TopP), dec:=2, inc:=0.05D)
        Row(t, "Top-p", numTopP)
        numMaxTok = Num(0, 262144, _s.MaxTokens, inc:=256)
        Row(t, "Max answer tokens", numMaxTok, "0 = no limit")
        cmbLang = New ComboBox() With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 180}
        cmbLang.Items.AddRange({"auto (Windows)", "English", "Ελληνικά"})
        cmbLang.SelectedIndex = Math.Max(0, Array.IndexOf({"auto", "en", "el"}, If(_s.Language, "auto")))
        Row(t, "Interface language", cmbLang)
        chkImages = New CheckBox() With {.Text = "🎨 Pictures & video (create, edit, enlarge, video editing)", .Checked = _s.EnableImages, .AutoSize = True}
        chkAudio = New CheckBox() With {.Text = "🎵 Music & voice (songs, speech, voice cloning)", .Checked = _s.EnableAudio, .AutoSize = True}
        Row(t, "Extra abilities", chkImages, "Off = hidden, and nothing for it is downloaded")
        Row(t, "", chkAudio)
        chkUpdates = New CheckBox() With {.Text = "Check for LocalMind updates at every start", .Checked = _s.CheckForUpdates, .AutoSize = True}
        Row(t, "Updates", chkUpdates)
        chkAutoInstall = New CheckBox() With {.Text = "Install LocalMind updates by themselves (used from the next start)", .Checked = _s.AutoInstallUpdates, .AutoSize = True}
        Row(t, "", chkAutoInstall)
        chkEngUpd = New CheckBox() With {.Text = "Keep the AI engines and the model list up to date automatically", .Checked = _s.AutoUpdateEngines, .AutoSize = True}
        Row(t, "", chkEngUpd)
        chkAgent = New CheckBox() With {.Text = "The AI may draw, make videos, compose and search the web by itself when you ask", .Checked = _s.AgentTools, .AutoSize = True}
        Row(t, "AI tools", chkAgent)
        chkMinTray = New CheckBox() With {.Text = "Minimize to the system tray", .Checked = _s.MinimizeToTray, .AutoSize = True}
        chkCloseTray = New CheckBox() With {.Text = "Close button keeps LocalMind running in the tray", .Checked = _s.CloseToTray, .AutoSize = True}
        Row(t, "Tray", chkMinTray)
        Row(t, "", chkCloseTray)
        txtHotkey = New TextBox() With {.Text = _s.QuickAskHotkey, .Width = 180, .ReadOnly = True, .Cursor = Cursors.Hand}
        Row(t, "Quick-ask hotkey", txtHotkey, "Click, then press the keys (Backspace = none)")
        AddHandler txtHotkey.KeyDown, Sub(s, e)
                                          e.SuppressKeyPress = True
                                          If e.KeyCode = Keys.Back OrElse e.KeyCode = Keys.Delete Then
                                              txtHotkey.Text = ""
                                              Return
                                          End If
                                          If e.KeyCode = Keys.ControlKey OrElse e.KeyCode = Keys.Menu OrElse e.KeyCode = Keys.ShiftKey Then Return
                                          If Not (e.Control OrElse e.Alt) Then Return   ' must include Ctrl or Alt
                                          Dim parts As New List(Of String)
                                          If e.Control Then parts.Add("Ctrl")
                                          If e.Alt Then parts.Add("Alt")
                                          If e.Shift Then parts.Add("Shift")
                                          parts.Add(e.KeyCode.ToString())
                                          txtHotkey.Text = String.Join("+", parts)
                                      End Sub
    End Sub

    '──────────────── Web & voice tab ────────────────
    Private cmbSearch, cmbVoiceModel, cmbVoiceLang, cmbTts, cmbAiVoice, cmbSavedVoice As ComboBox
    Private txtBrave, txtSearx As TextBox
    Private numPages As NumericUpDown

    Private Sub BuildTools(page As TabPage)
        Dim t = Grid(page)
        Wide(t, Theme.MakeLabel("🌐 Web search", bold:=True))
        cmbSearch = New ComboBox() With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 260}
        cmbSearch.Items.AddRange({"duckduckgo  (no key needed)", "brave  (free API key)", "searxng  (your own server)"})
        cmbSearch.SelectedIndex = Math.Max(0, Array.IndexOf({"duckduckgo", "brave", "searxng"}, If(_s.WebSearchProvider, "duckduckgo")))
        Row(t, "Search engine", cmbSearch)
        txtBrave = New TextBox() With {.Text = _s.BraveApiKey, .Width = 300, .UseSystemPasswordChar = True}
        Row(t, "Brave API key", txtBrave, "brave.com/search/api — free tier")
        txtSearx = New TextBox() With {.Text = _s.SearxngUrl, .Width = 300}
        Row(t, "SearXNG address", txtSearx, "e.g. http://192.168.1.5:8888")
        numPages = Num(0, 5, _s.WebPagesToRead)
        Row(t, "Pages to read", numPages, "Top results whose full text is given to the model")

        Wide(t, Theme.MakeLabel("🎤 Voice", bold:=True))
        cmbVoiceModel = New ComboBox() With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 260}
        cmbVoiceModel.Items.AddRange({"auto", "base-q5_1", "small-q5_1", "medium-q5_0", "large-v3-turbo-q5_0", "large-v3-q5_0"})
        cmbVoiceModel.SelectedItem = If(cmbVoiceModel.Items.Contains(_s.VoiceModel), _s.VoiceModel, "auto")
        Row(t, "Speech model", cmbVoiceModel, "auto = large-v3-turbo on NVIDIA, small otherwise (Greek works best with turbo)")
        cmbVoiceLang = New ComboBox() With {.DropDownStyle = ComboBoxStyle.DropDown, .Width = 120, .Text = _s.VoiceLanguage}
        cmbVoiceLang.Items.AddRange({"auto", "el", "en", "de", "fr", "es", "it"})
        Row(t, "Spoken language", cmbVoiceLang, "auto-detect, or a language code")
        cmbTts = New ComboBox() With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 300}
        cmbTts.Items.Add("(Windows default)")
        cmbTts.Items.AddRange(Voice.VoiceNames())
        cmbTts.SelectedItem = If(cmbTts.Items.Contains(_s.TtsVoice), _s.TtsVoice, "(Windows default)")
        Row(t, "Reading voice", cmbTts, "Greek text automatically uses a Greek voice if one is installed")
        cmbAiVoice = New ComboBox() With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 300}
        cmbAiVoice.Items.Add(Localizer.T("(use the Windows voice above)"))
        For Each m In AudioStudio.Models().Where(Function(x) Not x.IsMusic)
            cmbAiVoice.Items.Add(m)
        Next
        Dim curModel = AudioStudio.Models().FirstOrDefault(Function(x) x.Id = _s.ReadAloudModel)
        cmbAiVoice.SelectedItem = If(curModel IsNot Nothing, CObj(curModel), cmbAiVoice.Items(0))
        Row(t, "AI reading voice", cmbAiVoice, "Natural AI voice for 🔊 Read (downloads the model on first use)")
        cmbSavedVoice = New ComboBox() With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 300}
        cmbSavedVoice.Items.Add(Localizer.T("(model's own voice)"))
        cmbSavedVoice.Items.AddRange(VoiceLibrary.All().Cast(Of Object).ToArray())
        Dim curVoice = VoiceLibrary.Find(_s.ReadAloudVoice)
        cmbSavedVoice.SelectedIndex = If(curVoice Is Nothing, 0, Math.Max(0, cmbSavedVoice.Items.Cast(Of Object).ToList().FindIndex(Function(o) o.ToString() = curVoice.Name)))
        Row(t, "Speak in this voice", cmbSavedVoice, "Voices you saved in the Music & voice studio (e.g. your own)")
    End Sub

    '──────────────── Save ────────────────
    Private _themeChanged As Boolean

    Protected Overrides Sub OnFormClosed(e As FormClosedEventArgs)
        MyBase.OnFormClosed(e)
        If _themeChanged AndAlso Localizer.Box(Owner, "The new look is used after a restart. Restart LocalMind now?", "Appearance",
                                               MessageBoxButtons.YesNo, MessageBoxIcon.Question) = DialogResult.Yes Then
            Dim mf = TryCast(Owner, MainForm)
            If mf Is Nothing Then mf = Application.OpenForms.OfType(Of MainForm)().FirstOrDefault()
            mf?.RestartApp()
        End If
    End Sub

    Private Sub SaveAndClose(sender As Object, e As EventArgs)
        Dim engineBefore = (_s.EngineVariant, _s.ContextSize, _s.GpuLayers, _s.KvCacheType, _s.ParallelSlots, _s.ExtraEngineArgs, _s.AutoContext, (_s.MinAutoContext, _s.SpeedUpMode))
        Dim backendBefore = (_s.Backend, _s.OpenAiUrl, _s.OpenAiKey)
        Dim folderBefore = _s.EffectiveModelsFolder

        _s.Backend = If(rbOpenAi.Checked, BackendKind.OpenAiCompatible, BackendKind.BuiltIn)
        _s.OpenAiUrl = txtOpenAiUrl.Text.Trim()
        _s.OpenAiKey = txtOpenAiKey.Text.Trim()
        _s.EngineVariant = SelectedVariant()
        Dim folder = txtModels.Text.Trim()
        _s.ModelsFolder = If(String.Equals(folder, AppPaths.DefaultModelsFolder, StringComparison.OrdinalIgnoreCase), "", folder)
        _s.ContextSize = CInt(numCtx.Value)
        _s.AutoContext = chkAutoCtx.Checked
        _s.MinAutoContext = CInt(numMinCtx.Value)
        _s.CheckForUpdates = chkUpdates.Checked
        _s.AutoUpdateEngines = chkEngUpd.Checked
        _s.AutoInstallUpdates = chkAutoInstall.Checked
        _s.ShowStartupScreen = chkSplash.Checked
        Dim newTheme = ThemePresets.All(Math.Max(0, cmbTheme.SelectedIndex)).Name
        If newTheme <> Theme.PresetName Then
            _s.ThemeName = newTheme
            _themeChanged = True
        End If
        _s.AgentTools = chkAgent.Checked
        _s.EnableImages = chkImages.Checked
        _s.EnableAudio = chkAudio.Checked
        _s.FeaturesAsked = True
        Dim newLang = {"auto", "en", "el"}(Math.Max(0, cmbLang.SelectedIndex))
        If newLang <> If(_s.Language, "auto") Then
            _s.Language = newLang
            Localizer.Box(Me, "Restart LocalMind to change the language.", "Language")
        End If
        _s.WebSearchProvider = cmbSearch.SelectedItem.ToString().Split(" "c)(0)
        _s.BraveApiKey = txtBrave.Text.Trim()
        _s.SearxngUrl = txtSearx.Text.Trim()
        _s.WebPagesToRead = CInt(numPages.Value)
        _s.VoiceModel = cmbVoiceModel.SelectedItem.ToString()
        _s.VoiceLanguage = If(String.IsNullOrWhiteSpace(cmbVoiceLang.Text), "auto", cmbVoiceLang.Text.Trim())
        _s.TtsVoice = If(cmbTts.SelectedIndex <= 0, "", cmbTts.SelectedItem.ToString())
        Dim aiModel = TryCast(cmbAiVoice.SelectedItem, AudioModel)
        _s.ReadAloudModel = If(aiModel?.Id, "")
        _s.ReadAloudVoice = If(cmbSavedVoice.SelectedIndex <= 0, "", cmbSavedVoice.SelectedItem.ToString())
        If aiModel IsNot Nothing AndAlso Not AudioStudio.IsInstalled(aiModel) Then
            Localizer.Box(Me, "The AI voice is downloaded the first time it reads an answer.", "AI voice")
        End If
        _s.MinimizeToTray = chkMinTray.Checked
        _s.CloseToTray = chkCloseTray.Checked
        _s.QuickAskHotkey = txtHotkey.Text.Trim()
        _s.GpuLayers = If(String.IsNullOrWhiteSpace(cmbNgl.Text), "auto", cmbNgl.Text.Trim())
        _s.KvCacheType = cmbKv.SelectedItem.ToString()
        _s.ParallelSlots = CInt(numSlots.Value)
        _s.UnloadAfterIdleMinutes = CInt(numIdle.Value)
        _s.ExtraEngineArgs = txtExtra.Text.Trim()
        _s.SpeedUpMode = cmbSpeed.SelectedItem.ToString().Split(" "c)(0)
        _s.DownloadSpeedUpFiles = chkMtp.Checked
        _s.HuggingFaceToken = txtHfToken.Text.Trim()
        _s.VerifyDownloads = chkVerify.Checked
        _s.AutoResumeDownloads = chkResume.Checked
        _s.HistoryMessages = CInt(numHistory.Value)
        _s.KeepImagesInLastMessages = CInt(numImages.Value)
        _s.DefaultTemperature = CDbl(numTemp.Value)
        _s.TopP = CDbl(numTopP.Value)
        _s.MaxTokens = CInt(numMaxTok.Value)
        AppCore.SaveSettings()

        If Not backendBefore.Equals((_s.Backend, _s.OpenAiUrl, _s.OpenAiKey)) Then AppCore.ResetBackend()
        If Not String.Equals(folderBefore, _s.EffectiveModelsFolder, StringComparison.OrdinalIgnoreCase) Then AppCore.Library.Refresh()
        If Not engineBefore.Equals((_s.EngineVariant, _s.ContextSize, _s.GpuLayers, _s.KvCacheType, _s.ParallelSlots, _s.ExtraEngineArgs, _s.AutoContext, (_s.MinAutoContext, _s.SpeedUpMode))) Then
            ' Reload with the new settings on the next message.
            Dim unused = AppCore.Engine.StopAsync()
            AppCore.ResetBackend()
        End If
        DialogResult = DialogResult.OK
        Close()
    End Sub

    Protected Overrides Sub OnFormClosing(e As FormClosingEventArgs)
        _installCts?.Cancel()
        MyBase.OnFormClosing(e)
    End Sub
End Class

''' <summary>Live view of llama-server output — useful when a model fails to load.</summary>
Public Class EngineLogForm
    Inherits Form

    Private ReadOnly txt As TextBox

    Public Sub New()
        Text = "Engine log"
        Size = New Size(980, 560)
        StartPosition = FormStartPosition.CenterParent
        Theme.ApplyForm(Me)
        txt = New TextBox() With {.Dock = DockStyle.Fill, .Multiline = True, .ReadOnly = True, .ScrollBars = ScrollBars.Both,
                                  .WordWrap = False, .Font = New Font("Cascadia Code", 9.0!), .BackColor = Theme.CBack, .ForeColor = Theme.CText}
        Dim bar As New FlowLayoutPanel() With {.Dock = DockStyle.Bottom, .Height = 42, .Padding = New Padding(4)}
        Dim btnCopy = Theme.MakeButton("Copy all")
        Dim btnStop = Theme.MakeButton("Unload model")
        AddHandler btnCopy.Click, Sub()
                                      Try
                                          Clipboard.SetText(txt.Text)
                                      Catch
                                      End Try
                                  End Sub
        AddHandler btnStop.Click, Async Sub() Await AppCore.Engine.StopAsync()
        bar.Controls.AddRange({btnCopy, btnStop})
        Controls.Add(txt)
        Controls.Add(bar)
        txt.Text = AppCore.Engine.GetLog()
        AddHandler AppCore.Engine.LogLine, AddressOf OnLine
    End Sub

    Protected Overrides Sub OnShown(e As EventArgs)
        MyBase.OnShown(e)
        txt.SelectionStart = txt.TextLength
        txt.ScrollToCaret()
    End Sub

    Private Sub OnLine(line As String)
        If IsDisposed OrElse Not IsHandleCreated Then Return
        Me.Post(Sub()
                        If txt.TextLength > 400000 Then txt.Text = txt.Text.Substring(200000)
                        txt.AppendText(line & Environment.NewLine)
                    End Sub)
    End Sub

    Protected Overrides Sub OnFormClosed(e As FormClosedEventArgs)
        RemoveHandler AppCore.Engine.LogLine, AddressOf OnLine
        MyBase.OnFormClosed(e)
    End Sub
End Class
