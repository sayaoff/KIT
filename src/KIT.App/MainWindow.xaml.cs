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
    private readonly Forms.NotifyIcon _trayIcon;
    private readonly List<ApplicationTarget> _cleanApps = [];
    private readonly List<ApplicationTarget> _launchApps = [];
    private bool _reloadingCatalog;
    private bool _allowClose;

    public MainWindow(GameTrackingService trackingService, LocalDataPaths dataPaths)
    {
        InitializeComponent();
        _trackingService = trackingService;
        _dataPaths = dataPaths;
        _trackingService.StatusChanged += TrackingService_StatusChanged;
        _trackingService.ActivityRecorded += TrackingService_ActivityRecorded;
        _trackingService.CatalogChanged += TrackingService_CatalogChanged;
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
    }

    private Forms.ContextMenuStrip BuildTrayMenu()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open KIT", null, (_, _) => RestoreWindow());
        menu.Items.Add("Exit", null, (_, _) => Dispatcher.Invoke(RequestExit));
        return menu;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await _trackingService.StartAsync();
            ShowConfiguredPath();
            ReloadCatalog(_trackingService.Catalog.ActiveKitId);
            var activity = await _trackingService.ReadRecentActivityAsync();
            ActivityText.Text = activity.Count == 0
                ? "No activity yet."
                : string.Join(Environment.NewLine, activity.Select(FormatActivity));
            ActivityText.ScrollToEnd();
        }
        catch (Exception exception) { ShowError("KIT could not start.", exception); }
    }

    private async void SelectExecutable_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new WpfOpenFileDialog
        {
            Title = "Locate Counter-Strike 2", Filter = "Counter-Strike 2 (cs2.exe)|cs2.exe",
            CheckFileExists = true, Multiselect = false
        };
        if (_trackingService.Configuration is { } configuration)
            dialog.InitialDirectory = Path.GetDirectoryName(configuration.ExecutablePath);
        if (dialog.ShowDialog(this) is not true) return;

        await RunUiActionAsync(async () =>
        {
            await _trackingService.ConfigureAsync(dialog.FileName);
            ShowConfiguredPath();
        }, "KIT could not use the selected executable.");
    }

    private void KitSelector_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_reloadingCatalog || KitSelector.SelectedItem is not KitDefinition kit) return;
        LoadKitEditor(kit);
    }

    private async void NewKit_Click(object sender, RoutedEventArgs e)
    {
        var baseName = "New Kit";
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
        }, "Could not create the Kit.");
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
        }, "Could not save the Kit.");
    }

    private async void ActivateKit_Click(object sender, RoutedEventArgs e)
    {
        if (KitSelector.SelectedItem is not KitDefinition selected) return;
        await RunUiActionAsync(async () =>
        {
            await _trackingService.SetActiveKitAsync(selected.Id);
            ReloadCatalog(selected.Id);
        }, "Could not activate the Kit.");
    }

    private async void DeleteKit_Click(object sender, RoutedEventArgs e)
    {
        if (KitSelector.SelectedItem is not KitDefinition selected || selected.IsVanilla) return;
        if (WpfMessageBox.Show(this, $"Delete ‘{selected.Name}’?", "KIT", MessageBoxButton.YesNo,
                MessageBoxImage.Question) is not MessageBoxResult.Yes) return;
        await RunUiActionAsync(async () =>
        {
            await _trackingService.DeleteKitAsync(selected.Id);
            ReloadCatalog(_trackingService.Catalog.ActiveKitId);
        }, "Could not delete the Kit.");
    }

    private void AddCleanApp_Click(object sender, RoutedEventArgs e) => AddApplication(_cleanApps, CleanAppsList);
    private void AddLaunchApp_Click(object sender, RoutedEventArgs e) => AddApplication(_launchApps, LaunchAppsList);
    private void RemoveCleanApp_Click(object sender, RoutedEventArgs e) => RemoveApplication(_cleanApps, CleanAppsList);
    private void RemoveLaunchApp_Click(object sender, RoutedEventArgs e) => RemoveApplication(_launchApps, LaunchAppsList);

    private void AddApplication(List<ApplicationTarget> targets, System.Windows.Controls.ListBox listBox)
    {
        var dialog = new WpfOpenFileDialog
        {
            Title = "Select an application", Filter = "Windows applications (*.exe)|*.exe",
            CheckFileExists = true, Multiselect = false
        };
        if (dialog.ShowDialog(this) is not true) return;
        if (_trackingService.Configuration is { } configuration &&
            string.Equals(Path.GetFullPath(dialog.FileName), configuration.ExecutablePath, StringComparison.OrdinalIgnoreCase))
        {
            WpfMessageBox.Show(this, "CS2 cannot be added as a Kit action.", "KIT",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
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
        ActiveKitText.Text = $"Active Kit: {_trackingService.ActiveKit.Name}";
    }

    private void TrackingService_CatalogChanged(object? sender, KitCatalog catalog) =>
        Dispatcher.Invoke(() => ReloadCatalog(KitSelector.SelectedItem is KitDefinition selected ? selected.Id : catalog.ActiveKitId));

    private void TrackingService_StatusChanged(object? sender, TrackingStatus status) => Dispatcher.Invoke(() =>
    {
        StatusText.Text = status.Message;
        StatusDot.Fill = status.State switch
        {
            TrackingState.GameRunning => new SolidColorBrush(WpfColor.FromRgb(112, 224, 163)),
            TrackingState.Watching => new SolidColorBrush(WpfColor.FromRgb(111, 174, 255)),
            _ => new SolidColorBrush(WpfColor.FromRgb(169, 179, 191))
        };
        if (KitSelector.SelectedItem is KitDefinition selected) LoadKitEditor(selected);
    });

    private void TrackingService_ActivityRecorded(object? sender, ActivityEvent activity) => Dispatcher.Invoke(() =>
    {
        ActivityText.Text = ActivityText.Text == "No activity yet."
            ? FormatActivity(activity)
            : ActivityText.Text + Environment.NewLine + FormatActivity(activity);
        ActivityText.ScrollToEnd();
    });

    private static string FormatActivity(ActivityEvent activity)
    {
        var duration = activity.Duration is null ? "" : $" · {activity.Duration.Value:g}";
        var kit = activity.KitName is null ? "" : $" · {activity.KitName}";
        var details = activity.Details is null ? "" : $" · {activity.Details}";
        return $"{activity.OccurredAtUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}  {activity.Kind}{kit}{duration}{details}";
    }

    private void ShowConfiguredPath() =>
        ExecutablePathText.Text = _trackingService.Configuration?.ExecutablePath ?? "No executable selected";

    private async Task RunUiActionAsync(Func<Task> action, string heading)
    {
        try { await action(); }
        catch (Exception exception) { ShowError(heading, exception); }
    }

    private void ShowError(string heading, Exception exception) =>
        WpfMessageBox.Show(this, $"{heading}\n\n{exception.Message}\n\nData folder: {_dataPaths.RootDirectory}",
            "KIT", MessageBoxButton.OK, MessageBoxImage.Error);

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
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

    private void RequestExit()
    {
        _allowClose = true;
        _trayIcon.Visible = false;
        System.Windows.Application.Current.Shutdown();
    }
}
