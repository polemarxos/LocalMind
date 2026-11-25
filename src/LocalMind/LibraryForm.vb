Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.IO
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms

''' <summary>
''' 🖼 Library: every picture, video, song and voice clip LocalMind made, with the prompt that made it. Open, copy the
''' prompt, use it again in the chat, attach a picture to the chat, enlarge a picture, remove its background, delete.
''' </summary>
Public Class LibraryForm
    Inherits Form

    ''' <summary>Raised to put a prompt (and optionally a file to attach) into the chat box.</summary>
    Public Event ReuseRequested(prompt As String, attachFile As String)

    Private Shared ReadOnly PicExt As String() = {".png", ".jpg", ".jpeg", ".webp"}
    Private Shared ReadOnly VidExt As String() = {".mp4", ".webm", ".mov", ".avi"}
    Private Shared ReadOnly AudExt As String() = {".wav", ".mp3", ".flac", ".ogg", ".m4a"}

    Private ReadOnly cmbType As New ComboBox() With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 150}
    Private ReadOnly txtSearch As New TextBox() With {.Width = 260}
    Private ReadOnly list As New ListView()
    Private ReadOnly thumbs As New ImageList() With {.ImageSize = New Size(160, 120), .ColorDepth = ColorDepth.Depth32Bit}
    Private ReadOnly preview As New PictureBox() With {.Dock = DockStyle.Top, .Height = 260, .SizeMode = PictureBoxSizeMode.Zoom}
    Private ReadOnly lblInfo As Label
    Private ReadOnly txtPrompt As New TextBox() With {.Multiline = True, .ReadOnly = True, .ScrollBars = ScrollBars.Vertical, .Dock = DockStyle.Fill}
    Private ReadOnly lblStatus As Label
    Private ReadOnly actions As New FlowLayoutPanel() With {.Dock = DockStyle.Bottom, .Height = 118, .WrapContents = True}
    Private btnUpscale, btnCutout, btnAttach, btnEnhance As Button
    Private _cts As New CancellationTokenSource()
    Private _items As New List(Of String)()
    Private _busy As Boolean

    Public Sub New()
        Text = Localizer.T("🖼 Library — everything you made")
        Size = New Size(1180, 760)
        MinimumSize = New Size(860, 540)
        StartPosition = FormStartPosition.CenterParent
        Theme.ApplyForm(Me)

        Dim top As New FlowLayoutPanel() With {.Dock = DockStyle.Top, .Height = 44, .WrapContents = False, .Padding = New Padding(8, 6, 8, 2), .BackColor = Theme.CPanel}
        cmbType.Items.AddRange({Localizer.T("Everything"), Localizer.T("🖼 Pictures"), Localizer.T("🎬 Videos"), Localizer.T("🎵 Music"), Localizer.T("🗣 Voice")})
        cmbType.SelectedIndex = 0
        Theme.StyleInput(cmbType)
        txtSearch.PlaceholderText = Localizer.T("Search the prompts…")
        Theme.StyleInput(txtSearch)
        Dim btnFolder = Theme.MakeButton(Localizer.T("📁 Open folder"))
        Dim btnRefresh = Theme.MakeButton(Localizer.T("⟳ Refresh"))
        AddHandler btnFolder.Click, Sub() OpenWithShell(MediaStudio.OutputFolder)
        AddHandler btnRefresh.Click, Sub() LoadItems()
        AddHandler cmbType.SelectedIndexChanged, Sub() LoadItems()
        AddHandler txtSearch.TextChanged, Sub() LoadItems()
        top.Controls.AddRange({cmbType, txtSearch, btnRefresh, btnFolder})

        list.Dock = DockStyle.Fill
        list.View = View.LargeIcon
        list.LargeImageList = thumbs
        list.MultiSelect = True
        list.BackColor = Theme.CBack
        list.ForeColor = Theme.CText
        list.BorderStyle = BorderStyle.None
        AddHandler list.SelectedIndexChanged, Sub() ShowSelected()
        AddHandler list.DoubleClick, Sub() OpenSelected()
        AddHandler list.KeyDown, Sub(s, e)
                                     If e.KeyCode = Keys.Delete Then DeleteSelected()
                                 End Sub

        '──── right: preview, prompt, actions ────
        Dim side As New Panel() With {.Dock = DockStyle.Right, .Width = 400, .Padding = New Padding(10), .BackColor = Theme.CPanel}
        preview.BackColor = Theme.CBack
        lblInfo = Theme.MakeLabel("", dim_:=True)
        lblInfo.Dock = DockStyle.Top
        lblInfo.AutoSize = False
        lblInfo.Height = 66
        Theme.StyleInput(txtPrompt)
        txtPrompt.Dock = DockStyle.Fill
        lblStatus = Theme.MakeLabel("", dim_:=True)
        lblStatus.Dock = DockStyle.Bottom
        lblStatus.AutoSize = False
        lblStatus.Height = 24
        actions.BackColor = Theme.CPanel
        Dim btnOpen = Theme.MakeButton(Localizer.T("▶ Open"), primary:=True)
        Dim btnCopy = Theme.MakeButton(Localizer.T("📋 Copy prompt"))
        Dim btnReuse = Theme.MakeButton(Localizer.T("💬 Reuse in chat"))
        btnAttach = Theme.MakeButton(Localizer.T("📎 Attach to chat"))
        btnUpscale = Theme.MakeButton(Localizer.T("🔍 Enlarge 4×"))
        btnCutout = Theme.MakeButton(Localizer.T("✂ Remove background"))
        btnEnhance = Theme.MakeButton(Localizer.T("✨ Sharper & smoother"))
        Dim btnShow = Theme.MakeButton(Localizer.T("📁 Show file"))
        Dim btnDel = Theme.MakeButton(Localizer.T("🗑 Delete"))
        AddHandler btnOpen.Click, Sub() OpenSelected()
        AddHandler btnCopy.Click, Sub() CopyPrompt()
        AddHandler btnReuse.Click, Sub() Reuse(False)
        AddHandler btnAttach.Click, Sub() Reuse(True)
        AddHandler btnUpscale.Click, Async Sub() Await RunToolAsync("upscale")
        AddHandler btnCutout.Click, Async Sub() Await RunToolAsync("remove-bg")
        AddHandler btnEnhance.Click, Async Sub() Await RunToolAsync("enhance")
        AddHandler btnShow.Click, Sub()
                                      Dim f = SelectedFile()
                                      If f IsNot Nothing Then Diagnostics.Process.Start("explorer.exe", $"/select,""{f}""")
                                  End Sub
        AddHandler btnDel.Click, Sub() DeleteSelected()
        actions.Controls.AddRange({btnOpen, btnCopy, btnReuse, btnAttach, btnUpscale, btnCutout, btnEnhance, btnShow, btnDel})
        side.Controls.Add(txtPrompt)
        side.Controls.Add(lblInfo)
        side.Controls.Add(preview)
        side.Controls.Add(actions)
        side.Controls.Add(lblStatus)

        Controls.Add(list)
        Controls.Add(New Panel() With {.Dock = DockStyle.Right, .Width = 1, .BackColor = Theme.CBorder})
        Controls.Add(side)
        Controls.Add(top)
        Localizer.Apply(Me)
        LoadItems()
    End Sub

    Protected Overrides Sub OnFormClosed(e As FormClosedEventArgs)
        _cts.Cancel()
        preview.Image?.Dispose()
        MyBase.OnFormClosed(e)
    End Sub

    Private Shared Function KindOf(f As String) As String
        Dim ext = Path.GetExtension(f).ToLowerInvariant()
        If PicExt.Contains(ext) Then Return "picture"
        If VidExt.Contains(ext) Then Return "video"
        If AudExt.Contains(ext) Then
            Dim s = MediaStudio.LoadSettingsFor(f)
            Return If(s?.Kind = "voice", "voice", "music")
        End If
        Return Nothing
    End Function

    Private Sub LoadItems()
        _cts.Cancel()
        _cts = New CancellationTokenSource()
        Dim ct = _cts.Token
        Dim want = {"", "picture", "video", "music", "voice"}(Math.Max(0, cmbType.SelectedIndex))
        Dim q = txtSearch.Text.Trim()
        Dim files As New List(Of String)()
        Try
            files = Directory.EnumerateFiles(MediaStudio.OutputFolder).
                    Where(Function(f) KindOf(f) IsNot Nothing).
                    OrderByDescending(Function(f) File.GetLastWriteTime(f)).ToList()
        Catch
        End Try
        If want <> "" Then files = files.Where(Function(f) KindOf(f) = want).ToList()
        If q <> "" Then files = files.Where(Function(f) If(PromptOf(f), "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 OrElse
                                                         Path.GetFileName(f).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0).ToList()
        _items = files
        list.BeginUpdate()
        list.Items.Clear()
        For Each im As Image In thumbs.Images
            im.Dispose()
        Next
        thumbs.Images.Clear()
        thumbs.Images.Add("audio", Placeholder("🎵"))
        thumbs.Images.Add("voice", Placeholder("🗣"))
        thumbs.Images.Add("wait", Placeholder("…"))
        For Each f In files
            Dim k = KindOf(f)
            Dim cap = Path.GetFileNameWithoutExtension(f)
            Dim p = PromptOf(f)
            If Not String.IsNullOrWhiteSpace(p) Then cap = If(p.Length > 40, p.Substring(0, 40) & "…", p)
            Dim it As New ListViewItem(If(k = "video", "🎬 ", "") & cap) With {.Tag = f,
                .ImageKey = If(k = "music", "audio", If(k = "voice", "voice", "wait"))}
            list.Items.Add(it)
        Next
        list.EndUpdate()
        lblStatus.Text = String.Format(Localizer.T("{0} file(s) in {1}"), files.Count, MediaStudio.OutputFolder)
        Dim unused = LoadThumbsAsync(files, ct)
    End Sub

    Private Shared Function Placeholder(symbol As String) As Bitmap
        Dim b As New Bitmap(160, 120)
        Using g = Graphics.FromImage(b)
            g.Clear(Theme.CRaised)
            TextRenderer.DrawText(g, symbol, New Font("Segoe UI Emoji", 28), New Rectangle(0, 0, 160, 120), Theme.CDim,
                                  TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
        End Using
        Return b
    End Function

    Private Shared ReadOnly Property ThumbFolder As String
        Get
            Dim d = Path.Combine(AppPaths.Roaming, "LibraryThumbs")
            Directory.CreateDirectory(d)
            Return d
        End Get
    End Property

    Private Async Function LoadThumbsAsync(files As List(Of String), ct As CancellationToken) As Task
        For Each f In files
            If ct.IsCancellationRequested Then Return
            Dim k = KindOf(f)
            If k <> "picture" AndAlso k <> "video" Then Continue For
            Dim bmp As Bitmap = Nothing
            Try
                bmp = Await Task.Run(Async Function()
                                         Dim src = f
                                         If k = "video" Then
                                             Dim t = Path.Combine(ThumbFolder, Path.GetFileNameWithoutExtension(f) & "-" & File.GetLastWriteTime(f).Ticks.ToString() & ".jpg")
                                             If Not File.Exists(t) Then
                                                 Await VideoTools.FfmpegAsync($"-hide_banner -loglevel error -y -ss 0.3 -i ""{f}"" -frames:v 1 -vf scale=320:-1 ""{t}""", ct)
                                             End If
                                             If Not File.Exists(t) Then Return Nothing
                                             src = t
                                         End If
                                         Return Fit(src)
                                     End Function, ct)
            Catch ex As OperationCanceledException
                Return
            Catch
            End Try
            If bmp Is Nothing OrElse ct.IsCancellationRequested OrElse IsDisposed Then
                bmp?.Dispose()
                Continue For
            End If
            thumbs.Images.Add(f, bmp)
            For Each it As ListViewItem In list.Items
                If CStr(it.Tag) = f Then it.ImageKey = f : Exit For
            Next
        Next
    End Function

    ''' <summary>A 160×120 thumbnail, letterboxed.</summary>
    Private Shared Function Fit(file As String) As Bitmap
        Using img = ImageTools.Load(file)
            Dim b As New Bitmap(160, 120)
            Using g = Graphics.FromImage(b)
                g.Clear(Theme.CBack)
                g.InterpolationMode = Drawing2D.InterpolationMode.HighQualityBicubic
                Dim k = Math.Min(160 / img.Width, 120 / img.Height)
                Dim w = CInt(img.Width * k), h = CInt(img.Height * k)
                g.DrawImage(img, (160 - w) \ 2, (120 - h) \ 2, w, h)
            End Using
            Return b
        End Using
    End Function

    Private Shared Function PromptOf(f As String) As String
        Return MediaStudio.LoadSettingsFor(f)?.Prompt
    End Function

    Private Function SelectedFile() As String
        If list.SelectedItems.Count = 0 Then Return Nothing
        Return CStr(list.SelectedItems(0).Tag)
    End Function

    Private Sub ShowSelected()
        Dim f = SelectedFile()
        preview.Image?.Dispose()
        preview.Image = Nothing
        txtPrompt.Text = ""
        lblInfo.Text = ""
        If f Is Nothing OrElse Not File.Exists(f) Then Return
        Dim k = KindOf(f)
        Try
            If k = "picture" Then
                preview.Image = ImageTools.Load(f)
            ElseIf k = "video" Then
                Dim t = Directory.EnumerateFiles(ThumbFolder, Path.GetFileNameWithoutExtension(f) & "-*.jpg").FirstOrDefault()
                If t IsNot Nothing Then preview.Image = ImageTools.Load(t) Else preview.Image = Placeholder("🎬")
            Else
                preview.Image = Placeholder(If(k = "voice", "🗣", "🎵"))
            End If
        Catch
        End Try
        Dim s = MediaStudio.LoadSettingsFor(f)
        Dim info As New List(Of String)()
        info.Add(Path.GetFileName(f) & "  ·  " & Downloader.FormatBytes(New FileInfo(f).Length))
        If s IsNot Nothing Then
            If Not String.IsNullOrEmpty(s.Model) Then info.Add(s.Model) Else If Not String.IsNullOrEmpty(s.Recipe) Then info.Add(s.Recipe)
            If s.Width > 0 Then info.Add($"{s.Width}×{s.Height}" & If(s.Frames > 1, $" · {s.Frames} frames", "") & If(s.Seed > 0, $" · seed {s.Seed}", ""))
        End If
        info.Add(File.GetLastWriteTime(f).ToString("g"))
        lblInfo.Text = String.Join(vbCrLf, info)
        Dim parts As New List(Of String)()
        If s IsNot Nothing Then
            parts.Add(If(String.IsNullOrWhiteSpace(s.Prompt), Localizer.T("(no prompt saved)"), s.Prompt))
            If Not String.IsNullOrWhiteSpace(s.Negative) Then parts.Add(Localizer.T("Negative") & ": " & s.Negative)
            If s.Loras IsNot Nothing AndAlso s.Loras.Count > 0 Then parts.Add("LoRAs: " & String.Join(", ", s.Loras))
            If Not String.IsNullOrWhiteSpace(s.Lyrics) Then parts.Add(Localizer.T("Lyrics") & ":" & vbCrLf & s.Lyrics)
        Else
            parts.Add(Localizer.T("(no prompt saved for this file — it was made before LocalMind saved prompts, or elsewhere)"))
        End If
        txtPrompt.Text = String.Join(vbCrLf & vbCrLf, parts).Replace(vbLf, vbCrLf).Replace(vbCr & vbCrLf, vbCrLf)
        btnUpscale.Enabled = k = "picture"
        btnCutout.Enabled = k = "picture"
        btnAttach.Enabled = k = "picture" OrElse k = "music" OrElse k = "voice"
        btnEnhance.Enabled = k = "video"
    End Sub

    Private Sub OpenSelected()
        Dim f = SelectedFile()
        If f IsNot Nothing Then OpenWithShell(f)
    End Sub

    Private Shared Sub OpenWithShell(path As String)
        Try
            Diagnostics.Process.Start(New Diagnostics.ProcessStartInfo(path) With {.UseShellExecute = True})
        Catch
        End Try
    End Sub

    Private Sub CopyPrompt()
        Dim p = PromptOf(SelectedFile())
        If String.IsNullOrWhiteSpace(p) Then Return
        Try
            Clipboard.SetText(p)
            lblStatus.Text = Localizer.T("Prompt copied")
        Catch
        End Try
    End Sub

    Private Sub Reuse(attach As Boolean)
        Dim f = SelectedFile()
        If f Is Nothing Then Return
        RaiseEvent ReuseRequested(If(PromptOf(f), ""), If(attach, f, Nothing))
        lblStatus.Text = Localizer.T(If(attach, "Attached to the chat", "Prompt put in the chat box"))
    End Sub

    Private Sub DeleteSelected()
        Dim files = list.SelectedItems.Cast(Of ListViewItem)().Select(Function(i) CStr(i.Tag)).ToList()
        If files.Count = 0 Then Return
        If Localizer.Box(Me, String.Format(Localizer.T("Delete {0} file(s)? This cannot be undone."), files.Count), "Library",
                         MessageBoxButtons.YesNo, MessageBoxIcon.Warning) <> DialogResult.Yes Then Return
        preview.Image?.Dispose()
        preview.Image = Nothing
        For Each f In files
            For Each x In {f, MediaStudio.SettingsFile(f)}
                Try : If File.Exists(x) Then File.Delete(x)
                Catch : End Try
            Next
        Next
        LoadItems()
    End Sub

    ''' <summary>Enlarge / remove the background of a picture, or make a video sharper and smoother.</summary>
    Private Async Function RunToolAsync(kind As String) As Task
        Dim f = SelectedFile()
        If f Is Nothing OrElse _busy Then Return
        _busy = True
        actions.Enabled = False
        Dim prog = Sub(p As Integer, t As String)
                       Dim txt = If(p > 0, $"{Localizer.T(t)} ({p}%)", Localizer.T(t))
                       If InvokeRequired Then BeginInvoke(Sub() lblStatus.Text = txt) Else lblStatus.Text = txt
                   End Sub
        Try
            Dim result As String
            If kind = "enhance" Then
                result = Await VideoMaker.EnhanceAsync(f, VideoMaker.InstalledUpscaler(), True, True, prog, _cts.Token)
            Else
                Dim r = If(kind = "upscale",
                           If(VideoMaker.InstalledUpscaler(), MediaStudio.Recipes().FirstOrDefault(Function(x) x.IsUpscaler)),
                           MediaStudio.Recipes().FirstOrDefault(Function(x) x.Kind = "tool" AndAlso x.Can("remove-bg")))
                If r Is Nothing Then Throw New Exception(Localizer.T("This tool is not in the model list."))
                Dim v = If(MediaStudio.InstalledVariant(r), r.Variants.FirstOrDefault())
                If kind = "upscale" AndAlso Not MediaStudio.IsInstalled(r, v) Then Await MediaStudio.DownloadAsync(r, v, prog, _cts.Token)
                result = Await MediaStudio.GenerateAsync(New MediaStudio.GenerateRequest With {
                    .Recipe = r, .Variant = v, .Task = kind, .InputImages = New List(Of String) From {f}, .InputImage = f,
                    .Prompt = PromptOf(f)}, prog, _cts.Token)
            End If
            lblStatus.Text = "✔ " & Path.GetFileName(result)
            LoadItems()
            For Each it As ListViewItem In list.Items
                If CStr(it.Tag) = result Then it.Selected = True : it.EnsureVisible() : Exit For
            Next
        Catch ex As OperationCanceledException
            lblStatus.Text = Localizer.T("Stopped")
        Catch ex As Exception
            lblStatus.Text = "⚠ " & ex.Message.Split({vbCr, vbLf}, StringSplitOptions.RemoveEmptyEntries).LastOrDefault()
        Finally
            _busy = False
            actions.Enabled = True
        End Try
    End Function
End Class
