using System.Globalization;
using System.Windows;
using System.Windows.Media;
using DiceChaosDisplay.Models;
using DiceChaosDisplay.Services;

namespace DiceChaosDisplay.Controls;

public sealed record ActivePingAnimation(
    Point MapPoint,
    Color Color,
    DateTime StartedUtc,
    double DurationSeconds = 1.85);

public static class TacticalOverlayRenderer
{
    private static readonly Typeface UiBoldTypeface = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    public static void DrawAoeTemplates(
        DrawingContext dc,
        IEnumerable<AoeTemplateModel> templates,
        GridSettings grid,
        bool isDmView,
        AoeTemplateModel? selectedTemplate,
        double zoom,
        double pixelsPerDip,
        AoeTemplateModel? hoveredTemplate = null,
        bool showLabelsAlways = false)
    {
        double pxPerFoot = Math.Max(1.0, grid.PixelsPerCell) / Math.Max(1.0, grid.FeetPerCell);
        double invZoom = 1.0 / Math.Max(0.05, zoom);

        foreach (var aoe in templates)
        {
            if (!isDmView && !aoe.VisibleToPlayers) continue;

            Color c = GridRenderer.ParseColorOrDefault(aoe.ColorHex, Color.FromRgb(255, 107, 53));
            bool isSelected = ReferenceEquals(aoe, selectedTemplate) && isDmView;
            bool isHovered = ReferenceEquals(aoe, hoveredTemplate) && isDmView;

            byte fillAlpha = (byte)(!aoe.VisibleToPlayers ? 40 : (isSelected || isHovered ? 82 : 62));
            var fillBrush = new SolidColorBrush(Color.FromArgb(fillAlpha, c.R, c.G, c.B));
            fillBrush.Freeze();

            var strokeBrush = new SolidColorBrush(Color.FromArgb(235, c.R, c.G, c.B));
            strokeBrush.Freeze();

            var pen = new Pen(strokeBrush, (isSelected ? 3.4 : (isHovered ? 2.8 : 2.1)) * invZoom);
            if (!aoe.VisibleToPlayers)
            {
                pen.DashStyle = DashStyles.Dash;
            }
            pen.Freeze();

            Point origin = new(aoe.OriginX, aoe.OriginY);
            double sizePx = aoe.SizeFeet * pxPerFoot;
            double rad = aoe.DirectionDegrees * Math.PI / 180.0;

            switch (aoe.Shape)
            {
                case AoeShape.Sphere:
                {
                    dc.DrawEllipse(fillBrush, pen, origin, sizePx, sizePx);
                    break;
                }
                case AoeShape.Cube:
                {
                    dc.PushTransform(new RotateTransform(aoe.DirectionDegrees, origin.X, origin.Y));
                    var rect = new Rect(origin.X - sizePx * 0.5, origin.Y - sizePx * 0.5, sizePx, sizePx);
                    dc.DrawRectangle(fillBrush, pen, rect);
                    dc.Pop();
                    break;
                }
                case AoeShape.Cone:
                {
                    // D&D 5e PHB rule: width at distance D equals D (half-angle = atan(0.5))
                    var geom = new StreamGeometry();
                    using (var ctx = geom.Open())
                    {
                        Vector dir = new(Math.Cos(rad), Math.Sin(rad));
                        Vector perp = new(-dir.Y, dir.X);
                        Point endCenter = origin + dir * sizePx;
                        Point left = endCenter + perp * (sizePx * 0.5);
                        Point right = endCenter - perp * (sizePx * 0.5);

                        ctx.BeginFigure(origin, isFilled: true, isClosed: true);
                        ctx.LineTo(left, isStroked: true, isSmoothJoin: true);
                        ctx.ArcTo(right, new Size(sizePx, sizePx), 0, false, SweepDirection.Counterclockwise, isStroked: true, isSmoothJoin: true);
                    }
                    geom.Freeze();
                    dc.DrawGeometry(fillBrush, pen, geom);
                    break;
                }
                case AoeShape.Line:
                {
                    double widthPx = Math.Max(5.0, aoe.WidthFeet * pxPerFoot);
                    dc.PushTransform(new RotateTransform(aoe.DirectionDegrees, origin.X, origin.Y));
                    var rect = new Rect(origin.X, origin.Y - widthPx * 0.5, sizePx, widthPx);
                    dc.DrawRectangle(fillBrush, pen, rect);
                    dc.Pop();
                    break;
                }
            }

            // Origin anchor & DM rotation handle when selected (labels shown ONLY on hover/select or when ShowAoeLabelsAlways is enabled)
            if (isDmView)
            {
                var anchorOutline = (isSelected || isHovered) ? new Pen(Brushes.White, 1.8 * invZoom) : null;
                anchorOutline?.Freeze();
                dc.DrawEllipse(strokeBrush, anchorOutline, origin, (isSelected || isHovered ? 6.2 : 4.6) * invZoom, (isSelected || isHovered ? 6.2 : 4.6) * invZoom);

                if (isSelected && (aoe.Shape == AoeShape.Cone || aoe.Shape == AoeShape.Line || aoe.Shape == AoeShape.Cube))
                {
                    double handleDist = aoe.Shape == AoeShape.Cube ? sizePx * 0.65 : sizePx;
                    Point handlePt = new(origin.X + Math.Cos(rad) * handleDist, origin.Y + Math.Sin(rad) * handleDist);
                    var whitePen = new Pen(Brushes.White, 2.0 * invZoom);
                    whitePen.Freeze();
                    dc.DrawLine(whitePen, origin, handlePt);
                    dc.DrawEllipse(Brushes.Gold, whitePen, handlePt, 7.5 * invZoom, 7.5 * invZoom);
                }

                if (!string.IsNullOrWhiteSpace(aoe.Label) && (showLabelsAlways || isSelected || isHovered))
                {
                    DrawScreenScaledPill(dc, new Point(origin.X, origin.Y - 15 * invZoom), aoe.Label, c, invZoom, pixelsPerDip);
                }
            }
        }
    }

