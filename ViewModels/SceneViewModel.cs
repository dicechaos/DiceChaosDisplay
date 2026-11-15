using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media.Imaging;
using DiceChaosDisplay.Models;
using DiceChaosDisplay.Services;

namespace DiceChaosDisplay.ViewModels;

public sealed record SceneHistoryEntry(
    bool IsFogStep,
    string Description,
    Action? UndoAction = null,
    Action? RedoAction = null);

public sealed class SceneViewModel : ObservableObject, IDisposable
{
    private BitmapSource? _thumbnail;
    private LoadedMapAsset? _loadedAsset;
    private VideoMapInstance? _videoInstance;
    private FogEngine? _fogEngine;
    private bool _isLoading;
    private bool _isStaged;
    private bool _isLiveOnTv;
    private bool _isTvPowerOn;
    private string? _loadError;

    private readonly Stack<SceneHistoryEntry> _undoHistory = new();
    private readonly Stack<SceneHistoryEntry> _redoHistory = new();

    public MapScene Model { get; }

    public ObservableCollection<FogRoomRegion> Rooms { get; }
    public ObservableCollection<TokenMarkerModel> Tokens { get; }
    public ObservableCollection<AoeTemplateModel> AoeTemplates { get; }
    public ObservableCollection<DmPinModel> DmPins { get; }
    public ObservableCollection<PencilStrokeModel> PencilStrokes { get; }

    public Guid Id => Model.Id;

    public string Name
    {
        get => Model.Name;
        set
        {
            if (Model.Name != value)
            {
                Model.Name = value;
                OnPropertyChanged();
            }
        }
    }

    public SceneMediaType MediaType => Model.MediaType;
    public GridSettings Grid => Model.Grid;

    public int PixelWidth => Model.PixelWidth > 0 ? Model.PixelWidth : (_loadedAsset?.PixelWidth ?? _videoInstance?.PixelWidth ?? 1920);
    public int PixelHeight => Model.PixelHeight > 0 ? Model.PixelHeight : (_loadedAsset?.PixelHeight ?? _videoInstance?.PixelHeight ?? 1080);

    public string DimensionsBadge => $"{PixelWidth}×{PixelHeight}" + (MediaType == SceneMediaType.VideoMap ? " • VIDEO" : MediaType == SceneMediaType.UvttBundle ? " • UVTT" : "");

    public BitmapSource? Thumbnail
    {
        get => _thumbnail;
        private set => SetProperty(ref _thumbnail, value);
    }

    public LoadedMapAsset? LoadedAsset
    {
        get => _loadedAsset;
        private set => SetProperty(ref _loadedAsset, value);
    }

    public VideoMapInstance? VideoInstance
    {
        get => _videoInstance;
        private set => SetProperty(ref _videoInstance, value);
    }

