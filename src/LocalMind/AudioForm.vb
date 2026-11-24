Imports System
Imports System.Drawing
Imports System.IO
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports Microsoft.Web.WebView2.Core
Imports Microsoft.Web.WebView2.WinForms

''' <summary>Music & voice studio: speech in any voice (voice cloning from a short recording) and songs with vocals.</summary>
Public Class AudioForm
    Inherits Form

    Private ReadOnly _hw As HardwareInfo = HardwareInfo.Current()
    Private ReadOnly cmbMode, cmbLang, cmbSaved As ComboBox
    Private ReadOnly grid As DataGridView
    Private ReadOnly lblAbout, lblStatus, lblVoice As Label
    Private ReadOnly btnDownload, btnDelete, btnGenerate, btnRecord, btnPickVoice, btnClearVoice, btnTranscribe As Button
    Private ReadOnly prg As ProgressBar
    Private ReadOnly txtText, txtLyrics, txtVoiceWords As TextBox
    Private ReadOnly numSeconds, numSeed As NumericUpDown
    Private ReadOnly voicePanel, musicPanel As Panel
    Private ReadOnly player As WebView2
    Private ReadOnly lstOut As ListBox
    Private _voiceFile As String
    Private _cts As CancellationTokenSource
    Private _playerReady As Boolean

    Public Sub New()
        Text = "Music & voice studio"
        Size = New Size(1240, 820)
        MinimumSize = New Size(1000, 640)
        StartPosition = FormStartPosition.CenterParent
        Theme.ApplyForm(Me)

        '──── left: models ────
        Dim left As New Panel() With {.Dock = DockStyle.Left, .Width = 440, .Padding = New Padding(8), .BackColor = Theme.CRaised}
        cmbMode = New ComboBox() With {.Dock = DockStyle.Top, .DropDownStyle = ComboBoxStyle.DropDownList}
        cmbMode.Items.AddRange({Localizer.T("🗣 Speech & voice cloning"), Localizer.T("🎵 Songs (music with vocals)")})
        cmbMode.SelectedIndex = 0
        Theme.StyleInput(cmbMode)
        grid = New DataGridView() With {.Dock = DockStyle.Fill, .ReadOnly = True}
        Theme.StyleGrid(grid)
        grid.Columns.Add("name", "Model")
        grid.Columns.Add("size", "Size")
        grid.Columns.Add("fit", "On this PC")
        grid.Columns(0).AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
        grid.Columns(1).Width = 80
        grid.Columns(2).Width = 130
        Dim detail As New Panel() With {.Dock = DockStyle.Bottom, .Height = 180, .Padding = New Padding(0, 8, 0, 0)}
        lblAbout = New Label() With {.Dock = DockStyle.Top, .Height = 64, .ForeColor = Theme.CDim}
        Dim dlBar As New FlowLayoutPanel() With {.Dock = DockStyle.Top, .Height = 40}
        btnDownload = Theme.MakeButton("⬇ Download", primary:=True)
        btnDelete = Theme.MakeButton("🗑 Delete")
        dlBar.Controls.AddRange({btnDownload, btnDelete})
        prg = New ProgressBar() With {.Dock = DockStyle.Top, .Height = 14}
        lblStatus = New Label() With {.Dock = DockStyle.Top, .Height = 50, .ForeColor = Theme.CDim}
        detail.Controls.Add(lblStatus)
        detail.Controls.Add(prg)
        detail.Controls.Add(dlBar)
        detail.Controls.Add(lblAbout)
        left.Controls.Add(grid)
        left.Controls.Add(cmbMode)
        left.Controls.Add(detail)

        '──── right ────
        Dim right As New Panel() With {.Dock = DockStyle.Fill, .Padding = New Padding(10)}
        Dim top As New Panel() With {.Dock = DockStyle.Top, .Height = 360}

        ' Speech
        voicePanel = New Panel() With {.Dock = DockStyle.Fill}
        txtText = New TextBox() With {.Dock = DockStyle.Top, .Height = 120, .Multiline = True, .ScrollBars = ScrollBars.Vertical, .Font = New Font("Segoe UI", 10.5!),
                                      .PlaceholderText = "What should the voice say?"}
        Theme.StyleInput(txtText)
        Dim vRow1 As New FlowLayoutPanel() With {.Dock = DockStyle.Top, .Height = 40}
        cmbLang = New ComboBox() With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 90}
        Theme.StyleInput(cmbLang)
        btnRecord = Theme.MakeButton("🎤 Record my voice")
        btnPickVoice = Theme.MakeButton("📂 Voice file…")
        btnClearVoice = Theme.MakeButton("✕")
        lblVoice = Theme.MakeLabel("Voice: default")
        vRow1.Controls.AddRange({Theme.MakeLabel("Language"), cmbLang, btnRecord, btnPickVoice, btnClearVoice, lblVoice})
        Dim vRow0 As New FlowLayoutPanel() With {.Dock = DockStyle.Top, .Height = 40}
        cmbSaved = New ComboBox() With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 260}
        Theme.StyleInput(cmbSaved)
        Dim btnSaveVoice = Theme.MakeButton("💾 Save this voice…")
        Dim btnDelVoice = Theme.MakeButton("🗑")
        vRow0.Controls.AddRange({Theme.MakeLabel("My voices"), cmbSaved, btnSaveVoice, btnDelVoice})
        AddHandler cmbSaved.SelectedIndexChanged, Sub()
                                                      Dim sv = TryCast(cmbSaved.SelectedItem, SavedVoice)
                                                      If sv Is Nothing Then Return
                                                      SetVoice(sv.FullPath)
                                                      lblVoice.Text = Localizer.T("Voice") & ": " & sv.Name
                                                      txtVoiceWords.Text = sv.Words
                                                      If sv.Language IsNot Nothing AndAlso cmbLang.Items.Contains(sv.Language) Then cmbLang.SelectedItem = sv.Language
                                                  End Sub
        AddHandler btnSaveVoice.Click, Sub()
                                           If _voiceFile Is Nothing Then
                                               Localizer.Box(Me, "Record your voice or choose a voice file first.", "Save voice")
                                               Return
                                           End If
                                           Dim name = Microsoft.VisualBasic.Interaction.InputBox(Localizer.T("Name for this voice:"), Localizer.T("Save voice"), Path.GetFileNameWithoutExtension(_voiceFile))
                                           If String.IsNullOrWhiteSpace(name) Then Return
                                           VoiceLibrary.Add(name.Trim(), _voiceFile, txtVoiceWords.Text.Trim(), CStr(cmbLang.SelectedItem))
                                           FillSavedVoices(name.Trim())
                                       End Sub
        AddHandler btnDelVoice.Click, Sub()
                                          Dim sv = TryCast(cmbSaved.SelectedItem, SavedVoice)
                                          If sv Is Nothing Then Return
                                          If Localizer.Box(Me, $"Delete the voice '{sv.Name}'?", "Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) <> DialogResult.Yes Then Return
                                          VoiceLibrary.Delete(sv)
                                          SetVoice(Nothing)
                                          FillSavedVoices(Nothing)
                                      End Sub
        Dim vRow2 As New FlowLayoutPanel() With {.Dock = DockStyle.Top, .Height = 40}
        txtVoiceWords = New TextBox() With {.Width = 520, .PlaceholderText = "Words spoken in the recording (optional — makes the copy closer)"}
        Theme.StyleInput(txtVoiceWords)
        btnTranscribe = Theme.MakeButton("✍ Write them for me")
        vRow2.Controls.AddRange({txtVoiceWords, btnTranscribe})
        Dim vTip As New Label() With {.Dock = DockStyle.Top, .Height = 40, .ForeColor = Theme.CDim,
            .Text = Localizer.T("Tip: 5–20 seconds of clear speech without music works best. Only copy voices you have permission to use.")}
        voicePanel.Controls.Add(vTip)
        voicePanel.Controls.Add(vRow2)
        voicePanel.Controls.Add(vRow1)
        voicePanel.Controls.Add(vRow0)
        voicePanel.Controls.Add(txtText)

        ' Music
        musicPanel = New Panel() With {.Dock = DockStyle.Fill, .Visible = False}
        Dim style As New TextBox() With {.Dock = DockStyle.Top, .Height = 60, .Multiline = True, .Font = New Font("Segoe UI", 10.5!),
                                         .PlaceholderText = "Style: e.g. 'Greek summer pop, upbeat, bouzouki and synths, female vocals, 118 BPM'"}
        Theme.StyleInput(style)
        txtLyrics = New TextBox() With {.Dock = DockStyle.Fill, .Multiline = True, .ScrollBars = ScrollBars.Vertical, .Font = New Font("Segoe UI", 10.5!),
                                        .PlaceholderText = "[verse]" & vbCrLf & "Lyrics line…" & vbCrLf & "[chorus]" & vbCrLf & "Chorus line…" & vbCrLf & vbCrLf & "(leave empty for an instrumental)"}
        Theme.StyleInput(txtLyrics)
        Dim mRow As New FlowLayoutPanel() With {.Dock = DockStyle.Bottom, .Height = 38}
        numSeconds = New NumericUpDown() With {.Minimum = 0, .Maximum = 360, .Value = 0, .Increment = 15, .Width = 70}
        Theme.StyleInput(numSeconds)
        mRow.Controls.AddRange({Theme.MakeLabel("Max length (s, 0 = full song)"), numSeconds})
        musicPanel.Controls.Add(txtLyrics)
        musicPanel.Controls.Add(style)
        musicPanel.Controls.Add(mRow)
        musicPanel.Tag = style

        top.Controls.Add(voicePanel)
        top.Controls.Add(musicPanel)
        Dim genBar As New FlowLayoutPanel() With {.Dock = DockStyle.Top, .Height = 46}
        btnGenerate = Theme.MakeButton("▶ Generate", primary:=True, width:=160)
        btnGenerate.Font = Theme.BoldFont
        numSeed = New NumericUpDown() With {.Minimum = -1, .Maximum = Integer.MaxValue, .Value = -1, .Width = 100}
        Theme.StyleInput(numSeed)
        Dim btnFolder = Theme.MakeButton("📁 Open output folder")
        genBar.Controls.AddRange({btnGenerate, Theme.MakeLabel("Seed"), numSeed, btnFolder})

        player = New WebView2() With {.Dock = DockStyle.Top, .Height = 90, .DefaultBackgroundColor = Theme.CBack}
        lstOut = New ListBox() With {.Dock = DockStyle.Fill, .BackColor = Theme.CBack, .ForeColor = Theme.CText, .BorderStyle = BorderStyle.None}
        right.Controls.Add(lstOut)
        right.Controls.Add(player)
        right.Controls.Add(genBar)
        right.Controls.Add(top)
        Controls.Add(right)
        Controls.Add(left)

        AddHandler cmbMode.SelectedIndexChanged, Sub()
                                                    voicePanel.Visible = cmbMode.SelectedIndex = 0
                                                    musicPanel.Visible = cmbMode.SelectedIndex = 1
                                                    FillGrid()
                                                End Sub
        AddHandler grid.SelectionChanged, Sub() ShowModel()
        AddHandler btnDownload.Click, Async Sub() Await DownloadAsync()
        AddHandler btnDelete.Click, Sub()
                                        Dim m = SelectedModel()
                                        If m Is Nothing Then Return
                                        If Localizer.Box(Me, $"Delete {m.Name} from disk?", "Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) <> DialogResult.Yes Then Return
                                        AudioStudio.Delete(m)
                                        FillGrid()
                                    End Sub
        AddHandler btnGenerate.Click, Async Sub() Await GenerateAsync()
        AddHandler btnFolder.Click, Sub() ModelManagerForm.OpenFolder(MediaStudio.OutputFolder)
        AddHandler btnRecord.Click, Sub() ToggleRecord()
        AddHandler btnPickVoice.Click, Sub()
                                           Using d As New OpenFileDialog() With {.Filter = "Audio|*.wav;*.mp3;*.flac;*.ogg;*.m4a"}
                                               If d.ShowDialog(Me) = DialogResult.OK Then SetVoice(d.FileName)
                                           End Using
                                       End Sub
        AddHandler btnClearVoice.Click, Sub() SetVoice(Nothing)
        AddHandler btnTranscribe.Click, Async Sub() Await TranscribeAsync()
        AddHandler lstOut.SelectedIndexChanged, Sub() PlaySelected()
    End Sub

    Private Sub FillSavedVoices(selectName As String)
        cmbSaved.Items.Clear()
        cmbSaved.Items.Add(Localizer.T("(choose a saved voice)"))
        For Each sv In VoiceLibrary.All()
            cmbSaved.Items.Add(sv)
        Next
        Dim idx = cmbSaved.Items.Cast(Of Object).ToList().FindIndex(Function(o) TypeOf o Is SavedVoice AndAlso CType(o, SavedVoice).Name = selectName)
        cmbSaved.SelectedIndex = Math.Max(0, idx)
    End Sub

    Protected Overrides Async Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        FillSavedVoices(Nothing)
        FillGrid()
        LoadOutputs()
        Try
            Dim env = Await CoreWebView2Environment.CreateAsync(Nothing, AppPaths.WebViewFolder)
            Await player.EnsureCoreWebView2Async(env)
            player.CoreWebView2.SetVirtualHostNameToFolderMapping("outputs.localmind", MediaStudio.OutputFolder, CoreWebView2HostResourceAccessKind.Allow)
            _playerReady = True
        Catch
        End Try
    End Sub

    Private Function SelectedModel() As AudioModel
        Return If(grid.CurrentRow Is Nothing, Nothing, TryCast(grid.CurrentRow.Tag, AudioModel))
    End Function

    Private Sub FillGrid()
        Dim music = cmbMode.SelectedIndex = 1
        grid.Rows.Clear()
        For Each m In AudioStudio.Models().Where(Function(x) x.IsMusic = music)
            Dim fit = AudioStudio.Fit(m, _hw)
            Dim i = grid.Rows.Add(If(AudioStudio.IsInstalled(m), "✔ ", "") & m.Name, Downloader.FormatBytes(m.Size), ModelCatalog.FitText(fit, _hw.VramBytes = 0))
            grid.Rows(i).Tag = m
            grid.Rows(i).Cells(2).Style.ForeColor = ModelManagerForm.FitColor(fit)
        Next
        ' Preselect: Greek users get the multilingual voice model first.
        ShowModel()
    End Sub

    Private Sub ShowModel()
        Dim m = SelectedModel()
        If m Is Nothing Then Return
        lblAbout.Text = m.About & vbLf & AudioStudio.RequirementText(m, _hw)
        Dim installed = AudioStudio.IsInstalled(m)
        btnDownload.Enabled = Not installed AndAlso _cts Is Nothing
        btnDownload.Text = If(installed, Localizer.T("✔ Downloaded"), Localizer.T("⬇ Download") & $" ({Downloader.FormatBytes(AudioStudio.BytesToDownload(m))})")
        btnDelete.Enabled = installed AndAlso _cts Is Nothing
        cmbLang.Items.Clear()
        If m.Langs.Count > 1 Then
            cmbLang.Items.AddRange(m.Langs.Cast(Of Object).ToArray())
            Dim ui = If(Localizer.Language = "el", "el", "en")
            cmbLang.SelectedItem = If(m.Langs.Contains(ui), ui, m.Langs(0))
        End If
        cmbLang.Enabled = m.Langs.Count > 1
        btnRecord.Enabled = m.Clone : btnPickVoice.Enabled = m.Clone
        txtVoiceWords.Enabled = m.Transcript : btnTranscribe.Enabled = m.Transcript
        If m.Design Then txtText.PlaceholderText = Localizer.T("What should the voice say? Start with a voice description in brackets to design one, e.g. (a calm older man)Hello…")
    End Sub

    Private Async Function DownloadAsync() As Task
        Dim m = SelectedModel()
        If m Is Nothing Then Return
        If AudioStudio.Fit(m, _hw) = FitLevel.TooBig AndAlso Localizer.Box(Me,
                Localizer.T("This model probably won't run on this PC (not enough graphics memory + RAM):") & vbCrLf & vbCrLf &
                AudioStudio.RequirementText(m, _hw) & vbCrLf & vbCrLf & Localizer.T("Download anyway?"),
                "Memory", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) <> DialogResult.Yes Then Return
        _cts = New CancellationTokenSource()
        btnDownload.Enabled = False
        Try
            lblStatus.Text = Localizer.T("⬇ Added to the download list (Models → Downloads).")
            Await AudioStudio.DownloadAsync(m, Nothing, _cts.Token)
            lblStatus.Text = Localizer.T("Ready.")
        Catch ex As OperationCanceledException
            lblStatus.Text = Localizer.T("Download cancelled (it resumes next time).")
        Catch ex As Exception
            lblStatus.Text = "⚠ " & ex.Message
        Finally
            _cts = Nothing
            FillGrid()
        End Try
    End Function

    '──────────────── reference voice ────────────────
    Private Sub ToggleRecord()
        If Not Voice.IsRecording Then
            Try
                Voice.StartRecording()
                btnRecord.Text = Localizer.T("■ Stop recording")
                lblVoice.Text = Localizer.T("Recording… speak for 5–20 seconds")
            Catch ex As Exception
                Localizer.Box(Me, ex.Message, "Microphone")
            End Try
        Else
            Dim wav = Voice.StopRecording()
            btnRecord.Text = Localizer.T("🎤 Record my voice")
            If wav IsNot Nothing Then
                Dim keep = Path.Combine(MediaStudio.OutputFolder, $"voice-{DateTime.Now:yyyyMMdd-HHmmss}.wav")
                Try
                    File.Copy(wav, keep, True)
                    SetVoice(keep)
                Catch
                    SetVoice(wav)
                End Try
            End If
        End If
    End Sub

    Private Sub SetVoice(f As String)
        _voiceFile = f
        lblVoice.Text = If(f Is Nothing, Localizer.T("Voice: default"), Localizer.T("Voice") & ": " & Path.GetFileName(f))
        If f Is Nothing Then txtVoiceWords.Clear()
    End Sub

    Private Async Function TranscribeAsync() As Task
        If _voiceFile Is Nothing Then Return
        btnTranscribe.Enabled = False
        Try
            Dim wav = _voiceFile
            If Not wav.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) Then
                Dim ff = Await VideoTools.EnsureFfmpegAsync(Sub(t) Me.Post(Sub() lblStatus.Text = t), CancellationToken.None)
                wav = Path.Combine(Path.GetTempPath(), $"localmind-ref-{Guid.NewGuid():N}.wav")
                Using p = Diagnostics.Process.Start(New Diagnostics.ProcessStartInfo(ff, $"-y -loglevel error -i ""{_voiceFile}"" -ar 16000 -ac 1 ""{wav}""") With {.CreateNoWindow = True, .UseShellExecute = False})
                    Await p.WaitForExitAsync()
                End Using
            End If
            txtVoiceWords.Text = Await Voice.TranscribeAsync(wav, Sub(t) Me.Post(Sub() lblStatus.Text = Localizer.T(t)), CancellationToken.None)
            lblStatus.Text = Localizer.T("Done")
        Catch ex As Exception
            lblStatus.Text = "⚠ " & ex.Message
        Finally
            btnTranscribe.Enabled = True
        End Try
    End Function

    '──────────────── generate ────────────────
    Private Async Function GenerateAsync() As Task
        If _cts IsNot Nothing Then
            _cts.Cancel()
            AudioStudio.Cancel()
            Return
        End If
        Dim m = SelectedModel()
        If m Is Nothing Then Return
        Dim style = CType(musicPanel.Tag, TextBox)
        If m.IsMusic AndAlso String.IsNullOrWhiteSpace(style.Text) AndAlso String.IsNullOrWhiteSpace(txtLyrics.Text) Then
            style.Focus() : Return
        End If
        If Not m.IsMusic AndAlso String.IsNullOrWhiteSpace(txtText.Text) Then
            txtText.Focus() : Return
        End If
        Dim req As New AudioStudio.AudioRequest With {
            .Model = m, .Text = If(m.IsMusic, style.Text.Trim(), txtText.Text.Trim()), .Lyrics = txtLyrics.Text,
            .Language = CStr(cmbLang.SelectedItem), .SpeakerFile = _voiceFile, .SpeakerText = txtVoiceWords.Text,
            .Seed = CInt(Math.Min(Integer.MaxValue, numSeed.Value)), .Seconds = CInt(numSeconds.Value)}
        _cts = New CancellationTokenSource()
        btnGenerate.Text = Localizer.T("■ Stop")
        Dim sw = Diagnostics.Stopwatch.StartNew()
        Try
            Dim out = Await AudioStudio.GenerateAsync(req, Sub(p, t) Me.Post(Sub()
                                                                                    prg.Value = Math.Max(0, Math.Min(100, p))
                                                                                    lblStatus.Text = Localizer.T(t)
                                                                                End Sub), _cts.Token)
            lblStatus.Text = Localizer.T("Done") & $" · {sw.Elapsed.TotalSeconds:0}s"
            lstOut.Items.Insert(0, Path.GetFileName(out))
            lstOut.SelectedIndex = 0
        Catch ex As OperationCanceledException
            lblStatus.Text = Localizer.T("Stopped")
        Catch ex As Exception
            lblStatus.Text = "⚠ " & ex.Message.Split({vbCr, vbLf}, StringSplitOptions.RemoveEmptyEntries).LastOrDefault()
            Localizer.Box(Me, ex.Message, "Generation failed", MessageBoxButtons.OK, MessageBoxIcon.Warning)
        Finally
            _cts = Nothing
            btnGenerate.Text = Localizer.T("▶ Generate")
            FillGrid()
        End Try
    End Function

    Private Sub LoadOutputs()
        lstOut.Items.Clear()
        Dim ids = AudioStudio.Models().Select(Function(m) m.Id).ToList()
        For Each f In Directory.GetFiles(MediaStudio.OutputFolder, "*.wav").OrderByDescending(Function(x) File.GetCreationTime(x)).Take(100)
            lstOut.Items.Add(Path.GetFileName(f))
        Next
    End Sub

    Private Sub PlaySelected()
        If Not _playerReady OrElse lstOut.SelectedItem Is Nothing Then Return
        Dim url = "https://outputs.localmind/" & Uri.EscapeDataString(CStr(lstOut.SelectedItem))
        player.CoreWebView2.NavigateToString("<html><body style='margin:0;background:#1e1e1e;display:flex;align-items:center;justify-content:center;height:100vh'>" &
                                             $"<audio src='{url}' controls autoplay style='width:96%'></audio></body></html>")
    End Sub

    Protected Overrides Sub OnFormClosing(e As FormClosingEventArgs)
        _cts?.Cancel()
        AudioStudio.Cancel()
        If Voice.IsRecording Then Voice.StopRecording()
        MyBase.OnFormClosing(e)
    End Sub
End Class
