Imports System.Collections.Generic
Imports System.Runtime.Serialization
Imports Newtonsoft.Json

Public Class ChatMessage
    Public Property Role As String
    Public Property Content As String
    Public Property Thinking As String
    Public Property Images As List(Of String) = New List(Of String)()
    ''' <summary>Model that produced an assistant message (for mixed-model chats).</summary>
    Public Property Model As String
    Public Property TokensPerSecond As Double

    Public Function ShouldSerializeImages() As Boolean
        Return Images IsNot Nothing AndAlso Images.Count > 0
    End Function
End Class

Public Class ChatSession
    Public Property Id As String = Guid.NewGuid().ToString("N")
    Public Property Title As String = "New Chat"
    Public Property CreatedAt As DateTime = DateTime.Now
    Public Property UpdatedAt As DateTime = DateTime.Now
    Public Property Model As String
    Public Property PersonaName As String
    Public Property Messages As List(Of ChatMessage) = New List(Of ChatMessage)()

    <OnDeserialized>
    Friend Sub OnDeserializedMethod(context As System.Runtime.Serialization.StreamingContext)
        ' Chats saved by v1 have no UpdatedAt.
        If UpdatedAt = Nothing OrElse UpdatedAt < CreatedAt Then UpdatedAt = CreatedAt
    End Sub
End Class

Public Class Persona
    Public Property Id As String = Guid.NewGuid().ToString("N")
    Public Property Name As String
    Public Property SystemPrompt As String
    Public Property Temperature As Double = 0.7
End Class

Public Class OllamaTagsResponse
    Public Property models As List(Of OllamaModelInfo)
End Class

Public Class OllamaModelInfo
    Public Property name As String
    Public Property model As String
    Public Property size As Long
End Class

Public Class OllamaChatChunk
    Public Property model As String
    Public Property created_at As String
    Public Property message As ChatMessage
    Public Property done As Boolean
    Public Property done_reason As String
    Public Property eval_count As Integer
    Public Property eval_duration As Long
End Class
