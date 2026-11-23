Imports System
Imports System.Windows.Forms

Public Module Program
    <STAThread>
    Public Sub Main()
        Safety.Install()
        Updater.CleanupOldVersion()
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