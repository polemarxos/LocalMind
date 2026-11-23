Imports System
Imports System.Drawing
Imports System.IO
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms

''' <summary>Adds AI music or an AI voice-over to a video and saves it as MP4.</summary>
Public Class SoundtrackForm
    Inherits Form

    Private ReadOnly _video As String
    Private ReadOnly rbMusic, rbVoice As RadioButton
    Private ReadOnly txt As TextBox
    Private ReadOnly cmbModel, cmbVoice As ComboBox
    Private ReadOnly chkKeep As CheckBox
    Private ReadOnly lbl As Label
    Private ReadOnly btnGo As Button
    Private _cts As CancellationTokenSource
    <System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)>
    Public Property Result As String

    Public Sub New(video As String)
        _video = video
        Text = Localizer.T("Add music or a voice-over")
        ClientSize = New Size(620, 380)
        FormBorderStyle = FormBorderStyle.FixedDialog
        MaximizeBox = False
        StartPosition = FormStartPosition.CenterParent
        Theme.ApplyForm(Me)

        rbMusic = New RadioButton() With {.Text = Localizer.T("🎵 Music"), .Checked = True, .AutoSize = True, .Location = New Point(16, 14), .ForeColor = Theme.CText}
        rbVoice = New RadioButton() With {.Text = Localizer.T("🗣 Voice-over (narration)"), .AutoSize = True, .Location = New Point(140, 14), .ForeColor = Theme.CText}
        txt = New TextBox() With {.Location = New Point(16, 46), .Size = New Size(588, 120), .Multiline = True, .ScrollBars = ScrollBars.Vertical}
        Theme.StyleInput(txt)
        cmbModel = New ComboBox() With {.DropDownStyle = ComboBoxStyle.DropDownList, .Location = New Point(110, 180), .Width = 300}
        cmbVoice = New ComboBox() With {.DropDownStyle = ComboBoxStyle.DropDownList, .Location = New Point(110, 214), .Width = 300}
        Theme.StyleInput(cmbModel) : Theme.StyleInput(cmbVoice)
        Dim l1 = Theme.MakeLabel("Model") : l1.Location = New Point(16, 184)
        Dim l2 = Theme.MakeLabel("Voice") : l2.Location = New Point(16, 218)
        chkKeep = New CheckBox() With {.Text = Localizer.T("Keep the video's own sound (mixed in quieter)"), .AutoSize = True, .Location = New Point(16, 252), .ForeColor = Theme.CText}
        lbl = New Label() With {.Location = New Point(16, 282), .Size = New Size(588, 40), .ForeColor = Theme.CDim}
        btnGo = Theme.MakeButton("▶ Make MP4", primary:=True, width:=150)
        btnGo.Location = New Point(454, 330)
        Controls.AddRange({rbMusic, rbVoice, txt, l1, cmbModel, l2, cmbVoice, chkKeep, lbl, btnGo})

        AddHandler rbMusic.CheckedChanged, Sub() FillModels()
        AddHandler btnGo.Click, Async Sub() Await GoAsync()
        FillModels()
    End Sub

    Private Sub FillModels()
        Dim music = rbMusic.Checked
        txt.PlaceholderText = Localizer.T(If(music, "Music style, e.g. 'calm piano, cinematic, slow' — add [Lyrics] lines for vocals",
                                                    "What the narrator says…"))
        cmbModel.Items.Clear()
        For Each m In AudioStudio.Models().Where(Function(x) x.IsMusic = music)
            cmbModel.Items.Add(m)
        Next
        Dim installed = AudioStudio.Models().FirstOrDefault(Function(x) x.IsMusic = music AndAlso AudioStudio.IsInstalled(x))
        cmbModel.SelectedItem = If(installed, If(cmbModel.Items.Count > 0, cmbModel.Items(0), Nothing))
        cmbVoice.Items.Clear()
        cmbVoice.Items.Add(Localizer.T("(model's own voice)"))
        cmbVoice.Items.AddRange(VoiceLibrary.All().Cast(Of Object).ToArray())
        cmbVoice.SelectedIndex = 0
        cmbVoice.Enabled = Not music
    End Sub

    Private Async Function GoAsync() As Task
        If _cts IsNot Nothing Then
            _cts.Cancel()
            Return
        End If
        Dim m = TryCast(cmbModel.SelectedItem, AudioModel)
        If m Is Nothing OrElse String.IsNullOrWhiteSpace(txt.Text) Then Return
        _cts = New CancellationTokenSource()
        btnGo.Text = Localizer.T("■ Stop")
        Try
            Dim seconds = Await VideoTools.DurationAsync(_video, _cts.Token)
            Dim sv = TryCast(cmbVoice.SelectedItem, SavedVoice)
            Dim parts = txt.Text.Split({"[Lyrics]"}, 2, StringSplitOptions.None)
            Dim req As New AudioStudio.AudioRequest With {
                .Model = m, .Text = parts(0).Trim(), .Lyrics = If(parts.Length > 1, parts(1), Nothing),
                .Seconds = If(m.IsMusic, CInt(Math.Ceiling(Math.Max(5, seconds))), 0),
                .Language = If(Voice.IsGreek(txt.Text), "el", If(sv?.Language, "en")), .SpeakerFile = sv?.FullPath, .SpeakerText = sv?.Words}
            Dim wav = Await AudioStudio.GenerateAsync(req, Sub(p, t) BeginInvoke(Sub() lbl.Text = Localizer.T(t)), _cts.Token)
            lbl.Text = Localizer.T("Putting it under the video…")
            Dim outp = Path.Combine(Path.GetDirectoryName(_video), Path.GetFileNameWithoutExtension(_video) & If(m.IsMusic, "-music", "-voice") & ".mp4")
            Await VideoTools.AddAudioAsync(_video, wav, chkKeep.Checked, If(m.IsMusic, 0.9, 1.2), outp, Sub(t) BeginInvoke(Sub() lbl.Text = t), _cts.Token)
            Result = outp
            DialogResult = DialogResult.OK
            Close()
        Catch ex As OperationCanceledException
            lbl.Text = Localizer.T("Stopped")
        Catch ex As Exception
            lbl.Text = "⚠ " & ex.Message.Split({vbCr, vbLf}, StringSplitOptions.RemoveEmptyEntries).LastOrDefault()
        Finally
            _cts = Nothing
            If Not IsDisposed Then btnGo.Text = Localizer.T("▶ Make MP4")
        End Try
    End Function

    Protected Overrides Sub OnFormClosing(e As FormClosingEventArgs)
        _cts?.Cancel()
        MyBase.OnFormClosing(e)
    End Sub
End Class
