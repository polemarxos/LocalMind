Imports System.IO
Imports System.Reflection
Imports System.Text
Imports System.Text.RegularExpressions
Imports Markdig

Public Module MarkdownRenderer

    ' DisableHtml: model output is untrusted — raw <script>/<img onerror> must never run in the chat view.
    Private ReadOnly Pipeline As MarkdownPipeline =
        New MarkdownPipelineBuilder() _
            .UseAdvancedExtensions() _
            .UseEmphasisExtras() _
            .UseAutoLinks() _
            .UsePipeTables() _
            .UseTaskLists() _
            .UseEmojiAndSmiley() _
            .DisableHtml() _
            .Build()

    Public Function ToHtml(md As String) As String
        If md Is Nothing Then Return ""
        Return MediaLinks(Markdown.ToHtml(NormalizeMath(md), Pipeline))
    End Function

    ' Links to LocalMind's own outputs (songs, speech, videos) become players.
    Private ReadOnly MediaLink As New Regex("<a href=""(https://outputs\.localmind/[^""]+?\.(wav|mp3|mp4|webm))""[^>]*>([^<]*)</a>", RegexOptions.Compiled Or RegexOptions.IgnoreCase)

    Private Function MediaLinks(html As String) As String
        Return MediaLink.Replace(html, Function(m)
                                           Dim ext = m.Groups(2).Value.ToLowerInvariant()
                                           Dim isVideo = ext = "mp4" OrElse ext = "webm"
                                           Return $"<span class=""media""><span class=""media-title"">{m.Groups(3).Value}</span><br>" &
                                                  If(isVideo, $"<video src=""{m.Groups(1).Value}"" controls style=""max-width:100%""></video>",
                                                              $"<audio src=""{m.Groups(1).Value}"" controls style=""width:100%""></audio>") & "</span>"
                                       End Function)
    End Function

    Private ReadOnly CodeSplit As New Regex("(```[\s\S]*?(?:```|$)|`[^`\n]+`)", RegexOptions.Compiled)
    Private ReadOnly DisplayMath As New Regex("\\\[([\s\S]+?)\\\]", RegexOptions.Compiled)
    Private ReadOnly InlineMath As New Regex("\\\(([\s\S]+?)\\\)", RegexOptions.Compiled)

    ''' <summary>
    ''' Models usually write LaTeX as \( … \) and \[ … \]. In Markdown "\(" is just an escaped
    ''' bracket, so convert them to $…$ / $$…$$ (outside code) for the math extension + KaTeX.
    ''' </summary>
    Public Function NormalizeMath(md As String) As String
        If md.IndexOf("\(", StringComparison.Ordinal) < 0 AndAlso md.IndexOf("\[", StringComparison.Ordinal) < 0 Then Return md
        Dim parts = CodeSplit.Split(md)
        Dim sb As New StringBuilder(md.Length + 32)
        For Each part In parts
            If part.StartsWith("`") Then
                sb.Append(part)
            Else
                Dim t = DisplayMath.Replace(part, Function(m) vbLf & "$$" & vbLf & m.Groups(1).Value.Trim() & vbLf & "$$" & vbLf)
                t = InlineMath.Replace(t, Function(m) "$" & m.Groups(1).Value.Trim() & "$")
                sb.Append(t)
            End If
        Next
        Return sb.ToString()
    End Function

    Public Function BalanceFences(md As String) As String
        If String.IsNullOrEmpty(md) Then Return md
        Dim count = 0
        Dim i = 0
        While True
            i = md.IndexOf("```", i, StringComparison.Ordinal)
            If i < 0 Then Exit While
            count += 1
            i += 3
        End While
        If count Mod 2 = 1 Then Return md & vbLf & "```"
        Return md
    End Function

    Friend Function ReadResource(name As String) As String
        Using s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            If s Is Nothing Then Return ""
            Using r As New StreamReader(s)
                Return r.ReadToEnd()
            End Using
        End Using
    End Function

    Private _shell As String

    Public Function ShellHtml() As String
        If _shell IsNot Nothing Then Return _shell
        Dim sb As New StringBuilder()

        sb.AppendLine("<!DOCTYPE html>")
        sb.AppendLine("<html>")
        sb.AppendLine("<head>")
        sb.AppendLine("<meta charset=""utf-8"">")
        sb.AppendLine("<meta name=""viewport"" content=""width=device-width, initial-scale=1"">")
        ' highlight.js is embedded in the exe → syntax highlighting works offline.
        sb.AppendLine("<style>" & ReadResource("hljs-dark.min.css") & "</style>")
        sb.AppendLine("<script>" & ReadResource("hljs.min.js") & "</script>")
        ' KaTeX (fonts inlined) → formulas render offline.
        sb.AppendLine("<style>" & ReadResource("katex.min.css") & "</style>")
        sb.AppendLine("<script>" & ReadResource("katex.min.js") & "</script>")
        Dim cssStart = sb.Length
        sb.AppendLine("<style>")
        sb.AppendLine("  * { box-sizing: border-box; }")
        sb.AppendLine("  :root { color-scheme: dark; }")
        sb.AppendLine("  ::-webkit-scrollbar { width: 10px; height: 10px; } ::-webkit-scrollbar-track { background: transparent; }")
        sb.AppendLine("  ::-webkit-scrollbar-thumb { background: #3a3c42; border-radius: 5px; border: 2px solid #18181b; } ::-webkit-scrollbar-thumb:hover { background: #4a4c54; }")
        sb.AppendLine("  html, body { margin: 0; padding: 0; height: 100%; }")
        sb.AppendLine("  body {")
        sb.AppendLine("    font-family: 'Segoe UI', 'Inter', system-ui, sans-serif;")
        sb.AppendLine("    background: #18181b; color: #e4e4e7;")
        sb.AppendLine("    font-size: 14.5px; line-height: 1.65;")
        sb.AppendLine("    -webkit-font-smoothing: antialiased;")
        sb.AppendLine("  }")
        sb.AppendLine("  #chatBody { padding: 20px 28px 40px 28px; max-width: 980px; margin: 0 auto; }")
        sb.AppendLine("  #empty { color:#a1a1aa; text-align:center; margin-top:10vh; font-size:15px; }")
        sb.AppendLine("  #empty b { color:#fafafa; font-size:20px; display:block; margin-bottom:8px; }")
        sb.AppendLine("  #empty .logo { width:56px; height:56px; border-radius:16px; margin:0 auto 14px auto; background:linear-gradient(135deg,#7c3aed,#2563eb 55%,#06b6d4); display:flex; align-items:center; justify-content:center; font-size:28px; color:#fff; }")
        sb.AppendLine("  #empty h1 { color:#fafafa; font-size:26px; font-weight:600; margin:0 0 6px 0; }")
        sb.AppendLine("  #empty .sub { color:#a1a1aa; margin-bottom:26px; }")
        sb.AppendLine("  #empty .steps { display:flex; gap:12px; justify-content:center; flex-wrap:wrap; margin:0 auto; max-width:760px; }")
        sb.AppendLine("  #empty .step { flex:1 1 200px; background:#222327; border:1px solid #34363b; border-radius:12px; padding:14px 16px; text-align:left; }")
        sb.AppendLine("  #empty .step .n { display:inline-block; width:22px; height:22px; border-radius:11px; background:#2563eb; color:#fff; text-align:center; font-size:12px; line-height:22px; margin-right:8px; font-weight:600; }")
        sb.AppendLine("  #empty .step .t { color:#fafafa; font-weight:600; }")
        sb.AppendLine("  #empty .step .d { color:#a1a1aa; font-size:13px; margin-top:6px; }")
        sb.AppendLine("  #empty .cards { display:grid; grid-template-columns:repeat(auto-fit,minmax(200px,1fr)); gap:10px; max-width:760px; margin:0 auto; }")
        sb.AppendLine("  #empty .card { background:#222327; border:1px solid #34363b; border-radius:12px; padding:12px 14px; text-align:left; cursor:pointer; transition:background .12s,border-color .12s; }")
        sb.AppendLine("  #empty .card:hover { background:#2a2b31; border-color:#2563eb; }")
        sb.AppendLine("  #empty .card .t { color:#fafafa; font-weight:600; font-size:14px; }")
        sb.AppendLine("  #empty .card .d { color:#a1a1aa; font-size:12.5px; margin-top:3px; }")
        sb.AppendLine("  #empty .hint { color:#71717a; font-size:12.5px; margin-top:22px; }")
        sb.AppendLine("  .msg { margin-bottom: 20px; }")
        sb.AppendLine("  .msg.user { display: flex; justify-content: flex-end; }")
        sb.AppendLine("  .msg.user > div { max-width: 78%; }")
        sb.AppendLine("  .msg.user .bubble {")
        sb.AppendLine("    background: #2563eb; color: #fff; padding: 10px 15px;")
        sb.AppendLine("    border-radius: 16px 16px 4px 16px;")
        sb.AppendLine("    white-space: pre-wrap; word-wrap: break-word;")
        sb.AppendLine("  }")
        sb.AppendLine("  .thumbs { display:flex; flex-wrap:wrap; gap:6px; justify-content:flex-end; margin-bottom:6px; }")
        sb.AppendLine("  .thumbs img { max-height: 140px; max-width: 220px; border-radius: 8px; border:1px solid #333; margin:0; cursor:zoom-in; }")
        sb.AppendLine("  .msg.assistant .bubble {")
        sb.AppendLine("    background: #222327; padding: 14px 18px;")
        sb.AppendLine("    border-radius: 16px 16px 16px 4px; border: 1px solid #34363b;")
        sb.AppendLine("    max-width: 96%; overflow-wrap: break-word;")
        sb.AppendLine("  }")
        sb.AppendLine("  .msg .role { font-size: 11px; color: #888; margin-bottom: 6px; letter-spacing: .3px; text-transform: uppercase; }")
        sb.AppendLine("  .msg.user .role { text-align: right; }")
        sb.AppendLine("  .uactions { display:flex; gap:4px; justify-content:flex-end; margin-top:4px; opacity:0; transition:opacity .15s; }")
        sb.AppendLine("  .msg.user:hover .uactions { opacity:1; }")
        sb.AppendLine("  body.busy .uactions .ua:first-child { display:none; }")
        sb.AppendLine("  .ua { background:transparent; color:#a1a1aa; border:1px solid transparent; border-radius:6px; padding:2px 8px; font-size:11.5px; cursor:pointer; font-family:inherit; }")
        sb.AppendLine("  .ua:hover { background:#2a2b30; color:#e4e4e7; border-color:#3a3b41; }")
        sb.AppendLine("  .msg.user.editing > .editbox { width:78%; }")
        sb.AppendLine("  .editbox { background:#1c1d21; border:1px solid #2563eb; border-radius:14px; padding:10px 12px; box-shadow:0 0 0 3px rgba(37,99,235,.18); }")
        sb.AppendLine("  .editbox textarea { width:100%; box-sizing:border-box; background:transparent; color:#f4f4f5; border:none; outline:none; resize:none; font:inherit; line-height:1.5; }")
        sb.AppendLine("  .editbar { display:flex; gap:8px; align-items:center; justify-content:flex-end; margin-top:8px; }")
        sb.AppendLine("  .ehint { color:#71717a; font-size:11px; margin-right:auto; }")
        sb.AppendLine("  .editbar button { border-radius:8px; padding:5px 14px; font-size:12.5px; cursor:pointer; font-family:inherit; }")
        sb.AppendLine("  .ecancel { background:transparent; color:#d4d4d8; border:1px solid #3f3f46; }")
        sb.AppendLine("  .ecancel:hover { background:#27272a; }")
        sb.AppendLine("  .esend { background:#2563eb; color:#fff; border:1px solid #2563eb; }")
        sb.AppendLine("  .esend:hover { background:#1d4ed8; }")
        sb.AppendLine("  .meta { font-size: 11px; color: #777; margin-top: 6px; display:flex; gap:10px; align-items:center; }")
        sb.AppendLine("  .meta button { background:#333; color:#ccc; border:1px solid #444; border-radius:4px; padding:1px 8px; font-size:11px; cursor:pointer; }")
        sb.AppendLine("  .meta button:hover { background:#444; }")
        sb.AppendLine("  .bubble p { margin: 0 0 12px 0; }")
        sb.AppendLine("  .bubble p:last-child { margin-bottom: 0; }")
        sb.AppendLine("  .bubble strong, .bubble b { color: #ffffff; font-weight: 700; }")
        sb.AppendLine("  .bubble em, .bubble i { color: #e0c98b; font-style: italic; }")
        sb.AppendLine("  .bubble del { color: #777; }")
        sb.AppendLine("  .bubble mark { background: #4a3f17; color: #ffe98a; padding: 0 3px; border-radius: 2px; }")
        sb.AppendLine("  .bubble h1, .bubble h2, .bubble h3, .bubble h4, .bubble h5, .bubble h6 {")
        sb.AppendLine("    color: #ffffff; margin: 20px 0 10px 0; line-height: 1.3; font-weight: 600;")
        sb.AppendLine("  }")
        sb.AppendLine("  .bubble h1:first-child, .bubble h2:first-child, .bubble h3:first-child { margin-top: 0; }")
        sb.AppendLine("  .bubble h1 { font-size: 1.6em; border-bottom: 1px solid #3a3a3a; padding-bottom: 6px; }")
        sb.AppendLine("  .bubble h2 { font-size: 1.35em; border-bottom: 1px solid #333; padding-bottom: 4px; }")
        sb.AppendLine("  .bubble h3 { font-size: 1.15em; color: #4ea1ff; }")
        sb.AppendLine("  .bubble h4 { font-size: 1.05em; color: #9cdcfe; }")
        sb.AppendLine("  .bubble h5 { font-size: 1em; color: #9cdcfe; }")
        sb.AppendLine("  .bubble h6 { font-size: 0.9em; color: #888; text-transform: uppercase; letter-spacing: .5px; }")
        sb.AppendLine("  .bubble ul, .bubble ol { margin: 0 0 12px 0; padding-left: 24px; }")
        sb.AppendLine("  .bubble li { margin: 4px 0; }")
        sb.AppendLine("  .bubble li > p { margin: 0; }")
        sb.AppendLine("  .bubble li::marker { color: #4ea1ff; }")
        sb.AppendLine("  pre {")
        sb.AppendLine("    background: #1a1a1a !important; border: 1px solid #333;")
        sb.AppendLine("    border-radius: 8px; padding: 14px 16px; overflow-x: auto;")
        sb.AppendLine("    position: relative; margin: 12px 0; font-size: 13px; line-height: 1.5;")
        sb.AppendLine("  }")
        sb.AppendLine("  pre code.hljs { background: transparent; padding: 0; }")
        sb.AppendLine("  code { font-family: 'Cascadia Code', 'Consolas', 'Courier New', monospace; font-size: 13px; }")
        sb.AppendLine("  :not(pre) > code { background: #2d2d30; color: #ce9178; padding: 2px 6px; border-radius: 4px; font-size: 0.92em; }")
        sb.AppendLine("  .copy-btn {")
        sb.AppendLine("    position: absolute; top: 8px; right: 8px;")
        sb.AppendLine("    background: #3c3c3c; color: #ddd;")
        sb.AppendLine("    border: 1px solid #555; border-radius: 4px;")
        sb.AppendLine("    padding: 3px 10px; cursor: pointer; font-size: 11px;")
        sb.AppendLine("    opacity: 0; transition: opacity .15s;")
        sb.AppendLine("  }")
        sb.AppendLine("  pre:hover .copy-btn { opacity: 1; }")
        sb.AppendLine("  .copy-btn:hover { background: #4a4a4a; }")
        sb.AppendLine("  a { color: #4ea1ff; text-decoration: none; border-bottom: 1px dotted #4ea1ff; }")
        sb.AppendLine("  a:hover { color: #79b8ff; border-bottom-style: solid; }")
        sb.AppendLine("  blockquote {")
        sb.AppendLine("    border-left: 3px solid #4ea1ff; margin: 12px 0; padding: 6px 14px;")
        sb.AppendLine("    background: #2a2a2b; border-radius: 0 6px 6px 0; color: #bbb; font-style: italic;")
        sb.AppendLine("  }")
        sb.AppendLine("  blockquote p { margin: 4px 0; }")
        sb.AppendLine("  table { border-collapse: collapse; margin: 12px 0; width: 100%; font-size: 0.95em; }")
        sb.AppendLine("  th, td { border: 1px solid #3a3a3a; padding: 8px 12px; text-align: left; }")
        sb.AppendLine("  th { background: #2d2d30; color: #e4e4e7; font-weight: 600; }")
        sb.AppendLine("  tr:nth-child(even) td { background: #232324; }")
        sb.AppendLine("  hr { border: none; border-top: 1px solid #3a3a3a; margin: 20px 0; }")
        sb.AppendLine("  img { max-width: 100%; border-radius: 6px; margin: 8px 0; }")
        sb.AppendLine("  .cursor-inline::after { content: '\25CA'; animation: blink 1s steps(2) infinite; color: #4ea1ff; margin-left: 2px; }")
        sb.AppendLine("  @keyframes blink { 50% { opacity: 0; } }")
        sb.AppendLine("  .waiting { color:#888; font-style:italic; }
  .katex { font-size: 1.08em; } div.math { overflow-x: auto; overflow-y: hidden; margin: 10px 0; }")
        sb.AppendLine("  #lightbox { position:fixed; inset:0; background:rgba(0,0,0,.85); display:none; align-items:center; justify-content:center; z-index:9; cursor:zoom-out; }")
        sb.AppendLine("  #lightbox img { max-width:95%; max-height:95%; }")
        ' ---- thinking panel ----
        sb.AppendLine("  .thinking { background: #17171a; border-left: 3px solid #7c6cd4; border-radius: 6px; padding: 8px 12px; margin-bottom: 10px; font-size: 13px; color: #a0a0a8; }")
        sb.AppendLine("  .thinking summary { cursor: pointer; font-weight: 600; color: #9d8ee8; user-select: none; padding: 2px 0; outline: none; }")
        sb.AppendLine("  .thinking summary:hover { color: #b8aaf5; }")
        sb.AppendLine("  .thinking[open] summary { margin-bottom: 8px; }")
        sb.AppendLine("  .thinking-content { max-height: 320px; overflow-y: auto; padding-right: 8px; font-size: 12.5px; line-height: 1.55; color: #9a9aa2; border-top: 1px solid #2a2a2e; padding-top: 8px; }")
        sb.AppendLine("  .thinking-content p { margin: 0 0 8px 0; }")
        sb.AppendLine("  .thinking-content p:last-child { margin-bottom: 0; }")
        sb.AppendLine("  .thinking-content code { background: #26262b; color: #b8aaf5; padding: 1px 5px; border-radius: 3px; font-size: 11.5px; }")
        sb.AppendLine("  .thinking-content pre { background: #121216 !important; border: 1px solid #2a2a2e; padding: 8px 10px; font-size: 11.5px; margin: 6px 0; }")
        sb.AppendLine("  .thinking-content h1, .thinking-content h2, .thinking-content h3, .thinking-content h4, .thinking-content h5, .thinking-content h6 { color: #b8aaf5; font-size: 13px; margin: 8px 0 4px 0; }")
        sb.AppendLine("  .thinking-content ul, .thinking-content ol { padding-left: 20px; margin: 4px 0; }")
        sb.AppendLine("</style>")
        Dim themed = Theme.MapCss(sb.ToString(cssStart, sb.Length - cssStart))
        sb.Length = cssStart
        sb.Append(themed)
        sb.AppendLine("</head>")
        sb.AppendLine("<body>")
        sb.AppendLine("<div id='chatBody'></div>")
        sb.AppendLine("<div id='lightbox' onclick='this.style.display=""none""'><img id='lightboxImg'></div>")
        sb.AppendLine("<script>")
        sb.AppendLine(Js)
        sb.AppendLine("</script>")
        sb.AppendLine("</body>")
        sb.AppendLine("</html>")

        _shell = sb.ToString()
        Return _shell
    End Function

    Private Const Js As String = "
  var L = {};
  function tr(s) { return L[s] || s; }
  function esc(s) { return (s || '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;'); }
  // Only auto-scroll when the user is already at the bottom, so they can read back while a reply streams.
  function nearBottom() { return (window.innerHeight + window.scrollY) >= document.body.scrollHeight - 120; }
  var stick = true;
  window.addEventListener('scroll', function () { stick = nearBottom(); });
  function scroll(force) { if (force || stick) window.scrollTo(0, document.body.scrollHeight); }
  function body() { return document.getElementById('chatBody'); }
  function clearAll() { body().innerHTML = ''; stick = true; }
  function showEmpty(html) { body().innerHTML = '<div id=""empty"">' + html + '</div>'; }
  function usePrompt(el) { try { window.chrome.webview.postMessage({ type: 'prompt', text: el.getAttribute('data-p') }); } catch (e) {} }
  function removeEmpty() { var e = document.getElementById('empty'); if (e) e.remove(); }
  function zoom(src) { document.getElementById('lightboxImg').src = src; document.getElementById('lightbox').style.display = 'flex'; }

  function userNode(text, imgs, ui) {
    var d = document.createElement('div');
    d.className = 'msg user';
    d.setAttribute('data-u', ui);
    var thumbs = '';
    if (imgs && imgs.length) {
      thumbs = '<div class=""thumbs"">' + imgs.map(function (s) { return '<img src=""' + s + '"" onclick=""zoom(this.src)"">'; }).join('') + '</div>';
    }
    d.innerHTML = '<div><div class=""role"">' + tr('You') + '</div>' + thumbs + (text ? '<div class=""bubble"">' + esc(text) + '</div>' : '') +
      '<div class=""uactions""><button class=""ua"" title=""' + tr('Edit message') + '"">✏ ' + tr('Edit') + '</button><button class=""ua"">⧉ ' + tr('Copy') + '</button></div></div>';
    d.setAttribute('data-text', text || '');
    var bs = d.querySelectorAll('.ua');
    bs[0].onclick = function () { startEdit(d); };
    bs[1].onclick = function () { copyToClipboard(d.getAttribute('data-text'), bs[1]); };
    return d;
  }
  function appendUser(text, imgs) { removeEmpty(); body().appendChild(userNode(text, imgs, document.querySelectorAll('.msg.user').length)); scroll(true); }
  function setBusy(b) { if (b) document.body.classList.add('busy'); else document.body.classList.remove('busy'); }
  function startEdit(d) {
    if (document.body.classList.contains('busy') || d.classList.contains('editing')) return;
    var open = document.querySelector('.msg.user.editing'); if (open) open._cancel();
    var inner = d.firstChild, old = d.getAttribute('data-text');
    var box = document.createElement('div'); box.className = 'editbox';
    box.innerHTML = '<textarea rows=""3""></textarea><div class=""editbar""><span class=""ehint"">' + tr('Enter sends · Shift+Enter new line · Esc cancels') + '</span><button class=""ecancel"">' + tr('Cancel') + '</button><button class=""esend"">' + tr('Send') + '</button></div>';
    var ta = box.querySelector('textarea'); ta.value = old;
    function fit() { ta.style.height = 'auto'; ta.style.height = Math.min(ta.scrollHeight + 2, 360) + 'px'; }
    function cancel() { d.classList.remove('editing'); box.remove(); inner.style.display = ''; }
    function send() {
      var t = ta.value.replace(/\s+$/, '');
      if (!t.trim() && !d.querySelector('.thumbs')) return;
      if (t === old) { cancel(); return; }
      if (document.body.classList.contains('busy')) { box.querySelector('.ehint').textContent = tr('Wait for the answer to finish (or press Stop), then edit.'); return; }
      var b = d.querySelector('.bubble');
      cancel();
      if (b) b.textContent = t;
      d.setAttribute('data-edit', t); d.setAttribute('data-text', t);
      // everything after this message is replaced by the new answer
      while (d.nextSibling) d.parentNode.removeChild(d.nextSibling);
      try { window.chrome.webview.postMessage({ type: 'edit', index: parseInt(d.getAttribute('data-u'), 10), text: t }); } catch (e) {}
    }
    d._cancel = cancel;
    box.querySelector('.ecancel').onclick = cancel;
    box.querySelector('.esend').onclick = send;
    ta.addEventListener('input', fit);
    ta.addEventListener('keydown', function (e) {
      if (e.key === 'Escape') { e.preventDefault(); cancel(); }
      else if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); send(); }
    });
    inner.style.display = 'none'; d.classList.add('editing'); d.appendChild(box);
    fit(); ta.focus(); ta.setSelectionRange(ta.value.length, ta.value.length);
  }

  function assistantNode(id) {
    var d = document.createElement('div');
    d.className = 'msg assistant'; d.id = id;
    d.innerHTML = '<div><div class=""role"">' + tr('Assistant') + '</div><div class=""bubble"" id=""' + id + '-bubble""></div><div class=""meta"" id=""' + id + '-meta""></div></div>';
    return d;
  }
  function appendAssistantStart(id) {
    removeEmpty();
    body().appendChild(assistantNode(id));
    document.getElementById(id + '-bubble').innerHTML = '<span class=""waiting"">…</span>';
    scroll(true);
  }

  function ensureThinkingPanel(id) {
    var existing = document.getElementById(id + '-thinking');
    if (existing) return existing;
    var bubble = document.getElementById(id + '-bubble');
    if (!bubble) return null;
    var details = document.createElement('details');
    details.id = id + '-thinking'; details.className = 'thinking'; details.open = true;
    details.innerHTML = '<summary>' + tr('💭 Thinking…') + '</summary><div class=""thinking-content"" id=""' + id + '-thinking-body""></div>';
    bubble.parentElement.insertBefore(details, bubble);
    var w = bubble.querySelector('.waiting'); if (w) w.remove();
    return details;
  }
  function setThinkingHtml(id, html) {
    ensureThinkingPanel(id);
    var b = document.getElementById(id + '-thinking-body');
    if (!b) return;
    b.innerHTML = html; b.scrollTop = b.scrollHeight; scroll();
  }
  function collapseThinking(id) {
    var panel = document.getElementById(id + '-thinking');
    if (!panel) return;
    panel.open = false;
    var s = panel.querySelector('summary'); if (s) s.textContent = tr('💭 Thinking (click to expand)');
  }
  function setAssistantHtml(id, html) {
    var b = document.getElementById(id + '-bubble');
    if (!b) return;
    b.innerHTML = html;
    var last = b.lastElementChild;
    var caret = document.createElement('span'); caret.className = 'cursor-inline';
    if (last && /^(P|LI|H1|H2|H3|H4|H5|H6|TD|TH)$/.test(last.tagName)) last.appendChild(caret); else b.appendChild(caret);
    scroll();
  }
  function setStatusText(id, text) {
    var b = document.getElementById(id + '-bubble');
    if (b && b.querySelector('.waiting')) b.querySelector('.waiting').textContent = text;
  }

  function copyToClipboard(text, btn) {
    function flash() { if (!btn) return; var old = btn.textContent; btn.textContent = tr('Copied!'); setTimeout(function () { btn.textContent = old; }, 1200); }
    try {
      if (window.chrome && window.chrome.webview && window.chrome.webview.postMessage) {
        window.chrome.webview.postMessage({ type: 'copy', text: text }); flash(); return;
      }
    } catch (e) {}
    if (navigator.clipboard && window.isSecureContext) { navigator.clipboard.writeText(text).then(flash).catch(function () { execCommandCopy(text); flash(); }); return; }
    execCommandCopy(text); flash();
  }
  function execCommandCopy(text) {
    var ta = document.createElement('textarea');
    ta.value = text; ta.setAttribute('readonly', ''); ta.style.position = 'fixed'; ta.style.top = '-1000px'; ta.style.opacity = '0';
    document.body.appendChild(ta); ta.focus(); ta.select();
    try { document.execCommand('copy'); } catch (e) {}
    document.body.removeChild(ta);
  }
  // Render $…$ / $$…$$ produced by Markdig's math extension with KaTeX.
  function renderMath(root) {
    if (!window.katex) return;
    root.querySelectorAll('.math').forEach(function (el) {
      if (el.getAttribute('data-done')) return;
      var tex = el.textContent.trim(), display = el.tagName === 'DIV';
      tex = tex.replace(/^\\\(|\\\)$/g, '').replace(/^\\\[|\\\]$/g, '').trim();
      try { katex.render(tex, el, { displayMode: display, throwOnError: false, output: 'html' }); el.setAttribute('data-done', '1'); } catch (e) {}
    });
  }
  function decorate(b) {
    renderMath(b);
    b.querySelectorAll('pre').forEach(function (pre) {
      var codeEl = pre.querySelector('code');
      if (codeEl && window.hljs) { try { hljs.highlightElement(codeEl); } catch (e) {} }
      if (pre.querySelector('.copy-btn')) return;
      var btn = document.createElement('button');
      btn.className = 'copy-btn'; btn.textContent = tr('Copy');
      btn.onclick = function (ev) { ev.stopPropagation(); copyToClipboard(codeEl ? codeEl.innerText : pre.innerText, btn); };
      pre.appendChild(btn);
    });
  }
  function finalizeAssistant(id, html, thinkingHtml, meta) {
    var b = document.getElementById(id + '-bubble');
    if (!b) return;
    b.innerHTML = html;
    decorate(b);
    if (thinkingHtml) {
      var panel = ensureThinkingPanel(id);
      if (panel) {
        var tb = document.getElementById(id + '-thinking-body'); if (tb) { tb.innerHTML = thinkingHtml; renderMath(tb); }
        panel.open = false;
        var s = panel.querySelector('summary'); if (s) s.textContent = tr('💭 Thinking (click to expand)');
      }
    }
    var m = document.getElementById(id + '-meta');
    if (m) {
      m.innerHTML = '';
      var c = document.createElement('button'); c.textContent = tr('Copy answer');
      c.onclick = function () { copyToClipboard(b.innerText, c); };
      m.appendChild(c);
      var r = document.createElement('button'); r.textContent = tr('🔊 Read'); r.title = tr('Read aloud (click again to stop)');
      r.onclick = function () { try { window.chrome.webview.postMessage({ type: 'speak', text: b.innerText }); } catch (e) {} };
      m.appendChild(r);
      if (meta) { var sp = document.createElement('span'); sp.textContent = meta; m.appendChild(sp); }
    }
    scroll();
  }
  // Rebuild a whole conversation in one call (much faster than one call per message).
  function loadAll(items) {
    clearAll();
    var frag = document.createDocumentFragment();
    var finals = [];
    var ui = 0;
    items.forEach(function (it, i) {
      if (it.role === 'user') { frag.appendChild(userNode(it.text, it.imgs, ui++)); }
      else { var id = 'h' + i; frag.appendChild(assistantNode(id)); finals.push([id, it]); }
    });
    body().appendChild(frag);
    finals.forEach(function (f) { finalizeAssistant(f[0], f[1].html, f[1].thinkingHtml, f[1].meta); });
    scroll(true);
  }
"
End Module
