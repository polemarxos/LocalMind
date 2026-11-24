Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms
Imports System.Linq
Imports System.Collections.Generic

''' <summary>Shared dark styling so every window matches the main form.</summary>
Public Module Theme
    ' The look in use (Settings → Appearance); "Midnight" is the default. Set once by UsePreset at startup.
    Public CBack As Color = Color.FromArgb(24, 24, 27)
    Public CPanel As Color = Color.FromArgb(31, 31, 35)
    Public CRaised As Color = Color.FromArgb(41, 42, 47)
    Public CControl As Color = Color.FromArgb(54, 56, 62)
    Public CHover As Color = Color.FromArgb(66, 68, 75)
    Public CBorder As Color = Color.FromArgb(58, 60, 66)
    Public CText As Color = Color.FromArgb(232, 232, 236)
    Public CDim As Color = Color.FromArgb(150, 152, 162)
    Public CAccent As Color = Color.FromArgb(37, 99, 235)
    Public CAccentHover As Color = Color.FromArgb(59, 130, 246)
    Public CAccentSoft As Color = Color.FromArgb(30, 52, 96)
    ''' <summary>Text on accent-colored buttons (white, or black on bright accents).</summary>
    Public CAccentText As Color = Color.White
    Public CGood As Color = Color.FromArgb(110, 200, 120)
    Public CWarn As Color = Color.FromArgb(230, 180, 80)
    Public CBad As Color = Color.FromArgb(220, 100, 100)
    Public UiFont As New Font("Segoe UI", 9.5!)
    Public BoldFont As New Font("Segoe UI Semibold", 9.5!)
    Public SmallFont As New Font("Segoe UI", 8.5!)
    Public InputFont As New Font("Segoe UI", 11.0!)
    Public IsDark As Boolean = True
    Public PresetName As String = "Midnight"

    ' the original palette (what the designer files and older code use), to map to the chosen look
    Private ReadOnly Original As Color() = {Color.FromArgb(24, 24, 27), Color.FromArgb(31, 31, 35), Color.FromArgb(41, 42, 47),
                                            Color.FromArgb(54, 56, 62), Color.FromArgb(66, 68, 75), Color.FromArgb(58, 60, 66),
                                            Color.FromArgb(232, 232, 236), Color.FromArgb(150, 152, 162), Color.FromArgb(37, 99, 235),
                                            Color.FromArgb(59, 130, 246), Color.FromArgb(30, 52, 96), Color.FromArgb(110, 200, 120),
                                            Color.FromArgb(230, 180, 80), Color.FromArgb(220, 100, 100)}

    ''' <summary>Uses a look from ThemePresets (call before any window is made).</summary>
    Public Sub UsePreset(name As String)
        Dim p = ThemePresets.Find(name)
        PresetName = p.Name
        IsDark = p.Dark
        CBack = p.Back : CPanel = p.Panel : CRaised = p.Raised : CControl = p.Control
        CText = p.Text : CDim = p.Dimmed : CAccent = p.Accent
        CHover = ThemePresets.Mix(p.Control, p.Text, If(p.Dark, 0.1, 0.07))
        CBorder = ThemePresets.Mix(p.Raised, p.Text, If(p.Dark, 0.13, 0.14))
        CAccentHover = ThemePresets.Mix(p.Accent, If(p.Dark, Color.White, Color.Black), If(p.Dark, 0.16, 0.12))
        CAccentSoft = ThemePresets.Mix(p.Back, p.Accent, If(p.Dark, 0.28, 0.14))
        CAccentText = If(ThemePresets.Lum(p.Accent) > 0.55, Color.FromArgb(20, 20, 20), Color.White)
        CGood = p.Good : CWarn = p.Warn : CBad = p.Bad
        Dim fam = ThemePresets.FontOrDefault(p.Font)
        Dim semi = If(fam.StartsWith("Segoe UI"), ThemePresets.FontOrDefault(fam & " Semibold"), fam)
        If semi = "Segoe UI" AndAlso fam <> "Segoe UI" Then semi = fam
        UiFont = New Font(fam, p.FontSize)
        BoldFont = If(semi <> fam, New Font(semi, p.FontSize), New Font(fam, p.FontSize, FontStyle.Bold))
        SmallFont = New Font(fam, p.FontSize - 1.0!)
        InputFont = New Font(fam, p.FontSize + 1.5!)
    End Sub

    ''' <summary>A color from the original palette (or a gray / white / blue in code) in the chosen look.</summary>
    Public Function MapColor(c As Color, isBack As Boolean) As Color
        If c.A < 255 OrElse PresetName = "Midnight" Then Return c
        Dim current = {CBack, CPanel, CRaised, CControl, CHover, CBorder, CText, CDim, CAccent, CAccentHover, CAccentSoft, CGood, CWarn, CBad}
        For i = 0 To Original.Length - 1
            If Original(i).ToArgb() = c.ToArgb() Then Return current(i)
        Next
        If ThemePresets.IsGray(c) Then
            Dim l = ThemePresets.Lum(c)
            If isBack Then
                If l < 0.105 Then Return CBack
                If l < 0.135 Then Return CPanel
                If l < 0.185 Then Return CRaised
                If l < 0.235 Then Return CControl
                If l < 0.35 Then Return CHover
                Return c
            Else
                If l > 0.75 Then Return CText
                If l > 0.38 Then Return CDim
                Return c
            End If
        End If
        ' blues of the old accent
        If Math.Abs(CInt(c.R) - 37) + Math.Abs(CInt(c.G) - 99) + Math.Abs(CInt(c.B) - 235) < 90 Then Return If(isBack, CAccent, CAccent)
        Return c
    End Function

    ''' <summary>Puts a window (made with fixed colors) into the chosen look; also dark scrollbars on dark looks.</summary>
    Public Sub Recolor(root As Control)
        If root Is Nothing Then Return
        For Each c As Control In root.Controls
            Recolor(c)
        Next
        If PresetName <> "Midnight" Then
            Dim accentBack = root.BackColor.ToArgb() = CAccent.ToArgb() OrElse root.BackColor.ToArgb() = Original(8).ToArgb()
            root.BackColor = MapColor(root.BackColor, True)
            If accentBack Then
                root.ForeColor = CAccentText
            Else
                root.ForeColor = MapColor(root.ForeColor, False)
            End If
            If TypeOf root Is Button AndAlso Not accentBack Then
                Dim b = CType(root, Button)
                If b.FlatStyle = FlatStyle.Flat AndAlso b.FlatAppearance.MouseOverBackColor.A = 255 Then
                    b.FlatAppearance.MouseOverBackColor = MapColor(b.FlatAppearance.MouseOverBackColor, True)
                    b.FlatAppearance.MouseDownBackColor = MapColor(b.FlatAppearance.MouseDownBackColor, True)
                End If
            End If
        End If
        DarkScrollbars(root)
    End Sub

    ''' <summary>The chat page's colors (written for the default look) in the chosen look.</summary>
    Public Function MapCss(css As String) As String
        If PresetName = "Midnight" Then Return css
        Dim hex = Function(c As Color) $"#{c.R:x2}{c.G:x2}{c.B:x2}"
        Dim link = If(IsDark, ThemePresets.Mix(CAccent, Color.White, 0.35), CAccent)
        Dim map As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From {
            {"#18181b", hex(CBack)}, {"#1f1f23", hex(CPanel)}, {"#1e1e1e", hex(CBack)}, {"#222327", hex(CRaised)}, {"#2a2a2e", hex(CRaised)},
            {"#2d2d30", hex(CRaised)}, {"#2a2b31", hex(CControl)}, {"#34363b", hex(CControl)}, {"#3a3a3a", hex(CControl)}, {"#3a3c42", hex(CControl)},
            {"#333", hex(CControl)}, {"#444", hex(CHover)}, {"#4a4c54", hex(CHover)},
            {"#fafafa", hex(CText)}, {"#ffffff", hex(CText)}, {"#e4e4e7", hex(CText)}, {"#ddd", hex(CText)}, {"#ccc", hex(CText)}, {"#e0e0e0", hex(CText)},
            {"#a1a1aa", hex(CDim)}, {"#a0a0a8", hex(CDim)}, {"#9a9aa2", hex(CDim)}, {"#888", hex(CDim)}, {"#777", hex(CDim)}, {"#bbb", hex(CDim)},
            {"#2563eb", hex(CAccent)}, {"#4ea1ff", hex(link)}, {"#fff", hex(CAccentText)}}
        Dim result = System.Text.RegularExpressions.Regex.Replace(css, "#[0-9a-fA-F]{6}(?![0-9a-fA-F])|#[0-9a-fA-F]{3}(?![0-9a-fA-F])",
                     Function(m) If(map.ContainsKey(m.Value), map(m.Value), m.Value))
        result = result.Replace("color-scheme: dark;", If(IsDark, "color-scheme: dark;", "color-scheme: light;"))
        Return result.Replace("font-family: 'Segoe UI', 'Inter'", $"font-family: '{UiFont.FontFamily.Name}', 'Segoe UI', 'Inter'")
    End Function

    <Runtime.InteropServices.DllImport("uxtheme.dll", CharSet:=Runtime.InteropServices.CharSet.Unicode)>
    Private Function SetWindowTheme(hwnd As IntPtr, app As String, idList As String) As Integer
    End Function

    <Runtime.InteropServices.DllImport("user32.dll")>
    Private Function SendMessage(hwnd As IntPtr, msg As Integer, wParam As IntPtr, lParam As IntPtr) As IntPtr
    End Function

    ''' <summary>A progress bar in the app's colors (Windows draws it light green / white).</summary>
    Public Sub StyleProgress(pb As ProgressBar)
        Dim apply = Sub()
                        Try
                            SetWindowTheme(pb.Handle, "", "")
                            SendMessage(pb.Handle, &H2001, IntPtr.Zero, New IntPtr(ColorTranslator.ToWin32(CRaised)))    ' PBM_SETBKCOLOR
                            SendMessage(pb.Handle, &H409, IntPtr.Zero, New IntPtr(ColorTranslator.ToWin32(CAccent)))     ' PBM_SETBARCOLOR
                        Catch
                        End Try
                    End Sub
        If pb.IsHandleCreated Then apply() Else AddHandler pb.HandleCreated, Sub() apply()
    End Sub

    ''' <summary>Dark scrollbars (Windows 10 1809+) for lists, grids, text boxes and panels on dark looks.</summary>
    Public Sub DarkScrollbars(c As Control)
        If TypeOf c Is ProgressBar Then StyleProgress(CType(c, ProgressBar))
        If Not IsDark Then Return
        If TypeOf c Is ListView Then
            ' the header part right of the last column (Windows paints it white)
            Dim lvApply = Sub()
                              Try
                                  Dim hdr = SendMessage(c.Handle, &H101F, IntPtr.Zero, IntPtr.Zero)      ' LVM_GETHEADER
                                  If hdr <> IntPtr.Zero Then SetWindowTheme(hdr, "DarkMode_ItemsView", Nothing)
                              Catch
                              End Try
                          End Sub
            If c.IsHandleCreated Then lvApply() Else AddHandler c.HandleCreated, Sub() lvApply()
        End If
        If Not (TypeOf c Is ListView OrElse TypeOf c Is DataGridView OrElse TypeOf c Is TextBoxBase OrElse TypeOf c Is ListBox OrElse
                TypeOf c Is TreeView OrElse TypeOf c Is ScrollableControl) Then Return
        Dim apply = Sub()
                        Try
                            SetWindowTheme(c.Handle, "DarkMode_Explorer", Nothing)
                            If TypeOf c Is DataGridView Then
                                For Each sb In c.Controls.OfType(Of ScrollBar)()
                                    SetWindowTheme(sb.Handle, "DarkMode_Explorer", Nothing)
                                Next
                            End If
                        Catch
                        End Try
                    End Sub
        If c.IsHandleCreated Then apply() Else AddHandler c.HandleCreated, Sub() apply()
    End Sub

    ''' <summary>
    ''' Makes a list exactly tall enough for its rows (up to <paramref name="maxRows"/>), measured after it exists,
    ''' so it is right at every Windows display scale.
    ''' </summary>
    ''' <summary>The last column fills the list's width, so no horizontal scrollbar covers the rows.</summary>
    Public Sub FillLastColumn(lv As ListView)
        Dim fit = Sub()
                      If Not lv.IsHandleCreated OrElse lv.Columns.Count = 0 Then Return
                      Dim used = 0
                      For i = 0 To lv.Columns.Count - 2
                          used += lv.Columns(i).Width
                      Next
                      Dim w = Math.Max(60, lv.ClientSize.Width - used - 2)
                      If Math.Abs(lv.Columns(lv.Columns.Count - 1).Width - w) > 2 Then lv.Columns(lv.Columns.Count - 1).Width = w
                  End Sub
        AddHandler lv.SizeChanged, Sub() fit()
        AddHandler lv.HandleCreated, Sub() fit()
    End Sub

    Public Sub FitListHeight(lv As ListView, maxRows As Integer)
        ' Measured from the header and one row (the first row's rectangle is not reliable right after the
        ' list is created), with the last column filling the width so no horizontal scrollbar hides the rows.
        Dim fit = Sub()
                      Try
                          If lv.Items.Count = 0 Then Return
                          Dim used = 0
                          For i = 0 To lv.Columns.Count - 2
                              used += lv.Columns(i).Width
                          Next
                          Dim scroll = If(lv.Items.Count > maxRows, SystemInformation.VerticalScrollBarWidth, 0)
                          If lv.Columns.Count > 0 Then lv.Columns(lv.Columns.Count - 1).Width = Math.Max(60, lv.ClientSize.Width - used - scroll - 2)
                          Dim headerH = 0
                          Dim hdr = SendMessage(lv.Handle, &H101F, IntPtr.Zero, IntPtr.Zero)      ' LVM_GETHEADER
                          Dim hr As RECT
                          If hdr <> IntPtr.Zero AndAlso GetWindowRect(hdr, hr) Then headerH = hr.Bottom - hr.Top
                          If headerH <= 0 Then headerH = lv.Font.Height + 10
                          Dim rowH = 0
                          Try
                              rowH = lv.GetItemRect(0).Height
                          Catch
                          End Try
                          If rowH <= 0 OrElse rowH > lv.Font.Height * 3 Then rowH = lv.Font.Height + 6
                          Dim rows = Math.Min(maxRows, lv.Items.Count)
                          lv.Height = headerH + rowH * rows + (lv.Height - lv.ClientSize.Height) + 4
                      Catch
                      End Try
                  End Sub
        If lv.IsHandleCreated Then
            fit()
            lv.BeginInvoke(fit)          ' again once the list has its final width
        Else
            AddHandler lv.HandleCreated, Sub() lv.BeginInvoke(fit)
        End If
        AddHandler lv.SizeChanged, Sub()
                                       If lv.IsHandleCreated AndAlso lv.Columns.Count > 0 Then
                                           Dim used = 0
                                           For i = 0 To lv.Columns.Count - 2
                                               used += lv.Columns(i).Width
                                           Next
                                           Dim w = Math.Max(60, lv.ClientSize.Width - used - 2)
                                           If Math.Abs(lv.Columns(lv.Columns.Count - 1).Width - w) > 2 Then lv.Columns(lv.Columns.Count - 1).Width = w
                                       End If
                                   End Sub
    End Sub

    <System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)>
    Private Structure RECT
        Public Left, Top, Right, Bottom As Integer
    End Structure

    <System.Runtime.InteropServices.DllImport("user32.dll")>
    Private Function GetWindowRect(hwnd As IntPtr, ByRef r As RECT) As Boolean
    End Function

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
    <Runtime.InteropServices.DllImport("uxtheme.dll", EntryPoint:="#135")>
    Private Function SetPreferredAppMode(mode As Integer) As Integer
    End Function
    <Runtime.InteropServices.DllImport("uxtheme.dll", EntryPoint:="#136")>
    Private Sub FlushMenuThemes()
    End Sub

    Public Sub Install()
        ' dark scrollbars and system parts on dark looks (Windows 10 1809+; ignored elsewhere)
        Try
            SetPreferredAppMode(If(IsDark, 2, 3))
            FlushMenuThemes()
        Catch
        End Try
        ToolStripManager.Renderer = New ModernRenderer()
    End Sub

    <Runtime.InteropServices.DllImport("dwmapi.dll")>
    Private Function DwmSetWindowAttribute(hwnd As IntPtr, attr As Integer, ByRef value As Integer, size As Integer) As Integer
    End Function

    ''' <summary>Dark window title bar, matching the dark window.</summary>
    Public Sub DarkTitleBar(f As Form)
        Dim apply = Sub()
                        Try
                            Dim one = If(IsDark, 1, 0)
                            If DwmSetWindowAttribute(f.Handle, 20, one, 4) <> 0 Then DwmSetWindowAttribute(f.Handle, 19, one, 4)
                        Catch
                        End Try
                    End Sub
        If f.IsHandleCreated Then apply() Else AddHandler f.HandleCreated, Sub() apply()
    End Sub

    Public Sub ApplyForm(f As Form)
        DarkTitleBar(f)
        f.BackColor = CPanel
        f.ForeColor = CText
        f.Font = UiFont
        ' Translate every window once its controls exist.
        AddHandler f.Load, Sub()
                               Localizer.Apply(f)
                               Recolor(f)
                           End Sub
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
            .ForeColor = If(primary, CAccentText, CText),
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
            b.FlatAppearance.MouseDownBackColor = ThemePresets.Mix(CAccent, Color.Black, 0.2)
        Else
            b.BackColor = CControl
            b.FlatAppearance.MouseOverBackColor = CHover
            b.FlatAppearance.MouseDownBackColor = CRaised
        End If
        b.ForeColor = If(primary, CAccentText, If(danger, Color.White, CText))
        HookButton(b, primary, danger)
    End Sub

    '──────────────── professional buttons: rounded, bordered, icon + text ────────────────
    Private Class ButtonLook
        Public Hover, Down As Boolean
        Public Primary, Danger As Boolean
    End Class
    Private ReadOnly _buttons As New Runtime.CompilerServices.ConditionalWeakTable(Of Button, ButtonLook)()

    ''' <summary>Draws the button itself (rounded corners, thin border, icon instead of emoji).</summary>
    Private Sub HookButton(b As Button, primary As Boolean, danger As Boolean)
        Dim st As ButtonLook = Nothing
        If Not _buttons.TryGetValue(b, st) Then
            st = New ButtonLook()
            _buttons.Add(b, st)
            Try
                GetType(Control).GetProperty("DoubleBuffered", Reflection.BindingFlags.Instance Or Reflection.BindingFlags.NonPublic)?.SetValue(b, True)
            Catch
            End Try
            AddHandler b.MouseEnter, Sub()
                                         st.Hover = True
                                         b.Invalidate()
                                     End Sub
            AddHandler b.MouseLeave, Sub()
                                         st.Hover = False : st.Down = False : b.Invalidate()
                                     End Sub
            AddHandler b.MouseDown, Sub()
                                         st.Down = True
                                         b.Invalidate()
                                     End Sub
            AddHandler b.MouseUp, Sub()
                                         st.Down = False
                                         b.Invalidate()
                                     End Sub
            AddHandler b.EnabledChanged, Sub() b.Invalidate()
            AddHandler b.Paint, Sub(s, e) PaintButton(b, st, e.Graphics)
        End If
        st.Primary = primary
        st.Danger = danger
        FitButton(b)
    End Sub

    ''' <summary>Auto-sized buttons are always wide enough for icon + text (measured with the real font).</summary>
    Private Sub FitButton(b As Button)
        Static hooked As New Runtime.CompilerServices.ConditionalWeakTable(Of Button, Object)()
        Dim fit = Sub()
                      If Not b.AutoSize Then Return
                      Dim parts = Icons.Split(b.Text)
                      Dim w = TextRenderer.MeasureText(parts.After, b.Font, New Size(2000, 100), TextFormatFlags.SingleLine Or TextFormatFlags.NoPrefix).Width
                      Dim need = b.Padding.Horizontal + 22 + If(parts.Glyph IsNot Nothing, 16 + If(parts.After.Length > 0, 7, 0), 0) + w
                      If b.MinimumSize.Width <> need Then b.MinimumSize = New Size(need, b.MinimumSize.Height)
                  End Sub
        fit()
        Dim dummy As Object = Nothing
        If Not hooked.TryGetValue(b, dummy) Then
            hooked.Add(b, New Object())
            AddHandler b.TextChanged, Sub() fit()
            AddHandler b.FontChanged, Sub() fit()
        End If
    End Sub

    Private Sub PaintButton(b As Button, st As ButtonLook, g As Graphics)
        Dim outer = If(b.Parent?.BackColor, CPanel)
        If outer.A < 255 Then outer = CPanel
        g.Clear(outer)
        g.SmoothingMode = SmoothingMode.AntiAlias
        Dim fill = b.BackColor
        If b.Enabled Then
            If st.Down Then
                fill = b.FlatAppearance.MouseDownBackColor
            ElseIf st.Hover Then
                fill = b.FlatAppearance.MouseOverBackColor
            End If
        End If
        If fill.A = 0 OrElse fill.IsEmpty Then fill = b.BackColor
        Dim r As New RectangleF(0.5F, 0.5F, b.Width - 1.0F, b.Height - 1.0F)
        Using p = Rounded(r, 6), br As New SolidBrush(If(b.Enabled, fill, ThemePresets.Mix(fill, outer, 0.45)))
            g.FillPath(br, p)
        End Using
        Dim accent = st.Primary OrElse st.Danger OrElse b.BackColor.ToArgb() = CAccent.ToArgb()
        If Not accent Then
            Using p = Rounded(r, 6), pen As New Pen(ThemePresets.Mix(fill, CText, 0.13))
                g.DrawPath(pen, p)
            End Using
        End If
        g.SmoothingMode = SmoothingMode.Default
        Dim fore = If(b.Enabled, b.ForeColor, ThemePresets.Mix(b.ForeColor, fill, 0.55))
        Dim parts = Icons.Split(b.Text)
        Dim flags = TextFormatFlags.VerticalCenter Or TextFormatFlags.SingleLine Or TextFormatFlags.EndEllipsis Or
                    If(b.UseMnemonic, TextFormatFlags.Default, TextFormatFlags.NoPrefix)
        Dim area = New Rectangle(b.Padding.Left + 2, 0, b.Width - b.Padding.Horizontal - 4, b.Height)
        Dim iconW = If(parts.Glyph IsNot Nothing, 16, 0)
        Dim gap = If(parts.Glyph IsNot Nothing AndAlso parts.After.Length > 0, 7, 0)
        Dim textSize = If(parts.After.Length > 0, TextRenderer.MeasureText(g, parts.After, b.Font, area.Size, flags), Size.Empty)
        Dim total = iconW + gap + textSize.Width
        Dim left = If(b.TextAlign = ContentAlignment.MiddleLeft OrElse b.TextAlign = ContentAlignment.TopLeft OrElse b.TextAlign = ContentAlignment.BottomLeft,
                      area.Left, area.Left + Math.Max(0, (area.Width - total) \ 2))
        If parts.Glyph IsNot Nothing Then Icons.Draw(g, parts.Glyph, New Rectangle(left, 0, iconW, b.Height), fore, 10.5!)
        If parts.After.Length > 0 Then
            TextRenderer.DrawText(g, parts.After, b.Font, New Rectangle(left + iconW + gap, 0, Math.Min(textSize.Width + 2, area.Right - (left + iconW + gap)), b.Height), fore, flags)
        End If
    End Sub

    ''' <summary>Text with the icon for its leading emoji (tabs, owner-drawn lists).</summary>
    Public Sub DrawIconText(g As Graphics, text As String, font As Font, bounds As Rectangle, color As Color, centered As Boolean)
        Dim parts = Icons.Split(text)
        Dim flags = TextFormatFlags.VerticalCenter Or TextFormatFlags.SingleLine Or TextFormatFlags.NoPrefix Or TextFormatFlags.EndEllipsis
        If parts.Glyph Is Nothing Then
            TextRenderer.DrawText(g, text, font, bounds, color, flags Or If(centered, TextFormatFlags.HorizontalCenter, TextFormatFlags.Default))
            Return
        End If
        Dim w = 16 + 7 + TextRenderer.MeasureText(g, parts.After, font, bounds.Size, flags).Width
        Dim x = If(centered, bounds.X + Math.Max(0, (bounds.Width - w) \ 2), bounds.X)
        Icons.Draw(g, parts.Glyph, New Rectangle(x, bounds.Y, 16, bounds.Height), color, 10.5!)
        TextRenderer.DrawText(g, parts.After, font, New Rectangle(x + 23, bounds.Y, bounds.Right - (x + 23), bounds.Height), color, flags)
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
        If TypeOf c Is ComboBox Then DarkCombo(CType(c, ComboBox))
        DarkScrollbars(c)
    End Sub

    Public Sub StyleGrid(g As DataGridView)
        g.BackgroundColor = CBack
        g.BorderStyle = BorderStyle.None
        g.GridColor = ThemePresets.Mix(CBack, CText, 0.1)
        g.EnableHeadersVisualStyles = False
        g.ColumnHeadersDefaultCellStyle.BackColor = CRaised
        g.ColumnHeadersDefaultCellStyle.ForeColor = CText
        g.ColumnHeadersDefaultCellStyle.SelectionBackColor = CRaised
        g.ColumnHeadersDefaultCellStyle.Font = BoldFont
        g.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single
        g.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing
        g.ColumnHeadersHeight = 36
        g.ColumnHeadersDefaultCellStyle.Padding = New Padding(6, 0, 4, 0)
        g.ColumnHeadersDefaultCellStyle.ForeColor = CDim
        g.DefaultCellStyle.Padding = New Padding(4, 0, 4, 0)
        g.AdvancedColumnHeadersBorderStyle.All = DataGridViewAdvancedCellBorderStyle.None
        g.AdvancedColumnHeadersBorderStyle.Bottom = DataGridViewAdvancedCellBorderStyle.Single
        g.DefaultCellStyle.BackColor = CBack
        g.DefaultCellStyle.ForeColor = CText
        g.DefaultCellStyle.SelectionBackColor = CAccentSoft
        g.DefaultCellStyle.SelectionForeColor = CText
        g.AlternatingRowsDefaultCellStyle.BackColor = ThemePresets.Mix(CBack, CText, 0.025)
        g.GridColor = ThemePresets.Mix(CBack, CText, 0.07)
        DarkScrollbars(g)
        g.RowHeadersVisible = False
        g.AllowUserToAddRows = False
        g.AllowUserToDeleteRows = False
        g.AllowUserToResizeRows = False
        g.SelectionMode = DataGridViewSelectionMode.FullRowSelect
        g.MultiSelect = False
        g.RowTemplate.Height = 32
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
        DarkScrollbars(l)
        ' Dark column headers (Windows draws them white).
        l.OwnerDraw = True
        AddHandler l.DrawColumnHeader, Sub(s, e)
                                           Using b As New SolidBrush(CRaised)
                                               e.Graphics.FillRectangle(b, e.Bounds)
                                           End Using
                                           Using p As New Pen(CBorder)
                                               e.Graphics.DrawLine(p, e.Bounds.Right - 1, e.Bounds.Top + 4, e.Bounds.Right - 1, e.Bounds.Bottom - 4)
                                               e.Graphics.DrawLine(p, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1)
                                           End Using
                                           TextRenderer.DrawText(e.Graphics, e.Header.Text, BoldFont, New Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height),
                                                                 CText, TextFormatFlags.VerticalCenter Or TextFormatFlags.EndEllipsis Or TextFormatFlags.NoPrefix)
                                       End Sub
        AddHandler l.DrawItem, Sub(s, e) e.DrawDefault = True
        AddHandler l.DrawSubItem, Sub(s, e) e.DrawDefault = True
    End Sub

    ''' <summary>Simple dark tab control that owner-draws its headers.</summary>
    ''' <summary>
    ''' Paints a tab control's whole header strip and frame itself after Windows draws it, so there are no white or
    ''' boxy borders: a flat strip, a soft highlight for the chosen tab and an accent line under it.
    ''' </summary>
    Private Class TabPainter
        Inherits NativeWindow
        Private ReadOnly _t As TabControl
        Public Sub New(t As TabControl)
            _t = t
            If t.IsHandleCreated Then AssignHandle(t.Handle)
            AddHandler t.HandleCreated, Sub() AssignHandle(t.Handle)
            AddHandler t.HandleDestroyed, Sub() ReleaseHandle()
            AddHandler t.SelectedIndexChanged, Sub() t.Invalidate()
        End Sub
        Protected Overrides Sub WndProc(ByRef m As Message)
            MyBase.WndProc(m)
            If m.Msg = &HF AndAlso Not _t.IsDisposed Then
                Try
                    Using g = Graphics.FromHwnd(_t.Handle)
                        Paint(g)
                    End Using
                Catch
                End Try
            End If
        End Sub
        Private Sub Paint(g As Graphics)
            Dim disp = _t.DisplayRectangle
            Dim headerH = Math.Max(0, disp.Top - 4)
            Dim back = If(_t.Parent?.BackColor, CPanel)
            Using b As New SolidBrush(back)
                g.FillRectangle(b, 0, 0, _t.Width, headerH + 2)
                ' the frame Windows draws around the page
                g.FillRectangle(b, 0, headerH, disp.Left, _t.Height)
                g.FillRectangle(b, disp.Right, headerH, _t.Width - disp.Right, _t.Height)
                g.FillRectangle(b, 0, disp.Bottom, _t.Width, _t.Height - disp.Bottom)
                g.FillRectangle(b, 0, headerH, _t.Width, disp.Top - headerH)
            End Using
            Using p As New Pen(CBorder)
                g.DrawLine(p, 0, headerH + 1, _t.Width, headerH + 1)
            End Using
            For i = 0 To _t.TabCount - 1
                Dim r = _t.GetTabRect(i)
                r = New Rectangle(r.X, 2, r.Width, headerH - 2)
                Dim selected = i = _t.SelectedIndex
                If selected Then
                    Using b As New SolidBrush(ThemePresets.Mix(back, CText, 0.05))
                        g.FillRectangle(b, r.X + 2, r.Y, r.Width - 4, r.Height)
                    End Using
                    Using b As New SolidBrush(CAccent)
                        g.FillRectangle(b, r.X + 12, headerH - 2, r.Width - 24, 3)
                    End Using
                End If
                DrawIconText(g, _t.TabPages(i).Text.Replace("&&", "&"), If(selected, BoldFont, UiFont), r, If(selected, CText, CDim), True)
            Next
        End Sub
    End Class
    Private ReadOnly _tabPainters As New List(Of TabPainter)

    Public Sub StyleTabs(t As TabControl)
        _tabPainters.Add(New TabPainter(t))
        t.DrawMode = TabDrawMode.OwnerDrawFixed
        ' each tab as wide as its text (+ room for the icon), so all tabs fit
        t.SizeMode = TabSizeMode.Normal
        t.Padding = New Point(26, 10)
        AddHandler t.DrawItem,
            Sub(s, e)
                Dim selected = (e.Index = t.SelectedIndex)
                ' flat tabs: the chosen one is lighter with a blue line under it
                Dim r = New Rectangle(e.Bounds.X - 2, e.Bounds.Y - 2, e.Bounds.Width + 4, e.Bounds.Height + 4)
                Using b As New SolidBrush(If(selected, CRaised, CBack))
                    e.Graphics.FillRectangle(b, r)
                End Using
                If selected Then
                    Using b As New SolidBrush(CAccent)
                        e.Graphics.FillRectangle(b, e.Bounds.X + 10, e.Bounds.Bottom - 3, e.Bounds.Width - 20, 3)
                    End Using
                End If
                DrawIconText(e.Graphics, t.TabPages(e.Index).Text.Replace("&&", "&"), If(selected, BoldFont, UiFont), e.Bounds, If(selected, CText, CDim), True)
            End Sub
        For Each p As TabPage In t.TabPages
            p.BackColor = CPanel
            p.ForeColor = CText
        Next
    End Sub

    ''' <summary>Dark border and arrow for a combo box (Windows draws them white in flat style).</summary>
    Public Sub DarkCombo(c As ComboBox)
        If c Is Nothing Then Return
        c.FlatStyle = FlatStyle.Flat
        c.BackColor = CRaised
        c.ForeColor = CText
        _painters.Add(New ComboPainter(c))   ' keep alive (NativeWindow is only weakly referenced)
    End Sub

    Private ReadOnly _painters As New List(Of Object)()

    Private Class ComboPainter
        Inherits NativeWindow
        Private ReadOnly _c As ComboBox

        Public Sub New(c As ComboBox)
            _c = c
            If c.IsHandleCreated Then AssignHandle(c.Handle)
            AddHandler c.HandleCreated, Sub() AssignHandle(c.Handle)
            AddHandler c.HandleDestroyed, Sub() ReleaseHandle()
            AddHandler c.GotFocus, Sub() c.Invalidate()
            AddHandler c.LostFocus, Sub() c.Invalidate()
            AddHandler c.MouseEnter, Sub() c.Invalidate()
            AddHandler c.MouseLeave, Sub() c.Invalidate()
        End Sub

        Protected Overrides Sub WndProc(ByRef m As Message)
            MyBase.WndProc(m)
            If m.Msg = &HF Then PaintFrame()     ' WM_PAINT
        End Sub

        Private Sub PaintFrame()
            Try
                Using g = Graphics.FromHwnd(_c.Handle)
                    Dim w = _c.Width, h = _c.Height
                    Dim hot = _c.Focused OrElse _c.ClientRectangle.Contains(_c.PointToClient(Cursor.Position))
                    Using p As New Pen(_c.BackColor, 2)
                        g.DrawRectangle(p, 1, 1, w - 2, h - 2)
                    End Using
                    Dim aw = 20
                    Using b As New SolidBrush(_c.BackColor)
                        g.FillRectangle(b, w - aw - 1, 1, aw, h - 2)
                    End Using
                    g.SmoothingMode = SmoothingMode.AntiAlias
                    Dim cx = w - aw \ 2 - 2, cy = h \ 2
                    Using p As New Pen(CDim, 1.6F)
                        g.DrawLines(p, {New PointF(cx - 4, cy - 2), New PointF(cx, cy + 2), New PointF(cx + 4, cy - 2)})
                    End Using
                    g.SmoothingMode = SmoothingMode.Default
                    Using p As New Pen(If(hot, CAccent, CBorder))
                        g.DrawRectangle(p, 0, 0, w - 1, h - 1)
                    End Using
                End Using
            Catch
            End Try
        End Sub
    End Class

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
                Pill(e.Graphics, New Rectangle(Point.Empty, e.Item.Size), If(e.Item.Selected, ThemePresets.Mix(CAccentSoft, CAccent, 0.3), CAccentSoft), CAccent)
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

        Protected Overrides Sub InitializeItem(item As ToolStripItem)
            MyBase.InitializeItem(item)
            Dim parts = Icons.Split(item.Text)
            If parts.Glyph IsNot Nothing AndAlso Not item.IsOnDropDown Then
                ' the icon takes 22 px where the emoji (and its space) took less: add only the difference
                Dim emoji = item.Text.Substring(0, item.Text.Length - parts.After.Length)
                Dim had = TextRenderer.MeasureText(emoji, item.Font, New Size(200, 50), TextFormatFlags.NoPadding).Width
                Dim extra = Math.Max(0, 22 - had)
                If extra > 0 Then item.Padding = New Padding(item.Padding.Left, item.Padding.Top, item.Padding.Right + extra, item.Padding.Bottom)
            End If
        End Sub

        Protected Overrides Sub OnRenderItemText(e As ToolStripItemTextRenderEventArgs)
            If TypeOf e.Item Is ToolStripLabel AndAlso Not e.Item.IsOnDropDown AndAlso TypeOf e.Item IsNot ToolStripStatusLabel Then
                ' labels keep their own color (e.g. the green / red connection dot)
            ElseIf TypeOf e.Item Is ToolStripStatusLabel Then
                If e.Item.ForeColor = Color.White OrElse e.Item.ForeColor = SystemColors.ControlText Then e.TextColor = CDim
            ElseIf Not e.Item.Enabled Then
                e.TextColor = ThemePresets.Mix(CDim, CPanel, 0.4)
            ElseIf CStr(e.Item.Tag) = PrimaryTag Then
                e.TextColor = CAccentText
            ElseIf TypeOf e.Item Is ToolStripButton AndAlso CType(e.Item, ToolStripButton).Checked Then
                e.TextColor = CText
            Else
                e.TextColor = CText
            End If
            ' a crisp icon in place of the leading emoji
            Dim parts = Icons.Split(e.Text)
            If parts.Glyph IsNot Nothing AndAlso e.Item.DisplayStyle <> ToolStripItemDisplayStyle.Image Then
                Dim r = e.TextRectangle
                Icons.Draw(e.Graphics, parts.Glyph, New Rectangle(r.X, r.Y, 18, r.Height), e.TextColor, 10.0!)
                TextRenderer.DrawText(e.Graphics, parts.After, e.TextFont, New Rectangle(r.X + 22, r.Y, Math.Max(10, r.Width), r.Height), e.TextColor,
                                      (e.TextFormat And Not TextFormatFlags.HorizontalCenter And Not TextFormatFlags.Right) Or TextFormatFlags.Left Or TextFormatFlags.VerticalCenter)
                Return
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
