Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.Linq
Imports System.Windows.Forms

''' <summary>
''' Model Manager → Profiles: a downloaded model set up for the chat. Picture, edit and video profiles have LoRAs
''' (with their strength), trigger words, default, positive and negative prompts; music profiles a default style and
''' words always added to it; voice profiles a voice model and a saved voice. One profile of each kind is the chat's default.
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
    Private ReadOnly cmbVoice As New ComboBox() With {.DropDownStyle = ComboBoxStyle.DropDownList}
    ' rows only pictures / video have, the voice row, the rows whose label changes with the kind
    Private ReadOnly _visualRows As New List(Of Control)()
    Private ReadOnly _voiceRows As New List(Of Control)()
    Private _lblDefault, _lblPositive As Label
    Private _hintDefault, _hintPositive As Label
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
        lblChat.Height = 112
        lstProfiles.Dock = DockStyle.Fill
        lstProfiles.BackColor = Theme.CBack
        lstProfiles.ForeColor = Theme.CText
        lstProfiles.BorderStyle = BorderStyle.None
        lstProfiles.Font = Theme.UiFont
        lstProfiles.ItemHeight = 26
        lstProfiles.DrawMode = DrawMode.OwnerDrawFixed
        AddHandler lstProfiles.DrawItem, AddressOf DrawProfile
        AddHandler lstProfiles.SelectedIndexChanged, Sub() ShowProfile(TryCast(lstProfiles.SelectedItem, MediaProfile))
        Dim btns As New FlowLayoutPanel() With {.Dock = DockStyle.Bottom, .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .WrapContents = True, .BackColor = Theme.CPanel}
        Dim btnNew = Theme.MakeButton(Localizer.T("➕ New profile ▾"), primary:=True)
        Dim newMenu As New ContextMenuStrip()
        For Each k In Kinds
            Dim kk = k
            Dim it = newMenu.Items.Add(KindIcon(k) & "  " & Localizer.T(KindName(k)))
            AddHandler it.Click, Sub() NewProfile(kk.Id, Nothing)
        Next
        AddHandler btnNew.Click, Sub() newMenu.Show(btnNew, New Point(0, btnNew.Height))
        Dim btnCopy = Theme.MakeButton(Localizer.T("⧉ Duplicate"))
        Dim btnDel = Theme.MakeButton(Localizer.T("🗑 Delete"))
        AddHandler btnCopy.Click, Sub() DuplicateCurrent()
        AddHandler btnDel.Click, Sub() DeleteCurrent()
        btns.Controls.AddRange({btnNew, btnCopy, btnDel})
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
        _visualRows.AddRange(Row("", chkFast))
        _voiceRows.AddRange(Row("Voice", cmbVoice, Localizer.T("A saved voice (Characters → New voice from a file). Used when the chat asks to speak without a recording.")))

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
        _visualRows.AddRange(Row("LoRAs", gridLoras, Localizer.T("Only LoRAs made for this model's family are listed. Get more in the 🧩 LoRA category.")))
        Dim btnMyLoras = Theme.MakeButton(Localizer.T("🧩 My LoRAs: add files, edit trigger words…"))
        AddHandler btnMyLoras.Click, Sub()
                                         Dim r = SelectedRecipe()
                                         Using f As New LoraForm(If(r?.Family, ""), If(r?.Name, ""))
                                             f.ShowDialog(FindForm())
                                         End Using
                                         FillLoras(_current, SelectedRecipe())
                                     End Sub
        _visualRows.AddRange(Row("", btnMyLoras))
        Dim trig As New FlowLayoutPanel() With {.AutoSize = True, .Dock = DockStyle.Fill, .WrapContents = False, .Margin = New Padding(0)}
        txtTriggers.Width = 420
        Dim btnTrig = Theme.MakeButton(Localizer.T("⤓ From the LoRAs"))
        AddHandler btnTrig.Click, Sub() TriggersFromLoras()
        trig.Controls.AddRange({txtTriggers, btnTrig})
        _visualRows.AddRange(Row("Trigger words", trig))
        txtDefault.Height = 54
        Dim rd = Row("Default prompt", txtDefault, Localizer.T("Used when the chat asks for a picture / video without describing it"))
        _lblDefault = CType(rd(0), Label)
        _hintDefault = _lastHint
        txtPositive.Height = 54
        Dim rp = Row("Positive prompt", txtPositive, Localizer.T("Always added (style, quality, look)"))
        _lblPositive = CType(rp(0), Label)
        _hintPositive = _lastHint
        txtNegative.Height = 54
        _visualRows.AddRange(Row("Negative prompt", txtNegative, Localizer.T("What to avoid; empty = the model's own")))
        chkDefault.ForeColor = Theme.CText
        Row("", chkDefault)
        Dim save = Theme.MakeButton(Localizer.T("💾 Save profile"), primary:=True)
        AddHandler save.Click, Sub() SaveCurrent()
        Row("", save)
        For Each c In {CType(txtName, Control), txtTriggers, txtDefault, txtPositive, txtNegative, cmbModel, cmbVoice}
            Theme.StyleInput(c)
            c.Dock = DockStyle.Fill
        Next
        txtTriggers.Dock = DockStyle.None
        AddHandler cmbModel.SelectedIndexChanged, Sub() If Not _loading AndAlso _current IsNot Nothing AndAlso _current.IsVisual Then FillLoras(_current, SelectedRecipe())

        lblEmpty = Theme.MakeLabel(Localizer.T("Make a profile: a downloaded model with your LoRAs, trigger words and prompts (or, for music and voice, " &
                                               "a default style or voice). One profile of each kind is used by the chat."), dim_:=True)
        lblEmpty.AutoSize = False
        lblEmpty.Dock = DockStyle.Fill
        lblEmpty.Padding = New Padding(40)
        lblEmpty.TextAlign = ContentAlignment.MiddleCenter

        Controls.Add(editor)
        Controls.Add(lblEmpty)
        Controls.Add(New Panel() With {.Dock = DockStyle.Left, .Width = 1, .BackColor = Theme.CBorder})
        Controls.Add(left)
        Reload()
    End Sub

    Private _lastHint As Label

    ''' + "'''" + ''' <summary>Adds a labelled row; returns the label and the control (or its panel with the hint).</summary>
    Private Function Row(label As String, c As Control, Optional hint As String = Nothing) As Control()
        Dim l = Theme.MakeLabel(Localizer.T(label))
        l.Margin = New Padding(3, 8, 3, 3)
        editor.Controls.Add(l)
        _lastHint = Nothing
        If hint Is Nothing Then
            editor.Controls.Add(c)
            Return {l, c}
        End If
        Dim p As New TableLayoutPanel() With {.Dock = DockStyle.Fill, .AutoSize = True, .ColumnCount = 1, .Margin = New Padding(0)}
        p.Controls.Add(c)
        Dim h = Theme.MakeLabel(hint, dim_:=True)
        h.AutoSize = True
        p.Controls.Add(h)
        editor.Controls.Add(p)
        _lastHint = h
        Return {l, p}
    End Function

    '──── the kinds of profile ────
    Private Shared ReadOnly Kinds As (Id As String, Name As String, Icon As String)() = {
        ("picture", "Picture profile", "🖼"), ("edit", "Edit profile (changes pictures)", "✏"), ("video", "Video profile", "🎬"),
        ("music", "Music profile", "🎵"), ("voice", "Voice profile (cloned voices)", "🗣")}

    Private Shared Function KindIcon(k As (Id As String, Name As String, Icon As String)) As String
        Return k.Icon
    End Function
    Private Shared Function KindName(k As (Id As String, Name As String, Icon As String)) As String
        Return k.Name
    End Function
    Private Shared Function KindOf(id As String) As (Id As String, Name As String, Icon As String)
        Return Kinds.FirstOrDefault(Function(k) k.Id = id)
    End Function

    Public Sub Reload(Optional selectId As String = Nothing)
        _list = ProfileLibrary.All()
        lstProfiles.Items.Clear()
        For Each p In _list.OrderBy(Function(x) Array.FindIndex(Kinds, Function(k) k.Id = x.Kind)).ThenBy(Function(x) x.Name)
            lstProfiles.Items.Add(p)
        Next
        Dim lines As New List(Of String) From {Localizer.T("Chat uses") & ":"}
        For Each k In Kinds
            lines.Add(k.Icon & " " & If(ProfileLibrary.Find(ProfileLibrary.DefaultIdFor(k.Id))?.Name, Localizer.T("automatic")))
        Next
        lblChat.Text = String.Join(vbCrLf, lines)
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
        Dim isDefault = p.Id = ProfileLibrary.DefaultIdFor(p.Kind)
        Dim text = KindOf(p.Kind).Icon & " " & p.Name & If(isDefault, "   ★", "")
        TextRenderer.DrawText(e.Graphics, text, If(isDefault, Theme.BoldFont, Theme.UiFont), New Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height),
                              Theme.CText, TextFormatFlags.VerticalCenter Or TextFormatFlags.EndEllipsis)
    End Sub

    ''' + "'''" + ''' <summary>The downloaded models a profile of this kind can use.</summary>
    Private Function ModelsFor(kind As String) As List(Of ModelItem)
        If kind = "music" OrElse kind = "voice" Then
            Return AudioStudio.Models().Where(Function(m) (m.IsMusic = (kind = "music")) AndAlso (kind <> "voice" OrElse m.Clone) AndAlso AudioStudio.IsInstalled(m)).
                   OrderBy(Function(m) m.Name).Select(Function(m) New ModelItem With {.Id = m.Id, .Text = m.Name}).ToList()
        End If
        Dim video = kind = "video"
        Return MediaStudio.Recipes().Where(Function(r) r.IsVideo = video AndAlso Not r.IsUpscaler AndAlso r.Kind <> "tool" AndAlso
                                               MediaStudio.UsableVariant(r) IsNot Nothing AndAlso
                                               (kind <> "edit" OrElse r.Can("edit") OrElse r.Can("restyle"))).OrderBy(Function(r) r.Name).
                                     Select(Function(r) New ModelItem With {.Id = r.Id, .R = r, .Text = r.Name & "  [" & If(MediaStudio.UsableVariant(r)?.Label, "") & "]"}).ToList()
    End Function

    Private Function SelectedRecipe() As MediaRecipe
        Return TryCast(cmbModel.SelectedItem, ModelItem)?.R
    End Function

    Private Class ModelItem
        Public Id As String
        Public Text As String
        Public R As MediaRecipe
        Public Overrides Function ToString() As String
            Return Text
        End Function
    End Class

    Private Sub ShowProfile(p As MediaProfile)
        _current = p
        editor.Visible = p IsNot Nothing
        lblEmpty.Visible = p Is Nothing
        If p Is Nothing Then Return
        _loading = True
        Try
            Dim k = KindOf(p.Kind)
            lblKind.Text = k.Icon & " " & Localizer.T(k.Name)
            For Each c In _visualRows
                c.Visible = p.IsVisual
            Next
            For Each c In _voiceRows
                c.Visible = p.Kind = "voice"
            Next
            Dim music = p.Kind = "music", voice = p.Kind = "voice"
            _lblDefault.Text = Localizer.T(If(music, "Default style", If(voice, "Default words", "Default prompt")))
            _hintDefault.Text = Localizer.T(If(music, "Used when the chat asks for a song without describing it (genre, mood, instruments, voice)",
                                            If(voice, "Spoken when the chat gives no words", "Used when the chat asks for a picture / video without describing it")))
            _lblPositive.Text = Localizer.T(If(music, "Always added", "Positive prompt"))
            _hintPositive.Text = Localizer.T(If(music, "Added to every style (e.g. high quality mix, studio sound)", "Always added (style, quality, look)"))
            _lblPositive.Visible = Not voice
            txtPositive.Parent.Visible = Not voice
            txtName.Text = p.Name
            cmbModel.Items.Clear()
            For Each m In ModelsFor(p.Kind)
                cmbModel.Items.Add(m)
            Next
            Dim found = cmbModel.Items.Cast(Of ModelItem)().FirstOrDefault(Function(x) x.Id = p.Model)
            If found Is Nothing AndAlso Not String.IsNullOrEmpty(p.Model) Then
                found = New ModelItem With {.Id = p.Model, .Text = p.Model & "  " & Localizer.T("(model not downloaded)"),
                                            .R = MediaStudio.Recipes().FirstOrDefault(Function(x) x.Id = p.Model)}
                cmbModel.Items.Insert(0, found)
            End If
            cmbVoice.Items.Clear()
            cmbVoice.Items.Add(Localizer.T("(none — a recording must be attached)"))
            For Each v In VoiceLibrary.All()
                cmbVoice.Items.Add(v.Name)
            Next
            Dim vi = If(String.IsNullOrEmpty(p.Voice), -1, cmbVoice.Items.IndexOf(p.Voice))
            cmbVoice.SelectedIndex = Math.Max(0, vi)
            If found IsNot Nothing Then cmbModel.SelectedItem = found Else If cmbModel.Items.Count > 0 Then cmbModel.SelectedIndex = 0
            chkFast.Checked = p.Fast
            txtTriggers.Text = p.Triggers
            txtDefault.Text = p.DefaultPrompt
            txtPositive.Text = p.Positive
            txtNegative.Text = p.Negative
            chkDefault.Text = Localizer.T("★ Use this profile in the chat")
            chkDefault.Checked = p.Id = ProfileLibrary.DefaultIdFor(p.Kind)
            If p.IsVisual Then FillLoras(p, SelectedRecipe())
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
        Dim item = TryCast(cmbModel.SelectedItem, ModelItem)
        If item Is Nothing Then
            Localizer.Box(FindForm(), "Download a model for this profile first (Get models).", "Profiles")
            Return
        End If
        _current.Name = If(txtName.Text.Trim() = "", item.Text, txtName.Text.Trim())
        _current.Model = item.Id
        _current.Voice = If(_current.Kind = "voice" AndAlso cmbVoice.SelectedIndex > 0, cmbVoice.SelectedItem.ToString(), "")
        _current.Fast = chkFast.Checked
        _current.Triggers = txtTriggers.Text.Trim()
        _current.DefaultPrompt = txtDefault.Text.Trim()
        _current.Positive = txtPositive.Text.Trim()
        _current.Negative = txtNegative.Text.Trim()
        _current.Loras.Clear()
        For Each row As DataGridViewRow In If(_current.IsVisual, gridLoras.Rows.Cast(Of DataGridViewRow)(), Enumerable.Empty(Of DataGridViewRow)())
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
            ProfileLibrary.SetDefault(_current.Kind, _current.Id)
        ElseIf _current.Id = ProfileLibrary.DefaultIdFor(_current.Kind) Then
            ProfileLibrary.SetDefault(_current.Kind, "")
        End If
        Reload(_current.Id)
    End Sub

    ''' <summary>A new profile (for a model when one is given, e.g. from Installed → New profile).</summary>
    Public Sub NewProfile(kind As String, r As MediaRecipe)
        If r IsNot Nothing AndAlso kind <> "edit" Then kind = If(r.IsVideo, "video", "picture")
        Dim models = ModelsFor(kind)
        Dim m = If(r IsNot Nothing, models.FirstOrDefault(Function(x) x.Id = r.Id), models.FirstOrDefault())
        If m Is Nothing Then
            Localizer.Box(FindForm(), Localizer.T(Select1(kind)), "Profiles")
            Return
        End If
        Dim p As New MediaProfile With {.Kind = kind, .Model = m.Id, .Name = If(m.R?.Name, m.Text) & " · " & Localizer.T("my profile")}
        Dim list = ProfileLibrary.All()
        list.Add(p)
        ' the first profile of a kind becomes the chat's default
        If list.Where(Function(x) x.Kind = kind).Count() = 1 Then ProfileLibrary.SetDefault(kind, p.Id)
        ProfileLibrary.Save(list)
        Reload(p.Id)
        txtName.Focus()
        txtName.SelectAll()
    End Sub

    Private Shared Function Select1(kind As String) As String
        Select Case kind
            Case "video" : Return "Download a video model first (Get models → Video)."
            Case "edit" : Return "Download a picture model that can edit pictures first (Get models → Picture, e.g. Qwen-Image-Edit or FLUX Kontext)."
            Case "music" : Return "Download a music model first (Get models → Music)."
            Case "voice" : Return "Download a voice model that copies voices first (Get models → Voice)."
            Case Else : Return "Download a picture model first (Get models → Picture)."
        End Select
    End Function

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
        If ProfileLibrary.DefaultIdFor(_current.Kind) = _current.Id Then ProfileLibrary.SetDefault(_current.Kind, "")
        Reload()
    End Sub
End Class
