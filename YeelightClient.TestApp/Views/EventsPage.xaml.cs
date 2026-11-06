using System.Collections.ObjectModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Yeelight.TestApp.Controls;
using Yeelight.TestApp.Services;
using Yeelight.TestApp.Session;

namespace Yeelight.TestApp.Views;

/// <summary>Shows the events that the client raises, as it raises them.</summary>
public sealed partial class EventsPage : Page
{
    private const int MaxShown = 3000;

    private readonly AppSession _session = App.Session;
    private readonly DispatcherQueueTimer _timer;
    private long _shownThrough;
    private string? _lastExport;
    private bool _updating;

    /// <summary>Initializes a new instance of the <see cref="EventsPage"/> class.</summary>
    public EventsPage()
    {
        this.InitializeComponent();
        this.NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;

        this._updating = true;
        this.KindBox.ItemsSource = new[]
        {
            new Choice<EventKind?>(null, "All kinds of events"),
            new Choice<EventKind?>(EventKind.DeviceDiscovered, "DeviceDiscovered"),
            new Choice<EventKind?>(EventKind.DeviceStateChanged, "DeviceStateChanged"),
            new Choice<EventKind?>(EventKind.PropertyChanged, "PropertyChanged"),
        };
        this.KindBox.SelectedIndex = 0;
        this._updating = false;
        this.RefreshLightChoices();
        this._session.LightsChanged += (_, _) => this.RefreshLightChoices();
        this._session.ClientReplaced += (_, _) => this.RefreshLightChoices();

        // The events are recorded from every thread, whether this page is open or not: it reads what is new a few times a second, and
        // when it is opened for the first time it shows what happened before.
        this._timer = this.DispatcherQueue.CreateTimer();
        this._timer.Interval = TimeSpan.FromMilliseconds(200);
        this._timer.IsRepeating = true;
        this._timer.Tick += (_, _) => this.Poll();
        this.Loaded += (_, _) =>
        {
            this._timer.Start();
            this.Poll();
        };
        this.Unloaded += (_, _) => this._timer.Stop();
        this.Rebuild();
    }

    /// <summary>Gets the events that match the filters.</summary>
    internal ObservableCollection<EventEntry> Visible { get; } = [];

    /// <summary>Gets the events that are shown; for the smoke test.</summary>
    internal IReadOnlyList<EventEntry> Shown => this.Visible;

    /// <summary>Gets the text of the details of the selected event; for the smoke test.</summary>
    internal string Detail => this.DetailBox.Text;

    /// <summary>Gets the text of the counts; for the smoke test.</summary>
    internal string Count => this.CountText.Text;

    /// <summary>Gets a value indicating whether the explanation of an empty list is shown; for the smoke test.</summary>
    internal bool ShowsEmptyText => this.EmptyText.Visibility == Visibility.Visible;

    /// <summary>Selects the first event, for the smoke test.</summary>
    internal void SelectFirst()
    {
        if (this.Visible.Count > 0)
        {
            this.EventList.SelectedIndex = 0;
        }
    }

    /// <summary>Reads what is new now, instead of when the timer ticks; for the smoke test.</summary>
    internal void PollNow() => this.Poll();

    /// <summary>Chooses the light and the kind of event to show, and the text to look for, as a person does; for the smoke test.</summary>
    /// <param name="lightId">The id of the light, or <c>null</c> for all.</param>
    /// <param name="kind">The kind of event, or <c>null</c> for all.</param>
    /// <param name="text">The text.</param>
    internal void SetFilter(string? lightId, EventKind? kind, string text)
    {
        this._updating = true;
        this.LightBox.SelectedItem = this.LightBox.Items.OfType<Choice<string>>().FirstOrDefault(choice => choice.Value == (lightId ?? string.Empty)) ?? this.LightBox.Items.FirstOrDefault();
        this.KindBox.SelectedItem = this.KindBox.Items.OfType<Choice<EventKind?>>().FirstOrDefault(choice => choice.Value == kind) ?? this.KindBox.Items.FirstOrDefault();
        this.FilterBox.Text = text;
        this._updating = false;
        this.Rebuild();
    }

    /// <summary>Forgets the events that were recorded, here and in the session.</summary>
    internal void Reset()
    {
        this._session.Events.Clear();
        this.Visible.Clear();
        this._shownThrough = this._session.Events.Sequence;
        this.ShowDetail();
        this.ShowCount();
    }

    private static string AsLine(EventEntry entry) => $"{entry.TimeText}\t{entry.KindText}\t{entry.LightText}\t{entry.Summary}";

