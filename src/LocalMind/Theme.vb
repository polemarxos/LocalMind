Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms

''' <summary>Shared dark styling so every window matches the main form.</summary>
Public Module Theme
    ' A calm, neutral dark palette with one blue accent.
    Public ReadOnly CBack As Color = Color.FromArgb(24, 24, 27)
    Public ReadOnly CPanel As Color = Color.FromArgb(31, 31, 35)
    Public ReadOnly CRaised As Color = Color.FromArgb(41, 42, 47)
    Public ReadOnly CControl As Color = Color.FromArgb(54, 56, 62)
    Public ReadOnly CHover As Color = Color.FromArgb(66, 68, 75)
    Public ReadOnly CBorder As Color = Color.FromArgb(58, 60, 66)
    Public ReadOnly CText As Color = Color.FromArgb(232, 232, 236)
    Public ReadOnly CDim As Color = Color.FromArgb(150, 152, 162)
    Public ReadOnly CAccent As Color = Color.FromArgb(37, 99, 235)
    Public ReadOnly CAccentHover As Color = Color.FromArgb(59, 130, 246)
    Public ReadOnly CAccentSoft As Color = Color.FromArgb(30, 52, 96)
    Public ReadOnly CGood As Color = Color.FromArgb(110, 200, 120)
    Public ReadOnly CWarn As Color = Color.FromArgb(230, 180, 80)
    Public ReadOnly CBad As Color = Color.FromArgb(220, 100, 100)
    Public ReadOnly UiFont As New Font("Segoe UI", 9.5!)
    Public ReadOnly BoldFont As New Font("Segoe UI Semibold", 9.5!)
    Public ReadOnly SmallFont As New Font("Segoe UI", 8.5!)
    Public ReadOnly InputFont As New Font("Segoe UI", 11.0!)

    Private _appIcon As Icon

    ''' <summary>The LocalMind icon (all sizes, from the embedded app.ico), for windows and the tray.</summary>
    Public Function AppIcon(Optional size As Integer = 0) As Icon
        Try
            Using s = Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("app.ico")
                If size > 0 Then Return New Icon(s, size, size)
                If _appIcon Is Nothing Then _appIcon = New Icon(s)
                Return _appIcon
            End Using
        Catch
            Return Icon.ExtractAssociatedIcon(Application.ExecutablePath)
        End Try
    End Function

    ''' <summary>Dark menus and toolbars everywhere (call once at startup).</summary>
    Public Sub Install()
        ToolStripManager.Renderer = New ModernRenderer()
    End Sub

    Public Sub ApplyForm(f As Form)
        f.BackColor = CPanel
        f.ForeColor = CText
        f.Font = UiFont
        ' Translate every window once its controls exist.
        AddHandler f.Load, Sub() Localizer.Apply(f)
        Try
            f.Icon = AppIcon()
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
        StyleFlat(b, primary)
        Return b
    End Function

    ''' <summary>Flat button with hover and pressed colors.</summary>
    Public Sub StyleFlat(b As Button, Optional primary As Boolean = False, Optional danger As Boolean = False)
        b.FlatStyle = FlatStyle.Flat
        b.FlatAppearance.BorderSize = 0
        b.Cursor = Cursors.Hand
        If danger Then
            b.BackColor = Color.FromArgb(150, 45, 45)
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(180, 60, 60)
            b.FlatAppearance.MouseDownBackColor = Color.FromArgb(120, 35, 35)
        ElseIf primary Then
            b.BackColor = CAccent
            b.FlatAppearance.MouseOverBackColor = CAccentHover
            b.FlatAppearance.MouseDownBackColor = Color.FromArgb(29, 78, 216)
        Else
            b.BackColor = CControl
            b.FlatAppearance.MouseOverBackColor = CHover
            b.FlatAppearance.MouseDownBackColor = CRaised
        End If
        b.ForeColor = Color.White
    End Sub

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
        g.GridColor = Color.FromArgb(44, 45, 50)
        g.EnableHeadersVisualStyles = False
        g.ColumnHeadersDefaultCellStyle.BackColor = CRaised
        g.ColumnHeadersDefaultCellStyle.ForeColor = CText
        g.ColumnHeadersDefaultCellStyle.SelectionBackColor = CRaised
        g.ColumnHeadersDefaultCellStyle.Font = BoldFont
        g.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single
        g.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing
        g.ColumnHeadersHeight = 32
        g.DefaultCellStyle.BackColor = CBack
        g.DefaultCellStyle.ForeColor = CText
        g.DefaultCellStyle.SelectionBackColor = CAccentSoft
        g.DefaultCellStyle.SelectionForeColor = Color.White
        g.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(28, 28, 31)
        g.RowHeadersVisible = False
        g.AllowUserToAddRows = False
        g.AllowUserToDeleteRows = False
        g.AllowUserToResizeRows = False
        g.SelectionMode = DataGridViewSelectionMode.FullRowSelect
        g.MultiSelect = False
        g.RowTemplate.Height = 28
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
        t.ItemSize = New Size(170, 32)
        AddHandler t.DrawItem,
            Sub(s, e)
                Dim selected = (e.Index = t.SelectedIndex)
                Using b As New SolidBrush(If(selected, CAccent, CRaised))
                    e.Graphics.FillRectangle(b, e.Bounds)
                End Using
                TextRenderer.DrawText(e.Graphics, t.TabPages(e.Index).Text, BoldFont, e.Bounds, If(selected, Color.White, CText),
                                      TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
            End Sub
        For Each p As TabPage In t.TabPages
            p.BackColor = CPanel
            p.ForeColor = CText
        Next
    End Sub

    Public Function Rounded(r As RectangleF, radius As Single) As GraphicsPath
        Dim p As New GraphicsPath()
        Dim d = Math.Min(radius * 2, Math.Min(r.Width, r.Height))
        If d <= 0 Then
            p.AddRectangle(r)
            Return p
        End If
        p.AddArc(r.X, r.Y, d, d, 180, 90)
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90)
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90)
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90)
        p.CloseFigure()
        Return p
    End Function

    '──────────────── menus and toolbars ────────────────
    ''' <summary>Tag a ToolStripButton with this to draw it as the main (blue) action.</summary>
    Public Const PrimaryTag As String = "primary"

    Private Class ModernColors
        Inherits ProfessionalColorTable
        Public Overrides ReadOnly Property ToolStripDropDownBackground As Color = CRaised
        Public Overrides ReadOnly Property ImageMarginGradientBegin As Color = CRaised
        Public Overrides ReadOnly Property ImageMarginGradientMiddle As Color = CRaised
        Public Overrides ReadOnly Property ImageMarginGradientEnd As Color = CRaised
        Public Overrides ReadOnly Property MenuBorder As Color = CBorder
        Public Overrides ReadOnly Property MenuItemBorder As Color = CHover
        Public Overrides ReadOnly Property MenuItemSelected As Color = CControl
        Public Overrides ReadOnly Property MenuItemSelectedGradientBegin As Color = CControl
        Public Overrides ReadOnly Property MenuItemSelectedGradientEnd As Color = CControl
        Public Overrides ReadOnly Property MenuItemPressedGradientBegin As Color = CRaised
        Public Overrides ReadOnly Property MenuItemPressedGradientEnd As Color = CRaised
        Public Overrides ReadOnly Property MenuStripGradientBegin As Color = CBack
        Public Overrides ReadOnly Property MenuStripGradientEnd As Color = CBack
        Public Overrides ReadOnly Property SeparatorDark As Color = CBorder
        Public Overrides ReadOnly Property SeparatorLight As Color = CBorder
        Public Overrides ReadOnly Property CheckBackground As Color = CAccentSoft
        Public Overrides ReadOnly Property CheckSelectedBackground As Color = CAccent
        Public Overrides ReadOnly Property CheckPressedBackground As Color = CAccent
        Public Overrides ReadOnly Property StatusStripGradientBegin As Color = CBack
        Public Overrides ReadOnly Property StatusStripGradientEnd As Color = CBack
        Public Overrides ReadOnly Property ToolStripBorder As Color = CBorder
        Public Overrides ReadOnly Property OverflowButtonGradientBegin As Color = CPanel
        Public Overrides ReadOnly Property OverflowButtonGradientMiddle As Color = CPanel
        Public Overrides ReadOnly Property OverflowButtonGradientEnd As Color = CPanel
    End Class

    Private Class ModernRenderer
        Inherits ToolStripProfessionalRenderer

        Public Sub New()
            MyBase.New(New ModernColors())
            RoundedEdges = False
        End Sub

        Protected Overrides Sub OnRenderToolStripBackground(e As ToolStripRenderEventArgs)
            Using b As New SolidBrush(If(TypeOf e.ToolStrip Is ToolStripDropDown, CRaised, e.ToolStrip.BackColor))
                e.Graphics.FillRectangle(b, e.AffectedBounds)
            End Using
        End Sub

        Protected Overrides Sub OnRenderToolStripBorder(e As ToolStripRenderEventArgs)
            If TypeOf e.ToolStrip Is ToolStripDropDown Then
                Using p As New Pen(CBorder)
                    e.Graphics.DrawRectangle(p, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1)
                End Using
            ElseIf TypeOf e.ToolStrip IsNot StatusStrip AndAlso TypeOf e.ToolStrip IsNot MenuStrip AndAlso CStr(e.ToolStrip.Tag) = "bar" Then
                Using p As New Pen(CBorder)
                    e.Graphics.DrawLine(p, 0, e.ToolStrip.Height - 1, e.ToolStrip.Width, e.ToolStrip.Height - 1)
                End Using
            End If
        End Sub

        Private Shared Sub Pill(g As Graphics, bounds As Rectangle, fill As Color, Optional border As Color = Nothing)
            g.SmoothingMode = SmoothingMode.AntiAlias
            Dim r As New RectangleF(1, 3, bounds.Width - 2, bounds.Height - 6)
            Using path = Rounded(r, 6)
                Using b As New SolidBrush(fill)
                    g.FillPath(b, path)
                End Using
                If border <> Nothing AndAlso border.A > 0 Then
                    Using p As New Pen(border)
                        g.DrawPath(p, path)
                    End Using
                End If
            End Using
            g.SmoothingMode = SmoothingMode.Default
        End Sub

        Protected Overrides Sub OnRenderButtonBackground(e As ToolStripItemRenderEventArgs)
            Dim btn = TryCast(e.Item, ToolStripButton)
            Dim primary = CStr(e.Item.Tag) = PrimaryTag
            If primary Then
                Pill(e.Graphics, New Rectangle(Point.Empty, e.Item.Size), If(e.Item.Selected, CAccentHover, CAccent))
            ElseIf btn IsNot Nothing AndAlso btn.Checked Then
                Pill(e.Graphics, New Rectangle(Point.Empty, e.Item.Size), If(e.Item.Selected, Color.FromArgb(38, 66, 120), CAccentSoft), CAccent)
            ElseIf btn IsNot Nothing AndAlso btn.CheckOnClick Then
                ' an option you can switch on: outlined so it reads as a toggle
                Pill(e.Graphics, New Rectangle(Point.Empty, e.Item.Size), If(e.Item.Selected, CControl, Color.Transparent), CBorder)
            ElseIf e.Item.Pressed Then
                Pill(e.Graphics, New Rectangle(Point.Empty, e.Item.Size), CRaised)
            ElseIf e.Item.Selected Then
                Pill(e.Graphics, New Rectangle(Point.Empty, e.Item.Size), CControl)
            End If
        End Sub

        Protected Overrides Sub OnRenderDropDownButtonBackground(e As ToolStripItemRenderEventArgs)
            If e.Item.Selected OrElse e.Item.Pressed Then Pill(e.Graphics, New Rectangle(Point.Empty, e.Item.Size), CControl)
        End Sub

        Protected Overrides Sub OnRenderSplitButtonBackground(e As ToolStripItemRenderEventArgs)
            If e.Item.Selected OrElse e.Item.Pressed Then Pill(e.Graphics, New Rectangle(Point.Empty, e.Item.Size), CControl)
            Dim sb = TryCast(e.Item, ToolStripSplitButton)
            If sb IsNot Nothing Then DrawArrow(New ToolStripArrowRenderEventArgs(e.Graphics, sb, sb.DropDownButtonBounds, CDim, ArrowDirection.Down))
        End Sub

        Protected Overrides Sub OnRenderMenuItemBackground(e As ToolStripItemRenderEventArgs)
            If Not e.Item.Enabled Then Return
            If e.Item.Selected OrElse e.Item.Pressed Then
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Dim r As New RectangleF(If(e.Item.IsOnDropDown, 4, 0), 1, e.Item.Width - If(e.Item.IsOnDropDown, 8, 0), e.Item.Height - 2)
                Using path = Rounded(r, 5), b As New SolidBrush(If(e.Item.Pressed AndAlso Not e.Item.IsOnDropDown, CRaised, CControl))
                    e.Graphics.FillPath(b, path)
                End Using
                e.Graphics.SmoothingMode = SmoothingMode.Default
            End If
        End Sub

        Protected Overrides Sub OnRenderItemText(e As ToolStripItemTextRenderEventArgs)
            If TypeOf e.Item Is ToolStripLabel AndAlso Not e.Item.IsOnDropDown AndAlso TypeOf e.Item IsNot ToolStripStatusLabel Then
                ' labels keep their own color (e.g. the green / red connection dot)
            ElseIf TypeOf e.Item Is ToolStripStatusLabel Then
                If e.Item.ForeColor = Color.White OrElse e.Item.ForeColor = SystemColors.ControlText Then e.TextColor = CDim
            ElseIf Not e.Item.Enabled Then
                e.TextColor = Color.FromArgb(105, 107, 115)
            ElseIf CStr(e.Item.Tag) = PrimaryTag OrElse (TypeOf e.Item Is ToolStripButton AndAlso CType(e.Item, ToolStripButton).Checked) Then
                e.TextColor = Color.White
            Else
                e.TextColor = CText
            End If
            MyBase.OnRenderItemText(e)
        End Sub

        Protected Overrides Sub OnRenderSeparator(e As ToolStripSeparatorRenderEventArgs)
            Using p As New Pen(CBorder)
                If e.Vertical Then
                    Dim x = e.Item.Width \ 2
                    e.Graphics.DrawLine(p, x, 8, x, e.Item.Height - 8)
                Else
                    Dim y = e.Item.Height \ 2
                    e.Graphics.DrawLine(p, 8, y, e.Item.Width - 8, y)
                End If
            End Using
        End Sub

        Protected Overrides Sub OnRenderArrow(e As ToolStripArrowRenderEventArgs)
            e.ArrowColor = If(e.Item IsNot Nothing AndAlso Not e.Item.Enabled, CControl, CDim)
            MyBase.OnRenderArrow(e)
        End Sub

        Protected Overrides Sub OnRenderImageMargin(e As ToolStripRenderEventArgs)
            Using b As New SolidBrush(CRaised)
                e.Graphics.FillRectangle(b, e.AffectedBounds)
            End Using
        End Sub

        Protected Overrides Sub OnRenderStatusStripSizingGrip(e As ToolStripRenderEventArgs)
        End Sub
    End Class
End Module

''' <summary>A panel with rounded corners and a thin border (the message box "card").</summary>
Public Class RoundedPanel
    Inherits Panel

    <System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)> Public Property Radius As Integer = 12
    <System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)> Public Property BorderColor As Color = Theme.CBorder
    <System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)> Public Property FocusBorderColor As Color = Theme.CAccent
    <System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)> Public Property Focused_ As Boolean

    Public Sub New()
        SetStyle(ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or ControlStyles.ResizeRedraw Or ControlStyles.UserPaint, True)
    End Sub

    Protected Overrides Sub OnPaintBackground(e As PaintEventArgs)
        ' Corners show the parent's color so the card looks rounded.
        Using b As New SolidBrush(If(Parent?.BackColor, Theme.CBack))
            e.Graphics.FillRectangle(b, ClientRectangle)
        End Using
        e.Graphics.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias
        Dim r As New RectangleF(0.5F, 0.5F, Width - 1.5F, Height - 1.5F)
        Using path = Theme.Rounded(r, Radius)
            Using b As New SolidBrush(BackColor)
                e.Graphics.FillPath(b, path)
            End Using
            Using p As New Pen(If(Focused_, FocusBorderColor, BorderColor))
                e.Graphics.DrawPath(p, path)
            End Using
        End Using
    End Sub
End Class
