Imports System
Imports System.ComponentModel
Imports System.Drawing
Imports System.Runtime.InteropServices
Imports System.Windows.Forms

' Tray icon + global "quick ask" hotkey.
Partial Class MainForm

    Private _tray As NotifyIcon
    Private _trayLan As ToolStripMenuItem
    Private _reallyExit As Boolean
    Private _hotkeyRegistered As Boolean
    Private _quickAsk As QuickAskForm
    Private _trayTipShown As Boolean

    Private Const WM_HOTKEY As Integer = &H312
    Private Const HOTKEY_ID As Integer = &H4C4D   ' "LM"

    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function RegisterHotKey(hWnd As IntPtr, id As Integer, fsModifiers As UInteger, vk As UInteger) As Boolean
    End Function

    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function UnregisterHotKey(hWnd As IntPtr, id As Integer) As Boolean
    End Function

    Private Sub SetupTray()
        Dim menu As New ContextMenuStrip()
        Dim miOpen As New ToolStripMenuItem("Open LocalMind") With {.Font = New Font(menu.Font, FontStyle.Bold)}
        Dim miAsk As New ToolStripMenuItem("Quick ask…" & If(String.IsNullOrEmpty(Settings.QuickAskHotkey), "", "   " & Settings.QuickAskHotkey))
        _trayLan = New ToolStripMenuItem("Share on network")
        Dim miExit As New ToolStripMenuItem("Exit")
        AddHandler miOpen.Click, Sub() RestoreFromTray()
        AddHandler miAsk.Click, Sub() ShowQuickAsk()
        AddHandler _trayLan.Click, Sub() miLan_Click(Nothing, EventArgs.Empty)
        AddHandler miExit.Click, Sub()
                                     _reallyExit = True
                                     RestoreFromTray()
                                     Close()
                                 End Sub
        menu.Items.AddRange({miOpen, miAsk, _trayLan, New ToolStripSeparator(), miExit})
        AddHandler menu.Opening, Sub() _trayLan.Checked = AppCore.Lan IsNot Nothing AndAlso AppCore.Lan.IsRunning

        _tray = New NotifyIcon() With {.Icon = Theme.AppIcon(SystemInformation.SmallIconSize.Width), .Text = "LocalMind", .ContextMenuStrip = menu, .Visible = True}
        AddHandler _tray.MouseClick, Sub(s, e) If e.Button = MouseButtons.Left Then RestoreFromTray()
        Notifier.Balloon = Sub(t, x) BeginInvoke(Sub() _tray?.ShowBalloonTip(6000, t, x, ToolTipIcon.Info))
        RegisterQuickAskHotkey()
    End Sub

    ''' <summary>Registers e.g. "Ctrl+Alt+Space" as a system-wide hotkey.</summary>
    Friend Sub RegisterQuickAskHotkey()
        If _hotkeyRegistered Then
            UnregisterHotKey(Handle, HOTKEY_ID)
            _hotkeyRegistered = False
        End If
        Dim text = Settings.QuickAskHotkey
        If String.IsNullOrWhiteSpace(text) Then Return
        Try
            Dim keys = CType(New KeysConverter().ConvertFromInvariantString(text.Replace("Ctrl", "Control")), Keys)
            Dim mods As UInteger = &H4000UI   ' MOD_NOREPEAT
            If (keys And Keys.Control) = Keys.Control Then mods = mods Or 2UI
            If (keys And Keys.Alt) = Keys.Alt Then mods = mods Or 1UI
            If (keys And Keys.Shift) = Keys.Shift Then mods = mods Or 4UI
            Dim vk = CUInt(keys And Keys.KeyCode)
            _hotkeyRegistered = RegisterHotKey(Handle, HOTKEY_ID, mods, vk)
            If Not _hotkeyRegistered Then SetStatus($"Hotkey {text} is used by another app — change it in Settings.")
        Catch
            SetStatus($"Invalid hotkey '{text}' — change it in Settings.")
        End Try
    End Sub

    Protected Overrides Sub WndProc(ByRef m As Message)
        If m.Msg = WM_HOTKEY AndAlso m.WParam.ToInt32() = HOTKEY_ID Then
            ShowQuickAsk()
            Return
        End If
        MyBase.WndProc(m)
    End Sub

    Private Sub ShowQuickAsk()
        If _quickAsk IsNot Nothing AndAlso Not _quickAsk.IsDisposed Then
            _quickAsk.Activate()
            Return
        End If
        _quickAsk = New QuickAskForm()
        AddHandler _quickAsk.OpenInMain, Async Sub(id)
                                             RestoreFromTray()
                                             Dim s = AppCore.History.Load(id)
                                             If s IsNot Nothing AndAlso Not _isGenerating Then
                                                 _session = s
                                                 RefreshChatList()
                                                 Await RebuildChatDisplayAsync()
                                             End If
                                         End Sub
        _quickAsk.Show()
        _quickAsk.Activate()
    End Sub

    Private Sub RestoreFromTray()
        Show()
        If WindowState = FormWindowState.Minimized Then WindowState = FormWindowState.Normal
        Activate()
    End Sub

    Protected Overrides Sub OnResize(e As EventArgs)
        MyBase.OnResize(e)
        If WindowState = FormWindowState.Minimized AndAlso Settings IsNot Nothing AndAlso Settings.MinimizeToTray AndAlso _tray IsNot Nothing Then
            Hide()
            ShowTrayTipOnce()
        End If
    End Sub

    ''' <summary>Returns True when the close was turned into "hide to tray".</summary>
    Private Function HandleCloseToTray(e As FormClosingEventArgs) As Boolean
        If _reallyExit OrElse e.CloseReason <> CloseReason.UserClosing OrElse Not Settings.CloseToTray OrElse _tray Is Nothing Then Return False
        e.Cancel = True
        Hide()
        ShowTrayTipOnce()
        Return True
    End Function

    Private Sub ShowTrayTipOnce()
        If _trayTipShown Then Return
        _trayTipShown = True
        Try
            _tray.ShowBalloonTip(3000, "LocalMind is still running",
                                 "Click the tray icon to open it" & If(_hotkeyRegistered, $", or press {Settings.QuickAskHotkey} to ask something.", "."),
                                 ToolTipIcon.Info)
        Catch
        End Try
    End Sub

    Private Sub DisposeTray()
        Try
            If _hotkeyRegistered Then UnregisterHotKey(Handle, HOTKEY_ID)
            If _tray IsNot Nothing Then
                _tray.Visible = False
                _tray.Dispose()
            End If
        Catch
        End Try
    End Sub
End Class
