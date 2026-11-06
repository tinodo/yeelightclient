using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Yeelight.Tests.TestSupport;
using Yeelight.TestApp.Models;
using Yeelight.TestApp.Services;
using Yeelight.TestApp.Views;

namespace Yeelight.TestApp;

internal static partial class SmokeTest
{
    private sealed partial class Scenario
    {
        private async Task RawAsync()
        {
            var bulb = Bulb(SimulatedKind.Color);
            var item = Item(SimulatedKind.Color);
            Session.Select(item, byUser: true);
            var raw = await this.OpenAsync<RawPage>("raw");
            raw.SelectTab("send");
            await UntilAsync(() => raw.Current == item && Find<Button>(raw, "SendButton").IsEnabled, "the Raw page works on the selected light");
            var send = Find<Button>(raw, "SendButton");
            var firstHistory = raw.History.Count;

            async Task<Outcome?> SendAsync(string what, string method, string parameters, RawTarget target, params string[] expected)
            {
                Outcome? outcome = null;
                raw.Fill(method, parameters, target);
                await ExpectAsync(
                    bulb,
                    what,
                    async () => outcome = await RunAsync(raw, () => Click(send, "Send")),
                    expected);
                return outcome;
            }

            var power = await SendAsync("raw set_power", "set_power", "[\"off\", \"sudden\", 0]", RawTarget.Device, "set_power [\"off\",\"sudden\",0]");
            Check(power is { IsSuccess: true } && raw.BarSeverity == InfoBarSeverity.Success && raw.ResultText.Contains("Success:  true", StringComparison.Ordinal) && raw.ResultText.Contains("Values:   [\"ok\"]", StringComparison.Ordinal) && raw.ResultText.Contains("Id:", StringComparison.Ordinal) && raw.ResultText.Contains("Elapsed:", StringComparison.Ordinal), $"the answer shows the id, the success, the error and the values:{Environment.NewLine}{raw.ResultText}");
            Check(raw.History.Count == firstHistory + 1 && raw.History[0] is { Success: true, Method: "set_power" }, "the command is in the history");

            var refused = await SendAsync("raw set_bright to a light that is off", "set_bright", "[50, \"sudden\", 0]", RawTarget.Device, "set_bright [50,\"sudden\",0]");
            Check(refused is { Severity: OutcomeSeverity.Warning } && raw.BarSeverity == InfoBarSeverity.Warning && raw.ResultText.Contains("Success:  false", StringComparison.Ordinal) && raw.ResultText.Contains("code -1, \"general error\", kind DeviceRejected", StringComparison.Ordinal), $"a failing command shows the error of the light ({raw.ResultText.Replace(Environment.NewLine, " / ")})");
            Check(raw.History[0] is { Success: false, Method: "set_bright" }, "the failed command is in the history, as a failure");

            var unknown = await SendAsync("raw with a method that does not exist", "no_such_method", "[]", RawTarget.Device, "no_such_method []");
            Check(unknown is { Severity: OutcomeSeverity.Warning } && raw.ResultText.Contains("unsupported method", StringComparison.Ordinal), "the light answers that it does not support a method that does not exist");
            Check(raw.MethodHelp.Contains("not a method of the protocol", StringComparison.Ordinal), $"the page says that the method is not in the protocol ({raw.MethodHelp})");

            var invalid = await SendAsync("raw with parameters that are not JSON", "set_power", "[1,", RawTarget.Device);
            Check(invalid is { Severity: OutcomeSeverity.Warning } && raw.BarText.Contains("cannot be read", StringComparison.Ordinal), $"parameters that cannot be read are reported, and nothing is sent ({raw.BarText})");

            var props = await SendAsync("raw get_prop", "get_prop", "[\"power\", \"bright\"]", RawTarget.Device, "get_prop [\"power\",\"bright\"]");
            Check(props is { IsSuccess: true } && raw.ResultText.Contains($"\"{bulb.Get("power")}\"", StringComparison.Ordinal), $"get_prop answers with the values in order ({raw.ResultText.Replace(Environment.NewLine, " / ")})");

            // The background light of the ceiling light, sent as typed.
            var ceilingBulb = Bulb(SimulatedKind.Ceiling);
            Session.Select(Item(SimulatedKind.Ceiling), byUser: true);
            await UntilAsync(() => raw.Current == Item(SimulatedKind.Ceiling), "the Raw page follows the selection");
            raw.Fill("bg_set_power", "[\"on\", \"sudden\", 0]", RawTarget.Background);
            await ExpectAsync(
                ceilingBulb,
                "raw to the background light",
                async () => await RunAsync(raw, () => Click(send, "Send")),
                "bg_set_power [\"on\",\"sudden\",0]");
            Check(raw.LastOutcome is { IsSuccess: true } && Item(SimulatedKind.Ceiling).Device.Background.IsOn, "the background light is on");

            // The history fills the form again, with the target.
            var music = raw.History[^1];
            Check(music is { Method: "set_bright", Target: RawTarget.Music }, $"the first command, over the music session, is the oldest in the history ({music.Command})");
            Find<ListView>(raw, "HistoryList").SelectedItem = music;
            Check(Find<ComboBox>(raw, "MethodBox").Text == "set_bright" && Find<TextBox>(raw, "ParamsBox").Text == music.Parameters && Find<ComboBox>(raw, "TargetBox").SelectedIndex == 2, "selecting a command of the history fills in the form: the method, the parameters and the target");

            // Examples, and the line about the method.
            raw.Fill("set_scene", string.Empty);
            Click(Find<Button>(raw, "FillExampleButton"), "Fill in an example");
            var scene = ProtocolCatalog.Find("set_scene")!;
            Check(Find<TextBox>(raw, "ParamsBox").Text == scene.Examples[0] && raw.MethodHelp.Contains("Takes:", StringComparison.Ordinal), "Fill in an example fills in the first example, and the page explains the method");
            Click(Find<Button>(raw, "NextExampleButton"), "Next example");
            Check(Find<TextBox>(raw, "ParamsBox").Text == scene.Examples[1], "Next example fills in the next one");

            // The answer that is on show when the picture is taken: a refusal, with all its details.
            Session.Select(item, byUser: true);
            await UntilAsync(() => raw.Current == item, "the Raw page is back at the colour bulb");
            await SendAsync("raw set_bright, again", "set_bright", "[50, \"sudden\", 0]", RawTarget.Device, "set_bright [50,\"sudden\",0]");
            await this.ShotAsync("raw");
        }

