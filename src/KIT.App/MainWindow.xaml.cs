using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using KIT.Core.Models;
using KIT.Core.Services;
using KIT.Data;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;
using WpfColor = System.Windows.Media.Color;
using WpfMessageBox = System.Windows.MessageBox;
using WpfOpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace KIT.App;

public partial class MainWindow : Window
{
    private readonly GameTrackingService _trackingService;
    private readonly LocalDataPaths _dataPaths;
    private readonly JsonUserPreferencesRepository _preferencesRepository;
    private readonly Forms.NotifyIcon _trayIcon;
    private readonly List<ApplicationTarget> _cleanApps = [];
    private readonly List<ApplicationTarget> _launchApps = [];
    private readonly List<ActivityEvent> _activity = [];
    private readonly List<SessionRecord> _sessions = [];
    private bool _reloadingCatalog;
    private bool _allowClose;
    private string _currentLanguage;
    private string _currentTheme;
    private string _currentVisualStyle;
    private bool _initializingAppearance;
    private bool _startupComplete;
    private TrackingState? _previousTrackingState;
    private TrackingStatus? _lastStatus;
    private ResourceSample? _lastResourceSample;

    public MainWindow(GameTrackingService trackingService, LocalDataPaths dataPaths,
        JsonUserPreferencesRepository preferencesRepository, UserPreferences preferences)
    {
        InitializeComponent();
        _trackingService = trackingService;
        _dataPaths = dataPaths;
        _preferencesRepository = preferencesRepository;
        _currentLanguage = preferences.Language == "ru" ? "ru" : "en";
        _currentTheme = preferences.Theme == "light" ? "light" : "dark";
        _currentVisualStyle = preferences.VisualStyle == "aggressive" ? "aggressive" : "calm";
        _trackingService.StatusChanged += TrackingService_StatusChanged;
        _trackingService.ActivityRecorded += TrackingService_ActivityRecorded;
        _trackingService.CatalogChanged += TrackingService_CatalogChanged;
        _trackingService.ResourceSampled += TrackingService_ResourceSampled;
        _trackingService.SessionSaved += TrackingService_SessionSaved;
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
        _trayIcon = new Forms.NotifyIcon
        {
            Icon = Drawing.SystemIcons.Application,
            Text = "KIT Alpha",
            Visible = true,
            ContextMenuStrip = BuildTrayMenu()
        };
        _trayIcon.DoubleClick += (_, _) => RestoreWindow();
        Closed += (_, _) => _trayIcon.Dispose();

        UpdateLanguageButton();
        _initializingAppearance = true;
        (_currentVisualStyle == "aggressive" ? AggressiveStyleChoice : CalmStyleChoice).IsChecked = true;
        (_currentTheme == "light" ? LightThemeChoice : DarkThemeChoice).IsChecked = true;
        _initializingAppearance = false;

        // Checked fires while InitializeComponent is still constructing the visual tree,
        // so select and render Home explicitly before the first frame is shown.
        MainTabs.SelectedIndex = 0;
        PageTitle.Text = S("HomeTab");
        ShowConfiguredPath();
        ReloadCatalog(_trackingService.Catalog.ActiveKitId);
        RenderActivity();
        RenderSessions();
    }

