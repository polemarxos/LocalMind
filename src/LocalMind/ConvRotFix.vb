Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Text
Imports System.Text.RegularExpressions
Imports Newtonsoft.Json.Linq

''' <summary>
''' Some INT8 ConvRot exports store only {"format":"int8_tensorwise","convrot":true} per layer.
''' ComfyUI then uses a rotation group size of 256, but stable-diffusion.cpp needs the value written out.
''' This patch adds "convrot_groupsize":256 without rewriting the multi-GB file: the new settings are
''' appended at the end, and only the offsets of the small settings tensors in the header change.
''' The header keeps its exact length (spaces pad the end, as the safetensors format allows).
''' </summary>
Public Module ConvRotFix
    Public Const DefaultGroupSize As Integer = 256

    ''' <summary>Marker next to a patched file: holds the original download size.</summary>
    Public Function MarkerPath(file As String) As String
        Return file & ".lmfix"
    End Function

    ''' <summary>True when the file needed and got the patch, False when it needed nothing.</summary>
    Public Function EnsurePatched(file As String) As Boolean
        If Not IO.File.Exists(file) OrElse Not file.EndsWith(".safetensors", StringComparison.OrdinalIgnoreCase) Then Return False
        Using fs As New FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.Read)
            Dim lenBuf(7) As Byte
            fs.ReadExactly(lenBuf, 0, 8)
            Dim headerLen = BitConverter.ToInt64(lenBuf, 0)
            If headerLen <= 2 OrElse headerLen > 100_000_000 OrElse headerLen > fs.Length - 8 Then Return False
            Dim hb(CInt(headerLen) - 1) As Byte
            fs.ReadExactly(hb, 0, hb.Length)
            Dim header = Encoding.UTF8.GetString(hb)
            Dim dataStart = 8 + headerLen
            Dim root = JObject.Parse(header)

            ' Collect the settings tensors that lack a group size, grouped by their (fixed) JSON text.
            Dim fixes As New Dictionary(Of String, List(Of String))  ' new json → tensor names
            For Each prop In root.Properties()
                If Not prop.Name.EndsWith(".comfy_quant", StringComparison.Ordinal) Then Continue For
                Dim offs = prop.Value("data_offsets")
                Dim b = offs(0).Value(Of Long)(), e = offs(1).Value(Of Long)()
                If e <= b OrElse e - b > 65536 Then Continue For
                fs.Position = dataStart + b
                Dim qb(CInt(e - b) - 1) As Byte
                fs.ReadExactly(qb, 0, qb.Length)
                Dim q As JObject
                Try
                    q = JObject.Parse(Encoding.UTF8.GetString(qb))
                Catch
                    Continue For
                End Try
                If q.Value(Of String)("format") <> "int8_tensorwise" OrElse Not If(q.Value(Of Boolean?)("convrot"), False) OrElse q("convrot_groupsize") IsNot Nothing Then Continue For
                q("convrot_groupsize") = DefaultGroupSize
                Dim nj = q.ToString(Newtonsoft.Json.Formatting.None)
                If Not fixes.ContainsKey(nj) Then fixes(nj) = New List(Of String)
                fixes(nj).Add(prop.Name)
            Next
            If fixes.Count = 0 Then Return False

            ' Plan the appended payloads and rewrite only those header entries, keeping each entry's text layout.
            Dim originalSize = fs.Length
            Dim pos = fs.Length - dataStart
            Dim payload As New MemoryStream()
            Dim newHeader = header
            For Each kv In fixes
                Dim bytes = Encoding.UTF8.GetBytes(kv.Key)
                Dim b = pos, e = pos + bytes.Length
                For Each name In kv.Value
                    Dim rx As New Regex("(""" & Regex.Escape(name) & """\s*:\s*\{)([^{}]*)(\})")
                    Dim m = rx.Match(newHeader)
                    If Not m.Success Then Return False
                    Dim body = Regex.Replace(m.Groups(2).Value, """shape""\s*:\s*\[\s*\d+\s*\]", $"""shape"":[{bytes.Length}]")
                    body = Regex.Replace(body, """data_offsets""\s*:\s*\[\s*\d+\s*,\s*\d+\s*\]", $"""data_offsets"":[{b},{e}]")
                    newHeader = newHeader.Substring(0, m.Groups(2).Index) & body & newHeader.Substring(m.Groups(2).Index + m.Groups(2).Length)
                Next
                payload.Write(bytes, 0, bytes.Length)
                pos = e
            Next

            ' Same header length: trim or add trailing spaces. Give up if there is no room.
            Dim nb = Encoding.UTF8.GetBytes(newHeader.TrimEnd(" "c))
            If nb.Length > hb.Length Then Return False
            Dim finalHeader(hb.Length - 1) As Byte
            Array.Fill(finalHeader, CByte(32))
            Array.Copy(nb, finalHeader, nb.Length)
            JObject.Parse(Encoding.UTF8.GetString(finalHeader)) ' sanity check

            IO.File.WriteAllText(MarkerPath(file), originalSize.ToString())
            fs.Position = fs.Length
            payload.Position = 0
            payload.CopyTo(fs)
            fs.Flush(True)
            fs.Position = 8
            fs.Write(finalHeader, 0, finalHeader.Length)
            fs.Flush(True)
        End Using
        Return True
    End Function

    ''' <summary>The size the file had when it was downloaded (before any patch).</summary>
    Public Function OriginalLength(file As String) As Long
        Dim m = MarkerPath(file)
        Dim n As Long
        If IO.File.Exists(m) AndAlso Long.TryParse(IO.File.ReadAllText(m).Trim(), n) Then Return n
        Return If(IO.File.Exists(file), New FileInfo(file).Length, -1)
    End Function
End Module
