Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms

' Buttons inside chat answers (https://action.localmind/…): merge an extended video with the one before it, export a
' file. And deleting a message.
Partial Class MainForm

    Private Const ActionHost As String = "action.localmind"

    ''' <summary>True when the link was one of LocalMind's own buttons (and it was handled).</summary>
    Private Function TryChatAction(link As String) As Boolean
        Dim u As Uri = Nothing
        If Not System.Uri.TryCreate(link, UriKind.Absolute, u) OrElse Not String.Equals(u.Host, ActionHost, StringComparison.OrdinalIgnoreCase) Then Return False
        Dim q = Web.HttpUtility.ParseQueryString(u.Query)
        Select Case u.AbsolutePath.Trim("/"c).ToLowerInvariant()
            Case "merge"
                Dim unused = MergeVideosAsync(q("a"), q("b"))
            Case "export"
                ExportOutput(q("f"))
            Case "download"
                DownloadOutput(q("f"), q("as"))
        End Select
        Return True
    End Function

    ''' <summary>A file in the outputs folder by its name (no folders, so a link can't point elsewhere).</summary>
    Private Shared Function OutputFile(name As String) As String
        If String.IsNullOrWhiteSpace(name) Then Return Nothing
        Dim f = Path.Combine(MediaStudio.OutputFolder, Path.GetFileName(name))
        Return If(File.Exists(f), f, Nothing)
    End Function

    Private _merging As Boolean

    ''' <summary>🔗 Joins a video and its continuation into one MP4 and adds it to the chat, with an Export button.</summary>
    Private Async Function MergeVideosAsync(a As String, b As String) As Task
        Dim fa = OutputFile(a), fb = OutputFile(b)
        If fa Is Nothing OrElse fb Is Nothing Then
            SetStatus("One of the videos is no longer there (deleted or moved).")
            Return
        End If
        If _merging OrElse _isGenerating Then
            SetStatus("Wait for the current work to finish, then merge.")
            Return
        End If
        _merging = True
        Try
            SetStatus("Merging the videos…")
            Dim output = Path.Combine(MediaStudio.OutputFolder, $"{DateTime.Now:yyyyMMdd-HHmmss}-merged.mp4")
            Dim ia = Await VideoTools.InfoAsync(fa, CancellationToken.None)
            Dim ib = Await VideoTools.InfoAsync(fb, CancellationToken.None)
            If ia.HasAudio AndAlso ib.HasAudio Then
                Await VideoTools.JoinWithSoundAsync({fa, fb}, output, CancellationToken.None)
            Else
                ' the continuation starts on the last frame of the first video: that frame is shown once
                Await VideoTools.JoinAsync({fa, fb}, output, True, Sub(t) SetStatus(t), CancellationToken.None)
            End If
            Dim sa = MediaStudio.LoadSettingsFor(fa), sb = MediaStudio.LoadSettingsFor(fb)
            MediaStudio.SaveInfo(output, "video", If(sa?.Model, ""), String.Join("  →  ", {sa?.Prompt, sb?.Prompt}.Where(Function(x) Not String.IsNullOrWhiteSpace(x))), Nothing)
            Dim info = Await VideoTools.InfoAsync(output, CancellationToken.None)
            Dim name = Uri.EscapeDataString(Path.GetFileName(output))
            Dim md = $"[🎬 {Localizer.T("Merged video")}](https://outputs.localmind/{name})" & vbCrLf & vbCrLf &
                     $"_{info.Seconds:0.#} s · {info.Width}×{info.Height}_" & vbCrLf & vbCrLf &
                     $"[💾 {Localizer.T("Export…")}](https://{ActionHost}/export?f={name})"
            _session.Messages.Add(New ChatMessage With {.Role = "assistant", .Content = md})
            _session.UpdatedAt = DateTime.Now
            AppCore.History.Save(_session)
            Await RebuildChatDisplayAsync()
            SetStatus("Videos merged")
        Catch ex As Exception
            Safety.Log(ex, "merge videos")
            SetStatus("⚠ " & ex.Message)
        Finally
            _merging = False
        End Try
    End Function

    ''' <summary>
    ''' ⬇ Saves a result straight into the Downloads folder: a picture or video as it is, a sound as MP3, MP4
    ''' (with a waveform) or WAV. A short note in the chat says where it went.
    ''' </summary>
    Private Async Sub DownloadOutput(name As String, asFormat As String)
        Dim f = OutputFile(name)
        If f Is Nothing Then
            Await Js($"toast({Newtonsoft.Json.JsonConvert.SerializeObject(Localizer.T("That file is no longer there (deleted or moved)."))}, true);")
            Return
        End If
        Dim failed As String = Nothing
        Try
            Dim ext = Path.GetExtension(f).ToLowerInvariant()
            Dim fmt = If(asFormat, "").Trim().ToLowerInvariant()
            Dim wanted = If(MediaExport.IsAudio(f) AndAlso (fmt = "mp3" OrElse fmt = "mp4" OrElse fmt = "wav"), "." & fmt, ext)
            Dim target = UniquePath(Path.Combine(DownloadsFolder(), Path.GetFileNameWithoutExtension(f) & wanted))
            If wanted = ext Then
                IO.File.Copy(f, target, False)
            Else
                Await Js($"toast({Newtonsoft.Json.JsonConvert.SerializeObject(If(wanted = ".mp4", Localizer.T("Making the MP4…"), Localizer.T("Making the MP3…")))});")
                Await MediaExport.ConvertAsync(f, target, Sub(t) SetStatus(t), CancellationToken.None)
            End If
            Await Js($"toast({Newtonsoft.Json.JsonConvert.SerializeObject("✔ " & Localizer.T("Saved to Downloads:") & " " & Path.GetFileName(target))});")
            Return
        Catch ex As Exception
            failed = ex.Message
        End Try
        Await Js($"toast({Newtonsoft.Json.JsonConvert.SerializeObject("⚠ " & failed)}, true);")
    End Sub

    ''' <summary>The user's Downloads folder (where Windows has it, even when moved to another drive).</summary>
    Private Shared Function DownloadsFolder() As String
        Try
            Dim p As IntPtr
            If SHGetKnownFolderPath(New Guid("374DE290-123F-4565-9164-39C4925E467B"), 0, IntPtr.Zero, p) = 0 Then
                Dim d = Runtime.InteropServices.Marshal.PtrToStringUni(p)
                Runtime.InteropServices.Marshal.FreeCoTaskMem(p)
                If Not String.IsNullOrEmpty(d) AndAlso Directory.Exists(d) Then Return d
            End If
        Catch
        End Try
        Dim fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
        Directory.CreateDirectory(fallback)
        Return fallback
    End Function

    <Runtime.InteropServices.DllImport("shell32.dll", CharSet:=Runtime.InteropServices.CharSet.Unicode)>
    Private Shared Function SHGetKnownFolderPath(<Runtime.InteropServices.MarshalAs(Runtime.InteropServices.UnmanagedType.LPStruct)> rfid As Guid,
                                                 flags As UInteger, token As IntPtr, ByRef path As IntPtr) As Integer
    End Function

    ''' <summary>"song.mp3", else "song (2).mp3", "song (3).mp3"…</summary>
    Private Shared Function UniquePath(p As String) As String
        If Not IO.File.Exists(p) Then Return p
        Dim d = Path.GetDirectoryName(p), n = Path.GetFileNameWithoutExtension(p), e = Path.GetExtension(p)
        For i = 2 To 999
            Dim c = Path.Combine(d, $"{n} ({i}){e}")
            If Not IO.File.Exists(c) Then Return c
        Next
        Return Path.Combine(d, $"{n} ({DateTime.Now:HHmmss}){e}")
    End Function

    ''' <summary>
    ''' 💾 An answer saved as PDF, Word, LibreOffice, Excel… <paramref name="displayIndex"/> counts the messages as shown
    ''' (yours and answers, in order). The file is named after the question.
    ''' </summary>
    Private Async Sub ExportAnswer(displayIndex As Integer)
        If _session Is Nothing OrElse displayIndex < 0 Then Return
        Dim n = -1, pos = -1
        For k = 0 To _session.Messages.Count - 1
            Dim role = _session.Messages(k).Role
            If role = "user" OrElse role = "assistant" Then
                n += 1
                If n = displayIndex Then pos = k : Exit For
            End If
        Next
        If pos < 0 OrElse _session.Messages(pos).Role <> "assistant" Then Return
        Dim question = _session.Messages.Take(pos).LastOrDefault(Function(m) m.Role = "user")?.Content
        Dim title = If(question, "").Split({vbCr, vbLf}, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim()
        If title IsNot Nothing AndAlso title.Length > 80 Then title = title.Substring(0, 80).Trim() & "…"
        Try
            Dim saved = Await PdfExport.SaveAnswerAsync(Me, _session.Messages(pos).Content, title, Sub(t) SetStatus(t))
            If saved IsNot Nothing Then Await Js($"toast({Newtonsoft.Json.JsonConvert.SerializeObject("✔ " & Localizer.T("Saved:") & " " & IO.Path.GetFileName(saved))});")
        Catch ex As Exception
            FailureLog.Write("(export an answer)", "", ex)
            Localizer.Box(Me, ex.Message, Localizer.T("Export"), MessageBoxButtons.OK, MessageBoxIcon.Warning)
        End Try
    End Sub

    ''' <summary>💾 Saves a copy of a result where the user chooses.</summary>
    Private Async Sub ExportOutput(name As String)
        Dim f = OutputFile(name)
        If f Is Nothing Then
            SetStatus("That file is no longer there (deleted or moved).")
            Return
        End If
        Try
            Await MediaExport.ExportAsync(Me, f, Sub(t) SetStatus(t))
        Catch ex As Exception
            Localizer.Box(Me, ex.Message, "Export", MessageBoxButtons.OK, MessageBoxIcon.Warning)
        End Try
    End Sub

    ''' <summary>
    ''' 🗑 Deletes a message: one of yours (with the answer to it) or one answer. <paramref name="displayIndex"/> counts
    ''' the messages as shown (yours and answers, in order).
    ''' </summary>
    Private Async Sub DeleteMessage(displayIndex As Integer)
        If _isGenerating Then
            SetStatus("Wait for the answer to finish (or press Stop), then delete.")
            Return
        End If
        If _session Is Nothing OrElse displayIndex < 0 Then Return
        Dim n = -1, pos = -1
        For k = 0 To _session.Messages.Count - 1
            Dim role = _session.Messages(k).Role
            If role = "user" OrElse role = "assistant" Then
                n += 1
                If n = displayIndex Then pos = k : Exit For
            End If
        Next
        If pos < 0 Then Return
        Dim isUser = _session.Messages(pos).Role = "user"
        If Localizer.Box(Me, If(isUser, "Delete this message and its answer?", "Delete this answer?"), "Delete",
                         MessageBoxButtons.YesNo, MessageBoxIcon.Question) <> DialogResult.Yes Then Return
        Dim upTo = pos + 1
        If isUser Then
            While upTo < _session.Messages.Count AndAlso _session.Messages(upTo).Role <> "user"
                upTo += 1
            End While
        End If
        _session.Messages.RemoveRange(pos, upTo - pos)
        _session.UpdatedAt = DateTime.Now
        AppCore.History.Save(_session)
        Await RebuildChatDisplayAsync()
        SetStatus("Message deleted")
    End Sub
    ''' <summary>
    ''' A newer recommended setup was published (new or better models for this kind of PC): offer it once. Only for
    ''' people who installed a setup before, and only when something in it is not installed yet.
    ''' </summary>
    Private Async Function OfferSetupUpdateAsync() As Task
        Try
            If String.IsNullOrEmpty(Settings.SetupTier) Then Return
            Await Task.Delay(8000)
            Dim s = Await SetupLibrary.LoadAsync(Settings.SetupTier, CancellationToken.None)
            If s Is Nothing OrElse s.Version <= Math.Max(Settings.SetupVersion, Settings.SetupOfferedVersion) Then Return
            If Not s.Items.Any(Function(i) Not SetupLibrary.IsInstalled(i)) Then Return
            Settings.SetupOfferedVersion = s.Version
            AppCore.SaveSettings()
            Me.Post(Sub()
                        Using f As New SetupForm(Localizer.T("New recommended models are available for your PC"))
                            f.ShowDialog(Me)
                        End Using
                        _modelsDirty = True
                    End Sub)
        Catch ex As Exception
            Safety.Log(ex, "setup update check")
        End Try
    End Function

    Private Sub OpenRecommended()
        Using f As New SetupForm()
            f.ShowDialog(Me)
        End Using
        _modelsDirty = True
    End Sub

    ' ---------- full backup / restore ----------

    ''' <summary>💾 Saves every setting (API keys included), profiles, personas, characters, voices and chats in one file.</summary>
    Private Async Sub BackupEverything()
        If Localizer.Box(Me, Localizer.T("The backup contains your API keys, PINs and chats. Keep it private and don't send it to anyone.") & vbCrLf & vbCrLf &
                             Localizer.T("Model and LoRA files are not copied. The backup keeps their download links, and they download again automatically when you restore it."),
                         "Back up everything", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) <> DialogResult.OK Then Return
        Using d As New SaveFileDialog() With {.FileName = "localmind-backup-" & DateTime.Now.ToString("yyyy-MM-dd") & BackupService.Extension,
                                              .Filter = Localizer.T("LocalMind backup") & "|*" & BackupService.Extension}
            If d.ShowDialog(Me) <> DialogResult.OK Then Return
            Try
                If _session IsNot Nothing AndAlso _session.Messages.Count > 0 Then AppCore.History.Save(_session)
                Cursor = Cursors.WaitCursor
                ' LoRAs added from a file get their Civitai link first, so they can be downloaded again
                SetStatus(Localizer.T("Finding the download links of your LoRAs…"))
                Try
                    Await Civitai.IdentifyLorasAsync(CancellationToken.None)
                Catch ex As Exception
                    Safety.Log(ex, "identify LoRAs")
                End Try
                Dim setup = SetupLibrary.ExportInstalled(Localizer.T("My models"), HardwareInfo.Current())
                Dim n = BackupService.Export(d.FileName, setup)
                Cursor = Cursors.Default
                Dim noLink = SetupLibrary.LorasWithoutLink()
                Localizer.Box(Me, String.Format(Localizer.T("Saved {0} files and the download links of {1} models and LoRAs to {2}."), n, setup.Items.Count, d.FileName) &
                                  If(noLink.Count = 0, "", vbCrLf & vbCrLf & Localizer.T("These LoRAs have no download link (not found on Civitai), so they can't be downloaded again; keep their files yourself:") & " " & String.Join(", ", noLink)),
                              "Back up everything")
            Catch ex As Exception
                Cursor = Cursors.Default
                Localizer.Box(Me, ex.Message, "Back up everything", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            End Try
        End Using
    End Sub

    ''' <summary>📂 Puts a backup back: LocalMind restarts and restores it before loading anything.</summary>
    Private Sub RestoreBackup()
        Using d As New OpenFileDialog() With {.Filter = Localizer.T("LocalMind backup") & "|*" & BackupService.Extension & ";*.zip"}
            If d.ShowDialog(Me) <> DialogResult.OK Then Return
            Try
                Dim m = BackupService.Inspect(d.FileName)
                Dim made = m.Value(Of String)("created")
                Dim when_ As DateTime
                Dim info = If(DateTime.TryParse(made, when_), when_.ToString("g"), made) & " (LocalMind " & m.Value(Of String)("version") & ")"
                If Localizer.Box(Me, String.Format(Localizer.T("Restore the backup from {0}?"), info) & vbCrLf & vbCrLf &
                                     Localizer.T("Your current settings, profiles, personas, characters and chats are replaced. A copy of them is kept in the folder ending in -before-restore.") & vbCrLf & vbCrLf &
                                     Localizer.T("LocalMind restarts to finish."),
                                 "Restore from backup", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) <> DialogResult.OK Then Return
                BackupService.ScheduleRestore(d.FileName)
                RestartApp()
            Catch ex As Exception
                Localizer.Box(Me, ex.Message, "Restore from backup", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            End Try
        End Using
    End Sub

    ''' <summary>After a restore: downloads the models and LoRAs in the backup that aren't on this PC. True when it ran.</summary>
    Private Function OfferRestoredModels() As Boolean
        If Environment.GetEnvironmentVariable("LOCALMIND_RESTORED") <> "1" Then Return False
        Environment.SetEnvironmentVariable("LOCALMIND_RESTORED", Nothing)
        Dim setup = BackupService.TakePendingSetup()
        Dim missing = If(setup?.Items, New List(Of SetupItem)()).Where(Function(it) Not SetupLibrary.IsInstalled(it)).ToList()
        Dim queued = SetupForm.QueueDownloads(missing, HardwareInfo.Current())
        _modelsDirty = True
        Dim msg = Localizer.T("The backup was restored.")
        If queued > 0 Then msg &= vbCrLf & vbCrLf & String.Format(Localizer.T("{0} models and LoRAs are downloading from their links. You can follow them in Get models → Downloads, and keep using LocalMind meanwhile."), queued)
        Localizer.Box(Me, msg, "Restore from backup")
        Return True
    End Function
End Class
