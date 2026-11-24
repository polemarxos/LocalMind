Imports System
Imports System.Collections.Generic
Imports System.Linq

''' <summary>
''' The model families LocalMind lists. Chat: Qwen and Gemma 3 / 4. Pictures and video: FLUX, Krea 2, Qwen-Image,
''' LTX-2.5 and MiniMax H3 (with its fine-tunes such as 10Eros), plus the picture enlargers and the background
''' remover the app's tools use. Models already downloaded, and ones added by the user, always stay listed.
''' </summary>
Public Module ModelFamilies
    Private ReadOnly MediaKeep As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
        "FLUX.1", "FLUX.1-Kontext", "FLUX.2", "Krea", "Qwen-Image", "Qwen-Image-Edit", "LTX-2.5", "MiniMax-H3",
        "ESRGAN", "BiRefNet"}

    Public Function ChatAllowed(e As CatalogEntry) As Boolean
        Dim arch = If(e.Arch, "").ToLowerInvariant()
        If arch.StartsWith("qwen") OrElse arch.StartsWith("gemma3") OrElse arch.StartsWith("gemma4") Then Return True
        If arch.Length > 0 Then Return False
        ' (no architecture known, e.g. a search result: go by the name)
        Dim n = (If(e.Repo, "") & " " & If(e.Name, "")).ToLowerInvariant().Replace("-", "").Replace("_", "").Replace(" ", "")
        Return n.Contains("qwen") OrElse n.Contains("gemma3") OrElse n.Contains("gemma4")
    End Function

    Public Function MediaAllowed(r As MediaRecipe) As Boolean
        Return MediaKeep.Contains(If(r.Family, ""))
    End Function

    ''' <summary>The listed chat models, plus any already downloaded.</summary>
    Public Function KeepChat(list As List(Of CatalogEntry), Optional installed As Func(Of CatalogEntry, Boolean) = Nothing) As List(Of CatalogEntry)
        If list Is Nothing Then Return Nothing
        Return list.Where(Function(e) ChatAllowed(e) OrElse (installed IsNot Nothing AndAlso installed(e))).ToList()
    End Function
End Module
