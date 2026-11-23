Imports System
Imports System.Drawing
Imports System.IO
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms

Public Class SettingsForm
    Inherits Form

    Private ReadOnly _s As AppSettings
    Private _installCts As CancellationTokenSource

    ' Backend
    Private rbBuiltIn, rbOllama, rbOpenAi As RadioButton
    Private txtOllamaUrl, txtOpenAiUrl, txtOpenAiKey As TextBox
    ' Engine
    Private cmbVariant As ComboBox
    Private lblEngine As Label
    Private btnInstall As Button
    Private prgInstall As ProgressBar
    Private txtModels As TextBox
    Private numCtx, numSlots, numIdle As NumericUpDown
    Private cmbNgl, cmbKv As ComboBox
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
        tabs.TabPages.AddRange({tBackend, tEngine, tChat})
        Theme.StyleTabs(tabs)

        BuildBackend(tBackend)
        BuildEngine(tEngine)
        BuildChat(tChat)

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

    '──────────────── Backend tab ────────────────
    Private Sub BuildBackend(page As TabPage)
        Dim t = Grid(page)
        rbBuiltIn = New RadioButton() With {.Text = "Built-in engine (llama.cpp) — runs models itself, no Ollama needed  ★ recommended", .AutoSize = True, .Checked = _s.Backend = BackendKind.BuiltIn}
        rbOllama = New RadioButton() With {.Text = "Ollama", .AutoSize = True, .Checked = _s.Backend = BackendKind.Ollama}
        rbOpenAi = New RadioButton() With {.Text = "OpenAI-compatible server (LM Studio, vLLM, Jan, another PC running this app…)", .AutoSize = True, .Checked = _s.Backend = BackendKind.OpenAiCompatible}
        txtOllamaUrl = New TextBox() With {.Text = _s.OllamaUrl, .Width = 360}
        txtOpenAiUrl = New TextBox() With {.Text = _s.OpenAiUrl, .Width = 360}
        txtOpenAiKey = New TextBox() With {.Text = _s.OpenAiKey, .Width = 360, .UseSystemPasswordChar = True}

        Wide(t, Theme.MakeLabel("Where should the AI run?", bold:=True))
        Wide(t, rbBuiltIn)
        Wide(t, rbOllama)
        Row(t, "   Ollama URL", txtOllamaUrl, "Can be another PC: http://192.168.1.20:11434")
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
                                        Using d As New FolderBrowserDialog() With {.SelectedPath = txtModels.Text, .Description = "Folder for downloaded models (pick a big drive)"}
                                            If d.ShowDialog(Me) = DialogResult.OK Then txtModels.Text = d.SelectedPath
                                        End Using
                                    End Sub
        Row(t, "Models folder", pm)

        numCtx = Num(1024, 1048576, _s.ContextSize, inc:=1024)
        Row(t, "Context size (tokens)", numCtx, "Memory per chat. 8K–32K is typical; bigger = more VRAM")
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
        txtExtra = New TextBox() With {.Text = _s.ExtraEngineArgs, .Width = 360}
        Row(t, "Extra engine arguments", txtExtra, "Advanced llama-server flags, e.g. --n-cpu-moe 20")
        txtHfToken = New TextBox() With {.Text = _s.HuggingFaceToken, .Width = 360, .UseSystemPasswordChar = True}
        Row(t, "Hugging Face token", txtHfToken, "Only needed for gated models (Llama, some Gemma)")
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
            MessageBox.Show(Me, ex.Message, "Engine install", MessageBoxButtons.OK, MessageBoxIcon.Error)
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
    End Sub

    '──────────────── Save ────────────────
    Private Sub SaveAndClose(sender As Object, e As EventArgs)
        Dim engineBefore = (_s.EngineVariant, _s.ContextSize, _s.GpuLayers, _s.KvCacheType, _s.ParallelSlots, _s.ExtraEngineArgs)
        Dim backendBefore = (_s.Backend, _s.OllamaUrl, _s.OpenAiUrl, _s.OpenAiKey)
        Dim folderBefore = _s.EffectiveModelsFolder

        _s.Backend = If(rbOllama.Checked, BackendKind.Ollama, If(rbOpenAi.Checked, BackendKind.OpenAiCompatible, BackendKind.BuiltIn))
        _s.OllamaUrl = txtOllamaUrl.Text.Trim()
        _s.OpenAiUrl = txtOpenAiUrl.Text.Trim()
        _s.OpenAiKey = txtOpenAiKey.Text.Trim()
        _s.EngineVariant = SelectedVariant()
        Dim folder = txtModels.Text.Trim()
        _s.ModelsFolder = If(String.Equals(folder, Path.Combine(AppPaths.LocalData, "models"), StringComparison.OrdinalIgnoreCase), "", folder)
        _s.ContextSize = CInt(numCtx.Value)
        _s.GpuLayers = If(String.IsNullOrWhiteSpace(cmbNgl.Text), "auto", cmbNgl.Text.Trim())
        _s.KvCacheType = cmbKv.SelectedItem.ToString()
        _s.ParallelSlots = CInt(numSlots.Value)
        _s.UnloadAfterIdleMinutes = CInt(numIdle.Value)
        _s.ExtraEngineArgs = txtExtra.Text.Trim()
        _s.HuggingFaceToken = txtHfToken.Text.Trim()
        _s.VerifyDownloads = chkVerify.Checked
        _s.AutoResumeDownloads = chkResume.Checked
        _s.HistoryMessages = CInt(numHistory.Value)
        _s.KeepImagesInLastMessages = CInt(numImages.Value)
        _s.DefaultTemperature = CDbl(numTemp.Value)
        _s.TopP = CDbl(numTopP.Value)
        _s.MaxTokens = CInt(numMaxTok.Value)
        AppCore.SaveSettings()

        If Not backendBefore.Equals((_s.Backend, _s.OllamaUrl, _s.OpenAiUrl, _s.OpenAiKey)) Then AppCore.ResetBackend()
        If Not String.Equals(folderBefore, _s.EffectiveModelsFolder, StringComparison.OrdinalIgnoreCase) Then AppCore.Library.Refresh()
        If Not engineBefore.Equals((_s.EngineVariant, _s.ContextSize, _s.GpuLayers, _s.KvCacheType, _s.ParallelSlots, _s.ExtraEngineArgs)) Then
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
        BeginInvoke(Sub()
                        If txt.TextLength > 400000 Then txt.Text = txt.Text.Substring(200000)
                        txt.AppendText(line & Environment.NewLine)
                    End Sub)
    End Sub

    Protected Overrides Sub OnFormClosed(e As FormClosedEventArgs)
        RemoveHandler AppCore.Engine.LogLine, AddressOf OnLine
        MyBase.OnFormClosed(e)
    End Sub
End Class
