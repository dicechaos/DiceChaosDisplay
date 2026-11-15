using System.IO;
using System.Text.Json;
using DiceChaosDisplay.Models;

namespace DiceChaosDisplay.Services;

public sealed record UvttImportResult(
    MapScene Scene,
    LoadedMapAsset LoadedAsset);

public static class UvttImportService
{
    public static async Task<UvttImportResult> ImportAsync(string filePath, CancellationToken ct = default)
    {
        await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var doc = await JsonSerializer.DeserializeAsync<UvttDocument>(fs, cancellationToken: ct)
            ?? throw new InvalidDataException("Invalid Universal VTT (.uvtt / .dd2vtt) JSON file.");

        if (string.IsNullOrWhiteSpace(doc.ImageBase64))
            throw new InvalidDataException("UVTT file does not contain an embedded base64 map image.");

        // Strip optional data URI prefix if present
        string rawB64 = doc.ImageBase64;
        int commaIdx = rawB64.IndexOf(',');
        if (commaIdx >= 0)
            rawB64 = rawB64[(commaIdx + 1)..];

        byte[] imageBytes = Convert.FromBase64String(rawB64);
        LoadedMapAsset asset = await MapAssetService.LoadFromBytesAsync(imageBytes, ct);

        double ppg = doc.Resolution?.PixelsPerGrid > 1 ? doc.Resolution.PixelsPerGrid : 100.0;
        double originX = doc.Resolution?.MapOrigin?.X ?? 0.0;
        double originY = doc.Resolution?.MapOrigin?.Y ?? 0.0;

        var scene = new MapScene
        {
            Name = Path.GetFileNameWithoutExtension(filePath),
            MediaFilePath = filePath,
            MediaType = SceneMediaType.UvttBundle,
            PixelWidth = asset.PixelWidth,
            PixelHeight = asset.PixelHeight,
            TvCenterX = asset.PixelWidth * 0.5,
            TvCenterY = asset.PixelHeight * 0.5,
            Grid = new GridSettings
            {
                IsVisible = false, // UVTT maps often have baked-in grids, ready if toggled on
                GridType = GridType.Square,
                PixelsPerCell = ppg,
                OffsetX = (-originX * ppg) % ppg,
                OffsetY = (-originY * ppg) % ppg
            }
        };

        // Extract wall polylines in map pixel coordinates
        ExtractPolylines(doc.LineOfSight, originX, originY, ppg, scene.UvttWalls);
        ExtractPolylines(doc.ObjectsLineOfSight, originX, originY, ppg, scene.UvttWalls);

        // Extract doors / portals
        if (doc.Portals is not null)
        {
            foreach (var portal in doc.Portals)
            {
                if (portal.Bounds is { Count: >= 2 })
                {
                    var pts = portal.Bounds
                        .Select(p => new Point2D((p.X - originX) * ppg, (p.Y - originY) * ppg))
                        .ToList();
                    scene.UvttPortals.Add(pts);
                }
            }
        }

        return new UvttImportResult(scene, asset);
    }

    private static void ExtractPolylines(
        List<List<UvttPoint>>? source,
        double originX,
        double originY,
        double ppg,
        List<List<Point2D>> target)
    {
        if (source is null) return;
        foreach (var poly in source)
        {
            if (poly.Count < 2) continue;
            var converted = poly
                .Select(p => new Point2D((p.X - originX) * ppg, (p.Y - originY) * ppg))
                .ToList();
            target.Add(converted);
        }
    }
}
