Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.IO
Imports System.Linq
Imports System.Runtime.InteropServices
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms

''' <summary>
''' Microphone capture with simple voice-activity detection (winmm waveIn, 16 kHz mono 16-bit).
''' Raises Utterance with a WAV file each time you stop talking.
''' </summary>
Public Class WaveRecorder
    Implements IDisposable

    <StructLayout(LayoutKind.Sequential)>
    Private Structure WAVEFORMATEX
        Public wFormatTag As UShort
        Public nChannels As UShort
        Public nSamplesPerSec As UInteger
        Public nAvgBytesPerSec As UInteger
        Public nBlockAlign As UShort
        Public wBitsPerSample As UShort
        Public cbSize As UShort
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Private Structure WAVEHDR
        Public lpData As IntPtr
        Public dwBufferLength As UInteger
        Public dwBytesRecorded As UInteger
        Public dwUser As IntPtr
        Public dwFlags As UInteger
        Public dwLoops As UInteger
        Public lpNext As IntPtr
        Public reserved As IntPtr
    End Structure

    <DllImport("winmm.dll")> Private Shared Function waveInOpen(ByRef h As IntPtr, dev As Integer, ByRef fmt As WAVEFORMATEX, cb As IntPtr, inst As IntPtr, flags As Integer) As Integer
    End Function
    <DllImport("winmm.dll")> Private Shared Function waveInPrepareHeader(h As IntPtr, hdr As IntPtr, size As Integer) As Integer
    End Function
    <DllImport("winmm.dll")> Private Shared Function waveInUnprepareHeader(h As IntPtr, hdr As IntPtr, size As Integer) As Integer
    End Function
    <DllImport("winmm.dll")> Private Shared Function waveInAddBuffer(h As IntPtr, hdr As IntPtr, size As Integer) As Integer
    End Function
    <DllImport("winmm.dll")> Private Shared Function waveInStart(h As IntPtr) As Integer
    End Function
    <DllImport("winmm.dll")> Private Shared Function waveInReset(h As IntPtr) As Integer
    End Function
    <DllImport("winmm.dll")> Private Shared Function waveInClose(h As IntPtr) As Integer
    End Function

    Private Const Rate = 16000
    Private Const BufMs = 50
    Private Const BufBytes = Rate * 2 * BufMs \ 1000
    Private _h As IntPtr
    Private ReadOnly _hdrs As New List(Of IntPtr)
    Private _thread As Thread
    Private _running As Boolean
    Public Property Paused As Boolean
    ''' <summary>0–1 input level, for a meter.</summary>
    Public Property Level As Single
    Public Event Utterance(wavFile As String)

    Public Sub Start()
        Dim fmt As New WAVEFORMATEX With {.wFormatTag = 1, .nChannels = 1, .nSamplesPerSec = Rate, .wBitsPerSample = 16, .nBlockAlign = 2, .nAvgBytesPerSec = Rate * 2}
        Dim err = waveInOpen(_h, -1, fmt, IntPtr.Zero, IntPtr.Zero, 0)
        If err <> 0 Then Throw New Exception(Localizer.T("Could not open the microphone. Check that one is connected and allowed in Windows privacy settings.") & $" ({err})")
        For i = 1 To 8
            Dim hdr = Marshal.AllocHGlobal(Marshal.SizeOf(Of WAVEHDR)())
            Dim w As New WAVEHDR With {.lpData = Marshal.AllocHGlobal(BufBytes), .dwBufferLength = BufBytes}
            Marshal.StructureToPtr(w, hdr, False)
            waveInPrepareHeader(_h, hdr, Marshal.SizeOf(Of WAVEHDR)())
            waveInAddBuffer(_h, hdr, Marshal.SizeOf(Of WAVEHDR)())
            _hdrs.Add(hdr)
        Next
        _running = True
        waveInStart(_h)
        _thread = New Thread(AddressOf Loop_) With {.IsBackground = True}
        _thread.Start()
    End Sub

    ''' <summary>Polls finished buffers, detects speech and silence, and cuts utterances.</summary>
    Private Sub Loop_()
        Dim noise As Single = 300, speaking = False, silentMs = 0, speechMs = 0
        Dim cur As New MemoryStream()
        Dim pre As New Queue(Of Byte())         ' 300 ms before speech starts, so first syllables aren't lost
        While _running
            Dim any = False
            For Each hdr In _hdrs.ToArray()
                Dim w = Marshal.PtrToStructure(Of WAVEHDR)(hdr)
                If (w.dwFlags And 1UI) = 0 Then Continue For   ' WHDR_DONE
                any = True
                Dim data(CInt(w.dwBytesRecorded) - 1) As Byte
                If data.Length > 0 Then Marshal.Copy(w.lpData, data, 0, data.Length)
                w.dwFlags = w.dwFlags And Not 1UI
                Marshal.StructureToPtr(w, hdr, False)
                If _running Then waveInAddBuffer(_h, hdr, Marshal.SizeOf(Of WAVEHDR)())
                If data.Length < 2 OrElse Paused Then
                    If Paused Then speaking = False : cur.SetLength(0) : pre.Clear()
                    Continue For
                End If
                Dim sum As Double = 0
                For i = 0 To data.Length - 2 Step 2
                    Dim s = BitConverter.ToInt16(data, i)
                    sum += CDbl(s) * s
                Next
                Dim rms = CSng(Math.Sqrt(sum / (data.Length \ 2)))
                Level = Math.Min(1.0F, rms / 6000.0F)
                Dim loud = rms > Math.Max(500, noise * 3)
                If Not speaking Then
                    noise = noise * 0.95F + rms * 0.05F
                    pre.Enqueue(data)
                    While pre.Count > 6 : pre.Dequeue() : End While
                    If loud Then
                        speaking = True : silentMs = 0 : speechMs = 0
                        cur.SetLength(0)
                        For Each p In pre : cur.Write(p) : Next
                    End If
                Else
                    cur.Write(data)
                    speechMs += BufMs
                    silentMs = If(loud, 0, silentMs + BufMs)
                    If silentMs >= 900 OrElse speechMs > 30000 Then
                        speaking = False
                        pre.Clear()
                        If speechMs - silentMs >= 350 Then RaiseEvent Utterance(SaveWav(cur.ToArray()))
                        cur.SetLength(0)
                    End If
                End If
            Next
            If Not any Then Thread.Sleep(15)
        End While
    End Sub

    Private Shared Function SaveWav(pcm As Byte()) As String
        Dim f = Path.Combine(Path.GetTempPath(), $"localmind-talk-{Guid.NewGuid():N}.wav")
        Using fs = File.Create(f), bw As New BinaryWriter(fs)
            bw.Write(Text.Encoding.ASCII.GetBytes("RIFF")) : bw.Write(36 + pcm.Length) : bw.Write(Text.Encoding.ASCII.GetBytes("WAVE"))
            bw.Write(Text.Encoding.ASCII.GetBytes("fmt ")) : bw.Write(16) : bw.Write(CShort(1)) : bw.Write(CShort(1)) : bw.Write(Rate) : bw.Write(Rate * 2)
            bw.Write(CShort(2)) : bw.Write(CShort(16))
            bw.Write(Text.Encoding.ASCII.GetBytes("data")) : bw.Write(pcm.Length) : bw.Write(pcm)
        End Using
        Return f
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        _running = False
        Try
            _thread?.Join(500)
            If _h <> IntPtr.Zero Then
                waveInReset(_h)
                For Each hdr In _hdrs
                    waveInUnprepareHeader(_h, hdr, Marshal.SizeOf(Of WAVEHDR)())
                    Dim w = Marshal.PtrToStructure(Of WAVEHDR)(hdr)
                    Marshal.FreeHGlobal(w.lpData)
                    Marshal.FreeHGlobal(hdr)
                Next
                _hdrs.Clear()
                waveInClose(_h)
                _h = IntPtr.Zero
            End If
        Catch
        End Try
    End Sub
