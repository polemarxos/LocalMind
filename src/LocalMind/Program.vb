Imports System
Imports System.Windows.Forms

Public Module Program
    <STAThread>
    Public Sub Main()
        Safety.Install()
        Updater.CleanupOldVersion()
        ' a backup chosen in "Restore from backup…" goes back first, before any setting is read
        Try
            If BackupService.ApplyPendingRestore() Then Environment.SetEnvironmentVariable("LOCALMIND_RESTORED", "1")
        Catch ex As Exception
            Safety.Log(ex, "restore")
        End Try
        Application.SetHighDpiMode(HighDpiMode.SystemAware)
        Application.EnableVisualStyles()
        Application.SetCompatibleTextRenderingDefault(False)
        ' the chosen look (or LOCALMIND_THEME for previews), before any window exists
        Dim look = Environment.GetEnvironmentVariable("LOCALMIND_THEME")
        If String.IsNullOrEmpty(look) Then look = AppCore.Settings.ThemeName
        Dim n As Integer
        If Integer.TryParse(look, n) AndAlso n >= 1 AndAlso n <= ThemePresets.All.Count Then look = ThemePresets.All(n - 1).Name
        Theme.UsePreset(look)
        Theme.Install()
        Application.Run(New StartupContext())
    End Sub
End Module