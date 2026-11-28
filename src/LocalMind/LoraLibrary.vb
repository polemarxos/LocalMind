Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Text
Imports Newtonsoft.Json
Imports Newtonsoft.Json.Linq

''' <summary>One of the user's LoRA files with its settings.</summary>
Public Class UserLora
    ''' <summary>File name without extension (what sd.cpp uses in &lt;lora:NAME:W&gt;).</summary>
    Public Property Name As String
    Public Property Extension As String = ".safetensors"
    ''' <summary>Model family it was trained for (e.g. "SDXL", "FLUX.1", "Wan"); empty = any.</summary>
    Public Property Family As String = ""
    Public Property Triggers As String = ""
    Public Property Weight As Double = 1.0
    ''' <summary>Turned on in the studio (per family).</summary>
    Public Property Enabled As Boolean
    ''' <summary>Where it came from (e.g. "Civitai #12345") and the exact base model it was trained on.</summary>
    Public Property Source As String
    Public Property BaseModel As String
    ''' <summary>Where the file was downloaded from (so a setup can download it again on another PC).</summary>
    Public Property Url As String
    ''' <summary>Its Civitai page, when known.</summary>
    Public Property PageUrl As String
    ''' <summary>Already looked up on Civitai by its file hash (found or not).</summary>
    Public Property LookedUp As Boolean

    <JsonIgnore> Public ReadOnly Property FullPath As String
        Get
            Return Path.Combine(LoraLibrary.Folder, Name & Extension)
        End Get
    End Property

    Public Overrides Function ToString() As String
        Return Name
    End Function
End Class

