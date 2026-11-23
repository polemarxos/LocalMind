Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.IO
Imports System.Linq
Imports System.Threading
Imports System.Windows.Forms
Imports Text_ = System.Text.RegularExpressions

''' <summary>
''' 🎭 Cast: your characters and locations. Tick the ones to use in the next picture or video
''' (or write @Name in the prompt). Add a few clear pictures of each so they look the same every time.
''' </summary>
Public Class CastForm
    Inherits Form

    Private ReadOnly _list As List(Of CastLibrary.CastMember)
    Private ReadOnly lst As New ListView() With {.Dock = DockStyle.Fill, .View = View.LargeIcon, .MultiSelect = False, .CheckBoxes = True}
    Private ReadOnly icons As New ImageList() With {.ImageSize = New Size(96, 96), .ColorDepth = ColorDepth.Depth32Bit}
    Private ReadOnly txtName As New TextBox() With {.Width = 280}
    Private ReadOnly cmbKind As New ComboBox() With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 160}
    Private ReadOnly txtDesc As New TextBox() With {.Multiline = True, .Height = 90, .Width = 380, .ScrollBars = ScrollBars.Vertical}
    Private ReadOnly pics As New FlowLayoutPanel() With {.Height = 110, .Width = 380, .AutoScroll = True}
    Private ReadOnly editor As New TableLayoutPanel() With {.Dock = DockStyle.Right, .Width = 420, .ColumnCount = 1, .Padding = New Padding(12)}
    Private ReadOnly cmbVoice As New ComboBox() With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 185}
    Private ReadOnly cmbLora As New ComboBox() With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 185}
    Private ReadOnly lblWork As Label = Theme.MakeLabel("", dim_:=True)
    Private _current As CastLibrary.CastMember
    Private _loading As Boolean

    ''' <summary>The members ticked when the window closed with "Use".</summary>
    Public ReadOnly Property Picked As New List(Of CastLibrary.CastMember)

    Public Sub New(Optional preselected As IEnumerable(Of String) = Nothing)
        Text = Localizer.T("🎭 Cast — characters and locations")
        Size = New Size(1060, 760)
        MinimumSize = New Size(860, 520)
        StartPosition = FormStartPosition.CenterParent
        Theme.ApplyForm(Me)
        _list = CastLibrary.Load()

        Dim top As New FlowLayoutPanel() With {.Dock = DockStyle.Top, .AutoSize = True, .Padding = New Padding(8, 8, 8, 4)}
        Dim btnChar = Theme.MakeButton(Localizer.T("＋ Character"))
        Dim btnPlace = Theme.MakeButton(Localizer.T("＋ Location"))
        Dim btnDel = Theme.MakeButton(Localizer.T("🗑 Delete"))
        Dim hint = Theme.MakeLabel(Localizer.T("Tick who and where to use, or write @Name in your prompt. 2–3 clear pictures of each work best."), dim_:=True)
        top.Controls.AddRange({btnChar, btnPlace, btnDel, hint})
        AddHandler btnChar.Click, Sub() AddMember("character")
        AddHandler btnPlace.Click, Sub() AddMember("location")
        AddHandler btnDel.Click, Sub()
                                     If _current Is Nothing Then Return
                                     If Localizer.Box(Me, String.Format(Localizer.T("Delete {0} and its pictures?"), _current.Name), "Cast",
                                                      MessageBoxButtons.YesNo, MessageBoxIcon.Question) <> DialogResult.Yes Then Return
                                     CastLibrary.Delete(_list, _current)
                                     _current = Nothing
                                     FillList()
                                     ShowMember()
                                 End Sub

        lst.LargeImageList = icons
        lst.BackColor = Theme.CBack
        lst.ForeColor = Theme.CText
        lst.BorderStyle = BorderStyle.None
        AddHandler lst.SelectedIndexChanged, Sub()
                                                 If lst.SelectedItems.Count > 0 Then
                                                     _current = TryCast(lst.SelectedItems(0).Tag, CastLibrary.CastMember)
                                                     ShowMember()
                                                 End If
                                             End Sub

        ' editor on the right
        cmbKind.Items.AddRange({Localizer.T("🎭 Character"), Localizer.T("📍 Location")})
        For Each c As Control In {txtName, txtDesc, cmbKind, cmbVoice, cmbLora}
            Theme.StyleInput(c)
        Next
        Dim btnAddPic = Theme.MakeButton(Localizer.T("🖼 Add pictures…"))
        Dim btnDescribe = Theme.MakeButton(Localizer.T("📝 Describe from picture"))
        Dim lblHelp = Theme.MakeLabel(Localizer.T("Description: what must stay the same (face, hair, clothes / place, colors, light)."), dim_:=True)
        lblHelp.MaximumSize = New Size(380, 0)
        editor.BackColor = Theme.CRaised
        editor.Controls.Add(Theme.MakeLabel(Localizer.T("Name (use it as @Name in prompts)"), bold:=True))
        editor.Controls.Add(txtName)
        editor.Controls.Add(Theme.MakeLabel(Localizer.T("Type"), bold:=True))
        editor.Controls.Add(cmbKind)
        editor.Controls.Add(Theme.MakeLabel(Localizer.T("Description"), bold:=True))
        editor.Controls.Add(txtDesc)
        editor.Controls.Add(lblHelp)
        editor.Controls.Add(Theme.MakeLabel(Localizer.T("Pictures (right-click one to remove it)"), bold:=True))
        editor.Controls.Add(pics)
        Dim picBar As New FlowLayoutPanel() With {.AutoSize = True}
        picBar.Controls.AddRange({btnAddPic, btnDescribe})
        editor.Controls.Add(picBar)
        ' more pictures and LoRA training set
        Dim btnMore = Theme.MakeButton(Localizer.T("📸 Make more pictures"))
        Dim btnTrain = Theme.MakeButton(Localizer.T("📦 LoRA training set…"))
        Dim moreBar As New FlowLayoutPanel() With {.AutoSize = True}
        moreBar.Controls.AddRange({btnMore, btnTrain})
        editor.Controls.Add(moreBar)
        lblWork.MaximumSize = New Size(380, 0)
        editor.Controls.Add(lblWork)
        ' the member's voice and LoRA
        Dim vl As New TableLayoutPanel() With {.AutoSize = True, .ColumnCount = 2}
        vl.Controls.Add(Theme.MakeLabel(Localizer.T("Voice (talking videos)"), bold:=True), 0, 0)
        vl.Controls.Add(Theme.MakeLabel(Localizer.T("Own LoRA (optional)"), bold:=True), 1, 0)
        vl.Controls.Add(cmbVoice, 0, 1)
        vl.Controls.Add(cmbLora, 1, 1)
        editor.Controls.Add(vl)
        AddHandler cmbVoice.SelectedIndexChanged, Sub() SaveCurrent()
        AddHandler cmbLora.SelectedIndexChanged, Sub() SaveCurrent()
        AddHandler btnMore.Click, Async Sub()
                                      If _current Is Nothing Then Return
                                      Dim m = _current
                                      btnMore.Enabled = False
                                      Try
                                          Dim made = Await CastLibrary.MakeMorePicturesAsync(m, 4, Sub(p, t) Me.Post(Sub() lblWork.Text = t), CancellationToken.None)
                                          CastLibrary.Save(_list)
                                          For Each f In made
                                              Try : IO.File.Delete(f) : Catch : End Try
                                          Next
                                          lblWork.Text = String.Format(Localizer.T("{0} new pictures added — right-click any that don't look right to remove them."), made.Count)
                                          FillList()
                                          ShowMember()
                                      Catch ex As Exception
                                          lblWork.Text = "⚠ " & ex.Message.Split({vbCr, vbLf}, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()
                                      Finally
                                          btnMore.Enabled = True
                                      End Try
                                  End Sub
        AddHandler btnTrain.Click, Sub()
                                       If _current Is Nothing OrElse CastLibrary.ImagePaths(_current).Count = 0 Then
                                           Localizer.Box(Me, "Add a picture first.", "Cast")
                                           Return
                                       End If
                                       Using d As New FolderBrowserDialog() With {.Description = Localizer.T("Where to save the training set")}
                                           If d.ShowDialog(Me) <> DialogResult.OK Then Return
                                           Dim trigger = Text_.Regex.Replace(_current.Name.ToLowerInvariant(), "[^a-z0-9]+", "") & "_lm"
                                           If trigger = "_lm" Then trigger = "castmember_lm"
                                           Dim dir = CastLibrary.ExportTrainingSet(_current, d.SelectedPath, trigger)
                                           lblWork.Text = String.Format(Localizer.T("Saved {0} pictures with captions. Trigger word: {1}. Train the LoRA (see README.txt), add it in My LoRAs and pick it here."),
                                                                        CastLibrary.ImagePaths(_current).Count, trigger)
                                           ModelManagerForm.OpenFolder(dir)
                                       End Using
                                   End Sub
        AddHandler txtName.TextChanged, Sub() SaveCurrent()
        AddHandler txtDesc.TextChanged, Sub() SaveCurrent()
        AddHandler cmbKind.SelectedIndexChanged, Sub() SaveCurrent()
        AddHandler btnAddPic.Click, Sub() AddPictures()
        AddHandler btnDescribe.Click, Async Sub()
                                          If _current Is Nothing OrElse CastLibrary.ImagePaths(_current).Count = 0 Then
                                              Localizer.Box(Me, "Add a picture first.", "Cast")
                                              Return
                                          End If
                                          btnDescribe.Enabled = False
                                          btnDescribe.Text = Localizer.T("Looking…")
                                          Try
                                              txtDesc.Text = Await CastLibrary.DescribeAsync(CastLibrary.ImagePaths(_current)(0), _current.IsLocation, CancellationToken.None)
                                          Catch ex As Exception
                                              Localizer.Box(Me, ex.Message, "Cast")
                                          Finally
                                              btnDescribe.Enabled = True
                                              btnDescribe.Text = Localizer.T("📝 Describe from picture")
                                          End Try
                                      End Sub

        Dim bottom As New FlowLayoutPanel() With {.Dock = DockStyle.Bottom, .Height = 48, .FlowDirection = FlowDirection.RightToLeft, .Padding = New Padding(8)}
        Dim btnUse = Theme.MakeButton(Localizer.T("✔ Use the ticked ones"), primary:=True)
        Dim btnClose = Theme.MakeButton(Localizer.T("Close"))
        AddHandler btnUse.Click, Sub()
                                     Picked.Clear()
                                     For Each it As ListViewItem In lst.CheckedItems
                                         Picked.Add(CType(it.Tag, CastLibrary.CastMember))
                                     Next
                                     DialogResult = DialogResult.OK
                                 End Sub
        AddHandler btnClose.Click, Sub() DialogResult = DialogResult.Cancel
        bottom.Controls.AddRange({btnUse, btnClose})

        Controls.Add(lst)
        Controls.Add(editor)
        Controls.Add(top)
        Controls.Add(bottom)
        FillList(preselected)
        ShowMember()
    End Sub

    Private Sub FillList(Optional checkedIds As IEnumerable(Of String) = Nothing)
        Dim keep = If(checkedIds?.ToList(), lst.CheckedItems.Cast(Of ListViewItem)().Select(Function(i) CType(i.Tag, CastLibrary.CastMember).Id).ToList())
        lst.BeginUpdate()
        lst.Items.Clear()
        icons.Images.Clear()
        For Each m In _list.OrderBy(Function(x) If(x.IsLocation, 1, 0)).ThenBy(Function(x) x.Name)
            Dim key = m.Id
            Dim first = CastLibrary.ImagePaths(m).FirstOrDefault()
            Try
                If first IsNot Nothing Then
                    Using img = ImageTools.Load(first)
                        icons.Images.Add(key, Thumb(img))
                    End Using
                End If
            Catch
            End Try
            If Not icons.Images.ContainsKey(key) Then icons.Images.Add(key, Placeholder(m))
            Dim it As New ListViewItem(m.ToString()) With {.Tag = m, .ImageKey = If(icons.Images.ContainsKey(key), key, Nothing), .Checked = keep.Contains(m.Id)}
            lst.Items.Add(it)
            If _current IsNot Nothing AndAlso _current.Id = m.Id Then it.Selected = True
        Next
        If lst.SelectedItems.Count = 0 AndAlso lst.Items.Count > 0 Then
            lst.Items(0).Selected = True
            _current = TryCast(lst.Items(0).Tag, CastLibrary.CastMember)
        End If
        lst.EndUpdate()
    End Sub

    ''' <summary>A neutral tile with the first letter, for members without pictures yet.</summary>
    Private Shared Function Placeholder(m As CastLibrary.CastMember) As Image
        Dim bmp As New Bitmap(96, 96)
        Using g = Graphics.FromImage(bmp)
            g.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias
            g.TextRenderingHint = Drawing.Text.TextRenderingHint.AntiAliasGridFit
            g.Clear(Color.Transparent)
            Dim back = If(m.IsLocation, Color.FromArgb(38, 70, 60), Color.FromArgb(40, 58, 96))
            Using b As New SolidBrush(back)
                g.FillEllipse(b, 8, 8, 80, 80)
            End Using
            Dim letter = If(String.IsNullOrWhiteSpace(m.Name), "?", m.Name.Trim().Substring(0, 1).ToUpperInvariant())
            Using f As New Font("Segoe UI Semibold", 30.0F), sf As New StringFormat With {.Alignment = StringAlignment.Center, .LineAlignment = StringAlignment.Center}
                g.DrawString(letter, f, Brushes.White, New RectangleF(8, 8, 80, 80), sf)
            End Using
        End Using
        Return bmp
    End Function

    Private Shared Function Thumb(img As Image) As Image
        Dim b As New Bitmap(96, 96)
        Using g = Graphics.FromImage(b)
            g.Clear(Theme.CBack)
            g.InterpolationMode = Drawing2D.InterpolationMode.HighQualityBicubic
            Dim k = Math.Min(96.0 / img.Width, 96.0 / img.Height)
            Dim w = CInt(img.Width * k), h = CInt(img.Height * k)
            g.DrawImage(img, (96 - w) \ 2, (96 - h) \ 2, w, h)
        End Using
        Return b
    End Function

    Private Sub AddMember(kind As String)
        Dim m As New CastLibrary.CastMember With {.Kind = kind, .Name = If(kind = "location", Localizer.T("New place"), Localizer.T("New character"))}
        _list.Add(m)
        CastLibrary.Save(_list)
        _current = m
        FillList()
        ShowMember()
        AddPictures()
        txtName.Focus()
        txtName.SelectAll()
    End Sub

    Private Sub AddPictures()
        If _current Is Nothing Then Return
        Using d As New OpenFileDialog() With {.Multiselect = True, .Filter = "Pictures|*.png;*.jpg;*.jpeg;*.webp;*.bmp"}
            If d.ShowDialog(Me) <> DialogResult.OK Then Return
            For Each f In d.FileNames
                CastLibrary.AddImage(_current, f)
            Next
        End Using
        CastLibrary.Save(_list)
        FillList()
        ShowMember()
    End Sub

    Private Sub ShowMember()
        _loading = True
        editor.Enabled = _current IsNot Nothing
        txtName.Text = If(_current?.Name, "")
        txtDesc.Text = If(_current?.Description, "")
        cmbKind.SelectedIndex = If(_current IsNot Nothing AndAlso _current.IsLocation, 1, 0)
        cmbVoice.Items.Clear()
        cmbVoice.Items.Add(Localizer.T("(default voice)"))
        For Each v In VoiceLibrary.All()
            cmbVoice.Items.Add(v.Name)
        Next
        Dim vi = If(String.IsNullOrEmpty(_current?.Voice), -1, cmbVoice.Items.IndexOf(_current.Voice))
        cmbVoice.SelectedIndex = Math.Max(0, vi)
        cmbVoice.Enabled = _current IsNot Nothing AndAlso Not _current.IsLocation
        cmbLora.Items.Clear()
        cmbLora.Items.Add(Localizer.T("(none)"))
        For Each l In LoraLibrary.All()
            cmbLora.Items.Add(l.Name)
        Next
        Dim li = If(String.IsNullOrEmpty(_current?.Lora), -1, cmbLora.Items.IndexOf(_current.Lora))
        cmbLora.SelectedIndex = Math.Max(0, li)
        pics.Controls.Clear()
        If _current IsNot Nothing Then
            For Each f In CastLibrary.ImagePaths(_current)
                Dim file = f
                Dim pb As New PictureBox() With {.Size = New Size(92, 92), .SizeMode = PictureBoxSizeMode.Zoom, .BackColor = Theme.CBack, .Margin = New Padding(2)}
                Try
                    pb.Image = ImageTools.Load(f)
                Catch
                End Try
                AddHandler pb.MouseUp, Sub(s, e)
                                           If e.Button <> MouseButtons.Right OrElse _current Is Nothing Then Return
                                           _current.Images.Remove(Path.GetFileName(file))
                                           Try
                                               pb.Image?.Dispose()
                                               IO.File.Delete(file)
                                           Catch
                                           End Try
                                           CastLibrary.Save(_list)
                                           FillList()
                                           ShowMember()
                                       End Sub
                pics.Controls.Add(pb)
            Next
        End If
        _loading = False
    End Sub

    Private Sub SaveCurrent()
        If _loading OrElse _current Is Nothing Then Return
        _current.Name = txtName.Text.Trim()
        _current.Description = txtDesc.Text.Trim()
        _current.Kind = If(cmbKind.SelectedIndex = 1, "location", "character")
        _current.Voice = If(cmbVoice.SelectedIndex > 0, cmbVoice.SelectedItem.ToString(), "")
        _current.Lora = If(cmbLora.SelectedIndex > 0, cmbLora.SelectedItem.ToString(), "")
        CastLibrary.Save(_list)
        For Each it As ListViewItem In lst.Items
            If it.Tag Is _current Then it.Text = _current.ToString()
        Next
    End Sub
End Class
