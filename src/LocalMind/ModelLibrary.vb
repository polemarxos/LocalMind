Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Text.RegularExpressions
Imports Newtonsoft.Json

''' <summary>A GGUF model available on this PC.</summary>
Public Class LocalModel
    Public Property DisplayName As String
    Public Property MainFile As String          ' first file (part 1 for split models)
    Public Property MmprojFile As String        ' vision projector, if any
    Public Property Repo As String
    Public Property Quant As String
    Public Property SizeBytes As Long
    Public Property Params As Long
    Public Property DownloadedAt As DateTime
    Public Property Files As New List(Of String)()   ' relative to the model folder

    <JsonIgnore> Public Property Folder As String

    <JsonIgnore>
    Public ReadOnly Property IsVision As Boolean
        Get
            Return Not String.IsNullOrEmpty(MmprojFile) AndAlso File.Exists(MmprojFile)
        End Get
    End Property

    Public Overrides Function ToString() As String
        Return DisplayName
    End Function
End Class

''' <summary>
''' Scans the models folder. Models downloaded by the app live in their own sub-folder with
''' a model.json; any other *.gguf files the user drops into the folder are picked up too.
''' </summary>
Public Class ModelLibrary

    Public Const MetaFileName As String = "model.json"
    Private Shared ReadOnly SplitPart As New Regex("-(\d{5})-of-(\d{5})\.gguf$", RegexOptions.IgnoreCase)

    Private ReadOnly _folder As Func(Of String)
    Private _models As New List(Of LocalModel)()
    Private ReadOnly _lock As New Object()

    Public Event Changed()

    Public Sub New(folder As Func(Of String))
        _folder = folder
    End Sub

    Public ReadOnly Property Folder As String
        Get
            Dim f = _folder()
            Directory.CreateDirectory(f)
            Return f
        End Get
    End Property

    Public Function All() As List(Of LocalModel)
        SyncLock _lock
            Return _models.ToList()
        End SyncLock
    End Function

    Public Function FindByName(name As String) As LocalModel
        If String.IsNullOrEmpty(name) Then Return Nothing
        SyncLock _lock
            Return _models.FirstOrDefault(Function(m) String.Equals(m.DisplayName, name, StringComparison.OrdinalIgnoreCase))
        End SyncLock
    End Function

    Public Function HasRepoQuant(repo As String, quant As String) As Boolean
        SyncLock _lock
            Return _models.Any(Function(m) String.Equals(m.Repo, repo, StringComparison.OrdinalIgnoreCase) AndAlso
                                           String.Equals(m.Quant, quant, StringComparison.OrdinalIgnoreCase))
        End SyncLock
    End Function

    Public Function HasRepo(repo As String) As Boolean
        SyncLock _lock
            Return _models.Any(Function(m) String.Equals(m.Repo, repo, StringComparison.OrdinalIgnoreCase))
        End SyncLock
    End Function

    Public Sub Refresh()
        Dim found As New List(Of LocalModel)()
        Dim root = Folder
        Dim claimed As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

        ' 1) Models downloaded by the app (folder + model.json)
        For Each meta In SafeEnum(root, MetaFileName)
            Try
                Dim m = JsonConvert.DeserializeObject(Of LocalModel)(File.ReadAllText(meta))
                If m Is Nothing Then Continue For
                m.Folder = Path.GetDirectoryName(meta)
                m.MainFile = Path.Combine(m.Folder, Path.GetFileName(m.MainFile))
                If Not String.IsNullOrEmpty(m.MmprojFile) Then m.MmprojFile = Path.Combine(m.Folder, Path.GetFileName(m.MmprojFile))
                If Not File.Exists(m.MainFile) Then Continue For
                found.Add(m)
                For Each f In Directory.GetFiles(m.Folder, "*.gguf")
                    claimed.Add(f)
                Next
            Catch
            End Try
        Next

        ' 2) Loose GGUF files the user copied in themselves
        For Each f In SafeEnum(root, "*.gguf")
            If claimed.Contains(f) Then Continue For
            Dim name = Path.GetFileName(f)
            Dim low = name.ToLowerInvariant()
            If low.Contains("mmproj") OrElse low.StartsWith("mtp-") OrElse low.Contains("imatrix") Then Continue For
            Dim sp = SplitPart.Match(name)
            If sp.Success AndAlso sp.Groups(1).Value <> "00001" Then Continue For

            Dim dir = Path.GetDirectoryName(f)
            Dim mm = Directory.GetFiles(dir, "*mmproj*.gguf").OrderBy(Function(x) MmprojRank(x)).FirstOrDefault()
            Dim size As Long = New FileInfo(f).Length
            If sp.Success Then
                Dim prefix = name.Substring(0, sp.Index)
                size = Directory.GetFiles(dir, prefix & "-*-of-*.gguf").Sum(Function(x) New FileInfo(x).Length)
            End If
            Dim display = Path.GetFileNameWithoutExtension(If(sp.Success, name.Substring(0, sp.Index) & ".gguf", name))
            found.Add(New LocalModel With {
                .DisplayName = display, .MainFile = f, .MmprojFile = mm, .Folder = dir,
                .SizeBytes = size, .Quant = ModelCatalog.ParseQuant(name),
                .DownloadedAt = File.GetCreationTime(f)})
        Next

        ' Unique display names
        For Each grp In found.GroupBy(Function(m) m.DisplayName, StringComparer.OrdinalIgnoreCase).Where(Function(g) g.Count() > 1)
            Dim i = 1
            For Each m In grp.Skip(1)
                i += 1
                m.DisplayName &= $" ({i})"
            Next
        Next

        SyncLock _lock
            _models = found.OrderBy(Function(m) m.DisplayName, StringComparer.OrdinalIgnoreCase).ToList()
        End SyncLock
        RaiseEvent Changed()
    End Sub

    Friend Shared Function MmprojRank(path As String) As Integer
        Dim n = IO.Path.GetFileName(path).ToUpperInvariant()
        If n.Contains("F16") AndAlso Not n.Contains("BF16") Then Return 0
        If n.Contains("BF16") Then Return 1
        If n.Contains("Q8") Then Return 2
        If n.Contains("F32") Then Return 3
        Return 4
    End Function

    Private Shared Function SafeEnum(root As String, pattern As String) As IEnumerable(Of String)
        Try
            Return Directory.EnumerateFiles(root, pattern, New EnumerationOptions With {
                .RecurseSubdirectories = True, .IgnoreInaccessible = True, .MaxRecursionDepth = 4}).ToList()
        Catch
            Return New List(Of String)()
        End Try
    End Function

    ''' <summary>Deletes a model's files. Downloaded models lose their whole folder.</summary>
    Public Sub Delete(m As LocalModel)
        If File.Exists(Path.Combine(m.Folder, MetaFileName)) Then
            Directory.Delete(m.Folder, True)
        Else
            Dim sp = SplitPart.Match(Path.GetFileName(m.MainFile))
            If sp.Success Then
                Dim prefix = Path.GetFileName(m.MainFile).Substring(0, sp.Index)
                For Each f In Directory.GetFiles(m.Folder, prefix & "-*-of-*.gguf")
                    File.Delete(f)
                Next
            Else
                File.Delete(m.MainFile)
            End If
        End If
        Refresh()
    End Sub

    Public Function TotalSize() As Long
        SyncLock _lock
            Return _models.Sum(Function(m) m.SizeBytes)
        End SyncLock
    End Function

    Public Shared Function SafeFolderName(s As String) As String
        Dim r = s.Replace("/", "__")
        For Each c In Path.GetInvalidFileNameChars()
            r = r.Replace(c, "_"c)
        Next
        Return r
    End Function
End Class
