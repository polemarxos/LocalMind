Imports System
Imports System.Threading
Imports System.Threading.Tasks
Imports Newtonsoft.Json

' Chat side of ChatAgent: status updates in the answer bubble.
Partial Class MainForm
    Private Sub ToolStatus(t As String)
        SetStatus(t)
        Dim id = _streamMsgId
        ' overall: the steps done, plus this step's own percent ("Step 3/8 (36%)")
        Dim m = System.Text.RegularExpressions.Regex.Match(If(t, ""), "\((\d{1,3})%\)")
        Dim own = If(m.Success, Math.Min(100, CInt(m.Groups(1).Value)), 0)
        Dim total = Math.Max(1, ChatAgent.StepCount)
        Dim overall = CInt((Math.Min(ChatAgent.StepIndex, total - 1) * 100 + own) / total)
        Me.Post(Sub()
                    Js($"setStatusText('{id}', {JsonConvert.SerializeObject(Localizer.T(t))});")
                    Js($"setProgress('{id}', {overall});")
                End Sub)
    End Sub
End Class
