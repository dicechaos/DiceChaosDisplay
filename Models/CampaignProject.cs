using System.Text.Json.Serialization;
using System.Windows.Media.Imaging;
using DiceChaosDisplay.ViewModels;

namespace DiceChaosDisplay.Models;

public sealed class MapScene : ObservableObject
{
    private Guid _id = Guid.NewGuid();
    private string _name = "Untitled Scene";
    private string _mediaFilePath = "";
    private SceneMediaType _mediaType = SceneMediaType.StaticImage;
    private int _pixelWidth;
    private int _pixelHeight;
    private GridSettings _grid = new();
    private string? _fogMaskPngBase64;
    private bool _isVideoMuted = true;
    private double _videoVolume = 0.5;
    private double _tvCenterX;
    private double _tvCenterY;
    private double _tvZoom = 1.0;

    public Guid Id { get => _id; set => SetProperty(ref _id, value); }
    public string Name { get => _name; set => SetProperty(ref _name, value); }
    public string MediaFilePath { get => _mediaFilePath; set => SetProperty(ref _mediaFilePath, value); }
    public SceneMediaType MediaType { get => _mediaType; set => SetProperty(ref _mediaType, value); }
    public int PixelWidth { get => _pixelWidth; set => SetProperty(ref _pixelWidth, value); }
    public int PixelHeight { get => _pixelHeight; set => SetProperty(ref _pixelHeight, value); }
    public GridSettings Grid { get => _grid; set => SetProperty(ref _grid, value); }

    /// <summary>
    /// PNG-compressed fog mask persisted inside .dicechaos (~15-40KB per scene).
    /// Null means fresh unrevealed fog (100% hidden).
    /// </summary>
    public string? FogMaskPngBase64 { get => _fogMaskPngBase64; set => SetProperty(ref _fogMaskPngBase64, value); }

    public bool IsVideoMuted { get => _isVideoMuted; set => SetProperty(ref _isVideoMuted, value); }
    public double VideoVolume { get => _videoVolume; set => SetProperty(ref _videoVolume, Math.Clamp(value, 0.0, 1.0)); }

    public double TvCenterX { get => _tvCenterX; set => SetProperty(ref _tvCenterX, value); }
    public double TvCenterY { get => _tvCenterY; set => SetProperty(ref _tvCenterY, value); }
    public double TvZoom { get => _tvZoom; set => SetProperty(ref _tvZoom, value); }

    public List<FogRoomRegion> Rooms { get; set; } = new();
    public List<TokenMarkerModel> Tokens { get; set; } = new();
    public List<AoeTemplateModel> AoeTemplates { get; set; } = new();
    public List<DmPinModel> DmPins { get; set; } = new();
    public List<PencilStrokeModel> PencilStrokes { get; set; } = new();

    public List<List<Point2D>> UvttWalls { get; set; } = new();
    public List<List<Point2D>> UvttPortals { get; set; } = new();
}

public sealed class HandoutItemModel : ObservableObject
{
    private Guid _id = Guid.NewGuid();
    private string _title = "Handout";
    private string _imageFilePath = "";
    private BitmapSource? _thumbnail;
    private bool _isLiveOnTv;

    public Guid Id { get => _id; set => SetProperty(ref _id, value); }
    public string Title { get => _title; set => SetProperty(ref _title, value); }
    public string ImageFilePath { get => _imageFilePath; set => SetProperty(ref _imageFilePath, value); }

    [JsonIgnore]
    public BitmapSource? Thumbnail { get => _thumbnail; set => SetProperty(ref _thumbnail, value); }

    [JsonIgnore]
    public bool IsLiveOnTv { get => _isLiveOnTv; set => SetProperty(ref _isLiveOnTv, value); }
}

public sealed class TvDisplayProfile : ObservableObject
{
    private string? _preferredMonitorDeviceName;
    private double _diagonalInches = 50.0;
    private double _manualPhysicalPpi;
    private double _rotationAngle;
    private TvSyncMode _syncMode = TvSyncMode.MirrorDm;
    private string? _standbyImagePath;
    private string _standbyTitle = "";

    public string? PreferredMonitorDeviceName
    {
        get => _preferredMonitorDeviceName;
        set => SetProperty(ref _preferredMonitorDeviceName, value);
    }

    public double DiagonalInches
    {
        get => _diagonalInches;
        set => SetProperty(ref _diagonalInches, Math.Clamp(value, 10.0, 150.0));
    }

    public double ManualPhysicalPpi
    {
        get => _manualPhysicalPpi;
        set => SetProperty(ref _manualPhysicalPpi, Math.Max(0.0, value));
    }

    public double RotationAngle
    {
        get => _rotationAngle;
        set => SetProperty(ref _rotationAngle, value);
    }

    public TvSyncMode SyncMode
    {
        get => _syncMode;
        set => SetProperty(ref _syncMode, value);
    }

    public string? StandbyImagePath
    {
        get => _standbyImagePath;
        set => SetProperty(ref _standbyImagePath, value);
    }

    public string StandbyTitle
    {
        get => _standbyTitle;
        set => SetProperty(ref _standbyTitle, value);
    }
}

public sealed class CampaignProject
{
    public int SchemaVersion { get; set; } = 2;
    public string Name { get; set; } = "Untitled Campaign";
    public TvDisplayProfile TvProfile { get; set; } = new();
    public double DmFogOpacity { get; set; } = 0.55;
    public bool AutoPushSceneOnSelect { get; set; }
    public Guid? ActiveTvSceneId { get; set; }
    public Guid? ActiveStagedSceneId { get; set; }
    public List<MapScene> Scenes { get; set; } = new();
    public List<HandoutItemModel> Handouts { get; set; } = new();
}
