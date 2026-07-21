using System.Runtime.Versioning;
using Avalonia.Controls;
using Checkmk.PluginContracts;
using Checkmk.PluginContracts.Services;
using CheckmkPlugin.AgentUpdater.Services;
using CheckmkPlugin.AgentUpdater.Views;
using Microsoft.Extensions.DependencyInjection;
using NLog;

namespace CheckmkPlugin.AgentUpdater.Contributions;

/// <summary>Steuert den Menuepunkt "Client aktualisieren…" fuer Status- und
/// Hosts-Kontextmenues bei. Beim Klick oeffnet sich der Credential-Dialog,
/// danach das Fortschritts-Fenster.</summary>
[SupportedOSPlatform("windows")]
public sealed class UpdateClientContribution : IContextMenuContribution
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly IAgentUpdater _updater;
    private readonly IPluginSettingsStore _settings;

    public UpdateClientContribution(IAgentUpdater updater, IPluginSettingsStore settings)
    {
        _updater = updater;
        _settings = settings;
    }

    public string Label => "Client aktualisieren…";

    // Order >= 1000: landet unter den Cockpit-eigenen Eintraegen.
    public int Order => 1000;

    public bool SupportsLocation(ContextMenuLocation location) => location switch
    {
        ContextMenuLocation.StatusServiceRow => true,
        ContextMenuLocation.StatusHostNode => true,
        ContextMenuLocation.HostConfigRow => true,
        _ => false
    };

    public async Task ExecuteAsync(ContextMenuTarget target, CancellationToken ct = default)
    {
        if (target.OwnerWindow is not Window owner)
        {
            Log.Warn("Kein Owner-Window im Target — kann Dialog nicht anzeigen.");
            return;
        }
        if (!OperatingSystem.IsWindows())
        {
            Log.Info("Client-Update-Kontribution nur unter Windows verfuegbar.");
            return;
        }

        var settings = _settings.Load();

        var creds = await new CredentialDialog(target.HostName).ShowDialog<CredentialResult?>(owner);
        if (creds is null) return;

        var window = new AgentUpdateWindow(
            _updater,
            target.HostName,
            creds.User,
            creds.Password,
            settings.AgentShare,
            settings.AgentUpdateScript);
        await window.ShowDialog(owner);
    }
}
