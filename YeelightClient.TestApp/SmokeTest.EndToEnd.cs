using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Yeelight.TestApp.Services;
using Yeelight.TestApp.Models;
using Yeelight.TestApp.Session;
using Yeelight.TestApp.Views;

namespace Yeelight.TestApp;

internal static partial class SmokeTest
{
    /// <summary>Runs the smoke test and ends the app.</summary>
    /// <param name="window">The window.</param>
    /// <param name="path">The log.</param>
    /// <returns>A task that ends when the app is told to exit.</returns>
    public static async Task RunAsync(MainWindow window, string path)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
        AppSettings.ExportFolderOverride = Path.Combine(folder, "smoke-test-exports");
        Record($"smoke test of {typeof(App).Assembly.GetName().Name} {VersionInfo.OfLibrary().Version}, on {Environment.OSVersion.VersionString}; pictures and log go to {folder}");
        _ = EndAfterWatchdogAsync(window, path);
        var pulse = new Pulse(window.DispatcherQueue);
        try
        {
            await Task.Delay(800);
            await new Scenario(window, folder, pulse).PlayAsync();
        }
        catch (Exception exception)
        {
            Record($"FAILED: the smoke test stopped on an exception: {exception}", isProblem: true);
        }

        pulse.Stop();
        Check(pulse.LongestGap < TimeSpan.FromSeconds(2.5), $"the user interface was never blocked for long (the longest it did not respond: {pulse.LongestGap.TotalMilliseconds:0} ms)");
        await FinishAsync(window, path);
    }

    private static async Task EndAfterWatchdogAsync(MainWindow window, string path)
    {
        await Task.Delay(Watchdog);
        Record($"FAILED: the smoke test was not done after {Watchdog.TotalSeconds:0} seconds, and ends itself", isProblem: true);
        await FinishAsync(window, path);
    }

    /// <summary>Measures how long the thread of the user interface did not respond: it must never wait for a network.</summary>
    private sealed class Pulse
    {
        private readonly DispatcherQueueTimer _timer;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private TimeSpan _last;
        private TimeSpan _longest;
        private volatile bool _paused;

        public Pulse(DispatcherQueue queue)
        {
            this._timer = queue.CreateTimer();
            this._timer.Interval = TimeSpan.FromMilliseconds(25);
            this._timer.IsRepeating = true;
            this._timer.Tick += (_, _) =>
            {
                var now = this._clock.Elapsed;
                if (!this._paused)
                {
                    this._longest = TimeSpan.FromTicks(Math.Max(this._longest.Ticks, (now - this._last).Ticks));
                }

                this._last = now;
            };
            this._last = this._clock.Elapsed;
            this._timer.Start();
        }

        public TimeSpan LongestGap => this._longest;

        // A picture of the window is taken on the thread of the user interface, which does nothing else while it is made.
        public void Pause() => this._paused = true;

        public void Resume()
        {
            this._last = this._clock.Elapsed;
            this._paused = false;
        }

        public void Stop() => this._timer.Stop();
    }

    /// <summary>The scenario: a person who tries the app against simulated lights, one page at a time.</summary>
    private sealed partial class Scenario(MainWindow window, string folder, Pulse pulse)
    {
        private static AppSession Session => App.Session;

        public async Task PlayAsync()
        {
            await this.PhaseAsync("The app at the start", this.StartStateAsync);
            await this.PhaseAsync("Lights: the simulated lights, forget and probe", this.LightsAsync);
            await this.PhaseAsync("Control: the colour bulb", this.ControlColorAsync);
            await this.PhaseAsync("Control: the colour flow editor", this.ControlFlowAsync);
            await this.PhaseAsync("Control: name, sleep timer and connection", this.ControlDeviceAsync);
            await this.PhaseAsync("Control: the light follows the light", this.ControlFollowsAsync);
            await this.PhaseAsync("Control: a light without colours", this.ControlMonoAsync);
            await this.PhaseAsync("Control: the background light of a ceiling light", this.ControlBackgroundAsync);
            await this.PhaseAsync("Control: music mode", this.ControlMusicAsync);
            await this.PhaseAsync("Raw: any command", this.RawAsync);
            await this.PhaseAsync("Raw: properties and the reference", this.RawPropertiesAsync);
            await this.PhaseAsync("Settings: a new client, a slow light, a light that goes away", this.SettingsAsync);
            await this.PhaseAsync("Events and Log", this.EventsAndLogAsync);
            await this.PhaseAsync("Help, the theme, and stopping the simulated lights", this.HelpAndThemeAsync);
        }

        private async Task PhaseAsync(string name, Func<Task> body)
        {
            Record($"--- {name}");
            try
            {
                await body();
            }
            catch (Exception exception)
            {
                Record($"FAILED: {name}: {exception.GetType().Name}: {exception.Message}{Environment.NewLine}{exception.StackTrace}", isProblem: true);
            }
        }

        private async Task<T> OpenAsync<T>(string tag)
            where T : class
        {
            window.Navigate(tag);
            await Task.Delay(120);
            var page = window.CurrentPage as T;
            Check(page is not null, $"the {tag} page opens ({window.CurrentPage?.GetType().Name})");
            return page ?? throw new InvalidOperationException($"The {tag} page did not open.");
        }

        private async Task ShotAsync(string name)
        {
            await Task.Delay(250);
            pulse.Pause();
            try
            {
                await CaptureAsync(window, folder, name);
            }
            finally
            {
                pulse.Resume();
            }
        }

        private async Task StartStateAsync()
        {
            Check(!Session.IsSearching && !Session.IsListening, "the client neither searches nor listens when the app starts: nothing is sent to the network");
            Check(Session.Lights.Count == 0 && Session.Simulated.Running.Count == 0, "no light is known and no simulated light runs when the app starts");
            Check(Session.Client.Devices.Count == 0, "the client of the app knows no light");

            var lights = await this.OpenAsync<LightsPage>("lights");
            Check(lights.ShowsEmptyText && lights.Count == "No lights", "the Lights page explains that there are no lights yet");
            Check(!lights.SearchEverySwitchControl.IsOn && !lights.ListenSwitchControl.IsOn, "searching and listening are off");
            Check(!lights.OpenButtonControl.IsEnabled && !lights.ForgetButtonControl.IsEnabled, "Open and Forget wait for a light");

            var control = await this.OpenAsync<ControlPage>("control");
            Check(control.ShowsEmptyText, "the Control page explains that no light is selected");

            var raw = await this.OpenAsync<RawPage>("raw");
            Check(!Find<Button>(raw, "SendButton").IsEnabled, "the Raw page cannot send a command before there is a light");
            Check(raw.Methods.Count == ProtocolCatalog.Methods.Count && raw.ReferenceRows.Count == ProtocolCatalog.Properties.Count, "the reference of the Raw page lists every method and property of the protocol");

            var events = await this.OpenAsync<EventsPage>("events");
            Check(events.ShowsEmptyText, "the Events page explains that there are no events yet");
            var log = await this.OpenAsync<LogPage>("log");
            Check(log.ShowsEmptyText, "the Log page explains that there is nothing in the log yet");
            await this.OpenAsync<LightsPage>("lights");
        }

        private async Task LightsAsync()
        {
            var page = await this.OpenAsync<LightsPage>("lights");
            Click(Find<MenuFlyoutItem>(page, "StartAllItem"), "Start all three");
            await UntilAsync(() => Session.Lights.Count == 3 && Session.Lights.All(light => light.IsConnected), "the three simulated lights are found with a probe on the loopback and connected", 12);
            foreach (var item in Session.Lights.ToArray())
            {
                await WaitForConnectionAsync(item);
            }

            var kinds = new[] { SimulatedKind.Color, SimulatedKind.White, SimulatedKind.Ceiling };
            Check(kinds.All(kind => Bulb(kind).ClientCount == 1 && Bulb(kind).ConnectionCount == 1), "every simulated light has exactly one control connection from the client");
            Check(kinds.All(kind => Bulb(kind).Requests.Count == 1 && Bulb(kind).Requests[0].Method == "get_prop"), "the only thing that each light was asked is get_prop, when the client connected");
            Check(kinds.All(kind => Bulb(kind).SearchCount >= 1), "each light was searched by the probe (and the app searched nothing else: it never searches by itself)");
            await UntilAsync(() => page.Count == "3 lights, 3 connected", () => $"the Lights page counts the lights: {page.Count}", 4);
            Check(page.Notice.Contains("3 simulated lights are running", StringComparison.Ordinal) && page.NoticeSeverity == InfoBarSeverity.Success, "the Lights page says that the simulated lights run, on the loopback");
            Check(page.Items.Select(light => light.Title).OrderBy(title => title).SequenceEqual(["Simulated ceiling light", "Simulated colour bulb", "Simulated white bulb"]), "the lights have distinct names");
            Check(page.Items.All(light => light.Device.Address is { } address && address.Equals(System.Net.IPAddress.Loopback)), "all the lights are on the loopback");

            Check(Session.Selected == Item(SimulatedKind.Color), "the first light that was found is selected");
            Check(page.DetailRows.Any(row => row.Name == "Model" && row.Value == "color") && page.DetailRows.Any(row => row.Name == "Brightness" && row.Value == "100%"), "the details show the properties of the selected light");

            page.LightListControl.SelectedItem = Item(SimulatedKind.Ceiling);
            await UntilAsync(() => Session.Selected == Item(SimulatedKind.Ceiling) && page.DetailRows.Any(row => row.Name == "Background Power"), "selecting the ceiling light shows its background light too");
            Check(page.DetailMethods.Contains("bg_set_power") && page.DetailMethods.Contains("set_rgb"), "the methods of the ceiling light include the ones of its background light");
            page.LightListControl.SelectedItem = Item(SimulatedKind.White);
            await UntilAsync(() => Session.Selected == Item(SimulatedKind.White) && page.DetailMethods.Count > 0 && !page.DetailMethods.Contains("set_rgb"), "the white bulb lists no colour methods");
            await this.ShotAsync("lights");

            // Forget a light, and find it again with the box of the page and the port of the simulator.
            var ceiling = Bulb(SimulatedKind.Ceiling);
            page.LightListControl.SelectedItem = Item(SimulatedKind.Ceiling);
            await UntilAsync(() => Session.Selected == Item(SimulatedKind.Ceiling), "the ceiling light is selected again");
            Click(page.ForgetButtonControl, "Forget");
            await UntilAsync(() => Session.Lights.Count == 2 && Session.FindItem(ceiling.Id) is null, "Forget drops the light from the list");
            await UntilAsync(() => ceiling.ClientCount == 0, "the connection to the forgotten light was closed");
            page.ProbeBoxControl.Text = $"127.0.0.1:{ceiling.DiscoveryPort}";
            Click(page.ProbeButtonControl, "Probe address");
            await UntilAsync(() => Session.Lights.Count == 3 && Session.FindItem(ceiling.Id) is { IsConnected: true }, "Probe address finds the light again, with the port of the simulator", 8);
            await WaitForConnectionAsync(Item(SimulatedKind.Ceiling));

            // A probe that cannot work is reported, and the app goes on.
            page.ProbeBoxControl.Text = "not an address";
            Click(page.ProbeButtonControl, "Probe address");
            await UntilAsync(() => page.NoticeSeverity == InfoBarSeverity.Warning && page.Notice.Contains("not an IP address", StringComparison.Ordinal), () => $"a text that is no address is reported: {page.Notice}");
            page.ProbeBoxControl.Text = "127.0.0.1:9";
            Click(page.ProbeButtonControl, "Probe address");
            await UntilAsync(() => page.Notice.Contains("Nothing answered", StringComparison.Ordinal), () => $"a probe that nobody answers is reported: {page.Notice}", 8);
            Check(Session.Lights.Count == 3, "the failed probes changed nothing");
            Session.Select(Item(SimulatedKind.Color), byUser: true);
        }
    }
}
