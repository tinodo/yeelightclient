using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.System;
using Yeelight.TestApp.Controls;
using Yeelight.TestApp.Models;
using Yeelight.TestApp.Services;
using Yeelight.TestApp.Session;

namespace Yeelight.TestApp.Views;

/// <summary>
/// The commands of the library, one light at a time: the state of the light as it is now, the commands of the main light and of the background
/// light, what belongs to the device itself (name, sleep timer, connection) and music mode. Every command shows how it went at the bottom.
/// </summary>
public sealed partial class ControlPage : Page, ICommandHost
{
    private readonly AppSession _session = App.Session;
    private readonly CommandRunner _runner;
    private readonly ObservableCollection<PropertyRow> _state = [];
    private LightItem? _item;
    private bool _updating;
    private bool _ready;
    private bool _nameDirty;
    private bool _musicWasOn;
    private bool _musicStoppedByUser;
    private bool _demoRunning;
    private CancellationTokenSource? _demo;

    /// <summary>Initializes a new instance of the <see cref="ControlPage"/> class.</summary>
    public ControlPage()
    {
        this.InitializeComponent();
        this.NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
        this._runner = new CommandRunner(this.OutcomeBar, this.BusyRing);
        this.StateList.ItemsSource = this._state;
        this.TransitionSwitch.IsOn = this._session.Settings.TransitionEnabled;
        this.TransitionBox.Value = this._session.Settings.TransitionMilliseconds;
        this.TransitionBox.IsEnabled = this.TransitionSwitch.IsOn;

        this._session.SelectedChanged += (_, _) => this.OnSelectedChanged();
        this._session.ClientReplaced += (_, _) => this.OnSelectedChanged();
        this._session.MusicChanged += (_, _) => this.UpdateMusic();

        // In a window that is not tall the live state is folded, so that the commands have room; in a tall one it is open.
        bool? tall = null;
        this.SizeChanged += (_, e) =>
        {
            var isTall = e.NewSize.Height >= 700;
            if (tall != isTall)
            {
                tall = isTall;
                this.StateExpander.IsExpanded = isTall;
            }
        };
        this._ready = true;
        this.OnSelectedChanged();
    }

    /// <summary>Gets the lights that the combo box offers.</summary>
    internal ObservableCollection<LightItem> Items => this._session.Lights;

    /// <summary>Gets the runner of the commands, which knows how many were done and how the last one went; for the smoke test.</summary>
    internal CommandRunner Runner => this._runner;

    /// <summary>Gets the panel of the main light; for the smoke test.</summary>
    internal LightPanel MainLightPanel => this.MainPanel;

    /// <summary>Gets the panel of the background light; for the smoke test.</summary>
    internal LightPanel BackgroundLightPanel => this.BackgroundPanel;

    /// <summary>Gets the light that the page shows; for the smoke test.</summary>
    internal LightItem? Current => this._item;

    /// <summary>Gets what the live state shows; for the smoke test.</summary>
    internal IReadOnlyList<PropertyRow> StateRows => this._state;

    /// <summary>Gets the summary of the live state, next to the name; for the smoke test.</summary>
    internal string Summary => this.StateTitle.Text + " | " + this.StateSummary.Text;

    /// <summary>Gets the text of the bar at the bottom: how the last command went; for the smoke test.</summary>
    internal string OutcomeText => this.OutcomeBar.IsOpen ? this.OutcomeBar.Title + " | " + this.OutcomeBar.Message : string.Empty;

    /// <summary>Gets a value indicating whether the explanation for the lack of a light is shown; for the smoke test.</summary>
    internal bool ShowsEmptyText => this.EmptyPanel.Visibility == Visibility.Visible;

    /// <summary>Gets the text about the music mode session; for the smoke test.</summary>
    internal string MusicText => this.MusicStatus.Text;

    /// <summary>Gets the text of the result of the demo; for the smoke test.</summary>
    internal string DemoText => this.DemoResult.Text;

    /// <summary>Gets a value indicating whether the demo runs; for the smoke test.</summary>
    internal bool IsDemoRunning => this._demoRunning;

