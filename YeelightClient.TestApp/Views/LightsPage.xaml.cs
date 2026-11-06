using System.Collections.ObjectModel;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Yeelight.TestApp.Controls;
using Yeelight.TestApp.Models;
using Yeelight.TestApp.Services;
using Yeelight.TestApp.Session;

namespace Yeelight.TestApp.Views;

/// <summary>
/// The lights of the one client of the app, live. Searching, listening and probing start when a person asks for them: the app sends nothing
/// to the network by itself, and it never scans a network. The simulated lights (in this process, on the loopback) are for trying the app
/// without a light.
/// </summary>
public sealed partial class LightsPage : Page
{
    private static readonly TimeSpan SearchTime = TimeSpan.FromSeconds(4);
    private const double MinPageHeight = 800;

    private readonly AppSession _session = App.Session;
    private readonly ObservableCollection<PropertyRow> _rows = [];
    private LightItem? _watched;
    private bool _syncing;
    private bool _searching;
    private bool _probing;
    private bool _starting;

    /// <summary>Initializes a new instance of the <see cref="LightsPage"/> class.</summary>
    public LightsPage()
    {
        this.InitializeComponent();
        this.NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;

        this.IntervalBox.Value = this._session.Settings.SearchIntervalSeconds;
        this.ProbeBox.Text = this._session.Settings.ProbeAddress;
        this.PropertyList.ItemsSource = this._rows;

        this._session.LightsChanged += (_, _) => this.ShowLights();
        this._session.SelectedChanged += (_, _) => this.OnSelectedChanged();
        this._session.ClientReplaced += (_, _) => this.ShowLights();
        this._session.Ticked += (_, _) =>
        {
            this.ShowDetails();
            this.ShowLights();
        };
        this.ShowLights();
        this.OnSelectedChanged();
    }

    /// <summary>Gets the lights of the client, which the list shows.</summary>
    internal ObservableCollection<LightItem> Items => this._session.Lights;

    /// <summary>Gets the title of the notice bar and its message; for the smoke test.</summary>
    internal string Notice => this.NoticeBar.IsOpen ? this.NoticeBar.Title + " | " + this.NoticeBar.Message : string.Empty;

    /// <summary>Gets the severity of the notice bar; for the smoke test.</summary>
    internal InfoBarSeverity NoticeSeverity => this.NoticeBar.Severity;

    /// <summary>Gets the text above the list; for the smoke test.</summary>
    internal string Count => this.CountText.Text;

    /// <summary>Gets a value indicating whether the explanation of an empty list is shown; for the smoke test.</summary>
    internal bool ShowsEmptyText => this.EmptyPanel.Visibility == Visibility.Visible;

    /// <summary>Gets the rows of the details of the selected light; for the smoke test.</summary>
    internal IReadOnlyList<PropertyRow> DetailRows => this._rows;

    /// <summary>Gets the methods that the details list; for the smoke test.</summary>
    internal IReadOnlyList<string> DetailMethods => this.MethodList.ItemsSource as IReadOnlyList<string> ?? [];

    /// <summary>Gets the button that opens the light in the Control page; for the smoke test.</summary>
    internal Button OpenButtonControl => this.OpenButton;

    /// <summary>Gets the button that forgets the light; for the smoke test.</summary>
    internal Button ForgetButtonControl => this.ForgetButton;

    /// <summary>Gets the button that probes the address; for the smoke test.</summary>
    internal Button ProbeButtonControl => this.ProbeButton;

    /// <summary>Gets the box of the address to probe; for the smoke test.</summary>
    internal TextBox ProbeBoxControl => this.ProbeBox;

    /// <summary>Gets the button that stops the simulated lights; for the smoke test.</summary>
    internal Button StopSimulatedButtonControl => this.StopSimulatedButton;

    /// <summary>Gets the switch of the search that repeats; for the smoke test.</summary>
    internal ToggleSwitch SearchEverySwitchControl => this.SearchEverySwitch;

