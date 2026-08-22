using Checkmk.PluginContracts;
using Checkmk.PluginContracts.Services;
using CheckmkPlugin.AgentUpdater.Contributions;
using CheckmkPlugin.AgentUpdater.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CheckmkPlugin.AgentUpdater;

/// <summary>Plugin-Entry-Point. Registriert <see cref="IAgentUpdater"/>
/// (Contract fuer andere Plugins zum Wiederverwenden) und den
/// Kontextmenue-Eintrag "Client aktualisieren…" im Status- und Hosts-Tab.</summary>
public sealed class Plugin : IPlugin
{
    public PluginMetadata Metadata => new(
        Id: "kroste.checkmk.agent-updater",
        Name: "Client-Aktualisierung",
        Version: ThisAssembly.Version,
        Description: "Aktualisiert den Checkmk-Agent auf entfernten Windows-Hosts per Remote-PowerShell.",
        Author: "Kroste",
        UpdateChannelUrl: "https://api.github.com/repos/LHP542/Checkmk-Plugin-AgentUpdater/releases/latest");

    public void Register(IServiceCollection services, IPluginContext context)
    {
        // Plugin-Settings-Store liegt im plugin-eigenen Datenverzeichnis
        // (typischerweise %APPDATA%\Kroste\Checkmk\plugins\kroste.checkmk.agent-updater\).
        services.AddSingleton<IPluginSettingsStore>(_ => new PluginSettingsStore(context.PluginDataDirectory));

        // IAgentUpdater = Contract aus PluginContracts.Services. Andere Plugins
        // koennen den Service via IPluginContext.Services.GetRequiredService<IAgentUpdater>()
        // holen und in eigenen Workflows nutzen (siehe Plan fuer das vSphere-Plugin).
        services.AddSingleton<IAgentUpdater, AgentUpdaterService>();

        // Kontextmenue-Eintrag (Status/Hosts).
        services.AddSingleton<IContextMenuContribution, UpdateClientContribution>();
    }
}

/// <summary>MinVer schreibt die Version zur Compile-Zeit ins Assembly, wir
/// spiegeln sie ins PluginMetadata.Version. Uebernommen via AssemblyInformationalVersion.</summary>
internal static class ThisAssembly
{
    public static string Version => typeof(Plugin).Assembly
        .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
        .Cast<System.Reflection.AssemblyInformationalVersionAttribute>()
        .FirstOrDefault()?.InformationalVersion?.Split('+')[0]
        ?? "0.0.0";
}
