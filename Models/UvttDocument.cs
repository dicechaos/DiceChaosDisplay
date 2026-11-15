using System.Text.Json.Serialization;

namespace DiceChaosDisplay.Models;

public sealed class UvttDocument
{
    [JsonPropertyName("format")]
    public double Format { get; set; }

    [JsonPropertyName("resolution")]
    public UvttResolution? Resolution { get; set; }

    [JsonPropertyName("line_of_sight")]
    public List<List<UvttPoint>>? LineOfSight { get; set; }

    [JsonPropertyName("objects_line_of_sight")]
    public List<List<UvttPoint>>? ObjectsLineOfSight { get; set; }

    [JsonPropertyName("portals")]
    public List<UvttPortal>? Portals { get; set; }

    [JsonPropertyName("image")]
    public string? ImageBase64 { get; set; }
}

public sealed class UvttResolution
{
    [JsonPropertyName("map_origin")]
    public UvttPoint? MapOrigin { get; set; }

    [JsonPropertyName("map_size")]
    public UvttPoint? MapSize { get; set; }

    [JsonPropertyName("pixels_per_grid")]
    public double PixelsPerGrid { get; set; } = 100.0;
}

public sealed class UvttPoint
{
    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }
}

public sealed class UvttPortal
{
    [JsonPropertyName("position")]
    public UvttPoint? Position { get; set; }

    [JsonPropertyName("bounds")]
    public List<UvttPoint>? Bounds { get; set; }

    [JsonPropertyName("rotation")]
    public double Rotation { get; set; }

    [JsonPropertyName("closed")]
    public bool Closed { get; set; } = true;

    [JsonPropertyName("freestanding")]
    public bool Freestanding { get; set; }
}