    /// <summary>Gets the switch of the listening; for the smoke test.</summary>
    internal ToggleSwitch ListenSwitchControl => this.ListenSwitch;

    /// <summary>Gets the list of lights; for the smoke test.</summary>
    internal ListView LightListControl => this.LightList;

    /// <summary>Starts simulated lights of the kinds, as the menu does, and finds them with a probe on the loopback.</summary>
    /// <param name="kinds">The kinds of lights.</param>
    /// <returns>A task that ends when the lights were found (or could not be).</returns>
    internal async Task StartSimulatedAsync(IReadOnlyList<SimulatedKind> kinds)
    {
        if (this._starting)
        {
            return;
        }

        this._starting = true;
        this.SimulatedButton.IsEnabled = false;
        try
        {
            var found = await this._session.StartSimulatedAsync(kinds, CancellationToken.None);
            if (found.Count == kinds.Count)
            {
                Fluent.Show(
                    this.NoticeBar,
                    InfoBarSeverity.Success,
                    found.Count == 1 ? "A simulated light is running" : $"{found.Count} simulated lights are running",
                    "They live in this app, on the loopback address (127.0.0.1), and were found with a probe: nothing left this computer. Select one and open it in the Control page.");
            }
            else
            {
                Fluent.Show(this.NoticeBar, InfoBarSeverity.Warning, "A simulated light did not answer", $"{found.Count} of {kinds.Count} were found. Look at the Log page for what the client did.");
            }
        }
        catch (Exception exception)
        {
            Fluent.Show(this.NoticeBar, InfoBarSeverity.Error, "The simulated lights could not be started", exception.Message);
        }
        finally
        {
            this._starting = false;
            this.SimulatedButton.IsEnabled = true;
            this.ShowLights();
        }
    }

    /// <summary>Stops the simulated lights, as the button does.</summary>
    /// <returns>A task that ends when they are stopped.</returns>
    internal async Task StopSimulatedAsync()
    {
        try
        {
            var stopped = await this._session.StopSimulatedAsync();
            Fluent.Show(
                this.NoticeBar,
                InfoBarSeverity.Informational,
                stopped == 0 ? "No simulated lights were running" : $"Stopped {stopped} simulated lights",
                "Their connections were closed and the lights were forgotten.");
        }
        catch (Exception exception)
        {
            Fluent.Show(this.NoticeBar, InfoBarSeverity.Error, "The simulated lights could not be stopped", exception.Message);
        }
        finally
        {
            this.ShowLights();
        }
    }

    /// <summary>Probes the address in the box, as the button does.</summary>
    /// <returns>A task that ends when the probe is over.</returns>
    internal async Task ProbeAsync()
    {
        if (this._probing)
        {
            return;
        }

        var text = this.ProbeBox.Text.Trim();
        if (text.Length == 0)
        {
            Fluent.Show(this.NoticeBar, InfoBarSeverity.Warning, "Type the address of a light", "For example 192.168.1.50. The probe sends one search straight to that address.");
            return;
        }

        this._probing = true;
        this.ProbeButton.IsEnabled = false;
        this._session.Settings.ProbeAddress = text;
        Fluent.Show(this.NoticeBar, InfoBarSeverity.Informational, $"Probing {text}\u2026", "One search is sent to that address, again every second, for up to three seconds.");
        try
        {
            var item = await this._session.ProbeAsync(text, CancellationToken.None);
            if (item is null)
            {
                Fluent.Show(
                    this.NoticeBar,
                    InfoBarSeverity.Warning,
                    $"Nothing answered at {text}",
                    "Is there a light at that address, with LAN Control switched on in the Yeelight app (see Help)? Is it on this network, and is the discovery port in the Settings the one that it answers on (1982)?");
            }
            else
            {
                Fluent.Show(this.NoticeBar, InfoBarSeverity.Success, $"Found {item.Title}", $"{item.Subtitle}. The client is connecting to it.");
            }
        }
        catch (Exception exception)
        {
            var outcome = Outcome.Failed("Probe", exception, TimeSpan.Zero);
            Fluent.Show(this.NoticeBar, outcome.Severity == OutcomeSeverity.Error ? InfoBarSeverity.Error : InfoBarSeverity.Warning, outcome.Title, outcome.Message);
        }
        finally
        {
            this._probing = false;
            this.ProbeButton.IsEnabled = true;
            this.ShowLights();
        }
    }

