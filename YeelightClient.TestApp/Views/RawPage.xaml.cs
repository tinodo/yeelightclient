using System.Collections.ObjectModel;
using System.Diagnostics;
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
/// The explorer of the protocol: any method with any parameters, sent to the light, to its background light or over a music mode session,
/// and the answer of the light exactly as the library has it; properties by name; and a table of what the protocol has.
/// </summary>
public sealed partial class RawPage : Page
{
    private const int MaxHistory = 200;

    private readonly AppSession _session = App.Session;
    private LightItem? _item;
    private bool _updating;
    private bool _ready;
    private bool _sending;
    private int _exampleIndex;

    /// <summary>Initializes a new instance of the <see cref="RawPage"/> class.</summary>
    public RawPage()
    {
        this.InitializeComponent();
        this.NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
        this._updating = true;
        this.TargetBox.ItemsSource = new[]
        {
            new Choice<RawTarget>(RawTarget.Device, "The light, as the device sends it"),
            new Choice<RawTarget>(RawTarget.Background, "The background light, sent as typed"),
            new Choice<RawTarget>(RawTarget.Music, "The music mode session (no answer)"),
        };
        this.TargetBox.SelectedIndex = 0;
        this.MethodBox.ItemsSource = ProtocolCatalog.MethodNames;
        this.MethodBox.Text = "get_prop";
        this.ParamsBox.Text = ProtocolCatalog.Find("get_prop")?.Example ?? string.Empty;
        this._updating = false;

        this.FilterReference(string.Empty);
        this.UpdateMethodInfo();
        this.UpdateHistoryCount();
        this.UpdatePropertiesEmpty();
        this._session.SelectedChanged += (_, _) => this.OnSelectedChanged();
        this._session.ClientReplaced += (_, _) => this.OnSelectedChanged();
        this._ready = true;
        this.OnSelectedChanged();
    }

    /// <summary>Gets the lights that the combo box offers.</summary>
    internal ObservableCollection<LightItem> Items => this._session.Lights;

    /// <summary>Gets the commands that were sent, the newest first.</summary>
    internal ObservableCollection<RawHistoryItem> History { get; } = [];

    /// <summary>Gets the values that the last <c>get_prop</c> read.</summary>
    internal ObservableCollection<PropertyRow> PropertyValues { get; } = [];

    /// <summary>Gets the methods that the reference shows, after the filter.</summary>
    internal ObservableCollection<ProtocolMethod> Methods { get; } = [];

    /// <summary>Gets the properties that the reference shows, after the filter.</summary>
    internal ObservableCollection<ProtocolProperty> ReferenceRows { get; } = [];

    /// <summary>Gets how many commands were sent and answered (or failed); for the smoke test.</summary>
    internal int Completed { get; private set; }

    /// <summary>Gets how the last command went; for the smoke test.</summary>
    internal Outcome? LastOutcome { get; private set; }

    /// <summary>Gets the answer that the page shows; for the smoke test.</summary>
    internal string ResultText => this.ResultBox.Text;

    /// <summary>Gets the text of the bar above the answer; for the smoke test.</summary>
    internal string BarText => this.ResultBar.IsOpen ? this.ResultBar.Title + " | " + this.ResultBar.Message : string.Empty;

    /// <summary>Gets the severity of the bar above the answer; for the smoke test.</summary>
    internal InfoBarSeverity BarSeverity => this.ResultBar.Severity;

    /// <summary>Gets the line about the method that is typed; for the smoke test.</summary>
    internal string MethodHelp => this.MethodInfo.Text;

    /// <summary>Gets the text of the status of the properties tab; for the smoke test.</summary>
    internal string PropertiesText => this.PropertiesStatus.Text;

    /// <summary>Gets the light that the page sends to; for the smoke test.</summary>
    internal LightItem? Current => this._item;

    /// <summary>Shows a tab, as a click on it does.</summary>
    /// <param name="tag">The tab: <c>send</c>, <c>properties</c> or <c>reference</c>.</param>
    internal void SelectTab(string tag)
    {
        this.Tabs.SelectedItem = tag switch
        {
            "properties" => this.PropertiesTab,
            "reference" => this.ReferenceTab,
            _ => this.SendTab,
        };
        this.ShowTab();
    }

