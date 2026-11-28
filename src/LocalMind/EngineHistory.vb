Imports System
Imports System.IO
Imports System.Linq

''' <summary>
''' Keeps the last engine build that worked on this PC, so a new build that fails can be swapped back.
''' Each engine folder (Engine\cuda13, Engine\sd\cuda13, …) gets a works.txt once it has run without errors.
''' An update moves a working folder to "&lt;folder&gt;.prev" instead of deleting it; when the new build then fails while
''' running, <see cref="RollBack"/> puts the previous one back and remembers the failed version, which automatic
''' updates skip from then on.
''' </summary>
Public NotInheritable Class EngineHistory

    Private Const WorksFile As String = "works.txt"

    ''' <summary>Raised after a rollback, with a message for the status bar.</summary>
    Public Shared Event RolledBack(message As String)

    Public Shared Function PrevDir(dir As String) As String
        Return dir.TrimEnd(Path.DirectorySeparatorChar) & ".prev"
    End Function

    Public Shared Function Version(dir As String) As String
        Try
            Dim f = Path.Combine(dir, "version.txt")
            Return If(File.Exists(f), File.ReadAllText(f).Trim(), Nothing)
        Catch
            Return Nothing
        End Try
    End Function

    Public Shared Function Works(dir As String) As Boolean
        Return Not String.IsNullOrEmpty(dir) AndAlso File.Exists(Path.Combine(dir, WorksFile))
    End Function

    ''' <summary>The engine in this folder has just run without errors.</summary>
    Public Shared Sub MarkWorks(dir As String)
        Try
            If String.IsNullOrEmpty(dir) OrElse Not Directory.Exists(dir) Then Return
            If Not Works(dir) Then File.WriteAllText(Path.Combine(dir, WorksFile), DateTime.Now.ToString("s"))
            ' a rollback is confirmed: the failed build goes, and the user is told
            Dim msg As String = Nothing
            SyncLock _pending
                If _pending.TryGetValue(dir, msg) Then _pending.Remove(dir)
            End SyncLock
            If msg IsNot Nothing Then
                Try : Directory.Delete(BadDir(dir), True) : Catch : End Try
                RaiseEvent RolledBack(msg)
            End If
        Catch
        End Try
    End Sub

    ''' <summary>Puts a freshly downloaded build in place: a working build there now is kept as the previous one.</summary>
    Public Shared Sub Replace(target As String, staging As String)
        Dim prev = PrevDir(target)
        If Directory.Exists(target) Then
            If Works(target) Then
                If Directory.Exists(prev) Then Directory.Delete(prev, True)
                Directory.Move(target, prev)
            Else
                ' (never ran without errors: a working previous build, if any, stays the one to go back to)
                Directory.Delete(target, True)
            End If
        End If
        Directory.Move(staging, target)
    End Sub

    Private Shared Function Key(name As String, tag As String) As String
        Return name & "=" & tag
    End Function

    ''' <summary>A version that failed on this PC before (automatic updates don't install it again).</summary>
    Public Shared Function IsBad(name As String, tag As String) As Boolean
        If String.IsNullOrEmpty(tag) Then Return False
        Dim list = AppCore.Settings.BadEngineVersions
        Return list IsNot Nothing AndAlso list.Contains(Key(name, tag))
    End Function

    ''' <summary>The build in this folder never worked, and a previous one that did is kept.</summary>
    Public Shared Function CanRollBack(dir As String) As Boolean
        Return Not String.IsNullOrEmpty(dir) AndAlso Directory.Exists(dir) AndAlso Not Works(dir) AndAlso Works(PrevDir(dir))
    End Function

    Private Shared Function BadDir(dir As String) As String
        Return dir.TrimEnd(Path.DirectorySeparatorChar) & ".bad"
    End Function

    ' (rollbacks not yet confirmed: the message is shown once the previous build has really worked)
    Private Shared ReadOnly _pending As New Collections.Generic.Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)

    ''' <summary>
    ''' Swaps the previous, working build back in (the engine must not be running). It is only kept when that build
    ''' then works (<see cref="MarkWorks"/>); if it fails too, the problem isn't the new build, and
    ''' <see cref="UndoRollBack"/> puts the new one back.
    ''' </summary>
    Public Shared Function RollBack(dir As String, name As String, label As String) As Boolean
        If Not CanRollBack(dir) Then Return False
        Try
            Dim badTag = Version(dir)
            Dim bad = BadDir(dir)
            If Directory.Exists(bad) Then Directory.Delete(bad, True)
            Directory.Move(dir, bad)
            Directory.Move(PrevDir(dir), dir)
            If Not String.IsNullOrEmpty(badTag) Then
                Dim list = AppCore.Settings.BadEngineVersions
                If list Is Nothing Then
                    list = New Collections.Generic.List(Of String)()
                    AppCore.Settings.BadEngineVersions = list
                End If
                If Not list.Contains(Key(name, badTag)) Then list.Add(Key(name, badTag))
                While list.Count > 20   ' (only the last few are kept)
                    list.RemoveAt(0)
                End While
                AppCore.SaveSettings()
            End If
            SyncLock _pending
                _pending(dir) = String.Format(Localizer.T("The new {0} engine ({1}) failed on this PC — went back to the version that worked ({2})."),
                                              label, If(badTag, "?"), If(Version(dir), "?"))
            End SyncLock
            Return True
        Catch
            Return False
        End Try
    End Function

    ''' <summary>The previous build failed as well: the new one goes back in place and is not counted as failed.</summary>
    Public Shared Sub UndoRollBack(dir As String, name As String)
        Try
            Dim bad = BadDir(dir)
            If Not Directory.Exists(bad) Then Return
            Dim prev = PrevDir(dir)
            If Directory.Exists(prev) Then Directory.Delete(prev, True)
            Directory.Move(dir, prev)
            Directory.Move(bad, dir)
            Dim tag = Version(dir)
            If Not String.IsNullOrEmpty(tag) Then
                AppCore.Settings.BadEngineVersions?.Remove(Key(name, tag))
                AppCore.SaveSettings()
            End If
            SyncLock _pending
                _pending.Remove(dir)
            End SyncLock
        Catch
        End Try
    End Sub
End Class
