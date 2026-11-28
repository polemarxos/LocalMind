Imports System
Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.IO
Imports System.Linq
Imports System.Runtime.InteropServices
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Threading
Imports System.Threading.Tasks

''' <summary>
''' Voice input (microphone → whisper.cpp, fully offline) and voice output (Windows' built-in
''' SAPI voices, including Greek when installed).
''' </summary>
Public Module Voice

    '════════════════ Recording (winmm MCI: no extra libraries) ════════════════
    <DllImport("winmm.dll", CharSet:=CharSet.Unicode)>
    Private Function mciSendString(command As String, buffer As StringBuilder, bufferSize As Integer, hwndCallback As IntPtr) As Integer
    End Function

    Private _recording As Boolean
    Public ReadOnly Property IsRecording As Boolean
        Get
            Return _recording
        End Get
    End Property

    Private Sub Mci(cmd As String)
        Dim err = mciSendString(cmd, Nothing, 0, IntPtr.Zero)
        If err <> 0 Then Throw New Exception("Microphone error " & err & " (" & cmd.Split(" "c)(0) & "). Check that a microphone is connected and allowed in Windows privacy settings.")
    End Sub

    Public Sub StartRecording()
        If _recording Then Return
        Try : mciSendString("close lmrec", Nothing, 0, IntPtr.Zero) : Catch : End Try
        Mci("open new type waveaudio alias lmrec")
        ' 16 kHz mono 16-bit = what Whisper expects.
        Mci("set lmrec time format ms bitspersample 16 channels 1 samplespersec 16000 bytespersec 32000 alignment 2")
        Mci("record lmrec")
        _recording = True
    End Sub

    ''' <summary>Stops recording and returns the WAV file path.</summary>
    Public Function StopRecording() As String
        If Not _recording Then Return Nothing
        _recording = False
        Dim wav = Path.Combine(Path.GetTempPath(), $"localmind-voice-{DateTime.Now:HHmmss}.wav")
        Try
            Mci("stop lmrec")
            Mci($"save lmrec ""{wav}""")
        Finally
            mciSendString("close lmrec", Nothing, 0, IntPtr.Zero)
        End Try
        Return wav
    End Function

    '════════════════ Whisper ════════════════
    Private Function WhisperPackage(flavor As String) As ExternalTools.ToolPackage
        If flavor = "cuda13" Then
            ' whisper.cpp has no Windows x64 CUDA 13 build: LocalMind builds its own (engines-cuda13 release)
            Return New ExternalTools.ToolPackage With {.Name = "whisper", .Flavor = "cuda13", .Repo = Updater.Repo, .ExeName = "whisper-cli.exe",
                                                       .AssetPatterns = {"^whisper-.+-bin-win-cuda13-x64\.zip$"}}
        End If
        Return New ExternalTools.ToolPackage With {
            .Name = "whisper", .Flavor = flavor, .Repo = "ggml-org/whisper.cpp", .ExeName = "whisper-cli.exe",
            .AssetPatterns = If(flavor = "cuda12",
                                {"^whisper-(cublas-12[\d.]*-bin-x64|bin-win-cuda-12[\d.]*-x64)\.zip$"},
                                {"^whisper-(blas-bin-x64|bin-x64)\.zip$"})}
    End Function

    ''' <summary>Startup check: installed whisper.cpp builds still have their program (installed again if not).</summary>
    Public Async Function RepairWhisperAsync(progress As Action(Of String), ct As CancellationToken) As Task(Of Collections.Generic.List(Of String))
        Dim result As New Collections.Generic.List(Of String)()
        If Not OperatingSystem.IsWindows() Then Return result
        For Each flavor In {"cuda13", "cuda12", "cpu"}
            Dim r = Await ExternalTools.RepairIfBrokenAsync(WhisperPackage(flavor), progress, ct)
            If r IsNot Nothing Then result.Add(r)
        Next
        Return result
    End Function

    ''' <summary>Updates whisper.cpp builds that are installed, when a newer one exists.</summary>
    Public Async Function UpdateWhisperAsync(progress As Action(Of String), ct As CancellationToken) As Task
        If Not OperatingSystem.IsWindows() Then Return
        For Each flavor In {"cuda13", "cuda12", "cpu"}
            Dim p = WhisperPackage(flavor)
            If ExternalTools.FindExe(p) IsNot Nothing Then Await ExternalTools.InstallAsync(p, progress, ct, onlyIfNewer:=True)
        Next
    End Function

    Private Function PreferredFlavor() As String
        If HardwareInfo.Current().PrimaryVendor <> GpuVendor.Nvidia Then Return "cpu"
        Return If(AppCore.Engine.EffectiveVariant = "cuda13", "cuda13", "cuda12")
    End Function

    ''' <summary>Whisper model by hardware: large-v3-turbo on NVIDIA, small on other PCs.</summary>
    Public Function ModelFile() As String
        Dim chosen = If(AppCore.Settings.VoiceModel, "auto")
        If chosen = "auto" Then
            Dim hw = HardwareInfo.Current()
            chosen = If(hw.PrimaryVendor = GpuVendor.Nvidia AndAlso hw.VramGB >= 4, "large-v3-turbo-q5_0", "small-q5_1")
        End If
        Return "ggml-" & chosen & ".bin"
    End Function

    ''' <summary>Installs whisper.cpp and the speech model on first use; returns the package to run and the model.</summary>
    Private Async Function WhisperReadyAsync(progress As Action(Of String), ct As CancellationToken) As Task(Of (Pkg As ExternalTools.ToolPackage, Model As String))
        Dim flavor = PreferredFlavor()
        Dim pkg = WhisperPackage(flavor)
        If ExternalTools.FindExe(pkg) Is Nothing Then
            Dim gpuInstallFailed = False
            Try
                Await ExternalTools.InstallAsync(pkg, progress, ct)
            Catch ex As Exception When flavor <> "cpu"
                gpuInstallFailed = True
            End Try
            If gpuInstallFailed AndAlso flavor = "cuda13" Then
                ' CUDA 13 build not published yet: the CUDA 12 build
                gpuInstallFailed = False
                flavor = "cuda12"
                pkg = WhisperPackage(flavor)
                Try
                    If ExternalTools.FindExe(pkg) Is Nothing Then Await ExternalTools.InstallAsync(pkg, progress, ct)
                Catch ex As Exception
                    gpuInstallFailed = True
                End Try
            End If
            If gpuInstallFailed Then
                pkg = WhisperPackage("cpu")
                If ExternalTools.FindExe(pkg) Is Nothing Then Await ExternalTools.InstallAsync(pkg, progress, ct)
            End If
        End If
        If pkg.Flavor = "cuda12" Then ExternalTools.ShareCudaRuntime(ExternalTools.ToolDir(pkg))
        Dim model = Path.Combine(AppCore.Library.Folder, "Voice", ModelFile())
        If Not File.Exists(model) Then
            Await Downloader.DownloadFileAsync($"https://huggingface.co/ggerganov/whisper.cpp/resolve/main/{Path.GetFileName(model)}", model, Nothing,
                Sub(d, t, s) progress?.Invoke($"Downloading speech model… {If(t > 0, d * 100 \ t, 0)}%"), ct)
        End If
        Return (pkg, model)
    End Function

    ''' <summary>Transcribes a WAV file. Installs whisper.cpp and the model on first use.</summary>
    Public Async Function TranscribeAsync(wav As String, progress As Action(Of String), ct As CancellationToken) As Task(Of String)
        Dim ready = Await WhisperReadyAsync(progress, ct)
        Dim pkg = ready.Pkg, model = ready.Model
        progress?.Invoke("Transcribing…")
        Dim text = Await RunWhisperAsync(ExternalTools.FindExe(pkg), model, wav, ct)
        If text Is Nothing AndAlso pkg.Flavor <> "cpu" Then
            ' CUDA build could not start (driver/DLLs) → fall back to the CPU build once.
            Dim cpu = WhisperPackage("cpu")
            If ExternalTools.FindExe(cpu) Is Nothing Then Await ExternalTools.InstallAsync(cpu, progress, ct)
            text = Await RunWhisperAsync(ExternalTools.FindExe(cpu), model, wav, ct)
        End If
        If text Is Nothing Then Throw New Exception("Speech recognition failed.")
        Return text
    End Function

    ''' <summary>
    ''' Subtitles with timings (SRT) from a 16 kHz WAV. <paramref name="language"/> = spoken language ("auto" to
    ''' detect); <paramref name="toEnglish"/> makes Whisper write them in English. Returns the SRT file.
    ''' </summary>
    Public Async Function TranscribeSrtAsync(wav As String, language As String, toEnglish As Boolean, progress As Action(Of String), ct As CancellationToken) As Task(Of String)
        Dim ready = Await WhisperReadyAsync(progress, ct)
        progress?.Invoke("Writing subtitles…")
        For Each pkg In {ready.Pkg, WhisperPackage("cpu")}.Distinct()
            Dim exe = ExternalTools.FindExe(pkg)
            If exe Is Nothing Then
                Await ExternalTools.InstallAsync(pkg, progress, ct)
                exe = ExternalTools.FindExe(pkg)
            End If
            If exe Is Nothing Then Continue For
            Dim outBase = Path.Combine(Path.GetTempPath(), "localmind-srt-" & Guid.NewGuid().ToString("N"))
            Dim lang = If(String.IsNullOrWhiteSpace(language), "auto", language)
            Dim psi As New ProcessStartInfo(exe, $"-m ""{ready.Model}"" -f ""{wav}"" -l {lang}{If(toEnglish, " -tr", "")} -t {Math.Max(2, Environment.ProcessorCount - 2)} -np -osrt -of ""{outBase}""") With {
                .UseShellExecute = False, .CreateNoWindow = True, .RedirectStandardOutput = True, .RedirectStandardError = True,
                .WorkingDirectory = Path.GetDirectoryName(exe)}
            Using p = Process.Start(psi)
                Dim o = p.StandardOutput.ReadToEndAsync()
                Dim e = p.StandardError.ReadToEndAsync()
                Await p.WaitForExitAsync(ct)
                Await Task.WhenAll(o, e)
                If p.ExitCode = 0 AndAlso File.Exists(outBase & ".srt") Then Return outBase & ".srt"
            End Using
        Next
        Throw New Exception("Speech recognition failed.")
    End Function

    Private Async Function RunWhisperAsync(exe As String, model As String, wav As String, ct As CancellationToken) As Task(Of String)
        If exe Is Nothing Then Return Nothing
        Dim outBase = Path.Combine(Path.GetTempPath(), "localmind-stt-" & Guid.NewGuid().ToString("N"))
        Dim lang = If(String.IsNullOrWhiteSpace(AppCore.Settings.VoiceLanguage), "auto", AppCore.Settings.VoiceLanguage)
        Dim psi As New ProcessStartInfo(exe, $"-m ""{model}"" -f ""{wav}"" -l {lang} -t {Math.Max(2, Environment.ProcessorCount - 2)} -nt -np -otxt -of ""{outBase}""") With {
            .UseShellExecute = False, .CreateNoWindow = True, .RedirectStandardOutput = True, .RedirectStandardError = True,
            .WorkingDirectory = Path.GetDirectoryName(exe)}
        Using p = Process.Start(psi)
            Dim o = p.StandardOutput.ReadToEndAsync()
            Dim e = p.StandardError.ReadToEndAsync()
            Await p.WaitForExitAsync(ct)
            Await Task.WhenAll(o, e)
            Dim txtFile = outBase & ".txt"
            If p.ExitCode <> 0 OrElse Not File.Exists(txtFile) Then Return Nothing
            Dim text = File.ReadAllText(txtFile).Trim()
            File.Delete(txtFile)
            Return Regex.Replace(text, "\[(BLANK_AUDIO|Music|MUSIC)\]", "").Trim()
        End Using
    End Function

    '════════════════ Text to speech (Windows voices) ════════════════
    ' Every Windows-voice call runs on one speech thread of its own (an STA thread): the voice object is never used
    ' from two threads at once, which could crash LocalMind in the middle of a voice conversation.
    Private _voice As Object
    Private _defaultVoice As Object
    Private _speaking As Boolean
    ' (each Speak gets a number; Stop moves it on, so an older speech ends even if a new one starts right after)
    Private _speechNo As Integer
    Private _ttsThread As Thread
    Private ReadOnly _ttsQueue As New Collections.Concurrent.BlockingCollection(Of Action)()

    Public ReadOnly Property IsSpeaking As Boolean
        Get
            Return _speaking
        End Get
    End Property

    Private Sub OnSpeechThread(a As Action)
        SyncLock _ttsQueue
            If _ttsThread Is Nothing Then
                _ttsThread = New Thread(Sub()
                                            For Each work In _ttsQueue.GetConsumingEnumerable()
                                                Try
                                                    work()
                                                Catch ex As Exception
                                                    FailureLog.Write("(reading aloud)", "", ex)
                                                End Try
                                            Next
                                        End Sub) With {.IsBackground = True, .Name = "LocalMind speech"}
                _ttsThread.SetApartmentState(ApartmentState.STA)
                _ttsThread.Start()
            End If
        End SyncLock
        _ttsQueue.Add(a)
    End Sub

    ''' <summary>Runs on the speech thread and waits for the result (at most 15 s).</summary>
    Private Function OnSpeechThread(Of T)(f As Func(Of T)) As T
        Dim tcs As New TaskCompletionSource(Of T)(TaskCreationOptions.RunContinuationsAsynchronously)
        OnSpeechThread(Sub()
                           Try
                               tcs.TrySetResult(f())
                           Catch ex As Exception
                               tcs.TrySetException(ex)
                           End Try
                       End Sub)
        If Not tcs.Task.Wait(15000) Then Throw New TimeoutException("The Windows voice did not answer.")
        Return tcs.Task.Result
    End Function

    ' (speech thread only)
    Private Function GetVoice() As Object
        If _voice Is Nothing Then
            _voice = CreateObject("SAPI.SpVoice")
            _defaultVoice = _voice.Voice
        End If
        Return _voice
    End Function

    ''' <summary>
    ''' All Windows voices: the classic ones and the newer "OneCore" ones that Windows 10/11 installs with a language
    ''' (Settings → Time &amp; language → Speech), such as the Greek voice. (speech thread only)
    ''' </summary>
    Private Function AllVoiceTokens(v As Object) As List(Of Object)
        Dim list As New List(Of Object)()
        Dim seen As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        Dim add = Sub(tokens As Object)
                      If tokens Is Nothing Then Return
                      For i = 0 To CInt(tokens.Count) - 1
                          Try
                              Dim t = tokens.Item(i)
                              If seen.Add(CStr(t.GetDescription())) Then list.Add(t)
                          Catch
                          End Try
                      Next
                  End Sub
        Try
            add(v.GetVoices())
        Catch
        End Try
        For Each key In {"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Speech_OneCore\Voices", "HKEY_CURRENT_USER\SOFTWARE\Microsoft\Speech_OneCore\Voices"}
            Try
                Dim cat = CreateObject("SAPI.SpObjectTokenCategory")
                cat.SetId(key, False)
                add(cat.EnumerateTokens())
            Catch
            End Try
        Next
        Return list
    End Function

    Private Function IsGreekToken(t As Object) As Boolean
        Dim desc = "", lang = ""
        Try : desc = CStr(t.GetDescription()) : Catch : End Try
        Try : lang = CStr(t.GetAttribute("Language")) : Catch : End Try
        Return lang.Split(";"c).Any(Function(x) x.Trim().Equals("408", StringComparison.OrdinalIgnoreCase)) OrElse
               desc.IndexOf("Greek", StringComparison.OrdinalIgnoreCase) >= 0 OrElse desc.Contains("Ελλ")
    End Function

    ''' <summary>The voice to use: a Greek one for Greek text, else the one chosen in Settings, else Windows' default.</summary>
    Private Function PickVoice(v As Object, greek As Boolean) As Object
        Dim tokens = AllVoiceTokens(v)
        If greek Then Return tokens.FirstOrDefault(AddressOf IsGreekToken)
        Dim wanted = AppCore.Settings.TtsVoice
        If Not String.IsNullOrEmpty(wanted) Then
            Dim t = tokens.FirstOrDefault(Function(x) CStr(x.GetDescription()) = wanted)
            If t IsNot Nothing Then Return t
        End If
        Return _defaultVoice
    End Function

    ''' <summary>Installed Windows voices ("Microsoft Stefanos - Greek", …), the newer OneCore ones included.</summary>
    Public Function VoiceNames() As String()
        Try
            Return OnSpeechThread(Function() AllVoiceTokens(GetVoice()).Select(Function(t) CStr(t.GetDescription())).ToArray())
        Catch
            Return Array.Empty(Of String)()
        End Try
    End Function

    ''' <summary>A Windows voice that speaks Greek is installed.</summary>
    Public Function HasGreekVoice() As Boolean
        Try
            Return OnSpeechThread(Function() AllVoiceTokens(GetVoice()).Any(AddressOf IsGreekToken))
        Catch
            Return False
        End Try
    End Function

    Public ReadOnly Property NoGreekVoiceMessage As String
        Get
            Return Localizer.T("No Greek Windows voice is installed, so Greek can't be read aloud (an English voice only spells the letters). Add one in Windows Settings → Time & language → Speech → Add voices → Ελληνικά (Greek), or choose a multilingual AI voice (Chatterbox Multilingual) in Settings → Voice.")
        End Get
    End Property

    <DllImport("winmm.dll", CharSet:=CharSet.Unicode)>
    Private Function PlaySound(pszSound As String, hmod As IntPtr, fdwSound As UInteger) As Boolean
    End Function
    Private Const SND_SYNC As UInteger = 0, SND_FILENAME As UInteger = &H20000, SND_NODEFAULT As UInteger = 2

    Private _aiCts As CancellationTokenSource

    ''' <summary>Text made speakable: no code, Markdown, links, citation marks or emoji.</summary>
    Public Function CleanForSpeech(text As String) As String
        Return SpeechText.Plain(text)
    End Function

    Public Function IsGreek(text As String) As Boolean
        Dim t = If(text, "")
        Dim letters = Regex.Matches(t, "\p{L}").Count
        Return letters > 0 AndAlso Regex.Matches(t, "[Ͱ-Ͽἀ-῿]").Count > letters * 0.3
    End Function

    ''' <summary>Splits text into sentence groups of up to ~240 characters (short pieces start playing sooner).</summary>
    Public Function Chunks(text As String) As List(Of String)
        Dim parts = Regex.Split(text, "(?<=[.!?;·…])\s+")
        Dim list As New Collections.Generic.List(Of String)
        Dim cur As New StringBuilder()
        For Each p In parts
            If cur.Length > 0 AndAlso cur.Length + p.Length > 240 Then
                list.Add(cur.ToString().Trim())
                cur.Clear()
            End If
            cur.Append(p).Append(" "c)
        Next
        If cur.ToString().Trim().Length > 0 Then list.Add(cur.ToString().Trim())
        Return list
    End Function

    ''' <summary>The AI voice for this text: the chosen one, or for Greek one that speaks Greek (Nothing = none can).</summary>
    Private Function AiVoiceFor(greek As Boolean) As AudioModel
        Dim m = AudioStudio.Models().FirstOrDefault(Function(x) x.Id = AppCore.Settings.ReadAloudModel AndAlso Not x.IsMusic)
        If m Is Nothing OrElse Not greek Then Return m
        If m.Langs.Count = 0 OrElse m.Langs.Contains("el") Then Return m
        ' (the chosen voice has no Greek: another installed one that has)
        Return AudioStudio.Models().FirstOrDefault(Function(x) Not x.IsMusic AndAlso x.Langs.Contains("el") AndAlso AudioStudio.IsInstalled(x))
    End Function

    ''' <summary>
    ''' Reads text with an AI voice (Settings: ReadAloudModel + ReadAloudVoice): the next sentence is made while the
    ''' current one plays, so long answers start quickly. Greek text goes to a voice that speaks Greek: another AI
    ''' voice, else the Windows Greek voice.
    ''' </summary>
    Public Function SpeakAiAsync(text As String, Optional onStatus As Action(Of String) = Nothing) As Task
        StopSpeaking()
        Dim clean = SpeechText.Plain(text)
        Dim greek = IsGreek(clean)
        Dim m = AiVoiceFor(greek)
        If m Is Nothing Then
            ' no AI voice that can read this: the Windows voice (Greek, when installed)
            Speak(text, allowAi:=False)
            Return Task.Run(Async Function()
                                While _speaking
                                    Await Task.Delay(150)
                                End While
                            End Function)
        End If
        Dim cts As New CancellationTokenSource()
        _aiCts = cts
        Dim ct = cts.Token
        Dim saved = VoiceLibrary.Find(AppCore.Settings.ReadAloudVoice)
        Dim lang = If(greek, "el", If(saved?.Language, "en"))
        _speaking = True
        Return Task.Run(Async Function()
                            Try
                                Dim pieces = Chunks(clean)
                                Dim makeOne = Function(s As String) AudioStudio.GenerateAsync(New AudioStudio.AudioRequest With {
                                                  .Model = m, .Text = s, .Language = lang, .SpeakerFile = saved?.FullPath, .SpeakerText = saved?.Words},
                                                  Sub(p, t) onStatus?.Invoke(t), ct)
                                Dim nextWav = If(pieces.Count > 0, makeOne(pieces(0)), Nothing)
                                For i = 0 To pieces.Count - 1
                                    Dim wav = Await nextWav
                                    nextWav = If(i + 1 < pieces.Count, makeOne(pieces(i + 1)), Nothing)
                                    If ct.IsCancellationRequested Then Exit For
                                    PlaySound(wav, IntPtr.Zero, SND_FILENAME Or SND_SYNC Or SND_NODEFAULT)
                                    Try : IO.File.Delete(wav) : Catch : End Try
                                Next
                            Catch ex As OperationCanceledException
                            Catch ex As Exception
                                onStatus?.Invoke("⚠ " & ex.Message.Split({vbCr, vbLf}, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault())
                            Finally
                                _speaking = False
                            End Try
                        End Function)
    End Function

    ''' <summary>
    ''' Speaks text into a WAV file (for the phone): the AI voice from Settings if chosen (sentence by sentence,
    ''' joined), otherwise a Windows voice.
    ''' </summary>
    Public Async Function SpeakToFileAsync(text As String, ct As CancellationToken) As Task(Of String)
        Dim clean = SpeechText.Plain(text)
        Dim greek = IsGreek(clean)
        Dim out = Path.Combine(Path.GetTempPath(), $"localmind-speak-{Guid.NewGuid():N}.wav")
        Dim m = AiVoiceFor(greek)
        If m IsNot Nothing Then
            Dim saved = VoiceLibrary.Find(AppCore.Settings.ReadAloudVoice)
            Dim lang = If(greek, "el", If(saved?.Language, "en"))
            Dim parts As New Collections.Generic.List(Of String)
            For Each piece In Chunks(clean)
                parts.Add(Await AudioStudio.GenerateAsync(New AudioStudio.AudioRequest With {
                    .Model = m, .Text = piece, .Language = lang, .SpeakerFile = saved?.FullPath, .SpeakerText = saved?.Words}, Nothing, ct))
            Next
            JoinWavs(parts, out)
            For Each p In parts
                Try : IO.File.Delete(p) : Catch : End Try
            Next
            Return out
        End If
        Dim sentences = SpeechText.Prepare(text)
        Dim ok = Await Task.Run(Function() OnSpeechThread(Function()
                                                              Dim v = CreateObject("SAPI.SpVoice")
                                                              Dim tok = PickVoice(v, greek)
                                                              If greek AndAlso tok Is Nothing Then Return False
                                                              If tok IsNot Nothing Then v.Voice = tok
                                                              Dim fs = CreateObject("SAPI.SpFileStream")
                                                              fs.Format.Type = 22                 ' 22 kHz 16-bit mono
                                                              fs.Open(out, 3, False)              ' create for write
                                                              v.AudioOutputStream = fs
                                                              For Each part In sentences
                                                                  v.Speak(SpeechText.ToSapiXml(part), 8)   ' SVSFIsXML
                                                              Next
                                                              fs.Close()
                                                              Return True
                                                          End Function), ct)
        If Not ok Then Throw New Exception(NoGreekVoiceMessage)
        Return out
    End Function

    ''' <summary>Joins PCM WAV files of the same format into one.</summary>
    Public Sub JoinWavs(files As Collections.Generic.List(Of String), output As String)
        Dim header As Byte() = Nothing
        Using ms As New MemoryStream()
            For Each f In files
                Dim b = IO.File.ReadAllBytes(f)
                ' Find the "data" chunk.
                Dim i = 12
                While i + 8 <= b.Length
                    Dim id = Encoding.ASCII.GetString(b, i, 4)
                    Dim size = BitConverter.ToInt32(b, i + 4)
                    If id = "data" Then
                        If header Is Nothing Then header = b.Take(i + 8).ToArray()
                        ms.Write(b, i + 8, Math.Min(size, b.Length - i - 8))
                        Exit While
                    End If
                    i += 8 + size + (size And 1)
                End While
            Next
            If header Is Nothing Then Throw New Exception("No audio was made.")
            Dim data = ms.ToArray()
            Array.Copy(BitConverter.GetBytes(header.Length - 8 + data.Length), 0, header, 4, 4)
            Array.Copy(BitConverter.GetBytes(data.Length), 0, header, header.Length - 4, 4)
            Using o = IO.File.Create(output)
                o.Write(header, 0, header.Length)
                o.Write(data, 0, data.Length)
            End Using
        End Using
    End Sub

    ''' <summary>
    ''' Reads text aloud (returns at once): the chosen AI voice, or a Windows voice: the Greek one for Greek text, with
    ''' each sentence's emoji setting its mood. Throws when the text is Greek and no voice can read Greek.
    ''' </summary>
    Public Sub Speak(text As String, Optional allowAi As Boolean = True)
        If allowAi AndAlso Not String.IsNullOrEmpty(AppCore.Settings.ReadAloudModel) AndAlso
           AudioStudio.Models().Any(Function(x) x.Id = AppCore.Settings.ReadAloudModel) Then
            Dim unused = SpeakAiAsync(text)
            Return
        End If
        StopSpeaking()
        Dim sentences = SpeechText.Prepare(text)
        If sentences.Count = 0 Then Return
        Dim greek = IsGreek(String.Join(" ", sentences.Select(Function(p) p.Text)))
        ' (no Greek voice: say so, rather than an English voice spelling every letter)
        If greek AndAlso Not HasGreekVoice() Then Throw New Exception(NoGreekVoiceMessage)
        Dim mine = Interlocked.Increment(_speechNo)
        _speaking = True
        OnSpeechThread(Sub()
                           Try
                               Dim v = GetVoice()
                               Dim tok = PickVoice(v, greek)
                               If tok IsNot Nothing Then v.Voice = tok
                               For Each part In sentences
                                   If Volatile.Read(_speechNo) <> mine Then Exit For
                                   v.Speak(SpeechText.ToSapiXml(part), 1 Or 8)     ' SVSFlagsAsync | SVSFIsXML
                                   ' (waiting in short steps, so Stop takes effect at once)
                                   Do Until CBool(v.WaitUntilDone(100))
                                       If Volatile.Read(_speechNo) <> mine Then
                                           v.Speak("", 2)                         ' SVSFPurgeBeforeSpeak
                                           Exit For
                                       End If
                                   Loop
                               Next
                           Finally
                               If Volatile.Read(_speechNo) = mine Then _speaking = False
                           End Try
                       End Sub)
    End Sub

    Public Sub StopSpeaking()
        Interlocked.Increment(_speechNo)
        Try
            _aiCts?.Cancel()            ' also stops its llama-tts run
            PlaySound(Nothing, IntPtr.Zero, 0)
        Catch
        End Try
        _speaking = False
    End Sub
End Module
