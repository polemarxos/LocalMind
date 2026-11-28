Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Net.Http
Imports System.Reflection
Imports System.Threading
Imports System.Threading.Tasks
Imports Newtonsoft.Json
Imports Newtonsoft.Json.Linq

''' <summary>One model (or LoRA) in a recommended setup.</summary>
Public Class SetupItem
    ''' <summary>llm | media | audio | lora | civitai</summary>
    Public Property Kind As String = ""
    ''' <summary>media / audio: the recipe or audio model id.</summary>
    Public Property Id As String = ""
    ''' <summary>llm: the Hugging Face repo.</summary>
    Public Property Repo As String = ""
    ''' <summary>llm: the quantization ("Auto" = the best one for this PC).</summary>
    Public Property Quant As String = "Auto"
    ''' <summary>media: the preferred edition (empty or not fitting = the best one for this PC's graphics card).</summary>
    Public Property [Variant] As String = ""
    ''' <summary>Shown to the user.</summary>
    Public Property Name As String = ""
    Public Property About As String = ""
    ''' <summary>lora: download link, file name, family and trigger words.</summary>
    Public Property Url As String = ""
    Public Property FileName As String = ""
    Public Property Family As String = ""
    Public Property Triggers As String = ""
    ''' <summary>llm: the chat model selected after download.</summary>
    Public Property MainChat As Boolean
    ''' <summary>Ticked in the list by default.</summary>
    Public Property Recommended As Boolean = True
    ''' <summary>civitai: the Civitai model version to download (a checkpoint; FileName picks the file).</summary>
    Public Property VersionId As Long
    ''' <summary>civitai: the file's size and Civitai's base model (for the size shown before downloading).</summary>
    Public Property Size As Long
    Public Property BaseModel As String = ""
    ''' <summary>Used instead of this item on PCs without an NVIDIA graphics card (AMD, Intel): e.g. a GGUF edition
    ''' where this one is INT8 ConvRot, which only NVIDIA cards run fast. Profiles follow the swap.</summary>
    Public Property NotNvidia As SetupItem
    ''' <summary>Used instead of this item when the user picks Standard content (an official model with no explicit
    ''' content) — this item is the uncensored one. Can have its own NotNvidia edition.</summary>
    Public Property Standard As SetupItem
End Class

