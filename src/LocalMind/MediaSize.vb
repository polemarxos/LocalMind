Imports System
Imports System.Globalization
Imports System.Text.RegularExpressions
Imports Newtonsoft.Json.Linq

''' <summary>
''' The length and size the user asked for in the chat ("a video of 3 seconds at 0.2 MP", "a 2 megapixel picture").
''' Empty values mean the model's own defaults.
''' </summary>
Public Class MediaSize
    Public Property Seconds As Double
    Public Property Megapixels As Double
    ''' <summary>"720p", "1080p", "4k": the short side in pixels (0 = not asked).</summary>
    Public Property Lines As Integer

    Private Shared ReadOnly SecondsRx As New Regex("(\d+(?:[.,]\d+)?)\s*(?:-\s*)?(?:seconds?|secons?|secs?|sec\b|s\b|δευτερόλεπτ\w*|δευτ\w*)", RegexOptions.IgnoreCase Or RegexOptions.Compiled)
    Private Shared ReadOnly LinesRx As New Regex("\b(240|360|480|540|576|720|1080|1440|2160)\s*p\b|\b([248])k\b", RegexOptions.IgnoreCase Or RegexOptions.Compiled)
    Private Shared ReadOnly MpRx As New Regex("(\d+(?:[.,]\d+)?)\s*(?:mp\b|megapixels?|mpx\b|μεγαπίξελ)", RegexOptions.IgnoreCase Or RegexOptions.Compiled)

    ''' <summary>From the tool call (seconds / megapixels), else from the user's own words.</summary>
    Public Shared Function From(args As JObject, userText As String) As MediaSize
        Dim m As New MediaSize()
        Try
            m.Seconds = If(args?.Value(Of Double?)("seconds"), 0)
            m.Megapixels = If(args?.Value(Of Double?)("megapixels"), 0)
        Catch
        End Try
        Dim t = If(userText, "")
        If m.Seconds <= 0 Then m.Seconds = Number(SecondsRx.Match(t))
        If m.Megapixels <= 0 Then m.Megapixels = Number(MpRx.Match(t))
        If m.Megapixels <= 0 Then
            Dim lm = LinesRx.Match(t)
            If lm.Success Then m.Lines = If(lm.Groups(1).Success, CInt(lm.Groups(1).Value), If(lm.Groups(2).Value = "2", 1440, If(lm.Groups(2).Value = "4", 2160, 4320)))
        End If
        m.Seconds = Math.Min(Math.Max(0, m.Seconds), 20)
        m.Megapixels = Math.Min(Math.Max(0, m.Megapixels), 4)
        Return m
    End Function

    Private Shared Function Number(mm As Match) As Double
        If Not mm.Success Then Return 0
        Dim d As Double
        Return If(Double.TryParse(mm.Groups(1).Value.Replace(","c, "."c), NumberStyles.Float, CultureInfo.InvariantCulture, d), d, 0)
    End Function

    ''' <summary>Width and height: the model's own size, or the asked megapixels with the same shape (multiples of 32).</summary>
    Public Function Dims(defaultW As Integer, defaultH As Integer) As (W As Integer, H As Integer)
        If Lines > 0 AndAlso Megapixels <= 0 AndAlso defaultW > 0 AndAlso defaultH > 0 Then
            ' the short side is the "p" number, the shape stays the model's
            Dim aspectL = defaultW / CDbl(defaultH)
            Return If(aspectL >= 1, (Snap(Lines * aspectL), Snap(Lines)), (Snap(Lines), Snap(Lines / aspectL)))
        End If
        If Megapixels <= 0 OrElse defaultW <= 0 OrElse defaultH <= 0 Then Return (defaultW, defaultH)
        Dim aspect = defaultW / CDbl(defaultH)
        Dim h = Math.Sqrt(Megapixels * 1_000_000 / aspect)
        Dim w = h * aspect
        Return (Snap(w), Snap(h))
    End Function

    Private Shared Function Snap(x As Double) As Integer
        Return Math.Max(256, CInt(Math.Round(x / 32)) * 32)
    End Function

    ''' <summary>Frames for the asked length (4n+1, as video models need), or the model's default.</summary>
    Public Function FramesFor(r As MediaRecipe) As Integer
        Dim def = Math.Max(1, r.Frames)
        If Seconds <= 0 Then Return def
        Dim fps = If(r.Fps > 0, r.Fps, 16)
        Return VideoMaker.FramesFor(Seconds, fps, Math.Max(def, 20 * fps + 1))
    End Function

    Public Function SecondsText(frames As Integer, r As MediaRecipe) As String
        Dim fps = If(r.Fps > 0, r.Fps, 16)
        Return (frames / CDbl(fps)).ToString("0.#", CultureInfo.InvariantCulture) & " s"
    End Function
End Class