End Class

''' <summary>Hands-free conversation: you talk, the AI answers out loud, and it listens again.</summary>
Public Class VoiceChatForm
    Inherits Form

    Private ReadOnly _ask As Func(Of String, Task(Of String))
    Private ReadOnly lblState As Label
    Private ReadOnly meter As ProgressBar
    Private ReadOnly log As RichTextBox
    Private ReadOnly btnPause As Button
    Private ReadOnly tmr As New System.Windows.Forms.Timer() With {.Interval = 80}
    Private _rec As WaveRecorder
    Private _busy As Integer

    ''' <param name="ask">Sends a message to the chat and returns the answer text.</param>
    Public Sub New(ask As Func(Of String, Task(Of String)))
        _ask = ask
        Text = Localizer.T("Voice conversation")
        Size = New Size(560, 600)
        StartPosition = FormStartPosition.CenterParent
        Theme.ApplyForm(Me)
        lblState = New Label() With {.Dock = DockStyle.Top, .Height = 90, .TextAlign = ContentAlignment.MiddleCenter,
                                     .Font = New Font("Segoe UI", 20.0!, FontStyle.Bold), .ForeColor = Theme.CText}
        meter = New ProgressBar() With {.Dock = DockStyle.Top, .Height = 8, .Maximum = 100}
        log = New RichTextBox() With {.Dock = DockStyle.Fill, .ReadOnly = True, .BackColor = Theme.CBack, .ForeColor = Theme.CText,
                                      .BorderStyle = BorderStyle.None, .Font = New Font("Segoe UI", 10.5!)}
        Dim bar As New FlowLayoutPanel() With {.Dock = DockStyle.Bottom, .Height = 48, .Padding = New Padding(8)}
        btnPause = Theme.MakeButton("⏸ Pause", width:=120)
        Dim btnHush = Theme.MakeButton("🤫 Stop talking", width:=140)
        Dim tip = Theme.MakeLabel(Localizer.T("Just talk. A short pause ends your turn."))
        bar.Controls.AddRange({btnPause, btnHush, tip})
        Controls.Add(log)
        Controls.Add(bar)
        Controls.Add(meter)
        Controls.Add(lblState)
        AddHandler btnPause.Click, Sub()
                                       If _rec Is Nothing Then Return
                                       _rec.Paused = Not _rec.Paused
                                       btnPause.Text = Localizer.T(If(_rec.Paused, "▶ Listen", "⏸ Pause"))
                                       SetState(If(_rec.Paused, "⏸ " & Localizer.T("Paused"), "🎤 " & Localizer.T("Listening…")))
                                   End Sub
        AddHandler btnHush.Click, Sub() Voice.StopSpeaking()
        AddHandler tmr.Tick, Sub() If _rec IsNot Nothing Then meter.Value = CInt(_rec.Level * 100)
    End Sub

    Protected Overrides Sub OnShown(e As EventArgs)
        MyBase.OnShown(e)
        Try
            _rec = New WaveRecorder()
            AddHandler _rec.Utterance, AddressOf OnUtterance
            _rec.Start()
            tmr.Start()
            SetState("🎤 " & Localizer.T("Listening…"))
        Catch ex As Exception
            SetState("⚠")
            Append("⚠ " & ex.Message, Color.OrangeRed)
        End Try
    End Sub

    Private Sub SetState(t As String)
        If InvokeRequired Then
            BeginInvoke(Sub() SetState(t))
        Else
            lblState.Text = t
        End If
    End Sub

    Private Sub Append(t As String, c As Color)
        If InvokeRequired Then
            BeginInvoke(Sub() Append(t, c))
            Return
        End If
        log.SelectionStart = log.TextLength
        log.SelectionColor = c
        log.AppendText(t & vbLf & vbLf)
        log.ScrollToCaret()
    End Sub

    Private Async Sub OnUtterance(wav As String)
        If Interlocked.Exchange(_busy, 1) = 1 Then Return
        Try
            _rec.Paused = True                       ' don't hear ourselves
            SetState("✍ " & Localizer.T("Writing down…"))
            Dim text = (Await Voice.TranscribeAsync(wav, Nothing, CancellationToken.None)).Trim()
            If text.Length < 2 Then Return
            Append("🧑 " & text, Theme.CDim)
            SetState("🧠 " & Localizer.T("Thinking…"))
            Dim answer = Await CType(Invoke(Function() _ask(text)), Task(Of String))
            Append("🤖 " & answer, Theme.CText)
            SetState("🔊 " & Localizer.T("Speaking…"))
            If Not String.IsNullOrEmpty(AppCore.Settings.ReadAloudModel) Then
                Await Voice.SpeakAiAsync(answer)
            Else
                Voice.Speak(answer)
                While Voice.IsSpeaking
                    Await Task.Delay(150)
                End While
            End If
        Catch ex As Exception
            Append("⚠ " & ex.Message, Color.OrangeRed)
        Finally
            Try : File.Delete(wav) : Catch : End Try
            If _rec IsNot Nothing AndAlso Not IsDisposed Then
                _rec.Paused = False
                SetState("🎤 " & Localizer.T("Listening…"))
            End If
            Interlocked.Exchange(_busy, 0)
        End Try
    End Sub

    Protected Overrides Sub OnFormClosing(e As FormClosingEventArgs)
        tmr.Stop()
        Voice.StopSpeaking()
        _rec?.Dispose()
        _rec = Nothing
        MyBase.OnFormClosing(e)
    End Sub
End Class
