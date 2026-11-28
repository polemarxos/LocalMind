Imports System
Imports System.Collections.Generic
Imports System.Linq
Imports System.Security
Imports System.Text
Imports System.Text.RegularExpressions

''' <summary>
''' Text made ready for a voice: no Markdown, code or links, and no emoji read out. Emoji set the mood of their
''' sentence instead (😄 brighter and a little faster, 😢 slower and lower, 😠 firmer, 😮 higher, 🤔 calmer),
''' which Windows voices follow through SAPI's speed, pitch and volume tags. AI voices get the plain text.
''' </summary>
Public Module SpeechText

    Public Class Part
        Public Property Text As String
        ''' <summary>"", "happy", "sad", "angry", "surprised" or "calm".</summary>
        Public Property Mood As String = ""
    End Class

    ' (emoji: pictographs outside the basic plane, the symbol blocks, and their joiners / variation marks / skin tones)
    Private ReadOnly EmojiRx As New Regex("(?:[\uD83C-\uDBFF][\uDC00-\uDFFF]|[\u203C\u2049\u2190-\u21FF\u2300-\u23FF\u2460-\u27BF\u2900-\u297F\u2B00-\u2BFF\u3030\u303D\u3297\u3299]|[\uFE0E\uFE0F\u200D\u20E3])+",
                                          RegexOptions.Compiled)

    Private ReadOnly Moods As (Mood As String, Emoji As String())() = {
        ("happy", {"😀", "😃", "😄", "😁", "😆", "😊", "🙂", "😍", "🥰", "😎", "🤩", "🎉", "🥳", "👍", "💪", "🔥", "✨", "❤", "♥", "😂", "🤣", "😅", "😉", "😺", "🙌", "👏"}),
        ("sad", {"😢", "😭", "😞", "😔", "😟", "☹", "🙁", "💔", "🥺", "😿", "😥", "😓"}),
        ("angry", {"😠", "😡", "🤬", "👿", "💢", "😤"}),
        ("surprised", {"😮", "😲", "😯", "🤯", "😱", "‼", "❗", "😳"}),
        ("calm", {"🤔", "🧘", "😌", "💭", "😴", "🙏"})}

    ''' <summary>The mood its emoji give a piece of text ("" when none).</summary>
    Public Function MoodOf(text As String) As String
        If String.IsNullOrEmpty(text) Then Return ""
        For Each m In Moods
            If m.Emoji.Any(Function(e) text.Contains(e)) Then Return m.Mood
        Next
        Return ""
    End Function

    Public Function StripEmoji(text As String) As String
        Dim t = Regex.Replace(EmojiRx.Replace(If(text, ""), " "), "[ \t]{2,}", " ")
        Return Regex.Replace(t, " +([.,!?;:…·])", "$1").Trim()
    End Function

    ''' <summary>Markdown, code, links and citation marks out (emoji stay, for the moods).</summary>
    Private Function CleanMarkdown(text As String) As String
        Dim clean = Regex.Replace(If(text, ""), "```[\s\S]*?```", " (code) ")
        clean = Regex.Replace(clean, "!\[[^\]]*\]\([^)]*\)", " ")
        clean = Regex.Replace(clean, "\[([^\]]+)\]\([^)]*\)", "$1")
        clean = Regex.Replace(clean, "https?://\S+", " ")
        clean = Regex.Replace(clean, "[#*_`>|~]+", " ")
        clean = Regex.Replace(clean, "\[(\w?\d+)\]", "")
        Return Regex.Replace(clean, "\s+", " ").Trim()
    End Function

    ''' <summary>The text as sentences with their moods. An emoji right after a sentence ("Done! 🎉 Next…") belongs to it.</summary>
    Public Function Prepare(text As String) As List(Of Part)
        Dim result As New List(Of Part)()
        Dim sentences = Regex.Split(CleanMarkdown(text), "(?<=[.!?;·…])\s+")
        For Each s In sentences
            Dim lead = Regex.Match(s, "^\s*(?:" & EmojiRx.ToString() & "|\s)+")
            If lead.Success AndAlso lead.Length > 0 AndAlso result.Count > 0 Then
                ' (the emoji opening this piece closed the sentence before it)
                Dim m = MoodOf(lead.Value)
                If m <> "" Then result(result.Count - 1).Mood = m
                s = s.Substring(lead.Length)
            End If
            Dim mood = MoodOf(s)
            Dim plain = StripEmoji(s)
            If Regex.IsMatch(plain, "[\p{L}\p{N}]") Then
                result.Add(New Part With {.Text = plain, .Mood = mood})
            ElseIf mood <> "" AndAlso result.Count > 0 AndAlso result(result.Count - 1).Mood = "" Then
                result(result.Count - 1).Mood = mood
            End If
        Next
        Return result
    End Function

    ''' <summary>The whole text, plain, for AI voices (no emoji, no Markdown).</summary>
    Public Function Plain(text As String) As String
        Return String.Join(" ", Prepare(text).Select(Function(p) p.Text))
    End Function

    ''' <summary>One sentence as SAPI XML, with the speed, pitch and volume of its mood.</summary>
    Public Function ToSapiXml(p As Part) As String
        Dim t = SecurityElement.Escape(p.Text)
        Select Case p.Mood
            Case "happy" : Return $"<rate speed=""1""><pitch middle=""3"">{t}</pitch></rate>"
            Case "sad" : Return $"<volume level=""85""><rate speed=""-2""><pitch middle=""-4"">{t}</pitch></rate></volume>"
            Case "angry" : Return $"<volume level=""100""><rate speed=""1""><pitch middle=""-2""><emph>{t}</emph></pitch></rate></volume>"
            Case "surprised" : Return $"<pitch middle=""5"">{t}</pitch>"
            Case "calm" : Return $"<rate speed=""-1""><pitch middle=""-1"">{t}</pitch></rate>"
            Case Else : Return t
        End Select
    End Function
End Module
