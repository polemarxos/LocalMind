Imports System
Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.IO
Imports System.IO.Compression
Imports System.Linq
Imports System.Net
Imports System.Net.Http
Imports System.Net.Sockets
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Threading
Imports System.Threading.Tasks
Imports Newtonsoft.Json
Imports Newtonsoft.Json.Linq
Imports UglyToad.PdfPig

''' <summary>A passage found in the user's documents or on the web, used to ground an answer.</summary>
Public Class SourceRef
    Public Property Label As String        ' "[1]" / "[W1]"
    Public Property Title As String        ' file name or page title
    Public Property Location As String     ' path or URL
    Public Property Detail As String       ' "p. 3"
    Public Property Text As String
    Public Property Score As Double
End Class

''' <summary>
''' "Chat with your documents": files/folders are split into passages, embedded with a small
''' local embedding model (EmbeddingGemma via llama-server --embedding) and searched by meaning.
''' </summary>
Public Module Knowledge

    Public Const EmbedRepo As String = "ggml-org/embeddinggemma-300M-GGUF"
    Public Const EmbedFile As String = "embeddinggemma-300M-Q8_0.gguf"
    Private Const ChunkChars As Integer = 1100
    Private Const ChunkOverlap As Integer = 180

    Private ReadOnly _lock As New SemaphoreSlim(1, 1)
    Private _index As KnowledgeIndex
    Private _embedProc As Process
    Private _embedPort As Integer

    Public ReadOnly SupportedExtensions As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
        ".pdf", ".txt", ".md", ".markdown", ".csv", ".json", ".xml", ".log", ".html", ".htm", ".docx", ".rtf",
        ".vb", ".cs", ".py", ".js", ".ts", ".java", ".cpp", ".c", ".h", ".sql", ".yaml", ".yml", ".ini", ".srt"}

    '──────────────── storage ────────────────
    Private ReadOnly Property Folder As String
        Get
            Dim d = Path.Combine(AppPaths.Roaming, "Knowledge")
            Directory.CreateDirectory(d)
            Return d
        End Get
    End Property

    Public Class KnowledgeIndex
        Public Property Sources As New List(Of String)()           ' files and folders the user added
        Public Property Docs As New List(Of IndexedDoc)()
    End Class

    Public Class IndexedDoc
        Public Property Path As String
        Public Property Modified As Long
        Public Property Chunks As New List(Of Chunk)()
    End Class

    Public Class Chunk
        Public Property Text As String
        Public Property Page As String
        Public Property Vector As Single()
    End Class

    Private Function LoadIndex() As KnowledgeIndex
        If _index IsNot Nothing Then Return _index
        _index = New KnowledgeIndex()
        Try
            Dim f = Path.Combine(Folder, "sources.json")
            If File.Exists(f) Then _index.Sources = JsonConvert.DeserializeObject(Of List(Of String))(File.ReadAllText(f))
            Dim bin = Path.Combine(Folder, "index.bin")
            If File.Exists(bin) Then
                Using br As New BinaryReader(File.OpenRead(bin), Encoding.UTF8)
                    Dim n = br.ReadInt32()
                    For i = 1 To n
                        Dim d As New IndexedDoc With {.Path = br.ReadString(), .Modified = br.ReadInt64()}
                        Dim c = br.ReadInt32()
                        For j = 1 To c
                            Dim ch As New Chunk With {.Text = br.ReadString(), .Page = br.ReadString()}
                            Dim dim_ = br.ReadInt32()
                            ch.Vector = New Single(dim_ - 1) {}
                            For k = 0 To dim_ - 1
                                ch.Vector(k) = br.ReadSingle()
                            Next
                            d.Chunks.Add(ch)
                        Next
                        _index.Docs.Add(d)
                    Next
                End Using
            End If
        Catch
            _index = New KnowledgeIndex With {.Sources = _index.Sources}
        End Try
        Return _index
    End Function

    Private Sub SaveIndex()
        File.WriteAllText(Path.Combine(Folder, "sources.json"), JsonConvert.SerializeObject(_index.Sources, Formatting.Indented))
        Dim tmp = Path.Combine(Folder, "index.bin.tmp")
        Using bw As New BinaryWriter(File.Create(tmp), Encoding.UTF8)
            bw.Write(_index.Docs.Count)
            For Each d In _index.Docs
                bw.Write(d.Path) : bw.Write(d.Modified) : bw.Write(d.Chunks.Count)
                For Each c In d.Chunks
                    bw.Write(c.Text) : bw.Write(If(c.Page, ""))
                    bw.Write(c.Vector.Length)
                    For Each v In c.Vector
                        bw.Write(v)
                    Next
                Next
            Next
        End Using
        File.Move(tmp, Path.Combine(Folder, "index.bin"), True)
    End Sub

    Public Function Sources() As List(Of String)
        Return LoadIndex().Sources.ToList()
    End Function

    Public Function Stats() As (Docs As Integer, Chunks As Integer)
        Dim idx = LoadIndex()
        Return (idx.Docs.Count, idx.Docs.Sum(Function(d) d.Chunks.Count))
    End Function

    Public Sub AddSource(pathOrFolder As String)
        Dim idx = LoadIndex()
        If Not idx.Sources.Contains(pathOrFolder, StringComparer.OrdinalIgnoreCase) Then idx.Sources.Add(pathOrFolder)
        SaveIndex()
    End Sub

    Public Sub RemoveSource(pathOrFolder As String)
        Dim idx = LoadIndex()
        idx.Sources.RemoveAll(Function(s) String.Equals(s, pathOrFolder, StringComparison.OrdinalIgnoreCase))
        SaveIndex()
    End Sub

    '──────────────── indexing ────────────────
    ''' <summary>(Re)indexes all sources; unchanged files are skipped. Returns a summary.</summary>
    Public Async Function IndexAsync(progress As Action(Of String), ct As CancellationToken) As Task(Of String)
        Await _lock.WaitAsync(ct)
        Try
            Dim idx = LoadIndex()
            Dim files = idx.Sources.SelectMany(AddressOf ExpandSource).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            Dim keep As New List(Of IndexedDoc)
            Dim added = 0, skipped = 0, failed = 0
            Await EnsureEmbedderAsync(progress, ct)
            For i = 0 To files.Count - 1
                ct.ThrowIfCancellationRequested()
                Dim f = files(i)
                Dim mod_ = File.GetLastWriteTimeUtc(f).Ticks
                Dim existing = idx.Docs.FirstOrDefault(Function(d) String.Equals(d.Path, f, StringComparison.OrdinalIgnoreCase) AndAlso d.Modified = mod_)
                If existing IsNot Nothing Then
                    keep.Add(existing)
                    skipped += 1
                    Continue For
                End If
                progress?.Invoke($"Reading {i + 1}/{files.Count}: {Path.GetFileName(f)}")
                Try
                    Dim chunks = ChunkDocument(f)
                    If chunks.Count = 0 Then Continue For
                    For b = 0 To chunks.Count - 1 Step 16
                        Dim batch = chunks.Skip(b).Take(16).ToList()
                        Dim vecs = Await EmbedAsync(batch.Select(Function(c) "title: " & Path.GetFileName(f) & " | text: " & c.Text).ToList(), ct)
                        For k = 0 To batch.Count - 1
                            batch(k).Vector = vecs(k)
                        Next
                        progress?.Invoke($"Indexing {i + 1}/{files.Count}: {Path.GetFileName(f)} ({Math.Min(b + 16, chunks.Count)}/{chunks.Count})")
                    Next
                    keep.Add(New IndexedDoc With {.Path = f, .Modified = mod_, .Chunks = chunks})
                    added += 1
                Catch ex As OperationCanceledException
                    Throw
                Catch
                    failed += 1
                End Try
            Next
            idx.Docs = keep
            SaveIndex()
            Return $"{keep.Count} documents ready ({added} new/changed, {skipped} unchanged" & If(failed > 0, $", {failed} unreadable", "") & ")."
        Finally
            _lock.Release()
        End Try
    End Function

    Private Function ExpandSource(src As String) As IEnumerable(Of String)
        Try
            If File.Exists(src) Then Return {src}
            If Directory.Exists(src) Then
                Return Directory.EnumerateFiles(src, "*.*", New EnumerationOptions With {.RecurseSubdirectories = True, .IgnoreInaccessible = True}).
                    Where(Function(f) SupportedExtensions.Contains(Path.GetExtension(f)) AndAlso New FileInfo(f).Length < 60L * 1024 * 1024).
                    Take(5000).ToList()
            End If
        Catch
        End Try
        Return Array.Empty(Of String)()
    End Function

    ''' <summary>Extracts text (per page for PDFs) and cuts it into overlapping passages.</summary>
    Friend Function ChunkDocument(f As String) As List(Of Chunk)
        Dim pages As New List(Of (Page As String, Text As String))
        Dim ext = Path.GetExtension(f).ToLowerInvariant()
        Select Case ext
            Case ".pdf"
                Using doc = PdfDocument.Open(f)
                    For Each p In doc.GetPages()
                        pages.Add(("p. " & p.Number, p.Text))
                    Next
                End Using
            Case ".docx"
                pages.Add(("", ReadDocx(f)))
            Case ".html", ".htm"
                pages.Add(("", WebSearch.HtmlToText(File.ReadAllText(f))))
            Case ".rtf"
                pages.Add(("", Regex.Replace(File.ReadAllText(f), "\\[a-z]+-?\d* ?|[{}]", "")))
            Case Else
                pages.Add(("", File.ReadAllText(f)))
        End Select

        Dim result As New List(Of Chunk)
        For Each pg In pages
            Dim t = Regex.Replace(If(pg.Text, ""), "[ \t]+", " ").Trim()
            If t.Length < 20 Then Continue For
            Dim pos = 0
            While pos < t.Length
                Dim len = Math.Min(ChunkChars, t.Length - pos)
                ' Prefer to cut at a paragraph or sentence end.
                If pos + len < t.Length Then
                    Dim cut = t.LastIndexOfAny({ChrW(10), "."c, "!"c, "?"c}, pos + len - 1, Math.Min(len, 300))
                    If cut > pos + 400 Then len = cut - pos + 1
                End If
                result.Add(New Chunk With {.Text = t.Substring(pos, len).Trim(), .Page = pg.Page})
                If pos + len >= t.Length Then Exit While
                pos += Math.Max(1, len - ChunkOverlap)
            End While
        Next
        Return result
    End Function

    Private Function ReadDocx(f As String) As String
        Using z = ZipFile.OpenRead(f)
            Dim e = z.GetEntry("word/document.xml")
            If e Is Nothing Then Return ""
            Using r As New StreamReader(e.Open())
                Dim xml = r.ReadToEnd()
                xml = Regex.Replace(xml, "</w:p>", vbLf)
                xml = Regex.Replace(xml, "<w:tab/>", " ")
                Return WebUtility.HtmlDecode(Regex.Replace(xml, "<[^>]+>", ""))
            End Using
        End Using
    End Function

    '──────────────── search ────────────────
    ''' <summary>Top passages for a question (cosine similarity on normalized vectors).</summary>
    Public Async Function SearchAsync(query As String, topK As Integer, ct As CancellationToken) As Task(Of List(Of SourceRef))
        Dim idx = LoadIndex()
        If idx.Docs.Count = 0 OrElse String.IsNullOrWhiteSpace(query) Then Return New List(Of SourceRef)()
        Await EnsureEmbedderAsync(Nothing, ct)
        Dim q = (Await EmbedAsync(New List(Of String) From {"task: search result | query: " & query}, ct))(0)
        Dim hits As New List(Of (Score As Double, Doc As IndexedDoc, Chunk As Chunk))
        For Each d In idx.Docs
            For Each c In d.Chunks
                If c.Vector Is Nothing OrElse c.Vector.Length <> q.Length Then Continue For
                Dim s As Double = 0
                For k = 0 To q.Length - 1
                    s += q(k) * c.Vector(k)
                Next
                hits.Add((s, d, c))
            Next
        Next
        Dim n = 0
        Return hits.OrderByDescending(Function(h) h.Score).Take(topK).Select(Function(h)
                                                                              n += 1
                                                                              Return New SourceRef With {
                                                                                .Label = $"[{n}]", .Title = Path.GetFileName(h.Doc.Path),
                                                                                .Location = h.Doc.Path, .Detail = h.Chunk.Page,
                                                                                .Text = h.Chunk.Text, .Score = h.Score}
                                                                          End Function).ToList()
    End Function

    '──────────────── embedding engine ────────────────
    Private ReadOnly Property EmbedModelPath As String
        Get
            Return Path.Combine(AppCore.Library.Folder, "Embeddings", EmbedFile)
        End Get
    End Property

    ''' <summary>Starts a small llama-server in embedding mode (downloads model/engine on first use).</summary>
    Private Async Function EnsureEmbedderAsync(progress As Action(Of String), ct As CancellationToken) As Task
        If _embedProc IsNot Nothing AndAlso Not _embedProc.HasExited Then Return
        If Not AppCore.Engine.IsInstalled() Then
            progress?.Invoke("Installing the AI engine…")
            Await AppCore.Engine.InstallAsync(Nothing, ct, reloadModel:=False)
        End If
        If Not File.Exists(EmbedModelPath) Then
            Await Downloader.DownloadFileAsync($"https://huggingface.co/{EmbedRepo}/resolve/main/{EmbedFile}", EmbedModelPath,
                                               AppCore.Settings.HuggingFaceToken,
                                               Sub(d, t, s) progress?.Invoke($"Downloading the search model… {If(t > 0, d * 100 \ t, 0)}%"), ct)
        End If
        Dim l As New TcpListener(IPAddress.Loopback, 0)
        l.Start()
        _embedPort = CType(l.LocalEndpoint, IPEndPoint).Port
        l.Stop()
        Dim args = $"-m ""{EmbedModelPath}"" --embedding --pooling mean --host 127.0.0.1 --port {_embedPort} -c 2048 -b 2048 -ub 2048 -np 1 --no-webui"
        If AppCore.Engine.EffectiveVariant = "cpu" Then args &= " -ngl 0"
        _embedProc = AppCore.Engine.LaunchHelper(args)
        Dim sw = Stopwatch.StartNew()
        Do
            ct.ThrowIfCancellationRequested()
            If _embedProc.HasExited Then Throw New Exception("The search model could not start. See Tools → Engine log.")
            Try
                Using r = Await OpenAiCompatService.Http.GetAsync($"http://127.0.0.1:{_embedPort}/health", ct)
                    If r.IsSuccessStatusCode Then Exit Do
                End Using
            Catch ex As HttpRequestException
            End Try
            If sw.Elapsed.TotalSeconds > 120 Then Throw New TimeoutException("The search model did not start in time.")
            Await Task.Delay(250, ct)
        Loop
    End Function

    Private Async Function EmbedAsync(texts As List(Of String), ct As CancellationToken) As Task(Of List(Of Single()))
        Dim body As New JObject From {{"input", New JArray(texts.Select(Function(t) If(t.Length > 6000, t.Substring(0, 6000), t)).ToArray())}, {"model", "embed"}}
        Using content As New StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json")
            Using r = Await OpenAiCompatService.Http.PostAsync($"http://127.0.0.1:{_embedPort}/v1/embeddings", content, ct)
                Dim txt = Await r.Content.ReadAsStringAsync(ct)
                If Not r.IsSuccessStatusCode Then Throw New Exception("Embedding failed: " & OpenAiCompatService.ExtractError(txt))
                Dim data = CType(JObject.Parse(txt)("data"), JArray).OrderBy(Function(d) d.Value(Of Integer)("index"))
                Return data.Select(Function(d) Normalize(d("embedding").Select(Function(v) CSng(v)).ToArray())).ToList()
            End Using
        End Using
    End Function

    Private Function Normalize(v As Single()) As Single()
        Dim n = Math.Sqrt(v.Sum(Function(x) CDbl(x) * x))
        If n > 0 Then
            For i = 0 To v.Length - 1
                v(i) = CSng(v(i) / n)
            Next
        End If
        Return v
    End Function

    Public Sub StopEmbedder()
        Try
            If _embedProc IsNot Nothing AndAlso Not _embedProc.HasExited Then _embedProc.Kill(True)
        Catch
        End Try
        _embedProc = Nothing
    End Sub
End Module