        private async Task RawPropertiesAsync()
        {
            var bulb = Bulb(SimulatedKind.Color);
            var raw = await this.OpenAsync<RawPage>("raw");
            raw.SelectTab("properties");
            await Task.Delay(120);
            Find<TextBox>(raw, "NamesBox").Text = "power bright nonexistent";
            await ExpectAsync(
                bulb,
                "Get properties of three names",
                async () => await RunAsync(raw, () => Click(Find<Button>(raw, "GetPropertiesButton"), "Get properties")),
                "get_prop [\"power\",\"bright\",\"nonexistent\"]");
            Check(raw.PropertyValues.Count == 3 && raw.PropertyValues[0] == new PropertyRow("power", bulb.Get("power")) && raw.PropertyValues[1] == new PropertyRow("bright", bulb.Get("bright")), $"the values are those of the light ({string.Join(", ", raw.PropertyValues.Select(row => $"{row.Name}={row.Value}"))})");
            Check(raw.PropertyValues[2].Value.StartsWith("(empty", StringComparison.Ordinal), "a name that the light does not know has an empty value");
            Check(raw.PropertiesText.Contains("3 properties read", StringComparison.Ordinal), $"the page says how long it took ({raw.PropertiesText})");

            Click(Find<Button>(raw, "AllNamesButton"), "All names");
            var all = ProtocolCatalog.PropertyNames;
            Check(Find<TextBox>(raw, "NamesBox").Text.Split(' ').SequenceEqual(all), "All names fills in every property of the protocol");
            await ExpectAsync(
                bulb,
                "Get properties of every name",
                async () => await RunAsync(raw, () => Click(Find<Button>(raw, "GetPropertiesButton"), "Get properties")),
                "get_prop [" + string.Join(",", all.Select(Json)) + "]");
            Check(raw.PropertyValues.Count == all.Count && raw.PropertiesText.Contains($"{all.Count} properties read", StringComparison.Ordinal), $"all {all.Count} properties are listed");
            Check(raw.PropertyValues.Single(row => row.Name == "name").Value == bulb.Name, "one of them is the name of the light, as the light has it");

            // The reference: every method and property, and a filter.
            raw.SelectTab("reference");
            await Task.Delay(120);
            Check(raw.Methods.Count >= 30 && raw.Methods.Any(method => method.Name == "set_power") && raw.Methods.Any(method => method.Name == "bg_set_scene") && raw.ReferenceRows.Count == 23, $"the reference lists {raw.Methods.Count} methods and {raw.ReferenceRows.Count} properties");
            Find<AutoSuggestBox>(raw, "ReferenceFilter").Text = "scene";
            await UntilAsync(() => raw.Methods.Count < ProtocolCatalog.Methods.Count, () => $"the filter of the reference works when it is typed in ({raw.Methods.Count} methods are left)");
            Check(raw.Methods.Count > 0 && raw.Methods.All(method => method.Name.Contains("scene", StringComparison.Ordinal) || method.Summary.Contains("scene", StringComparison.OrdinalIgnoreCase) || method.Parameters.Contains("scene", StringComparison.OrdinalIgnoreCase) || method.Group.Contains("scene", StringComparison.OrdinalIgnoreCase)) && raw.Methods.Any(method => method.Name == "set_scene") && raw.Methods.Any(method => method.Name == "bg_set_scene"), $"the filter keeps the methods about scenes ({string.Join(", ", raw.Methods.Select(method => method.Name))})");
            Find<AutoSuggestBox>(raw, "ReferenceFilter").Text = string.Empty;
            await UntilAsync(() => raw.Methods.Count == ProtocolCatalog.Methods.Count, "an empty filter shows everything again");
            raw.SelectTab("send");
        }

