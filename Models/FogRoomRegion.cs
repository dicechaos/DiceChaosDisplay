using System.Text.Json.Serialization;
using DiceChaosDisplay.ViewModels;

namespace DiceChaosDisplay.Models;

public sealed class FogRoomRegion : ObservableObject
{
    private Guid _id = Guid.NewGuid();
    private string _name = "Room 1";
    private List<Point2D> _points = new();
    private bool _isRevealed;

    public Guid Id
    {
        get => _id;
        set => SetProperty(ref _id, value);
    }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public List<Point2D> Points
    {
        get => _points;
        set
        {
            if (SetProperty(ref _points, value))
                OnPropertyChanged(nameof(Centroid));
        }
    }

    public bool IsRevealed
    {
        get => _isRevealed;
        set => SetProperty(ref _isRevealed, value);
    }

    [JsonIgnore]
    public Point2D Centroid
    {
        get
        {
            if (_points.Count == 0) return new Point2D(0, 0);
            double sumX = 0, sumY = 0;
            foreach (var pt in _points)
            {
                sumX += pt.X;
                sumY += pt.Y;
            }
            return new Point2D(sumX / _points.Count, sumY / _points.Count);
        }
    }
}
