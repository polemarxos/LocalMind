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

''' <summary>Image & video studio: pick a model that fits the GPU, download it, describe, generate.</summary>
Public Class StudioForm
    Inherits Form

    Private ReadOnly _hw As HardwareInfo = HardwareInfo.Current()
    Private ReadOnly cmbShow, cmbKind, cmbVariant, cmbSize As ComboBox
    Private ReadOnly grid As DataGridView
    Private ReadOnly lblAbout, lblStatus As Label
    Private ReadOnly btnDownload, btnDelete, btnGenerate, btnInput, btnClearInput As Button
    Private ReadOnly prg As ProgressBar
    Private ReadOnly txtPrompt, txtNeg As TextBox
    Private ReadOnly numSteps, numCfg, numSeed, numFrames As NumericUpDown
    Private ReadOnly picInput As PictureBox
    Private ReadOnly preview As WebView2
    Private ReadOnly gallery As ListView
    Private ReadOnly thumbs As New ImageList() With {.ImageSize = New Size(96, 96), .ColorDepth = ColorDepth.Depth32Bit}
    Private _inputPath As String
    Private _cts As CancellationTokenSource
    Private _previewReady As Boolean

    Public Sub New()
        Text = "Image & video studio"
        Size = New Size(1400, 900)
        MinimumSize = New Size(1100, 700)
        StartPosition = FormStartPosition.CenterParent
        Theme.ApplyForm(Me)

        '──── left: models ────
        Dim left As New Panel() With {.Dock = DockStyle.Left, .Width = 470, .Padding = New Padding(8), .BackColor = Theme.CRaised}
        Dim filt As New FlowLayoutPanel() With {.Dock = DockStyle.Top, .Height = 36}
        cmbShow = New ComboBox() With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 230}
        cmbShow.Items.AddRange({Localizer.T("★ Best for my GPU"), Localizer.T("All models (show everything)")})
        cmbShow.SelectedIndex = 0
        cmbKind = New ComboBox() With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 190}
        cmbKind.Items.AddRange({Localizer.T("Images & video"), Localizer.T("Images"), Localizer.T("Video")})
        cmbKind.SelectedIndex = 0
        Theme.StyleInput(cmbShow) : Theme.StyleInput(cmbKind)
        filt.Controls.AddRange({cmbShow, cmbKind})

        grid = New DataGridView() With {.Dock = DockStyle.Fill, .ReadOnly = True}
        Theme.StyleGrid(grid)
        grid.Columns.Add("name", "Model")
        grid.Columns.Add("kind", "Type")
        grid.Columns.Add("best", "Best for you")
        grid.Columns.Add("fit", "On this PC")
        grid.Columns(0).AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
        grid.Columns(1).Width = 55
        grid.Columns(2).Width = 120
        grid.Columns(3).Width = 130

        Dim detail As New Panel() With {.Dock = DockStyle.Bottom, .Height = 190, .Padding = New Padding(0, 8, 0, 0)}
        lblAbout = New Label() With {.Dock = DockStyle.Top, .Height = 58, .ForeColor = Theme.CDim}
        cmbVariant = New ComboBox() With {.Dock = DockStyle.Top, .DropDownStyle = ComboBoxStyle.DropDownList}
        Theme.StyleInput(cmbVariant)
        Dim dlBar As New FlowLayoutPanel() With {.Dock = DockStyle.Top, .Height = 40}
        btnDownload = Theme.MakeButton("⬇ Download", primary:=True)
        btnDelete = Theme.MakeButton("🗑 Delete")
        dlBar.Controls.AddRange({btnDownload, btnDelete})
        prg = New ProgressBar() With {.Dock = DockStyle.Top, .Height = 14}
        lblStatus = New Label() With {.Dock = DockStyle.Top, .Height = 40, .ForeColor = Theme.CDim}
        detail.Controls.Add(lblStatus)
        detail.Controls.Add(prg)
        detail.Controls.Add(dlBar)
        detail.Controls.Add(cmbVariant)
        detail.Controls.Add(lblAbout)
        left.Controls.Add(grid)
        left.Controls.Add(filt)
        left.Controls.Add(detail)

        '──── right: create ────
        Dim right As New Panel() With {.Dock = DockStyle.Fill, .Padding = New Padding(10)}
        Dim form As New TableLayoutPanel() With {.Dock = DockStyle.Top, .Height = 250, .ColumnCount = 2}
        form.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        form.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 150))
        txtPrompt = New TextBox() With {.Dock = DockStyle.Fill, .Multiline = True, .ScrollBars = ScrollBars.Vertical, .Height = 90,
                                        .Font = New Font("Segoe UI", 10.5!), .PlaceholderText = "Describe the image or video (English works best)…"}
        txtNeg = New TextBox() With {.Dock = DockStyle.Fill, .PlaceholderText = "Negative prompt (things to avoid) — optional"}
        Theme.StyleInput(txtPrompt) : Theme.StyleInput(txtNeg)
        picInput = New PictureBox() With {.Dock = DockStyle.Fill, .SizeMode = PictureBoxSizeMode.Zoom, .BackColor = Theme.CBack, .Height = 90}
        Dim opts As New FlowLayoutPanel() With {.Dock = DockStyle.Fill, .Height = 70, .WrapContents = True}
        cmbSize = New ComboBox() With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 150}
        Theme.StyleInput(cmbSize)
        numSteps = New NumericUpDown() With {.Minimum = 1, .Maximum = 150, .Width = 60}
        numCfg = New NumericUpDown() With {.Minimum = 0, .Maximum = 20, .DecimalPlaces = 1, .Increment = 0.5D, .Width = 60}
        numSeed = New NumericUpDown() With {.Minimum = -1, .Maximum = Integer.MaxValue, .Value = -1, .Width = 110}
        numFrames = New NumericUpDown() With {.Minimum = 1, .Maximum = 161, .Increment = 4, .Width = 60}
        For Each n In {numSteps, numCfg, numSeed, numFrames}
            Theme.StyleInput(n)
        Next
        btnInput = Theme.MakeButton("🖼 Input image…")
        btnClearInput = Theme.MakeButton("✕")
        opts.Controls.AddRange({Theme.MakeLabel("Size"), cmbSize, Theme.MakeLabel("Steps"), numSteps, Theme.MakeLabel("CFG"), numCfg,
                                Theme.MakeLabel("Seed"), numSeed, Theme.MakeLabel("Frames"), numFrames, btnInput, btnClearInput})
        Dim genBar As New FlowLayoutPanel() With {.Dock = DockStyle.Fill, .Height = 44}
        btnGenerate = Theme.MakeButton("🎨 Generate", primary:=True, width:=160)
        btnGenerate.Font = Theme.BoldFont
        Dim btnFolder = Theme.MakeButton("📁 Open output folder")
        genBar.Controls.AddRange({btnGenerate, btnFolder})
        form.Controls.Add(txtPrompt, 0, 0) : form.Controls.Add(picInput, 1, 0)
        form.Controls.Add(txtNeg, 0, 1) : form.SetColumnSpan(txtNeg, 2)
        form.Controls.Add(opts, 0, 2) : form.SetColumnSpan(opts, 2)
        form.Controls.Add(genBar, 0, 3) : form.SetColumnSpan(genBar, 2)
        form.RowStyles.Add(New RowStyle(SizeType.Absolute, 96))
        form.RowStyles.Add(New RowStyle(SizeType.Absolute, 30))
        form.RowStyles.Add(New RowStyle(SizeType.Absolute, 74))
        form.RowStyles.Add(New RowStyle(SizeType.Absolute, 46))

        preview = New WebView2() With {.Dock = DockStyle.Fill, .DefaultBackgroundColor = Theme.CBack}
        gallery = New ListView() With {.Dock = DockStyle.Bottom, .Height = 132, .View = View.LargeIcon, .LargeImageList = thumbs,
                                       .BackColor = Theme.CBack, .ForeColor = Theme.CText, .BorderStyle = BorderStyle.None, .MultiSelect = False}
        right.Controls.Add(preview)
        right.Controls.Add(gallery)
        right.Controls.Add(form)

        Controls.Add(right)
        Controls.Add(left)

        '──── events ────
        AddHandler cmbShow.SelectedIndexChanged, Sub() FillGrid()
        AddHandler cmbKind.SelectedIndexChanged, Sub() FillGrid()
        AddHandler grid.SelectionChanged, Sub() ShowRecipe()
        AddHandler cmbVariant.SelectedIndexChanged, Sub() UpdateButtons()
        AddHandler btnDownload.Click, Async Sub() Await DownloadAsync()
        AddHandler btnDelete.Click, Sub() DeleteSelected()
        AddHandler btnGenerate.Click, Async Sub() Await GenerateAsync()
        AddHandler btnFolder.Click, Sub() ModelManagerForm.OpenFolder(MediaStudio.OutputFolder)
        AddHandler btnInput.Click, Sub() PickInput()
        AddHandler btnClearInput.Click, Sub() SetInput(Nothing)
        AddHandler gallery.SelectedIndexChanged, Sub()
                                                     If gallery.SelectedItems.Count > 0 Then ShowOutput(CStr(gallery.SelectedItems(0).Tag))
                                                 End Sub
        AddHandler gallery.ItemActivate, Sub()
                                             If gallery.SelectedItems.Count > 0 Then ModelManagerForm.OpenUrl(CStr(gallery.SelectedItems(0).Tag))
                                         End Sub
        Dim galleryMenu As New ContextMenuStrip()
        Dim miUse = galleryMenu.Items.Add(Localizer.T("Use as input"))
        AddHandler miUse.Click, Sub()
                                    If gallery.SelectedItems.Count > 0 Then SetInput(CStr(gallery.SelectedItems(0).Tag))
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
        gallery.ContextMenuStrip = galleryMenu
    End Sub

    Protected Overrides Async Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        FillGrid()
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

    '──────────────── model list ────────────────
    Private Function SelectedRecipe() As MediaRecipe
        Return If(grid.CurrentRow Is Nothing, Nothing, TryCast(grid.CurrentRow.Tag, MediaRecipe))
    End Function

    Private Sub FillGrid()
        Dim list = If(cmbShow.SelectedIndex = 0, MediaStudio.BestForGpu(_hw), MediaStudio.Recipes())
        If cmbKind.SelectedIndex = 1 Then list = list.Where(Function(r) Not r.IsVideo).ToList()
        If cmbKind.SelectedIndex = 2 Then list = list.Where(Function(r) r.IsVideo).ToList()
        grid.Rows.Clear()
        For Each r In list
            Dim v = MediaStudio.PickVariant(r, _hw)
            Dim fit = MediaStudio.Fit(r, v, _hw)
            Dim installed = MediaStudio.InstalledVariant(r)
            Dim i = grid.Rows.Add(If(installed IsNot Nothing, "✔ ", "") & r.Name, If(r.IsVideo, "🎬", "🖼"),
                                  $"{v.Label} · {Downloader.FormatBytes(v.Total)}", ModelCatalog.FitText(fit, _hw.VramBytes = 0))
            grid.Rows(i).Tag = r
            grid.Rows(i).Cells(3).Style.ForeColor = ModelManagerForm.FitColor(fit)
        Next
        If grid.Rows.Count = 0 Then lblAbout.Text = Localizer.T("No model fits fully on this GPU — choose 'All models' to see options that use system RAM.")
    End Sub

    Private Sub ShowRecipe()
        Dim r = SelectedRecipe()
        If r Is Nothing Then Return
        lblAbout.Text = r.About & If(r.Input = "required", "  " & Localizer.T("(needs an input image)"), "")
        cmbVariant.Items.Clear()
        Dim pick = MediaStudio.PickVariant(r, _hw)
        Dim installed = MediaStudio.InstalledVariant(r)
        For Each v In r.Variants
            cmbVariant.Items.Add(New VariantItem(r, v, _hw, v Is pick))
        Next
        Dim sel = If(installed, pick)
        cmbVariant.SelectedIndex = r.Variants.IndexOf(sel)

        ' Model defaults
        cmbSize.Items.Clear()
        Dim sizes = If(r.IsVideo, {"832×480", "480×832", "640×640", "1280×704", "704×1280"},
                                  {"1024×1024", "1152×896", "896×1152", "1344×768", "768×1344", "768×768", "1536×1024"})
        cmbSize.Items.AddRange(sizes)
        Dim def = $"{r.Width}×{r.Height}"
        cmbSize.SelectedItem = If(cmbSize.Items.Contains(def), def, sizes(0))
        numSteps.Value = Math.Max(1, r.Steps)
        numCfg.Value = CDec(Math.Min(20, r.Cfg))
        numFrames.Value = Math.Max(1, r.Frames)
        numFrames.Enabled = r.IsVideo
        txtNeg.Text = If(r.IsVideo, "", r.Negative)   ' Wan uses its own built-in negative unless the user types one
        btnInput.Enabled = r.Input <> "none"
        UpdateButtons()
    End Sub

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
        btnDownload.Enabled = Not installed AndAlso _cts Is Nothing
        btnDownload.Text = If(installed, Localizer.T("✔ Downloaded"), Localizer.T("⬇ Download") & $" ({Downloader.FormatBytes(MediaStudio.BytesToDownload(r, v))})")
        btnDelete.Enabled = installed AndAlso _cts Is Nothing
        btnGenerate.Enabled = installed OrElse _cts IsNot Nothing
    End Sub

    Private Async Function DownloadAsync() As Task
        Dim r = SelectedRecipe()
        Dim v = SelectedVariant()
        If r Is Nothing OrElse v Is Nothing Then Return
        _cts = New CancellationTokenSource()
        btnDownload.Enabled = False
        Try
            Await MediaStudio.DownloadAsync(r, v, Sub(p, t) BeginInvoke(Sub()
                                                                            prg.Value = Math.Max(0, Math.Min(100, p))
                                                                            lblStatus.Text = t
                                                                        End Sub), _cts.Token)
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

    '──────────────── input image ────────────────
    Private Sub PickInput()
        Using d As New OpenFileDialog() With {.Filter = "Images|*.png;*.jpg;*.jpeg;*.webp;*.bmp"}
            If d.ShowDialog(Me) = DialogResult.OK Then SetInput(d.FileName)
        End Using
    End Sub

    Private Sub SetInput(path As String)
        _inputPath = path
        Dim old = picInput.Image
        picInput.Image = Nothing
        old?.Dispose()
        If path IsNot Nothing AndAlso File.Exists(path) AndAlso Not path.EndsWith(".webm") Then
            Using fs As New FileStream(path, FileMode.Open, FileAccess.Read)
                picInput.Image = Image.FromStream(fs)
            End Using
        End If
    End Sub

    '──────────────── generate ────────────────
    Private Async Function GenerateAsync() As Task
        If _cts IsNot Nothing Then
            _cts.Cancel()
            MediaStudio.Cancel()
            Return
        End If
        Dim r = SelectedRecipe()
        Dim v = SelectedVariant()
        If r Is Nothing OrElse v Is Nothing Then Return
        If String.IsNullOrWhiteSpace(txtPrompt.Text) Then
            txtPrompt.Focus()
            Return
        End If
        If r.Input = "required" AndAlso String.IsNullOrEmpty(_inputPath) Then
            Localizer.Box(Me, "This model animates a picture — choose an input image first.", "Input image")
            Return
        End If
        Dim wh = cmbSize.SelectedItem.ToString().Split("×"c)
        Dim req As New MediaStudio.GenerateRequest With {
            .Recipe = r, .Variant = v, .Prompt = txtPrompt.Text.Trim(),
            .Negative = If(String.IsNullOrWhiteSpace(txtNeg.Text), If(r.IsVideo, r.Negative, ""), txtNeg.Text.Trim()),
            .Width = CInt(wh(0)), .Height = CInt(wh(1)), .Steps = CInt(numSteps.Value), .Cfg = CDbl(numCfg.Value),
            .Seed = CLng(numSeed.Value), .Frames = CInt(numFrames.Value), .InputImage = _inputPath}

        ' Free VRAM: the chat model and image model can't both sit on a small GPU.
        If AppCore.Engine.CurrentModel IsNot Nothing AndAlso AppCore.Engine.ActiveRequests = 0 Then Await AppCore.Engine.StopAsync()

        _cts = New CancellationTokenSource()
        btnGenerate.Text = Localizer.T("■ Stop")
        btnDownload.Enabled = False
        btnDelete.Enabled = False
        Dim sw = Diagnostics.Stopwatch.StartNew()
        Try
            Dim out = Await MediaStudio.GenerateAsync(req, Sub(p, t) BeginInvoke(Sub()
                                                                                    prg.Value = Math.Max(0, Math.Min(100, p))
                                                                                    lblStatus.Text = Localizer.T(t)
                                                                                End Sub), _cts.Token)
            lblStatus.Text = Localizer.T("Done") & $" · {sw.Elapsed.TotalSeconds:0}s · {Path.GetFileName(out)}"
            AddToGallery(out, 0)
            ShowOutput(out)
        Catch ex As OperationCanceledException
            lblStatus.Text = Localizer.T("Stopped")
        Catch ex As Exception
            lblStatus.Text = "⚠ " & ex.Message.Split({vbCr, vbLf}, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()
            Localizer.Box(Me, ex.Message, "Generation failed", MessageBoxButtons.OK, MessageBoxIcon.Warning)
        Finally
            _cts = Nothing
            btnGenerate.Text = Localizer.T("🎨 Generate")
            UpdateButtons()
        End Try
    End Function

    '──────────────── preview & gallery ────────────────
    Private Sub ShowHtml(body As String)
        If Not _previewReady Then Return
        preview.CoreWebView2.NavigateToString("<html><body style='margin:0;background:#1e1e1e;display:flex;align-items:center;justify-content:center;height:100vh'>" & body & "</body></html>")
    End Sub

    Private Sub ShowOutput(path As String)
        Dim url = "https://outputs.localmind/" & Uri.EscapeDataString(IO.Path.GetFileName(path))
        If path.EndsWith(".webm", StringComparison.OrdinalIgnoreCase) Then
            ShowHtml($"<video src='{url}' controls autoplay loop style='max-width:100%;max-height:100%'></video>")
        Else
            ShowHtml($"<img src='{url}' style='max-width:100%;max-height:100%;object-fit:contain'>")
        End If
    End Sub

    Private Sub LoadGallery()
        gallery.Items.Clear()
        thumbs.Images.Clear()
        Dim files = Directory.GetFiles(MediaStudio.OutputFolder).Where(Function(f) f.EndsWith(".png") OrElse f.EndsWith(".webm")).
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
        Dim it As New ListViewItem(IO.Path.GetFileNameWithoutExtension(path).Substring(0, Math.Min(15, IO.Path.GetFileNameWithoutExtension(path).Length)), key) With {.Tag = path}
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
