# Checkmk-Plugin-AgentUpdater

Plugin für das [Checkmk Cockpit](https://github.com/Kroste/Checkmk), das die
**Client-Aktualisierung** (Checkmk-Agent-Update auf entfernten Windows-Hosts
per Remote-PowerShell) als Kontextmenü-Eintrag bereitstellt.

Ohne dieses Plugin gibt es im Cockpit ab v1.7.0 den Menüpunkt „Client
aktualisieren…" nicht mehr — nur wer das Plugin installiert hat, sieht ihn.

## Installation

1. Neuestes ZIP von den [Releases](https://github.com/Kroste/Checkmk-Plugin-AgentUpdater/releases)
   herunterladen (`Checkmk-Plugin-AgentUpdater-x.y.z.zip`).
2. Entpacken. Die enthaltene `CheckmkPlugin.AgentUpdater.dll` in den Ordner
   `plugins/` **neben** deiner `Checkmk.App.exe` legen. Ordner ggf. anlegen.
3. Cockpit starten. Im Log siehst du unter Info:
   `Plugin registriert: Client-Aktualisierung 1.0.0 (kroste.checkmk.agent-updater)`.

Der Menüpunkt „Client aktualisieren…" erscheint jetzt im Rechtsklick-Menü der
Service-Grid, der Baumansicht und der Hosts-Liste.

## Konfiguration

Beim ersten Aufruf verwendet das Plugin Defaults:

- **Agent-Share**: `\\samba01\542$\5424_IT-Basis-Dienste\CheckMK` — muss das
  aktuelle MSI enthalten. Das Plugin nimmt automatisch die zuletzt geänderte
  `*.msi` aus dem Ordner.
- **PowerShell-Skript-Vorlage**: läuft auf dem Zielhost, macht Deinstall →
  Install → Register. Enthält den Register-Befehl inkl. `--trust-cert` und
  Passwort (Klartext).

Beide Werte werden nach dem ersten Start unter
`%APPDATA%\Kroste\Checkmk\plugins\kroste.checkmk.agent-updater\settings.json`
abgelegt und können dort per Editor geändert werden. (Eine In-App-Settings-UI
gibt's noch nicht — kommt in einer späteren Version.)

## Ablauf beim Update

1. Rechtsklick auf einen Service oder Host → **„Client aktualisieren…"**.
2. **Credential-Dialog**: Admin-Anmeldedaten (`DOMAENE\Benutzer` + Passwort)
   für die Remote-Session. **WinRM am Zielhost muss aktiv sein** — in der DMZ
   typischerweise geblockt.
3. **Fortschritts-Fenster**: das Plugin öffnet eine PSSession zum Host, kopiert
   den Installer per `Copy-Item -ToSession` (umgeht Double-Hop), führt die
   Skript-Vorlage aus (Deinstall/Install/Register), meldet Zeile für Zeile die
   PowerShell-Ausgabe.
4. Erfolg wird am Exit-Code festgemacht, nicht an stderr — native Tools wie
   `msiexec` oder `cmk-agent-ctl` schreiben Infos manchmal nach stderr.

## Wichtige Details (aus schmerzhaften Live-Tests gelernt)

- Der Register-Befehl braucht `--trust-cert`, sonst hängt sich
  `cmk-agent-ctl register` in einer interaktiven Zertifikatsabfrage auf.
- `Start-Process msiexec.exe -Wait` meldet Nicht-Null-ExitCodes nicht als
  Fehler. Die Default-Vorlage nutzt `-PassThru` + `$proc.ExitCode`-Prüfung.

## Sicherheit

- Admin-Passwort wird nur im Prozess-Memory gehalten, niemals ins Log
  geschrieben.
- Das temporäre `.ps1`-Skript wird nach der Ausführung gelöscht.
- Die Skript-Vorlage enthält im Register-Befehl das Agent-Register-Passwort
  im Klartext (typischer Automation-User) — das ist bewusst so, weil der User
  die Vorlage sowieso lokal editiert.

## Contract für andere Plugins

Das Plugin exportiert `IAgentUpdater` (aus `Checkmk.PluginContracts.Services`)
in den DI-Container. Andere Plugins können es via
`context.Services.GetRequiredService<IAgentUpdater>()` konsumieren — z. B. das
vSphere-Baseimage-Plugin, das das Update in einer Batch-Schleife über mehrere
Baseimage-VMs fährt.

## Voraussetzungen

- Windows (WinRM/PowerShell-Remoting ist Windows-only).
- Cockpit ≥ **v1.7.0** (ab da ist das Plugin-System im Cockpit-Kern).
- WinRM am Zielhost aktiv (`winrm quickconfig`).
- Admin-Rechte auf dem Zielhost.

## Repo-Layout

```
Checkmk-Plugin-AgentUpdater/
├── CheckmkPlugin.AgentUpdater/
│   ├── Contributions/       # IContextMenuContribution
│   ├── Services/            # AgentUpdaterService + PluginSettings
│   ├── UI/                  # ChromeWindow + TitleBar (dupliziert aus Cockpit)
│   ├── Views/               # AgentUpdateWindow + CredentialDialog
│   ├── Plugin.cs            # IPlugin-Entry-Point
│   └── CheckmkPlugin.AgentUpdater.csproj
├── deps/
│   └── Checkmk.PluginContracts.dll   # committed, siehe deps/README.md
├── .github/workflows/       # CI + Release
├── Directory.Build.props
├── Directory.Packages.props
└── Checkmk-Plugin-AgentUpdater.slnx
```

## Fachbereich

5424 IT-Basis-Dienste (LHP)