        private async Task SettingsAsync()
        {
            var bulb = Bulb(SimulatedKind.Color);
            var old = Session.Client;
            var settings = await this.OpenAsync<SettingsPage>("settings");

            // Settings that cannot be used are refused, and the client stays as it is (a box without a number is what a person can leave behind).
            var timeoutBox = Find<NumberBox>(settings, "CommandTimeoutBox");
            settings.Edit(commandTimeout: 1, commandsPerMinute: 45, autoConnect: true);
            timeoutBox.Value = double.NaN;
            Check(!await settings.ApplyAsync(confirm: false) && ReferenceEquals(Session.Client, old) && settings.StatusText.Contains("cannot be used", StringComparison.Ordinal), $"settings that the client cannot use are refused ({settings.StatusText})");

            // Applying makes a new client: the list is cleared, and the old client lets go of the lights.
            settings.Edit(commandTimeout: 1, commandsPerMinute: 45, autoConnect: true);
            Check(await settings.ApplyAsync(confirm: false), $"the settings are applied ({settings.StatusText})");
            await this.ShotAsync("settings");
            Check(!ReferenceEquals(Session.Client, old), "applying the settings made a new client");
            Check(Session.Settings.CommandsPerMinute == 45 && Math.Abs(Session.Settings.CommandTimeoutSeconds - 1) < 0.001, "the new client has the new settings");
            Check(Session.Lights.Count == 0 && Session.Selected is null && Session.Client.Devices.Count == 0, "the list of lights was cleared");
            await UntilAsync(() => Enum.GetValues<SimulatedKind>().All(kind => Bulb(kind).ClientCount == 0), "the connections of the old client to the lights were closed", 8);
            var disposed = false;
            try
            {
                await old.ProbeAsync(System.Net.IPAddress.Loopback, bulb.DiscoveryPort);
            }
            catch (ObjectDisposedException)
            {
                disposed = true;
            }

            Check(disposed, "the old client is disposed");
            Check(Session.Log.Snapshot().Any(entry => entry.Text.StartsWith("New client.", StringComparison.Ordinal)), "the log notes the new client and its settings");
            var lights = await this.OpenAsync<LightsPage>("lights");
            Check(lights.ShowsEmptyText, "the Lights page is empty again, and explains it");
            var control = await this.OpenAsync<ControlPage>("control");
            Check(control.ShowsEmptyText, "the Control page has no light any more");

            // Find a light again, with the box of the Lights page.
            await this.OpenAsync<LightsPage>("lights");
            var props = bulb.RequestsOf("get_prop").Count;
            lights.ProbeBoxControl.Text = $"127.0.0.1:{bulb.DiscoveryPort}";
            Click(lights.ProbeButtonControl, "Probe address");
            await UntilAsync(() => Session.Lights.Count == 1 && Session.FindItem(bulb.Id) is { IsConnected: true } && bulb.RequestsOf("get_prop").Count == props + 1, "the colour bulb is found again by the new client, and connected", 8);
            await Task.Delay(200);
            var item = Item(SimulatedKind.Color);
            Check(Session.Selected == item, "the light that was found is selected");

            // The command timeout is the one that was applied: one second, not the three of the library.
            var page = await this.ShowLightAsync(SimulatedKind.Color);
            bulb.Delay = request => request.Method == "get_prop" ? TimeSpan.FromSeconds(2.5) : TimeSpan.Zero;
            var timedOut = await RunAsync(page, () => Click(Find<Button>(page, "RefreshButton"), "Refresh"));
            Check(timedOut is { Severity: OutcomeSeverity.Error } && timedOut.Message.Contains("Timeout", StringComparison.Ordinal) && timedOut.Elapsed > TimeSpan.FromSeconds(0.8) && timedOut.Elapsed < TimeSpan.FromSeconds(2.2), $"a light that is slow is reported as a timeout after the second that was set ({timedOut?.Summary})");
            Check(Find<InfoBar>(page, "OutcomeBar").Severity == InfoBarSeverity.Error, "the bar at the bottom shows it as an error");

            // A light that disappears in the middle of a command.
            bulb.Delay = request => request.Method == "get_prop" ? TimeSpan.FromSeconds(2) : TimeSpan.Zero;
            var completed = page.Runner.Completed;
            Click(Find<Button>(page, "RefreshButton"), "Refresh");
            await Task.Delay(300);
            bulb.DropConnections();
            await UntilAsync(() => page.Runner.Completed > completed && !page.Runner.IsBusy, "the command ends when the light drops the connection", 5);
            var lost = page.Runner.Last;
            Check(lost is { Severity: OutcomeSeverity.Error } && lost.Message.Contains("ConnectionLost", StringComparison.Ordinal) && lost.Elapsed < TimeSpan.FromSeconds(1.8), $"the loss of the connection is reported, long before the light would have answered ({lost?.Summary})");
            bulb.Delay = _ => TimeSpan.Zero;
            await UntilAsync(() => item.Device.IsConnected || bulb.ClientCount > 0, "the client connects to the light again", 10);
            var again = await RunAsync(page, () => Click(Find<Button>(page, "RefreshButton"), "Refresh"));
            Succeeded(again, "Refresh after the light came back");
        }

