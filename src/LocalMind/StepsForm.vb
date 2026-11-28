Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.Linq
Imports System.Windows.Forms

''' <summary>
''' The steps a message asks for ("🖼 Picture → 🎬 Video → 🎵 Song"), shown before they run. Untick a step to skip it;
''' it starts by itself after a few seconds when nothing is touched.
''' </summary>
Public Class StepsForm
    Inherits Form

    Private ReadOnly _tools As List(Of String)
    Private ReadOnly _checks As New List(Of CheckBox)()
    Private ReadOnly _btnStart As Button
    Private ReadOnly _chkDontAsk As CheckBox
    Private ReadOnly _timer As New Timer() With {.Interval = 1000}
    Private _left As Integer = 8

    ''' <summary>The steps to run (in order), when Start was chosen.</summary>
    Public ReadOnly Property Chosen As List(Of String)
        Get
            Return _tools.Where(Function(t, i) _checks(i).Checked).ToList()
        End Get
    End Property

    Public ReadOnly Property DontAskAgain As Boolean
        Get
            Return _chkDontAsk.Checked
        End Get
    End Property

    Public Sub New(tools As List(Of String))
        _tools = tools
        Text = Localizer.T("Steps")
        FormBorderStyle = FormBorderStyle.FixedDialog
        MaximizeBox = False
        MinimizeBox = False
        ShowInTaskbar = False
        StartPosition = FormStartPosition.CenterParent
        AutoSize = True
        AutoSizeMode = AutoSizeMode.GrowAndShrink
        Theme.ApplyForm(Me)
        Padding = New Padding(16)

        Dim flow As New FlowLayoutPanel() With {.FlowDirection = FlowDirection.TopDown, .AutoSize = True, .WrapContents = False, .Dock = DockStyle.Fill}
        Dim title = Theme.MakeLabel(Localizer.T("Your message asks for these steps:"), bold:=True)
        title.AutoSize = True
        title.Margin = New Padding(0, 0, 0, 8)
        flow.Controls.Add(title)
        For i = 0 To tools.Count - 1
            Dim c As New CheckBox() With {.Text = $"{i + 1}.  {ChatAgent.StepLabel(tools(i))}", .Checked = True, .AutoSize = True,
                                          .Margin = New Padding(6, 2, 0, 2), .ForeColor = Theme.CText}
            AddHandler c.CheckedChanged, Sub() StopCountdown()
            _checks.Add(c)
            flow.Controls.Add(c)
        Next
        Dim hint = Theme.MakeLabel(Localizer.T("Untick a step to skip it. A step that needs the one before it (a video from the picture) uses the last picture in the chat instead."), dim_:=True)
        hint.AutoSize = True
        hint.MaximumSize = New Size(420, 0)
        hint.Margin = New Padding(0, 8, 0, 8)
        flow.Controls.Add(hint)
        _chkDontAsk = New CheckBox() With {.Text = Localizer.T("Don't show this again (Settings → AI tools)"), .AutoSize = True, .ForeColor = Theme.CDim}
        flow.Controls.Add(_chkDontAsk)

        Dim buttons As New FlowLayoutPanel() With {.FlowDirection = FlowDirection.RightToLeft, .AutoSize = True, .Dock = DockStyle.Bottom,
                                                   .Margin = New Padding(0, 10, 0, 0), .MinimumSize = New Size(420, 0)}
        _btnStart = Theme.MakeButton("", primary:=True, width:=130)
        _btnStart.DialogResult = DialogResult.OK
        Dim btnCancel = Theme.MakeButton(Localizer.T("Cancel"), width:=100)
        btnCancel.DialogResult = DialogResult.Cancel
        buttons.Controls.Add(_btnStart)
        buttons.Controls.Add(btnCancel)
        flow.Controls.Add(buttons)
        Controls.Add(flow)
        AcceptButton = _btnStart
        CancelButton = btnCancel
        ShowLeft()

        AddHandler _timer.Tick, Sub()
                                    _left -= 1
                                    If _left <= 0 Then
                                        _timer.Stop()
                                        DialogResult = DialogResult.OK
                                    Else
                                        ShowLeft()
                                    End If
                                End Sub
        AddHandler Shown, Sub() _timer.Start()
        AddHandler FormClosed, Sub() _timer.Dispose()
    End Sub

    Private Sub ShowLeft()
        _btnStart.Text = Localizer.T("Start") & If(_timer.Enabled OrElse _left = 8, $" ({_left})", "")
    End Sub

    Private Sub StopCountdown()
        If _timer.Enabled Then
            _timer.Stop()
            _btnStart.Text = Localizer.T("Start")
        End If
        ' (nothing ticked: there is nothing to start)
        _btnStart.Enabled = _checks.Any(Function(c) c.Checked)
    End Sub
End Class