    public FogEngine? FogEngine
    {
        get => _fogEngine;
        private set => SetProperty(ref _fogEngine, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public bool IsStaged
    {
        get => _isStaged;
        set => SetProperty(ref _isStaged, value);
    }

    public bool IsLiveOnTv
    {
        get => _isLiveOnTv;
        set
        {
            if (SetProperty(ref _isLiveOnTv, value))
            {
                OnPropertyChanged(nameof(IsActivelyBroadcasting));
                OnPropertyChanged(nameof(IsQueuedWhileTvOff));
            }
        }
    }

    public bool IsTvPowerOn
    {
        get => _isTvPowerOn;
        set
        {
            if (SetProperty(ref _isTvPowerOn, value))
            {
                OnPropertyChanged(nameof(IsActivelyBroadcasting));
                OnPropertyChanged(nameof(IsQueuedWhileTvOff));
            }
        }
    }

    public bool IsActivelyBroadcasting => _isLiveOnTv && _isTvPowerOn;
    public bool IsQueuedWhileTvOff => _isLiveOnTv && !_isTvPowerOn;

    public string? LoadError
    {
        get => _loadError;
        private set => SetProperty(ref _loadError, value);
    }

    public bool CanUndo => _undoHistory.Count > 0;
    public bool CanRedo => _redoHistory.Count > 0;
    public bool IsHydrated => (_loadedAsset is not null || _videoInstance is not null) && _fogEngine is not null;

    public event EventHandler? VisualContentInvalidated;
    public event EventHandler? HistoryChanged;

    public SceneViewModel(MapScene model, LoadedMapAsset? preloadedAsset = null)
    {
        Model = model;
        Rooms = new ObservableCollection<FogRoomRegion>(model.Rooms);
        Tokens = new ObservableCollection<TokenMarkerModel>(model.Tokens);
        AoeTemplates = new ObservableCollection<AoeTemplateModel>(model.AoeTemplates);
        DmPins = new ObservableCollection<DmPinModel>(model.DmPins);
        PencilStrokes = new ObservableCollection<PencilStrokeModel>(model.PencilStrokes);

        Rooms.CollectionChanged += (_, _) => OnPropertyChanged(nameof(RoomsCountText));
        Tokens.CollectionChanged += (_, _) => OnPropertyChanged(nameof(TokensCountText));
        AoeTemplates.CollectionChanged += (_, _) => OnPropertyChanged(nameof(AoeCountText));
        DmPins.CollectionChanged += (_, _) => OnPropertyChanged(nameof(PinsCountText));
        PencilStrokes.CollectionChanged += (_, _) => OnPropertyChanged(nameof(SketchesCountText));

        Model.Grid.PropertyChanged += (_, _) => RaiseVisualInvalidated();

        if (preloadedAsset is not null)
        {
            ApplyLoadedAsset(preloadedAsset);
        }
    }

    public string RoomsCountText => $"{Rooms.Count}";
    public string TokensCountText => $"{Tokens.Count}";
    public string AoeCountText => $"{AoeTemplates.Count}";
    public string PinsCountText => $"{DmPins.Count}";
    public string SketchesCountText => $"{PencilStrokes.Count}";

    #region Unified Scene Undo / Redo

    public void RecordFogHistoryStep(string description = "Fog Edit")
    {
        _undoHistory.Push(new SceneHistoryEntry(IsFogStep: true, description));
        _redoHistory.Clear();
        RaiseHistoryChanged();
    }

    public void RecordCustomHistoryStep(string description, Action undoAction, Action redoAction)
    {
        _undoHistory.Push(new SceneHistoryEntry(IsFogStep: false, description, undoAction, redoAction));
        _redoHistory.Clear();
        RaiseHistoryChanged();
    }

    public bool Undo()
    {
        if (_undoHistory.Count == 0) return false;
        var entry = _undoHistory.Pop();

        if (entry.IsFogStep)
        {
            if (_fogEngine?.Undo() != true)
            {
                RaiseHistoryChanged();
                return false;
            }
        }
        else
        {
            entry.UndoAction?.Invoke();
        }

        _redoHistory.Push(entry);
        RaiseHistoryChanged();
        RaiseVisualInvalidated();
        return true;
    }

    public bool Redo()
    {
        if (_redoHistory.Count == 0) return false;
        var entry = _redoHistory.Pop();

        if (entry.IsFogStep)
        {
            if (_fogEngine?.Redo() != true)
            {
                RaiseHistoryChanged();
                return false;
            }
        }
        else
        {
            entry.RedoAction?.Invoke();
        }

        _undoHistory.Push(entry);
        RaiseHistoryChanged();
        RaiseVisualInvalidated();
        return true;
    }

    private void RaiseHistoryChanged()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    #endregion

    public async Task EnsureHydratedAsync(CancellationToken ct = default)
    {
        if (IsHydrated) return;

        IsLoading = true;
        LoadError = null;
        try
        {
            if (!File.Exists(Model.MediaFilePath))
            {
                throw new FileNotFoundException($"Map media file not found: {Model.MediaFilePath}");
            }

            switch (Model.MediaType)
            {
                case SceneMediaType.UvttBundle:
                {
                    var res = await UvttImportService.ImportAsync(Model.MediaFilePath, ct);
                    if (Model.UvttWalls.Count == 0 && res.Scene.UvttWalls.Count > 0)
                        Model.UvttWalls.AddRange(res.Scene.UvttWalls);
                    if (Model.UvttPortals.Count == 0 && res.Scene.UvttPortals.Count > 0)
                        Model.UvttPortals.AddRange(res.Scene.UvttPortals);
                    ApplyLoadedAsset(res.LoadedAsset);
                    break;
                }
                case SceneMediaType.VideoMap:
                {
                    var vid = new VideoMapInstance(Model.MediaFilePath);
                    vid.MediaReady += (_, _) =>
                    {
                        if (vid.Thumbnail is not null) Thumbnail = vid.Thumbnail;
                        Model.PixelWidth = vid.PixelWidth;
                        Model.PixelHeight = vid.PixelHeight;
                        if (_fogEngine is null || _fogEngine.MaskWidth <= 1)
                        {
                            InitializeFog(vid.PixelWidth, vid.PixelHeight);
                        }
                        OnPropertyChanged(nameof(PixelWidth));
                        OnPropertyChanged(nameof(PixelHeight));
                        OnPropertyChanged(nameof(DimensionsBadge));
                        RaiseVisualInvalidated();
                    };
                    VideoInstance = vid;
                    await vid.StartAsync(Model.IsVideoMuted, Model.VideoVolume);
                    Model.PixelWidth = vid.PixelWidth;
                    Model.PixelHeight = vid.PixelHeight;
                    if (Model.TvCenterX == 0 && Model.TvCenterY == 0)
                    {
                        Model.TvCenterX = vid.PixelWidth * 0.5;
                        Model.TvCenterY = vid.PixelHeight * 0.5;
                    }
                    InitializeFog(vid.PixelWidth, vid.PixelHeight);
                    break;
                }
                default:
                {
                    var asset = await MapAssetService.LoadFromFileAsync(Model.MediaFilePath, ct);
                    ApplyLoadedAsset(asset);
                    break;
                }
            }

            RaiseVisualInvalidated();
        }
        catch (Exception ex)
        {
            LoadError = ex.Message;
            if (_fogEngine is null)
            {
                InitializeFog(Math.Max(100, Model.PixelWidth), Math.Max(100, Model.PixelHeight));
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task LoadThumbnailOnlyAsync()
    {
        if (Thumbnail is not null || !File.Exists(Model.MediaFilePath)) return;
        try
        {
            if (Model.MediaType == SceneMediaType.StaticImage)
            {
                Thumbnail = await Task.Run(() => MapAssetService.LoadSimpleFrozenBitmap(Model.MediaFilePath, 256));
            }
            else if (Model.MediaType == SceneMediaType.UvttBundle)
            {
                var res = await UvttImportService.ImportAsync(Model.MediaFilePath);
                Thumbnail = res.LoadedAsset.Thumbnail;
                Model.PixelWidth = res.LoadedAsset.PixelWidth;
                Model.PixelHeight = res.LoadedAsset.PixelHeight;
                OnPropertyChanged(nameof(DimensionsBadge));
            }
        }
        catch { }
    }

    public void DehydrateIfInactive()
    {
        if (IsStaged || IsLiveOnTv) return;

        PersistRuntimeStateToModel();
        _undoHistory.Clear();
        _redoHistory.Clear();
        _videoInstance?.Dispose();
        VideoInstance = null;
        LoadedAsset = null;
        FogEngine = null;
        RaiseHistoryChanged();
    }

    public void PersistRuntimeStateToModel()
    {
        if (_fogEngine is not null)
        {
            Model.FogMaskPngBase64 = _fogEngine.ExportPngBase64();
        }

        Model.Rooms.Clear();
        Model.Rooms.AddRange(Rooms);

        Model.Tokens.Clear();
        Model.Tokens.AddRange(Tokens);

        Model.AoeTemplates.Clear();
        Model.AoeTemplates.AddRange(AoeTemplates);

        Model.DmPins.Clear();
        Model.DmPins.AddRange(DmPins);

        Model.PencilStrokes.Clear();
        Model.PencilStrokes.AddRange(PencilStrokes);
    }

    public void ToggleRoomReveal(FogRoomRegion room, double softness = 0.06)
    {
        if (_fogEngine is null) return;
        bool prev = room.IsRevealed;
        bool next = !prev;
        room.IsRevealed = next;

        _fogEngine.ApplyPolygon(
            room.Points,
            next ? FogPaintOperation.Reveal : FogPaintOperation.Hide,
            softness,
            recordUndo: true);

        _undoHistory.Push(new SceneHistoryEntry(
            IsFogStep: false,
            $"Toggle {room.Name}",
            UndoAction: () =>
            {
                room.IsRevealed = prev;
                _fogEngine?.Undo();
            },
            RedoAction: () =>
            {
                room.IsRevealed = next;
                _fogEngine?.Redo();
            }));
        _redoHistory.Clear();
        RaiseHistoryChanged();
        RaiseVisualInvalidated();
    }

    public void RaiseVisualInvalidated() => VisualContentInvalidated?.Invoke(this, EventArgs.Empty);

    private void ApplyLoadedAsset(LoadedMapAsset asset)
    {
        LoadedAsset = asset;
        Thumbnail = asset.Thumbnail;
        Model.PixelWidth = asset.PixelWidth;
        Model.PixelHeight = asset.PixelHeight;

        if (Model.TvCenterX == 0 && Model.TvCenterY == 0)
        {
            Model.TvCenterX = asset.PixelWidth * 0.5;
            Model.TvCenterY = asset.PixelHeight * 0.5;
        }

        InitializeFog(asset.PixelWidth, asset.PixelHeight);
        OnPropertyChanged(nameof(PixelWidth));
        OnPropertyChanged(nameof(PixelHeight));
        OnPropertyChanged(nameof(DimensionsBadge));
    }

    private void InitializeFog(int width, int height)
    {
        _fogEngine = new FogEngine(width, height, Model.FogMaskPngBase64);
        _undoHistory.Clear();
        _redoHistory.Clear();
        OnPropertyChanged(nameof(FogEngine));
        RaiseHistoryChanged();
    }

    public void Dispose()
    {
        _videoInstance?.Dispose();
        _videoInstance = null;
    }
}
