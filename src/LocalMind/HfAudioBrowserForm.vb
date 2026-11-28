Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.Linq
Imports System.Net.Http
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports Newtonsoft.Json.Linq

''' <summary>
''' 🌐 Browse Hugging Face for more music or voice-cloning models (other uploads and sizes of the models LocalMind's
''' engine runs: MiniMax Music 3, YuE2, Qwen3-TTS, Chatterbox, VoxCPM2, Fish S2…). Pick a repo, its model file and its
''' audio file (mmproj), and the model is added to the list and downloaded.
''' </summary>
Public Class HfAudioBrowserForm
    Inherits Form

    Private ReadOnly _kind As String
    Private ReadOnly txtSearch As New TextBox() With {.Width = 320}
    Private ReadOnly lstRepos As New ListView() With {.Dock = DockStyle.Fill, .View = View.Details, .FullRowSelect = True, .MultiSelect = False, .HideSelection = False}
    Private ReadOnly cmbModel As New ComboBox() With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 420}
    Private ReadOnly cmbMmproj As New ComboBox() With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 420}
    Private ReadOnly cmbBase As New ComboBox() With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 420}
    Private ReadOnly txtName As New TextBox() With {.Width = 420}
    Private ReadOnly lblInfo As Label
    Private ReadOnly btnAdd As Button
    Private _cts As CancellationTokenSource

    ''' <summary>Set when a model was added (the list then refreshes).</summary>
    Public ReadOnly Property Added As Boolean
        Get
            Return _added
        End Get
    End Property
    Private _added As Boolean

    Private Class FileRow
        Public Path As String
        Public Size As Long
        Public Overrides Function ToString() As String
            Return Path & "   (" & Downloader.FormatBytes(Size) & ")"
        End Function
    End Class

    ''' <param name="kind">"music" or "voice"</param>
    Public Sub New(kind As String)
        _kind = If(kind = "music", "music", "voice")
        Text = Localizer.T(If(_kind = "music", "Browse Hugging Face — music models", "Browse Hugging Face — voice cloning models"))
        Size = New Size(1080, 640)
        MinimumSize = New Size(860, 480)
        StartPosition = FormStartPosition.CenterParent
        Theme.ApplyForm(Me)

        Dim top As New FlowLayoutPanel() With {.Dock = DockStyle.Top, .Height = 44, .Padding = New Padding(8, 8, 8, 4), .WrapContents = False, .BackColor = Theme.CPanel}
        txtSearch.Text = If(_kind = "music", "MiniMax-Music3", "Qwen3-TTS")
        Theme.StyleInput(txtSearch)
        Dim btnSearch = Theme.MakeButton(Localizer.T("🔎 Search"), primary:=True)
        Dim quick As New ComboBox() With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 170}
        quick.Items.AddRange(If(_kind = "music", {"MiniMax-Music3", "YuE2"}, {"Qwen3-TTS", "chatterbox", "VoxCPM2", "s2-pro"}))
        Theme.StyleInput(quick)
        AddHandler quick.SelectedIndexChanged, Async Sub()
                                                   txtSearch.Text = CStr(quick.SelectedItem)
                                                   Await SearchAsync()
                                               End Sub
        AddHandler btnSearch.Click, Async Sub() Await SearchAsync()
        AddHandler txtSearch.KeyDown, Async Sub(s, e)
                                          If e.KeyCode = Keys.Enter Then
                                              e.SuppressKeyPress = True
                                              Await SearchAsync()
                                          End If
                                      End Sub
        top.Controls.AddRange({txtSearch, btnSearch, Theme.MakeLabel(Localizer.T("Model:"), dim_:=True), quick})

        Theme.StyleListView(lstRepos)
        lstRepos.Columns.Add(Localizer.T("Hugging Face repo"), 360)
        lstRepos.Columns.Add(Localizer.T("Downloads"), 100, HorizontalAlignment.Right)
        lstRepos.Columns.Add(Localizer.T("Likes"), 70, HorizontalAlignment.Right)
        lstRepos.Columns.Add(Localizer.T("Updated"), 110)
        AddHandler lstRepos.SelectedIndexChanged, Async Sub() Await LoadFilesAsync()

        Dim right As New TableLayoutPanel() With {.Dock = DockStyle.Right, .Width = 470, .Padding = New Padding(10), .ColumnCount = 1, .AutoScroll = True, .BackColor = Theme.CPanel}
        Dim addRow = Sub(caption As String, c As Control)
                         Dim l = Theme.MakeLabel(Localizer.T(caption), dim_:=True)
                         l.Margin = New Padding(0, 8, 0, 2)
                         right.Controls.Add(l)
                         c.Margin = New Padding(0, 0, 0, 2)
                         right.Controls.Add(c)
                     End Sub
        Theme.StyleInput(cmbModel) : Theme.StyleInput(cmbMmproj) : Theme.StyleInput(cmbBase) : Theme.StyleInput(txtName)
        addRow("Model file", cmbModel)
        addRow("Audio file (mmproj)", cmbMmproj)
        addRow("Works like (its settings come from this model)", cmbBase)
        addRow("Name in LocalMind", txtName)
        For Each b In AudioStudio.Models().Where(Function(m) Not m.IsUser AndAlso m.IsMusic = (_kind = "music")).GroupBy(Function(m) FamilyKey(m.Model.Repo & " " & m.Name)).Select(Function(g) g.First())
            cmbBase.Items.Add(b)
        Next
        If cmbBase.Items.Count > 0 Then cmbBase.SelectedIndex = 0
        AddHandler cmbModel.SelectedIndexChanged, Sub() SuggestName()
        lblInfo = Theme.MakeLabel("", dim_:=True)
        lblInfo.AutoSize = False
        lblInfo.Size = New Size(440, 110)
        lblInfo.Margin = New Padding(0, 12, 0, 4)
        right.Controls.Add(lblInfo)
        btnAdd = Theme.MakeButton(Localizer.T("⬇ Add and download"), primary:=True)
        btnAdd.Enabled = False
        AddHandler btnAdd.Click, Async Sub() Await AddAsync()
        right.Controls.Add(btnAdd)

        Controls.Add(lstRepos)
        Controls.Add(right)
        Controls.Add(top)
        Localizer.Apply(Me)
        lblInfo.Text = Localizer.T("Community uploads of the same models in other sizes. A file made for another program (e.g. audio.cpp component files) won't load; the ones with a model file and an mmproj file next to it are for llama.cpp, which LocalMind uses.")
        AddHandler Shown, Async Sub() Await SearchAsync()
        AddHandler FormClosed, Sub() _cts?.Cancel()
    End Sub

    ''' <summary>Which known model a repo / file is an edition of.</summary>
    Private Shared Function FamilyKey(s As String) As String
        Dim t = If(s, "").ToLowerInvariant().Replace("_", "-")
        If t.Contains("minimax-music") OrElse t.Contains("minimax music") Then Return "minimax-music"
        If t.Contains("yue") Then Return "yue"
        If t.Contains("qwen3-tts") OrElse t.Contains("qwen3 tts") Then Return "qwen3-tts"
        If t.Contains("chatterbox") Then Return If(t.Contains("turbo"), "chatterbox-turbo", "chatterbox")
        If t.Contains("voxcpm") Then Return "voxcpm"
        If t.Contains("s2-pro") OrElse t.Contains("fish") Then Return "s2"
        Return t
    End Function

    Private Async Function GetJsonAsync(url As String, ct As CancellationToken) As Task(Of JToken)
        Using req As New HttpRequestMessage(HttpMethod.Get, url)
            req.Headers.UserAgent.ParseAdd("LocalMind/" & Updater.CurrentVersion.ToString())
            Using resp = Await Downloader.Http.SendAsync(req, ct)
                resp.EnsureSuccessStatusCode()
                Return JToken.Parse(Await resp.Content.ReadAsStringAsync(ct))
            End Using
        End Using
    End Function

    Private Async Function SearchAsync() As Task
        _cts?.Cancel()
        _cts = New CancellationTokenSource()
        Dim ct = _cts.Token
        lstRepos.Items.Clear()
        cmbModel.Items.Clear() : cmbMmproj.Items.Clear()
        btnAdd.Enabled = False
        Try
            Dim q = Uri.EscapeDataString(txtSearch.Text.Trim())
            Dim arr = TryCast(Await GetJsonAsync($"https://huggingface.co/api/models?search={q}&filter=gguf&sort=downloads&direction=-1&limit=60", ct), JArray)
            If ct.IsCancellationRequested OrElse IsDisposed Then Return
            lstRepos.BeginUpdate()
            For Each m In If(arr, New JArray()).OfType(Of JObject)()
                Dim id = m.Value(Of String)("id")
                Dim it As New ListViewItem(id) With {.Tag = id}
                it.SubItems.Add(m.Value(Of Long?)("downloads").GetValueOrDefault().ToString("N0"))
                it.SubItems.Add(m.Value(Of Long?)("likes").GetValueOrDefault().ToString("N0"))
                Dim when_ = m.Value(Of DateTime?)("lastModified")
                it.SubItems.Add(If(when_.HasValue, when_.Value.ToString("yyyy-MM-dd"), ""))
                lstRepos.Items.Add(it)
            Next
            lstRepos.EndUpdate()
            If lstRepos.Items.Count = 0 Then lblInfo.Text = Localizer.T("Nothing found. Try another name.")
        Catch ex As OperationCanceledException
        Catch ex As Exception
            If Not IsDisposed Then lblInfo.Text = "⚠ " & ex.Message
        End Try
    End Function

    Private Async Function LoadFilesAsync() As Task
        If lstRepos.SelectedItems.Count = 0 Then Return
        Dim repo = CStr(lstRepos.SelectedItems(0).Tag)
        _cts?.Cancel()
        _cts = New CancellationTokenSource()
        Dim ct = _cts.Token
        cmbModel.Items.Clear() : cmbMmproj.Items.Clear()
        btnAdd.Enabled = False
        lblInfo.Text = Localizer.T("Reading the files…")
        Try
            Dim arr = TryCast(Await GetJsonAsync($"https://huggingface.co/api/models/{repo}/tree/main?recursive=true", ct), JArray)
            If ct.IsCancellationRequested OrElse IsDisposed Then Return
            Dim files = If(arr, New JArray()).OfType(Of JObject)().Where(Function(f) f.Value(Of String)("type") = "file" AndAlso
                                                                         f.Value(Of String)("path").EndsWith(".gguf", StringComparison.OrdinalIgnoreCase)).
                        Select(Function(f) New FileRow With {.Path = f.Value(Of String)("path"),
                                                             .Size = If(f("lfs")?.Value(Of Long?)("size"), f.Value(Of Long?)("size")).GetValueOrDefault()}).ToList()
            Dim isProj = Function(p As String) p.ToLowerInvariant().Contains("mmproj")
            For Each f In files.Where(Function(x) Not isProj(x.Path)).OrderBy(Function(x) x.Size)
                cmbModel.Items.Add(f)
            Next
            ' the audio file: full precision first (what the engine was tested with), then Q8
            Dim rank = Function(p As String) As Integer
                           Dim l = p.ToLowerInvariant()
                           If l.Contains("bf16") Then Return 0
                           If l.Contains("f16") Then Return 1
                           If l.Contains("q8") Then Return 2
                           Return 3
                       End Function
            For Each f In files.Where(Function(x) isProj(x.Path)).OrderBy(Function(x) rank(x.Path))
                cmbMmproj.Items.Add(f)
            Next
            ' a mid-size model file by default (Q8 if there is one)
            Dim q8 = cmbModel.Items.Cast(Of FileRow)().FirstOrDefault(Function(x) x.Path.ToLowerInvariant().Contains("q8"))
            If q8 IsNot Nothing Then cmbModel.SelectedItem = q8 Else If cmbModel.Items.Count > 0 Then cmbModel.SelectedIndex = cmbModel.Items.Count \ 2
            If cmbMmproj.Items.Count > 0 Then cmbMmproj.SelectedIndex = 0
            ' its family, from the repo name
            Dim fk = FamilyKey(repo)
            For i = 0 To cmbBase.Items.Count - 1
                Dim b = CType(cmbBase.Items(i), AudioModel)
                If FamilyKey(b.Model.Repo & " " & b.Name) = fk Then cmbBase.SelectedIndex = i : Exit For
            Next
            If cmbModel.Items.Count = 0 OrElse cmbMmproj.Items.Count = 0 Then
                lblInfo.Text = Localizer.T("This repo has no model file + mmproj file pair, so LocalMind's engine can't run it (it may be for another program).")
            Else
                lblInfo.Text = String.Format(Localizer.T("{0} model files, {1} audio files. Pick the size, then press Add and download."), cmbModel.Items.Count, cmbMmproj.Items.Count)
                btnAdd.Enabled = True
            End If
            SuggestName()
        Catch ex As OperationCanceledException
        Catch ex As Exception
            If Not IsDisposed Then lblInfo.Text = "⚠ " & ex.Message
        End Try
    End Function

    Private Sub SuggestName()
        Dim f = TryCast(cmbModel.SelectedItem, FileRow)
        If f Is Nothing OrElse lstRepos.SelectedItems.Count = 0 Then Return
        Dim repo = CStr(lstRepos.SelectedItems(0).Tag)
        Dim who = repo.Split("/"c)(0)
        Dim quant = System.Text.RegularExpressions.Regex.Match(IO.Path.GetFileNameWithoutExtension(f.Path), "(?i)(i?q\d[\w]*|bf16|f16|f32)$").Value
        Dim baseName = TryCast(cmbBase.SelectedItem, AudioModel)?.Name
        baseName = System.Text.RegularExpressions.Regex.Replace(If(baseName, repo.Split("/"c).Last()), "\s*·.*$|\s*\((songs)\)", "").Trim()
        txtName.Text = $"{baseName} · {If(quant <> "", quant.ToUpperInvariant(), IO.Path.GetFileNameWithoutExtension(f.Path))} · {who}" & If(_kind = "music", " (songs)", "")
    End Sub

    Private Async Function AddAsync() As Task
        Dim f = TryCast(cmbModel.SelectedItem, FileRow)
        Dim mp = TryCast(cmbMmproj.SelectedItem, FileRow)
        Dim b = TryCast(cmbBase.SelectedItem, AudioModel)
        If f Is Nothing OrElse mp Is Nothing OrElse lstRepos.SelectedItems.Count = 0 Then Return
        Dim repo = CStr(lstRepos.SelectedItems(0).Tag)
        Dim m As New AudioModel With {
            .Id = "hf:" & repo.ToLowerInvariant() & "/" & f.Path.ToLowerInvariant(),
            .Name = If(String.IsNullOrWhiteSpace(txtName.Text), repo, txtName.Text.Trim()),
            .Kind = _kind,
            .About = String.Format(Localizer.T("Added from Hugging Face ({0}). Works like {1}."), repo, If(b?.Name, "?")),
            .Model = New MediaFile With {.Repo = repo, .Path = f.Path, .Size = f.Size},
            .Mmproj = New MediaFile With {.Repo = repo, .Path = mp.Path, .Size = mp.Size},
            .Clone = If(b IsNot Nothing, b.Clone, _kind = "voice"),
            .Langs = If(b?.Langs, New List(Of String)()),
            .Transcript = b IsNot Nothing AndAlso b.Transcript,
            .Design = b IsNot Nothing AndAlso b.Design}
        m = AudioStudio.AddUserModel(m)
        _added = True
        btnAdd.Enabled = False
        lblInfo.Text = Localizer.T("Added — downloading. You can follow it in the Downloads list and close this window.")
        Dim t = AudioStudio.DownloadAsync(m, Nothing, CancellationToken.None)
        Dim unused = t.ContinueWith(Sub(x) If x.Exception IsNot Nothing Then Safety.Log(x.Exception.GetBaseException(), "audio download"), TaskScheduler.Default)
        Await Task.Delay(300)
        btnAdd.Enabled = True
    End Function
End Class
