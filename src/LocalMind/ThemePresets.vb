Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.Linq

''' <summary>
''' The looks you can choose in Settings → Appearance: colors, font and contrast for every window and the chat.
''' A preset is applied once at startup (before any window is made).
''' </summary>
Public Module ThemePresets

    Public Class Preset
        Public Property Name As String
        Public Property Dark As Boolean
        Public Property Back As Color        ' lists, inputs, chat
        Public Property Panel As Color       ' window background
        Public Property Raised As Color      ' bars, headers, cards
        Public Property Control As Color     ' buttons
        Public Property Text As Color
        Public Property Dimmed As Color
        Public Property Accent As Color
        Public Property Font As String = "Segoe UI"
        Public Property FontSize As Single = 9.5!
        Public Property Good As Color
        Public Property Warn As Color
        Public Property Bad As Color
        Public Overrides Function ToString() As String
            Return Name
        End Function
    End Class

    Private Function H(hex As String) As Color
        Return ColorTranslator.FromHtml(hex)
    End Function

    Private Function D(name As String, back As String, panel As String, raised As String, control As String, text As String, dimmed As String,
                       accent As String, Optional font As String = "Segoe UI", Optional size As Single = 9.5!) As Preset
        Return New Preset With {.Name = name, .Dark = True, .Back = H(back), .Panel = H(panel), .Raised = H(raised), .Control = H(control),
                                .Text = H(text), .Dimmed = H(dimmed), .Accent = H(accent), .Font = font, .FontSize = size,
                                .Good = Color.FromArgb(110, 200, 120), .Warn = Color.FromArgb(230, 180, 80), .Bad = Color.FromArgb(225, 105, 105)}
    End Function

    Private Function L(name As String, back As String, panel As String, raised As String, control As String, text As String, dimmed As String,
                       accent As String, Optional font As String = "Segoe UI", Optional size As Single = 9.5!) As Preset
        Return New Preset With {.Name = name, .Dark = False, .Back = H(back), .Panel = H(panel), .Raised = H(raised), .Control = H(control),
                                .Text = H(text), .Dimmed = H(dimmed), .Accent = H(accent), .Font = font, .FontSize = size,
                                .Good = Color.FromArgb(22, 128, 61), .Warn = Color.FromArgb(180, 110, 0), .Bad = Color.FromArgb(200, 40, 40)}
    End Function

    ''' <summary>All looks, in the order shown in Settings (numbered like the preview pictures).</summary>
    Public ReadOnly All As List(Of Preset) = New List(Of Preset) From {
        D("Midnight", "#18181B", "#1F1F23", "#292A2F", "#36383E", "#E8E8EC", "#9698A2", "#2563EB", "Segoe UI Variable Text"),
        D("Graphite Pro", "#1C1C1E", "#232325", "#2C2C2E", "#3A3A3C", "#F2F2F7", "#98989F", "#0A84FF", "Segoe UI Variable Text"),
        D("Windows 11 Dark", "#1C1C1C", "#202020", "#2B2B2B", "#373737", "#FFFFFF", "#A0A0A0", "#0078D4", "Segoe UI Variable Text"),
        D("Studio Code", "#1E1E1E", "#252526", "#2D2D30", "#3C3C3C", "#D4D4D4", "#9D9D9D", "#0E639C"),
        D("GitHub Dark", "#0D1117", "#161B22", "#1C2128", "#2D333B", "#E6EDF3", "#8D96A0", "#1F6FEB"),
        D("Nord", "#2E3440", "#3B4252", "#434C5E", "#4C566A", "#ECEFF4", "#A3ACBD", "#5E81AC"),
        D("Dracula", "#21222C", "#282A36", "#343746", "#44475A", "#F8F8F2", "#A4A9C4", "#BD93F9"),
        D("One Dark", "#21252B", "#282C34", "#2C313A", "#3A3F4B", "#DCDFE4", "#9DA5B4", "#4D78CC"),
        D("Tokyo Night", "#16161E", "#1A1B26", "#24283B", "#2F3549", "#C0CAF5", "#8089B3", "#7AA2F7"),
        D("Catppuccin", "#181825", "#1E1E2E", "#313244", "#45475A", "#CDD6F4", "#A6ADC8", "#CBA6F7"),
        D("Gruvbox", "#1D2021", "#282828", "#32302F", "#45403D", "#EBDBB2", "#BDAE93", "#D65D0E", "Bahnschrift"),
        D("Solarized Dark", "#002B36", "#073642", "#0B4250", "#145361", "#EEE8D5", "#93A1A1", "#268BD2"),
        D("Monokai", "#1E1F1C", "#272822", "#33342D", "#414339", "#F8F8F2", "#A6A28C", "#F92672"),
        D("Deep Ocean", "#0B1622", "#0F1E2E", "#16283C", "#1F3550", "#E3EEF8", "#8FA8C0", "#0EA5E9"),
        D("Forest", "#111A15", "#16201B", "#1E2B24", "#2A3A31", "#E4EFE7", "#93AA9B", "#2EA043"),
        D("Crimson", "#161214", "#1C1719", "#262022", "#352C2F", "#F1E9EA", "#A8999C", "#E5484D"),
        D("Amber", "#15120E", "#1A1612", "#241F19", "#332C23", "#F3EADF", "#AE9F8C", "#D97706", "Bahnschrift"),
        D("Royal Purple", "#120F22", "#17132A", "#211C38", "#2E2750", "#ECE8FB", "#A29BC4", "#7C3AED"),
        D("OLED Black", "#000000", "#0A0A0A", "#141414", "#222222", "#F5F5F5", "#9A9A9A", "#3B82F6"),
        D("Slate Teal", "#151C24", "#1B2430", "#23303E", "#2E3E50", "#E6EEF5", "#93A4B5", "#0D9488"),
        D("High Contrast", "#000000", "#000000", "#101010", "#202020", "#FFFFFF", "#D0D0D0", "#FFD400", "Segoe UI", 10.5!),
        D("Terminal", "#0C0C0C", "#111111", "#1A1A1A", "#262626", "#D7E4D7", "#8FA58F", "#16A34A", "Cascadia Mono", 9.5!),
        L("Windows 11 Light", "#FFFFFF", "#F3F3F3", "#EBEBEB", "#FBFBFB", "#1B1B1B", "#5F5F5F", "#005FB8", "Segoe UI Variable Text"),
        L("Paper", "#FFFFFF", "#FAFAF7", "#F0EFEA", "#FFFFFF", "#1F2328", "#656D76", "#2563EB"),
        L("GitHub Light", "#FFFFFF", "#F6F8FA", "#EAEEF2", "#F6F8FA", "#1F2328", "#59636E", "#0969DA"),
        L("Nord Snow", "#FFFFFF", "#ECEFF4", "#E5E9F0", "#F5F7FA", "#2E3440", "#5B6476", "#5E81AC"),
        L("Solarized Light", "#FFFBEF", "#FDF6E3", "#EEE8D5", "#FFFBEF", "#073642", "#586E75", "#268BD2"),
        L("Mint", "#FFFFFF", "#F2FAF6", "#E3F2EB", "#FFFFFF", "#10231A", "#4F6B5D", "#059669"),
        L("Lavender", "#FFFFFF", "#F6F4FE", "#ECE8FB", "#FFFFFF", "#1E1736", "#625A7E", "#6D28D9"),
        L("Sepia", "#FBF6EA", "#F4ECD8", "#EADFC6", "#FBF6EA", "#3B2F23", "#7A6650", "#8B5E34", "Georgia", 9.5!)}

    Public Function Find(name As String) As Preset
        Return If(All.FirstOrDefault(Function(p) p.Name.Equals(If(name, ""), StringComparison.OrdinalIgnoreCase)), All(0))
    End Function

    ''' <summary>A font that exists on this PC (the preset's, else Segoe UI).</summary>
    Public Function FontOrDefault(family As String) As String
        Try
            Using f As New FontFamily(family)
                Return f.Name
            End Using
        Catch
            Return "Segoe UI"
        End Try
    End Function

    Public Function Mix(a As Color, b As Color, t As Double) As Color
        t = Math.Max(0, Math.Min(1, t))
        Return Color.FromArgb(CInt(a.R + (CInt(b.R) - a.R) * t), CInt(a.G + (CInt(b.G) - a.G) * t), CInt(a.B + (CInt(b.B) - a.B) * t))
    End Function

    ''' <summary>Relative brightness 0 (black) … 1 (white).</summary>
    Public Function Lum(c As Color) As Double
        Return (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0
    End Function

    Public Function IsGray(c As Color) As Boolean
        Return Math.Max(c.R, Math.Max(c.G, c.B)) - Math.Min(c.R, Math.Min(c.G, c.B)) < 28
    End Function
End Module
