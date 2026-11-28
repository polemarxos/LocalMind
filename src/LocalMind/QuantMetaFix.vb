Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Text
Imports Newtonsoft.Json
Imports Newtonsoft.Json.Linq

''' <summary>
''' Some INT8 checkpoints (e.g. those made with "Star Ultimate Model Converter") keep their ComfyUI quantization
''' settings in the file's metadata ("_quantization_metadata": int8_tensorwise, convrot, group size) instead of in
''' one small ".comfy_quant" tensor per layer. The engine only reads the tensors: without them it takes rotated
''' (ConvRot) INT8 weights as plain ones and every picture / video comes out as coloured noise. This adds the
''' ".comfy_quant" tensors from the metadata. The header is rewritten in place when the new one fits (the settings
''' move out of the metadata, so it usually does); otherwise the file is copied once with a larger header.
''' </summary>
Public Module QuantMetaFix
    ''' <summary>True when the file was changed.</summary>
    Public Function EnsureComfyQuantTensors(file As String, Optional progress As Action(Of String) = Nothing) As Boolean
        If Not IO.File.Exists(file) OrElse Not file.EndsWith(".safetensors", StringComparison.OrdinalIgnoreCase) Then Return False
        Dim header As JObject, hb As Byte(), originalSize As Long
        Using fs As New FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read)
            Dim lenBuf(7) As Byte
            fs.ReadExactly(lenBuf, 0, 8)
            Dim headerLen = BitConverter.ToInt64(lenBuf, 0)
            If headerLen <= 2 OrElse headerLen > 200_000_000 OrElse headerLen > fs.Length - 8 Then Return False
            ReDim hb(CInt(headerLen) - 1)
            fs.ReadExactly(hb, 0, hb.Length)
            originalSize = fs.Length
        End Using
        Try
            header = JObject.Parse(Encoding.UTF8.GetString(hb))
        Catch
            Return False
        End Try
        Dim meta = TryCast(header("__metadata__"), JObject)
        Dim qm = meta?.Value(Of String)("_quantization_metadata")
        If String.IsNullOrWhiteSpace(qm) Then Return False
        Dim layers As JObject
        Try
            layers = TryCast(JObject.Parse(qm)("layers"), JObject)
        Catch
            Return False
        End Try
        If layers Is Nothing OrElse layers.Count = 0 Then Return False
        ' already has the per-layer tensors (from this fix or the converter): nothing to do
        If layers.Properties().Any(Function(p) header(p.Name & ".comfy_quant") IsNot Nothing) Then Return False
        ' only layers whose weight is in the file
        Dim wanted = layers.Properties().Where(Function(p) header(p.Name & ".weight") IsNot Nothing).ToList()
        If wanted.Count = 0 Then Return False

        ' the new header: the settings move from the metadata into small tensors after the data
        Dim dataEnd As Long = 0
        For Each p In header.Properties()
            If p.Name = "__metadata__" Then Continue For
            Dim o = TryCast(p.Value("data_offsets"), JArray)
            If o IsNot Nothing AndAlso o.Count = 2 Then dataEnd = Math.Max(dataEnd, o(1).Value(Of Long)())
        Next
        Dim newMeta = CType(meta.DeepClone(), JObject)
        newMeta.Remove("_quantization_metadata")
        Dim nh As New JObject()
        If newMeta.Count > 0 Then nh("__metadata__") = newMeta
        For Each p In header.Properties()
            If p.Name <> "__metadata__" Then nh(p.Name) = p.Value
        Next
        Dim payload As New MemoryStream()
        Dim at As New Dictionary(Of String, (B As Long, E As Long))()
        Dim pos = dataEnd
        For Each p In wanted
            Dim cfg = CType(p.Value, JObject)
            Dim json = cfg.ToString(Formatting.None)
            If Not at.ContainsKey(json) Then
                Dim bytes = Encoding.UTF8.GetBytes(json)
                payload.Write(bytes, 0, bytes.Length)
                at(json) = (pos, pos + bytes.Length)
                pos += bytes.Length
            End If
            Dim r = at(json)
            nh(p.Name & ".comfy_quant") = New JObject From {{"dtype", "U8"}, {"shape", New JArray(r.E - r.B)}, {"data_offsets", New JArray(r.B, r.E)}}
        Next
        Dim nb = Encoding.UTF8.GetBytes(nh.ToString(Formatting.None))
        Dim dataStart = 8 + CLng(hb.Length)

        If nb.Length <= hb.Length Then
            ' in place: the data stays where it is, the small tensors go after it
            Dim finalHeader(hb.Length - 1) As Byte
            Array.Fill(finalHeader, CByte(32))
            Array.Copy(nb, finalHeader, nb.Length)
            JObject.Parse(Encoding.UTF8.GetString(finalHeader))     ' sanity check
            KeepOriginalSize(file, originalSize)
            Using fs As New FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.Read)
                fs.SetLength(dataStart + dataEnd)                   ' (anything after the data is not referenced)
                fs.Position = fs.Length
                payload.Position = 0
                payload.CopyTo(fs)
                fs.Flush(True)
                fs.Position = 8
                fs.Write(finalHeader, 0, finalHeader.Length)
                fs.Flush(True)
            End Using
            Return True
        End If

        ' no room: write a copy with the bigger header (8-byte aligned), then swap it in
        progress?.Invoke(Localizer.T("Preparing the model file (once)…"))
        Dim padded = CInt(((nb.Length + 7) \ 8) * 8)
        Dim newHeader(padded - 1) As Byte
        Array.Fill(newHeader, CByte(32))
        Array.Copy(nb, newHeader, nb.Length)
        Dim tmp = file & ".lmfix.tmp"
        KeepOriginalSize(file, originalSize)
        Using src As New FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20)
            Using dst As New FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20)
                dst.Write(BitConverter.GetBytes(CLng(padded)), 0, 8)
                dst.Write(newHeader, 0, newHeader.Length)
                src.Position = dataStart
                Dim buf(8 << 20) As Byte
                Dim left = dataEnd
                While left > 0
                    Dim n = src.Read(buf, 0, CInt(Math.Min(buf.Length, left)))
                    If n <= 0 Then Exit While
                    dst.Write(buf, 0, n)
                    left -= n
                End While
                payload.Position = 0
                payload.CopyTo(dst)
                dst.Flush(True)
            End Using
        End Using
        IO.File.Move(tmp, file, True)
        Return True
    End Function

    ''' <summary>Remembers the downloaded size, so the file still counts as downloaded after the change.</summary>
    Private Sub KeepOriginalSize(file As String, size As Long)
        Dim m = ConvRotFix.MarkerPath(file)
        If Not IO.File.Exists(m) Then IO.File.WriteAllText(m, size.ToString())
    End Sub
End Module
