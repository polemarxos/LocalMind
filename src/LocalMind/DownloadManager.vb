Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports Newtonsoft.Json

Public Enum DownloadStatus
    Queued
    Resolving
    Downloading
    Verifying
    Completed
    Failed
    Cancelled
End Enum

Public Class DownloadJob
    Public Property Id As String = Guid.NewGuid().ToString("N")
    Public Property Repo As String
    Public Property ModelName As String
    Public Property RequestedQuant As String = "Auto"
    Public Property Quant As String
    Public Property Params As Long
    Public Property IsVisionRepo As Boolean
    Public Property IncludeVision As Boolean = True
    Public Property Files As New List(Of RepoFile)()
    Public Property Mmproj As RepoFile
    Public Property Mtp As RepoFile
    Public Property TargetDir As String

    <JsonIgnore> Public Property Status As DownloadStatus = DownloadStatus.Queued
    <JsonIgnore> Public Property BytesDone As Long
    <JsonIgnore> Public Property BytesTotal As Long
    <JsonIgnore> Public Property Speed As Double
    <JsonIgnore> Public Property ErrorText As String
    <JsonIgnore> Public Property Cts As CancellationTokenSource

    <JsonIgnore> Public ReadOnly Property Percent As Integer
        Get
            If BytesTotal <= 0 Then Return 0
            Return CInt(Math.Min(100, BytesDone * 100 \ BytesTotal))
        End Get
    End Property

    <JsonIgnore> Public ReadOnly Property StatusText As String
        Get
            Select Case Status
                Case DownloadStatus.Downloading
                    Dim eta = If(Speed > 0, TimeSpan.FromSeconds((BytesTotal - BytesDone) / Speed), TimeSpan.Zero)
                    Return $"{Percent}%  ·  {Downloader.FormatBytes(BytesDone)} / {Downloader.FormatBytes(BytesTotal)}  ·  " &
                           $"{Downloader.FormatBytes(CLng(Speed))}/s" & If(Speed > 0, $"  ·  {FormatEta(eta)} left", "")
                Case DownloadStatus.Failed : Return "Failed: " & ErrorText
                Case DownloadStatus.Resolving : Return "Looking up files…"
                Case DownloadStatus.Verifying : Return "Verifying checksum…"
                Case DownloadStatus.Completed : Return "✔ Ready to use"
                Case Else : Return Status.ToString()
            End Select
        End Get
    End Property

    Private Shared Function FormatEta(t As TimeSpan) As String
        If t.TotalHours >= 1 Then Return $"{CInt(Math.Floor(t.TotalHours))}h {t.Minutes}m"
        If t.TotalMinutes >= 1 Then Return $"{t.Minutes}m {t.Seconds}s"
        Return $"{t.Seconds}s"
    End Function

    <JsonIgnore> Public ReadOnly Property Title As String
        Get
            Return ModelName & " · " & If(String.IsNullOrEmpty(Quant), RequestedQuant, Quant)
        End Get
    End Property
End Class

