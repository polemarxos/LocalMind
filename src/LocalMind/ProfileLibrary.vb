Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports Newtonsoft.Json

''' <summary>A LoRA in a profile, with its own strength.</summary>
Public Class ProfileLora
    Public Property Name As String = ""
    Public Property Weight As Double = 1.0
End Class

''' <summary>
''' A ready set-up for one downloaded picture or video model: its LoRAs (with trigger words) and the prompts that are
''' always added. The chat uses the default picture profile and the default video profile.
''' </summary>
Public Class MediaProfile
    Public Property Id As String = Guid.NewGuid().ToString("N").Substring(0, 10)
    Public Property Name As String = ""
    ''' <summary>picture | video | edit (changes a picture: an attached one or one made in the chat)</summary>
    Public Property Kind As String = "picture"
    ''' <summary>The model (recipe id).</summary>
    Public Property Model As String = ""
    Public Property Loras As New List(Of ProfileLora)()
    ''' <summary>Trigger words added to every prompt (the LoRAs' own ones are added too).</summary>
    Public Property Triggers As String = ""
    ''' <summary>Used when the chat gives no description ("make a picture").</summary>
    Public Property DefaultPrompt As String = ""
    ''' <summary>Always added to the prompt (style, quality words).</summary>
    Public Property Positive As String = ""
    ''' <summary>What to avoid; empty = the model's own.</summary>
    Public Property Negative As String = ""
    ''' <summary>Use the model's speed LoRA (fewer steps) when it has one.</summary>
    Public Property Fast As Boolean = True
    ''' <summary>Voice profiles: the saved voice to speak with when no recording is attached.</summary>
    Public Property Voice As String = ""

    ''' <summary>Picture, edit and video profiles (they have LoRAs and prompts).</summary>
    <JsonIgnore> Public ReadOnly Property IsVisual As Boolean
        Get
            Return Kind = "picture" OrElse Kind = "edit" OrElse Kind = "video"
        End Get
    End Property

    <JsonIgnore> Public ReadOnly Property IsEdit As Boolean
        Get
            Return String.Equals(Kind, "edit", StringComparison.OrdinalIgnoreCase)
        End Get
    End Property

    <JsonIgnore> Public ReadOnly Property IsVideo As Boolean
        Get
            Return String.Equals(Kind, "video", StringComparison.OrdinalIgnoreCase)
        End Get
    End Property

    Public Overrides Function ToString() As String
        Return Name
    End Function
End Class

Public Module ProfileLibrary
    Private ReadOnly _lock As New Object()

    Private ReadOnly Property StoreFile As String
        Get
            Return Path.Combine(AppPaths.Roaming, "profiles.json")
        End Get
    End Property

    Public Function All() As List(Of MediaProfile)
        SyncLock _lock
            Try
                If File.Exists(StoreFile) Then
                    Return If(JsonConvert.DeserializeObject(Of List(Of MediaProfile))(File.ReadAllText(StoreFile)), New List(Of MediaProfile)())
                End If
            Catch
            End Try
            Return New List(Of MediaProfile)()
        End SyncLock
    End Function

    Public Sub Save(list As List(Of MediaProfile))
        SyncLock _lock
            Directory.CreateDirectory(Path.GetDirectoryName(StoreFile))
            File.WriteAllText(StoreFile, JsonConvert.SerializeObject(list, Formatting.Indented))
        End SyncLock
    End Sub

    Public Function Find(idOrName As String) As MediaProfile
        If String.IsNullOrWhiteSpace(idOrName) Then Return Nothing
        Dim list = All()
        Return If(list.FirstOrDefault(Function(p) p.Id = idOrName),
                  list.FirstOrDefault(Function(p) String.Equals(p.Name, idOrName, StringComparison.OrdinalIgnoreCase)))
    End Function

    ''' <summary>The default picture or video profile for the chat, when its model is still downloaded.</summary>
    Public Function ForChat(video As Boolean) As MediaProfile
        Dim p = Find(If(video, AppCore.Settings.ChatVideoProfile, AppCore.Settings.ChatImageProfile))
        If p Is Nothing OrElse p.IsVideo <> video OrElse p.IsEdit Then Return Nothing
        Dim r = RecipeOf(p)
        If r Is Nothing OrElse MediaStudio.UsableVariant(r) Is Nothing Then Return Nothing
        Return p
    End Function

    ''' <summary>The default edit profile for the chat ("change the sky in that picture"), when its model is downloaded.</summary>
    Public Function ForChatEdit() As MediaProfile
        Dim p = Find(AppCore.Settings.ChatEditProfile)
        If p Is Nothing OrElse Not p.IsEdit Then Return Nothing
        Dim r = RecipeOf(p)
        If r Is Nothing OrElse MediaStudio.UsableVariant(r) Is Nothing Then Return Nothing
        Return p
    End Function

    ''' <summary>The chat default setting that belongs to a profile kind.</summary>
    Public Function DefaultIdFor(kind As String) As String
        Select Case kind
            Case "video" : Return AppCore.Settings.ChatVideoProfile
            Case "edit" : Return AppCore.Settings.ChatEditProfile
            Case "music" : Return AppCore.Settings.ChatMusicProfile
            Case "voice" : Return AppCore.Settings.ChatVoiceProfile
            Case Else : Return AppCore.Settings.ChatImageProfile
        End Select
    End Function

    Public Sub SetDefault(kind As String, id As String)
        Select Case kind
            Case "video" : AppCore.Settings.ChatVideoProfile = id
            Case "edit" : AppCore.Settings.ChatEditProfile = id
            Case "music" : AppCore.Settings.ChatMusicProfile = id
            Case "voice" : AppCore.Settings.ChatVoiceProfile = id
            Case Else : AppCore.Settings.ChatImageProfile = id
        End Select
        AppCore.SaveSettings()
    End Sub

    ''' + "'''" + ''' <summary>The chat's music or voice profile, when its model is downloaded.</summary>
    Public Function ForChatAudio(kind As String) As MediaProfile
        Dim p = Find(DefaultIdFor(kind))
        If p Is Nothing OrElse p.Kind <> kind Then Return Nothing
        Dim m = AudioStudio.Models().FirstOrDefault(Function(x) x.Id = p.Model)
        If m Is Nothing OrElse Not AudioStudio.IsInstalled(m) Then Return Nothing
        Return p
    End Function

    Public Function RecipeOf(p As MediaProfile) As MediaRecipe
        If p Is Nothing Then Return Nothing
        Return MediaStudio.Recipes().FirstOrDefault(Function(r) r.Id = p.Model)
    End Function

    ''' <summary>The profile's LoRAs as files (missing ones are skipped), each with the profile's strength.</summary>
    Public Function LorasOf(p As MediaProfile) As List(Of UserLora)
        Dim mine = LoraLibrary.All()
        Dim list As New List(Of UserLora)()
        If p Is Nothing Then Return list
        For Each pl In p.Loras
            Dim u = mine.FirstOrDefault(Function(x) String.Equals(x.Name, pl.Name, StringComparison.OrdinalIgnoreCase))
            If u Is Nothing Then Continue For
            list.Add(New UserLora With {.Name = u.Name, .Extension = u.Extension, .Family = u.Family, .Triggers = u.Triggers,
                                        .Weight = pl.Weight, .Enabled = True, .Source = u.Source, .BaseModel = u.BaseModel})
        Next
        Return list
    End Function

    ''' <summary>The prompt with the profile's default / trigger / positive words, and the negative prompt to use.</summary>
    Public Function Compose(p As MediaProfile, prompt As String, modelNegative As String) As (Prompt As String, Negative As String)
        If p Is Nothing Then Return (prompt, modelNegative)
        Dim parts As New List(Of String)()
        Dim main = If(prompt, "").Trim()
        If main.Length = 0 Then main = p.DefaultPrompt.Trim()
        If main.Length > 0 Then parts.Add(main)
        For Each extra In {p.Triggers, p.Positive}
            Dim t = If(extra, "").Trim().Trim(","c).Trim()
            If t.Length > 0 AndAlso main.IndexOf(t, StringComparison.OrdinalIgnoreCase) < 0 Then parts.Add(t)
        Next
        Dim neg = If(String.IsNullOrWhiteSpace(p.Negative), modelNegative, p.Negative.Trim())
        Return (String.Join(", ", parts), neg)
    End Function
End Module