        private async Task EventsAndLogAsync()
        {
            var colour = Item(SimulatedKind.Color);
            var events = await this.OpenAsync<EventsPage>("events");
            events.PollNow();
            await Task.Delay(300);
            var all = Session.Events.Snapshot();
            Check(all.Count(entry => entry.Kind == EventKind.DeviceDiscovered) >= 5, $"DeviceDiscovered was raised for every light that was found, also when it was found again ({all.Count(entry => entry.Kind == EventKind.DeviceDiscovered)})");
            Check(all.Any(entry => entry.Kind == EventKind.DeviceStateChanged && entry.Property == "Brightness") && all.Any(entry => entry.Kind == EventKind.DeviceStateChanged && entry.IsBackground) && all.Any(entry => entry.Kind == EventKind.PropertyChanged), "DeviceStateChanged (also of a background light) and PropertyChanged were raised");
            Check(events.Shown.Count > 50 && !events.ShowsEmptyText, $"the Events page lists them ({events.Count})");
            events.SetFilter(null, EventKind.DeviceDiscovered, string.Empty);
            events.PollNow();
            Check(events.Shown.Count >= 5 && events.Shown.All(entry => entry.Kind == EventKind.DeviceDiscovered), "the filter by kind keeps the discoveries");
            events.SetFilter(colour.Id, null, string.Empty);
            events.PollNow();
            Check(events.Shown.Count > 0 && events.Shown.All(entry => string.Equals(entry.LightId, colour.Id, StringComparison.OrdinalIgnoreCase)), $"the filter by light keeps the events of that light ({events.Shown.Count})");
            events.SetFilter(null, null, "Brightness");
            events.PollNow();
            Check(events.Shown.Count > 0 && events.Shown.All(entry => entry.Summary.Contains("Brightness", StringComparison.Ordinal) || entry.Property.Contains("Brightness", StringComparison.Ordinal)), "the filter by text keeps what mentions Brightness");
            events.SelectFirst();
            Check(events.Detail.Contains("Brightness", StringComparison.Ordinal), "selecting an event shows its data");
            events.SetFilter(null, null, string.Empty);
            events.PollNow();
            var exported = events.Export();
            Check(exported is not null && File.Exists(exported) && events.Shown.Count > 50 && File.ReadAllLines(exported).Length == events.Shown.Count && File.ReadAllLines(exported).All(line => JsonDocument.Parse(line).RootElement.ValueKind == JsonValueKind.Object), $"Export wrote the {events.Shown.Count} events as JSON lines ({exported})");
            await this.ShotAsync("events");

            var log = await this.OpenAsync<LogPage>("log");

            // What the client logs goes through ILogger: a warning of the client (here made the way the library makes one) is in the log too.
            Session.Logger.LogWarning("{Light}: the smoke test makes a warning, to see it in the log", colour.Id);
            log.PollNow();
            await Task.Delay(300);
            var entries = Session.Log.Snapshot();
            Check(entries.Any(entry => entry.Kind == LogKind.Sent && entry.Method == "set_bright" && entry.Text.Contains("\"method\":\"set_bright\"", StringComparison.Ordinal) && entry.Text.Contains("[35,\"smooth\",500]", StringComparison.Ordinal)), "the log has the set_bright that was sent with a transition, as it went over the wire");
            Check(entries.Any(entry => entry.Kind == LogKind.Received && entry.Method == "set_bright" && entry.ElapsedMs is > 0), "the log has the answers, with the time that the light took");
            Check(entries.Any(entry => entry.Failed) && entries.Any(entry => entry.Kind == LogKind.Client && entry.Level == LogLevel.Information && entry.Text.Contains("Connected", StringComparison.Ordinal)), "the log has answers that failed, and the information on a connection");
            Check(entries.Any(entry => entry.Level == LogLevel.Warning && string.Equals(entry.Light, colour.Id, StringComparison.OrdinalIgnoreCase) && entry.Text.Contains("smoke test makes a warning", StringComparison.Ordinal)), "a warning is in the log, with the light that it is about");
            var longest = entries.OrderByDescending(entry => entry.Text.Length).First();
            Check(longest.Text.Length > 250 && longest.Text.TrimEnd().EndsWith('}'), $"a long message is kept whole ({longest.Text.Length} characters)");
            Check(log.Shown.Count > 100 && !log.ShowsEmptyText, $"the Log page lists the entries ({log.Count})");
            log.SetFilter(LogLevel.Trace, colour.Id, "set_bright");
            log.PollNow();
            Check(log.Shown.Count > 0 && log.Shown.All(entry => string.Equals(entry.Light, colour.Id, StringComparison.OrdinalIgnoreCase) && (entry.Method == "set_bright" || entry.Title.Contains("set_bright", StringComparison.Ordinal) || entry.Text.Contains("set_bright", StringComparison.Ordinal))), $"the filter by light and text keeps the messages about set_bright of that light ({log.Shown.Count})");
            log.SetFilter(LogLevel.Warning, null, string.Empty);
            log.PollNow();
            Check(log.Shown.Count > 0 && log.Shown.All(entry => entry.Level >= LogLevel.Warning), $"the filter by level keeps the warnings ({log.Shown.Count})");
            log.SetFilter(LogLevel.Trace, null, string.Empty, sent: false, received: false, client: true);
            log.PollNow();
            Check(log.Shown.Count > 0 && log.Shown.All(entry => entry.Kind == LogKind.Client), "the filter by kind can hide the messages and keep the reports of the client");
            log.SetFilter(LogLevel.Trace, null, string.Empty);
            log.PollNow();
            log.SelectLast();
            Check(log.Detail.Length > 0, "selecting an entry shows it whole");
            var logExport = log.Export();
            Check(logExport is not null && File.Exists(logExport) && log.Shown.Count > 100 && File.ReadAllLines(logExport).Length == log.Shown.Count && File.ReadAllLines(logExport).All(line => JsonDocument.Parse(line).RootElement is { ValueKind: JsonValueKind.Object } row && (row.TryGetProperty("text", out _) || row.TryGetProperty("frame", out _))), $"Export wrote the {log.Shown.Count} entries of the log as JSON lines ({logExport})");
            await this.ShotAsync("log");

            // Clear forgets what was recorded.
            log.Reset();
            events.Reset();
            Check(log.Shown.Count == 0 && Session.Log.Count == 0 && log.ShowsEmptyText && events.Shown.Count == 0 && Session.Events.Count == 0 && events.ShowsEmptyText, "Clear empties the log and the events");
        }

