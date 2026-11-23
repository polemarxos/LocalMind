Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.IO
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms

''' <summary>Find LoRAs and checkpoints on Civitai and install them with one click.</summary>
Public Class CivitaiForm
    Inherits Form

    Private ReadOnly _hw As HardwareInfo = HardwareInfo.Current()
    Private ReadOnly cmbSite, cmbType, cmbBase, cmbSort, cmbVersion As ComboBox
    Private ReadOnly txtSearch, txtKey As TextBox
    Private ReadOnly list As ListView
    Private ReadOnly thumbs As New ImageList() With {.ImageSize = New Size(150, 150), .ColorDepth = ColorDepth.Depth32Bit}
    Private ReadOnly pic As PictureBox
    Private ReadOnly lblInfo, lblStatus As Label
    Private ReadOnly txtTriggers As TextBox
    Private ReadOnly btnInstall, btnMore, btnOpen As Button
    Private ReadOnly prg As ProgressBar
    Private _cursor As String
    Private _searchCts, _dlCts As CancellationTokenSource
    <System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)>
    Public Property InstalledSomething As Boolean

    ''' <param name="checkpoints">Open on the checkpoints list instead of LoRAs.</param>
    ''' <param name="family">LoRA family / recipe family to preselect (e.g. "SDXL").</param>
    Public Sub New(Optional checkpoints As Boolean = False, Optional family As String = Nothing)
        Text = "Civitai — LoRAs & models"
        Size = New Size(1300, 860)
        MinimumSize = New Size(1000, 640)
        StartPosition = FormStartPosition.CenterParent
        Theme.ApplyForm(Me)

        Dim top As New FlowLayoutPanel() With {.Dock = DockStyle.Top, .Height = 84, .Padding = New Padding(6), .WrapContents = True}
        cmbSite = New ComboBox() With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 230}
        cmbSite.Items.AddRange({Localizer.T("civitai.com (safe for work)"), Localizer.T("civitai.red (everything, 18+)")})
        cmbType = New ComboBox() With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 150}
        cmbType.Items.AddRange({Localizer.T("LoRAs"), Localizer.T("Checkpoints (models)")})
        cmbBase = New ComboBox() With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 250}
        cmbBase.Items.Add(Localizer.T("All models LocalMind can use"))
        cmbBase.Items.AddRange(Civitai.Groups.Select(Function(g) CObj(g.Label)).ToArray())
        cmbSort = New ComboBox() With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 150}
        cmbSort.Items.AddRange({"Most Downloaded", "Highest Rated", "Newest"})
        txtSearch = New TextBox() With {.Width = 260, .PlaceholderText = "Search, e.g. 'watercolor', 'pixel art', 'realistic'…"}
        Dim btnSearch = Theme.MakeButton("🔍 Search", primary:=True)
        txtKey = New TextBox() With {.Width = 300, .UseSystemPasswordChar = True, .Text = AppCore.Settings.CivitaiApiKey,
                                     .PlaceholderText = "Civitai API key (only needed for some downloads)"}
        For Each c As Control In {cmbSite, cmbType, cmbBase, cmbSort, txtSearch, txtKey}
            Theme.StyleInput(c)
        Next
        top.Controls.AddRange({cmbSite, cmbType, cmbBase, cmbSort, txtSearch, btnSearch, Theme.MakeLabel("API key"), txtKey})

        Dim right As New Panel() With {.Dock = DockStyle.Right, .Width = 420, .Padding = New Padding(8), .BackColor = Theme.CRaised}
        pic = New PictureBox() With {.Dock = DockStyle.Top, .Height = 360, .SizeMode = PictureBoxSizeMode.Zoom, .BackColor = Theme.CBack}
        lblInfo = New Label() With {.Dock = DockStyle.Top, .Height = 90, .ForeColor = Theme.CText}
        cmbVersion = New ComboBox() With {.Dock = DockStyle.Top, .DropDownStyle = ComboBoxStyle.DropDownList}
        Theme.StyleInput(cmbVersion)
        Dim trigLabel = Theme.MakeLabel("Trigger words (added to your prompt automatically)")
        trigLabel.Dock = DockStyle.Top
        txtTriggers = New TextBox() With {.Dock = DockStyle.Top, .Height = 50, .Multiline = True, .ReadOnly = True}
        Theme.StyleInput(txtTriggers)
        Dim bar As New FlowLayoutPanel() With {.Dock = DockStyle.Top, .Height = 44}
        btnInstall = Theme.MakeButton("⬇ Install", primary:=True, width:=150)
        btnOpen = Theme.MakeButton("🌐 Open page")
        bar.Controls.AddRange({btnInstall, btnOpen})
        prg = New ProgressBar() With {.Dock = DockStyle.Top, .Height = 14}
        lblStatus = New Label() With {.Dock = DockStyle.Fill, .ForeColor = Theme.CDim}
        right.Controls.Add(lblStatus)
        right.Controls.Add(prg)
        right.Controls.Add(bar)
        right.Controls.Add(txtTriggers)
        right.Controls.Add(trigLabel)
        right.Controls.Add(cmbVersion)
        right.Controls.Add(lblInfo)
        right.Controls.Add(pic)

        list = New ListView() With {.Dock = DockStyle.Fill, .View = View.LargeIcon, .LargeImageList = thumbs, .MultiSelect = False,
                                    .BackColor = Theme.CBack, .ForeColor = Theme.CText, .BorderStyle = BorderStyle.None}
        btnMore = Theme.MakeButton("More results…")
        btnMore.Dock = DockStyle.Bottom
        Controls.Add(list)
        Controls.Add(btnMore)
        Controls.Add(right)
        Controls.Add(top)

        cmbSite.SelectedIndex = If(AppCore.Settings.CivitaiRed AndAlso AppCore.Settings.CivitaiRedConfirmed, 1, 0)
        cmbType.SelectedIndex = If(checkpoints, 1, 0)
        cmbSort.SelectedIndex = 0
        Dim gi = Array.FindIndex(Civitai.Groups, Function(g) family IsNot Nothing AndAlso LoraLibrary.FamilyMatches(g.Family, family))
        cmbBase.SelectedIndex = If(gi >= 0, gi + 1, 0)

        AddHandler btnSearch.Click, Async Sub() Await SearchAsync(True)
        AddHandler txtSearch.KeyDown, Async Sub(s, e)
                                          If e.KeyCode = Keys.Enter Then
                                              e.SuppressKeyPress = True
                                              Await SearchAsync(True)
                                          End If
                                      End Sub
        AddHandler btnMore.Click, Async Sub() Await SearchAsync(False)
        For Each c In {cmbType, cmbBase, cmbSort}
            AddHandler c.SelectedIndexChanged, Async Sub() Await SearchAsync(True)
        Next
        AddHandler cmbSite.SelectedIndexChanged, Async Sub()
                                                     If cmbSite.SelectedIndex = 1 AndAlso Not AppCore.Settings.CivitaiRedConfirmed Then
                                                         If Localizer.Box(Me, "civitai.red shows Civitai's full catalogue, including adult content. Continue only if you are 18 or older.",
                                                                          "civitai.red", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) <> DialogResult.Yes Then
                                                             cmbSite.SelectedIndex = 0
                                                             Return
                                                         End If
                                                         AppCore.Settings.CivitaiRedConfirmed = True
                                                     End If
                                                     AppCore.Settings.CivitaiRed = cmbSite.SelectedIndex = 1
                                                     AppCore.SaveSettings()
                                                     Await SearchAsync(True)
                                                 End Sub
        AddHandler txtKey.Leave, Sub()
                                     AppCore.Settings.CivitaiApiKey = txtKey.Text.Trim()
                                     AppCore.SaveSettings()
                                 End Sub
        AddHandler list.SelectedIndexChanged, Sub() ShowSelected()
        AddHandler cmbVersion.SelectedIndexChanged, Async Sub() Await ShowVersionAsync()
        AddHandler btnInstall.Click, Async Sub() Await InstallAsync()
        AddHandler btnOpen.Click, Sub()
                                      Dim m = SelectedModel()
                                      If m IsNot Nothing Then ModelManagerForm.OpenUrl($"https://{If(cmbSite.SelectedIndex = 1, "civitai.red", "civitai.com")}/models/{m.Id}")
                                  End Sub
    End Sub

    Protected Overrides Async Sub OnShown(e As EventArgs)
        MyBase.OnShown(e)
        Await SearchAsync(True)
    End Sub

    Private Function SelectedModel() As Civitai.CivModel
        Return If(list.SelectedItems.Count = 0, Nothing, TryCast(list.SelectedItems(0).Tag, Civitai.CivModel))
    End Function

    Private Function SelectedVersion() As Civitai.CivVersion
        Return TryCast(cmbVersion.SelectedItem, Civitai.CivVersion)
    End Function

    Private Async Function SearchAsync(fresh As Boolean) As Task
        _searchCts?.Cancel()
        _searchCts = New CancellationTokenSource()
        Dim ct = _searchCts.Token
        If fresh Then
            _cursor = Nothing
            list.Items.Clear()
            thumbs.Images.Clear()
        End If
        Dim bases As IEnumerable(Of String) = If(cmbBase.SelectedIndex <= 0,
                                                 Civitai.Groups.Where(Function(g) cmbType.SelectedIndex = 0 OrElse g.Template IsNot Nothing).SelectMany(Function(g) g.BaseModels),
                                                 Civitai.Groups(cmbBase.SelectedIndex - 1).BaseModels)
        lblStatus.Text = Localizer.T("Searching Civitai…")
        btnMore.Enabled = False
        Try
            Dim res = Await Civitai.SearchAsync(txtSearch.Text, cmbType.SelectedIndex = 0, bases, cmbSite.SelectedIndex = 1,
                                                CStr(cmbSort.SelectedItem), _cursor, ct)
            If ct.IsCancellationRequested Then Return
            _cursor = res.NextCursor
            Dim red = cmbSite.SelectedIndex = 1
            For Each m In res.Items
                Dim v = m.Versions(0)
                Dim it As New ListViewItem($"{m.Name}{vbLf}{v.BaseModel} · ⬇{Compact(m.Downloads)}") With {.Tag = m, .ImageKey = $"m{m.Id}"}
                list.Items.Add(it)
                If v.ImageUrl IsNot Nothing AndAlso (red OrElse Not v.ImageNsfw) Then
                    Dim key = $"m{m.Id}"
                    Dim url = Civitai.ThumbUrl(v.ImageUrl, 300)
                    Dim unused = LoadThumbAsync(key, url, ct)
                End If
            Next
            lblStatus.Text = $"{list.Items.Count} " & Localizer.T("results")
            btnMore.Enabled = Not String.IsNullOrEmpty(_cursor)
        Catch ex As OperationCanceledException
        Catch ex As Exception
            lblStatus.Text = "⚠ " & ex.Message
        End Try
    End Function

    Private Shared Function Compact(n As Long) As String
        Return If(n >= 1_000_000, $"{n / 1_000_000.0:0.#}M", If(n >= 1000, $"{n / 1000.0:0.#}k", n.ToString()))
    End Function

    Private Async Function LoadThumbAsync(key As String, url As String, ct As CancellationToken) As Task
        Try
            Dim bytes = Await Downloader.Http.GetByteArrayAsync(url, ct)
            Using ms As New MemoryStream(bytes)
                Using img = Image.FromStream(ms)
                    Dim bmp As New Bitmap(150, 150)
                    Using g = Graphics.FromImage(bmp)
                        g.Clear(Theme.CBack)
                        Dim k = Math.Min(150.0 / img.Width, 150.0 / img.Height)
                        Dim w = CInt(img.Width * k), h = CInt(img.Height * k)
                        g.InterpolationMode = Drawing2D.InterpolationMode.HighQualityBicubic
                        g.DrawImage(img, (150 - w) \ 2, (150 - h) \ 2, w, h)
                    End Using
                    If Not IsDisposed AndAlso Not ct.IsCancellationRequested Then
                        thumbs.Images.Add(key, bmp)
                        list.Invalidate()
                    End If
                End Using
            End Using
        Catch
        End Try
    End Function

    Private Sub ShowSelected()
        Dim m = SelectedModel()
        cmbVersion.Items.Clear()
        If m Is Nothing Then Return
        For Each v In m.Versions
            cmbVersion.Items.Add(v)
        Next
        cmbVersion.SelectedIndex = 0
    End Sub

    Private Async Function ShowVersionAsync() As Task
        Dim m = SelectedModel()
        Dim v = SelectedVersion()
        If m Is Nothing OrElse v Is Nothing Then Return
        Dim f = v.BestFile()
        Dim g = Civitai.GroupFor(v.BaseModel)
        Dim fitText = ""
        If Not m.IsLora AndAlso f IsNot Nothing Then
            Dim vram = _hw.VramBytes
            fitText = If(vram = 0, "", If(f.SizeBytes + 2.5 * 1024 * 1024 * 1024 <= vram, Localizer.T("✔ fits your GPU"), Localizer.T("uses system RAM too (slower)")))
        End If
        lblInfo.Text = $"{m.Name}" & vbLf & $"{Localizer.T("by")} {m.Creator} · ⬇ {Compact(m.Downloads)} · 👍 {Compact(m.Likes)}" & vbLf &
                       $"{m.Type} · {v.BaseModel}" & If(f IsNot Nothing, $" · {Downloader.FormatBytes(f.SizeBytes)}", "") & If(fitText <> "", " · " & fitText, "") &
                       If(g.Family Is Nothing, vbLf & Localizer.T("⚠ LocalMind can't use this base model."), If(Not m.IsLora AndAlso g.Template Is Nothing, vbLf & Localizer.T("⚠ Video checkpoints aren't supported yet — LoRAs are."), ""))
        txtTriggers.Text = String.Join(", ", v.TrainedWords)
        If f IsNot Nothing Then
            Dim vv = v
            Dim check = Task.Run(Async Function()
                                     Dim locked = False
                                     Try
                                         locked = Await Civitai.NeedsLoginAsync(f, CancellationToken.None)
                                     Catch
                                     End Try
                                     If locked AndAlso Not IsDisposed Then
                                         BeginInvoke(Sub()
                                                         If SelectedVersion() Is vv Then lblInfo.Text &= vbLf & Localizer.T("🔒 Needs a Civitai account: paste your API key at the top first.")
                                                     End Sub)
                                     End If
                                 End Function)
        End If
        btnInstall.Enabled = f IsNot Nothing AndAlso g.Family IsNot Nothing AndAlso (m.IsLora OrElse g.Template IsNot Nothing) AndAlso _dlCts Is Nothing
        btnInstall.Text = If(AlreadyInstalled(m, v), Localizer.T("✔ Installed"), Localizer.T("⬇ Install"))
        pic.Image?.Dispose()
        pic.Image = Nothing
        If v.ImageUrl IsNot Nothing AndAlso (cmbSite.SelectedIndex = 1 OrElse Not v.ImageNsfw) Then
            Try
                Dim bytes = Await Downloader.Http.GetByteArrayAsync(Civitai.ThumbUrl(v.ImageUrl, 512))
                If SelectedVersion() Is v Then
                    Using ms As New MemoryStream(bytes)
                        pic.Image = New Bitmap(Image.FromStream(ms))
                    End Using
                End If
            Catch
            End Try
        End If
    End Function

    Private Function AlreadyInstalled(m As Civitai.CivModel, v As Civitai.CivVersion) As Boolean
        Dim f = v.BestFile()
        If f Is Nothing Then Return False
        Dim n = Path.GetFileNameWithoutExtension(f.Name).Replace(" ", "_")
        If m.IsLora Then Return File.Exists(Path.Combine(LoraLibrary.Folder, f.Name.Replace(" ", "_")))
        Return File.Exists(Path.Combine(MediaStudio.UserModelFolder, f.Name.Replace(" ", "_")))
    End Function

    Private Async Function InstallAsync() As Task
        Dim m = SelectedModel()
        Dim v = SelectedVersion()
        If m Is Nothing OrElse v Is Nothing Then Return
        AppCore.Settings.CivitaiApiKey = txtKey.Text.Trim()
        AppCore.SaveSettings()
        _dlCts = New CancellationTokenSource()
        btnInstall.Enabled = False
        Try
            Dim prog = Sub(p As Integer, t As String) BeginInvoke(Sub()
                                                                     prg.Value = Math.Max(0, Math.Min(100, p))
                                                                     lblStatus.Text = t
                                                                 End Sub)
            If m.IsLora Then
                Dim l = Await Civitai.InstallLoraAsync(m, v, prog, _dlCts.Token)
                lblStatus.Text = Localizer.T("✔ LoRA installed and turned on for") & " " & If(l?.Family, "") &
                                 If(String.IsNullOrEmpty(l?.Triggers), "", vbLf & Localizer.T("Trigger words") & ": " & l.Triggers)
            Else
                Dim um = Await Civitai.InstallCheckpointAsync(m, v, prog, _dlCts.Token)
                lblStatus.Text = Localizer.T("✔ Model installed. Find it in the studio as") & " ⭐ " & um.Name
            End If
            InstalledSomething = True
        Catch ex As OperationCanceledException
            lblStatus.Text = Localizer.T("Download cancelled (it resumes next time).")
        Catch ex As Exception
            lblStatus.Text = "⚠ " & ex.Message
        Finally
            _dlCts = Nothing
        End Try
        Await ShowVersionAsync()
    End Function

    Protected Overrides Sub OnFormClosing(e As FormClosingEventArgs)
        _searchCts?.Cancel()
        _dlCts?.Cancel()
        AppCore.Settings.CivitaiApiKey = txtKey.Text.Trim()
        AppCore.SaveSettings()
        MyBase.OnFormClosing(e)
    End Sub
End Class
