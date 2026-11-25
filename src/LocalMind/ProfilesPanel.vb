Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.Linq
Imports System.Windows.Forms

''' <summary>
''' Model Manager → Profiles: a downloaded picture or video model with its LoRAs (and their strength), trigger words,
''' default, positive and negative prompts. One picture profile and one video profile are the chat's defaults.
''' </summary>
Public Class ProfilesPanel
    Inherits UserControl

    Private _list As List(Of MediaProfile)
    Private ReadOnly lstProfiles As New ListBox()
    Private ReadOnly lblChat As Label
    Private ReadOnly txtName As New TextBox()
    Private ReadOnly lblKind As Label
    Private ReadOnly cmbModel As New ComboBox() With {.DropDownStyle = ComboBoxStyle.DropDownList}
    Private ReadOnly chkFast As New CheckBox() With {.AutoSize = True}
    Private ReadOnly gridLoras As New DataGridView()
    Private ReadOnly txtTriggers As New TextBox()
    Private ReadOnly txtDefault As New TextBox() With {.Multiline = True, .ScrollBars = ScrollBars.Vertical}
    Private ReadOnly txtPositive As New TextBox() With {.Multiline = True, .ScrollBars = ScrollBars.Vertical}
    Private ReadOnly txtNegative As New TextBox() With {.Multiline = True, .ScrollBars = ScrollBars.Vertical}
    Private ReadOnly chkDefault As New CheckBox() With {.AutoSize = True}
    Private ReadOnly editor As TableLayoutPanel
    Private ReadOnly lblEmpty As Label
    Private _current As MediaProfile
    Private _loading As Boolean

    Public Sub New()
        Dock = DockStyle.Fill
        BackColor = Theme.CPanel

        '──── left: the profiles ────
        Dim left As New Panel() With {.Dock = DockStyle.Left, .Width = 290, .Padding = New Padding(8), .BackColor = Theme.CPanel}
        lblChat = Theme.MakeLabel("", dim_:=True)
        lblChat.Dock = DockStyle.Top
        lblChat.AutoSize = False
        lblChat.Height = 58
        lstProfiles.Dock = DockStyle.Fill
        lstProfiles.BackColor = Theme.CBack
        lstProfiles.ForeColor = Theme.CText
        lstProfiles.BorderStyle = BorderStyle.None
        lstProfiles.Font = Theme.UiFont
        lstProfiles.ItemHeight = 26
        lstProfiles.DrawMode = DrawMode.OwnerDrawFixed
        AddHandler lstProfiles.DrawItem, AddressOf DrawProfile
        AddHandler lstProfiles.SelectedIndexChanged, Sub() ShowProfile(TryCast(lstProfiles.SelectedItem, MediaProfile))
        Dim btns As New FlowLayoutPanel() With {.Dock = DockStyle.Bottom, .Height = 76, .WrapContents = True, .BackColor = Theme.CPanel}
        Dim btnNewPic = Theme.MakeButton(Localizer.T("➕ Picture profile"))
        Dim btnNewVid = Theme.MakeButton(Localizer.T("➕ Video profile"))
        Dim btnCopy = Theme.MakeButton(Localizer.T("⧉ Duplicate"))
        Dim btnDel = Theme.MakeButton(Localizer.T("🗑 Delete"))
        AddHandler btnNewPic.Click, Sub() NewProfile("picture", Nothing)
        AddHandler btnNewVid.Click, Sub() NewProfile("video", Nothing)
        AddHandler btnCopy.Click, Sub() DuplicateCurrent()
        AddHandler btnDel.Click, Sub() DeleteCurrent()
        btns.Controls.AddRange({btnNewPic, btnNewVid, btnCopy, btnDel})
        left.Controls.Add(lstProfiles)
        left.Controls.Add(lblChat)
        left.Controls.Add(btns)

        '──── right: the editor ────
        editor = New TableLayoutPanel() With {.Dock = DockStyle.Fill, .ColumnCount = 2, .AutoScroll = True, .Padding = New Padding(12, 8, 12, 8), .BackColor = Theme.CPanel}
        editor.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 150))
        editor.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        lblKind = Theme.MakeLabel("", bold:=True)
        Row("", lblKind)
        Row("Name", txtName)
        Row("Model", cmbModel)
        chkFast.Text = Localizer.T("Fast (use the model's speed LoRA when it has one)")
        chkFast.ForeColor = Theme.CText
        Row("", chkFast)

        Theme.StyleGrid(gridLoras)
        gridLoras.Height = 170
        gridLoras.Dock = DockStyle.Fill
        gridLoras.AllowUserToAddRows = False
        gridLoras.RowHeadersVisible = False
        gridLoras.Columns.Add(New DataGridViewCheckBoxColumn() With {.Name = "use", .HeaderText = Localizer.T("Use"), .Width = 46})
        gridLoras.Columns.Add(New DataGridViewTextBoxColumn() With {.Name = "name", .HeaderText = Localizer.T("LoRA"), .ReadOnly = True, .Width = 230})
        gridLoras.Columns.Add(New DataGridViewTextBoxColumn() With {.Name = "weight", .HeaderText = Localizer.T("Strength"), .Width = 80})
        gridLoras.Columns.Add(New DataGridViewTextBoxColumn() With {.Name = "triggers", .HeaderText = Localizer.T("Trigger words"), .ReadOnly = True,
                                                                     .AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill})
        AddHandler gridLoras.CurrentCellDirtyStateChanged, Sub() If gridLoras.IsCurrentCellDirty Then gridLoras.CommitEdit(DataGridViewDataErrorContexts.Commit)
        Row("LoRAs", gridLoras, Localizer.T("Only LoRAs made for this model's family are listed. Get more in the 🧩 LoRA category."))
        Dim btnMyLoras = Theme.MakeButton(Localizer.T("🧩 My LoRAs: add files, edit trigger words…"))
        AddHandler btnMyLoras.Click, Sub()
                                         Dim r = SelectedRecipe()
                                         Using f As New LoraForm(If(r?.Family, ""), If(r?.Name, ""))
                                             f.ShowDialog(FindForm())
                                         End Using
                                         FillLoras(_current, SelectedRecipe())
                                     End Sub
        Row("", btnMyLoras)
        Dim trig As New FlowLayoutPanel() With {.AutoSize = True, .Dock = DockStyle.Fill, .WrapContents = False, .Margin = New Padding(0)}
        txtTriggers.Width = 420
        Dim btnTrig = Theme.MakeButton(Localizer.T("⤓ From the LoRAs"))
        AddHandler btnTrig.Click, Sub() TriggersFromLoras()
        trig.Controls.AddRange({txtTriggers, btnTrig})
        Row("Trigger words", trig)
        txtDefault.Height = 54
        Row("Default prompt", txtDefault, Localizer.T("Used when the chat asks for a picture / video without describing it"))
        txtPositive.Height = 54
        Row("Positive prompt", txtPositive, Localizer.T("Always added (style, quality, look)"))
        txtNegative.Height = 54
        Row("Negative prompt", txtNegative, Localizer.T("What to avoid; empty = the model's own"))
        chkDefault.ForeColor = Theme.CText
        Row("", chkDefault)
        Dim save = Theme.MakeButton(Localizer.T("💾 Save profile"), primary:=True)
        AddHandler save.Click, Sub() SaveCurrent()
        Row("", save)
        For Each c In {CType(txtName, Control), txtTriggers, txtDefault, txtPositive, txtNegative, cmbModel}
            Theme.StyleInput(c)
            c.Dock = DockStyle.Fill
        Next
        txtTriggers.Dock = DockStyle.None
        AddHandler cmbModel.SelectedIndexChanged, Sub() If Not _loading Then FillLoras(_current, SelectedRecipe())

        lblEmpty = Theme.MakeLabel(Localizer.T("Make a profile: a downloaded model with your LoRAs, trigger words and prompts. " &
                                               "Then pick one picture profile and one video profile for the chat."), dim_:=True)
        lblEmpty.Dock = DockStyle.Fill
        lblEmpty.TextAlign = ContentAlignment.MiddleCenter

        Controls.Add(editor)
        Controls.Add(lblEmpty)
        Controls.Add(New Panel() With {.Dock = DockStyle.Left, .Width = 1, .BackColor = Theme.CBorder})
        Controls.Add(left)
        Reload()
    End Sub

    Private Sub Row(label As String, c As Control, Optional hint As String = Nothing)
        Dim l = Theme.MakeLabel(Localizer.T(label))
        l.Margin = New Padding(3, 8, 3, 3)
        editor.Controls.Add(l)
        If hint Is Nothing Then
            editor.Controls.Add(c)
        Else
            Dim p As New TableLayoutPanel() With {.Dock = DockStyle.Fill, .AutoSize = True, .ColumnCount = 1, .Margin = New Padding(0)}
            p.Controls.Add(c)
            Dim h = Theme.MakeLabel(hint, dim_:=True)
            h.AutoSize = True
            p.Controls.Add(h)
            editor.Controls.Add(p)
        End If
    End Sub

    Public Sub Reload(Optional selectId As String = Nothing)
        _list = ProfileLibrary.All()
        lstProfiles.Items.Clear()
        For Each p In _list.OrderBy(Function(x) If(x.IsVideo, 1, 0)).ThenBy(Function(x) x.Name)
            lstProfiles.Items.Add(p)
        Next
        Dim pic = ProfileLibrary.Find(AppCore.Settings.ChatImageProfile)
        Dim vid = ProfileLibrary.Find(AppCore.Settings.ChatVideoProfile)
        lblChat.Text = Localizer.T("Chat uses") & ":" & vbCrLf &
                       "🖼 " & If(pic?.Name, Localizer.T("automatic")) & vbCrLf &
                       "🎬 " & If(vid?.Name, Localizer.T("automatic"))
        Dim want = _list.FirstOrDefault(Function(x) x.Id = selectId)
        If want IsNot Nothing Then
            lstProfiles.SelectedItem = want
        ElseIf lstProfiles.Items.Count > 0 Then
            lstProfiles.SelectedIndex = 0
        Else
            ShowProfile(Nothing)
        End If
    End Sub

    Private Sub DrawProfile(sender As Object, e As DrawItemEventArgs)
        If e.Index < 0 Then Return
        Dim p = CType(lstProfiles.Items(e.Index), MediaProfile)
        Dim sel = (e.State And DrawItemState.Selected) <> 0
        Using b As New SolidBrush(If(sel, Theme.CAccentSoft, Theme.CBack))
            e.Graphics.FillRectangle(b, e.Bounds)
        End Using
        Dim isDefault = p.Id = If(p.IsVideo, AppCore.Settings.ChatVideoProfile, AppCore.Settings.ChatImageProfile)
        Dim text = If(p.IsVideo, "🎬 ", "🖼 ") & p.Name & If(isDefault, "   ★", "")
        TextRenderer.DrawText(e.Graphics, text, If(isDefault, Theme.BoldFont, Theme.UiFont), New Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height),
                              Theme.CText, TextFormatFlags.VerticalCenter Or TextFormatFlags.EndEllipsis)
    End Sub

    Private Function ModelsFor(kind As String) As List(Of MediaRecipe)
        Dim video = kind = "video"
        Return MediaStudio.Recipes().Where(Function(r) r.IsVideo = video AndAlso Not r.IsUpscaler AndAlso r.Kind <> "tool" AndAlso
                                               MediaStudio.UsableVariant(r) IsNot Nothing).OrderBy(Function(r) r.Name).ToList()
    End Function

    Private Function SelectedRecipe() As MediaRecipe
        Return TryCast(cmbModel.SelectedItem, RecipeItem)?.R
    End Function

    Private Class RecipeItem
        Public R As MediaRecipe
        Public Overrides Function ToString() As String
            Return R.Name & "  [" & If(MediaStudio.UsableVariant(R)?.Label, "") & "]"
        End Function
    End Class

    Private Sub ShowProfile(p As MediaProfile)
        _current = p
        editor.Visible = p IsNot Nothing
        lblEmpty.Visible = p Is Nothing
        If p Is Nothing Then Return
        _loading = True
        Try
            lblKind.Text = If(p.IsVideo, "🎬 " & Localizer.T("Video profile"), "🖼 " & Localizer.T("Picture profile"))
            txtName.Text = p.Name
            cmbModel.Items.Clear()
            For Each r In ModelsFor(p.Kind)
                cmbModel.Items.Add(New RecipeItem With {.R = r})
            Next
            Dim found = cmbModel.Items.Cast(Of RecipeItem)().FirstOrDefault(Function(x) x.R.Id = p.Model)
            If found Is Nothing AndAlso Not String.IsNullOrEmpty(p.Model) Then
                Dim r = MediaStudio.Recipes().FirstOrDefault(Function(x) x.Id = p.Model)
                If r IsNot Nothing Then
                    found = New RecipeItem With {.R = r}
                    cmbModel.Items.Insert(0, found)
                End If
            End If
            If found IsNot Nothing Then cmbModel.SelectedItem = found Else If cmbModel.Items.Count > 0 Then cmbModel.SelectedIndex = 0
            chkFast.Checked = p.Fast
            txtTriggers.Text = p.Triggers
            txtDefault.Text = p.DefaultPrompt
            txtPositive.Text = p.Positive
            txtNegative.Text = p.Negative
            chkDefault.Text = If(p.IsVideo, Localizer.T("★ Default video profile for the chat"), Localizer.T("★ Default picture profile for the chat"))
            chkDefault.Checked = p.Id = If(p.IsVideo, AppCore.Settings.ChatVideoProfile, AppCore.Settings.ChatImageProfile)
            FillLoras(p, SelectedRecipe())
        Finally
            _loading = False
        End Try
    End Sub

    Private Sub FillLoras(p As MediaProfile, r As MediaRecipe)
        gridLoras.Rows.Clear()
        If p Is Nothing Then Return
        For Each l In LoraLibrary.All().Where(Function(x) r Is Nothing OrElse LoraLibrary.FamilyMatches(x.Family, r.Family)).OrderBy(Function(x) x.Name)
            Dim pl = p.Loras.FirstOrDefault(Function(x) String.Equals(x.Name, l.Name, StringComparison.OrdinalIgnoreCase))
            gridLoras.Rows.Add(pl IsNot Nothing, l.Name, If(pl?.Weight, l.Weight).ToString("0.##", Globalization.CultureInfo.InvariantCulture), l.Triggers)
        Next
        If gridLoras.Rows.Count = 0 Then
            gridLoras.Rows.Add(False, Localizer.T("(no LoRA for this model yet)"), "", "")
            gridLoras.Rows(0).ReadOnly = True
        End If
    End Sub

    Private Sub TriggersFromLoras()
        Dim words As New List(Of String)()
        For Each row As DataGridViewRow In gridLoras.Rows
            If CBool(If(row.Cells(0).Value, False)) AndAlso Not String.IsNullOrWhiteSpace(CStr(row.Cells(3).Value)) Then words.Add(CStr(row.Cells(3).Value).Trim().Trim(","c))
        Next
        Dim have = txtTriggers.Text.Trim()
        For Each w In words
            If have.IndexOf(w, StringComparison.OrdinalIgnoreCase) < 0 Then have = If(have = "", w, have & ", " & w)
        Next
        txtTriggers.Text = have
    End Sub

    Private Sub SaveCurrent()
        If _current Is Nothing Then Return
        Dim r = SelectedRecipe()
        If r Is Nothing Then
            Localizer.Box(FindForm(), "Download a model for this profile first (Models → Picture or Video).", "Profiles")
            Return
        End If
        _current.Name = If(txtName.Text.Trim() = "", r.Name, txtName.Text.Trim())
        _current.Model = r.Id
        _current.Fast = chkFast.Checked
        _current.Triggers = txtTriggers.Text.Trim()
        _current.DefaultPrompt = txtDefault.Text.Trim()
        _current.Positive = txtPositive.Text.Trim()
        _current.Negative = txtNegative.Text.Trim()
        _current.Loras.Clear()
        For Each row As DataGridViewRow In gridLoras.Rows
            If row.ReadOnly OrElse Not CBool(If(row.Cells(0).Value, False)) Then Continue For
            Dim w As Double = 1
            Double.TryParse(CStr(row.Cells(2).Value).Replace(","c, "."c), Globalization.NumberStyles.Float, Globalization.CultureInfo.InvariantCulture, w)
            _current.Loras.Add(New ProfileLora With {.Name = CStr(row.Cells(1).Value), .Weight = Math.Max(-4, Math.Min(4, w))})
        Next
        Dim list = ProfileLibrary.All()
        list.RemoveAll(Function(x) x.Id = _current.Id)
        list.Add(_current)
        ProfileLibrary.Save(list)
        If chkDefault.Checked Then
            If _current.IsVideo Then AppCore.Settings.ChatVideoProfile = _current.Id Else AppCore.Settings.ChatImageProfile = _current.Id
        ElseIf _current.Id = If(_current.IsVideo, AppCore.Settings.ChatVideoProfile, AppCore.Settings.ChatImageProfile) Then
            If _current.IsVideo Then AppCore.Settings.ChatVideoProfile = "" Else AppCore.Settings.ChatImageProfile = ""
        End If
        AppCore.SaveSettings()
        Reload(_current.Id)
    End Sub

    ''' <summary>A new profile (for a model when one is given, e.g. from Installed → New profile).</summary>
    Public Sub NewProfile(kind As String, r As MediaRecipe)
        If r IsNot Nothing Then kind = If(r.IsVideo, "video", "picture")
        Dim models = ModelsFor(kind)
        If r Is Nothing Then r = models.FirstOrDefault()
        If r Is Nothing Then
            Localizer.Box(FindForm(), If(kind = "video", "Download a video model first (Models → Video).", "Download a picture model first (Models → Picture)."), "Profiles")
            Return
        End If
        Dim p As New MediaProfile With {.Kind = kind, .Model = r.Id, .Name = r.Name & " · " & Localizer.T("my profile")}
        Dim list = ProfileLibrary.All()
        list.Add(p)
        ' the first profile of a kind becomes the chat's default
        If list.Where(Function(x) x.Kind = kind).Count() = 1 Then
            If kind = "video" Then AppCore.Settings.ChatVideoProfile = p.Id Else AppCore.Settings.ChatImageProfile = p.Id
            AppCore.SaveSettings()
        End If
        ProfileLibrary.Save(list)
        Reload(p.Id)
        txtName.Focus()
        txtName.SelectAll()
    End Sub

    Private Sub DuplicateCurrent()
        If _current Is Nothing Then Return
        Dim c = Newtonsoft.Json.JsonConvert.DeserializeObject(Of MediaProfile)(Newtonsoft.Json.JsonConvert.SerializeObject(_current))
        c.Id = Guid.NewGuid().ToString("N").Substring(0, 10)
        c.Name &= " (2)"
        Dim list = ProfileLibrary.All()
        list.Add(c)
        ProfileLibrary.Save(list)
        Reload(c.Id)
    End Sub

    Private Sub DeleteCurrent()
        If _current Is Nothing Then Return
        If Localizer.Box(FindForm(), String.Format(Localizer.T("Delete the profile {0}? The model and LoRAs stay."), _current.Name), "Profiles",
                         MessageBoxButtons.YesNo, MessageBoxIcon.Question) <> DialogResult.Yes Then Return
        Dim list = ProfileLibrary.All()
        list.RemoveAll(Function(x) x.Id = _current.Id)
        ProfileLibrary.Save(list)
        If AppCore.Settings.ChatImageProfile = _current.Id Then AppCore.Settings.ChatImageProfile = ""
        If AppCore.Settings.ChatVideoProfile = _current.Id Then AppCore.Settings.ChatVideoProfile = ""
        AppCore.SaveSettings()
        Reload()
    End Sub
End Class