        private async Task HelpAndThemeAsync()
        {
            var help = await this.OpenAsync<HelpPage>("help");
            Check(help.LibraryVersion.Length > 0 && char.IsDigit(help.LibraryVersion[0]), $"the Help page shows the version of the library ({help.LibraryVersion})");
            await this.ShotAsync("help");

            // The other two simulated lights are found again, for a picture of the Lights page in the dark theme.
            foreach (var kind in new[] { SimulatedKind.White, SimulatedKind.Ceiling })
            {
                await Session.ProbeLoopbackAsync(Bulb(kind).DiscoveryPort, CancellationToken.None);
            }

            await UntilAsync(() => Session.Lights.Count == 3 && Session.Lights.All(light => light.IsConnected), "all three lights are found again", 8);
            var settings = await this.OpenAsync<SettingsPage>("settings");
            Find<ComboBox>(settings, "ThemeBox").SelectedIndex = 2;
            await UntilAsync(() => Session.Settings.Theme == AppTheme.Dark && window.Content is FrameworkElement { RequestedTheme: ElementTheme.Dark }, "choosing Dark in the settings makes the app dark");
            var lights = await this.OpenAsync<LightsPage>("lights");
            await this.ShotAsync("lights-dark");
            Find<ComboBox>(await this.OpenAsync<SettingsPage>("settings"), "ThemeBox").SelectedIndex = 1;
            await UntilAsync(() => Session.Settings.Theme == AppTheme.Light && window.Content is FrameworkElement { RequestedTheme: ElementTheme.Light }, "choosing Light makes the app light");
            Find<ComboBox>(await this.OpenAsync<SettingsPage>("settings"), "ThemeBox").SelectedIndex = 0;
            await UntilAsync(() => Session.Settings.Theme == AppTheme.System, "the theme can go back to the one of the system");

            await this.SmallWindowAsync();

            // Stop the simulated lights, as a person does.
            lights = await this.OpenAsync<LightsPage>("lights");
            Click(lights.StopSimulatedButtonControl, "Stop simulated lights");
            await UntilAsync(() => Session.Simulated.Running.Count == 0 && Session.Lights.Count == 0, "Stop simulated lights stops them and forgets them", 8);
            Check(lights.ShowsEmptyText, "the Lights page is empty again");
        }

