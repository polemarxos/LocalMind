Imports System
Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Globalization
Imports System.IO
Imports System.IO.Compression
Imports System.Linq
Imports System.Security
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Threading
Imports System.Threading.Tasks
Imports Markdig
Imports Markdig.Extensions.Tables
Imports Markdig.Syntax

''' <summary>
''' A chat answer in other formats: Word / LibreOffice Writer / RTF (through pandoc, downloaded on first use),
''' Excel / LibreOffice Calc / CSV (the tables of the answer, written here), HTML, Markdown and plain text.
''' (PDF is printed by WebView2: <see cref="PdfExport"/>.)
''' </summary>
Public Module DocExport

    Private ReadOnly Pipeline As MarkdownPipeline = New MarkdownPipelineBuilder().UseAdvancedExtensions().Build()

    '──────────────── tables (Excel, Calc, CSV) ────────────────
    ''' <summary>Every table in the answer: rows of cells as plain text (the first row is the header).</summary>
    Public Function Tables(md As String) As List(Of List(Of List(Of String)))
        Dim result As New List(Of List(Of List(Of String)))()
        If String.IsNullOrWhiteSpace(md) Then Return result
        Dim doc = Markdown.Parse(md, Pipeline)
        For Each tbl In doc.Descendants(Of Table)()
            Dim rows As New List(Of List(Of String))()
            For Each row In tbl.OfType(Of TableRow)()
                Dim cells As New List(Of String)()
                For Each cell In row.OfType(Of TableCell)()
                    Dim src = If(cell.Span.Start >= 0 AndAlso cell.Span.End < md.Length, md.Substring(cell.Span.Start, cell.Span.Length), "")
                    cells.Add(PlainText(src).Trim())
                Next
                rows.Add(cells)
            Next
            If rows.Count > 0 Then result.Add(rows)
        Next
        Return result
    End Function

    ''' <summary>The tables, or, for an answer without one, its lines one per row (so the sheet is never empty).</summary>
    Private Function SheetsFor(md As String) As List(Of List(Of List(Of String)))
        Dim t = Tables(md)
        If t.Count > 0 Then Return t
        Dim lines = PlainText(md).Split({vbCrLf, vbLf}, StringSplitOptions.None).Select(Function(l) l.TrimEnd()).Where(Function(l) l <> "").
                    Select(Function(l) New List(Of String) From {l}).ToList()
        Return New List(Of List(Of List(Of String))) From {lines}
    End Function

    Private ReadOnly NumberRx As New Regex("^-?\d+(\.\d+)?$", RegexOptions.Compiled)

    Private Function AsNumber(s As String, ByRef v As Double) As Boolean
        Return NumberRx.IsMatch(s) AndAlso Double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, v)
    End Function

    Public Sub WriteXlsx(md As String, path As String)
        Dim sheets = SheetsFor(md)
        Dim hasTables = Tables(md).Count > 0
        Using fs = File.Create(path), zip As New ZipArchive(fs, ZipArchiveMode.Create)
            Dim put = Sub(name As String, xml As String)
                          Using w As New StreamWriter(zip.CreateEntry(name).Open(), New UTF8Encoding(False))
                              w.Write(xml)
                          End Using
                      End Sub
            Dim ct As New StringBuilder("<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?><Types xmlns=""http://schemas.openxmlformats.org/package/2006/content-types"">" &
                "<Default Extension=""rels"" ContentType=""application/vnd.openxmlformats-package.relationships+xml""/><Default Extension=""xml"" ContentType=""application/xml""/>" &
                "<Override PartName=""/xl/workbook.xml"" ContentType=""application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml""/>" &
                "<Override PartName=""/xl/styles.xml"" ContentType=""application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml""/>")
            For i = 1 To sheets.Count
                ct.Append($"<Override PartName=""/xl/worksheets/sheet{i}.xml"" ContentType=""application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml""/>")
            Next
            ct.Append("</Types>")
            put("[Content_Types].xml", ct.ToString())
            put("_rels/.rels", "<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?><Relationships xmlns=""http://schemas.openxmlformats.org/package/2006/relationships"">" &
                "<Relationship Id=""rId1"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"" Target=""xl/workbook.xml""/></Relationships>")
            Dim wb As New StringBuilder("<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?><workbook xmlns=""http://schemas.openxmlformats.org/spreadsheetml/2006/main"" " &
                "xmlns:r=""http://schemas.openxmlformats.org/officeDocument/2006/relationships""><sheets>")
            Dim rels As New StringBuilder("<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?><Relationships xmlns=""http://schemas.openxmlformats.org/package/2006/relationships"">")
            For i = 1 To sheets.Count
                wb.Append($"<sheet name=""{SecurityElement.Escape(SheetName(i, hasTables))}"" sheetId=""{i}"" r:id=""rId{i}""/>")
                rels.Append($"<Relationship Id=""rId{i}"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"" Target=""worksheets/sheet{i}.xml""/>")
            Next
            wb.Append("</sheets></workbook>")
            rels.Append($"<Relationship Id=""rId{sheets.Count + 1}"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles"" Target=""styles.xml""/></Relationships>")
            put("xl/workbook.xml", wb.ToString())
            put("xl/_rels/workbook.xml.rels", rels.ToString())
            ' style 1 = bold (the header row)
            put("xl/styles.xml", "<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?><styleSheet xmlns=""http://schemas.openxmlformats.org/spreadsheetml/2006/main"">" &
                "<fonts count=""2""><font><sz val=""11""/><name val=""Calibri""/></font><font><b/><sz val=""11""/><name val=""Calibri""/></font></fonts>" &
                "<fills count=""2""><fill><patternFill patternType=""none""/></fill><fill><patternFill patternType=""gray125""/></fill></fills>" &
                "<borders count=""1""><border><left/><right/><top/><bottom/><diagonal/></border></borders>" &
                "<cellStyleXfs count=""1""><xf numFmtId=""0"" fontId=""0"" fillId=""0"" borderId=""0""/></cellStyleXfs>" &
                "<cellXfs count=""2""><xf numFmtId=""0"" fontId=""0"" fillId=""0"" borderId=""0"" xfId=""0""/><xf numFmtId=""0"" fontId=""1"" fillId=""0"" borderId=""0"" xfId=""0"" applyFont=""1""/></cellXfs>" &
                "<cellStyles count=""1""><cellStyle name=""Normal"" xfId=""0"" builtinId=""0""/></cellStyles>" &
                "</styleSheet>")
            For i = 1 To sheets.Count
                Dim rows = sheets(i - 1)
                Dim sx As New StringBuilder("<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?><worksheet xmlns=""http://schemas.openxmlformats.org/spreadsheetml/2006/main"">")
                Dim cols = rows.Max(Function(r) r.Count)
                If cols > 0 Then
                    sx.Append("<cols>")
                    For c = 0 To cols - 1
                        Dim cc = c
                        Dim w = Math.Min(60, Math.Max(8, rows.Max(Function(r) If(cc < r.Count, r(cc).Length, 0)) + 2))
                        sx.Append($"<col min=""{c + 1}"" max=""{c + 1}"" width=""{w}"" customWidth=""1""/>")
                    Next
                    sx.Append("</cols>")
                End If
                sx.Append("<sheetData>")
                For r = 0 To rows.Count - 1
                    sx.Append($"<row r=""{r + 1}"">")
                    For c = 0 To rows(r).Count - 1
                        Dim refName = ColName(c) & (r + 1).ToString(CultureInfo.InvariantCulture)
                        Dim style = If(r = 0 AndAlso hasTables, " s=""1""", "")
                        Dim v As Double
                        If r > 0 AndAlso AsNumber(rows(r)(c), v) Then
                            sx.Append($"<c r=""{refName}""{style}><v>{v.ToString("R", CultureInfo.InvariantCulture)}</v></c>")
                        ElseIf rows(r)(c) <> "" Then
                            sx.Append($"<c r=""{refName}""{style} t=""inlineStr""><is><t xml:space=""preserve"">{SecurityElement.Escape(rows(r)(c))}</t></is></c>")
                        End If
                    Next
                    sx.Append("</row>")
                Next
                sx.Append("</sheetData></worksheet>")
                put($"xl/worksheets/sheet{i}.xml", sx.ToString())
            Next
        End Using
    End Sub

    Public Sub WriteOds(md As String, path As String)
        Dim sheets = SheetsFor(md)
        Dim hasTables = Tables(md).Count > 0
        Using fs = File.Create(path), zip As New ZipArchive(fs, ZipArchiveMode.Create)
            ' (ODF: "mimetype" comes first and is not compressed)
            Using w As New StreamWriter(zip.CreateEntry("mimetype", CompressionLevel.NoCompression).Open(), New UTF8Encoding(False))
                w.Write("application/vnd.oasis.opendocument.spreadsheet")
            End Using
            Dim put = Sub(name As String, xml As String)
                          Using w As New StreamWriter(zip.CreateEntry(name).Open(), New UTF8Encoding(False))
                              w.Write(xml)
                          End Using
                      End Sub
            put("META-INF/manifest.xml", "<?xml version=""1.0"" encoding=""UTF-8""?><manifest:manifest xmlns:manifest=""urn:oasis:names:tc:opendocument:xmlns:manifest:1.0"" manifest:version=""1.2"">" &
                "<manifest:file-entry manifest:full-path=""/"" manifest:version=""1.2"" manifest:media-type=""application/vnd.oasis.opendocument.spreadsheet""/>" &
                "<manifest:file-entry manifest:full-path=""content.xml"" manifest:media-type=""text/xml""/></manifest:manifest>")
            Dim sb As New StringBuilder("<?xml version=""1.0"" encoding=""UTF-8""?><office:document-content xmlns:office=""urn:oasis:names:tc:opendocument:xmlns:office:1.0"" " &
                "xmlns:table=""urn:oasis:names:tc:opendocument:xmlns:table:1.0"" xmlns:text=""urn:oasis:names:tc:opendocument:xmlns:text:1.0"" " &
                "xmlns:style=""urn:oasis:names:tc:opendocument:xmlns:style:1.0"" xmlns:fo=""urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0"" office:version=""1.2"">" &
                "<office:automatic-styles><style:style style:name=""hdr"" style:family=""table-cell""><style:text-properties fo:font-weight=""bold""/></style:style></office:automatic-styles>" &
                "<office:body><office:spreadsheet>")
            For i = 1 To sheets.Count
                sb.Append($"<table:table table:name=""{SecurityElement.Escape(SheetName(i, hasTables))}"">")
                Dim rows = sheets(i - 1)
                For r = 0 To rows.Count - 1
                    sb.Append("<table:table-row>")
                    For Each cell In rows(r)
                        Dim style = If(r = 0 AndAlso hasTables, " table:style-name=""hdr""", "")
                        Dim v As Double
                        If r > 0 AndAlso AsNumber(cell, v) Then
                            sb.Append($"<table:table-cell{style} office:value-type=""float"" office:value=""{v.ToString("R", CultureInfo.InvariantCulture)}""><text:p>{SecurityElement.Escape(cell)}</text:p></table:table-cell>")
                        Else
                            sb.Append($"<table:table-cell{style} office:value-type=""string""><text:p>{SecurityElement.Escape(cell)}</text:p></table:table-cell>")
                        End If
                    Next
                    sb.Append("</table:table-row>")
                Next
                sb.Append("</table:table>")
            Next
            sb.Append("</office:spreadsheet></office:body></office:document-content>")
            put("content.xml", sb.ToString())
        End Using
    End Sub

    ''' <summary>CSV that Excel opens with the right letters (UTF-8 with BOM); tables one after another.</summary>
    Public Sub WriteCsv(md As String, path As String)
        Dim sb As New StringBuilder()
        Dim first = True
        For Each sheet In SheetsFor(md)
            If Not first Then sb.AppendLine()
            first = False
            For Each row In sheet
                sb.AppendLine(String.Join(",", row.Select(Function(c) If(c.IndexOfAny({","c, """"c, ControlChars.Cr, ControlChars.Lf}) >= 0, """" & c.Replace("""", """""") & """", c))))
            Next
        Next
        File.WriteAllText(path, sb.ToString(), New UTF8Encoding(True))
    End Sub

    Private Function SheetName(i As Integer, hasTables As Boolean) As String
        Return If(hasTables, Localizer.T("Table") & " " & i, Localizer.T("Answer"))
    End Function

    Private Function ColName(i As Integer) As String
        Dim s = ""
        i += 1
        While i > 0
            Dim m = (i - 1) Mod 26
            s = ChrW(65 + m) & s
            i = (i - 1) \ 26
        End While
        Return s
    End Function

    '──────────────── text formats ────────────────
    Public Function PlainText(md As String) As String
        If String.IsNullOrEmpty(md) Then Return ""
        Return Markdown.ToPlainText(CleanLinks(md), Pipeline).Trim()
    End Function

    ' LocalMind's own links: pictures stay, songs / videos become their name, buttons go
    Private ReadOnly OutPicture As New Regex("!\[([^\]]*)\]\(https://outputs\.localmind/([^)\s]+)\)", RegexOptions.Compiled)
    Private ReadOnly OutLink As New Regex("(?<!!)\[([^\]]*)\]\(https://outputs\.localmind/[^)\s]+\)", RegexOptions.Compiled)
    Private ReadOnly ActionLink As New Regex("\[[^\]]*\]\(https://action\.localmind/[^)\s]*\)", RegexOptions.Compiled)

    Private Function CleanLinks(md As String) As String
        Return OutLink.Replace(ActionLink.Replace(md, ""), "$1")
    End Function

    ''' <summary>Markdown for other programs: the pictures point to their files on this PC.</summary>
    Public Function PortableMarkdown(md As String) As String
        Dim t = CleanLinks(If(md, ""))
        Return OutPicture.Replace(t, Function(m)
                                         Dim f = Path.Combine(MediaStudio.OutputFolder, Path.GetFileName(Uri.UnescapeDataString(m.Groups(2).Value)))
                                         Return $"![{m.Groups(1).Value}](<{f.Replace("\"c, "/"c)}>)"
                                     End Function)
    End Function

    ''' <summary>A web page that works on its own: the pictures are inside it.</summary>
    Public Function StandaloneHtml(md As String, title As String) As String
        Dim html = MarkdownRenderer.PrintHtml(md, title)
        Return Regex.Replace(html, "src=""https://outputs\.localmind/([^""]+)""",
                             Function(m)
                                 Try
                                     Dim f = Path.Combine(MediaStudio.OutputFolder, Path.GetFileName(Uri.UnescapeDataString(m.Groups(1).Value)))
                                     If Not File.Exists(f) Then Return m.Value
                                     Dim mime = MimeFor(Path.GetExtension(f).ToLowerInvariant())
                                     Return $"src=""data:{mime};base64,{Convert.ToBase64String(File.ReadAllBytes(f))}"""
                                 Catch
                                     Return m.Value
                                 End Try
                             End Function)
    End Function

    Private Function MimeFor(ext As String) As String
        Select Case ext
            Case ".jpg", ".jpeg" : Return "image/jpeg"
            Case ".webp" : Return "image/webp"
            Case ".gif" : Return "image/gif"
            Case Else : Return "image/png"
        End Select
    End Function

    '──────────────── Word, LibreOffice Writer, RTF (pandoc) ────────────────
    Public Function PandocPackage() As ExternalTools.ToolPackage
        Return New ExternalTools.ToolPackage With {.Name = "pandoc", .Flavor = "win64", .Repo = "jgm/pandoc", .ExeName = "pandoc.exe",
                                                   .AssetPatterns = {"^pandoc-[\d.]+-windows-x86_64\.zip$"}}
    End Function

    ''' <summary>docx / odt / rtf through pandoc (downloaded the first time, about 40 MB).</summary>
    Public Async Function PandocAsync(md As String, title As String, target As String, format As String,
                                      status As Action(Of String), ct As CancellationToken,
                                      Optional pandocExe As String = Nothing) As Task
        Dim pkg = PandocPackage()
        Dim exe = If(pandocExe, ExternalTools.FindExe(pkg))
        If exe Is Nothing Then
            status?.Invoke(Localizer.T("Getting the document converter (pandoc, once)…"))
            Await ExternalTools.InstallAsync(pkg, status, ct)
            exe = ExternalTools.FindExe(pkg)
            If exe Is Nothing Then Throw New Exception(Localizer.T("The document converter (pandoc) could not be installed."))
        End If
        Dim src = Path.Combine(Path.GetTempPath(), "localmind-export-" & Guid.NewGuid().ToString("N").Substring(0, 8) & ".md")
        File.WriteAllText(src, PortableMarkdown(md), New UTF8Encoding(False))
        Dim tmp = target & ".part"
        Try
            Dim psi As New ProcessStartInfo(exe) With {.UseShellExecute = False, .CreateNoWindow = True, .RedirectStandardError = True, .RedirectStandardOutput = True}
            For Each a In {"-f", "markdown+tex_math_dollars+pipe_tables+emoji", "-t", format, "-s", "-o", tmp,
                           "--resource-path=" & MediaStudio.OutputFolder, "--metadata", "title=" & If(title, ""), src}
                psi.ArgumentList.Add(a)
            Next
            status?.Invoke(Localizer.T("Making the document…"))
            Using p = Process.Start(psi)
                Dim errTask = p.StandardError.ReadToEndAsync()
                Dim outTask = p.StandardOutput.ReadToEndAsync()
                Await p.WaitForExitAsync(ct)
                Dim errText = Await errTask
                Await outTask
                If p.ExitCode <> 0 OrElse Not File.Exists(tmp) Then Throw New Exception("pandoc: " & errText.Trim())
            End Using
            If File.Exists(target) Then File.Delete(target)
            File.Move(tmp, target)
        Finally
            Try : File.Delete(src) : Catch : End Try
            Try
                If File.Exists(tmp) Then File.Delete(tmp)
            Catch
            End Try
        End Try
    End Function
End Module
