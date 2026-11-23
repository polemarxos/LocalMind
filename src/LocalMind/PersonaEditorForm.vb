Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.Linq
Imports System.Windows.Forms

Public Class PersonaEditorForm
    Inherits Form

    Private lst As ListBox
    Private txtName As TextBox
    Private txtPrompt As TextBox
    Private numTemp As NumericUpDown
    Private btnAdd As Button
    Private btnDelete As Button
    Private btnOK As Button
    Private btnCancel As Button
    Private _working As List(Of Persona)
    Private _suppressListEvents As Boolean
    Private _saved As Boolean = False

    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        Localizer.Apply(Me)
    End Sub

    Public Sub New(personas As List(Of Persona))
        Try
            Me.Icon = Theme.AppIcon()
        Catch
        End Try

        _working = New List(Of Persona)()
        If personas IsNot Nothing Then
            For Each p In personas
                _working.Add(New Persona With {
                    .Id = p.Id,
                    .Name = p.Name,
                    .SystemPrompt = p.SystemPrompt,
                    .Temperature = p.Temperature})
            Next
        End If

        Text = "Personas"
        Size = New Size(780, 560)
        MinimumSize = New Size(620, 460)
        StartPosition = FormStartPosition.CenterParent
        BackColor = Color.FromArgb(37, 37, 38)
        ForeColor = Color.FromArgb(212, 212, 212)
        Font = New Font("Segoe UI", 9.5!)

        lst = New ListBox() With {
            .Left = 12, .Top = 12, .Width = 200, .Height = 440,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Bottom Or AnchorStyles.Left,
            .BackColor = Color.FromArgb(30, 30, 30),
            .ForeColor = Color.FromArgb(212, 212, 212),
            .BorderStyle = BorderStyle.FixedSingle,
            .IntegralHeight = False
        }
        AddHandler lst.SelectedIndexChanged, AddressOf OnSelect

        btnAdd = New Button() With {
            .Text = "＋ Add", .Left = 12, .Top = 460, .Width = 96, .Height = 30,
            .Anchor = AnchorStyles.Bottom Or AnchorStyles.Left,
            .FlatStyle = FlatStyle.Flat,
            .BackColor = Color.FromArgb(0, 122, 204),
            .ForeColor = Color.White
        }
        btnAdd.FlatAppearance.BorderSize = 0
        AddHandler btnAdd.Click, AddressOf OnAdd

        btnDelete = New Button() With {
            .Text = "Delete", .Left = 116, .Top = 460, .Width = 96, .Height = 30,
            .Anchor = AnchorStyles.Bottom Or AnchorStyles.Left,
            .FlatStyle = FlatStyle.Flat,
            .BackColor = Color.FromArgb(60, 60, 60),
            .ForeColor = Color.White
        }
        btnDelete.FlatAppearance.BorderSize = 0
        AddHandler btnDelete.Click, AddressOf OnDelete

        Dim lblName As New Label() With {
            .Text = "Name", .Left = 228, .Top = 14, .Width = 400,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right,
            .ForeColor = Color.FromArgb(180, 180, 180)
        }

        txtName = New TextBox() With {
            .Left = 228, .Top = 36, .Width = 524, .Height = 24,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right,
            .BackColor = Color.FromArgb(30, 30, 30),
            .ForeColor = Color.FromArgb(220, 220, 220),
            .BorderStyle = BorderStyle.FixedSingle
        }
        AddHandler txtName.TextChanged,
            Sub()
                If _suppressListEvents Then Return
                Dim i = lst.SelectedIndex
                If i < 0 Then Return
                _working(i).Name = txtName.Text
                _suppressListEvents = True
                lst.Items(i) = If(String.IsNullOrWhiteSpace(txtName.Text), "(unnamed)", txtName.Text)
                _suppressListEvents = False
            End Sub

        Dim lblPrompt As New Label() With {
            .Text = "System prompt", .Left = 228, .Top = 74, .Width = 400,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right,
            .ForeColor = Color.FromArgb(180, 180, 180)
        }

        txtPrompt = New TextBox() With {
            .Left = 228, .Top = 96, .Width = 524, .Height = 320,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Bottom Or AnchorStyles.Left Or AnchorStyles.Right,
            .Multiline = True, .ScrollBars = ScrollBars.Vertical,
            .BackColor = Color.FromArgb(30, 30, 30),
            .ForeColor = Color.FromArgb(220, 220, 220),
            .BorderStyle = BorderStyle.FixedSingle,
            .AcceptsReturn = True
        }
        AddHandler txtPrompt.TextChanged,
            Sub()
                Dim i = lst.SelectedIndex
                If i < 0 Then Return
                _working(i).SystemPrompt = txtPrompt.Text
            End Sub

        Dim lblTemp As New Label() With {
            .Text = "Temperature", .Left = 228, .Top = 426, .Width = 100,
            .Anchor = AnchorStyles.Bottom Or AnchorStyles.Left,
            .ForeColor = Color.FromArgb(180, 180, 180)
        }

        numTemp = New NumericUpDown() With {
            .Left = 340, .Top = 424, .Width = 80,
            .Anchor = AnchorStyles.Bottom Or AnchorStyles.Left,
            .DecimalPlaces = 1, .Increment = 0.1D,
            .Minimum = 0D, .Maximum = 2D,
            .BackColor = Color.FromArgb(30, 30, 30),
            .ForeColor = Color.FromArgb(220, 220, 220),
            .BorderStyle = BorderStyle.FixedSingle
        }
        AddHandler numTemp.ValueChanged,
            Sub()
                Dim i = lst.SelectedIndex
                If i < 0 Then Return
                _working(i).Temperature = CDbl(numTemp.Value)
            End Sub

        Dim lblHint As New Label() With {
            .Text = "Lower = more precise. Higher = more creative.",
            .Left = 430, .Top = 428, .Width = 320,
            .Anchor = AnchorStyles.Bottom Or AnchorStyles.Left,
            .ForeColor = Color.FromArgb(120, 120, 120),
            .Font = New Font("Segoe UI", 8.25!)
        }

        btnOK = New Button() With {
            .Text = "OK", .Left = 568, .Top = 460, .Width = 90, .Height = 30,
            .Anchor = AnchorStyles.Bottom Or AnchorStyles.Right,
            .FlatStyle = FlatStyle.Flat,
            .BackColor = Color.FromArgb(0, 122, 204), .ForeColor = Color.White
        }
        btnOK.FlatAppearance.BorderSize = 0
        AddHandler btnOK.Click, AddressOf OnOK

        btnCancel = New Button() With {
            .Text = "Cancel", .Left = 662, .Top = 460, .Width = 90, .Height = 30,
            .Anchor = AnchorStyles.Bottom Or AnchorStyles.Right,
            .FlatStyle = FlatStyle.Flat,
            .BackColor = Color.FromArgb(60, 60, 60), .ForeColor = Color.White
        }
        btnCancel.FlatAppearance.BorderSize = 0
        AddHandler btnCancel.Click, Sub() DialogResult = DialogResult.Cancel

        AcceptButton = btnOK
        CancelButton = btnCancel

        Controls.AddRange(New Control() {
            lst, btnAdd, btnDelete,
            lblName, txtName,
            lblPrompt, txtPrompt,
            lblTemp, numTemp, lblHint,
            btnOK, btnCancel})

        ReloadList()
    End Sub

    ' ★ Save directly to disk. Only set DialogResult.OK if the save succeeded.
    Private Sub OnOK()
        CommitPendingFields()

        Try
            Dim svc As New PersonaService()
            svc.Save(_working)
        Catch ex As Exception
            Localizer.Box("Could not save personas:" & vbCrLf & vbCrLf & ex.Message,
                            "Save failed", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End Try

        _saved = True
        DialogResult = DialogResult.OK
    End Sub

    Private Sub CommitPendingFields()
        Dim i = lst.SelectedIndex
        If i < 0 OrElse i >= _working.Count Then Return
        _working(i).Name = txtName.Text
        _working(i).SystemPrompt = txtPrompt.Text
        _working(i).Temperature = CDbl(numTemp.Value)
    End Sub

    Private Sub ReloadList()
        _suppressListEvents = True
        Dim prev = lst.SelectedIndex
        lst.Items.Clear()
        For Each p In _working
            lst.Items.Add(If(String.IsNullOrWhiteSpace(p.Name), "(unnamed)", p.Name))
        Next
        _suppressListEvents = False
        If lst.Items.Count > 0 Then
            lst.SelectedIndex = Math.Min(Math.Max(prev, 0), lst.Items.Count - 1)
        Else
            ClearFields()
        End If
    End Sub

    Private Sub ClearFields()
        _suppressListEvents = True
        txtName.Text = ""
        txtPrompt.Text = ""
        numTemp.Value = 0.7D
        _suppressListEvents = False
    End Sub

    Private Sub OnSelect(sender As Object, e As EventArgs)
        If _suppressListEvents Then Return
        Dim i = lst.SelectedIndex
        If i < 0 OrElse i >= _working.Count Then
            ClearFields()
            Return
        End If
        _suppressListEvents = True
        txtName.Text = _working(i).Name
        txtPrompt.Text = _working(i).SystemPrompt
        numTemp.Value = CDec(_working(i).Temperature)
        _suppressListEvents = False
    End Sub

    Private Sub OnAdd(sender As Object, e As EventArgs)
        Dim baseName = "New persona"
        Dim name = baseName
        Dim n = 2
        While _working.Any(Function(p) String.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
            name = baseName & " " & n
            n += 1
        End While

        _working.Add(New Persona With {
            .Name = name, .SystemPrompt = "", .Temperature = 0.7})
        ReloadList()
        lst.SelectedIndex = _working.Count - 1
        txtName.Focus()
        txtName.SelectAll()
    End Sub

    Private Sub OnDelete(sender As Object, e As EventArgs)
        Dim i = lst.SelectedIndex
        If i < 0 Then Return
        Dim name = If(String.IsNullOrWhiteSpace(_working(i).Name), "(unnamed)", _working(i).Name)
        Dim ans = Localizer.Box("Delete persona """ & name & """?",
                                  "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Question)
        If ans <> DialogResult.Yes Then Return
        _working.RemoveAt(i)
        ReloadList()
    End Sub
End Class