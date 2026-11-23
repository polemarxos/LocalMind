Imports System
Imports System.Drawing
Imports System.IO
Imports System.Linq
Imports System.Windows.Forms
Imports QRCoder

''' <summary>Turn LAN sharing on/off, show the address + QR code for phones.</summary>
Public Class LanForm
    Inherits Form

    Private ReadOnly chkOn As CheckBox
    Private ReadOnly numPort As NumericUpDown
    Private ReadOnly txtPin As TextBox
    Private ReadOnly chkPin As CheckBox
    Private ReadOnly chkApi As CheckBox
    Private ReadOnly lstUrls As ListBox
    Private ReadOnly picQr As PictureBox
    Private ReadOnly lblStatus As Label
    Private ReadOnly txtLog As TextBox
    Private _busy As Boolean

    Public Sub New()
        Text = "Share on local network"
        Size = New Size(860, 640)
        MinimumSize = New Size(780, 560)
        StartPosition = FormStartPosition.CenterParent
        Theme.ApplyForm(Me)

        Dim left As New TableLayoutPanel() With {.Dock = DockStyle.Fill, .ColumnCount = 2, .Padding = New Padding(14)}
        left.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 150))
        left.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))

        chkOn = New CheckBox() With {.Text = "Share this PC's AI on my network", .AutoSize = True, .Font = Theme.BoldFont,
                                     .Checked = AppCore.Lan IsNot Nothing AndAlso AppCore.Lan.IsRunning}
        numPort = New NumericUpDown() With {.Minimum = 1024, .Maximum = 65535, .Value = AppCore.Settings.LanPort, .Width = 100}
        txtPin = New TextBox() With {.Text = AppCore.Settings.LanPin, .Width = 120, .Font = New Font("Cascadia Code", 11.0!)}
        Dim btnNewPin = Theme.MakeButton("New PIN")
        chkPin = New CheckBox() With {.Text = "Require PIN (recommended)", .AutoSize = True, .Checked = AppCore.Settings.LanRequirePin}
        chkApi = New CheckBox() With {.Text = "OpenAI-compatible API at /v1 (for other apps)", .AutoSize = True, .Checked = AppCore.Settings.LanOpenAiApi}
        Dim chkAuto As New CheckBox() With {.Text = "Start sharing automatically when the app opens", .AutoSize = True, .Checked = AppCore.Settings.LanEnabled}
        Theme.StyleInput(numPort)
        Theme.StyleInput(txtPin)
        lblStatus = Theme.MakeLabel("", dim_:=True)
        lstUrls = New ListBox() With {.Height = 90, .Dock = DockStyle.Fill, .BackColor = Theme.CBack, .ForeColor = Color.FromArgb(78, 161, 255),
                                      .BorderStyle = BorderStyle.FixedSingle, .Font = New Font("Segoe UI", 11.0!)}
        Dim btnCopy = Theme.MakeButton("Copy address")
        Dim btnOpen = Theme.MakeButton("Open in browser")
        Dim btnFw = Theme.MakeButton("🛡 Allow through Windows Firewall")
        txtLog = New TextBox() With {.Multiline = True, .ReadOnly = True, .ScrollBars = ScrollBars.Vertical, .Dock = DockStyle.Fill,
                                     .BackColor = Theme.CBack, .ForeColor = Theme.CDim, .Font = Theme.SmallFont, .Height = 120}

        left.Controls.Add(chkOn) : left.SetColumnSpan(chkOn, 2)
        left.Controls.Add(Theme.MakeLabel("Port")) : left.Controls.Add(numPort)
        Dim pinRow As New FlowLayoutPanel() With {.AutoSize = True, .Margin = New Padding(0)}
        pinRow.Controls.AddRange({txtPin, btnNewPin, chkPin})
        left.Controls.Add(Theme.MakeLabel("PIN")) : left.Controls.Add(pinRow)
        left.Controls.Add(New Label()) : left.Controls.Add(chkApi)
        left.Controls.Add(New Label()) : left.Controls.Add(chkAuto)
        left.Controls.Add(Theme.MakeLabel("Open on phone/PC")) : left.Controls.Add(lstUrls)
        Dim urlRow As New FlowLayoutPanel() With {.AutoSize = True, .Margin = New Padding(0)}
        urlRow.Controls.AddRange({btnCopy, btnOpen, btnFw})
        left.Controls.Add(New Label()) : left.Controls.Add(urlRow)
        left.Controls.Add(New Label()) : left.Controls.Add(lblStatus)
        Dim help = Theme.MakeLabel(
            "• Phone and PC must be on the same Wi-Fi/network." & vbCrLf &
            "• If it doesn't load: press 'Allow through Windows Firewall' and make sure the" & vbCrLf &
            "  Windows network profile is 'Private' (Settings → Network → Properties)." & vbCrLf &
            "• Other apps: base URL  http://<address>:" & AppCore.Settings.LanPort & "/v1   API key = your PIN." & vbCrLf &
            "• Chats from other devices appear in this app's chat list too.", dim_:=True)
        left.Controls.Add(help) : left.SetColumnSpan(help, 2)
        left.Controls.Add(Theme.MakeLabel("Activity")) : left.Controls.Add(txtLog)
        For i = 0 To 9
            left.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        Next
        left.RowStyles.Add(New RowStyle(SizeType.Percent, 100))

        Dim right As New Panel() With {.Dock = DockStyle.Right, .Width = 300, .Padding = New Padding(14), .BackColor = Theme.CRaised}
        Dim lblQr = New Label() With {.Dock = DockStyle.Top, .Height = 40, .Text = "Scan with your phone camera" & vbCrLf & "(logs in automatically)",
                                      .ForeColor = Color.White, .TextAlign = ContentAlignment.MiddleCenter}
        picQr = New PictureBox() With {.Dock = DockStyle.Top, .Height = 272, .SizeMode = PictureBoxSizeMode.Zoom, .BackColor = Color.White}
        right.Controls.Add(picQr)
        right.Controls.Add(lblQr)

        Controls.Add(left)
        Controls.Add(right)

        ' Events
        AddHandler chkOn.CheckedChanged, Async Sub()
                                             If _busy Then Return
                                             Await ApplyAsync(chkOn.Checked)
                                         End Sub
        AddHandler btnNewPin.Click, Sub()
                                        txtPin.Text = Random.Shared.Next(100000, 999999).ToString()
                                    End Sub
        AddHandler txtPin.TextChanged, Sub()
                                           AppCore.Settings.LanPin = txtPin.Text.Trim()
                                           AppCore.SaveSettings()
                                           UpdateUrls()
                                       End Sub
        AddHandler chkPin.CheckedChanged, Sub()
                                              AppCore.Settings.LanRequirePin = chkPin.Checked
                                              AppCore.SaveSettings()
                                              UpdateUrls()
                                          End Sub
        AddHandler chkApi.CheckedChanged, Sub()
                                              AppCore.Settings.LanOpenAiApi = chkApi.Checked
                                              AppCore.SaveSettings()
                                          End Sub
        AddHandler chkAuto.CheckedChanged, Sub()
                                               AppCore.Settings.LanEnabled = chkAuto.Checked
                                               AppCore.SaveSettings()
                                           End Sub
        AddHandler lstUrls.SelectedIndexChanged, Sub() DrawQr()
        AddHandler btnCopy.Click, Sub()
                                      If lstUrls.SelectedItem IsNot Nothing Then
                                          Try
                                              Clipboard.SetText(lstUrls.SelectedItem.ToString())
                                          Catch
                                          End Try
                                      End If
                                  End Sub
        AddHandler btnOpen.Click, Sub() If lstUrls.SelectedItem IsNot Nothing Then ModelManagerForm.OpenUrl(lstUrls.SelectedItem.ToString())
        AddHandler btnFw.Click, Sub()
                                    If LanServer.AddFirewallRule(CInt(numPort.Value)) Then
                                        lblStatus.Text = "Firewall rule added for port " & numPort.Value
                                    Else
                                        lblStatus.Text = "Firewall rule was not added (cancelled or no admin rights)."
                                    End If
                                End Sub

        If AppCore.Lan IsNot Nothing Then AddHandler AppCore.Lan.Activity, AddressOf OnActivity
        UpdateUrls()
    End Sub

    Private Async Function ApplyAsync(turnOn As Boolean) As Threading.Tasks.Task
        _busy = True
        chkOn.Enabled = False
        Try
            If AppCore.Lan Is Nothing Then
                AppCore.Lan = New LanServer()
                AddHandler AppCore.Lan.Activity, AddressOf OnActivity
            End If
            If turnOn Then
                AppCore.Settings.LanPort = CInt(numPort.Value)
                AppCore.SaveSettings()
                Await AppCore.Lan.StartAsync(AppCore.Settings.LanPort)
                lblStatus.Text = "Sharing is ON. Anyone on your network with the PIN can chat."
                lblStatus.ForeColor = Theme.CGood
            Else
                Await AppCore.Lan.StopAsync()
                lblStatus.Text = "Sharing is off."
                lblStatus.ForeColor = Theme.CDim
            End If
        Catch ex As Exception
            lblStatus.Text = "Could not start: " & ex.Message
            lblStatus.ForeColor = Theme.CBad
            chkOn.Checked = False
        Finally
            chkOn.Enabled = True
            _busy = False
            UpdateUrls()
        End Try
    End Function

    Private Sub UpdateUrls()
        Dim sel = lstUrls.SelectedIndex
        lstUrls.Items.Clear()
        For Each u In LanServer.LocalUrls(CInt(numPort.Value))
            lstUrls.Items.Add(u)
        Next
        If lstUrls.Items.Count = 0 Then lstUrls.Items.Add("(no network connection found)")
        lstUrls.SelectedIndex = If(sel >= 0 AndAlso sel < lstUrls.Items.Count, sel, 0)
        If AppCore.Lan Is Nothing OrElse Not AppCore.Lan.IsRunning Then
            If lblStatus.Text = "" Then lblStatus.Text = "Sharing is off."
        End If
        DrawQr()
    End Sub

    Private Sub DrawQr()
        Dim url = lstUrls.SelectedItem?.ToString()
        If url Is Nothing OrElse Not url.StartsWith("http") Then
            picQr.Image = Nothing
            Return
        End If
        ' The PIN goes in the #fragment: it is read by the page and never sent in a request URL.
        If chkPin.Checked Then url &= "#pin=" & Uri.EscapeDataString(txtPin.Text.Trim())
        Try
            Using gen As New QRCodeGenerator()
                Using data = gen.CreateQrCode(url, QRCodeGenerator.ECCLevel.M)
                    Dim png = New PngByteQRCode(data).GetGraphic(10)
                    Dim old = picQr.Image
                    picQr.Image = Image.FromStream(New MemoryStream(png))
                    old?.Dispose()
                End Using
            End Using
        Catch
            picQr.Image = Nothing
        End Try
    End Sub

    Private Sub OnActivity(text As String)
        If IsDisposed OrElse Not IsHandleCreated Then Return
        Me.Post(Sub()
                        txtLog.AppendText(text & Environment.NewLine)
                    End Sub)
    End Sub

    Protected Overrides Sub OnShown(e As EventArgs)
        MyBase.OnShown(e)
        If AppCore.Lan IsNot Nothing AndAlso AppCore.Lan.IsRunning Then
            lblStatus.Text = "Sharing is ON (port " & AppCore.Lan.Port & ")."
            lblStatus.ForeColor = Theme.CGood
        End If
    End Sub

    Protected Overrides Sub OnFormClosed(e As FormClosedEventArgs)
        If AppCore.Lan IsNot Nothing Then RemoveHandler AppCore.Lan.Activity, AddressOf OnActivity
        MyBase.OnFormClosed(e)
    End Sub
End Class
