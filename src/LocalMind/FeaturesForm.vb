Imports System
Imports System.Drawing
Imports System.Windows.Forms

''' <summary>First start: which abilities to show. Nothing is downloaded for abilities that stay off.</summary>
Public Class FeaturesForm
    Inherits Form

    Public Sub New()
        Text = "LocalMind"
        FormBorderStyle = FormBorderStyle.FixedDialog
        MaximizeBox = False : MinimizeBox = False
        StartPosition = FormStartPosition.CenterParent
        ClientSize = New Size(560, 330)
        Theme.ApplyForm(Me)

        Dim title As New Label() With {.Text = "What would you like LocalMind to do?", .Font = New Font("Segoe UI", 13.0!, FontStyle.Bold),
                                       .AutoSize = True, .Location = New Point(20, 18), .ForeColor = Theme.CText}
        Dim info As New Label() With {.Text = "You can change this any time in Tools → Settings → Chat. Nothing is downloaded for an ability that is off, " &
                                              "and even when it is on, models download only when you pick one.",
                                      .Location = New Point(22, 56), .Size = New Size(520, 44), .ForeColor = Theme.CDim}
        Dim chat As New CheckBox() With {.Text = "💬 Chat with AI (always on)", .Checked = True, .Enabled = False, .AutoSize = True, .Location = New Point(26, 112)}
        Dim img As New CheckBox() With {.Text = "🎨 Pictures && video: create, edit, enlarge pictures, make and edit videos", .Checked = AppCore.Settings.EnableImages,
                                        .AutoSize = True, .Location = New Point(26, 146)}
        Dim aud As New CheckBox() With {.Text = "🎵 Music && voice: songs with vocals, speech, voice cloning", .Checked = AppCore.Settings.EnableAudio,
                                        .AutoSize = True, .Location = New Point(26, 180)}
        Dim note As New Label() With {.Text = "Just want a chat assistant? Leave both unticked.", .Location = New Point(26, 220), .AutoSize = True, .ForeColor = Theme.CDim}
        Dim ok = Theme.MakeButton("Continue", primary:=True, width:=130)
        ok.Location = New Point(ClientSize.Width - 150, ClientSize.Height - 54)
        AddHandler ok.Click, Sub()
                                 AppCore.Settings.EnableImages = img.Checked
                                 AppCore.Settings.EnableAudio = aud.Checked
                                 AppCore.Settings.FeaturesAsked = True
                                 AppCore.SaveSettings()
                                 DialogResult = DialogResult.OK
                                 Close()
                             End Sub
        AcceptButton = ok
        Controls.AddRange({title, info, chat, img, aud, note, ok})
    End Sub
End Class
