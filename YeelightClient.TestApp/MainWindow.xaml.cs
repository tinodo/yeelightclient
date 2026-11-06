using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;
using Windows.System;
using WinRT.Interop;
using Yeelight.TestApp.Services;
using Yeelight.TestApp.Views;

namespace Yeelight.TestApp;

/// <summary>The window of the app: a title bar, the navigation, and the page that is shown.</summary>
public sealed partial class MainWindow : Window
{
    private const int PreferredWidth = 1360;
    private const int PreferredHeight = 900;

    private bool _closing;
    private bool _closeAllowed;
    private int _themeStamp;

    /// <summary>Initializes a new instance of the <see cref="MainWindow"/> class.</summary>
    public MainWindow()
    {
        this.InitializeComponent();

        this.ExtendsContentIntoTitleBar = true;
        this.SetTitleBar(this.AppTitleBar);
        this.AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        this.SetIcon();
        this.SetInitialSize();
        this.ApplyTheme();

        App.Session.LightsChanged += (_, _) => this.ShowState();
        App.Session.Ticked += (_, _) => this.ShowState();
        App.Session.ClientReplaced += (_, _) => this.ShowState();
        App.Session.SettingsChanged += (_, _) => this.ApplyTheme();
        this.ShowState();

        // The client closes its connections and the simulated lights stop before the window goes: that takes a moment, which must not block the thread of the window.
        this.AppWindow.Closing += this.OnClosing;

        // A page whose XAML cannot be read must not close the app: the log says why, and so does the smoke test.
        this.NavFrame.Navigated += (_, e) => this.RefreshTheme(e.Content);
        this.NavFrame.NavigationFailed += (_, e) =>
        {
            e.Handled = true;
            var message = $"The page {e.SourcePageType.Name} could not be opened: {e.Exception}";
            SmokeTest.Record(message, isProblem: true);
            App.Session.Logger.Note(Microsoft.Extensions.Logging.LogLevel.Error, message);
        };
    }

    /// <summary>Gets the page that is shown.</summary>
    internal object? CurrentPage => this.NavFrame.Content;

    /// <summary>Opens a page by the tag of its navigation item.</summary>
    /// <param name="tag">The tag: <c>lights</c>, <c>control</c>, <c>raw</c>, <c>events</c>, <c>log</c>, <c>settings</c> or <c>help</c>.</param>
    internal void Navigate(string tag)
    {
        foreach (var item in this.NavView.MenuItems.Concat(this.NavView.FooterMenuItems).OfType<NavigationViewItem>())
        {
            if ((string?)item.Tag == tag)
            {
                this.NavView.SelectedItem = item;
                return;
            }
        }
    }

    /// <summary>Closes the window without the wait of the normal close: for the smoke test, which has closed everything already.</summary>
    internal void ForceClose()
    {
        this._closeAllowed = true;
        this.Close();
    }

    /// <summary>Sets the theme of the window to the one in the settings.</summary>
    internal void ApplyTheme()
    {
        if (this.Content is FrameworkElement root)
        {
            var theme = App.Session.Settings.Theme switch
            {
                AppTheme.Light => ElementTheme.Light,
                AppTheme.Dark => ElementTheme.Dark,
                _ => ElementTheme.Default,
            };
            if (root.RequestedTheme != theme || this._themeStamp == 0)
            {
                root.RequestedTheme = theme;
                this._themeStamp++;
            }
        }
    }

    // A page that is kept in the cache while the theme changes is not told, and keeps the colours of the old theme (an InfoBar, for one):
    // when it is shown again it is made to read its theme resources again.
    private void RefreshTheme(object? content)
    {
        if (content is FrameworkElement page)
        {
            if (page.Tag is int seen && seen != this._themeStamp)
            {
                var original = page.RequestedTheme;
                page.RequestedTheme = page.ActualTheme == ElementTheme.Dark ? ElementTheme.Light : ElementTheme.Dark;
                page.RequestedTheme = original;
            }

            page.Tag = this._themeStamp;
        }
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    private void SetIcon()
    {
        var icon = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (File.Exists(icon))
        {
            this.AppWindow.SetIcon(icon);
        }
    }

    // The size is in pixels: scale it to the screen the window is on, and keep it inside the work area.
    private void SetInitialSize()
    {
        var scale = Math.Max(GetDpiForWindow(WindowNative.GetWindowHandle(this)), 96) / 96.0;
        var area = DisplayArea.GetFromWindowId(this.AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var width = (int)Math.Min(PreferredWidth * scale, area.Width);
        var height = (int)Math.Min(PreferredHeight * scale, area.Height);
        this.AppWindow.Resize(new SizeInt32(width, height));
    }

    private async void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (this._closeAllowed)
        {
            return;
        }

        args.Cancel = true;
        if (this._closing)
        {
            return;
        }

        this._closing = true;
        try
        {
            await Task.WhenAny(App.Session.ShutdownAsync(), Task.Delay(TimeSpan.FromSeconds(8)));
        }
        catch (Exception exception)
        {
            SmokeTest.Record($"Closing the session failed: {exception}", isProblem: true);
        }

        this._closeAllowed = true;
        this.Close();
    }

    private void TitleBar_PaneToggleRequested(TitleBar sender, object args)
    {
        this.NavView.IsPaneOpen = !this.NavView.IsPaneOpen;
    }

    private void Accelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        var tag = sender.Key switch
        {
            VirtualKey.Number1 => "lights",
            VirtualKey.Number2 => "control",
            VirtualKey.Number3 => "raw",
            VirtualKey.Number4 => "events",
            VirtualKey.Number5 => "log",
            VirtualKey.Number6 => "settings",
            VirtualKey.F1 => "help",
            _ => null,
        };
        if (tag is not null)
        {
            this.Navigate(tag);
            args.Handled = true;
        }
    }

    // In a narrow window the pane shows only icons, and the status text that is in it would wrap to a column of single letters.
    private void NavView_DisplayModeChanged(NavigationView sender, NavigationViewDisplayModeChangedEventArgs args)
    {
        this.StatusPanel.Visibility = args.DisplayMode == NavigationViewDisplayMode.Expanded ? Visibility.Visible : Visibility.Collapsed;
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item)
        {
            return;
        }

        var page = (string?)item.Tag switch
        {
            "lights" => typeof(LightsPage),
            "control" => typeof(ControlPage),
            "events" => typeof(EventsPage),
            "log" => typeof(LogPage),
            "settings" => typeof(SettingsPage),
            "help" => typeof(HelpPage),
            "raw" => typeof(RawPage),
            _ => null,
        };

        if (page is not null && this.NavFrame.CurrentSourcePageType != page)
        {
            this.NavFrame.Navigate(page);
        }
    }

    private void ShowState()
    {
        var session = App.Session;
        if (session.IsClosing)
        {
            return;
        }

        var lights = session.Lights;
        var connected = lights.Count(light => light.IsConnected);
        this.StatusText.Text = lights.Count switch
        {
            0 => "No lights yet",
            1 => $"1 light \u00b7 {connected} connected",
            _ => $"{lights.Count} lights \u00b7 {connected} connected",
        };

        var activity = new List<string>();
        if (session.IsSearching)
        {
            activity.Add("Searching");
        }

        if (session.IsListening)
        {
            activity.Add("Listening");
        }

        var simulated = session.Simulated.Running.Count;
        if (simulated > 0)
        {
            activity.Add($"{simulated} simulated");
        }

        this.ActivityText.Text = activity.Count == 0 ? "Nothing is sent to the network until you ask." : string.Join(" \u00b7 ", activity);
    }
}
