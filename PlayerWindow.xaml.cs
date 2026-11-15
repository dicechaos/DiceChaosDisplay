using System.Windows;
using System.Windows.Input;
using DiceChaosDisplay.Services;
using DiceChaosDisplay.ViewModels;

namespace DiceChaosDisplay;

public partial class PlayerWindow : Window
{
    private readonly MainViewModel _viewModel;
    private Window? _dmOwnerWindow;

    public PlayerWindow(MainViewModel viewModel, Window? dmOwnerWindow = null)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _dmOwnerWindow = dmOwnerWindow;
        DataContext = _viewModel;

        ConfigureInitialChromeBeforeShow();
        SourceInitialized += OnSourceInitialized;
    }

    private MonitorDescriptor? ResolveTargetMonitor() =>
        _viewModel.SelectedTvMonitor
        ?? DisplayCalibrationService.SelectTargetTvMonitor(_viewModel.TvProfile.PreferredMonitorDeviceName);

    private bool ShouldUseFloatingPreview(MonitorDescriptor? target) =>
        !_viewModel.ForceFullscreenOnSameMonitor
        && DisplayCalibrationService.IsSameMonitorAsWindow(_dmOwnerWindow, target);

    private void ConfigureInitialChromeBeforeShow()
    {
        var target = ResolveTargetMonitor();
        bool preview = ShouldUseFloatingPreview(target);
        _viewModel.IsPlayerWindowPreviewMode = preview;

        if (preview)
        {
            ShowActivated = true;
            ShowInTaskbar = true;
            Title = "DiceChaos — Player TV Preview (Press F12 or Esc to Close)";
            DisplayCalibrationService.PositionFloatingPreviewWindowOnMonitor(this, target);
        }
        else
        {
            // Keep keyboard focus on MainWindow when launching borderless on a secondary TV!
            ShowActivated = false;
            ShowInTaskbar = false;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            Title = "DiceChaos TV Display";
        }
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        ApplyDisplayPlacement(_dmOwnerWindow);
    }

    public void MoveToTargetMonitor() => ApplyDisplayPlacement(_dmOwnerWindow);

    public void ApplyDisplayPlacement(Window? dmOwnerWindow)
    {
        if (dmOwnerWindow is not null)
            _dmOwnerWindow = dmOwnerWindow;

        var target = ResolveTargetMonitor();
        bool preview = ShouldUseFloatingPreview(target);
        _viewModel.IsPlayerWindowPreviewMode = preview;

        if (preview)
        {
            Title = "DiceChaos — Player TV Preview (Press F12 or Esc to Close)";
            if (!ShowInTaskbar)
                ShowInTaskbar = true;

            DisplayCalibrationService.PositionFloatingPreviewWindowOnMonitor(this, target);
        }
        else
        {
            Title = "DiceChaos TV Display";
            if (ShowInTaskbar)
                ShowInTaskbar = false;

            if (target is not null)
            {
                DisplayCalibrationService.PositionBorderlessWindowOnMonitor(this, target);
            }
            else
            {
                WindowStyle = WindowStyle.None;
                ResizeMode = ResizeMode.NoResize;
                WindowState = WindowState.Maximized;
            }
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Escape or Key.F12)
        {
            e.Handled = true;
            var ownerToFocus = _dmOwnerWindow;
            Close();
            ownerToFocus?.Activate();
            return;
        }

        if (e.Key == Key.B && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            _viewModel.IsTvBlackedOut = !_viewModel.IsTvBlackedOut;
        }
    }
}
