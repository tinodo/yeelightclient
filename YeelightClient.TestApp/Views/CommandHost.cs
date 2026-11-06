using System.Diagnostics;
using Microsoft.UI.Xaml.Controls;
using Yeelight.TestApp.Controls;
using Yeelight.TestApp.Services;

namespace Yeelight.TestApp.Views;

/// <summary>What a panel of the Control page needs of the page: the transition that every command takes, and a place to run a command and say how it went.</summary>
internal interface ICommandHost
{
    /// <summary>Gets the transition that a command that takes one is given, or <c>null</c> when the commands are sudden.</summary>
    TimeSpan? Transition { get; }

    /// <summary>Runs a command on the thread of the user interface, without ever blocking it, and shows how it went: how long the light took, or why it did not work. Nothing is thrown.</summary>
    /// <param name="what">What the command does, in words (<c>Set brightness</c>).</param>
    /// <param name="command">The command.</param>
    /// <param name="success">What to say when it worked, or <c>null</c> for the time that the light took.</param>
    /// <returns>How it went.</returns>
    Task<Outcome> RunAsync(string what, Func<CancellationToken, Task> command, string? success = null);

    /// <summary>Says that something that was typed cannot be sent.</summary>
    /// <param name="what">What was tried.</param>
    /// <param name="problem">What is wrong.</param>
    void Reject(string what, string problem);
}

/// <summary>
/// Runs the commands of a page and reports each one in an <see cref="InfoBar"/>: success with the time that the light took, or the
/// exception of the library (kind, error code and message) in words. The smoke test reads how many commands were done and the last outcome.
/// </summary>
internal sealed class CommandRunner(InfoBar bar, ProgressRing? ring = null)
{
    private int _running;

    /// <summary>Gets how many commands were done, whatever came of them.</summary>
    public int Completed { get; private set; }

    /// <summary>Gets how the last command went, or <c>null</c> when there was none.</summary>
    public Outcome? Last { get; private set; }

    /// <summary>Gets a value indicating whether a command is on its way.</summary>
    public bool IsBusy => this._running > 0;

    /// <summary>Runs a command and shows how it went.</summary>
    /// <param name="what">What the command does.</param>
    /// <param name="command">The command.</param>
    /// <param name="success">What to say when it worked, or <c>null</c> for the time that it took.</param>
    /// <returns>How it went.</returns>
    public async Task<Outcome> RunAsync(string what, Func<CancellationToken, Task> command, string? success = null)
    {
        var clock = Stopwatch.StartNew();
        Outcome outcome;
        this.Busy(1);
        try
        {
            await command(CancellationToken.None);
            outcome = success is null ? Outcome.Succeeded(what, clock.Elapsed) : Outcome.Succeeded(what, success, clock.Elapsed);
        }
        catch (Exception exception)
        {
            outcome = Outcome.Failed(what, exception, clock.Elapsed);
        }
        finally
        {
            this.Busy(-1);
        }

        this.Show(outcome);
        return outcome;
    }

    /// <summary>Shows an outcome that did not come from a command.</summary>
    /// <param name="outcome">The outcome.</param>
    public void Show(Outcome outcome)
    {
        this.Last = outcome;
        this.Completed++;
        Fluent.Show(bar, outcome);
    }

    private void Busy(int change)
    {
        this._running += change;
        if (ring is not null)
        {
            ring.IsActive = this._running > 0;
        }
    }
}
