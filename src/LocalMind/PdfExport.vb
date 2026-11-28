Imports System
Imports System.IO
Imports System.Linq
Imports System.Text.RegularExpressions
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports Microsoft.Web.WebView2.Core
Imports Microsoft.Web.WebView2.WinForms

''' <summary>
''' 💾 Saves a chat answer (a summary of a document, a plan, a table…) as PDF, Word, LibreOffice, Excel and more.
''' PDF is printed by an invisible WebView2, the same engine the chat uses; the other formats are in DocExport.
''' </summary>
Public Module PdfExport

    ' (format, file extension, name in the list)
    Private ReadOnly Formats As (Key As String, Ext As String, Label As String)() = {
        ("pdf", ".pdf", "PDF"),
        ("docx", ".docx", "Word"),
        ("odt", ".odt", "LibreOffice / OpenOffice Writer"),
        ("rtf", ".rtf", "Rich Text (WordPad)"),
        ("xlsx", ".xlsx", "Excel — the tables of the answer"),
        ("ods", ".ods", "LibreOffice / OpenOffice Calc — the tables of the answer"),
        ("csv", ".csv", "CSV — the tables of the answer"),
        ("html", ".html", "Web page"),
        ("md", ".md", "Markdown"),
        ("txt", ".txt", "Plain text")}

    ''' <summary>
    ''' Asks where and in which format (the list in the save window), then saves the answer there.
    ''' </summary>
    Public Async Function SaveAnswerAsync(owner As Form, markdown As String, title As String, status As Action(Of String)) As Task(Of String)
        If String.IsNullOrWhiteSpace(markdown) Then Return Nothing
        Dim target As String
        Dim fmt As String
        Using d As New SaveFileDialog()
            d.Filter = String.Join("|", Formats.Select(Function(f) $"{Localizer.T(f.Label)} (*{f.Ext})|*{f.Ext}"))
            ' (an answer that is mostly a table starts on Excel, others on PDF)
            d.FilterIndex = If(DocExport.Tables(markdown).Count > 0 AndAlso Not Regex.IsMatch(markdown, "^#{1,3} ", RegexOptions.Multiline), 5, 1)
            d.FileName = SafeName(title)
            d.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            If d.ShowDialog(owner) <> DialogResult.OK Then Return Nothing
            Dim chosen = Formats(Math.Max(0, Math.Min(Formats.Length - 1, d.FilterIndex - 1)))
            fmt = chosen.Key
            target = d.FileName
            If Not Path.GetExtension(target).Equals(chosen.Ext, StringComparison.OrdinalIgnoreCase) Then target &= chosen.Ext
        End Using
        Dim utf8 As New Text.UTF8Encoding(False)
        Select Case fmt
            Case "pdf" : Await PrintPdfAsync(markdown, title, target, status)
            Case "docx", "odt", "rtf" : Await DocExport.PandocAsync(markdown, title, target, fmt, status, Threading.CancellationToken.None)
            Case "xlsx" : DocExport.WriteXlsx(markdown, target)
            Case "ods" : DocExport.WriteOds(markdown, target)
            Case "csv" : DocExport.WriteCsv(markdown, target)
            Case "html" : File.WriteAllText(target, DocExport.StandaloneHtml(markdown, title), utf8)
            Case "md" : File.WriteAllText(target, If(String.IsNullOrWhiteSpace(title), "", "# " & title & vbCrLf & vbCrLf) & DocExport.PortableMarkdown(markdown), utf8)
            Case Else : File.WriteAllText(target, If(String.IsNullOrWhiteSpace(title), "", title & vbCrLf & vbCrLf) & DocExport.PlainText(markdown), New Text.UTF8Encoding(True))
        End Select
        status?.Invoke(Localizer.T("Saved to") & " " & target)
        Return target
    End Function

    ''' <summary>📄 The answer laid out as a plain page and printed to PDF by an invisible WebView2.</summary>
    Public Async Function PrintPdfAsync(markdown As String, title As String, target As String, status As Action(Of String)) As Task
        status?.Invoke(Localizer.T("Making the PDF…"))
        Dim html = MarkdownRenderer.PrintHtml(markdown, title)
        Dim host As New Form() With {.ShowInTaskbar = False, .FormBorderStyle = FormBorderStyle.None, .StartPosition = FormStartPosition.Manual,
                                     .Location = New Drawing.Point(-20000, -20000), .Size = New Drawing.Size(900, 1200), .Opacity = 0}
        Dim wv As New WebView2() With {.Dock = DockStyle.Fill}
        host.Controls.Add(wv)
        Try
            host.Show()
            Dim env = Await CoreWebView2Environment.CreateAsync(Nothing, AppPaths.WebViewFolder)
            Await wv.EnsureCoreWebView2Async(env)
            ' (pictures made by LocalMind come from the outputs folder)
            wv.CoreWebView2.SetVirtualHostNameToFolderMapping("outputs.localmind", MediaStudio.OutputFolder, CoreWebView2HostResourceAccessKind.Allow)
            Dim loaded As New TaskCompletionSource(Of Boolean)()
            AddHandler wv.CoreWebView2.NavigationCompleted, Sub(s, a) loaded.TrySetResult(True)
            wv.CoreWebView2.NavigateToString(html)
            Await Task.WhenAny(loaded.Task, Task.Delay(15000))
            ' wait for the pictures (at most 10 s)
            For i = 1 To 40
                Dim r = Await wv.CoreWebView2.ExecuteScriptAsync("Array.prototype.every.call(document.images, function (i) { return i.complete; })")
                If r = "true" Then Exit For
                Await Task.Delay(250)
            Next
            Dim ps = env.CreatePrintSettings()
            ps.ShouldPrintBackgrounds = True
            ps.ShouldPrintHeaderAndFooter = False
            Dim tmp = target & ".part"
            If Not Await wv.CoreWebView2.PrintToPdfAsync(tmp, ps) Then Throw New Exception(Localizer.T("The PDF could not be made."))
            If File.Exists(target) Then File.Delete(target)
            File.Move(tmp, target)
        Finally
            wv.Dispose()
            host.Dispose()
        End Try
    End Function

    ''' <summary>A file name from the question ("Summarize report.pdf" → "Summarize report").</summary>
    Public Function SafeName(title As String) As String
        Dim t = Regex.Replace(If(title, ""), "[\\/:*?""<>|\r\n\t]+", " ").Trim()
        t = Regex.Replace(t, "\s{2,}", " ")
        If t.Length > 60 Then t = t.Substring(0, 60).Trim()
        Return If(t = "", "LocalMind " & DateTime.Now.ToString("yyyy-MM-dd HHmm"), t)
    End Function
End Module