    /// <summary>Searches now, as the button does: a search is sent and the client waits for the answers.</summary>
    /// <param name="time">How long to wait; four seconds when not given.</param>
    /// <returns>A task that ends when the time has passed.</returns>
    internal async Task SearchNowAsync(TimeSpan? time = null)
    {
        if (this._searching)
        {
            return;
        }

        this._searching = true;
        this.SearchButton.IsEnabled = false;
        this.SearchRing.IsActive = true;
        var wait = time ?? SearchTime;
        Fluent.Show(this.NoticeBar, InfoBarSeverity.Informational, "Searching\u2026", $"A search was sent to the multicast group of the lights and as a broadcast, from every network of this computer. Waiting {wait.TotalSeconds:0.#} seconds for the answers.");
        try
        {
            var count = await this._session.DiscoverAsync(wait, CancellationToken.None);
            if (count == 0)
            {
                Fluent.Show(
                    this.NoticeBar,
                    InfoBarSeverity.Warning,
                    "No light answered",
                    "Is LAN Control switched on for the light, in the Yeelight app (see Help)? Are the light and this computer on the same network, without client isolation? If you know its address, use Probe address.");
            }
            else
            {
                Fluent.Show(this.NoticeBar, InfoBarSeverity.Success, "The search is done", count == 1 ? "The client knows 1 light." : $"The client knows {count} lights.");
            }
        }
        catch (Exception exception)
        {
            var outcome = Outcome.Failed("Search", exception, TimeSpan.Zero);
            Fluent.Show(this.NoticeBar, outcome.Severity == OutcomeSeverity.Error ? InfoBarSeverity.Error : InfoBarSeverity.Warning, outcome.Title, outcome.Message);
        }
        finally
        {
            this._searching = false;
            this.SearchButton.IsEnabled = true;
            this.SearchRing.IsActive = false;
            this.ShowLights();
        }
    }

    /// <summary>Forgets the selected light, as the button does.</summary>
    /// <returns>A task that ends when the light is forgotten.</returns>
    internal async Task ForgetSelectedAsync()
    {
        if (this._session.Selected is not { } item)
        {
            return;
        }

        try
        {
            await this._session.ForgetAsync(item);
            Fluent.Show(this.NoticeBar, InfoBarSeverity.Informational, $"Forgot {item.Title}", "The connection was closed and the light was dropped. It comes back when it answers a search or a probe again.");
        }
        catch (Exception exception)
        {
            var outcome = Outcome.Failed("Forget", exception, TimeSpan.Zero);
            Fluent.Show(this.NoticeBar, InfoBarSeverity.Error, outcome.Title, outcome.Message);
        }
    }

    /// <summary>Opens the selected light in the Control page, as the button does.</summary>
    internal void OpenSelected()
    {
        if (this._session.Selected is { } item)
        {
            this._session.Select(item, byUser: true);
            App.MainWindow?.Navigate("control");
        }
    }

