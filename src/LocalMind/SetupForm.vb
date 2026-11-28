Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.IO
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms

''' <summary>
''' Recommended models: the ready-made setup for this PC (8 GB or 16 GB graphics card) as a list with tick boxes.
''' Shown at the first start, when a newer setup is published, and from Models → Recommended models.
''' </summary>
Public Class SetupForm
    Inherits Form

    Private ReadOnly _hw As HardwareInfo = HardwareInfo.Current()
    Private ReadOnly cmbTier As New ComboBox() With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 240}
    Private ReadOnly lst As New ListView()
    Private ReadOnly lblInfo As Label
    Private ReadOnly lblTotal As Label
    Private ReadOnly btnGo As Button
    Private _setup As SetupFile
    Private _loading As Boolean

    ''' <summary>True when downloads were started.</summary>
    Public ReadOnly Property Started As Boolean
        Get
            Return _started
        End Get
    End Property
    Private _started As Boolean

    Private ReadOnly _preset As SetupFile

    ''' <summary>With <paramref name="preset"/>, shows that list (e.g. from a restored backup) instead of the recommended one.</summary>
    Public Sub New(Optional intro As String = Nothing, Optional preset As SetupFile = Nothing)
        _preset = preset
        Text = Localizer.T("Recommended models")
        Size = New Size(960, 640)
        MinimumSize = New Size(760, 480)
        StartPosition = FormStartPosition.CenterParent
        Theme.ApplyForm(Me)

        Dim head As New Panel() With {.Dock = DockStyle.Top, .Height = 132, .Padding = New Padding(14, 10, 14, 6), .BackColor = Theme.CPanel}
        Dim title = Theme.MakeLabel(If(intro, Localizer.T("Get everything you need in one step")), bold:=True)
        title.Dock = DockStyle.Top
        title.Font = New Font(Theme.BoldFont.FontFamily, 13.0!, FontStyle.Bold)
        lblInfo = Theme.MakeLabel("", dim_:=True)
        lblInfo.Dock = DockStyle.Top
        lblInfo.AutoSize = False
        lblInfo.Height = 44
        Dim bar As New FlowLayoutPanel() With {.Dock = DockStyle.Bottom, .Height = 34, .WrapContents = False, .BackColor = Theme.CPanel}
        cmbTier.Items.AddRange({Localizer.T("8 GB graphics card (or none)"), Localizer.T("16 GB graphics card or more")})
        Theme.StyleInput(cmbTier)
        Dim btnImport = Theme.MakeButton(Localizer.T("📂 Open a setup file…"))
        Dim btnExport = Theme.MakeButton(Localizer.T("💾 Export my setup…"))
        AddHandler btnImport.Click, Sub() ImportFile()
        AddHandler btnExport.Click, Sub() ExportSetup(Me)
        bar.Controls.AddRange({cmbTier, btnImport, btnExport})
        head.Controls.Add(lblInfo)
        head.Controls.Add(title)
        head.Controls.Add(bar)

        lst.Dock = DockStyle.Fill
        lst.View = View.Details
        lst.CheckBoxes = True
        lst.FullRowSelect = True
        Theme.StyleListView(lst)
        lst.Columns.Add(Localizer.T("Model"), 380)
        lst.Columns.Add(Localizer.T("For"), 150)
        lst.Columns.Add(Localizer.T("Size"), 110)
        lst.Columns.Add(Localizer.T("Status"), 220)
        AddHandler lst.ItemCheck, Sub(s, e)
                                      ' installed ones can't be unticked / ticked
                                      If Not _loading AndAlso CStr(lst.Items(e.Index).SubItems(3).Text) = Localizer.T("✔ Installed") Then e.NewValue = e.CurrentValue
                                  End Sub
        AddHandler lst.ItemChecked, Sub() UpdateTotal()

        Dim bottom As New FlowLayoutPanel() With {.Dock = DockStyle.Bottom, .Height = 52, .FlowDirection = FlowDirection.RightToLeft, .Padding = New Padding(8), .BackColor = Theme.CPanel}
        btnGo = Theme.MakeButton(Localizer.T("⬇ Download selected"), primary:=True)
        Dim btnLater = Theme.MakeButton(Localizer.T("Not now"))
        lblTotal = Theme.MakeLabel("", dim_:=True)
        lblTotal.Margin = New Padding(10, 10, 10, 3)
        AddHandler btnGo.Click, Sub() StartDownloads()
        AddHandler btnLater.Click, Sub() Close()
        bottom.Controls.AddRange({btnGo, btnLater, lblTotal})

        Controls.Add(lst)
        Controls.Add(bottom)
        Controls.Add(head)
        Localizer.Apply(Me)

        cmbTier.SelectedIndex = If(SetupLibrary.TierFor(_hw) = "8gb", 0, 1)
        AddHandler cmbTier.SelectedIndexChanged, Async Sub() Await LoadTierAsync()
        AddHandler Shown, Async Sub()
                              If _preset IsNot Nothing Then ShowSetup(_preset) Else Await LoadTierAsync()
                          End Sub
    End Sub

    Private ReadOnly Property Tier As String
        Get
            Return If(cmbTier.SelectedIndex = 0, "8gb", "16gb")
        End Get
    End Property

    Private Async Function LoadTierAsync() As Task
        lblInfo.Text = Localizer.T("Reading the list…")
        lst.Items.Clear()
        Dim s = Await SetupLibrary.LoadAsync(Tier, CancellationToken.None)
        If IsDisposed Then Return
        ShowSetup(s)
    End Function

    Private Sub ShowSetup(s As SetupFile)
        ' (AMD / Intel cards get the editions that run well on them)
        s = SetupLibrary.ForThisPc(s, _hw)
        _setup = s
        _loading = True
        lst.BeginUpdate()
        lst.Items.Clear()
        If s Is Nothing Then
            lblInfo.Text = Localizer.T("The list could not be read. Check the internet connection and try again.")
        Else
            lblInfo.Text = String.Format(Localizer.T("This PC: {0}. {1} — chosen for your graphics card; untick anything you don't want."),
                                         _hw.Summary, If(String.IsNullOrEmpty(s.Name), Localizer.T("Recommended setup"), s.Name))
            For Each it In s.Items
                Dim installed = SetupLibrary.IsInstalled(it)
                Dim size = SetupLibrary.SizeOf(it, _hw)
                Dim li As New ListViewItem({DisplayName(it), Localizer.T(KindText(it)),
                                            If(size > 0, Downloader.FormatBytes(size), If(it.Kind = "llm", Localizer.T("best fit"), "")),
                                            If(installed, Localizer.T("✔ Installed"), Localizer.T("New")) & If(String.IsNullOrEmpty(it.About), "", "  ·  " & it.About)}) With {
                    .Tag = it, .Checked = installed OrElse it.Recommended, .ForeColor = If(installed, Theme.CDim, Theme.CText)}
                lst.Items.Add(li)
            Next
            If (s.Settings IsNot Nothing AndAlso s.Settings.Count > 0) OrElse s.Personas.Count > 0 Then
                lst.Items.Add(New ListViewItem({Localizer.T("Recommended settings") & If(s.Personas.Count > 0, " + " & String.Format(Localizer.T("{0} personas"), s.Personas.Count), ""),
                                                Localizer.T("Settings"), "", Localizer.T("Context size, memory use, picture options…")}) With {.Tag = "settings", .Checked = True})
            End If
            If s.Profiles.Count > 0 Then
                Dim li As New ListViewItem({String.Format(Localizer.T("{0} ready-made profiles (models with their prompts and LoRAs)"), s.Profiles.Count),
                                            Localizer.T("Profiles"), "", Localizer.T("Added with the models")}) With {.Tag = "profiles", .Checked = True}
                lst.Items.Add(li)
            End If
        End If
        lst.EndUpdate()
        _loading = False
        UpdateTotal()
    End Sub

    Private Shared Function DisplayName(it As SetupItem) As String
        If Not String.IsNullOrEmpty(it.Name) Then Return it.Name
        Select Case it.Kind
            Case "media" : Return If(MediaStudio.Recipes().FirstOrDefault(Function(r) r.Id = it.Id)?.Name, it.Id)
            Case "audio" : Return If(AudioStudio.Models().FirstOrDefault(Function(m) m.Id = it.Id)?.Name, it.Id)
            Case "llm" : Return it.Repo
            Case Else : Return Path.GetFileNameWithoutExtension(it.FileName)
        End Select
    End Function

    Private Shared Function KindText(it As SetupItem) As String
        Select Case it.Kind
            Case "llm" : Return If(it.MainChat, "💬 Chat (main)", "💬 Chat")
            Case "audio"
                Dim m = AudioStudio.Models().FirstOrDefault(Function(x) x.Id = it.Id)
                Return If(m IsNot Nothing AndAlso m.IsMusic, "🎵 Music", "🗣 Voice")
            Case "lora" : Return "🧩 LoRA"
            Case "civitai" : Return "🖼 Picture"
            Case "media"
                Dim r = MediaStudio.Recipes().FirstOrDefault(Function(x) x.Id = it.Id)
                If r Is Nothing Then Return "🖼 Picture"
                If r.IsVideo Then Return "🎬 Video"
                If r.IsUpscaler OrElse r.Kind = "tool" Then Return "🛠 Tool"
                If Not r.Can("create") Then Return "✏ Picture editing"
                Return "🖼 Picture"
        End Select
        Return it.Kind
    End Function

    Private Sub UpdateTotal()
        If _loading Then Return
        Dim total As Long = 0, n = 0
        For Each li As ListViewItem In lst.Items
            Dim it = TryCast(li.Tag, SetupItem)
            If it Is Nothing OrElse Not li.Checked OrElse SetupLibrary.IsInstalled(it) Then Continue For
            n += 1
            total += SetupLibrary.SizeOf(it, _hw)
        Next
        lblTotal.Text = If(n = 0, Localizer.T("Everything ticked is already installed."),
                           String.Format(Localizer.T("{0} to download · about {1} (plus the chat models)"), n, Downloader.FormatBytes(total)))
        btnGo.Enabled = _setup IsNot Nothing
    End Sub

    ''' <summary>Queues every ticked model in the download list (Models → Downloads) and adds the profiles.</summary>
    Private Sub StartDownloads()
        If _setup Is Nothing Then Return
        Dim picked = lst.Items.Cast(Of ListViewItem)().Where(Function(li) li.Checked).Select(Function(li) TryCast(li.Tag, SetupItem)).Where(Function(x) x IsNot Nothing).ToList()
        ' Civitai only lets signed-in users download some models: ask for the free API key first
        If picked.Any(Function(x) x.Kind = "civitai" AndAlso Not SetupLibrary.IsInstalled(x)) AndAlso String.IsNullOrWhiteSpace(AppCore.Settings.CivitaiApiKey) Then
            CivitaiKeyForm.Edit(Me, Localizer.T("Some of these models come from Civitai, which needs your free Civitai API key to download them. Paste it here, or skip and those models are left out."))
            If String.IsNullOrWhiteSpace(AppCore.Settings.CivitaiApiKey) Then picked.RemoveAll(Function(x) x.Kind = "civitai")
        End If
        Dim queued = QueueDownloads(picked, _hw)
        Dim withProfiles = lst.Items.Cast(Of ListViewItem)().Any(Function(x) CStr(TryCast(x.Tag, String)) = "profiles" AndAlso x.Checked)
        If withProfiles Then SetupLibrary.ApplyProfiles(_setup)
        Dim withSettings = lst.Items.Cast(Of ListViewItem)().Any(Function(x) CStr(TryCast(x.Tag, String)) = "settings" AndAlso x.Checked)
        If withSettings Then SetupLibrary.ApplySettings(_setup)
        AppCore.Settings.SetupTier = Tier
        AppCore.Settings.SetupVersion = Math.Max(AppCore.Settings.SetupVersion, _setup.Version)
        AppCore.SaveSettings()
        _started = queued > 0
        Localizer.Box(Me, If(queued > 0, String.Format(Localizer.T("{0} downloads started. You can follow them in Get models → Downloads, and keep using LocalMind meanwhile."), queued),
                                         Localizer.T("Everything is already installed.")), "Recommended models")
        Close()
    End Sub

    ''' <summary>Starts the downloads of the items not installed yet (in the background). Returns how many started.</summary>
    Public Shared Function QueueDownloads(items As IEnumerable(Of SetupItem), hw As HardwareInfo) As Integer
        Dim queued = 0
        For Each it In items
            If it Is Nothing OrElse SetupLibrary.IsInstalled(it) Then Continue For
            Try
                Select Case it.Kind
                    Case "llm"
                        ' the catalog entry (size, vision…) so "Auto" picks the right file for this graphics card
                        Dim age As String = Nothing
                        Dim e = ModelCatalog.LoadCachedOrEmbedded(age).FirstOrDefault(Function(x) String.Equals(x.Repo, it.Repo, StringComparison.OrdinalIgnoreCase))
                        If e Is Nothing Then e = New CatalogEntry With {.Repo = it.Repo, .Name = If(String.IsNullOrEmpty(it.Name), it.Repo, it.Name)}
                        AppCore.Downloads.Enqueue(e, If(String.IsNullOrEmpty(it.Quant), "Auto", it.Quant))
                    Case "media"
                        Dim r = MediaStudio.Recipes().FirstOrDefault(Function(x) x.Id = it.Id)
                        Dim v = SetupLibrary.VariantFor(r, it.Variant, hw)
                        If r Is Nothing OrElse v Is Nothing Then Continue For
                        Fire(MediaStudio.DownloadAsync(r, v, Nothing, CancellationToken.None, forEdit:=r.Can("edit") OrElse r.Can("reference"), withSpeed:=True))
                    Case "audio"
                        Dim m = AudioStudio.Models().FirstOrDefault(Function(x) x.Id = it.Id)
                        If m Is Nothing Then Continue For
                        Fire(AudioStudio.DownloadAsync(m, Nothing, CancellationToken.None))
                    Case "civitai"
                        If it.VersionId <= 0 Then Continue For
                        Fire(Civitai.InstallVersionAsync(it.VersionId, it.FileName, it.Id, CancellationToken.None))
                    Case "lora"
                        If String.IsNullOrEmpty(it.Url) Then Continue For
                        Fire(Civitai.InstallLoraFromUrlAsync(it.Url, it.FileName, it.Family, it.Triggers, CancellationToken.None))
                    Case Else
                        Continue For
                End Select
                queued += 1
            Catch ex As Exception
                Safety.Log(ex, "setup download " & DisplayName(it))
            End Try
        Next
        Return queued
    End Function

    ''' <summary>Runs a download in the background; errors show in the download list and the error log.</summary>
    Private Shared Sub Fire(t As Task)
        t.ContinueWith(Sub(x) If x.Exception IsNot Nothing Then Safety.Log(x.Exception.GetBaseException(), "setup download"),
                       TaskScheduler.Default)
    End Sub

    Private Sub ImportFile()
        Using d As New OpenFileDialog() With {.Filter = Localizer.T("LocalMind setup") & "|*.json|*.*|*.*"}
            If d.ShowDialog(Me) <> DialogResult.OK Then Return
            Try
                ShowSetup(SetupLibrary.Parse(File.ReadAllText(d.FileName)))
            Catch ex As Exception
                Localizer.Box(Me, ex.Message, "Recommended models", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            End Try
        End Using
    End Sub

    ''' <summary>💾 Saves what is installed here (models, LoRAs, profiles) as a setup file.</summary>
    Public Shared Sub ExportSetup(owner As IWin32Window)
        Dim hw = HardwareInfo.Current()
        Dim tier = SetupLibrary.TierFor(hw)
        Using d As New SaveFileDialog() With {.FileName = "localmind-setup-" & tier & ".json", .Filter = Localizer.T("LocalMind setup") & "|*.json"}
            If d.ShowDialog(owner) <> DialogResult.OK Then Return
            Dim s = SetupLibrary.ExportInstalled(If(tier = "8gb", "8 GB graphics card", "16 GB graphics card"), hw)
            File.WriteAllText(d.FileName, SetupLibrary.ToJson(s))
            Dim noLink = SetupLibrary.LorasWithoutLink()
            Localizer.Box(owner, String.Format(Localizer.T("Saved {0} models, {1} profiles, {2} personas and your settings to {3}."),
                                               s.Items.Count, s.Profiles.Count, s.Personas.Count, d.FileName) &
                                 If(noLink.Count = 0, "", vbCrLf & vbCrLf & Localizer.T("Not included (added from a file, so there is no download link):") & " " & String.Join(", ", noLink)) &
                                 vbCrLf & vbCrLf & Localizer.T("API keys, PINs, folders and your theme are never saved in this file."), "Export my setup")
        End Using
    End Sub
End Class
