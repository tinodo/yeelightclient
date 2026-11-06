using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Yeelight.TestApp.Services;
using Yeelight.TestApp.Session;

namespace Yeelight.TestApp;

/// <summary>The test app of Yeelight Client.</summary>
public partial class App : Application
{
    private Window? _window;

    /// <summary>Initializes a new instance of the <see cref="App"/> class.</summary>
    public App()
    {
        this.InitializeComponent();
        this.UnhandledException += this.OnUnhandledException;
    }

    /// <summary>Gets what the pages share: the client, the lights, the log and the events.</summary>
    internal static AppSession Session { get; private set; } = null!;

    /// <summary>Gets the window of the app.</summary>
    internal static MainWindow? MainWindow { get; private set; }

    /// <inheritdoc/>
    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        // Nothing is sent to the network here: the client of the session is made, and does nothing until a person asks it to look for a light.
        var smokeTest = SmokeTest.IsRequested(out var path);
        if (smokeTest)
        {
            // The smoke test must neither read nor write the settings of the person who runs it.
            AppSettings.Persist = false;
        }

        var settings = AppSettings.Load();
        if (smokeTest)
        {
            // The smoke test sends the simulated lights more commands in a minute than a real light takes (the limit of 60 a minute is
            // the subject of the tests of the library, not of this one).
            settings.CommandsPerMinute = 600;
        }

        Session = new AppSession(DispatcherQueue.GetForCurrentThread(), settings);
        MainWindow = new MainWindow();
        this._window = MainWindow;
        this._window.Activate();

        if (smokeTest)
        {
            _ = SmokeTest.RunAsync(MainWindow, path);
        }
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        // A tool for trying things out should not close because one of them went wrong: say so in the log instead.
        e.Handled = true;
        SmokeTest.Record($"Unhandled exception: {e.Exception}", isProblem: true);
        Session?.Logger.Note(LogLevel.Error, $"Unhandled exception: {e.Exception}");
    }
}
