Imports System
Imports System.Drawing
Imports System.IO
Imports System.Linq
Imports System.Windows.Forms

''' <summary>Your LoRAs: tick the ones to use, set strength and trigger words. Shows the LoRAs for one model family.</summary>
Public Class LoraForm
    Inherits Form

    Private ReadOnly _family As String
    Private ReadOnly grid As DataGridView
    Private ReadOnly chkAll As CheckBox

    Public Sub New(family As String, modelName As String)
        _family = family
        Text = Localizer.T("My LoRAs") & If(String.IsNullOrEmpty(modelName), "", " — " & modelName)
        Size = New Size(980, 560)
        StartPosition = FormStartPosition.CenterParent
        Theme.ApplyForm(Me)

        Dim top As New FlowLayoutPanel() With {.Dock = DockStyle.Top, .Height = 46, .Padding = New Padding(6)}
        Dim btnAdd = Theme.MakeButton("➕ Add LoRA files…", primary:=True, width:=170)
        Dim btnFolder = Theme.MakeButton("📁 Open LoRA folder")
        Dim btnDel = Theme.MakeButton("🗑 Delete")
        Dim btnRead = Theme.MakeButton("↻ Read trigger words again")
        chkAll = New CheckBox() With {.Text = Localizer.T("Show LoRAs for all models"), .AutoSize = True, .ForeColor = Theme.CText, .Margin = New Padding(12, 8, 0, 0)}
        Dim btnCiv = Theme.MakeButton("🌐 Find on Civitai…")
        top.Controls.AddRange({btnAdd, btnCiv, btnFolder, btnDel, btnRead, chkAll})
        AddHandler btnCiv.Click, Sub()
                                     SaveRows()
                                     Using f As New CivitaiForm(checkpoints:=False, family:=_family)
                                         f.ShowDialog(Me)
                                     End Using
                                     Fill()
                                 End Sub

        Dim info As New Label() With {.Dock = DockStyle.Bottom, .Height = 52, .ForeColor = Theme.CDim, .Padding = New Padding(8, 4, 8, 4),
            .Text = Localizer.T("Tick a LoRA to use it. Its trigger words are added to your prompt automatically (edit them here). " &
                                "Strength 1.0 is normal; lower it if the style is too strong. Files you copy into the LoRA folder appear here too.")}

        grid = New DataGridView() With {.Dock = DockStyle.Fill, .AllowUserToAddRows = False, .AllowUserToDeleteRows = False, .SelectionMode = DataGridViewSelectionMode.FullRowSelect}
        Theme.StyleGrid(grid)
        grid.ReadOnly = False
        grid.Columns.Add(New DataGridViewCheckBoxColumn() With {.Name = "use", .HeaderText = Localizer.T("Use"), .Width = 45})
        grid.Columns.Add(New DataGridViewTextBoxColumn() With {.Name = "name", .HeaderText = Localizer.T("LoRA"), .ReadOnly = True, .AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill})
        Dim famCol As New DataGridViewComboBoxColumn() With {.Name = "family", .HeaderText = Localizer.T("For model"), .Width = 140, .FlatStyle = FlatStyle.Flat}
        famCol.Items.AddRange(LoraLibrary.Families.Select(Function(f) If(f = "", Localizer.T("(any)"), f)).Cast(Of Object).ToArray())
        grid.Columns.Add(famCol)
        grid.Columns.Add(New DataGridViewTextBoxColumn() With {.Name = "trig", .HeaderText = Localizer.T("Trigger words"), .Width = 300})
        grid.Columns.Add(New DataGridViewTextBoxColumn() With {.Name = "w", .HeaderText = Localizer.T("Strength"), .Width = 70})

        Controls.Add(grid)
        Controls.Add(info)
        Controls.Add(top)

        AddHandler btnAdd.Click, Sub() AddFiles()
        AddHandler btnFolder.Click, Sub() ModelManagerForm.OpenFolder(LoraLibrary.Folder)
        AddHandler btnDel.Click, Sub() DeleteSelected()
        AddHandler btnRead.Click, Sub()
                                      For Each row As DataGridViewRow In grid.SelectedRows
                                          Dim l = CType(row.Tag, UserLora)
                                          Dim meta = LoraLibrary.ReadMetadata(l.FullPath)
                                          row.Cells("trig").Value = meta.Triggers
                                          If meta.Family <> "" Then row.Cells("family").Value = meta.Family
                                      Next
                                  End Sub
        AddHandler chkAll.CheckedChanged, Sub()
                                              SaveRows()
                                              Fill()
                                          End Sub
        AddHandler grid.CurrentCellDirtyStateChanged, Sub() If grid.IsCurrentCellDirty Then grid.CommitEdit(DataGridViewDataErrorContexts.Commit)
        AddHandler grid.DataError, Sub(s, e) e.ThrowException = False
        AllowDrop = True
        AddHandler DragEnter, Sub(s, e) e.Effect = If(e.Data.GetDataPresent(DataFormats.FileDrop), DragDropEffects.Copy, DragDropEffects.None)
        AddHandler DragDrop, Sub(s, e)
                                 SaveRows()
                                 ImportFiles(CType(e.Data.GetData(DataFormats.FileDrop), String()))
                             End Sub
        Fill()
    End Sub

    Private Sub Fill()
        grid.Rows.Clear()
        Dim list = If(chkAll.Checked OrElse String.IsNullOrEmpty(_family), LoraLibrary.All(), LoraLibrary.ForFamily(_family))
        For Each l In list
            Dim i = grid.Rows.Add(l.Enabled, l.Name, If(l.Family = "", Localizer.T("(any)"), l.Family), l.Triggers, l.Weight.ToString("0.##", Globalization.CultureInfo.InvariantCulture))
            grid.Rows(i).Tag = l
        Next
    End Sub

    Private Sub SaveRows()
        For Each row As DataGridViewRow In grid.Rows
            Dim l = CType(row.Tag, UserLora)
            l.Enabled = CBool(If(row.Cells("use").Value, False))
            Dim fam = CStr(row.Cells("family").Value)
            l.Family = If(fam = Localizer.T("(any)") OrElse fam Is Nothing, "", fam)
            l.Triggers = If(CStr(row.Cells("trig").Value), "").Trim()
            Dim w As Double
            If Double.TryParse(CStr(row.Cells("w").Value)?.Replace(","c, "."c), Globalization.NumberStyles.Float, Globalization.CultureInfo.InvariantCulture, w) Then
                l.Weight = Math.Max(-2, Math.Min(3, w))
            End If
        Next
        LoraLibrary.Save()
    End Sub

    Private Sub AddFiles()
        Using d As New OpenFileDialog() With {.Filter = "LoRA|*.safetensors;*.ckpt;*.gguf", .Multiselect = True}
            If d.ShowDialog(Me) = DialogResult.OK Then
                SaveRows()
                ImportFiles(d.FileNames)
            End If
        End Using
    End Sub

    Private Sub ImportFiles(files As String())
        Cursor = Cursors.WaitCursor
        Try
            For Each f In files
                Dim l = LoraLibrary.Import(f)
                If l IsNot Nothing Then
                    l.Enabled = True
                    If l.Family = "" AndAlso Not String.IsNullOrEmpty(_family) Then l.Family = _family
                End If
            Next
            LoraLibrary.Save()
        Catch ex As Exception
            Localizer.Box(Me, ex.Message, "LoRA", MessageBoxButtons.OK, MessageBoxIcon.Warning)
        Finally
            Cursor = Cursors.Default
        End Try
        Fill()
    End Sub

    Private Sub DeleteSelected()
        If grid.SelectedRows.Count = 0 Then Return
        If Localizer.Box(Me, "Delete the selected LoRA files from disk?", "Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) <> DialogResult.Yes Then Return
        For Each row As DataGridViewRow In grid.SelectedRows
            LoraLibrary.Delete(CType(row.Tag, UserLora))
        Next
        Fill()
    End Sub

    Protected Overrides Sub OnFormClosing(e As FormClosingEventArgs)
        grid.EndEdit()
        SaveRows()
        MyBase.OnFormClosing(e)
    End Sub
End Class
