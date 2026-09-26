using System.Windows;
using System.Windows.Threading;
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

        var paths = new LocalDataPaths();
        DispatcherUnhandledException += (_, args) =>
            StartupDiagnostics.Write(paths, "Unhandled UI error: " + args.Exception);
        _activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivationEventName);
        _instanceMutex = new Mutex(true, InstanceMutexName, out var isFirstInstance);
        _ownsInstanceMutex = isFirstInstance;
        if (!isFirstInstance)
        {
            StartupDiagnostics.Write(paths, "Secondary launch requested window activation.");
            _activationEvent.Set();
            Shutdown();
            return;
        }

        StartupDiagnostics.Write(paths, "Primary instance acquired.");
        try
        {
            var preferences = new JsonUserPreferencesRepository(paths);
            // preferences.json is tiny. Reading it synchronously here avoids blocking
            // the WPF dispatcher on an async continuation before a window exists.
            var savedPreferences = preferences.Load();
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

            // Show a real, populated first frame before reading history, recovery state,
            // or starting process monitoring. A slow disk must never turn KIT into an
            // unexplained background-only process.
            mainWindow.Show();
            mainWindow.Activate();
            StartupDiagnostics.Write(paths, "Main window shown.");
            Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, mainWindow.BeginInitialization);
        }
        catch (Exception exception)
        {
            StartupDiagnostics.Write(paths, "Fatal startup error: " + exception);
            System.Windows.MessageBox.Show($"KIT could not open.\n\n{exception.Message}\n\nLog: {paths.StartupLogFile}",
                "KIT", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
        }
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
            Task.Run(async () => await _trackingService.DisposeAsync()).GetAwaiter().GetResult();
        }

        if (_ownsInstanceMutex)
        {
            _instanceMutex?.ReleaseMutex();
        }
        _instanceMutex?.Dispose();

        base.OnExit(e);
    }
}