        // The app works from 900 by 600 up: at that size nothing sticks out of the window on the right, and the pages scroll.
        private async Task SmallWindowAsync()
        {
            var original = window.AppWindow.Size;
            var scale = window.Content.XamlRoot.RasterizationScale;
            window.AppWindow.Resize(new Windows.Graphics.SizeInt32((int)Math.Round(900 * scale), (int)Math.Round(600 * scale)));
            await Task.Delay(500);
            var root = (FrameworkElement)window.Content;
            Check(root.ActualWidth is > 850 and <= 900 && root.ActualHeight is > 540 and <= 600, $"the window is 900 by 600 ({root.ActualWidth:0} by {root.ActualHeight:0})");

            bool Inside(FrameworkElement element)
            {
                var box = element.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point(0, 0));
                return element.ActualWidth > 0 && box.X >= -1 && box.X + element.ActualWidth <= root.ActualWidth + 1;
            }

            Session.Select(Item(SimulatedKind.Color), byUser: true);
            var lights = await this.OpenAsync<LightsPage>("lights");
            await Task.Delay(250);
            Check(new FrameworkElement[] { lights.ProbeButtonControl, lights.StopSimulatedButtonControl, lights.OpenButtonControl, lights.ForgetButtonControl, Find<Button>(lights, "SearchButton") }.All(Inside), "at 900 by 600 the buttons of the Lights page are inside the window");
            await this.ShotAsync("small-lights");
            var control = await this.OpenAsync<ControlPage>("control");
            await Task.Delay(250);
            Check(new FrameworkElement[] { Find<Button>(control, "RefreshButton"), Find<ToggleSwitch>(control, "TransitionSwitch"), Find<ComboBox>(control, "LightBox"), control.MainLightPanel }.All(Inside), "at 900 by 600 the controls of the Control page are inside the window");
            await this.ShotAsync("small-control");
            var raw = await this.OpenAsync<RawPage>("raw");
            await Task.Delay(250);
            Check(new FrameworkElement[] { Find<Button>(raw, "SendButton"), Find<ComboBox>(raw, "MethodBox"), Find<ComboBox>(raw, "TargetBox") }.All(Inside), "at 900 by 600 the controls of the Raw page are inside the window");
            await this.ShotAsync("small-raw");
            window.AppWindow.Resize(original);
            await Task.Delay(250);
        }
    }
}
