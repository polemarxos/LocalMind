Imports System
Imports System.Diagnostics
Imports System.IO
Imports System.Net
Imports System.Net.Http
Imports System.Net.Http.Headers
Imports System.Security.Cryptography
Imports System.Threading
Imports System.Threading.Tasks

''' <summary>Resumable HTTP file downloads (used for models and the engine).</summary>
Public Module Downloader

    Public ReadOnly Http As HttpClient = CreateClient()

    Private Function CreateClient() As HttpClient
        Dim handler As New SocketsHttpHandler() With {
            .AutomaticDecompression = DecompressionMethods.None,
            .AllowAutoRedirect = True,
            .MaxAutomaticRedirections = 10,
            .ConnectTimeout = TimeSpan.FromSeconds(30)
        }
        Dim c As New HttpClient(handler) With {.Timeout = Timeout.InfiniteTimeSpan}
        c.DefaultRequestHeaders.UserAgent.ParseAdd("LocalMind/2.0 (+https://github.com/polemarxos/LocalMind)")
        Return c
    End Function

    ''' <summary>
    ''' Downloads url → destPath, resuming from destPath.part if it exists.
    ''' progress(bytesDone, bytesTotal, bytesPerSecond) is called at most ~5×/s.
    ''' </summary>
    Public Async Function DownloadFileAsync(url As String, destPath As String, bearerToken As String,
                                           progress As Action(Of Long, Long, Double),
                                           ct As CancellationToken,
                                           Optional expectedSize As Long = 0) As Task
        Directory.CreateDirectory(Path.GetDirectoryName(destPath))
        Dim part = destPath & ".part"

        If File.Exists(destPath) AndAlso (expectedSize <= 0 OrElse New FileInfo(destPath).Length = expectedSize) Then
            progress?.Invoke(New FileInfo(destPath).Length, New FileInfo(destPath).Length, 0)
            Return
        End If

        Dim attempt = 0
        Do
            attempt += 1
            Try
                Await DownloadOnceAsync(url, part, bearerToken, progress, ct, expectedSize)
                Exit Do
            Catch ex As OperationCanceledException
                Throw
            Catch ex As HttpRequestException When attempt < 6 AndAlso IsTransient(ex)
            Catch ex As IOException When attempt < 6
            End Try
            ' Transient network error: back off and resume from where we stopped.
            Await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, 2 ^ attempt)), ct)
        Loop

        If File.Exists(destPath) Then File.Delete(destPath)
        File.Move(part, destPath)
    End Function

    Private Function IsTransient(ex As HttpRequestException) As Boolean
        If ex.StatusCode Is Nothing Then Return True
        Dim c = CInt(ex.StatusCode.Value)
        Return c = 408 OrElse c = 429 OrElse c >= 500
    End Function

    Private Async Function DownloadOnceAsync(url As String, part As String, bearerToken As String,
                                            progress As Action(Of Long, Long, Double),
                                            ct As CancellationToken, expectedSize As Long) As Task
        Dim existing As Long = If(File.Exists(part), New FileInfo(part).Length, 0)
        If expectedSize > 0 AndAlso existing = expectedSize Then Return
        If expectedSize > 0 AndAlso existing > expectedSize Then
            File.Delete(part)
            existing = 0
        End If

        Using req As New HttpRequestMessage(HttpMethod.Get, url)
            If Not String.IsNullOrWhiteSpace(bearerToken) Then
                req.Headers.Authorization = New AuthenticationHeaderValue("Bearer", bearerToken.Trim())
            End If
            If existing > 0 Then req.Headers.Range = New RangeHeaderValue(existing, Nothing)

            Using resp = Await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct)
                If resp.StatusCode = HttpStatusCode.RequestedRangeNotSatisfiable Then
                    ' Already complete (or server lost the file) — let the size check decide.
                    Return
                End If
                If resp.StatusCode = HttpStatusCode.Unauthorized OrElse resp.StatusCode = HttpStatusCode.Forbidden Then
                    Throw New HttpRequestException("Access denied (" & CInt(resp.StatusCode) & "). This model is gated: accept its license on huggingface.co and add your Hugging Face token in Settings.", Nothing, resp.StatusCode)
                End If
                If Not resp.IsSuccessStatusCode Then
                    Throw New HttpRequestException("Download failed: HTTP " & CInt(resp.StatusCode), Nothing, resp.StatusCode)
                End If

                Dim resumed = (resp.StatusCode = HttpStatusCode.PartialContent)
                If Not resumed Then existing = 0
                Dim total As Long = If(resp.Content.Headers.ContentLength, 0L) + existing
                If expectedSize > 0 Then total = expectedSize

                Using src = Await resp.Content.ReadAsStreamAsync(ct)
                    Using dst As New FileStream(part, If(resumed, FileMode.Append, FileMode.Create), FileAccess.Write, FileShare.Read, 1 << 20, True)
                        Dim buffer(1 << 20) As Byte
                        Dim done = existing
                        Dim sw = Stopwatch.StartNew()
                        Dim lastReport As Long = 0
                        Dim windowBytes As Long = 0
                        Dim windowStart As Long = 0
                        Dim speed As Double = 0
                        Do
                            Dim n = Await src.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)
                            If n = 0 Then Exit Do
                            Await dst.WriteAsync(buffer.AsMemory(0, n), ct)
                            done += n
                            windowBytes += n
                            Dim now = sw.ElapsedMilliseconds
                            If now - windowStart >= 1000 Then
                                speed = windowBytes * 1000.0 / (now - windowStart)
                                windowBytes = 0
                                windowStart = now
                            End If
                            If now - lastReport >= 200 Then
                                lastReport = now
                                progress?.Invoke(done, total, speed)
                            End If
                        Loop
                        progress?.Invoke(done, total, speed)
                    End Using
                End Using
            End Using
        End Using

        If expectedSize > 0 Then
            Dim got = New FileInfo(part).Length
            If got < expectedSize Then Throw New IOException($"Connection dropped at {FormatBytes(got)} of {FormatBytes(expectedSize)}")
        End If
    End Function

    ''' <summary>SHA-256 of a file as lowercase hex (Hugging Face LFS oid format).</summary>
    Public Async Function Sha256Async(filePath As String, ct As CancellationToken) As Task(Of String)
        Using fs As New FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, True)
            Dim hash = Await SHA256.HashDataAsync(fs, ct)
            Return Convert.ToHexString(hash).ToLowerInvariant()
        End Using
    End Function

    Public Function FormatBytes(bytes As Long) As String
        If bytes < 1024 Then Return bytes & " B"
        If bytes < 1024L * 1024 Then Return (bytes / 1024.0).ToString("0.0") & " KB"
        If bytes < 1024L * 1024 * 1024 Then Return (bytes / 1024.0 / 1024.0).ToString("0.0") & " MB"
        Return (bytes / 1024.0 / 1024.0 / 1024.0).ToString("0.00") & " GB"
    End Function
End Module
