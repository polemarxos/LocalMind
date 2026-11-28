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

    ''' <summary>💾 Saves a copy of a result where the user chooses.</summary>
    Private Sub ExportOutput(name As String)
        Dim f = OutputFile(name)
        If f Is Nothing Then
            SetStatus("That file is no longer there (deleted or moved).")
            Return
        End If
        Dim ext = Path.GetExtension(f)
        Using d As New SaveFileDialog() With {.FileName = Path.GetFileName(f), .Filter = $"*{ext}|*{ext}|*.*|*.*",
                                              .InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)}
            If d.ShowDialog(Me) <> DialogResult.OK Then Return
            Try
                File.Copy(f, d.FileName, True)
                SetStatus("Saved to " & d.FileName)
            Catch ex As Exception
                Localizer.Box(Me, ex.Message, "Export", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            End Try
        End Using
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
End Class
