using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Yeelight.TestApp.Controls;
using Yeelight.TestApp.Services;
using Yeelight.TestApp.Session;

namespace Yeelight.TestApp.Views;

/// <summary>The settings of the client (applying them makes a new client) and the theme of the app.</summary>
public sealed partial class SettingsPage : Page
{
    private readonly AppSession _session = App.Session;
    private bool _loading;

    /// <summary>Initializes a new instance of the <see cref="SettingsPage"/> class.</summary>
    public SettingsPage()
    {
        this.InitializeComponent();
        this.NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;

        this.ThemeBox.ItemsSource = new[]
        {
            new Choice<AppTheme>(AppTheme.System, "Use the system setting"),
            new Choice<AppTheme>(AppTheme.Light, "Light"),
            new Choice<AppTheme>(AppTheme.Dark, "Dark"),
        };
        this.Load(this._session.Settings);
        this.SettingsPathText.Text = $"Settings are kept in {Path.Combine(AppSettings.Folder, "settings.json")}";
        this.ExportPathText.Text = $"Exports of the log and the events are written to {AppSettings.ExportFolder}";
    }

    /// <summary>Fills in the controls with the values of settings, as a person would; for the smoke test.</summary>
    /// <param name="settings">The settings.</param>
    internal void Load(AppSettings settings)
    {
        this._loading = true;
        this.AutoConnectSwitch.IsOn = settings.AutoConnect;
        this.RefreshOnConnectSwitch.IsOn = settings.RefreshOnConnect;
        this.UseBroadcastSwitch.IsOn = settings.UseBroadcast;
        this.CommandTimeoutBox.Value = settings.CommandTimeoutSeconds;
        this.ConnectTimeoutBox.Value = settings.ConnectTimeoutSeconds;
        this.KeepAliveBox.Value = settings.KeepAliveSeconds;
        this.CommandsPerMinuteBox.Value = settings.CommandsPerMinute;
        this.SearchBurstBox.Value = settings.SearchBurst;
        this.DiscoveryPortBox.Value = settings.DiscoveryPort;
        this.ThemeBox.SelectedItem = this.ThemeBox.Items.OfType<Choice<AppTheme>>().FirstOrDefault(choice => choice.Value == settings.Theme);
        this._loading = false;
    }

    /// <summary>Types values in the number boxes, as a person would; for the smoke test.</summary>
    /// <param name="commandTimeout">The command timeout in seconds.</param>
    /// <param name="commandsPerMinute">The number of commands a minute.</param>
    /// <param name="autoConnect">Whether the client connects to a light as soon as it is found.</param>
    internal void Edit(double commandTimeout, int commandsPerMinute, bool autoConnect)
    {
        this.CommandTimeoutBox.Value = commandTimeout;
        this.CommandsPerMinuteBox.Value = commandsPerMinute;
        this.AutoConnectSwitch.IsOn = autoConnect;
    }

    /// <summary>Makes the settings of what the controls hold.</summary>
    /// <returns>The settings. A box that holds no number gives a value that the settings refuse.</returns>
    internal AppSettings ReadDraft()
    {
        var draft = this._session.Settings.Clone();
        draft.AutoConnect = this.AutoConnectSwitch.IsOn;
        draft.RefreshOnConnect = this.RefreshOnConnectSwitch.IsOn;
        draft.UseBroadcast = this.UseBroadcastSwitch.IsOn;
        draft.CommandTimeoutSeconds = this.CommandTimeoutBox.Value;
        draft.ConnectTimeoutSeconds = this.ConnectTimeoutBox.Value;
        draft.KeepAliveSeconds = this.KeepAliveBox.Value;
        draft.CommandsPerMinute = ToInt(this.CommandsPerMinuteBox.Value);
        draft.SearchBurst = ToInt(this.SearchBurstBox.Value);
        draft.DiscoveryPort = ToInt(this.DiscoveryPortBox.Value);
        return draft;
    }

    /// <summary>Applies the settings: checks them, asks whether the list of lights may be cleared, and makes a new client.</summary>
    /// <param name="confirm">Whether to ask; the smoke test does not.</param>
    /// <returns><c>true</c> when the settings were applied.</returns>
    internal async Task<bool> ApplyAsync(bool confirm)
    {
        var draft = this.ReadDraft();
        var problems = draft.Validate();
        if (problems.Count > 0)
        {
            Fluent.Show(this.StatusBar, InfoBarSeverity.Warning, "These settings cannot be used", string.Join(' ', problems));
            return false;
        }

        if (confirm && !await this.ConfirmAsync())
        {
            return false;
        }

        this.Busy.IsActive = true;
        this.ApplyButton.IsEnabled = false;
        try
        {
            await this._session.ApplySettingsAsync(draft);
            draft.Save();
            Fluent.Show(
                this.StatusBar,
                InfoBarSeverity.Success,
                "The settings were applied",
                "A new client was made and the list of lights was cleared. Search, probe an address or start the simulated lights to find lights again.");
            return true;
        }
        catch (Exception exception)
        {
            Fluent.Show(this.StatusBar, InfoBarSeverity.Error, "The settings could not be applied", exception.Message);
            return false;
        }
        finally
        {
            this.Busy.IsActive = false;
            this.ApplyButton.IsEnabled = true;
        }
    }

    /// <summary>Gets what the status bar says; for the smoke test.</summary>
    internal string StatusText => this.StatusBar.IsOpen ? this.StatusBar.Title + " " + this.StatusBar.Message : string.Empty;

    private static int ToInt(double value) => double.IsNaN(value) || double.IsInfinity(value) ? 0 : (int)Math.Clamp(Math.Round(value), int.MinValue, int.MaxValue);

    private async Task<bool> ConfirmAsync()
    {
        var count = this._session.Lights.Count;
        var dialog = new ContentDialog
        {
            XamlRoot = this.XamlRoot,
            Title = "Apply the settings?",
            Content = "Applying the settings closes the client and makes a new one. "
                + (count == 0 ? "No lights are in the list now. " : $"The list of lights is cleared ({count} lights) and has to be filled again by a search or a probe. ")
                + "Searching and listening stop, and music mode sessions end.",
            PrimaryButtonText = "Apply",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private async void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await this.ApplyAsync(confirm: true);
        }
        catch (Exception exception)
        {
            Fluent.Show(this.StatusBar, InfoBarSeverity.Error, "The settings could not be applied", exception.Message);
        }
    }

    private void DefaultsButton_Click(object sender, RoutedEventArgs e)
    {
        var defaults = new AppSettings { Theme = this._session.Settings.Theme };
        this.Load(defaults);
        Fluent.Show(this.StatusBar, InfoBarSeverity.Informational, "The defaults of the client are filled in", "Apply them to make a new client of them.");
    }

    private void ThemeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!this._loading && this.ThemeBox.SelectedItem is Choice<AppTheme> choice && choice.Value != this._session.Settings.Theme)
        {
            this._session.Settings.Theme = choice.Value;
            this._session.NotifySettingsChanged();
        }
    }

    private void OpenExportsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(AppSettings.ExportFolder);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppSettings.ExportFolder}\"") { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            Fluent.Show(this.StatusBar, InfoBarSeverity.Warning, "The folder could not be opened", exception.Message);
        }
    }
}