    /// <inheritdoc/>
    TimeSpan? ICommandHost.Transition => this.TransitionSwitch.IsOn
        ? TimeSpan.FromMilliseconds(double.IsNaN(this.TransitionBox.Value) ? 500 : Math.Max(30, this.TransitionBox.Value))
        : null;

    /// <summary>Shows a tab, as a click on it does.</summary>
    /// <param name="tag">The tab: <c>light</c>, <c>background</c>, <c>device</c> or <c>music</c>.</param>
    internal void SelectTab(string tag)
    {
        var tab = tag switch
        {
            "background" => this.BackgroundTab,
            "device" => this.DeviceTab,
            "music" => this.MusicTab,
            _ => this.LightTab,
        };
        this.Tabs.SelectedItem = tab;
        this.ShowTab();
    }

    /// <summary>Starts the demo of music mode, as the button does; for the smoke test.</summary>
    /// <param name="seconds">How long the demo runs.</param>
    /// <returns>A task that ends when the demo is over.</returns>
    internal Task RunDemoAsync(double seconds)
    {
        this.DemoSecondsBox.Value = seconds;
        return this.RunDemoCoreAsync();
    }

    /// <inheritdoc/>
    Task<Outcome> ICommandHost.RunAsync(string what, Func<CancellationToken, Task> command, string? success) => this._runner.RunAsync(what, command, success);

    /// <inheritdoc/>
    void ICommandHost.Reject(string what, string problem)
    {
        this._runner.Show(new Outcome(OutcomeSeverity.Warning, $"{what}: cannot be sent", problem, TimeSpan.Zero));
    }

    private static string Describe(YeelightDevice device)
    {
        var text = new StringBuilder();
        text.Append(device.IsConnected ? "Connected to " : "Not connected to ");
        text.Append(device.Address is { } address ? $"{address}:{device.Port}" : device.Location);
        text.Append(". Last heard from ").Append(LightFormat.LastSeen(device.LastSeen, DateTimeOffset.Now)).Append('.');
        return text.ToString();
    }

    private static bool Can(YeelightDevice device, string method) => device.SupportedMethods.Count == 0 || device.Supports(method);

    private void OnSelectedChanged()
    {
        var item = this._session.Selected;
        if (!ReferenceEquals(this._item, item))
        {
            if (this._item is not null)
            {
                this._item.Changed -= this.OnItemChanged;
            }

            this._item = item;
            this._nameDirty = false;
            this._musicWasOn = item is not null && this._session.GetMusic(item.Id) is not null;
            this._musicStoppedByUser = false;
            if (item is not null)
            {
                item.Changed += this.OnItemChanged;
            }
        }

        this._updating = true;
        this.LightBox.SelectedItem = item;
        this._updating = false;
        this.EmptyPanel.Visibility = item is null ? Visibility.Visible : Visibility.Collapsed;
        this.ContentGrid.Visibility = item is null ? Visibility.Collapsed : Visibility.Visible;
        this.RefreshButton.IsEnabled = item is not null;
        this.MainPanel.Bind(item, false, this);
        this.BackgroundPanel.Bind(item, true, this);
        this.UpdateAll();
    }

    private void OnItemChanged(object? sender, EventArgs e) => this.UpdateAll();

    private void UpdateAll()
    {
        this.UpdateState();
        this.UpdateTabs();
        this.UpdateDevice();
        this.UpdateMusic();
    }

    private void UpdateState()
    {
        if (this._item is not { } item)
        {
            this._state.Clear();
            this.StateTitle.Text = string.Empty;
            this.StateSummary.Text = string.Empty;
            return;
        }

        var device = item.Device;
        this.ConnectedDot.Visibility = device.IsConnected ? Visibility.Visible : Visibility.Collapsed;
        this.DisconnectedDot.Visibility = device.IsConnected ? Visibility.Collapsed : Visibility.Visible;
        this.StateSwatch.Background = item.SwatchBrush;
        this.StateSwatch.Opacity = item.SwatchOpacity;
        this.StateTitle.Text = item.Title;
        var summary = new StringBuilder(item.ConnectionText).Append(" \u00b7 ").Append(item.StateText);
        if (device.IsOn)
        {
            summary.Append(" \u00b7 ").Append(LightFormat.ColorModeText(device.ColorMode));
        }

        if (device.SupportsBackgroundLight)
        {
            summary.Append(" \u00b7 background ").Append(device.Background.IsOn ? $"on, {device.Background.Brightness}%" : "off");
        }

        this.StateSummary.Text = summary.ToString();

        var rows = new List<PropertyRow>(LightFormat.StateRows(device))
        {
            new("Sleep timer", LightFormat.TimerText(device.SleepTimerRemaining)),
        };
        if (device.ActiveMode is { } mode)
        {
            rows.Add(new PropertyRow("Active mode", mode.ToString()));
        }

        if (device.SupportsBackgroundLight)
        {
            rows.AddRange(LightFormat.StateRows(device.Background, "Background "));
        }

        PropertyRows.Sync(this._state, rows);
    }

