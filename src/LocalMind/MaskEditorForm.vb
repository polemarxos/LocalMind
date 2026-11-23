Imports System
Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Drawing.Imaging
Imports System.IO
Imports System.Windows.Forms

''' <summary>
''' Paint over the part of a picture that should change. Left mouse paints, right mouse erases.
''' Saves a black-and-white mask the size of the picture (white = change).
''' </summary>
Public Class MaskEditorForm
    Inherits Form

    Private ReadOnly _image As Image
    Private ReadOnly _mask As Bitmap
    Private ReadOnly canvas As PictureBox
    Private ReadOnly trkBrush As TrackBar
    Private _last As Point?
    <System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)>
    Public Property MaskPath As String

    Public Sub New(imagePath As String, Optional existingMask As String = Nothing)
        Text = "Paint the area to change"
        Size = New Size(1000, 820)
        StartPosition = FormStartPosition.CenterParent
        Theme.ApplyForm(Me)
        Using fs As New FileStream(imagePath, FileMode.Open, FileAccess.Read)
            _image = New Bitmap(Image.FromStream(fs))
        End Using
        _mask = New Bitmap(_image.Width, _image.Height, PixelFormat.Format32bppArgb)
        Using g = Graphics.FromImage(_mask)
            g.Clear(Color.Transparent)
            If existingMask IsNot Nothing AndAlso File.Exists(existingMask) Then
                Using m As New Bitmap(existingMask)
                    For y = 0 To Math.Min(m.Height, _mask.Height) - 1 Step 1
                        For x = 0 To Math.Min(m.Width, _mask.Width) - 1
                            If m.GetPixel(x, y).R > 127 Then _mask.SetPixel(x, y, Color.FromArgb(150, 255, 40, 80))
                        Next
                    Next
                End Using
            End If
        End Using

        Dim bar As New FlowLayoutPanel() With {.Dock = DockStyle.Top, .Height = 44, .Padding = New Padding(6)}
        trkBrush = New TrackBar() With {.Minimum = 4, .Maximum = 200, .Value = Math.Max(8, Math.Min(200, Math.Max(_image.Width, _image.Height) \ 16)),
                                        .Width = 220, .TickStyle = TickStyle.None}
        Dim btnClear = Theme.MakeButton("Clear")
        Dim btnAll = Theme.MakeButton("Invert")
        Dim btnOk = Theme.MakeButton("✔ Use this area", primary:=True, width:=150)
        Dim hint = Theme.MakeLabel("Left mouse = paint · Right mouse = erase")
        bar.Controls.AddRange({Theme.MakeLabel("Brush"), trkBrush, btnClear, btnAll, btnOk, hint})

        canvas = New PictureBox() With {.Dock = DockStyle.Fill, .BackColor = Theme.CBack, .SizeMode = PictureBoxSizeMode.Zoom, .Cursor = Cursors.Cross}
        Controls.Add(canvas)
        Controls.Add(bar)

        AddHandler canvas.Paint, AddressOf DrawCanvas
        AddHandler canvas.MouseDown, Sub(s, e)
                                         _last = Nothing
                                         Stroke(e)
                                     End Sub
        AddHandler canvas.MouseMove, Sub(s, e) If e.Button <> MouseButtons.None Then Stroke(e)
        AddHandler canvas.MouseUp, Sub() _last = Nothing
        AddHandler btnClear.Click, Sub()
                                       Using g = Graphics.FromImage(_mask)
                                           g.Clear(Color.Transparent)
                                       End Using
                                       canvas.Invalidate()
                                   End Sub
        AddHandler btnAll.Click, Sub()
                                     For y = 0 To _mask.Height - 1
                                         For x = 0 To _mask.Width - 1
                                             _mask.SetPixel(x, y, If(_mask.GetPixel(x, y).A > 0, Color.Transparent, Color.FromArgb(150, 255, 40, 80)))
                                         Next
                                     Next
                                     canvas.Invalidate()
                                 End Sub
        AddHandler btnOk.Click, Sub() SaveMask()
    End Sub

    ''' <summary>Where the picture is drawn inside the PictureBox (Zoom mode).</summary>
    Private Function ImageRect() As RectangleF
        Dim k = Math.Min(canvas.Width / _image.Width, canvas.Height / _image.Height)
        Dim w = _image.Width * k, h = _image.Height * k
        Return New RectangleF((canvas.Width - w) / 2, (canvas.Height - h) / 2, CSng(w), CSng(h))
    End Function

    Private Sub DrawCanvas(sender As Object, e As PaintEventArgs)
        Dim r = ImageRect()
        e.Graphics.InterpolationMode = InterpolationMode.HighQualityBilinear
        e.Graphics.DrawImage(_image, r)
        e.Graphics.DrawImage(_mask, r)
    End Sub

    Private Sub Stroke(e As MouseEventArgs)
        Dim r = ImageRect()
        If r.Width <= 0 Then Return
        Dim k = _image.Width / r.Width
        Dim p As New Point(CInt((e.X - r.X) * k), CInt((e.Y - r.Y) * k))
        Dim isErase = e.Button = MouseButtons.Right
        Using g = Graphics.FromImage(_mask)
            g.SmoothingMode = SmoothingMode.AntiAlias
            g.CompositingMode = CompositingMode.SourceCopy
            Dim c = If(isErase, Color.Transparent, Color.FromArgb(150, 255, 40, 80))
            Using pen As New Pen(c, trkBrush.Value) With {.StartCap = LineCap.Round, .EndCap = LineCap.Round}
                If _last.HasValue Then
                    g.DrawLine(pen, _last.Value, p)
                Else
                    Using b As New SolidBrush(c)
                        g.FillEllipse(b, p.X - trkBrush.Value \ 2, p.Y - trkBrush.Value \ 2, trkBrush.Value, trkBrush.Value)
                    End Using
                End If
            End Using
        End Using
        _last = p
        canvas.Invalidate()
    End Sub

    Private Sub SaveMask()
        Dim any = False
        Using bw As New Bitmap(_mask.Width, _mask.Height, PixelFormat.Format24bppRgb)
            For y = 0 To _mask.Height - 1
                For x = 0 To _mask.Width - 1
                    Dim isOn = _mask.GetPixel(x, y).A > 0
                    any = any OrElse isOn
                    bw.SetPixel(x, y, If(isOn, Color.White, Color.Black))
                Next
            Next
            If Not any Then
                Localizer.Box(Me, "Paint over the part of the picture you want to change first.", "Paint")
                Return
            End If
            MaskPath = Path.Combine(Path.GetTempPath(), $"localmind-mask-{Guid.NewGuid():N}.png")
            bw.Save(MaskPath, ImageFormat.Png)
        End Using
        DialogResult = DialogResult.OK
        Close()
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then
            _image?.Dispose()
            _mask?.Dispose()
        End If
        MyBase.Dispose(disposing)
    End Sub
End Class
