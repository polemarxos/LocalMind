Imports System
Imports System.Drawing
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms

''' <summary>
''' The loading screen at start: 1. checks for a LocalMind update (and installs it), 2. updates the model list,
''' 3. loads the interface — then shows the main window. The chat model is loaded only when it is needed.
''' </summary>
Public Class StartupContext
    Inherits ApplicationContext

    Private ReadOnly _splash As SplashForm

    Public Sub New()
        Dim skip = Not AppCore.Settings.ShowStartupScreen OrElse Environment.GetEnvironmentVariable("LOCALMIND_NOSPLASH") = "1"
        If skip Then
            MainForm = New MainForm()
            Return
        End If
        _splash = New SplashForm()
        AddHandler _splash.Shown, Async Sub() Await RunAsync()
        _splash.Show()
    End Sub

    Private Shared Async Function Within(t As Task, seconds As Integer) As Task(Of Boolean)
        Dim done = Await Task.WhenAny(t, Task.Delay(TimeSpan.FromSeconds(seconds)))
        Return done Is t AndAlso Not t.IsFaulted
    End Function

    Private Async Function RunAsync() As Task
        Dim s = AppCore.Settings
        ' 1. LocalMind update
        _splash.Begin(0)
        Try
            If s.CheckForUpdates AndAlso Environment.GetEnvironmentVariable("LOCALMIND_PREVIEW") Is Nothing Then
                Dim check = Updater.CheckAsync(CancellationToken.None)
                If Await Within(check, 10) Then
                    Dim info = check.Result
                    s.LastUpdateCheck = DateTime.Now
                    If info IsNot Nothing AndAlso AppPaths.IsPortable AndAlso s.AutoInstallUpdates Then
                        _splash.Detail(String.Format(Localizer.T("Installing LocalMind {0}…"), info.Tag))
                        Dim exe = Await Updater.InstallAsync(info, Sub(p) _splash.Post(Sub() _splash.Sub_(p, String.Format(Localizer.T("Downloading LocalMind {0}…"), info.Tag) & $" {p}%")), CancellationToken.None)
                        AppCore.SaveSettings()
                        _splash.Detail(Localizer.T("Starting the new version…"))
                        Await Task.Delay(600)
                        Updater.Restart(exe)
                        ExitThread()
                        Return
                    End If
                    _splash.Done(0, If(info Is Nothing, Localizer.T("up to date"), String.Format(Localizer.T("{0} available"), info.Tag)))
                Else
                    _splash.Done(0, Localizer.T("skipped (no answer)"))
                End If
            Else
                _splash.Done(0, Localizer.T("off"))
            End If
        Catch
            _splash.Done(0, Localizer.T("skipped (offline?)"))
        End Try

        ' 2. model list
        _splash.Begin(1)
        Try
            If ModelCatalog.CacheIsStale() AndAlso Environment.GetEnvironmentVariable("LOCALMIND_PREVIEW") Is Nothing Then
                Dim fetch = ModelCatalog.FetchLiveAsync(CancellationToken.None)
                Dim ok = Await Within(fetch, 25)
                _splash.Done(1, If(ok, String.Format(Localizer.T("{0} models"), fetch.Result.Count), Localizer.T("continues in the background")))
            Else
                _splash.Done(1, Localizer.T("up to date"))
            End If
        Catch
            _splash.Done(1, Localizer.T("skipped (offline?)"))
        End Try

        ' first start: the recommended models (or "set up myself") before the app opens; closing it closes LocalMind
        Dim openModels = False
        If Not s.FirstRunDone AndAlso s.Backend = BackendKind.BuiltIn AndAlso AppCore.Library.All().Count = 0 AndAlso
           Environment.GetEnvironmentVariable("LOCALMIND_PREVIEW") Is Nothing Then
            _splash.Hide()
            If Not s.FeaturesAsked Then
                Using ff As New FeaturesForm()
                    ff.ShowDialog()
                End Using
            End If
            Dim choice As String
            Using f As New SetupForm(Localizer.T("Welcome! Download the recommended models for your PC?"), welcome:=True)
                f.ShowDialog()
                choice = f.Choice
            End Using
            If choice = "" Then
                AppCore.SaveSettings()
                _splash.Close()
                ExitThread()
                Return
            End If
            s.FirstRunDone = True
            AppCore.SaveSettings()
            openModels = (choice = "manual")
            _splash.Show()
        End If

        ' 3. interface
        _splash.Begin(2)
        Dim main As New MainForm() With {.Opacity = 0, .OpenModelsAfterLoad = openModels}
        main.Show()
        Await Within(main.LoadDone.Task, 90)
        _splash.Done(2)
        If main.IsDisposed Then
            ' (closed while loading)
            _splash.Close()
            ExitThread()
            Return
        End If

        ' the chat model is not loaded here: it loads with the first message (no memory used until then)
        Await Task.Delay(250)
        MainForm = main
        main.Opacity = 1
        main.Activate()
        _splash.Close()
    End Function
End Class