    private void Scroller_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // The list and the details need room: below this height the page scrolls instead of squeezing them.
        this.PageGrid.Height = Math.Max(this.Scroller.ActualHeight, MinPageHeight);
    }

    private static bool Same(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        return left.Count == right.Count && left.SequenceEqual(right, StringComparer.Ordinal);
    }

    private void ShowLights()
    {
        var lights = this._session.Lights;
        var connected = lights.Count(light => light.IsConnected);
        this.CountText.Text = lights.Count switch
        {
            0 => "No lights",
            1 => $"1 light, {connected} connected",
            _ => $"{lights.Count} lights, {connected} connected",
        };
        this.EmptyPanel.Visibility = lights.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        this.StopSimulatedButton.IsEnabled = this._session.Simulated.Running.Count > 0;

        // The client may have been replaced: what it does is what the switches show.
        this._syncing = true;
        this.SearchEverySwitch.IsOn = this._session.IsSearching;
        this.ListenSwitch.IsOn = this._session.IsListening;
        this._syncing = false;
    }

    private void OnSelectedChanged()
    {
        var item = this._session.Selected;
        if (!ReferenceEquals(this.LightList.SelectedItem, item))
        {
            this._syncing = true;
            this.LightList.SelectedItem = item;
            this._syncing = false;
        }

        if (this._watched is not null)
        {
            this._watched.Changed -= this.OnItemChanged;
        }

        this._watched = item;
        if (item is not null)
        {
            item.Changed += this.OnItemChanged;
        }

        this.ShowDetails();
    }

    private void OnItemChanged(object? sender, EventArgs e) => this.ShowDetails();

    private void ShowDetails()
    {
        var item = this._session.Selected;
        this.OpenButton.IsEnabled = item is not null;
        this.ForgetButton.IsEnabled = item is not null;
        this.CopyButton.IsEnabled = item is not null;
        if (item is null)
        {
            this.DetailTitle.Text = "Select a light";
            this.DetailSubtitle.Text = "Choose a light in the list to see all its properties and the methods that it lists.";
            this._rows.Clear();
            this.MethodList.ItemsSource = null;
            this.MethodsPanel.Visibility = Visibility.Collapsed;
            return;
        }

        this.DetailTitle.Text = item.Title;
        this.DetailSubtitle.Text = item.Subtitle;
        PropertyRows.Sync(this._rows, LightFormat.Properties(item.Device, DateTimeOffset.Now));
        var methods = item.Device.SupportedMethods;
        this.MethodsPanel.Visibility = Visibility.Visible;
        this.MethodsTitle.Text = methods.Count == 0 ? "The light has not listed its methods yet" : $"Methods that the light lists ({methods.Count})";
        if (this.MethodList.ItemsSource is not IReadOnlyList<string> shown || !Same(shown, methods))
        {
            this.MethodList.ItemsSource = methods;
        }
    }

    private string DescribeSelected()
    {
        var text = new StringBuilder();
        text.AppendLine(this.DetailTitle.Text);
        foreach (var row in this._rows)
        {
            text.AppendLine($"{row.Name}: {row.Value}");
        }

        if (this._session.Selected is { } item)
        {
            text.AppendLine($"Methods: {string.Join(' ', item.Device.SupportedMethods)}");
        }

        return text.ToString();
    }

    private async void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await this.SearchNowAsync();
        }
        catch (Exception exception)
        {
            Fluent.Show(this.NoticeBar, InfoBarSeverity.Error, "The search failed", exception.Message);
        }
    }

    private void SearchEverySwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (this._syncing)
        {
            return;
        }

        try
        {
            if (this.SearchEverySwitch.IsOn)
            {
                this.StartSearching();
            }
            else
            {
                this._session.StopSearching();
                Fluent.Show(this.NoticeBar, InfoBarSeverity.Informational, "Stopped searching", "The lights that were found stay in the list.");
            }
        }
        catch (Exception exception)
        {
            this._syncing = true;
            this.SearchEverySwitch.IsOn = this._session.IsSearching;
            this._syncing = false;
            Fluent.Show(this.NoticeBar, InfoBarSeverity.Warning, "The search could not be started", exception.Message);
        }
    }

    private void StartSearching()
    {
        var seconds = this.IntervalBox.Value;
        if (double.IsNaN(seconds) || seconds < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(seconds), "Type the number of seconds between two searches: 1 or more.");
        }

        this._session.Settings.SearchIntervalSeconds = (int)Math.Min(seconds, 86_400);
        this._session.StartSearching(TimeSpan.FromSeconds(this._session.Settings.SearchIntervalSeconds));
        Fluent.Show(
            this.NoticeBar,
            InfoBarSeverity.Informational,
            $"Searching every {this._session.Settings.SearchIntervalSeconds} seconds",
            "A search is sent now and again every time that the interval is over. Lights that answer are added to the list.");
        this.ShowLights();
    }

    private void IntervalBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!this._syncing && this.SearchEverySwitch.IsOn && !double.IsNaN(args.NewValue) && args.NewValue >= 1)
        {
            try
            {
                this.StartSearching();
            }
            catch (Exception exception)
            {
                Fluent.Show(this.NoticeBar, InfoBarSeverity.Warning, "The interval could not be changed", exception.Message);
            }
        }
    }

    private void ListenSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (this._syncing)
        {
            return;
        }

        try
        {
            if (this.ListenSwitch.IsOn)
            {
                this._session.StartListening();
                Fluent.Show(this.NoticeBar, InfoBarSeverity.Informational, "Listening", "The client listens to what the lights say by themselves, which they do now and then. It can take a while: Search now is quicker.");
            }
            else
            {
                this._session.StopListening();
                Fluent.Show(this.NoticeBar, InfoBarSeverity.Informational, "Stopped listening", "The lights that were found stay in the list.");
            }

            this.ShowLights();
        }
        catch (Exception exception)
        {
            this._syncing = true;
            this.ListenSwitch.IsOn = this._session.IsListening;
            this._syncing = false;
            Fluent.Show(this.NoticeBar, InfoBarSeverity.Warning, "Listening could not be changed", exception.Message);
        }
    }

    private async void ProbeButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await this.ProbeAsync();
        }
        catch (Exception exception)
        {
            Fluent.Show(this.NoticeBar, InfoBarSeverity.Error, "The probe failed", exception.Message);
        }
    }

    private async void ProbeBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            try
            {
                await this.ProbeAsync();
            }
            catch (Exception exception)
            {
                Fluent.Show(this.NoticeBar, InfoBarSeverity.Error, "The probe failed", exception.Message);
            }
        }
    }

    private async void SimulatedItem_Click(object sender, RoutedEventArgs e)
    {
        var tag = (sender as FrameworkElement)?.Tag as string;
        IReadOnlyList<SimulatedKind> kinds = tag switch
        {
            "color" => [SimulatedKind.Color],
            "white" => [SimulatedKind.White],
            "ceiling" => [SimulatedKind.Ceiling],
            _ => [SimulatedKind.Color, SimulatedKind.White, SimulatedKind.Ceiling],
        };
        try
        {
            await this.StartSimulatedAsync(kinds);
        }
        catch (Exception exception)
        {
            Fluent.Show(this.NoticeBar, InfoBarSeverity.Error, "The simulated lights could not be started", exception.Message);
        }
    }

    private async void TrySimulatedButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await this.StartSimulatedAsync([SimulatedKind.Color, SimulatedKind.White, SimulatedKind.Ceiling]);
        }
        catch (Exception exception)
        {
            Fluent.Show(this.NoticeBar, InfoBarSeverity.Error, "The simulated lights could not be started", exception.Message);
        }
    }

    private async void StopSimulatedButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await this.StopSimulatedAsync();
        }
        catch (Exception exception)
        {
            Fluent.Show(this.NoticeBar, InfoBarSeverity.Error, "The simulated lights could not be stopped", exception.Message);
        }
    }

    private void LightList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!this._syncing)
        {
            this._session.Select(this.LightList.SelectedItem as LightItem, byUser: true);
        }
    }

    private void LightList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e) => this.OpenSelected();

    private void LightList_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            this.OpenSelected();
        }
    }

    private void OpenButton_Click(object sender, RoutedEventArgs e) => this.OpenSelected();

    private async void ForgetButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await this.ForgetSelectedAsync();
        }
        catch (Exception exception)
        {
            Fluent.Show(this.NoticeBar, InfoBarSeverity.Error, "The light could not be forgotten", exception.Message);
        }
    }

    private async void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        await ClipboardHelper.CopyAsync(this.CopyButton, this.DescribeSelected());
    }
}