    public static void DrawTokensAndRings(
        DrawingContext dc,
        IEnumerable<TokenMarkerModel> tokens,
        GridSettings grid,
        bool isDmView,
        TokenMarkerModel? selectedToken,
        double zoom,
        double pixelsPerDip)
    {
        double cellPx = Math.Max(8.0, grid.PixelsPerCell);
        double pxPerFoot = cellPx / Math.Max(1.0, grid.FeetPerCell);
        double invZoom = 1.0 / Math.Max(0.05, zoom);

        foreach (var tok in tokens)
        {
            if (!isDmView && !tok.VisibleToPlayers) continue;

            Color c = GridRenderer.ParseColorOrDefault(tok.ColorHex, Color.FromRgb(0, 229, 255));
            Point center = new(tok.MapX, tok.MapY);
            double radius = (tok.SizeCells * cellPx) * 0.5;
            bool isSelected = ReferenceEquals(tok, selectedToken) && isDmView;

            // Optional outer Aura radius (e.g. 10ft Paladin Aura / 15ft Spirit Guardians)
            if (tok.AuraRadiusFeet > 0.5)
            {
                double auraRadius = radius + tok.AuraRadiusFeet * pxPerFoot;
                var auraFill = new SolidColorBrush(Color.FromArgb(32, c.R, c.G, c.B));
                auraFill.Freeze();
                var auraPen = new Pen(new SolidColorBrush(Color.FromArgb(170, c.R, c.G, c.B)), 2.0 * invZoom)
                {
                    DashStyle = DashStyles.Dash
                };
                auraPen.Freeze();
                dc.DrawEllipse(auraFill, auraPen, center, auraRadius, auraRadius);
            }

            if (tok.Kind == TokenMarkerKind.ConditionRing)
            {
                // Hollow glowing ring designed to illuminate AROUND a physical miniature base placed on the TV!
                var outerGlowPen = new Pen(new SolidColorBrush(Color.FromArgb(85, c.R, c.G, c.B)), Math.Max(8.0 * invZoom, radius * 0.22));
                outerGlowPen.Freeze();
                dc.DrawEllipse(null, outerGlowPen, center, radius, radius);

                var crispRingPen = new Pen(new SolidColorBrush(Color.FromArgb(245, c.R, c.G, c.B)), Math.Max(3.2 * invZoom, radius * 0.09));
                if (!tok.VisibleToPlayers) crispRingPen.DashStyle = DashStyles.Dash;
                crispRingPen.Freeze();
                dc.DrawEllipse(null, crispRingPen, center, radius, radius);
            }
            else
            {
                // Solid/translucent numbered monster/NPC token
                byte alpha = (byte)(tok.VisibleToPlayers ? 215 : 115);
                var fill = new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
                fill.Freeze();
                var border = new Pen(isSelected ? Brushes.Gold : Brushes.White, (isSelected ? 3.5 : 2.2) * invZoom);
                if (!tok.VisibleToPlayers) border.DashStyle = DashStyles.Dash;
                border.Freeze();

                dc.DrawEllipse(fill, border, center, radius * 0.88, radius * 0.88);

                if (!string.IsNullOrWhiteSpace(tok.Label))
                {
                    double fontSize = Math.Max(10.0 * invZoom, radius * 0.72);
                    var ft = new FormattedText(
                        tok.Label,
                        CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight,
                        UiBoldTypeface,
                        fontSize,
                        Brushes.Black,
                        pixelsPerDip);
                    dc.DrawText(ft, new Point(center.X - ft.Width * 0.5, center.Y - ft.Height * 0.5));
                }
            }

            if (isSelected)
            {
                var selPen = new Pen(Brushes.Gold, 2.0 * invZoom) { DashStyle = DashStyles.Dot };
                selPen.Freeze();
                dc.DrawEllipse(null, selPen, center, radius + 6 * invZoom, radius + 6 * invZoom);
            }

            if (tok.Kind == TokenMarkerKind.ConditionRing && !string.IsNullOrWhiteSpace(tok.Label))
            {
                DrawScreenScaledPill(dc, new Point(center.X, center.Y + radius + 12 * invZoom), tok.Label, c, invZoom, pixelsPerDip);
            }
        }
    }

