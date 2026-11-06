using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Yeelight.Tests.TestSupport;
using Yeelight.TestApp.Services;
using Yeelight.TestApp.Session;
using Yeelight.TestApp.Views;

namespace Yeelight.TestApp;

/// <summary>What the scenario of the smoke test uses to act like a person (buttons are pressed through their automation peers, as a screen reader does) and to look at the simulated lights.</summary>
internal static partial class SmokeTest
{
    /// <summary>Gets the control of a page, or of a panel, by its name.</summary>
    private static T Find<T>(FrameworkElement scope, string name)
        where T : class
    {
        return scope.FindName(name) as T ?? throw new InvalidOperationException($"{scope.GetType().Name} has no {typeof(T).Name} named {name}.");
    }

    /// <summary>Presses a button the way a screen reader does: it has to be there, and enabled, as it is for a person.</summary>
    private static void Click(FrameworkElement element, string what)
    {
        if (element is Control { IsEnabled: false })
        {
            Check(false, $"the {what} button can be pressed, but it is disabled");
            return;
        }

        var peer = FrameworkElementAutomationPeer.CreatePeerForElement(element);
        if (peer?.GetPattern(PatternInterface.Invoke) is IInvokeProvider invoke)
        {
            invoke.Invoke();
        }
        else if (peer?.GetPattern(PatternInterface.Toggle) is IToggleProvider toggle)
        {
            toggle.Toggle();
        }
        else
        {
            throw new InvalidOperationException($"The {what} cannot be pressed through UI Automation.");
        }
    }

    /// <summary>Turns a switch the way a screen reader does.</summary>
    private static void Flip(ToggleSwitch toggle, string what)
    {
        Check(toggle.IsEnabled, $"the {what} switch is enabled");
        var peer = FrameworkElementAutomationPeer.CreatePeerForElement(toggle);
        (peer?.GetPattern(PatternInterface.Toggle) as IToggleProvider ?? throw new InvalidOperationException($"The {what} cannot be turned through UI Automation.")).Toggle();
    }

    /// <summary>Writes the parameters of a request that the simulated light saw as the JSON array that they were.</summary>
    private static string Args(SimulatedRequest request) => "[" + string.Join(",", request.Parameters.Select(parameter => parameter.GetRawText())) + "]";

    private static string Describe(SimulatedRequest request) => request.Method + " " + Args(request) + (request.OverMusic ? " (music)" : string.Empty);

    /// <summary>Gets a simulated light by its kind.</summary>
    private static SimulatedBulb Bulb(SimulatedKind kind)
    {
        return App.Session.Simulated.Running.First(light => light.Kind == kind).Bulb;
    }

    private static LightItem Item(SimulatedKind kind)
    {
        var bulb = Bulb(kind);
        return App.Session.FindItem(bulb.Id) ?? throw new InvalidOperationException($"The list has no light {bulb.Id}.");
    }

    /// <summary>Waits for a condition and records whether it came true.</summary>
    private static async Task<bool> UntilAsync(Func<bool> condition, string what, double seconds = 5)
    {
        var ok = await WaitUntilAsync(condition, TimeSpan.FromSeconds(seconds));
        Check(ok, what);
        return ok;
    }

    /// <summary>Waits for a condition and records whether it came true, with a description that is made when the wait is over (it can say what the page shows by then).</summary>
    private static async Task<bool> UntilAsync(Func<bool> condition, Func<string> what, double seconds = 5)
    {
        var ok = await WaitUntilAsync(condition, TimeSpan.FromSeconds(seconds));
        Check(ok, what());
        return ok;
    }

    /// <summary>
    /// Waits until the light has told its whole state after the connection was made (<c>get_prop</c>, read when a connection is made): what
    /// the simulated light says right after that can overtake it, which a real light does not do, and a command before that would be
    /// answered with a state that is out of date.
    /// </summary>
    private static async Task WaitForConnectionAsync(LightItem item)
    {
        var id = item.Id;
        var ok = await WaitUntilAsync(
            () => item.IsConnected && App.Session.Log.Snapshot().Any(entry => entry.Kind == LogKind.Received && entry.Method == "get_prop" && string.Equals(entry.Light, id, StringComparison.OrdinalIgnoreCase)),
            TimeSpan.FromSeconds(8));
        Check(ok, $"{item.Title} is connected and has told its state");
        await Task.Delay(80);
    }

    /// <summary>
    /// Does something that sends commands, waits until the simulated light has seen them all, and checks that it saw exactly these (the
    /// method and the parameters, as JSON), in this order, and nothing else.
    /// </summary>
    private static async Task ExpectAsync(SimulatedBulb bulb, string what, Func<Task> act, params string[] expected)
    {
        var before = bulb.Requests.Count;
        await act();
        var arrived = await WaitUntilAsync(() => bulb.Requests.Count - before >= expected.Length, TimeSpan.FromSeconds(6));
        if (arrived)
        {
            // A request that should not have been sent would come soon after the ones that should.
            await Task.Delay(40);
        }

        var got = bulb.Requests.Skip(before).Select(Describe).ToArray();
        Check(arrived && got.SequenceEqual(expected, StringComparer.Ordinal), $"{what}: the light received {string.Join(" then ", expected.Length == 0 ? ["nothing"] : expected)}" + (got.SequenceEqual(expected) ? string.Empty : $" (it received {(got.Length == 0 ? "nothing" : string.Join(" | ", got))})"));
    }

    /// <summary>Does something on the Control page that runs a command, and waits until the page says how it went.</summary>
    private static async Task<Outcome?> RunAsync(ControlPage page, Action trigger)
    {
        var completed = page.Runner.Completed;
        trigger();
        var done = await WaitUntilAsync(() => page.Runner.Completed > completed && !page.Runner.IsBusy, TimeSpan.FromSeconds(10));
        Check(done, "the Control page says how the command went");
        await Task.Delay(60);
        return page.Runner.Last;
    }

    /// <summary>Does something on the Raw page that sends a command, and waits until the page shows the answer.</summary>
    private static async Task<Outcome?> RunAsync(RawPage page, Action trigger)
    {
        var completed = page.Completed;
        trigger();
        var done = await WaitUntilAsync(() => page.Completed > completed, TimeSpan.FromSeconds(10));
        Check(done, "the Raw page shows the answer");
        await Task.Delay(40);
        return page.LastOutcome;
    }

    private static string Json(string text) => JsonSerializer.Serialize(text);
}
