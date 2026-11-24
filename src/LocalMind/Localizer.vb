Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Reflection
Imports System.Windows.Forms
Imports Newtonsoft.Json

''' <summary>
''' Interface language. English texts in the code are the keys; Resources/lang-el.json holds
''' the Greek. Apply() walks a window (controls, menus, tool strips, tabs, columns, tooltips,
''' placeholders) once it is built, so forms don't need per-string changes.
''' </summary>
Public Module Localizer

    Private _map As Dictionary(Of String, String)
    Private _phrases As List(Of KeyValuePair(Of String, String))
    Private _lang As String

    ''' <summary>"en" or "el" — chosen in Settings; "auto" follows Windows.</summary>
    Public ReadOnly Property Language As String
        Get
            If _lang Is Nothing Then
                Dim s = If(AppCore.Settings.Language, "auto").ToLowerInvariant()
                _lang = If(s = "auto", If(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName = "el", "el", "en"), s)
            End If
            Return _lang
        End Get
    End Property

    Private Function Map() As Dictionary(Of String, String)
        If _map IsNot Nothing Then Return _map
        _map = New Dictionary(Of String, String)(StringComparer.Ordinal)
        If Language = "el" Then
            Try
                Using st = Assembly.GetExecutingAssembly().GetManifestResourceStream("lang-el.json")
                    Using r As New StreamReader(st)
                        _map = JsonConvert.DeserializeObject(Of Dictionary(Of String, String))(r.ReadToEnd())
                    End Using
                End Using
            Catch
            End Try
        End If
        ' Longer phrases first, for messages built from several parts.
        _phrases = _map.Where(Function(kv) kv.Key.Length >= 10).OrderByDescending(Function(kv) kv.Key.Length).ToList()
        Return _map
    End Function

    ''' <summary>Translates a UI text (returns it unchanged when there is no translation).</summary>
    Public Function T(text As String) As String
        If String.IsNullOrEmpty(text) OrElse Language = "en" Then Return text
        Dim m = Map()
        Dim hit As String = Nothing
        If m.TryGetValue(text, hit) Then Return hit
        Dim trimmed = text.Trim()
        If trimmed <> text AndAlso m.TryGetValue(trimmed, hit) Then Return text.Replace(trimmed, hit)
        ' Composite message: translate the known parts it contains.
        Dim result = text
        For Each kv In _phrases
            If result.Contains(kv.Key) Then result = result.Replace(kv.Key, kv.Value)
        Next
        Return result
    End Function

    ''' <summary>MessageBox with translated text and caption.</summary>
    Public Function Box(owner As IWin32Window, text As String, Optional caption As String = "",
                        Optional buttons As MessageBoxButtons = MessageBoxButtons.OK,
                        Optional icon As MessageBoxIcon = MessageBoxIcon.None) As DialogResult
        Return BoxForm.Ask(owner, T(text), T(caption), buttons, icon)
    End Function

    Public Function Box(text As String, Optional caption As String = "",
                        Optional buttons As MessageBoxButtons = MessageBoxButtons.OK,
                        Optional icon As MessageBoxIcon = MessageBoxIcon.None) As DialogResult
        Return BoxForm.Ask(Nothing, T(text), T(caption), buttons, icon)
    End Function

    ''' <summary>Strings used inside the chat view (JavaScript).</summary>
    Public Function ChatViewStrings() As String
        Dim keys = {"You", "Assistant", "Copy", "Copied!", "Copy answer", "🔊 Read", "Read aloud (click again to stop)",
                    "💭 Thinking…", "💭 Thinking (click to expand)",
                    "Edit", "Edit message", "Cancel", "Send", "Enter sends · Shift+Enter new line · Esc cancels"}
        Return JsonConvert.SerializeObject(keys.ToDictionary(Function(k) k, Function(k) T(k)))
    End Function

    ''' <summary>Translates a whole window in place.</summary>
    Public Sub Apply(root As Control)
        If Language = "en" OrElse root Is Nothing Then Return
        Try
            Walk(root)
        Catch
        End Try
    End Sub

    Private Sub Walk(c As Control)
        If TypeOf c Is Form OrElse TypeOf c Is Label OrElse TypeOf c Is ButtonBase OrElse TypeOf c Is TabPage OrElse TypeOf c Is GroupBox OrElse TypeOf c Is LinkLabel Then
            c.Text = T(c.Text)
        End If
        If TypeOf c Is TextBox Then
            Dim tb = CType(c, TextBox)
            tb.PlaceholderText = T(tb.PlaceholderText)
        End If
        If TypeOf c Is ToolStrip Then
            For Each it As ToolStripItem In CType(c, ToolStrip).Items
                WalkItem(it)
            Next
        End If
        If TypeOf c Is DataGridView Then
            For Each col As DataGridViewColumn In CType(c, DataGridView).Columns
                col.HeaderText = T(col.HeaderText)
            Next
        End If
        If TypeOf c Is ListView Then
            For Each col As ColumnHeader In CType(c, ListView).Columns
                col.Text = T(col.Text)
            Next
        End If
        If c.ContextMenuStrip IsNot Nothing Then
            For Each it As ToolStripItem In c.ContextMenuStrip.Items
                WalkItem(it)
            Next
        End If
        For Each child As Control In c.Controls
            Walk(child)
        Next
    End Sub

    Public Sub WalkItem(it As ToolStripItem)
        If TypeOf it Is ToolStripComboBox Then Return    ' values are data, not captions
        it.Text = T(it.Text)
        it.ToolTipText = T(it.ToolTipText)
        Dim dd = TryCast(it, ToolStripDropDownItem)
        If dd IsNot Nothing Then
            For Each sub_ As ToolStripItem In dd.DropDownItems
                WalkItem(sub_)
            Next
        End If
    End Sub
End Module