    /// <summary>Fills in the form, as a person does: the method, the parameters and where to send them.</summary>
    /// <param name="method">The method.</param>
    /// <param name="parameters">The parameters, as a JSON array.</param>
    /// <param name="target">Where to send them.</param>
    internal void Fill(string method, string parameters, RawTarget target = RawTarget.Device)
    {
        this._updating = true;
        this.MethodBox.Text = method;
        this.ParamsBox.Text = parameters;
        this.TargetBox.SelectedItem = ((IEnumerable<Choice<RawTarget>>)this.TargetBox.ItemsSource).First(choice => choice.Value == target);
        this._updating = false;
        this.UpdateMethodInfo();
    }

    /// <summary>Applies a filter to the reference, as a person does by typing.</summary>
    /// <param name="text">The text.</param>
    internal void FilterReference(string text)
    {
        var words = text.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        bool Matches(params string[] fields) => words.All(word => fields.Any(field => field.Contains(word, StringComparison.OrdinalIgnoreCase)));
        var methods = ProtocolCatalog.Methods.Where(method => Matches(method.Name, method.Group, method.Parameters, method.Summary)).ToList();
        var properties = ProtocolCatalog.Properties.Where(property => Matches(property.Name, property.Meaning)).ToList();
        Replace(this.Methods, methods);
        Replace(this.ReferenceRows, properties);
        this.MethodsTitle.Text = $"Methods ({methods.Count} of {ProtocolCatalog.Methods.Count})";
        this.PropertiesTitle.Text = $"Properties ({properties.Count} of {ProtocolCatalog.Properties.Count}): what get_prop and the props notification of the light have";
    }

    /// <summary>Sends the command of the form, as the Send button does.</summary>
    /// <returns>A task that ends when the light has answered (or has not).</returns>
    internal async Task SendAsync()
    {
        if (this._sending)
        {
            return;
        }

        if (this._item is not { } item)
        {
            this.Show(new Outcome(OutcomeSeverity.Warning, "Choose a light first", "There is no light to send to: find one on the Lights page, and select it.", TimeSpan.Zero), null);
            return;
        }

        var method = this.MethodBox.Text.Trim();
        if (method.Length == 0)
        {
            this.Show(new Outcome(OutcomeSeverity.Warning, "Type a method", "A command needs a method, for example set_power. Choose one in the list, or type any.", TimeSpan.Zero), null);
            return;
        }

        if (!JsonTools.TryParseParameters(this.ParamsBox.Text, out var parameters, out var problem))
        {
            this.Show(new Outcome(OutcomeSeverity.Warning, "The parameters cannot be read", problem, TimeSpan.Zero), null);
            return;
        }

        var target = (this.TargetBox.SelectedItem as Choice<RawTarget>)?.Value ?? RawTarget.Device;
        var music = target == RawTarget.Music ? this._session.GetMusic(item.Id) : null;
        if (target == RawTarget.Music && music is null)
        {
            this.Show(new Outcome(OutcomeSeverity.Warning, "No music mode session", "There is no music mode session for this light: start one in the Music mode tab of the Control page, and send again.", TimeSpan.Zero), null);
            return;
        }

        var text = JsonTools.FormatParameters(parameters);
        this._sending = true;
        this.SendButton.IsEnabled = false;
        this.BusyRing.IsActive = true;
        var clock = Stopwatch.StartNew();
        Outcome outcome;
        string answer;
        bool success;
        string summary;
        try
        {
            if (music is not null)
            {
                await music.SendCommandAsync(method, parameters);
                outcome = Outcome.Succeeded(method, "Sent over the music session; the light does not answer these", clock.Elapsed);
                answer = "Sent over the music mode session.\r\nA light does not answer the commands that it gets over that connection, and has no limit on their number.\r\nElapsed:  " + Outcome.FormatElapsed(clock.Elapsed);
                success = true;
                summary = "sent, no answer";
            }
            else
            {
                var light = target == RawTarget.Background ? (YeelightLight)item.Device.Background : item.Device;
                var result = await light.SendCommandAsync(method, parameters);
                outcome = RawFormat.ToOutcome(method, result, clock.Elapsed);
                answer = RawFormat.Details(result, clock.Elapsed);
                success = result.Success;
                summary = RawFormat.Summary(result);
            }
        }
        catch (Exception exception)
        {
            outcome = Outcome.Failed(method, exception, clock.Elapsed);
            answer = outcome.Summary;
            success = false;
            summary = JsonTools.OneLine(outcome.Title, 90);
        }
        finally
        {
            this._sending = false;
            this.SendButton.IsEnabled = true;
            this.BusyRing.IsActive = false;
        }

        this.Show(outcome, answer);
        this.History.Insert(0, new RawHistoryItem(DateTimeOffset.Now, target, method, text, success, summary));
        while (this.History.Count > MaxHistory)
        {
            this.History.RemoveAt(this.History.Count - 1);
        }

        this.UpdateHistoryCount();
    }

