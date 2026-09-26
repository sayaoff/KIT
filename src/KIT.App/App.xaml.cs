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

        MainWindow = new MainWindow(_trackingService, paths, preferences, savedPreferences.Language);
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

    protected override void OnExit(ExitEventArgs e)
    {
        if (_trackingService is not null)
        {
            _trackingService.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        base.OnExit(e);
    }
}
