Imports System
Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Drawing
Imports System.IO
Imports System.Linq
Imports System.Text.RegularExpressions
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms

''' <summary>
''' Browse hundreds of GGUF models, see at a glance what runs on this PC's VRAM,
''' tick the ones you want and press Download — files, quantization and the vision
''' projector are chosen automatically.
''' </summary>
Public Class ModelManagerForm
    Inherits Form

    Private ReadOnly _hw As HardwareInfo = HardwareInfo.Current()
    Private _entries As New List(Of CatalogEntry)()
    Private _searchResults As List(Of CatalogEntry)
    Private ReadOnly _checked As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
    Private ReadOnly _quantChoice As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
    Private _detailCts As CancellationTokenSource
    Private _filterTimer As System.Windows.Forms.Timer
    Private _dlTimer As System.Windows.Forms.Timer
    Private _suppressGridEvents As Boolean

    ' controls
    Private lblHw As Label
    Private lblInfo As Label
    Private tabs As TabControl
    Private txtFilter As TextBox
    Private cmbCategory As ComboBox
    Private cmbSort As ComboBox
    Private cmbShow As ComboBox
    Private cmbTop As ComboBox
    Private _best As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
    Private _bestUncensored As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
    Private btnRefresh As Button
    Private btnHub As Button
    Private btnRecommend As Button
    Private btnDownload As Button
    Private grid As DataGridView
    Private pnlDetail As Panel
    Private lblDetailTitle As Label
    Private lblDetailInfo As Label
    Private lstQuants As ListView
    Private chkVision As CheckBox
    Private lnkHf As LinkLabel
    Private lstInstalled As ListView
    Private lblInstalledInfo As Label
    Private gridDl As DataGridView

    Private Shared ReadOnly QuantChoices As String() = {"Auto", "Q8_0", "Q6_K", "Q5_K_M", "Q5_K_S", "Q4_K_M", "Q4_K_S", "IQ4_XS", "IQ4_NL",
                                                         "Q4_0", "Q3_K_L", "Q3_K_M", "IQ3_M", "IQ3_XXS", "Q2_K", "IQ2_M", "BF16"}


    Public Sub New()
        Text = "Model Manager"
        Size = New Size(1320, 860)
        MinimumSize = New Size(1000, 640)
        StartPosition = FormStartPosition.CenterParent
        Theme.ApplyForm(Me)
        BuildUi()
    End Sub

    '════════════════════════════════════════════════════════════
    '  Layout
    '════════════════════════════════════════════════════════════
    Private Sub BuildUi()
        ' Header
        Dim header As New Panel() With {.Dock = DockStyle.Top, .Height = 70, .BackColor = Theme.CBack, .Padding = New Padding(16, 10, 16, 8)}
        lblHw = New Label() With {.AutoSize = True, .Font = New Font("Segoe UI Semibold", 12.0!), .ForeColor = Color.White, .Location = New Point(16, 10)}
        lblInfo = New Label() With {.AutoSize = True, .Font = Theme.SmallFont, .ForeColor = Theme.CDim, .Location = New Point(17, 40)}
        header.Controls.AddRange({lblHw, lblInfo})
        AddHandler header.Paint, Sub(s, e)
                                     Using p As New Pen(Theme.CBorder)
                                         e.Graphics.DrawLine(p, 0, header.Height - 1, header.Width, header.Height - 1)
                                     End Using
                                 End Sub

        ' Downloads panel (bottom)
        Dim dlPanel As New Panel() With {.Dock = DockStyle.Bottom, .Height = 180, .Padding = New Padding(10, 6, 10, 8), .BackColor = Theme.CBack}
        Dim dlBar As New FlowLayoutPanel() With {.Dock = DockStyle.Top, .Height = 38, .WrapContents = False}
        Dim lblDl = Theme.MakeLabel("⬇ DOWNLOADS", bold:=True, dim_:=True)
        lblDl.Margin = New Padding(3, 10, 12, 3)
        Dim btnCancel = Theme.MakeButton("Cancel")
        Dim btnCancelDel = Theme.MakeButton("Cancel && delete files")
        Dim btnRetry = Theme.MakeButton("Retry / resume")
        Dim btnClear = Theme.MakeButton("Clear finished")
        Dim btnFolder = Theme.MakeButton("📁 Models folder")
        AddHandler btnCancel.Click, Sub() AppCore.Downloads.Cancel(SelectedJob())
        AddHandler btnCancelDel.Click, Sub() AppCore.Downloads.CancelAndDelete(SelectedJob())
        AddHandler btnRetry.Click, Sub() AppCore.Downloads.Retry(SelectedJob())
        AddHandler btnClear.Click, Sub() AppCore.Downloads.ClearFinished()
        AddHandler btnFolder.Click, Sub() OpenFolder(AppCore.Library.Folder)
        dlBar.Controls.AddRange({lblDl, btnCancel, btnCancelDel, btnRetry, btnClear, btnFolder})

        gridDl = New DataGridView() With {.Dock = DockStyle.Fill, .ReadOnly = True}
        Theme.StyleGrid(gridDl)
        gridDl.Columns.Add("model", "Model")
        gridDl.Columns.Add("progress", "Progress")
        gridDl.Columns.Add("status", "Status")
        gridDl.Columns(0).Width = 360
        gridDl.Columns(1).Width = 220
        gridDl.Columns(2).AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
        AddHandler gridDl.CellPainting, AddressOf PaintProgress
        dlPanel.Controls.Add(gridDl)
        dlPanel.Controls.Add(dlBar)

        ' Tabs
        tabs = New TabControl() With {.Dock = DockStyle.Fill}
        Dim tabCatalog As New TabPage("Get models")
        Dim tabInstalled As New TabPage("Installed")
        tabs.TabPages.AddRange({tabCatalog, tabInstalled})
        If AppCore.Settings.EnableImages Then
            ' picture, picture-editing and video models
            Dim tabMedia As New TabPage(Localizer.T("🎨 Pictures & video"))
            tabMedia.Controls.Add(New MediaModelsPanel())
            tabs.TabPages.Add(tabMedia)
            ' (screenshot checks open this tab directly)
            If Environment.GetEnvironmentVariable("LOCALMIND_MODELS_TAB") = "media" Then tabs.SelectedTab = tabMedia
        End If
        Theme.StyleTabs(tabs)
        AddHandler tabs.SelectedIndexChanged, Sub() If tabs.SelectedIndex = 1 Then RefreshInstalled()

        BuildCatalogTab(tabCatalog)
        BuildInstalledTab(tabInstalled)

        Controls.Add(tabs)
        Controls.Add(dlPanel)
        Controls.Add(header)
    End Sub

    Private Sub BuildCatalogTab(page As TabPage)
        Dim bar As New FlowLayoutPanel() With {.Dock = DockStyle.Top, .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
                                               .WrapContents = True, .Padding = New Padding(4, 4, 4, 2)}
        txtFilter = New TextBox() With {.Width = 260, .PlaceholderText = "Search, e.g. qwen coder or Qwen3.8-27B-Uncensored:iq4_xs", .Margin = New Padding(3, 6, 3, 3)}
        Theme.StyleInput(txtFilter)
        cmbCategory = New ComboBox() With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 170, .Margin = New Padding(3, 6, 3, 3)}
        cmbCategory.Items.AddRange({"All categories", "💬 General chat", "👁 Vision (images)", "🧠 Reasoning / thinking",
                                    "💻 Coding", "⚡ MoE (fast on low VRAM)", "🔓 Uncensored", "Tiny (<4B)", "Small (4–10B)",
                                    "Medium (10–20B)", "Large (20–40B)", "Very large (40–130B)", "Huge (130B+)", "✔ Installed"})
        cmbCategory.SelectedIndex = 0
        Theme.StyleInput(cmbCategory)
        cmbSort = New ComboBox() With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 140, .Margin = New Padding(3, 6, 3, 3)}
        cmbSort.Items.AddRange({"Most popular", "Most liked", "Newest", "Smallest first", "Largest first", "Name"})
        cmbSort.SelectedIndex = 0
        Theme.StyleInput(cmbSort)
        cmbShow = New ComboBox() With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 215, .Margin = New Padding(3, 6, 3, 3)}
        cmbShow.Items.AddRange({"★ Best that fit my GPU", "Everything that runs on this PC", "All models (show everything)"}.Select(Function(x) Localizer.T(x)).ToArray())
        cmbShow.SelectedIndex = 0
        Theme.StyleInput(cmbShow)
        cmbTop = Popular.MakeCombo()
        btnRefresh = Theme.MakeButton("⟳ Update list")
        btnHub = Theme.MakeButton("🔍 Search all of Hugging Face")
        btnRecommend = Theme.MakeButton("★ Recommend for my PC")
        chkFitGpu = New CheckBox() With {.Text = Localizer.T("✔ Only models that fit my GPU"), .AutoSize = True, .Checked = AppCore.Settings.OnlyFitGpu,
                                         .ForeColor = Theme.CText, .Margin = New Padding(10, 9, 3, 3)}
        AddHandler chkFitGpu.CheckedChanged, Sub()
                                                 AppCore.Settings.OnlyFitGpu = chkFitGpu.Checked
                                                 AppCore.SaveSettings()
                                                 ApplyFilter()
                                             End Sub
        bar.Controls.AddRange({cmbShow, cmbTop, txtFilter, cmbCategory, cmbSort, chkFitGpu, btnRecommend, btnHub, btnRefresh})

        _filterTimer = New System.Windows.Forms.Timer() With {.Interval = 250}
        AddHandler _filterTimer.Tick, Sub()
                                          _filterTimer.Stop()
                                          ApplyFilter()
                                      End Sub
        AddHandler txtFilter.TextChanged, Sub()
                                              _filterTimer.Stop()
                                              _filterTimer.Start()
                                          End Sub
        AddHandler txtFilter.KeyDown, Sub(s, e)
                                          If e.KeyCode = Keys.Enter AndAlso e.Control Then
                                              e.SuppressKeyPress = True
                                              btnHub.PerformClick()
                                          End If
                                      End Sub
        AddHandler cmbCategory.SelectedIndexChanged, Sub() ApplyFilter()
        AddHandler cmbSort.SelectedIndexChanged, Sub()
                                                     _sortCol = Nothing      ' the list order replaces a column sort
                                                     ApplyFilter()
                                                 End Sub
        AddHandler cmbShow.SelectedIndexChanged, Sub() ApplyFilter()
        AddHandler cmbTop.SelectedIndexChanged, Sub() ApplyFilter()
        AddHandler btnRefresh.Click, Async Sub() Await RefreshCatalogAsync(force:=True)
        AddHandler btnHub.Click, Async Sub() Await SearchHubAsync()
        AddHandler btnRecommend.Click, Sub() RecommendForMe()

        ' Bottom action bar
        Dim actions As New FlowLayoutPanel() With {.Dock = DockStyle.Bottom, .Height = 44, .WrapContents = False, .Padding = New Padding(4)}
        btnDownload = Theme.MakeButton("⬇  Download selected (0)", primary:=True)
        btnDownload.Font = Theme.BoldFont
        Dim btnNone = Theme.MakeButton("Clear selection")
        Dim hint = Theme.MakeLabel("Tick models and press Download · green = fits in VRAM · amber = uses RAM too", dim_:=True)
        Dim hintTip As New ToolTip()
        hintTip.SetToolTip(hint, Localizer.T("Keep the quant on Auto (the best that fits). Type name:quant to pick one (e.g. :iq4_xs). Ctrl+Enter searches all of Hugging Face."))
        AddHandler btnDownload.Click, Sub() DownloadSelected()
        AddHandler btnNone.Click, Sub()
                                      _checked.Clear()
                                      ApplyFilter()
                                  End Sub
        actions.Controls.AddRange({btnDownload, btnNone, hint})

        ' Grid + details
        Dim split As New SplitContainer() With {.Dock = DockStyle.Fill, .Orientation = Orientation.Vertical, .FixedPanel = FixedPanel.Panel2}

        grid = New DataGridView() With {.Dock = DockStyle.Fill, .EditMode = DataGridViewEditMode.EditOnEnter}
        Theme.StyleGrid(grid)
        grid.Columns.Add(New DataGridViewCheckBoxColumn() With {.Name = "sel", .HeaderText = "", .Width = 32})
        grid.Columns.Add(New DataGridViewTextBoxColumn() With {.Name = "name", .HeaderText = "Model", .ReadOnly = True, .AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, .MinimumWidth = 220})
        grid.Columns.Add(New DataGridViewTextBoxColumn() With {.Name = "params", .HeaderText = "Size", .ReadOnly = True, .Width = 70})
        grid.Columns.Add(New DataGridViewTextBoxColumn() With {.Name = "badges", .HeaderText = "Abilities", .ReadOnly = True, .Width = 230})
        grid.Columns.Add(New DataGridViewComboBoxColumn() With {.Name = "quant", .HeaderText = "Quant", .Width = 95, .FlatStyle = FlatStyle.Flat, .DisplayStyle = DataGridViewComboBoxDisplayStyle.Nothing, .ToolTipText = "Click to choose the file size (quant). Auto picks the best one for your PC."})
        grid.Columns.Add(New DataGridViewTextBoxColumn() With {.Name = "file", .HeaderText = "Download", .ReadOnly = True, .Width = 85})
        grid.Columns.Add(New DataGridViewTextBoxColumn() With {.Name = "fit", .HeaderText = "On this PC", .ReadOnly = True, .Width = 165})
        grid.Columns.Add(New DataGridViewTextBoxColumn() With {.Name = "dl", .HeaderText = "Downloads", .ReadOnly = True, .Width = 85})
        grid.Columns.Add(New DataGridViewTextBoxColumn() With {.Name = "upd", .HeaderText = "Updated", .ReadOnly = True, .Width = 85})
        CType(grid.Columns("quant"), DataGridViewComboBoxColumn).Items.AddRange(QuantChoices)
        grid.Columns("dl").DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight
        grid.Columns("file").DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight
        ' Click a column title to sort by it; click again to reverse.
        For Each c As DataGridViewColumn In grid.Columns
            c.SortMode = If(c.Name = "sel", DataGridViewColumnSortMode.NotSortable, DataGridViewColumnSortMode.Programmatic)
            If c.Name <> "sel" Then c.ToolTipText = If(String.IsNullOrEmpty(c.ToolTipText), Localizer.T("Click to sort"), c.ToolTipText & " · " & Localizer.T("Click the title to sort"))
        Next
        AddHandler grid.ColumnHeaderMouseClick, Sub(s, e)
                                                    Dim col = grid.Columns(e.ColumnIndex)
                                                    If col.SortMode <> DataGridViewColumnSortMode.Programmatic OrElse e.Button <> MouseButtons.Left Then Return
                                                    If _sortCol = col.Name Then
                                                        _sortDesc = Not _sortDesc
                                                    Else
                                                        _sortCol = col.Name
                                                        ' numbers that are "better" when big start from the top
                                                        _sortDesc = (col.Name = "dl" OrElse col.Name = "upd")
                                                    End If
                                                    ApplyFilter()
                                                End Sub
        AddHandler grid.CurrentCellDirtyStateChanged, Sub()
                                                          If grid.IsCurrentCellDirty Then grid.CommitEdit(DataGridViewDataErrorContexts.Commit)
                                                      End Sub
        AddHandler grid.CellValueChanged, AddressOf Grid_CellValueChanged
        AddHandler grid.SelectionChanged, Async Sub() Await ShowDetailsAsync()
        AddHandler grid.DataError, Sub(s, e) e.ThrowException = False
        AddHandler grid.CellDoubleClick, Sub(s, e)
                                             If e.RowIndex >= 0 AndAlso grid.Columns(e.ColumnIndex).Name <> "quant" Then
                                                 Dim r = grid.Rows(e.RowIndex)
                                                 r.Cells("sel").Value = Not CBool(If(r.Cells("sel").Value, False))
                                             End If
                                         End Sub

        ' Details panel
        pnlDetail = New Panel() With {.Dock = DockStyle.Fill, .Padding = New Padding(10), .BackColor = Theme.CRaised}
        lblDetailTitle = New Label() With {.Dock = DockStyle.Top, .Height = 46, .Font = New Font("Segoe UI", 11.0!, FontStyle.Bold), .ForeColor = Color.White, .Text = "Select a model"}
        lblDetailInfo = New Label() With {.Dock = DockStyle.Top, .Height = 110, .ForeColor = Theme.CDim, .Font = Theme.SmallFont}
        Dim lblQ = New Label() With {.Dock = DockStyle.Top, .Height = 22, .Text = "Available files (click one to choose it):", .ForeColor = Theme.CText}
        lstQuants = New ListView() With {.Dock = DockStyle.Fill}
        Theme.StyleListView(lstQuants)
        lstQuants.Columns.Add("Quant", 110)
        lstQuants.Columns.Add("Size", 80)
        lstQuants.Columns.Add("On this PC", 140)
        AddHandler lstQuants.ItemActivate, AddressOf QuantPicked
        AddHandler lstQuants.Click, AddressOf QuantPicked
        chkVision = New CheckBox() With {.Dock = DockStyle.Bottom, .Height = 26, .Text = "Include vision projector (image input)", .Checked = True}
        lnkHf = New LinkLabel() With {.Dock = DockStyle.Bottom, .Height = 24, .Text = "Open model page on Hugging Face", .LinkColor = Color.FromArgb(78, 161, 255)}
        AddHandler lnkHf.LinkClicked, Sub()
                                          Dim e = SelectedEntry()
                                          If e IsNot Nothing Then OpenUrl("https://huggingface.co/" & e.Repo)
                                      End Sub
        pnlDetail.Controls.Add(lstQuants)
        pnlDetail.Controls.Add(lblQ)
        pnlDetail.Controls.Add(lblDetailInfo)
        pnlDetail.Controls.Add(lblDetailTitle)
        pnlDetail.Controls.Add(chkVision)
        pnlDetail.Controls.Add(lnkHf)

        split.Panel1.Controls.Add(grid)
        split.Panel2.Controls.Add(pnlDetail)

        page.Controls.Add(split)
        page.Controls.Add(actions)
        page.Controls.Add(bar)
        AddHandler page.Layout, Sub()
                                    Try
                                        If split.Width > 700 Then split.SplitterDistance = split.Width - 360
                                    Catch
                                    End Try
                                End Sub
    End Sub

    Private Sub BuildInstalledTab(page As TabPage)
        Dim bar As New FlowLayoutPanel() With {.Dock = DockStyle.Top, .Height = 42, .WrapContents = False, .Padding = New Padding(4)}
        Dim btnDelete = Theme.MakeButton("🗑 Delete")
        Dim btnOpen = Theme.MakeButton("📁 Open folder")
        Dim btnImport = Theme.MakeButton("➕ Import GGUF file…")
        Dim btnRescan = Theme.MakeButton("⟳ Rescan")
        Dim btnUse = Theme.MakeButton("Use in chat", primary:=True)
        Dim btnSpeed = Theme.MakeButton("⚡ Speed test")
        lblInstalledInfo = Theme.MakeLabel("", dim_:=True)
        bar.Controls.AddRange({btnUse, btnSpeed, btnDelete, btnOpen, btnImport, btnRescan, lblInstalledInfo})
        AddHandler btnSpeed.Click, Async Sub() Await RunSpeedTestsAsync(btnSpeed)

        lstInstalled = New ListView() With {.Dock = DockStyle.Fill}
        Theme.StyleListView(lstInstalled)
        lstInstalled.MultiSelect = True
        lstInstalled.Columns.Add("Model", 360)
        lstInstalled.Columns.Add("Quant", 95)
        lstInstalled.Columns.Add("Size", 80)
        lstInstalled.Columns.Add("Vision", 55)
        lstInstalled.Columns.Add("Speed", 85)
        lstInstalled.Columns.Add("Reading", 90)
        lstInstalled.Columns.Add("Load", 60)
        lstInstalled.Columns.Add("Context", 70)
        lstInstalled.Columns.Add("Source", 240)

        AddHandler btnDelete.Click, AddressOf DeleteInstalled
        AddHandler btnOpen.Click, Sub()
                                      Dim m = SelectedInstalled()
                                      OpenFolder(If(m?.Folder, AppCore.Library.Folder))
                                  End Sub
        AddHandler btnImport.Click, AddressOf ImportGguf
        AddHandler btnRescan.Click, Sub()
                                        AppCore.Library.Refresh()
                                        RefreshInstalled()
                                    End Sub
        AddHandler btnUse.Click, Sub()
                                     Dim m = SelectedInstalled()
                                     If m Is Nothing Then Return
                                     ModelToUse = m.DisplayName
                                     DialogResult = DialogResult.OK
                                     Close()
                                 End Sub
        AddHandler lstInstalled.DoubleClick, Sub() btnUse.PerformClick()

        page.Controls.Add(lstInstalled)
        page.Controls.Add(bar)
    End Sub

    ''' <summary>Set when the user presses "Use in chat".</summary>
    <ComponentModel.DesignerSerializationVisibility(ComponentModel.DesignerSerializationVisibility.Hidden)>
    Public Property ModelToUse As String

    '════════════════════════════════════════════════════════════
    '  Load
    '════════════════════════════════════════════════════════════
    Protected Overrides Async Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        lblHw.Text = _hw.Summary
        Dim age As String = Nothing
        _entries = ModelCatalog.LoadCachedOrEmbedded(age)
        RecomputeBest()
        UpdateInfoLabel(age)
        ApplyFilter()
        RefreshDownloads()

        _dlTimer = New System.Windows.Forms.Timer() With {.Interval = 500}
        AddHandler _dlTimer.Tick, Sub() RefreshDownloads()
        _dlTimer.Start()

        If ModelCatalog.CacheIsStale() Then Await RefreshCatalogAsync(force:=False)
    End Sub

    Protected Overrides Sub OnFormClosed(e As FormClosedEventArgs)
        _dlTimer?.Stop()
        _detailCts?.Cancel()
        MyBase.OnFormClosed(e)
    End Sub

    Private Sub RecomputeBest()
        _best = ModelCatalog.BestThatFit(_entries, _hw, AppCore.Settings.ContextSize)
        _bestUncensored = ModelCatalog.BestThatFit(_entries, _hw, AppCore.Settings.ContextSize, uncensored:=True)
    End Sub

    Private Sub UpdateInfoLabel(age As String)
        Dim vramNote = If(_hw.VramBytes = 0, "No GPU found — models run on the CPU (smaller models recommended). ", "")
        lblInfo.Text = $"{vramNote}Showing the {_best.Count} best models for {_hw.VramGB:0.#} GB VRAM (of {_entries.Count}, {age}) — choose 'All models' to see everything · estimates use {AppCore.Settings.ContextSize \ 1024}K context · " &
                       $"installed: {AppCore.Library.All().Count} ({Downloader.FormatBytes(AppCore.Library.TotalSize())}) · " &
                       $"folder: {AppCore.Library.Folder}"
    End Sub

    Private Async Function RefreshCatalogAsync(force As Boolean) As Task
        btnRefresh.Enabled = False
        btnRefresh.Text = "Updating…"
        Try
            Dim list = Await ModelCatalog.FetchLiveAsync(CancellationToken.None)
            If list.Count > 50 Then
                _entries = list
                _searchResults = Nothing
                RecomputeBest()
                UpdateInfoLabel("updated just now")
                ApplyFilter()
            End If
        Catch ex As Exception
            If force Then Localizer.Box(Me, "Could not reach Hugging Face:" & vbCrLf & ex.Message & vbCrLf & vbCrLf &
                                          "The built-in list is still available offline.", "Update list", MessageBoxButtons.OK, MessageBoxIcon.Warning)
        Finally
            btnRefresh.Enabled = True
            btnRefresh.Text = "⟳ Update list"
        End Try
    End Function

    Private Async Function SearchHubAsync() As Task
        Dim q = ModelCatalog.SplitQuery(txtFilter.Text).Name
        If q.Contains("/"c) Then q = q.Split("/"c).Last()
        q = Regex.Replace(q, "-GGUF$", "", RegexOptions.IgnoreCase)
        If _searchResults IsNot Nothing AndAlso q.Length = 0 Then
            _searchResults = Nothing
            btnHub.Text = "🔍 Search all of Hugging Face"
            ApplyFilter()
            Return
        End If
        If q.Length < 2 Then
            Localizer.Box(Me, "Type a model name in the search box first (e.g. 'mistral', 'phi', 'llama 70b').", "Search")
            txtFilter.Focus()
            Return
        End If
        btnHub.Enabled = False
        Try
            _searchResults = Await ModelCatalog.SearchHubAsync(q, CancellationToken.None)
            btnHub.Text = "← Back to catalog"
            ' Show raw results without the text filter (the query already did that).
            ApplyFilter(ignoreText:=True)
            lblInfo.Text = $"{_searchResults.Count} Hugging Face results for '{q}' (any uploader — prefer known quantizers). Clear the search box and press the button to go back."
        Catch ex As Exception
            Localizer.Box(Me, "Search failed: " & ex.Message, "Search", MessageBoxButtons.OK, MessageBoxIcon.Warning)
        Finally
            btnHub.Enabled = True
        End Try
    End Function

    '════════════════════════════════════════════════════════════
    '  Filtering / grid
    '════════════════════════════════════════════════════════════
    Private Sub ApplyFilter(Optional ignoreText As Boolean = False)
        Dim ctx = AppCore.Settings.ContextSize
        Dim src As IEnumerable(Of CatalogEntry) = If(_searchResults, _entries)
        Dim query = ModelCatalog.SplitQuery(txtFilter.Text)
        Dim q = If(ignoreText OrElse _searchResults IsNot Nothing, "", query.Name)

        If q.Length > 0 Then
            Dim words = q.ToLowerInvariant().Split(" "c, StringSplitOptions.RemoveEmptyEntries)
            src = src.Where(Function(e)
                                Dim hay = (e.Repo & " " & e.Arch & " " & e.Badges).ToLowerInvariant()
                                Return words.All(Function(w) hay.Contains(w))
                            End Function)
        End If

        Select Case cmbCategory.SelectedIndex
            Case 1 : src = src.Where(Function(e) Not e.IsCoder AndAlso Not e.IsUncensored)
            Case 2 : src = src.Where(Function(e) e.IsVision)
            Case 3 : src = src.Where(Function(e) e.IsReasoning)
            Case 4 : src = src.Where(Function(e) e.IsCoder)
            Case 5 : src = src.Where(Function(e) e.IsMoE)
            Case 6 : src = src.Where(Function(e) e.IsUncensored)
            Case 7 To 12
                Dim cls = cmbCategory.SelectedItem.ToString()
                src = src.Where(Function(e) e.SizeClass = cls)
            Case 13 : src = src.Where(Function(e) AppCore.Library.HasRepo(e.Repo))
        End Select

        ' Typing a name searches all models, not only the best ones for this GPU.
        If _searchResults Is Nothing AndAlso q.Length = 0 Then
            Select Case cmbShow.SelectedIndex
                Case 0   ' best that fully fit in VRAM (default)
                    ' The Uncensored category has its own best list; the normal one never contains them.
                    Dim bestSet = If(cmbCategory.SelectedIndex = 6, _bestUncensored, _best)
                    Dim picked = src.Where(Function(e) bestSet.Contains(e.Repo) OrElse AppCore.Library.HasRepo(e.Repo)).ToList()
                    If picked.Count = 0 AndAlso cmbCategory.SelectedIndex > 0 Then
                        ' Nothing in this category made the "best" cut: show what runs here instead of an empty list.
                        picked = src.Where(Function(e) e.Params > 0 AndAlso
                            ModelCatalog.Fit(e, ModelCatalog.EstimateFileBytes(e, ModelCatalog.AutoQuant(e, _hw, ctx)), _hw, ctx) <> FitLevel.TooBig).ToList()
                    End If
                    src = picked
                Case 1   ' anything that runs, incl. GPU+CPU offload
                    src = src.Where(Function(e) e.Params <= 0 OrElse
                        ModelCatalog.Fit(e, ModelCatalog.EstimateFileBytes(e, ModelCatalog.AutoQuant(e, _hw, ctx)), _hw, ctx) <> FitLevel.TooBig)
            End Select
        End If
        ' Only models that run entirely on the graphics card (with the file size picked for each).
        If chkFitGpu IsNot Nothing AndAlso chkFitGpu.Checked Then
            src = src.Where(Function(e) e.Params > 0 AndAlso
                If(_hw.VramBytes = 0, ModelCatalog.Fit(e, ModelCatalog.EstimateFileBytes(e, EffectiveQuant(e)), _hw, ctx) <> FitLevel.TooBig,
                   ModelCatalog.Fit(e, ModelCatalog.EstimateFileBytes(e, EffectiveQuant(e)), _hw, ctx) = FitLevel.Fits))
        End If

        ' Only the most downloaded ones (a search shows everything that matches).
        If _searchResults Is Nothing AndAlso q.Length = 0 AndAlso cmbCategory.SelectedIndex <> 13 Then
            src = Popular.Top(src, Popular.Count(cmbTop), Function(e) e.Downloads, Function(e) AppCore.Library.HasRepo(e.Repo))
        End If
        If _sortCol IsNot Nothing Then
            Dim ctx2 = ctx
            Dim key As Func(Of CatalogEntry, IComparable)
            Select Case _sortCol
                Case "name" : key = Function(e) e.Name.ToLowerInvariant()
                Case "params" : key = Function(e) e.Params
                Case "badges" : key = Function(e) e.Badges
                Case "quant" : key = Function(e) QuantFor(e)
                Case "file" : key = Function(e) ModelCatalog.EstimateFileBytes(e, EffectiveQuant(e))
                Case "fit" : key = Function(e) CInt(ModelCatalog.Fit(e, ModelCatalog.EstimateFileBytes(e, EffectiveQuant(e)), _hw, ctx2)) * 1000000000000L - ModelCatalog.EstimateFileBytes(e, EffectiveQuant(e))
                Case "upd" : key = Function(e) If(e.Updated, "")
                Case Else : key = Function(e) e.Downloads
            End Select
            src = If(_sortDesc, src.OrderByDescending(key), src.OrderBy(key))
        Else
        Select Case cmbSort.SelectedIndex
            Case 1 : src = src.OrderByDescending(Function(e) e.Likes)
            Case 2 : src = src.OrderByDescending(Function(e) e.Updated)
            Case 3 : src = src.OrderBy(Function(e) e.Params)
            Case 4 : src = src.OrderByDescending(Function(e) e.Params)
            Case 5 : src = src.OrderBy(Function(e) e.Name, StringComparer.OrdinalIgnoreCase)
            Case Else : src = src.OrderByDescending(Function(e) e.Downloads)
        End Select
        End If
        For Each c As DataGridViewColumn In grid.Columns
            If c.SortMode = DataGridViewColumnSortMode.Programmatic Then
                c.HeaderCell.SortGlyphDirection = If(c.Name <> _sortCol, SortOrder.None, If(_sortDesc, SortOrder.Descending, SortOrder.Ascending))
            End If
        Next

        Dim list = src.ToList()
        _suppressGridEvents = True
        grid.SuspendLayout()
        grid.Rows.Clear()
        Dim rows As New List(Of DataGridViewRow)(list.Count)
        For Each e In list
            Dim r As New DataGridViewRow()
            r.CreateCells(grid)
            r.Tag = e
            r.Cells(0).Value = _checked.Contains(e.Repo)
            Dim installed = AppCore.Library.HasRepo(e.Repo)
            r.Cells(1).Value = If(installed, "✔ ", "") & e.Name & "   ·  " & e.Author
            r.Cells(2).Value = If(e.Params > 0, FormatParams(e.ParamsB), "?")
            r.Cells(3).Value = e.Badges
            Dim qv As String = Nothing
            r.Cells(4).Value = If(_quantChoice.TryGetValue(e.Repo, qv), qv, "Auto")
            r.Cells(7).Value = FormatCount(e.Downloads)
            r.Cells(8).Value = e.Updated
            FillFitCells(r, e)
            rows.Add(r)
        Next
        grid.Rows.AddRange(rows.ToArray())
        ' "name:quant" → pre-select that quant (and tick the model when it is the only match).
        If query.Quant IsNot Nothing Then
            For Each r As DataGridViewRow In grid.Rows
                Dim cell = CType(r.Cells(4), DataGridViewComboBoxCell)
                If Not cell.Items.Contains(query.Quant) Then
                    cell.Items.Clear()
                    cell.Items.AddRange(QuantChoices)
                    cell.Items.Add(query.Quant)
                End If
                cell.Value = query.Quant
                Dim e = TryCast(r.Tag, CatalogEntry)
                If e IsNot Nothing Then
                    _quantChoice(e.Repo) = query.Quant
                    FillFitCells(r, e)
                End If
            Next
            If grid.Rows.Count = 1 Then
                grid.Rows(0).Cells(0).Value = True
                Dim e0 = TryCast(grid.Rows(0).Tag, CatalogEntry)
                If e0 IsNot Nothing Then _checked.Add(e0.Repo)
            End If
        End If
        grid.ResumeLayout()
        _suppressGridEvents = False
        UpdateDownloadButton()
    End Sub

    Private Sub FillFitCells(r As DataGridViewRow, e As CatalogEntry, Optional realBytes As Long = 0)
        Dim ctx = AppCore.Settings.ContextSize
        Dim choice = If(r.Cells(4).Value?.ToString(), "Auto")
        Dim quant = If(choice = "Auto", ModelCatalog.AutoQuant(e, _hw, ctx), choice)
        If e.Params <= 0 Then
            r.Cells(5).Value = "?"
            r.Cells(6).Value = "size unknown"
            Return
        End If
        Dim bytes = If(realBytes > 0, realBytes, ModelCatalog.EstimateFileBytes(e, quant))
        Dim fit = ModelCatalog.Fit(e, bytes, _hw, ctx)
        r.Cells(5).Value = If(realBytes > 0, "", "~") & Downloader.FormatBytes(bytes)
        r.Cells(6).Value = ModelCatalog.FitText(fit, _hw.VramBytes = 0) & If(choice = "Auto", "  (" & quant & ")", "")
        r.Cells(6).Style.ForeColor = FitColor(fit)
        r.Cells(1).Style.ForeColor = If(fit = FitLevel.TooBig, Theme.CDim, Theme.CText)
    End Sub

    Private Sub Grid_CellValueChanged(sender As Object, e As DataGridViewCellEventArgs)
        If _suppressGridEvents OrElse e.RowIndex < 0 Then Return
        Dim r = grid.Rows(e.RowIndex)
        Dim entry = TryCast(r.Tag, CatalogEntry)
        If entry Is Nothing Then Return
        Select Case grid.Columns(e.ColumnIndex).Name
            Case "sel"
                If CBool(If(r.Cells(0).Value, False)) Then _checked.Add(entry.Repo) Else _checked.Remove(entry.Repo)
                UpdateDownloadButton()
            Case "quant"
                _quantChoice(entry.Repo) = If(r.Cells(4).Value?.ToString(), "Auto")
                FillFitCells(r, entry)
        End Select
    End Sub

    Private Sub UpdateDownloadButton()
        btnDownload.Text = Localizer.T("⬇  Download selected") & $" ({_checked.Count})"
        btnDownload.Enabled = _checked.Count > 0
    End Sub

    Private Function SelectedEntry() As CatalogEntry
        If grid.CurrentRow Is Nothing Then Return Nothing
        Return TryCast(grid.CurrentRow.Tag, CatalogEntry)
    End Function

    '════════════════════════════════════════════════════════════
    '  Details panel (real file sizes from Hugging Face)
    '════════════════════════════════════════════════════════════
    Private Async Function ShowDetailsAsync() As Task
        Dim e = SelectedEntry()
        If e Is Nothing Then Return
        _detailCts?.Cancel()
        _detailCts = New CancellationTokenSource()
        Dim ct = _detailCts.Token
        Dim row = grid.CurrentRow

        lblDetailTitle.Text = e.Name
        Dim info As New Text.StringBuilder()
        info.AppendLine($"{e.Repo}")
        If e.Params > 0 Then info.AppendLine($"{e.ParamsB:0.##} billion parameters" & If(e.ActiveParamsB > 0, $" ({e.ActiveParamsB:0.#}B active per token)", "") & $" · {e.Arch}")
        If e.ContextLength > 0 Then info.AppendLine($"Max context: {e.ContextLength:N0} tokens")
        If Not String.IsNullOrEmpty(e.License) Then info.AppendLine("License: " & e.License)
        info.AppendLine($"{FormatCount(e.Downloads)} downloads · {e.Likes:N0} likes · updated {e.Updated}")
        If e.IsMoE Then info.AppendLine("MoE: only part of the model works per token, so it stays fast even when it spills into RAM.")
        lblDetailInfo.Text = info.ToString()
        chkVision.Visible = e.IsVision

        lstQuants.Items.Clear()
        lstQuants.Items.Add(New ListViewItem("Loading file list…"))
        Try
            Await Task.Delay(250, ct)   ' debounce fast scrolling
            Dim files = Await ModelCatalog.GetRepoFilesAsync(e.Repo, AppCore.Settings.HuggingFaceToken, ct)
            If ct.IsCancellationRequested Then Return
            If e.Params <= 0 Then
                Dim q4 = files.Quants.FirstOrDefault(Function(q) q.Label.Contains("Q4_K_M"))
                If q4 IsNot Nothing Then e.Params = CLng(q4.TotalBytes * 8 / 4.85)
            End If
            Dim ctx = AppCore.Settings.ContextSize
            Dim auto = ModelCatalog.PickQuant(e, files, "Auto", _hw, ctx)
            lstQuants.BeginUpdate()
            lstQuants.Items.Clear()
            For Each q In files.Quants.OrderByDescending(Function(x) x.TotalBytes)
                Dim fit = ModelCatalog.Fit(e, q.TotalBytes, _hw, ctx)
                Dim it As New ListViewItem(If(q Is auto, "★ ", "") & q.Label) With {.Tag = q}
                it.SubItems.Add(Downloader.FormatBytes(q.TotalBytes) & If(q.Files.Count > 1, $" ({q.Files.Count} parts)", ""))
                it.SubItems.Add(ModelCatalog.FitText(fit, _hw.VramBytes = 0))
                it.ForeColor = FitColor(fit)
                lstQuants.Items.Add(it)
            Next
            If files.Mmproj IsNot Nothing Then
                chkVision.Visible = True
                chkVision.Text = $"Include vision projector ({Downloader.FormatBytes(files.Mmproj.Size)})"
            End If
            lstQuants.EndUpdate()

            ' Replace the estimate in the grid with the real size of the auto/selected file.
            If row IsNot Nothing AndAlso row.Tag Is e AndAlso auto IsNot Nothing Then
                Dim choice = If(row.Cells(4).Value?.ToString(), "Auto")
                Dim chosen = ModelCatalog.PickQuant(e, files, choice, _hw, ctx)
                If chosen IsNot Nothing Then
                    _suppressGridEvents = True
                    FillFitCells(row, e, chosen.TotalBytes)
                    If choice = "Auto" Then row.Cells(6).Value = ModelCatalog.FitText(ModelCatalog.Fit(e, chosen.TotalBytes, _hw, ctx), _hw.VramBytes = 0) & "  (" & chosen.Label & ")"
                    _suppressGridEvents = False
                End If
            End If
        Catch ex As OperationCanceledException
        Catch ex As Exception
            lstQuants.Items.Clear()
            lstQuants.Items.Add(New ListViewItem("⚠ " & ex.Message))
        End Try
    End Function

    Private Sub QuantPicked(sender As Object, e As EventArgs)
        If lstQuants.SelectedItems.Count = 0 OrElse grid.CurrentRow Is Nothing Then Return
        Dim q = TryCast(lstQuants.SelectedItems(0).Tag, QuantOption)
        If q Is Nothing Then Return
        Dim row = grid.CurrentRow
        Dim cell = CType(row.Cells(4), DataGridViewComboBoxCell)
        If Not cell.Items.Contains(q.Label) Then
            ' Column items are shared; give this cell its own list including the exact quant.
            cell.Items.Clear()
            cell.Items.AddRange(QuantChoices)
            cell.Items.Add(q.Label)
        End If
        cell.Value = q.Label
        row.Cells(0).Value = True
    End Sub

    '════════════════════════════════════════════════════════════
    '  Recommend / download
    '════════════════════════════════════════════════════════════
    Private Sub RecommendForMe()
        Dim ctx = AppCore.Settings.ContextSize
        Dim fitting = _entries.Where(Function(e) _best.Contains(e.Repo)).OrderByDescending(Function(e) e.Downloads).ToList()
        If fitting.Count = 0 Then
            ' CPU-only or tiny GPU: allow offloaded models, prefer small/MoE.
            fitting = _entries.Where(Function(e) e.Params > 0 AndAlso e.ParamsB <= 9 AndAlso Not e.IsUncensored).
                               OrderByDescending(Function(e) e.Downloads).ToList()
        End If
        Dim picks As New List(Of CatalogEntry)
        Dim add = Sub(pred As Func(Of CatalogEntry, Boolean))
                      Dim p = fitting.Where(pred).OrderByDescending(Function(e) e.ParamsB * Math.Log10(Math.Max(10, e.Downloads))).
                                      FirstOrDefault(Function(e) Not picks.Contains(e))
                      If p IsNot Nothing Then picks.Add(p)
                  End Sub
        add(Function(e) Not e.IsCoder AndAlso e.Downloads > 50000)              ' best general model that fits
        add(Function(e) e.IsVision AndAlso e.Downloads > 20000)                 ' vision
        add(Function(e) e.IsCoder AndAlso e.Downloads > 10000)                  ' coding
        add(Function(e) e.ParamsB <= 4.5 AndAlso e.Downloads > 50000)           ' fast small model
        If picks.Count = 0 Then
            Localizer.Box(Me, "No recommendation available — try 'Update list'.", "Recommend")
            Return
        End If
        For Each p In picks
            _checked.Add(p.Repo)
        Next
        cmbCategory.SelectedIndex = 0
        txtFilter.Text = ""
        If picks.Any(Function(p) Not _best.Contains(p.Repo)) Then cmbShow.SelectedIndex = 1
        ApplyFilter()
        Localizer.Box(Me, "Selected for you:" & vbCrLf & vbCrLf &
                        String.Join(vbCrLf, picks.Select(Function(p) "• " & p.Name & "   " & p.Badges)) & vbCrLf & vbCrLf &
                        "Untick anything you don't want, then press Download.", "Recommended for " & $"{_hw.VramGB:0} GB VRAM",
                        MessageBoxButtons.OK, MessageBoxIcon.Information)
    End Sub

    Private Sub DownloadSelected()
        Dim all = _entries.Concat(If(_searchResults, New List(Of CatalogEntry)())).
                  GroupBy(Function(e) e.Repo, StringComparer.OrdinalIgnoreCase).Select(Function(g) g.First()).ToList()
        Dim sel = all.Where(Function(e) _checked.Contains(e.Repo)).ToList()
        If sel.Count = 0 Then Return

        Dim ctx = AppCore.Settings.ContextSize
        Dim tooBig = sel.Where(Function(e) e.Params > 0 AndAlso
            ModelCatalog.Fit(e, ModelCatalog.EstimateFileBytes(e, QuantFor(e)), _hw, ctx) = FitLevel.TooBig).ToList()
        If tooBig.Count > 0 Then
            Dim ans = Localizer.Box(Me, "These probably won't run on this PC (not enough VRAM + RAM):" & vbCrLf & vbCrLf &
                                      String.Join(vbCrLf, tooBig.Select(Function(e) "• " & e.Name)) & vbCrLf & vbCrLf &
                                      "Download them anyway?", "Too big", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning)
            If ans = DialogResult.Cancel Then Return
            If ans = DialogResult.No Then sel = sel.Except(tooBig).ToList()
        End If

        For Each e In sel
            Dim job = AppCore.Downloads.Enqueue(e, QuantFor(e))
            job.IncludeVision = chkVision.Checked OrElse Not e.IsVision
            _checked.Remove(e.Repo)
        Next
        ApplyFilter()
        RefreshDownloads()
    End Sub

    Private _sortCol As String
    Private _sortDesc As Boolean
    Private chkFitGpu As CheckBox

    ''' <summary>The quant that will really be downloaded for this model (Auto resolved).</summary>
    Private Function EffectiveQuant(e As CatalogEntry) As String
        Dim q = QuantFor(e)
        Return If(q = "Auto", ModelCatalog.AutoQuant(e, _hw, AppCore.Settings.ContextSize), q)
    End Function

    Private Function QuantFor(e As CatalogEntry) As String
        Dim q As String = Nothing
        If _quantChoice.TryGetValue(e.Repo, q) Then Return q
        Return "Auto"
    End Function

    '════════════════════════════════════════════════════════════
    '  Downloads grid
    '════════════════════════════════════════════════════════════
    Private Sub RefreshDownloads()
        Dim jobs = AppCore.Downloads.Jobs()
        If gridDl.Rows.Count <> jobs.Count Then
            gridDl.Rows.Clear()
            For Each j In jobs
                Dim i = gridDl.Rows.Add(j.Title, "", j.StatusText)
                gridDl.Rows(i).Tag = j
            Next
        Else
            For i = 0 To jobs.Count - 1
                Dim r = gridDl.Rows(i)
                r.Tag = jobs(i)
                r.Cells(0).Value = jobs(i).Title
                r.Cells(2).Value = jobs(i).StatusText
                gridDl.InvalidateCell(1, i)
            Next
        End If
        For Each r As DataGridViewRow In gridDl.Rows
            Dim j = CType(r.Tag, DownloadJob)
            r.Cells(2).Style.ForeColor = If(j.Status = DownloadStatus.Failed, Theme.CBad,
                                         If(j.Status = DownloadStatus.Completed, Theme.CGood, Theme.CText))
        Next
    End Sub

    Private Sub PaintProgress(sender As Object, e As DataGridViewCellPaintingEventArgs)
        If e.RowIndex < 0 OrElse e.ColumnIndex <> 1 Then Return
        Dim j = TryCast(gridDl.Rows(e.RowIndex).Tag, DownloadJob)
        If j Is Nothing Then Return
        e.PaintBackground(e.CellBounds, True)
        Dim r = e.CellBounds
        r.Inflate(-6, -7)
        Using bg As New SolidBrush(Color.FromArgb(55, 55, 58))
            e.Graphics.FillRectangle(bg, r)
        End Using
        Dim pct = If(j.Status = DownloadStatus.Completed, 100, j.Percent)
        If pct > 0 Then
            Dim fill = New Rectangle(r.X, r.Y, CInt(r.Width * pct / 100.0), r.Height)
            Using fg As New SolidBrush(If(j.Status = DownloadStatus.Failed, Theme.CBad, If(j.Status = DownloadStatus.Completed, Theme.CGood, Theme.CAccent)))
                e.Graphics.FillRectangle(fg, fill)
            End Using
        End If
        TextRenderer.DrawText(e.Graphics, pct & "%", Theme.SmallFont, r, Color.White, TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
        e.Handled = True
    End Sub

    Private Function SelectedJob() As DownloadJob
        If gridDl.CurrentRow Is Nothing Then Return Nothing
        Return TryCast(gridDl.CurrentRow.Tag, DownloadJob)
    End Function

    '════════════════════════════════════════════════════════════
    '  Installed tab
    '════════════════════════════════════════════════════════════
    Private Sub RefreshInstalled()
        lstInstalled.BeginUpdate()
        lstInstalled.Items.Clear()
        For Each m In AppCore.Library.All()
            Dim it As New ListViewItem(m.DisplayName) With {.Tag = m}
            it.SubItems.Add(If(m.Quant, ""))
            it.SubItems.Add(Downloader.FormatBytes(m.SizeBytes))
            it.SubItems.Add(If(m.IsVision, "👁 yes", ""))
            Dim sp = SpeedTest.GetResult(m)
            it.SubItems.Add(If(sp IsNot Nothing, $"{sp.GenTokensPerSecond:0.0} tok/s", "—"))
            it.SubItems.Add(If(sp IsNot Nothing, $"{sp.PromptTokensPerSecond:0} tok/s", ""))
            it.SubItems.Add(If(sp IsNot Nothing, $"{sp.LoadSeconds:0.0}s", ""))
            it.SubItems.Add(If(sp IsNot Nothing AndAlso sp.ContextTokens > 0, $"{sp.ContextTokens \ 1024}K", ""))
            it.SubItems.Add(If(m.Repo, Path.GetFileName(m.MainFile)))
            lstInstalled.Items.Add(it)
        Next
        lstInstalled.EndUpdate()
        Dim free = ""
        Try
            Dim d As New DriveInfo(Path.GetPathRoot(AppCore.Library.Folder))
            free = $" · {Downloader.FormatBytes(d.AvailableFreeSpace)} free on {d.Name}"
        Catch
        End Try
        lblInstalledInfo.Text = $"{AppCore.Library.All().Count} models · {Downloader.FormatBytes(AppCore.Library.TotalSize())}{free}"
    End Sub

    ''' <summary>Benchmarks the selected models (or all) one after another.</summary>
    Private Async Function RunSpeedTestsAsync(btn As Button) As Task
        If AppCore.Settings.Backend <> BackendKind.BuiltIn Then
            Localizer.Box(Me, "The speed test uses the built-in engine (Tools → Settings → Backend).", "Speed test")
            Return
        End If
        Dim models = lstInstalled.SelectedItems.Cast(Of ListViewItem)().Select(Function(i) CType(i.Tag, LocalModel)).ToList()
        If models.Count = 0 Then models = AppCore.Library.All()
        If models.Count = 0 Then Return
        If models.Count > 1 AndAlso Localizer.Box(Me, $"Test {models.Count} models? Each one is loaded once (this can take a few minutes).",
                                                    "Speed test", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) <> DialogResult.OK Then Return
        btn.Enabled = False
        Try
            Dim i = 0
            For Each m In models
                i += 1
                lblInstalledInfo.Text = $"Testing {i}/{models.Count}: {m.DisplayName}…"
                Try
                    Dim r = Await SpeedTest.RunAsync(m, CancellationToken.None)
                Catch ex As Exception
                    Localizer.Box(Me, m.DisplayName & ": " & ex.Message, "Speed test", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                End Try
                RefreshInstalled()
            Next
        Finally
            btn.Enabled = True
        End Try
    End Function

    Private Function SelectedInstalled() As LocalModel
        If lstInstalled.SelectedItems.Count = 0 Then Return Nothing
        Return TryCast(lstInstalled.SelectedItems(0).Tag, LocalModel)
    End Function

    Private Async Sub DeleteInstalled(sender As Object, e As EventArgs)
        Dim m = SelectedInstalled()
        If m Is Nothing Then Return
        If Localizer.Box(Me, $"Delete {m.DisplayName} ({Downloader.FormatBytes(m.SizeBytes)}) from disk?", "Delete model",
                           MessageBoxButtons.YesNo, MessageBoxIcon.Warning) <> DialogResult.Yes Then Return
        Try
            If AppCore.Engine.CurrentModel IsNot Nothing AndAlso
               String.Equals(AppCore.Engine.CurrentModel.MainFile, m.MainFile, StringComparison.OrdinalIgnoreCase) Then
                Await AppCore.Engine.StopAsync()
            End If
            AppCore.Library.Delete(m)
            RefreshInstalled()
            ApplyFilter()
        Catch ex As Exception
            Localizer.Box(Me, "Could not delete: " & ex.Message, "Delete", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

    Private Sub ImportGguf(sender As Object, e As EventArgs)
        Using dlg As New OpenFileDialog() With {.Filter = "GGUF models (*.gguf)|*.gguf", .Multiselect = True, .Title = "Import GGUF model files"}
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            Dim move = Localizer.Box(Me, "Move the files into the models folder?" & vbCrLf & vbCrLf &
                                       "Yes = move (no extra disk space)   No = copy", "Import",
                                       MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question)
            If move = DialogResult.Cancel Then Return
            Dim target = Path.Combine(AppCore.Library.Folder, "imported")
            Directory.CreateDirectory(target)
            Cursor = Cursors.WaitCursor
            Try
                For Each f In dlg.FileNames
                    Dim dest = Path.Combine(target, Path.GetFileName(f))
                    If move = DialogResult.Yes Then File.Move(f, dest, True) Else File.Copy(f, dest, True)
                Next
            Catch ex As Exception
                Localizer.Box(Me, "Import failed: " & ex.Message, "Import", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Finally
                Cursor = Cursors.Default
            End Try
            AppCore.Library.Refresh()
            RefreshInstalled()
        End Using
    End Sub

    '════════════════════════════════════════════════════════════
    '  Helpers
    '════════════════════════════════════════════════════════════
    Friend Shared Function FitColor(f As FitLevel) As Color
        Select Case f
            Case FitLevel.Fits : Return Theme.CGood
            Case FitLevel.MoeOffload, FitLevel.PartialOffload : Return Theme.CWarn
            Case Else : Return Theme.CBad
        End Select
    End Function

    Private Shared Function FormatParams(b As Double) As String
        If b < 1 Then Return $"{b * 1000:0}M"
        If b < 10 Then Return $"{b:0.#}B"
        Return $"{b:0}B"
    End Function

    Private Shared Function FormatCount(n As Long) As String
        If n >= 1000000 Then Return $"{n / 1000000.0:0.#}M"
        If n >= 1000 Then Return $"{n / 1000.0:0}K"
        Return n.ToString()
    End Function

    Friend Shared Sub OpenFolder(path As String)
        Try
            Directory.CreateDirectory(path)
            Process.Start(New ProcessStartInfo("explorer.exe", """" & path & """") With {.UseShellExecute = True})
        Catch
        End Try
    End Sub

    Friend Shared Sub OpenUrl(url As String)
        Try
            Process.Start(New ProcessStartInfo(url) With {.UseShellExecute = True})
        Catch
        End Try
    End Sub
End Class
