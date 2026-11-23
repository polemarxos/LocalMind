Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.Linq
Imports System.Windows.Forms

' Prompt library button + chat folders.
Partial Class MainForm

    Private _btnPrompts As ToolStripDropDownButton
    Private _cmbFolder As ComboBox
    Private Const AllChatsLabel As String = "📂 All chats"
    Private Const NoFolderLabel As String = "(No folder)"
    Private Const NewFolderLabel As String = "＋ New folder…"

    '──────────────── Prompts ────────────────
    Private Sub BuildPromptsButton()
        _btnPrompts = New ToolStripDropDownButton("📝 Prompts") With {.ToolTipText = "Insert a saved prompt"}
        toolTop.Items.Insert(toolTop.Items.IndexOf(btnThinking) + 1, _btnPrompts)
        AddHandler _btnPrompts.DropDownOpening, Sub() FillPromptsMenu()
        FillPromptsMenu()
    End Sub

    Private Sub FillPromptsMenu()
        _btnPrompts.DropDownItems.Clear()
        For Each p In PromptLibrary.Load()
            Dim it As New ToolStripMenuItem(p.Name) With {.ToolTipText = If(p.Text.Length > 200, p.Text.Substring(0, 200) & "…", p.Text)}
            AddHandler it.Click, Sub()
                                     txtInput.Text = PromptLibrary.Apply(p, txtInput.Text)
                                     txtInput.SelectionStart = txtInput.TextLength
                                     txtInput.Focus()
                                 End Sub
            _btnPrompts.DropDownItems.Add(it)
        Next
        _btnPrompts.DropDownItems.Add(New ToolStripSeparator())
        Dim miSave As New ToolStripMenuItem("Save message box as prompt…") With {.Enabled = txtInput.TextLength > 0}
        AddHandler miSave.Click, Sub()
                                     Using f As New PromptEditorForm(txtInput.Text)
                                         f.ShowDialog(Me)
                                     End Using
                                 End Sub
        Dim miManage As New ToolStripMenuItem("Manage prompts…")
        AddHandler miManage.Click, Sub()
                                       Using f As New PromptEditorForm()
                                           f.ShowDialog(Me)
                                       End Using
                                   End Sub
        _btnPrompts.DropDownItems.AddRange({miSave, miManage})
    End Sub

    '──────────────── Folders ────────────────
    Private Sub BuildFolderFilter()
        _cmbFolder = New ComboBox() With {
            .Dock = DockStyle.Top, .DropDownStyle = ComboBoxStyle.DropDownList, .FlatStyle = FlatStyle.Flat,
            .BackColor = Color.FromArgb(45, 45, 48), .ForeColor = Color.FromArgb(212, 212, 212), .Font = New Font("Segoe UI", 9.5!)}
        splitMain.Panel1.Controls.Add(_cmbFolder)
        ' Dock order: New chat button, search box, folder filter, then the list.
        splitMain.Panel1.Controls.SetChildIndex(_cmbFolder, 1)
        FillFolderCombo(AllChatsLabel)
        AddHandler _cmbFolder.SelectedIndexChanged, Sub()
                                                        If _cmbFolder.SelectedItem?.ToString() = NewFolderLabel Then
                                                            Dim name = AskFolderName()
                                                            FillFolderCombo(If(name, AllChatsLabel))
                                                            Return
                                                        End If
                                                        RefreshChatList()
                                                    End Sub
    End Sub

    ''' <summary>Nothing = all chats; "" = chats without a folder; otherwise the folder name.</summary>
    Private Function CurrentFolderFilter() As String
        Dim sel = _cmbFolder?.SelectedItem?.ToString()
        If sel Is Nothing OrElse sel = AllChatsLabel OrElse sel = NewFolderLabel Then Return Nothing
        If sel = NoFolderLabel Then Return ""
        Return sel.Substring(2)   ' strip "📁"
    End Function

    Private _extraFolders As New List(Of String)()

    Private Function AllFolders() As List(Of String)
        Return AppCore.History.ListAll().Select(Function(s) s.Folder).Where(Function(f) Not String.IsNullOrWhiteSpace(f)).
               Concat(_extraFolders).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(Function(f) f, StringComparer.OrdinalIgnoreCase).ToList()
    End Function

    Private Sub FillFolderCombo(selectLabel As String)
        _cmbFolder.BeginUpdate()
        _cmbFolder.Items.Clear()
        _cmbFolder.Items.Add(AllChatsLabel)
        For Each f In AllFolders()
            _cmbFolder.Items.Add("📁" & f)
        Next
        _cmbFolder.Items.Add(NoFolderLabel)
        _cmbFolder.Items.Add(NewFolderLabel)
        Dim target = If(selectLabel = AllChatsLabel OrElse selectLabel = NoFolderLabel, selectLabel, "📁" & selectLabel)
        Dim idx = _cmbFolder.Items.IndexOf(target)
        _cmbFolder.SelectedIndex = If(idx >= 0, idx, 0)
        _cmbFolder.EndUpdate()
    End Sub

    Private Function AskFolderName() As String
        Dim name = Microsoft.VisualBasic.Interaction.InputBox("Folder name:", "New folder", "").Trim()
        If name.Length = 0 Then Return Nothing
        If Not _extraFolders.Contains(name, StringComparer.OrdinalIgnoreCase) Then _extraFolders.Add(name)
        Return name
    End Function

    Private Sub BuildMoveMenu(miMove As ToolStripMenuItem)
        miMove.DropDownItems.Clear()
        Dim item = TryCast(lstChats.SelectedItem, ChatListItem)
        miMove.Enabled = item IsNot Nothing
        If item Is Nothing Then Return
        For Each f In AllFolders()
            Dim folder = f
            Dim mi As New ToolStripMenuItem(folder) With {.Checked = String.Equals(item.Session.Folder, folder, StringComparison.OrdinalIgnoreCase)}
            AddHandler mi.Click, Sub() MoveChat(item.Session, folder)
            miMove.DropDownItems.Add(mi)
        Next
        If miMove.DropDownItems.Count > 0 Then miMove.DropDownItems.Add(New ToolStripSeparator())
        Dim miNone As New ToolStripMenuItem(NoFolderLabel) With {.Checked = String.IsNullOrEmpty(item.Session.Folder)}
        AddHandler miNone.Click, Sub() MoveChat(item.Session, "")
        Dim miNew As New ToolStripMenuItem(NewFolderLabel)
        AddHandler miNew.Click, Sub()
                                    Dim name = AskFolderName()
                                    If name IsNot Nothing Then MoveChat(item.Session, name)
                                End Sub
        miMove.DropDownItems.AddRange({miNone, miNew})
    End Sub

    Private Sub MoveChat(s As ChatSession, folder As String)
        s.Folder = folder
        AppCore.History.Save(s)
        Dim cur = CurrentFolderFilter()
        FillFolderCombo(If(cur Is Nothing, AllChatsLabel, If(cur = "", NoFolderLabel, cur)))
        RefreshChatList()
        SetStatus(If(folder = "", "Moved out of folder", "Moved to " & folder))
    End Sub
End Class
