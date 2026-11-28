Imports System
Imports System.IO
Imports System.Linq
Imports System.Text

''' <summary>
''' Help → Copy diagnostics: what is needed to understand a problem on this PC, as plain text to paste in a message.
''' The PC, the graphics driver, the settings that change how the engines run, which engine builds are installed and
''' which of their programs exist, and the last failures. No API keys, chats or personal settings.
''' </summary>
Public NotInheritable Class DiagnosticsReport

    Private Shared ReadOnly KeyPrograms As String() = {"llama-server", "llama-tts", "sd-cli", "whisper-cli", "ffmpeg"}

    Public Shared Function Build() As String
        Dim sb As New StringBuilder()
        Dim s = AppCore.Settings
        sb.AppendLine("LocalMind diagnostics · " & DateTime.Now.ToString("yyyy-MM-dd HH:mm"))
        Section(sb, "App")
        Line(sb, "Version", Updater.CurrentVersion.ToString(3))
        Line(sb, "Windows", Environment.OSVersion.VersionString & If(Environment.Is64BitOperatingSystem, " (64-bit)", ""))
        Line(sb, "Portable", If(AppPaths.IsPortable, "yes", "no") & " · " & AppPaths.AppDir)
        Line(sb, "Language", s.Language)

        Section(sb, "PC")
        Try
            Dim hw = HardwareInfo.Current()
            Line(sb, "Summary", hw.Summary)
            For Each g In hw.Gpus
                Line(sb, "GPU", $"{g.Name} · {g.Vendor} · {g.VramBytes / (1024.0 ^ 3):0.0} GB" & If(g.ComputeCap > 0, $" · compute {g.ComputeCap:0.0}", ""))
            Next
            Line(sb, "CUDA (driver)", If(hw.CudaDriverVersion > 0, hw.CudaDriverVersion.ToString("0.0", Globalization.CultureInfo.InvariantCulture), "—") &
                                      If(hw.DriverVersion > 0, " · driver " & hw.DriverVersion.ToString("0.##", Globalization.CultureInfo.InvariantCulture), ""))
            Line(sb, "Free RAM", $"{HardwareInfo.FreeRamBytes() / (1024.0 ^ 3):0.0} GB")
            Line(sb, "Low-RAM mode", If(s.LowRamActive(hw.VramGB), "on", "off"))
        Catch ex As Exception
            Line(sb, "PC", "(could not read: " & ex.Message & ")")
        End Try

        Section(sb, "Settings")
        Line(sb, "Backend", s.Backend.ToString())
        Line(sb, "Engine", $"{s.EngineVariant} → {AppCore.Engine.EffectiveVariant}")
        Line(sb, "Engine updates", If(Not s.AutoUpdateEngines, "off", If(s.KeepWorkingEngines, "stay on working versions", "automatic")))
        Line(sb, "Pictures / music", $"{If(s.EnableImages, "on", "off")} / {If(s.EnableAudio, "on", "off")}")
        Line(sb, "Content", If(s.UncensoredContent, "Uncensored", "Standard"))
        Line(sb, "Engine state", AppCore.Engine.State.ToString() & " · " & AppCore.Engine.StateMessage)
        Line(sb, "Loaded model", If(AppCore.Engine.CurrentModel?.DisplayName, "—"))
        If s.BadEngineVersions IsNot Nothing AndAlso s.BadEngineVersions.Count > 0 Then
            Line(sb, "Skipped versions", String.Join(", ", s.BadEngineVersions))
        End If

        Section(sb, "Engines")
        Try
            Dim root = AppPaths.EngineFolder
            If Not Directory.Exists(root) Then
                sb.AppendLine("  (none installed yet)")
            Else
                For Each vf In Directory.EnumerateFiles(root, "version.txt", SearchOption.AllDirectories).OrderBy(Function(x) x)
                    Dim d = Path.GetDirectoryName(vf)
                    Dim rel = Path.GetRelativePath(root, d)
                    Dim exes = Directory.EnumerateFiles(d, "*.exe", SearchOption.AllDirectories).
                                         Select(Function(f) Path.GetFileNameWithoutExtension(f).ToLowerInvariant()).ToHashSet()
                    Dim keys = KeyPrograms.Where(Function(k) exes.Contains(k)).ToList()
                    Dim info = $"{EngineHistory.Version(d)} · {exes.Count} programs" &
                               If(keys.Count > 0, " (" & String.Join(", ", keys) & ")", "") &
                               If(EngineHistory.Works(d), " · works", "")
                    ' (an engine folder without its main program is what the startup check repairs)
                    If rel.Equals(AppCore.Engine.EffectiveVariant, StringComparison.OrdinalIgnoreCase) AndAlso Not exes.Contains("llama-tts") Then info &= " · ⚠ llama-tts missing"
                    Line(sb, rel, info)
                Next
            End If
        Catch ex As Exception
            sb.AppendLine("  (could not read: " & ex.Message & ")")
        End Try

        Section(sb, "Last failures")
        Dim fails = FailureLog.LastEntries(3)
        sb.AppendLine(If(String.IsNullOrWhiteSpace(fails), "  (none)", fails.TrimEnd()))
        Return sb.ToString()
    End Function

    Private Shared Sub Section(sb As StringBuilder, title As String)
        sb.AppendLine()
        sb.AppendLine("── " & title)
    End Sub

    Private Shared Sub Line(sb As StringBuilder, name As String, value As String)
        sb.AppendLine("  " & name.PadRight(16) & " " & If(value, ""))
    End Sub
End Class
