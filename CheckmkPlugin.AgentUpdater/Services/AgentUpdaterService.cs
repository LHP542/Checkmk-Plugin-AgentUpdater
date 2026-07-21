using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using Checkmk.PluginContracts.Services;
using NLog;

namespace CheckmkPlugin.AgentUpdater.Services;

/// <summary>
/// Aktualisiert den Checkmk-Agent auf einem entfernten Windows-Host per
/// Remote-PowerShell. Implementiert den <see cref="IAgentUpdater"/>-Contract,
/// damit andere Plugins (z. B. das vSphere-Baseimage-Plugin) das Update im
/// Batch benutzen koennen.
///
/// Ablauf: powershell.exe fuehrt ein aeusseres Skript aus (baut PSSession
/// mit den geprompteten Admin-Credentials, sucht das MSI im Share, kopiert
/// per <c>Copy-Item -ToSession</c> auf den Host und laedt dann die
/// editierbare Skript-Vorlage im <c>Invoke-Command</c>-Block aus (Deinstall/
/// Install/Register).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class AgentUpdaterService : IAgentUpdater
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    public async Task<AgentUpdateResult> UpdateAsync(
        string hostName,
        string user,
        string password,
        string agentShare,
        string scriptTemplate,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        const string remoteInstaller = @"C:\Windows\Temp\checkmk-agent-update.msi";

        var body = scriptTemplate
            .Replace("{host}", hostName)
            .Replace("{installer}", remoteInstaller);

        // Aeusseres Skript (laeuft lokal, baut die Session auf). Tokens statt
        // String-Interpolation, damit die PowerShell-{ } nicht kollidieren.
        const string outer = """
            $ErrorActionPreference = 'Stop'
            $u = '%%USER%%'
            $p = '%%PASS%%'
            $h = '%%HOST%%'
            $share = '%%SHARE%%'
            $remote = '%%REMOTE%%'
            Write-Output "== Client-Aktualisierung fuer $h =="
            Write-Output "Suche aktuellen Installer in $share ..."
            $src = Get-ChildItem -Path $share -Filter *.msi -ErrorAction Stop |
                   Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName
            if (-not $src) { throw "Kein MSI im Share gefunden: $share" }
            Write-Output "Installer: $src"
            $sec = ConvertTo-SecureString $p -AsPlainText -Force
            $cred = New-Object System.Management.Automation.PSCredential($u, $sec)
            Write-Output "Verbinde zu $h ..."
            $s = New-PSSession -ComputerName $h -Credential $cred
            try {
                Write-Output "Kopiere Installer auf $h ..."
                Copy-Item -ToSession $s -Path $src -Destination $remote -Force
                Invoke-Command -Session $s -ScriptBlock {
            %%BODY%%
                }
            }
            finally { if ($s) { Remove-PSSession $s } }
            """;

        var script = outer
            .Replace("%%USER%%", Esc(user))
            .Replace("%%PASS%%", Esc(password))
            .Replace("%%HOST%%", Esc(hostName))
            .Replace("%%SHARE%%", Esc(agentShare))
            .Replace("%%REMOTE%%", remoteInstaller)
            .Replace("%%BODY%%", IndentBody(body));

        return await ExecutePowerShellAsync(script, progress, ct);
    }

    private static string IndentBody(string body)
    {
        var sb = new StringBuilder();
        foreach (var line in body.Replace("\r\n", "\n").Split('\n'))
            sb.Append("            ").Append(line).Append('\n');
        return sb.ToString().TrimEnd('\n');
    }

    /// <summary>PowerShell einzelnes '-Argument fuer single-quoted Strings escapen.</summary>
    private static string Esc(string s) => s.Replace("'", "''");

    private static async Task<AgentUpdateResult> ExecutePowerShellAsync(
        string script, IProgress<string>? progress, CancellationToken ct)
    {
        var output = new StringBuilder();
        var ok = true;

        void Emit(string line)
        {
            output.AppendLine(line);
            progress?.Report(line);
        }

        // Mehrzeilige Skripte laufen ueber '-Command -' (STDIN) unzuverlaessig und
        // liefern teils gar keine Ausgabe. Deshalb: Temp-.ps1 schreiben und mit -File
        // ausfuehren.
        var tempPs1 = Path.Combine(Path.GetTempPath(), $"cmk-agent-update-{Guid.NewGuid():N}.ps1");
        try
        {
            await File.WriteAllTextAsync(tempPs1, script, new UTF8Encoding(true), ct);

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{tempPs1}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var proc = new Process { StartInfo = psi };
            proc.OutputDataReceived += (_, e) => { if (e.Data is not null) Emit(e.Data); };
            // stderr NICHT automatisch als Fehler werten — native Tools (cmk-agent-ctl,
            // msiexec) schreiben auch Infos dorthin. Erfolg entscheidet der Exit-Code.
            proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) Emit("» " + e.Data); };

            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            await proc.WaitForExitAsync(ct);

            ok = proc.ExitCode == 0;
            Emit($"--- Exit-Code: {proc.ExitCode} ---");
        }
        catch (Exception ex)
        {
            // WICHTIG: niemals 'script' loggen — enthaelt Admin- und Register-Passwort.
            Log.Warn(ex, "Agent-Update-Prozess konnte nicht ausgefuehrt werden.");
            Emit("FEHLER: " + ex.Message);
            ok = false;
        }
        finally
        {
            try { if (File.Exists(tempPs1)) File.Delete(tempPs1); }
            catch { /* best effort */ }
        }

        if (output.Length == 0)
            Emit("(keine Ausgabe erhalten — Skript wurde evtl. nicht ausgefuehrt)");

        return new AgentUpdateResult(ok, output.ToString());
    }
}
