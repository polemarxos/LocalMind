Imports System
Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.IO
Imports System.IO.Compression
Imports System.Linq
Imports Newtonsoft.Json
Imports Newtonsoft.Json.Linq

''' <summary>
''' Full backup of LocalMind's own data in one .lmbackup (zip) file: every setting (API keys, PINs and tokens
''' included), profiles, personas, characters and their pictures, saved voices, LoRA settings, prompts, chats and
''' documents. Models are not copied (too big); the backup lists them, and after a restore LocalMind offers to download
''' them. A restore is done at the next start, before anything is loaded, so nothing overwrites it on the way.
''' </summary>
Public Module BackupService
    Public Const Extension As String = ".lmbackup"
    Private Const ManifestName As String = "_backup.json"
    Private Const SetupName As String = "_setup/setup.json"
    Private Const PendingName As String = "restore-pending" & Extension
    Public Const PendingSetupName As String = "pending-setup.json"

    ' caches and logs are left out (they are made again)
    Private ReadOnly SkipFolders As String() = {"LibraryThumbs", "EBWebView", "WebView", "WebView2", "webview", "Logs"}
    Private ReadOnly SkipFiles As String() = {"errors.log", "engine-last.log", "catalog-cache.json", PendingName, PendingSetupName}

    Private Function Included(relative As String) As Boolean
        Dim parts = relative.Replace("\"c, "/"c).Split("/"c)
        If parts.Take(parts.Length - 1).Any(Function(p) SkipFolders.Contains(p, StringComparer.OrdinalIgnoreCase) OrElse p.EndsWith("-before-restore", StringComparison.OrdinalIgnoreCase)) Then Return False
        Dim name = parts.Last()
        If SkipFiles.Contains(name, StringComparer.OrdinalIgnoreCase) Then Return False
        If name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) OrElse name.EndsWith(".lock", StringComparison.OrdinalIgnoreCase) Then Return False
        Return True
    End Function

    ''' <summary>Writes the backup. Returns how many files went in.</summary>
    Public Function Export(zipPath As String, setup As SetupFile) As Integer
        AppCore.SaveSettings()
        Dim root = AppPaths.Roaming
        Dim count = 0
        Dim tmp = zipPath & ".tmp"
        If File.Exists(tmp) Then File.Delete(tmp)
        Using z = ZipFile.Open(tmp, ZipArchiveMode.Create)
            For Each f In Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                Dim rel = Path.GetRelativePath(root, f)
                If Not Included(rel) Then Continue For
                Try
                    ' (read with sharing, so files in use can still be copied)
                    Using src As New FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite Or FileShare.Delete)
                        Using dst = z.CreateEntry(rel.Replace("\"c, "/"c), CompressionLevel.Optimal).Open()
                            src.CopyTo(dst)
                        End Using
                    End Using
                    count += 1
                Catch ex As IOException
                End Try
            Next
            If setup IsNot Nothing Then
                Using w As New StreamWriter(z.CreateEntry(SetupName).Open())
                    w.Write(SetupLibrary.ToJson(setup))
                End Using
            End If
            Using w As New StreamWriter(z.CreateEntry(ManifestName).Open())
                w.Write(New JObject From {{"app", "LocalMind"}, {"version", Updater.CurrentVersion.ToString()},
                                          {"created", DateTime.Now.ToString("o")}, {"files", count}}.ToString())
            End Using
        End Using
        File.Move(tmp, zipPath, True)
        Return count
    End Function

    ''' <summary>Checks that the file is a LocalMind backup; returns its manifest (version, date) or throws.</summary>
    Public Function Inspect(zipPath As String) As JObject
        Using z = ZipFile.OpenRead(zipPath)
            Dim e = z.GetEntry(ManifestName)
            If e Is Nothing Then Throw New InvalidDataException(Localizer.T("This is not a LocalMind backup."))
            Using r As New StreamReader(e.Open())
                Dim m = JObject.Parse(r.ReadToEnd())
                If m.Value(Of String)("app") <> "LocalMind" Then Throw New InvalidDataException(Localizer.T("This is not a LocalMind backup."))
                Return m
            End Using
        End Using
    End Function

    ''' <summary>Copies the backup next to the data; it is restored when LocalMind starts again.</summary>
    Public Sub ScheduleRestore(zipPath As String)
        Inspect(zipPath)
        File.Copy(zipPath, Path.Combine(AppPaths.Roaming, PendingName), True)
    End Sub

    ''' <summary>
    ''' Called first thing at start: restores a scheduled backup. The current data is kept in a "-before-restore"
    ''' folder next to it. Returns True when something was restored.
    ''' </summary>
    Public Function ApplyPendingRestore() As Boolean
        Dim root = AppPaths.Roaming
        Dim pending = Path.Combine(root, PendingName)
        If Not File.Exists(pending) Then Return False
        WaitForOtherCopies()
        Try
            Dim keep = root.TrimEnd(Path.DirectorySeparatorChar) & "-before-restore"
            If Directory.Exists(keep) Then Directory.Delete(keep, True)
            Directory.CreateDirectory(keep)
            For Each f In Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                Dim rel = Path.GetRelativePath(root, f)
                If String.Equals(rel, PendingName, StringComparison.OrdinalIgnoreCase) Then Continue For
                Dim dest = Path.Combine(keep, rel)
                Directory.CreateDirectory(Path.GetDirectoryName(dest))
                Try : File.Copy(f, dest, True) : Catch : End Try
            Next
            Dim fullRoot = Path.GetFullPath(root)
            Using z = ZipFile.OpenRead(pending)
                For Each e In z.Entries
                    If e.FullName = ManifestName OrElse String.IsNullOrEmpty(e.Name) Then Continue For
                    If e.FullName = SetupName Then
                        e.ExtractToFile(Path.Combine(root, PendingSetupName), True)
                        Continue For
                    End If
                    Dim dest = Path.GetFullPath(Path.Combine(root, e.FullName))
                    ' (never outside the data folder)
                    If Not dest.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase) Then Continue For
                    Directory.CreateDirectory(Path.GetDirectoryName(dest))
                    e.ExtractToFile(dest, True)
                Next
            End Using
            Return True
        Finally
            Try : File.Delete(pending) : Catch : End Try
        End Try
    End Function

    ''' <summary>The closing copy saves its settings and chats on the way out; wait for it so it can't overwrite the restore.</summary>
    Private Sub WaitForOtherCopies()
        Try
            Dim me_ = Process.GetCurrentProcess()
            For Each p In Process.GetProcessesByName(me_.ProcessName)
                If p.Id = me_.Id Then Continue For
                Try : p.WaitForExit(20000) : Catch : End Try
            Next
        Catch
        End Try
    End Sub

    ''' <summary>The model list that came with a restored backup (once), to offer the downloads.</summary>
    Public Function TakePendingSetup() As SetupFile
        Dim f = Path.Combine(AppPaths.Roaming, PendingSetupName)
        If Not File.Exists(f) Then Return Nothing
        Try
            Return SetupLibrary.Parse(File.ReadAllText(f))
        Catch
            Return Nothing
        Finally
            Try : File.Delete(f) : Catch : End Try
        End Try
    End Function
End Module
