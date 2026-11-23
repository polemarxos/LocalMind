Imports System
Imports System.Drawing
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms

''' <summary>
''' Small always-on-top window opened by the global hotkey: type a question, get the answer
''' right there. Every quick chat is saved to history; "Open in LocalMind" continues it there.
''' </summary>
Public Class QuickAskForm
    Inherits Form

    Private ReadOnly txtQ As TextBox
    Private ReadOnly txtA As RichTextBox
    Private ReadOnly lbl As Label
    Private ReadOnly btnOpen As Button
    Private ReadOnly btnCopy As Button
    Private _cts As CancellationTokenSource
    Private ReadOnly _buf As New StringBuilder()
    Private ReadOnly _bufLock As New Object()
    Private ReadOnly _timer As New System.Windows.Forms.Timer() With {.Interval = 80}
    Private _session As ChatSession

    ''' <summary>Raised when the user wants to continue this chat in the main window.</summary>
    Public Event OpenInMain(sessionId As String)

    Public Sub New()
        Text = "LocalMind — Quick ask"
        FormBorderStyle = FormBorderStyle.SizableToolWindow
        StartPosition = FormStartPosition.Manual
        TopMost = True
        ShowInTaskbar = False
        KeyPreview = True
        Size = New Size(680, 440)
        MinimumSize = New Size(420, 220)
        Theme.ApplyForm(Me)
        Padding = New Padding(10)

        txtQ = New TextBox() With {.Dock = DockStyle.Top, .Font = New Font("Segoe UI", 12.0!), .PlaceholderText = "Ask anything…  (Enter = ask, Esc = close)"}
        Theme.StyleInput(txtQ)
        txtA = New RichTextBox() With {.Dock = DockStyle.Fill, .ReadOnly = True, .BorderStyle = BorderStyle.None,
                                       .BackColor = Theme.CBack, .ForeColor = Theme.CText, .Font = New Font("Segoe UI", 10.5!)}
        Dim bottom As New FlowLayoutPanel() With {.Dock = DockStyle.Bottom, .Height = 40, .FlowDirection = FlowDirection.LeftToRight}
        btnOpen = Theme.MakeButton("Open in LocalMind", primary:=True)
        btnCopy = Theme.MakeButton("Copy answer")
        lbl = Theme.MakeLabel("", dim_:=True)
        bottom.Controls.AddRange({btnOpen, btnCopy, lbl})
        Dim spacer As New Panel() With {.Dock = DockStyle.Top, .Height = 8}

        Controls.Add(txtA)
        Controls.Add(spacer)
        Controls.Add(txtQ)
        Controls.Add(bottom)

        AddHandler txtQ.KeyDown, Async Sub(s, e)
                                     If e.KeyCode = Keys.Enter Then
                                         e.SuppressKeyPress = True
                                         Await AskAsync()
                                     End If
                                 End Sub
        AddHandler btnOpen.Click, Sub()
                                      If _session IsNot Nothing AndAlso _session.Messages.Count > 0 Then RaiseEvent OpenInMain(_session.Id)
                                      Close()
                                  End Sub
        AddHandler btnCopy.Click, Sub()
                                      Try
                                          If txtA.TextLength > 0 Then Clipboard.SetText(txtA.Text)
                                      Catch
                                      End Try
                                  End Sub
        AddHandler _timer.Tick, AddressOf Flush
        lbl.Text = If(AppCore.Settings.LastModel, "no model selected")
    End Sub

    Protected Overrides Sub OnShown(e As EventArgs)
        MyBase.OnShown(e)
        Dim wa = Screen.FromPoint(Cursor.Position).WorkingArea
        Location = New Point(wa.Left + (wa.Width - Width) \ 2, wa.Top + wa.Height \ 5)
        Activate()
        txtQ.Focus()
    End Sub

    Protected Overrides Sub OnKeyDown(e As KeyEventArgs)
        If e.KeyCode = Keys.Escape Then
            Close()
            Return
        End If
        MyBase.OnKeyDown(e)
    End Sub

    Private Async Function AskAsync() As Task
        Dim q = txtQ.Text.Trim()
        If q.Length = 0 OrElse _cts IsNot Nothing Then Return
        Dim model = AppCore.Settings.LastModel
        If String.IsNullOrEmpty(model) Then
            txtA.Text = "No model selected — open LocalMind and pick or download a model first."
            Return
        End If

        If _session Is Nothing Then _session = New ChatSession With {.Title = If(q.Length > 40, q.Substring(0, 40) & "…", q), .Model = model}
        _session.Messages.Add(New ChatMessage With {.Role = "user", .Content = q})
        If txtA.TextLength > 0 Then txtA.AppendText(vbLf & vbLf)
        txtA.SelectionFont = New Font(txtA.Font, FontStyle.Bold)
        txtA.SelectionColor = Color.FromArgb(120, 180, 255)
        txtA.AppendText("› " & q & vbLf)
        txtA.SelectionFont = txtA.Font
        txtA.SelectionColor = Theme.CText
        txtQ.Clear()
        lbl.Text = "Thinking…"
        _cts = New CancellationTokenSource()
        _timer.Start()

        Dim persona = AppCore.FindPersona(AppCore.Settings.LastPersona)
        Dim payload = AppCore.BuildPayload(_session, persona)
        Dim opts As New ChatRequestOptions With {
            .Model = model, .Temperature = If(persona?.Temperature, AppCore.Settings.DefaultTemperature),
            .TopP = AppCore.Settings.TopP, .MaxTokens = AppCore.Settings.MaxTokens, .EnableThinking = False}
        Dim backend = AppCore.Backend
        Dim answer As String
        Try
            Dim r = Await Task.Run(Function() backend.StreamChatAsync(payload, opts, Sub(t) Return,
                Sub(t)
                    SyncLock _bufLock
                        _buf.Append(t)
                    End SyncLock
                End Sub, _cts.Token))
            answer = r.Content
            lbl.Text = If(r.EngineTokensPerSecond > 0, $"{model} · {r.EngineTokensPerSecond:0.0} tok/s", model)
        Catch ex As OperationCanceledException
            answer = "[stopped]"
        Catch ex As Exception
            answer = "⚠ " & ex.Message
            lbl.Text = ""
        Finally
            _timer.Stop()
            Flush(Nothing, EventArgs.Empty)
            _cts = Nothing
        End Try
        _session.Messages.Add(New ChatMessage With {.Role = "assistant", .Content = answer, .Model = model})
        _session.UpdatedAt = DateTime.Now
        AppCore.History.Save(_session)
    End Function

    Private Sub Flush(sender As Object, e As EventArgs)
        Dim chunk As String
        SyncLock _bufLock
            chunk = _buf.ToString()
            _buf.Clear()
        End SyncLock
        If chunk.Length = 0 Then Return
        If lbl.Text = "Thinking…" Then lbl.Text = "Writing…"
        txtA.AppendText(chunk)
        txtA.SelectionStart = txtA.TextLength
        txtA.ScrollToCaret()
    End Sub

    Protected Overrides Sub OnFormClosing(e As FormClosingEventArgs)
        _cts?.Cancel()
        _timer.Stop()
        MyBase.OnFormClosing(e)
    End Sub
End Class
