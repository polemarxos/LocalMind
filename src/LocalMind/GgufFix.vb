Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Text

''' <summary>
''' GGUF files made with the ComfyUI-GGUF converter store some small tensors flattened to one dimension and keep
''' their real shape in "comfy.gguf.orig_shape.*" settings. ComfyUI reshapes them; stable-diffusion.cpp does not and
''' stops with "cannot load component" (e.g. the 10Eros MiniMax H3 and many community video models).
''' This repair gives those tensors their real shape back in the file's header. Only the header changes, in place:
''' a padding setting keeps the header at its exact length, so the multi-GB data is not touched or copied and the
''' file keeps its download size.
''' </summary>
Public Module GgufFix
    Private Const OrigPrefix As String = "comfy.gguf.orig_shape."
    Private Const PadKey As String = "localmind.pad"

    Private Class Reader
        Public B As Byte()
        Public O As Integer
        Public Function U32() As UInteger
            Dim v = BitConverter.ToUInt32(B, O) : O += 4 : Return v
        End Function
        Public Function U64() As ULong
            Dim v = BitConverter.ToUInt64(B, O) : O += 8 : Return v
        End Function
        Public Function Str() As String
            Dim n = CInt(U64())
            Dim s = Encoding.UTF8.GetString(B, O, n) : O += n : Return s
        End Function
        ''' <summary>Reads one value of the type; numbers come back as Long/Double, arrays as lists.</summary>
        Public Function Value(t As UInteger) As Object
            Select Case t
                Case 0UI : Dim v = B(O) : O += 1 : Return CLng(v)
                Case 1UI : Dim v = CSByte(If(B(O) > 127, CInt(B(O)) - 256, B(O))) : O += 1 : Return CLng(v)
                Case 2UI : Dim v = BitConverter.ToUInt16(B, O) : O += 2 : Return CLng(v)
                Case 3UI : Dim v = BitConverter.ToInt16(B, O) : O += 2 : Return CLng(v)
                Case 4UI : Return CLng(U32())
                Case 5UI : Dim v = BitConverter.ToInt32(B, O) : O += 4 : Return CLng(v)
                Case 6UI : Dim v = BitConverter.ToSingle(B, O) : O += 4 : Return CDbl(v)
                Case 7UI : Dim v = B(O) <> 0 : O += 1 : Return v
                Case 8UI : Return Str()
                Case 9UI
                    Dim at = U32()
                    Dim n = CLng(U64())
                    Dim l As New List(Of Object)
                    For i = 1L To n
                        l.Add(Value(at))
                    Next
                    Return l
                Case 10UI : Return CLng(U64())
                Case 11UI : Dim v = BitConverter.ToInt64(B, O) : O += 8 : Return v
                Case 12UI : Dim v = BitConverter.ToDouble(B, O) : O += 8 : Return v
                Case Else : Throw New InvalidDataException("unknown GGUF value type " & t)
            End Select
        End Function
    End Class

    Private Function Align(n As Long, a As Long) As Long
        Return ((n + a - 1) \ a) * a       ' (VB: \ binds looser than *)
    End Function

    ''' <summary>True when the file needed and got the repair; False when it needed nothing (or is not a GGUF).</summary>
    Public Function EnsurePatched(file As String) As Boolean
        If Not IO.File.Exists(file) OrElse Not file.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase) Then Return False
        ' Most headers fit in 2 MB; text encoders with a big vocabulary need more.
        Try
            Return Patch(file, 2L * 1024 * 1024)
        Catch ex As ArgumentException
            Return Patch(file, 128L * 1024 * 1024)
        End Try
    End Function

    Private Function Patch(file As String, readBytes As Long) As Boolean
        Using fs As New FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.Read)
            Dim want = CInt(Math.Min(fs.Length, readBytes))
            Dim buf(want - 1) As Byte
            fs.ReadExactly(buf, 0, want)
            If want < 24 OrElse Encoding.ASCII.GetString(buf, 0, 4) <> "GGUF" Then Return False
            Dim r As New Reader With {.B = buf, .O = 4}
            Dim version = r.U32()
            If version < 2 Then Return False
            Dim nTensors = CLng(r.U64())
            Dim nKv = CLng(r.U64())

            ' settings: keep their exact bytes, except the shape notes this repair applies
            Dim kept As New List(Of Byte())
            Dim shapes As New Dictionary(Of String, List(Of Long))(StringComparer.Ordinal)
            Dim alignment = 32L
            For i = 1L To nKv
                Dim start = r.O
                Dim key = r.Str()
                Dim t = r.U32()
                Dim v = r.Value(t)
                If key.StartsWith(OrigPrefix, StringComparison.Ordinal) AndAlso TypeOf v Is List(Of Object) Then
                    shapes(key.Substring(OrigPrefix.Length)) = CType(v, List(Of Object)).Select(Function(x) Convert.ToInt64(x)).ToList()
                ElseIf key = PadKey Then
                    ' (an earlier repair's padding is rebuilt)
                Else
                    If key = "general.alignment" Then alignment = Math.Max(1L, Convert.ToInt64(v))
                    kept.Add(buf.Skip(start).Take(r.O - start).ToArray())
                End If
            Next
            If shapes.Count = 0 Then Return False

            ' tensor infos, with the real shape where one was noted (GGUF dims run innermost first: reversed)
            Dim infos As New MemoryStream()
            Dim w As New BinaryWriter(infos)
            Dim changed = 0
            For i = 1L To nTensors
                Dim name = r.Str()
                Dim nd = CInt(r.U32())
                Dim dims As New List(Of Long)
                For d = 1 To nd
                    dims.Add(CLng(r.U64()))
                Next
                Dim type = r.U32()
                Dim offset = r.U64()
                Dim orig As List(Of Long) = Nothing
                If shapes.TryGetValue(name, orig) AndAlso orig.Count > 0 Then
                    Dim real = Enumerable.Reverse(orig).ToList()
                    Dim have = dims.Aggregate(1L, Function(a, b) a * b)
                    Dim need = real.Aggregate(1L, Function(a, b) a * b)
                    If need = have AndAlso Not real.SequenceEqual(dims) Then
                        dims = real
                        changed += 1
                    End If
                End If
                Dim nb = Encoding.UTF8.GetBytes(name)
                w.Write(CULng(nb.Length)) : w.Write(nb)
                w.Write(CUInt(dims.Count))
                For Each d In dims
                    w.Write(CULng(d))
                Next
                w.Write(type) : w.Write(offset)
            Next
            Dim dataStart = Align(r.O, alignment)
            If changed = 0 Then Return False

            ' new header: same settings minus the shape notes, plus padding so the data starts where it did
            Dim keyBytes = Encoding.UTF8.GetBytes(PadKey)
            Dim head As New MemoryStream()
            Dim hw As New BinaryWriter(head)
            hw.Write(Encoding.ASCII.GetBytes("GGUF")) : hw.Write(version)
            hw.Write(CULng(nTensors)) : hw.Write(CULng(kept.Count + 1))
            For Each k In kept
                hw.Write(k)
            Next
            Dim padOverhead = 8 + keyBytes.Length + 4 + 8
            Dim padLen = dataStart - (head.Length + padOverhead + infos.Length)
            If padLen < 0 Then Return False             ' (never: the removed notes are larger than the new dims)
            hw.Write(CULng(keyBytes.Length)) : hw.Write(keyBytes) : hw.Write(8UI)
            hw.Write(CULng(padLen)) : hw.Write(Enumerable.Repeat(CByte(32), CInt(padLen)).ToArray())
            hw.Write(infos.ToArray())
            If head.Length <> dataStart Then Return False
            fs.Position = 0
            fs.Write(head.ToArray(), 0, CInt(head.Length))
            fs.Flush(True)
            Return True
        End Using
    End Function
End Module
