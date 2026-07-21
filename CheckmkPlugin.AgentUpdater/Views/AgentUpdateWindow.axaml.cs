using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Checkmk.PluginContracts.Services;
using CheckmkPlugin.AgentUpdater.Services;
using CheckmkPlugin.AgentUpdater.UI;

namespace CheckmkPlugin.AgentUpdater.Views;

public partial class AgentUpdateWindow : ChromeWindow
{
    private readonly string _host = "";
    private readonly string _user = "";
    private readonly string _password = "";
    private readonly string _share = "";
    private readonly string _script = "";
    private readonly IAgentUpdater _updater = null!;

    public AgentUpdateWindow(
        IAgentUpdater updater,
        string host, string user, string password, string share, string script)
    {
        AvaloniaXamlLoader.Load(this);
        _updater = updater;
        _host = host;
        _user = user;
        _password = password;
        _share = share;
        _script = script;

        this.FindControl<TitleBar>("AppTitleBar")!.Title = $"Client-Aktualisierung — {host}";
        Opened += async (_, _) => await RunAsync();
    }

    public AgentUpdateWindow() => AvaloniaXamlLoader.Load(this);

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private async Task RunAsync()
    {
        var status = this.FindControl<TextBlock>("StatusText")!;
        var output = this.FindControl<TextBox>("OutputBox")!;
        var closeBtn = this.FindControl<Button>("DismissButton")!;

        if (!OperatingSystem.IsWindows())
        {
            status.Text = "Nur unter Windows verfuegbar (Remote-PowerShell).";
            closeBtn.IsEnabled = true;
            return;
        }

        status.Text = "Läuft… (Deinstallation → Installation → Registrierung)";

        var progress = new Progress<string>(line =>
            Dispatcher.UIThread.Post(() => output.Text += line + Environment.NewLine));

        AgentUpdateResult result;
        try
        {
            result = await _updater.UpdateAsync(_host, _user, _password, _share, _script, progress);
        }
        catch (Exception ex)
        {
            result = new AgentUpdateResult(false, ex.Message);
        }

        status.Text = result.Success
            ? "✔ Abgeschlossen."
            : "✖ Fehlgeschlagen — siehe Ausgabe.";
        if (string.IsNullOrEmpty(output.Text))
            output.Text = result.Output;
        closeBtn.IsEnabled = true;
    }
}
