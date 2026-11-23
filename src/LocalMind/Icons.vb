Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.Linq

''' <summary>
''' Crisp monochrome Windows icons (Segoe Fluent Icons / Segoe MDL2 Assets) in place of the colored emoji at the
''' start of button, menu and tab texts. The texts keep their emoji (so translations and code stay the same);
''' only the drawing swaps them for the matching icon.
''' </summary>
Public Module Icons
    Private ReadOnly Map As New Dictionary(Of String, Char) From {
        {"⬇", ChrW(&HE896)}, {"⬆", ChrW(&HE898)}, {"🗑", ChrW(&HE74D)}, {"✨", ChrW(&HE945)}, {"⚡", ChrW(&HE945)},
        {"📁", ChrW(&HE8B7)}, {"📂", ChrW(&HE838)}, {"🗂", ChrW(&HE8B7)}, {"🔍", ChrW(&HE721)}, {"⟳", ChrW(&HE72C)},
        {"🔄", ChrW(&HE72C)}, {"♻", ChrW(&HE72C)}, {"★", ChrW(&HE735)}, {"⭐", ChrW(&HE735)}, {"☆", ChrW(&HE734)},
        {"🎨", ChrW(&HE790)}, {"🎬", ChrW(&HE714)}, {"📽", ChrW(&HE714)}, {"🎞", ChrW(&HE8B2)}, {"🎵", ChrW(&HE8D6)},
        {"🎶", ChrW(&HE8D6)}, {"📱", ChrW(&HE8EA)}, {"■", ChrW(&HE71A)}, {"⏹", ChrW(&HE71A)}, {"▶", ChrW(&HE768)},
        {"✔", ChrW(&HE73E)}, {"✓", ChrW(&HE73E)}, {"✖", ChrW(&HE711)}, {"✕", ChrW(&HE711)}, {"❌", ChrW(&HE711)},
        {"🎭", ChrW(&HE716)}, {"👥", ChrW(&HE716)}, {"📝", ChrW(&HE70F)}, {"✏", ChrW(&HE70F)}, {"🖼", ChrW(&HEB9F)},
        {"⚙", ChrW(&HE713)}, {"💬", ChrW(&HE8BD)}, {"🌐", ChrW(&HE774)}, {"＋", ChrW(&HE710)}, {"⏩", ChrW(&HE72A)},
        {"🗣", ChrW(&HE720)}, {"🎙", ChrW(&HE720)}, {"🎤", ChrW(&HE720)}, {"📸", ChrW(&HE722)}, {"📷", ChrW(&HE722)},
        {"📦", ChrW(&HE7B8)}, {"🔊", ChrW(&HE767)}, {"🌙", ChrW(&HE708)}, {"💤", ChrW(&HE708)}, {"🔓", ChrW(&HE785)},
        {"🔒", ChrW(&HE72E)}, {"💾", ChrW(&HE74E)}, {"📋", ChrW(&HE8C8)}, {"🔗", ChrW(&HE71B)}, {"ℹ", ChrW(&HE946)},
        {"❓", ChrW(&HE897)}, {"🧩", ChrW(&HE8F1)}, {"📚", ChrW(&HE8F1)}, {"🖌", ChrW(&HE7E6)}, {"✂", ChrW(&HE8C6)},
        {"🏠", ChrW(&HE80F)}, {"🧍", ChrW(&HE77B)}, {"💭", ChrW(&HE8BD)}, {"📎", ChrW(&HE723)}, {"👁", ChrW(&HE890)},
        {"📄", ChrW(&HE8A5)}, {"⏳", ChrW(&HE916)}, {"🕘", ChrW(&HE823)}, {"🧹", ChrW(&HE74D)}, {"↻", ChrW(&HE72C)},
        {"⏸", ChrW(&HE769)}, {"🛑", ChrW(&HE71A)}, {"🎯", ChrW(&HE7C1)}, {"🧾", ChrW(&HE8A5)}, {"✉", ChrW(&HE715)},
        {"🔧", ChrW(&HE90F)}, {"🛠", ChrW(&HE90F)}, {"📊", ChrW(&HE9D9)}, {"🧪", ChrW(&HE9D9)}, {"🤖", ChrW(&HE99A)},
        {"🧠", ChrW(&HE99A)}, {"📤", ChrW(&HE898)}, {"📥", ChrW(&HE896)}, {"🔁", ChrW(&HE8EE)}, {"🎧", ChrW(&HE7F6)}}

    Private _family As FontFamily
    Private _checked As Boolean

    ''' <summary>The icon font of this PC (Windows 11: Segoe Fluent Icons; Windows 10: Segoe MDL2 Assets), or Nothing.</summary>
    Public ReadOnly Property Family As FontFamily
        Get
            If Not _checked Then
                _checked = True
                For Each name In {"Segoe Fluent Icons", "Segoe MDL2 Assets"}
                    Try
                        Dim f As New FontFamily(name)
                        If f.Name = name Then _family = f : Exit For
                    Catch
                    End Try
                Next
            End If
            Return _family
        End Get
    End Property

    ''' <summary>The icon for the emoji at the start of a text, and the text without it (or Nothing, text).</summary>
    Public Function Split(text As String) As (Glyph As String, After As String)
        If String.IsNullOrEmpty(text) OrElse Family Is Nothing Then Return (Nothing, text)
        Dim t = text.TrimStart()
        Dim lead = text.Length - t.Length
        ' the first "character" may be a surrogate pair, maybe followed by the emoji variation selector
        Dim n = If(Char.IsHighSurrogate(t(0)) AndAlso t.Length > 1, 2, 1)
        Dim key = t.Substring(0, n)
        Dim glyph As Char
        If Not Map.TryGetValue(key, glyph) Then Return (Nothing, text)
        Dim rest = t.Substring(n)
        If rest.StartsWith(ChrW(&HFE0F)) Then rest = rest.Substring(1)
        Return (glyph.ToString(), rest.TrimStart())
    End Function

    ''' <summary>Draws an icon centred in a box.</summary>
    Public Sub Draw(g As Graphics, glyph As String, box As Rectangle, color As Color, size As Single)
        Using f As New Font(Family, size, GraphicsUnit.Point)
            System.Windows.Forms.TextRenderer.DrawText(g, glyph, f, box, color,
                System.Windows.Forms.TextFormatFlags.HorizontalCenter Or System.Windows.Forms.TextFormatFlags.VerticalCenter Or System.Windows.Forms.TextFormatFlags.NoPadding)
        End Using
    End Sub
End Module