''' <summary>
''' Sequential download queue. A job file (download.json) is written into the target folder
''' while downloading, so interrupted downloads are found and resumed on the next start.
''' </summary>
Public Class DownloadManager

    Private Const PendingFile As String = "download.json"

    Private ReadOnly _library As ModelLibrary
    Private ReadOnly _settings As Func(Of AppSettings)
    Private ReadOnly _jobs As New List(Of DownloadJob)()
    Private ReadOnly _lock As New Object()
    Private _worker As Task

    Public Event JobChanged(job As DownloadJob)
    Public Event JobCompleted(job As DownloadJob)

    Public Sub New(library As ModelLibrary, settings As Func(Of AppSettings))
        _library = library
        _settings = settings
    End Sub

    Public Function Jobs() As List(Of DownloadJob)
        SyncLock _lock
            Return _jobs.ToList()
        End SyncLock
    End Function

    Public ReadOnly Property ActiveCount As Integer
        Get
            SyncLock _lock
                Return _jobs.Where(Function(j) j.Status = DownloadStatus.Queued OrElse j.Status = DownloadStatus.Resolving OrElse
                                               j.Status = DownloadStatus.Downloading OrElse j.Status = DownloadStatus.Verifying).Count()
            End SyncLock
        End Get
    End Property

    ''' <summary>Queues a model from the catalog. Quant "Auto" picks the best one for this PC.</summary>
    Public Function Enqueue(e As CatalogEntry, quant As String) As DownloadJob
        SyncLock _lock
            Dim existing = _jobs.FirstOrDefault(Function(j) j.Repo.Equals(e.Repo, StringComparison.OrdinalIgnoreCase) AndAlso
                                                       j.RequestedQuant = quant AndAlso j.Status <= DownloadStatus.Verifying)
            If existing IsNot Nothing Then Return existing
            Dim job As New DownloadJob With {
                .Repo = e.Repo, .ModelName = e.Name, .RequestedQuant = If(quant, "Auto"),
                .Params = e.Params, .IsVisionRepo = e.IsVision
            }
            _jobs.Add(job)
            RaiseEvent JobChanged(job)
            EnsureWorker()
            Return job
        End SyncLock
    End Function

    Public Sub Cancel(job As DownloadJob)
        If job Is Nothing Then Return
        If job.Status = DownloadStatus.Queued Then
            job.Status = DownloadStatus.Cancelled
            RaiseEvent JobChanged(job)
        Else
            job.Cts?.Cancel()
        End If
    End Sub

    ''' <summary>Cancels and deletes the partial files.</summary>
    Public Sub CancelAndDelete(job As DownloadJob)
        Cancel(job)
        Task.Run(Async Function()
                     Await Task.Delay(1500)
                     Try
                         If job.TargetDir IsNot Nothing AndAlso Directory.Exists(job.TargetDir) AndAlso
                            Not File.Exists(Path.Combine(job.TargetDir, ModelLibrary.MetaFileName)) Then
                             Directory.Delete(job.TargetDir, True)
                         End If
                     Catch
                     End Try
                 End Function)
    End Sub

    Public Sub Retry(job As DownloadJob)
        If job Is Nothing Then Return
        If job.Status = DownloadStatus.Failed OrElse job.Status = DownloadStatus.Cancelled Then
            job.Status = DownloadStatus.Queued
            job.ErrorText = Nothing
            RaiseEvent JobChanged(job)
            SyncLock _lock
                EnsureWorker()
            End SyncLock
        End If
    End Sub

    Public Sub ClearFinished()
        SyncLock _lock
            _jobs.RemoveAll(Function(j) j.Status = DownloadStatus.Completed OrElse j.Status = DownloadStatus.Cancelled)
        End SyncLock
        RaiseEvent JobChanged(Nothing)
    End Sub

    ''' <summary>Finds interrupted downloads from a previous session and queues them again.</summary>
    Public Function ResumePending() As Integer
        Dim n = 0
        Try
            For Each f In Directory.EnumerateFiles(_library.Folder, PendingFile, SearchOption.AllDirectories)
                Try
                    Dim job = JsonConvert.DeserializeObject(Of DownloadJob)(File.ReadAllText(f))
                    If job Is Nothing OrElse String.IsNullOrEmpty(job.Repo) Then Continue For
                    job.TargetDir = Path.GetDirectoryName(f)
                    job.Status = DownloadStatus.Queued
                    SyncLock _lock
                        If _jobs.Any(Function(j) j.TargetDir = job.TargetDir) Then Continue For
                        _jobs.Add(job)
                        EnsureWorker()
                    End SyncLock
                    RaiseEvent JobChanged(job)
                    n += 1
                Catch
                End Try
            Next
        Catch
        End Try
        Return n
    End Function

    ' Up to three models download at the same time.
    Private Const Parallel As Integer = 3
    Private ReadOnly _workers As New List(Of Task)

    Private Sub EnsureWorker()
        ' caller holds _lock
        _workers.RemoveAll(Function(w) w.IsCompleted)
        Dim waiting = Enumerable.Count(_jobs, Function(j As DownloadJob) j.Status = DownloadStatus.Queued)
        While _workers.Count < Parallel AndAlso waiting > 0
            _workers.Add(Task.Run(AddressOf WorkLoopAsync))
            waiting -= 1
        End While
        _worker = _workers.LastOrDefault()
    End Sub

    Private Async Function WorkLoopAsync() As Task
        Do
            Dim job As DownloadJob
            SyncLock _lock
                job = _jobs.FirstOrDefault(Function(j) j.Status = DownloadStatus.Queued)
                If job IsNot Nothing Then job.Status = DownloadStatus.Resolving     ' claimed: no other worker takes it
            End SyncLock
            If job Is Nothing Then Return
            Await RunJobAsync(job)
        Loop
    End Function

    Private Async Function RunJobAsync(job As DownloadJob) As Task
        job.Cts = New CancellationTokenSource()
        Dim ct = job.Cts.Token
        Dim s = _settings()
        Try
            ' 1) Resolve the exact files (unless resuming a job that already knows them)
            If job.Files.Count = 0 Then
                job.Status = DownloadStatus.Resolving
                RaiseEvent JobChanged(job)
                Dim repoFiles = Await ModelCatalog.GetRepoFilesAsync(job.Repo, s.HuggingFaceToken, ct)
                Dim entry As New CatalogEntry With {.Repo = job.Repo, .Name = job.ModelName, .Params = job.Params}
                If entry.Params <= 0 Then
                    ' Unknown size (e.g. HF search result) — infer from the Q4_K_M file size.
                    Dim q4 = repoFiles.Quants.FirstOrDefault(Function(q) q.Label.Contains("Q4_K_M"))
                    If q4 IsNot Nothing Then entry.Params = CLng(q4.TotalBytes * 8 / 4.85)
                End If
                Dim pick = ModelCatalog.PickQuant(entry, repoFiles, job.RequestedQuant, HardwareInfo.Current(), s.ContextSize)
                If pick Is Nothing Then Throw New Exception("No GGUF model files found in " & job.Repo)
                job.Quant = pick.Label
                job.Files = pick.Files.ToList()
                job.Mmproj = If(job.IncludeVision, repoFiles.Mmproj, Nothing)
                job.Mtp = If(s.DownloadSpeedUpFiles, repoFiles.Mtp, Nothing)
                job.TargetDir = Path.Combine(_library.Folder, ModelLibrary.SafeFolderName(job.Repo) & "__" & job.Quant)
            End If

            If _library.HasRepoQuant(job.Repo, job.Quant) AndAlso
               File.Exists(Path.Combine(job.TargetDir, ModelLibrary.MetaFileName)) Then
                job.Status = DownloadStatus.Completed
                RaiseEvent JobChanged(job)
                Return
            End If

            Directory.CreateDirectory(job.TargetDir)
            CheckDiskSpace(job)
            File.WriteAllText(Path.Combine(job.TargetDir, PendingFile), JsonConvert.SerializeObject(job, Formatting.Indented))

            ' 2) Download every file (model parts + vision projector)
            Dim all = job.Files.ToList()
            If job.Mmproj IsNot Nothing Then all.Add(job.Mmproj)
            If job.Mtp IsNot Nothing Then all.Add(job.Mtp)
            job.BytesTotal = all.Sum(Function(f) f.Size)
            job.Status = DownloadStatus.Downloading
            RaiseEvent JobChanged(job)

            Dim doneBefore As Long = 0
            For Each rf In all
                Dim dest = Path.Combine(job.TargetDir, Path.GetFileName(rf.Path))
                Dim url = $"https://huggingface.co/{job.Repo}/resolve/main/{EscapePath(rf.Path)}"
                Dim baseDone = doneBefore
                Await Downloader.DownloadFileAsync(url, dest, s.HuggingFaceToken,
                    Sub(done, total, speed)
                        job.BytesDone = baseDone + done
                        job.Speed = speed
                        RaiseEvent JobChanged(job)
                    End Sub, ct, rf.Size)

                ' 3) Integrity check against the Hugging Face LFS SHA-256
                If s.VerifyDownloads AndAlso Not String.IsNullOrEmpty(rf.Sha256) AndAlso rf.Sha256.Length = 64 Then
                    job.Status = DownloadStatus.Verifying
                    RaiseEvent JobChanged(job)
                    Dim h = Await Downloader.Sha256Async(dest, ct)
                    If Not h.Equals(rf.Sha256, StringComparison.OrdinalIgnoreCase) Then
                        File.Delete(dest)
                        Throw New IOException("Checksum mismatch for " & Path.GetFileName(dest) & " — the file was deleted, press Retry.")
                    End If
                    job.Status = DownloadStatus.Downloading
                End If
                doneBefore += rf.Size
                job.BytesDone = doneBefore
            Next

            ' 4) Register in the library
            Dim meta As New LocalModel With {
                .DisplayName = job.ModelName & " · " & job.Quant,
                .MainFile = Path.GetFileName(job.Files(0).Path),
                .MmprojFile = If(job.Mmproj IsNot Nothing, Path.GetFileName(job.Mmproj.Path), Nothing),
                .MtpFile = If(job.Mtp IsNot Nothing, Path.GetFileName(job.Mtp.Path), Nothing),
                .Repo = job.Repo, .Quant = job.Quant,
                .SizeBytes = job.BytesTotal, .Params = job.Params,
                .DownloadedAt = DateTime.Now,
                .Files = all.Select(Function(f) Path.GetFileName(f.Path)).ToList()
            }
            File.WriteAllText(Path.Combine(job.TargetDir, ModelLibrary.MetaFileName), JsonConvert.SerializeObject(meta, Formatting.Indented))
            File.Delete(Path.Combine(job.TargetDir, PendingFile))

            job.Status = DownloadStatus.Completed
            job.Speed = 0
            _library.Refresh()
            RaiseEvent JobChanged(job)
            RaiseEvent JobCompleted(job)

        Catch ex As OperationCanceledException
            job.Status = DownloadStatus.Cancelled
            RaiseEvent JobChanged(job)
        Catch ex As Exception
            job.Status = DownloadStatus.Failed
            job.ErrorText = ex.Message
            RaiseEvent JobChanged(job)
        End Try
    End Function

    Private Sub CheckDiskSpace(job As DownloadJob)
        Try
            Dim need = job.Files.Sum(Function(f) f.Size) + If(job.Mmproj?.Size, 0L)
            Dim have = Directory.GetFiles(job.TargetDir).Sum(Function(f) New FileInfo(f).Length)
            Dim drive As New DriveInfo(Path.GetPathRoot(job.TargetDir))
            If drive.AvailableFreeSpace < (need - have) + 512L * 1024 * 1024 Then
                Throw New IOException($"Not enough disk space on {drive.Name}: need {Downloader.FormatBytes(need - have)}, " &
                                      $"free {Downloader.FormatBytes(drive.AvailableFreeSpace)}. Change the models folder in Settings.")
            End If
        Catch ex As IOException
            Throw
        Catch
        End Try
    End Sub

    Private Shared Function EscapePath(p As String) As String
        Return String.Join("/", p.Split("/"c).Select(Function(seg) Uri.EscapeDataString(seg)))
    End Function
End Class
