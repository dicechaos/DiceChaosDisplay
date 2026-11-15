using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DiceChaosDisplay.Controls;
using DiceChaosDisplay.Models;
using DiceChaosDisplay.Services;

namespace DiceChaosDisplay.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private CampaignProject _campaign = new();
    private string? _currentCampaignFilePath;
    private bool _hasUnsavedChanges;

    private SceneViewModel? _stagedScene;
    private SceneViewModel? _liveTvScene;

    private MonitorDescriptor? _selectedTvMonitor;
    private bool _isPlayerWindowOpen;
    private bool _isPlayerWindowPreviewMode;
    private bool _forceFullscreenOnSameMonitor;
    private bool _isTvBlackedOut;
    private BitmapSource? _standbyArtworkBitmap;

    private BitmapSource? _activeHandoutBitmap;
    private string _activeHandoutTitle = "";
    private HandoutItemModel? _liveHandoutItem;

    private bool _isTvCalibrationRulerVisible;
    private double _calibrationRulerDips = 96.0;

    private CameraState _dmCamera = new(960, 540, 0.5, 0);
    private CameraState _tvCamera = new(960, 540, 0.5, 0);
    private Size _dmViewportSize = new(800, 600);
    private Size _tvViewportSize = new(1920, 1080);

    private bool _isRightInspectorExpanded = true;
    private bool _isSceneStripExpanded = true;

    private DmToolMode _activeTool = DmToolMode.FogBrush;
    private FogPaintOperation _fogOperation = FogPaintOperation.Reveal;
    private double _fogBrushRadius = 80.0;
    private double _fogFeatherSoftness = 0.25;
    private double _dmFogOpacity = 0.55;

    // Layer Visibility & Display Toggles
    private bool _showFogOnDm = true;
    private bool _showUvttWallGuides = true;
    private bool _showDmRoomBadges = true;
    private bool _showTokensLayer = true;
    private bool _showAoeLayer = true;
    private bool _showPencilLayer = true;
    private bool _showDmPinsLayer = true;
    private bool _showTvFrameGuide = true;
    private bool _showAoeLabelsAlways;

    private string _pencilColorHex = "#FF4757";
    private double _pencilThickness = 6.0;

    private bool _showRulerOnTv = true;
    private bool _isRulerActive;
    private Point _rulerStartMapPt;
    private Point _rulerEndMapPt;

    private AoeShape _newAoeShape = AoeShape.Sphere;
    private double _newAoeSizeFeet = 20.0;
    private double _newAoeWidthFeet = 5.0;
    private string _newAoeColorHex = "#FF6B35";
    private AoeTemplateModel? _selectedAoeTemplate;

    private TokenMarkerKind _newTokenKind = TokenMarkerKind.ConditionRing;
    private double _newTokenSizeCells = 1.0;
    private double _newTokenAuraFeet;
    private string _newTokenLabel = ""; // Default: Clean Glow-Only Ring without colliding text boxes!
    private string _newTokenColorHex = "#00E5FF";
    private bool _snapTokensToGrid = true;
    private int _monsterAutoNumber = 1;
    private TokenMarkerModel? _selectedToken;

    private DmPinModel? _selectedDmPin;

    private string _statusMessage = "Ready — Drop a 4K/8K map, .dd2vtt/.uvtt, or MP4/WEBM battlemap onto the workspace.";

    private readonly List<ActivePingAnimation> _activePings = new();

    public ObservableCollection<SceneViewModel> Scenes { get; } = new();
    public ObservableCollection<HandoutItemModel> Handouts { get; } = new();
    public ObservableCollection<MonitorDescriptor> AvailableMonitors { get; } = new();
    public ObservableCollection<string> RecentCampaignFiles { get; } = new();

    public IReadOnlyList<ActivePingAnimation> ActivePings => _activePings;

    public event EventHandler? ViewportRepaintRequested;
    public event EventHandler? RequestFitDmCamera;

    public string CampaignName
    {
        get => _campaign.Name;
        set
        {
            if (_campaign.Name != value)
            {
                _campaign.Name = value;
                HasUnsavedChanges = true;
                OnPropertyChanged();
                OnPropertyChanged(nameof(WindowTitle));
            }
        }
    }

    public string? CurrentCampaignFilePath
    {
        get => _currentCampaignFilePath;
        private set
        {
            if (SetProperty(ref _currentCampaignFilePath, value))
                OnPropertyChanged(nameof(WindowTitle));
        }
    }

    public bool HasUnsavedChanges
    {
        get => _hasUnsavedChanges;
        set
        {
            if (SetProperty(ref _hasUnsavedChanges, value))
                OnPropertyChanged(nameof(WindowTitle));
        }
    }

    public string WindowTitle =>
        $"DiceChaos DM Studio — {CampaignName}{(HasUnsavedChanges ? " *" : "")}";

    public TvDisplayProfile TvProfile => _campaign.TvProfile;

    public SceneViewModel? StagedScene
    {
        get => _stagedScene;
        private set
        {
            if (ReferenceEquals(_stagedScene, value)) return;
            if (_stagedScene is not null)
            {
                _stagedScene.IsStaged = false;
                _stagedScene.HistoryChanged -= OnSceneHistoryChanged;
            }
            _stagedScene = value;
            if (_stagedScene is not null)
            {
                _stagedScene.IsStaged = true;
                _stagedScene.HistoryChanged += OnSceneHistoryChanged;
            }

            SelectedToken = null;
            SelectedAoeTemplate = null;
            SelectedDmPin = null;

            OnPropertyChanged();
            OnPropertyChanged(nameof(HasStagedScene));
            OnPropertyChanged(nameof(IsStagedDifferentFromLive));
            OnPropertyChanged(nameof(IsSceneActivelyBroadcasting));
            OnPropertyChanged(nameof(IsSceneQueuedWhileTvOff));
            NotifyUndoRedoChanged();
            CleanupInactiveScenes();
            RequestViewportRepaint();
        }
    }

    public SceneViewModel? LiveTvScene
    {
        get => _liveTvScene;
        private set
        {
            if (ReferenceEquals(_liveTvScene, value)) return;
            if (_liveTvScene is not null) _liveTvScene.IsLiveOnTv = false;
            _liveTvScene = value;
            if (_liveTvScene is not null) _liveTvScene.IsLiveOnTv = true;

            OnPropertyChanged();
            OnPropertyChanged(nameof(HasLiveTvScene));
            OnPropertyChanged(nameof(IsStagedDifferentFromLive));
            OnPropertyChanged(nameof(IsSceneActivelyBroadcasting));
            OnPropertyChanged(nameof(IsSceneQueuedWhileTvOff));
            CleanupInactiveScenes();
            UpdateTvCameraSync();
            RequestViewportRepaint();
        }
    }

    public bool HasStagedScene => _stagedScene is not null;
    public bool HasLiveTvScene => _liveTvScene is not null;
    public bool IsStagedDifferentFromLive => _stagedScene is not null && !ReferenceEquals(_stagedScene, _liveTvScene);
    public bool IsSceneActivelyBroadcasting => _stagedScene is not null && ReferenceEquals(_stagedScene, _liveTvScene) && _isPlayerWindowOpen;
    public bool IsSceneQueuedWhileTvOff => _stagedScene is not null && ReferenceEquals(_stagedScene, _liveTvScene) && !_isPlayerWindowOpen;

    public bool IsRightInspectorExpanded
    {
        get => _isRightInspectorExpanded;
        set => SetProperty(ref _isRightInspectorExpanded, value);
    }

    public bool IsSceneStripExpanded
    {
        get => _isSceneStripExpanded;
        set => SetProperty(ref _isSceneStripExpanded, value);
    }

    public void ToggleFocusMode()
    {
        bool anyOpen = _isRightInspectorExpanded || _isSceneStripExpanded;
        IsRightInspectorExpanded = !anyOpen;
        IsSceneStripExpanded = !anyOpen;
    }

    public bool AutoPushSceneOnSelect
    {
        get => _campaign.AutoPushSceneOnSelect;
        set
        {
            if (_campaign.AutoPushSceneOnSelect != value)
            {
                _campaign.AutoPushSceneOnSelect = value;
                OnPropertyChanged();
                if (value && _stagedScene is not null)
                {
                    _ = PushStagedSceneToTvAsync();
                }
            }
        }
    }

    public MonitorDescriptor? SelectedTvMonitor
    {
        get => _selectedTvMonitor;
        set
        {
            if (SetProperty(ref _selectedTvMonitor, value) && value is not null)
            {
                TvProfile.PreferredMonitorDeviceName = value.DeviceName;
                SaveDefaultTvProfile();
                SyncOfflineTvViewportSizeFromMonitor();
                UpdateTvCameraSync();
                OnPropertyChanged(nameof(ComputedPhysicalPpiDisplay));
            }
        }
    }

    public bool IsPlayerWindowOpen
    {
        get => _isPlayerWindowOpen;
        set
        {
            if (SetProperty(ref _isPlayerWindowOpen, value))
            {
                foreach (var s in Scenes)
                    s.IsTvPowerOn = value;

                if (!value)
                {
                    IsPlayerWindowPreviewMode = false;
                    SyncOfflineTvViewportSizeFromMonitor();
                }

                OnPropertyChanged(nameof(TvPowerButtonText));
                OnPropertyChanged(nameof(TvPowerToolTip));
                OnPropertyChanged(nameof(IsSceneActivelyBroadcasting));
                OnPropertyChanged(nameof(IsSceneQueuedWhileTvOff));
                RequestViewportRepaint();
            }
        }
    }

    public bool IsPlayerWindowPreviewMode
    {
        get => _isPlayerWindowPreviewMode;
        set
        {
            if (SetProperty(ref _isPlayerWindowPreviewMode, value))
            {
                OnPropertyChanged(nameof(TvPowerButtonText));
                OnPropertyChanged(nameof(TvPowerToolTip));
            }
        }
    }

    public bool ForceFullscreenOnSameMonitor
    {
        get => _forceFullscreenOnSameMonitor;
        set => SetProperty(ref _forceFullscreenOnSameMonitor, value);
    }

    public string TvPowerButtonText =>
        !_isPlayerWindowOpen ? "TV Off" : (_isPlayerWindowPreviewMode ? "● TV Preview" : "● TV Live");

    public string TvPowerToolTip =>
        !_isPlayerWindowOpen
            ? "Player TV Screen is OFF — Click or press F12 to turn ON"
            : (_isPlayerWindowPreviewMode
                ? "Player TV is ON in Floating Preview Mode (same display as DM Studio) — Click or press F12 / Esc to turn OFF"
                : "Player TV Screen is LIVE on Secondary Display — Click or press F12 to turn OFF");

    public void PrepareForPlayerWindowOpen()
    {
        // Spoiler safety: auto-engage Blackout if turned ON before any scene has been pushed/queued.
        // Once a scene is queued/live, preserve the exact prior Blackout & camera state across toggles.
        if (_liveTvScene is null)
        {
            IsTvBlackedOut = true;
        }
    }

    public void SyncOfflineTvViewportSizeFromMonitor()
    {
        if (!_isPlayerWindowOpen && _selectedTvMonitor is not null)
        {
            TvViewportSize = DisplayCalibrationService.GetEstimatedDipViewportSize(_selectedTvMonitor);
        }
    }

    public bool IsTvBlackedOut
    {
        get => _isTvBlackedOut;
        set
        {
            if (SetProperty(ref _isTvBlackedOut, value))
                RequestViewportRepaint();
        }
    }

    public BitmapSource? StandbyArtworkBitmap
    {
        get => _standbyArtworkBitmap;
        private set
        {
            if (SetProperty(ref _standbyArtworkBitmap, value))
                OnPropertyChanged(nameof(HasStandbyArtwork));
        }
    }

    public bool HasStandbyArtwork => _standbyArtworkBitmap is not null;

    public BitmapSource? ActiveHandoutBitmap
    {
        get => _activeHandoutBitmap;
        private set
        {
            if (SetProperty(ref _activeHandoutBitmap, value))
                OnPropertyChanged(nameof(IsHandoutVisible));
        }
    }

    public string ActiveHandoutTitle
    {
        get => _activeHandoutTitle;
        private set => SetProperty(ref _activeHandoutTitle, value);
    }

    public bool IsHandoutVisible => _activeHandoutBitmap is not null;

    public bool IsTvCalibrationRulerVisible
    {
        get => _isTvCalibrationRulerVisible;
        set => SetProperty(ref _isTvCalibrationRulerVisible, value);
    }

    public double CalibrationRulerDips
    {
        get => _calibrationRulerDips;
        set => SetProperty(ref _calibrationRulerDips, Math.Clamp(value, 24.0, 400.0));
    }

    public CameraState DmCamera
    {
        get => _dmCamera;
        set
        {
            if (SetProperty(ref _dmCamera, value))
            {
                if (TvProfile.SyncMode == TvSyncMode.MirrorDm && ReferenceEquals(_stagedScene, _liveTvScene))
                {
                    UpdateTvCameraSync();
                }
                OnPropertyChanged(nameof(DmZoomPercentText));
                RequestViewportRepaint();
            }
        }
    }

    public CameraState TvCamera
    {
        get => _tvCamera;
        set
        {
            if (SetProperty(ref _tvCamera, value))
            {
                if (_liveTvScene is not null)
                {
                    _liveTvScene.Model.TvCenterX = value.CenterMapX;
                    _liveTvScene.Model.TvCenterY = value.CenterMapY;
                    _liveTvScene.Model.TvZoom = value.Zoom;
                }
                RequestViewportRepaint();
            }
        }
    }

    public Size DmViewportSize
    {
        get => _dmViewportSize;
        set
        {
            if (SetProperty(ref _dmViewportSize, value))
            {
                if (TvProfile.SyncMode == TvSyncMode.MirrorDm) UpdateTvCameraSync();
                RequestViewportRepaint();
            }
        }
    }

    public Size TvViewportSize
    {
        get => _tvViewportSize;
        set
        {
            if (SetProperty(ref _tvViewportSize, value))
            {
                UpdateTvCameraSync();
                RequestViewportRepaint();
            }
        }
    }

    public string DmZoomPercentText => $"{_dmCamera.Zoom * 100.0:0}%";

    public double EffectiveTvPhysicalPpi
    {
        get
        {
            if (TvProfile.ManualPhysicalPpi > 5.0)
                return TvProfile.ManualPhysicalPpi;

            var mon = _selectedTvMonitor ?? AvailableMonitors.FirstOrDefault();
            int pw = mon?.PhysicalBounds.Width ?? 3840;
            int ph = mon?.PhysicalBounds.Height ?? 2160;
            return DisplayCalibrationService.CalculatePhysicalPpi(pw, ph, TvProfile.DiagonalInches);
        }
    }

    public string ComputedPhysicalPpiDisplay =>
        TvProfile.ManualPhysicalPpi > 5.0
            ? $"{EffectiveTvPhysicalPpi:0.1} PPI (Ruler Calibrated)"
            : $"{EffectiveTvPhysicalPpi:0.1} PPI ({TvProfile.DiagonalInches:0.#}\" TV)";

    public DmToolMode ActiveTool
    {
        get => _activeTool;
        set
        {
            if (SetProperty(ref _activeTool, value))
            {
                IsRulerActive = false;
                RequestViewportRepaint();
            }
        }
    }

    public FogPaintOperation FogOperation
    {
        get => _fogOperation;
        set
        {
            if (SetProperty(ref _fogOperation, value))
                RequestViewportRepaint();
        }
    }

    public double FogBrushRadius
    {
        get => _fogBrushRadius;
        set
        {
            if (SetProperty(ref _fogBrushRadius, Math.Clamp(value, 8.0, 800.0)))
                RequestViewportRepaint();
        }
    }

    public double FogFeatherSoftness
    {
        get => _fogFeatherSoftness;
        set => SetProperty(ref _fogFeatherSoftness, Math.Clamp(value, 0.0, 1.0));
    }

    public double DmFogOpacity
    {
        get => _dmFogOpacity;
        set
        {
            double clamped = Math.Clamp(value, 0.1, 0.9);
            if (SetProperty(ref _dmFogOpacity, clamped))
            {
                _campaign.DmFogOpacity = clamped;
                RequestViewportRepaint();
            }
        }
    }

    // Layer Visibility Properties
    public bool ShowFogOnDm
    {
        get => _showFogOnDm;
        set { if (SetProperty(ref _showFogOnDm, value)) RequestViewportRepaint(); }
    }

    public bool ShowUvttWallGuides
    {
        get => _showUvttWallGuides;
        set { if (SetProperty(ref _showUvttWallGuides, value)) RequestViewportRepaint(); }
    }

    public bool ShowDmRoomBadges
    {
        get => _showDmRoomBadges;
        set { if (SetProperty(ref _showDmRoomBadges, value)) RequestViewportRepaint(); }
    }

    public bool ShowTokensLayer
    {
        get => _showTokensLayer;
        set { if (SetProperty(ref _showTokensLayer, value)) RequestViewportRepaint(); }
    }

    public bool ShowAoeLayer
    {
        get => _showAoeLayer;
        set { if (SetProperty(ref _showAoeLayer, value)) RequestViewportRepaint(); }
    }

    public bool ShowPencilLayer
    {
        get => _showPencilLayer;
        set { if (SetProperty(ref _showPencilLayer, value)) RequestViewportRepaint(); }
    }

    public bool ShowDmPinsLayer
    {
        get => _showDmPinsLayer;
        set { if (SetProperty(ref _showDmPinsLayer, value)) RequestViewportRepaint(); }
    }

    public bool ShowTvFrameGuide
    {
        get => _showTvFrameGuide;
        set { if (SetProperty(ref _showTvFrameGuide, value)) RequestViewportRepaint(); }
    }

    public bool ShowAoeLabelsAlways
    {
        get => _showAoeLabelsAlways;
        set { if (SetProperty(ref _showAoeLabelsAlways, value)) RequestViewportRepaint(); }
    }

    public bool CanUndo => _stagedScene?.CanUndo ?? false;
    public bool CanRedo => _stagedScene?.CanRedo ?? false;

    public string PencilColorHex
    {
        get => _pencilColorHex;
        set => SetProperty(ref _pencilColorHex, value);
    }

    public double PencilThickness
    {
        get => _pencilThickness;
        set => SetProperty(ref _pencilThickness, Math.Clamp(value, 2.0, 30.0));
    }

    public bool ShowRulerOnTv
    {
        get => _showRulerOnTv;
        set { if (SetProperty(ref _showRulerOnTv, value)) RequestViewportRepaint(); }
    }

    public bool IsRulerActive
    {
        get => _isRulerActive;
        set { if (SetProperty(ref _isRulerActive, value)) RequestViewportRepaint(); }
    }

    public Point RulerStartMapPt
    {
        get => _rulerStartMapPt;
        set { _rulerStartMapPt = value; RequestViewportRepaint(); }
    }

    public Point RulerEndMapPt
    {
        get => _rulerEndMapPt;
        set { _rulerEndMapPt = value; RequestViewportRepaint(); }
    }

    public AoeShape NewAoeShape
    {
        get => _newAoeShape;
        set
        {
            if (SetProperty(ref _newAoeShape, value) && _selectedAoeTemplate is not null)
            {
                _selectedAoeTemplate.Shape = value;
                _selectedAoeTemplate.Label = $"{_selectedAoeTemplate.SizeFeet:0}ft {value}";
                RequestViewportRepaint();
            }
        }
    }

    public double NewAoeSizeFeet
    {
        get => _newAoeSizeFeet;
        set
        {
            double clamped = Math.Clamp(value, 5.0, 300.0);
            if (SetProperty(ref _newAoeSizeFeet, clamped) && _selectedAoeTemplate is not null)
            {
                _selectedAoeTemplate.SizeFeet = clamped;
                _selectedAoeTemplate.Label = $"{clamped:0}ft {_selectedAoeTemplate.Shape}";
                RequestViewportRepaint();
            }
        }
    }

    public double NewAoeWidthFeet
    {
        get => _newAoeWidthFeet;
        set
        {
            double clamped = Math.Clamp(value, 5.0, 60.0);
            if (SetProperty(ref _newAoeWidthFeet, clamped) && _selectedAoeTemplate is not null)
            {
                _selectedAoeTemplate.WidthFeet = clamped;
                RequestViewportRepaint();
            }
        }
    }

    public string NewAoeColorHex
    {
        get => _newAoeColorHex;
        set => SetProperty(ref _newAoeColorHex, value);
    }

    public AoeTemplateModel? SelectedAoeTemplate
    {
        get => _selectedAoeTemplate;
        set
        {
            if (SetProperty(ref _selectedAoeTemplate, value))
                RequestViewportRepaint();
        }
    }

    public TokenMarkerKind NewTokenKind
    {
        get => _newTokenKind;
        set => SetProperty(ref _newTokenKind, value);
    }

    public double NewTokenSizeCells
    {
        get => _newTokenSizeCells;
        set => SetProperty(ref _newTokenSizeCells, Math.Clamp(value, 0.5, 8.0));
    }

    public double NewTokenAuraFeet
    {
        get => _newTokenAuraFeet;
        set => SetProperty(ref _newTokenAuraFeet, Math.Clamp(value, 0.0, 120.0));
    }

    public string NewTokenLabel
    {
        get => _newTokenLabel;
        set => SetProperty(ref _newTokenLabel, value);
    }

    public string NewTokenColorHex
    {
        get => _newTokenColorHex;
        set => SetProperty(ref _newTokenColorHex, value);
    }

    public bool SnapTokensToGrid
    {
        get => _snapTokensToGrid;
        set => SetProperty(ref _snapTokensToGrid, value);
    }

    public TokenMarkerModel? SelectedToken
    {
        get => _selectedToken;
        set
        {
            if (SetProperty(ref _selectedToken, value))
                RequestViewportRepaint();
        }
    }

    public DmPinModel? SelectedDmPin
    {
        get => _selectedDmPin;
        set
        {
            if (SetProperty(ref _selectedDmPin, value))
                RequestViewportRepaint();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public MainViewModel()
    {
        var userSettings = CampaignStorageService.LoadUserSettings();
        _campaign.TvProfile = userSettings.DefaultTvProfile;
        RefreshRecentCampaigns(userSettings);
        HookTvProfileEvents();
        RefreshMonitors();
        ReloadStandbyArtwork();
    }

    public void RefreshRecentCampaigns(AppUserSettings? preloaded = null)
    {
        var settings = preloaded ?? CampaignStorageService.LoadUserSettings();
        RecentCampaignFiles.Clear();
        foreach (var path in settings.RecentCampaignFiles.Where(File.Exists))
        {
            RecentCampaignFiles.Add(path);
        }
    }

    public void RefreshMonitors()
    {
        var list = DisplayCalibrationService.GetMonitors();
        AvailableMonitors.Clear();
        foreach (var m in list)
            AvailableMonitors.Add(m);

        SelectedTvMonitor = DisplayCalibrationService.SelectTargetTvMonitor(TvProfile.PreferredMonitorDeviceName);
    }

    #region Condition Presets

    public void ApplyConditionPreset(string presetKey)
    {
        (string color, string label, double aura) = presetKey switch
        {
            "GlowOnly" => ("#00E5FF", "", 0.0),
            "Concentrating" => ("#00E5FF", "Concentrating", 0.0),
            "Bloodied" => ("#FF4757", "Bloodied", 0.0),
            "Blessed" => ("#FFD166", "Blessed", 0.0),
            "Hexed" => ("#9B5DE5", "Hexed", 0.0),
            "Poisoned" => ("#2ED573", "Poisoned", 0.0),
            "Prone" => ("#FFA502", "Prone", 0.0),
            "Aura10" => ("#FFD166", "", 10.0),
            "Aura15" => ("#00F5D4", "", 15.0),
            _ => ("#00E5FF", "", 0.0)
        };

        NewTokenKind = TokenMarkerKind.ConditionRing;
        NewTokenColorHex = color;
        NewTokenLabel = label;
        NewTokenAuraFeet = aura;

        if (_selectedToken is not null)
        {
            _selectedToken.ColorHex = color;
            _selectedToken.Label = label;
            _selectedToken.AuraRadiusFeet = aura;
            HasUnsavedChanges = true;
            RequestViewportRepaint();
        }
    }

    #endregion

    #region Scenes & Stage-and-Push

    public async Task AddScenesFromFilesAsync(IEnumerable<string> filePaths)
    {
        SceneViewModel? firstAdded = null;

        foreach (string path in filePaths)
        {
            if (!File.Exists(path)) continue;

            string ext = Path.GetExtension(path).ToLowerInvariant();
            SceneMediaType mediaType = ext switch
            {
                ".uvtt" or ".dd2vtt" or ".df2vtt" => SceneMediaType.UvttBundle,
                ".mp4" or ".webm" or ".mkv" or ".avi" or ".mov" => SceneMediaType.VideoMap,
                _ => SceneMediaType.StaticImage
            };

            SceneViewModel svm;
            if (mediaType == SceneMediaType.UvttBundle)
            {
                StatusMessage = $"Importing Universal VTT map '{Path.GetFileName(path)}'...";
                var uvtt = await UvttImportService.ImportAsync(path);
                svm = AttachSceneViewModel(uvtt.Scene, uvtt.LoadedAsset);
            }
            else
            {
                var sceneModel = new MapScene
                {
                    Name = Path.GetFileNameWithoutExtension(path),
                    MediaFilePath = path,
                    MediaType = mediaType
                };
                svm = AttachSceneViewModel(sceneModel);
                _ = svm.LoadThumbnailOnlyAsync();
            }

            _campaign.Scenes.Add(svm.Model);
            Scenes.Add(svm);
            firstAdded ??= svm;
            HasUnsavedChanges = true;
        }

        if (firstAdded is not null)
        {
            await SelectAndStageSceneAsync(firstAdded, pushIfFirstOrAuto: LiveTvScene is null || AutoPushSceneOnSelect);
            StatusMessage = $"Loaded '{firstAdded.Name}' ({firstAdded.DimensionsBadge}).";
        }
    }

    public async Task SelectAndStageSceneAsync(SceneViewModel scene, bool pushIfFirstOrAuto = false)
    {
        StatusMessage = $"Loading '{scene.Name}'...";
        await scene.EnsureHydratedAsync();

        StagedScene = scene;
        _campaign.ActiveStagedSceneId = scene.Id;
        RequestFitDmCamera?.Invoke(this, EventArgs.Empty);

        if (pushIfFirstOrAuto || AutoPushSceneOnSelect)
        {
            await PushStagedSceneToTvAsync();
        }
        else
        {
            StatusMessage = $"Staged '{scene.Name}' privately on DM screen. Click 'Push to TV' when ready.";
        }
    }

    public async Task PushStagedSceneToTvAsync()
    {
        if (_stagedScene is null) return;
        await _stagedScene.EnsureHydratedAsync();

        LiveTvScene = _stagedScene;
        _campaign.ActiveTvSceneId = _stagedScene.Id;

        if (TvProfile.SyncMode == TvSyncMode.IndependentTv)
        {
            TvCamera = ViewportMath.FitMapToViewport(
                _stagedScene.PixelWidth,
                _stagedScene.PixelHeight,
                _tvViewportSize,
                TvProfile.RotationAngle,
                0.98);
        }
        else
        {
            UpdateTvCameraSync();
        }

        StatusMessage = _isPlayerWindowOpen
            ? $"LIVE on TV: '{_stagedScene.Name}'."
            : $"Queued '{_stagedScene.Name}' for TV (Player TV is OFF — press F12 or click 'TV Off' when ready).";
    }

    public async Task DuplicateSceneAsync(SceneViewModel source)
    {
        source.PersistRuntimeStateToModel();
        string raw = JsonSerializer.Serialize(source.Model);
        var clonedModel = JsonSerializer.Deserialize<MapScene>(raw)!;
        clonedModel.Id = Guid.NewGuid();
        clonedModel.Name = $"{source.Name} (Copy)";

        var svm = AttachSceneViewModel(clonedModel);
        await svm.LoadThumbnailOnlyAsync();

        int idx = Scenes.IndexOf(source);
        int insertAt = idx >= 0 ? idx + 1 : Scenes.Count;
        Scenes.Insert(insertAt, svm);
        _campaign.Scenes.Insert(insertAt, clonedModel);
        HasUnsavedChanges = true;
        StatusMessage = $"Duplicated scene '{clonedModel.Name}'.";
    }

    public void MoveScene(SceneViewModel scene, int direction)
    {
        int oldIdx = Scenes.IndexOf(scene);
        if (oldIdx < 0) return;
        int newIdx = Math.Clamp(oldIdx + direction, 0, Scenes.Count - 1);
        if (newIdx == oldIdx) return;

        Scenes.Move(oldIdx, newIdx);
        _campaign.Scenes.RemoveAt(oldIdx);
        _campaign.Scenes.Insert(newIdx, scene.Model);
        HasUnsavedChanges = true;
    }

    public void RemoveScene(SceneViewModel scene)
    {
        int idx = Scenes.IndexOf(scene);
        if (idx < 0) return;

        Scenes.RemoveAt(idx);
        _campaign.Scenes.Remove(scene.Model);
        scene.Dispose();
        HasUnsavedChanges = true;

        if (ReferenceEquals(LiveTvScene, scene))
        {
            LiveTvScene = Scenes.FirstOrDefault();
        }

        if (ReferenceEquals(StagedScene, scene))
        {
            var next = Scenes.Count > 0 ? Scenes[Math.Clamp(idx, 0, Scenes.Count - 1)] : null;
            if (next is not null)
                _ = SelectAndStageSceneAsync(next, pushIfFirstOrAuto: false);
            else
                StagedScene = null;
        }
    }

    #endregion

    #region TV Sync & Centering

    public void CenterTvAtMapPoint(Point mapPt)
    {
        var scene = _stagedScene;
        if (scene is null) return;

        // If currently mirroring DM, switch cleanly to Independent or keep PhysicalOneInch so centering TV holds!
        if (TvProfile.SyncMode == TvSyncMode.MirrorDm)
        {
            TvProfile.SyncMode = TvSyncMode.IndependentTv;
        }

        if (!ReferenceEquals(scene, _liveTvScene))
        {
            scene.Model.TvCenterX = Math.Clamp(mapPt.X, 0, scene.PixelWidth);
            scene.Model.TvCenterY = Math.Clamp(mapPt.Y, 0, scene.PixelHeight);
            StatusMessage = "Updated staged TV target position.";
            return;
        }

        TvCamera = TvCamera with
        {
            CenterMapX = Math.Clamp(mapPt.X, 0, scene.PixelWidth),
            CenterMapY = Math.Clamp(mapPt.Y, 0, scene.PixelHeight)
        };
        HasUnsavedChanges = true;
        StatusMessage = $"Centered TV view at ({mapPt.X:0}, {mapPt.Y:0}).";
    }

    public void UpdateTvCameraSync()
    {
        var tvScene = _liveTvScene;
        if (tvScene is null) return;

        double rot = TvProfile.RotationAngle;

        switch (TvProfile.SyncMode)
        {
            case TvSyncMode.PhysicalOneInch:
            {
                uint effDpi = _selectedTvMonitor?.EffectiveDpiX ?? 96;
                double zoom = DisplayCalibrationService.CalculateOneInchZoom(
                    EffectiveTvPhysicalPpi,
                    effDpi,
                    tvScene.Grid.PixelsPerCell);

                double cx = tvScene.Model.TvCenterX > 0 ? tvScene.Model.TvCenterX : tvScene.PixelWidth * 0.5;
                double cy = tvScene.Model.TvCenterY > 0 ? tvScene.Model.TvCenterY : tvScene.PixelHeight * 0.5;
                TvCamera = new CameraState(cx, cy, zoom, rot);
                break;
            }
            case TvSyncMode.MirrorDm:
            {
                if (ReferenceEquals(_stagedScene, _liveTvScene) && _dmViewportSize.Width > 10 && _tvViewportSize.Width > 10)
                {
                    bool swapped = Math.Abs(rot - 90) < 1 || Math.Abs(rot - 270) < 1;
                    double effTvW = swapped ? _tvViewportSize.Height : _tvViewportSize.Width;
                    double effTvH = swapped ? _tvViewportSize.Width : _tvViewportSize.Height;

                    double ratio = Math.Min(effTvW / _dmViewportSize.Width, effTvH / _dmViewportSize.Height);
                    double mirroredZoom = Math.Clamp(_dmCamera.Zoom * ratio, ViewportMath.MinZoom, ViewportMath.MaxZoom);
                    TvCamera = new CameraState(_dmCamera.CenterMapX, _dmCamera.CenterMapY, mirroredZoom, rot);
                }
                else
                {
                    TvCamera = _tvCamera with { RotationDegrees = rot };
                }
                break;
            }
            case TvSyncMode.IndependentTv:
            {
                double zoom = tvScene.Model.TvZoom > 0.01 ? tvScene.Model.TvZoom : _tvCamera.Zoom;
                double cx = tvScene.Model.TvCenterX > 0 ? tvScene.Model.TvCenterX : tvScene.PixelWidth * 0.5;
                double cy = tvScene.Model.TvCenterY > 0 ? tvScene.Model.TvCenterY : tvScene.PixelHeight * 0.5;
                TvCamera = new CameraState(cx, cy, zoom, rot);
                break;
            }
        }
    }

    public void ApplyOnTvRulerCalibration()
    {
        uint effDpi = _selectedTvMonitor?.EffectiveDpiX ?? 96;
        double dpiScale = effDpi / 96.0;
        TvProfile.ManualPhysicalPpi = Math.Clamp(_calibrationRulerDips * dpiScale, 20.0, 600.0);
        IsTvCalibrationRulerVisible = false;
        SaveDefaultTvProfile();
        UpdateTvCameraSync();
        OnPropertyChanged(nameof(ComputedPhysicalPpiDisplay));
        StatusMessage = $"Calibrated 1-inch physical TV scale to {TvProfile.ManualPhysicalPpi:0.1} PPI.";
    }

    public void ResetToDiagonalInchesCalibration()
    {
        TvProfile.ManualPhysicalPpi = 0;
        SaveDefaultTvProfile();
        UpdateTvCameraSync();
        OnPropertyChanged(nameof(ComputedPhysicalPpiDisplay));
    }

    public void SetStandbyImage(string? imagePath)
    {
        TvProfile.StandbyImagePath = imagePath;
        ReloadStandbyArtwork();
        SaveDefaultTvProfile();
        HasUnsavedChanges = true;
    }

    #endregion

    #region Pre-Staged Handouts Gallery & Projection

    public async Task AddHandoutsFromFilesAsync(IEnumerable<string> filePaths)
    {
        foreach (string path in filePaths)
        {
            if (!File.Exists(path)) continue;
            var item = new HandoutItemModel
            {
                Title = Path.GetFileNameWithoutExtension(path),
                ImageFilePath = path,
                Thumbnail = await Task.Run(() => MapAssetService.LoadSimpleFrozenBitmap(path, 256))
            };
            _campaign.Handouts.Add(item);
            Handouts.Add(item);
            HasUnsavedChanges = true;
        }
        StatusMessage = $"Added handouts to gallery ({Handouts.Count} total).";
    }

    public void ProjectHandoutItem(HandoutItemModel item)
    {
        if (_liveHandoutItem is not null)
            _liveHandoutItem.IsLiveOnTv = false;

        var bmp = MapAssetService.LoadSimpleFrozenBitmap(item.ImageFilePath, 3840);
        if (bmp is not null)
        {
            _liveHandoutItem = item;
            item.IsLiveOnTv = true;
            ActiveHandoutTitle = item.Title;
            ActiveHandoutBitmap = bmp;
            StatusMessage = _isPlayerWindowOpen
                ? $"Projecting handout '{item.Title}' on TV."
                : $"Queued handout '{item.Title}' (Player TV is OFF — press F12 to display).";
        }
    }

    public void ShowAdHocHandoutOnTv(string imagePath)
    {
        if (_liveHandoutItem is not null)
        {
            _liveHandoutItem.IsLiveOnTv = false;
            _liveHandoutItem = null;
        }

        var bmp = MapAssetService.LoadSimpleFrozenBitmap(imagePath, 3840);
        if (bmp is not null)
        {
            ActiveHandoutTitle = Path.GetFileNameWithoutExtension(imagePath);
            ActiveHandoutBitmap = bmp;
            StatusMessage = _isPlayerWindowOpen
                ? $"Showing handout '{ActiveHandoutTitle}' on TV."
                : $"Queued handout '{ActiveHandoutTitle}' (Player TV is OFF — press F12 to display).";
        }
    }

    public void RemoveHandout(HandoutItemModel item)
    {
        if (ReferenceEquals(_liveHandoutItem, item))
            DismissHandout();

        Handouts.Remove(item);
        _campaign.Handouts.Remove(item);
        HasUnsavedChanges = true;
    }

    public void DismissHandout()
    {
        if (_liveHandoutItem is not null)
        {
            _liveHandoutItem.IsLiveOnTv = false;
            _liveHandoutItem = null;
        }
        ActiveHandoutBitmap = null;
        ActiveHandoutTitle = "";
        StatusMessage = "Dismissed TV handout.";
    }

    #endregion

    #region Unified Undo/Redo, Fog, & Tactical Layer Operations

    public void TriggerPing(Point mapPoint)
    {
        Color c = GridRenderer.ParseColorOrDefault(PencilColorHex, Colors.Cyan);
        _activePings.RemoveAll(p => (DateTime.UtcNow - p.StartedUtc).TotalSeconds > p.DurationSeconds);
        _activePings.Add(new ActivePingAnimation(mapPoint, c, DateTime.UtcNow));
        RequestViewportRepaint();
    }

    public void Undo()
    {
        if (_stagedScene?.Undo() == true)
        {
            HasUnsavedChanges = true;
            NotifyUndoRedoChanged();
            RequestViewportRepaint();
        }
    }

    public void Redo()
    {
        if (_stagedScene?.Redo() == true)
        {
            HasUnsavedChanges = true;
            NotifyUndoRedoChanged();
            RequestViewportRepaint();
        }
    }

    public void RevealAllFog()
    {
        if (_stagedScene?.FogEngine is null) return;
        var scene = _stagedScene;
        var prevRoomStates = scene.Rooms.ToDictionary(r => r, r => r.IsRevealed);

        scene.FogEngine.RevealAll();
        foreach (var r in scene.Rooms) r.IsRevealed = true;

        scene.RecordCustomHistoryStep(
            "Reveal All Fog",
            undoAction: () =>
            {
                scene.FogEngine?.Undo();
                foreach (var kv in prevRoomStates) kv.Key.IsRevealed = kv.Value;
            },
            redoAction: () =>
            {
                scene.FogEngine?.Redo();
                foreach (var kv in prevRoomStates) kv.Key.IsRevealed = true;
            });

        HasUnsavedChanges = true;
        NotifyUndoRedoChanged();
        RequestViewportRepaint();
    }

    public void HideAllFog()
    {
        if (_stagedScene?.FogEngine is null) return;
        var scene = _stagedScene;
        var prevRoomStates = scene.Rooms.ToDictionary(r => r, r => r.IsRevealed);

        scene.FogEngine.HideAll();
        foreach (var r in scene.Rooms) r.IsRevealed = false;

        scene.RecordCustomHistoryStep(
            "Hide All Fog",
            undoAction: () =>
            {
                scene.FogEngine?.Undo();
                foreach (var kv in prevRoomStates) kv.Key.IsRevealed = kv.Value;
            },
            redoAction: () =>
            {
                scene.FogEngine?.Redo();
                foreach (var kv in prevRoomStates) kv.Key.IsRevealed = false;
            });

        HasUnsavedChanges = true;
        NotifyUndoRedoChanged();
        RequestViewportRepaint();
    }

    public void AddPencilStrokeWithHistory(PencilStrokeModel stroke)
    {
        var scene = _stagedScene;
        if (scene is null) return;

        scene.PencilStrokes.Add(stroke);
        scene.RecordCustomHistoryStep(
            "Pencil Sketch",
            undoAction: () => scene.PencilStrokes.Remove(stroke),
            redoAction: () => scene.PencilStrokes.Add(stroke));

        HasUnsavedChanges = true;
        RequestViewportRepaint();
    }

    public void ClearPencilDrawings()
    {
        var scene = _stagedScene;
        if (scene is null || scene.PencilStrokes.Count == 0) return;

        var snapshot = scene.PencilStrokes.ToList();
        scene.PencilStrokes.Clear();
        scene.RecordCustomHistoryStep(
            "Clear Sketches",
            undoAction: () => { foreach (var s in snapshot) scene.PencilStrokes.Add(s); },
            redoAction: () => scene.PencilStrokes.Clear());

        HasUnsavedChanges = true;
        RequestViewportRepaint();
        StatusMessage = "Cleared pencil sketches (Ctrl+Z to undo).";
    }

    public void ClearAllAoes()
    {
        var scene = _stagedScene;
        if (scene is null || scene.AoeTemplates.Count == 0) return;

        var snapshot = scene.AoeTemplates.ToList();
        SelectedAoeTemplate = null;
        scene.AoeTemplates.Clear();

        scene.RecordCustomHistoryStep(
            "Clear All Spell AoEs",
            undoAction: () => { foreach (var a in snapshot) scene.AoeTemplates.Add(a); },
            redoAction: () => { SelectedAoeTemplate = null; scene.AoeTemplates.Clear(); });

        HasUnsavedChanges = true;
        RequestViewportRepaint();
        StatusMessage = $"Cleared {snapshot.Count} spell templates (Ctrl+Z to undo).";
    }

    public void ClearAllTokens()
    {
        var scene = _stagedScene;
        if (scene is null || scene.Tokens.Count == 0) return;

        var snapshot = scene.Tokens.ToList();
        SelectedToken = null;
        scene.Tokens.Clear();

        scene.RecordCustomHistoryStep(
            "Clear All Rings & Tokens",
            undoAction: () => { foreach (var t in snapshot) scene.Tokens.Add(t); },
            redoAction: () => { SelectedToken = null; scene.Tokens.Clear(); });

        HasUnsavedChanges = true;
        RequestViewportRepaint();
        StatusMessage = $"Cleared {snapshot.Count} rings/tokens (Ctrl+Z to undo).";
    }

    public void ClearAllRooms()
    {
        var scene = _stagedScene;
        if (scene is null || scene.Rooms.Count == 0) return;

        var snapshot = scene.Rooms.ToList();
        scene.Rooms.Clear();

        scene.RecordCustomHistoryStep(
            "Clear Fog Rooms",
            undoAction: () => { foreach (var r in snapshot) scene.Rooms.Add(r); },
            redoAction: () => scene.Rooms.Clear());

        HasUnsavedChanges = true;
        RequestViewportRepaint();
    }

    public void ClearAllDmPins()
    {
        var scene = _stagedScene;
        if (scene is null || scene.DmPins.Count == 0) return;

        var snapshot = scene.DmPins.ToList();
        SelectedDmPin = null;
        scene.DmPins.Clear();

        scene.RecordCustomHistoryStep(
            "Clear DM Pins",
            undoAction: () => { foreach (var p in snapshot) scene.DmPins.Add(p); },
            redoAction: () => { SelectedDmPin = null; scene.DmPins.Clear(); });

        HasUnsavedChanges = true;
        RequestViewportRepaint();
    }

    public TokenMarkerModel PlaceNewTokenAt(Point mapPoint)
    {
        var scene = _stagedScene ?? throw new InvalidOperationException("No active staged scene.");

        Point pos = _snapTokensToGrid ? SnapPointToCellCenter(mapPoint, scene.Grid, _newTokenSizeCells) : mapPoint;
        string label = _newTokenKind == TokenMarkerKind.NumberedToken
            ? (string.IsNullOrWhiteSpace(_newTokenLabel) ? $"M{_monsterAutoNumber++}" : _newTokenLabel)
            : _newTokenLabel;

        var token = new TokenMarkerModel
        {
            Kind = _newTokenKind,
            MapX = pos.X,
            MapY = pos.Y,
            SizeCells = _newTokenSizeCells,
            AuraRadiusFeet = _newTokenAuraFeet,
            Label = label,
            ColorHex = _newTokenColorHex,
            VisibleToPlayers = true
        };

        token.PropertyChanged += (_, _) => { HasUnsavedChanges = true; RequestViewportRepaint(); };
        scene.Tokens.Add(token);
        scene.RecordCustomHistoryStep(
            "Add Ring/Token",
            undoAction: () => { if (ReferenceEquals(SelectedToken, token)) SelectedToken = null; scene.Tokens.Remove(token); },
            redoAction: () => scene.Tokens.Add(token));

        SelectedToken = token;
        HasUnsavedChanges = true;
        RequestViewportRepaint();
        return token;
    }

    public AoeTemplateModel PlaceNewAoeAt(Point mapPoint)
    {
        var scene = _stagedScene ?? throw new InvalidOperationException("No active staged scene.");

        var aoe = new AoeTemplateModel
        {
            Shape = _newAoeShape,
            OriginX = mapPoint.X,
            OriginY = mapPoint.Y,
            SizeFeet = _newAoeSizeFeet,
            WidthFeet = _newAoeWidthFeet,
            DirectionDegrees = 0,
            ColorHex = _newAoeColorHex,
            Label = $"{_newAoeSizeFeet:0}ft {_newAoeShape}"
        };

        aoe.PropertyChanged += (_, _) => { HasUnsavedChanges = true; RequestViewportRepaint(); };
        scene.AoeTemplates.Add(aoe);
        scene.RecordCustomHistoryStep(
            "Add Spell AoE",
            undoAction: () => { if (ReferenceEquals(SelectedAoeTemplate, aoe)) SelectedAoeTemplate = null; scene.AoeTemplates.Remove(aoe); },
            redoAction: () => scene.AoeTemplates.Add(aoe));

        SelectedAoeTemplate = aoe;
        HasUnsavedChanges = true;
        RequestViewportRepaint();
        return aoe;
    }

    public DmPinModel PlaceNewDmPinAt(Point mapPoint)
    {
        var scene = _stagedScene ?? throw new InvalidOperationException("No active staged scene.");

        int num = scene.DmPins.Count + 1;
        var pin = new DmPinModel
        {
            MapX = mapPoint.X,
            MapY = mapPoint.Y,
            Code = $"P{num}",
            Title = $"Location {num}",
            Notes = ""
        };

        pin.PropertyChanged += (_, _) => { HasUnsavedChanges = true; RequestViewportRepaint(); };
        scene.DmPins.Add(pin);
        scene.RecordCustomHistoryStep(
            "Add DM Pin",
            undoAction: () => { if (ReferenceEquals(SelectedDmPin, pin)) SelectedDmPin = null; scene.DmPins.Remove(pin); },
            redoAction: () => scene.DmPins.Add(pin));

        SelectedDmPin = pin;
        HasUnsavedChanges = true;
        RequestViewportRepaint();
        return pin;
    }

    public void DeleteToken(TokenMarkerModel tok)
    {
        var scene = _stagedScene;
        if (scene is null || !scene.Tokens.Contains(tok)) return;
        if (ReferenceEquals(SelectedToken, tok)) SelectedToken = null;
        scene.Tokens.Remove(tok);
        scene.RecordCustomHistoryStep("Delete Token", () => scene.Tokens.Add(tok), () => scene.Tokens.Remove(tok));
        HasUnsavedChanges = true;
        RequestViewportRepaint();
    }

    public void DeleteAoe(AoeTemplateModel aoe)
    {
        var scene = _stagedScene;
        if (scene is null || !scene.AoeTemplates.Contains(aoe)) return;
        if (ReferenceEquals(SelectedAoeTemplate, aoe)) SelectedAoeTemplate = null;
        scene.AoeTemplates.Remove(aoe);
        scene.RecordCustomHistoryStep("Delete Spell AoE", () => scene.AoeTemplates.Add(aoe), () => scene.AoeTemplates.Remove(aoe));
        HasUnsavedChanges = true;
        RequestViewportRepaint();
    }

    public void DeleteDmPin(DmPinModel pin)
    {
        var scene = _stagedScene;
        if (scene is null || !scene.DmPins.Contains(pin)) return;
        if (ReferenceEquals(SelectedDmPin, pin)) SelectedDmPin = null;
        scene.DmPins.Remove(pin);
        scene.RecordCustomHistoryStep("Delete DM Pin", () => scene.DmPins.Add(pin), () => scene.DmPins.Remove(pin));
        HasUnsavedChanges = true;
        RequestViewportRepaint();
    }

    public void DeleteRoom(FogRoomRegion room)
    {
        var scene = _stagedScene;
        if (scene is null || !scene.Rooms.Contains(room)) return;
        scene.Rooms.Remove(room);
        scene.RecordCustomHistoryStep("Delete Room", () => scene.Rooms.Add(room), () => scene.Rooms.Remove(room));
        HasUnsavedChanges = true;
        RequestViewportRepaint();
    }

    public void DeleteSelectedElement()
    {
        if (_selectedToken is not null) DeleteToken(_selectedToken);
        else if (_selectedAoeTemplate is not null) DeleteAoe(_selectedAoeTemplate);
        else if (_selectedDmPin is not null) DeleteDmPin(_selectedDmPin);
    }

    public Point SnapPointToCellCenter(Point mapPt, GridSettings grid, double tokenSizeCells = 1.0)
    {
        if (grid.GridType != GridType.Square) return mapPt;
        double step = Math.Max(8.0, grid.PixelsPerCell);
        double ox = ((grid.OffsetX % step) + step) % step;
        double oy = ((grid.OffsetY % step) + step) % step;

        bool isEvenSize = Math.Abs((tokenSizeCells % 2.0) - 0.0) < 0.1;
        double halfShift = isEvenSize ? 0.0 : step * 0.5;

        double sx = Math.Round((mapPt.X - ox - halfShift) / step) * step + ox + halfShift;
        double sy = Math.Round((mapPt.Y - oy - halfShift) / step) * step + oy + halfShift;
        return new Point(sx, sy);
    }

    #endregion

    #region Campaign Persistence

    public async Task SaveCampaignAsync(string filePath)
    {
        foreach (var s in Scenes)
            s.PersistRuntimeStateToModel();

        _campaign.DmFogOpacity = _dmFogOpacity;
        _campaign.ActiveStagedSceneId = _stagedScene?.Id;
        _campaign.ActiveTvSceneId = _liveTvScene?.Id;

        await CampaignStorageService.SaveCampaignAsync(_campaign, filePath);
        CampaignStorageService.RecordRecentCampaign(filePath);
        RefreshRecentCampaigns();
        CurrentCampaignFilePath = filePath;
        HasUnsavedChanges = false;
        StatusMessage = $"Saved campaign to '{Path.GetFileName(filePath)}'.";
    }

    public async Task LoadCampaignAsync(string filePath)
    {
        var loaded = await CampaignStorageService.LoadCampaignAsync(filePath);

        foreach (var old in Scenes) old.Dispose();
        Scenes.Clear();
        Handouts.Clear();
        DismissHandout();
        StagedScene = null;
        LiveTvScene = null;

        _campaign = loaded;
        _dmFogOpacity = loaded.DmFogOpacity;
        HookTvProfileEvents();
        RefreshMonitors();
        ReloadStandbyArtwork();

        foreach (var model in _campaign.Scenes)
        {
            var svm = AttachSceneViewModel(model);
            Scenes.Add(svm);
            _ = svm.LoadThumbnailOnlyAsync();
        }

        foreach (var h in _campaign.Handouts)
        {
            Handouts.Add(h);
            _ = Task.Run(() =>
            {
                var thumb = MapAssetService.LoadSimpleFrozenBitmap(h.ImageFilePath, 256);
                Application.Current?.Dispatcher.InvokeAsync(() => h.Thumbnail = thumb);
            });
        }

        var toStage = Scenes.FirstOrDefault(s => s.Id == _campaign.ActiveStagedSceneId) ?? Scenes.FirstOrDefault();
        var toLive = Scenes.FirstOrDefault(s => s.Id == _campaign.ActiveTvSceneId) ?? toStage;

        if (toLive is not null)
        {
            await toLive.EnsureHydratedAsync();
            LiveTvScene = toLive;
        }

        if (toStage is not null)
        {
            await SelectAndStageSceneAsync(toStage, pushIfFirstOrAuto: false);
        }

        CampaignStorageService.RecordRecentCampaign(filePath);
        RefreshRecentCampaigns();
        CurrentCampaignFilePath = filePath;
        HasUnsavedChanges = false;

        OnPropertyChanged(nameof(CampaignName));
        OnPropertyChanged(nameof(TvProfile));
        OnPropertyChanged(nameof(DmFogOpacity));
        OnPropertyChanged(nameof(AutoPushSceneOnSelect));
        OnPropertyChanged(nameof(ComputedPhysicalPpiDisplay));
        StatusMessage = $"Opened campaign '{CampaignName}' ({Scenes.Count} scenes, {Handouts.Count} handouts).";
    }

    public void NewCampaign()
    {
        foreach (var old in Scenes) old.Dispose();
        Scenes.Clear();
        Handouts.Clear();
        DismissHandout();
        StagedScene = null;
        LiveTvScene = null;

        var savedTv = TvProfile;
        _campaign = new CampaignProject { TvProfile = savedTv };
        CurrentCampaignFilePath = null;
        HasUnsavedChanges = false;
        OnPropertyChanged(nameof(CampaignName));
        OnPropertyChanged(nameof(WindowTitle));
        StatusMessage = "Started new campaign workspace.";
    }

    #endregion

    public void NotifyUndoRedoChanged()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    public void RequestViewportRepaint() => ViewportRepaintRequested?.Invoke(this, EventArgs.Empty);

    private SceneViewModel AttachSceneViewModel(MapScene model, LoadedMapAsset? preloaded = null)
    {
        var svm = new SceneViewModel(model, preloaded)
        {
            IsTvPowerOn = _isPlayerWindowOpen
        };
        svm.VisualContentInvalidated += (_, _) => RequestViewportRepaint();
        foreach (var t in svm.Tokens) t.PropertyChanged += (_, _) => RequestViewportRepaint();
        foreach (var a in svm.AoeTemplates) a.PropertyChanged += (_, _) => RequestViewportRepaint();
        foreach (var p in svm.DmPins) p.PropertyChanged += (_, _) => RequestViewportRepaint();
        return svm;
    }

    private void OnSceneHistoryChanged(object? sender, EventArgs e) => NotifyUndoRedoChanged();

    private void CleanupInactiveScenes()
    {
        foreach (var s in Scenes)
            s.DehydrateIfInactive();
    }

    private void HookTvProfileEvents()
    {
        TvProfile.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(TvDisplayProfile.DiagonalInches)
                or nameof(TvDisplayProfile.ManualPhysicalPpi)
                or nameof(TvDisplayProfile.RotationAngle)
                or nameof(TvDisplayProfile.SyncMode))
            {
                if (e.PropertyName == nameof(TvDisplayProfile.DiagonalInches) && TvProfile.ManualPhysicalPpi > 0)
                {
                    TvProfile.ManualPhysicalPpi = 0;
                }
                UpdateTvCameraSync();
                SaveDefaultTvProfile();
                OnPropertyChanged(nameof(ComputedPhysicalPpiDisplay));
                RequestViewportRepaint();
            }
        };
    }

    private void ReloadStandbyArtwork()
    {
        StandbyArtworkBitmap = MapAssetService.LoadSimpleFrozenBitmap(TvProfile.StandbyImagePath, 3840);
        RequestViewportRepaint();
    }

    private void SaveDefaultTvProfile()
    {
        var settings = CampaignStorageService.LoadUserSettings();
        settings.DefaultTvProfile = TvProfile;
        CampaignStorageService.SaveUserSettings(settings);
    }

    public void Dispose()
    {
        foreach (var s in Scenes)
            s.Dispose();
    }
}