    public static void DrawPencilStrokes(
        DrawingContext dc,
        IEnumerable<PencilStrokeModel> strokes,
        PencilStrokeModel? activePreviewStroke,
        double zoom)
    {
        double invZoom = 1.0 / Math.Max(0.05, zoom);

        void RenderSingle(PencilStrokeModel s)
        {
            if (s.Points.Count == 0) return;
            Color c = GridRenderer.ParseColorOrDefault(s.ColorHex, Color.FromRgb(255, 71, 87));
            var brush = new SolidColorBrush(c);
            brush.Freeze();

            if (s.Points.Count == 1)
            {
                double r = Math.Max(2.0 * invZoom, s.Thickness * 0.5);
                dc.DrawEllipse(brush, null, new Point(s.Points[0].X, s.Points[0].Y), r, r);
                return;
            }

            var pen = new Pen(brush, Math.Max(2.0 * invZoom, s.Thickness))
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round
            };
            pen.Freeze();

            var geom = new StreamGeometry();
            using (var ctx = geom.Open())
            {
                ctx.BeginFigure(new Point(s.Points[0].X, s.Points[0].Y), isFilled: false, isClosed: false);
                for (int i = 1; i < s.Points.Count; i++)
                {
                    ctx.LineTo(new Point(s.Points[i].X, s.Points[i].Y), isStroked: true, isSmoothJoin: true);
                }
            }
            geom.Freeze();
            dc.DrawGeometry(null, pen, geom);
        }

        foreach (var stroke in strokes)
            RenderSingle(stroke);

