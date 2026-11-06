using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml.Controls;
using Yeelight.TestApp.Services;

namespace Yeelight.TestApp.Views;

/// <summary>Help and about: the version of the library, the links, how to switch on LAN Control, and what to know about lights.</summary>
public sealed partial class HelpPage : Page
{
    /// <summary>Initializes a new instance of the <see cref="HelpPage"/> class.</summary>
    public HelpPage()
    {
        this.InitializeComponent();
        this.NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;

        var library = VersionInfo.OfLibrary();
        this.LibraryText.Text = library.Build.Length == 0 ? library.Version : $"{library.Version} (build {library.Build})";
        var app = VersionInfo.OfAssembly(Assembly.GetExecutingAssembly());
        this.AppText.Text = app.Build.Length == 0 ? app.Version : $"{app.Version} (build {app.Build})";
        this.RuntimeText.Text = $"{RuntimeInformation.FrameworkDescription} on {RuntimeInformation.OSDescription} ({RuntimeInformation.ProcessArchitecture})";
        this.SettingsText.Text = AppSettings.Persist
            ? Path.Combine(AppSettings.Folder, "settings.json")
            : "not kept (the smoke test does not touch your settings)";
    }

    /// <summary>Gets the version of the library as the page shows it; for the smoke test.</summary>
    internal string LibraryVersion => this.LibraryText.Text;
}
