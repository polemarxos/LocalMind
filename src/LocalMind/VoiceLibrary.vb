Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports Newtonsoft.Json

''' <summary>A cloned voice kept for reuse: a short recording plus (optionally) its words.</summary>
Public Class SavedVoice
    Public Property Name As String
    ''' <summary>File name inside Data\Voices.</summary>
    Public Property File As String
    Public Property Words As String
    Public Property Language As String
    Public Property Created As DateTime = DateTime.Now

    <JsonIgnore> Public ReadOnly Property FullPath As String
        Get
            Return Path.Combine(VoiceLibrary.Folder, File)
        End Get
    End Property

    Public Overrides Function ToString() As String
        Return Name
    End Function
End Class

''' <summary>Your saved voices (Data\Voices, Data\voices.json), used by the music & voice studio and by read-aloud.</summary>
Public Module VoiceLibrary
    Public ReadOnly Property Folder As String
        Get
            Dim d = Path.Combine(AppPaths.Roaming, "Voices")
            Directory.CreateDirectory(d)
            Return d
        End Get
    End Property

    Private ReadOnly Property StoreFile As String
        Get
            Return Path.Combine(AppPaths.Roaming, "voices.json")
        End Get
    End Property

    Public Function All() As List(Of SavedVoice)
        Try
            If IO.File.Exists(StoreFile) Then
                Return JsonConvert.DeserializeObject(Of List(Of SavedVoice))(IO.File.ReadAllText(StoreFile)).
                       Where(Function(v) IO.File.Exists(v.FullPath)).OrderBy(Function(v) v.Name).ToList()
            End If
        Catch
        End Try
        Return New List(Of SavedVoice)()
    End Function

    Public Function Find(name As String) As SavedVoice
        Return All().FirstOrDefault(Function(v) v.Name.Equals(If(name, ""), StringComparison.OrdinalIgnoreCase))
    End Function

    ''' <summary>Copies the recording into the library under a name (replacing a voice with the same name).</summary>
    Public Function Add(name As String, recording As String, words As String, language As String) As SavedVoice
        Dim list = All()
        Dim old = list.FirstOrDefault(Function(x) x.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
        If old IsNot Nothing Then
            list.Remove(old)
            Try : IO.File.Delete(old.FullPath) : Catch : End Try
        End If
        Dim safe = String.Concat(name.Select(Function(c) If(Path.GetInvalidFileNameChars().Contains(c), "_"c, c)))
        Dim fileName = $"{safe}-{DateTime.Now:yyyyMMddHHmmss}{Path.GetExtension(recording)}"
        IO.File.Copy(recording, Path.Combine(Folder, fileName), True)
        Dim sv As New SavedVoice With {.Name = name, .File = fileName, .Words = words, .Language = language}
        list.Add(sv)
        Save(list)
        Return sv
    End Function

    Public Sub Delete(v As SavedVoice)
        Dim list = All()
        list.RemoveAll(Function(x) x.Name = v.Name)
        Try : IO.File.Delete(v.FullPath) : Catch : End Try
        Save(list)
    End Sub

    Private Sub Save(list As List(Of SavedVoice))
        IO.File.WriteAllText(StoreFile, JsonConvert.SerializeObject(list, Formatting.Indented))
    End Sub
End Module
