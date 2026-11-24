Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Text
Imports System.Text.RegularExpressions
Imports Newtonsoft.Json.Linq

''' <summary>
''' Repairs tensor shapes the engine rejects ("tensor 'x.weight_scale' has wrong shape in model metadata: got [1, 5376, 1, 1],
''' expected [5376, 1, 1, 1]"). Some int8 / fp8 checkpoints store their scales as [N, 1] where the engine wants [N].
''' The element count is the same, so only the shape in the safetensors header changes: the header keeps its length
''' (padded with spaces) and the file keeps its size.
''' </summary>
Public Module ShapeFix
    Private ReadOnly Wrong As New Regex("tensor '([^']+)' has wrong shape[^:]*: got \[([\d,\s]+)\], expected \[([\d,\s]+)\]", RegexOptions.Compiled)
    Private ReadOnly Component As New Regex("cannot load component from '([^']+)'", RegexOptions.Compiled)

    ''' <summary>Fixes the shapes the engine log complains about. True when a file was changed (worth trying again).</summary>
    Public Function TryFixFromLog(log As String, Optional fallbackFiles As IEnumerable(Of String) = Nothing) As Boolean
        If String.IsNullOrEmpty(log) Then Return False
        Dim wants As New Dictionary(Of String, Long())(StringComparer.Ordinal)
        For Each m As Match In Wrong.Matches(log)
            Dim got = Dims(m.Groups(2).Value), exp = Dims(m.Groups(3).Value)
            If got Is Nothing OrElse exp Is Nothing OrElse Product(got) <> Product(exp) Then Continue For
            wants(m.Groups(1).Value) = TorchShape(exp)
        Next
        If wants.Count = 0 Then Return False
        Dim files As New List(Of String)()
        For Each m As Match In Component.Matches(log)
            files.Add(m.Groups(1).Value)
        Next
        If fallbackFiles IsNot Nothing Then files.AddRange(fallbackFiles)
        Dim changed = False
        For Each f In files.Distinct(StringComparer.OrdinalIgnoreCase)
            Try
                If FixFile(f, wants) > 0 Then changed = True
            Catch ex As IOException
            Catch ex As UnauthorizedAccessException
            End Try
        Next
        Return changed
    End Function

    ''' <summary>Sets the given tensors (engine names) to the given shapes (torch order). Returns how many changed.</summary>
    Public Function FixFile(file As String, wants As Dictionary(Of String, Long())) As Integer
        If Not IO.File.Exists(file) OrElse Not file.EndsWith(".safetensors", StringComparison.OrdinalIgnoreCase) Then Return 0
        Using fs As New FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.Read)
            Dim lenBuf(7) As Byte
            fs.ReadExactly(lenBuf, 0, 8)
            Dim headerLen = BitConverter.ToInt64(lenBuf, 0)
            If headerLen <= 2 OrElse headerLen > 200_000_000 OrElse headerLen > fs.Length - 8 Then Return 0
            Dim hb(CInt(headerLen) - 1) As Byte
            fs.ReadExactly(hb, 0, hb.Length)
            Dim header = Encoding.UTF8.GetString(hb)
            Dim root = JObject.Parse(header)
            Dim names = root.Properties().Select(Function(p) p.Name).Where(Function(n) n <> "__metadata__").ToList()
            Dim newHeader = header
            Dim count = 0
            For Each kv In wants
                Dim key = MatchName(kv.Key, names)
                If key Is Nothing Then Continue For
                Dim cur = root(key)?("shape")?.Select(Function(x) x.Value(Of Long)()).ToArray()
                If cur Is Nothing OrElse Product(cur) <> Product(kv.Value) OrElse cur.SequenceEqual(kv.Value) Then Continue For
                Dim rx As New Regex("(""" & Regex.Escape(key) & """\s*:\s*\{)([^{}]*)(\})")
                Dim m = rx.Match(newHeader)
                If Not m.Success Then Continue For
                Dim body = Regex.Replace(m.Groups(2).Value, """shape""\s*:\s*\[[\d,\s]*\]", """shape"":[" & String.Join(",", kv.Value) & "]")
                newHeader = newHeader.Substring(0, m.Groups(2).Index) & body & newHeader.Substring(m.Groups(2).Index + m.Groups(2).Length)
                count += 1
            Next
            If count = 0 Then Return 0
            Dim nb = Encoding.UTF8.GetBytes(newHeader.TrimEnd(" "c))
            If nb.Length > hb.Length Then Return 0
            Dim finalHeader(hb.Length - 1) As Byte
            Array.Fill(finalHeader, CByte(32))
            Array.Copy(nb, finalHeader, nb.Length)
            JObject.Parse(Encoding.UTF8.GetString(finalHeader)) ' sanity check
            fs.Position = 8
            fs.Write(finalHeader, 0, finalHeader.Length)
            fs.Flush(True)
            Return count
        End Using
    End Function

    ''' <summary>The file's name for an engine tensor name ("model.diffusion_model.blocks.9…" → "blocks.9…" or the same).</summary>
    Private Function MatchName(engineName As String, names As List(Of String)) As String
        Dim exact = names.FirstOrDefault(Function(n) n = engineName)
        If exact IsNot Nothing Then Return exact
        ' the longest file name the engine name ends with, or that ends with the engine name's tail
        Dim best = names.Where(Function(n) engineName.EndsWith("." & n, StringComparison.Ordinal)).OrderByDescending(Function(n) n.Length).FirstOrDefault()
        If best IsNot Nothing Then Return best
        Dim parts = engineName.Split("."c)
        For skip = 1 To Math.Min(3, parts.Length - 2)
            Dim tail = String.Join(".", parts.Skip(skip))
            Dim hit = names.FirstOrDefault(Function(n) n = tail OrElse n.EndsWith("." & tail, StringComparison.Ordinal))
            If hit IsNot Nothing Then Return hit
        Next
        Return Nothing
    End Function

    Private Function Dims(s As String) As Long()
        Try
            Return s.Split(","c).Select(Function(x) Long.Parse(x.Trim())).ToArray()
        Catch
            Return Nothing
        End Try
    End Function

    Private Function Product(d As Long()) As Long
        Dim p As Long = 1
        For Each x In d
            p *= x
        Next
        Return p
    End Function

    ''' <summary>Engine order (ggml, innermost first, padded with 1s) → torch order without the padding.</summary>
    Public Function TorchShape(ggml As Long()) As Long()
        Dim t = ggml.Reverse().SkipWhile(Function(x) x = 1).ToArray()
        Return If(t.Length = 0, New Long() {1}, t)
    End Function
End Module
