using System.Windows;
using KIT.Core.Services;
using KIT.Data;
using KIT.Infrastructure.Windows;

namespace KIT.App;

public partial class App : System.Windows.Application
{
    private const string InstanceMutexName = @"Local\KIT.Alpha.Instance";
    private const string ActivationEventName = @"Local\KIT.Alpha.Activate";
    private GameTrackingService? _trackingService;
    private Mutex? _instanceMutex;
    private EventWaitHandle? _activationEvent;
    private RegisteredWaitHandle? _activationRegistration;
    private bool _ownsInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivationEventName);
        _instanceMutex = new Mutex(true, InstanceMutexName, out var isFirstInstance);
        _ownsInstanceMutex = isFirstInstance;
        if (!isFirstInstance)
        {
            _activationEvent.Set();
            Shutdown();
            return;
        }

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

        var mainWindow = new MainWindow(_trackingService, paths, preferences, savedPreferences);
        MainWindow = mainWindow;
        _activationRegistration = ThreadPool.RegisterWaitForSingleObject(
            _activationEvent,
            (_, timedOut) =>
            {
                if (!timedOut) Dispatcher.BeginInvoke(mainWindow.RestoreFromExternalLaunch);
            },
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);
        mainWindow.Show();
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
        _activationRegistration?.Unregister(null);
        _activationEvent?.Dispose();
        if (_trackingService is not null)
        {
            _trackingService.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        if (_ownsInstanceMutex)
        {
            _instanceMutex?.ReleaseMutex();
        }
        _instanceMutex?.Dispose();

        base.OnExit(e);
    }
}
