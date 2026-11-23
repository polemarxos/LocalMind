Imports System
Imports System.Drawing
Imports System.IO
Imports System.Threading
Imports System.Windows.Forms

''' <summary>Pick the folders/files LocalMind may search when "📚 Docs" is on.</summary>
Public Class KnowledgeForm
    Inherits Form

    Private ReadOnly lst As ListBox
    Private ReadOnly lbl As Label
    Private ReadOnly btnIndex As Button
    Private _cts As CancellationTokenSource

    Public Sub New()
        Text = "My documents"
        Size = New Size(780, 520)
        MinimumSize = New Size(620, 400)
        StartPosition = FormStartPosition.CenterParent
        Theme.ApplyForm(Me)

        Dim intro = New Label() With {.Dock = DockStyle.Top, .Height = 54, .Padding = New Padding(10, 8, 10, 0), .ForeColor = Theme.CDim,
            .Text = "Add folders or files (PDF, Word, text, Markdown, code, web pages…). They stay on this PC. " &
                    "Turn on 📚 Docs in the toolbar and LocalMind answers from them and cites the source."}
        lst = New ListBox() With {.Dock = DockStyle.Fill, .BackColor = Theme.CBack, .ForeColor = Theme.CText, .BorderStyle = BorderStyle.FixedSingle,
                                  .IntegralHeight = False, .Font = New Font("Segoe UI", 10.0!)}
        Dim bar As New FlowLayoutPanel() With {.Dock = DockStyle.Bottom, .Height = 76, .Padding = New Padding(6)}
        Dim btnFolder = Theme.MakeButton("📁 Add folder…")
        Dim btnFiles = Theme.MakeButton("📄 Add files…")
        Dim btnRemove = Theme.MakeButton("Remove")
        btnIndex = Theme.MakeButton("⟳ Index now", primary:=True)
        lbl = New Label() With {.AutoSize = False, .Width = 720, .Height = 24, .ForeColor = Theme.CDim}
        bar.Controls.AddRange({btnFolder, btnFiles, btnRemove, btnIndex, lbl})
        Controls.Add(lst)
        Controls.Add(intro)
        Controls.Add(bar)

        AddHandler btnFolder.Click, Sub()
                                        Using d As New FolderBrowserDialog() With {.Description = "Folder with documents"}
                                            If d.ShowDialog(Me) = DialogResult.OK Then
                                                Knowledge.AddSource(d.SelectedPath)
                                                Reload()
                                            End If
                                        End Using
                                    End Sub
        AddHandler btnFiles.Click, Sub()
                                       Using d As New OpenFileDialog() With {.Multiselect = True, .Title = "Documents",
                                           .Filter = "Documents|*.pdf;*.docx;*.txt;*.md;*.html;*.htm;*.csv;*.json;*.xml;*.rtf;*.srt|Code|*.vb;*.cs;*.py;*.js;*.ts;*.java;*.cpp;*.c;*.h;*.sql|All files|*.*"}
                                           If d.ShowDialog(Me) = DialogResult.OK Then
                                               For Each f In d.FileNames
                                                   Knowledge.AddSource(f)
                                               Next
                                               Reload()
                                           End If
                                       End Using
                                   End Sub
        AddHandler btnRemove.Click, Sub()
                                        If lst.SelectedItem Is Nothing Then Return
                                        Knowledge.RemoveSource(lst.SelectedItem.ToString())
                                        Reload()
                                    End Sub
        AddHandler btnIndex.Click, AddressOf IndexClicked
        Reload()
    End Sub

    Private Sub Reload()
        lst.Items.Clear()
        For Each s In Knowledge.Sources()
            lst.Items.Add(s)
        Next
        Dim st = Knowledge.Stats()
        lbl.Text = $"{st.Docs} documents indexed ({st.Chunks} passages). Press 'Index now' after adding or changing files."
    End Sub

    Private Async Sub IndexClicked(sender As Object, e As EventArgs)
        If _cts IsNot Nothing Then
            _cts.Cancel()
            Return
        End If
        _cts = New CancellationTokenSource()
        btnIndex.Text = "Cancel"
        Try
            Dim summary = Await Knowledge.IndexAsync(Sub(t) Me.Post(Sub() lbl.Text = t), _cts.Token)
            lbl.Text = summary
        Catch ex As OperationCanceledException
            lbl.Text = "Indexing cancelled."
        Catch ex As Exception
            lbl.Text = "⚠ " & ex.Message
        Finally
            _cts = Nothing
            btnIndex.Text = "⟳ Index now"
        End Try
    End Sub

    Protected Overrides Sub OnFormClosing(e As FormClosingEventArgs)
        _cts?.Cancel()
        MyBase.OnFormClosing(e)
    End Sub
End Class
