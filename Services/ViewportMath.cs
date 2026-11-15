using System.Windows;
using System.Windows.Media;
using DiceChaosDisplay.Models;

namespace DiceChaosDisplay.Services;

public readonly record struct CameraState(
    double CenterMapX,
    double CenterMapY,
    double Zoom,
    double RotationDegrees = 0.0);

public static class ViewportMath
{
    public const double MinZoom = 0.02;
    public const double MaxZoom = 16.0;

    /// <summary>
    /// Builds the affine Matrix transforming Map Coordinates -> Viewport Screen Coordinates (DIPs).
    /// </summary>
    public static Matrix BuildMapToScreenMatrix(CameraState camera, Size viewportSize)
    {
        double zoom = Math.Clamp(camera.Zoom, MinZoom, MaxZoom);
        var m = Matrix.Identity;
        m.Translate(-camera.CenterMapX, -camera.CenterMapY);
        m.Scale(zoom, zoom);
        if (Math.Abs(camera.RotationDegrees) > 0.001)
        {
            m.Rotate(camera.RotationDegrees);
        }
        m.Translate(viewportSize.Width * 0.5, viewportSize.Height * 0.5);
        return m;
    }

    public static Point MapToScreen(Point mapPoint, CameraState camera, Size viewportSize)
    {
        var m = BuildMapToScreenMatrix(camera, viewportSize);
        return m.Transform(mapPoint);
    }

    public static Point ScreenToMap(Point screenPoint, CameraState camera, Size viewportSize)
    {
        var m = BuildMapToScreenMatrix(camera, viewportSize);
        if (!m.HasInverse) return new Point(camera.CenterMapX, camera.CenterMapY);
        m.Invert();
        return m.Transform(screenPoint);
    }

    /// <summary>
    /// Computes a new CameraState zoomed around a fixed screen anchor point (e.g., mouse cursor)
    /// so the map pixel underneath the cursor remains completely stationary.
    /// </summary>
    public static CameraState ZoomAroundScreenPoint(
        CameraState current,
        Size viewportSize,
        Point anchorScreenPoint,
        double zoomMultiplier)
    {
        Point mapAnchor = ScreenToMap(anchorScreenPoint, current, viewportSize);
        double newZoom = Math.Clamp(current.Zoom * zoomMultiplier, MinZoom, MaxZoom);

        // Compute where mapAnchor lands if only Zoom changes, then shift CenterMap by the map-space delta
        var tempCamera = current with { Zoom = newZoom };
        Point newAnchorMapAtSameScreen = ScreenToMap(anchorScreenPoint, tempCamera, viewportSize);

        double deltaMapX = mapAnchor.X - newAnchorMapAtSameScreen.X;
        double deltaMapY = mapAnchor.Y - newAnchorMapAtSameScreen.Y;

        return new CameraState(
            current.CenterMapX + deltaMapX,
            current.CenterMapY + deltaMapY,
            newZoom,
            current.RotationDegrees);
    }

    /// <summary>
    /// Pans the camera given a screen-space mouse drag delta (supports any rotation angle).
    /// </summary>
    public static CameraState PanByScreenDelta(
        CameraState current,
        Size viewportSize,
        Vector screenDelta)
    {
        Point screenCenter = new(viewportSize.Width * 0.5, viewportSize.Height * 0.5);
        Point targetScreen = screenCenter - screenDelta;
        Point newMapCenter = ScreenToMap(targetScreen, current, viewportSize);
        return current with { CenterMapX = newMapCenter.X, CenterMapY = newMapCenter.Y };
    }

    /// <summary>
    /// Computes a CameraState that fits the entire map inside the viewport with optional padding.
    /// </summary>
    public static CameraState FitMapToViewport(
        double mapWidth,
        double mapHeight,
        Size viewportSize,
        double rotationDegrees = 0.0,
        double paddingFraction = 0.94)
    {
        if (mapWidth <= 0 || mapHeight <= 0 || viewportSize.Width <= 1 || viewportSize.Height <= 1)
        {
            return new CameraState(mapWidth * 0.5, mapHeight * 0.5, 1.0, rotationDegrees);
        }

        double normalizedRot = ((rotationDegrees % 360) + 360) % 360;
        bool swapped = Math.Abs(normalizedRot - 90.0) < 1.0 || Math.Abs(normalizedRot - 270.0) < 1.0;

        double effMapW = swapped ? mapHeight : mapWidth;
        double effMapH = swapped ? mapWidth : mapHeight;

        double scaleX = (viewportSize.Width * paddingFraction) / effMapW;
        double scaleY = (viewportSize.Height * paddingFraction) / effMapH;
        double zoom = Math.Clamp(Math.Min(scaleX, scaleY), MinZoom, MaxZoom);

        return new CameraState(mapWidth * 0.5, mapHeight * 0.5, zoom, rotationDegrees);
    }

    /// <summary>
    /// Computes the Axis-Aligned Bounding Box (in Map Coordinates) of what the camera currently sees.
    /// Automatically accounts for 0/90/180/270 degree rotation!
    /// Used both for High-Res Tile Frustum Culling and for the DM's green TV Viewport Indicator.
    /// </summary>
    public static Rect GetVisibleMapBounds(CameraState camera, Size viewportSize)
    {
        if (viewportSize.Width <= 0 || viewportSize.Height <= 0)
            return new Rect(camera.CenterMapX, camera.CenterMapY, 0, 0);

        Point p0 = ScreenToMap(new Point(0, 0), camera, viewportSize);
        Point p1 = ScreenToMap(new Point(viewportSize.Width, 0), camera, viewportSize);
        Point p2 = ScreenToMap(new Point(viewportSize.Width, viewportSize.Height), camera, viewportSize);
        Point p3 = ScreenToMap(new Point(0, viewportSize.Height), camera, viewportSize);

        double minX = Math.Min(Math.Min(p0.X, p1.X), Math.Min(p2.X, p3.X));
        double minY = Math.Min(Math.Min(p0.Y, p1.Y), Math.Min(p2.Y, p3.Y));
        double maxX = Math.Max(Math.Max(p0.X, p1.X), Math.Max(p2.X, p3.X));
        double maxY = Math.Max(Math.Max(p0.Y, p1.Y), Math.Max(p2.Y, p3.Y));

        return new Rect(minX, minY, Math.Max(0, maxX - minX), Math.Max(0, maxY - minY));
    }

    /// <summary>
    /// Computes D&D 5e distance in feet between two map points given grid settings.
    /// </summary>
    public static double MeasureDistanceFeet(Point a, Point b, GridSettings grid)
    {
        double ppc = Math.Max(1.0, grid.PixelsPerCell);
        double dxCells = Math.Abs(b.X - a.X) / ppc;
        double dyCells = Math.Abs(b.Y - a.Y) / ppc;

        double cells = grid.DiagonalRule switch
        {
            DiagonalRule.Standard5e_5_5_5 => Math.Max(dxCells, dyCells),
            DiagonalRule.Variant_5_10_5 => Math.Max(dxCells, dyCells) + 0.5 * Math.Min(dxCells, dyCells),
            _ => Math.Sqrt(dxCells * dxCells + dyCells * dyCells)
        };

        return cells * grid.FeetPerCell;
    }
}