    private Forms.ContextMenuStrip BuildTrayMenu()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(S("TrayOpen"), null, (_, _) => RestoreWindow());
        menu.Items.Add(S("TrayExit"), null, (_, _) => Dispatcher.Invoke(RequestExit));
        return menu;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await _trackingService.StartAsync();
            ShowConfiguredPath();
            ReloadCatalog(_trackingService.Catalog.ActiveKitId);
            _activity.AddRange(await _trackingService.ReadRecentActivityAsync());
            RenderActivity();
            _sessions.AddRange(await _trackingService.ReadRecentSessionsAsync());
            RenderSessions();
        }
        catch (Exception exception) { ShowError(S("ErrorStart"), exception); }
        finally { _startupComplete = true; }
    }

    private async void SelectExecutable_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new WpfOpenFileDialog
        {
            Title = S("LocateCs2"), Filter = "Counter-Strike 2 (cs2.exe)|cs2.exe",
            CheckFileExists = true, Multiselect = false
        };
        if (_trackingService.Configuration is { } configuration)
            dialog.InitialDirectory = Path.GetDirectoryName(configuration.ExecutablePath);
        if (dialog.ShowDialog(this) is not true) return;

        await RunUiActionAsync(async () =>
        {
            await _trackingService.ConfigureAsync(dialog.FileName);
            ShowConfiguredPath();
        }, S("ErrorExecutable"));
    }

    private void KitSelector_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_reloadingCatalog || KitSelector.SelectedItem is not KitDefinition kit) return;
        LoadKitEditor(kit);
    }

    private async void NewKit_Click(object sender, RoutedEventArgs e)
    {
        var baseName = S("NewKit");
        var name = baseName;
        var suffix = 2;
        while (_trackingService.Catalog.Kits.Any(kit => string.Equals(kit.Name, name, StringComparison.OrdinalIgnoreCase)))
            name = $"{baseName} {suffix++}";
        await RunUiActionAsync(async () =>
        {
            var kit = await _trackingService.CreateKitAsync(name);
            ReloadCatalog(kit.Id);
            KitNameText.Focus();
            KitNameText.SelectAll();
        }, S("ErrorCreate"));
    }

    private async void SaveKit_Click(object sender, RoutedEventArgs e)
    {
        if (KitSelector.SelectedItem is not KitDefinition selected) return;
        var updated = selected with
        {
            Name = KitNameText.Text,
            CleanModeApps = [.. _cleanApps],
            LaunchApps = [.. _launchApps]
        };
        await RunUiActionAsync(async () =>
        {
            await _trackingService.SaveKitAsync(updated);
            ReloadCatalog(updated.Id);
        }, S("ErrorSave"));
    }

    private async void ActivateKit_Click(object sender, RoutedEventArgs e)
    {
        if (KitSelector.SelectedItem is not KitDefinition selected) return;
        await RunUiActionAsync(async () =>
        {
            await _trackingService.SetActiveKitAsync(selected.Id);
            ReloadCatalog(selected.Id);
        }, S("ErrorActivate"));
    }

    private async void DeleteKit_Click(object sender, RoutedEventArgs e)
    {
        if (KitSelector.SelectedItem is not KitDefinition selected || selected.IsVanilla) return;
        if (WpfMessageBox.Show(this, string.Format(S("DeletePrompt"), selected.Name), "KIT", MessageBoxButton.YesNo,
                MessageBoxImage.Question) is not MessageBoxResult.Yes) return;
        await RunUiActionAsync(async () =>
        {
            await _trackingService.DeleteKitAsync(selected.Id);
            ReloadCatalog(_trackingService.Catalog.ActiveKitId);
        }, S("ErrorDelete"));
    }

    private void AddCleanApp_Click(object sender, RoutedEventArgs e) => AddApplication(_cleanApps, CleanAppsList, isCleanMode: true);
    private void AddLaunchApp_Click(object sender, RoutedEventArgs e) => AddApplication(_launchApps, LaunchAppsList, isCleanMode: false);
    private void RemoveCleanApp_Click(object sender, RoutedEventArgs e) => RemoveApplication(_cleanApps, CleanAppsList);
    private void RemoveLaunchApp_Click(object sender, RoutedEventArgs e) => RemoveApplication(_launchApps, LaunchAppsList);

    private void AddApplication(List<ApplicationTarget> targets, System.Windows.Controls.ListBox listBox, bool isCleanMode)
    {
        var dialog = new WpfOpenFileDialog
        {
            Title = S("SelectApplication"), Filter = "Windows applications (*.exe)|*.exe",
            CheckFileExists = true, Multiselect = false
        };
        if (dialog.ShowDialog(this) is not true) return;
        if (_trackingService.Configuration is { } configuration &&
            string.Equals(Path.GetFullPath(dialog.FileName), configuration.ExecutablePath, StringComparison.OrdinalIgnoreCase))
        {
            WpfMessageBox.Show(this, S("Cs2ActionError"), "KIT",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (isCleanMode && WpfMessageBox.Show(this, S("CleanConfirm"), S("CleanConfirmTitle"),
                MessageBoxButton.YesNo, MessageBoxImage.Warning) is not MessageBoxResult.Yes) return;
        if (targets.Any(target => string.Equals(target.ExecutablePath, dialog.FileName, StringComparison.OrdinalIgnoreCase))) return;
        targets.Add(new ApplicationTarget(Path.GetFullPath(dialog.FileName), Path.GetFileNameWithoutExtension(dialog.FileName)));
        RefreshTargetList(listBox, targets);
    }

    private static void RemoveApplication(List<ApplicationTarget> targets, System.Windows.Controls.ListBox listBox)
    {
        if (listBox.SelectedItem is not ApplicationTarget selected) return;
        targets.Remove(selected);
        RefreshTargetList(listBox, targets);
    }

    private static void RefreshTargetList(System.Windows.Controls.ListBox listBox, List<ApplicationTarget> targets)
    {
        listBox.ItemsSource = null;
        listBox.ItemsSource = targets;
    }

    private void LoadKitEditor(KitDefinition kit)
    {
        KitNameText.Text = kit.Name;
        _cleanApps.Clear();
        _cleanApps.AddRange(kit.CleanModeApps);
        _launchApps.Clear();
        _launchApps.AddRange(kit.LaunchApps);
        RefreshTargetList(CleanAppsList, _cleanApps);
        RefreshTargetList(LaunchAppsList, _launchApps);
        var editable = !kit.IsVanilla && !_trackingService.IsGameRunning;
        KitNameText.IsEnabled = editable;
        SaveKitButton.IsEnabled = editable;
        DeleteKitButton.IsEnabled = editable;
        AddCleanButton.IsEnabled = editable;
        RemoveCleanButton.IsEnabled = editable;
        AddLaunchButton.IsEnabled = editable;
        RemoveLaunchButton.IsEnabled = editable;
        ActivateKitButton.IsEnabled = !_trackingService.IsGameRunning && kit.Id != _trackingService.Catalog.ActiveKitId;
    }

    private void ReloadCatalog(Guid selectedKitId)
    {
        _reloadingCatalog = true;
        KitSelector.ItemsSource = null;
        KitSelector.ItemsSource = _trackingService.Catalog.Kits;
        KitSelector.SelectedItem = _trackingService.Catalog.Kits.FirstOrDefault(kit => kit.Id == selectedKitId)
                                   ?? _trackingService.ActiveKit;
        _reloadingCatalog = false;
        if (KitSelector.SelectedItem is KitDefinition selected) LoadKitEditor(selected);
        RenderDashboard();
    }

    private void TrackingService_CatalogChanged(object? sender, KitCatalog catalog) =>
        Dispatcher.Invoke(() => ReloadCatalog(KitSelector.SelectedItem is KitDefinition selected ? selected.Id : catalog.ActiveKitId));

    private void TrackingService_StatusChanged(object? sender, TrackingStatus status) => Dispatcher.Invoke(() =>
    {
        var minimizeToTray = _startupComplete && status.State is TrackingState.GameRunning &&
                             _previousTrackingState is not TrackingState.GameRunning;
        var restoreFromTray = status.State is TrackingState.Watching &&
                              _previousTrackingState is TrackingState.GameRunning;
        _previousTrackingState = status.State;
        _lastStatus = status;
        StatusText.Text = FormatStatus(status);
        HomeGameStateText.Text = FormatStatus(status);
        StatusDot.Fill = status.State switch
        {
            TrackingState.GameRunning => new SolidColorBrush(WpfColor.FromRgb(126, 168, 141)),
            TrackingState.Watching => new SolidColorBrush(WpfColor.FromRgb(126, 145, 168)),
            _ => new SolidColorBrush(WpfColor.FromRgb(140, 144, 151))
        };
        if (KitSelector.SelectedItem is KitDefinition selected) LoadKitEditor(selected);
        if (status.State is not TrackingState.GameRunning)
        {
            _lastResourceSample = null;
            LiveMetricsText.Text = S("MonitoringIdle");
        }
        if (minimizeToTray && IsVisible) HideToTray();
        else if (restoreFromTray) RestoreWindow();
    });

    private void TrackingService_ActivityRecorded(object? sender, ActivityEvent activity) => Dispatcher.Invoke(() =>
    {
        _activity.Add(activity);
        RenderActivity();
    });

    private void TrackingService_ResourceSampled(object? sender, ResourceSample sample) => Dispatcher.Invoke(() =>
    {
        _lastResourceSample = sample;
        LiveMetricsText.Text = string.Format(S("LiveMetricsFormat"), sample.CpuPercent,
            sample.WorkingSetBytes / 1024d / 1024d);
    });

    private void TrackingService_SessionSaved(object? sender, SessionRecord session) => Dispatcher.Invoke(() =>
    {
        _sessions.Add(session);
        RenderSessions();
    });

    private void ShowConfiguredPath() =>
        ExecutablePathText.Text = _trackingService.Configuration?.ExecutablePath ?? S("NoExecutable");

    private void RenderActivity()
    {
        RecentActivityList.ItemsSource = _activity.Count == 0
            ? [new ActivityRow(S("NoActivity"), "", "")]
            : _activity.TakeLast(5).Reverse().Select(activity => new ActivityRow(
                S("Activity" + activity.Kind),
                FormatActivityDetail(activity),
                activity.OccurredAtUtc.ToLocalTime().ToString("HH:mm"))).ToList();
    }

    private void RenderSessions()
    {
        SessionsGrid.ItemsSource = null;
        SessionsGrid.ItemsSource = _sessions.OrderByDescending(session => session.StartedAtUtc)
            .Select(session => new SessionRow(
                session.StartedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                session.KitName,
                FormatDuration(session.Duration),
                session.Metrics.SampleCount == 0 ? "—" : $"{session.Metrics.AverageCpuPercent:F1}%",
                session.Metrics.SampleCount == 0 ? "—" : $"{session.Metrics.PeakCpuPercent:F1}%",
                session.Metrics.SampleCount == 0 ? "—" : $"{session.Metrics.AverageWorkingSetBytes / 1024d / 1024d:F0} {S("MegabytesUnit")}",
                session.Metrics.SampleCount == 0 ? "—" : $"{session.Metrics.PeakWorkingSetBytes / 1024d / 1024d:F0} {S("MegabytesUnit")}"))
            .ToList();
        RenderDashboard();
    }

    private void RenderDashboard()
    {
        var kit = _trackingService.ActiveKit;
        HomeActiveKitNameText.Text = kit.Name;
        HomeKitSummaryText.Text = string.Format(S("KitActionsSummary"),
            kit.CleanModeApps.Count, kit.LaunchApps.Count);
        HomeGameStateText.Text = _lastStatus is null ? S("StatusStarting") : FormatStatus(_lastStatus);

        var last = _sessions.OrderByDescending(session => session.EndedAtUtc).FirstOrDefault();
        if (last is null)
        {
            LastSessionDurationText.Text = "—";
            LastSessionDateText.Text = S("NoSessionsYet");
            DashboardCpuText.Text = "—";
            DashboardCpuPeakText.Text = "";
            DashboardRamText.Text = "—";
            DashboardRamPeakText.Text = "";
            return;
        }

        LastSessionDurationText.Text = FormatDuration(last.Duration);
        LastSessionDateText.Text = last.StartedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        if (last.Metrics.SampleCount == 0)
        {
            DashboardCpuText.Text = "—";
            DashboardCpuPeakText.Text = "";
            DashboardRamText.Text = "—";
            DashboardRamPeakText.Text = "";
            return;
        }

        DashboardCpuText.Text = $"{last.Metrics.AverageCpuPercent:F1}%";
        DashboardCpuPeakText.Text = string.Format(S("PeakValueFormat"), $"{last.Metrics.PeakCpuPercent:F1}%");
        DashboardRamText.Text = $"{last.Metrics.AverageWorkingSetBytes / 1024d / 1024d:F0} {S("MegabytesUnit")}";
        DashboardRamPeakText.Text = string.Format(S("PeakValueFormat"),
            $"{last.Metrics.PeakWorkingSetBytes / 1024d / 1024d:F0} {S("MegabytesUnit")}");
    }

    private string FormatActivityDetail(ActivityEvent activity)
    {
        if (activity.Kind is ActivityEventKind.ActionWarning && !string.IsNullOrWhiteSpace(activity.Details))
            return activity.Details;
        var kit = activity.KitName ?? _trackingService.ActiveKit.Name;
        return activity.Duration is null ? kit : $"{kit} · {FormatDuration(activity.Duration.Value)}";
    }

    private static string FormatDuration(TimeSpan duration) =>
        duration.TotalHours >= 1 ? duration.ToString(@"h\:mm\:ss") : duration.ToString(@"m\:ss");

    private string FormatStatus(TrackingStatus status) => status.State switch
    {
        TrackingState.NotConfigured => S("StatusNotConfigured"),
        TrackingState.Watching => string.Format(S("StatusWatching"), _trackingService.ActiveKit.Name),
        TrackingState.GameRunning => string.Format(S("StatusRunning"), _trackingService.ActiveKit.Name),
        TrackingState.Stopped => S("StatusStopped"),
        _ => status.Message
    };

    private void Navigation_Checked(object sender, RoutedEventArgs e)
    {
        if (MainTabs is null || PageTitle is null || sender is not System.Windows.Controls.RadioButton item) return;
        if (!int.TryParse(item.Tag?.ToString(), out var index)) return;
        MainTabs.SelectedIndex = index;
        PageTitle.Text = index switch
        {
            1 => S("KitsTab"),
            2 => S("SessionsTab"),
            3 => S("SettingsTab"),
            _ => S("HomeTab")
        };
    }

    private async void LanguageToggle_Click(object sender, RoutedEventArgs e)
    {
        _currentLanguage = _currentLanguage == "ru" ? "en" : "ru";
        App.ApplyLanguage(_currentLanguage);
        await SavePreferencesAsync();
        UpdateLanguageButton();
        if (HomeNavigation.IsChecked is true) PageTitle.Text = S("HomeTab");
        else if (KitsNavigation.IsChecked is true) PageTitle.Text = S("KitsTab");
        else if (SessionsNavigation.IsChecked is true) PageTitle.Text = S("SessionsTab");
        else PageTitle.Text = S("SettingsTab");
        ShowConfiguredPath();
        if (_lastStatus is not null) StatusText.Text = FormatStatus(_lastStatus);
        RenderActivity();
        RenderSessions();
        RenderDashboard();
        LiveMetricsText.Text = _lastResourceSample is null
            ? S("MonitoringIdle")
            : string.Format(S("LiveMetricsFormat"), _lastResourceSample.CpuPercent,
                _lastResourceSample.WorkingSetBytes / 1024d / 1024d);
        var oldMenu = _trayIcon.ContextMenuStrip;
        _trayIcon.ContextMenuStrip = BuildTrayMenu();
        oldMenu?.Dispose();
    }

    private void UpdateLanguageButton() =>
        LanguageToggleButton.Content = _currentLanguage == "ru" ? "RU  Русский" : "EN  English";

    private async void Appearance_Checked(object sender, RoutedEventArgs e)
    {
        if (_initializingAppearance || sender is not System.Windows.Controls.RadioButton choice) return;
        if (choice == CalmStyleChoice || choice == AggressiveStyleChoice)
            _currentVisualStyle = choice.Tag?.ToString() == "aggressive" ? "aggressive" : "calm";
        else
            _currentTheme = choice.Tag?.ToString() == "light" ? "light" : "dark";
        App.ApplyAppearance(_currentTheme, _currentVisualStyle);
        await SavePreferencesAsync();
    }

    private Task SavePreferencesAsync() =>
        _preferencesRepository.SaveAsync(new UserPreferences(_currentLanguage, _currentTheme, _currentVisualStyle));

    private void EditActiveKit_Click(object sender, RoutedEventArgs e)
    {
        KitsNavigation.IsChecked = true;
        ReloadCatalog(_trackingService.ActiveKit.Id);
    }

    private void ViewSessions_Click(object sender, RoutedEventArgs e) => SessionsNavigation.IsChecked = true;

    private static string S(string key) =>
        System.Windows.Application.Current.TryFindResource(key) as string ?? key;

    private async Task RunUiActionAsync(Func<Task> action, string heading)
    {
        try { await action(); }
        catch (Exception exception) { ShowError(heading, exception); }
    }

    private void ShowError(string heading, Exception exception) =>
        WpfMessageBox.Show(this, $"{heading}\n\n{exception.Message}\n\n{string.Format(S("DataFolder"), _dataPaths.RootDirectory)}",
            "KIT", MessageBoxButton.OK, MessageBoxImage.Error);

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        HideToTray();
    }

    private void HideToTray()
    {
        Hide();
        ShowInTaskbar = false;
    }

    private void RestoreWindow()
    {
        Dispatcher.Invoke(() =>
        {
            ShowInTaskbar = true;
            Show();
            WindowState = WindowState.Normal;
            Activate();
        });
    }

    public void RestoreFromExternalLaunch() => RestoreWindow();

    private void RequestExit()
    {
        _allowClose = true;
        _trayIcon.Visible = false;
        System.Windows.Application.Current.Shutdown();
    }

    private sealed record SessionRow(string Date, string Kit, string Duration,
        string AverageCpu, string PeakCpu, string AverageRam, string PeakRam);
    private sealed record ActivityRow(string Title, string Detail, string Time);
}
