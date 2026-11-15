using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DiceChaosDisplay.Models;
using DiceChaosDisplay.ViewModels;
using DiceChaosDisplay.Views;
using Microsoft.Win32;

namespace DiceChaosDisplay;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private PlayerWindow? _playerWindow;
    private bool _isDmFullscreen;
    private WindowState _prevDmWindowState = WindowState.Normal;
    private WindowStyle _prevDmWindowStyle = WindowStyle.SingleBorderWindow;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        DataContext = _viewModel;

        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        SystemEvents.DisplaySettingsChanged += OnSystemDisplaySettingsChanged;
        _viewModel.SyncOfflineTvViewportSizeFromMonitor();
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        SystemEvents.DisplaySettingsChanged -= OnSystemDisplaySettingsChanged;
    }

    private void OnSystemDisplaySettingsChanged(object? sender, EventArgs e)
    {
        _ = Dispatcher.InvokeAsync(() =>
        {
            _viewModel.RefreshMonitors();
            if (_playerWindow is not null && _playerWindow.IsLoaded)
            {
                _playerWindow.ApplyDisplayPlacement(this);
            }
        });
    }

    #region File & Campaign Menu Actions

    private async void NewCampaign_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscardUnsavedIfNeeded()) return;
        _viewModel.NewCampaign();
        await Task.CompletedTask;
    }

    private async void OpenCampaign_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscardUnsavedIfNeeded()) return;

        var dlg = new OpenFileDialog
        {
            Title = "Open DiceChaos Campaign",
            Filter = "DiceChaos Campaign (*.dicechaos)|*.dicechaos|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog(this) == true)
        {
            await LoadCampaignSafeAsync(dlg.FileName);
        }
    }

    private void RecentCampaignsMenu_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        _viewModel.RefreshRecentCampaigns();
        RecentCampaignsMenu.Items.Clear();

        if (_viewModel.RecentCampaignFiles.Count == 0)
        {
            RecentCampaignsMenu.Items.Add(new MenuItem { Header = "(No recent campaigns)", IsEnabled = false });
            return;
        }

        foreach (string file in _viewModel.RecentCampaignFiles)
        {
            string captured = file;
            var item = new MenuItem
            {
                Header = Path.GetFileName(captured),
                ToolTip = captured
            };
            item.Click += async (_, _) =>
            {
                if (!ConfirmDiscardUnsavedIfNeeded()) return;
                await LoadCampaignSafeAsync(captured);
            };
            RecentCampaignsMenu.Items.Add(item);
        }
    }

    private async Task LoadCampaignSafeAsync(string filePath)
    {
        try
        {
            await _viewModel.LoadCampaignAsync(filePath);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not load campaign:\n{ex.Message}", "Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void SaveCampaign_Click(object sender, RoutedEventArgs e)
    {
        await SaveCampaignInteractiveAsync(forceSaveAs: false);
    }

    private async void SaveCampaignAs_Click(object sender, RoutedEventArgs e)
    {
        await SaveCampaignInteractiveAsync(forceSaveAs: true);
    }

    private async Task<bool> SaveCampaignInteractiveAsync(bool forceSaveAs)
    {
        string? path = forceSaveAs ? null : _viewModel.CurrentCampaignFilePath;
        if (string.IsNullOrWhiteSpace(path))
        {
            var dlg = new SaveFileDialog
            {
                Title = "Save DiceChaos Campaign",
                FileName = $"{_viewModel.CampaignName}.dicechaos",
                DefaultExt = ".dicechaos",
                Filter = "DiceChaos Campaign (*.dicechaos)|*.dicechaos"
            };
            if (dlg.ShowDialog(this) != true) return false;
            path = dlg.FileName;
        }

        try
        {
            await _viewModel.SaveCampaignAsync(path);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Failed to save campaign:\n{ex.Message}", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private void ExitMenu_Click(object sender, RoutedEventArgs e) => Close();

    #endregion

    #region Scenes & Stage-and-Push Workflow

    private async void AddScene_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Add Battlemaps, Universal VTT (.dd2vtt/.uvtt), or Animated Video Maps",
            Multiselect = true,
            Filter = "All Supported Maps (*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.dd2vtt;*.uvtt;*.df2vtt;*.mp4;*.webm;*.mkv)|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.dd2vtt;*.uvtt;*.df2vtt;*.mp4;*.webm;*.mkv|Static Images (*.png;*.jpg;*.jpeg;*.webp;*.bmp)|*.png;*.jpg;*.jpeg;*.webp;*.bmp|Universal VTT (*.dd2vtt;*.uvtt;*.df2vtt)|*.dd2vtt;*.uvtt;*.df2vtt|Video Maps (*.mp4;*.webm;*.mkv)|*.mp4;*.webm;*.mkv|All Files (*.*)|*.*"
        };

        if (dlg.ShowDialog(this) == true)
        {
            await _viewModel.AddScenesFromFilesAsync(dlg.FileNames);
        }
    }

    private async void DuplicateStagedScene_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.StagedScene is { } s)
        {
            await _viewModel.DuplicateSceneAsync(s);
        }
    }

    private async void SceneCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SceneViewModel scene })
        {
            bool pushImmediate = e.ClickCount >= 2;
            await _viewModel.SelectAndStageSceneAsync(scene, pushIfFirstOrAuto: pushImmediate);
            e.Handled = true;
        }
    }

    private static SceneViewModel? TryGetSceneFromMenuSender(object sender)
    {
        if (sender is FrameworkElement { DataContext: SceneViewModel direct })
            return direct;
        if (sender is MenuItem { Parent: ContextMenu { PlacementTarget: FrameworkElement { DataContext: SceneViewModel viaTarget } } })
            return viaTarget;
        return null;
    }

    private async void SceneMenu_Push_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetSceneFromMenuSender(sender) is { } scene)
        {
            await _viewModel.SelectAndStageSceneAsync(scene, pushIfFirstOrAuto: true);
        }
    }

    private async void SceneMenu_Duplicate_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetSceneFromMenuSender(sender) is { } scene)
        {
            await _viewModel.DuplicateSceneAsync(scene);
        }
    }

    private void SceneMenu_MoveLeft_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetSceneFromMenuSender(sender) is { } scene)
            _viewModel.MoveScene(scene, -1);
    }

    private void SceneMenu_MoveRight_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetSceneFromMenuSender(sender) is { } scene)
            _viewModel.MoveScene(scene, +1);
    }

    private async void SceneMenu_ResetFog_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetSceneFromMenuSender(sender) is { } scene)
        {
            await scene.EnsureHydratedAsync();
            scene.FogEngine?.HideAll();
            foreach (var r in scene.Rooms) r.IsRevealed = false;
            _viewModel.HasUnsavedChanges = true;
            _viewModel.RequestViewportRepaint();
        }
    }

    private void RemoveSceneCard_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetSceneFromMenuSender(sender) is { } scene)
        {
            _viewModel.RemoveScene(scene);
            e.Handled = true;
        }
    }

    private async void PushToTv_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.PushStagedSceneToTvAsync();
    }

    #endregion

    #region Drag & Drop Files

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) return;

        var campaignFile = files.FirstOrDefault(f => Path.GetExtension(f).Equals(".dicechaos", StringComparison.OrdinalIgnoreCase));
        if (campaignFile is not null)
        {
            if (ConfirmDiscardUnsavedIfNeeded())
                await LoadCampaignSafeAsync(campaignFile);
            return;
        }

        await _viewModel.AddScenesFromFilesAsync(files);
    }

    #endregion

    #region TV Window, Sync & Pre-Staged Handouts

    private void TogglePlayerWindow_Click(object sender, RoutedEventArgs e) => TogglePlayerWindow();

    public void TogglePlayerWindow()
    {
        if (_playerWindow is not null || _viewModel.IsPlayerWindowOpen)
        {
            ClosePlayerWindow();
        }
        else
        {
            OpenPlayerWindow();
        }
    }

    public void OpenPlayerWindow()
    {
        if (_playerWindow is not null)
        {
            _playerWindow.ApplyDisplayPlacement(this);
            return;
        }

        _viewModel.PrepareForPlayerWindowOpen();

        _playerWindow = new PlayerWindow(_viewModel, this);
        _playerWindow.Closed += (_, _) =>
        {
            _playerWindow = null;
            if (_viewModel.IsPlayerWindowOpen)
            {
                _viewModel.IsPlayerWindowOpen = false;
                _viewModel.StatusMessage = "Player TV Screen turned OFF (press F12 or click 'TV Off' to turn back ON).";
            }
        };

        _viewModel.IsPlayerWindowOpen = true;
        _playerWindow.Show();

        if (_viewModel.IsPlayerWindowPreviewMode)
        {
            _viewModel.StatusMessage = "Player TV opened in Floating Preview (same screen as DM Studio) — select/connect a 2nd display for fullscreen, or press F12 / Esc to close.";
        }
        else
        {
            if (IsLoaded) Activate();
            _viewModel.StatusMessage = _viewModel.IsTvBlackedOut && !_viewModel.HasLiveTvScene
                ? "Player TV Screen is LIVE (Auto-Blackout engaged for spoiler safety — push a scene or press 'B' to reveal)."
                : "Player TV Screen is LIVE on secondary display (press F12 to toggle OFF).";
        }
    }

    public void ClosePlayerWindow()
    {
        var win = _playerWindow;
        _playerWindow = null;
        _viewModel.IsPlayerWindowOpen = false;

        if (win is not null)
        {
            win.Close();
        }

        _viewModel.StatusMessage = "Player TV Screen turned OFF (press F12 or click 'TV Off' to turn back ON).";
    }

    private void ForceFullscreenSameMonitor_Click(object sender, RoutedEventArgs e)
    {
        if (_playerWindow is not null)
        {
            _playerWindow.ApplyDisplayPlacement(this);
        }
    }

    private void TvMonitor_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_playerWindow is not null)
        {
            _playerWindow.ApplyDisplayPlacement(this);
        }
    }

    private void RefreshMonitors_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.RefreshMonitors();
        if (_playerWindow is not null)
        {
            _playerWindow.ApplyDisplayPlacement(this);
        }
    }

    private void CenterTv_Click(object sender, RoutedEventArgs e) => DmViewport.CenterTvAtCursorOrViewCenter();

    private void SetSyncOneInch_Click(object sender, RoutedEventArgs e) => _viewModel.TvProfile.SyncMode = TvSyncMode.PhysicalOneInch;
    private void SetSyncMirror_Click(object sender, RoutedEventArgs e) => _viewModel.TvProfile.SyncMode = TvSyncMode.MirrorDm;
    private void SetSyncIndependent_Click(object sender, RoutedEventArgs e) => _viewModel.TvProfile.SyncMode = TvSyncMode.IndependentTv;

    private void OpenTvRulerCalibration_Click(object sender, RoutedEventArgs e)
    {
        OpenPlayerWindow();
        _viewModel.IsTvCalibrationRulerVisible = true;
        _viewModel.IsRightInspectorExpanded = true;
        _viewModel.StatusMessage = "Showing 1-Inch Calibration Shapes on TV — adjust slider in the 'TV & 1\"' tab until it matches a physical 1-inch mini base!";
    }

    private async void AddHandouts_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Add Monster, NPC, Letter, or Item Artwork to Handout Gallery",
            Multiselect = true,
            Filter = "Images (*.png;*.jpg;*.jpeg;*.webp;*.bmp)|*.png;*.jpg;*.jpeg;*.webp;*.bmp|All Files (*.*)|*.*"
        };
        if (dlg.ShowDialog(this) == true)
        {
            await _viewModel.AddHandoutsFromFilesAsync(dlg.FileNames);
            _viewModel.IsRightInspectorExpanded = true;
        }
    }

    private void ProjectHandoutCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: HandoutItemModel item })
        {
            _viewModel.ProjectHandoutItem(item);
        }
    }

    private void RemoveHandoutCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: HandoutItemModel item })
        {
            _viewModel.RemoveHandout(item);
        }
    }

    private void ShowAdHocHandout_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select Monster, NPC, Item, or Puzzle Artwork for TV Handout",
            Filter = "Images (*.png;*.jpg;*.jpeg;*.webp;*.bmp)|*.png;*.jpg;*.jpeg;*.webp;*.bmp|All Files (*.*)|*.*"
        };
        if (dlg.ShowDialog(this) == true)
        {
            _viewModel.ShowAdHocHandoutOnTv(dlg.FileName);
        }
    }

    private void DismissHandout_Click(object sender, RoutedEventArgs e) => _viewModel.DismissHandout();

    private void SetTvRotation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string s } && double.TryParse(s, CultureInfo.InvariantCulture, out double deg))
        {
            _viewModel.TvProfile.RotationAngle = deg;
        }
    }

    private void SetTvDiagonalPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string s } && double.TryParse(s, CultureInfo.InvariantCulture, out double diag))
        {
            _viewModel.TvProfile.DiagonalInches = diag;
        }
    }

    private void ApplyRulerCalibration_Click(object sender, RoutedEventArgs e) => _viewModel.ApplyOnTvRulerCalibration();
    private void ResetDiagonalCalibration_Click(object sender, RoutedEventArgs e) => _viewModel.ResetToDiagonalInchesCalibration();

    private void ChooseStandbyImage_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Choose Standby / Splash Screen Image",
            Filter = "Images (*.png;*.jpg;*.jpeg;*.webp;*.bmp)|*.png;*.jpg;*.jpeg;*.webp;*.bmp"
        };
        if (dlg.ShowDialog(this) == true)
        {
            _viewModel.SetStandbyImage(dlg.FileName);
        }
    }

    private void ClearStandbyImage_Click(object sender, RoutedEventArgs e) => _viewModel.SetStandbyImage(null);

    #endregion

    #region View, Camera, Focus Mode & Shortcuts Window

    private void ToggleFocusMode_Click(object sender, RoutedEventArgs e) => _viewModel.ToggleFocusMode();
    private void ToggleRightInspector_Click(object sender, RoutedEventArgs e) => _viewModel.IsRightInspectorExpanded = !_viewModel.IsRightInspectorExpanded;
    private void ToggleSceneStrip_Click(object sender, RoutedEventArgs e) => _viewModel.IsSceneStripExpanded = !_viewModel.IsSceneStripExpanded;

    private void ToggleDmFullscreen_Click(object sender, RoutedEventArgs e) => ToggleDmFullscreen();

    private void ToggleDmFullscreen()
    {
        if (!_isDmFullscreen)
        {
            _prevDmWindowState = WindowState;
            _prevDmWindowStyle = WindowStyle;
            WindowStyle = WindowStyle.None;
            WindowState = WindowState.Maximized;
            _isDmFullscreen = true;
        }
        else
        {
            WindowStyle = _prevDmWindowStyle;
            WindowState = _prevDmWindowState;
            _isDmFullscreen = false;
        }
    }

    private void FitMap_Click(object sender, RoutedEventArgs e) => DmViewport.FitDmToCurrentScene();
    private void ZoomIn_Click(object sender, RoutedEventArgs e) => DmViewport.ZoomByStep(1.22);
    private void ZoomOut_Click(object sender, RoutedEventArgs e) => DmViewport.ZoomByStep(1.0 / 1.22);
    private void Zoom100_Click(object sender, RoutedEventArgs e) => DmViewport.SetZoomOneToOne();

    private void OpenShortcuts_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new ShortcutsWindow { Owner = this };
        dlg.ShowDialog();
    }

    #endregion

    #region Unified Undo/Redo, Fog, Tactical & Media Handlers

    private void Undo_Click(object sender, RoutedEventArgs e) => _viewModel.Undo();
    private void Redo_Click(object sender, RoutedEventArgs e) => _viewModel.Redo();
    private void RevealAll_Click(object sender, RoutedEventArgs e) => _viewModel.RevealAllFog();
    private void HideAll_Click(object sender, RoutedEventArgs e) => _viewModel.HideAllFog();

    private void ClearPencil_Click(object sender, RoutedEventArgs e) => _viewModel.ClearPencilDrawings();
    private void ClearAllAoes_Click(object sender, RoutedEventArgs e) => _viewModel.ClearAllAoes();
    private void ClearAllTokens_Click(object sender, RoutedEventArgs e) => _viewModel.ClearAllTokens();
    private void ClearAllRooms_Click(object sender, RoutedEventArgs e) => _viewModel.ClearAllRooms();
    private void ClearAllDmPins_Click(object sender, RoutedEventArgs e) => _viewModel.ClearAllDmPins();

    private void ActivateRoomPrep_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ActiveTool = DmToolMode.RoomPrep;
        _viewModel.StatusMessage = "Room Prep Mode: Click around a room's corners, then Double-Click, Right-Click, or press Enter to save a 1-click room!";
    }

    private void ToggleRoomItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: FogRoomRegion room } && _viewModel.StagedScene is not null)
        {
            _viewModel.StagedScene.ToggleRoomReveal(room, _viewModel.FogFeatherSoftness);
            _viewModel.HasUnsavedChanges = true;
            _viewModel.NotifyUndoRedoChanged();
        }
    }

    private void FocusRoomItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: FogRoomRegion room })
        {
            Point c = new(room.Centroid.X, room.Centroid.Y);
            _viewModel.DmCamera = _viewModel.DmCamera with { CenterMapX = c.X, CenterMapY = c.Y };
            _viewModel.CenterTvAtMapPoint(c);
        }
    }

    private void DeleteRoomItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: FogRoomRegion room })
        {
            _viewModel.DeleteRoom(room);
        }
    }

    private void SetPencilColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string hex })
            _viewModel.PencilColorHex = hex;
    }

    private void ConditionPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string preset })
        {
            _viewModel.ApplyConditionPreset(preset);
            _viewModel.ActiveTool = DmToolMode.PlaceToken;
        }
    }

    private void SetTokenSize_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string s } && double.TryParse(s, CultureInfo.InvariantCulture, out double size))
        {
            _viewModel.NewTokenSizeCells = size;
            if (_viewModel.SelectedToken is not null) _viewModel.SelectedToken.SizeCells = size;
            _viewModel.ActiveTool = DmToolMode.PlaceToken;
        }
    }

    private void SetTokenAura_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string s } && double.TryParse(s, CultureInfo.InvariantCulture, out double aura))
        {
            _viewModel.NewTokenAuraFeet = aura;
            if (_viewModel.SelectedToken is not null) _viewModel.SelectedToken.AuraRadiusFeet = aura;
        }
    }

    private void SetAoeSize_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string s } && double.TryParse(s, CultureInfo.InvariantCulture, out double ft))
        {
            _viewModel.NewAoeSizeFeet = ft;
            _viewModel.ActiveTool = DmToolMode.PlaceAoe;
        }
    }

    private void SetAoeColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string hex })
        {
            _viewModel.NewAoeColorHex = hex;
            if (_viewModel.SelectedAoeTemplate is not null)
            {
                _viewModel.SelectedAoeTemplate.ColorHex = hex;
            }
        }
    }

    private void DeleteSelected_Click(object sender, RoutedEventArgs e) => _viewModel.DeleteSelectedElement();

    private void Start3x3GridAlign_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ActiveTool = DmToolMode.GridAlign3x3;
        _viewModel.StatusMessage = "3×3 Grid Matcher: Drag the White Top-Left handle to an intersection, then drag the Gold Bottom-Right handle over 3×3 squares!";
    }

    private void DoneGridAlign_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ActiveTool = DmToolMode.PanSelect;
    }

    private void NudgeGridPpc_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.StagedScene is null) return;
        if (sender is FrameworkElement { Tag: string s } && double.TryParse(s, CultureInfo.InvariantCulture, out double delta))
        {
            _viewModel.StagedScene.Grid.PixelsPerCell += delta;
            _viewModel.HasUnsavedChanges = true;
            if (_viewModel.TvProfile.SyncMode == TvSyncMode.PhysicalOneInch)
                _viewModel.UpdateTvCameraSync();
        }
    }

    private void SetGridColor_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.StagedScene is not null && sender is FrameworkElement { Tag: string hex })
        {
            _viewModel.StagedScene.Grid.ColorHex = hex;
            _viewModel.HasUnsavedChanges = true;
        }
    }

    private void ToggleVideoPlayPause_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.StagedScene?.VideoInstance?.TogglePlayPause();
    }

    private void VideoAudioChanged_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.StagedScene?.VideoInstance is { } vid)
        {
            vid.SetAudio(_viewModel.StagedScene.Model.IsVideoMuted, _viewModel.StagedScene.Model.VideoVolume);
        }
    }

    private void VideoVolumeSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_viewModel?.StagedScene?.VideoInstance is { } vid)
        {
            vid.SetAudio(_viewModel.StagedScene.Model.IsVideoMuted, _viewModel.StagedScene.Model.VideoVolume);
        }
    }

    #endregion

    #region Global Keyboard Shortcuts & Exit Prompt

    private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F12 && Keyboard.Modifiers == ModifierKeys.None)
        {
            TogglePlayerWindow();
            e.Handled = true;
            return;
        }

        // Do not hijack single-key shortcuts while typing in a TextBox!
        if (Keyboard.FocusedElement is TextBox)
        {
            if (e.Key == Key.Escape)
            {
                DmViewport.Focus();
                e.Handled = true;
            }
            return;
        }

        ModifierKeys mods = Keyboard.Modifiers;
        if ((mods & ModifierKeys.Control) != 0)
        {
            bool shift = (mods & ModifierKeys.Shift) != 0;
            switch (e.Key)
            {
                case Key.Z:
                    _viewModel.Undo();
                    e.Handled = true;
                    break;
                case Key.Y:
                    _viewModel.Redo();
                    e.Handled = true;
                    break;
                case Key.S:
                    e.Handled = true;
                    await SaveCampaignInteractiveAsync(forceSaveAs: shift);
                    break;
                case Key.N:
                    e.Handled = true;
                    NewCampaign_Click(this, new RoutedEventArgs());
                    break;
                case Key.O:
                    e.Handled = true;
                    OpenCampaign_Click(this, new RoutedEventArgs());
                    break;
                case Key.I:
                    e.Handled = true;
                    AddScene_Click(this, new RoutedEventArgs());
                    break;
                case Key.Enter:
                    e.Handled = true;
                    PushToTv_Click(this, new RoutedEventArgs());
                    break;
                case Key.D0:
                case Key.NumPad0:
                    e.Handled = true;
                    DmViewport.SetZoomOneToOne();
                    break;
            }
            return;
        }

        if (mods == ModifierKeys.None)
        {
            switch (e.Key)
            {
                case Key.D1: _viewModel.ActiveTool = DmToolMode.PanSelect; e.Handled = true; break;
                case Key.D2: _viewModel.ActiveTool = DmToolMode.FogBrush; e.Handled = true; break;
                case Key.D3: _viewModel.ActiveTool = DmToolMode.FogBox; e.Handled = true; break;
                case Key.D4: _viewModel.ActiveTool = DmToolMode.FogPolygon; e.Handled = true; break;
                case Key.D5: _viewModel.ActiveTool = DmToolMode.RoomPrep; e.Handled = true; break;
                case Key.D6: _viewModel.ActiveTool = DmToolMode.GridAlign3x3; e.Handled = true; break;
                case Key.D7: _viewModel.ActiveTool = DmToolMode.Pencil; e.Handled = true; break;
                case Key.D8: _viewModel.ActiveTool = DmToolMode.Ping; e.Handled = true; break;
                case Key.D9: _viewModel.ActiveTool = DmToolMode.Ruler; e.Handled = true; break;
                case Key.A: _viewModel.ActiveTool = DmToolMode.PlaceAoe; e.Handled = true; break;
                case Key.T: _viewModel.ActiveTool = DmToolMode.PlaceToken; e.Handled = true; break;
                case Key.N: _viewModel.ActiveTool = DmToolMode.PlaceDmPin; e.Handled = true; break;
                case Key.B: _viewModel.IsTvBlackedOut = !_viewModel.IsTvBlackedOut; e.Handled = true; break;
                case Key.C: DmViewport.CenterTvAtCursorOrViewCenter(); e.Handled = true; break;
                case Key.F: DmViewport.FitDmToCurrentScene(); e.Handled = true; break;
                case Key.Tab: _viewModel.ToggleFocusMode(); e.Handled = true; break;
                case Key.F1: OpenShortcuts_Click(this, new RoutedEventArgs()); e.Handled = true; break;
                case Key.F11: ToggleDmFullscreen(); e.Handled = true; break;
            }
        }
    }

    private bool ConfirmDiscardUnsavedIfNeeded()
    {
        if (!_viewModel.HasUnsavedChanges) return true;
        var res = MessageBox.Show(
            this,
            "You have unsaved changes in the current campaign. Continue without saving?",
            "Unsaved Changes",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        return res == MessageBoxResult.Yes;
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        string tvSuffix = _viewModel.IsPlayerWindowOpen ? " and close the TV display" : "";
        string prompt = _viewModel.HasUnsavedChanges
            ? $"You have unsaved campaign changes.\nAre you sure you want to exit DiceChaos{tvSuffix}?"
            : $"Are you sure you want to exit DiceChaos{tvSuffix}?";

        var result = MessageBox.Show(
            this,
            prompt,
            "Confirm Exit",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.No)
        {
            e.Cancel = true;
            return;
        }

        ClosePlayerWindow();
        _viewModel.Dispose();
        Application.Current.Shutdown();
    }

    #endregion
}
