Imports System
Imports System.Drawing
Imports System.Windows.Forms

''' <summary>Messages and questions in the app's own look (instead of the white Windows message box).</summary>
Public Class BoxForm
    Inherits Form

    Public Shared Function Ask(owner As IWin32Window, text As String, caption As String, buttons As MessageBoxButtons, icon As MessageBoxIcon) As DialogResult
        Dim ctl = TryCast(owner, Control)
        If ctl IsNot Nothing AndAlso Not ctl.IsDisposed AndAlso ctl.InvokeRequired Then
            Return CType(ctl.Invoke(Function() Ask(owner, text, caption, buttons, icon)), DialogResult)
        End If
        Using f As New BoxForm(text, caption, buttons, icon)
            Dim visibleOwner = ctl IsNot Nothing AndAlso Not ctl.IsDisposed AndAlso ctl.Visible AndAlso (TypeOf ctl IsNot Form OrElse CType(ctl, Form).Opacity > 0)
            If visibleOwner Then
                f.StartPosition = FormStartPosition.CenterParent
                Return f.ShowDialog(owner)
            End If
            f.StartPosition = FormStartPosition.CenterScreen
            f.TopMost = True
            Return f.ShowDialog()
        End Using
    End Function

    Private Sub New(text As String, caption As String, buttons As MessageBoxButtons, icon As MessageBoxIcon)
        Me.Text = If(String.IsNullOrEmpty(caption), "LocalMind", caption)
        FormBorderStyle = FormBorderStyle.FixedDialog
        MinimizeBox = False
        MaximizeBox = False
        ShowInTaskbar = False
        AutoSize = True
        AutoSizeMode = AutoSizeMode.GrowAndShrink
        Theme.ApplyForm(Me)
        BackColor = Theme.CPanel
        KeyPreview = True

        Dim glyph = "", color = Theme.CAccent
        Select Case icon
            Case MessageBoxIcon.Information : glyph = "ℹ"
            Case MessageBoxIcon.Warning : glyph = "⚠" : color = Theme.CWarn
            Case MessageBoxIcon.Error : glyph = "✖" : color = Theme.CBad
            Case MessageBoxIcon.Question : glyph = "?"
        End Select

        Dim body As New TableLayoutPanel() With {.AutoSize = True, .ColumnCount = 2, .Padding = New Padding(20, 20, 24, 10), .BackColor = Theme.CPanel}
        Dim ic As New Label() With {.Text = glyph, .AutoSize = False, .Size = New Size(If(glyph.Length > 0, 40, 0), 40), .ForeColor = color,
                                    .Font = New Font(Theme.UiFont.FontFamily, 20, FontStyle.Bold), .TextAlign = ContentAlignment.TopCenter,
                                    .Margin = New Padding(0, 0, If(glyph.Length > 0, 12, 0), 0)}
        Dim msg As New Label() With {.Text = text, .AutoSize = True, .MaximumSize = New Size(520, 0), .ForeColor = Theme.CText, .Font = Theme.UiFont,
                                     .Margin = New Padding(0, 8, 0, 8), .UseMnemonic = False}
        body.Controls.Add(ic, 0, 0)
        body.Controls.Add(msg, 1, 0)

        Dim bar As New FlowLayoutPanel() With {.AutoSize = True, .FlowDirection = FlowDirection.RightToLeft, .Dock = DockStyle.Bottom,
                                               .Padding = New Padding(12, 10, 12, 12), .BackColor = Theme.CRaised}
        Dim add = Function(caption2 As String, result As DialogResult, primary As Boolean) As Button
                      Dim b = Theme.MakeButton(Localizer.T(caption2), primary:=primary)
                      b.MinimumSize = New Size(96, 32)
                      b.DialogResult = result
                      b.UseMnemonic = False
                      bar.Controls.Add(b)
                      Return b
                  End Function
        Select Case buttons
            Case MessageBoxButtons.OKCancel
                CancelButton = add("Cancel", DialogResult.Cancel, False) : AcceptButton = add("OK", DialogResult.OK, True)
            Case MessageBoxButtons.YesNo
                CancelButton = add("No", DialogResult.No, False) : AcceptButton = add("Yes", DialogResult.Yes, True)
            Case MessageBoxButtons.YesNoCancel
                CancelButton = add("Cancel", DialogResult.Cancel, False) : add("No", DialogResult.No, False) : AcceptButton = add("Yes", DialogResult.Yes, True)
            Case MessageBoxButtons.RetryCancel
                CancelButton = add("Cancel", DialogResult.Cancel, False) : AcceptButton = add("Retry", DialogResult.Retry, True)
            Case MessageBoxButtons.AbortRetryIgnore
                add("Ignore", DialogResult.Ignore, False) : AcceptButton = add("Retry", DialogResult.Retry, True) : CancelButton = add("Abort", DialogResult.Abort, False)
            Case Else
                Dim ok = add("OK", DialogResult.OK, True)
                AcceptButton = ok : CancelButton = ok
        End Select

        Dim wrap As New TableLayoutPanel() With {.AutoSize = True, .ColumnCount = 1, .Dock = DockStyle.Fill, .BackColor = Theme.CPanel, .MinimumSize = New Size(380, 0)}
        wrap.Controls.Add(body, 0, 0)
        bar.Dock = DockStyle.Fill
        wrap.Controls.Add(bar, 0, 1)
        Controls.Add(wrap)
        ' Ctrl+C copies the message, like the Windows message box
        AddHandler KeyDown, Sub(s, e)
                                If e.Control AndAlso e.KeyCode = Keys.C Then
                                    Try : Clipboard.SetText(Me.Text & Environment.NewLine & text) : Catch : End Try
                                End If
                            End Sub
    End Sub
End Class
