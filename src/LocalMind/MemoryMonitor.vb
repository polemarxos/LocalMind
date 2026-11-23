Imports System
Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Linq
Imports System.Runtime.InteropServices

''' <summary>Live graphics-memory and RAM use (Windows performance counters: works for NVIDIA, AMD and Intel).</summary>
Public Module MemoryMonitor

    <StructLayout(LayoutKind.Sequential)>
    Private Structure MEMORYSTATUSEX
        Public dwLength As UInteger
        Public dwMemoryLoad As UInteger
        Public ullTotalPhys As ULong
        Public ullAvailPhys As ULong
        Public ullTotalPageFile As ULong
        Public ullAvailPageFile As ULong
        Public ullTotalVirtual As ULong
        Public ullAvailVirtual As ULong
        Public ullAvailExtendedVirtual As ULong
    End Structure

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Function GlobalMemoryStatusEx(ByRef m As MEMORYSTATUSEX) As Boolean
    End Function

    <DllImport("pdh.dll", CharSet:=CharSet.Unicode)> Private Function PdhOpenQuery(src As String, user As IntPtr, ByRef q As IntPtr) As Integer
    End Function
    <DllImport("pdh.dll", CharSet:=CharSet.Unicode)> Private Function PdhAddEnglishCounter(q As IntPtr, path As String, user As IntPtr, ByRef c As IntPtr) As Integer
    End Function
    <DllImport("pdh.dll")> Private Function PdhCollectQueryData(q As IntPtr) As Integer
    End Function
    <DllImport("pdh.dll", CharSet:=CharSet.Unicode)> Private Function PdhGetFormattedCounterArray(c As IntPtr, fmt As UInteger, ByRef size As UInteger, ByRef count As UInteger, buf As IntPtr) As Integer
    End Function

    Private _q, _c As IntPtr
    Private _pdhFailed As Boolean

    ''' <summary>Used and total system RAM in bytes.</summary>
    Public Function Ram() As (Used As Long, Total As Long)
        Dim m As New MEMORYSTATUSEX With {.dwLength = CUInt(Marshal.SizeOf(Of MEMORYSTATUSEX)())}
        If Not OperatingSystem.IsWindows() OrElse Not GlobalMemoryStatusEx(m) Then Return (0, 0)
        Return (CLng(m.ullTotalPhys - m.ullAvailPhys), CLng(m.ullTotalPhys))
    End Function

    ''' <summary>Dedicated graphics memory in use (largest adapter), in bytes; -1 if unknown.</summary>
    Public Function VramUsed() As Long
        If Not OperatingSystem.IsWindows() OrElse _pdhFailed Then Return -1
        Try
            If _q = IntPtr.Zero Then
                If PdhOpenQuery(Nothing, IntPtr.Zero, _q) <> 0 OrElse
                   PdhAddEnglishCounter(_q, "\GPU Adapter Memory(*)\Dedicated Usage", IntPtr.Zero, _c) <> 0 Then
                    _pdhFailed = True
                    Return -1
                End If
                PdhCollectQueryData(_q)
            End If
            If PdhCollectQueryData(_q) <> 0 Then Return -1
            Dim size As UInteger = 0, count As UInteger = 0
            Const PDH_FMT_LARGE As UInteger = &H400
            PdhGetFormattedCounterArray(_c, PDH_FMT_LARGE, size, count, IntPtr.Zero)
            If size = 0 Then Return -1
            Dim buf = Marshal.AllocHGlobal(CInt(size))
            Try
                If PdhGetFormattedCounterArray(_c, PDH_FMT_LARGE, size, count, buf) <> 0 Then Return -1
                ' PDH_FMT_COUNTERVALUE_ITEM_W: name pointer, then {status (4) + padding (4) + 8-byte value}.
                Dim itemSize = IntPtr.Size + 16
                Dim best As Long = 0
                For i = 0 To CInt(count) - 1
                    Dim v = Marshal.ReadInt64(buf, i * itemSize + IntPtr.Size + 8)
                    best = Math.Max(best, v)
                Next
                Return best
            Finally
                Marshal.FreeHGlobal(buf)
            End Try
        Catch
            _pdhFailed = True
            Return -1
        End Try
    End Function

    ''' <summary>One line for the status bar.</summary>
    Public Function Summary(hw As HardwareInfo) As String
        Dim gb = 1024.0 * 1024 * 1024
        Dim r = Ram()
        Dim v = VramUsed()
        Dim parts As New List(Of String)
        If hw.VramBytes > 0 Then parts.Add($"GPU {If(v >= 0, (v / gb).ToString("0.0"), "?")}/{hw.VramGB:0} GB")
        If r.Total > 0 Then parts.Add($"RAM {r.Used / gb:0.0}/{r.Total / gb:0} GB")
        Dim busy As New List(Of String)
        If AppCore.Engine.CurrentModel IsNot Nothing Then busy.Add("💬 " & AppCore.Engine.CurrentModel.DisplayName.Split("·"c)(0).Trim())
        If MediaStudio.IsBusy Then busy.Add("🎨")
        If AudioStudio.IsBusy Then busy.Add("🎵")
        Return "🖥 " & String.Join(" · ", parts) & If(busy.Count > 0, "  |  " & String.Join(" ", busy), "")
    End Function
End Module
