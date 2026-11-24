Imports System
Imports System.Threading
Imports System.Threading.Tasks
Imports Newtonsoft.Json

' Chat side of ChatAgent: status updates in the answer bubble.
Partial Class MainForm
    Private Sub ToolStatus(t As String)
        SetStatus(t)
        Dim id = _streamMsgId
        Me.Post(Sub() Js($"setStatusText('{id}', {JsonConvert.SerializeObject(Localizer.T(t))});"))
    End Sub
End Class
