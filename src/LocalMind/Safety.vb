Imports System
Imports System.IO
Imports System.Runtime.CompilerServices
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms

''' <summary>
''' Safety nets: UI updates from background work that never crash when the window is already closed, and one place
''' that catches any unexpected error (logged to Data\errors.log and shown) instead of closing LocalMind.
''' </summary>
Public Module Safety

    ''' <summary>Runs the action on the window's thread — or does nothing if the window is closed or not ready.</summary>
    <Extension>
    Public Sub Post(c As Control, a As Action)
        If c Is Nothing OrElse a Is Nothing OrElse c.IsDisposed OrElse c.Disposing OrElse Not c.IsHandleCreated Then Return
        Try
            c.BeginInvoke(Sub()
                              If c.IsDisposed Then Return
                              Try
                                  a()
                              Catch ex As ObjectDisposedException
                              End Try
                          End Sub)
        Catch ex As ObjectDisposedException
        Catch ex As InvalidOperationException     ' handle destroyed meanwhile
        End Try
    End Sub

    Private ReadOnly Property LogFile As String
        Get
            Return Path.Combine(AppPaths.Roaming, "errors.log")
        End Get
    End Property

    Public Sub Log(ex As Exception, where As String)
        Try
            Dim fi As New FileInfo(LogFile)
            If fi.Exists AndAlso fi.Length > 2_000_000 Then fi.Delete()
            File.AppendAllText(LogFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {where} · LocalMind {Application.ProductVersion.Split("+"c)(0)}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}")
        Catch
        End Try
    End Sub

    Private _lastShown As DateTime = DateTime.MinValue
    Private _showing As Integer

    ''' <summary>Catches every error nobody else caught (call once at startup).</summary>
    Public Sub Install()
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException)
        AddHandler Application.ThreadException, Sub(s, e) Report(e.Exception, "UI")
        AddHandler AppDomain.CurrentDomain.UnhandledException, Sub(s, e) Log(TryCast(e.ExceptionObject, Exception), "background (fatal)")
        AddHandler TaskScheduler.UnobservedTaskException, Sub(s, e)
                                                              Log(e.Exception, "background task")
                                                              e.SetObserved()
                                                          End Sub
    End Sub

    ''' <summary>Logs the error and tells the user (at most one message at a time, and not more than every few seconds).</summary>
    Public Sub Report(ex As Exception, where As String)
        Log(ex, where)
        If TypeOf ex Is OperationCanceledException OrElse TypeOf ex Is ObjectDisposedException Then Return
        If (DateTime.Now - _lastShown).TotalSeconds < 4 OrElse Interlocked.Exchange(_showing, 1) = 1 Then Return
        Try
            _lastShown = DateTime.Now
            Dim msg = ex.Message.Split({vbCr, vbLf}, StringSplitOptions.RemoveEmptyEntries)
            BoxForm.Ask(Nothing, Localizer.T("Something went wrong, but LocalMind keeps running:") & Environment.NewLine & Environment.NewLine &
                        If(msg.Length > 0, msg(0), ex.GetType().Name) & Environment.NewLine & Environment.NewLine &
                        Localizer.T("Details are saved in") & " " & LogFile, "LocalMind", MessageBoxButtons.OK, MessageBoxIcon.Warning)
        Catch
        Finally
            Interlocked.Exchange(_showing, 0)
        End Try
    End Sub
End Module
