Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.IO
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports Microsoft.Web.WebView2.Core
Imports Microsoft.Web.WebView2.WinForms

''' <summary>
''' Image & video studio: choose what to do (create, edit, paint an area, combine, restyle, enlarge, video…),
''' pick a model that fits the GPU, download it, describe, generate.
''' </summary>
Public Class StudioForm
    Inherits Form

    ''' <summary>Task id, menu label, and whether it works on video models.</summary>
    Private Shared ReadOnly AllTasks As (Id As String, Label As String, Video As Boolean)() = {
        ("create", "✨ Create a picture", False),
        ("edit", "✏ Edit a picture (describe the change)", False),
        ("inpaint", "🖌 Change part of a picture (paint the area)", False),
        ("combine", "🧩 Combine pictures", False),
        ("restyle", "🎨 Restyle a picture", False),
        ("pose", "🧍 Copy a pose (from picture 2)", False),
        ("layout", "🏠 Copy layout / depth of a picture", False),
        ("outline", "✏ Sketch → picture (keep the outlines)", False),
        ("restore", "🩹 Restore an old photo / fix faces", False),
        ("remove-bg", "✂ Remove the background", False),
        ("upscale", "🔍 Enlarge a picture (4×)", False),
        ("create", "🎬 Create a video", True),
        ("animate", "🎞 Animate a picture", True),
        ("video-edit", "✂ Edit / restyle a video", True),
        ("reference", "🧩 Video from reference pictures (people, places, things)", True),
        ("storyboard", "📽 Storyboard — several shots, one video", True),
        ("first-last", "🎯 Video between two pictures (first → last)", True),
        ("talk", "🗣 Talking character (picture + voice)", True),
        ("extend", "⏩ Extend a video", True),
        ("enhance", "✨ Enhance a video (sharper, smoother)", True)}
    ' Only the tasks a listed (or downloaded) model can do; "enhance" and background removal need none of the listed kinds.
    Private Shared ReadOnly TaskList As (Id As String, Label As String, Video As Boolean)() =
        AllTasks.Where(Function(t) t.Id = "enhance" OrElse MediaStudio.Recipes().Any(Function(r) Suits(r, t))).ToArray()

    Private ReadOnly _hw As HardwareInfo = HardwareInfo.Current()
    Private ReadOnly cmbShow, cmbTask, cmbVariant, cmbSize As ComboBox
    Private ReadOnly grid As DataGridView
    Private ReadOnly lblAbout, lblStatus, lblStrength, lblVideo, lblFrames, lblNeed As Label
    Private ReadOnly btnDefault As Button
    Private ReadOnly _tips As New ToolTip()
    Private ReadOnly btnDownload, btnDelete, btnGenerate, btnVideo, btnMask, btnLoras As Button
    Private ReadOnly chkFast As CheckBox
    Private ReadOnly prg As ProgressBar
    Private ReadOnly txtPrompt, txtNeg As TextBox
    Private ReadOnly numSteps, numCfg, numSeed, numFrames, numStrength, numCount As NumericUpDown
    Private ReadOnly cmbStyle As ComboBox
    Private ReadOnly btnStop As Button
    Private ReadOnly lblQueue As Label
    ''' <summary>Jobs waiting. Before (optional) makes the start picture first, e.g. a scene with the cast for a video.</summary>
    Private ReadOnly _queue As New Queue(Of (Label As String, Work As Func(Of Action(Of Integer, String), CancellationToken, Task(Of List(Of String)))))
    ''' <summary>Characters and locations picked for the next jobs.</summary>
    Private ReadOnly _cast As New List(Of CastLibrary.CastMember)
    Private btnCast As Button
    Private lblCast As Label
    ' talking character: the words and the voice (or a recording); enhance: what to improve
    Private txtSay As TextBox
    Private cmbVoice As ComboBox
    Private chkSharper, chkSmooth As CheckBox
    Private _audioPath As String
    Private lblSize, lblSteps, lblCfg, lblSeed, lblPictures, lblStyle As Label
    Private btnImprove, btnDescribe As Button
    Private ReadOnly _voiceNames As New List(Of String)
    Private _fillingVoices As Boolean
    Private _running As Boolean

    ''' <summary>Style presets: words added to the end of the prompt.</summary>
    Private Shared ReadOnly Styles As (Label As String, Words As String)() = {
        ("(no style)", ""), ("📷 Photo", ", professional photograph, natural light, sharp focus, high detail"),
        ("🎬 Cinematic", ", cinematic film still, dramatic lighting, shallow depth of field, color graded"),
        ("🌸 Anime", ", anime style, clean line art, vibrant colors"), ("🖌 Watercolor", ", watercolor painting, soft washes, paper texture"),
        ("🖼 Oil painting", ", oil painting, visible brush strokes, rich colors"), ("🧊 3D render", ", 3D render, octane, soft global illumination, highly detailed"),
        ("👾 Pixel art", ", pixel art, 16-bit, crisp pixels"), ("💥 Comic", ", comic book style, bold ink lines, halftone shading"),
        ("✏ Pencil sketch", ", pencil sketch, graphite, cross-hatching"), ("🐉 Fantasy art", ", epic fantasy illustration, detailed, dramatic"),
        ("📦 Product shot", ", studio product photo, clean background, softbox lighting"), ("🏛 Architecture", ", architectural photography, wide angle, golden hour")}
    Private ReadOnly slots(2) As PictureBox
    Private ReadOnly slotPanel As FlowLayoutPanel
    Private ReadOnly preview As WebView2
    Private ReadOnly gallery As ListView
    Private ReadOnly thumbs As New ImageList() With {.ImageSize = New Size(96, 96), .ColorDepth = ColorDepth.Depth32Bit}
    Private ReadOnly _inputs(2) As String
    Private _videoPath, _maskPath As String
    Private _cts As CancellationTokenSource
    Private txtSearch As TextBox
    Private _galleryButtons As Button()
    Private chkFavs, chkIdle, chkSleep As CheckBox

    <Runtime.InteropServices.StructLayout(Runtime.InteropServices.LayoutKind.Sequential)>
    Private Structure LASTINPUTINFO
        Public cbSize As UInteger
        Public dwTime As UInteger
    End Structure
    <Runtime.InteropServices.DllImport("user32.dll")>
    Private Shared Function GetLastInputInfo(ByRef plii As LASTINPUTINFO) As Boolean
    End Function

    ''' <summary>How long nobody has touched the mouse or keyboard.</summary>
    Private Shared Function IdleTime() As TimeSpan
        Try
            Dim li As New LASTINPUTINFO With {.cbSize = CUInt(Runtime.InteropServices.Marshal.SizeOf(GetType(LASTINPUTINFO)))}
            If GetLastInputInfo(li) Then
                Dim now = Environment.TickCount64 And &HFFFFFFFFL
                Return TimeSpan.FromMilliseconds((now - CLng(li.dwTime) + &H100000000L) Mod &H100000000L)
            End If
        Catch
        End Try
        Return TimeSpan.Zero
    End Function
    Private _previewReady, _loadingRecipe As Boolean

    Public Sub New()
        Text = "Image & video studio"
        Size = New Size(1420, 920)
        MinimumSize = New Size(1150, 720)
        StartPosition = FormStartPosition.CenterParent
        Theme.ApplyForm(Me)

        '──── left: task + models ────
        Dim left As New Panel() With {.Dock = DockStyle.Left, .Width = 480, .Padding = New Padding(8), .BackColor = Theme.CRaised}
        Dim filt As New FlowLayoutPanel() With {.Dock = DockStyle.Top, .Height = 36}
        cmbTask = New ComboBox() With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 260}
        cmbTask.Items.AddRange(TaskList.Select(Function(t) CObj(Localizer.T(t.Label))).ToArray())
        cmbTask.SelectedIndex = 0
        cmbShow = New ComboBox() With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 190}
        cmbShow.Items.AddRange({Localizer.T("★ Best for my GPU"), Localizer.T("All models (show everything)"), Localizer.T("🔓 Uncensored")})
        cmbShow.SelectedIndex = 0
        Theme.StyleInput(cmbShow) : Theme.StyleInput(cmbTask)
        Dim btnCivitai = Theme.MakeButton("🌐 More on Civitai…")
        filt.Height = 74
        filt.Controls.AddRange({cmbTask, cmbShow, btnCivitai})
        AddHandler btnCivitai.Click, Sub()
                                         Using f As New CivitaiForm(checkpoints:=True, family:=SelectedRecipe()?.Family)
                                             f.ShowDialog(Me)
                                             If f.InstalledSomething Then
                                                 FillGrid()
                                                 UpdateLoraButton()
                                             End If
                                         End Using
                                     End Sub

        grid = New DataGridView() With {.Dock = DockStyle.Fill, .ReadOnly = True}
        Theme.StyleGrid(grid)
        grid.Columns.Add("name", "Model")
        grid.Columns.Add("kind", "Type")
        grid.Columns.Add("best", "Best for you")
        grid.Columns.Add("fit", "On this PC")
        grid.Columns(0).AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
        grid.Columns(1).Width = 45
        grid.Columns(2).Width = 130
        grid.Columns(3).Width = 130

        Dim detail As New Panel() With {.Dock = DockStyle.Bottom, .Height = 200, .Padding = New Padding(0, 8, 0, 0)}
        lblAbout = New Label() With {.Dock = DockStyle.Top, .Height = 64, .ForeColor = Theme.CDim}
        cmbVariant = New ComboBox() With {.Dock = DockStyle.Top, .DropDownStyle = ComboBoxStyle.DropDownList}
        Theme.StyleInput(cmbVariant)
        lblNeed = New Label() With {.Dock = DockStyle.Top, .Height = 22, .ForeColor = Theme.CDim}
        Dim dlBar As New FlowLayoutPanel() With {.Dock = DockStyle.Top, .Height = 40}
        btnDownload = Theme.MakeButton("⬇ Download", primary:=True)
        btnDelete = Theme.MakeButton("🗑 Delete")
        btnDefault = Theme.MakeButton("☆ Default")
        dlBar.Controls.AddRange({btnDownload, btnDelete, btnDefault})
        AddHandler btnDefault.Click, Sub()
                                         Dim rr = SelectedRecipe()
                                         If rr Is Nothing Then Return
                                         If rr.IsVideo Then
                                             AppCore.Settings.ChatVideoModel = If(AppCore.Settings.ChatVideoModel = rr.Id, "", rr.Id)
                                         Else
                                             AppCore.Settings.ChatImageModel = If(AppCore.Settings.ChatImageModel = rr.Id, "", rr.Id)
                                         End If
                                         AppCore.SaveSettings()
                                         UpdateButtons()
                                     End Sub
        prg = New ProgressBar() With {.Dock = DockStyle.Top, .Height = 14}
        lblStatus = New Label() With {.Dock = DockStyle.Top, .Height = 40, .ForeColor = Theme.CDim}
        detail.Controls.Add(lblStatus)
        detail.Controls.Add(prg)
        detail.Controls.Add(dlBar)
        detail.Controls.Add(lblNeed)
        detail.Controls.Add(cmbVariant)
        detail.Controls.Add(lblAbout)
        left.Controls.Add(grid)
        left.Controls.Add(filt)
        left.Controls.Add(detail)

        '──── right: inputs, prompt, settings ────
        Dim right As New Panel() With {.Dock = DockStyle.Fill, .Padding = New Padding(10)}
        Dim form As New TableLayoutPanel() With {.Dock = DockStyle.Top, .Height = 296, .ColumnCount = 2}
        form.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        form.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 300))
        txtPrompt = New TextBox() With {.Dock = DockStyle.Fill, .Multiline = True, .ScrollBars = ScrollBars.Vertical,
                                        .Font = New Font("Segoe UI", 10.5!)}
        txtNeg = New TextBox() With {.Dock = DockStyle.Fill, .PlaceholderText = "Negative prompt (things to avoid) — optional"}
        Theme.StyleInput(txtPrompt) : Theme.StyleInput(txtNeg)

        ' Up to three input pictures: click to choose, right-click to remove, or drop files on them.
        slotPanel = New FlowLayoutPanel() With {.Dock = DockStyle.Fill, .WrapContents = False}
        For i = 0 To 2
            Dim k = i
            slots(i) = New PictureBox() With {.Size = New Size(92, 92), .SizeMode = PictureBoxSizeMode.Zoom, .BackColor = Theme.CBack,
                                              .BorderStyle = BorderStyle.FixedSingle, .Cursor = Cursors.Hand, .Margin = New Padding(2), .AllowDrop = True}
            AddHandler slots(i).MouseUp, Sub(s, e)
                                             If e.Button = MouseButtons.Right Then SetInput(k, Nothing) Else PickInput(k)
                                         End Sub
            AddHandler slots(i).Paint, Sub(s, e)
                                           If slots(k).Image Is Nothing Then
                                               TextRenderer.DrawText(e.Graphics, If(k = 0, Localizer.T("🖼 Choose picture"), "+"), Font, slots(k).ClientRectangle, Theme.CDim,
                                                                     TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter Or TextFormatFlags.WordBreak)
                                           End If
                                       End Sub
            AddHandler slots(i).DragEnter, Sub(s, e) e.Effect = If(e.Data.GetDataPresent(DataFormats.FileDrop), DragDropEffects.Copy, DragDropEffects.None)
            AddHandler slots(i).DragDrop, Sub(s, e)
                                              Dim files = CType(e.Data.GetData(DataFormats.FileDrop), String())
                                              If files?.Length > 0 Then SetInput(k, files(0))
                                          End Sub
            slotPanel.Controls.Add(slots(i))
        Next

        Dim opts As New FlowLayoutPanel() With {.Dock = DockStyle.Fill, .WrapContents = True}
        cmbSize = New ComboBox() With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 120}
        Theme.StyleInput(cmbSize)
        numSteps = New NumericUpDown() With {.Minimum = 1, .Maximum = 150, .Width = 55}
        numCfg = New NumericUpDown() With {.Minimum = 0, .Maximum = 20, .DecimalPlaces = 1, .Increment = 0.5D, .Width = 55}
        numSeed = New NumericUpDown() With {.Minimum = -1, .Maximum = Integer.MaxValue, .Value = -1, .Width = 100}
        numFrames = New NumericUpDown() With {.Minimum = 1, .Maximum = 241, .Increment = 4, .Width = 55}
        numStrength = New NumericUpDown() With {.Minimum = 0.05D, .Maximum = 2D, .DecimalPlaces = 2, .Increment = 0.05D, .Value = 0.75D, .Width = 60}
        numCount = New NumericUpDown() With {.Minimum = 1, .Maximum = 4, .Value = 1, .Width = 45}
        For Each n In {numSteps, numCfg, numSeed, numFrames, numStrength, numCount}
            Theme.StyleInput(n)
        Next
        cmbStyle = New ComboBox() With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 140}
        cmbStyle.Items.AddRange(Styles.Select(Function(s) CObj(Localizer.T(s.Label))).ToArray())
        cmbStyle.SelectedIndex = 0
        Theme.StyleInput(cmbStyle)
        lblStrength = Theme.MakeLabel("Strength")
        lblFrames = Theme.MakeLabel("Frames")
        chkFast = New CheckBox() With {.Text = "⚡ Fast", .AutoSize = True, .ForeColor = Theme.CText, .Checked = AppCore.Settings.MediaFast,
                                       .Margin = New Padding(8, 6, 4, 0)}
        btnLoras = Theme.MakeButton("🧩 LoRAs")
        btnMask = Theme.MakeButton("🖌 Paint area…")
        btnVideo = Theme.MakeButton("🎞 Choose video…")
        lblVideo = Theme.MakeLabel("")
        lblSize = Theme.MakeLabel("Size") : lblSteps = Theme.MakeLabel("Steps") : lblCfg = Theme.MakeLabel("CFG")
        lblSeed = Theme.MakeLabel("Seed") : lblPictures = Theme.MakeLabel("Pictures") : lblStyle = Theme.MakeLabel("Style")
        opts.Controls.AddRange({lblSize, cmbSize, lblSteps, numSteps, lblCfg, numCfg,
                                lblSeed, numSeed, lblPictures, numCount, lblStyle, cmbStyle,
                                lblFrames, numFrames, lblStrength, numStrength, chkFast, btnLoras, btnMask, btnVideo, lblVideo})
        btnCast = Theme.MakeButton(Localizer.T("🎭 Cast…"))
        lblCast = Theme.MakeLabel("", dim_:=True)
        AddHandler btnCast.Click, Sub() PickCast()
        opts.Controls.AddRange({btnCast, lblCast})
        txtSay = New TextBox() With {.Width = 330, .Height = 48, .Multiline = True, .AcceptsReturn = True, .ScrollBars = ScrollBars.Vertical,
                                     .PlaceholderText = Localizer.T("Words to say — or a conversation, one line each: @Anna: Hello! / @Max: Hi…")}
        Theme.StyleInput(txtSay)
        cmbVoice = New ComboBox() With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 200}
        Theme.StyleInput(cmbVoice)
        FillVoices()
        AddHandler cmbVoice.DropDown, Sub()
                                          ' voices saved in the music & voice studio meanwhile
                                          Dim keep = cmbVoice.SelectedIndex
                                          Dim wasRecording = keep = cmbVoice.Items.Count - 1
                                          Dim name = PickedVoiceName()
                                          FillVoices()
                                          _fillingVoices = True
                                          If wasRecording Then
                                              cmbVoice.SelectedIndex = cmbVoice.Items.Count - 1
                                          ElseIf name IsNot Nothing AndAlso _voiceNames.Contains(name) Then
                                              cmbVoice.SelectedIndex = _voiceNames.IndexOf(name) + 1
                                          End If
                                          _fillingVoices = False
                                      End Sub
        AddHandler cmbVoice.SelectedIndexChanged, Sub()
                                                      If _fillingVoices Then Return
                                                      If cmbVoice.SelectedIndex = cmbVoice.Items.Count - 1 Then
                                                          Using d As New OpenFileDialog() With {.Filter = "Audio|*.wav;*.mp3;*.m4a;*.ogg;*.flac;*.webm"}
                                                              If d.ShowDialog(Me) = DialogResult.OK Then _audioPath = d.FileName
                                                          End Using
                                                          If _audioPath Is Nothing Then cmbVoice.SelectedIndex = 0
                                                      End If
                                                      UpdateTaskUi()
                                                  End Sub
        chkSharper = New CheckBox() With {.Text = Localizer.T("🔍 2× sharper"), .AutoSize = True, .ForeColor = Theme.CText, .Checked = True, .Margin = New Padding(8, 6, 4, 0)}
        chkSmooth = New CheckBox() With {.Text = Localizer.T("🌊 2× smoother motion"), .AutoSize = True, .ForeColor = Theme.CText, .Checked = True, .Margin = New Padding(8, 6, 4, 0)}
        opts.Controls.AddRange({txtSay, cmbVoice, chkSharper, chkSmooth})
        Dim genBar As New FlowLayoutPanel() With {.Dock = DockStyle.Fill}
        btnGenerate = Theme.MakeButton("🎨 Generate", primary:=True, width:=160)
        btnGenerate.Font = Theme.BoldFont
        Dim btnFolder = Theme.MakeButton("📁 Open output folder")
        btnDescribe = Theme.MakeButton("📝 Describe picture")
        AddHandler btnDescribe.Click, Async Sub() Await DescribeAsync()
        genBar.Controls.Add(btnDescribe)
        btnImprove = Theme.MakeButton(Localizer.T("✨ Improve prompt"))
        AddHandler btnImprove.Click, Async Sub() Await ImprovePromptAsync(btnImprove)
        genBar.Controls.Add(btnImprove)
        btnStop = Theme.MakeButton("■ Stop")
        btnStop.Visible = False
        lblQueue = Theme.MakeLabel("")
        Dim btnFilm = Theme.MakeButton(Localizer.T("🎬 Film editor"))
        AddHandler btnFilm.Click, Sub()
                                      Using f As New FilmForm()
                                          f.ShowDialog(Me)
                                          If f.Exported IsNot Nothing AndAlso File.Exists(f.Exported) Then
                                              AddToGallery(f.Exported, 0)
                                              ShowOutput(f.Exported)
                                          End If
                                      End Using
                                  End Sub
        genBar.Controls.AddRange({btnGenerate, btnStop, lblQueue})
        _galleryButtons = {btnFilm, btnFolder}
        form.Controls.Add(txtPrompt, 0, 0) : form.Controls.Add(slotPanel, 1, 0)
        form.Controls.Add(txtNeg, 0, 1) : form.SetColumnSpan(txtNeg, 2)
        form.Controls.Add(opts, 0, 2) : form.SetColumnSpan(opts, 2)
        form.Controls.Add(genBar, 0, 3) : form.SetColumnSpan(genBar, 2)
        form.RowStyles.Add(New RowStyle(SizeType.Absolute, 100))
        form.RowStyles.Add(New RowStyle(SizeType.Absolute, 30))
        form.RowStyles.Add(New RowStyle(SizeType.Absolute, 112))
        form.RowStyles.Add(New RowStyle(SizeType.Absolute, 46))

        preview = New WebView2() With {.Dock = DockStyle.Fill, .DefaultBackgroundColor = Theme.CBack}
        gallery = New ListView() With {.Dock = DockStyle.Bottom, .Height = 132, .View = View.LargeIcon, .LargeImageList = thumbs,
                                       .BackColor = Theme.CBack, .ForeColor = Theme.CText, .BorderStyle = BorderStyle.None, .MultiSelect = False}
        right.Controls.Add(preview)
        right.Controls.Add(gallery)
        ' history: search, favourites, and the overnight queue
        Dim histBar As New FlowLayoutPanel() With {.Dock = DockStyle.Bottom, .Height = 36, .WrapContents = False, .Padding = New Padding(0, 4, 0, 0)}
        txtSearch = New TextBox() With {.Width = 230, .PlaceholderText = Localizer.T("🔍 Search by prompt, model, @name…")}
        Theme.StyleInput(txtSearch)
        chkFavs = New CheckBox() With {.Text = Localizer.T("★ Favourites"), .AutoSize = True, .ForeColor = Theme.CText, .Margin = New Padding(8, 6, 4, 0)}
        chkIdle = New CheckBox() With {.Text = Localizer.T("🌙 Start when I'm away"), .AutoSize = True, .ForeColor = Theme.CText, .Margin = New Padding(16, 6, 4, 0)}
        chkSleep = New CheckBox() With {.Text = Localizer.T("💤 Sleep the PC when all is done"), .AutoSize = True, .ForeColor = Theme.CText, .Margin = New Padding(8, 6, 4, 0)}
        Dim tip As New ToolTip()
        tip.SetToolTip(chkIdle, Localizer.T("Jobs wait until nobody has used the PC for 5 minutes, then run one after another (e.g. overnight)."))
        histBar.Controls.AddRange({txtSearch, chkFavs, chkIdle, chkSleep})
        For Each gb In _galleryButtons
            gb.Margin = New Padding(6, 0, 0, 0)
            histBar.Controls.Add(gb)
        Next
        right.Controls.Add(histBar)
        Dim searchTimer As New System.Windows.Forms.Timer() With {.Interval = 400}
        AddHandler searchTimer.Tick, Sub()
                                         searchTimer.Stop()
                                         LoadGallery()
                                     End Sub
        AddHandler txtSearch.TextChanged, Sub()
                                              searchTimer.Stop()
                                              searchTimer.Start()
                                          End Sub
        AddHandler chkFavs.CheckedChanged, Sub() LoadGallery()
        right.Controls.Add(form)

        Controls.Add(right)
        Controls.Add(left)

        '──── events ────
        AddHandler cmbShow.SelectedIndexChanged, Sub() FillGrid()
        AddHandler cmbTask.SelectedIndexChanged, Sub()
                                                    FillGrid()
                                                    UpdateTaskUi()
                                                End Sub
        AddHandler grid.SelectionChanged, Sub() ShowRecipe()
        AddHandler cmbVariant.SelectedIndexChanged, Sub() UpdateButtons()
        AddHandler btnDownload.Click, Async Sub() Await DownloadAsync()
        AddHandler btnDelete.Click, Sub() DeleteSelected()
        AddHandler btnGenerate.Click, Async Sub() Await GenerateAsync()
        AddHandler btnStop.Click, Sub()
                                      _queue.Clear()
                                      _cts?.Cancel()
                                      MediaStudio.Cancel()
                                      UpdateQueueLabel()
                                  End Sub
        AddHandler btnFolder.Click, Sub() ModelManagerForm.OpenFolder(MediaStudio.OutputFolder)
        AddHandler chkFast.CheckedChanged, Sub()
                                               AppCore.Settings.MediaFast = chkFast.Checked
                                               AppCore.SaveSettings()
                                               If Not _loadingRecipe Then ApplyStepDefaults()
                                           End Sub
        AddHandler btnLoras.Click, Sub()
                                       Dim r = SelectedRecipe()
                                       Using f As New LoraForm(r?.Family, r?.Name)
                                           f.ShowDialog(Me)
                                       End Using
                                       UpdateLoraButton()
                                   End Sub
        AddHandler btnMask.Click, Sub() PaintMask()
        AddHandler btnVideo.Click, Sub() PickVideo()
        AddHandler gallery.SelectedIndexChanged, Sub()
                                                     If gallery.SelectedItems.Count > 0 Then ShowOutput(CStr(gallery.SelectedItems(0).Tag))
                                                 End Sub
        AddHandler gallery.ItemActivate, Sub()
                                             If gallery.SelectedItems.Count > 0 Then ModelManagerForm.OpenUrl(CStr(gallery.SelectedItems(0).Tag))
                                         End Sub

        Dim galleryMenu As New ContextMenuStrip()
        Dim miUse = galleryMenu.Items.Add(Localizer.T("Use as input"))
        AddHandler miUse.Click, Sub()
                                    If gallery.SelectedItems.Count = 0 Then Return
                                    Dim f = CStr(gallery.SelectedItems(0).Tag)
                                    If IsVideoFile(f) Then
                                        SetVideo(f)
                                    Else
                                        Dim free = Array.FindIndex(_inputs, Function(x) x Is Nothing)
                                        SetInput(If(free < 0, 0, free), f)
                                    End If
                                End Sub
        Dim miCopy = galleryMenu.Items.Add(Localizer.T("Copy image"))
        AddHandler miCopy.Click, Sub()
                                     If gallery.SelectedItems.Count = 0 Then Return
                                     Dim p = CStr(gallery.SelectedItems(0).Tag)
                                     If p.EndsWith(".png") Then
                                         Using img = Image.FromFile(p)
                                             Clipboard.SetImage(img)
                                         End Using
                                     End If
                                 End Sub
        Dim miSound = galleryMenu.Items.Add(Localizer.T("🎵 Add music or a voice-over…"))
        AddHandler miSound.Click, Sub()
                                      If gallery.SelectedItems.Count = 0 Then Return
                                      Dim f = CStr(gallery.SelectedItems(0).Tag)
                                      If Not IsVideoFile(f) Then
                                          Localizer.Box(Me, "Choose a video in the gallery first.", "Soundtrack")
                                          Return
                                      End If
                                      Using dlg As New SoundtrackForm(f)
                                          If dlg.ShowDialog(Me) = DialogResult.OK AndAlso dlg.Result IsNot Nothing Then
                                              AddToGallery(dlg.Result, 0)
                                              ShowOutput(dlg.Result)
                                          End If
                                      End Using
                                  End Sub
        AddHandler galleryMenu.Opening, Sub() miSound.Visible = AppCore.Settings.EnableAudio
        Dim miExtend = galleryMenu.Items.Add(Localizer.T("⏩ Extend this video"))
        AddHandler miExtend.Click, Sub() UseVideoFor("extend")
        Dim miEnhance = galleryMenu.Items.Add(Localizer.T("✨ Enhance this video (sharper, smoother)"))
        AddHandler miEnhance.Click, Sub() UseVideoFor("enhance")
        AddHandler galleryMenu.Opening, Sub()
                                            Dim isVid = gallery.SelectedItems.Count > 0 AndAlso IsVideoFile(CStr(gallery.SelectedItems(0).Tag))
                                            miExtend.Visible = isVid : miEnhance.Visible = isVid
                                        End Sub
        Dim miFav = galleryMenu.Items.Add(Localizer.T("★ Favourite"))
        AddHandler miFav.Click, Sub()
                                    If gallery.SelectedItems.Count = 0 Then Return
                                    Dim it = gallery.SelectedItems(0)
                                    Dim f = CStr(it.Tag)
                                    Dim nm = IO.Path.GetFileNameWithoutExtension(f)
                                    it.Text = If(History.ToggleFavorite(f), "★ ", "") & nm.Substring(0, Math.Min(15, nm.Length))
                                End Sub
        Dim miFilm = galleryMenu.Items.Add(Localizer.T("🎬 Add to film"))
        AddHandler miFilm.Click, Sub()
                                     If gallery.SelectedItems.Count = 0 Then Return
                                     FilmProject.AddToCurrent(CStr(gallery.SelectedItems(0).Tag))
                                     lblStatus.Text = Localizer.T("Added to the film — open 🎬 Film editor to arrange and export it.")
                                 End Sub
        Dim miSubs = galleryMenu.Items.Add(Localizer.T("💬 Add subtitles…"))
        AddHandler miSubs.Click, Sub()
                                     If gallery.SelectedItems.Count = 0 Then Return
                                     Dim f = CStr(gallery.SelectedItems(0).Tag)
                                     If IsVideoFile(f) Then QueueSubtitles(f)
                                 End Sub
        AddHandler galleryMenu.Opening, Sub()
                                            Dim sel = gallery.SelectedItems.Count > 0
                                            Dim f = If(sel, CStr(gallery.SelectedItems(0).Tag), "")
                                            miFav.Text = Localizer.T(If(sel AndAlso History.IsFavorite(f), "☆ Remove from favourites", "★ Favourite"))
                                            miSubs.Visible = sel AndAlso IsVideoFile(f)
                                        End Sub
        Dim miReuse = galleryMenu.Items.Add(Localizer.T("♻ Reuse these settings"))
        AddHandler miReuse.Click, Sub()
                                      If gallery.SelectedItems.Count > 0 Then ReuseSettings(CStr(gallery.SelectedItems(0).Tag))
                                  End Sub
        Dim miMp4 = galleryMenu.Items.Add(Localizer.T("Save video as MP4"))
        AddHandler miMp4.Click, Async Sub()
                                    If gallery.SelectedItems.Count = 0 Then Return
                                    Dim f = CStr(gallery.SelectedItems(0).Tag)
                                    If Not f.EndsWith(".webm", StringComparison.OrdinalIgnoreCase) Then Return
                                    Try
                                        lblStatus.Text = Localizer.T("Converting to MP4…")
                                        Dim mp4 = Await VideoTools.ToMp4Async(f, Sub(t) Me.Post(Sub() lblStatus.Text = Localizer.T(t)), CancellationToken.None)
                                        lblStatus.Text = Localizer.T("Saved") & ": " & Path.GetFileName(mp4)
                                        ModelManagerForm.OpenFolder(Path.GetDirectoryName(mp4))
                                    Catch ex As Exception
                                        lblStatus.Text = "⚠ " & ex.Message
                                    End Try
                                End Sub
        gallery.ContextMenuStrip = galleryMenu
    End Sub

    Protected Overrides Async Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        Dim previewTask = Environment.GetEnvironmentVariable("LOCALMIND_STUDIO_TASK")
        If Not String.IsNullOrEmpty(previewTask) Then
            Dim pi = Array.FindIndex(TaskList, Function(x) x.Id = previewTask AndAlso (x.Video OrElse previewTask <> "create"))
            If pi >= 0 Then cmbTask.SelectedIndex = pi
        End If
        FillGrid()
        UpdateTaskUi()
        LoadGallery()
        Try
            Dim env = Await CoreWebView2Environment.CreateAsync(Nothing, AppPaths.WebViewFolder)
            Await preview.EnsureCoreWebView2Async(env)
            preview.CoreWebView2.SetVirtualHostNameToFolderMapping("outputs.localmind", MediaStudio.OutputFolder, CoreWebView2HostResourceAccessKind.Allow)
            _previewReady = True
            If gallery.Items.Count > 0 Then ShowOutput(CStr(gallery.Items(0).Tag)) Else ShowHtml("<p style='color:#777;font:15px Segoe UI;text-align:center;margin-top:30%'>" &
                Net.WebUtility.HtmlEncode(Localizer.T("Your creations appear here.")) & "</p>")
        Catch
        End Try
    End Sub

    '──────────────── task ────────────────
    ''' <summary>An installed picture model that can combine reference pictures (makes cast scenes for videos).</summary>
    Private Function CastSceneRecipe() As MediaRecipe
        Return VideoMaker.CastSceneRecipe()
    End Function

    ''' <summary>Voices for a talking character: the default, your saved (cloned) voices, or a recording.</summary>
    Private Sub FillVoices()
        _fillingVoices = True
        cmbVoice.Items.Clear()
        _voiceNames.Clear()
        cmbVoice.Items.Add(Localizer.T("🔊 Default voice"))
        For Each v In VoiceLibrary.All()
            _voiceNames.Add(v.Name)
            cmbVoice.Items.Add("🎙 " & v.Name)
        Next
        cmbVoice.Items.Add(Localizer.T("🎵 Use a recording…"))
        cmbVoice.SelectedIndex = 0
        _fillingVoices = False
    End Sub

    Private Function UsesRecording() As Boolean
        Return cmbVoice.SelectedIndex = cmbVoice.Items.Count - 1 AndAlso _audioPath IsNot Nothing
    End Function

    Private Function PickedVoiceName() As String
        Dim i = cmbVoice.SelectedIndex - 1
        Return If(i >= 0 AndAlso i < _voiceNames.Count, _voiceNames(i), Nothing)
    End Function

    ''' <summary>The gallery's video, for "Extend" or "Enhance".</summary>
    Private Sub UseVideoFor(task As String)
        If gallery.SelectedItems.Count = 0 Then Return
        Dim f = CStr(gallery.SelectedItems(0).Tag)
        If Not IsVideoFile(f) Then Return
        Dim ti = Array.FindIndex(TaskList, Function(x) x.Id = task)
        If ti >= 0 Then cmbTask.SelectedIndex = ti
        SetVideo(f)
    End Sub

    ''' <summary>Tasks that work on a video you choose.</summary>
    Private Shared Function TakesVideo(t As String) As Boolean
        Return t = "video-edit" OrElse t = "extend" OrElse t = "enhance"
    End Function

    ''' <summary>Which models a task lists.</summary>
    Private Shared Function Suits(r As MediaRecipe, t As (Id As String, Label As String, Video As Boolean)) As Boolean
        Select Case t.Id
            Case "storyboard" : Return r.IsVideo AndAlso (r.Can("create") OrElse r.Can("animate"))
            Case "extend" : Return r.IsVideo AndAlso r.Can("animate")
            Case "enhance" : Return r.IsUpscaler
            Case Else : Return r.Can(t.Id) AndAlso (r.IsUpscaler OrElse r.IsVideo = t.Video)
        End Select
    End Function

    Private Sub PickCast()
        Using f As New CastForm(_cast.Select(Function(m) m.Id))
            If f.ShowDialog(Me) = DialogResult.OK Then
                _cast.Clear()
                _cast.AddRange(f.Picked)
            Else
                ' names or pictures may have changed: keep the same people, with their new details
                Dim fresh = CastLibrary.Load()
                Dim ids = _cast.Select(Function(m) m.Id).ToList()
                _cast.Clear()
                _cast.AddRange(fresh.Where(Function(m) ids.Contains(m.Id)))
            End If
        End Using
        lblCast.Text = String.Join("  ", _cast.Select(Function(m) m.ToString()))
        btnCast.Text = If(_cast.Count > 0, Localizer.T("🎭 Cast") & $" ({_cast.Count})", Localizer.T("🎭 Cast…"))
    End Sub

    Private Function CurrentTask() As (Id As String, Label As String, Video As Boolean)
        Return TaskList(Math.Max(0, cmbTask.SelectedIndex))
    End Function

    ''' <summary>Shows only the inputs and settings the chosen task uses.</summary>
    Private Sub UpdateTaskUi()
        Dim t = CurrentTask().Id
        Dim r = SelectedRecipe()
        Dim maxPics = PicturesFor(t, r)
        For i = 0 To 2
            slots(i).Visible = i < maxPics
        Next
        slotPanel.Visible = maxPics > 0
        Dim pictureOnly = t = "upscale" OrElse t = "remove-bg"
        numCount.Enabled = Not CurrentTask().Video AndAlso Not pictureOnly
        cmbStyle.Enabled = Not pictureOnly AndAlso t <> "restore"
        btnMask.Visible = t = "inpaint"
        btnMask.Text = If(_maskPath IsNot Nothing, Localizer.T("🖌 Area painted ✔"), Localizer.T("🖌 Paint area…"))
        btnVideo.Visible = TakesVideo(t)
        lblVideo.Visible = TakesVideo(t)
        txtSay.Visible = t = "talk" AndAlso Not UsesRecording()
        cmbVoice.Visible = t = "talk"
        chkSharper.Visible = t = "enhance" : chkSmooth.Visible = t = "enhance"
        ' only the settings a task uses (keeps the options on screen)
        Dim enhance = t = "enhance"
        For Each c As Control In {lblSize, cmbSize, lblSteps, numSteps, lblCfg, numCfg, lblSeed, numSeed, lblStyle, cmbStyle, btnLoras, btnCast, lblCast, btnImprove, txtNeg}
            c.Visible = Not enhance
        Next
        lblStyle.Visible = lblStyle.Visible AndAlso t <> "talk" : cmbStyle.Visible = lblStyle.Visible
        chkFast.Visible = Not enhance AndAlso r IsNot Nothing AndAlso r.Speed IsNot Nothing
        lblPictures.Visible = Not CurrentTask().Video : numCount.Visible = lblPictures.Visible
        btnDescribe.Visible = Not CurrentTask().Video OrElse t = "storyboard" OrElse t = "talk"
        Dim usesControl = (t = "layout" OrElse t = "outline") AndAlso r IsNot Nothing AndAlso r.Parts.Any(Function(p) p.Control)
        lblStrength.Visible = t = "inpaint" OrElse t = "restyle" OrElse t = "video-edit" OrElse usesControl
        numStrength.Visible = lblStrength.Visible
        If t = "inpaint" Then numStrength.Value = 1D Else If t = "restyle" Then numStrength.Value = 0.6D Else If t = "video-edit" Then numStrength.Value = 1D Else If usesControl Then numStrength.Value = 0.8D
        Dim isVideo = CurrentTask().Video
        lblFrames.Visible = isVideo AndAlso t <> "talk" AndAlso t <> "enhance" : numFrames.Visible = lblFrames.Visible
        lblFrames.Text = Localizer.T(If(t = "storyboard", "Frames per shot", "Frames"))
        Dim upscale = pictureOnly OrElse t = "enhance"
        For Each c As Control In {numSteps, numCfg, numSeed, cmbSize, txtNeg, chkFast, btnLoras}
            c.Enabled = Not upscale
        Next
        txtPrompt.Enabled = Not upscale
        txtPrompt.PlaceholderText = Localizer.T(If(t = "edit" OrElse t = "inpaint", "Describe the change, e.g. 'make it night' or 'replace the car with a horse'…",
                                                If(t = "combine", "Say how to combine them, e.g. 'the person from picture 1 sitting in the room from picture 2'…",
                                                If(t = "video-edit", "Describe the new look of the video, e.g. 'oil painting style'…",
                                                If(t = "storyboard", "One shot per line, e.g. '@Anna walks into @Old harbor at dawn', next line 'Close-up: @Anna smiles'… Or write an idea and press ✨ Improve prompt to get the shots.",
                                                If(t = "reference", "Pictures = who and what to show. Describe the video, e.g. 'the woman from picture 1 walks the dog from picture 2 on a beach'…",
                                                If(t = "first-last", "Picture 1 = first frame, picture 2 = last frame. Describe the motion in between…",
                                                If(t = "talk", "Picture = the person. Describe the scene (optional), e.g. 'a woman talking to the camera, warm light'…",
                                                If(t = "extend", "What happens next in the video?…",
                                                If(t = "pose", "Picture 1 = who, picture 2 = the pose. Add details if you like, e.g. 'in a park'…",
                                                If(t = "layout", "Picture = the layout to copy. Describe the new picture, e.g. 'a cozy wooden cabin interior'…",
                                                If(t = "outline", "Picture = your sketch. Describe what it should become…",
                                                If(t = "restore", "(optional) anything special, e.g. 'colorize it'…",
                                                If(pictureOnly OrElse t = "enhance", "(no description needed)", "Describe the picture or video (English works best)…"))))))))))))))
        btnGenerate.Text = Localizer.T(If(t = "upscale", "🔍 Enlarge", If(t = "remove-bg", "✂ Remove background", If(t = "enhance", "✨ Enhance", "🎨 Generate"))))
        If t = "enhance" Then btnGenerate.Enabled = True
    End Sub

    ''' <summary>How many input pictures a task takes.</summary>
    Private Shared Function PicturesFor(t As String, r As MediaRecipe) As Integer
        Select Case t
            Case "combine" : Return Math.Max(2, Math.Min(3, If(r?.Refs, 3)))
            Case "pose" : Return 2
            Case "edit", "inpaint", "restyle", "upscale", "animate", "layout", "outline", "restore", "remove-bg" : Return 1
            Case "video-edit" : Return 1          ' optional reference picture (e.g. a character)
            Case "first-last" : Return 2
            Case "reference" : Return 3
            Case "talk" : Return 1
            Case "storyboard" : Return 1          ' optional start picture
            Case "extend", "enhance" : Return 0
            Case Else : Return 0
        End Select
    End Function

    '──────────────── model list ────────────────
    Private Function SelectedRecipe() As MediaRecipe
        Return If(grid.CurrentRow Is Nothing, Nothing, TryCast(grid.CurrentRow.Tag, MediaRecipe))
    End Function

    Private Sub FillGrid()
        Dim list As List(Of MediaRecipe)
        Select Case cmbShow.SelectedIndex
            Case 0 : list = MediaStudio.BestForGpu(_hw)
            Case 2   ' uncensored: the ones that fit first, then the rest (they may use system RAM)
                Dim fit = MediaStudio.BestForGpu(_hw, uncensored:=True)
                list = fit.Concat(MediaStudio.Recipes().Where(Function(x) x.Uncensored AndAlso Not fit.Contains(x))).ToList()
            Case Else : list = MediaStudio.Recipes()
        End Select
        Dim t = CurrentTask()
        list = list.Where(Function(r) Suits(r, t)).ToList()
        grid.Rows.Clear()
        For Each r In list
            Dim v = MediaStudio.PickVariant(r, _hw)
            Dim fit = MediaStudio.Fit(r, v, _hw)
            Dim installed = MediaStudio.InstalledVariant(r)
            Dim i = grid.Rows.Add(If(installed IsNot Nothing, "✔ ", "") & r.Name & If(r.Speed IsNot Nothing, " ⚡", "") & If(r.Uncensored, " 🔓", ""),
                                  If(r.IsVideo, "🎬", If(r.IsUpscaler, "🔍", "🖼")),
                                  $"{v.Label} · {Downloader.FormatBytes(v.Total)}", ModelCatalog.FitText(fit, _hw.VramBytes = 0))
            grid.Rows(i).Tag = r
            grid.Rows(i).Cells(3).Style.ForeColor = ModelManagerForm.FitColor(fit)
        Next
        If grid.Rows.Count = 0 AndAlso t.Id = "enhance" Then
            lblAbout.Text = Localizer.T("No model is needed: LocalMind sharpens and smooths the video by itself. A picture enlarger (Real-ESRGAN, under 'All models') makes it even sharper.")
            cmbVariant.Items.Clear()
            btnGenerate.Enabled = True
            btnDownload.Enabled = False
        ElseIf grid.Rows.Count = 0 Then
            lblAbout.Text = Localizer.T("No model for this task fits fully on this GPU — choose 'All models' to see options that use system RAM.")
            cmbVariant.Items.Clear()
            btnGenerate.Enabled = False
            btnDownload.Enabled = False
        End If
    End Sub

    Private Sub ShowRecipe()
        Dim r = SelectedRecipe()
        If r Is Nothing Then Return
        _loadingRecipe = True
        lblAbout.Text = r.About & If(r.Speed IsNot Nothing, "  ⚡ " & Localizer.T("Fast mode") & ": " & r.Speed.Label, "")
        cmbVariant.Items.Clear()
        Dim pick = MediaStudio.PickVariant(r, _hw)
        Dim installed = MediaStudio.InstalledVariant(r)
        For Each v In r.Variants
            cmbVariant.Items.Add(New VariantItem(r, v, _hw, v Is pick))
        Next
        Dim sel = If(installed, pick)
        cmbVariant.SelectedIndex = r.Variants.IndexOf(sel)

        cmbSize.Items.Clear()
        Dim sizes = If(r.IsVideo, {"832×480", "480×832", "640×640", "1280×704", "704×1280"},
                                  {"1024×1024", "1152×896", "896×1152", "1344×768", "768×1344", "768×768", "1536×1024"})
        cmbSize.Items.AddRange(sizes)
        Dim def = $"{r.Width}×{r.Height}"
        If r.Width > 0 AndAlso Not cmbSize.Items.Contains(def) Then cmbSize.Items.Insert(0, def)
        cmbSize.SelectedItem = If(cmbSize.Items.Contains(def), def, sizes(0))
        numFrames.Value = Math.Max(1, Math.Min(numFrames.Maximum, Math.Max(1, r.Frames)))
        txtNeg.Text = If(r.IsVideo, "", r.Negative)   ' Wan uses its own built-in negative unless the user types one
        chkFast.Visible = r.Speed IsNot Nothing
        ApplyStepDefaults()
        _loadingRecipe = False
        UpdateTaskUi()
        UpdateLoraButton()
        UpdateButtons()
    End Sub

    ''' <summary>Steps and CFG of the model, or of its Turbo / Lightning LoRA when ⚡ Fast is on.</summary>
    Private Sub ApplyStepDefaults()
        Dim r = SelectedRecipe()
        If r Is Nothing Then Return
        Dim fast = chkFast.Checked AndAlso r.Speed IsNot Nothing
        numSteps.Value = Math.Max(1, If(fast, r.Speed.Steps, r.Steps))
        numCfg.Value = CDec(Math.Min(20, If(fast, r.Speed.Cfg, r.Cfg)))
    End Sub

    Private Sub UpdateLoraButton()
        Dim r = SelectedRecipe()
        If r Is Nothing OrElse r.IsUpscaler Then
            btnLoras.Text = Localizer.T("🧩 LoRAs")
            Return
        End If
        Dim n = ActiveLoras(r).Count
        btnLoras.Text = Localizer.T("🧩 LoRAs") & If(n > 0, $" ({n})", "")
    End Sub

    Private Function ActiveLoras(r As MediaRecipe) As List(Of UserLora)
        Return LoraLibrary.ForFamily(r.Family).Where(Function(l) l.Enabled).ToList()
    End Function

    Private Class VariantItem
        Public ReadOnly V As MediaVariant
        Private ReadOnly _text As String
        Public Sub New(r As MediaRecipe, v As MediaVariant, hw As HardwareInfo, best As Boolean)
            Me.V = v
            _text = $"{If(best, "★ ", "")}{v.Label}  ·  {Downloader.FormatBytes(v.Total)}  ·  {ModelCatalog.FitText(MediaStudio.Fit(r, v, hw), hw.VramBytes = 0)}" &
                    If(MediaStudio.IsInstalled(r, v), "  ·  ✔", "")
        End Sub
        Public Overrides Function ToString() As String
            Return _text
        End Function
    End Class

    Private Function SelectedVariant() As MediaVariant
        Return TryCast(cmbVariant.SelectedItem, VariantItem)?.V
    End Function

    Private Sub UpdateButtons()
        Dim r = SelectedRecipe()
        Dim v = SelectedVariant()
        If r Is Nothing OrElse v Is Nothing Then Return
        Dim installed = MediaStudio.IsInstalled(r, v)
        Dim fast = chkFast.Checked AndAlso r.Speed IsNot Nothing
        lblNeed.Text = MediaStudio.RequirementText(r, v, _hw)
        lblNeed.ForeColor = ModelManagerForm.FitColor(MediaStudio.Fit(r, v, _hw))
        btnDownload.Enabled = Not installed AndAlso _cts Is Nothing
        btnDownload.Text = If(installed, Localizer.T("✔ Downloaded"), Localizer.T("⬇ Download") & $" ({Downloader.FormatBytes(MediaStudio.BytesToDownload(r, v, fast))})")
        btnDelete.Enabled = installed AndAlso _cts Is Nothing
        Dim isDefault = r.Id = If(r.IsVideo, AppCore.Settings.ChatVideoModel, AppCore.Settings.ChatImageModel)
        btnDefault.Visible = Not r.IsUpscaler AndAlso r.Kind <> "tool"
        btnDefault.Enabled = MediaStudio.UsableVariant(r) IsNot Nothing
        btnDefault.Text = If(isDefault, Localizer.T("★ Default"), Localizer.T("☆ Make default"))
        _tips.SetToolTip(btnDefault, Localizer.T(If(r.IsVideo, "Use this model when you ask the chat for a video (click again for automatic)",
                                                            "Use this model when you ask the chat for a picture (click again for automatic)")))
        btnGenerate.Enabled = installed OrElse _cts IsNot Nothing OrElse CurrentTask().Id = "enhance"
    End Sub

    Private Async Function DownloadAsync() As Task
        Dim r = SelectedRecipe()
        Dim v = SelectedVariant()
        If r Is Nothing OrElse v Is Nothing Then Return
        Dim fit = MediaStudio.Fit(r, v, _hw)
        If fit = FitLevel.TooBig AndAlso Localizer.Box(Me,
                Localizer.T("This model probably won't run on this PC (not enough graphics memory + RAM):") & vbCrLf & vbCrLf &
                MediaStudio.RequirementText(r, v, _hw) & vbCrLf & vbCrLf & Localizer.T("Download anyway?"),
                "Memory", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) <> DialogResult.Yes Then Return
        _cts = New CancellationTokenSource()
        btnDownload.Enabled = False
        Try
            lblStatus.Text = Localizer.T("⬇ Added to the download list (Models → Downloads).")
            Await MediaStudio.DownloadAsync(r, v, Nothing, _cts.Token, withSpeed:=chkFast.Checked)
            lblStatus.Text = Localizer.T("Ready — write a description and press Generate.")
        Catch ex As OperationCanceledException
            lblStatus.Text = Localizer.T("Download cancelled (it resumes next time).")
        Catch ex As Exception
            lblStatus.Text = "⚠ " & ex.Message
        Finally
            _cts = Nothing
            FillGridKeepSelection(r)
        End Try
    End Function

    Private Sub DeleteSelected()
        Dim r = SelectedRecipe()
        Dim v = SelectedVariant()
        If r Is Nothing OrElse v Is Nothing Then Return
        If Localizer.Box(Me, $"Delete {r.Name} ({v.Label}) from disk?", "Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) <> DialogResult.Yes Then Return
        MediaStudio.DeleteVariant(r, v)
        FillGridKeepSelection(r)
    End Sub

    Private Sub FillGridKeepSelection(r As MediaRecipe)
        FillGrid()
        For Each row As DataGridViewRow In grid.Rows
            If row.Tag Is r Then
                grid.CurrentCell = row.Cells(0)
                Exit For
            End If
        Next
        ShowRecipe()
    End Sub

    '──────────────── inputs ────────────────
    Private Shared Function IsVideoFile(f As String) As Boolean
        Dim e = Path.GetExtension(f).ToLowerInvariant()
        Return {".webm", ".mp4", ".mov", ".mkv", ".avi", ".m4v", ".gif"}.Contains(e)
    End Function

    Private Sub PickInput(i As Integer)
        Using d As New OpenFileDialog() With {.Filter = "Images|*.png;*.jpg;*.jpeg;*.webp;*.bmp"}
            If d.ShowDialog(Me) = DialogResult.OK Then SetInput(i, d.FileName)
        End Using
    End Sub

    Private Sub SetInput(i As Integer, path As String)
        _inputs(i) = path
        If i = 0 Then _maskPath = Nothing        ' a new picture needs a new painted area
        Dim old = slots(i).Image
        slots(i).Image = Nothing
        old?.Dispose()
        If path IsNot Nothing AndAlso File.Exists(path) AndAlso Not IsVideoFile(path) Then
            Try
                Using fs As New FileStream(path, FileMode.Open, FileAccess.Read)
                    slots(i).Image = New Bitmap(Image.FromStream(fs))
                End Using
            Catch
                _inputs(i) = Nothing
            End Try
        End If
        slots(i).Invalidate()
        UpdateTaskUi()
    End Sub

    Private Sub PaintMask()
        If _inputs(0) Is Nothing Then
            PickInput(0)
            If _inputs(0) Is Nothing Then Return
        End If
        Using f As New MaskEditorForm(_inputs(0), _maskPath)
            If f.ShowDialog(Me) = DialogResult.OK Then _maskPath = f.MaskPath
        End Using
        UpdateTaskUi()
    End Sub

    Private Sub PickVideo()
        Using d As New OpenFileDialog() With {.Filter = "Videos|*.mp4;*.mov;*.mkv;*.webm;*.avi;*.m4v;*.gif"}
            If d.ShowDialog(Me) = DialogResult.OK Then SetVideo(d.FileName)
        End Using
    End Sub

    Private Sub SetVideo(f As String)
        _videoPath = f
        lblVideo.Text = If(f Is Nothing, "", "🎞 " & Path.GetFileName(f))
        Dim vi = Array.FindIndex(TaskList, Function(t) t.Id = "video-edit")
        If Not TakesVideo(CurrentTask().Id) AndAlso vi >= 0 Then cmbTask.SelectedIndex = vi
        UpdateTaskUi()
    End Sub

    '──────────────── generate ────────────────
    Private Async Function GenerateAsync() As Task
        If CurrentTask().Id = "enhance" Then
            If QueueEnhance() AndAlso Not _running Then Await RunQueueAsync()
            Return
        End If
        Dim r = SelectedRecipe()
        Dim v = SelectedVariant()
        If r Is Nothing OrElse v Is Nothing Then Return
        Dim t = CurrentTask().Id
        If t = "storyboard" OrElse t = "first-last" OrElse t = "talk" OrElse t = "extend" Then
            If QueueVideoJob(r, v, t) AndAlso Not _running Then Await RunQueueAsync()
            Return
        End If
        Dim pics = _inputs.Where(Function(x) x IsNot Nothing).ToList()
        ' 🎭 Cast: who and where (ticked, or written as @Name in the prompt)
        Dim cast = CastLibrary.Involved(txtPrompt.Text, _cast)
        Dim castPics = CastLibrary.ReferencePictures(cast, 3)
        Dim before As MediaStudio.GenerateRequest = Nothing
        If cast.Count > 0 AndAlso castPics.Count > 0 AndAlso pics.Count = 0 Then
            If Not r.IsVideo Then
                ' pictures: models that take reference pictures put the same faces and places in the new picture
                If (t = "create" OrElse t = "combine") AndAlso r.Can("combine") Then
                    t = "combine"
                    pics = castPics.Take(Math.Max(1, r.Refs)).ToList()
                End If
            ElseIf r.Can("reference") AndAlso t <> "video-edit" Then
                ' MiniMax H3 Ref2VA: the cast pictures themselves are the references
                t = "reference"
                pics = castPics.Take(Math.Max(1, r.Refs)).ToList()
            ElseIf r.Can("video-edit") AndAlso t <> "video-edit" Then
                ' Wan VACE: "reference to video" with one sheet showing the whole cast
                t = "animate"
                pics = {CastLibrary.ReferenceSheet(cast, 832, 480)}.ToList()
            ElseIf t = "video-edit" Then
                pics = {CastLibrary.ReferenceSheet(cast, 832, 480)}.ToList()
            ElseIf r.Can("animate") Then
                ' photo → video models: first make the opening scene with the cast, then animate it
                Dim maker = CastSceneRecipe()
                If maker IsNot Nothing Then
                    t = "animate"
                    before = New MediaStudio.GenerateRequest With {
                        .Recipe = maker, .Variant = MediaStudio.InstalledVariant(maker), .Task = "combine",
                        .Prompt = CastLibrary.ComposePrompt(txtPrompt.Text.Trim(), _cast) & ", opening frame of a film scene, cinematic",
                        .Width = If(r.Width > 0, r.Width, 832), .Height = If(r.Height > 0, r.Height, 480),
                        .Steps = If(AppCore.Settings.MediaFast AndAlso maker.Speed IsNot Nothing, maker.Speed.Steps, maker.Steps),
                        .Cfg = If(AppCore.Settings.MediaFast AndAlso maker.Speed IsNot Nothing, maker.Speed.Cfg, maker.Cfg),
                        .Fast = AppCore.Settings.MediaFast AndAlso maker.Speed IsNot Nothing,
                        .InputImages = castPics.Take(Math.Max(1, maker.Refs)).ToList()}
                ElseIf r.Input = "required" Then
                    Localizer.Box(Me, "To use the cast with this photo → video model, LocalMind first makes the opening scene with a picture model that combines pictures (Qwen-Image Edit, FLUX.2 klein or Qwen-Image 2.1). Download one of them, or choose a start picture yourself.", "Cast")
                    Return
                End If
            End If
        End If
        Dim noText = t = "upscale" OrElse t = "remove-bg" OrElse t = "restore"
        If Not noText AndAlso String.IsNullOrWhiteSpace(txtPrompt.Text) AndAlso t <> "pose" Then
            txtPrompt.Focus()
            Return
        End If
        Select Case t
            Case "edit", "restyle", "upscale", "inpaint", "layout", "outline", "restore", "remove-bg"
                If pics.Count = 0 Then
                    Localizer.Box(Me, "Choose the picture first (click the picture box).", "Picture")
                    Return
                End If
            Case "combine", "pose"
                If pics.Count < 2 Then
                    Localizer.Box(Me, "Choose at least two pictures to combine.", "Pictures")
                    Return
                End If
            Case "animate"
                If pics.Count = 0 AndAlso r.Input = "required" AndAlso before Is Nothing Then
                    Localizer.Box(Me, "This model animates a picture — choose an input image first.", "Input image")
                    Return
                End If
            Case "video-edit"
                If String.IsNullOrEmpty(_videoPath) Then
                    PickVideo()
                    If String.IsNullOrEmpty(_videoPath) Then Return
                End If
        End Select
        If t = "inpaint" AndAlso _maskPath Is Nothing Then
            PaintMask()
            If _maskPath Is Nothing Then Return
        End If

        Dim w = 1024, h = 1024
        If cmbSize.SelectedItem IsNot Nothing Then
            Dim wh = cmbSize.SelectedItem.ToString().Split("×"c)
            w = CInt(wh(0)) : h = CInt(wh(1))
        End If
        If {"edit", "inpaint", "restyle", "layout", "outline", "restore"}.Contains(t) AndAlso pics.Count > 0 Then
            ' Keep the picture's shape: same aspect ratio as the input, about the chosen size, multiples of 16.
            Try
                Using img = ImageTools.Load(pics(0))
                    Dim area = CDbl(w) * h
                    Dim k = Math.Sqrt(area / (CDbl(img.Width) * img.Height))
                    w = Math.Max(256, CInt(Math.Round(img.Width * k / 16)) * 16)
                    h = Math.Max(256, CInt(Math.Round(img.Height * k / 16)) * 16)
                End Using
            Catch
            End Try
        End If
        Dim style = If(cmbStyle.Enabled, Styles(Math.Max(0, cmbStyle.SelectedIndex)).Words, "")
        Dim req As New MediaStudio.GenerateRequest With {
            .Recipe = r, .Variant = v, .Task = t, .Prompt = CastLibrary.ComposePrompt(txtPrompt.Text.Trim(), _cast) & style,
            .Negative = If(String.IsNullOrWhiteSpace(txtNeg.Text), If(r.IsVideo, r.Negative, ""), txtNeg.Text.Trim()),
            .Width = w, .Height = h, .Steps = CInt(numSteps.Value), .Cfg = CDbl(numCfg.Value),
            .Seed = CLng(numSeed.Value), .Frames = CInt(numFrames.Value), .InputImages = pics, .MaskImage = _maskPath,
            .InputVideo = _videoPath, .Strength = CDbl(numStrength.Value), .Count = If(numCount.Enabled, CInt(numCount.Value), 1),
            .Fast = chkFast.Checked AndAlso r.Speed IsNot Nothing, .Loras = If(r.IsUpscaler OrElse r.Kind = "tool", New List(Of UserLora)(), WithCastLoras(ActiveLoras(r), r, ""))}

        ' Queue it: jobs run one after another, so you can keep adding while one is running.
        Dim pre = before
        Dim work As Func(Of Action(Of Integer, String), CancellationToken, Task(Of List(Of String))) =
            Async Function(prog, ct)
                If pre IsNot Nothing Then
                    ' the opening scene with the cast, then the video starts from it
                    Await MediaStudio.GenerateAsync(pre, Sub(p, tx) prog(p, Localizer.T("🎭 Making the opening scene with the cast") & " — " & Localizer.T(tx)), ct)
                    If pre.Outputs.Count > 0 Then req.InputImages = New List(Of String) From {pre.Outputs(0)}
                End If
                Await MediaStudio.GenerateAsync(req, prog, ct)
                Dim outs As New List(Of String)(req.Outputs)
                If pre IsNot Nothing Then outs.AddRange(pre.Outputs)
                Return outs
            End Function
        Enqueue($"{r.Name}: {Truncate(If(txtPrompt.Text.Trim(), t), 40)}", work)
        If Not _running Then Await RunQueueAsync()
    End Function

    Private Sub Enqueue(label As String, work As Func(Of Action(Of Integer, String), CancellationToken, Task(Of List(Of String))))
        _queue.Enqueue((label, work))
        UpdateQueueLabel()
    End Sub

    '──────────────── longer video jobs ────────────────
    ''' <summary>Width × height from the size box.</summary>
    Private Function ChosenSize() As (W As Integer, H As Integer)
        Dim w = 832, h = 480
        If cmbSize.SelectedItem IsNot Nothing Then
            Dim wh = cmbSize.SelectedItem.ToString().Split("×"c)
            w = CInt(wh(0)) : h = CInt(wh(1))
        End If
        Return (w, h)
    End Function

    ''' <summary>About the chosen size, but with the picture's shape (multiples of 16).</summary>
    Private Function ShapeOf(pic As String, w As Integer, h As Integer) As (W As Integer, H As Integer)
        If pic Is Nothing Then Return (w, h)
        Try
            Using img = ImageTools.Load(pic)
                Dim k = Math.Sqrt(CDbl(w) * h / (CDbl(img.Width) * img.Height))
                Return (Math.Max(256, CInt(Math.Round(img.Width * k / 16)) * 16), Math.Max(256, CInt(Math.Round(img.Height * k / 16)) * 16))
            End Using
        Catch
            Return (w, h)
        End Try
    End Function

    ''' <summary>A request with the settings on screen (model, prompt with the cast and style, size, steps…).</summary>
    Private Function BaseRequest(r As MediaRecipe, v As MediaVariant, task As String, prompt As String, size As (W As Integer, H As Integer)) As MediaStudio.GenerateRequest
        Dim style = If(cmbStyle.Enabled, Styles(Math.Max(0, cmbStyle.SelectedIndex)).Words, "")
        Return New MediaStudio.GenerateRequest With {
            .Recipe = r, .Variant = v, .Task = task, .Prompt = CastLibrary.ComposePrompt(prompt, _cast) & style,
            .Negative = If(String.IsNullOrWhiteSpace(txtNeg.Text), r.Negative, txtNeg.Text.Trim()),
            .Width = size.W, .Height = size.H, .Steps = CInt(numSteps.Value), .Cfg = CDbl(numCfg.Value),
            .Seed = CLng(numSeed.Value), .Frames = CInt(numFrames.Value),
            .Fast = chkFast.Checked AndAlso r.Speed IsNot Nothing, .Loras = WithCastLoras(ActiveLoras(r), r, prompt)}
    End Function

    ''' <summary>The cast members' own LoRAs (for this model family) added to the active ones.</summary>
    Private Function WithCastLoras(list As List(Of UserLora), r As MediaRecipe, prompt As String) As List(Of UserLora)
        Dim res = New List(Of UserLora)(list)
        For Each l In CastLibrary.LorasFor(CastLibrary.Involved(prompt & " " & txtPrompt.Text, _cast), r.Family)
            If Not res.Any(Function(x) x.Name.Equals(l.Name, StringComparison.OrdinalIgnoreCase)) Then res.Add(l)
        Next
        Return res
    End Function

    ''' <summary>Storyboard, first → last, talking character or extend: checks the inputs and queues the job.</summary>
    Private Function QueueVideoJob(r As MediaRecipe, v As MediaVariant, t As String) As Boolean
        Dim pics = _inputs.Where(Function(x) x IsNot Nothing).ToList()
        Dim size = ChosenSize()
        Dim text = txtPrompt.Text.Trim()
        Dim work As Func(Of Action(Of Integer, String), CancellationToken, Task(Of List(Of String))) = Nothing
        Dim label = r.Name
        Select Case t
            Case "first-last"
                If pics.Count < 2 Then
                    Localizer.Box(Me, "Choose the first picture and the last picture.", "Pictures")
                    Return False
                End If
                Dim req = BaseRequest(r, v, "first-last", If(text.Length > 0, text, "smooth natural motion from the first frame to the last frame"), ShapeOf(pics(0), size.W, size.H))
                req.InputImages = pics.Take(2).ToList()
                label &= ": " & Localizer.T("first → last")
                work = Async Function(prog, ct)
                           Await MediaStudio.GenerateAsync(req, prog, ct)
                           Return req.Outputs
                       End Function

            Case "talk"
                Dim cast = CastLibrary.Involved(text & " " & txtSay.Text, _cast)
                Dim chosenPic = pics.FirstOrDefault()
                Dim castPic = cast.Where(Function(m) Not m.IsLocation).Select(Function(m) CastLibrary.ImagePaths(m).FirstOrDefault()).FirstOrDefault(Function(f) f IsNot Nothing)
                Dim useRecording = UsesRecording()
                Dim words = txtSay.Text.Trim()
                Dim voiceName = PickedVoiceName()
                Dim scene = If(text.Length > 0, text, "a person is talking to the camera, natural lip movement and facial expressions")
                ' "@Anna: Hello!" lines: each character speaks with their own picture and voice
                Dim dialogue = If(useRecording, New List(Of (Member As CastLibrary.CastMember, Words As String))(), CastLibrary.ParseDialogue(words))
                Dim lines As New List(Of (Picture As String, VoiceName As String, Words As String, Prompt As String))
                If Not useRecording Then
                    If words.Length = 0 Then
                        txtSay.Focus()
                        Localizer.Box(Me, "Write the words to say, or choose a recording in the voice list.", "Talking character")
                        Return False
                    End If
                    Dim style = If(cmbStyle.Enabled, Styles(Math.Max(0, cmbStyle.SelectedIndex)).Words, "")
                    For Each d In dialogue
                        Dim m = d.Member
                        Dim p = If(m IsNot Nothing, CastLibrary.ImagePaths(m).FirstOrDefault(), Nothing)
                        If p Is Nothing Then p = If(chosenPic, castPic)
                        If p Is Nothing Then
                            Localizer.Box(Me, If(m IsNot Nothing, String.Format(Localizer.T("Add a picture to {0} in the cast first."), m.Name),
                                                 Localizer.T("Choose the picture of the person who talks (click the picture box), or tick a cast character with a picture.")), "Talking character")
                            Return False
                        End If
                        Dim voice = If(m IsNot Nothing AndAlso Not String.IsNullOrEmpty(m.Voice) AndAlso voiceName Is Nothing, m.Voice,
                                       If(voiceName, cast.FirstOrDefault(Function(c) Not c.IsLocation AndAlso Not String.IsNullOrEmpty(c.Voice) AndAlso dialogue.Count = 1)?.Voice))
                        Dim who = If(m IsNot Nothing, New List(Of CastLibrary.CastMember) From {m}, New List(Of CastLibrary.CastMember)(_cast))
                        lines.Add((p, voice, d.Words, CastLibrary.ComposePrompt(scene, who) & style))
                    Next
                    If VideoMaker.SpeechModel(lines.Any(Function(l) l.VoiceName IsNot Nothing)) Is Nothing Then
                        Localizer.Box(Me, "To speak the words, download a voice model first (🎵 Music & voice), or choose a recording in the voice list.", "Talking character")
                        Return False
                    End If
                End If
                Dim pic = If(lines.Count > 0, lines(0).Picture, If(chosenPic, castPic))
                If pic Is Nothing Then
                    Localizer.Box(Me, "Choose the picture of the person who talks (click the picture box), or tick a cast character with a picture.", "Talking character")
                    Return False
                End If
                Dim req = BaseRequest(r, v, "talk", scene, ShapeOf(pic, size.W, size.H))
                Dim recording = _audioPath
                label &= ": " & Localizer.T("talking") & " — " & Truncate(If(useRecording, Path.GetFileName(recording), words.Replace(vbCrLf, " ")), 30)
                work = Async Function(prog, ct)
                           If useRecording Then Return New List(Of String) From {Await VideoMaker.TalkAsync(req, pic, recording, prog, ct)}
                           Return New List(Of String) From {Await VideoMaker.ConversationAsync(req, lines, prog, ct)}
                       End Function

            Case "extend"
                If String.IsNullOrEmpty(_videoPath) Then
                    PickVideo()
                    If String.IsNullOrEmpty(_videoPath) Then Return False
                End If
                If text.Length = 0 Then
                    txtPrompt.Focus()
                    Return False
                End If
                Dim video = _videoPath
                Dim req = BaseRequest(r, v, "animate", text, size)
                label &= ": " & Localizer.T("extend") & " " & Path.GetFileName(video)
                work = Async Function(prog, ct)
                           Dim info = Await VideoTools.InfoAsync(video, ct)
                           If info.Width >= 256 AndAlso info.Height >= 256 Then
                               ' the video's shape, at about the chosen size (an enlarged video would be too big to make)
                               Dim k = Math.Min(1.0, Math.Sqrt(CDbl(req.Width) * req.Height / (CDbl(info.Width) * info.Height)))
                               req.Width = Math.Max(256, (CInt(info.Width * k) \ 16) * 16)
                               req.Height = Math.Max(256, (CInt(info.Height * k) \ 16) * 16)
                           End If
                           Return New List(Of String) From {Await VideoMaker.ExtendAsync(req, video, prog, ct)}
                       End Function

            Case "storyboard"
                Dim shots = VideoMaker.SplitShots(text)
                If shots.Count < 2 Then
                    Localizer.Box(Me, "Write one shot per line (at least two), or write an idea and press ✨ Improve prompt to turn it into shots.", "Storyboard")
                    Return False
                End If
                Dim style = If(cmbStyle.Enabled, Styles(Math.Max(0, cmbStyle.SelectedIndex)).Words, "")
                Dim composed = shots.Select(Function(x) CastLibrary.ComposePrompt(x, _cast) & style).ToList()
                Dim start = pics.FirstOrDefault()
                If start IsNot Nothing Then size = ShapeOf(start, size.W, size.H)
                Dim cast = CastLibrary.Involved(text, _cast)
                Dim reference As String = Nothing
                Dim opening As MediaStudio.GenerateRequest = Nothing
                If start Is Nothing AndAlso cast.Count > 0 AndAlso CastLibrary.ReferencePictures(cast, 1).Count > 0 Then
                    If r.Can("video-edit") OrElse r.Can("reference") Then
                        reference = CastLibrary.ReferenceSheet(cast, size.W, size.H)        ' Wan VACE / H3 Ref2VA: the same cast in every shot
                    ElseIf r.Can("animate") Then
                        opening = VideoMaker.CastSceneRequest(shots(0), cast, size.W, size.H)
                    End If
                End If
                If start Is Nothing AndAlso reference Is Nothing AndAlso opening Is Nothing AndAlso r.Input = "required" Then
                    Localizer.Box(Me, "This model starts from a picture: choose a start picture (click the picture box), or tick cast members and download a picture model that combines pictures (Qwen-Image Edit, FLUX.2 klein or Qwen-Image 2.1).", "Storyboard")
                    Return False
                End If
                Dim req = BaseRequest(r, v, "animate", "", size)
                label &= ": " & Localizer.T("storyboard") & $" ({shots.Count} " & Localizer.T("shots") & ")"
                work = Async Function(prog, ct) Await VideoMaker.StoryboardAsync(req, composed, start, reference, opening, prog, ct)
        End Select
        If work Is Nothing Then Return False
        Enqueue(label, work)
        Return True
    End Function

    ''' <summary>Sharper (AI enlarger if installed) and / or smoother (twice the frames) version of a video.</summary>
    Private Function QueueEnhance() As Boolean
        If String.IsNullOrEmpty(_videoPath) Then
            PickVideo()
            If String.IsNullOrEmpty(_videoPath) Then Return False
        End If
        If Not chkSharper.Checked AndAlso Not chkSmooth.Checked Then
            Localizer.Box(Me, "Tick 'Sharper' or 'Smoother' (or both).", "Enhance")
            Return False
        End If
        Dim sel = SelectedRecipe()
        Dim upscaler = If(sel IsNot Nothing AndAlso sel.IsUpscaler AndAlso MediaStudio.InstalledVariant(sel) IsNot Nothing, sel, VideoMaker.InstalledUpscaler())
        Dim video = _videoPath
        Dim sharper = chkSharper.Checked, smoother = chkSmooth.Checked
        Enqueue(Localizer.T("✨ Enhance") & " " & Path.GetFileName(video),
                Async Function(prog, ct) New List(Of String) From {Await VideoMaker.EnhanceAsync(video, upscaler, sharper, smoother, prog, ct)})
        Return True
    End Function

    ''' <summary>A chat model rewrites the idea as a strong prompt for this model (or as shots for a storyboard).</summary>
    Private Async Function ImprovePromptAsync(btn As Button) As Task
        Dim t = CurrentTask().Id
        btn.Enabled = False
        lblStatus.Text = Localizer.T("✨ Writing a better prompt…")
        Try
            Dim n = VideoMaker.SplitShots(txtPrompt.Text).Count
            Dim shots = If(n >= 2, Math.Min(10, n), 4)
            Dim text = Await VideoMaker.ImprovePromptAsync(txtPrompt.Text, SelectedRecipe(), t, shots, CancellationToken.None)
            If text.Length > 0 Then
                txtPrompt.Text = text.Replace(vbCrLf, vbLf).Replace(vbLf, vbCrLf)
                lblStatus.Text = Localizer.T("Prompt improved — change anything you like and press Generate.")
            Else
                lblStatus.Text = Localizer.T("The chat model gave no answer — try again or choose another chat model.")
            End If
        Catch ex As Exception
            lblStatus.Text = "⚠ " & ex.Message
        Finally
            btn.Enabled = True
        End Try
    End Function

    ''' <summary>A vision chat model looks at picture 1 and writes a prompt for it (handy for restyling or "make more like this").</summary>
    Private Async Function DescribeAsync() As Task
        Dim pic = _inputs.FirstOrDefault(Function(x) x IsNot Nothing)
        If pic Is Nothing Then
            PickInput(0)
            pic = _inputs(0)
            If pic Is Nothing Then Return
        End If
        Dim vision = AppCore.Library.All().FirstOrDefault(Function(m) Not String.IsNullOrEmpty(m.MmprojFile))
        If AppCore.Settings.Backend = BackendKind.BuiltIn AndAlso vision Is Nothing Then
            Localizer.Box(Me, "This needs a chat model that can see pictures (marked 👁 in the Model Manager). Download one first, e.g. Gemma 3 or Qwen3-VL.", "Describe picture")
            Return
        End If
        lblStatus.Text = Localizer.T("Looking at the picture…")
        Try
            Dim b64 = Convert.ToBase64String(File.ReadAllBytes(pic))
            Dim msgs As New List(Of ChatMessage) From {
                New ChatMessage With {.Role = "system", .Content = "You write prompts for an image generator. Describe the picture as one detailed English prompt: subject, setting, composition, style, colors, lighting, camera. Reply with the prompt only."},
                New ChatMessage With {.Role = "user", .Content = "Describe this picture as a prompt.", .Images = New List(Of String) From {b64}}}
            Dim opts As New ChatRequestOptions With {.Model = If(vision?.DisplayName, AppCore.Settings.LastModel), .Temperature = 0.3, .MaxTokens = 300, .EnableThinking = False}
            Dim backend = AppCore.Backend
            Dim res = Await Task.Run(Function() backend.StreamChatAsync(msgs, opts, Sub(t) Return, Sub(t) Return, CancellationToken.None))
            Dim text = If(res?.Content, "").Trim().Trim(""""c)
            If text.Length > 0 Then
                txtPrompt.Text = text
                lblStatus.Text = Localizer.T("Prompt written from the picture — edit it and press Generate.")
            End If
        Catch ex As Exception
            lblStatus.Text = "⚠ " & ex.Message
        End Try
    End Function

    Private Shared Function Truncate(s As String, n As Integer) As String
        Return If(s.Length <= n, s, s.Substring(0, n) & "…")
    End Function

    Private Sub UpdateQueueLabel()
        lblQueue.Text = If(_queue.Count > 0, $"⏳ {_queue.Count} " & Localizer.T("waiting"), "")
        btnStop.Visible = _running
    End Sub

    Private Async Function RunQueueAsync() As Task
        _running = True
        Dim doneCount = 0, failed = 0
        Dim stopped = False
        Try
            While _queue.Count > 0
                ' 🌙 overnight: wait until nobody uses the PC
                While chkIdle.Checked AndAlso IdleTime() < TimeSpan.FromMinutes(5) AndAlso _queue.Count > 0
                    lblStatus.Text = Localizer.T("🌙 Waiting until the PC is not used for 5 minutes…") & $" ({_queue.Count} " & Localizer.T("waiting") & ")"
                    btnStop.Visible = True
                    Await Task.Delay(5000)
                End While
                If _queue.Count = 0 Then Exit While
                Dim job = _queue.Dequeue()
                UpdateQueueLabel()
                _cts = New CancellationTokenSource()
                btnDownload.Enabled = False
                btnDelete.Enabled = False
                Dim sw = Diagnostics.Stopwatch.StartNew()
                Try
                    Dim outs = Await job.Work(Sub(p, tx) Me.Post(Sub()
                                                                         prg.Value = Math.Max(0, Math.Min(100, p))
                                                                         lblStatus.Text = job.Label & " — " & Localizer.T(tx)
                                                                     End Sub), _cts.Token)
                    lblStatus.Text = Localizer.T("Done") & $" · {sw.Elapsed.TotalSeconds:0}s"
                    outs = outs.Where(Function(f) f IsNot Nothing AndAlso File.Exists(f)).ToList()
                    For i = outs.Count - 1 To 0 Step -1
                        AddToGallery(outs(i), 0)
                    Next
                    If outs.Count > 0 Then ShowOutput(outs(0))
                    doneCount += 1
                Catch ex As OperationCanceledException
                    lblStatus.Text = Localizer.T("Stopped")
                    stopped = True
                Catch ex As Exception
                    failed += 1
                    lblStatus.Text = "⚠ " & ex.Message.Split({vbCr, vbLf}, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()
                    If _queue.Count = 0 AndAlso Not chkIdle.Checked AndAlso Not chkSleep.Checked Then Localizer.Box(Me, ex.Message, "Generation failed", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                Finally
                    _cts = Nothing
                End Try
            End While
        Finally
            _running = False
            UpdateQueueLabel()
            UpdateTaskUi()
            UpdateButtons()
        End Try
        ' tell the user (tray balloon; the phone page shows it too) and maybe put the PC to sleep
        If doneCount + failed > 1 OrElse chkIdle.Checked OrElse chkSleep.Checked OrElse Not ContainsFocus Then
            Notifier.Notify(Localizer.T("LocalMind studio"), String.Format(Localizer.T("{0} done"), doneCount) & If(failed > 0, ", " & String.Format(Localizer.T("{0} failed"), failed), ""))
        End If
        If chkSleep.Checked AndAlso Not stopped AndAlso doneCount > 0 Then
            chkSleep.Checked = False
            Await Task.Delay(15000)          ' a moment to see it
            If Not _running Then Application.SetSuspendState(PowerState.Suspend, False, False)
        End If
    End Function

    ''' <summary>Subtitles for a video: asks the languages and the style, then queues the job.</summary>
    Private Sub QueueSubtitles(video As String)
        Using f As New Form() With {.Text = Localizer.T("💬 Subtitles"), .Size = New Size(460, 300), .FormBorderStyle = FormBorderStyle.FixedDialog,
                                    .StartPosition = FormStartPosition.CenterParent, .MinimizeBox = False, .MaximizeBox = False}
            Theme.ApplyForm(f)
            Dim langs = VideoMaker.SubtitleLanguages
            Dim cmbSpoken As New ComboBox() With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 220, .Location = New Point(200, 20)}
            cmbSpoken.Items.Add(Localizer.T("Detect automatically"))
            cmbSpoken.Items.AddRange(langs.Select(Function(l) CObj(Localizer.T(l.Name))).ToArray())
            cmbSpoken.SelectedIndex = 0
            Dim cmbTarget As New ComboBox() With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 220, .Location = New Point(200, 60)}
            cmbTarget.Items.Add(Localizer.T("Same as spoken"))
            cmbTarget.Items.AddRange(langs.Select(Function(l) CObj(Localizer.T(l.Name))).ToArray())
            cmbTarget.SelectedIndex = 0
            Theme.StyleInput(cmbSpoken) : Theme.StyleInput(cmbTarget)
            Dim l1 = Theme.MakeLabel(Localizer.T("Spoken language")) : l1.Location = New Point(16, 24)
            Dim l2 = Theme.MakeLabel(Localizer.T("Subtitles in")) : l2.Location = New Point(16, 64)
            Dim rbBurn As New RadioButton() With {.Text = Localizer.T("Write them into the picture (always visible)"), .AutoSize = True, .Checked = True, .ForeColor = Theme.CText, .Location = New Point(16, 104)}
            Dim rbTrack As New RadioButton() With {.Text = Localizer.T("As a track players can turn on and off"), .AutoSize = True, .ForeColor = Theme.CText, .Location = New Point(16, 132)}
            Dim ok = Theme.MakeButton(Localizer.T("💬 Make subtitles"), primary:=True, width:=180)
            ok.Location = New Point(240, 200)
            ok.DialogResult = DialogResult.OK
            f.Controls.AddRange({l1, cmbSpoken, l2, cmbTarget, rbBurn, rbTrack, ok})
            f.AcceptButton = ok
            If f.ShowDialog(Me) <> DialogResult.OK Then Return
            Dim spoken = If(cmbSpoken.SelectedIndex > 0, langs(cmbSpoken.SelectedIndex - 1).Code, "auto")
            Dim target = If(cmbTarget.SelectedIndex > 0, langs(cmbTarget.SelectedIndex - 1).Code, "")
            Dim burn = rbBurn.Checked
            Enqueue(Localizer.T("💬 Subtitles") & " " & Path.GetFileName(video),
                    Async Function(prog, ct) New List(Of String) From {(Await VideoMaker.SubtitlesAsync(video, spoken, target, burn, prog, ct)).Video})
        End Using
        If Not _running Then
            Dim __ = RunQueueAsync()
        End If
    End Sub

    ''' <summary>Loads the model, task, prompt and settings a past result was made with.</summary>
    Private Sub ReuseSettings(output As String)
        Dim s = MediaStudio.LoadSettingsFor(output)
        If s Is Nothing Then
            Localizer.Box(Me, "No saved settings for this file (made with an older version).", "Reuse settings")
            Return
        End If
        Dim r = MediaStudio.Recipes().FirstOrDefault(Function(x) x.Id = s.Recipe)
        If r Is Nothing Then Return
        Dim ti = Array.FindIndex(TaskList, Function(x) x.Id = s.Task AndAlso x.Video = r.IsVideo)
        If ti < 0 Then ti = Array.FindIndex(TaskList, Function(x) x.Id = "create" AndAlso x.Video = r.IsVideo)
        cmbShow.SelectedIndex = 1          ' all models, so the one used is surely listed
        cmbTask.SelectedIndex = ti
        FillGridKeepSelection(r)
        Dim vi = r.Variants.FindIndex(Function(x) x.Label = s.Variant)
        If vi >= 0 Then cmbVariant.SelectedIndex = vi
        txtPrompt.Text = s.Prompt
        txtNeg.Text = s.Negative
        Dim size = $"{s.Width}×{s.Height}"
        If Not cmbSize.Items.Contains(size) Then cmbSize.Items.Insert(0, size)
        cmbSize.SelectedItem = size
        chkFast.Checked = s.Fast
        numSteps.Value = Math.Max(numSteps.Minimum, Math.Min(numSteps.Maximum, s.Steps))
        numCfg.Value = CDec(Math.Min(20, s.Cfg))
        numSeed.Value = Math.Min(numSeed.Maximum, s.Seed)
        If s.Frames > 0 Then numFrames.Value = Math.Max(1, Math.Min(numFrames.Maximum, s.Frames))
        numStrength.Value = CDec(Math.Max(0.05, Math.Min(2, s.Strength)))
        cmbStyle.SelectedIndex = 0
        For i = 0 To 2
            SetInput(i, If(i < s.Inputs.Count AndAlso File.Exists(s.Inputs(i)), s.Inputs(i), Nothing))
        Next
        lblStatus.Text = Localizer.T("Settings loaded — change what you like and press Generate. Seed") & $" {s.Seed}"
    End Sub

    '──────────────── preview & gallery ────────────────
    Private Sub ShowHtml(body As String)
        If Not _previewReady Then Return
        preview.CoreWebView2.NavigateToString("<html><body style='margin:0;background:#1e1e1e;display:flex;align-items:center;justify-content:center;height:100vh'>" & body & "</body></html>")
    End Sub

    Private Sub ShowOutput(path As String)
        Dim url = "https://outputs.localmind/" & Uri.EscapeDataString(IO.Path.GetFileName(path))
        If IsVideoFile(path) Then
            ShowHtml($"<video src='{url}' controls autoplay loop style='max-width:100%;max-height:100%'></video>")
        ElseIf path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) Then
            ShowHtml($"<audio src='{url}' controls autoplay></audio>")
        Else
            ShowHtml($"<img src='{url}' style='max-width:100%;max-height:100%;object-fit:contain'>")
        End If
    End Sub

    Private Sub LoadGallery()
        gallery.Items.Clear()
        thumbs.Images.Clear()
        If txtSearch IsNot Nothing AndAlso (txtSearch.Text.Trim().Length > 0 OrElse chkFavs.Checked) Then
            For Each f In History.Find(txtSearch.Text.Trim().Replace("@", ""), chkFavs.Checked, 120)
                AddToGallery(f, -1)
            Next
            Return
        End If
        Dim files = Directory.GetFiles(MediaStudio.OutputFolder).Where(Function(f) (f.EndsWith(".png") OrElse f.EndsWith(".webm") OrElse f.EndsWith(".mp4")) AndAlso Not Path.GetFileName(f).Contains("-shot")).
                    OrderByDescending(Function(f) File.GetCreationTime(f)).Take(80)
        For Each f In files
            AddToGallery(f, -1)
        Next
    End Sub

    Private Sub AddToGallery(path As String, index As Integer)
        Dim key = path
        Try
            If path.EndsWith(".png") Then
                Using fs As New FileStream(path, FileMode.Open, FileAccess.Read)
                    Using img = Image.FromStream(fs)
                        Dim sz = FitThumb(img.Size)
                        Dim bmp As New Bitmap(96, 96)
                        Using g = Graphics.FromImage(bmp)
                            g.Clear(Theme.CBack)
                            g.InterpolationMode = Drawing2D.InterpolationMode.HighQualityBicubic
                            g.DrawImage(img, (96 - sz.Width) \ 2, (96 - sz.Height) \ 2, sz.Width, sz.Height)
                        End Using
                        thumbs.Images.Add(key, bmp)
                    End Using
                End Using
            Else
                Dim bmp As New Bitmap(96, 96)
                Using g = Graphics.FromImage(bmp)
                    g.Clear(Theme.CRaised)
                    TextRenderer.DrawText(g, "🎬", New Font("Segoe UI Emoji", 28), New Rectangle(0, 0, 96, 96), Color.White,
                                          TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
                End Using
                thumbs.Images.Add(key, bmp)
            End If
        Catch
            Return
        End Try
        Dim nm = IO.Path.GetFileNameWithoutExtension(path)
        Dim it As New ListViewItem(If(History.IsFavorite(path), "★ ", "") & nm.Substring(0, Math.Min(15, nm.Length)), key) With {.Tag = path}
        If index < 0 Then gallery.Items.Add(it) Else gallery.Items.Insert(index, it)
    End Sub

    Private Shared Function FitThumb(s As Size) As Size
        Dim k = 96.0 / Math.Max(s.Width, s.Height)
        Return New Size(Math.Max(1, CInt(s.Width * k)), Math.Max(1, CInt(s.Height * k)))
    End Function

    Protected Overrides Sub OnFormClosing(e As FormClosingEventArgs)
        _cts?.Cancel()
        MediaStudio.Cancel()
        MyBase.OnFormClosing(e)
    End Sub
End Class
