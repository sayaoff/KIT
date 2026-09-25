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
        _trackingService = new GameTrackingService(
            new JsonGameConfigurationRepository(paths),
            new JsonKitRepository(paths),
            new JsonRecoveryStateRepository(paths),
            new JsonLinesActivityLog(paths),
            new PollingGameProcessWatcher(),
            new WindowsGameProcessInspector(),
            new WindowsSessionActionCoordinator(),
            new SystemClock());

        MainWindow = new MainWindow(_trackingService, paths);
        MainWindow.Show();
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
