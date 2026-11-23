Imports System
Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Globalization
Imports System.Linq
Imports System.Text.RegularExpressions
Imports Microsoft.Win32

Public Enum GpuVendor
    None
    Nvidia
    Amd
    Intel
    Other
End Enum

Public Class GpuDevice
    Public Property Name As String
    Public Property Vendor As GpuVendor
    Public Property VramBytes As Long
    Public Property ComputeCap As Double      ' NVIDIA only (e.g. 8.6)
End Class

''' <summary>
''' Detects GPUs, VRAM and system RAM without WMI. NVIDIA cards are queried through
''' nvidia-smi (exact numbers, CUDA version); everything else through the display-adapter
''' registry key, whose 64-bit qwMemorySize value is accurate even above 4 GB
''' (WMI's AdapterRAM is capped at 4 GB).
''' </summary>
Public Class HardwareInfo

    Public Property Gpus As New List(Of GpuDevice)()
    Public Property RamBytes As Long
    Public Property CudaDriverVersion As Double     ' highest CUDA version the driver supports
    Public Property CpuThreads As Integer = Environment.ProcessorCount

    Private Shared _cached As HardwareInfo
    Private Shared ReadOnly _lock As New Object()

    Public Shared Function Current() As HardwareInfo
        SyncLock _lock
            If _cached Is Nothing Then _cached = Detect()
            Return _cached
        End SyncLock
    End Function

    Public Shared Sub Invalidate()
        SyncLock _lock
            _cached = Nothing
        End SyncLock
    End Sub

    ''' <summary>Usable VRAM: NVIDIA total if present, otherwise the largest discrete adapter.</summary>
    Public ReadOnly Property VramBytes As Long
        Get
            Dim nv = Gpus.Where(Function(g) g.Vendor = GpuVendor.Nvidia).ToList()
            If nv.Count > 0 Then Return nv.Sum(Function(g) g.VramBytes)
            If Gpus.Count = 0 Then Return 0
            Dim best = Gpus.Max(Function(g) g.VramBytes)
            ' Integrated GPUs report a tiny dedicated carve-out; treat them as "no GPU".
            Return If(best >= 2L * 1024 * 1024 * 1024, best, 0)
        End Get
    End Property

    Public ReadOnly Property VramGB As Double
        Get
            Return VramBytes / 1024.0 / 1024.0 / 1024.0
        End Get
    End Property

    Public ReadOnly Property RamGB As Double
        Get
            Return RamBytes / 1024.0 / 1024.0 / 1024.0
        End Get
    End Property

    Public ReadOnly Property PrimaryVendor As GpuVendor
        Get
            If Gpus.Any(Function(g) g.Vendor = GpuVendor.Nvidia) Then Return GpuVendor.Nvidia
            If VramBytes = 0 Then Return GpuVendor.None
            Dim best = Gpus.OrderByDescending(Function(g) g.VramBytes).First()
            Return best.Vendor
        End Get
    End Property

    Public ReadOnly Property Summary As String
        Get
            Dim gpuText As String
            If Gpus.Count = 0 OrElse VramBytes = 0 Then
                gpuText = "No dedicated GPU detected (CPU mode)"
            Else
                Dim main = Gpus.Where(Function(g) g.VramBytes > 0).OrderByDescending(Function(g) g.VramBytes).ToList()
                gpuText = String.Join(" + ", main.Select(Function(g) g.Name)) &
                          $" · {VramGB:0.0} GB VRAM"
            End If
            Return $"{gpuText} · {RamGB:0} GB RAM · {CpuThreads} CPU threads"
        End Get
    End Property

    '────────────────────────────────────────────────────────────
    Private Shared Function Detect() As HardwareInfo
        Dim hw As New HardwareInfo()
        Try
            hw.RamBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes
        Catch
        End Try

        Try
            DetectNvidia(hw)
        Catch
        End Try

        Try
            DetectFromRegistry(hw)
        Catch
        End Try

        Return hw
    End Function

    Private Shared Sub DetectNvidia(hw As HardwareInfo)
        Dim csv = RunTool("nvidia-smi", "--query-gpu=name,memory.total,compute_cap --format=csv,noheader,nounits")
        If String.IsNullOrWhiteSpace(csv) Then Return

        For Each line In csv.Split({vbCr, vbLf}, StringSplitOptions.RemoveEmptyEntries)
            Dim parts = line.Split(","c).Select(Function(p) p.Trim()).ToArray()
            If parts.Length < 2 Then Continue For
            Dim mib As Double
            Double.TryParse(parts(1), NumberStyles.Float, CultureInfo.InvariantCulture, mib)
            Dim cc As Double
            If parts.Length >= 3 Then Double.TryParse(parts(2), NumberStyles.Float, CultureInfo.InvariantCulture, cc)
            hw.Gpus.Add(New GpuDevice With {
                .Name = parts(0), .Vendor = GpuVendor.Nvidia,
                .VramBytes = CLng(mib * 1024 * 1024), .ComputeCap = cc})
        Next

        Dim header = RunTool("nvidia-smi", "")
        Dim m = Regex.Match(If(header, ""), "CUDA Version:\s*(\d+\.\d+)")
        If m.Success Then
            Dim v As Double
            If Double.TryParse(m.Groups(1).Value, NumberStyles.Float, CultureInfo.InvariantCulture, v) Then
                hw.CudaDriverVersion = v
            End If
        End If
    End Sub

    Private Shared Sub DetectFromRegistry(hw As HardwareInfo)
        Const ClassKey = "SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}"
        Using root = Registry.LocalMachine.OpenSubKey(ClassKey)
            If root Is Nothing Then Return
            For Each subName In root.GetSubKeyNames()
                If Not Regex.IsMatch(subName, "^\d{4}$") Then Continue For
                Using k = root.OpenSubKey(subName)
                    If k Is Nothing Then Continue For
                    Dim desc = TryCast(k.GetValue("DriverDesc"), String)
                    If String.IsNullOrWhiteSpace(desc) Then Continue For
                    If desc.IndexOf("Basic Display", StringComparison.OrdinalIgnoreCase) >= 0 Then Continue For
                    If desc.IndexOf("Remote", StringComparison.OrdinalIgnoreCase) >= 0 Then Continue For

                    Dim vendor = VendorFromName(desc)
                    ' NVIDIA cards are already covered (more precisely) by nvidia-smi.
                    If vendor = GpuVendor.Nvidia AndAlso hw.Gpus.Any(Function(g) g.Vendor = GpuVendor.Nvidia) Then Continue For

                    Dim bytes = ReadMemorySize(k)
                    If hw.Gpus.Any(Function(g) g.Name = desc AndAlso g.VramBytes = bytes) Then Continue For
                    hw.Gpus.Add(New GpuDevice With {.Name = desc, .Vendor = vendor, .VramBytes = bytes})
                End Using
            Next
        End Using
    End Sub

    Private Shared Function ReadMemorySize(k As RegistryKey) As Long
        Dim v = k.GetValue("HardwareInformation.qwMemorySize")
        Dim r = ToLong(v)
        If r > 0 Then Return r
        Return ToLong(k.GetValue("HardwareInformation.MemorySize"))
    End Function

    Private Shared Function ToLong(v As Object) As Long
        If v Is Nothing Then Return 0
        If TypeOf v Is Long Then Return CLng(v)
        If TypeOf v Is Integer Then Return CLng(CUInt(CInt(v) And &HFFFFFFFFL))
        Dim b = TryCast(v, Byte())
        If b IsNot Nothing Then
            If b.Length >= 8 Then Return BitConverter.ToInt64(b, 0)
            If b.Length >= 4 Then Return BitConverter.ToUInt32(b, 0)
        End If
        Return 0
    End Function

    Private Shared Function VendorFromName(n As String) As GpuVendor
        Dim s = n.ToLowerInvariant()
        If s.Contains("nvidia") OrElse s.Contains("geforce") OrElse s.Contains("quadro") OrElse s.Contains("rtx") Then Return GpuVendor.Nvidia
        If s.Contains("amd") OrElse s.Contains("radeon") Then Return GpuVendor.Amd
        If s.Contains("intel") OrElse s.Contains(" arc") Then Return GpuVendor.Intel
        Return GpuVendor.Other
    End Function

    Friend Shared Function RunTool(exe As String, args As String, Optional timeoutMs As Integer = 6000) As String
        Try
            Dim psi As New ProcessStartInfo(exe, args) With {
                .UseShellExecute = False, .CreateNoWindow = True,
                .RedirectStandardOutput = True, .RedirectStandardError = True
            }
            Using p = Process.Start(psi)
                Dim outTask = p.StandardOutput.ReadToEndAsync()
                If Not p.WaitForExit(timeoutMs) Then
                    Try : p.Kill() : Catch : End Try
                    Return Nothing
                End If
                Return outTask.Result
            End Using
        Catch
            Return Nothing
        End Try
    End Function
End Class
