Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports Newtonsoft.Json

''' <summary>
''' The studio's history: every result in the outputs folder, searchable by prompt, model, task and file name
''' (from the settings saved next to each result), and your ★ favourites (Data\favorites.json).
''' </summary>
Public Module History
    Private ReadOnly _text As New Dictionary(Of String, (Stamp As DateTime, Text As String))(StringComparer.OrdinalIgnoreCase)
    Private _favorites As HashSet(Of String)

    Private ReadOnly Property FavoritesFile As String
        Get
            Return Path.Combine(AppPaths.Roaming, "favorites.json")
        End Get
    End Property

    Private ReadOnly _favLock As New Object()

    Private Function Favs() As HashSet(Of String)
        If _favorites Is Nothing Then
            _favorites = New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            Try
                If File.Exists(FavoritesFile) Then
                    For Each n In JsonConvert.DeserializeObject(Of List(Of String))(File.ReadAllText(FavoritesFile))
                        _favorites.Add(n)
                    Next
                End If
            Catch
            End Try
        End If
        Return _favorites
    End Function

    Public Function IsFavorite(file As String) As Boolean
        SyncLock _favLock
            Return Favs().Contains(Path.GetFileName(file))
        End SyncLock
    End Function

    ''' <summary>Turns ★ on or off; returns the new state.</summary>
    Public Function ToggleFavorite(file As String) As Boolean
        SyncLock _favLock
            Dim f = Favs()
            Dim n = Path.GetFileName(file)
            Dim now = Not f.Remove(n)
            If now Then f.Add(n)
            Try
                IO.File.WriteAllText(FavoritesFile, JsonConvert.SerializeObject(f.ToList()))
            Catch
            End Try
            Return now
        End SyncLock
    End Function

    ''' <summary>What a result can be found by: file name, prompt, model, task (cached until the settings file changes).</summary>
    Public Function SearchText(file As String) As String
        Dim settings = MediaStudio.SettingsFile(file)
        Dim stamp = If(IO.File.Exists(settings), IO.File.GetLastWriteTimeUtc(settings), DateTime.MinValue)
        Dim hit As (Stamp As DateTime, Text As String) = (DateTime.MinValue, Nothing)
        SyncLock _text
            If _text.TryGetValue(file, hit) AndAlso hit.Stamp = stamp Then Return hit.Text
        End SyncLock
        Dim t = Path.GetFileNameWithoutExtension(file)
        Dim s = MediaStudio.LoadSettingsFor(file)
        If s IsNot Nothing Then
            Dim r = MediaStudio.Recipes().FirstOrDefault(Function(x) x.Id = s.Recipe)
            t &= " " & s.Prompt & " " & s.Recipe & " " & If(r?.Name, "") & " " & s.Task & " " & s.Variant
        End If
        SyncLock _text
            _text(file) = (stamp, t)
        End SyncLock
        Return t
    End Function

    ''' <summary>
    ''' Results newest first: all words of <paramref name="query"/> must appear (any order); with
    ''' <paramref name="favoritesOnly"/> only ★ ones.
    ''' </summary>
    Public Function Find(query As String, favoritesOnly As Boolean, max As Integer) As List(Of String)
        Dim words = If(query, "").Split({" "c}, StringSplitOptions.RemoveEmptyEntries)
        Dim files = Directory.GetFiles(MediaStudio.OutputFolder).
            Where(Function(f) (f.EndsWith(".png") OrElse f.EndsWith(".webm") OrElse f.EndsWith(".mp4")) AndAlso Not Path.GetFileName(f).Contains("-shot")).
            OrderByDescending(Function(f) IO.File.GetCreationTime(f))
        Dim res As New List(Of String)
        For Each f In files
            If favoritesOnly AndAlso Not IsFavorite(f) Then Continue For
            If words.Length > 0 Then
                Dim t = SearchText(f)
                If Not words.All(Function(w) t.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0) Then Continue For
            End If
            res.Add(f)
            If res.Count >= max Then Exit For
        Next
        Return res
    End Function
End Module

''' <summary>Messages for the user when a long job ends (tray balloon on the PC; the phone page picks them up too).</summary>
Public Module Notifier
    ''' <summary>Set by the main window: shows a tray balloon.</summary>
    Public Property Balloon As Action(Of String, String)
    Private ReadOnly _recent As New List(Of (Id As Integer, Title As String, Text As String))
    Private _next As Integer

    Public Sub Notify(title As String, text As String)
        SyncLock _recent
            _next += 1
            _recent.Add((_next, title, text))
            If _recent.Count > 30 Then _recent.RemoveAt(0)
        End SyncLock
        Try
            Balloon?.Invoke(title, text)
        Catch
        End Try
    End Sub

    ''' <summary>Messages after the given id (for the phone page).</summary>
    Public Function Since(id As Integer) As List(Of (Id As Integer, Title As String, Text As String))
        SyncLock _recent
            Return _recent.Where(Function(x) x.Id > id).ToList()
        End SyncLock
    End Function

    Public ReadOnly Property LastId As Integer
        Get
            SyncLock _recent
                Return _next
            End SyncLock
        End Get
    End Property
End Module
