Imports System
Imports System.Windows.Forms

Public Module Program
    <STAThread>
    Public Sub Main()
        Updater.CleanupOldVersion()
        Application.SetHighDpiMode(HighDpiMode.SystemAware)
        Application.EnableVisualStyles()
        Application.SetCompatibleTextRenderingDefault(False)
        Theme.Install()
        Application.Run(New MainForm())
    End Sub
End Module