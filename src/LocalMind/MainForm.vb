Imports System
Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Drawing
Imports System.IO
Imports System.Linq
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports Microsoft.Web.WebView2.Core
Imports Newtonsoft.Json
Imports Newtonsoft.Json.Linq

Public Class MainForm

    Private _personas As List(Of Persona)
    Private _session As ChatSession
    Private _cts As CancellationTokenSource
    Private _isGenerating As Boolean
    Private _webReady As Boolean
    Private _streamMsgId As String
    Private _streamTokens As Integer
    Private _streamStart As DateTime
    Private _pendingAttachments As New List(Of AttachmentItem)()
    Private _chatContextMenu As ContextMenuStrip
    Private _miDeleteChat As ToolStripMenuItem
    Private _miRenameChat As ToolStripMenuItem

    Private ReadOnly _liveAssistantText As New StringBuilder()
    Private ReadOnly _liveTextLock As New Object()
    Private ReadOnly _liveThinkingText As New StringBuilder()
    Private ReadOnly _liveThinkingLock As New Object()
    Private _lastRenderedThinkingLength As Integer
    Private _firstContentSeen As Boolean

    Private _flushTimer As System.Windows.Forms.Timer
    Private _lastRenderedLength As Integer
    Private _lastRenderedAt As DateTime

    Private _connTimer As System.Windows.Forms.Timer
    Private _statusTimer As System.Windows.Forms.Timer
    Private _lastOnlineState As Boolean = False
    Private _isCheckingConnection As Boolean = False
    Private _isFirstConnectionCheck As Boolean = True
    Private _chatListDirty As Boolean
    Private _modelsDirty As Boolean
    Private _engineInstallTask As Task

    Private Const NoPersonaLabel As String = "(No persona)"
    Private Const SearchPlaceholder As String = "Search chats…"

    Private Class AttachmentItem
        Public Path As String
        Public IsImage As Boolean
        Public IsAudio As Boolean
        Public Text As String
        Public Base64 As String
        Public Chip As Control
    End Class

    Public Sub New()
        InitializeComponent()
        Theme.DarkTitleBar(Me)
        MediaStudio.InpaintComposer = AddressOf ImageTools.CompositeInpaint
        OnnxTools.Register()
        Try
            Me.Icon = Theme.AppIcon()
        Catch
        End Try
    End Sub

    Private ReadOnly Property Settings As AppSettings
        Get
            Return AppCore.Settings
        End Get
    End Property

    '════════════════════════════════════════════════════════════
    '  Startup
    '════════════════════════════════════════════════════════════
    Private Async Sub MainForm_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        _session = New ChatSession()
        Text = "LocalMind " & Application.ProductVersion.Split("+"c)(0)

        _flushTimer = New System.Windows.Forms.Timer() With {.Interval = 60}
        AddHandler _flushTimer.Tick, AddressOf FlushLiveRender

        _connTimer = New System.Windows.Forms.Timer() With {.Interval = 5000}
        AddHandler _connTimer.Tick, AddressOf CheckConnectionTick

        ' Cheap UI-thread pump for things that change on background threads.
        _statusTimer = New System.Windows.Forms.Timer() With {.Interval = 700}
        AddHandler _statusTimer.Tick, AddressOf StatusTick
        _statusTimer.Start()

        AddHandler AppCore.Engine.StateChanged, AddressOf OnEngineState
        AddHandler AppCore.Library.Changed, Sub() _modelsDirty = True
        AddHandler AppCore.History.Changed, AddressOf OnHistoryChanged
        AddHandler AppCore.Downloads.JobCompleted, AddressOf OnDownloadCompleted
        AddHandler AppCore.BackendChanged, Sub() _modelsDirty = True

        LoadPersonasIntoCombo()
        RefreshChatList()
        Await InitWebViewAsync()

        AppCore.Library.Refresh()
        Await RefreshModelsKeepSelectionAsync()
        Await CheckConnectionNowAsync()
        _connTimer.Start()

        btnThinking.Checked = Settings.ShowThinking
        AddHandler cmbModels.SelectedIndexChanged, AddressOf SaveModelSelection
        AddHandler cmbPersonas.SelectedIndexChanged, AddressOf SavePersonaSelection
        AddHandler btnThinking.CheckedChanged, AddressOf SaveThinkingPreference

        _webReady = True
        txtSearch.Text = ""
        txtSearch.PlaceholderText = SearchPlaceholder
        txtSearch.ForeColor = Theme.CText

        BuildChatContextMenu()
        BuildFileMenuExtras()
        BuildPromptsButton()
        BuildToolsUi()
        BuildFolderFilter()
        SetupTray()
        AddHandler txtInput.DragEnter, AddressOf Files_DragEnter
        AddHandler txtInput.DragDrop, AddressOf Files_DragDrop
        AddHandler Me.DragEnter, AddressOf Files_DragEnter
        AddHandler Me.DragDrop, AddressOf Files_DragDrop

        miNewChat.ShortcutKeys = Keys.Control Or Keys.N
        ApplyModernLayout()
        Localizer.Apply(Me)
        Theme.Recolor(Me)
        If _tray?.ContextMenuStrip IsNot Nothing Then
            For Each it As ToolStripItem In _tray.ContextMenuStrip.Items
                Localizer.WalkItem(it)
            Next
        End If
        ShowEmptyState()
        SetStatus("Ready")

        If Settings.AutoResumeDownloads Then
            Dim n = AppCore.Downloads.ResumePending()
            If n > 0 Then SetStatus($"Resuming {n} unfinished download(s)…")
        End If
        If Settings.LanEnabled Then Await StartLanAsync()

        FirstRun()
        PrepareEngineInBackground()
        ' (screenshot checks)
        Select Case Environment.GetEnvironmentVariable("LOCALMIND_PREVIEW")
            Case "studio" : OpenStudio()
            Case "cast"
                Dim cf As New CastForm()
                cf.Show(Me)
            Case "film"
                FilmProject.AddToCurrent(IO.Path.Combine(MediaStudio.OutputFolder, "sample.png"))
                Dim fm As New FilmForm()
                fm.Show(Me)
            Case "box"
                Me.Post(Sub() Localizer.Box(Me, "Delete Qwen3-8B (Q4_K_M) from disk? The file is 4.7 GB.", "Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Warning))
            Case "settings-look"
                Me.Post(Sub()
                            Using sf As New SettingsForm()
                                AddHandler sf.Shown, Sub()
                                                         Dim tabs = sf.Controls.OfType(Of TabControl)().FirstOrDefault()
                                                         If tabs IsNot Nothing Then tabs.SelectedIndex = tabs.TabCount - 1
                                                     End Sub
                                sf.ShowDialog(Me)
                            End Using
                        End Sub)
            Case Else
                ' any window by name (screenshot checks): LOCALMIND_PREVIEW=form:KnowledgeForm
                Dim pv = Environment.GetEnvironmentVariable("LOCALMIND_PREVIEW")
                If pv IsNot Nothing AndAlso pv.StartsWith("form:") Then
                    Try
                        Dim t = GetType(MainForm).Assembly.GetType("LocalMind." & pv.Substring(5))
                        Dim ctor = t.GetConstructors().OrderBy(Function(c) c.GetParameters().Length).First()
                        Dim args = ctor.GetParameters().Select(Function(pp) As Object
                                                                   If pp.HasDefaultValue Then Return pp.DefaultValue
                                                                   If pp.ParameterType Is GetType(String) Then Return ""
                                                                   If pp.ParameterType Is GetType(List(Of Persona)) Then Return AppCore.Personas.Load()
                                                                   If pp.ParameterType.IsGenericType AndAlso pp.ParameterType.GetGenericTypeDefinition() Is GetType(IEnumerable(Of )) Then Return New List(Of String) From {"Qwen3-8B-Q4_K_M"}
                                                                   If pp.ParameterType Is GetType(Func(Of String, Task(Of String))) Then Return CType(Function(q As String) Task.FromResult(""), Func(Of String, Task(Of String)))
                                                                   Return Nothing
                                                               End Function).ToArray()
                        Dim f = CType(ctor.Invoke(args), Form)
                        f.Show(Me)
                    Catch ex As Exception
                        SetStatus("preview: " & ex.Message)
                    End Try
                End If
        End Select

        ' Help → Check for updates / Update everything now, plus a quiet daily round that updates
        ' LocalMind, the engines and the model list by itself.
        Dim miUpdate As New ToolStripMenuItem(Localizer.T("Check for updates…"))
        AddHandler miUpdate.Click, Async Sub() Await CheckUpdatesAsync(manual:=True)
        Dim miUpdateAll As New ToolStripMenuItem(Localizer.T("Update everything now (app, engines, model list)"))
        AddHandler miUpdateAll.Click, Sub() Task.Run(Function() UpdateEverythingAsync(manual:=True))
        menuHelp.DropDownItems.Insert(0, miUpdateAll)
        menuHelp.DropDownItems.Insert(0, miUpdate)
        AutoUpdateEverythingLater()
        LoadDone.TrySetResult(True)
    End Sub

    ''' <summary>Set when the window has finished loading (the loading screen waits for it).</summary>
    <System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)>
    Public ReadOnly Property LoadDone As New TaskCompletionSource(Of Boolean)()

    ''' <summary>Closes LocalMind and opens it again (e.g. for a new look).</summary>
    Public Sub RestartApp()
        Updater.Restart(Environment.ProcessPath)
        _reallyExit = True
        Close()
    End Sub

    Private _updating As Boolean

    Private Async Function CheckUpdatesAsync(manual As Boolean) As Task
        If _updating Then Return
        _updating = True
        Try
            Dim info = Await Updater.CheckAsync(CancellationToken.None)
            Settings.LastUpdateCheck = DateTime.Now
            AppCore.SaveSettings()
            If info Is Nothing Then
                If manual Then Localizer.Box(Me, $"You have the latest version ({Updater.CurrentVersion}).", "Updates",
                                               MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If
            Dim notes = If(info.Notes, "")
            If notes.Length > 700 Then notes = notes.Substring(0, 700) & "…"
            If Localizer.Box(Me, $"LocalMind {info.Tag} is available (you have {Updater.CurrentVersion})." & vbCrLf & vbCrLf &
                               notes & vbCrLf & vbCrLf & "Update now? LocalMind will restart; your chats and models are kept.",
                               "Update available", MessageBoxButtons.YesNo, MessageBoxIcon.Information) <> DialogResult.Yes Then Return
            If Not AppPaths.IsPortable Then
                ModelManagerForm.OpenUrl(If(info.PageUrl, Updater.ReleasesPage))
                Return
            End If
            If _isGenerating Then
                Localizer.Box(Me, "Wait for the current answer to finish, then use Help → Check for updates.", "Update")
                Return
            End If
            Dim exe = Await Updater.InstallAsync(info, Sub(p) SetStatus($"Downloading update… {p}%"), CancellationToken.None)
            Updater.Restart(exe)
            _reallyExit = True
            Close()
        Catch ex As Exception
            If manual Then Localizer.Box(Me, "Update failed: " & ex.Message, "Update", MessageBoxButtons.OK, MessageBoxIcon.Warning)
        Finally
            _updating = False
        End Try
    End Function

    ''' <summary>One-time welcome: choose abilities, then the Model Manager for new users.</summary>
    Private Sub FirstRun()
        If Not Settings.FeaturesAsked Then
            Using f As New FeaturesForm()
                f.ShowDialog(Me)
            End Using
            ApplyFeatures()
        End If

        If Not Settings.FirstRunDone Then
            Settings.FirstRunDone = True
            AppCore.SaveSettings()
            If Settings.Backend = BackendKind.BuiltIn AndAlso AppCore.Library.All().Count = 0 Then
                Localizer.Box(Me,
                    "Welcome! This app runs AI models directly on your PC." & vbCrLf & vbCrLf &
                    "Next, the Model Manager opens: press '★ Recommend for my PC' (or tick any model), " &
                    "then 'Download'. Everything else is set up automatically.",
                    "Welcome", MessageBoxButtons.OK, MessageBoxIcon.Information)
                OpenModelManager()
            End If
        End If
    End Sub

    ''' <summary>Installs the llama.cpp engine in the background so it's ready when the first model finishes downloading.</summary>
    Private Sub PrepareEngineInBackground()
        If Settings.Backend <> BackendKind.BuiltIn OrElse AppCore.Engine.IsInstalled() Then Return
        If _engineInstallTask IsNot Nothing AndAlso Not _engineInstallTask.IsCompleted Then Return
        Dim prog As New Progress(Of (Text As String, Percent As Integer))(Sub(p) SetStatus(p.Text))
        _engineInstallTask = Task.Run(Async Function()
                                          Try
                                              Await AppCore.Engine.InstallAsync(prog, CancellationToken.None)
                                              SetStatus("AI engine installed")
                                          Catch ex As Exception
                                              SetStatus("Engine install failed (will retry on first message): " & ex.Message)
                                          End Try
                                      End Function)
    End Sub


    '════════════════════════════════════════════════════════════
    '  Personas
    '════════════════════════════════════════════════════════════
    Private Sub LoadPersonasIntoCombo()
        _personas = AppCore.Personas.Load()
        Dim prevName = If(cmbPersonas.SelectedItem?.ToString(), Settings.LastPersona)

        cmbPersonas.Items.Clear()
        cmbPersonas.Items.Add(NoPersonaLabel)
        For Each p In _personas
            cmbPersonas.Items.Add(p.Name)
        Next

        Dim idx = 0
        If Not String.IsNullOrEmpty(prevName) AndAlso prevName <> NoPersonaLabel Then
            For i = 0 To _personas.Count - 1
                If String.Equals(_personas(i).Name, prevName, StringComparison.OrdinalIgnoreCase) Then
                    idx = i + 1
                    Exit For
                End If
            Next
        End If
        If cmbPersonas.Items.Count > 0 Then cmbPersonas.SelectedIndex = idx
    End Sub

    Private ReadOnly Property CurrentPersona As Persona
        Get
            If cmbPersonas.SelectedIndex <= 0 OrElse _personas Is Nothing Then Return Nothing
            Dim i = cmbPersonas.SelectedIndex - 1
            If i < 0 OrElse i >= _personas.Count Then Return Nothing
            Return _personas(i)
        End Get
    End Property

    Private Sub SavePersonaSelection(sender As Object, e As EventArgs)
        Dim p = CurrentPersona
        Settings.LastPersona = If(p IsNot Nothing, p.Name, "")
        AppCore.SaveSettings()
        If p IsNot Nothing Then SetStatus("Persona: " & p.Name)
    End Sub

    Private Sub btnEditPersona_Click(sender As Object, e As EventArgs) Handles btnEditPersona.Click
        Dim currentId = CurrentPersona?.Id
        Using dlg As New PersonaEditorForm(_personas)
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
        End Using
        _personas = AppCore.Personas.Load()
        LoadPersonasIntoCombo()
        Dim idx = 0
        If currentId IsNot Nothing Then
            For i = 0 To _personas.Count - 1
                If _personas(i).Id = currentId Then
                    idx = i + 1
                    Exit For
                End If
            Next
        End If
        If cmbPersonas.Items.Count > 0 Then cmbPersonas.SelectedIndex = idx
        SetStatus($"Saved {_personas.Count} persona(s) → {AppCore.Personas.FilePath}")
    End Sub

    '════════════════════════════════════════════════════════════
    '  Backend status / models
    '════════════════════════════════════════════════════════════
    Private Async Sub CheckConnectionTick(sender As Object, e As EventArgs)
        Await CheckConnectionNowAsync()
    End Sub

    Private Async Function CheckConnectionNowAsync() As Task
        If Settings.Backend = BackendKind.BuiltIn Then
            ShowEngineState(AppCore.Engine.State, AppCore.Engine.StateMessage)
            Return
        End If
        If _isCheckingConnection Then Return
        _isCheckingConnection = True
        Try
            Dim online As Boolean
            Try
                online = Await AppCore.Backend.IsOnlineAsync()
            Catch
                online = False
            End Try

            Dim name = "Server"
            SetConnLabel(If(online, $"● {name} connected", $"● {name} offline"), If(online, Color.LimeGreen, Color.IndianRed))

            If online AndAlso Not _lastOnlineState AndAlso Not _isFirstConnectionCheck AndAlso Not _isGenerating Then
                SetStatus(name & " reconnected — refreshing models…")
                Await RefreshModelsKeepSelectionAsync()
            ElseIf Not online AndAlso _lastOnlineState Then
                SetStatus(name & " disconnected")
            End If
            _lastOnlineState = online
            _isFirstConnectionCheck = False
        Finally
            _isCheckingConnection = False
        End Try
    End Function

    Private Sub SetConnLabel(text As String, color As Color)
        text = Localizer.T(text)
        If lblConnection.Text <> text Then lblConnection.Text = text
        ' softer versions of the traffic-light colors
        lblConnection.ForeColor = If(color = Color.LimeGreen, Theme.CGood, If(color = Color.Gold, Theme.CWarn,
                                  If(color = Color.IndianRed, Theme.CBad, If(color = Color.Gray, Theme.CDim, color))))
    End Sub

    Private Sub OnEngineState(state As EngineState, message As String)
        If IsDisposed OrElse Not IsHandleCreated Then Return
        Me.Post(Sub()
                        If Settings.Backend <> BackendKind.BuiltIn Then Return
                        ShowEngineState(state, message)
                        If Not String.IsNullOrEmpty(message) Then SetStatus(message)
                        If _isGenerating AndAlso state = EngineState.Loading AndAlso _streamMsgId IsNot Nothing Then
                            Js($"setStatusText('{_streamMsgId}', {JsonConvert.SerializeObject("⏳ " & message)});")
                        End If
                    End Sub)
    End Sub

    Private Sub ShowEngineState(state As EngineState, message As String)
        Select Case state
            Case EngineState.Ready
                SetConnLabel("● Ready", Color.LimeGreen)
                lblConnection.ToolTipText = If(AppCore.Engine.CurrentModel?.DisplayName, "")
            Case EngineState.Loading
                SetConnLabel("◌ Loading model…", Color.Gold)
            Case EngineState.Installing
                SetConnLabel("◌ Installing engine…", Color.Gold)
            Case EngineState.NotInstalled
                SetConnLabel("○ Engine not installed", Color.Gray)
            Case EngineState.Error
                SetConnLabel("● Engine error", Color.IndianRed)
                lblConnection.ToolTipText = message
            Case Else
                SetConnLabel("○ Engine idle", Color.Silver)
        End Select
    End Sub

    Private Async Function RefreshModelsKeepSelectionAsync() As Task
        Dim wanted = If(cmbModels.SelectedItem?.ToString(), Settings.LastModel)
        Dim models As List(Of String)
        Try
            models = Await AppCore.Backend.ListModelsAsync()
        Catch
            models = New List(Of String)()
        End Try
        cmbModels.Items.Clear()
        For Each m In models
            cmbModels.Items.Add(m)
        Next
        Dim idx = -1
        If Not String.IsNullOrEmpty(wanted) Then idx = cmbModels.Items.IndexOf(wanted)
        If idx < 0 AndAlso cmbModels.Items.Count > 0 Then idx = 0
        _fillingModels = True
        Try
            If idx >= 0 Then cmbModels.SelectedIndex = idx
        Finally
            _fillingModels = False
        End Try
        If _session IsNot Nothing AndAlso _session.Messages.Count = 0 Then ShowEmptyState()
    End Function

    ''' <summary>True while the list is refilled (not a choice of the user: nothing is loaded).</summary>
    Private _fillingModels As Boolean

    Private Sub SaveModelSelection(sender As Object, e As EventArgs)
        If cmbModels.SelectedItem IsNot Nothing Then Settings.LastModel = cmbModels.SelectedItem.ToString()
        AppCore.SaveSettings()
        If _session IsNot Nothing AndAlso _session.Messages.Count = 0 Then ShowEmptyState()
        ' Warm up when the user picks a model, so the first answer is quicker (never at start or on a list refresh).
        If Settings.Backend = BackendKind.BuiltIn AndAlso Not _isGenerating AndAlso Not _fillingModels Then
            Dim m = AppCore.Library.FindByName(Settings.LastModel)
            If m IsNot Nothing AndAlso AppCore.Engine.IsInstalled() Then
                Task.Run(Async Function()
                             Try
                                 Await AppCore.Engine.EnsureModelAsync(m, CancellationToken.None)
                             Catch ex As Exception
                                 SetStatus("Could not load model: " & FirstLine(ex.Message))
                             End Try
                         End Function)
            End If
        End If
    End Sub

    Private Sub SaveThinkingPreference(sender As Object, e As EventArgs)
        Settings.ShowThinking = btnThinking.Checked
        AppCore.SaveSettings()
        SetStatus(If(btnThinking.Checked, "Thinking mode enabled", "Thinking mode disabled"))
    End Sub

    ''' <summary>UI-thread housekeeping: download progress, refreshes requested by background threads.</summary>
    Private Async Sub StatusTick(sender As Object, e As EventArgs)
        ' (download progress shows only in Models → Downloads)
        If lblDownloads.Visible Then lblDownloads.Visible = False
        Dim lanText = LanButtonText()
        If btnLan.Text <> lanText Then btnLan.Text = lanText

        If _modelsDirty AndAlso Not _isGenerating Then
            _modelsDirty = False
            Await RefreshModelsKeepSelectionAsync()
        End If
        If _chatListDirty Then
            _chatListDirty = False
            If txtSearch.Text = SearchPlaceholder OrElse txtSearch.Text.Trim() = "" Then RefreshChatList()
        End If
    End Sub

    Private Sub OnDownloadCompleted(job As DownloadJob)
        If job.Work IsNot Nothing Then Return          ' (pictures, video, music, voice: nothing to do for the chat)
        _modelsDirty = True
        SetStatus($"✔ {job.ModelName} ({job.Quant}) downloaded — ready to chat")
        If String.IsNullOrEmpty(Settings.LastModel) OrElse AppCore.Library.FindByName(Settings.LastModel) Is Nothing Then
            Settings.LastModel = job.ModelName & " · " & job.Quant
            AppCore.SaveSettings()
        End If
    End Sub

    Private Sub OnHistoryChanged(id As String)
        _chatListDirty = True
        ' A phone continued the chat that is open here → reload it so the PC shows the new messages.
        If IsDisposed OrElse Not IsHandleCreated Then Return
        Me.Post(Async Sub()
                        If _isGenerating OrElse _session Is Nothing OrElse _session.Id <> id Then Return
                        Dim fresh = AppCore.History.Load(id)
                        If fresh IsNot Nothing AndAlso fresh IsNot _session Then
                            _session = fresh
                            Await RebuildChatDisplayAsync()
                        End If
                    End Sub)
    End Sub

    '════════════════════════════════════════════════════════════
    '  WebView
    '════════════════════════════════════════════════════════════
    ''' <summary>
    ''' Runs a script in the chat view. Until the view is ready (it starts in the background, and a chat can be
    ''' clicked before that), nothing runs: the chat is drawn in full once the view is ready.
    ''' </summary>
    Private Function Js(script As String) As Task(Of String)
        If webChat Is Nothing OrElse webChat.IsDisposed OrElse webChat.CoreWebView2 Is Nothing OrElse Not _viewReady Then
            _redrawWhenReady = True
            Return Task.FromResult(Of String)(Nothing)
        End If
        Try
            Return webChat.ExecuteScriptAsync(script)
        Catch ex As InvalidOperationException
            _redrawWhenReady = True
            Return Task.FromResult(Of String)(Nothing)
        End Try
    End Function

    Private _viewReady As Boolean
    Private _redrawWhenReady As Boolean

    Private Async Function InitWebViewAsync() As Task
        Try
            Dim env = Await CoreWebView2Environment.CreateAsync(Nothing, AppPaths.WebViewFolder)
            Await webChat.EnsureCoreWebView2Async(env)
            webChat.CoreWebView2.Settings.AreDefaultContextMenusEnabled = True
            webChat.CoreWebView2.Settings.AreDevToolsEnabled = False
            webChat.CoreWebView2.Settings.IsStatusBarEnabled = False
            AddHandler webChat.CoreWebView2.WebMessageReceived, AddressOf OnWebMessage
            Try
                webChat.CoreWebView2.SetVirtualHostNameToFolderMapping("outputs.localmind", MediaStudio.OutputFolder,
                                                                      CoreWebView2HostResourceAccessKind.Allow)
            Catch
            End Try
            ' Links in answers open in the real browser, not inside the chat view.
            AddHandler webChat.CoreWebView2.NewWindowRequested, Sub(s, a)
                                                                    a.Handled = True
                                                                    ModelManagerForm.OpenUrl(a.Uri)
                                                                End Sub
            AddHandler webChat.CoreWebView2.NavigationStarting, Sub(s, a)
                                                                    If a.Uri.StartsWith("http", StringComparison.OrdinalIgnoreCase) Then
                                                                        a.Cancel = True
                                                                        ModelManagerForm.OpenUrl(a.Uri)
                                                                    End If
                                                                End Sub
            Dim tcs As New TaskCompletionSource(Of Boolean)()
            Dim handler As EventHandler(Of CoreWebView2NavigationCompletedEventArgs) = Sub(s, a) tcs.TrySetResult(True)
            AddHandler webChat.CoreWebView2.NavigationCompleted, handler
            webChat.CoreWebView2.NavigateToString(MarkdownRenderer.ShellHtml())
            Await Task.WhenAny(tcs.Task, Task.Delay(5000))
            _viewReady = True
            Await Js("L = " & Localizer.ChatViewStrings() & ";")
            RemoveHandler webChat.CoreWebView2.NavigationCompleted, handler
            ' a chat chosen while the view was starting is drawn now
            If _redrawWhenReady AndAlso _session IsNot Nothing Then
                _redrawWhenReady = False
                Await RebuildChatDisplayAsync()
            End If
        Catch ex As Exception
            Localizer.Box("WebView2 runtime not found." & vbCrLf & ex.Message,
                            "WebView2", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Function

    Private Sub OnWebMessage(sender As Object, e As CoreWebView2WebMessageReceivedEventArgs)
        Try
            Dim obj = JObject.Parse(e.WebMessageAsJson)
            If obj("type")?.ToString() = "prompt" Then
                txtInput.Text = obj("text")?.ToString()
                txtInput.SelectionStart = txtInput.TextLength
                txtInput.Focus()
                Return
            End If
            If obj("type")?.ToString() = "speak" Then
                SpeakAnswer(obj("text")?.ToString())
                Return
            End If
            If obj("type")?.ToString() = "copy" Then
                Dim text = obj("text")?.ToString()
                If Not String.IsNullOrEmpty(text) Then
                    Try
                        Clipboard.SetText(text)
                    Catch
                        Thread.Sleep(50)
                        Try : Clipboard.SetText(text) : Catch : End Try
                    End Try
                    SetStatus("Copied to clipboard")
                End If
            End If
        Catch
        End Try
    End Sub

    Private Sub ShowEmptyState()
        If webChat.CoreWebView2 Is Nothing Then Return
        Dim enc = Function(s As String) System.Net.WebUtility.HtmlEncode(Localizer.T(s))
        Dim html As String
        If cmbModels.Items.Count = 0 Then
            If Settings.Backend = BackendKind.BuiltIn Then
                ' First start: three plain steps.
                Dim stepHtml = Function(n As Integer, t As String, d As String) $"<div class='step'><span class='n'>{n}</span><span class='t'>{enc(t)}</span><div class='d'>{enc(d)}</div></div>"
                html = "<div class='logo'>✦</div><h1>" & enc("Welcome to LocalMind") & "</h1>" &
                       "<div class='sub'>" & enc("Your private AI. Everything runs on this PC — nothing is sent to the internet.") & "</div>" &
                       "<div class='steps'>" &
                       stepHtml(1, "Get a model", "Click ⬇ Get models at the top, press ★ Recommend for my PC, then Download.") &
                       stepHtml(2, "Choose it", "When the download finishes, pick the model in the Model box at the top.") &
                       stepHtml(3, "Start chatting", "Type your message in the box below and press Ctrl+Enter.") &
                       "</div>"
            Else
                html = "<b>" & enc("No models found") & "</b>" & enc("Check the connection in Tools → Settings.")
            End If
        Else
            ' Ready: a few ideas to start with (click one to put it in the message box).
            Dim card = Function(t As String, d As String, p As String) $"<div class='card' data-p='{System.Net.WebUtility.HtmlEncode(Localizer.T(p))}' onclick='usePrompt(this)'><div class='t'>{enc(t)}</div><div class='d'>{enc(d)}</div></div>"
            Dim cards As New Text.StringBuilder()
            cards.Append(card("✍️ Write", "an email, a post, a story…", "Write a friendly email to "))
            cards.Append(card("💡 Explain", "anything, simply", "Explain in simple words: "))
            cards.Append(card("📄 Summarize", "a document — attach it first", "Summarize the attached document in 5 bullet points."))
            If Settings.EnableImages Then
                cards.Append(card("🖼 Make a picture", "just describe it", "Generate an image of "))
                cards.Append(card("🎬 Make a video", "a short clip from words or a photo", "Generate a video of "))
            End If
            If Settings.EnableAudio Then cards.Append(card("🎵 Make a song", "lyrics and music", "Make a song about "))
            html = "<div class='logo'>✦</div><h1>" & enc("How can I help?") & "</h1>" &
                   "<div class='sub'>" & enc("Model") & ": " & System.Net.WebUtility.HtmlEncode(If(cmbModels.SelectedItem?.ToString(), "")) & "</div>" &
                   "<div class='cards'>" & cards.ToString() & "</div>" &
                   "<div class='hint'>" & enc("Ctrl+Enter sends · paste or drop pictures and files to attach them") & "</div>"
        End If
        Js($"showEmpty({JsonConvert.SerializeObject(html)});")
    End Sub

    '════════════════════════════════════════════════════════════
    '  Sending / streaming
    '════════════════════════════════════════════════════════════
    Private Async Sub btnSend_Click(sender As Object, e As EventArgs) Handles btnSend.Click
        Await SendCurrentInputAsync()
    End Sub

    Private Async Function SendCurrentInputAsync() As Task
        If _isGenerating Then Return
        If cmbModels.SelectedIndex < 0 Then
            If Settings.Backend = BackendKind.BuiltIn Then
                If Localizer.Box(Me, "No model is downloaded yet. Open the Model Manager now?", "No model",
                                   MessageBoxButtons.YesNo, MessageBoxIcon.Question) = DialogResult.Yes Then OpenModelManager()
            Else
                Localizer.Box(Me, "Select a model first. Check that the server in Tools → Settings is reachable.")
            End If
            Return
        End If

        Dim text = txtInput.Text.Trim()
        If text.Length = 0 AndAlso _pendingAttachments.Count = 0 Then Return

        Dim userMsg As New ChatMessage With {.Role = "user", .Content = text}
        For Each a In _pendingAttachments
            If a.IsImage Then
                userMsg.Images.Add(a.Base64)
            ElseIf a.IsAudio Then
                userMsg.AudioFiles.Add(a.Path)
                userMsg.Content &= vbCrLf & vbCrLf & "[Attached voice recording: " & Path.GetFileName(a.Path).Substring(23) & "]"
            Else
                userMsg.Content &= vbCrLf & vbCrLf &
                    "[Attached document: " & Path.GetFileName(a.Path) & "]" & vbCrLf &
                    DocumentService.Trim(a.Text) & vbCrLf & "[End of document]"
            End If
        Next

        _session.Messages.Add(userMsg)
        If _session.Messages.Count = 1 Then
            _session.Title = If(text.Length > 0, Truncate(text, 40), "File chat")
        End If
        _session.Model = cmbModels.SelectedItem?.ToString()
        _session.PersonaName = If(CurrentPersona IsNot Nothing, CurrentPersona.Name, "")
        _session.UpdatedAt = DateTime.Now

        Await Js($"appendUser({JsonConvert.SerializeObject(DisplayText(userMsg.Content))}, {ImagesJson(userMsg)})")

        txtInput.Clear()
        ClearAttachments()
        If _btnImage IsNot Nothing AndAlso _btnImage.Checked Then
            Await GenerateImageAsync(text, userMsg)
        Else
            Await GenerateAsync()
        End If
    End Function

    Private Async Function GenerateAsync() As Task
        If _isGenerating Then Return

        _isGenerating = True
        btnSend.Enabled = False
        btnStop.Enabled = True
        btnRegenerate.Enabled = False
        _cts = New CancellationTokenSource()

        SyncLock _liveTextLock
            _liveAssistantText.Clear()
        End SyncLock
        SyncLock _liveThinkingLock
            _liveThinkingText.Clear()
        End SyncLock
        _lastRenderedLength = 0
        _lastRenderedThinkingLength = 0
        _firstContentSeen = False
        _streamTokens = 0
        _streamStart = DateTime.Now
        lblTokens.Text = "0 tok/s"

        _streamMsgId = "msg-" & Guid.NewGuid().ToString("N").Substring(0, 8)
        Await Js($"appendAssistantStart('{_streamMsgId}')")
        SetStatus("Thinking…")
        _flushTimer.Start()

        Dim persona = CurrentPersona
        Dim payload = AppCore.BuildPayload(_session, persona)
        Dim opts As New ChatRequestOptions With {
            .Model = _session.Model,
            .Temperature = If(persona IsNot Nothing, persona.Temperature, Settings.DefaultTemperature),
            .TopP = Settings.TopP,
            .MaxTokens = Settings.MaxTokens,
            .EnableThinking = btnThinking.Checked
        }

        Dim result As ChatResult = Nothing
        Dim assistantText As String = ""
        Dim sw = Stopwatch.StartNew()
        Dim backend = AppCore.Backend
        Dim ct = _cts.Token
        Dim sources As List(Of SourceRef) = Nothing

        Try
            If _btnDocs.Checked OrElse _btnWeb.Checked Then
                Dim msgId = _streamMsgId
                sources = Await AppCore.AugmentAsync(payload, _btnDocs.Checked, _btnWeb.Checked,
                    Sub(t)
                        SetStatus(t)
                        Js($"setStatusText('{msgId}', {JsonConvert.SerializeObject(t)});")
                    End Sub, ct)
            End If
            ' Run off the UI thread: token callbacks only append to buffers; the timer renders.
            Dim streamOnce = Function(o As ChatRequestOptions) Task.Run(Function() backend.StreamChatAsync(payload, o,
                Sub(token)
                    SyncLock _liveThinkingLock
                        _liveThinkingText.Append(token)
                    End SyncLock
                End Sub,
                Sub(token)
                    SyncLock _liveTextLock
                        _liveAssistantText.Append(token)
                    End SyncLock
                    Interlocked.Increment(_streamTokens)
                End Sub, ct))
            ' The model may use tools by itself (draw, make a song, search the web) when the user asks for it.
            Dim answer = Await ChatAgent.AnswerAsync(streamOnce, payload, opts, Not _btnWeb.Checked, AddressOf ToolStatus,
                                                     Sub()
                                                         SyncLock _liveTextLock
                                                             _liveAssistantText.Clear()
                                                         End SyncLock
                                                     End Sub, ct)
            result = answer.Result
            If answer.Sources IsNot Nothing Then sources = answer.Sources
            assistantText = result.Content & answer.Markdown & AppCore.SourcesMarkdown(sources)
        Catch oce As OperationCanceledException
            SyncLock _liveTextLock
                assistantText = _liveAssistantText.ToString()
            End SyncLock
            assistantText &= vbCrLf & vbCrLf & "_[stopped]_"
        Catch ex As Exception
            assistantText = "⚠ Error: " & ex.Message
        Finally
            sw.Stop()
            _flushTimer.Stop()
            _isGenerating = False
            btnSend.Enabled = True
            btnStop.Enabled = False
            btnRegenerate.Enabled = True
        End Try

        Dim thinkingText As String
        If result IsNot Nothing Then
            thinkingText = result.Thinking
        Else
            SyncLock _liveThinkingLock
                thinkingText = _liveThinkingText.ToString()
            End SyncLock
        End If

        Dim tokens = If(result IsNot Nothing AndAlso result.Tokens > 0, result.Tokens, _streamTokens)
        Dim tps = If(result IsNot Nothing AndAlso result.EngineTokensPerSecond > 0, result.EngineTokensPerSecond,
                     If(sw.Elapsed.TotalSeconds > 0, tokens / sw.Elapsed.TotalSeconds, 0))

        Dim msg As New ChatMessage With {
            .Role = "assistant", .Content = assistantText, .Thinking = thinkingText,
            .Model = _session.Model, .TokensPerSecond = Math.Round(tps, 1)}

        Await Js(
            $"finalizeAssistant('{_streamMsgId}', {JsonConvert.SerializeObject(MarkdownRenderer.ToHtml(assistantText))}, " &
            $"{JsonConvert.SerializeObject(If(String.IsNullOrEmpty(thinkingText), "", MarkdownRenderer.ToHtml(thinkingText)))}, " &
            $"{JsonConvert.SerializeObject(MetaText(msg))});")

        _session.Messages.Add(msg)
        _session.UpdatedAt = DateTime.Now
        AppCore.History.Save(_session)
        RefreshChatList()

        lblTokens.Text = $"{tps:0.0} tok/s  ({tokens} tokens, {sw.Elapsed.TotalSeconds:0.0}s)"
        SetStatus("Done")
        If Settings.AutoReadAnswers AndAlso result IsNot Nothing AndAlso Not Voice.IsSpeaking AndAlso Not _inVoiceChat Then SpeakAnswer(assistantText)
    End Function

    Private Sub FlushLiveRender(sender As Object, e As EventArgs)
        Dim contentSnapshot As String
        Dim thinkingSnapshot As String
        SyncLock _liveTextLock
            contentSnapshot = _liveAssistantText.ToString()
        End SyncLock
        SyncLock _liveThinkingLock
            thinkingSnapshot = _liveThinkingText.ToString()
        End SyncLock

        Dim secs = (DateTime.Now - _streamStart).TotalSeconds
        If secs > 0.15 AndAlso _streamTokens > 0 Then
            lblTokens.Text = $"{_streamTokens / secs:0.0} tok/s  ({_streamTokens} tokens)"
        End If

        ' Re-rendering markdown is O(length): render less often as the answer grows.
        Dim minInterval = Math.Min(600, 60 + Math.Max(contentSnapshot.Length, thinkingSnapshot.Length) \ 80)
        If (DateTime.Now - _lastRenderedAt).TotalMilliseconds < minInterval Then Return

        Dim contentChanged = contentSnapshot.Length > _lastRenderedLength
        Dim thinkingChanged = thinkingSnapshot.Length > _lastRenderedThinkingLength
        If Not contentChanged AndAlso Not thinkingChanged Then Return
        _lastRenderedAt = DateTime.Now

        If thinkingChanged Then
            _lastRenderedThinkingLength = thinkingSnapshot.Length
            Dim tHtml = MarkdownRenderer.ToHtml(MarkdownRenderer.BalanceFences(thinkingSnapshot))
            Js($"setThinkingHtml('{_streamMsgId}', {JsonConvert.SerializeObject(tHtml)});")
            SetStatus("Thinking…")
        End If

        If contentChanged Then
            _lastRenderedLength = contentSnapshot.Length
            Dim html = MarkdownRenderer.ToHtml(MarkdownRenderer.BalanceFences(contentSnapshot))
            Js($"setAssistantHtml('{_streamMsgId}', {JsonConvert.SerializeObject(html)});")
            If Not _firstContentSeen Then
                _firstContentSeen = True
                Js($"collapseThinking('{_streamMsgId}');")
                SetStatus("Writing…")
            End If
        End If
    End Sub

    Private Sub btnStop_Click(sender As Object, e As EventArgs) Handles btnStop.Click
        Try : _cts?.Cancel() : Catch : End Try
        SetStatus("Stopped")
    End Sub

    Private Async Sub btnRegenerate_Click(sender As Object, e As EventArgs) Handles btnRegenerate.Click
        If _isGenerating Then Return
        While _session.Messages.Count > 0 AndAlso _session.Messages.Last().Role = "assistant"
            _session.Messages.RemoveAt(_session.Messages.Count - 1)
        End While
        If _session.Messages.Count = 0 Then Return
        _session.Model = If(cmbModels.SelectedItem?.ToString(), _session.Model)
        Await RebuildChatDisplayAsync()
        Await GenerateAsync()
    End Sub

    ''' <summary>Rebuilds the whole conversation with a single script call.</summary>
    Private Async Function RebuildChatDisplayAsync() As Task
        If _session.Messages.Count = 0 Then
            ShowEmptyState()
            Return
        End If
        Dim items As New JArray()
        For Each m In _session.Messages
            If m.Role = "user" Then
                Dim o As New JObject From {{"role", "user"}, {"text", DisplayText(m.Content)}}
                If m.Images IsNot Nothing AndAlso m.Images.Count > 0 Then
                    o("imgs") = New JArray(m.Images.Select(Function(i) ImageUtil.ToDataUrl(i)).ToArray())
                End If
                items.Add(o)
            ElseIf m.Role = "assistant" Then
                items.Add(New JObject From {
                    {"role", "assistant"},
                    {"html", MarkdownRenderer.ToHtml(m.Content)},
                    {"thinkingHtml", If(String.IsNullOrEmpty(m.Thinking), "", MarkdownRenderer.ToHtml(m.Thinking))},
                    {"meta", MetaText(m)}})
            End If
        Next
        Await Js("loadAll(" & items.ToString(Formatting.None) & ");")
    End Function

    Private Shared Function MetaText(m As ChatMessage) As String
        Dim parts As New List(Of String)
        If Not String.IsNullOrEmpty(m.Model) Then parts.Add(m.Model)
        If m.TokensPerSecond > 0 Then parts.Add($"{m.TokensPerSecond:0.0} tok/s")
        Return String.Join("  ·  ", parts)
    End Function

    Private Shared ReadOnly DocBlock As New Regex("\[Attached document: (.+?)\][\s\S]*?\[End of document\]", RegexOptions.Compiled)

    ''' <summary>Shows "📎 file.pdf" instead of the full document text inside the user bubble.</summary>
    Private Shared Function DisplayText(content As String) As String
        If String.IsNullOrEmpty(content) Then Return ""
        Return DocBlock.Replace(content, "📎 $1").Trim()
    End Function

    Private Shared Function ImagesJson(m As ChatMessage) As String
        If m.Images Is Nothing OrElse m.Images.Count = 0 Then Return "null"
        Return JsonConvert.SerializeObject(m.Images.Select(Function(i) ImageUtil.ToDataUrl(i)).ToList())
    End Function

    Private Sub btnClear_Click(sender As Object, e As EventArgs) Handles btnClear.Click
        txtInput.Clear()
        ClearAttachments()
    End Sub

    '════════════════════════════════════════════════════════════
    '  Input: keys, paste, drag & drop
    '════════════════════════════════════════════════════════════
    Private Async Sub txtInput_KeyDown(sender As Object, e As KeyEventArgs) Handles txtInput.KeyDown
        If e.Control AndAlso e.KeyCode = Keys.Enter Then
            e.SuppressKeyPress = True
            Await SendCurrentInputAsync()
        ElseIf e.KeyCode = Keys.Escape AndAlso _isGenerating Then
            e.SuppressKeyPress = True
            btnStop.PerformClick()
        ElseIf (e.Control AndAlso e.KeyCode = Keys.V) OrElse (e.Shift AndAlso e.KeyCode = Keys.Insert) Then
            ' Paste images/files as attachments instead of embedding them in the text box.
            Try
                If Clipboard.ContainsImage() Then
                    e.SuppressKeyPress = True
                    Using img = Clipboard.GetImage()
                        AddAttachment(New AttachmentItem With {
                            .Path = $"pasted-{DateTime.Now:HHmmss}.png", .IsImage = True,
                            .Base64 = DocumentService.ImageToBase64(img)})
                    End Using
                    SetStatus("Image pasted as attachment")
                ElseIf Clipboard.ContainsFileDropList() Then
                    e.SuppressKeyPress = True
                    AttachFiles(Clipboard.GetFileDropList().Cast(Of String)().ToArray())
                ElseIf Clipboard.ContainsText() Then
                    ' Plain-text paste (drop formatting from web pages / Word).
                    e.SuppressKeyPress = True
                    txtInput.SelectedText = Clipboard.GetText()
                End If
            Catch
            End Try
        End If
    End Sub

    Private Sub Files_DragEnter(sender As Object, e As DragEventArgs)
        If e.Data.GetDataPresent(DataFormats.FileDrop) Then e.Effect = DragDropEffects.Copy
    End Sub

    Private Sub Files_DragDrop(sender As Object, e As DragEventArgs)
        Dim files = TryCast(e.Data.GetData(DataFormats.FileDrop), String())
        If files Is Nothing OrElse files.Length = 0 Then Return
        ' Dropping .gguf files imports models instead of attaching them.
        Dim ggufs = files.Where(Function(f) f.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase)).ToArray()
        If ggufs.Length > 0 Then
            If Localizer.Box(Me, $"Add {ggufs.Length} GGUF model file(s) to your models folder (copy)?", "Import models",
                               MessageBoxButtons.YesNo, MessageBoxIcon.Question) = DialogResult.Yes Then
                Dim target = Path.Combine(AppCore.Library.Folder, "imported")
                Directory.CreateDirectory(target)
                For Each f In ggufs
                    Try
                        File.Copy(f, Path.Combine(target, Path.GetFileName(f)), True)
                    Catch ex As Exception
                        Localizer.Box(Me, ex.Message, "Import", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                    End Try
                Next
                AppCore.Library.Refresh()
            End If
            files = files.Except(ggufs).ToArray()
        End If
        If files.Length > 0 Then AttachFiles(files)
    End Sub

    '════════════════════════════════════════════════════════════
    '  Chat list & search
    '════════════════════════════════════════════════════════════
    Private Sub RefreshChatList()
        Dim selectedId = If(_session?.Id, "")
        lstChats.BeginUpdate()
        lstChats.Items.Clear()
        Dim folder = CurrentFolderFilter()
        For Each s In AppCore.History.ListAll()
            If folder IsNot Nothing AndAlso Not String.Equals(If(s.Folder, ""), folder, StringComparison.OrdinalIgnoreCase) Then Continue For
            Dim i = lstChats.Items.Add(New ChatListItem(s))
            ' Re-selecting the open chat is a no-op in lstChats_SelectedIndexChanged.
            If s.Id = selectedId Then lstChats.SelectedIndex = i
        Next
        lstChats.EndUpdate()
    End Sub

    Private Async Sub lstChats_SelectedIndexChanged(sender As Object, e As EventArgs) Handles lstChats.SelectedIndexChanged
        Dim item = TryCast(lstChats.SelectedItem, ChatListItem)
        If item Is Nothing OrElse _isGenerating Then Return
        If _session IsNot Nothing AndAlso item.Session.Id = _session.Id Then Return
        _session = AppCore.History.Load(item.Session.Id)
        If _session Is Nothing Then _session = item.Session
        ' Switch to the model this chat used, if it is available.
        If Not String.IsNullOrEmpty(_session.Model) AndAlso cmbModels.Items.Contains(_session.Model) Then
            cmbModels.SelectedItem = _session.Model
        End If
        Await RebuildChatDisplayAsync()
    End Sub

    Private Sub txtSearch_TextChanged(sender As Object, e As EventArgs) Handles txtSearch.TextChanged
        If Not _webReady Then Return
        Dim q = txtSearch.Text.Trim()
        If String.IsNullOrEmpty(q) OrElse q = SearchPlaceholder Then
            RefreshChatList()
            Return
        End If
        lstChats.BeginUpdate()
        lstChats.Items.Clear()
        For Each hit In AppCore.History.Search(q)
            lstChats.Items.Add(New ChatListItem(hit.Item1, hit.Item2))
        Next
        lstChats.EndUpdate()
    End Sub

    Private Sub txtSearch_GotFocus(sender As Object, e As EventArgs) Handles txtSearch.GotFocus
        ' (the box shows its placeholder by itself)
    End Sub

    Private Sub txtSearch_LostFocus(sender As Object, e As EventArgs) Handles txtSearch.LostFocus
        ' (the box shows its placeholder by itself)
    End Sub

    '════════════════════════════════════════════════════════════
    '  New / Save / Export / Import
    '════════════════════════════════════════════════════════════
    Private Sub btnNewChat_Click(sender As Object, e As EventArgs) Handles btnNewChat.Click, miNewChat.Click
        If _isGenerating Then Return
        AppCore.History.Save(_session)
        _session = New ChatSession() With {.Folder = CurrentFolderFilter()}
        txtInput.Clear()
        ClearAttachments()
        RefreshChatList()
        ShowEmptyState()
        txtInput.Focus()
        SetStatus("New chat")
    End Sub

    Private Sub miSave_Click(sender As Object, e As EventArgs) Handles miSave.Click
        If _session.Messages.Count = 0 Then Return
        AppCore.History.Save(_session)
        RefreshChatList()
        SetStatus("Saved")
    End Sub

    Private Sub miExport_Click(sender As Object, e As EventArgs) Handles miExport.Click
        If _session Is Nothing OrElse _session.Messages.Count = 0 Then
            Localizer.Box("Nothing to export yet — send a message first.")
            Return
        End If
        Using dlg As New SaveFileDialog()
            dlg.Title = "Export chat"
            dlg.Filter = "Markdown chat (*.md)|*.md|All files (*.*)|*.*"
            dlg.DefaultExt = "md"
            dlg.AddExtension = True
            dlg.FileName = SafeFileName(If(_session.Title, "chat")) & ".md"
            If dlg.ShowDialog() <> DialogResult.OK Then Return
            Try
                ChatFileIO.ExportToFile(_session, dlg.FileName)
                SetStatus("Exported → " & Path.GetFileName(dlg.FileName))
            Catch ex As Exception
                Localizer.Box("Export failed:" & vbCrLf & ex.Message, "Export", MessageBoxButtons.OK, MessageBoxIcon.Error)
            End Try
        End Using
    End Sub

    Private Async Sub miImport_Click(sender As Object, e As EventArgs) Handles miImport.Click
        If _isGenerating Then
            Localizer.Box("Wait for the current response to finish or press Stop first.")
            Return
        End If
        Using dlg As New OpenFileDialog()
            dlg.Title = "Import chat"
            dlg.Filter = "Markdown chat (*.md)|*.md|Markdown (*.markdown)|*.markdown|Text (*.txt)|*.txt|All files (*.*)|*.*"
            If dlg.ShowDialog() <> DialogResult.OK Then Return
            Try
                If _session IsNot Nothing AndAlso _session.Messages.Count > 0 Then AppCore.History.Save(_session)
                Dim imported = ChatFileIO.ImportFromFile(dlg.FileName)
                If imported.Messages.Count = 0 Then
                    Localizer.Box("The file did not contain any messages.", "Import", MessageBoxButtons.OK, MessageBoxIcon.Information)
                    Return
                End If
                AppCore.History.Save(imported)
                _session = imported
                RefreshChatList()
                Await RebuildChatDisplayAsync()
                SetStatus($"Imported {imported.Messages.Count} messages from {Path.GetFileName(dlg.FileName)}")
            Catch ex As Exception
                Localizer.Box("Import failed:" & vbCrLf & ex.Message, "Import", MessageBoxButtons.OK, MessageBoxIcon.Error)
            End Try
        End Using
    End Sub

    '════════════════════════════════════════════════════════════
    '  Delete / rename chat, clear history
    '════════════════════════════════════════════════════════════
    Private Sub BuildChatContextMenu()
        _chatContextMenu = New ContextMenuStrip() With {.BackColor = Color.FromArgb(45, 45, 48), .ForeColor = Color.FromArgb(220, 220, 220)}
        _miRenameChat = New ToolStripMenuItem("Rename…")
        AddHandler _miRenameChat.Click, AddressOf RenameSelectedChat
        _miDeleteChat = New ToolStripMenuItem("Delete this chat")
        AddHandler _miDeleteChat.Click, AddressOf DeleteSelectedChat
        Dim miClearAll As New ToolStripMenuItem("Clear all history…")
        AddHandler miClearAll.Click, AddressOf ClearAllHistory

        Dim miMove As New ToolStripMenuItem("Move to folder")
        _chatContextMenu.Items.Add(_miRenameChat)
        _chatContextMenu.Items.Add(miMove)
        _chatContextMenu.Items.Add(_miDeleteChat)
        AddHandler _chatContextMenu.Opening, Sub() BuildMoveMenu(miMove)
        _chatContextMenu.Items.Add(New ToolStripSeparator())
        _chatContextMenu.Items.Add(miClearAll)
        lstChats.ContextMenuStrip = _chatContextMenu
        AddHandler _chatContextMenu.Opening, Sub(s, e)
                                                 Dim has = lstChats.SelectedItem IsNot Nothing
                                                 _miDeleteChat.Enabled = has
                                                 _miRenameChat.Enabled = has
                                             End Sub
        ' Right-click selects the item under the mouse.
        AddHandler lstChats.MouseDown, Sub(s, e)
                                           If e.Button = MouseButtons.Right Then
                                               Dim i = lstChats.IndexFromPoint(e.Location)
                                               If i >= 0 Then lstChats.SelectedIndex = i
                                           End If
                                       End Sub
        AddHandler lstChats.KeyDown, Sub(s, e)
                                         If e.KeyCode = Keys.Delete Then DeleteSelectedChat(Nothing, EventArgs.Empty)
                                         If e.KeyCode = Keys.F2 Then RenameSelectedChat(Nothing, EventArgs.Empty)
                                     End Sub
    End Sub

    Private Sub BuildFileMenuExtras()
        Dim idx = menuFile.DropDownItems.IndexOf(miExit)
        If idx < 0 Then Return
        Dim miClear As New ToolStripMenuItem("Clear All History…")
        AddHandler miClear.Click, AddressOf ClearAllHistory
        menuFile.DropDownItems.Insert(idx, New ToolStripSeparator())
        menuFile.DropDownItems.Insert(idx + 1, miClear)
    End Sub

    Private Sub RenameSelectedChat(sender As Object, e As EventArgs)
        Dim item = TryCast(lstChats.SelectedItem, ChatListItem)
        If item Is Nothing Then Return
        Dim name = Microsoft.VisualBasic.Interaction.InputBox("New name for this chat:", "Rename chat", item.Session.Title)
        If String.IsNullOrWhiteSpace(name) Then Return
        item.Session.Title = name.Trim()
        AppCore.History.Save(item.Session)
        RefreshChatList()
    End Sub

    Private Sub DeleteSelectedChat(sender As Object, e As EventArgs)
        Dim item = TryCast(lstChats.SelectedItem, ChatListItem)
        If item Is Nothing Then Return
        If Localizer.Box("Delete this conversation?" & vbCrLf & vbCrLf & item.Session.Title,
                           "Confirm Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) <> DialogResult.Yes Then Return
        AppCore.History.Delete(item.Session.Id)
        If _session IsNot Nothing AndAlso _session.Id = item.Session.Id Then
            _session = New ChatSession()
            txtInput.Clear()
            ClearAttachments()
            ShowEmptyState()
        End If
        RefreshChatList()
        SetStatus("Chat deleted")
    End Sub

    Private Sub ClearAllHistory(sender As Object, e As EventArgs)
        Dim all = AppCore.History.ListAll()
        If all.Count = 0 Then
            Localizer.Box("No saved conversations to delete.")
            Return
        End If
        If Localizer.Box($"Delete ALL {all.Count} saved conversations?" & vbCrLf & vbCrLf & "This cannot be undone.",
                           "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) <> DialogResult.Yes Then Return
        For Each s In all
            AppCore.History.Delete(s.Id)
        Next
        _session = New ChatSession()
        txtInput.Clear()
        ClearAttachments()
        RefreshChatList()
        ShowEmptyState()
        SetStatus($"Deleted {all.Count} chats")
    End Sub

    '════════════════════════════════════════════════════════════
    '  Attachments
    '════════════════════════════════════════════════════════════
    Private Sub btnAttachDoc_Click(sender As Object, e As EventArgs) Handles btnAttachDoc.Click
        Using dlg As New OpenFileDialog()
            dlg.Filter = "Documents|*.txt;*.md;*.pdf;*.csv;*.json;*.xml;*.log;*.vb;*.cs;*.py;*.js|Sound (voice to copy)|*.mp3;*.wav;*.m4a;*.ogg;*.opus;*.flac;*.aac;*.wma|All files|*.*"
            dlg.Multiselect = True
            If dlg.ShowDialog() <> DialogResult.OK Then Return
            AttachFiles(dlg.FileNames)
        End Using
    End Sub

    Private Sub btnAttachImage_Click(sender As Object, e As EventArgs) Handles btnAttachImage.Click
        Using dlg As New OpenFileDialog()
            dlg.Filter = "Images|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp"
            dlg.Multiselect = True
            If dlg.ShowDialog() <> DialogResult.OK Then Return
            AttachFiles(dlg.FileNames)
        End Using
    End Sub

    Private Sub btnAttachAny_Click(sender As Object, e As EventArgs) Handles btnAttachAny.Click
        Using dlg As New OpenFileDialog()
            dlg.Title = "Attach any file"
            dlg.Filter = "All files (*.*)|*.*"
            dlg.Multiselect = True
            If dlg.ShowDialog() <> DialogResult.OK Then Return
            AttachFiles(dlg.FileNames)
        End Using
    End Sub

    Private Sub AttachFiles(paths As String())
        Dim added = 0
        Dim skipped As New List(Of String)
        For Each f In paths
            Try
                If Directory.Exists(f) Then Continue For
                If ChatAgent.IsAudioFile(f) Then
                    ' a voice recording (to copy the voice), kept with the conversation
                    AddAttachment(New AttachmentItem With {.Path = ChatAgent.KeepChatAudio(f), .IsAudio = True})
                    added += 1
                    Continue For
                End If
                Dim content = DocumentService.ExtractAnyFile(f)
                Select Case content.Kind
                    Case ContentKind.Image
                        AddAttachment(New AttachmentItem With {.Path = f, .IsImage = True, .Base64 = content.Base64})
                    Case ContentKind.Text
                        AddAttachment(New AttachmentItem With {.Path = f, .IsImage = False, .Text = content.Text})
                    Case ContentKind.Binary
                        AddAttachment(New AttachmentItem With {.Path = f, .IsImage = False, .Text = content.Description})
                End Select
                added += 1
            Catch ex As Exception
                skipped.Add(Path.GetFileName(f) & " (" & ex.Message & ")")
            End Try
        Next
        If added > 0 Then SetStatus($"Attached {added} file(s)")
        If skipped.Count > 0 Then
            Localizer.Box("Could not attach:" & vbCrLf & String.Join(vbCrLf, skipped), "Attach", MessageBoxButtons.OK, MessageBoxIcon.Warning)
        End If
        Dim selected = AppCore.Library.FindByName(cmbModels.SelectedItem?.ToString())
        If Settings.Backend = BackendKind.BuiltIn AndAlso selected IsNot Nothing AndAlso Not selected.IsVision AndAlso
           _pendingAttachments.Any(Function(a) a.IsImage) Then
            SetStatus("Note: the selected model has no vision support — pick a 👁 model to read images")
        End If
    End Sub

    Private Sub AddAttachment(item As AttachmentItem)
        Dim chip As New Label() With {
            .Text = "  " & If(item.IsImage, "🖼 ", If(item.IsAudio, "🎙 ", "📄 ")) & If(item.IsAudio, Path.GetFileName(item.Path).Substring(23), Path.GetFileName(item.Path)) & "  ✕  ",
            .AutoSize = True, .Padding = New Padding(6, 4, 6, 4), .Margin = New Padding(2),
            .BackColor = Theme.CControl, .ForeColor = Theme.CText,
            .Font = New Font("Segoe UI", 8.5!), .Cursor = Cursors.Hand
        }
        AddHandler chip.Click, Sub()
                                   flpAttachments.Controls.Remove(chip)
                                   _pendingAttachments.Remove(item)
                                   If _pendingAttachments.Count = 0 Then flpAttachments.Height = 0
                               End Sub
        item.Chip = chip
        _pendingAttachments.Add(item)
        flpAttachments.Controls.Add(chip)
        flpAttachments.Height = 34
    End Sub

    Private Sub ClearAttachments()
        _pendingAttachments.Clear()
        flpAttachments.Controls.Clear()
        flpAttachments.Height = 0
    End Sub

    '════════════════════════════════════════════════════════════
    '  Menus: models, settings, LAN
    '════════════════════════════════════════════════════════════
    Private Sub OpenModelManager()
        Using f As New ModelManagerForm()
            If f.ShowDialog(Me) = DialogResult.OK AndAlso Not String.IsNullOrEmpty(f.ModelToUse) Then
                Settings.LastModel = f.ModelToUse
                AppCore.SaveSettings()
            End If
        End Using
        _modelsDirty = True
    End Sub

    Private Sub miModelManager_Click(sender As Object, e As EventArgs) Handles miModelManager.Click, btnGetModels.Click
        If Settings.Backend <> BackendKind.BuiltIn Then
            If Localizer.Box(Me, "Downloaded models are used by the built-in engine, but you are using " &
                               AppCore.Backend.DisplayName & "." & vbCrLf & vbCrLf & "Switch to the built-in engine?",
                               "Model Manager", MessageBoxButtons.YesNo, MessageBoxIcon.Question) = DialogResult.Yes Then
                Settings.Backend = BackendKind.BuiltIn
                AppCore.SaveSettings()
                AppCore.ResetBackend()
                PrepareEngineInBackground()
            End If
        End If
        OpenModelManager()
    End Sub

    Private Sub miOpenModels_Click(sender As Object, e As EventArgs) Handles miOpenModels.Click
        ModelManagerForm.OpenFolder(AppCore.Library.Folder)
    End Sub

    Private Async Sub miUnload_Click(sender As Object, e As EventArgs) Handles miUnload.Click
        Await AppCore.Engine.StopAsync()
        SetStatus("Model unloaded — VRAM freed")
    End Sub

    Private Async Sub miSettings_Click(sender As Object, e As EventArgs) Handles miSettings.Click
        Using f As New SettingsForm()
            If f.ShowDialog(Me) <> DialogResult.OK Then Return
        End Using
        RegisterQuickAskHotkey()
        ApplyFeatures()
        _isFirstConnectionCheck = True
        Await RefreshModelsKeepSelectionAsync()
        Await CheckConnectionNowAsync()
        PrepareEngineInBackground()
    End Sub

    Private Sub OpenCompare()
        Dim models = cmbModels.Items.Cast(Of Object)().Select(Function(o) o.ToString()).ToList()
        If models.Count < 2 Then
            Localizer.Box(Me, "Download at least two models to compare them.", "Compare models")
            Return
        End If
        Dim f As New CompareForm(models)
        f.Show(Me)
    End Sub

    Private Sub miEngineLog_Click(sender As Object, e As EventArgs) Handles miEngineLog.Click
        Dim f As New EngineLogForm()
        f.Show(Me)
    End Sub

    Private Sub miLan_Click(sender As Object, e As EventArgs) Handles miLan.Click, btnLan.Click
        Using f As New LanForm()
            f.ShowDialog(Me)
        End Using
    End Sub

    Private Async Function StartLanAsync() As Task
        Try
            If AppCore.Lan Is Nothing Then AppCore.Lan = New LanServer()
            Await AppCore.Lan.StartAsync(Settings.LanPort)
            SetStatus("Sharing on network: " & String.Join("  ", LanServer.LocalUrls(Settings.LanPort).Take(1)))
        Catch ex As Exception
            SetStatus("LAN sharing could not start: " & ex.Message)
        End Try
    End Function

    '════════════════════════════════════════════════════════════
    '  Misc
    '════════════════════════════════════════════════════════════
    Private Sub miExit_Click(sender As Object, e As EventArgs) Handles miExit.Click
        Close()
    End Sub

    Private Sub miTips_Click(sender As Object, e As EventArgs) Handles miTips.Click
        Localizer.Box(Me,
            "Ctrl+Enter  send" & vbCrLf &
            "Esc  stop the answer" & vbCrLf &
            "Ctrl+V  paste text, screenshots or copied files" & vbCrLf &
            "Drag files onto the window to attach them (.gguf files are imported as models)" & vbCrLf &
            "Ctrl+M  Model Manager     Ctrl+,  Settings" & vbCrLf &
            "F2 / Del on a chat  rename / delete" & vbCrLf & vbCrLf &
            "Speed tips:" & vbCrLf &
            "• Green models in the Model Manager run fully on the GPU (fastest)." & vbCrLf &
            "• MoE models (⚡) stay fast even when bigger than your VRAM." & vbCrLf &
            "• Settings → KV cache q8_0 lets you use a longer context with less VRAM." & vbCrLf &
            "• Tools → Share on network lets your phone use this PC's GPU.",
            "Tips & shortcuts", MessageBoxButtons.OK, MessageBoxIcon.Information)
    End Sub

    Private Sub miAbout_Click(sender As Object, e As EventArgs) Handles miAbout.Click
        Dim engine = AppCore.Engine.InstalledVersion()
        Localizer.Box("LocalMind " & Application.ProductVersion.Split("+"c)(0) & vbCrLf &
                        "Private AI for Windows: chat, pictures, video, music and voice, all on your own PC." & vbCrLf & vbCrLf &
                        "Backend: " & AppCore.Backend.DisplayName & vbCrLf &
                        "Engine: " & If(engine, "not installed") & vbCrLf &
                        HardwareInfo.Current().Summary,
                        "About", MessageBoxButtons.OK, MessageBoxIcon.Information)
    End Sub

    Private Sub SetStatus(text As String)
        If IsDisposed Then Return
        If InvokeRequired Then
            Try
                Me.Post(Sub() SetStatus(text))
            Catch
            End Try
            Return
        End If
        lblStatus.Text = Localizer.T(text)
    End Sub

    Private Shared Function FirstLine(s As String) As String
        If s Is Nothing Then Return ""
        Dim i = s.IndexOfAny({ChrW(13), ChrW(10)})
        Return If(i > 0, s.Substring(0, i), s)
    End Function

    Private Shared Function Truncate(s As String, n As Integer) As String
        If s.Length <= n Then Return s
        Return s.Substring(0, n) & "…"
    End Function

    Private Shared Function SafeFileName(s As String) As String
        For Each c In Path.GetInvalidFileNameChars()
            s = s.Replace(c, "_")
        Next
        Return s
    End Function

    Protected Overrides Sub OnFormClosing(e As FormClosingEventArgs)
        If HandleCloseToTray(e) Then Return
        If AppCore.Downloads.ActiveCount > 0 AndAlso e.CloseReason = CloseReason.UserClosing Then
            If Localizer.Box(Me, "Downloads are still running. They will resume next time you open the app." & vbCrLf & vbCrLf &
                               "Close anyway?", "Downloads running", MessageBoxButtons.YesNo, MessageBoxIcon.Question) <> DialogResult.Yes Then
                e.Cancel = True
                Return
            End If
        End If
        Try
            If _session IsNot Nothing AndAlso _session.Messages.Count > 0 Then AppCore.History.Save(_session)
            _cts?.Cancel()
            _flushTimer?.Stop()
            _connTimer?.Stop()
            _statusTimer?.Stop()
        Catch
        End Try
        MyBase.OnFormClosing(e)
    End Sub

    Protected Overrides Sub OnFormClosed(e As FormClosedEventArgs)
        DisposeTray()
        AppCore.Shutdown()
        MyBase.OnFormClosed(e)
    End Sub

    Private Class ChatListItem
        Public Property Session As ChatSession
        Public Property Snippet As String

        Public Sub New(s As ChatSession, Optional snippet As String = Nothing)
            Me.Session = s
            Me.Snippet = snippet
        End Sub

        Public Overrides Function ToString() As String
            If Not String.IsNullOrEmpty(Snippet) Then
                Return "🔍 " & If(Session.Title, "Untitled") & " — " & Snippet
            End If
            Return If(Session.Title, "Untitled") & "  ·  " & Session.UpdatedAt.ToString("MMM d, HH:mm")
        End Function
    End Class
End Class
