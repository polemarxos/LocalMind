Imports System
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms

' Keeps everything up to date by itself: LocalMind, the AI engines (llama.cpp, stable-diffusion.cpp,
' whisper.cpp, ffmpeg) and the list of models. Work waits until the AI is idle; a LocalMind update is
' installed in the background and used from the next start (or right away with "Restart to update").
Partial Class MainForm

    Private _btnRestartUpdate As ToolStripButton
    Private _updatedExe As String
    Private _autoUpdateStarted As Boolean

    ''' <summary>Starts the quiet update round shortly after every start.</summary>
    Private Sub AutoUpdateEverythingLater()
        If _autoUpdateStarted Then Return
        _autoUpdateStarted = True
        Dim unused = Task.Run(Async Function()
                                  Try
                                      Await Task.Delay(TimeSpan.FromSeconds(15))
                                      Await WaitUntilIdleAsync()
                                      Await UpdateEverythingAsync(manual:=False)
                                  Catch
                                  End Try
                              End Function)
    End Sub

    Private Async Function WaitUntilIdleAsync() As Task
        For i = 1 To 120
            If AppCore.Engine.ActiveRequests = 0 AndAlso Not _isGenerating AndAlso Not MediaStudio.IsBusy AndAlso Not AudioStudio.IsBusy Then Return
            Await Task.Delay(TimeSpan.FromSeconds(30))
        Next
    End Function

    ''' <summary>Updates the app, the engines and the model list. Manual = Help → Update everything now.</summary>
    Private Async Function UpdateEverythingAsync(manual As Boolean) As Task
        Dim report As New List(Of String)
        Dim s = Settings

        ' 1. LocalMind itself
        ' Checked at every start (and with Help → Update everything now).
        If manual OrElse s.CheckForUpdates Then
            If s.AutoInstallUpdates OrElse manual Then
                Dim r = Await InstallAppUpdateQuietlyAsync()
                If r IsNot Nothing Then report.Add(r)
            Else
                BeginInvoke(Async Sub() Await CheckUpdatesAsync(manual:=False))
            End If
        End If

        ' 2. AI engines (only the ones that are installed)
        If manual OrElse s.AutoUpdateEngines Then
            Dim status As Action(Of String) = Sub(t) SetStatus(t)
            Try
                If s.Backend = BackendKind.BuiltIn AndAlso AppCore.Engine.IsInstalled() Then
                    Dim before = AppCore.Engine.InstalledVersion(AppCore.Engine.EffectiveVariant)
                    Dim prog As New Progress(Of (Text As String, Percent As Integer))(Sub(p) SetStatus(p.Text))
                    Dim tag = Await AppCore.Engine.InstallAsync(prog, CancellationToken.None)
                    If tag <> before Then report.Add("llama.cpp " & tag)
                End If
            Catch ex As Exception
                report.Add("⚠ llama.cpp: " & ex.Message)
            End Try
            For Each u In {("stable-diffusion.cpp", CType(Function() If(s.EnableImages, MediaStudio.UpdateEngineAsync(status, CancellationToken.None), Task.CompletedTask), Func(Of Task))),
                           ("whisper.cpp", CType(Function() Voice.UpdateWhisperAsync(status, CancellationToken.None), Func(Of Task))),
                           ("ffmpeg", CType(Function() VideoTools.UpdateAsync(status, CancellationToken.None), Func(Of Task)))}
                Try
                    Await u.Item2()
                Catch ex As Exception
                    report.Add($"⚠ {u.Item1}: {ex.Message}")
                End Try
            Next
            s.LastEngineCheck = DateTime.Now
        End If

        ' 3. the list of chat models
        If manual OrElse ModelCatalog.CacheIsStale() Then
            Try
                Dim list = Await ModelCatalog.FetchLiveAsync(CancellationToken.None)
                If manual Then report.Add(Localizer.T("Model list") & $": {list.Count}")
            Catch
            End Try
        End If

        AppCore.SaveSettings()
        ' Don't overwrite the "LocalMind … is installed" message.
        If _updatedExe Is Nothing Then SetStatus(Localizer.T("Everything is up to date"))
        If manual Then
            BeginInvoke(Sub() Localizer.Box(Me, Localizer.T("Everything is up to date.") &
                                            If(report.Count > 0, vbCrLf & vbCrLf & String.Join(vbCrLf, report), ""),
                                            "Updates", MessageBoxButtons.OK, MessageBoxIcon.Information))
        End If
    End Function

    ''' <summary>Downloads and installs a newer LocalMind in place; it is used from the next start.</summary>
    Private Async Function InstallAppUpdateQuietlyAsync() As Task(Of String)
        If _updatedExe IsNot Nothing Then Return Nothing
        Dim info = Await Updater.CheckAsync(CancellationToken.None)
        Settings.LastUpdateCheck = DateTime.Now
        If info Is Nothing Then Return Nothing
        If Not AppPaths.IsPortable Then
            ' Installed copies (not portable) can't replace themselves: just tell the user.
            BeginInvoke(Sub() SetStatus($"LocalMind {info.Tag} is available — Help → Check for updates"))
            Return "LocalMind " & info.Tag
        End If
        Await WaitUntilIdleAsync()
        Dim exe = Await Updater.InstallAsync(info, Sub(p) SetStatus(Localizer.T("Downloading the LocalMind update…") & $" {p}%"), CancellationToken.None)
        _updatedExe = exe
        BeginInvoke(Sub() ShowRestartForUpdate(info.Tag))
        Return "LocalMind " & info.Tag
    End Function

    Private Sub ShowRestartForUpdate(tag As String)
        SetStatus(String.Format(Localizer.T("LocalMind {0} is installed — it starts the next time you open LocalMind."), tag))
        If _btnRestartUpdate Is Nothing Then
            _btnRestartUpdate = New ToolStripButton(Localizer.T("🔄 Restart to update")) With {
                .Tag = Theme.PrimaryTag, .Alignment = ToolStripItemAlignment.Right,
                .ToolTipText = String.Format(Localizer.T("Restart now to use LocalMind {0}. Your chats and models are kept."), tag)}
            AddHandler _btnRestartUpdate.Click, Sub()
                                                    If _isGenerating Then
                                                        Localizer.Box(Me, "Wait for the current answer to finish.", "Update")
                                                        Return
                                                    End If
                                                    Updater.Restart(_updatedExe)
                                                    _reallyExit = True
                                                    Close()
                                                End Sub
            toolTop.Items.Insert(0, _btnRestartUpdate)
            _btnRestartUpdate.Alignment = ToolStripItemAlignment.Right
        End If
        Try
            _tray?.ShowBalloonTip(5000, "LocalMind", String.Format(Localizer.T("LocalMind {0} is installed — it starts the next time you open LocalMind."), tag), ToolTipIcon.Info)
        Catch
        End Try
    End Sub
End Class
