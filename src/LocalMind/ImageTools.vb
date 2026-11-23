Imports System
Imports System.Drawing
Imports System.Drawing.Imaging
Imports System.IO
Imports System.Linq
Imports System.Runtime.InteropServices

''' <summary>Fast pixel work on pictures (Windows, System.Drawing): masks, blending, alpha.</summary>
Public Module ImageTools

    ''' <summary>Loads a picture without keeping the file locked.</summary>
    Public Function Load(path As String) As Bitmap
        Using fs As New FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)
            Using img = Image.FromStream(fs)
                Return New Bitmap(img)
            End Using
        End Using
    End Function

    ''' <summary>ARGB pixels of a 32-bit copy of the bitmap.</summary>
    Public Function Pixels(bmp As Bitmap) As Integer()
        Using b32 = If(bmp.PixelFormat = PixelFormat.Format32bppArgb, Nothing, New Bitmap(bmp.Width, bmp.Height, PixelFormat.Format32bppArgb))
            Dim src = bmp
            If b32 IsNot Nothing Then
                Using g = Graphics.FromImage(b32)
                    g.DrawImage(bmp, 0, 0, bmp.Width, bmp.Height)
                End Using
                src = b32
            End If
            Dim data = src.LockBits(New Rectangle(0, 0, src.Width, src.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb)
            Dim px(src.Width * src.Height - 1) As Integer
            Marshal.Copy(data.Scan0, px, 0, px.Length)
            src.UnlockBits(data)
            Return px
        End Using
    End Function

    Public Function FromPixels(px As Integer(), w As Integer, h As Integer) As Bitmap
        Dim bmp As New Bitmap(w, h, PixelFormat.Format32bppArgb)
        Dim data = bmp.LockBits(New Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb)
        Marshal.Copy(px, 0, data.Scan0, px.Length)
        bmp.UnlockBits(data)
        Return bmp
    End Function

    ''' <summary>Box blur (3 passes ≈ Gaussian) of a 0–255 channel.</summary>
    Public Function Blur(ch As Byte(), w As Integer, h As Integer, radius As Integer) As Byte()
        If radius < 1 Then Return ch
        Dim a = ch.Select(Function(b) CSng(b)).ToArray()
        Dim tmp(a.Length - 1) As Single
        For pass = 1 To 3
            ' horizontal
            For y = 0 To h - 1
                Dim row = y * w
                Dim sum As Single = 0
                For x = -radius To radius
                    sum += a(row + Math.Clamp(x, 0, w - 1))
                Next
                For x = 0 To w - 1
                    tmp(row + x) = sum / (2 * radius + 1)
                    sum += a(row + Math.Min(w - 1, x + radius + 1)) - a(row + Math.Max(0, x - radius))
                Next
            Next
            ' vertical
            For x = 0 To w - 1
                Dim sum As Single = 0
                For y = -radius To radius
                    sum += tmp(Math.Clamp(y, 0, h - 1) * w + x)
                Next
                For y = 0 To h - 1
                    a(y * w + x) = sum / (2 * radius + 1)
                    sum += tmp(Math.Min(h - 1, y + radius + 1) * w + x) - tmp(Math.Max(0, y - radius) * w + x)
                Next
            Next
        Next
        Return a.Select(Function(v) CByte(Math.Clamp(CInt(v), 0, 255))).ToArray()
    End Function

    Private Function Resized(bmp As Bitmap, w As Integer, h As Integer) As Bitmap
        If bmp.Width = w AndAlso bmp.Height = h Then Return New Bitmap(bmp)
        Dim r As New Bitmap(w, h, PixelFormat.Format32bppArgb)
        Using g = Graphics.FromImage(r)
            g.InterpolationMode = Drawing2D.InterpolationMode.HighQualityBicubic
            g.PixelOffsetMode = Drawing2D.PixelOffsetMode.HighQuality
            g.DrawImage(bmp, 0, 0, w, h)
        End Using
        Return r
    End Function

    ''' <summary>
    ''' Puts the newly drawn area back into the original picture through a softened mask, so everything outside
    ''' the painted area stays pixel-perfect and the edges blend smoothly. Overwrites <paramref name="result"/>.
    ''' </summary>
    Public Sub CompositeInpaint(original As String, result As String, mask As String)
        Using orig = Load(original), gen0 = Load(result), m0 = Load(mask)
            Dim w = orig.Width, h = orig.Height
            Using gen = Resized(gen0, w, h), m = Resized(m0, w, h)
                Dim po = Pixels(orig), pg = Pixels(gen), pm = Pixels(m)
                Dim ch(pm.Length - 1) As Byte
                For i = 0 To pm.Length - 1
                    ch(i) = CByte((pm(i) >> 16) And &HFF)
                Next
                ' Grow the painted area a little, then feather it.
                Dim radius = Math.Max(2, Math.Max(w, h) \ 160)
                Dim grown = Blur(ch, w, h, radius)
                For i = 0 To grown.Length - 1
                    grown(i) = If(grown(i) > 8, CByte(255), CByte(0))
                Next
                Dim soft = Blur(grown, w, h, radius)
                Dim outPx(po.Length - 1) As Integer
                For i = 0 To po.Length - 1
                    Dim k = soft(i) / 255.0F
                    Dim a = po(i), b = pg(i)
                    Dim r = CInt(((a >> 16) And &HFF) * (1 - k) + ((b >> 16) And &HFF) * k)
                    Dim gg = CInt(((a >> 8) And &HFF) * (1 - k) + ((b >> 8) And &HFF) * k)
                    Dim bb = CInt((a And &HFF) * (1 - k) + (b And &HFF) * k)
                    outPx(i) = (&HFF << 24) Or (r << 16) Or (gg << 8) Or bb
                Next
                Using res = FromPixels(outPx, w, h)
                    res.Save(result, ImageFormat.Png)
                End Using
            End Using
        End Using
    End Sub

    ''' <summary>Applies a 0–255 alpha map (any size, resized) to a picture and saves it as a transparent PNG.</summary>
    Public Sub ApplyAlpha(picture As String, alpha As Byte(), aw As Integer, ah As Integer, output As String)
        Using img = Load(picture)
            Dim w = img.Width, h = img.Height
            Dim px = Pixels(img)
            ' Smoothly scale the small alpha map up to the picture size.
            Dim grey(alpha.Length - 1) As Integer
            For i = 0 To alpha.Length - 1
                Dim v = CInt(alpha(i))
                grey(i) = (&HFF << 24) Or (v << 16) Or (v << 8) Or v
            Next
            Dim big As Integer()
            Using small = FromPixels(grey, aw, ah)
                Using scaled = Resized(small, w, h)
                    big = Pixels(scaled)
                End Using
            End Using
            For i = 0 To px.Length - 1
                Dim al = (big(i) >> 16) And &HFF
                px(i) = (al << 24) Or (px(i) And &HFFFFFF)
            Next
            Using res = FromPixels(px, w, h)
                res.Save(output, ImageFormat.Png)
            End Using
        End Using
    End Sub

    ''' <summary>RGB of a picture resized to w × h, as floats 0–1 in CHW order (for ONNX models), normalized.</summary>
    Public Function ToTensor(picture As String, w As Integer, h As Integer, mean As Single(), std As Single()) As (Data As Single(), OrigW As Integer, OrigH As Integer)
        Using img = Load(picture)
            Using r = Resized(img, w, h)
                Dim px = Pixels(r)
                Dim t(3 * w * h - 1) As Single
                For i = 0 To px.Length - 1
                    t(i) = ((((px(i) >> 16) And &HFF) / 255.0F) - mean(0)) / std(0)
                    t(w * h + i) = ((((px(i) >> 8) And &HFF) / 255.0F) - mean(1)) / std(1)
                    t(2 * w * h + i) = (((px(i) And &HFF) / 255.0F) - mean(2)) / std(2)
                Next
                Return (t, img.Width, img.Height)
            End Using
        End Using
    End Function

    ''' <summary>Saves a single-channel float map (normalized to 0–255) as a grey PNG of the given size.</summary>
    Public Sub SaveGrey(values As Single(), w As Integer, h As Integer, outW As Integer, outH As Integer, output As String, Optional invert As Boolean = False)
        Dim mn = values.Min(), mx = values.Max()
        Dim scale = If(mx > mn, 255.0F / (mx - mn), 0)
        Dim px(values.Length - 1) As Integer
        For i = 0 To values.Length - 1
            Dim v = CInt((values(i) - mn) * scale)
            If invert Then v = 255 - v
            px(i) = (&HFF << 24) Or (v << 16) Or (v << 8) Or v
        Next
        Using small = FromPixels(px, w, h)
            Using big = Resized(small, outW, outH)
                big.Save(output, ImageFormat.Png)
            End Using
        End Using
    End Sub

    ''' <summary>Edge map (Sobel, thresholded) for "keep the outlines" control.</summary>
    Public Sub SaveEdges(picture As String, output As String)
        Using img = Load(picture)
            Dim w = img.Width, h = img.Height
            Dim px = Pixels(img)
            Dim g(px.Length - 1) As Single
            For i = 0 To px.Length - 1
                g(i) = 0.299F * ((px(i) >> 16) And &HFF) + 0.587F * ((px(i) >> 8) And &HFF) + 0.114F * (px(i) And &HFF)
            Next
            Dim outPx(px.Length - 1) As Integer
            For y = 1 To h - 2
                For x = 1 To w - 2
                    Dim i = y * w + x
                    Dim gx = -g(i - w - 1) - 2 * g(i - 1) - g(i + w - 1) + g(i - w + 1) + 2 * g(i + 1) + g(i + w + 1)
                    Dim gy = -g(i - w - 1) - 2 * g(i - w) - g(i - w + 1) + g(i + w - 1) + 2 * g(i + w) + g(i + w + 1)
                    Dim v = If(Math.Sqrt(gx * gx + gy * gy) > 110, 255, 0)
                    outPx(i) = (&HFF << 24) Or (v << 16) Or (v << 8) Or v
                Next
            Next
            For i = 0 To outPx.Length - 1
                If outPx(i) = 0 Then outPx(i) = &HFF << 24
            Next
            Using res = FromPixels(outPx, w, h)
                res.Save(output, ImageFormat.Png)
            End Using
        End Using
    End Sub
End Module