''' <summary>The loading screen (logo, the four steps, a progress bar).</summary>
Public Class SplashForm
    Inherits Form

    Private ReadOnly _steps As String() = {"Checking for updates", "Updating the model list", "Loading the interface"}
    Private ReadOnly _state(2) As Integer          ' 0 waiting, 1 running, 2 done
    Private ReadOnly _notes(2) As String
    Private _detail As String = ""
    Private _sub As Integer
    Private _current As Integer
    Private ReadOnly _spin As New System.Windows.Forms.Timer() With {.Interval = 80}
    Private _tick As Integer

    Public Sub New()
        FormBorderStyle = FormBorderStyle.None
        StartPosition = FormStartPosition.CenterScreen
        Size = New Size(560, 330)
        BackColor = Theme.CPanel
        ShowInTaskbar = True
        Text = "LocalMind"
        DoubleBuffered = True
        Try
            Icon = Theme.AppIcon()
        Catch
        End Try
        For i = 0 To _steps.Length - 1
            _steps(i) = Localizer.T(_steps(i))
        Next
        AddHandler _spin.Tick, Sub()
                                   _tick += 1
                                   Invalidate()
                               End Sub
        _spin.Start()
    End Sub

    Protected Overrides ReadOnly Property CreateParams As CreateParams
        Get
            Dim cp = MyBase.CreateParams
            cp.ClassStyle = cp.ClassStyle Or &H20000     ' drop shadow
            Return cp
        End Get
    End Property

    Public Sub Begin(i As Integer)
        Ui(Sub()
               _current = i : _state(i) = 1 : _sub = 0 : _detail = ""
           End Sub)
    End Sub

    Public Sub Done(i As Integer, Optional note As String = Nothing)
        Ui(Sub()
               _state(i) = 2 : _notes(i) = note : _sub = 0 : _detail = ""
           End Sub)
    End Sub

    Public Sub Detail(text As String)
        Ui(Sub() _detail = text)
    End Sub

    Public Sub Sub_(percent As Integer, text As String)
        Ui(Sub()
               _sub = Math.Max(0, Math.Min(100, percent)) : _detail = text
           End Sub)
    End Sub

    Private Sub Ui(a As Action)
        If IsDisposed Then Return
        If InvokeRequired Then
            BeginInvoke(a)
        Else
            a()
        End If
        Invalidate()
    End Sub

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        Dim g = e.Graphics
        g.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias
        g.TextRenderingHint = Drawing.Text.TextRenderingHint.ClearTypeGridFit
        Using b As New SolidBrush(Theme.CPanel) : g.FillRectangle(b, ClientRectangle) : End Using
        Using b As New SolidBrush(Theme.CAccent) : g.FillRectangle(b, 0, 0, Width, 4) : End Using
        Using p As New Pen(Theme.CBorder) : g.DrawRectangle(p, 0, 0, Width - 1, Height - 1) : End Using
        Try
            Using ic = Theme.AppIcon(48)
                g.DrawIcon(ic, New Rectangle(32, 34, 48, 48))
            End Using
        Catch
        End Try
        Using big As New Font(Theme.UiFont.FontFamily, 20, FontStyle.Bold)
            TextRenderer.DrawText(g, "LocalMind", big, New Point(92, 30), Theme.CText)
        End Using
        TextRenderer.DrawText(g, Localizer.T("Private AI on your PC") & "  ·  " & Application.ProductVersion.Split("+"c)(0), Theme.UiFont, New Point(95, 66), Theme.CDim)

        Dim y = 122
        For i = 0 To _steps.Length - 1
            Dim glyph As String, col As Color
            Select Case _state(i)
                Case 2 : glyph = "✔" : col = Theme.CGood
                Case 1 : glyph = "◐◓◑◒"(_tick \ 2 Mod 4) : col = Theme.CAccent
                Case Else : glyph = "○" : col = Theme.CDim
            End Select
            TextRenderer.DrawText(g, glyph, Theme.BoldFont, New Rectangle(34, y, 24, 24), col, TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
            TextRenderer.DrawText(g, _steps(i), If(_state(i) = 1, Theme.BoldFont, Theme.UiFont), New Point(64, y + 3), If(_state(i) = 0, Theme.CDim, Theme.CText))
            If Not String.IsNullOrEmpty(_notes(i)) Then
                TextRenderer.DrawText(g, _notes(i), Theme.SmallFont, New Rectangle(290, y + 3, Width - 320, 20), Theme.CDim, TextFormatFlags.Right Or TextFormatFlags.EndEllipsis)
            End If
            y += 34
        Next

        ' progress bar: one part per step, plus the progress inside the running step
        Dim doneSteps = _state.Count(Function(x) x = 2)
        Dim running = If(_state(_current) = 1, If(_sub > 0, _sub / 100.0, 0.35 + 0.15 * Math.Sin(_tick / 6.0)), 0)
        Dim pct = Math.Min(1.0, (doneSteps + Math.Max(0, running)) / _steps.Length)
        Dim bar As New Rectangle(34, Height - 70, Width - 68, 8)
        Using p = Theme.Rounded(bar, 4), b As New SolidBrush(Theme.CRaised) : g.FillPath(b, p) : End Using
        If pct > 0.01 Then
            Using p = Theme.Rounded(New RectangleF(bar.X, bar.Y, CSng(bar.Width * pct), bar.Height), 4), b As New SolidBrush(Theme.CAccent) : g.FillPath(b, p) : End Using
        End If
        TextRenderer.DrawText(g, If(_detail, ""), Theme.SmallFont, New Rectangle(34, Height - 54, Width - 68, 22), Theme.CDim, TextFormatFlags.EndEllipsis)
    End Sub

    Protected Overrides Sub OnFormClosed(e As FormClosedEventArgs)
        _spin.Stop()
        MyBase.OnFormClosed(e)
    End Sub
End Class
