Imports System
Imports System.Drawing
Imports System.Net.Http
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports Newtonsoft.Json.Linq

''' <summary>
''' The Civitai API key in one place: what it is for, how to get one (step by step, with a button to the right page),
''' a box to paste it, a check that the key works, and Save / Remove.
''' </summary>
Public Class CivitaiKeyForm
    Inherits Form

    Private ReadOnly txtKey As TextBox
    Private ReadOnly btnShow, btnPaste, btnTest, btnRemove, btnSave As Button
    Private ReadOnly lblState, lblCheck As Label

    ''' <summary>Opens the dialog. Returns True when the saved key changed.</summary>
    Public Shared Function Edit(owner As IWin32Window, Optional reason As String = Nothing) As Boolean
        Dim before = If(AppCore.Settings.CivitaiApiKey, "")
        Using f As New CivitaiKeyForm(reason)
            f.ShowDialog(owner)
        End Using
        Return If(AppCore.Settings.CivitaiApiKey, "") <> before
    End Function

    ''' <summary>Text for a button that opens this dialog (shows whether a key is saved).</summary>
    Public Shared Function ButtonText() As String
        Return If(String.IsNullOrWhiteSpace(AppCore.Settings.CivitaiApiKey), Localizer.T("🔑 Civitai API key"), Localizer.T("🔑 Civitai API key ✔"))
    End Function

    Public Sub New(Optional reason As String = Nothing)
        Text = Localizer.T("Civitai API key")
        FormBorderStyle = FormBorderStyle.FixedDialog
        MinimizeBox = False
        MaximizeBox = False
        ShowInTaskbar = False
        StartPosition = FormStartPosition.CenterParent
        ClientSize = New Size(640, 560)
        Theme.ApplyForm(Me)
        BackColor = Theme.CPanel
        KeyPreview = True

        Dim body As New Panel() With {.Dock = DockStyle.Fill, .Padding = New Padding(26, 20, 26, 10), .BackColor = Theme.CPanel}
        Dim title As New Label() With {.Text = Localizer.T("Civitai API key"), .AutoSize = True, .ForeColor = Theme.CText,
                                       .Font = New Font(Theme.UiFont.FontFamily, 15.0F, FontStyle.Bold), .Location = New Point(24, 18)}
        Dim intro As New Label() With {.AutoSize = False, .Location = New Point(26, 56), .Size = New Size(588, 44), .ForeColor = Theme.CDim, .UseMnemonic = False,
                                       .Text = If(reason, Localizer.T("Some models on Civitai can only be downloaded with a free Civitai account (many NSFW and early-access models). Your API key lets LocalMind download them for you."))}
        If reason IsNot Nothing Then intro.ForeColor = Theme.CWarn

        ' how to get one
        Dim steps As New Panel() With {.Location = New Point(26, 108), .Size = New Size(588, 218), .BackColor = Theme.CRaised, .Padding = New Padding(16, 12, 16, 12)}
        Dim stepsTitle As New Label() With {.Text = Localizer.T("How to get your key (free, about a minute)"), .AutoSize = True, .Location = New Point(16, 12),
                                            .Font = Theme.BoldFont, .ForeColor = Theme.CText}
        steps.Controls.Add(stepsTitle)
        Dim lines = {
            Localizer.T("Sign in (or sign up) at civitai.com."),
            Localizer.T("Open Account settings (button below, or profile picture → Settings)."),
            Localizer.T("Scroll down to ""Security & apps"" and click ""Add API Key""."),
            Localizer.T("Give it a name (e.g. LocalMind), save, and copy the key. It is shown only once."),
            Localizer.T("Paste it below and click Save.")}
        For i = 0 To lines.Length - 1
            Dim num As New Label() With {.Text = (i + 1).ToString(), .Size = New Size(22, 22), .Location = New Point(16, 42 + i * 26),
                                         .TextAlign = ContentAlignment.MiddleCenter, .BackColor = Theme.CAccent, .ForeColor = Color.White, .Font = Theme.SmallFont}
            Dim txt As New Label() With {.Text = lines(i), .AutoSize = False, .Size = New Size(526, 22), .Location = New Point(46, 43 + i * 26),
                                         .ForeColor = Theme.CText, .UseMnemonic = False}
            steps.Controls.AddRange({num, txt})
        Next
        Dim btnOpen = Theme.MakeButton(Localizer.T("🌐 Open Civitai account settings"))
        btnOpen.Location = New Point(16, 42 + lines.Length * 26 + 4)
        btnOpen.AutoSize = True
        AddHandler btnOpen.Click, Sub() ModelManagerForm.OpenUrl("https://civitai.com/user/account")
        steps.Controls.Add(btnOpen)

        ' the key
        Dim lblKey As New Label() With {.Text = Localizer.T("Your API key"), .AutoSize = True, .Location = New Point(26, 340), .Font = Theme.BoldFont, .ForeColor = Theme.CText}
        txtKey = New TextBox() With {.Location = New Point(26, 364), .Width = 380, .UseSystemPasswordChar = True, .Text = If(AppCore.Settings.CivitaiApiKey, ""),
                                     .PlaceholderText = Localizer.T("Paste your key here")}
        Theme.StyleInput(txtKey)
        btnShow = Theme.MakeButton("👁")
        btnShow.Location = New Point(412, 361)
        btnShow.Width = 44
        btnPaste = Theme.MakeButton(Localizer.T("📋 Paste"))
        btnPaste.Location = New Point(462, 361)
        btnPaste.Width = 152
        lblState = New Label() With {.AutoSize = False, .Location = New Point(26, 400), .Size = New Size(588, 22), .ForeColor = Theme.CDim, .UseMnemonic = False}
        lblCheck = New Label() With {.AutoSize = False, .Location = New Point(26, 422), .Size = New Size(588, 44), .ForeColor = Theme.CDim, .UseMnemonic = False}
        Dim privacy As New Label() With {.AutoSize = False, .Location = New Point(26, 466), .Size = New Size(588, 36), .ForeColor = Theme.CDim, .Font = Theme.SmallFont,
                                         .Text = Localizer.T("The key is kept only on this PC and sent only to Civitai. It works for civitai.red too. Keep it private; you can delete it on Civitai at any time.")}
        body.Controls.AddRange({title, intro, steps, lblKey, txtKey, btnShow, btnPaste, lblState, lblCheck, privacy})

        ' buttons
        Dim bar As New FlowLayoutPanel() With {.Dock = DockStyle.Bottom, .Height = 56, .FlowDirection = FlowDirection.RightToLeft, .Padding = New Padding(20, 10, 20, 10),
                                               .BackColor = Theme.CBack}
        btnSave = Theme.MakeButton(Localizer.T("Save"), primary:=True, width:=110)
        Dim btnCancel = Theme.MakeButton(Localizer.T("Cancel"), width:=100)
        btnTest = Theme.MakeButton(Localizer.T("✓ Test key"), width:=120)
        btnRemove = Theme.MakeButton(Localizer.T("🗑 Remove key"), width:=140)
        bar.Controls.AddRange({btnSave, btnCancel, btnTest, btnRemove})
        Controls.Add(body)
        Controls.Add(bar)
        AcceptButton = btnSave
        CancelButton = btnCancel

        AddHandler btnShow.Click, Sub()
                                      txtKey.UseSystemPasswordChar = Not txtKey.UseSystemPasswordChar
                                      btnShow.Text = If(txtKey.UseSystemPasswordChar, "👁", "🙈")
                                  End Sub
        AddHandler btnPaste.Click, Sub()
                                       Try
                                           If Clipboard.ContainsText() Then txtKey.Text = Clipboard.GetText().Trim()
                                       Catch
                                       End Try
                                   End Sub
        AddHandler txtKey.TextChanged, Sub() UpdateState()
        AddHandler btnCancel.Click, Sub() Close()
        AddHandler btnRemove.Click, Sub()
                                        AppCore.Settings.CivitaiApiKey = ""
                                        AppCore.SaveSettings()
                                        txtKey.Text = ""
                                        lblCheck.ForeColor = Theme.CDim
                                        lblCheck.Text = Localizer.T("The key was removed from this PC.")
                                        UpdateState()
                                    End Sub
        AddHandler btnTest.Click, Async Sub() Await TestAsync()
        AddHandler btnSave.Click, Async Sub()
                                      Dim k = txtKey.Text.Trim()
                                      If k <> "" Then
                                          Dim ok = Await TestAsync()
                                          If ok = False Then
                                              If Localizer.Box(Me, "Civitai did not accept this key. Save it anyway?", "Civitai API key",
                                                               MessageBoxButtons.YesNo, MessageBoxIcon.Warning) <> DialogResult.Yes Then Return
                                          End If
                                      End If
                                      AppCore.Settings.CivitaiApiKey = k
                                      AppCore.SaveSettings()
                                      DialogResult = DialogResult.OK
                                      Close()
                                  End Sub
        UpdateState()
    End Sub

    Private Sub UpdateState()
        Dim saved = If(AppCore.Settings.CivitaiApiKey, "")
        If saved = "" Then
            lblState.Text = Localizer.T("No key saved yet.")
            lblState.ForeColor = Theme.CDim
        Else
            lblState.Text = Localizer.T("✔ A key is saved") & $" (…{If(saved.Length > 4, saved.Substring(saved.Length - 4), saved)})" &
                            If(txtKey.Text.Trim() <> saved AndAlso txtKey.Text.Trim() <> "", "  ·  " & Localizer.T("not saved: the key above is new"), "")
            lblState.ForeColor = Theme.CGood
        End If
        btnRemove.Enabled = saved <> ""
        btnTest.Enabled = txtKey.Text.Trim() <> ""
    End Sub

    ''' <summary>Asks Civitai who the key belongs to. True = works, False = refused, Nothing = could not check.</summary>
    Private Async Function TestAsync() As Task(Of Boolean?)
        Dim k = txtKey.Text.Trim()
        If k = "" Then Return Nothing
        btnTest.Enabled = False
        lblCheck.ForeColor = Theme.CDim
        lblCheck.Text = Localizer.T("Checking the key with Civitai…")
        Try
            Using req As New HttpRequestMessage(HttpMethod.Get, "https://civitai.com/api/v1/me")
                req.Headers.Authorization = New Headers.AuthenticationHeaderValue("Bearer", k)
                req.Headers.UserAgent.ParseAdd("LocalMind/" & Updater.CurrentVersion.ToString())
                Using cts As New CancellationTokenSource(TimeSpan.FromSeconds(20))
                    Using resp = Await Downloader.Http.SendAsync(req, cts.Token)
                        If resp.StatusCode = Net.HttpStatusCode.Unauthorized OrElse resp.StatusCode = Net.HttpStatusCode.Forbidden Then
                            lblCheck.ForeColor = Theme.CBad
                            lblCheck.Text = Localizer.T("✖ Civitai did not accept this key. Copy it again (the whole key) or create a new one.")
                            Return False
                        End If
                        If resp.IsSuccessStatusCode Then
                            Dim who = ""
                            Try
                                Dim j = JObject.Parse(Await resp.Content.ReadAsStringAsync())
                                who = If(j.Value(Of String)("username"), j.Value(Of String)("name"))
                            Catch
                            End Try
                            lblCheck.ForeColor = Theme.CGood
                            lblCheck.Text = Localizer.T("✔ The key works") & If(String.IsNullOrEmpty(who), ".", " — " & Localizer.T("signed in as") & " " & who & ".")
                            Return True
                        End If
                        lblCheck.Text = Localizer.T("Could not check the key right now (Civitai answered") & $" {CInt(resp.StatusCode)}). " & Localizer.T("You can still save it.")
                        Return Nothing
                    End Using
                End Using
            End Using
        Catch ex As Exception
            lblCheck.Text = Localizer.T("Could not reach Civitai to check the key. You can still save it.")
            Return Nothing
        Finally
            btnTest.Enabled = txtKey.Text.Trim() <> ""
        End Try
    End Function
End Class