''' <summary>
''' A ready-made set of models and profiles for a kind of PC (8 GB or 16 GB graphics card). The first start offers it,
''' and newer versions published in the repository (setups/8gb.json, setups/16gb.json) are offered again.
''' "Export my setup" writes the models and profiles installed on this PC in the same format.
''' </summary>
Public Class SetupFile
    Public Property Name As String = ""
    ''' <summary>Graphics memory it is made for, in GB.</summary>
    Public Property Vram As Integer = 16
    ''' <summary>Raised when the list changes; a newer version is offered to users who installed an older one.</summary>
    Public Property Version As Integer = 1
    Public Property Items As New List(Of SetupItem)()
    Public Property Profiles As New List(Of MediaProfile)()
    ''' <summary>Profile id used by the chat for each kind (picture, edit, video, music, voice).</summary>
    Public Property Defaults As New Dictionary(Of String, String)()
    ''' <summary>App settings to use (only the ones in SetupLibrary.SharedSettings; never keys, PINs or folders).</summary>
    Public Property Settings As JObject
    ''' <summary>Your own personas.</summary>
    Public Property Personas As New List(Of Persona)()
    ''' <summary>The profiles and chat defaults used instead when the user picks Standard content (empty = the same ones).</summary>
    Public Property StandardProfiles As New List(Of MediaProfile)()
    Public Property StandardDefaults As New Dictionary(Of String, String)()
End Class

Public Module SetupLibrary
    Public ReadOnly OnlineBase As String = $"https://raw.githubusercontent.com/{Updater.Repo}/main/setups/"

    ''' <summary>"8gb" for graphics cards under 10 GB (and PCs without one or with under 24 GB of RAM), "12gb" for
    ''' 10–14 GB, else "16gb".</summary>
    Public Function TierFor(hw As HardwareInfo) As String
        ' (the 16 GB setup's video model also needs about 32 GB of RAM)
        If hw Is Nothing OrElse hw.VramGB < 10 OrElse (hw.RamBytes > 0 AndAlso hw.RamBytes < 24L * 1024 * 1024 * 1024) Then Return "8gb"
        Return If(hw.VramGB < 14, "12gb", "16gb")
    End Function

    ''' <summary>The setup for a tier: the published one when it can be read, else the one built into LocalMind.</summary>
    Public Async Function LoadAsync(tier As String, ct As CancellationToken) As Task(Of SetupFile)
        Try
            Using cts = CancellationTokenSource.CreateLinkedTokenSource(ct)
                cts.CancelAfter(TimeSpan.FromSeconds(10))
                Dim json = Await Downloader.Http.GetStringAsync(OnlineBase & tier & ".json", cts.Token)
                Dim s = JsonConvert.DeserializeObject(Of SetupFile)(json)
                If s IsNot Nothing AndAlso s.Items.Count > 0 Then Return s
            End Using
        Catch ex As OperationCanceledException When Not ct.IsCancellationRequested
        Catch ex As HttpRequestException
        Catch ex As JsonException
        End Try
        Return BuiltIn(tier)
    End Function

    Public Function BuiltIn(tier As String) As SetupFile
        Try
            Using st = Assembly.GetExecutingAssembly().GetManifestResourceStream("setup-" & tier & ".json")
                If st Is Nothing Then Return Nothing
                Using r As New StreamReader(st)
                    Return JsonConvert.DeserializeObject(Of SetupFile)(r.ReadToEnd())
                End Using
            End Using
        Catch
            Return Nothing
        End Try
    End Function

    Public Function Parse(json As String) As SetupFile
        Return JsonConvert.DeserializeObject(Of SetupFile)(json)
    End Function

    Public Function ToJson(s As SetupFile) As String
        Return JsonConvert.SerializeObject(s, Formatting.Indented,
                                           New JsonSerializerSettings With {.DefaultValueHandling = DefaultValueHandling.Ignore})
    End Function

    '──────────────── what is installed ────────────────
    Public Function IsInstalled(it As SetupItem) As Boolean
        Select Case it.Kind
            Case "llm"
                Return AppCore.Library.All().Any(Function(m) String.Equals(m.Repo, it.Repo, StringComparison.OrdinalIgnoreCase))
            Case "media"
                Dim r = MediaStudio.Recipes().FirstOrDefault(Function(x) x.Id = it.Id)
                Return r IsNot Nothing AndAlso MediaStudio.InstalledVariant(r) IsNot Nothing
            Case "audio"
                Dim m = AudioStudio.Models().FirstOrDefault(Function(x) x.Id = it.Id)
                Return m IsNot Nothing AndAlso AudioStudio.IsInstalled(m)
            Case "lora"
                Return LoraLibrary.All().Any(Function(l) String.Equals(l.Name, Path.GetFileNameWithoutExtension(it.FileName), StringComparison.OrdinalIgnoreCase))
            Case "civitai"
                Dim r = MediaStudio.Recipes().FirstOrDefault(Function(x) x.Id = it.Id)
                If r IsNot Nothing AndAlso MediaStudio.InstalledVariant(r) IsNot Nothing Then Return True
                ' (the same file installed under another id)
                Return Not String.IsNullOrEmpty(it.FileName) AndAlso MediaStudio.LoadUserModels().Any(Function(u) String.Equals(u.File, it.FileName, StringComparison.OrdinalIgnoreCase) AndAlso
                                                                                                       File.Exists(Path.Combine(MediaStudio.UserModelFolder, u.File)))
        End Select
        Return False
    End Function

    ''' <summary>
    ''' The setup as it fits this PC and the content choice: with Standard, items that have a Standard edition (made by
    ''' the official makers, no explicit content) use it; then, on a PC without an NVIDIA card, items that have a
    ''' NotNvidia edition use it. The profiles that used a swapped model use the new one.
    ''' </summary>
    Public Function ForThisPc(s As SetupFile, hw As HardwareInfo, Optional uncensored As Boolean = True) As SetupFile
        If s Is Nothing Then Return Nothing
        Dim nvidia = hw IsNot Nothing AndAlso hw.Gpus.Any(Function(g) g.Vendor = GpuVendor.Nvidia)
        Dim needStd = Not uncensored AndAlso (s.Items.Any(Function(i) i.Standard IsNot Nothing) OrElse s.StandardProfiles.Count > 0)
        Dim needAlt = Not nvidia AndAlso s.Items.Any(Function(i) i.NotNvidia IsNot Nothing OrElse (i.Standard?.NotNvidia IsNot Nothing))
        If Not needStd AndAlso Not needAlt Then Return s
        Dim c = Parse(ToJson(s))
        If needStd Then
            Swap(c, Function(i) i.Standard)
            ' Standard has profiles of its own
            If c.StandardProfiles.Count > 0 Then
                c.Profiles = c.StandardProfiles
                If c.StandardDefaults.Count > 0 Then c.Defaults = c.StandardDefaults
            End If
        End If
        If Not nvidia Then Swap(c, Function(i) i.NotNvidia)
        Return c
    End Function

    Private Sub Swap(c As SetupFile, pick As Func(Of SetupItem, SetupItem))
        Dim swapped As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
        For i = 0 To c.Items.Count - 1
            Dim alt = pick(c.Items(i))
            If alt Is Nothing Then Continue For
            alt.Recommended = c.Items(i).Recommended
            If c.Items(i).Kind = "llm" Then alt.MainChat = c.Items(i).MainChat
            Dim oldKey = If(c.Items(i).Kind = "llm", c.Items(i).Repo, c.Items(i).Id)
            Dim newKey = If(alt.Kind = "llm", alt.Repo, alt.Id)
            If Not String.IsNullOrEmpty(oldKey) AndAlso Not String.IsNullOrEmpty(newKey) AndAlso oldKey <> newKey Then swapped(oldKey) = newKey
            c.Items(i) = alt
        Next
        For Each p In c.Profiles
            Dim n As String = Nothing
            If p.Model IsNot Nothing AndAlso swapped.TryGetValue(p.Model, n) Then p.Model = n
        Next
    End Sub


    ''' <summary>The edition of a picture/video model for this PC: the named one if it fits, else the largest that fits.</summary>
    Public Function VariantFor(r As MediaRecipe, preferred As String, hw As HardwareInfo) As MediaVariant
        If r Is Nothing OrElse r.Variants.Count = 0 Then Return Nothing
        Dim named = r.Variants.FirstOrDefault(Function(v) String.Equals(v.Label, preferred, StringComparison.OrdinalIgnoreCase))
        ' (the edition the setup names, unless this PC can't run it at all)
        If named IsNot Nothing AndAlso MediaStudio.Fit(r, named, hw) <> FitLevel.TooBig Then Return named
        Dim fits = r.Variants.Where(Function(v) MediaStudio.Fit(r, v, hw) = FitLevel.Fits).OrderByDescending(Function(v) v.Total).FirstOrDefault()
        If fits IsNot Nothing Then Return fits
        ' nothing fits on the card: the smallest (it runs with part of it in RAM)
        Return If(named, r.Variants.OrderBy(Function(v) v.Total).First())
    End Function

    ''' <summary>Download size of an item on this PC (0 when unknown, e.g. chat models before their files are listed).</summary>
    Public Function SizeOf(it As SetupItem, hw As HardwareInfo) As Long
        Select Case it.Kind
            Case "media"
                Dim r = MediaStudio.Recipes().FirstOrDefault(Function(x) x.Id = it.Id)
                Dim v = VariantFor(r, it.Variant, hw)
                Return If(v Is Nothing, 0, MediaStudio.BytesToDownload(r, v, True))
            Case "audio"
                Dim m = AudioStudio.Models().FirstOrDefault(Function(x) x.Id = it.Id)
                Return If(m Is Nothing, 0, m.Size)
            Case "civitai"
                ' the file, plus what its family needs (VAE, text encoder) when not downloaded yet
                Dim g = Civitai.GroupFor(If(it.BaseModel, ""))
                Dim t = If(g.Template Is Nothing, Nothing, MediaStudio.Recipes().FirstOrDefault(Function(x) x.Id = g.Template))
                Dim parts = If(t Is Nothing, 0L, t.Parts.Where(Function(p) Not p.Control AndAlso Not MediaStudio.IsPresent(p)).Sum(Function(p) p.Size))
                Return it.Size + parts
        End Select
        Return 0
    End Function

    ''' <summary>Adds the setup's profiles (replacing ones with the same id) and makes its defaults the chat's.</summary>
    Public Sub ApplyProfiles(s As SetupFile, Optional overwriteDefaults As Boolean = True)
        If s Is Nothing Then Return
        Dim list = ProfileLibrary.All()
        For Each p In s.Profiles
            list.RemoveAll(Function(x) x.Id = p.Id)
            list.Add(p)
        Next
        ProfileLibrary.Save(list)
        For Each kv In s.Defaults
            If overwriteDefaults OrElse String.IsNullOrEmpty(ProfileLibrary.DefaultIdFor(kv.Key)) Then ProfileLibrary.SetDefault(kv.Key, kv.Value)
        Next
    End Sub

    ''' + "'''" + ''' <summary>
    ''' + "'''" + ''' Settings a setup carries: how the chat, engine, pictures and voice behave. Personal things (API keys, PINs,
    ''' + "'''" + ''' folders, language, theme, window and update choices, the GPU backend) stay on each PC.
    ''' + "'''" + ''' </summary>
    Public ReadOnly SharedSettings As String() = {
        "ShowThinking", "HistoryMessages", "KeepImagesInLastMessages", "MaxTokens", "TopP", "DefaultTemperature",
        "ContextSize", "AutoContext", "MinAutoContext", "GpuLayers", "ParallelSlots", "KvCacheType", "ExtraEngineArgs", "UnloadAfterIdleMinutes",
        "ImageGpuSharing", "ImproveImagePrompt", "EnableImages", "EnableAudio", "MediaFast", "LowRamMode", "AgentTools",
        "ShowNsfw", "OnlyFitGpu", "UseDocuments", "UseWeb", "WebSearchProvider", "WebPagesToRead", "VoiceModel", "VoiceLanguage",
        "ReadAloudModel", "ReadAloudVoice", "AutoReadAnswers", "SpeedUpMode", "DownloadSpeedUpFiles", "CivitaiRed"}

    Public Function ExportSettings() As JObject
        Dim all = JObject.FromObject(AppCore.Settings)
        Dim o As New JObject()
        For Each k In SharedSettings
            If all(k) IsNot Nothing Then o(k) = all(k)
        Next
        Return o
    End Function

    ''' + "'''" + ''' <summary>Applies a setup's settings and personas (personas with the same name are replaced).</summary>
    Public Sub ApplySettings(s As SetupFile)
        If s Is Nothing Then Return
        If s.Settings IsNot Nothing Then
            Dim o As New JObject()
            For Each k In SharedSettings
                If s.Settings(k) IsNot Nothing Then o(k) = s.Settings(k)
            Next
            JsonConvert.PopulateObject(o.ToString(), AppCore.Settings)
            AppCore.SaveSettings()
        End If
        If s.Personas IsNot Nothing AndAlso s.Personas.Count > 0 Then
            Dim list = AppCore.Personas.Load()
            For Each p In s.Personas
                list.RemoveAll(Function(x) String.Equals(x.Name, p.Name, StringComparison.OrdinalIgnoreCase))
                list.Add(p)
            Next
            AppCore.Personas.Save(list)
        End If
    End Sub

    '──────────────── export ────────────────
    ''' <summary>Everything installed here (chat models, picture / video / music / voice models, LoRAs) and the profiles.</summary>
    ''' + "'''" + ''' <summary>LoRAs that can't go in a setup: added from a file, so there is no link to download them from.</summary>
    Public Function LorasWithoutLink() As List(Of String)
        Return LoraLibrary.All().Where(Function(x) String.IsNullOrEmpty(x.Url)).Select(Function(x) x.Name).ToList()
    End Function

    Public Function ExportInstalled(name As String, hw As HardwareInfo) As SetupFile
        Dim s As New SetupFile With {.Name = name, .Vram = CInt(Math.Round(If(hw?.VramGB, 0))), .Version = 1}
        Dim chat = AppCore.Settings.LastModel
        For Each m In AppCore.Library.All().Where(Function(x) Not String.IsNullOrEmpty(x.Repo))
            s.Items.Add(New SetupItem With {.Kind = "llm", .Repo = m.Repo, .Quant = "Auto",
                                            .Name = m.DisplayName, .MainChat = String.Equals(m.DisplayName, chat, StringComparison.OrdinalIgnoreCase)})
        Next
        Dim users = MediaStudio.LoadUserModels()
        For Each r In MediaStudio.Recipes()
            Dim v = MediaStudio.InstalledVariant(r)
            If v Is Nothing Then Continue For
            ' your own checkpoints (from Civitai): downloaded again from their version
            Dim um = users.FirstOrDefault(Function(u) u.Id = r.Id)
            If um IsNot Nothing AndAlso um.VersionId > 0 Then
                s.Items.Add(New SetupItem With {.Kind = "civitai", .Id = r.Id, .VersionId = um.VersionId, .FileName = um.File, .Name = r.Name,
                                                .Size = um.Size, .BaseModel = um.BaseModel})
                Continue For
            End If
            s.Items.Add(New SetupItem With {.Kind = "media", .Id = r.Id, .Variant = v.Label, .Name = r.Name})
        Next
        For Each m In AudioStudio.Models().Where(AddressOf AudioStudio.IsInstalled)
            s.Items.Add(New SetupItem With {.Kind = "audio", .Id = m.Id, .Name = m.Name})
        Next
        For Each l In LoraLibrary.All().Where(Function(x) Not String.IsNullOrEmpty(x.Url))
            s.Items.Add(New SetupItem With {.Kind = "lora", .Url = l.Url, .FileName = l.Name & l.Extension, .Family = l.Family,
                                            .Triggers = l.Triggers, .Name = l.Name})
        Next
        s.Profiles = ProfileLibrary.All()
        s.Settings = ExportSettings()
        Dim builtIn = PersonaService.BuiltIns().Select(Function(b) b.Id).ToHashSet()
        s.Personas = AppCore.Personas.Load().Where(Function(p) Not builtIn.Contains(p.Id)).ToList()
        For Each k In {"picture", "edit", "video", "music", "voice"}
            Dim id = ProfileLibrary.DefaultIdFor(k)
            If Not String.IsNullOrEmpty(id) Then s.Defaults(k) = id
        Next
        Return s
    End Function
End Module
