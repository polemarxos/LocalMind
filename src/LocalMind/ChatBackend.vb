Imports System
Imports System.Collections.Generic
Imports System.Threading
Imports System.Threading.Tasks

Public Class ChatRequestOptions
    Public Property Model As String
    Public Property Temperature As Double = 0.7
    Public Property TopP As Double = 0.9
    Public Property MaxTokens As Integer = 0
    Public Property EnableThinking As Boolean
End Class

Public Class ChatResult
    Public Property Content As String = ""
    Public Property Thinking As String = ""
    Public Property Tokens As Integer
    ''' <summary>Generation speed reported by the engine itself (0 if unknown).</summary>
    Public Property EngineTokensPerSecond As Double
    ''' <summary>Prompt-processing speed reported by llama.cpp (0 if unknown).</summary>
    Public Property PromptTokensPerSecond As Double
End Class

''' <summary>
''' A source of chat completions. Implemented by the built-in llama.cpp engine,
''' Ollama, and any OpenAI-compatible server (LM Studio, vLLM, another PC running this app…).
''' </summary>
Public Interface IChatBackend
    ReadOnly Property DisplayName As String
    Function IsOnlineAsync() As Task(Of Boolean)
    Function ListModelsAsync() As Task(Of List(Of String))
    Function StreamChatAsync(messages As List(Of ChatMessage),
                             opts As ChatRequestOptions,
                             onThinking As Action(Of String),
                             onToken As Action(Of String),
                             ct As CancellationToken) As Task(Of ChatResult)
End Interface

''' <summary>
''' Splits a streamed text into thinking/content when a model emits raw
''' &lt;think&gt;…&lt;/think&gt; tags instead of a separate reasoning field.
''' Robust to tags split across chunks.
''' </summary>
Public Class ThinkTagSplitter
    Private ReadOnly _onThinking As Action(Of String)
    Private ReadOnly _onContent As Action(Of String)
    Private _inThink As Boolean
    Private _pending As String = ""
    Private _sawAnyContent As Boolean

    Public Sub New(onThinking As Action(Of String), onContent As Action(Of String))
        _onThinking = onThinking
        _onContent = onContent
    End Sub

    Public Sub Feed(chunk As String)
        If String.IsNullOrEmpty(chunk) Then Return
        Dim s = _pending & chunk
        _pending = ""
        Do While s.Length > 0
            Dim tag = If(_inThink, "</think>", "<think>")
            Dim idx = s.IndexOf(tag, StringComparison.Ordinal)
            If idx >= 0 Then
                Emit(s.Substring(0, idx))
                s = s.Substring(idx + tag.Length)
                ' Only treat <think> as a tag at the very start of the answer.
                If Not _inThink AndAlso _sawAnyContent Then
                    Emit(tag)
                    Continue Do
                End If
                _inThink = Not _inThink
                Continue Do
            End If
            ' Hold back a possible partial tag at the end of the chunk.
            Dim keep = PartialSuffix(s, tag)
            Emit(s.Substring(0, s.Length - keep))
            _pending = s.Substring(s.Length - keep)
            Exit Do
        Loop
    End Sub

    Public Sub Flush()
        If _pending.Length > 0 Then Emit(_pending)
        _pending = ""
    End Sub

    Private Sub Emit(text As String)
        If text.Length = 0 Then Return
        If _inThink Then
            _onThinking(text)
        Else
            If Not _sawAnyContent Then
                text = text.TrimStart()           ' drop the newline left after </think>
                If text.Length = 0 Then Return
            End If
            _sawAnyContent = True
            _onContent(text)
        End If
    End Sub

    Private Shared Function PartialSuffix(s As String, tag As String) As Integer
        For n = Math.Min(tag.Length - 1, s.Length) To 1 Step -1
            If s.EndsWith(tag.Substring(0, n), StringComparison.Ordinal) Then Return n
        Next
        Return 0
    End Function
End Class
