using System.Windows;
using KIT.Core.Services;
using KIT.Data;
using KIT.Infrastructure.Windows;

namespace KIT.App;

public partial class App : System.Windows.Application
{
    private GameTrackingService? _trackingService;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var paths = new LocalDataPaths();
        var preferences = new JsonUserPreferencesRepository(paths);
        var savedPreferences = preferences.LoadAsync().GetAwaiter().GetResult();
        ApplyLanguage(savedPreferences.Language);
        ApplyAppearance(savedPreferences.Theme, savedPreferences.VisualStyle);
        _trackingService = new GameTrackingService(
            new JsonGameConfigurationRepository(paths),
            new JsonKitRepository(paths),
            new JsonRecoveryStateRepository(paths),
            new JsonLinesActivityLog(paths),
            new JsonLinesSessionRepository(paths),
            new PollingGameProcessWatcher(),
            new WindowsProcessResourceMonitor(),
            new WindowsGameProcessInspector(),
            new WindowsSessionActionCoordinator(),
            new SystemClock());

        MainWindow = new MainWindow(_trackingService, paths, preferences, savedPreferences);
        MainWindow.Show();
    }

    public static void ApplyLanguage(string language)
    {
        var app = Current;
        var dictionaries = app.Resources.MergedDictionaries;
        var existing = dictionaries.FirstOrDefault(dictionary =>
            dictionary.Source?.OriginalString.Contains("Resources/Strings.", StringComparison.OrdinalIgnoreCase) is true);
        var replacement = new ResourceDictionary
        {
            Source = new Uri($"Resources/Strings.{(language == "ru" ? "ru" : "en")}.xaml", UriKind.Relative)
        };
        if (existing is null) dictionaries.Insert(0, replacement);
        else dictionaries[dictionaries.IndexOf(existing)] = replacement;
    }

    public static void ApplyAppearance(string theme, string visualStyle)
    {
        var normalizedTheme = theme == "light" ? "Light" : "Dark";
        var normalizedStyle = visualStyle == "aggressive" ? "Aggressive" : "Calm";
        var dictionaries = Current.Resources.MergedDictionaries;
        var existing = dictionaries.FirstOrDefault(dictionary =>
            dictionary.Source?.OriginalString.Contains("Themes/", StringComparison.OrdinalIgnoreCase) is true);
        var replacement = new ResourceDictionary
        {
            Source = new Uri($"Themes/{normalizedTheme}{normalizedStyle}.xaml", UriKind.Relative)
        };
        if (existing is null) dictionaries.Add(replacement);
        else dictionaries[dictionaries.IndexOf(existing)] = replacement;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_trackingService is not null)
        {
            _trackingService.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        base.OnExit(e);
    }
}
