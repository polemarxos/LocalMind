Imports System
Imports System.IO
Imports System.Linq
Imports System.Text
Imports UglyToad.PdfPig

Public Enum ContentKind
    Text
    Image
    Binary
End Enum

Public Class ExtractedContent
    Public Property Kind As ContentKind
    Public Property Text As String
    Public Property Base64 As String
    Public Property Description As String
End Class

Public Module DocumentService

    Public Const MaxChars As Integer = 16000
    Private Const MaxImageBytes As Long = 15L * 1024 * 1024      ' 15 MB

    Private Function ExtractPdf(filePath As String) As String
        Dim sb As New StringBuilder()
        Using doc = PdfDocument.Open(filePath)
            For Each page In doc.GetPages()
                sb.AppendLine(page.Text)
                sb.AppendLine()
            Next
        End Using
        Return sb.ToString()
    End Function

    Public Function Trim(text As String, Optional limit As Integer = MaxChars) As String
        If text Is Nothing Then Return ""
        If text.Length <= limit Then Return text
        Return text.Substring(0, limit) & vbCrLf & vbCrLf & "[…document truncated…]"
    End Function

    '────────────────────────────────────────────────────────────
    '  Universal extractor — handles ANY file
    '────────────────────────────────────────────────────────────
    Public Function ExtractAnyFile(filePath As String) As ExtractedContent
        Dim result As New ExtractedContent()
        Dim ext = Path.GetExtension(filePath).ToLowerInvariant()

        ' 1. Images → base64 for vision models
        Select Case True
            Case ImageTools.IsPicture(filePath) AndAlso ext <> ".svg"
                Dim fi As New FileInfo(filePath)
                If fi.Length > MaxImageBytes Then
                    result.Kind = ContentKind.Binary
                    result.Description = "[Image too large to attach: " & fi.Name &
                                         ", " & FormatSize(fi.Length) & ". Limit is 15 MB.]"
                    Return result
                End If
                result.Kind = ContentKind.Image
                ' WebP, AVIF, HEIC, TIFF… become PNG: vision models and the picture engine read PNG / JPEG
                result.Base64 = PrepareImageFile(If(ext = ".gif", filePath, ImageTools.AsPng(filePath)))
                Return result
        End Select

        ' 2. PDFs → text
        If ext = ".pdf" Then
            Try
                result.Kind = ContentKind.Text
                result.Text = ExtractPdf(filePath)
                Return result
            Catch
                ' fall through to binary description
            End Try
        End If

        ' 3. Try to read as text (UTF-8 / UTF-16, BOM-aware, null-byte heuristic)
        Dim txt = TryReadText(filePath)
        If txt IsNot Nothing Then
            result.Kind = ContentKind.Text
            result.Text = txt
            Return result
        End If

        ' 4. Everything else → describe the binary file
        Dim fi2 As New FileInfo(filePath)
        result.Kind = ContentKind.Binary
        result.Description =
            "[Binary file attached: " & fi2.Name & "]" & vbCrLf &
            "Type: " & If(String.IsNullOrEmpty(ext), "(no extension)", ext) & vbCrLf &
            "Size: " & FormatSize(fi2.Length) & vbCrLf &
            "Note: This is a binary file. Its raw contents cannot be interpreted as text."
        Return result
    End Function

    '────────────────────────────────────────────────────────────
    '  Images: downscale big photos (vision models work at ~1–2 MP anyway)
    '────────────────────────────────────────────────────────────
    Public Const MaxImageSide As Integer = 2048

    ''' <summary>Returns base64 of the image, downscaled/re-encoded when it is very large.</summary>
    Public Function PrepareImageFile(filePath As String) As String
        Dim ext = Path.GetExtension(filePath).ToLowerInvariant()
        Dim raw = File.ReadAllBytes(filePath)
        ' GDI+ can't decode WebP; GIFs may be animated — send those untouched.
        If ext = ".gif" Then Return Convert.ToBase64String(raw)
        Try
            Using ms As New MemoryStream(raw)
                Using img = Drawing.Image.FromStream(ms)
                    If img.Width <= MaxImageSide AndAlso img.Height <= MaxImageSide AndAlso raw.Length <= 3 * 1024 * 1024 AndAlso ext <> ".bmp" Then
                        Return Convert.ToBase64String(raw)
                    End If
                    Return ImageToBase64(img, keepPng:=(ext = ".png"))
                End Using
            End Using
        Catch
            Return Convert.ToBase64String(raw)
        End Try
    End Function

    ''' <summary>Encodes an image (e.g. from the clipboard) as base64, downscaled to MaxImageSide.</summary>
    Public Function ImageToBase64(img As Drawing.Image, Optional keepPng As Boolean = True) As String
        Dim scale = Math.Min(1.0, MaxImageSide / Math.Max(img.Width, img.Height))
        Dim w = Math.Max(1, CInt(img.Width * scale))
        Dim h = Math.Max(1, CInt(img.Height * scale))
        Using bmp As New Drawing.Bitmap(w, h)
            Using g = Drawing.Graphics.FromImage(bmp)
                g.InterpolationMode = Drawing.Drawing2D.InterpolationMode.HighQualityBicubic
                g.DrawImage(img, 0, 0, w, h)
            End Using
            Using outMs As New MemoryStream()
                If keepPng Then
                    bmp.Save(outMs, Drawing.Imaging.ImageFormat.Png)
                    ' Photos pasted as PNG can be huge — fall back to JPEG when PNG is wasteful.
                    If outMs.Length > 2 * 1024 * 1024 Then
                        outMs.SetLength(0)
                        SaveJpeg(bmp, outMs, 90)
                    End If
                Else
                    SaveJpeg(bmp, outMs, 90)
                End If
                Return Convert.ToBase64String(outMs.ToArray())
            End Using
        End Using
    End Function

    Private Sub SaveJpeg(bmp As Drawing.Bitmap, s As Stream, quality As Long)
        Dim codec = Drawing.Imaging.ImageCodecInfo.GetImageEncoders().FirstOrDefault(Function(c) c.MimeType = "image/jpeg")
        If codec Is Nothing Then
            bmp.Save(s, Drawing.Imaging.ImageFormat.Jpeg)
            Return
        End If
        Using p As New Drawing.Imaging.EncoderParameters(1)
            p.Param(0) = New Drawing.Imaging.EncoderParameter(Drawing.Imaging.Encoder.Quality, quality)
            bmp.Save(s, codec, p)
        End Using
    End Sub

    '────────────────────────────────────────────────────────────
    '  Helpers
    '────────────────────────────────────────────────────────────
    Private Function TryReadText(filePath As String) As String
        Try
            Dim bytes = File.ReadAllBytes(filePath)
            If bytes.Length = 0 Then Return ""

            ' BOM detection
            If bytes.Length >= 3 AndAlso bytes(0) = &HEF AndAlso bytes(1) = &HBB AndAlso bytes(2) = &HBF Then
                Return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3)
            End If
            If bytes.Length >= 2 AndAlso bytes(0) = &HFF AndAlso bytes(1) = &HFE Then
                Return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2)
            End If
            If bytes.Length >= 2 AndAlso bytes(0) = &HFE AndAlso bytes(1) = &HFF Then
                Return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2)
            End If

            ' Heuristic: null byte in first 8 KB → binary
            Dim checkLen = Math.Min(bytes.Length, 8192)
            For i = 0 To checkLen - 1
                If bytes(i) = 0 Then Return Nothing
            Next

            ' Looks like text — decode as UTF-8 (lossy on invalid sequences, fine for our purpose)
            Return Encoding.UTF8.GetString(bytes)
        Catch
            Return Nothing
        End Try
    End Function

    Private Function FormatSize(bytes As Long) As String
        If bytes < 1024 Then Return bytes & " B"
        If bytes < 1024L * 1024 Then Return (bytes / 1024.0).ToString("0.0") & " KB"
        If bytes < 1024L * 1024 * 1024 Then Return (bytes / 1024.0 / 1024.0).ToString("0.0") & " MB"
        Return (bytes / 1024.0 / 1024.0 / 1024.0).ToString("0.0") & " GB"
    End Function
End Module