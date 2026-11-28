Imports System
Imports System.Text.RegularExpressions
Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.IO
Imports System.IO.Compression
Imports System.Linq
Imports System.Net.Http
Imports System.Reflection
Imports System.Threading
Imports System.Threading.Tasks
Imports Newtonsoft.Json.Linq

''' <summary>
''' Checks GitHub Releases for a newer LocalMind.exe and installs it in place:
''' the running exe is renamed to LocalMind.old.exe (Windows allows renaming a running exe),
''' the new one takes its name, the app restarts, and the old file is deleted on the next start.
''' </summary>
Public Module Updater

    Public Const Repo As String = "polemarxos/LocalMind"
    Public ReadOnly ReleasesPage As String = $"https://github.com/{Repo}/releases/latest"

    Public Class UpdateInfo
        Public Property Version As Version
        Public Property Tag As String
        Public Property Notes As String
        Public Property ZipUrl As String
        Public Property PageUrl As String
        Public Property SizeBytes As Long
    End Class

    Public ReadOnly Property CurrentVersion As Version
        Get
            Dim v = Assembly.GetExecutingAssembly().GetName().Version
            Return New Version(v.Major, v.Minor, Math.Max(0, v.Build))
        End Get
    End Property

    ''' <summary>Returns update info when a newer release exists, otherwise Nothing.</summary>
    Public Async Function CheckAsync(ct As CancellationToken) As Task(Of UpdateInfo)
        Using req As New HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repo}/releases/latest")
            req.Headers.UserAgent.ParseAdd("LocalMind/" & CurrentVersion.ToString())
            req.Headers.Accept.ParseAdd("application/vnd.github+json")
            Using cts = CancellationTokenSource.CreateLinkedTokenSource(ct)
                cts.CancelAfter(TimeSpan.FromSeconds(20))
                Using resp = Await Downloader.Http.SendAsync(req, cts.Token)
                    If Not resp.IsSuccessStatusCode Then Return Nothing
                    Dim o = JObject.Parse(Await resp.Content.ReadAsStringAsync(cts.Token))
                    Dim tag = o.Value(Of String)("tag_name")
                    Dim v As Version = Nothing
                    If tag Is Nothing OrElse Not Version.TryParse(tag.TrimStart("v"c, "V"c), v) Then Return Nothing
                    v = New Version(v.Major, v.Minor, Math.Max(0, v.Build))
                    If v <= CurrentVersion Then Return Nothing
                    Dim asset = CType(o("assets"), JArray).FirstOrDefault(
                        Function(a) a.Value(Of String)("name").EndsWith(".zip", StringComparison.OrdinalIgnoreCase) OrElse
                                    a.Value(Of String)("name").EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    Return New UpdateInfo With {
                        .Version = v, .Tag = tag,
                        .Notes = o.Value(Of String)("body"),
                        .ZipUrl = asset?.Value(Of String)("browser_download_url"),
                        .SizeBytes = If(asset?.Value(Of Long)("size"), 0L),
                        .PageUrl = o.Value(Of String)("html_url")
                    }
                End Using
            End Using
        End Using
    End Function

    '──────────────── what's new (the update window) ────────────────
    Private ReadOnly KindWords As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From {
        {"Added", "New"}, {"Changed", "Changed"}, {"Fixed", "Fixed"}, {"Removed", "Removed"}}

    ''' <summary>
    ''' The changes since <paramref name="since"/>, one short plain line each ("New: Buttons under every picture"), newest
    ''' first: the bold title of each change in CHANGELOG.md.
    ''' </summary>
    Public Function WhatsNew(changelog As String, since As Version) As List(Of String)
        Dim result As New List(Of String)()
        If String.IsNullOrEmpty(changelog) Then Return result
        Dim take = False
        Dim kind = "Changed"
        For Each raw In changelog.Split({vbCrLf, vbLf}, StringSplitOptions.None)
            Dim h = Regex.Match(raw, "^##\s+v?(\d+\.\d+(\.\d+)?)\b")
            If h.Success Then
                Dim v As Version = Nothing
                take = Version.TryParse(h.Groups(1).Value, v) AndAlso New Version(v.Major, v.Minor, Math.Max(0, v.Build)) > since
                kind = "Changed"
                Continue For
            End If
            If Not take Then Continue For
            Dim k = Regex.Match(raw, "^###\s+(\w+)")
            If k.Success Then
                kind = If(KindWords.ContainsKey(k.Groups(1).Value), KindWords(k.Groups(1).Value), k.Groups(1).Value)
                Continue For
            End If
            If raw.StartsWith("- ") Then result.Add(Localizer.T(kind) & ": " & ChangeTitle(raw.Substring(2)))
        Next
        Return result
    End Function

    Private Function ChangeTitle(item As String) As String
        Dim b = Regex.Match(item, "^\*\*(.+?)\*\*")
        Dim t = If(b.Success, b.Groups(1).Value, item)
        t = Regex.Replace(t, "\[([^\]]*)\]\([^)]*\)", "$1").Replace("**", "").Replace("`", "")
        t = Regex.Replace(t, "\s+", " ").Trim()
        If Not b.Success Then t = Regex.Split(t, "(?<=[.!?])\s")(0)
        t = t.TrimEnd("."c).Trim()
        Return If(t.Length <= 110, t, t.Substring(0, 107).TrimEnd() & "…")
    End Function

    ''' <summary>The release notes as plain lines (for when CHANGELOG.md can't be read): the "- " lines without links.</summary>
    Public Function PlainNotes(notes As String) As List(Of String)
        Dim result As New List(Of String)()
        For Each raw In If(notes, "").Split({vbCrLf, vbLf}, StringSplitOptions.None)
            Dim t = raw.Trim()
            If Not t.StartsWith("- ") Then Continue For
            t = Regex.Replace(t.Substring(2), "\[([^\]]*)\]\([^)]*\)", "$1").Replace("**", "").Replace("`", "").Trim()
            If t <> "" Then result.Add(t)
        Next
        Return result
    End Function

    ''' <summary>What changed since this version, read from CHANGELOG.md on GitHub (all the versions skipped too).</summary>
    Public Async Function WhatsNewAsync(info As UpdateInfo, ct As CancellationToken) As Task(Of List(Of String))
        Try
            Using cts = CancellationTokenSource.CreateLinkedTokenSource(ct)
                cts.CancelAfter(TimeSpan.FromSeconds(10))
                Dim log = Await Downloader.Http.GetStringAsync($"https://raw.githubusercontent.com/{Repo}/main/CHANGELOG.md", cts.Token)
                Dim lines = WhatsNew(log, CurrentVersion)
                If lines.Count > 0 Then Return lines
            End Using
        Catch
        End Try
        Return PlainNotes(info?.Notes)
    End Function

    ''' <summary>Downloads the update and swaps the exe. Returns the path of the new exe to start.</summary>
    Public Async Function InstallAsync(info As UpdateInfo, progress As Action(Of Integer), ct As CancellationToken) As Task(Of String)
        If info.ZipUrl Is Nothing Then Throw New InvalidOperationException("The release has no download attached.")
        Dim exePath = Environment.ProcessPath
        Dim dir = Path.GetDirectoryName(exePath)
        Dim tmp = Path.Combine(Path.GetTempPath(), "LocalMind-update-" & info.Tag)
        If Directory.Exists(tmp) Then Directory.Delete(tmp, True)
        Directory.CreateDirectory(tmp)

        Dim download = Path.Combine(tmp, Path.GetFileName(New Uri(info.ZipUrl).LocalPath))
        Await Downloader.DownloadFileAsync(info.ZipUrl, download, Nothing,
            Sub(done, total, speed) progress?.Invoke(If(total > 0, CInt(done * 100 \ total), 0)), ct, info.SizeBytes)

        Dim newExe As String
        If download.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) Then
            ZipFile.ExtractToDirectory(download, tmp, True)
            newExe = Directory.GetFiles(tmp, "*.exe", SearchOption.AllDirectories).FirstOrDefault()
        Else
            newExe = download
        End If
        If newExe Is Nothing Then Throw New InvalidOperationException("The update package does not contain an exe.")

        ' Swap: rename the running exe out of the way, put the new one in its place.
        Dim oldExe = Path.Combine(dir, Path.GetFileNameWithoutExtension(exePath) & ".old.exe")
        If File.Exists(oldExe) Then File.Delete(oldExe)
        File.Move(exePath, oldExe)
        Try
            File.Copy(newExe, exePath, True)
        Catch
            File.Move(oldExe, exePath)   ' roll back
            Throw
        End Try
        Try
            Directory.Delete(tmp, True)
        Catch
        End Try
        Return exePath
    End Function

    ''' <summary>Deletes the previous version left behind by an update.</summary>
    Public Sub CleanupOldVersion()
        Try
            Dim exePath = Environment.ProcessPath
            Dim oldExe = Path.Combine(Path.GetDirectoryName(exePath), Path.GetFileNameWithoutExtension(exePath) & ".old.exe")
            If File.Exists(oldExe) Then File.Delete(oldExe)
        Catch
        End Try
    End Sub

    Public Sub Restart(exePath As String)
        Process.Start(New ProcessStartInfo(exePath) With {.UseShellExecute = True, .WorkingDirectory = Path.GetDirectoryName(exePath)})
    End Sub
End Module
