Imports System
Imports System.IO
Imports System.Text
Imports System.Text.RegularExpressions

Public Module ChatFileIO

    Private ReadOnly RoleMarker As New Regex("<!--\s*(user|assistant)\s*-->", RegexOptions.IgnoreCase)
    Private ReadOnly ModelMarker As New Regex("<!--\s*model:\s*(.*?)\s*-->", RegexOptions.IgnoreCase)
    Private ReadOnly DateMarker As New Regex("<!--\s*date:\s*(.*?)\s*-->", RegexOptions.IgnoreCase)
    Private ReadOnly TitleLine As New Regex("^\s*#\s+(.+?)\s*$", RegexOptions.Multiline)
    Private ReadOnly ThinkingBlock As New Regex("<!--\s*thinking\s*-->([\s\S]*?)<!--\s*/thinking\s*-->", RegexOptions.IgnoreCase)

    Public Sub ExportToFile(session As ChatSession, filePath As String)
        Dim sb As New StringBuilder()

        sb.AppendLine("# " & If(String.IsNullOrWhiteSpace(session.Title), "Untitled chat", session.Title))
        sb.AppendLine()
        sb.AppendLine("<!-- model: " & If(session.Model, "") & " -->")
        sb.AppendLine("<!-- date: " & session.CreatedAt.ToString("yyyy-MM-dd HH:mm") & " -->")
        sb.AppendLine()

        For Each m In session.Messages
            Dim role = If(m.Role = "user", "user", "assistant")
            sb.AppendLine("<!-- " & role & " -->")

            If m.Role = "assistant" AndAlso Not String.IsNullOrEmpty(m.Thinking) Then
                sb.AppendLine("<!-- thinking -->")
                sb.AppendLine(m.Thinking)
                sb.AppendLine("<!-- /thinking -->")
                sb.AppendLine()
            End If

            sb.AppendLine(If(m.Content, ""))
            sb.AppendLine()
        Next

        File.WriteAllText(filePath, sb.ToString(), New UTF8Encoding(False))
    End Sub

    Public Function ImportFromFile(filePath As String) As ChatSession
        Dim text = File.ReadAllText(filePath)
        Dim session As New ChatSession()

        Dim tm = TitleLine.Match(text)
        If tm.Success Then
            session.Title = tm.Groups(1).Value.Trim()
        Else
            session.Title = Path.GetFileNameWithoutExtension(filePath)
        End If

        Dim mm = ModelMarker.Match(text)
        If mm.Success Then session.Model = mm.Groups(1).Value.Trim()

        Dim dm = DateMarker.Match(text)
        If dm.Success Then
            Dim parsed As DateTime
            If DateTime.TryParse(dm.Groups(1).Value.Trim(), parsed) Then
                session.CreatedAt = parsed
            End If
        End If

        Dim matches = RoleMarker.Matches(text)
        For i = 0 To matches.Count - 1
            Dim m = matches(i)
            Dim startPos = m.Index + m.Length
            Dim endPos = If(i + 1 < matches.Count, matches(i + 1).Index, text.Length)
            Dim raw = text.Substring(startPos, endPos - startPos)

            If raw.StartsWith(vbCrLf) Then
                raw = raw.Substring(2)
            ElseIf raw.StartsWith(vbLf) Then
                raw = raw.Substring(1)
            End If
            raw = raw.TrimEnd(vbCr, vbLf)

            Dim role = m.Groups(1).Value.ToLowerInvariant()
            Dim thinking As String = ""

            If role = "assistant" Then
                Dim thinkMatch = ThinkingBlock.Match(raw)
                If thinkMatch.Success Then
                    thinking = thinkMatch.Groups(1).Value.Trim()
                    raw = (raw.Substring(0, thinkMatch.Index) &
                           raw.Substring(thinkMatch.Index + thinkMatch.Length)).Trim()
                End If
            End If

            session.Messages.Add(New ChatMessage With {
                .Role = role,
                .Content = raw,
                .Thinking = thinking
            })
        Next

        If session.Messages.Count = 0 Then
            session.Messages.Add(New ChatMessage With {
                .Role = "user",
                .Content = text.Trim()
            })
        End If

        Return session
    End Function
End Module