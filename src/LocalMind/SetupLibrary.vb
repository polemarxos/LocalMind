Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Net.Http
Imports System.Reflection
Imports System.Threading
Imports System.Threading.Tasks
Imports Newtonsoft.Json

''' <summary>One model (or LoRA) in a recommended setup.</summary>
Public Class SetupItem
    ''' <summary>llm | media | audio | lora</summary>
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
End Class

Public Module SetupLibrary
    Public ReadOnly OnlineBase As String = $"https://raw.githubusercontent.com/{Updater.Repo}/main/setups/"

    ''' <summary>"8gb" for graphics cards under 12 GB (and PCs without one), else "16gb".</summary>
    Public Function TierFor(hw As HardwareInfo) As String
        Return If(hw Is Nothing OrElse hw.VramGB < 12, "8gb", "16gb")
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
        End Select
        Return False
    End Function

    ''' <summary>The edition of a picture/video model for this PC: the named one if it fits, else the largest that fits.</summary>
    Public Function VariantFor(r As MediaRecipe, preferred As String, hw As HardwareInfo) As MediaVariant
        If r Is Nothing OrElse r.Variants.Count = 0 Then Return Nothing
        Dim named = r.Variants.FirstOrDefault(Function(v) String.Equals(v.Label, preferred, StringComparison.OrdinalIgnoreCase))
        If named IsNot Nothing AndAlso MediaStudio.Fit(r, named, hw) = FitLevel.Fits Then Return named
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

    '──────────────── export ────────────────
    ''' <summary>Everything installed here (chat models, picture / video / music / voice models, LoRAs) and the profiles.</summary>
    Public Function ExportInstalled(name As String, hw As HardwareInfo) As SetupFile
        Dim s As New SetupFile With {.Name = name, .Vram = CInt(Math.Round(If(hw?.VramGB, 0))), .Version = 1}
        Dim chat = AppCore.Settings.LastModel
        For Each m In AppCore.Library.All().Where(Function(x) Not String.IsNullOrEmpty(x.Repo))
            s.Items.Add(New SetupItem With {.Kind = "llm", .Repo = m.Repo, .Quant = "Auto",
                                            .Name = m.DisplayName, .MainChat = String.Equals(m.DisplayName, chat, StringComparison.OrdinalIgnoreCase)})
        Next
        For Each r In MediaStudio.Recipes()
            Dim v = MediaStudio.InstalledVariant(r)
            If v Is Nothing Then Continue For
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
        For Each k In {"picture", "edit", "video", "music", "voice"}
            Dim id = ProfileLibrary.DefaultIdFor(k)
            If Not String.IsNullOrEmpty(id) Then s.Defaults(k) = id
        Next
        Return s
    End Function
End Module