    private void UpdateTabs()
    {
        var supported = this._item?.Device.SupportsBackgroundLight == true;
        this.BackgroundTab.Visibility = supported ? Visibility.Visible : Visibility.Collapsed;
        if (!supported && ReferenceEquals(this.Tabs.SelectedItem, this.BackgroundTab))
        {
            this.Tabs.SelectedItem = this.LightTab;
            this.ShowTab();
        }
    }

    private void ShowTab()
    {
        var tag = (this.Tabs.SelectedItem as SelectorBarItem)?.Tag as string;
        this.LightView.Visibility = tag is null or "light" ? Visibility.Visible : Visibility.Collapsed;
        this.BackgroundView.Visibility = tag == "background" ? Visibility.Visible : Visibility.Collapsed;
        this.DeviceView.Visibility = tag == "device" ? Visibility.Visible : Visibility.Collapsed;
        this.MusicView.Visibility = tag == "music" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateDevice()
    {
        if (this._item is not { } item)
        {
            return;
        }

        var device = item.Device;
        if (!this._nameDirty && this.NameBox.Text != device.Name)
        {
            this._updating = true;
            this.NameBox.Text = device.Name;
            this._updating = false;
        }

        this.UpdateNameCount();
        this.TimerText.Text = device.SleepTimerRemaining > TimeSpan.Zero
            ? $"The light turns itself off in {(int)device.SleepTimerRemaining.TotalMinutes} minutes (the delayoff property, SleepTimerRemaining)."
            : "No sleep timer is running (SleepTimerRemaining is zero).";
        this.ConnectionText.Text = Describe(device);
        this.ConnectButton.IsEnabled = !device.IsConnected;
        this.DisconnectButton.IsEnabled = device.IsConnected;
        this.SetNameButton.IsEnabled = Can(device, "set_name");
        this.NameBox.IsEnabled = Can(device, "set_name");
        this.SetTimerButton.IsEnabled = Can(device, "cron_add");
        this.GetTimerButton.IsEnabled = Can(device, "cron_get");
        this.CancelTimerButton.IsEnabled = Can(device, "cron_del");
        this.TimerBox.IsEnabled = this.SetTimerButton.IsEnabled;
        this.ToggleAllButton.IsEnabled = Can(device, "dev_toggle");
    }

    private void UpdateNameCount()
    {
        var bytes = Encoding.UTF8.GetByteCount(this.NameBox.Text);
        this.NameCount.Text = bytes > 64 ? $"{bytes} bytes: too long, 64 at most" : $"{bytes} of 64 bytes";
    }

    private void UpdateMusic()
    {
        if (this._item is not { } item)
        {
            return;
        }

        var device = item.Device;
        var music = this._session.GetMusic(item.Id);
        var canStart = Can(device, "set_music");
        this.StartMusicButton.IsEnabled = canStart && music is null;
        this.StopMusicButton.IsEnabled = music is not null;
        this.MusicAddressBox.IsEnabled = music is null;
        this.RunDemoButton.IsEnabled = music is not null && !this._demoRunning;
        this.StopDemoButton.IsEnabled = this._demoRunning;
        this.DemoSecondsBox.IsEnabled = !this._demoRunning;
        if (music is not null)
        {
            this._musicWasOn = true;
            this.MusicStatus.Text = "Music mode is on: the light is connected to this computer, and the commands that go over that connection have no limit and get no answer.";
        }
        else if (this._musicWasOn && !this._musicStoppedByUser)
        {
            this._musicWasOn = false;
            this.MusicStatus.Text = "The music mode session ended by itself: the light closed the connection (the Completion of the session). Start it again to go on.";
        }
        else
        {
            this._musicWasOn = false;
            this.MusicStatus.Text = canStart
                ? (device.IsMusicModeOn ? "The light says that it is in music mode (another program may have started it)." : "Music mode is off.")
                : "This light does not list set_music: it has no music mode.";
        }
    }

    private async Task RunCoreAsync(string what, Func<YeelightDevice, CancellationToken, Task> command, string? success = null)
    {
        if (this._item is not { } item)
        {
            return;
        }

        try
        {
            await this._runner.RunAsync(what, token => command(item.Device, token), success);
        }
        catch (Exception exception)
        {
            ((ICommandHost)this).Reject(what, exception.Message);
        }
        finally
        {
            this.UpdateAll();
        }
    }

    private void LightBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (this._ready && !this._updating)
        {
            this._session.Select(this.LightBox.SelectedItem as LightItem, byUser: true);
        }
    }