    private EventFilter CurrentFilter()
    {
        return new EventFilter
        {
            LightId = (this.LightBox.SelectedItem as Choice<string>)?.Value ?? string.Empty,
            Kind = (this.KindBox.SelectedItem as Choice<EventKind?>)?.Value,
            Text = this.FilterBox.Text ?? string.Empty,
        };
    }

    private void RefreshLightChoices()
    {
        var previous = (this.LightBox.SelectedItem as Choice<string>)?.Value ?? string.Empty;
        var choices = new List<Choice<string>> { new(string.Empty, "All lights") };
        choices.AddRange(this._session.Lights.Select(light => new Choice<string>(light.Id, $"{light.Title} \u00b7 {LogEntry.ShortId(light.Id)}")));
        this._updating = true;
        this.LightBox.ItemsSource = choices;
        this.LightBox.SelectedItem = choices.FirstOrDefault(choice => choice.Value == previous) ?? choices[0];
        this._updating = false;
    }

    private void Poll()
    {
        if (this.PauseButton.IsChecked == true)
        {
            this.ShowCount();
            return;
        }

        var fresh = this._session.Events.Since(this._shownThrough);
        if (fresh.Count > 0)
        {
            this._shownThrough = fresh[^1].Sequence;
            var filter = this.CurrentFilter();
            foreach (var entry in fresh.Where(filter.Matches))
            {
                this.Visible.Add(entry);
            }

            // Down to what is allowed in one go, so that the cost of removing from the front is not paid for every event.
            if (this.Visible.Count > MaxShown + 200)
            {
                for (var remove = this.Visible.Count - MaxShown; remove > 0; remove--)
                {
                    this.Visible.RemoveAt(0);
                }
            }

            if (this.FollowButton.IsChecked == true && this.EventList.SelectedItem is null && this.Visible.Count > 0)
            {
                this.EventList.ScrollIntoView(this.Visible[^1]);
            }
        }

        this.ShowCount();
    }

    private void Rebuild()
    {
        var snapshot = this._session.Events.Snapshot();
        this._shownThrough = snapshot.Count > 0 ? snapshot[^1].Sequence : this._session.Events.Sequence;
        var filter = this.CurrentFilter();
        this.Visible.Clear();
        foreach (var entry in snapshot.Where(filter.Matches).TakeLast(MaxShown))
        {
            this.Visible.Add(entry);
        }

        this.ShowCount();
    }

    private void ShowCount()
    {
        this.CountText.Text = $"{this.Visible.Count:N0} shown, {this._session.Events.Sequence:N0} received" + (this.PauseButton.IsChecked == true ? " \u00b7 paused" : string.Empty);
        this.EmptyText.Visibility = this.Visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowDetail()
    {
        if (this.EventList.SelectedItem is not EventEntry entry)
        {
            this.DetailTitle.Text = "Select an event";
            this.DetailBox.Text = string.Empty;
            return;
        }

        this.DetailTitle.Text = $"{entry.KindText} \u00b7 {entry.TimeText}";
        this.DetailBox.Text = entry.ToJson();
    }

    private void Filter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!this._updating)
        {
            this.Rebuild();
        }
    }

    private void FilterBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput && !this._updating)
        {
            this.Rebuild();
        }
    }

    private void PauseButton_Click(object sender, RoutedEventArgs e)
    {
        if (this.PauseButton.IsChecked != true)
        {
            this.Rebuild();
        }
        else
        {
            this.ShowCount();
        }
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e) => this.Reset();

    private void EventList_SelectionChanged(object sender, SelectionChangedEventArgs e) => this.ShowDetail();

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        this.Export();
    }

    /// <summary>Writes the events that are shown to a file; for the smoke test.</summary>
    /// <returns>The path of the file, or <c>null</c> when it could not be written.</returns>
    internal string? Export()
    {
        try
        {
            var path = SessionExporter.ExportEvents(this.Visible.ToArray());
            this._lastExport = path;
            Fluent.Show(this.ExportBar, InfoBarSeverity.Success, $"{this.Visible.Count:N0} events were written", path);
            return path;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            this._lastExport = null;
            Fluent.Show(this.ExportBar, InfoBarSeverity.Error, "The export failed", exception.Message);
            return null;
        }
    }

    private void ShowInFolderButton_Click(object sender, RoutedEventArgs e) => SessionExporter.ShowInFolder(this._lastExport);

    private async void CopyAllButton_Click(object sender, RoutedEventArgs e)
    {
        await ClipboardHelper.CopyAsync(this.CopyAllButton, string.Join(Environment.NewLine, this.Visible.Select(AsLine)));
    }

    private async void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        await ClipboardHelper.CopyAsync(this.CopyButton, this.DetailBox.Text);
    }
}
