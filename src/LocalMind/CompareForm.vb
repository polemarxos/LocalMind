Imports System
Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Drawing
Imports System.Linq
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms

''' <summary>Ask several models the same question and compare answers, speed and time side by side.</summary>
Public Class CompareForm
    Inherits Form

    Private ReadOnly txtPrompt As TextBox
    Private ReadOnly btnRun As Button
    Private ReadOnly chkThink As CheckBox
    Private ReadOnly numCols As NumericUpDown
    Private ReadOnly grid As TableLayoutPanel
    Private ReadOnly _cols As New List(Of Column)()
    Private _cts As CancellationTokenSource
    Private ReadOnly _timer As New System.Windows.Forms.Timer() With {.Interval = 100}

    Private Class Column
        Public Combo As ComboBox
        Public Stats As Label
        Public Output As RichTextBox
        Public ReadOnly Buffer As New StringBuilder()
        Public ReadOnly Lock As New Object()
    End Class

    Public Sub New(models As IEnumerable(Of String))
        Text = "Compare models"
        Size = New Size(1300, 800)
        MinimumSize = New Size(900, 500)
        StartPosition = FormStartPosition.CenterParent
        Theme.ApplyForm(Me)

        Dim top As New Panel() With {.Dock = DockStyle.Top, .Height = 120, .Padding = New Padding(10)}
        txtPrompt = New TextBox() With {.Dock = DockStyle.Fill, .Multiline = True, .ScrollBars = ScrollBars.Vertical, .Font = New Font("Segoe UI", 10.5!),
                                        .PlaceholderText = "Question to ask every model…"}
        Theme.StyleInput(txtPrompt)
        Dim side As New FlowLayoutPanel() With {.Dock = DockStyle.Right, .Width = 200, .FlowDirection = FlowDirection.TopDown}
        btnRun = Theme.MakeButton("▶ Compare", primary:=True, width:=180)
        chkThink = New CheckBox() With {.Text = "Allow thinking", .AutoSize = True, .Checked = False}
        numCols = New NumericUpDown() With {.Minimum = 2, .Maximum = 3, .Value = 2, .Width = 60}
        Theme.StyleInput(numCols)
        Dim colRow As New FlowLayoutPanel() With {.AutoSize = True, .Margin = New Padding(0)}
        colRow.Controls.AddRange({Theme.MakeLabel("Models:"), numCols})
        side.Controls.AddRange({btnRun, chkThink, colRow})
        top.Controls.Add(txtPrompt)
        top.Controls.Add(side)

        grid = New TableLayoutPanel() With {.Dock = DockStyle.Fill, .RowCount = 1, .Padding = New Padding(6)}
        Controls.Add(grid)
        Controls.Add(top)

        Dim modelList = models.ToArray()
        AddHandler numCols.ValueChanged, Sub() BuildColumns(modelList)
        AddHandler btnRun.Click, Async Sub() Await RunAsync()
        AddHandler _timer.Tick, Sub() FlushAll()
        BuildColumns(modelList)
    End Sub

    Private Sub BuildColumns(models As String())
        Dim previous = _cols.Select(Function(c) c.Combo.SelectedItem?.ToString()).ToList()
        grid.SuspendLayout()
        grid.Controls.Clear()
        grid.ColumnStyles.Clear()
        _cols.Clear()
        Dim n = CInt(numCols.Value)
        grid.ColumnCount = n
        For i = 0 To n - 1
            grid.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F / n))
            Dim col As New Column()
            Dim panel As New Panel() With {.Dock = DockStyle.Fill, .Padding = New Padding(4)}
            col.Combo = New ComboBox() With {.Dock = DockStyle.Top, .DropDownStyle = ComboBoxStyle.DropDownList}
            col.Combo.Items.AddRange(models)
            Theme.StyleInput(col.Combo)
            Dim want = If(i < previous.Count, previous(i), Nothing)
            If want IsNot Nothing AndAlso col.Combo.Items.Contains(want) Then
                col.Combo.SelectedItem = want
            ElseIf col.Combo.Items.Count > i Then
                col.Combo.SelectedIndex = i
            ElseIf col.Combo.Items.Count > 0 Then
                col.Combo.SelectedIndex = 0
            End If
            col.Stats = New Label() With {.Dock = DockStyle.Top, .Height = 24, .ForeColor = Theme.CDim, .TextAlign = ContentAlignment.MiddleLeft}
            col.Output = New RichTextBox() With {.Dock = DockStyle.Fill, .ReadOnly = True, .BorderStyle = BorderStyle.None,
                                                 .BackColor = Theme.CBack, .ForeColor = Theme.CText, .Font = New Font("Segoe UI", 10.0!)}
            panel.Controls.Add(col.Output)
            panel.Controls.Add(col.Stats)
            panel.Controls.Add(col.Combo)
            grid.Controls.Add(panel, i, 0)
            _cols.Add(col)
        Next
        grid.ResumeLayout()
    End Sub

    Private Async Function RunAsync() As Task
        If _cts IsNot Nothing Then
            _cts.Cancel()
            Return
        End If
        Dim q = txtPrompt.Text.Trim()
        If q.Length = 0 Then Return
        _cts = New CancellationTokenSource()
        btnRun.Text = "■ Stop"
        For Each c In _cols
            c.Output.Clear()
            c.Stats.Text = "waiting…"
            SyncLock c.Lock
                c.Buffer.Clear()
            End SyncLock
        Next
        _timer.Start()
        Try
            Dim jobs = _cols.Select(Function(c) Function() AskAsync(c, q, _cts.Token)).ToList()
            If AppCore.Settings.Backend = BackendKind.BuiltIn Then
                ' One GPU: models run one after another (each is loaded in turn).
                For Each j In jobs
                    Await j()
                Next
            Else
                Await Task.WhenAll(jobs.Select(Function(j) j()))
            End If
        Finally
            _timer.Stop()
            FlushAll()
            _cts = Nothing
            btnRun.Text = "▶ Compare"
        End Try
    End Function

    Private Async Function AskAsync(c As Column, q As String, ct As CancellationToken) As Task
        Dim model = c.Combo.SelectedItem?.ToString()
        If model Is Nothing Then Return
        c.Stats.Text = "loading / thinking…"
        Dim sw = Stopwatch.StartNew()
        Dim firstToken As Double = 0
        Try
            Dim r = Await Task.Run(Function() AppCore.Backend.StreamChatAsync(
                New List(Of ChatMessage) From {New ChatMessage With {.Role = "user", .Content = q}},
                New ChatRequestOptions With {.Model = model, .Temperature = AppCore.Settings.DefaultTemperature,
                                             .TopP = AppCore.Settings.TopP, .EnableThinking = chkThink.Checked},
                Sub(t) Return,
                Sub(t)
                    If firstToken = 0 Then firstToken = sw.Elapsed.TotalSeconds
                    SyncLock c.Lock
                        c.Buffer.Append(t)
                    End SyncLock
                End Sub, ct))
            sw.Stop()
            Dim tps = If(r.EngineTokensPerSecond > 0, r.EngineTokensPerSecond, r.Tokens / Math.Max(0.1, sw.Elapsed.TotalSeconds))
            c.Stats.Text = $"{tps:0.0} tok/s · {r.Tokens} tokens · {sw.Elapsed.TotalSeconds:0.0}s total · first word after {firstToken:0.0}s"
        Catch ex As OperationCanceledException
            c.Stats.Text = "stopped"
        Catch ex As Exception
            c.Stats.Text = "⚠ " & ex.Message
        End Try
    End Function

    Private Sub FlushAll()
        For Each c In _cols
            Dim chunk As String
            SyncLock c.Lock
                chunk = c.Buffer.ToString()
                c.Buffer.Clear()
            End SyncLock
            If chunk.Length > 0 Then
                c.Output.AppendText(chunk)
                If c.Stats.Text.StartsWith("loading") Then c.Stats.Text = "writing…"
            End If
        Next
    End Sub

    Protected Overrides Sub OnFormClosing(e As FormClosingEventArgs)
        _cts?.Cancel()
        _timer.Stop()
        MyBase.OnFormClosing(e)
    End Sub
End Class
