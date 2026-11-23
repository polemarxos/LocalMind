Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.IO
Imports System.Linq
Imports System.Threading
Imports System.Windows.Forms

''' <summary>
''' 🎬 Film editor: put your clips and pictures in order, add music and voice-overs, and export one film.
''' </summary>
Public Class FilmForm
    Inherits Form

    Private _film As FilmProject
    Private _file As String
    Private ReadOnly lst As New ListView() With {.Dock = DockStyle.Fill, .View = View.Details, .FullRowSelect = True, .MultiSelect = False, .HideSelection = False}
    Private ReadOnly thumbs As New ImageList() With {.ImageSize = New Size(64, 40), .ColorDepth = ColorDepth.Depth32Bit}
    Private ReadOnly numSeconds As New NumericUpDown() With {.Minimum = 0.5D, .Maximum = 60, .DecimalPlaces = 1, .Increment = 0.5D, .Width = 60}
    Private ReadOnly txtName As New TextBox() With {.Width = 260}
    Private ReadOnly cmbSize As New ComboBox() With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 200}
    Private ReadOnly chkFades As New CheckBox() With {.AutoSize = True}
    Private ReadOnly chkSound As New CheckBox() With {.AutoSize = True}
    Private ReadOnly lblMusic As Label = Theme.MakeLabel("", dim_:=True)
    Private ReadOnly numVolume As New NumericUpDown() With {.Minimum = 0, .Maximum = 200, .Increment = 5, .Width = 60}
    Private ReadOnly lstVo As New ListBox() With {.Height = 110, .Width = 330, .IntegralHeight = False}
    Private ReadOnly lblStatus As Label = Theme.MakeLabel("", dim_:=True)
    Private ReadOnly prg As New ProgressBar() With {.Width = 330, .Height = 12}
    Private ReadOnly btnExport As Button
    Private _cts As CancellationTokenSource
    Private _loading As Boolean

    ''' <summary>The exported film (to show in the studio's gallery).</summary>
    <System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)>
    Public Property Exported As String

    Private Shared ReadOnly Sizes As (Label As String, W As Integer, H As Integer)() = {
        ("Same as the first clip", 0, 0), ("1280×720 (HD, wide)", 1280, 720), ("1920×1080 (Full HD)", 1920, 1080),
        ("832×480 (small, wide)", 832, 480), ("720×1280 (phone, tall)", 720, 1280), ("1080×1920 (phone, tall, Full HD)", 1080, 1920),
        ("1080×1080 (square)", 1080, 1080)}

    Public Sub New()
        Text = Localizer.T("🎬 Film editor")
        Size = New Size(1100, 700)
        MinimumSize = New Size(900, 560)
        StartPosition = FormStartPosition.CenterParent
        Theme.ApplyForm(Me)
        _film = FilmProject.LoadCurrent()

        ' left: the clips in order
        Dim top As New FlowLayoutPanel() With {.Dock = DockStyle.Top, .AutoSize = True, .Padding = New Padding(8, 8, 8, 4)}
        Dim btnAdd = Theme.MakeButton(Localizer.T("＋ Add clips / pictures…"), primary:=True)
        Dim btnUp = Theme.MakeButton("▲"), btnDown = Theme.MakeButton("▼")
        Dim btnRemove = Theme.MakeButton(Localizer.T("🗑 Remove"))
        btnUp.Width = 40 : btnDown.Width = 40
        top.Controls.AddRange({btnAdd, btnUp, btnDown, btnRemove, Theme.MakeLabel(Localizer.T("Picture shown for (s)")), numSeconds})
        Theme.StyleInput(numSeconds)
        lst.Columns.Add("#", 40)
        lst.Columns.Add(Localizer.T("Clip"), 420)
        lst.Columns.Add(Localizer.T("Length"), 90)
        lst.SmallImageList = thumbs
        Theme.StyleListView(lst)
        lst.BorderStyle = BorderStyle.None
        lst.AllowDrop = True
        Dim left As New Panel() With {.Dock = DockStyle.Fill}
        Dim hint = Theme.MakeLabel(Localizer.T("Tip: in the studio, right-click a picture or video → 🎬 Add to film. Drop files here too."), dim_:=True)
        hint.Dock = DockStyle.Bottom : hint.Padding = New Padding(8)
        left.Controls.Add(lst)
        left.Controls.Add(top)
        left.Controls.Add(hint)

        ' right: film settings, sound, export
        Dim right As New FlowLayoutPanel() With {.Dock = DockStyle.Right, .Width = 370, .FlowDirection = FlowDirection.TopDown, .WrapContents = False,
                                                 .Padding = New Padding(14), .BackColor = Theme.CRaised, .AutoScroll = True}
        For Each c As Control In {txtName, cmbSize, numVolume}
            Theme.StyleInput(c)
        Next
        cmbSize.Items.AddRange(Sizes.Select(Function(x) CObj(Localizer.T(x.Label))).ToArray())
        chkFades.Text = Localizer.T("Fade in and out between clips") : chkFades.ForeColor = Theme.CText
        chkSound.Text = Localizer.T("Keep the clips' own sound") : chkSound.ForeColor = Theme.CText
        Dim btnMusic = Theme.MakeButton(Localizer.T("🎵 Choose music…"))
        Dim btnNoMusic = Theme.MakeButton(Localizer.T("No music"))
        Dim btnVo = Theme.MakeButton(Localizer.T("🎙 Add voice-over…"))
        Dim btnVoDel = Theme.MakeButton(Localizer.T("Remove"))
        Theme.StyleInput(lstVo)
        lstVo.BorderStyle = BorderStyle.FixedSingle
        btnExport = Theme.MakeButton(Localizer.T("🎬 Export film"), primary:=True, width:=200)
        Dim btnStop = Theme.MakeButton(Localizer.T("■ Stop"))
        Dim btnNew = Theme.MakeButton(Localizer.T("New film"))
        Dim btnOpen = Theme.MakeButton(Localizer.T("📂 Open…"))
        Dim btnSave = Theme.MakeButton(Localizer.T("💾 Save as…"))
        lblStatus.MaximumSize = New Size(330, 0)
        lblMusic.MaximumSize = New Size(330, 0)
        Dim row = Function(cs As Control()) As FlowLayoutPanel
                      Dim f As New FlowLayoutPanel() With {.AutoSize = True, .WrapContents = False, .Margin = New Padding(0, 0, 0, 4)}
                      f.Controls.AddRange(cs)
                      Return f
                  End Function
        right.Controls.AddRange({
            Theme.MakeLabel(Localizer.T("Name"), bold:=True), txtName,
            Theme.MakeLabel(Localizer.T("Size"), bold:=True), cmbSize, chkFades, chkSound,
            Theme.MakeLabel(Localizer.T("Music"), bold:=True), row({btnMusic, btnNoMusic}), lblMusic,
            row({Theme.MakeLabel(Localizer.T("Music volume %")), numVolume}),
            Theme.MakeLabel(Localizer.T("Voice-overs (start second · file)"), bold:=True), lstVo, row({btnVo, btnVoDel}),
            Theme.MakeLabel(" "), row({btnExport, btnStop}), prg, lblStatus,
            Theme.MakeLabel(" "), row({btnNew, btnOpen, btnSave})})

        Controls.Add(left)
        Controls.Add(right)

        AddHandler btnAdd.Click, Sub()
                                     Using d As New OpenFileDialog() With {.Multiselect = True, .InitialDirectory = MediaStudio.OutputFolder,
                                         .Filter = "Videos and pictures|*.mp4;*.webm;*.mov;*.mkv;*.avi;*.png;*.jpg;*.jpeg;*.webp;*.bmp"}
                                         If d.ShowDialog(Me) <> DialogResult.OK Then Return
                                         AddFiles(d.FileNames)
                                     End Using
                                 End Sub
        AddHandler lst.DragEnter, Sub(s, e) e.Effect = If(e.Data.GetDataPresent(DataFormats.FileDrop), DragDropEffects.Copy, DragDropEffects.None)
        AddHandler lst.DragDrop, Sub(s, e) AddFiles(CType(e.Data.GetData(DataFormats.FileDrop), String()))
        AddHandler btnUp.Click, Sub() MoveSelected(-1)
        AddHandler btnDown.Click, Sub() MoveSelected(1)
        AddHandler btnRemove.Click, Sub()
                                        Dim i = SelectedIndex()
                                        If i < 0 Then Return
                                        _film.Clips.RemoveAt(i)
                                        Changed()
                                        FillList(Math.Min(i, _film.Clips.Count - 1))
                                    End Sub
        AddHandler lst.SelectedIndexChanged, Sub()
                                                 Dim i = SelectedIndex()
                                                 _loading = True
                                                 numSeconds.Enabled = i >= 0 AndAlso _film.Clips(i).IsPicture
                                                 If numSeconds.Enabled Then numSeconds.Value = CDec(Math.Max(0.5, Math.Min(60, _film.Clips(i).Seconds)))
                                                 _loading = False
                                             End Sub
        AddHandler numSeconds.ValueChanged, Sub()
                                                Dim i = SelectedIndex()
                                                If _loading OrElse i < 0 Then Return
                                                _film.Clips(i).Seconds = CDbl(numSeconds.Value)
                                                lst.Items(i).SubItems(2).Text = $"{_film.Clips(i).Seconds:0.#} s"
                                                Changed()
                                            End Sub
        AddHandler txtName.TextChanged, Sub()
                                            If _loading Then Return
                                            _film.Name = txtName.Text.Trim() : Changed()
                                        End Sub
        AddHandler cmbSize.SelectedIndexChanged, Sub()
                                                     If _loading Then Return
                                                     Dim sz = Sizes(Math.Max(0, cmbSize.SelectedIndex))
                                                     _film.Width = sz.W : _film.Height = sz.H : Changed()
                                                 End Sub
        AddHandler chkFades.CheckedChanged, Sub()
                                                If _loading Then Return
                                                _film.Fades = chkFades.Checked : Changed()
                                            End Sub
        AddHandler chkSound.CheckedChanged, Sub()
                                                If _loading Then Return
                                                _film.KeepClipSound = chkSound.Checked : Changed()
                                            End Sub
        AddHandler numVolume.ValueChanged, Sub()
                                               If _loading Then Return
                                               _film.MusicVolume = CDbl(numVolume.Value) / 100 : Changed()
                                           End Sub
        AddHandler btnMusic.Click, Sub()
                                       Using d As New OpenFileDialog() With {.InitialDirectory = MediaStudio.OutputFolder, .Filter = "Audio|*.wav;*.mp3;*.m4a;*.ogg;*.flac"}
                                           If d.ShowDialog(Me) <> DialogResult.OK Then Return
                                           _film.Music = d.FileName : Changed() : ShowFilm()
                                       End Using
                                   End Sub
        AddHandler btnNoMusic.Click, Sub()
                                         _film.Music = "" : Changed() : ShowFilm()
                                     End Sub
        AddHandler btnVo.Click, Sub()
                                    Using d As New OpenFileDialog() With {.InitialDirectory = MediaStudio.OutputFolder, .Filter = "Audio|*.wav;*.mp3;*.m4a;*.ogg;*.flac"}
                                        If d.ShowDialog(Me) <> DialogResult.OK Then Return
                                        Dim start = AskSeconds()
                                        If start < 0 Then Return
                                        _film.VoiceOvers.Add(New FilmProject.VoiceOver With {.File = d.FileName, .Start = start})
                                        Changed() : ShowFilm()
                                    End Using
                                End Sub
        AddHandler btnVoDel.Click, Sub()
                                       If lstVo.SelectedIndex < 0 Then Return
                                       _film.VoiceOvers.RemoveAt(lstVo.SelectedIndex)
                                       Changed() : ShowFilm()
                                   End Sub
        AddHandler btnExport.Click, Async Sub()
                                        If _cts IsNot Nothing Then Return
                                        _cts = New CancellationTokenSource()
                                        btnExport.Enabled = False
                                        Try
                                            Exported = Await _film.ExportAsync(Sub(p, t) BeginInvoke(Sub()
                                                                                                        prg.Value = Math.Max(0, Math.Min(100, p))
                                                                                                        lblStatus.Text = t
                                                                                                    End Sub), _cts.Token)
                                            lblStatus.Text = Localizer.T("Film saved") & ": " & Path.GetFileName(Exported)
                                            ModelManagerForm.OpenUrl(Exported)
                                        Catch ex As OperationCanceledException
                                            lblStatus.Text = Localizer.T("Stopped")
                                        Catch ex As Exception
                                            lblStatus.Text = "⚠ " & ex.Message.Split({vbCr, vbLf}, StringSplitOptions.RemoveEmptyEntries).LastOrDefault()
                                        Finally
                                            _cts = Nothing
                                            btnExport.Enabled = True
                                        End Try
                                    End Sub
        AddHandler btnStop.Click, Sub() _cts?.Cancel()
        AddHandler btnNew.Click, Sub()
                                     If _film.Clips.Count > 0 AndAlso Localizer.Box(Me, "Start a new, empty film? (Save this one first if you want to keep it.)", "Film",
                                                                                   MessageBoxButtons.YesNo, MessageBoxIcon.Question) <> DialogResult.Yes Then Return
                                     _film = New FilmProject()
                                     _file = Nothing
                                     Changed() : ShowFilm()
                                 End Sub
        AddHandler btnOpen.Click, Sub()
                                      Using d As New OpenFileDialog() With {.InitialDirectory = FilmProject.Folder, .Filter = "LocalMind film|*.lmfilm"}
                                          If d.ShowDialog(Me) <> DialogResult.OK Then Return
                                          _film = FilmProject.Load(d.FileName)
                                          _file = d.FileName
                                          Changed() : ShowFilm()
                                      End Using
                                  End Sub
        AddHandler btnSave.Click, Sub()
                                      Using d As New SaveFileDialog() With {.InitialDirectory = FilmProject.Folder, .Filter = "LocalMind film|*.lmfilm", .FileName = _film.Name & ".lmfilm"}
                                          If d.ShowDialog(Me) <> DialogResult.OK Then Return
                                          _film.Save(d.FileName)
                                          _file = d.FileName
                                          lblStatus.Text = Localizer.T("Saved") & ": " & Path.GetFileName(d.FileName)
                                      End Using
                                  End Sub
        ShowFilm()
    End Sub

    ''' <summary>Asks at which second of the film a voice-over starts (-1 = cancelled).</summary>
    Private Function AskSeconds() As Double
        Using f As New Form() With {.Text = Localizer.T("Voice-over"), .Size = New Size(340, 150), .FormBorderStyle = FormBorderStyle.FixedDialog,
                                    .StartPosition = FormStartPosition.CenterParent, .MinimizeBox = False, .MaximizeBox = False}
            Theme.ApplyForm(f)
            Dim n As New NumericUpDown() With {.Minimum = 0, .Maximum = 36000, .DecimalPlaces = 1, .Width = 90, .Location = New Point(200, 18)}
            Theme.StyleInput(n)
            Dim l = Theme.MakeLabel(Localizer.T("Starts at second:"))
            l.Location = New Point(16, 20)
            Dim ok = Theme.MakeButton("OK", primary:=True)
            ok.Location = New Point(200, 60)
            ok.DialogResult = DialogResult.OK
            f.Controls.AddRange({l, n, ok})
            f.AcceptButton = ok
            Return If(f.ShowDialog(Me) = DialogResult.OK, CDbl(n.Value), -1)
        End Using
    End Function

    Private Function SelectedIndex() As Integer
        Return If(lst.SelectedIndices.Count > 0, lst.SelectedIndices(0), -1)
    End Function

    Private Sub AddFiles(files As IEnumerable(Of String))
        For Each f In files
            If File.Exists(f) Then _film.Clips.Add(New FilmProject.Clip With {.File = f})
        Next
        Changed()
        FillList(_film.Clips.Count - 1)
    End Sub

    Private Sub MoveSelected(delta As Integer)
        Dim i = SelectedIndex()
        Dim j = i + delta
        If i < 0 OrElse j < 0 OrElse j >= _film.Clips.Count Then Return
        Dim c = _film.Clips(i)
        _film.Clips.RemoveAt(i)
        _film.Clips.Insert(j, c)
        Changed()
        FillList(j)
    End Sub

    Private Sub Changed()
        Try
            _film.Save()
            If _file IsNot Nothing Then _film.Save(_file)
        Catch
        End Try
    End Sub

    Private Sub ShowFilm()
        _loading = True
        txtName.Text = _film.Name
        Dim si = Array.FindIndex(Sizes, Function(x) x.W = _film.Width AndAlso x.H = _film.Height)
        cmbSize.SelectedIndex = Math.Max(0, si)
        chkFades.Checked = _film.Fades
        chkSound.Checked = _film.KeepClipSound
        numVolume.Value = CDec(Math.Max(0, Math.Min(200, _film.MusicVolume * 100)))
        lblMusic.Text = If(String.IsNullOrEmpty(_film.Music), Localizer.T("(no music)"), "🎵 " & Path.GetFileName(_film.Music))
        lstVo.Items.Clear()
        For Each v In _film.VoiceOvers
            lstVo.Items.Add($"{v.Start:0.#} s · {Path.GetFileName(v.File)}")
        Next
        _loading = False
        FillList(-1)
    End Sub

    Private Sub FillList(selectIndex As Integer)
        lst.BeginUpdate()
        lst.Items.Clear()
        thumbs.Images.Clear()
        For i = 0 To _film.Clips.Count - 1
            Dim c = _film.Clips(i)
            Dim key = i.ToString()
            thumbs.Images.Add(key, Thumb(c))
            Dim it As New ListViewItem({(i + 1).ToString(), Path.GetFileName(c.File), If(c.IsPicture, $"{c.Seconds:0.#} s", "🎬")}, key)
            If Not File.Exists(c.File) Then it.ForeColor = Color.IndianRed
            lst.Items.Add(it)
        Next
        lst.EndUpdate()
        If selectIndex >= 0 AndAlso selectIndex < lst.Items.Count Then
            lst.Items(selectIndex).Selected = True
            lst.Items(selectIndex).EnsureVisible()
        End If
    End Sub

    Private Function Thumb(c As FilmProject.Clip) As Image
        Dim bmp As New Bitmap(64, 40)
        Using g = Graphics.FromImage(bmp)
            g.Clear(Theme.CRaised)
            If c.IsPicture AndAlso File.Exists(c.File) Then
                Try
                    Using img = ImageTools.Load(c.File)
                        Dim k = Math.Min(64 / img.Width, 40 / img.Height)
                        Dim w = CInt(img.Width * k), h = CInt(img.Height * k)
                        g.DrawImage(img, (64 - w) \ 2, (40 - h) \ 2, w, h)
                    End Using
                Catch
                End Try
            Else
                TextRenderer.DrawText(g, "🎬", New Font("Segoe UI Emoji", 14), New Rectangle(0, 0, 64, 40), Color.White,
                                      TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
            End If
        End Using
        Return bmp
    End Function

    Protected Overrides Sub OnFormClosing(e As FormClosingEventArgs)
        _cts?.Cancel()
        MyBase.OnFormClosing(e)
    End Sub
End Class
