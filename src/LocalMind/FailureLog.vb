Imports System
Imports System.IO
Imports System.Linq
Imports System.Text

''' <summary>
''' Data\Logs\failures.log: every request that failed, with what was asked, the step, the error and the end of the
''' engine's own log. Help → Copy diagnostics includes the last entries.
''' </summary>
Public NotInheritable Class FailureLog

    Private Shared ReadOnly _lock As New Object()
    Private Const MaxBytes As Long = 1024 * 1024

    Public Shared ReadOnly Property Folder As String
        Get
            Return Path.Combine(AppPaths.Roaming, "Logs")
        End Get
    End Property

    Public Shared ReadOnly Property FilePath As String
        Get
            Return Path.Combine(Folder, "failures.log")
        End Get
    End Property

    ''' <summary>Adds an entry. Never throws.</summary>
    Public Shared Sub Write(request As String, stepName As String, ex As Exception)
        Try
            Dim sb As New StringBuilder()
            sb.AppendLine("════════ " & DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") & " · LocalMind " & Updater.CurrentVersion.ToString(3))
            sb.AppendLine("Request: " & Shorten(If(request, ""), 600))
            If Not String.IsNullOrEmpty(stepName) Then sb.AppendLine("Step:    " & stepName)
            sb.AppendLine("Error:   " & If(ex?.Message, "(none)").Trim())
            Dim engineLog = TryCast(ex?.Data("engineLog"), String)
            If Not String.IsNullOrEmpty(engineLog) Then
                Dim lines = engineLog.Split({vbCrLf, vbLf}, StringSplitOptions.None)
                ' the first line is the command line; then the end of the engine's output
                sb.AppendLine("Command: " & Shorten(lines(0), 1500))
                sb.AppendLine("Engine output (end):")
                For Each l In lines.Skip(1).Where(Function(x) x.Trim() <> "").Reverse().Take(40).Reverse()
                    sb.AppendLine("  " & Shorten(l, 400))
                Next
            ElseIf ex IsNot Nothing AndAlso Not String.IsNullOrEmpty(ex.StackTrace) Then
                sb.AppendLine("Where:   " & Shorten(ex.GetType().Name & " " & ex.StackTrace.Split({vbCrLf, vbLf}, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim(), 300))
            End If
            sb.AppendLine()
            SyncLock _lock
                Directory.CreateDirectory(Folder)
                ' (kept small: the oldest half goes when it grows past 1 MB)
                Dim fi As New FileInfo(FilePath)
                If fi.Exists AndAlso fi.Length > MaxBytes Then
                    Dim all = File.ReadAllText(FilePath)
                    File.WriteAllText(FilePath, all.Substring(all.Length \ 2))
                End If
                File.AppendAllText(FilePath, sb.ToString(), Encoding.UTF8)
            End SyncLock
        Catch
        End Try
    End Sub

    ''' <summary>The last few entries (for diagnostics).</summary>
    Public Shared Function LastEntries(count As Integer) As String
        Try
            If Not File.Exists(FilePath) Then Return ""
            Dim parts = File.ReadAllText(FilePath).Split({"════════ "}, StringSplitOptions.RemoveEmptyEntries)
            Return String.Concat(parts.Skip(Math.Max(0, parts.Length - count)).Select(Function(p) "════════ " & p))
        Catch
            Return ""
        End Try
    End Function

    Private Shared Function Shorten(s As String, max As Integer) As String
        s = If(s, "")
        Return If(s.Length > max, s.Substring(0, max) & "…", s)
    End Function
End Class
