Imports System
Imports System.Drawing
Imports System.Windows.Forms

''' <summary>The small "Closing LocalMind…" window: a bar and what is being done, while models and jobs stop.</summary>
Public Class ClosingForm
    Inherits Form

    Private ReadOnly lbl As Label
    Private ReadOnly bar As ProgressBar

    Public Sub New()
        FormBorderStyle = FormBorderStyle.None
        StartPosition = FormStartPosition.CenterScreen
        ShowInTaskbar = True
        TopMost = True
        Size = New Size(420, 110)
        Text = "LocalMind"
        Theme.ApplyForm(Me)
        BackColor = Theme.CPanel
        Padding = New Padding(18, 16, 18, 16)
        Dim title = Theme.MakeLabel(Localizer.T("Closing LocalMind…"), bold:=True)
        title.Dock = DockStyle.Top
        title.Height = 26
        bar = New ProgressBar() With {.Dock = DockStyle.Top, .Height = 8, .Style = ProgressBarStyle.Continuous, .Maximum = 100}
        lbl = Theme.MakeLabel("", dim_:=True)
        lbl.Dock = DockStyle.Top
        lbl.Height = 34
        lbl.Padding = New Padding(0, 8, 0, 0)
        Controls.Add(lbl)
        Controls.Add(bar)
        Controls.Add(title)
        AddHandler Paint, Sub(s, e) ControlPaint.DrawBorder(e.Graphics, ClientRectangle, Theme.CBorder, ButtonBorderStyle.Solid)
    End Sub

    ''' <summary>Shows a step: what is being done and how far closing is.</summary>
    Public Sub [Step](text As String, percent As Integer)
        If IsDisposed Then Return
        lbl.Text = Localizer.T(text)
        bar.Value = Math.Max(0, Math.Min(100, percent))
        Refresh()
    End Sub
End Class
