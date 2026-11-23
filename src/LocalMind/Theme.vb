Imports System.Drawing
Imports System.Windows.Forms

''' <summary>Shared dark styling so every window matches the main form.</summary>
Public Module Theme
    Public ReadOnly CBack As Color = Color.FromArgb(30, 30, 30)
    Public ReadOnly CPanel As Color = Color.FromArgb(37, 37, 38)
    Public ReadOnly CRaised As Color = Color.FromArgb(45, 45, 48)
    Public ReadOnly CControl As Color = Color.FromArgb(60, 60, 60)
    Public ReadOnly CText As Color = Color.FromArgb(212, 212, 212)
    Public ReadOnly CDim As Color = Color.FromArgb(150, 150, 150)
    Public ReadOnly CAccent As Color = Color.FromArgb(0, 122, 204)
    Public ReadOnly CGood As Color = Color.FromArgb(110, 200, 120)
    Public ReadOnly CWarn As Color = Color.FromArgb(230, 180, 80)
    Public ReadOnly CBad As Color = Color.FromArgb(220, 100, 100)
    Public ReadOnly UiFont As New Font("Segoe UI", 9.5!)
    Public ReadOnly BoldFont As New Font("Segoe UI", 9.5!, FontStyle.Bold)
    Public ReadOnly SmallFont As New Font("Segoe UI", 8.5!)

    Public Sub ApplyForm(f As Form)
        f.BackColor = CPanel
        f.ForeColor = CText
        f.Font = UiFont
        ' Translate every window once its controls exist.
        AddHandler f.Load, Sub() Localizer.Apply(f)
        Try
            f.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath)
        Catch
        End Try
    End Sub

    Public Function MakeButton(caption As String, Optional primary As Boolean = False, Optional width As Integer = 0) As Button
        Dim b As New Button() With {
            .Text = caption,
            .FlatStyle = FlatStyle.Flat,
            .BackColor = If(primary, CAccent, CControl),
            .ForeColor = Color.White,
            .Height = 30,
            .AutoSize = (width = 0),
            .AutoSizeMode = AutoSizeMode.GrowOnly,
            .Margin = New Padding(3),
            .Padding = New Padding(8, 0, 8, 0),
            .Cursor = Cursors.Hand
        }
        If width > 0 Then b.Width = width
        b.FlatAppearance.BorderSize = 0
        Return b
    End Function

    Public Function MakeLabel(caption As String, Optional bold As Boolean = False, Optional dim_ As Boolean = False) As Label
        Return New Label() With {
            .Text = caption, .AutoSize = True,
            .Font = If(bold, BoldFont, UiFont),
            .ForeColor = If(dim_, CDim, CText),
            .Margin = New Padding(3, 8, 3, 3)
        }
    End Function

    Public Sub StyleInput(c As Control)
        c.BackColor = CBack
        c.ForeColor = CText
        If TypeOf c Is TextBox Then CType(c, TextBox).BorderStyle = BorderStyle.FixedSingle
        If TypeOf c Is ComboBox Then CType(c, ComboBox).FlatStyle = FlatStyle.Flat
    End Sub

    Public Sub StyleGrid(g As DataGridView)
        g.BackgroundColor = CBack
        g.BorderStyle = BorderStyle.None
        g.GridColor = Color.FromArgb(50, 50, 52)
        g.EnableHeadersVisualStyles = False
        g.ColumnHeadersDefaultCellStyle.BackColor = CRaised
        g.ColumnHeadersDefaultCellStyle.ForeColor = CText
        g.ColumnHeadersDefaultCellStyle.SelectionBackColor = CRaised
        g.ColumnHeadersDefaultCellStyle.Font = BoldFont
        g.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single
        g.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing
        g.ColumnHeadersHeight = 30
        g.DefaultCellStyle.BackColor = CBack
        g.DefaultCellStyle.ForeColor = CText
        g.DefaultCellStyle.SelectionBackColor = Color.FromArgb(9, 71, 113)
        g.DefaultCellStyle.SelectionForeColor = Color.White
        g.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(35, 35, 36)
        g.RowHeadersVisible = False
        g.AllowUserToAddRows = False
        g.AllowUserToDeleteRows = False
        g.AllowUserToResizeRows = False
        g.SelectionMode = DataGridViewSelectionMode.FullRowSelect
        g.MultiSelect = False
        g.RowTemplate.Height = 26
        g.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal
        ' Double buffering removes flicker when progress updates repaint rows.
        GetType(DataGridView).GetProperty("DoubleBuffered", Reflection.BindingFlags.Instance Or Reflection.BindingFlags.NonPublic)?.SetValue(g, True)
    End Sub

    Public Sub StyleListView(l As ListView)
        l.BackColor = CBack
        l.ForeColor = CText
        l.BorderStyle = BorderStyle.FixedSingle
        l.FullRowSelect = True
        l.View = View.Details
        l.HideSelection = False
    End Sub

    ''' <summary>Simple dark tab control that owner-draws its headers.</summary>
    Public Sub StyleTabs(t As TabControl)
        t.DrawMode = TabDrawMode.OwnerDrawFixed
        t.SizeMode = TabSizeMode.Fixed
        t.ItemSize = New Size(170, 30)
        AddHandler t.DrawItem,
            Sub(s, e)
                Dim selected = (e.Index = t.SelectedIndex)
                Using b As New SolidBrush(If(selected, CAccent, CRaised))
                    e.Graphics.FillRectangle(b, e.Bounds)
                End Using
                TextRenderer.DrawText(e.Graphics, t.TabPages(e.Index).Text, BoldFont, e.Bounds, Color.White,
                                      TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
            End Sub
        For Each p As TabPage In t.TabPages
            p.BackColor = CPanel
            p.ForeColor = CText
        Next
    End Sub
End Module
