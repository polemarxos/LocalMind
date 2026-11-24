Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms

''' <summary>
''' Model Manager tab "🎨 Pictures &amp; video": every picture, picture-editing and video model the Studio can
''' use (hand-made ones and hundreds of community models), with the best file size for this PC, sorting,
''' search and download.
''' </summary>
Public Class MediaModelsPanel
    Inherits Panel

    Private ReadOnly _hw As HardwareInfo = HardwareInfo.Current()
    Private ReadOnly grid As New DataGridView() With {.Dock = DockStyle.Fill, .EditMode = DataGridViewEditMode.EditOnEnter}
    Private ReadOnly cmbShow As New ComboBox() With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 200, .Margin = New Padding(3, 6, 3, 3)}
    Private ReadOnly cmbType As New ComboBox() With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 190, .Margin = New Padding(3, 6, 3, 3)}
    Private ReadOnly txtFilter As New TextBox() With {.Width = 240, .Margin = New Padding(3, 6, 3, 3)}
    Private ReadOnly lblInfo As Label
    Private ReadOnly lblAbout As New Label() With {.Dock = DockStyle.Bottom, .Height = 40, .Padding = New Padding(8, 4, 8, 4), .ForeColor = Theme.CDim}
    Private ReadOnly btnDownload As Button
    Private ReadOnly btnCancel As Button
    Private ReadOnly bar As New ProgressBar() With {.Width = 220, .Height = 18, .Margin = New Padding(8, 12, 3, 3), .Visible = False}
    Private ReadOnly lblStatus As Label
    Private ReadOnly _checked As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
    Private ReadOnly _variantChoice As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
    Private _sortCol As String = "dl"
    Private _sortDesc As Boolean = True
    Private _cts As CancellationTokenSource
    Private _suppress As Boolean
    Private _suppressType As Boolean
    Private ReadOnly _filterTimer As New System.Windows.Forms.Timer() With {.Interval = 250}
    Private ReadOnly details As New MediaDetailsView() With {.Dock = DockStyle.Fill}
    Private ReadOnly chkNsfw As New CheckBox() With {.AutoSize = True, .Margin = New Padding(10, 9, 3, 3), .ForeColor = Theme.CText}
    Private ReadOnly chkFit As New CheckBox() With {.AutoSize = True, .Margin = New Padding(10, 9, 3, 3), .ForeColor = Theme.CText}

    ''' <summary>Raised after downloads, so the Studio can refresh.</summary>
    Public Event Downloaded()

    ' Pictures and videos are listed apart (the two buttons above the list); each has its own kinds.
    Private Shared ReadOnly PictureTypes As (Label As String, Test As Func(Of MediaRecipe, Boolean))() = {
        ("All picture models", Function(r) True),
        ("🖼 Create pictures", Function(r) r.Can("create")),
        ("✏️ Edit pictures", Function(r) r.Can("edit") OrElse r.Can("inpaint")),
        ("🔍 Enlarge / tools", Function(r) r.IsUpscaler OrElse r.Kind = "tool")}
    Private Shared ReadOnly VideoTypes As (Label As String, Test As Func(Of MediaRecipe, Boolean))() = {
        ("All video models", Function(r) True),
        ("🎬 Make videos", Function(r) r.Can("create") OrElse r.Can("animate")),
        ("🗣 Talking videos", Function(r) r.Can("talk")),
        ("🎞 Edit videos", Function(r) r.Can("video-edit"))}
    Private ReadOnly optPictures As New RadioButton() With {.Appearance = Appearance.Button, .AutoSize = True, .Checked = True, .Margin = New Padding(3, 5, 0, 3),
                                                             .FlatStyle = FlatStyle.Flat, .Padding = New Padding(10, 3, 10, 3), .TextAlign = ContentAlignment.MiddleCenter, .UseMnemonic = False, .MinimumSize = New Size(0, 34)}
    Private ReadOnly optVideos As New RadioButton() With {.Appearance = Appearance.Button, .AutoSize = True, .Margin = New Padding(0, 5, 8, 3),
                                                           .FlatStyle = FlatStyle.Flat, .Padding = New Padding(10, 3, 10, 3), .TextAlign = ContentAlignment.MiddleCenter, .UseMnemonic = False, .MinimumSize = New Size(0, 34)}

    Private ReadOnly optMusic As New RadioButton() With {.Appearance = Appearance.Button, .AutoSize = True, .Margin = New Padding(0, 5, 8, 3),
                                                          .FlatStyle = FlatStyle.Flat, .Padding = New Padding(10, 3, 10, 3), .TextAlign = ContentAlignment.MiddleCenter, .UseMnemonic = False, .MinimumSize = New Size(0, 34)}
    Private Shared ReadOnly AudioTypes As (Label As String, Test As Func(Of AudioModel, Boolean))() = {
        ("All music & voice models", Function(m) True),
        ("🎵 Songs & music", Function(m) m.IsMusic),
        ("🗣 Voices (read aloud, cloning)", Function(m) Not m.IsMusic)}
    Private ReadOnly _checkedAudio As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

    Private ReadOnly Property Types As (Label As String, Test As Func(Of MediaRecipe, Boolean))()
        Get
            Return If(optVideos.Checked, VideoTypes, PictureTypes)
        End Get
    End Property

    Private Sub FillTypes()
        cmbType.Items.Clear()
        If optMusic.Checked Then
            cmbType.Items.AddRange(AudioTypes.Select(Function(t) CObj(Localizer.T(t.Label))).ToArray())
        Else
            cmbType.Items.AddRange(Types.Select(Function(t) CObj(Localizer.T(t.Label))).ToArray())
        End If
        cmbType.SelectedIndex = 0
    End Sub

    ''' <summary>Shows one kind of model: "picture", "video", "music" or "voice".</summary>
    Public Sub ShowKind(kind As String)
        _audioKind = If(kind = "music" OrElse kind = "voice", kind, "")
        chkNsfw.Visible = _audioKind = ""          ' (no uncensored sound models)
        btnCivitai.Visible = _audioKind = ""       ' (Civitai has no voice or music models LocalMind can use)
        Dim target = If(kind = "video", optVideos, If(_audioKind <> "", optMusic, optPictures))
        ' The three kind buttons are no longer on screen (the Model Manager's category bar replaced them), so
        ' Windows does not switch the others off by itself: do it here, or e.g. the video models would stay
        ' listed under Picture after switching back and forth.
        For Each o In {optPictures, optVideos, optMusic}
            If o IsNot target AndAlso o.Checked Then o.Checked = False
        Next
        If target.Checked Then
            _suppressType = True
            FillTypes()
            _suppressType = False
            Fill()
        Else
            target.Checked = True
        End If
        txtFilter.PlaceholderText = Localizer.T(If(kind = "video", "Search, e.g. minimax, 10eros, ltx", If(_audioKind <> "", "Search, e.g. qwen3-tts, chatterbox", "Search, e.g. flux, krea, qwen-image")))
    End Sub

    Private _audioKind As String = ""
    Private btnCivitai As Button

    ' The page on the right always shows the selected row's model (or nothing when the list is empty).
    Private Sub SyncDetails()
        If grid.Rows.Count = 0 OrElse grid.CurrentRow Is Nothing Then
            _shown = Nothing
            details.Clear()
            lblAbout.Text = ""
        Else
            If grid.CurrentRow.Tag IsNot _shown Then _shown = Nothing
            ShowAbout()
        End If
    End Sub

    Private Sub StyleKind(o As RadioButton)
        o.FlatAppearance.BorderColor = Theme.CBorder
        o.FlatAppearance.CheckedBackColor = Theme.CAccent
        o.FlatAppearance.MouseOverBackColor = Theme.CHover
        o.BackColor = Theme.CControl
        o.ForeColor = If(o.Checked, Color.White, Theme.CText)
        o.Font = If(o.Checked, Theme.BoldFont, Me.Font)
    End Sub

    Public Sub New()
        Dock = DockStyle.Fill
        BackColor = Theme.CPanel

        Dim top As New FlowLayoutPanel() With {.Dock = DockStyle.Top, .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
                                               .WrapContents = True, .Padding = New Padding(4, 4, 4, 2)}
        cmbShow.Items.AddRange({Localizer.T("★ Best for my GPU"), Localizer.T("All models (show everything)"), Localizer.T("🔓 Uncensored"), Localizer.T("✔ Installed")})
        cmbShow.SelectedIndex = 0
        Theme.StyleInput(cmbShow)
        optPictures.Text = Localizer.T("🖼 Picture models")
        optVideos.Text = Localizer.T("🎬 Video models")
        optVideos.Margin = New Padding(0, 5, 0, 3)
        optMusic.Text = Localizer.T("🎵 Music & voice models")
        FillTypes()
        StyleKind(optPictures) : StyleKind(optVideos) : StyleKind(optMusic)
        For Each o In {optPictures, optVideos, optMusic}
            AddHandler o.CheckedChanged, Sub(s, e)
                                             Dim rb = CType(s, RadioButton)
                                             StyleKind(rb)
                                             If Not rb.Checked Then Return
                                             _suppressType = True
                                             FillTypes()
                                             _suppressType = False
                                             Fill()
                                             ShowAbout()
                                         End Sub
        Next
        Theme.StyleInput(cmbType)
        txtFilter.PlaceholderText = Localizer.T("Search, e.g. flux, krea, wan i2v, convrot")
        Theme.StyleInput(txtFilter)
        Dim btnStudio = Theme.MakeButton(Localizer.T("🎨 Open the Pictures & video studio"))
        btnStudio.UseMnemonic = False
        AddHandler btnStudio.Click, Sub()
                                        Dim f = Application.OpenForms.OfType(Of StudioForm)().FirstOrDefault()
                                        If f Is Nothing Then
                                            f = New StudioForm()
                                            f.Show()
                                        Else
                                            f.Activate()
                                        End If
                                    End Sub
        ' Civitai (civitai.com, or civitai.red for everything): checkpoints and LoRAs, installed into the studio
        btnCivitai = Theme.MakeButton(Localizer.T("🌐 Browse Civitai"))
        AddHandler btnCivitai.Click, Sub()
                                         Dim fam = TryCast(grid.CurrentRow?.Tag, MediaRecipe)?.Family
                                         Using f As New CivitaiForm(checkpoints:=True, family:=fam, kind:=If(optVideos.Checked, "video", "picture"))
                                             f.ShowDialog(Me)
                                             If f.InstalledSomething Then
                                                 Fill()
                                                 RaiseEvent Downloaded()
                                             End If
                                         End Using
                                     End Sub
        chkNsfw.Text = Localizer.T("Show NSFW / uncensored")
        chkNsfw.Checked = AppCore.Settings.ShowNsfw
        details.ShowNsfw = chkNsfw.Checked
        AddHandler chkNsfw.CheckedChanged, Sub()
                                               AppCore.Settings.ShowNsfw = chkNsfw.Checked
                                               AppCore.SaveSettings()
                                               details.ShowNsfw = chkNsfw.Checked
                                               Fill()
                                               ShowAbout()
                                           End Sub
        chkFit.Text = Localizer.T("Only models that fit my GPU")
        chkFit.Checked = AppCore.Settings.OnlyFitGpu
        AddHandler chkFit.CheckedChanged, Sub()
                                              AppCore.Settings.OnlyFitGpu = chkFit.Checked
                                              AppCore.SaveSettings()
                                              Fill()
                                          End Sub
        top.Controls.AddRange({cmbShow, txtFilter, chkFit, chkNsfw, btnCivitai, btnStudio})      ' (the kind is chosen above, in the Model Manager)
        lblInfo = Theme.MakeLabel("", dim_:=True)
        lblInfo.Dock = DockStyle.Top
        lblInfo.AutoSize = False
        lblInfo.Height = 22
        lblInfo.Padding = New Padding(8, 3, 0, 0)

        Theme.StyleGrid(grid)
        grid.Columns.Add(New DataGridViewCheckBoxColumn() With {.Name = "sel", .HeaderText = "", .Width = 32})
        grid.Columns.Add(New DataGridViewTextBoxColumn() With {.Name = "name", .HeaderText = Localizer.T("Model"), .ReadOnly = True, .AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, .MinimumWidth = 220})
        grid.Columns.Add(New DataGridViewTextBoxColumn() With {.Name = "type", .HeaderText = Localizer.T("Can do"), .ReadOnly = True, .Width = 170})
        grid.Columns.Add(New DataGridViewTextBoxColumn() With {.Name = "steps", .HeaderText = Localizer.T("Steps"), .ReadOnly = True, .Width = 70,
                                                               .ToolTipText = Localizer.T("Steps LocalMind uses (⚡ = with the fast mode)")})
        grid.Columns.Add(New DataGridViewTextBoxColumn() With {.Name = "family", .HeaderText = Localizer.T("Family"), .ReadOnly = True, .Width = 110})
        grid.Columns.Add(New DataGridViewComboBoxColumn() With {.Name = "variant", .HeaderText = Localizer.T("File"), .Width = 120, .FlatStyle = FlatStyle.Flat,
                                                                .DisplayStyle = DataGridViewComboBoxDisplayStyle.Nothing,
                                                                .ToolTipText = Localizer.T("Click to choose the file size. Auto picks the best one for your PC.")})
        grid.Columns.Add(New DataGridViewTextBoxColumn() With {.Name = "size", .HeaderText = Localizer.T("Download"), .ReadOnly = True, .Width = 90})
        grid.Columns.Add(New DataGridViewTextBoxColumn() With {.Name = "fit", .HeaderText = Localizer.T("On this PC"), .ReadOnly = True, .Width = 160})
        grid.Columns.Add(New DataGridViewTextBoxColumn() With {.Name = "dl", .HeaderText = Localizer.T("Downloads"), .ReadOnly = True, .Width = 85})
        grid.Columns.Add(New DataGridViewTextBoxColumn() With {.Name = "src", .HeaderText = Localizer.T("From"), .ReadOnly = True, .Width = 120})
        grid.Columns("size").DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight
        grid.Columns("dl").DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight
        For Each c As DataGridViewColumn In grid.Columns
            c.SortMode = If(c.Name = "sel", DataGridViewColumnSortMode.NotSortable, DataGridViewColumnSortMode.Programmatic)
        Next
        AddHandler grid.ColumnHeaderMouseClick, Sub(s, e)
                                                    Dim col = grid.Columns(e.ColumnIndex)
                                                    If col.SortMode <> DataGridViewColumnSortMode.Programmatic Then Return
                                                    If _sortCol = col.Name Then
                                                        _sortDesc = Not _sortDesc
                                                    Else
                                                        _sortCol = col.Name
                                                        _sortDesc = (col.Name = "dl")
                                                    End If
                                                    Fill()
                                                End Sub
        AddHandler grid.CurrentCellDirtyStateChanged, Sub()
                                                          If grid.IsCurrentCellDirty Then grid.CommitEdit(DataGridViewDataErrorContexts.Commit)
                                                      End Sub
        AddHandler grid.CellValueChanged, AddressOf CellChanged
        AddHandler grid.SelectionChanged, Sub() ShowAbout()
        AddHandler grid.DataError, Sub(s, e) e.ThrowException = False

        Dim actions As New FlowLayoutPanel() With {.Dock = DockStyle.Bottom, .Height = 44, .WrapContents = False, .Padding = New Padding(4)}
        btnDownload = Theme.MakeButton(Localizer.T("⬇  Download selected") & " (0)", primary:=True)
        btnDownload.Font = Theme.BoldFont
        btnCancel = Theme.MakeButton(Localizer.T("Cancel"))
        btnCancel.Visible = False
        lblStatus = Theme.MakeLabel(Localizer.T("Tick models and press Download. Shared parts (VAE, text encoder) are downloaded once and reused."), dim_:=True)
        AddHandler btnDownload.Click, Async Sub() Await DownloadSelectedAsync()
        AddHandler btnCancel.Click, Sub() _cts?.Cancel()
        actions.Controls.AddRange({btnDownload, btnCancel, bar, lblStatus})

        Dim split As New SplitContainer() With {.Dock = DockStyle.Fill, .Orientation = Orientation.Vertical, .FixedPanel = FixedPanel.Panel2,
                                                .BackColor = Theme.CBorder, .SplitterWidth = 3}
        split.Panel1.Controls.Add(grid)
        split.Panel2.Controls.Add(details)
        split.Panel1.BackColor = Theme.CPanel
        split.Panel2.BackColor = Theme.CPanel
        AddHandler split.HandleCreated, Sub()
                                            Try
                                                split.SplitterDistance = Math.Max(300, split.Width - 430)
                                            Catch
                                            End Try
                                        End Sub
        AddHandler details.VariantPicked, Sub(rr, label)
                                              _variantChoice(rr.Id) = label
                                              For Each row As DataGridViewRow In grid.Rows
                                                  If row.Tag Is rr Then
                                                      _suppress = True
                                                      row.Cells(grid.Columns("variant").Index).Value = label
                                                      _suppress = False
                                                      FillFit(row, rr)
                                                  End If
                                              Next
                                          End Sub
        Controls.Add(split)      ' (the model page on the right replaces the old one-line description)
        Controls.Add(lblInfo)
        Controls.Add(top)
        Controls.Add(actions)

        AddHandler cmbShow.SelectedIndexChanged, Sub() Fill()
        AddHandler cmbType.SelectedIndexChanged, Sub()
                                                     If Not _suppressType Then Fill()
                                                 End Sub
        AddHandler _filterTimer.Tick, Sub()
                                          _filterTimer.Stop()
                                          Fill()
                                      End Sub
        AddHandler txtFilter.TextChanged, Sub()
                                              _filterTimer.Stop()
                                              _filterTimer.Start()
                                          End Sub
        Fill()
    End Sub

    Private Shared Function TypeText(r As MediaRecipe) As String
        Dim l As New List(Of String)
        If r.IsUpscaler Then Return Localizer.T("🔍 Enlarge")
        If r.Kind = "tool" Then Return Localizer.T("🧰 Tool")
        If r.IsVideo Then
            If r.Can("create") Then l.Add(Localizer.T("🎬 Text→video"))
            If r.Can("animate") Then l.Add(Localizer.T("📷→🎬 Photo→video"))
            If r.Can("video-edit") Then l.Add(Localizer.T("🎞 Edit video"))
        Else
            If r.Can("create") Then l.Add(Localizer.T("🖼 Create"))
            If r.Can("edit") Then l.Add(Localizer.T("✏️ Edit"))
            If r.Can("inpaint") AndAlso Not r.Can("edit") Then l.Add(Localizer.T("🖌 Brush edit"))
        End If
        Return String.Join(" · ", l) & If(r.Uncensored, " · 🔓", "")
    End Function

    Private Function ChosenVariant(r As MediaRecipe) As MediaVariant
        Dim label As String = Nothing
        If _variantChoice.TryGetValue(r.Id, label) AndAlso label <> "Auto" Then
            Dim v = r.Variants.FirstOrDefault(Function(x) x.Label = label)
            If v IsNot Nothing Then Return v
        End If
        Return If(MediaStudio.InstalledVariant(r), MediaStudio.PickVariant(r, _hw))
    End Function

    Private Function SortKey(r As MediaRecipe) As IComparable
        Select Case _sortCol
            Case "name" : Return r.Name.ToLowerInvariant()
            Case "type" : Return TypeText(r)
            Case "family" : Return If(r.Family, "")
            Case "steps" : Return r.Steps + r.HighSteps
            Case "variant" : Return ChosenVariant(r).Label
            Case "size" : Return ChosenVariant(r).Total
            Case "fit" : Return CLng(MediaStudio.Fit(r, ChosenVariant(r), _hw)) * 1000000000000L + ChosenVariant(r).Total
            Case "src" : Return If(r.Repo, "")
            Case Else : Return r.Downloads
        End Select
    End Function

    Public Sub Fill()
        If optMusic.Checked Then
            FillAudio()
            Return
        End If
        grid.Columns("variant").Visible = True
        grid.Columns("steps").Visible = True
        Dim q = txtFilter.Text.Trim().ToLowerInvariant()
        Dim all = MediaStudio.Recipes().Where(Function(r) r.IsVideo = optVideos.Checked).ToList()
        Dim test = Types(Math.Max(0, cmbType.SelectedIndex)).Test
        Dim list As IEnumerable(Of MediaRecipe) = all.Where(test)
        Select Case cmbShow.SelectedIndex
            Case 0 : If q.Length = 0 Then list = list.Where(Function(r) (chkNsfw.Checked OrElse Not r.Uncensored) AndAlso (Not r.Auto OrElse r.Downloads >= 2000) AndAlso
                                                                  MediaStudio.Fit(r, MediaStudio.PickVariant(r, _hw), _hw) <> FitLevel.TooBig)
            Case 1 : If Not chkNsfw.Checked Then list = list.Where(Function(r) Not r.Uncensored)
            Case 2 : list = list.Where(Function(r) r.Uncensored)
            Case 3 : list = list.Where(Function(r) MediaStudio.InstalledVariant(r) IsNot Nothing)
        End Select
        ' (a PC without a graphics card: everything that runs)
        If chkFit.Checked Then list = list.Where(Function(r) If(_hw.VramBytes = 0, MediaStudio.Fit(r, ChosenVariant(r), _hw) <> FitLevel.TooBig,
                                                              MediaStudio.Fit(r, ChosenVariant(r), _hw) = FitLevel.Fits))
        If q.Length > 0 Then
            Dim words = q.Split(" "c, StringSplitOptions.RemoveEmptyEntries)
            list = list.Where(Function(r)
                                  Dim hay = (r.Name & " " & r.Family & " " & r.Repo & " " & r.Id & " " & String.Join(" ", r.Variants.Select(Function(v) v.Label & " " & v.Format))).ToLowerInvariant()
                                  Return words.All(Function(w) hay.Contains(w))
                              End Function)
        End If
        list = If(_sortDesc, list.OrderByDescending(AddressOf SortKey), list.OrderBy(AddressOf SortKey))
        Dim items = list.ToList()

        _suppress = True
        grid.SuspendLayout()
        grid.Rows.Clear()
        Dim rows As New List(Of DataGridViewRow)(items.Count)
        For Each r In items
            Dim row As New DataGridViewRow()
            row.CreateCells(grid)
            row.Tag = r
            Dim installed = MediaStudio.InstalledVariant(r) IsNot Nothing
            row.Cells(0).Value = _checked.Contains(r.Id)
            row.Cells(1).Value = If(installed, "✔ ", "") & r.Name & If(r.Speed IsNot Nothing, " ⚡", "")
            row.Cells(2).Value = TypeText(r)
            row.Cells(grid.Columns("steps").Index).Value = StepsText(r)
            row.Cells(grid.Columns("family").Index).Value = r.Family
            Dim cell = CType(row.Cells(grid.Columns("variant").Index), DataGridViewComboBoxCell)
            cell.Items.Add("Auto")
            For Each v In r.Variants
                cell.Items.Add(v.Label)
            Next
            Dim chosen As String = Nothing
            cell.Value = If(_variantChoice.TryGetValue(r.Id, chosen) AndAlso cell.Items.Contains(chosen), chosen, "Auto")
            row.Cells(grid.Columns("dl").Index).Value = FormatCount(r.Downloads)
            row.Cells(grid.Columns("src").Index).Value = If(r.Repo, "").Split("/"c)(0)
            FillFit(row, r)
            rows.Add(row)
        Next
        grid.Rows.AddRange(rows.ToArray())
        For Each c As DataGridViewColumn In grid.Columns
            If c.SortMode = DataGridViewColumnSortMode.Programmatic Then
                c.HeaderCell.SortGlyphDirection = If(c.Name <> _sortCol, SortOrder.None, If(_sortDesc, SortOrder.Descending, SortOrder.Ascending))
            End If
        Next
        grid.ResumeLayout()
        _suppress = False
        If items.Count = 0 AndAlso cmbShow.SelectedIndex = 0 AndAlso q.Length = 0 Then
            lblInfo.Text = Localizer.T("None of these fits this PC's graphics card — choose 'All models' above to see them all (they can use system RAM).")
            UpdateButton()
            SyncDetails()
            Return
        End If
        lblInfo.Text = String.Format(Localizer.T(If(optVideos.Checked, "Showing {0} of {1} video models · click a column title to sort", "Showing {0} of {1} picture models · click a column title to sort")), items.Count, all.Count)
        UpdateButton()
        SyncDetails()
    End Sub

    ''' <summary>The voice and music models (read aloud, voice cloning, songs) in the same list.</summary>
    Private Sub FillAudio()
        Dim q = txtFilter.Text.Trim().ToLowerInvariant()
        Dim test = If(_audioKind = "music", AudioTypes(1).Test, If(_audioKind = "voice", AudioTypes(2).Test, AudioTypes(0).Test))
        Dim all = AudioStudio.Models().Where(test).ToList()
        Dim list As IEnumerable(Of AudioModel) = all
        If cmbShow.SelectedIndex = 3 Then list = list.Where(AddressOf AudioStudio.IsInstalled)
        If cmbShow.SelectedIndex = 0 OrElse chkFit.Checked Then list = list.Where(Function(m) AudioStudio.Fit(m, _hw) <> FitLevel.TooBig OrElse AudioStudio.IsInstalled(m))
        If q.Length > 0 Then
            Dim words = q.Split(" "c, StringSplitOptions.RemoveEmptyEntries)
            list = list.Where(Function(m) words.All(Function(w) (m.Name & " " & m.Kind & " " & m.Model.Repo & " " & m.Id & " " & String.Join(" ", m.Langs)).ToLowerInvariant().Contains(w)))
        End If
        Dim key As Func(Of AudioModel, IComparable)
        Select Case _sortCol
            Case "name" : key = Function(m) m.Name.ToLowerInvariant()
            Case "type", "family" : key = Function(m) m.Kind
            Case "size" : key = Function(m) m.Size
            Case "fit" : key = Function(m) CLng(AudioStudio.Fit(m, _hw)) * 1000000000000L + m.Size
            Case "src" : key = Function(m) m.Model.Repo
            Case Else : key = Function(m) m.Downloads
        End Select
        Dim items = If(_sortDesc, list.OrderByDescending(key), list.OrderBy(key)).ToList()

        _suppress = True
        grid.SuspendLayout()
        grid.Rows.Clear()
        grid.Columns("variant").Visible = False
        grid.Columns("steps").Visible = False
        For Each m In items
            Dim row As New DataGridViewRow()
            row.CreateCells(grid)
            row.Tag = m
            Dim installed = AudioStudio.IsInstalled(m)
            row.Cells(0).Value = _checkedAudio.Contains(m.Id)
            row.Cells(1).Value = If(installed, "✔ ", "") & m.Name
            row.Cells(2).Value = If(m.IsMusic, Localizer.T("🎵 Songs"), Localizer.T("🗣 Voice") & If(m.Clone, " · " & Localizer.T("cloning"), ""))
            row.Cells(grid.Columns("family").Index).Value = If(m.IsMusic, Localizer.T("Music"), Localizer.T("Voice"))
            row.Cells(grid.Columns("size").Index).Value = Downloader.FormatBytes(AudioStudio.BytesToDownload(m))
            Dim fit = AudioStudio.Fit(m, _hw)
            row.Cells(grid.Columns("fit").Index).Value = ModelCatalog.FitText(fit, _hw.VramBytes = 0)
            row.Cells(grid.Columns("fit").Index).Style.ForeColor = ModelManagerForm.FitColor(fit)
            row.Cells(grid.Columns("dl").Index).Value = If(m.Downloads > 0, FormatCount(m.Downloads), "—")
            row.Cells(grid.Columns("src").Index).Value = m.Model.Repo.Split("/"c)(0)
            grid.Rows.Add(row)
        Next
        For Each c As DataGridViewColumn In grid.Columns
            If c.SortMode = DataGridViewColumnSortMode.Programmatic Then
                c.HeaderCell.SortGlyphDirection = If(c.Name <> _sortCol, SortOrder.None, If(_sortDesc, SortOrder.Descending, SortOrder.Ascending))
            End If
        Next
        grid.ResumeLayout()
        _suppress = False
        lblInfo.Text = String.Format(Localizer.T(If(_audioKind = "music", "Showing {0} of {1} music models · click a column title to sort", "Showing {0} of {1} voice models · click a column title to sort")), items.Count, all.Count)
        UpdateButton()
        SyncDetails()
    End Sub

    Private Sub FillFit(row As DataGridViewRow, r As MediaRecipe)
        Dim v = ChosenVariant(r)
        Dim fit = MediaStudio.Fit(r, v, _hw)
        row.Cells(grid.Columns("size").Index).Value = Downloader.FormatBytes(MediaStudio.BytesToDownload(r, v))
        row.Cells(grid.Columns("fit").Index).Value = ModelCatalog.FitText(fit, _hw.VramBytes = 0) & "  (" & v.Label & ")"
        row.Cells(grid.Columns("fit").Index).Style.ForeColor = ModelManagerForm.FitColor(fit)
        row.Cells(1).Style.ForeColor = If(fit = FitLevel.TooBig, Theme.CDim, Theme.CText)
    End Sub

    Private Shared Function FormatCount(n As Long) As String
        If n >= 1000000 Then Return (n / 1000000.0).ToString("0.#") & "M"
        If n >= 1000 Then Return (n / 1000.0).ToString("0.#") & "K"
        Return n.ToString()
    End Function

    Private Sub CellChanged(sender As Object, e As DataGridViewCellEventArgs)
        If _suppress OrElse e.RowIndex < 0 Then Return
        Dim row = grid.Rows(e.RowIndex)
        Dim am = TryCast(row.Tag, AudioModel)
        If am IsNot Nothing Then
            If e.ColumnIndex = 0 Then
                If CBool(row.Cells(0).Value) Then _checkedAudio.Add(am.Id) Else _checkedAudio.Remove(am.Id)
                UpdateButton()
            End If
            Return
        End If
        Dim r = TryCast(row.Tag, MediaRecipe)
        If r Is Nothing Then Return
        If e.ColumnIndex = 0 Then
            If CBool(row.Cells(0).Value) Then _checked.Add(r.Id) Else _checked.Remove(r.Id)
            UpdateButton()
        ElseIf grid.Columns(e.ColumnIndex).Name = "variant" Then
            _variantChoice(r.Id) = If(row.Cells(grid.Columns("variant").Index).Value?.ToString(), "Auto")
            FillFit(row, r)
            ShowAbout()
        End If
    End Sub

    Private Sub ShowAbout()
        Dim am = TryCast(grid.CurrentRow?.Tag, AudioModel)
        If am IsNot Nothing Then
            lblAbout.Text = am.About & "   " & AudioStudio.RequirementText(am, _hw)
            _shown = Nothing
            details.ShowAudio(am)
            Return
        End If
        Dim r = TryCast(grid.CurrentRow?.Tag, MediaRecipe)
        If r Is Nothing Then Return
        Dim v = ChosenVariant(r)
        lblAbout.Text = r.About & "   " & MediaStudio.RequirementText(r, v, _hw)
        If _shown IsNot r Then
            _shown = r
            details.ShowRecipe(r, v)
        End If
    End Sub

    Private _shown As MediaRecipe

    Private Shared Function StepsText(r As MediaRecipe) As String
        If r.IsUpscaler OrElse r.Kind = "tool" Then Return "—"
        Dim s = If(r.HighSteps > 0, $"{r.HighSteps}+{r.Steps}", r.Steps.ToString())
        If r.Speed IsNot Nothing Then s &= $" (⚡{If(r.Speed.HighSteps > 0, r.Speed.HighSteps & "+", "")}{r.Speed.Steps})"
        Return s
    End Function

    Private Sub UpdateButton()
        Dim n = _checked.Count + _checkedAudio.Count
        btnDownload.Text = Localizer.T("⬇  Download selected") & $" ({n})"
        btnDownload.Enabled = _cts Is Nothing
    End Sub

    ''' <summary>
    ''' Downloads everything ticked in the picture, video and music lists at the same time (three at once;
    ''' a part two models share is fetched only once). One failing model does not stop the others.
    ''' </summary>
    Private Async Function DownloadSelectedAsync() As Task
        Dim jobs As New List(Of (Name As String, Work As Func(Of Action(Of Integer, String), CancellationToken, Task), Done As Action))
        For Each r In MediaStudio.Recipes().Where(Function(x) _checked.Contains(x.Id)).ToList()
            Dim rr = r, v = ChosenVariant(r)
            jobs.Add((rr.Name, Function(p, c) MediaStudio.DownloadAsync(rr, v, p, c, forEdit:=rr.Can("edit")), Sub() _checked.Remove(rr.Id)))
        Next
        For Each m In AudioStudio.Models().Where(Function(x) _checkedAudio.Contains(x.Id)).ToList()
            Dim mm = m
            jobs.Add((mm.Name, Function(p, c) AudioStudio.DownloadAsync(mm, p, c), Sub() _checkedAudio.Remove(mm.Id)))
        Next
        If jobs.Count = 0 Then
            Localizer.Box(Me, "Tick one or more models first (in any of the picture, video or music lists).", "Download")
            Return
        End If
        _cts = New CancellationTokenSource()
        Dim ct = _cts.Token
        btnDownload.Enabled = False
        btnCancel.Visible = True
        bar.Visible = True
        bar.Value = 0
        Dim pct(jobs.Count - 1) As Integer
        Dim lines(jobs.Count - 1) As String
        Dim ok = 0, failed As New List(Of String)
        Dim gate As New SemaphoreSlim(3, 3)
        Dim report = Sub()
                         Dim running = Enumerable.Range(0, jobs.Count).Where(Function(i) pct(i) > 0 AndAlso pct(i) < 100).ToList()
                         Dim all = CInt(pct.Sum() / jobs.Count)
                         Me.Post(Sub()
                                     bar.Value = Math.Max(0, Math.Min(100, all))
                                     lblStatus.Text = String.Format(Localizer.T("{0} of {1} ready · downloading: {2}"), ok, jobs.Count,
                                                                    If(running.Count = 0, "…", String.Join("  |  ", running.Select(Function(i) $"{jobs(i).Name} {pct(i)}%"))))
                                 End Sub)
                     End Sub
        Dim tasks = Enumerable.Range(0, jobs.Count).Select(
            Async Function(i)
                Await gate.WaitAsync(ct)
                Try
                    Await jobs(i).Work(Sub(p, t)
                                           pct(i) = Math.Max(1, Math.Min(99, p))
                                           report()
                                       End Sub, ct)
                    pct(i) = 100
                    Interlocked.Increment(ok)
                    Me.Post(jobs(i).Done)
                Catch ex As OperationCanceledException
                Catch ex As Exception
                    pct(i) = 100
                    SyncLock failed
                        failed.Add(jobs(i).Name & ": " & ex.Message)
                    End SyncLock
                    Safety.Log(ex, "Model download: " & jobs(i).Name)
                Finally
                    gate.Release()
                    report()
                End Try
            End Function).ToList()
        Try
            Await Task.WhenAll(tasks)
            If ct.IsCancellationRequested Then
                lblStatus.Text = Localizer.T("Download cancelled.")
            ElseIf failed.Count > 0 Then
                lblStatus.Text = String.Format(Localizer.T("{0} ready, {1} failed."), ok, failed.Count) & "  ⚠ " & failed(0)
            Else
                lblStatus.Text = String.Format(Localizer.T("Done: {0} model(s) ready."), ok)
            End If
        Catch ex As OperationCanceledException
            lblStatus.Text = Localizer.T("Download cancelled.")
        Finally
            btnCancel.Visible = False
            bar.Visible = False
            _cts = Nothing
            btnDownload.Enabled = True
            Fill()
            RaiseEvent Downloaded()
        End Try
    End Function
End Class
