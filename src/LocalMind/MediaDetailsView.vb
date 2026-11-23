Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.Globalization
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms

''' <summary>
''' The right-hand "model page" in the Pictures &amp; video tab: what the model is, the settings LocalMind uses
''' (steps, CFG, sampler, size, video length), its files, stats, description and sample pictures.
''' </summary>
Public Class MediaDetailsView
    Inherits Panel

    Private ReadOnly _hw As HardwareInfo = HardwareInfo.Current()
    Private ReadOnly flow As New FlowLayoutPanel() With {.Dock = DockStyle.Fill, .FlowDirection = FlowDirection.TopDown, .WrapContents = False,
                                                         .AutoScroll = True, .Padding = New Padding(12, 10, 12, 10), .BackColor = Theme.CPanel}
    Private ReadOnly _tips As New ToolTip() With {.InitialDelay = 300, .AutoPopDelay = 20000}
    Private _cts As CancellationTokenSource
    Private _recipe As MediaRecipe

    ''' <summary>The user clicked a file in the list (to download that one).</summary>
    Public Event VariantPicked(r As MediaRecipe, label As String)

    <System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)>
    Public Property ShowNsfw As Boolean

    Public Sub New()
        BackColor = Theme.CPanel
        Controls.Add(flow)
        AddHandler flow.Resize, Sub() FitWidths()
        Clear()
    End Sub

    Private ReadOnly Property ContentWidth As Integer
        Get
            Return Math.Max(200, flow.ClientSize.Width - flow.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 4)
        End Get
    End Property

    Private Sub FitWidths()
        For Each c As Control In flow.Controls
            If TypeOf c Is FlowLayoutPanel OrElse TypeOf c Is ListView OrElse (TypeOf c Is Label AndAlso Not c.AutoSize) OrElse TypeOf c Is TextBox Then c.Width = ContentWidth
            If TypeOf c Is Label AndAlso c.AutoSize Then CType(c, Label).MaximumSize = New Size(ContentWidth, 0)
        Next
    End Sub

    Public Sub Clear()
        flow.Controls.Clear()
        flow.Controls.Add(Text_(Localizer.T("Click a model to see its details, settings and sample pictures."), dim_:=True))
    End Sub

    Private Function Text_(s As String, Optional bold As Boolean = False, Optional dim_ As Boolean = False, Optional size As Single = 0) As Label
        Dim l As New Label() With {.Text = s, .AutoSize = True, .MaximumSize = New Size(ContentWidth, 0), .Margin = New Padding(0, 2, 0, 4),
                                   .ForeColor = If(dim_, Theme.CDim, Theme.CText), .UseMnemonic = False,
                                   .Font = If(size > 0, New Font("Segoe UI Semibold", size), If(bold, Theme.BoldFont, Theme.UiFont))}
        Return l
    End Function

    Private Function Heading(s As String) As Label
        Dim l = Text_(s, bold:=True)
        l.Margin = New Padding(0, 12, 0, 4)
        l.ForeColor = Theme.CAccentHover
        Return l
    End Function

    Private Shared Function Inv(d As Double) As String
        Return d.ToString("0.##", CultureInfo.InvariantCulture)
    End Function

    ''' <summary>The page of a voice or music model (Models → Music &amp; voice).</summary>
    Public Sub ShowAudio(m As AudioModel)
        _cts?.Cancel()
        _recipe = Nothing
        flow.SuspendLayout()
        flow.Controls.Clear()
        flow.Controls.Add(Text_(m.Name, size:=13.0F))
        flow.Controls.Add(Text_(If(m.IsMusic, Localizer.T("🎵 Songs and music"), Localizer.T("🗣 Voice: read aloud, voice cloning")) &
                                "   ·   " & m.Model.Repo.Split("/"c)(0) & " · Hugging Face", dim_:=True))
        flow.Controls.Add(Text_(m.About))
        flow.Controls.Add(Heading(Localizer.T("What it can do")))
        If m.IsMusic Then
            flow.Controls.Add(Text_("•  " & Localizer.T("Makes songs with vocals from a description and your lyrics (🎵 Music in the main window).")))
        Else
            flow.Controls.Add(Text_("•  " & Localizer.T("Reads text aloud; used for chat answers, talking videos and voice-overs.")))
            If m.Clone Then flow.Controls.Add(Text_("•  " & Localizer.T("Copies a voice from a short recording (voice cloning).")))
            If m.Design Then flow.Controls.Add(Text_("•  " & Localizer.T("Makes a new voice from a description.")))
        End If
        If m.Langs IsNot Nothing AndAlso m.Langs.Count > 0 Then flow.Controls.Add(Text_("•  " & Localizer.T("Languages") & ": " & String.Join(", ", m.Langs)))
        flow.Controls.Add(Text_(AudioStudio.RequirementText(m, _hw), dim_:=True))
        flow.Controls.Add(Heading(Localizer.T("Files")))
        For Each f In {m.Model, m.Mmproj}
            flow.Controls.Add(Text_("•  " & IO.Path.GetFileName(f.Path) & "  ·  " & Downloader.FormatBytes(f.Size), dim_:=True))
        Next
        flow.ResumeLayout()
        FitWidths()
    End Sub

    Public Async Sub ShowRecipe(r As MediaRecipe, chosen As MediaVariant)
        _cts?.Cancel()
        _cts = New CancellationTokenSource()
        Dim ct = _cts.Token
        _recipe = r
        flow.SuspendLayout()
        flow.Controls.Clear()

        flow.Controls.Add(Text_(r.Name, size:=13.0F))
        Dim who = If(r.Repo, "")
        flow.Controls.Add(Text_(If(r.Auto, Localizer.T("Community model") & " · ", "") & If(who <> "", who.Split("/"c)(0) & " · Hugging Face", "") &
                                If(r.Uncensored, "   ·   🔓 " & Localizer.T("Uncensored / NSFW"), ""), dim_:=True))
        flow.Controls.Add(Text_(r.About))

        flow.Controls.Add(Heading(Localizer.T("Settings LocalMind uses")))
        For Each s In ModelDetails.SettingsLines(r)
            flow.Controls.Add(Text_("•  " & s))
        Next
        flow.Controls.Add(Text_(MediaStudio.RequirementText(r, chosen, _hw), dim_:=True))

        ' files to choose from
        flow.Controls.Add(Heading(Localizer.T("Files (click one to choose it)")))
        Dim lv As New ListView() With {.Height = Math.Min(200, 30 + 22 * r.Variants.Count), .Width = ContentWidth}
        Theme.StyleListView(lv)
        lv.Columns.Add(Localizer.T("File"), 130)
        lv.Columns.Add(Localizer.T("Size"), 80)
        lv.Columns.Add(Localizer.T("On this PC"), 170)
        For Each v In r.Variants
            Dim fit = MediaStudio.Fit(r, v, _hw)
            Dim it As New ListViewItem({If(v Is chosen, "★ ", "") & v.Label, Downloader.FormatBytes(v.Total), ModelCatalog.FitText(fit, _hw.VramBytes = 0)}) With {
                .Tag = v.Label, .ForeColor = ModelManagerForm.FitColor(fit)}
            lv.Items.Add(it)
        Next
        AddHandler lv.SelectedIndexChanged, Sub()
                                                If lv.SelectedItems.Count > 0 Then RaiseEvent VariantPicked(r, CStr(lv.SelectedItems(0).Tag))
                                            End Sub
        flow.Controls.Add(lv)
        Theme.FitListHeight(lv, 6)
        If Environment.GetEnvironmentVariable("LOCALMIND_MODELS_KIND") = "files" Then BeginInvoke(Sub() flow.ScrollControlIntoView(lv))   ' (screenshot checks)          ' measured: right at every display scale

        Dim lblStats = Text_(Localizer.T("Loading details…"), dim_:=True)
        flow.Controls.Add(Heading(Localizer.T("About")))
        flow.Controls.Add(lblStats)
        Dim pics As New FlowLayoutPanel() With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .WrapContents = True,
                                                .MaximumSize = New Size(ContentWidth, 0), .Width = ContentWidth, .Margin = New Padding(0, 4, 0, 4)}
        Dim picsHeading = Heading(Localizer.T("Sample pictures"))
        flow.Controls.Add(picsHeading)
        flow.Controls.Add(pics)
        Dim desc As New TextBox() With {.Multiline = True, .ReadOnly = True, .ScrollBars = ScrollBars.Vertical, .Height = 180, .Width = ContentWidth,
                                        .BackColor = Theme.CBack, .ForeColor = Theme.CText, .BorderStyle = BorderStyle.None, .Margin = New Padding(0, 4, 0, 4)}
        Dim descHeading = Heading(Localizer.T("Description"))
        flow.Controls.Add(descHeading)
        flow.Controls.Add(desc)
        Dim links As New FlowLayoutPanel() With {.AutoSize = True, .WrapContents = True, .Margin = New Padding(0, 6, 0, 0)}
        flow.Controls.Add(links)
        flow.ResumeLayout(True)
        FitWidths()

        Dim info As ModelDetails.Info
        Try
            info = Await ModelDetails.GetAsync(r, ShowNsfw, ct)
        Catch ex As OperationCanceledException
            Return
        Catch ex As Exception
            lblStats.Text = "⚠ " & ex.Message
            Return
        End Try
        If ct.IsCancellationRequested OrElse IsDisposed Then Return

        Dim stats As New List(Of String)
        stats.Add($"⬇ {r.Downloads:N0} {Localizer.T("downloads")}")
        If info.Likes > 0 Then stats.Add($"♥ {info.Likes:N0}")
        If info.Updated <> "" Then stats.Add($"{Localizer.T("updated")} {info.Updated}")
        If info.License <> "" Then stats.Add($"{Localizer.T("license")}: {info.License}")
        Dim statText = String.Join("   ·   ", stats)
        If info.BaseModel <> "" Then statText &= vbCrLf & $"{Localizer.T("Based on")}: {info.BaseModel}"
        If info.AuthorSettings <> "" Then statText &= vbCrLf & $"{Localizer.T("The author recommends")}: {info.AuthorSettings}"
        If info.CivitaiSteps <> "" Then statText &= vbCrLf & $"{Localizer.T("Creators' pictures use")}: {info.CivitaiSteps}"
        If info.TriggerWords.Count > 0 Then statText &= vbCrLf & $"{Localizer.T("Trigger words")}: {String.Join(", ", info.TriggerWords)}"
        If info.Tags.Count > 0 Then statText &= vbCrLf & String.Join(" · ", info.Tags.Take(8))
        lblStats.Text = statText
        lblStats.ForeColor = Theme.CText

        desc.Text = If(info.Description <> "", info.Description, Localizer.T("(No description from the author.)"))
        For Each link In {(info.PageUrl, "🔗 Hugging Face"), (info.CivitaiUrl, "🔗 Civitai")}
            If String.IsNullOrEmpty(link.Item1) Then Continue For
            Dim ll As New LinkLabel() With {.Text = link.Item2, .AutoSize = True, .LinkColor = Theme.CAccentHover, .Margin = New Padding(0, 0, 14, 0)}
            Dim u = link.Item1
            AddHandler ll.LinkClicked, Sub() ModelManagerForm.OpenUrl(u)
            links.Controls.Add(ll)
        Next

        If info.Samples.Count = 0 Then
            pics.Controls.Add(Text_(Localizer.T("(No sample pictures found.)"), dim_:=True))
            Return
        End If
        Dim size = Math.Max(110, (ContentWidth - 12) \ 2)
        For Each s In info.Samples.Take(12)
            Dim pb As New PictureBox() With {.Width = size, .Height = size, .SizeMode = PictureBoxSizeMode.Zoom, .BackColor = Theme.CBack,
                                             .Margin = New Padding(0, 0, 6, 6), .Cursor = Cursors.Hand}
            pics.Controls.Add(pb)
            Dim tip = String.Join(vbCrLf, {s.Settings, If(String.IsNullOrEmpty(s.Prompt), "", If(s.Prompt.Length > 300, s.Prompt.Substring(0, 300) & "…", s.Prompt))}.Where(Function(x) Not String.IsNullOrEmpty(x)))
            If tip <> "" Then _tips.SetToolTip(pb, tip)
            Dim sample = s
            Dim unused = LoadPictureAsync(pb, sample, ct)
        Next
    End Sub

    Private Async Function LoadPictureAsync(pb As PictureBox, s As ModelDetails.Sample, ct As CancellationToken) As Task
        Dim img As Image = Nothing
        Try
            img = Await ModelDetails.GetImageAsync(s.Url, ct)
        Catch
        End Try
        If ct.IsCancellationRequested OrElse pb.IsDisposed Then Return
        If img Is Nothing Then
            pb.Visible = False
            Return
        End If
        Dim hidden = s.Nsfw AndAlso Not ShowNsfw
        pb.Image = If(hidden, ModelDetails.Blur(img), img)
        If hidden Then
            _tips.SetToolTip(pb, Localizer.T("NSFW picture (blurred). Click to show it, or tick 🔓 Show NSFW at the top."))
            AddHandler pb.Paint, Sub(sender, e)
                                     If pb.Tag IsNot Nothing Then Return
                                     TextRenderer.DrawText(e.Graphics, "🔓 NSFW", Theme.BoldFont, pb.ClientRectangle, Color.White,
                                                           TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
                                 End Sub
        End If
        AddHandler pb.Click, Sub()
                                 If hidden AndAlso pb.Tag Is Nothing Then
                                     pb.Tag = "shown"
                                     pb.Image = img
                                     Return
                                 End If
                                 ShowLarge(img, s)
                             End Sub
    End Function

    Private Sub ShowLarge(img As Image, s As ModelDetails.Sample)
        Dim f As New Form() With {.Text = If(s.Settings, "Picture"), .StartPosition = FormStartPosition.CenterParent,
                                  .Size = New Size(Math.Min(1100, img.Width + 40), Math.Min(900, img.Height + 120))}
        Theme.ApplyForm(f)
        Dim pb As New PictureBox() With {.Dock = DockStyle.Fill, .SizeMode = PictureBoxSizeMode.Zoom, .Image = img, .BackColor = Theme.CBack}
        f.Controls.Add(pb)
        If Not String.IsNullOrEmpty(s.Prompt) Then
            Dim t As New TextBox() With {.Dock = DockStyle.Bottom, .Multiline = True, .ReadOnly = True, .Height = 70, .Text = s.Prompt,
                                         .BackColor = Theme.CBack, .ForeColor = Theme.CText, .BorderStyle = BorderStyle.None, .ScrollBars = ScrollBars.Vertical}
            f.Controls.Add(t)
        End If
        f.Show(FindForm())
    End Sub
End Class
