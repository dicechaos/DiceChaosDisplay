using DiceChaosDisplay.ViewModels;

namespace DiceChaosDisplay.Models;

public readonly record struct Point2D(double X, double Y);

public sealed class GridSettings : ObservableObject
{
    private bool _isVisible;
    private GridType _gridType = GridType.Square;
    private double _pixelsPerCell = 100.0;
    private double _offsetX;
    private double _offsetY;
    private string _colorHex = "#FFFFFF";
    private double _opacity = 0.35;
    private double _lineThickness = 1.5;
    private double _feetPerCell = 5.0;
    private DiagonalRule _diagonalRule = DiagonalRule.Standard5e_5_5_5;

    public bool IsVisible
    {
        get => _isVisible;
        set => SetProperty(ref _isVisible, value);
    }

    public GridType GridType
    {
        get => _gridType;
        set => SetProperty(ref _gridType, value);
    }

    public double PixelsPerCell
    {
        get => _pixelsPerCell;
        set => SetProperty(ref _pixelsPerCell, Math.Clamp(value, 8.0, 2000.0));
    }

    public double OffsetX
    {
        get => _offsetX;
        set => SetProperty(ref _offsetX, value);
    }

    public double OffsetY
    {
        get => _offsetY;
        set => SetProperty(ref _offsetY, value);
    }

    public string ColorHex
    {
        get => _colorHex;
        set => SetProperty(ref _colorHex, value);
    }

    public double Opacity
    {
        get => _opacity;
        set => SetProperty(ref _opacity, Math.Clamp(value, 0.05, 1.0));
    }

    public double LineThickness
    {
        get => _lineThickness;
        set => SetProperty(ref _lineThickness, Math.Clamp(value, 0.5, 8.0));
    }

    public double FeetPerCell
    {
        get => _feetPerCell;
        set => SetProperty(ref _feetPerCell, Math.Max(1.0, value));
    }

    public DiagonalRule DiagonalRule
    {
        get => _diagonalRule;
        set => SetProperty(ref _diagonalRule, value);
    }

    public GridSettings Clone() => new()
    {
        IsVisible = IsVisible,
        GridType = GridType,
        PixelsPerCell = PixelsPerCell,
        OffsetX = OffsetX,
        OffsetY = OffsetY,
        ColorHex = ColorHex,
        Opacity = Opacity,
        LineThickness = LineThickness,
        FeetPerCell = FeetPerCell,
        DiagonalRule = DiagonalRule
    };
}
