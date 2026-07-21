using System.Text.Json;
using NLog;

namespace CheckmkPlugin.AgentUpdater.Services;

/// <summary>Persistente Plugin-Einstellungen: wo liegt der aktuelle MSI-Share,
/// welche Skript-Vorlage laeuft auf dem Zielhost.</summary>
public sealed class PluginSettings
{
    /// <summary>Share, in dem der aktuelle Checkmk-Agent-Installer (MSI) liegt.</summary>
    public string AgentShare { get; set; } = @"\\samba01\542$\5424_IT-Basis-Dienste\CheckMK";

    /// <summary>Editierbare PowerShell-Skript-Vorlage, die auf dem Zielhost
    /// ausgefuehrt wird. Platzhalter: {host} = Hostname, {installer} = lokaler
    /// Pfad des kopierten Installers. Enthaelt auch den Register-Befehl inkl.
    /// Passwort (Klartext, bewusst).</summary>
    public string AgentUpdateScript { get; set; } = DefaultAgentUpdateScript;

    public const string DefaultAgentUpdateScript =
        "# Laeuft auf dem Zielhost. {host}=Hostname, {installer}=lokaler MSI-Pfad.\n" +
        "$ErrorActionPreference = 'Stop'\n" +
        "\n" +
        "# 1) Vorhandenen Checkmk-Agent deinstallieren (falls vorhanden)\n" +
        "#    msiexec meldet Fehler nur via ExitCode — Start-Process -Wait ohne -PassThru\n" +
        "#    verschluckt sie. Deshalb -PassThru und ExitCode explizit pruefen.\n" +
        "$keys = 'HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\*'," +
        "'HKLM:\\SOFTWARE\\WOW6432Node\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\*'\n" +
        "Get-ItemProperty $keys -ErrorAction SilentlyContinue |\n" +
        "  Where-Object { $_.DisplayName -like 'Checkmk Agent*' } |\n" +
        "  ForEach-Object {\n" +
        "    Write-Output \"Deinstalliere $($_.DisplayName)\"\n" +
        "    $p = Start-Process msiexec.exe -ArgumentList \"/x $($_.PSChildName) /qn /norestart\" -Wait -PassThru\n" +
        "    if ($p.ExitCode -ne 0) { throw \"msiexec /x fehlgeschlagen (ExitCode $($p.ExitCode))\" }\n" +
        "  }\n" +
        "\n" +
        "# 2) Aktuellen Client installieren (wurde nach {installer} kopiert)\n" +
        "Write-Output 'Installiere neuen Agent'\n" +
        "$p = Start-Process msiexec.exe -ArgumentList \"/i `\"{installer}`\" /qn /norestart\" -Wait -PassThru\n" +
        "if ($p.ExitCode -ne 0) { throw \"msiexec /i fehlgeschlagen (ExitCode $($p.ExitCode))\" }\n" +
        "\n" +
        "# 3) Registrieren (--trust-cert: Server-Zertifikat ohne interaktive Rueckfrage vertrauen)\n" +
        "Write-Output 'Registriere Agent-Controller'\n" +
        "& \"C:\\Program Files (x86)\\checkmk\\service\\cmk-agent-ctl.exe\" register --trust-cert " +
        "-H {host} -s cmk.lhp.intern -i LHP -U Agent_cmk -P ************\n" +
        "Write-Output 'Fertig.'\n";
}

public interface IPluginSettingsStore
{
    PluginSettings Load();
    void Save(PluginSettings settings);
    string FilePath { get; }
}

/// <summary>Speichert die Plugin-Settings im Plugin-Datenverzeichnis
/// (typischerweise <c>%APPDATA%\Kroste\Checkmk\plugins\agent-updater\settings.json</c>).</summary>
public sealed class PluginSettingsStore : IPluginSettingsStore
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly string _path;

    public string FilePath => _path;

    public PluginSettingsStore(string pluginDataDirectory)
    {
        Directory.CreateDirectory(pluginDataDirectory);
        _path = Path.Combine(pluginDataDirectory, "settings.json");
    }

    public PluginSettings Load()
    {
        if (!File.Exists(_path)) return new PluginSettings();
        try
        {
            return JsonSerializer.Deserialize<PluginSettings>(File.ReadAllText(_path))
                   ?? new PluginSettings();
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Plugin-Settings konnten nicht geladen werden — nutze Defaults.");
            return new PluginSettings();
        }
    }

    public void Save(PluginSettings settings)
    {
        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(settings, JsonOpts));
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Plugin-Settings konnten nicht gespeichert werden: {Path}", _path);
        }
    }
}
