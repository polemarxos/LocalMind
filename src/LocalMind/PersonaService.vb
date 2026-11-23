Imports System
Imports System.Collections.Generic
Imports System.IO
Imports Newtonsoft.Json

Public Class PersonaService

    Public ReadOnly FilePath As String

    Public Sub New()
        Dim dir = AppPaths.Roaming
        Directory.CreateDirectory(dir)
        FilePath = Path.Combine(dir, "personas.json")
    End Sub

    Public Function Load() As List(Of Persona)
        If File.Exists(FilePath) Then
            Try
                Dim text = File.ReadAllText(FilePath)
                If Not String.IsNullOrWhiteSpace(text) Then
                    Dim list = JsonConvert.DeserializeObject(Of List(Of Persona))(text)
                    If list IsNot Nothing Then Return list
                End If
            Catch
            End Try
        End If
        Return Defaults()
    End Function

    ''' <summary>
    ''' Saves the persona list. Throws on failure — callers must handle.
    ''' </summary>
    Public Sub Save(list As List(Of Persona))
        If list Is Nothing Then
            Throw New ArgumentNullException(NameOf(list), "Persona list is Nothing.")
        End If
        Dim json = JsonConvert.SerializeObject(list, Formatting.Indented)
        File.WriteAllText(FilePath, json)
    End Sub

    Private Function Defaults() As List(Of Persona)
        Return New List(Of Persona) From {
            New Persona With {
                .Name = "Helpful Assistant",
                .SystemPrompt = "You are a helpful, friendly assistant. Answer clearly and concisely.",
                .Temperature = 0.7
            }
        }
    End Function
End Class