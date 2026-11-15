using DiceChaosDisplay.ViewModels;

namespace DiceChaosDisplay.Models;

public sealed class PencilStrokeModel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ColorHex { get; set; } = "#FF4757";
    public double Thickness { get; set; } = 6.0;
    public List<Point2D> Points { get; set; } = new();
}

public sealed class AoeTemplateModel : ObservableObject
{
    private Guid _id = Guid.NewGuid();
    private AoeShape _shape = AoeShape.Sphere;
    private double _originX;
    private double _originY;
    private double _sizeFeet = 20.0;
    private double _widthFeet = 5.0;
    private double _directionDegrees;
    private string _colorHex = "#FF6B35";
    private string _label = "20ft Sphere";
    private bool _visibleToPlayers = true;

    public Guid Id { get => _id; set => SetProperty(ref _id, value); }
    public AoeShape Shape { get => _shape; set => SetProperty(ref _shape, value); }
    public double OriginX { get => _originX; set => SetProperty(ref _originX, value); }
    public double OriginY { get => _originY; set => SetProperty(ref _originY, value); }
    public double SizeFeet { get => _sizeFeet; set => SetProperty(ref _sizeFeet, Math.Clamp(value, 5.0, 500.0)); }
    public double WidthFeet { get => _widthFeet; set => SetProperty(ref _widthFeet, Math.Clamp(value, 5.0, 100.0)); }
    public double DirectionDegrees { get => _directionDegrees; set => SetProperty(ref _directionDegrees, value); }
    public string ColorHex { get => _colorHex; set => SetProperty(ref _colorHex, value); }
    public string Label { get => _label; set => SetProperty(ref _label, value); }
    public bool VisibleToPlayers { get => _visibleToPlayers; set => SetProperty(ref _visibleToPlayers, value); }
}

public sealed class TokenMarkerModel : ObservableObject
{
    private Guid _id = Guid.NewGuid();
    private TokenMarkerKind _kind = TokenMarkerKind.ConditionRing;
    private double _mapX;
    private double _mapY;
    private double _sizeCells = 1.0;
    private double _auraRadiusFeet;
    private string _label = "";
    private string _colorHex = "#00E5FF";
    private bool _visibleToPlayers = true;

    public Guid Id { get => _id; set => SetProperty(ref _id, value); }
    public TokenMarkerKind Kind { get => _kind; set => SetProperty(ref _kind, value); }
    public double MapX { get => _mapX; set => SetProperty(ref _mapX, value); }
    public double MapY { get => _mapY; set => SetProperty(ref _mapY, value); }
    public double SizeCells { get => _sizeCells; set => SetProperty(ref _sizeCells, Math.Clamp(value, 0.5, 10.0)); }
    public double AuraRadiusFeet { get => _auraRadiusFeet; set => SetProperty(ref _auraRadiusFeet, Math.Clamp(value, 0.0, 120.0)); }
    public string Label { get => _label; set => SetProperty(ref _label, value); }
    public string ColorHex { get => _colorHex; set => SetProperty(ref _colorHex, value); }
    public bool VisibleToPlayers { get => _visibleToPlayers; set => SetProperty(ref _visibleToPlayers, value); }
}

public sealed class DmPinModel : ObservableObject
{
    private Guid _id = Guid.NewGuid();
    private double _mapX;
    private double _mapY;
    private string _code = "1";
    private string _title = "Room Note";
    private string _notes = "";
    private string _colorHex = "#FFD166";

    public Guid Id { get => _id; set => SetProperty(ref _id, value); }
    public double MapX { get => _mapX; set => SetProperty(ref _mapX, value); }
    public double MapY { get => _mapY; set => SetProperty(ref _mapY, value); }
    public string Code { get => _code; set => SetProperty(ref _code, value); }
    public string Title { get => _title; set => SetProperty(ref _title, value); }
    public string Notes { get => _notes; set => SetProperty(ref _notes, value); }
    public string ColorHex { get => _colorHex; set => SetProperty(ref _colorHex, value); }
}
