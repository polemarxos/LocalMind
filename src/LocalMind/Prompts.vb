Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.IO
Imports System.Linq
Imports System.Windows.Forms
Imports Newtonsoft.Json

Public Class SavedPrompt
    Public Property Name As String
    Public Property Text As String
End Class

''' <summary>Reusable prompts. "{text}" is replaced by what is already typed in the message box.</summary>
Public Module PromptLibrary

    Private ReadOnly Property FilePath As String
        Get
            Return Path.Combine(AppPaths.Roaming, "prompts.json")
        End Get
    End Property

    Public Function Load() As List(Of SavedPrompt)
        Try
            If File.Exists(FilePath) Then
                Dim l = JsonConvert.DeserializeObject(Of List(Of SavedPrompt))(File.ReadAllText(FilePath))
                If l IsNot Nothing Then Return l
            End If
        Catch
        End Try
        Return Defaults()
    End Function

    Public Sub Save(list As List(Of SavedPrompt))
        File.WriteAllText(FilePath, JsonConvert.SerializeObject(list, Formatting.Indented))
    End Sub

    ''' <summary>Fills a prompt with the current input text.</summary>
    Public Function Apply(p As SavedPrompt, currentInput As String) As String
        If p.Text.Contains("{text}") Then Return p.Text.Replace("{text}", If(currentInput, "").Trim())
        If String.IsNullOrWhiteSpace(currentInput) Then Return p.Text
        Return p.Text & vbCrLf & vbCrLf & currentInput.Trim()
    End Function

    Private Function Defaults() As List(Of SavedPrompt)
        Return New List(Of SavedPrompt) From {
            New SavedPrompt With {.Name = "Summarize", .Text = "Summarize the following in 5 short bullet points:" & vbCrLf & vbCrLf & "{text}"},
            New SavedPrompt With {.Name = "Translate to Greek", .Text = "Translate to natural Greek. Keep the formatting:" & vbCrLf & vbCrLf & "{text}"},
            New SavedPrompt With {.Name = "Translate to English", .Text = "Translate to natural English. Keep the formatting:" & vbCrLf & vbCrLf & "{text}"},
            New SavedPrompt With {.Name = "Fix grammar", .Text = "Correct spelling and grammar. Return only the corrected text:" & vbCrLf & vbCrLf & "{text}"},
            New SavedPrompt With {.Name = "Explain code", .Text = "Explain what this code does, step by step, then list possible bugs:" & vbCrLf & vbCrLf & "{text}"},
            New SavedPrompt With {.Name = "Write an email", .Text = "Write a short, polite email about the following. Give a subject line too:" & vbCrLf & vbCrLf & "{text}"},
            New SavedPrompt With {.Name = "Explain simply", .Text = "Explain this like I'm 12, with an everyday example:" & vbCrLf & vbCrLf & "{text}"}
        }
    End Function
End Module

''' <summary>Add / edit / delete saved prompts.</summary>
Public Class PromptEditorForm
    Inherits Form

    Private ReadOnly _list As List(Of SavedPrompt)
    Private ReadOnly lst As ListBox
    Private ReadOnly txtName As TextBox
    Private ReadOnly txtText As TextBox
    Private _loading As Boolean

    Public Sub New(Optional newText As String = Nothing)
        Text = "Prompt library"
        Size = New Size(820, 540)
        MinimumSize = New Size(640, 420)
        StartPosition = FormStartPosition.CenterParent
        Theme.ApplyForm(Me)
        _list = PromptLibrary.Load()

        lst = New ListBox() With {.Dock = DockStyle.Left, .Width = 220, .BackColor = Theme.CBack, .ForeColor = Theme.CText,
                                  .BorderStyle = BorderStyle.FixedSingle, .IntegralHeight = False}
        Dim right As New Panel() With {.Dock = DockStyle.Fill, .Padding = New Padding(10, 0, 0, 0)}
        txtName = New TextBox() With {.Dock = DockStyle.Top, .PlaceholderText = "Name"}
        Theme.StyleInput(txtName)
        Dim hint = New Label() With {.Dock = DockStyle.Top, .Height = 26, .ForeColor = Theme.CDim,
                                     .Text = "Tip: {text} is replaced by what you've typed in the message box."}
        txtText = New TextBox() With {.Dock = DockStyle.Fill, .Multiline = True, .ScrollBars = ScrollBars.Vertical, .AcceptsReturn = True,
                                      .Font = New Font("Segoe UI", 10.0!)}
        Theme.StyleInput(txtText)
        right.Controls.Add(txtText)
        right.Controls.Add(hint)
        right.Controls.Add(txtName)

        Dim bar As New FlowLayoutPanel() With {.Dock = DockStyle.Bottom, .Height = 44, .Padding = New Padding(4)}
        Dim btnAdd = Theme.MakeButton("＋ New")
        Dim btnDel = Theme.MakeButton("🗑 Delete")
        Dim btnSave = Theme.MakeButton("Save && close", primary:=True)
        bar.Controls.AddRange({btnAdd, btnDel, btnSave})
        Controls.Add(right)
        Controls.Add(lst)
        Controls.Add(bar)

        AddHandler lst.SelectedIndexChanged, Sub()
                                                 Dim p = TryCast(lst.SelectedItem, PromptItem)
                                                 _loading = True
                                                 txtName.Text = If(p?.P.Name, "")
                                                 txtText.Text = If(p?.P.Text, "")
                                                 _loading = False
                                             End Sub
        AddHandler txtName.TextChanged, Sub()
                                            Dim p = TryCast(lst.SelectedItem, PromptItem)
                                            If _loading OrElse p Is Nothing Then Return
                                            p.P.Name = txtName.Text
                                            lst.Items(lst.SelectedIndex) = p   ' refresh caption
                                        End Sub
        AddHandler txtText.TextChanged, Sub()
                                            Dim p = TryCast(lst.SelectedItem, PromptItem)
                                            If Not _loading AndAlso p IsNot Nothing Then p.P.Text = txtText.Text
                                        End Sub
        AddHandler btnAdd.Click, Sub() AddPrompt("New prompt", "")
        AddHandler btnDel.Click, Sub()
                                     Dim p = TryCast(lst.SelectedItem, PromptItem)
                                     If p Is Nothing Then Return
                                     _list.Remove(p.P)
                                     lst.Items.Remove(p)
                                 End Sub
        AddHandler btnSave.Click, Sub()
                                      _list.RemoveAll(Function(x) String.IsNullOrWhiteSpace(x.Name) OrElse String.IsNullOrWhiteSpace(x.Text))
                                      PromptLibrary.Save(_list)
                                      DialogResult = DialogResult.OK
                                      Close()
                                  End Sub

        For Each p In _list
            lst.Items.Add(New PromptItem(p))
        Next
        If Not String.IsNullOrWhiteSpace(newText) Then
            AddPrompt("New prompt", newText)
        ElseIf lst.Items.Count > 0 Then
            lst.SelectedIndex = 0
        End If
    End Sub

    Private Sub AddPrompt(name As String, text As String)
        Dim p As New SavedPrompt With {.Name = name, .Text = text}
        _list.Add(p)
        lst.SelectedIndex = lst.Items.Add(New PromptItem(p))
        txtName.Focus()
        txtName.SelectAll()
    End Sub

    Private Class PromptItem
        Public ReadOnly P As SavedPrompt
        Public Sub New(p As SavedPrompt)
            Me.P = p
        End Sub
        Public Overrides Function ToString() As String
            Return If(String.IsNullOrWhiteSpace(P.Name), "(unnamed)", P.Name)
        End Function
    End Class
End Class
