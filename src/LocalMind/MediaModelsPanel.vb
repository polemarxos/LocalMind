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
    Private ReadOnly _filterTimer As New System.Windows.Forms.Timer() With {.Interval = 250}

    ''' <summary>Raised after downloads, so the Studio can refresh.</summary>
    Public Event Downloaded()

    Private Shared ReadOnly Types As (Label As String, Test As Func(Of MediaRecipe, Boolean))() = {
        ("All types", Function(r) True),
        ("🖼 Create pictures", Function(r) Not r.IsVideo AndAlso r.Can("create")),
        ("✏️ Edit pictures", Function(r) Not r.IsVideo AndAlso (r.Can("edit") OrElse r.Can("inpaint"))),
        ("🎬 Make videos", Function(r) r.IsVideo AndAlso (r.Can("create") OrElse r.Can("animate"))),
        ("🎞 Edit videos", Function(r) r.Can("video-edit")),
        ("🔍 Enlarge / tools", Function(r) r.IsUpscaler OrElse r.Kind = "tool")}

    Public Sub New()
        Dock = DockStyle.Fill
        BackColor = Theme.CPanel

        Dim top As New FlowLayoutPanel() With {.Dock = DockStyle.Top, .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
                                               .WrapContents = True, .Padding = New Padding(4, 4, 4, 2)}
        cmbShow.Items.AddRange({Localizer.T("★ Best for my GPU"), Localizer.T("All models (show everything)"), Localizer.T("🔓 Uncensored"), Localizer.T("✔ Installed")})
        cmbShow.SelectedIndex = 0
        Theme.StyleInput(cmbShow)
        cmbType.Items.AddRange(Types.Select(Function(t) Localizer.T(t.Label)).ToArray())
        cmbType.SelectedIndex = 0
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
        top.Controls.AddRange({cmbShow, cmbType, txtFilter, btnStudio})
        lblInfo = Theme.MakeLabel("", dim_:=True)
        lblInfo.Dock = DockStyle.Top
        lblInfo.AutoSize = False
        lblInfo.Height = 22
        lblInfo.Padding = New Padding(8, 3, 0, 0)

        Theme.StyleGrid(grid)
        grid.Columns.Add(New DataGridViewCheckBoxColumn() With {.Name = "sel", .HeaderText = "", .Width = 32})
        grid.Columns.Add(New DataGridViewTextBoxColumn() With {.Name = "name", .HeaderText = Localizer.T("Model"), .ReadOnly = True, .AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, .MinimumWidth = 220})
        grid.Columns.Add(New DataGridViewTextBoxColumn() With {.Name = "type", .HeaderText = Localizer.T("Can do"), .ReadOnly = True, .Width = 170})
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

        Controls.Add(grid)
        Controls.Add(lblAbout)
        Controls.Add(lblInfo)
        Controls.Add(top)
        Controls.Add(actions)

        AddHandler cmbShow.SelectedIndexChanged, Sub() Fill()
        AddHandler cmbType.SelectedIndexChanged, Sub() Fill()
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
            Case "variant" : Return ChosenVariant(r).Label
            Case "size" : Return ChosenVariant(r).Total
            Case "fit" : Return CLng(MediaStudio.Fit(r, ChosenVariant(r), _hw)) * 1000000000000L + ChosenVariant(r).Total
            Case "src" : Return If(r.Repo, "")
            Case Else : Return r.Downloads
        End Select
    End Function

    Public Sub Fill()
        Dim q = txtFilter.Text.Trim().ToLowerInvariant()
        Dim all = MediaStudio.Recipes()
        Dim test = Types(Math.Max(0, cmbType.SelectedIndex)).Test
        Dim list As IEnumerable(Of MediaRecipe) = all.Where(test)
        Select Case cmbShow.SelectedIndex
            Case 0 : If q.Length = 0 Then list = list.Where(Function(r) Not r.Uncensored AndAlso (Not r.Auto OrElse r.Downloads >= 2000) AndAlso
                                                                  MediaStudio.Fit(r, MediaStudio.PickVariant(r, _hw), _hw) <> FitLevel.TooBig)
            Case 2 : list = list.Where(Function(r) r.Uncensored)
            Case 3 : list = list.Where(Function(r) MediaStudio.InstalledVariant(r) IsNot Nothing)
        End Select
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
            row.Cells(3).Value = r.Family
            Dim cell = CType(row.Cells(4), DataGridViewComboBoxCell)
            cell.Items.Add("Auto")
            For Each v In r.Variants
                cell.Items.Add(v.Label)
            Next
            Dim chosen As String = Nothing
            cell.Value = If(_variantChoice.TryGetValue(r.Id, chosen) AndAlso cell.Items.Contains(chosen), chosen, "Auto")
            row.Cells(7).Value = FormatCount(r.Downloads)
            row.Cells(8).Value = If(r.Repo, "").Split("/"c)(0)
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
        lblInfo.Text = String.Format(Localizer.T("Showing {0} of {1} picture and video models · click a column title to sort"), items.Count, all.Count)
        UpdateButton()
    End Sub

    Private Sub FillFit(row As DataGridViewRow, r As MediaRecipe)
        Dim v = ChosenVariant(r)
        Dim fit = MediaStudio.Fit(r, v, _hw)
        row.Cells(5).Value = Downloader.FormatBytes(MediaStudio.BytesToDownload(r, v))
        row.Cells(6).Value = ModelCatalog.FitText(fit, _hw.VramBytes = 0) & "  (" & v.Label & ")"
        row.Cells(6).Style.ForeColor = ModelManagerForm.FitColor(fit)
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
        Dim r = TryCast(row.Tag, MediaRecipe)
        If r Is Nothing Then Return
        If e.ColumnIndex = 0 Then
            If CBool(row.Cells(0).Value) Then _checked.Add(r.Id) Else _checked.Remove(r.Id)
            UpdateButton()
        ElseIf e.ColumnIndex = 4 Then
            _variantChoice(r.Id) = If(row.Cells(4).Value?.ToString(), "Auto")
            FillFit(row, r)
            ShowAbout()
        End If
    End Sub

    Private Sub ShowAbout()
        Dim r = TryCast(grid.CurrentRow?.Tag, MediaRecipe)
        If r Is Nothing Then Return
        Dim v = ChosenVariant(r)
        lblAbout.Text = r.About & "   " & MediaStudio.RequirementText(r, v, _hw)
    End Sub

    Private Sub UpdateButton()
        btnDownload.Text = Localizer.T("⬇  Download selected") & $" ({_checked.Count})"
    End Sub

    Private Async Function DownloadSelectedAsync() As Task
        Dim picks = MediaStudio.Recipes().Where(Function(r) _checked.Contains(r.Id)).ToList()
        If picks.Count = 0 Then
            Localizer.Box(Me, "Tick one or more models first.", "Download")
            Return
        End If
        _cts = New CancellationTokenSource()
        btnDownload.Enabled = False
        btnCancel.Visible = True
        bar.Visible = True
        Dim done = 0
        Try
            For Each r In picks
                Dim v = ChosenVariant(r)
                Dim name = r.Name
                Await MediaStudio.DownloadAsync(r, v,
                    Sub(p, t) BeginInvoke(Sub()
                                              bar.Value = Math.Max(0, Math.Min(100, p))
                                              lblStatus.Text = $"{done + 1}/{picks.Count} · {name} · {Localizer.T(t)}"
                                          End Sub),
                    _cts.Token, forEdit:=r.Can("edit"))
                done += 1
                _checked.Remove(r.Id)
            Next
            lblStatus.Text = String.Format(Localizer.T("Done: {0} model(s) ready in the Pictures & video studio."), done)
        Catch ex As OperationCanceledException
            lblStatus.Text = Localizer.T("Download cancelled.")
        Catch ex As Exception
            lblStatus.Text = "⚠ " & ex.Message
        Finally
            btnDownload.Enabled = True
            btnCancel.Visible = False
            bar.Visible = False
            _cts = Nothing
            Fill()
            RaiseEvent Downloaded()
        End Try
    End Function
End Class
