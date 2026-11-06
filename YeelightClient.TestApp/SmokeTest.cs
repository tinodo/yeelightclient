using System.Diagnostics;
using System.Text;
using Microsoft.UI.Xaml;
using Yeelight.TestApp.Session;

namespace Yeelight.TestApp;

/// <summary>
/// Started as <c>YeelightClient.TestApp.exe --smoke-test &lt;log&gt;</c>, the app opens every page, drives them the way a person does against
/// simulated lights (in this process, on the loopback: nothing leaves the computer), checks what the simulated lights received and what
/// the pages show, writes what happened to the log, takes a picture of each page next to the log, and ends with exit code 0 (or 1 when
/// something went wrong). CI runs it after every build: the XAML of an app is only read when the app runs, so this is where a mistake in
/// it shows, and the pictures are how the design can be looked at without running the app.
/// </summary>
internal static partial class SmokeTest
{
    // A smoke test that hangs would hold up CI: it ends itself, as a failure, well before CI would kill it (two minutes).
    private static readonly TimeSpan Watchdog = TimeSpan.FromSeconds(100);

    private static readonly StringBuilder Report = new();
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static bool hasProblem;
    private static string? writtenTo;
    private static int finished;

    /// <summary>Determines whether the app was asked to run the smoke test.</summary>
    /// <param name="path">Where to write the log: the argument that follows the option, or <c>smoke-test.log</c> next to the executable.</param>
    /// <returns><c>true</c> when the smoke test was asked for.</returns>
    public static bool IsRequested(out string path)
    {
        var arguments = Environment.GetCommandLineArgs();
        var index = Array.IndexOf(arguments, "--smoke-test");
        path = index >= 0 && index + 1 < arguments.Length && !arguments[index + 1].StartsWith("--", StringComparison.Ordinal)
            ? arguments[index + 1]
            : Path.Combine(AppContext.BaseDirectory, "smoke-test.log");
        return index >= 0;
    }

    /// <summary>Adds a line to the log; a problem makes the smoke test fail.</summary>
    /// <param name="message">The line.</param>
    /// <param name="isProblem">Whether it is a problem.</param>
    public static void Record(string message, bool isProblem = false)
    {
        lock (Report)
        {
            var line = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"[{Clock.Elapsed.TotalSeconds,6:0.0} s] {message}");
            Report.AppendLine(line);
            hasProblem |= isProblem;

            // The log is written when the checks are done: what the app reports while it closes is added to it.
            if (writtenTo is not null)
            {
                try
                {
                    File.AppendAllText(writtenTo, "after the checks: " + line + Environment.NewLine);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // Nothing more can be done about it.
                }
            }
        }
    }

    /// <summary>Records what a check found: ok, or a failure that fails the smoke test.</summary>
    /// <param name="condition">Whether what is checked holds.</param>
    /// <param name="what">What is checked, in words.</param>
    public static void Check(bool condition, string what) => Record(condition ? $"ok: {what}" : $"FAILED: {what}", isProblem: !condition);

    /// <summary>Waits for something that happens on another thread, or on the thread of the user interface after this one is free.</summary>
    /// <param name="condition">What has to become true.</param>
    /// <param name="timeout">How long to wait; the default is five seconds.</param>
    /// <returns><c>true</c> when it became true in time.</returns>
    public static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan? timeout = null)
    {
        var deadline = Stopwatch.StartNew();
        var limit = timeout ?? TimeSpan.FromSeconds(5);
        while (deadline.Elapsed < limit)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(15);
        }

        return condition();
    }

    /// <summary>Takes a picture of the window, next to the log.</summary>
    /// <param name="window">The window.</param>
    /// <param name="folder">The folder of the log.</param>
    /// <param name="name">The name of the picture: <c>smoke-test-name.png</c>.</param>
    /// <returns>A task that ends when the picture is written (or when it could not be).</returns>
    private static async Task CaptureAsync(MainWindow window, string folder, string name)
    {
        var root = window.Content as Microsoft.UI.Xaml.Controls.Panel;
        var original = root?.Background;
        try
        {
            if (window.Content is UIElement content)
            {
                // The picture holds what the app paints, and what the Mica backdrop shows is not painted by the app: without a
                // background the picture would be transparent there. The colour is that of the base layer of the theme that is shown.
                if (root is not null)
                {
                    var dark = (window.Content as FrameworkElement)?.ActualTheme == ElementTheme.Dark;
                    root.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(dark ? Windows.UI.Color.FromArgb(255, 0x20, 0x20, 0x20) : Windows.UI.Color.FromArgb(255, 0xF3, 0xF3, 0xF3));
                }

                await ScreenCapture.SaveAsync(content, Path.Combine(folder, $"smoke-test-{name}.png"));
                Record($"took a picture of {name}");
            }
        }
        catch (Exception exception)
        {
            // A picture is a convenience: its failure is reported, but does not fail the smoke test.
            Record($"no picture of {name}: {exception.GetType().Name}: {exception.Message}");
        }
        finally
        {
            if (root is not null)
            {
                root.Background = original;
            }
        }
    }

    /// <summary>Writes the log, closes everything, and ends the app with the exit code of the smoke test.</summary>
    /// <param name="window">The window.</param>
    /// <param name="path">The log.</param>
    /// <returns>A task that ends when the app is told to exit.</returns>
    private static async Task FinishAsync(MainWindow window, string path)
    {
        if (Interlocked.Exchange(ref finished, 1) != 0)
        {
            return;
        }

        // Everything that the app started is stopped before the process ends: the connections, the music session, the simulated lights.
        try
        {
            await App.Session.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(15));
            Record("the session was shut down: the client is disposed and the simulated lights are stopped");
        }
        catch (Exception exception)
        {
            Record($"shutting down the session failed: {exception}", isProblem: true);
        }

        string text;
        bool failed;
        lock (Report)
        {
            text = Report.ToString();
            failed = hasProblem;
        }

        try
        {
            File.WriteAllText(path, text + (failed ? "FAILED" : "OK") + Environment.NewLine);
            lock (Report)
            {
                writtenTo = path;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            failed = true;
        }
        finally
        {
            Environment.ExitCode = failed ? 1 : 0;
            window.ForceClose();
            Application.Current.Exit();
        }
    }
}
