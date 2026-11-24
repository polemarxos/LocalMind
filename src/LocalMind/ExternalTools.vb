Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.IO.Compression
Imports System.Linq
Imports System.Net.Http
Imports System.Text.RegularExpressions
Imports System.Threading
Imports System.Threading.Tasks
Imports Newtonsoft.Json.Linq

''' <summary>
''' Installs helper programs (whisper.cpp, stable-diffusion.cpp) from their official GitHub
''' releases into &lt;exe folder&gt;\Engine\&lt;name&gt;\&lt;variant&gt;.
''' </summary>
Public Module ExternalTools

    Public Class ToolPackage
        Public Property Name As String                 ' e.g. "whisper"
        Public Property Flavor As String              ' e.g. "cuda12"
        Public Property Repo As String                 ' owner/name on GitHub
        Public Property AssetPatterns As String()      ' all must be found in one release (main zip first)
        Public Property ExeName As String              ' e.g. "whisper-cli.exe"
    End Class

    Public Function ToolDir(p As ToolPackage) As String
        Return Path.Combine(AppPaths.EngineFolder, p.Name, p.Flavor)
    End Function

    Public Function FindExe(p As ToolPackage) As String
        Dim d = ToolDir(p)
        If Not Directory.Exists(d) Then Return Nothing
        Dim exe = If(OperatingSystem.IsWindows(), p.ExeName, Path.GetFileNameWithoutExtension(p.ExeName))
        Return Directory.EnumerateFiles(d, exe, SearchOption.AllDirectories).FirstOrDefault()
    End Function

    Public Function InstalledTag(p As ToolPackage) As String
        Dim f = Path.Combine(ToolDir(p), "version.txt")
        Return If(File.Exists(f), File.ReadAllText(f).Trim(), Nothing)
    End Function

    ''' <summary>Downloads the newest release that has every asset the package needs.</summary>
    Public Async Function InstallAsync(p As ToolPackage, progress As Action(Of String), ct As CancellationToken,
                                       Optional onlyIfNewer As Boolean = False) As Task(Of String)
        progress?.Invoke($"Looking for {p.Name} ({p.Flavor}) on GitHub…")
        Dim rx = p.AssetPatterns.Select(Function(x) New Regex(x, RegexOptions.IgnoreCase)).ToArray()
        Dim urls As List(Of (Url As String, Size As Long)) = Nothing
        Dim tag As String = Nothing

        Using req As New HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{p.Repo}/releases?per_page=15")
            req.Headers.UserAgent.ParseAdd("LocalMind/" & Updater.CurrentVersion.ToString())
            req.Headers.Accept.ParseAdd("application/vnd.github+json")
            Using resp = Await Downloader.Http.SendAsync(req, ct)
                resp.EnsureSuccessStatusCode()
                For Each rel In JArray.Parse(Await resp.Content.ReadAsStringAsync(ct))
                    If rel.Value(Of Boolean)("draft") Then Continue For
                    Dim assets = CType(rel("assets"), JArray)
                    Dim found As New List(Of (Url As String, Size As Long))
                    For Each r In rx
                        Dim a = assets.FirstOrDefault(Function(x) r.IsMatch(x.Value(Of String)("name")))
                        If a Is Nothing Then Exit For
                        found.Add((a.Value(Of String)("browser_download_url"), a.Value(Of Long)("size")))
                    Next
                    If found.Count = rx.Length Then
                        urls = found
                        tag = rel.Value(Of String)("tag_name")
                        Exit For
                    End If
                Next
            End Using
        End Using
        If urls Is Nothing Then Throw New Exception($"No Windows build of {p.Name} ({p.Flavor}) found in the latest releases of {p.Repo}.")
        If onlyIfNewer AndAlso FindExe(p) IsNot Nothing AndAlso tag = InstalledTag(p) Then Return tag

        Dim target = ToolDir(p)
        Dim staging = target & ".new"
        If Directory.Exists(staging) Then Directory.Delete(staging, True)
        Directory.CreateDirectory(staging)
        Dim total = urls.Sum(Function(u) u.Size)
        Dim before As Long = 0
        For Each u In urls
            Dim zip = Path.Combine(staging, Path.GetFileName(New Uri(u.Url).LocalPath))
            Dim b = before
            Await Downloader.DownloadFileAsync(u.Url, zip, Nothing,
                Sub(done, size, speed) progress?.Invoke($"Downloading {p.Name}… {If(total > 0, (b + done) * 100 \ total, 0)}%"), ct, u.Size)
            ZipFile.ExtractToDirectory(zip, staging, True)
            File.Delete(zip)
            before += u.Size
        Next
        File.WriteAllText(Path.Combine(staging, "version.txt"), tag)
        If Directory.Exists(target) Then Directory.Delete(target, True)
        Directory.Move(staging, target)
        ' the builds for another GPU backend (after switching CUDA ↔ Vulkan ↔ CPU) are no longer used
        Try
            For Each other In Directory.GetDirectories(Path.GetDirectoryName(target))
                If Not String.Equals(other.TrimEnd(Path.DirectorySeparatorChar), target.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) Then
                    Try : Directory.Delete(other, True) : Catch : End Try
                End If
            Next
        Catch
        End Try
        progress?.Invoke($"{p.Name} installed ({tag})")
        Return tag
    End Function

    ''' <summary>Copies CUDA runtime DLLs from the llama.cpp CUDA 12 build when a tool lacks them.</summary>
    Public Sub ShareCudaRuntime(toolDir As String)
        Try
            Dim llama = Path.Combine(AppPaths.EngineFolder, "cuda12")
            If Not Directory.Exists(llama) Then Return
            Dim exeDir = Path.GetDirectoryName(Directory.EnumerateFiles(toolDir, "*.exe", SearchOption.AllDirectories).FirstOrDefault())
            If exeDir Is Nothing Then Return
            For Each dll In Directory.GetFiles(llama, "*.dll").Where(Function(f) Regex.IsMatch(Path.GetFileName(f), "^(cudart|cublas|cublasLt)64_\d+\.dll$", RegexOptions.IgnoreCase))
                Dim dest = Path.Combine(exeDir, Path.GetFileName(dll))
                If Not File.Exists(dest) Then File.Copy(dll, dest)
            Next
        Catch
        End Try
    End Sub
End Module
