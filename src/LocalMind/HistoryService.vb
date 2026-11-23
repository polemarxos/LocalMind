Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports Newtonsoft.Json

''' <summary>
''' Chat history on disk (one JSON file per chat) with an in-memory cache keyed by file
''' timestamp, so listing/searching no longer re-reads every file after each message.
''' Thread-safe: the desktop UI and LAN clients may save at the same time.
''' </summary>
Public Class HistoryService

    Public ReadOnly Folder As String
    Private ReadOnly _cache As New Dictionary(Of String, (Stamp As DateTime, Session As ChatSession))(StringComparer.OrdinalIgnoreCase)
    Private ReadOnly _lock As New Object()

    ''' <summary>Raised after a chat is saved or deleted (id of that chat). May fire on any thread.</summary>
    Public Event Changed(id As String)

    Public Sub New()
        Folder = Path.Combine(AppPaths.Roaming, "chats")
        Directory.CreateDirectory(Folder)
    End Sub

    Private Function FileFor(id As String) As String
        ' Ids are GUIDs; strip anything else so a LAN client can't escape the folder.
        Dim safe = New String(If(id, "").Where(Function(c) Char.IsLetterOrDigit(c) OrElse c = "-"c OrElse c = "_"c).ToArray())
        If safe.Length = 0 Then safe = "invalid"
        Return Path.Combine(Folder, safe & ".json")
    End Function

    Public Sub Save(session As ChatSession)
        If session Is Nothing OrElse session.Messages.Count = 0 Then Return
        SyncLock _lock
            Dim filePath = FileFor(session.Id)
            Dim tmp = filePath & ".tmp"
            File.WriteAllText(tmp, JsonConvert.SerializeObject(session, Formatting.Indented))
            File.Move(tmp, filePath, True)
            _cache(session.Id) = (File.GetLastWriteTimeUtc(filePath), session)
        End SyncLock
        RaiseEvent Changed(session.Id)
    End Sub

    Public Function Load(id As String) As ChatSession
        SyncLock _lock
            Dim filePath = FileFor(id)
            If Not File.Exists(filePath) Then Return Nothing
            Return LoadCached(filePath)
        End SyncLock
    End Function

    ''' <summary>An independent copy (for LAN clients), so two writers never share one object.</summary>
    Public Function LoadCopy(id As String) As ChatSession
        Dim s = Load(id)
        If s Is Nothing Then Return Nothing
        SyncLock _lock
            Return JsonConvert.DeserializeObject(Of ChatSession)(JsonConvert.SerializeObject(s))
        End SyncLock
    End Function

    Private Function LoadCached(filePath As String) As ChatSession
        Dim stamp = File.GetLastWriteTimeUtc(filePath)
        Dim id = Path.GetFileNameWithoutExtension(filePath)
        Dim hit As (Stamp As DateTime, Session As ChatSession) = Nothing
        If _cache.TryGetValue(id, hit) AndAlso hit.Stamp = stamp Then Return hit.Session
        Dim s = JsonConvert.DeserializeObject(Of ChatSession)(File.ReadAllText(filePath))
        If s IsNot Nothing Then _cache(id) = (stamp, s)
        Return s
    End Function

    Public Function [Delete](id As String) As Boolean
        Dim ok = False
        SyncLock _lock
            Dim filePath = FileFor(id)
            _cache.Remove(id)
            If File.Exists(filePath) Then
                File.Delete(filePath)
                ok = True
            End If
        End SyncLock
        If ok Then RaiseEvent Changed(id)
        Return ok
    End Function

    ''' <summary>All sessions ordered by most recent activity first.</summary>
    Public Function ListAll() As List(Of ChatSession)
        Dim list As New List(Of ChatSession)
        SyncLock _lock
            If Not Directory.Exists(Folder) Then Return list
            Dim seen As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            For Each f In Directory.GetFiles(Folder, "*.json")
                Try
                    Dim s = LoadCached(f)
                    If s IsNot Nothing Then
                        list.Add(s)
                        seen.Add(Path.GetFileNameWithoutExtension(f))
                    End If
                Catch
                End Try
            Next
            ' Drop cache entries for files deleted outside the app.
            For Each k In _cache.Keys.Where(Function(x) Not seen.Contains(x)).ToList()
                _cache.Remove(k)
            Next
        End SyncLock
        Return list.OrderByDescending(Function(s) s.UpdatedAt).ToList()
    End Function

    ''' <summary>Search across all saved conversations. Returns (session, snippet) pairs.</summary>
    Public Function Search(query As String) As List(Of Tuple(Of ChatSession, String))
        Dim results As New List(Of Tuple(Of ChatSession, String))()
        If String.IsNullOrWhiteSpace(query) Then Return results

        For Each s In ListAll()
            If s.Title IsNot Nothing AndAlso s.Title.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 Then
                results.Add(Tuple.Create(s, s.Title))
                Continue For
            End If
            Dim hit = s.Messages.FirstOrDefault(Function(m) m.Content IsNot Nothing AndAlso
                                                    m.Content.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
            If hit IsNot Nothing Then results.Add(Tuple.Create(s, Snippet(hit.Content, query, 80)))
        Next
        Return results
    End Function

    Private Function Snippet(text As String, query As String, radius As Integer) As String
        Dim idx = text.IndexOf(query, StringComparison.OrdinalIgnoreCase)
        If idx < 0 Then Return text.Substring(0, Math.Min(text.Length, radius * 2))
        Dim start = Math.Max(0, idx - radius)
        Dim length = Math.Min(text.Length - start, query.Length + radius * 2)
        Dim snip = text.Substring(start, length).Replace(vbCrLf, " ").Replace(vbLf, " ")
        If start > 0 Then snip = "…" & snip
        If start + length < text.Length Then snip &= "…"
        Return snip
    End Function
End Class
