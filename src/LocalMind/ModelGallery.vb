Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.IO
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms

''' <summary>
''' Picture and video models as a gallery: a picture for every model, its page on the right and one-click install.
''' Two sources share the same look: LocalMind's list (Hugging Face models with their settings) and Civitai
''' (civitai.com, or civitai.red for everything) with its checkpoints and LoRAs. Several installs can run at once.
''' Used in the Model Manager (Picture and Video) and as the Civitai window of the studio and the LoRA list.
''' </summary>
Public Class ModelGallery
    Inherits UserControl

    Private Const ThumbSize As Integer = 168
    Private Const PageSize As Integer = 40
    Private ReadOnly _hw As HardwareInfo = HardwareInfo.Current()
    Private ReadOnly _kind As String               ' "picture", "video" or Nothing (any)
    Private ReadOnly _local As Boolean             ' the LocalMind list is offered as a source

    Private ReadOnly cmbSource, cmbType, cmbBase, cmbSort, cmbVersion As ComboBox
    Private ReadOnly txtSearch As TextBox
    Private ReadOnly chkNsfw As CheckBox
    Private ReadOnly btnKey As Button
    Private ReadOnly list As ListView
    Private ReadOnly thumbs As New ImageList() With {.ImageSize = New Size(ThumbSize, ThumbSize), .ColorDepth = ColorDepth.Depth32Bit}
    Private ReadOnly pic As PictureBox
    Private ReadOnly strip As FlowLayoutPanel
    Private ReadOnly lblTitle, lblInfo, lblStatus, lblListInfo As Label
    Private ReadOnly txtAbout As TextBox
    Private ReadOnly btnInstall, btnDelete, btnMore, btnOpen As Button

    Private _cursor As String
    Private _localItems As List(Of MediaRecipe)
    Private _localShown As Integer
    Private _searchCts, _picCts As CancellationTokenSource
    ' installs running now (several at once), by model key
    Private ReadOnly _installs As New Dictionary(Of String, (Cts As CancellationTokenSource, Pct As Integer))
    Private ReadOnly _thumbGate As New SemaphoreSlim(4)

    ''' <summary>Raised after something was installed or deleted.</summary>
    Public Event Changed()
    <System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)>
    Public Property InstalledSomething As Boolean

    ''' <param name="kind">"picture" or "video" to show only that kind; Nothing for both.</param>
    ''' <param name="localList">Offer LocalMind's own list as a source (the Model Manager); otherwise only Civitai.</param>
    ''' <param name="loras">Open on Civitai LoRAs instead of models.</param>
    ''' <param name="family">Model family to open on (e.g. "SDXL").</param>
    Public Sub New(Optional kind As String = Nothing, Optional localList As Boolean = True, Optional loras As Boolean = False, Optional family As String = Nothing)
        _kind = kind
        _local = localList
        Dock = DockStyle.Fill
        BackColor = Theme.CPanel

        '──── top bar ────
        Dim top As New FlowLayoutPanel() With {.Dock = DockStyle.Top, .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
                                               .WrapContents = True, .Padding = New Padding(6, 6, 6, 2), .BackColor = Theme.CPanel}
        cmbSource = MakeCombo(220)
        If _local Then cmbSource.Items.Add(Localizer.T("LocalMind list (Hugging Face)"))
        cmbSource.Items.AddRange({Localizer.T("Civitai (safe for work)"), Localizer.T("Civitai.red (everything, 18+)")})
        cmbType = MakeCombo(160)
        cmbType.Items.AddRange({Localizer.T("Models (checkpoints)"), Localizer.T("LoRAs")})
        cmbBase = MakeCombo(230)
        cmbSort = MakeCombo(160)
        txtSearch = New TextBox() With {.Width = 240, .Margin = New Padding(3, 6, 3, 3), .PlaceholderText = Localizer.T("Search…")}
        Theme.StyleInput(txtSearch)
        chkNsfw = New CheckBox() With {.Text = Localizer.T("Show NSFW / uncensored"), .AutoSize = True, .Checked = AppCore.Settings.ShowNsfw,
                                       .ForeColor = Theme.CText, .Margin = New Padding(10, 9, 3, 3)}
        ' one clear button for the Civitai key: opens a window with the steps to get one, a box to paste it, a check and Save
        btnKey = Theme.MakeButton(CivitaiKeyForm.ButtonText())
        btnKey.Margin = New Padding(10, 5, 3, 3)
        btnKey.UseMnemonic = False
        AddHandler btnKey.Click, Sub()
                                     CivitaiKeyForm.Edit(FindForm())
                                     btnKey.Text = CivitaiKeyForm.ButtonText()
                                 End Sub
        top.Controls.AddRange({cmbSource, cmbType, cmbBase, cmbSort, txtSearch, chkNsfw, btnKey})

        '──── right: the model's page ────
        Dim right As New Panel() With {.Dock = DockStyle.Right, .Width = 430, .Padding = New Padding(12, 10, 12, 10), .BackColor = Theme.CRaised}
        pic = New PictureBox() With {.Dock = DockStyle.Top, .Height = 330, .SizeMode = PictureBoxSizeMode.Zoom, .BackColor = Theme.CBack, .Cursor = Cursors.Hand}
        ' every picture of the model, small, under the big one (click one to see it large)
        strip = New FlowLayoutPanel() With {.Dock = DockStyle.Top, .Height = 74, .WrapContents = False, .AutoScroll = True,
                                            .Padding = New Padding(0, 4, 0, 0), .BackColor = Theme.CRaised}
        AddHandler pic.Click, Sub() If pic.Image IsNot Nothing Then ShowLarge(pic.Image)
        lblTitle = New Label() With {.Dock = DockStyle.Top, .Height = 34, .Font = New Font(Theme.UiFont.FontFamily, 12.5F, FontStyle.Bold), .ForeColor = Theme.CText,
                                     .UseMnemonic = False, .AutoEllipsis = True, .Padding = New Padding(0, 8, 0, 0)}
        lblInfo = New Label() With {.Dock = DockStyle.Top, .Height = 70, .ForeColor = Theme.CDim, .UseMnemonic = False}
        cmbVersion = New ComboBox() With {.Dock = DockStyle.Top, .DropDownStyle = ComboBoxStyle.DropDownList}
        Theme.StyleInput(cmbVersion)
        Dim bar As New FlowLayoutPanel() With {.Dock = DockStyle.Top, .Height = 46, .Padding = New Padding(0, 6, 0, 0), .WrapContents = False}
        btnInstall = Theme.MakeButton(Localizer.T("⬇ Install"), primary:=True, width:=150)
        btnDelete = Theme.MakeButton(Localizer.T("🗑 Delete"))
        btnOpen = Theme.MakeButton(Localizer.T("🌐 Open page"))
        bar.Controls.AddRange({btnInstall, btnDelete, btnOpen})
        lblStatus = New Label() With {.Dock = DockStyle.Top, .Height = 40, .ForeColor = Theme.CDim, .UseMnemonic = False}
        txtAbout = New TextBox() With {.Dock = DockStyle.Fill, .Multiline = True, .ReadOnly = True, .ScrollBars = ScrollBars.Vertical,
                                       .BorderStyle = BorderStyle.None, .BackColor = Theme.CRaised, .ForeColor = Theme.CText}
        ' the picture takes a fair share of the height, so the name, files and Install always show
        AddHandler right.Resize, Sub() pic.Height = Math.Max(110, Math.Min(360, CInt((right.ClientSize.Height - 370) * 0.8)))
        right.Controls.Add(txtAbout)
        right.Controls.Add(lblStatus)
        right.Controls.Add(bar)
        right.Controls.Add(cmbVersion)
        right.Controls.Add(lblInfo)
        right.Controls.Add(lblTitle)
        right.Controls.Add(strip)
        right.Controls.Add(pic)

        '──── the gallery ────
        list = New ListView() With {.Dock = DockStyle.Fill, .View = View.LargeIcon, .LargeImageList = thumbs, .MultiSelect = False,
                                    .BackColor = Theme.CBack, .ForeColor = Theme.CText, .BorderStyle = BorderStyle.None}
        Dim bottom As New FlowLayoutPanel() With {.Dock = DockStyle.Bottom, .Height = 40, .WrapContents = False, .Padding = New Padding(4), .BackColor = Theme.CPanel}
        btnMore = Theme.MakeButton(Localizer.T("More…"))
        lblListInfo = Theme.MakeLabel("", dim_:=True)
        lblListInfo.Margin = New Padding(10, 9, 3, 3)
        Dim btnStudio = Theme.MakeButton(Localizer.T("🎨 Open the studio"))
        AddHandler btnStudio.Click, Sub()
                                        Dim f = Application.OpenForms.OfType(Of StudioForm)().FirstOrDefault()
                                        If f Is Nothing Then
                                            f = New StudioForm()
                                            f.Show()
                                        Else
                                            f.Activate()
                                        End If
                                    End Sub
        bottom.Controls.AddRange({btnMore, lblListInfo})
        If _local Then bottom.Controls.Add(btnStudio)

        Controls.Add(list)
        Controls.Add(bottom)
        Controls.Add(right)
        Controls.Add(top)

        '──── start values ────
        cmbSource.SelectedIndex = If(_local, 0, If(AppCore.Settings.CivitaiRed AndAlso AppCore.Settings.CivitaiRedConfirmed, 1, 0))
        cmbType.SelectedIndex = If(loras, 1, 0)
        ' (screenshot checks: open on a source / family)
        Dim pvSrc = Environment.GetEnvironmentVariable("LOCALMIND_GALLERY_SOURCE")
        If pvSrc = "civitai" AndAlso _local Then cmbSource.SelectedIndex = 1
        FillChoices(If(family, Environment.GetEnvironmentVariable("LOCALMIND_GALLERY_FAMILY")))
        If pvSrc = "civitai" AndAlso Environment.GetEnvironmentVariable("LOCALMIND_GALLERY_FAMILY") = "LTX-2.5" Then
            Dim gi = Groups().FindIndex(Function(g) g.Family = "LTX-2.5")
            If gi >= 0 Then cmbBase.SelectedIndex = gi + 1
        End If

        AddHandler cmbSource.SelectedIndexChanged, Async Sub() Await SourceChangedAsync()
        AddHandler cmbType.SelectedIndexChanged, Async Sub()
                                                     FillChoices(Nothing)
                                                     Await SearchAsync(True)
                                                 End Sub
        For Each c In {cmbBase, cmbSort}
            AddHandler c.SelectedIndexChanged, Async Sub() Await SearchAsync(True)
        Next
        AddHandler txtSearch.KeyDown, Async Sub(s, e)
                                          If e.KeyCode = Keys.Enter Then
                                              e.SuppressKeyPress = True
                                              Await SearchAsync(True)
                                          End If
                                      End Sub
        Dim searchTimer As New System.Windows.Forms.Timer() With {.Interval = 450}
        AddHandler searchTimer.Tick, Async Sub()
                                         searchTimer.Stop()
                                         Await SearchAsync(True)
                                     End Sub
        AddHandler txtSearch.TextChanged, Sub()
                                              searchTimer.Stop()
                                              searchTimer.Start()
                                          End Sub
        AddHandler chkNsfw.CheckedChanged, Async Sub()
                                               AppCore.Settings.ShowNsfw = chkNsfw.Checked
                                               AppCore.SaveSettings()
                                               Await SearchAsync(True)
                                           End Sub
        AddHandler btnMore.Click, Async Sub() Await SearchAsync(False)
        AddHandler list.SelectedIndexChanged, Sub() ShowSelected()
        AddHandler cmbVersion.SelectedIndexChanged, Async Sub() Await ShowVersionAsync()
        AddHandler btnInstall.Click, Async Sub() Await InstallAsync()
        AddHandler btnDelete.Click, Sub() DeleteSelected()
        AddHandler btnOpen.Click, Sub()
                                      Dim url = PageUrl()
                                      If url IsNot Nothing Then ModelManagerForm.OpenUrl(url)
                                  End Sub
        UpdateSourceUi()
    End Sub

    Private Shared Function MakeCombo(width As Integer) As ComboBox
        Dim c As New ComboBox() With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = width, .Margin = New Padding(3, 6, 3, 3)}
        Theme.StyleInput(c)
        Return c
    End Function

    Private _started As Boolean

    ''' <summary>Loads the first page (call when the gallery is first shown).</summary>
    Public Async Sub Start()
        If _started Then Return
        _started = True
        Await SearchAsync(True)
    End Sub

    Protected Overrides Sub OnHandleCreated(e As EventArgs)
        MyBase.OnHandleCreated(e)
        If Visible AndAlso Parent IsNot Nothing Then BeginInvoke(Sub() Start())
    End Sub

    Protected Overrides Sub OnVisibleChanged(e As EventArgs)
        MyBase.OnVisibleChanged(e)
        If Visible AndAlso IsHandleCreated Then Start()
    End Sub

    '──────────────── sources and choices ────────────────
    Private ReadOnly Property IsLocal As Boolean
        Get
            Return _local AndAlso cmbSource.SelectedIndex = 0
        End Get
    End Property

    Private ReadOnly Property IsRed As Boolean
        Get
            Return cmbSource.SelectedIndex = If(_local, 2, 1)
        End Get
    End Property

    Private Function Groups() As List(Of (Label As String, BaseModels As String(), Family As String, Template As String))
        Return Civitai.Groups.Where(Function(g) _kind Is Nothing OrElse Civitai.IsVideoGroup(g) = (_kind = "video")).
                              Where(Function(g) cmbType.SelectedIndex = 1 OrElse g.Template IsNot Nothing).ToList()
    End Function

    Private Function LocalFamilies() As List(Of String)
        Return MediaStudio.Recipes().Where(AddressOf KindMatches).Select(Function(r) r.Family).Where(Function(f) f IsNot Nothing).Distinct().OrderBy(Function(f) f).ToList()
    End Function

    Private Function KindMatches(r As MediaRecipe) As Boolean
        If r.IsUpscaler OrElse r.Kind = "tool" Then Return _kind <> "video"
        Return _kind Is Nothing OrElse r.IsVideo = (_kind = "video")
    End Function

    Private Sub FillChoices(family As String)
        cmbBase.Items.Clear()
        cmbSort.Items.Clear()
        If IsLocal Then
            cmbBase.Items.Add(Localizer.T("All families"))
            Dim fams = LocalFamilies()
            cmbBase.Items.AddRange(fams.Select(Function(f) CObj(f)).ToArray())
            Dim i = If(family Is Nothing, -1, fams.FindIndex(Function(f) f = family))
            cmbBase.SelectedIndex = If(i >= 0, i + 1, 0)
            cmbSort.Items.AddRange({Localizer.T("Most downloaded"), Localizer.T("Installed first"), Localizer.T("Smallest first"), Localizer.T("Name")})
        Else
            cmbBase.Items.Add(Localizer.T(If(_kind = "video", "All video models", If(_kind = "picture", "All picture models", "All models LocalMind can use"))))
            Dim gs = Groups()
            cmbBase.Items.AddRange(gs.Select(Function(g) CObj(g.Label)).ToArray())
            Dim i = If(family Is Nothing, -1, gs.FindIndex(Function(g) LoraLibrary.FamilyMatches(g.Family, family)))
            cmbBase.SelectedIndex = If(i >= 0, i + 1, 0)
            cmbSort.Items.AddRange({"Most Downloaded", "Highest Rated", "Newest"})
        End If
        cmbSort.SelectedIndex = 0
    End Sub

    Private Sub UpdateSourceUi()
        cmbType.Visible = Not IsLocal
        chkNsfw.Visible = IsLocal
        btnKey.Visible = Not IsLocal
        txtSearch.PlaceholderText = Localizer.T(If(IsLocal, If(_kind = "video", "Search, e.g. minimax, 10eros, ltx", "Search, e.g. flux, krea, qwen-image"),
                                                   "Search Civitai, e.g. realistic, anime, 10eros"))
    End Sub

    Private Async Function SourceChangedAsync() As Task
        If IsRed AndAlso Not AppCore.Settings.CivitaiRedConfirmed Then
            If Localizer.Box(FindForm(), "civitai.red shows Civitai's full catalogue, including adult content. Continue only if you are 18 or older.",
                             "civitai.red", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) <> DialogResult.Yes Then
                cmbSource.SelectedIndex = If(_local, 1, 0)
                Return
            End If
            AppCore.Settings.CivitaiRedConfirmed = True
        End If
        If Not IsLocal Then
            AppCore.Settings.CivitaiRed = IsRed
            AppCore.SaveSettings()
        End If
        UpdateSourceUi()
        FillChoices(Nothing)
        Await SearchAsync(True)
    End Function

    '──────────────── search ────────────────
    Private Async Function SearchAsync(fresh As Boolean) As Task
        If Not _started Then Return
        _searchCts?.Cancel()
        _searchCts = New CancellationTokenSource()
        Dim ct = _searchCts.Token
        If fresh Then
            _cursor = Nothing
            _localShown = 0
            list.BeginUpdate()
            list.Items.Clear()
            list.EndUpdate()
            For Each img As Image In thumbs.Images
                img.Dispose()
            Next
            thumbs.Images.Clear()
        End If
        If IsLocal Then
            If fresh Then _localItems = LocalResults()
            Dim page = _localItems.Skip(_localShown).Take(PageSize).ToList()
            _localShown += page.Count
            list.BeginUpdate()
            For Each r In page
                Dim inst = MediaStudio.InstalledVariant(r) IsNot Nothing
                list.Items.Add(New ListViewItem(If(inst, "✔ ", "") & r.Name & $"  ·  ⬇{Compact(r.Downloads)}") With {.Tag = r, .ImageKey = "r:" & r.Id})
            Next
            list.EndUpdate()
            For Each r In page
                Dim unused = LoadRecipeThumbAsync(r, ct)
            Next
            lblListInfo.Text = String.Format(Localizer.T("{0} of {1} models"), _localShown, _localItems.Count)
            btnMore.Enabled = _localShown < _localItems.Count
            If fresh AndAlso list.Items.Count > 0 Then list.Items(0).Selected = True
            If list.Items.Count = 0 Then ClearPage()
            Return
        End If

        Dim gs = Groups()
        Dim sel = cmbBase.SelectedIndex
        Dim bases = If(sel >= 1 AndAlso sel - 1 < gs.Count, gs(sel - 1).BaseModels.AsEnumerable(), gs.SelectMany(Function(g) g.BaseModels))
        lblListInfo.Text = Localizer.T("Searching Civitai…")
        btnMore.Enabled = False
        Try
            Dim res = Await Civitai.SearchAsync(txtSearch.Text, cmbType.SelectedIndex = 1, bases, IsRed, CStr(cmbSort.SelectedItem), _cursor, ct)
            If ct.IsCancellationRequested Then Return
            _cursor = res.NextCursor
            list.BeginUpdate()
            For Each m In res.Items
                Dim v = m.Versions(0)
                list.Items.Add(New ListViewItem($"{m.Name}  ·  {v.BaseModel} · ⬇{Compact(m.Downloads)}") With {.Tag = m, .ImageKey = $"c:{m.Id}"})
            Next
            list.EndUpdate()
            For Each m In res.Items
                Dim v = m.Versions(0)
                ' the card's picture: the first one (civitai.red shows NSFW ones too; civitai.com the first safe one)
                Dim cover = v.Images.FirstOrDefault(Function(x) IsRed OrElse Not x.Nsfw)
                If cover.Url IsNot Nothing Then
                    Dim unused = LoadThumbAsync($"c:{m.Id}", Civitai.ThumbUrl(cover.Url, 320), ct)
                End If
            Next
            lblListInfo.Text = $"{list.Items.Count} " & Localizer.T("results")
            btnMore.Enabled = Not String.IsNullOrEmpty(_cursor)
            If fresh AndAlso list.Items.Count > 0 Then list.Items(0).Selected = True
            If list.Items.Count = 0 Then ClearPage()
        Catch ex As OperationCanceledException
        Catch ex As Exception
            lblListInfo.Text = "⚠ " & ex.Message
        End Try
    End Function

    Private Function LocalResults() As List(Of MediaRecipe)
        Dim q = txtSearch.Text.Trim().ToLowerInvariant()
        Dim items = MediaStudio.Recipes().Where(AddressOf KindMatches).ToList()
        If Not chkNsfw.Checked Then items = items.Where(Function(r) Not r.Uncensored).ToList()
        If cmbBase.SelectedIndex > 0 Then
            Dim fam = CStr(cmbBase.SelectedItem)
            items = items.Where(Function(r) r.Family = fam).ToList()
        End If
        If q.Length > 0 Then
            Dim words = q.Split(" "c, StringSplitOptions.RemoveEmptyEntries)
            items = items.Where(Function(r) words.All(Function(w) (r.Name & " " & r.Family & " " & r.Repo & " " & r.Id).ToLowerInvariant().Contains(w))).ToList()
        End If
        Select Case cmbSort.SelectedIndex
            Case 1 : Return items.OrderByDescending(Function(r) MediaStudio.InstalledVariant(r) IsNot Nothing).ThenByDescending(Function(r) r.Downloads).ToList()
            Case 2 : Return items.OrderBy(Function(r) MediaStudio.PickVariant(r, _hw).Total).ToList()
            Case 3 : Return items.OrderBy(Function(r) r.Name, StringComparer.OrdinalIgnoreCase).ToList()
            Case Else : Return items.OrderByDescending(Function(r) r.Downloads).ToList()
        End Select
    End Function

    Private Shared Function Compact(n As Long) As String
        Return If(n >= 1_000_000, $"{n / 1_000_000.0:0.#}M", If(n >= 1000, $"{n / 1000.0:0.#}k", n.ToString()))
    End Function

    '──────────────── pictures ────────────────
    Private Async Function LoadRecipeThumbAsync(r As MediaRecipe, ct As CancellationToken) As Task
        Try
            Await _thumbGate.WaitAsync(ct)
            Try
                Dim info = Await ModelDetails.GetAsync(r, chkNsfw.Checked, ct)
                Dim s = info.Samples.FirstOrDefault(Function(x) chkNsfw.Checked OrElse Not x.Nsfw)
                Dim img As Image = Nothing
                If s IsNot Nothing Then img = Await ModelDetails.GetImageAsync(s.Url, ct)
                If img Is Nothing Then
                    ' no example pictures on its Hugging Face page (most GGUF uploads): show one made with this family
                    Dim url = Await FamilyPictureAsync(r, ct)
                    If url IsNot Nothing Then img = Await ModelDetails.GetImageAsync(url, ct)
                End If
                If ct.IsCancellationRequested OrElse IsDisposed Then Return
                AddThumb("r:" & r.Id, If(img, TextTile(r.Family)))
            Finally
                _thumbGate.Release()
            End Try
        Catch
        End Try
    End Function

    ' Civitai's name for each model family, for example pictures of models that have none of their own.
    Private Shared ReadOnly CivitaiBase As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From {
        {"LTX-2.5", "LTXV 2.5"}, {"MiniMax-H3", "MiniMax H3"}, {"Qwen-Image", "Qwen"}, {"Qwen-Image-Edit", "Qwen"},
        {"FLUX.1", "Flux.1 D"}, {"FLUX.1-Kontext", "Flux.1 Kontext"}, {"FLUX.2", "Flux.2 Klein 9B"}, {"Krea", "Krea 2"},
        {"Z-Image", "ZImageTurbo"}, {"SDXL", "SDXL 1.0"}, {"SD1.5", "SD 1.5"}, {"Anima", "Anima"}}
    Private Shared ReadOnly _familyPics As New System.Collections.Concurrent.ConcurrentDictionary(Of String, Task(Of List(Of String)))

    Private Async Function FamilyPictureAsync(r As MediaRecipe, ct As CancellationToken) As Task(Of String)
        Dim base As String = Nothing
        If r.Family Is Nothing OrElse Not CivitaiBase.TryGetValue(r.Family, base) Then Return Nothing
        Dim nsfw = chkNsfw.Checked
        Dim urls = Await _familyPics.GetOrAdd(base & "|" & nsfw.ToString(),
            Function(k) Task.Run(Async Function()
                                     Dim res = Await Civitai.SearchAsync("", False, {base}, False, "Most Downloaded", Nothing, CancellationToken.None)
                                     Return res.Items.SelectMany(Function(m) m.Versions.Take(2)).
                                                Where(Function(v) v.ImageUrl IsNot Nothing AndAlso (nsfw OrElse Not v.ImageNsfw)).
                                                Select(Function(v) Civitai.ThumbUrl(v.ImageUrl, 320)).Distinct().ToList()
                                 End Function))
        If urls Is Nothing OrElse urls.Count = 0 Then Return Nothing
        ' each model gets its own picture of the family (the same one every time)
        Dim h = 0
        For Each c In r.Id
            h = (h * 31 + AscW(c)) And &HFFFFFF
        Next
        Return urls(h Mod urls.Count)
    End Function

    Private Async Function LoadThumbAsync(key As String, url As String, ct As CancellationToken) As Task
        Try
            Await _thumbGate.WaitAsync(ct)
            Try
                Dim img = Await ModelDetails.GetImageAsync(url, ct)
                If img Is Nothing OrElse ct.IsCancellationRequested OrElse IsDisposed Then Return
                AddThumb(key, img)
            Finally
                _thumbGate.Release()
            End Try
        Catch
        End Try
    End Function

    Private Sub AddThumb(key As String, img As Image)
        Dim bmp As New Bitmap(ThumbSize, ThumbSize)
        Using g = Graphics.FromImage(bmp)
            g.Clear(Theme.CBack)
            g.InterpolationMode = Drawing2D.InterpolationMode.HighQualityBicubic
            ' fill the square (crop the long side), like Civitai's cards
            Dim k = Math.Max(ThumbSize / img.Width, ThumbSize / img.Height)
            Dim w = CInt(img.Width * k), h = CInt(img.Height * k)
            g.DrawImage(img, (ThumbSize - w) \ 2, (ThumbSize - h) \ 2, w, h)
        End Using
        If thumbs.Images.ContainsKey(key) Then thumbs.Images.RemoveByKey(key)
        thumbs.Images.Add(key, bmp)
        list.Invalidate()
        ' the selected model's page shows the same picture until (or unless) a better one loads
        If pic.Image Is Nothing AndAlso list.SelectedItems.Count > 0 AndAlso list.SelectedItems(0).ImageKey = key Then pic.Image = img
    End Sub

    Private Shared Function TextTile(text As String) As Image
        Dim bmp As New Bitmap(ThumbSize, ThumbSize)
        Using g = Graphics.FromImage(bmp)
            g.Clear(Theme.CRaised)
            TextRenderer.DrawText(g, If(text, ""), New Font(Theme.UiFont.FontFamily, 12.0F, FontStyle.Bold), New Rectangle(0, 0, ThumbSize, ThumbSize), Theme.CDim,
                                  TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter Or TextFormatFlags.WordBreak)
        End Using
        Return bmp
    End Function

    '──────────────── the page on the right ────────────────
    Private Function SelectedTag() As Object
        Return If(list.SelectedItems.Count = 0, Nothing, list.SelectedItems(0).Tag)
    End Function

    Private Sub ClearPage()
        lblTitle.Text = ""
        lblInfo.Text = ""
        txtAbout.Text = ""
        cmbVersion.Items.Clear()
        pic.Image = Nothing
        btnInstall.Enabled = False
        btnDelete.Visible = False
    End Sub

    Private Sub ShowSelected()
        Dim tag = SelectedTag()
        cmbVersion.Items.Clear()
        If tag Is Nothing Then Return
        Dim r = TryCast(tag, MediaRecipe)
        If r IsNot Nothing Then
            Dim pick = If(MediaStudio.InstalledVariant(r), MediaStudio.PickVariant(r, _hw))
            For Each v In r.Variants
                cmbVersion.Items.Add(New VariantChoice(r, v, _hw))
            Next
            cmbVersion.SelectedIndex = Math.Max(0, r.Variants.IndexOf(pick))
        Else
            Dim m = CType(tag, Civitai.CivModel)
            For Each v In m.Versions
                cmbVersion.Items.Add(v)
            Next
            cmbVersion.SelectedIndex = 0
        End If
    End Sub

    Private Class VariantChoice
        Public ReadOnly V As MediaVariant
        Private ReadOnly _text As String
        Public Sub New(r As MediaRecipe, v As MediaVariant, hw As HardwareInfo)
            Me.V = v
            _text = $"{v.Label}  ·  {Downloader.FormatBytes(v.Total)}  ·  {ModelCatalog.FitText(MediaStudio.Fit(r, v, hw), hw.VramBytes = 0)}"
        End Sub
        Public Overrides Function ToString() As String
            Return _text
        End Function
    End Class

    Private Function Key(tag As Object, ver As Object) As String
        Dim r = TryCast(tag, MediaRecipe)
        If r IsNot Nothing Then Return "r:" & r.Id & ":" & CType(ver, VariantChoice).V.Label
        Return "c:" & CType(ver, Civitai.CivVersion).Id
    End Function

    Private Async Function ShowVersionAsync() As Task
        Dim tag = SelectedTag()
        Dim ver = cmbVersion.SelectedItem
        If tag Is Nothing OrElse ver Is Nothing Then Return
        _picCts?.Cancel()
        _picCts = New CancellationTokenSource()
        Dim ct = _picCts.Token
        Dim running As (Cts As CancellationTokenSource, Pct As Integer) = Nothing
        Dim busy = _installs.TryGetValue(Key(tag, ver), running)
        Dim r = TryCast(tag, MediaRecipe)
        Dim imgUrl As String = Nothing
        Dim stripUrls As New List(Of String)
        ClearStrip()

        If r IsNot Nothing Then
            Dim v = CType(ver, VariantChoice).V
            Dim installed = MediaStudio.IsInstalled(r, v)
            lblTitle.Text = r.Name
            lblInfo.Text = $"{r.Family} · {If(r.Repo, "").Split("/"c)(0)} · ⬇ {Compact(r.Downloads)}" & If(r.Uncensored, "  ·  🔓 " & Localizer.T("uncensored"), "") & vbLf &
                           MediaStudio.RequirementText(r, v, _hw) & vbLf &
                           If(installed, Localizer.T("✔ Installed"), Localizer.T("Download") & ": " & Downloader.FormatBytes(MediaStudio.BytesToDownload(r, v)))
            txtAbout.Text = r.About & vbCrLf & vbCrLf & String.Join(vbCrLf, ModelDetails.SettingsLines(r).Select(Function(x) "•  " & x))
            btnInstall.Text = If(busy, Localizer.T("⬇ In the download list"), If(installed, Localizer.T("✔ Installed"), Localizer.T("⬇ Install")))
            btnInstall.Enabled = Not busy AndAlso Not installed
            btnDelete.Visible = installed AndAlso Not busy
            pic.Image = Nothing
            Try
                Dim info = Await ModelDetails.GetAsync(r, chkNsfw.Checked, ct)
                If ct.IsCancellationRequested Then Return
                Dim allowed = info.Samples.Where(Function(x) chkNsfw.Checked OrElse Not x.Nsfw).ToList()
                Dim s = allowed.FirstOrDefault()
                If info.Description <> "" Then txtAbout.Text &= vbCrLf & vbCrLf & info.Description
                imgUrl = s?.Url
                stripUrls = allowed.Select(Function(x) x.Url).ToList()
                If imgUrl Is Nothing Then imgUrl = Await FamilyPictureAsync(r, ct)
            Catch
            End Try
        Else
            Dim m = CType(tag, Civitai.CivModel)
            Dim v = CType(ver, Civitai.CivVersion)
            Dim f = v.BestFile()
            Dim g = Civitai.GroupFor(v.BaseModel)
            Dim fitText = ""
            If Not m.IsLora AndAlso f IsNot Nothing AndAlso _hw.VramBytes > 0 Then
                fitText = If(f.SizeBytes + 2.5 * 1024 * 1024 * 1024 <= _hw.VramBytes, Localizer.T("✔ fits your GPU"), Localizer.T("uses system RAM too (slower)"))
            End If
            Dim installed = AlreadyInstalled(m, v)
            lblTitle.Text = m.Name
            lblInfo.Text = $"{Localizer.T("by")} {m.Creator} · ⬇ {Compact(m.Downloads)} · 👍 {Compact(m.Likes)}" & vbLf &
                           $"{m.Type} · {v.BaseModel}" & If(f IsNot Nothing, $" · {Downloader.FormatBytes(f.SizeBytes)}", "") & If(fitText <> "", " · " & fitText, "") & vbLf &
                           If(g.Family Is Nothing, Localizer.T("⚠ LocalMind can't use this base model."),
                              If(Not m.IsLora AndAlso g.Template Is Nothing, Localizer.T("⚠ LocalMind can't run checkpoints of this type yet — LoRAs work."),
                                 If(f Is Nothing, Localizer.T("⚠ No file LocalMind can run in this version."), "")))
            txtAbout.Text = If(v.TrainedWords.Count > 0, Localizer.T("Trigger words (added to your prompt automatically)") & ": " & String.Join(", ", v.TrainedWords), "")
            btnInstall.Text = If(busy, Localizer.T("⬇ In the download list"), If(installed, Localizer.T("✔ Installed"), Localizer.T("⬇ Install")))
            btnInstall.Enabled = Not busy AndAlso (Not installed AndAlso f IsNot Nothing AndAlso g.Family IsNot Nothing AndAlso (m.IsLora OrElse g.Template IsNot Nothing))
            btnDelete.Visible = False
            ' civitai.red: every picture, NSFW too; civitai.com: only the safe ones
            Dim shown = v.Images.Where(Function(x) IsRed OrElse Not x.Nsfw).Select(Function(x) x.Url).ToList()
            If shown.Count > 0 Then imgUrl = Civitai.ThumbUrl(shown(0), 640)
            stripUrls = shown.Select(Function(x) Civitai.ThumbUrl(x, 640)).ToList()
            If f IsNot Nothing Then
                Dim vv = v
                Dim unused = Task.Run(Async Function()
                                          Dim locked = False
                                          Try
                                              locked = Await Civitai.NeedsLoginAsync(f, CancellationToken.None)
                                          Catch
                                          End Try
                                          If locked AndAlso Not IsDisposed Then
                                              Me.Post(Sub()
                                                          If cmbVersion.SelectedItem Is vv Then lblInfo.Text &= vbLf & Localizer.T("🔒 Needs a Civitai account: click 🔑 Civitai API key at the top first.")
                                                      End Sub)
                                          End If
                                      End Function)
            End If
        End If
        pic.Image = Nothing
        FillStrip(stripUrls, ver, ct)
        If imgUrl Is Nothing Then Return
        Try
            Dim img = Await ModelDetails.GetImageAsync(imgUrl, ct)
            If Not ct.IsCancellationRequested AndAlso cmbVersion.SelectedItem Is ver Then pic.Image = img
        Catch
        End Try
    End Function

    Private Sub ClearStrip()
        For Each c As Control In strip.Controls.Cast(Of Control)().ToList()
            CType(c, PictureBox).Image?.Dispose()
            c.Dispose()
        Next
        strip.Controls.Clear()
    End Sub

    ''' <summary>Small pictures under the big one; a click shows that picture large.</summary>
    Private Sub FillStrip(urls As List(Of String), ver As Object, ct As CancellationToken)
        strip.Visible = urls.Count > 1
        If urls.Count <= 1 Then Return
        For Each u In urls.Take(20)
            Dim url = u
            Dim pb As New PictureBox() With {.Size = New Size(64, 64), .SizeMode = PictureBoxSizeMode.Zoom, .BackColor = Theme.CBack,
                                             .Margin = New Padding(0, 0, 6, 0), .Cursor = Cursors.Hand}
            strip.Controls.Add(pb)
            Dim unused = Task.Run(Async Function()
                                      Try
                                          Dim img = Await ModelDetails.GetImageAsync(url, ct)
                                          If img Is Nothing OrElse ct.IsCancellationRequested Then Return
                                          Me.Post(Sub()
                                                      If pb.IsDisposed Then Return
                                                      pb.Image = img
                                                  End Sub)
                                      Catch
                                      End Try
                                  End Function)
            AddHandler pb.Click, Sub()
                                     If pb.Image IsNot Nothing AndAlso cmbVersion.SelectedItem Is ver Then pic.Image = New Bitmap(pb.Image)
                                 End Sub
        Next
    End Sub

    ''' <summary>A picture at full size in its own window.</summary>
    Private Sub ShowLarge(img As Image)
        Using f As New Form() With {.Text = lblTitle.Text, .StartPosition = FormStartPosition.CenterParent, .BackColor = Color.Black,
                                    .Size = New Size(Math.Min(1200, img.Width + 40), Math.Min(900, img.Height + 60)), .ShowInTaskbar = False, .KeyPreview = True}
            Theme.DarkTitleBar(f)
            Dim big As New PictureBox() With {.Dock = DockStyle.Fill, .SizeMode = PictureBoxSizeMode.Zoom, .Image = img, .BackColor = Color.Black}
            f.Controls.Add(big)
            AddHandler big.Click, Sub() f.Close()
            AddHandler f.KeyDown, Sub(s2, e2) If e2.KeyCode = Keys.Escape Then f.Close()
            f.ShowDialog(FindForm())
            big.Image = Nothing
        End Using
    End Sub

    Private Function PageUrl() As String
        Dim tag = SelectedTag()
        Dim r = TryCast(tag, MediaRecipe)
        If r IsNot Nothing Then Return If(String.IsNullOrEmpty(r.Repo) OrElse r.Repo = "local", Nothing, "https://huggingface.co/" & r.Repo)
        Dim m = TryCast(tag, Civitai.CivModel)
        Return If(m Is Nothing, Nothing, $"https://{If(IsRed, "civitai.red", "civitai.com")}/models/{m.Id}")
    End Function

    Private Function AlreadyInstalled(m As Civitai.CivModel, v As Civitai.CivVersion) As Boolean
        Dim f = v.BestFile()
        If f Is Nothing Then Return False
        If m.IsLora Then Return File.Exists(Path.Combine(LoraLibrary.Folder, f.Name.Replace(" ", "_")))
        Return File.Exists(Path.Combine(MediaStudio.UserModelFolder, f.Name.Replace(" ", "_")))
    End Function

    '──────────────── install (several at once) ────────────────
    Private Async Function InstallAsync() As Task
        Dim tag = SelectedTag()
        Dim ver = cmbVersion.SelectedItem
        If tag Is Nothing OrElse ver Is Nothing Then Return
        Dim k = Key(tag, ver)
        Dim running As (Cts As CancellationTokenSource, Pct As Integer) = Nothing
        If _installs.TryGetValue(k, running) Then Return        ' (already in the download list; cancel it there)
        Dim cts As New CancellationTokenSource()
        _installs(k) = (cts, 0)
        Dim name = If(TryCast(tag, MediaRecipe)?.Name, TryCast(tag, Civitai.CivModel)?.Name)
        Dim item = list.SelectedItems(0)
        Await ShowVersionAsync()
        Dim prog As Action(Of Integer, String) = Nothing       ' (progress shows in Models → Downloads only)
        lblStatus.Text = Localizer.T("⬇ Added to the download list (Models → Downloads).")
        Dim message As String
        Dim needsKey = False
        Try
            Dim r = TryCast(tag, MediaRecipe)
            If r IsNot Nothing Then
                Await MediaStudio.DownloadAsync(r, CType(ver, VariantChoice).V, prog, cts.Token, forEdit:=r.Can("edit"))
                message = "✔ " & name & " " & Localizer.T("is ready in the studio.")
            Else
                Dim m = CType(tag, Civitai.CivModel)
                Dim v = CType(ver, Civitai.CivVersion)
                If m.IsLora Then
                    Dim l = Await Civitai.InstallLoraAsync(m, v, prog, cts.Token)
                    message = Localizer.T("✔ LoRA installed and turned on for") & " " & If(l?.Family, "")
                Else
                    Dim um = Await Civitai.InstallCheckpointAsync(m, v, prog, cts.Token)
                    message = Localizer.T("✔ Model installed. Find it in the studio as") & " ⭐ " & um.Name
                End If
            End If
            InstalledSomething = True
            If item.ListView IsNot Nothing AndAlso TypeOf tag Is MediaRecipe AndAlso Not item.Text.StartsWith("✔") Then item.Text = "✔ " & item.Text
            RaiseEvent Changed()
        Catch ex As OperationCanceledException
            message = Localizer.T("Download cancelled (it resumes next time).")
        Catch ex As Exception
            message = "⚠ " & name & ": " & ex.Message
            needsKey = TypeOf tag Is Civitai.CivModel AndAlso ex.Message.Contains("API key")
        Finally
            _installs.Remove(k)
            cts.Dispose()
        End Try
        If IsDisposed Then Return
        lblStatus.Text = message
        If needsKey AndAlso CivitaiKeyForm.Edit(FindForm(), Localizer.T("This model can only be downloaded with a Civitai account. Add your free API key, then press Install again.")) Then
            btnKey.Text = CivitaiKeyForm.ButtonText()
            lblStatus.Text = Localizer.T("✔ Key saved. Press Install again.")
        End If
        Dim cur = cmbVersion.SelectedItem
        If SelectedTag() IsNot Nothing AndAlso cur IsNot Nothing AndAlso Key(SelectedTag(), cur) = k Then
            Await ShowVersionAsync()
            lblStatus.Text = message
        End If
    End Function

    Private Sub DeleteSelected()
        Dim r = TryCast(SelectedTag(), MediaRecipe)
        Dim vc = TryCast(cmbVersion.SelectedItem, VariantChoice)
        If r Is Nothing OrElse vc Is Nothing Then Return
        If Localizer.Box(FindForm(), $"Delete {r.Name} ({vc.V.Label}) from disk?", "Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) <> DialogResult.Yes Then Return
        MediaStudio.DeleteVariant(r, vc.V)
        If list.SelectedItems.Count > 0 AndAlso list.SelectedItems(0).Text.StartsWith("✔ ") Then list.SelectedItems(0).Text = list.SelectedItems(0).Text.Substring(2)
        RaiseEvent Changed()
        Dim unused = ShowVersionAsync()
    End Sub

    ''' <summary>Stops searches and downloads (the window is closing).</summary>
    Public Sub Shutdown()
        _searchCts?.Cancel()
        _picCts?.Cancel()
        For Each i In _installs.Values.ToList()
            i.Cts.Cancel()
        Next
    End Sub
End Class
