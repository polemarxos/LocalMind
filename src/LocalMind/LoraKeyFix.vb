Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Text
Imports Newtonsoft.Json
Imports Newtonsoft.Json.Linq

''' <summary>
''' The picture / video engine stops with "LoRA has no diffusion or text-encoder targets" when none of a LoRA's
''' tensor names start the way it expects ("diffusion_model.", "transformer.", "lora_unet_", "text_encoder."…).
''' Trainers that save "base_model.model.blocks…", "model.blocks…" or bare "blocks…" names hit this. Such a LoRA is
''' renamed once (the weights don't change; the original is kept in Loras\_original). A LoRA that still has nothing
''' the engine can use (made for another kind of model) is left out of the run, with a note.
''' </summary>
Public Module LoraKeyFix
    ' names the engine maps to the model or its text encoder (after its own "lora." / "lora_" prefix is taken off)
    Private ReadOnly Known As String() = {"diffusion_model.", "model.diffusion_model.", "unet.", "transformer.", "transformer_blocks", "single_transformer_blocks",
                                          "token_refiner", "text_encoder.", "text_encoders.", "te.", "te1.", "te2.", "te3.", "conditioner.", "clip_vision.",
                                          "clip_l.", "clip_g.", "cond_stage_model.", "unet_", "te_", "te1_", "te2_", "te3_"}
    ' the start of a diffusion-transformer layer name
    Private ReadOnly DitStarts As String() = {"blocks.", "double_blocks.", "single_blocks.", "layers.", "joint_blocks.", "transformer_blocks.", "single_transformer_blocks."}

    Private Function ReadHeader(file As String, ByRef headerLen As Long) As JObject
        Using fs As New FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read)
            If fs.Length < 16 Then Return Nothing
            Dim lenBuf(7) As Byte
            fs.ReadExactly(lenBuf, 0, 8)
            headerLen = BitConverter.ToInt64(lenBuf, 0)
            If headerLen <= 2 OrElse headerLen > 200_000_000 OrElse headerLen > fs.Length - 8 Then Return Nothing
            Dim hb(CInt(headerLen) - 1) As Byte
            fs.ReadExactly(hb, 0, hb.Length)
            Try
                Return JObject.Parse(Encoding.UTF8.GetString(hb))
            Catch
                Return Nothing
            End Try
        End Using
    End Function

    ''' <summary>The name as the engine sees it after taking off its "lora." / "lora_" / "lycoris_" prefix.</summary>
    Private Function Inner(name As String) As String
        For Each p In {"lora.", "lora_", "lycoris_", "lycoris."}
            If name.StartsWith(p, StringComparison.Ordinal) Then Return name.Substring(p.Length)
        Next
        Return name
    End Function

    Private Function Targetable(names As IEnumerable(Of String)) As Boolean
        Return names.Any(Function(n) Known.Any(Function(k) Inner(n).StartsWith(k, StringComparison.Ordinal)))
    End Function

    ''' <summary>The name the engine understands, or Nothing when it isn't a transformer layer.</summary>
    Public Function Renamed(name As String) As String
        Dim n = name
        For Each p In {"base_model.model.", "model.model.", "model."}
            If n.StartsWith(p, StringComparison.Ordinal) AndAlso Not n.StartsWith("model.diffusion_model.", StringComparison.Ordinal) Then n = n.Substring(p.Length) : Exit For
        Next
        If DitStarts.Any(Function(d) n.StartsWith(d, StringComparison.Ordinal)) Then Return "diffusion_model." & n
        Return Nothing
    End Function

    ''' <summary>
    ''' True when the engine can use the LoRA (renamed first when that is all it needs). False: leave it out.
    ''' </summary>
    Public Function Prepare(file As String, Optional progress As Action(Of String) = Nothing) As Boolean
        If Not file.EndsWith(".safetensors", StringComparison.OrdinalIgnoreCase) Then Return True   ' (other formats: the engine decides)
        Dim headerLen As Long
        Dim header As JObject
        Try
            header = ReadHeader(file, headerLen)
        Catch
            Return True
        End Try
        If header Is Nothing Then Return True
        Dim names = header.Properties().Select(Function(p) p.Name).Where(Function(n) n <> "__metadata__").ToList()
        If names.Count = 0 Then Return False
        If Targetable(names) Then Return True
        ' renamable? (most of its layers look like transformer layers)
        Dim mapped = names.Select(Function(n) (Old:=n, [New]:=Renamed(n))).ToList()
        If mapped.Where(Function(m) m.[New] IsNot Nothing).Count() < Math.Max(1, names.Count \ 2) Then Return False
        progress?.Invoke(Localizer.T("Preparing a LoRA for the engine (once)…"))
        Dim nh As New JObject()
        If header("__metadata__") IsNot Nothing Then nh("__metadata__") = header("__metadata__")
        For Each m In mapped
            nh(If(m.[New], m.Old)) = header(m.Old)
        Next
        Dim nb = Encoding.UTF8.GetBytes(nh.ToString(Formatting.None))
        Dim padded = CInt(((nb.Length + 7) \ 8) * 8)
        Dim newHeader(padded - 1) As Byte
        Array.Fill(newHeader, CByte(32))
        Array.Copy(nb, newHeader, nb.Length)
        Dim keep = Path.Combine(Path.GetDirectoryName(file), "_original")
        Directory.CreateDirectory(keep)
        Dim original = Path.Combine(keep, Path.GetFileName(file))
        Dim tmp = file & ".lmfix.tmp"
        Using src As New FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20)
            Using dst As New FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20)
                dst.Write(BitConverter.GetBytes(CLng(padded)), 0, 8)
                dst.Write(newHeader, 0, newHeader.Length)
                src.Position = 8 + headerLen
                src.CopyTo(dst)
            End Using
        End Using
        If Not IO.File.Exists(original) Then IO.File.Move(file, original) Else IO.File.Delete(file)
        IO.File.Move(tmp, file)
        Return True
    End Function
End Module
