Imports System
Imports System.Collections.Generic
Imports System.Linq
Imports System.Net
Imports System.Net.Http
Imports System.Text.RegularExpressions
Imports System.Threading
Imports System.Threading.Tasks
Imports Newtonsoft.Json.Linq

''' <summary>
''' Web search for up-to-date answers. DuckDuckGo needs no key; Brave Search (free API key)
''' and a self-hosted SearXNG are also supported. The top pages are fetched and their main
''' text is passed to the model with [W1], [W2]… labels so it can cite them.
''' </summary>
Public Module WebSearch

    Private ReadOnly Http As HttpClient = CreateClient()

    Private Function CreateClient() As HttpClient
        Dim h As New SocketsHttpHandler() With {.AutomaticDecompression = DecompressionMethods.All, .AllowAutoRedirect = True,
                                                .ConnectTimeout = TimeSpan.FromSeconds(10)}
        Dim c As New HttpClient(h) With {.Timeout = TimeSpan.FromSeconds(15)}
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36 LocalMind")
        c.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en,el;q=0.8")
        Return c
    End Function

    Public Class SearchHit
        Public Property Title As String
        Public Property Url As String
        Public Property Snippet As String
    End Class

    ''' <summary>Searches and reads the best pages. Returns sources ready to put in the prompt.</summary>
    Public Async Function SearchAndReadAsync(query As String, ct As CancellationToken) As Task(Of List(Of SourceRef))
        Dim s = AppCore.Settings
        Dim hits As List(Of SearchHit)
        Select Case If(s.WebSearchProvider, "duckduckgo").ToLowerInvariant()
            Case "brave" : hits = Await BraveAsync(query, s.BraveApiKey, ct)
            Case "searxng" : hits = Await SearxAsync(query, s.SearxngUrl, ct)
            Case Else
                ' Keyless: DuckDuckGo, then Wikipedia if DuckDuckGo is rate-limiting this connection.
                Dim ddgError As Exception = Nothing
                Try
                    hits = Await DuckDuckGoAsync(query, ct)
                Catch ex As OperationCanceledException When ct.IsCancellationRequested
                    Throw
                Catch ex As Exception
                    ddgError = ex
                    hits = Nothing
                End Try
                If hits Is Nothing OrElse hits.Count = 0 Then
                    hits = Await WikipediaAsync(query, ct)
                    If hits.Count = 0 Then Throw New Exception("Web search is blocked right now (" & ddgError?.Message & "). " &
                                                               "For reliable search add a free Brave Search key in Settings → Web & voice.")
                End If
        End Select
        hits = hits.Where(Function(h) h.Url.StartsWith("http")).GroupBy(Function(h) h.Url).Select(Function(g) g.First()).Take(8).ToList()

        Dim readCount = Math.Max(0, Math.Min(5, s.WebPagesToRead))
        Dim pages = Await Task.WhenAll(hits.Take(readCount).Select(Function(h) FetchTextAsync(h.Url, ct)))
        Dim result As New List(Of SourceRef)
        For i = 0 To hits.Count - 1
            Dim body = If(i < pages.Length, pages(i), Nothing)
            Dim text = If(String.IsNullOrWhiteSpace(body), hits(i).Snippet, hits(i).Snippet & vbLf & body)
            If String.IsNullOrWhiteSpace(text) Then Continue For
            result.Add(New SourceRef With {.Label = $"[W{result.Count + 1}]", .Title = hits(i).Title, .Location = hits(i).Url,
                                           .Text = Truncate(text, If(i < readCount, 2500, 400))})
        Next
        Return result
    End Function

    Private Async Function DuckDuckGoAsync(q As String, ct As CancellationToken) As Task(Of List(Of SearchHit))
        Dim html = Await Http.GetStringAsync("https://html.duckduckgo.com/html/?q=" & Uri.EscapeDataString(q), ct)
        Dim list = ParseDuckDuckGo(html)
        If list.Count = 0 Then Throw New Exception("DuckDuckGo returned no results (it may be rate-limiting)")
        Return list
    End Function

    Friend Function ParseDuckDuckGo(html As String) As List(Of SearchHit)
        Dim list As New List(Of SearchHit)
        Dim anchors = Regex.Matches(html, "<a[^>]+class=""result__a""[^>]+href=""([^""]+)""[^>]*>([\s\S]*?)</a>")
        For i = 0 To anchors.Count - 1
            Dim m = anchors(i)
            ' Look for this result's snippet only up to the next result.
            Dim segStart = m.Index + m.Length
            Dim segEnd = If(i + 1 < anchors.Count, anchors(i + 1).Index, html.Length)
            Dim sn = Regex.Match(html.Substring(segStart, segEnd - segStart), "class=""result__snippet""[^>]*>([\s\S]*?)</a>")
            Dim url = WebUtility.HtmlDecode(m.Groups(1).Value)
            Dim uddg = Regex.Match(url, "[?&]uddg=([^&]+)")
            If uddg.Success Then url = Uri.UnescapeDataString(uddg.Groups(1).Value)
            If url.StartsWith("//") Then url = "https:" & url
            If url.Contains("duckduckgo.com/y.js") Then Continue For   ' ads
            list.Add(New SearchHit With {.Url = url, .Title = StripTags(m.Groups(2).Value), .Snippet = If(sn.Success, StripTags(sn.Groups(1).Value), "")})
        Next
        Return list
    End Function

    ''' <summary>Wikipedia full-text search (Greek Wikipedia for Greek questions).</summary>
    Private Async Function WikipediaAsync(q As String, ct As CancellationToken) As Task(Of List(Of SearchHit))
        Dim lang = If(Regex.IsMatch(q, "[\u0370-\u03FF]"), "el", "en")
        Using req As New HttpRequestMessage(HttpMethod.Get, $"https://{lang}.wikipedia.org/w/rest.php/v1/search/page?limit=6&q=" & Uri.EscapeDataString(q))
            ' Wikimedia asks for an identifying User-Agent.
            req.Headers.UserAgent.Clear()
            req.Headers.UserAgent.ParseAdd($"LocalMind/{Updater.CurrentVersion} (https://github.com/{Updater.Repo})")
            Using r = Await Http.SendAsync(req, ct)
                If Not r.IsSuccessStatusCode Then Return New List(Of SearchHit)()
                Dim o = JObject.Parse(Await r.Content.ReadAsStringAsync(ct))
                Return CType(o("pages"), JArray).Select(Function(p) New SearchHit With {
                    .Title = p.Value(Of String)("title") & " — Wikipedia",
                    .Url = $"https://{lang}.wikipedia.org/wiki/" & Uri.EscapeDataString(p.Value(Of String)("key")),
                    .Snippet = StripTags(p.Value(Of String)("excerpt"))}).ToList()
            End Using
        End Using
    End Function

    Private Async Function BraveAsync(q As String, key As String, ct As CancellationToken) As Task(Of List(Of SearchHit))
        If String.IsNullOrWhiteSpace(key) Then Throw New Exception("Add your Brave Search API key in Settings (free at brave.com/search/api).")
        Using req As New HttpRequestMessage(HttpMethod.Get, "https://api.search.brave.com/res/v1/web/search?count=8&q=" & Uri.EscapeDataString(q))
            req.Headers.Add("X-Subscription-Token", key.Trim())
            req.Headers.Accept.ParseAdd("application/json")
            Using r = Await Http.SendAsync(req, ct)
                r.EnsureSuccessStatusCode()
                Dim o = JObject.Parse(Await r.Content.ReadAsStringAsync(ct))
                Return If(o("web")?("results")?.Select(Function(x) New SearchHit With {
                    .Title = StripTags(x.Value(Of String)("title")), .Url = x.Value(Of String)("url"),
                    .Snippet = StripTags(x.Value(Of String)("description"))}).ToList(), New List(Of SearchHit))
            End Using
        End Using
    End Function

    Private Async Function SearxAsync(q As String, baseUrl As String, ct As CancellationToken) As Task(Of List(Of SearchHit))
        If String.IsNullOrWhiteSpace(baseUrl) Then Throw New Exception("Set your SearXNG address in Settings.")
        Dim o = JObject.Parse(Await Http.GetStringAsync(baseUrl.TrimEnd("/"c) & "/search?format=json&q=" & Uri.EscapeDataString(q), ct))
        Return CType(o("results"), JArray).Select(Function(x) New SearchHit With {
            .Title = x.Value(Of String)("title"), .Url = x.Value(Of String)("url"), .Snippet = x.Value(Of String)("content")}).ToList()
    End Function

    Private Async Function FetchTextAsync(url As String, ct As CancellationToken) As Task(Of String)
        Try
            Using cts = CancellationTokenSource.CreateLinkedTokenSource(ct)
                cts.CancelAfter(TimeSpan.FromSeconds(8))
                Using r = Await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token)
                    If Not r.IsSuccessStatusCode Then Return Nothing
                    Dim type = r.Content.Headers.ContentType?.MediaType
                    If type IsNot Nothing AndAlso Not type.Contains("html") AndAlso Not type.StartsWith("text") Then Return Nothing
                    Dim html = Await r.Content.ReadAsStringAsync(cts.Token)
                    Return Truncate(HtmlToText(html), 6000)
                End Using
            End Using
        Catch
            Return Nothing
        End Try
    End Function

    ''' <summary>Readable text from a web page (drops scripts, menus, footers, tags).</summary>
    Public Function HtmlToText(html As String) As String
        If String.IsNullOrEmpty(html) Then Return ""
        Dim m = Regex.Match(html, "<(article|main)[\s>][\s\S]*?</\1>", RegexOptions.IgnoreCase)
        Dim t = If(m.Success AndAlso m.Length > 500, m.Value, html)
        t = Regex.Replace(t, "<(script|style|noscript|svg|nav|header|footer|aside|form|iframe)[\s\S]*?</\1>", " ", RegexOptions.IgnoreCase)
        t = Regex.Replace(t, "<!--[\s\S]*?-->", " ")
        t = Regex.Replace(t, "<(br|/p|/div|/li|/h\d|/tr)[^>]*>", vbLf, RegexOptions.IgnoreCase)
        t = Regex.Replace(t, "<[^>]+>", " ")
        t = WebUtility.HtmlDecode(t)
        t = Regex.Replace(t, "[ \t\r\f\v]+", " ")
        t = Regex.Replace(t, "\n\s*\n+", vbLf)
        Return t.Trim()
    End Function

    Private Function StripTags(s As String) As String
        If s Is Nothing Then Return ""
        Return WebUtility.HtmlDecode(Regex.Replace(s, "<[^>]+>", "")).Trim()
    End Function

    Private Function Truncate(s As String, n As Integer) As String
        If s Is Nothing OrElse s.Length <= n Then Return s
        Return s.Substring(0, n) & "…"
    End Function
End Module