        if (activePreviewStroke is not null)
            RenderSingle(activePreviewStroke);
    }

    public static void DrawRuler(
        DrawingContext dc,
        Point startMapPt,
        Point endMapPt,
        GridSettings grid,
        double zoom,
        double pixelsPerDip)
    {
        double invZoom = 1.0 / Math.Max(0.05, zoom);
        var shadowPen = new Pen(new SolidColorBrush(Color.FromArgb(180, 0, 0, 0)), 5.0 * invZoom)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        shadowPen.Freeze();

        var linePen = new Pen(Brushes.Gold, 2.8 * invZoom)
        {
            DashStyle = DashStyles.Dash,
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        linePen.Freeze();

        dc.DrawLine(shadowPen, startMapPt, endMapPt);
        dc.DrawLine(linePen, startMapPt, endMapPt);
        dc.DrawEllipse(Brushes.Gold, null, startMapPt, 5.0 * invZoom, 5.0 * invZoom);
        dc.DrawEllipse(Brushes.Gold, null, endMapPt, 5.0 * invZoom, 5.0 * invZoom);

        double distFeet = ViewportMath.MeasureDistanceFeet(startMapPt, endMapPt, grid);
        double squares = distFeet / Math.Max(1.0, grid.FeetPerCell);
        string text = $"{distFeet:0.#} ft ({squares:0.#} sq)";

        Point mid = new((startMapPt.X + endMapPt.X) * 0.5, (startMapPt.Y + endMapPt.Y) * 0.5 - 16 * invZoom);
        DrawScreenScaledPill(dc, mid, text, Colors.Gold, invZoom, pixelsPerDip);
    }

    public static void DrawPings(
        DrawingContext dc,
        IReadOnlyList<ActivePingAnimation> pings,
        DateTime nowUtc,
        double zoom)
    {
        double invZoom = 1.0 / Math.Max(0.05, zoom);

        foreach (var ping in pings)
        {
            double t = (nowUtc - ping.StartedUtc).TotalSeconds / ping.DurationSeconds;
            if (t < 0 || t > 1.0) continue;

            // Two staggered expanding sonar rings + center beacon
            for (int ring = 0; ring < 2; ring++)
            {
                double rt = Math.Clamp((t - ring * 0.2) / 0.8, 0.0, 1.0);
                if (rt <= 0 || rt >= 1.0) continue;

                double eased = 1.0 - Math.Pow(1.0 - rt, 3);
                double radius = (12.0 + 78.0 * eased) * invZoom;
                byte alpha = (byte)((1.0 - rt) * 245);

                var pen = new Pen(new SolidColorBrush(Color.FromArgb(alpha, ping.Color.R, ping.Color.G, ping.Color.B)), (4.5 - 2.5 * rt) * invZoom);
                pen.Freeze();
                dc.DrawEllipse(null, pen, ping.MapPoint, radius, radius);
            }

            byte centerAlpha = (byte)(Math.Max(0, 1.0 - t) * 255);
            var centerBrush = new SolidColorBrush(Color.FromArgb(centerAlpha, 255, 255, 255));
            centerBrush.Freeze();
            var haloBrush = new SolidColorBrush(Color.FromArgb((byte)(centerAlpha * 0.8), ping.Color.R, ping.Color.G, ping.Color.B));
            haloBrush.Freeze();
            dc.DrawEllipse(haloBrush, null, ping.MapPoint, 10 * invZoom, 10 * invZoom);
            dc.DrawEllipse(centerBrush, null, ping.MapPoint, 4.5 * invZoom, 4.5 * invZoom);
        }
    }

    public static Rect DrawScreenScaledPill(
        DrawingContext dc,
        Point centerMapPt,
        string text,
        Color accentColor,
        double invZoom,
        double pixelsPerDip)
    {
        double fontSize = 12.0 * invZoom;
        var ft = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            UiBoldTypeface,
            fontSize,
            Brushes.White,
            pixelsPerDip);

        double padX = 7.0 * invZoom;
        double padY = 3.0 * invZoom;
        var rect = new Rect(
            centerMapPt.X - ft.Width * 0.5 - padX,
            centerMapPt.Y - ft.Height * 0.5 - padY,
            ft.Width + padX * 2,
            ft.Height + padY * 2);

        var bg = new SolidColorBrush(Color.FromArgb(215, 18, 22, 31));
        bg.Freeze();
        var borderPen = new Pen(new SolidColorBrush(accentColor), 1.5 * invZoom);
        borderPen.Freeze();

        dc.DrawRoundedRectangle(bg, borderPen, rect, 5 * invZoom, 5 * invZoom);
        dc.DrawText(ft, new Point(centerMapPt.X - ft.Width * 0.5, centerMapPt.Y - ft.Height * 0.5));
        return rect;
    }
}
