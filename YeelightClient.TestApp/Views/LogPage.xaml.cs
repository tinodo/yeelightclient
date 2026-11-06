using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Yeelight.TestApp.Controls;
using Yeelight.TestApp.Services;
using Yeelight.TestApp.Session;

namespace Yeelight.TestApp.Views;

/// <summary>Chooses how an entry of the log looks: entries that deserve attention are marked.</summary>
public sealed class LogTemplateSelector : DataTemplateSelector
{
    /// <summary>Gets or sets the template of an ordinary entry.</summary>
    public DataTemplate? Normal { get; set; }

    /// <summary>Gets or sets the template of an entry that deserves attention.</summary>
    public DataTemplate? Problem { get; set; }

    /// <inheritdoc/>
    protected override DataTemplate SelectTemplateCore(object item) =>
        (item is LogEntry { IsProblem: true } ? this.Problem : this.Normal) ?? throw new InvalidOperationException("The templates of the log are missing.");

    /// <inheritdoc/>
    protected override DataTemplate SelectTemplateCore(object item, DependencyObject container) => this.SelectTemplateCore(item);
}

/// <summary>The wire log: everything that the client logs, filtered, followed, copied and exported.</summary>
public sealed partial class LogPage : Page
{
    private const int MaxShown = 2500;

    private readonly AppSession _session = App.Session;
    private readonly DispatcherQueueTimer _timer;
    private string? _lastExport;
    private long _shownThrough;
    private bool _updating;

