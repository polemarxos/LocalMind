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