''' <summary>
''' Your LoRAs in Models\Loras: drop .safetensors files there (or use Add). Trigger words and the model family
''' are read from the file's own metadata when present and can be edited. Built-in speed LoRAs ("speed-…")
''' live in the same folder but are not listed.
''' </summary>
Public Module LoraLibrary
    Private ReadOnly _lock As New Object()
    Private _items As List(Of UserLora)

    Public Const SpeedPrefix As String = "speed-"
    ''' <summary>LoRAs that are part of a built-in model (e.g. the uncensored Wan 2.2); hidden like speed LoRAs.</summary>
    Public Const BuiltInPrefix As String = "builtin-"

    Public ReadOnly Property Folder As String
        Get
            Dim d = Path.Combine(AppCore.Library.Folder, "Loras")
            Directory.CreateDirectory(d)
            Return d
        End Get
    End Property

    Private ReadOnly Property StoreFile As String
        Get
            Return Path.Combine(AppPaths.Roaming, "loras.json")
        End Get
    End Property

    ''' <summary>All LoRA files in the folder, with saved settings; new files get metadata read automatically.</summary>
    Public Function All() As List(Of UserLora)
        SyncLock _lock
            If _items Is Nothing Then
                Try
                    _items = If(File.Exists(StoreFile), JsonConvert.DeserializeObject(Of List(Of UserLora))(File.ReadAllText(StoreFile)), Nothing)
                Catch
                End Try
                If _items Is Nothing Then _items = New List(Of UserLora)()
            End If
            Dim files = Directory.EnumerateFiles(Folder).Where(Function(f) f.EndsWith(".safetensors", StringComparison.OrdinalIgnoreCase) OrElse
                                                                         f.EndsWith(".ckpt", StringComparison.OrdinalIgnoreCase) OrElse
                                                                         f.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase)).
                                                         Where(Function(f) Not Path.GetFileName(f).StartsWith(SpeedPrefix, StringComparison.OrdinalIgnoreCase) AndAlso
                                                                       Not Path.GetFileName(f).StartsWith(BuiltInPrefix, StringComparison.OrdinalIgnoreCase)).ToList()
            Dim changed = False
            For Each f In files
                Dim n = Path.GetFileNameWithoutExtension(f)
                If _items.Any(Function(x) x.Name.Equals(n, StringComparison.OrdinalIgnoreCase)) Then Continue For
                Dim meta = ReadMetadata(f)
                _items.Add(New UserLora With {.Name = n, .Extension = Path.GetExtension(f), .Family = meta.Family, .Triggers = meta.Triggers})
                changed = True
            Next
            Dim gone = _items.RemoveAll(Function(x) Not File.Exists(x.FullPath))
            If changed OrElse gone > 0 Then SaveLocked()
            Return _items.OrderBy(Function(x) x.Name, StringComparer.OrdinalIgnoreCase).ToList()
        End SyncLock
    End Function

    ''' <summary>LoRAs that fit a model family (or have no family set).</summary>
    Public Function ForFamily(family As String) As List(Of UserLora)
        Return All().Where(Function(x) String.IsNullOrEmpty(x.Family) OrElse FamilyMatches(x.Family, family)).ToList()
    End Function

    Public Function FamilyMatches(loraFamily As String, recipeFamily As String) As Boolean
        If String.IsNullOrEmpty(loraFamily) OrElse String.IsNullOrEmpty(recipeFamily) Then Return True
        Dim a = loraFamily.ToLowerInvariant(), b = recipeFamily.ToLowerInvariant()
        ' Edit / VACE variants accept LoRAs of their base family.
        Return a = b OrElse b.StartsWith(a & "-") OrElse a.StartsWith(b & "-")
    End Function

    Public Sub Save()
        SyncLock _lock
            SaveLocked()
        End SyncLock
    End Sub

    Private Sub SaveLocked()
        Try
            File.WriteAllText(StoreFile, JsonConvert.SerializeObject(_items, Formatting.Indented))
        Catch
        End Try
    End Sub

    ''' <summary>Copies a LoRA file into the folder and returns its entry.</summary>
    Public Function Import(file As String) As UserLora
        Dim target = Path.Combine(Folder, Path.GetFileName(file))
        If Not String.Equals(Path.GetFullPath(file), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase) Then IO.File.Copy(file, target, True)
        Return All().FirstOrDefault(Function(x) x.Name.Equals(Path.GetFileNameWithoutExtension(target), StringComparison.OrdinalIgnoreCase))
    End Function

    Public Sub Delete(l As UserLora)
        SyncLock _lock
            Try : File.Delete(l.FullPath) : Catch : End Try
            _items?.RemoveAll(Function(x) x.Name = l.Name)
            SaveLocked()
        End SyncLock
    End Sub

    '──────────────── metadata ────────────────
    ''' <summary>Reads trigger words and the base model family from a safetensors header (kohya / modelspec keys).</summary>
    Public Function ReadMetadata(file As String) As (Family As String, Triggers As String)
        Dim family = GuessFamilyFromName(Path.GetFileNameWithoutExtension(file))
        Dim triggers = ""
        Try
            If Not file.EndsWith(".safetensors", StringComparison.OrdinalIgnoreCase) Then Return (family, triggers)
            Using fs As New FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read)
                Dim lb(7) As Byte
                fs.ReadExactly(lb, 0, 8)
                Dim n = BitConverter.ToInt64(lb, 0)
                If n <= 2 OrElse n > 50_000_000 Then Return (family, triggers)
                Dim hb(CInt(n) - 1) As Byte
                fs.ReadExactly(hb, 0, hb.Length)
                Dim meta = TryCast(JObject.Parse(Encoding.UTF8.GetString(hb))("__metadata__"), JObject)
                If meta Is Nothing Then Return (family, triggers)
                For Each k In {"modelspec.trigger_phrase", "trigger_phrase", "trigger_words", "trigger_word", "ss_trigger_words", "activation_text"}
                    Dim t = meta.Value(Of String)(k)
                    If Not String.IsNullOrWhiteSpace(t) Then triggers = t.Trim() : Exit For
                Next
                If triggers = "" Then
                    Dim freq = meta.Value(Of String)("ss_tag_frequency")
                    If Not String.IsNullOrEmpty(freq) Then
                        Dim counts As New Dictionary(Of String, Integer)(StringComparer.OrdinalIgnoreCase)
                        For Each ds In JObject.Parse(freq).Properties()
                            For Each tag In CType(ds.Value, JObject).Properties()
                                Dim key = tag.Name.Trim()
                                counts(key) = If(counts.ContainsKey(key), counts(key), 0) + tag.Value.Value(Of Integer)()
                            Next
                        Next
                        triggers = String.Join(", ", counts.OrderByDescending(Function(x) x.Value).Take(3).Select(Function(x) x.Key))
                    End If
                End If
                Dim arch = (If(meta.Value(Of String)("modelspec.architecture"), "") & " " & If(meta.Value(Of String)("ss_base_model_version"), "") & " " &
                            If(meta.Value(Of String)("ss_sd_model_name"), "")).ToLowerInvariant()
                Dim fromMeta = GuessFamilyFromName(arch)
                If fromMeta <> "" Then family = fromMeta
            End Using
        Catch
        End Try
        Return (family, triggers)
    End Function

    Private Function GuessFamilyFromName(s As String) As String
        Dim t = s.ToLowerInvariant()
        If t.Contains("kontext") Then Return "FLUX.1-Kontext"
        If t.Contains("flux.2") OrElse t.Contains("flux2") OrElse t.Contains("klein") Then Return "FLUX.2"
        If t.Contains("flux") Then Return "FLUX.1"
        If t.Contains("qwen") AndAlso t.Contains("edit") Then Return "Qwen-Image-Edit"
        If t.Contains("qwen") Then Return "Qwen-Image"
        If t.Contains("z-image") OrElse t.Contains("z_image") OrElse t.Contains("zimage") Then Return "Z-Image"
        If t.Contains("wan") Then Return "Wan"
        If t.Contains("ltx") Then Return "LTX"
        If t.Contains("anima") Then Return "Anima"
        If t.Contains("krea") Then Return "Krea"
        If t.Contains("sdxl") OrElse t.Contains("sd_xl") OrElse t.Contains("pony") OrElse t.Contains("illustrious") OrElse t.Contains("noobai") Then Return "SDXL"
        If t.Contains("sd1") OrElse t.Contains("sd 1.5") OrElse t.Contains("sd15") OrElse t.Contains("v1-5") OrElse t.Contains("sd_v1") Then Return "SD1.5"
        Return ""
    End Function

    Public ReadOnly Property Families As String()
        Get
            Return {"", "Z-Image", "FLUX.1", "FLUX.1-Kontext", "FLUX.2", "Qwen-Image", "Qwen-Image-Edit", "Krea", "Anima", "SDXL", "SD1.5", "Wan", "LTX"}
        End Get
    End Property
End Module
