using System.Windows;
using System.Windows.Media;
using DiceChaosDisplay.Models;

namespace DiceChaosDisplay.Controls;

public static class GridRenderer
{
    public static void DrawGrid(
        DrawingContext dc,
        GridSettings settings,
        Rect fullMapRect,
        Rect visibleMapRect,
        double currentZoom)
    {
        if (!settings.IsVisible || settings.PixelsPerCell < 4.0) return;

        Rect clip = Rect.Intersect(fullMapRect, visibleMapRect);
        if (clip.IsEmpty || clip.Width <= 0 || clip.Height <= 0) return;

        // Avoid sub-pixel moire noise if zoomed way out so cells are tiny (< 4 screen pixels)
        double screenCellSize = settings.PixelsPerCell * currentZoom;
        if (screenCellSize < 4.0) return;

        Color baseColor = ParseColorOrDefault(settings.ColorHex, Colors.White);
        baseColor.A = (byte)Math.Clamp(settings.Opacity * 255.0, 5.0, 255.0);

        var brush = new SolidColorBrush(baseColor);
        brush.Freeze();

        // Keep grid stroke crisp in screen space regardless of map zoom
        double mapStrokeWidth = Math.Clamp(settings.LineThickness / Math.Max(0.05, currentZoom), 0.35, 16.0);
        var pen = new Pen(brush, mapStrokeWidth);
        pen.Freeze();

        var geom = new StreamGeometry();
        using (var ctx = geom.Open())
        {
            switch (settings.GridType)
            {
                case GridType.HexPointyTop:
                    BuildPointyHexGeometry(ctx, settings, clip);
                    break;
                case GridType.HexFlatTop:
                    BuildFlatHexGeometry(ctx, settings, clip);
                    break;
                default:
                    BuildSquareGeometry(ctx, settings, clip);
                    break;
            }
        }
        geom.Freeze();

        dc.PushClip(new RectangleGeometry(fullMapRect));
        dc.DrawGeometry(null, pen, geom);
        dc.Pop();
    }

    private static void BuildSquareGeometry(StreamGeometryContext ctx, GridSettings s, Rect clip)
    {
        double step = s.PixelsPerCell;
        double ox = ((s.OffsetX % step) + step) % step;
        double oy = ((s.OffsetY % step) + step) % step;

        double firstX = Math.Floor((clip.Left - ox) / step) * step + ox;
        for (double x = firstX; x <= clip.Right; x += step)
        {
            if (x < clip.Left - 1) continue;
            ctx.BeginFigure(new Point(x, clip.Top), isFilled: false, isClosed: false);
            ctx.LineTo(new Point(x, clip.Bottom), isStroked: true, isSmoothJoin: false);
        }

        double firstY = Math.Floor((clip.Top - oy) / step) * step + oy;
        for (double y = firstY; y <= clip.Bottom; y += step)
        {
            if (y < clip.Top - 1) continue;
            ctx.BeginFigure(new Point(clip.Left, y), isFilled: false, isClosed: false);
            ctx.LineTo(new Point(clip.Right, y), isStroked: true, isSmoothJoin: false);
        }
    }

    private static void BuildPointyHexGeometry(StreamGeometryContext ctx, GridSettings s, Rect clip)
    {
        // Flat-to-flat width = PixelsPerCell => R = PixelsPerCell / sqrt(3)
        double w = s.PixelsPerCell;
        double r = w / Math.Sqrt(3.0);
        double rowStep = 1.5 * r;

        double ox = ((s.OffsetX % w) + w) % w;
        double oy = ((s.OffsetY % (rowStep * 2)) + rowStep * 2) % (rowStep * 2);

        int minRow = (int)Math.Floor((clip.Top - oy) / rowStep) - 1;
        int maxRow = (int)Math.Ceiling((clip.Bottom - oy) / rowStep) + 1;
        int minCol = (int)Math.Floor((clip.Left - ox) / w) - 1;
        int maxCol = (int)Math.Ceiling((clip.Right - ox) / w) + 1;

        for (int row = minRow; row <= maxRow; row++)
        {
            double cy = oy + row * rowStep;
            double rowShift = ((row & 1) != 0) ? w * 0.5 : 0.0;

            for (int col = minCol; col <= maxCol; col++)
            {
                double cx = ox + col * w + rowShift;
                DrawHexPolygon(ctx, cx, cy, r, pointyTop: true);
            }
        }
    }

    private static void BuildFlatHexGeometry(StreamGeometryContext ctx, GridSettings s, Rect clip)
    {
        // Flat-to-flat height = PixelsPerCell => R = PixelsPerCell / sqrt(3)
        double h = s.PixelsPerCell;
        double r = h / Math.Sqrt(3.0);
        double colStep = 1.5 * r;

        double ox = ((s.OffsetX % (colStep * 2)) + colStep * 2) % (colStep * 2);
        double oy = ((s.OffsetY % h) + h) % h;

        int minCol = (int)Math.Floor((clip.Left - ox) / colStep) - 1;
        int maxCol = (int)Math.Ceiling((clip.Right - ox) / colStep) + 1;
        int minRow = (int)Math.Floor((clip.Top - oy) / h) - 1;
        int maxRow = (int)Math.Ceiling((clip.Bottom - oy) / h) + 1;

        for (int col = minCol; col <= maxCol; col++)
        {
            double cx = ox + col * colStep;
            double colShift = ((col & 1) != 0) ? h * 0.5 : 0.0;

            for (int row = minRow; row <= maxRow; row++)
            {
                double cy = oy + row * h + colShift;
                DrawHexPolygon(ctx, cx, cy, r, pointyTop: false);
            }
        }
    }

    private static void DrawHexPolygon(StreamGeometryContext ctx, double cx, double cy, double r, bool pointyTop)
    {
        double startDeg = pointyTop ? -90.0 : 0.0;
        for (int i = 0; i < 6; i++)
        {
            double rad = (startDeg + i * 60.0) * Math.PI / 180.0;
            Point pt = new(cx + r * Math.Cos(rad), cy + r * Math.Sin(rad));
            if (i == 0) ctx.BeginFigure(pt, isFilled: false, isClosed: true);
            else ctx.LineTo(pt, isStroked: true, isSmoothJoin: false);
        }
    }

    public static Color ParseColorOrDefault(string? hex, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(hex)) return fallback;
        try
        {
            if (ColorConverter.ConvertFromString(hex) is Color c) return c;
        }
        catch { }
        return fallback;
    }
}
