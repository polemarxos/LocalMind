Imports System
Imports System.Collections.Generic
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks

''' <summary>
''' Shared services used by both the desktop window and the LAN web server,
''' so a chat started on a phone shows up on the PC and vice versa.
''' </summary>
Public Module AppCore

    Public ReadOnly SettingsSvc As New SettingsService()
    Public Settings As AppSettings = SettingsSvc.Load()
    Public ReadOnly History As New HistoryService()
    Public ReadOnly Personas As New PersonaService()
    Public ReadOnly Library As New ModelLibrary(Function() Settings.EffectiveModelsFolder)
    Public ReadOnly Engine As New LlamaEngine(Function() Settings)
    Public ReadOnly Downloads As New DownloadManager(Library, Function() Settings)
    Public Lan As LanServer

    Public Event BackendChanged()

    Private _backend As IChatBackend

    Public Sub SaveSettings()
        SettingsSvc.Save(Settings)
    End Sub

    ''' <summary>The backend selected in Settings (rebuilt when settings change).</summary>
    Public ReadOnly Property Backend As IChatBackend
        Get
            If _backend Is Nothing Then _backend = CreateBackend()
            Return _backend
        End Get
    End Property

    Public Sub ResetBackend()
        _backend = Nothing
        RaiseEvent BackendChanged()
    End Sub

    Private Function CreateBackend() As IChatBackend
        Select Case Settings.Backend
            Case BackendKind.Ollama
                Return New OllamaService(Settings.OllamaUrl)
            Case BackendKind.OpenAiCompatible
                Dim svc As New OpenAiCompatService(Settings.OpenAiUrl, Settings.OpenAiKey) With {.Name = "Server"}
                svc.SendLlamaExtras = Not Settings.OpenAiUrl.Contains("api.openai.com")
                Return svc
            Case Else
                Return New BuiltInBackend()
        End Select
    End Function

    ''' <summary>
    ''' Builds the message list sent to the model: persona system prompt + recent history.
    ''' Old images are dropped (they are re-sent on every turn and cost a lot of context).
    ''' </summary>
    Public Function BuildPayload(session As ChatSession, persona As Persona) As List(Of ChatMessage)
        Dim payload As New List(Of ChatMessage)
        If persona IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(persona.SystemPrompt) Then
            payload.Add(New ChatMessage With {.Role = "system", .Content = persona.SystemPrompt})
        End If

        Dim limit = Math.Max(2, Settings.HistoryMessages)
        Dim history = session.Messages.Where(Function(m) m.Role = "user" OrElse m.Role = "assistant").ToList()
        If history.Count > limit Then history = history.Skip(history.Count - limit).ToList()
        ' Never start the window on an assistant turn.
        While history.Count > 1 AndAlso history(0).Role = "assistant"
            history.RemoveAt(0)
        End While

        Dim keepImagesFrom = history.Count - Math.Max(1, Settings.KeepImagesInLastMessages)
        For i = 0 To history.Count - 1
            Dim m = history(i)
            Dim copy As New ChatMessage With {.Role = m.Role, .Content = m.Content}
            If m.Images IsNot Nothing AndAlso m.Images.Count > 0 Then
                If i >= keepImagesFrom Then
                    copy.Images = m.Images.ToList()
                Else
                    copy.Content &= $" [{m.Images.Count} earlier image(s) omitted]"
                End If
            End If
            payload.Add(copy)
        Next
        Return payload
    End Function

    ''' <summary>
    ''' Adds passages from the user's documents and/or the web to the prompt (as a system message
    ''' right before the question) and returns the sources so the UI can list them.
    ''' </summary>
    Public Async Function AugmentAsync(payload As List(Of ChatMessage), useDocs As Boolean, useWeb As Boolean,
                                      progress As Action(Of String), ct As CancellationToken) As Task(Of List(Of SourceRef))
        Dim sources As New List(Of SourceRef)
        Dim lastUser = payload.LastOrDefault(Function(m) m.Role = "user")
        If lastUser Is Nothing OrElse (Not useDocs AndAlso Not useWeb) Then Return sources
        Dim query = Text.RegularExpressions.Regex.Replace(If(lastUser.Content, ""), "\[Attached document:[\s\S]*?\[End of document\]", "").Trim()
        If query.Length > 400 Then query = query.Substring(0, 400)
        If query.Length = 0 Then Return sources

        Dim sb As New Text.StringBuilder()
        If useDocs Then
            Try
                progress?.Invoke("📚 Searching your documents…")
                Dim hits = Await Knowledge.SearchAsync(query, 6, ct)
                If hits.Count > 0 Then
                    sb.AppendLine("Relevant excerpts from the user's own documents. Use them to answer and cite them like [1].")
                    For Each h In hits
                        sb.AppendLine($"{h.Label} {h.Title}{If(String.IsNullOrEmpty(h.Detail), "", ", " & h.Detail)}:").AppendLine(h.Text).AppendLine()
                    Next
                    sources.AddRange(hits)
                End If
            Catch ex As OperationCanceledException
                Throw
            Catch ex As Exception
                progress?.Invoke("Document search failed: " & ex.Message)
            End Try
        End If
        If useWeb Then
            Try
                progress?.Invoke("🌐 Searching the web…")
                Dim hits = Await WebSearch.SearchAndReadAsync(query, ct)
                If hits.Count > 0 Then
                    sb.AppendLine($"Web search results (today is {DateTime.Now:yyyy-MM-dd}). Prefer them for current facts and cite them like [W1].")
                    For Each h In hits
                        sb.AppendLine($"{h.Label} {h.Title} — {h.Location}").AppendLine(h.Text).AppendLine()
                    Next
                    sources.AddRange(hits)
                End If
            Catch ex As OperationCanceledException
                Throw
            Catch ex As Exception
                progress?.Invoke("Web search failed: " & ex.Message)
            End Try
        End If
        If sb.Length > 0 Then
            sb.AppendLine("If these sources don't contain the answer, say so and answer from general knowledge.")
            payload.Insert(payload.LastIndexOf(lastUser), New ChatMessage With {.Role = "system", .Content = sb.ToString()})
        End If
        Return sources
    End Function

    ''' <summary>Markdown list of sources shown under an answer.</summary>
    Public Function SourcesMarkdown(sources As List(Of SourceRef)) As String
        If sources Is Nothing OrElse sources.Count = 0 Then Return ""
        Dim sb As New Text.StringBuilder(vbLf & vbLf & "---" & vbLf & "**Sources**" & vbLf)
        For Each s In sources
            If s.Location.StartsWith("http") Then
                sb.AppendLine($"- {s.Label} [{s.Title.Replace("[", "(").Replace("]", ")")}]({s.Location})")
            Else
                sb.AppendLine($"- {s.Label} {s.Title}{If(String.IsNullOrEmpty(s.Detail), "", " (" & s.Detail & ")")}")
            End If
        Next
        Return sb.ToString()
    End Function

    Public Function FindPersona(name As String) As Persona
        If String.IsNullOrWhiteSpace(name) Then Return Nothing
        Return Personas.Load().FirstOrDefault(Function(p) String.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
    End Function

    Public Sub Shutdown()
        Try
            Lan?.StopAsync().Wait(3000)
        Catch
        End Try
        Try
            For Each j In Downloads.Jobs()
                j.Cts?.Cancel()
            Next
        Catch
        End Try
        Knowledge.StopEmbedder()
        Voice.StopSpeaking()
        Engine.Dispose()
    End Sub
End Module

''' <summary>Backend that runs models locally with the bundled llama.cpp engine.</summary>
Public Class BuiltInBackend
    Implements IChatBackend

    Public ReadOnly Property DisplayName As String Implements IChatBackend.DisplayName
        Get
            Return "Built-in engine (" & LlamaEngine.VariantLabel(AppCore.Engine.EffectiveVariant) & ")"
        End Get
    End Property

    Public Function IsOnlineAsync() As Task(Of Boolean) Implements IChatBackend.IsOnlineAsync
        Return Task.FromResult(AppCore.Engine.IsInstalled())
    End Function

    Public Function ListModelsAsync() As Task(Of List(Of String)) Implements IChatBackend.ListModelsAsync
        Return Task.FromResult(AppCore.Library.All().Select(Function(m) m.DisplayName).ToList())
    End Function

    Public Async Function StreamChatAsync(messages As List(Of ChatMessage), opts As ChatRequestOptions,
                                         onThinking As Action(Of String), onToken As Action(Of String),
                                         ct As CancellationToken) As Task(Of ChatResult) Implements IChatBackend.StreamChatAsync
        Dim model = AppCore.Library.FindByName(opts.Model)
        If model Is Nothing Then
            Throw New Exception("Model '" & opts.Model & "' is not downloaded. Open Models → Model Manager to get one.")
        End If

        Using Await AppCore.Engine.AcquireAsync(model, ct)

            ' Images to a model without a vision projector would just error out.
            If Not model.IsVision AndAlso messages.Any(Function(m) m.Images IsNot Nothing AndAlso m.Images.Count > 0) Then
                messages = messages.Select(Function(m) New ChatMessage With {
                    .Role = m.Role,
                    .Content = m.Content & If(m.Images?.Count > 0, " [image omitted: this model has no vision support]", "")
                }).ToList()
            End If

            Dim client As New OpenAiCompatService(AppCore.Engine.BaseUrl) With {.SendLlamaExtras = True}
            Return Await client.StreamChatAsync(messages, opts, onThinking, onToken, ct)
        End Using
    End Function
End Class