    /// <summary>Reads the properties that are named in the box, as the button does.</summary>
    /// <returns>A task that ends when the light has answered (or has not).</returns>
    internal async Task GetPropertiesAsync()
    {
        if (this._item is not { } item)
        {
            this.PropertiesStatus.Text = "Choose a light first.";
            return;
        }

        var names = JsonTools.ParseNames(this.NamesBox.Text);
        if (names.Count == 0)
        {
            this.PropertiesStatus.Text = "Name at least one property, for example power.";
            return;
        }

        this.GetPropertiesButton.IsEnabled = false;
        this.BusyRing.IsActive = true;
        var clock = Stopwatch.StartNew();
        try
        {
            var values = await item.Device.GetPropertiesAsync(names);
            var rows = names
                .Select(name => new PropertyRow(name, values.TryGetValue(name, out var value) ? (value.Length == 0 ? "(empty: the light does not know it)" : value) : "(no value)"))
                .ToList();
            PropertyRows.Sync(this.PropertyValues, rows);
            this.PropertiesStatus.Text = $"{names.Count} properties read in {Outcome.FormatElapsed(clock.Elapsed)}.";
            this.LastOutcome = Outcome.Succeeded("get_prop", $"{names.Count} properties", clock.Elapsed);
        }
        catch (Exception exception)
        {
            this.LastOutcome = Outcome.Failed("get_prop", exception, clock.Elapsed);
            this.PropertiesStatus.Text = this.LastOutcome.Summary;
        }
        finally
        {
            this.GetPropertiesButton.IsEnabled = true;
            this.BusyRing.IsActive = false;
            this.Completed++;
            this.UpdatePropertiesEmpty();
        }
    }

    private static void Replace<T>(ObservableCollection<T> target, IReadOnlyList<T> items)
    {
        if (target.SequenceEqual(items))
        {
            return;
        }

        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }

    private void Show(Outcome outcome, string? answer)
    {
        this.LastOutcome = outcome;
        this.Completed++;
        Fluent.Show(this.ResultBar, outcome);
        if (answer is not null)
        {
            this.ResultBox.Text = answer;
        }
    }

    private void OnSelectedChanged()
    {
        this._item = this._session.Selected;
        this._updating = true;
        this.LightBox.SelectedItem = this._item;
        this._updating = false;
        this.SendButton.IsEnabled = this._item is not null && !this._sending;
        this.GetPropertiesButton.IsEnabled = this._item is not null;
    }

    private void ShowTab()
    {
        var tag = (this.Tabs.SelectedItem as SelectorBarItem)?.Tag as string;
        this.SendView.Visibility = tag is null or "send" ? Visibility.Visible : Visibility.Collapsed;
        this.PropertiesView.Visibility = tag == "properties" ? Visibility.Visible : Visibility.Collapsed;
        this.ReferenceView.Visibility = tag == "reference" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateMethodInfo()
    {
        var name = this.MethodBox.Text.Trim();
        if (name.Length == 0)
        {
            this.MethodInfo.Text = "Choose a method in the list, or type any: the light answers with an error for a method that it does not know.";
        }
        else if (ProtocolCatalog.Find(name) is { } method)
        {
            this.MethodInfo.Text = $"Takes: {method.Parameters}. {method.Summary}{(method.NeedsPower ? " Only while the light is on." : string.Empty)}";
        }
        else
        {
            this.MethodInfo.Text = "This is not a method of the protocol that the app knows: the light most likely answers \"unsupported method\" (error code -1). The Reference tab lists them all.";
        }
    }

    private void UpdateHistoryCount()
    {
        this.HistoryEmpty.Visibility = this.History.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        this.HistoryCount.Text = this.History.Count == 0 ? string.Empty : $"{this.History.Count} commands";
        this.ClearHistoryButton.IsEnabled = this.History.Count > 0;
    }

    private void UpdatePropertiesEmpty()
    {
        this.PropertiesEmpty.Visibility = this.PropertyValues.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void FillExample(bool next)
    {
        var method = ProtocolCatalog.Find(this.MethodBox.Text);
        if (method is null)
        {
            this.Show(new Outcome(OutcomeSeverity.Informational, "No example for that method", "The app has examples for the methods of the protocol: choose one in the list.", TimeSpan.Zero), null);
            return;
        }

        this._exampleIndex = next ? (this._exampleIndex + 1) % method.Examples.Count : 0;
        this._updating = true;
        this.ParamsBox.Text = method.Examples[this._exampleIndex];
        this._updating = false;
    }

    private void LightBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (this._ready && !this._updating)
        {
            this._session.Select(this.LightBox.SelectedItem as LightItem, byUser: true);
        }
    }

    private void TargetBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!this._ready || this._updating || this._item is null)
        {
            return;
        }

