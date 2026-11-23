Imports System
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
        Return New ExternalTools.ToolPackage With {
            .Name = "whisper", .Flavor = flavor, .Repo = "ggml-org/whisper.cpp", .ExeName = "whisper-cli.exe",
            .AssetPatterns = If(flavor = "cuda12",
                                {"^whisper-(cublas-12[\d.]*-bin-x64|bin-win-cuda-12[\d.]*-x64)\.zip$"},
                                {"^whisper-(blas-bin-x64|bin-x64)\.zip$"})}
    End Function

    Private Function PreferredFlavor() As String
        Return If(HardwareInfo.Current().PrimaryVendor = GpuVendor.Nvidia, "cuda12", "cpu")
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

    ''' <summary>Transcribes a WAV file. Installs whisper.cpp and the model on first use.</summary>
    Public Async Function TranscribeAsync(wav As String, progress As Action(Of String), ct As CancellationToken) As Task(Of String)
        Dim flavor = PreferredFlavor()
        Dim pkg = WhisperPackage(flavor)
        If ExternalTools.FindExe(pkg) Is Nothing Then
            Dim gpuInstallFailed = False
            Try
                Await ExternalTools.InstallAsync(pkg, progress, ct)
            Catch ex As Exception When flavor <> "cpu"
                gpuInstallFailed = True
            End Try
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

    '════════════════ Text to speech (SAPI) ════════════════
    Private _voice As Object
    Private _speaking As Boolean

    Public ReadOnly Property IsSpeaking As Boolean
        Get
            Return _speaking
        End Get
    End Property

    ''' <summary>Installed Windows voices ("Microsoft Stefanos - Greek", …).</summary>
    Public Function VoiceNames() As String()
        Try
            Dim v = GetVoice()
            Dim tokens = v.GetVoices()
            Dim names As New Collections.Generic.List(Of String)
            For i = 0 To CInt(tokens.Count) - 1
                names.Add(CStr(tokens.Item(i).GetDescription()))
            Next
            Return names.ToArray()
        Catch
            Return Array.Empty(Of String)()
        End Try
    End Function

    Private Function GetVoice() As Object
        If _voice Is Nothing Then _voice = CreateObject("SAPI.SpVoice")
        Return _voice
    End Function

    ''' <summary>Reads text aloud (async). Picks a Greek voice for Greek text when available.</summary>
    Public Sub Speak(text As String)
        Try
            Dim v = GetVoice()
            StopSpeaking()
            Dim clean = Regex.Replace(If(text, ""), "```[\s\S]*?```", " (code) ")
            clean = Regex.Replace(clean, "[#*_`>|]+", " ")
            clean = Regex.Replace(clean, "\[(\w?\d+)\]", "")
            Dim isGreek = Regex.Matches(clean, "[Ͱ-Ͽ]").Count > clean.Length * 0.2
            Dim wanted = AppCore.Settings.TtsVoice
            Dim tokens = v.GetVoices()
            For i = 0 To CInt(tokens.Count) - 1
                Dim t = tokens.Item(i)
                Dim desc = CStr(t.GetDescription())
                Dim lang = ""
                Try
                    lang = CStr(t.GetAttribute("Language"))
                Catch
                End Try
                Dim greekVoice = lang.Split(";"c).Any(Function(x) x.Trim().Equals("408", StringComparison.OrdinalIgnoreCase)) OrElse desc.Contains("Greek")
                If (isGreek AndAlso greekVoice) OrElse (Not isGreek AndAlso Not String.IsNullOrEmpty(wanted) AndAlso desc = wanted) Then
                    v.Voice = t
                    Exit For
                End If
            Next
            _speaking = True
            v.Speak(clean, 1)   ' SVSFlagsAsync
            Task.Run(Sub()
                         Try
                             v.WaitUntilDone(-1)
                         Catch
                         End Try
                         _speaking = False
                     End Sub)
        Catch ex As Exception
            _speaking = False
            Throw New Exception("Text-to-speech is not available: " & ex.Message)
        End Try
    End Sub

    Public Sub StopSpeaking()
        Try
            If _voice IsNot Nothing Then _voice.Speak("", 2)   ' SVSFPurgeBeforeSpeak
        Catch
        End Try
        _speaking = False
    End Sub
End Module
