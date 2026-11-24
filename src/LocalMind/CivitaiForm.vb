Imports System
Imports System.Drawing
Imports System.Windows.Forms

''' <summary>Find LoRAs and checkpoints on Civitai and install them with one click (the same gallery as Models → Picture / Video).</summary>
Public Class CivitaiForm
    Inherits Form

    Private ReadOnly gallery As ModelGallery

    <System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)>
    Public ReadOnly Property InstalledSomething As Boolean
        Get
            Return gallery.InstalledSomething
        End Get
    End Property

    ''' <param name="checkpoints">Open on the checkpoints list instead of LoRAs.</param>
    ''' <param name="family">LoRA family / recipe family to preselect (e.g. "SDXL").</param>
    ''' <param name="kind">"picture" or "video": only models of that kind.</param>
    Public Sub New(Optional checkpoints As Boolean = False, Optional family As String = Nothing, Optional kind As String = Nothing)
        Text = "Civitai — LoRAs & models"
        Size = New Size(1300, 860)
        MinimumSize = New Size(1000, 640)
        StartPosition = FormStartPosition.CenterParent
        Theme.ApplyForm(Me)
        gallery = New ModelGallery(kind, localList:=False, loras:=Not checkpoints, family:=family)
        Controls.Add(gallery)
    End Sub

    Protected Overrides Sub OnShown(e As EventArgs)
        MyBase.OnShown(e)
        gallery.Start()
    End Sub

    Protected Overrides Sub OnFormClosing(e As FormClosingEventArgs)
        gallery.Shutdown()
        MyBase.OnFormClosing(e)
    End Sub
End Class