        if ((this.TargetBox.SelectedItem as Choice<RawTarget>)?.Value == RawTarget.Music && this._session.GetMusic(this._item.Id) is null)
        {
            this.Show(new Outcome(OutcomeSeverity.Informational, "No music mode session yet", "Start one in the Music mode tab of the Control page: the light then takes commands over it without a limit.", TimeSpan.Zero), null);
        }
    }

    private void Tabs_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (this._ready)
        {
            this.ShowTab();
        }
    }

    private void MethodBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (this._ready && !this._updating && this.MethodBox.SelectedItem is string name)
        {
            this._updating = true;
            this.MethodBox.Text = name;
            this._updating = false;
            this.FillExample(next: false);
            this.UpdateMethodInfo();
        }
    }

    private void MethodBox_TextSubmitted(ComboBox sender, ComboBoxTextSubmittedEventArgs args)
    {
        // The text that was typed is the method, whether or not it is in the list.
        sender.Text = args.Text;
        this.UpdateMethodInfo();
        args.Handled = true;
    }

    private async void ParamsBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
        {
            e.Handled = true;
            await this.SafeSendAsync();
        }
    }

    private async void SendButton_Click(object sender, RoutedEventArgs e) => await this.SafeSendAsync();

    private async Task SafeSendAsync()
    {
        try
        {
            this.UpdateMethodInfo();
            await this.SendAsync();
        }
        catch (Exception exception)
        {
            this.Show(Outcome.Failed("Send", exception, TimeSpan.Zero), null);
        }
    }

    private void FillExampleButton_Click(object sender, RoutedEventArgs e) => this.FillExample(next: false);

    private void NextExampleButton_Click(object sender, RoutedEventArgs e) => this.FillExample(next: true);

    private async void CopyResultButton_Click(object sender, RoutedEventArgs e)
    {
        await ClipboardHelper.CopyAsync(this.CopyResultButton, this.ResultBox.Text);
    }

    private void ClearHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        this.History.Clear();
        this.UpdateHistoryCount();
    }

    private void HistoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (this._updating || this.HistoryList.SelectedItem is not RawHistoryItem entry)
        {
            return;
        }

        this.Fill(entry.Method, entry.Parameters, entry.Target);
        this._updating = true;
        this.HistoryList.SelectedItem = null;
        this._updating = false;
    }

    private async void GetPropertiesButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await this.GetPropertiesAsync();
        }
        catch (Exception exception)
        {
            this.PropertiesStatus.Text = Outcome.Failed("get_prop", exception, TimeSpan.Zero).Summary;
        }
    }

    private void AllNamesButton_Click(object sender, RoutedEventArgs e)
    {
        this.NamesBox.Text = string.Join(' ', ProtocolCatalog.PropertyNames);
    }

    private async void CopyPropertiesButton_Click(object sender, RoutedEventArgs e)
    {
        var text = new StringBuilder();
        foreach (var row in this.PropertyValues)
        {
            text.Append(row.Name).Append(": ").AppendLine(row.Value);
        }

        await ClipboardHelper.CopyAsync(this.CopyPropertiesButton, text.ToString());
    }

    private void ReferenceFilter_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput || args.Reason == AutoSuggestionBoxTextChangeReason.ProgrammaticChange)
        {
            this.FilterReference(sender.Text);
        }
    }

    private void MethodList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ProtocolMethod method)
        {
            this.Fill(method.Name, method.Example);
            this._exampleIndex = 0;
            this.SelectTab("send");
        }
    }
}