    private void Tabs_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (this._ready)
        {
            this.ShowTab();
        }
    }

    private void Transition_Changed(object sender, RoutedEventArgs e)
    {
        if (!this._ready)
        {
            return;
        }

        this.TransitionBox.IsEnabled = this.TransitionSwitch.IsOn;
        this._session.Settings.TransitionEnabled = this.TransitionSwitch.IsOn;
        this._session.Settings.Save();
    }

    private void TransitionBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (this._ready && !double.IsNaN(args.NewValue))
        {
            this._session.Settings.TransitionMilliseconds = (int)Math.Clamp(args.NewValue, 30, 600_000);
            this._session.Settings.Save();
        }
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await this.RunCoreAsync("Refresh", (device, token) => device.RefreshAsync(token), "All the properties were read again");
    }

    private void NameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (this._ready && !this._updating)
        {
            this._nameDirty = true;
        }

        this.UpdateNameCount();
    }

    private async void NameBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            await this.SetNameAsync();
        }
    }

    private async void SetNameButton_Click(object sender, RoutedEventArgs e) => await this.SetNameAsync();

    private async Task SetNameAsync()
    {
        var name = this.NameBox.Text;
        this._nameDirty = false;
        await this.RunCoreAsync($"Set name to \"{name}\"", (device, token) => device.SetNameAsync(name, token));
    }

    private async void SetTimerButton_Click(object sender, RoutedEventArgs e)
    {
        await this.RunCoreAsync(
            "Set sleep timer",
            (device, token) => device.SetSleepTimerAsync(TimeSpan.FromMinutes(double.IsNaN(this.TimerBox.Value) ? 0 : this.TimerBox.Value), token),
            $"The light turns itself off in {this.TimerBox.Value:0} minutes");
    }

    private async void GetTimerButton_Click(object sender, RoutedEventArgs e)
    {
        TimeSpan? timer = null;
        var outcome = await this.RunCoreOutcomeAsync("Get sleep timer", async (device, token) => timer = await device.GetSleepTimerAsync(token));
        if (outcome is { IsSuccess: true })
        {
            this._runner.Show(Outcome.Succeeded("Get sleep timer", timer is { } length ? $"The light has a timer of {length.TotalMinutes:0} minutes" : "The light has no sleep timer", outcome.Elapsed));
        }
    }

    private async Task<Outcome?> RunCoreOutcomeAsync(string what, Func<YeelightDevice, CancellationToken, Task> command)
    {
        if (this._item is not { } item)
        {
            return null;
        }

        var outcome = await this._runner.RunAsync(what, token => command(item.Device, token));
        this.UpdateAll();
        return outcome;
    }

    private async void CancelTimerButton_Click(object sender, RoutedEventArgs e)
    {
        await this.RunCoreAsync("Cancel sleep timer", (device, token) => device.CancelSleepTimerAsync(token));
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        await this.RunCoreAsync("Connect", (device, token) => device.ConnectAsync(token), "There is a connection to the light");
    }

    private async void DisconnectButton_Click(object sender, RoutedEventArgs e)
    {
        await this.RunCoreAsync("Disconnect", (device, _) => device.DisconnectAsync(), "The connection was closed; a command makes a new one");
    }

    private async void ToggleAllButton_Click(object sender, RoutedEventArgs e)
    {
        await this.RunCoreAsync("Toggle both lights", (device, token) => device.ToggleAllAsync(token));
    }

    private async void StartMusicButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (this._item is not { } item)
            {
                return;
            }

            IPAddress? address = null;
            var text = this.MusicAddressBox.Text.Trim();
            if (text.Length > 0 && !IPAddress.TryParse(text, out address))
            {
                ((ICommandHost)this).Reject("Start music mode", $"\"{text}\" is not an IP address. Leave the box empty to use the address that reaches the light.");
                return;
            }

            this._musicStoppedByUser = false;
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await this._runner.RunAsync(
                "Start music mode",
                _ => this._session.StartMusicAsync(item, address, limit.Token),
                "The light connected: commands over the session have no quota");
            this.UpdateMusic();
        }
        catch (Exception exception)
        {
            ((ICommandHost)this).Reject("Start music mode", exception.Message);
        }
    }

    private async void StopMusicButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (this._item is not { } item)
            {
                return;
            }

            this._musicStoppedByUser = true;
            this._demo?.Cancel();
            await this._runner.RunAsync("Stop music mode", _ => this._session.StopMusicAsync(item.Id), "The session was closed");
            this.UpdateMusic();
        }
        catch (Exception exception)
        {
            ((ICommandHost)this).Reject("Stop music mode", exception.Message);
        }
    }

    private async void RunDemoButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await this.RunDemoCoreAsync();
        }
        catch (Exception exception)
        {
            ((ICommandHost)this).Reject("Music mode demo", exception.Message);
        }
    }

    private void StopDemoButton_Click(object sender, RoutedEventArgs e) => this._demo?.Cancel();

    private async Task RunDemoCoreAsync()
    {
        if (this._item is not { } item || this._session.GetMusic(item.Id) is not { } music || this._demoRunning)
        {
            return;
        }

        var duration = TimeSpan.FromSeconds(double.IsNaN(this.DemoSecondsBox.Value) ? 10 : Math.Clamp(this.DemoSecondsBox.Value, 1, 300));
        var color = Can(item.Device, "set_rgb") || Can(item.Device, "set_hsv");
        using var cancel = new CancellationTokenSource();
        this._demo = cancel;
        this._demoRunning = true;
        this.DemoProgress.Value = 0;
        this.DemoResult.Text = "Running\u2026";
        this.UpdateMusic();
        var clock = Stopwatch.StartNew();
        var progress = new Progress<int>(sent =>
        {
            this.DemoProgress.Value = Math.Min(1, clock.Elapsed.TotalSeconds / duration.TotalSeconds);
            this.DemoResult.Text = $"Sent {sent} commands in {clock.Elapsed.TotalSeconds:0.0} s ({sent / Math.Max(0.001, clock.Elapsed.TotalSeconds):0.0} a second). No quota applies.";
        });
        try
        {
            var result = await MusicDemo.RunAsync(music, color, duration, MusicDemo.CommandsPerSecond, progress, cancel.Token);
            this.DemoProgress.Value = result.Cancelled || result.ConnectionLost ? this.DemoProgress.Value : 1;
            this.DemoResult.Text = result.Describe();
            this._runner.Show(new Outcome(
                result.ConnectionLost ? OutcomeSeverity.Warning : OutcomeSeverity.Success,
                result.ConnectionLost ? "Music mode demo: the session ended" : "Music mode demo: done",
                result.Describe(),
                result.Elapsed));
        }
        finally
        {
            this._demoRunning = false;
            this._demo = null;
            this.UpdateMusic();
        }
    }

    private void GoToLightsButton_Click(object sender, RoutedEventArgs e) => App.MainWindow?.Navigate("lights");

    private async void StartSimulatedButton_Click(object sender, RoutedEventArgs e)
    {
        await this._runner.RunAsync(
            "Start the simulated lights",
            token => this._session.StartSimulatedAsync([SimulatedKind.Color, SimulatedKind.White, SimulatedKind.Ceiling], token),
            "Three simulated lights run in this app, on the loopback address");
        this.OnSelectedChanged();
    }
}