    /// <summary>Initializes a new instance of the <see cref="LogPage"/> class.</summary>
    public LogPage()
    {
        this.InitializeComponent();
        this.NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;

        this._updating = true;
        this.LevelBox.ItemsSource = new[]
        {
            new Choice<LogLevel>(LogLevel.Trace, "Everything (debug and up)"),
            new Choice<LogLevel>(LogLevel.Information, "Information and up"),
            new Choice<LogLevel>(LogLevel.Warning, "Warnings and up"),
            new Choice<LogLevel>(LogLevel.Error, "Errors only"),
        };
        this.LevelBox.SelectedIndex = 0;
        this._updating = false;
        this.RefreshLightChoices();
        this._session.LightsChanged += (_, _) => this.RefreshLightChoices();
        this._session.ClientReplaced += (_, _) => this.RefreshLightChoices();

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

    /// <summary>Gets the entries that match the filters.</summary>
    internal ObservableCollection<LogEntry> Visible { get; } = [];

    /// <summary>Gets the entries that are shown; for the smoke test.</summary>
    internal IReadOnlyList<LogEntry> Shown => this.Visible;

    /// <summary>Gets the text of the details of the selected entry; for the smoke test.</summary>
    internal string Detail => this.DetailBox.Text;

    /// <summary>Gets the text of the counts; for the smoke test.</summary>
    internal string Count => this.CountText.Text;

    /// <summary>Gets a value indicating whether the explanation of an empty list is shown; for the smoke test.</summary>
    internal bool ShowsEmptyText => this.EmptyText.Visibility == Visibility.Visible;

    /// <summary>Selects the first entry, for the smoke test.</summary>
    internal void SelectFirst()
    {
        if (this.Visible.Count > 0)
        {
            this.EntryList.SelectedIndex = 0;
        }
    }

    /// <summary>Selects the last entry, for the smoke test.</summary>
    internal void SelectLast()
    {
        if (this.Visible.Count > 0)
        {
            this.EntryList.SelectedIndex = this.Visible.Count - 1;
        }
    }

    /// <summary>Reads what is new now, instead of when the timer ticks; for the smoke test.</summary>
    internal void PollNow() => this.Poll();

    /// <summary>Chooses what to show, as a person does; for the smoke test.</summary>
    /// <param name="level">The lowest level.</param>
    /// <param name="lightId">The id of the light, or <c>null</c> for all.</param>
    /// <param name="text">The text.</param>
    /// <param name="sent">Whether the messages that were sent are shown.</param>
    /// <param name="received">Whether the messages that were received are shown.</param>
    /// <param name="client">Whether what the client reported is shown.</param>
    internal void SetFilter(LogLevel level, string? lightId, string text, bool sent = true, bool received = true, bool client = true)
    {
        this._updating = true;
        this.LevelBox.SelectedItem = this.LevelBox.Items.OfType<Choice<LogLevel>>().FirstOrDefault(choice => choice.Value == level) ?? this.LevelBox.Items.FirstOrDefault();
        this.LightBox.SelectedItem = this.LightBox.Items.OfType<Choice<string>>().FirstOrDefault(choice => choice.Value == (lightId ?? string.Empty)) ?? this.LightBox.Items.FirstOrDefault();
        this.FilterBox.Text = text;
        this.SentButton.IsChecked = sent;
        this.ReceivedButton.IsChecked = received;
        this.ClientButton.IsChecked = client;
        this._updating = false;
        this.Rebuild();
    }

    /// <summary>Forgets the log, here and in the session.</summary>
    internal void Reset()
    {
        this._session.Log.Clear();
        this.Visible.Clear();
        this._shownThrough = this._session.Log.Sequence;
        this.ShowDetail();
        this.ShowCount();
    }

    private static string AsLine(LogEntry entry) => $"{entry.TimeText}\t{entry.Direction}\t{entry.LevelText}\t{entry.LightText}\t{entry.Title}\t{entry.ElapsedText}".TrimEnd('\t');

    private LogFilter CurrentFilter()
    {
        return new LogFilter
        {
            MinimumLevel = (this.LevelBox.SelectedItem as Choice<LogLevel>)?.Value ?? LogLevel.Trace,
            LightId = (this.LightBox.SelectedItem as Choice<string>)?.Value ?? string.Empty,
            Text = this.FilterBox.Text ?? string.Empty,
            ShowSent = this.SentButton.IsChecked == true,
            ShowReceived = this.ReceivedButton.IsChecked == true,
            ShowClient = this.ClientButton.IsChecked == true,
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

        var fresh = this._session.Log.Since(this._shownThrough);
        if (fresh.Count > 0)
        {
            this._shownThrough = fresh[^1].Sequence;
            var filter = this.CurrentFilter();
            foreach (var entry in fresh.Where(filter.Matches))
            {
                this.Visible.Add(entry);
            }

            if (this.Visible.Count > MaxShown + 200)
            {
                for (var remove = this.Visible.Count - MaxShown; remove > 0; remove--)
                {
                    this.Visible.RemoveAt(0);
                }
            }

            if (this.FollowButton.IsChecked == true && this.EntryList.SelectedItem is null && this.Visible.Count > 0)
            {
                this.EntryList.ScrollIntoView(this.Visible[^1]);
            }
        }

        // The log lets go of its oldest entries when there are too many: the rows must not keep alive what the log let go of.
        var oldest = this._session.Log.OldestSequence;
        while (this.Visible.Count > 0 && this.Visible[0].Sequence < oldest)
        {
            this.Visible.RemoveAt(0);
        }

        this.ShowCount();
    }

    private void Rebuild()
    {
        var snapshot = this._session.Log.Snapshot();
        this._shownThrough = snapshot.Count > 0 ? snapshot[^1].Sequence : this._session.Log.Sequence;
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
        this.CountText.Text = $"{this.Visible.Count:N0} shown, {this._session.Log.Sequence:N0} in total" + (this.PauseButton.IsChecked == true ? " \u00b7 paused" : string.Empty);
        this.EmptyText.Visibility = this.Visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowDetail()
    {
        if (this.EntryList.SelectedItem is not LogEntry entry)
        {
            this.DetailTitle.Text = "Select an entry";
            this.DetailMeta.Text = string.Empty;
            this.DetailBox.Text = string.Empty;
            return;
        }

        this.DetailTitle.Text = $"{entry.Title} \u00b7 {entry.TimeText}";
        this.DetailMeta.Text = (entry.Light.Length > 0 ? $"Light {entry.Light} \u00b7 " : string.Empty) + entry.Level + (entry.ElapsedText.Length > 0 ? $" \u00b7 answered in {entry.ElapsedText}" : string.Empty);
        this.DetailBox.Text = entry.Kind == LogKind.Client ? entry.Text : JsonTools.Pretty(entry.Text);
    }

    private void Filter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!this._updating)
        {
            this.Rebuild();
        }
    }

    private void Filter_Click(object sender, RoutedEventArgs e) => this.Rebuild();

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

    private void EntryList_SelectionChanged(object sender, SelectionChangedEventArgs e) => this.ShowDetail();

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        this.Export();
    }

    /// <summary>Writes the entries that are shown to a file; for the smoke test.</summary>
    /// <returns>The path of the file, or <c>null</c> when it could not be written.</returns>
    internal string? Export()
    {
        try
        {
            var path = SessionExporter.ExportLog(this.Visible.ToArray());
            this._lastExport = path;
            Fluent.Show(this.ExportBar, InfoBarSeverity.Success, $"{this.Visible.Count:N0} entries were written", path);
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
