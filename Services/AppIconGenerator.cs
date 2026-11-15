using System.IO;
using System.Text;
using SkiaSharp;

namespace DiceChaosDisplay.Services;

/// <summary>
/// Deterministic high-DPI brand icon renderer and Win32 multi-resolution .ico compiler
/// for DiceChaosDisplay, visually harmonized with the partner Mind Map suite icon.
/// </summary>
public static class AppIconGenerator
{
    public static readonly int[] StandardIcoSizes = [16, 24, 32, 48, 64, 128, 256];

    // Exact Suite Palette (sampled & harmonized with Partner Mind Map Icon)
    private static readonly SKColor RimSkyTop = new(0x36, 0xA2, 0xD8);
    private static readonly SKColor RimSkyBottom = new(0x28, 0x8B, 0xBE);
    private static readonly SKColor RimInnerBevel = new(0x18, 0x30, 0x4E);
    private static readonly SKColor DarkCasing = new(0x06, 0x0B, 0x15);

    private static readonly SKColor BgCenterNavy = new(0x12, 0x20, 0x3B);
    private static readonly SKColor BgMidNavy = new(0x0A, 0x11, 0x21);
    private static readonly SKColor BgEdgeNavy = new(0x07, 0x0C, 0x18);
    private static readonly SKColor BlueprintGridColor = new(0x22, 0x3A, 0x66, 0x58);

    private static readonly SKColor NeonIceWhite = new(0xF0, 0xFC, 0xFF);
    private static readonly SKColor NeonSkyCyan = new(0x38, 0xBD, 0xF8);
    private static readonly SKColor NeonEmerald = new(0x38, 0xE0, 0x8F);
    private static readonly SKColor NeonCoral = new(0xFF, 0x6E, 0x7F);
    private static readonly SKColor NeonGold = new(0xFB, 0xBF, 0x24);
    private static readonly SKColor NeonViolet = new(0xC0, 0x84, 0xFC);

    public enum IconLodTier
    {
        Hero,   // 64px - 1024px: Full detail, blueprint grid, 3 concentric rings, dashed rays, "20" numeral
        Medium, // 32px - 48px: Bold strokes, simplified 2x2 grid, 3 bold accent rings, crisp d20 facets
        Micro   // 16px - 24px: High-contrast taskbar silhouette, bold squircle, bold screen slab + crisp d20
    }

    public static IconLodTier GetLodTier(int targetSize) => targetSize switch
    {
        <= 24 => IconLodTier.Micro,
        <= 48 => IconLodTier.Medium,
        _ => IconLodTier.Hero
    };

    public static SKBitmap RenderIconBitmap(int pixelSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pixelSize, 16);

        IconLodTier lod = GetLodTier(pixelSize);
        int renderSize = pixelSize switch
        {
            <= 32 => pixelSize * 4,
            <= 128 => pixelSize * 2,
            _ => pixelSize
        };

        using var hiRes = new SKBitmap(new SKImageInfo(renderSize, renderSize, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(hiRes))
        {
            canvas.Clear(SKColors.Transparent);
            float scale = renderSize / 1024f;
            canvas.Scale(scale, scale);
            DrawMasterArtboard(canvas, lod, pixelSize);
        }

        if (renderSize == pixelSize)
        {
            return hiRes.Copy();
        }

        var output = new SKBitmap(new SKImageInfo(pixelSize, pixelSize, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var srcImage = SKImage.FromBitmap(hiRes);
        using var outCanvas = new SKCanvas(output);
        outCanvas.Clear(SKColors.Transparent);
        outCanvas.DrawImage(
            srcImage,
            new SKRect(0, 0, pixelSize, pixelSize),
            new SKSamplingOptions(SKCubicResampler.CatmullRom));
        return output;
    }

    public static byte[] EncodePng(int pixelSize)
    {
        using var bmp = RenderIconBitmap(pixelSize);
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    public static byte[] BuildMultiResolutionIco(ReadOnlySpan<int> sizes)
    {
        var frames = new List<(int Size, byte[] PngBytes)>(sizes.Length);
        foreach (int s in sizes)
        {
            frames.Add((s, EncodePng(s)));
        }

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);

        // ICONDIR header (6 bytes)
        bw.Write((ushort)0);             // Reserved = 0
        bw.Write((ushort)1);             // Type = 1 (ICO)
        bw.Write((ushort)frames.Count);  // Image count

        int dataOffset = 6 + frames.Count * 16;
        foreach (var (size, png) in frames)
        {
            byte dimByte = size >= 256 ? (byte)0 : (byte)size;
            bw.Write(dimByte);           // Width (0 means 256+)
            bw.Write(dimByte);           // Height (0 means 256+)
            bw.Write((byte)0);           // Color palette count
            bw.Write((byte)0);           // Reserved
            bw.Write((ushort)1);         // Color planes
            bw.Write((ushort)32);        // Bits per pixel (32-bit ARGB)
            bw.Write((uint)png.Length);  // Byte size of PNG payload
            bw.Write((uint)dataOffset);  // Offset from start of file
            dataOffset += png.Length;
        }

        foreach (var (_, png) in frames)
        {
            bw.Write(png);
        }

        bw.Flush();
        return ms.ToArray();
    }

    public static void GenerateAllProjectAssets(string assetsDir)
    {
        Directory.CreateDirectory(assetsDir);

        File.WriteAllBytes(Path.Combine(assetsDir, "AppIcon.ico"), BuildMultiResolutionIco(StandardIcoSizes));
        File.WriteAllBytes(Path.Combine(assetsDir, "AppIcon-256.png"), EncodePng(256));
        File.WriteAllBytes(Path.Combine(assetsDir, "AppIcon-1024.png"), EncodePng(1024));
        File.WriteAllText(Path.Combine(assetsDir, "AppIcon.svg"), BuildMasterSvg(), Encoding.UTF8);
    }

    public static void RenderSuiteComparisonSheet(string partnerIconPath, string outputPngPath)
    {
        const int width = 1280;
        const int height = 780;

        using var sheet = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(sheet);
        canvas.Clear(new SKColor(0x0B, 0x0E, 0x16));

        using var titleFace = SKTypeface.FromFamilyName("Segoe UI", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
        using var regularFace = SKTypeface.FromFamilyName("Segoe UI", SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
        using var headerFont = new SKFont(titleFace, 24f) { Subpixel = true, Edging = SKFontEdging.Antialias };
        using var subFont = new SKFont(regularFace, 15f) { Subpixel = true, Edging = SKFontEdging.Antialias };
        using var labelFont = new SKFont(titleFace, 16f) { Subpixel = true, Edging = SKFontEdging.Antialias };
        using var smallFont = new SKFont(regularFace, 13f) { Subpixel = true, Edging = SKFontEdging.Antialias };

        using var whitePaint = new SKPaint { IsAntialias = true, Color = new SKColor(0xEC, 0xF0, 0xFA) };
        using var mutedPaint = new SKPaint { IsAntialias = true, Color = new SKColor(0x8F, 0x9D, 0xBB) };
        using var cyanPaint = new SKPaint { IsAntialias = true, Color = NeonSkyCyan };
        using var cardFill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = new SKColor(0x13, 0x18, 0x26) };
        using var cardBorder = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, Color = new SKColor(0x28, 0x34, 0x4E), StrokeWidth = 1.5f };

        var cubic = new SKSamplingOptions(SKCubicResampler.CatmullRom);
        var exact = new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None);

        canvas.DrawText("APP SUITE ICON DESIGN & OPTICAL LOD PROOF", 48f, 48f, SKTextAlign.Left, headerFont, cyanPaint);
        canvas.DrawText("Partner Mind Map App  •  DiceChaosDisplay Tabletop Studio  •  Windows 11 Taskbar & Multi-Size Mipmaps", 48f, 74f, SKTextAlign.Left, subFont, mutedPaint);

        // Left Card: Partner Mind Map Icon
        var leftCard = new SKRoundRect(new SKRect(48f, 100f, 620f, 520f), 16f);
        canvas.DrawRoundRect(leftCard, cardFill);
        canvas.DrawRoundRect(leftCard, cardBorder);
        canvas.DrawText("PARTNER APP — MIND MAP", 334f, 136f, SKTextAlign.Center, labelFont, mutedPaint);

        SKBitmap? partnerBmp = File.Exists(partnerIconPath) ? SKBitmap.Decode(partnerIconPath) : null;
        using (partnerBmp)
        {
            if (partnerBmp is not null)
            {
                using var partnerImg = SKImage.FromBitmap(partnerBmp);
                canvas.DrawImage(partnerImg, new SKRect(174f, 158f, 494f, 478f), cubic);
            }

            // Right Card: New DiceChaosDisplay Icon
            var rightCard = new SKRoundRect(new SKRect(660f, 100f, 1232f, 520f), 16f);
            canvas.DrawRoundRect(rightCard, cardFill);
            canvas.DrawRoundRect(rightCard, cardBorder);
            canvas.DrawText("NEW — DICECHAOSDISPLAY (HERO 1024→320px)", 946f, 136f, SKTextAlign.Center, labelFont, whitePaint);

            using var heroBmp = RenderIconBitmap(512);
            using var heroImg = SKImage.FromBitmap(heroBmp);
            canvas.DrawImage(heroImg, new SKRect(786f, 158f, 1106f, 478f), cubic);

            // Bottom Strip Left: Simulated Windows 11 Dark Taskbar (24px & 32px Pinned Pair)
            var taskbarCard = new SKRoundRect(new SKRect(48f, 548f, 620f, 740f), 16f);
            canvas.DrawRoundRect(taskbarCard, cardFill);
            canvas.DrawRoundRect(taskbarCard, cardBorder);
            canvas.DrawText("SIMULATED WINDOWS 11 TASKBAR PAIR (32px & 24px)", 334f, 582f, SKTextAlign.Center, labelFont, whitePaint);

            var micaBar = new SKRoundRect(new SKRect(120f, 610f, 548f, 686f), 12f);
            using var micaFill = new SKPaint { IsAntialias = true, Color = new SKColor(0x1C, 0x21, 0x30) };
            canvas.DrawRoundRect(micaBar, micaFill);
            canvas.DrawRoundRect(micaBar, cardBorder);

            using var dc32 = RenderIconBitmap(32);
            using var dc32Img = SKImage.FromBitmap(dc32);
            using var dc24 = RenderIconBitmap(24);
            using var dc24Img = SKImage.FromBitmap(dc24);

            if (partnerBmp is not null)
            {
                using var pImg = SKImage.FromBitmap(partnerBmp);
                canvas.DrawImage(pImg, new SKRect(186f, 632f, 218f, 664f), cubic);
                canvas.DrawImage(dc32Img, new SKRect(234f, 632f, 266f, 664f), exact);
                canvas.DrawText("32×32 Taskbar", 226f, 712f, SKTextAlign.Center, smallFont, mutedPaint);

                canvas.DrawImage(pImg, new SKRect(398f, 636f, 422f, 660f), cubic);
                canvas.DrawImage(dc24Img, new SKRect(438f, 636f, 462f, 660f), exact);
                canvas.DrawText("24×24 Taskbar", 430f, 712f, SKTextAlign.Center, smallFont, mutedPaint);
            }

            // Bottom Strip Right: Dedicated Optical LOD Mipmaps (96, 64, 48, 32, 24, 16)
            var lodCard = new SKRoundRect(new SKRect(660f, 548f, 1232f, 740f), 16f);
            canvas.DrawRoundRect(lodCard, cardFill);
            canvas.DrawRoundRect(lodCard, cardBorder);
            canvas.DrawText("MULTI-RESOLUTION .ICO FRAMES (ACTUAL PIXEL SCALE)", 946f, 582f, SKTextAlign.Center, labelFont, whitePaint);

            int[] previewSizes = [96, 64, 48, 32, 24, 16];
            float curX = 708f;
            foreach (int sz in previewSizes)
            {
                using var lodBmp = RenderIconBitmap(sz);
                using var lodImg = SKImage.FromBitmap(lodBmp);
                float drawY = 656f - sz * 0.5f;
                canvas.DrawImage(lodImg, new SKRect(curX, drawY, curX + sz, drawY + sz), exact);
                canvas.DrawText($"{sz}px", curX + sz * 0.5f, 718f, SKTextAlign.Center, smallFont, mutedPaint);
                curX += sz + 38f;
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPngPath)!);
        using var finalImg = SKImage.FromBitmap(sheet);
        using var pngData = finalImg.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(outputPngPath, pngData.ToArray());
    }

    private static void DrawMasterArtboard(SKCanvas canvas, IconLodTier lod, int targetPixelSize)
    {
        float strokeMult = lod switch
        {
            IconLodTier.Micro => 1.80f,
            IconLodTier.Medium => 1.30f,
            _ => 1.0f
        };

        // 1. Squircle Container (23.2% corner radius matching Partner Mind Map Icon)
        float margin = lod == IconLodTier.Micro ? 32f : 48f;
        var squircleRect = new SKRect(margin, margin, 1024f - margin, 1024f - margin);
        float cornerRadius = squircleRect.Width * 0.232f;
        using var squircleRRect = new SKRoundRect(squircleRect, cornerRadius, cornerRadius);

        // Subtle outer dark casing so transparent squircle pops cleanly on any OS surface
        using (var outerDark = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            Color = new SKColor(4, 8, 16, 215),
            StrokeWidth = 18f * strokeMult
        })
        {
            canvas.DrawRoundRect(squircleRRect, outerDark);
        }

        // Clip interior to squircle
        canvas.Save();
        canvas.ClipRoundRect(squircleRRect, SKClipOperation.Intersect, antialias: true);

        // Midnight Navy Radial Fill
        using (var bgPaint = new SKPaint { IsAntialias = true })
        using (var bgShader = SKShader.CreateRadialGradient(
            new SKPoint(512f, 455f),
            610f,
            [BgCenterNavy, BgMidNavy, BgEdgeNavy],
            [0.0f, 0.58f, 1.0f],
            SKShaderTileMode.Clamp))
        {
            bgPaint.Shader = bgShader;
            canvas.DrawRect(squircleRect, bgPaint);
        }

        // Blueprint Coordinate Grid (Hero tier only so small icons stay ultra-clean)
        if (lod == IconLodTier.Hero)
        {
            using var gridPaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                Color = BlueprintGridColor,
                StrokeWidth = 2.2f
            };
            float step = squircleRect.Width / 10f;
            for (int i = 1; i < 10; i++)
            {
                float pos = squircleRect.Left + i * step;
                canvas.DrawLine(pos, squircleRect.Top, pos, squircleRect.Bottom, gridPaint);
                canvas.DrawLine(squircleRect.Left, pos, squircleRect.Right, pos, gridPaint);
            }
        }

        // Ambient Volumetric Backlight Behind Floating d20 & Tabletop Display
        DrawRadialGlow(canvas, new SKPoint(512f, 352f), 320f, NeonSkyCyan.WithAlpha(62));
        DrawRadialGlow(canvas, new SKPoint(512f, 755f), 330f, NeonEmerald.WithAlpha(44));

        // 2. Isometric Perspective Tabletop Battlemap Display (Lower Half, optically balanced to y = 882)
        DrawIsometricTabletopDisplay(canvas, lod, strokeMult);

        // 3. Holographic Projection Cone & Dashed Suite Rays (Hero & Medium)
        if (lod != IconLodTier.Micro)
        {
            DrawHolographicProjection(canvas, lod, strokeMult);
        }

        // 4. Floating Glowing Multi-Neon Prism Icosahedron / d20 (Upper-Center Hero, top at y = 128)
        DrawFloatingNeonD20(canvas, lod, strokeMult, targetPixelSize);

        canvas.Restore(); // End squircle clip

        // 5. Signature Double-Rimmed Sky-Blue Squircle Border (matching Mind Map icon)
        if (lod != IconLodTier.Micro)
        {
            var innerBevelRect = squircleRect;
            innerBevelRect.Inflate(-13f, -13f);
            using var innerRRect = new SKRoundRect(innerBevelRect, cornerRadius - 11f, cornerRadius - 11f);
            using var innerGroovePaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                Color = DarkCasing,
                StrokeWidth = 6f
            };
            canvas.DrawRoundRect(innerRRect, innerGroovePaint);

            var hairlineRect = innerBevelRect;
            hairlineRect.Inflate(-4f, -4f);
            using var hairlineRRect = new SKRoundRect(hairlineRect, cornerRadius - 15f, cornerRadius - 15f);
            using var hairlinePaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                Color = RimInnerBevel,
                StrokeWidth = 3.2f
            };
            canvas.DrawRoundRect(hairlineRRect, hairlinePaint);
        }

        using (var rimPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 14f * (lod == IconLodTier.Micro ? 1.55f : 1.0f)
        })
        using (var rimShader = SKShader.CreateLinearGradient(
            new SKPoint(512f, squircleRect.Top),
            new SKPoint(512f, squircleRect.Bottom),
            [RimSkyTop, RimSkyBottom],
            null,
            SKShaderTileMode.Clamp))
        {
            rimPaint.Shader = rimShader;
            canvas.DrawRoundRect(squircleRRect, rimPaint);
        }
    }

    private static void DrawIsometricTabletopDisplay(SKCanvas canvas, IconLodTier lod, float strokeMult)
    {
        // Symmetric perspective tabletop flatscreen TV (fills lower half cleanly to y = 880)
        var backLeft = new SKPoint(246f, 636f);
        var backRight = new SKPoint(778f, 636f);
        var frontRight = new SKPoint(878f, 852f);
        var frontLeft = new SKPoint(146f, 852f);

        // TV 3D Front Chassis Depth Lip
        float lipDepth = lod == IconLodTier.Micro ? 32f : 26f;
        using (var chassisBuilder = new SKPathBuilder())
        {
            chassisBuilder.MoveTo(frontLeft.X, frontLeft.Y);
            chassisBuilder.LineTo(frontRight.X, frontRight.Y);
            chassisBuilder.LineTo(frontRight.X - 15f, frontRight.Y + lipDepth);
            chassisBuilder.LineTo(frontLeft.X + 15f, frontLeft.Y + lipDepth);
            chassisBuilder.Close();
            using var chassisPath = chassisBuilder.Detach();

            using var chassisFill = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
                Color = new SKColor(0x0D, 0x1E, 0x36)
            };
            canvas.DrawPath(chassisPath, chassisFill);
            DrawNeonPath(canvas, chassisPath, RimSkyTop, 7.8f * strokeMult, 14f);
        }

        // Screen Glass Quad Path
        using var screenBuilder = new SKPathBuilder();
        screenBuilder.MoveTo(backLeft);
        screenBuilder.LineTo(backRight);
        screenBuilder.LineTo(frontRight);
        screenBuilder.LineTo(frontLeft);
        screenBuilder.Close();
        using var screenPath = screenBuilder.Detach();

        using (var screenFill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill })
        using (var screenGrad = SKShader.CreateLinearGradient(
            new SKPoint(512f, 636f),
            new SKPoint(512f, 852f),
            [new SKColor(0x0B, 0x1A, 0x2E, 245), new SKColor(0x10, 0x2B, 0x3F, 250)],
            null,
            SKShaderTileMode.Clamp))
        {
            screenFill.Shader = screenGrad;
            canvas.DrawPath(screenPath, screenFill);
        }

        // Perspective Battlemap Grid Clipped Inside Screen Glass
        canvas.Save();
        canvas.ClipPath(screenPath, SKClipOperation.Intersect, antialias: true);

        if (lod != IconLodTier.Micro)
        {
            int cols = lod == IconLodTier.Hero ? 4 : 2;
            int rows = lod == IconLodTier.Hero ? 3 : 2;

            using var mapGridGlow = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                Color = NeonEmerald.WithAlpha(75),
                StrokeWidth = 9f * strokeMult,
                MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 6f)
            };
            using var mapGridLine = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                Color = NeonEmerald.WithAlpha(195),
                StrokeWidth = 3.6f * strokeMult
            };

            for (int c = 1; c < cols; c++)
            {
                float u = (float)c / cols;
                var topPt = Lerp(backLeft, backRight, u);
                var botPt = Lerp(frontLeft, frontRight, u);
                canvas.DrawLine(topPt, botPt, mapGridGlow);
                canvas.DrawLine(topPt, botPt, mapGridLine);
            }

            for (int r = 1; r < rows; r++)
            {
                float tLinear = (float)r / rows;
                float tPersp = MathF.Pow(tLinear, 1.15f);
                var leftPt = Lerp(backLeft, frontLeft, tPersp);
                var rightPt = Lerp(backRight, frontRight, tPersp);
                canvas.DrawLine(leftPt, rightPt, mapGridGlow);
                canvas.DrawLine(leftPt, rightPt, mapGridLine);
            }
        }

        canvas.Restore();

        // Crisp Neon Sky-Cyan TV Bezel Outline
        DrawNeonPath(canvas, screenPath, NeonSkyCyan, 10.0f * strokeMult, 18f);

        // Glowing Emerald "LIVE" Power Pill on Front Bezel Lip
        if (lod != IconLodTier.Micro)
        {
            DrawNeonSegment(
                canvas,
                new SKPoint(464f, 865f),
                new SKPoint(560f, 865f),
                NeonEmerald,
                6.8f * strokeMult,
                12f);
        }

        // 3 Signature Dark-Cased Concentric Neon Tactical Rings on the TV Glass
        if (lod == IconLodTier.Hero)
        {
            DrawPerspectiveTacticalRing(canvas, new SKPoint(306f, 774f), rx: 48f, ry: 22f, NeonCoral, strokeMult);
            DrawPerspectiveTacticalRing(canvas, new SKPoint(718f, 774f), rx: 48f, ry: 22f, NeonGold, strokeMult);
            DrawPerspectiveTacticalRing(canvas, new SKPoint(512f, 724f), rx: 60f, ry: 27f, NeonViolet, strokeMult);
        }
        else if (lod == IconLodTier.Medium)
        {
            DrawPerspectiveTacticalRing(canvas, new SKPoint(302f, 768f), rx: 52f, ry: 25f, NeonCoral, strokeMult, simplified: true);
            DrawPerspectiveTacticalRing(canvas, new SKPoint(722f, 768f), rx: 52f, ry: 25f, NeonGold, strokeMult, simplified: true);
            DrawPerspectiveTacticalRing(canvas, new SKPoint(512f, 726f), rx: 56f, ry: 26f, NeonViolet, strokeMult, simplified: true);
        }
        else
        {
            DrawPerspectiveTacticalRing(canvas, new SKPoint(308f, 756f), rx: 52f, ry: 26f, NeonCoral, strokeMult * 0.9f, simplified: true);
            DrawPerspectiveTacticalRing(canvas, new SKPoint(716f, 756f), rx: 52f, ry: 26f, NeonGold, strokeMult * 0.9f, simplified: true);
        }
    }

    private static void DrawHolographicProjection(SKCanvas canvas, IconLodTier lod, float strokeMult)
    {
        using (var coneBuilder = new SKPathBuilder())
        {
            coneBuilder.MoveTo(452f, 720f);
            coneBuilder.LineTo(318f, 464f);
            coneBuilder.LineTo(706f, 464f);
            coneBuilder.LineTo(572f, 720f);
            coneBuilder.Close();
            using var conePath = coneBuilder.Detach();

            using var conePaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
            using var coneGrad = SKShader.CreateLinearGradient(
                new SKPoint(512f, 720f),
                new SKPoint(512f, 440f),
                [NeonViolet.WithAlpha(56), NeonSkyCyan.WithAlpha(26), SKColors.Transparent],
                [0.0f, 0.6f, 1.0f],
                SKShaderTileMode.Clamp);
            conePaint.Shader = coneGrad;
            canvas.DrawPath(conePath, conePaint);
        }

        if (lod == IconLodTier.Hero)
        {
            DrawDashedNeonRay(canvas, new SKPoint(450f, 714f), new SKPoint(324f, 472f), NeonViolet, 7.8f * strokeMult);
            DrawDashedNeonRay(canvas, new SKPoint(574f, 714f), new SKPoint(700f, 472f), NeonEmerald, 7.8f * strokeMult);
        }
    }

    private static void DrawFloatingNeonD20(SKCanvas canvas, IconLodTier lod, float strokeMult, int targetPixelSize)
    {
        var center = lod == IconLodTier.Micro ? new SKPoint(512f, 356f) : new SKPoint(512f, 352f);
        float radius = lod == IconLodTier.Micro ? 236f : 224f;
        float innerRadius = radius * 0.59f;

        // 6 Outer Hexagon Vertices (Pointy-Top: 0=Top, 1=TopRight, 2=BottomRight, 3=Bottom, 4=BottomLeft, 5=TopLeft)
        var hex = new SKPoint[6];
        for (int i = 0; i < 6; i++)
        {
            float rad = (-90f + i * 60f) * MathF.PI / 180f;
            hex[i] = new SKPoint(center.X + MathF.Cos(rad) * radius, center.Y + MathF.Sin(rad) * radius);
        }

        // 3 Inner Upright Equilateral Triangle Vertices (0=Top, 1=BottomRight, 2=BottomLeft)
        var tri = new SKPoint[3];
        for (int i = 0; i < 3; i++)
        {
            float rad = (-90f + i * 120f) * MathF.PI / 180f;
            tri[i] = new SKPoint(center.X + MathF.Cos(rad) * innerRadius, center.Y + MathF.Sin(rad) * innerRadius);
        }

        // Translucent dark-sapphire crystal body fill
        using var hexBuilder = new SKPathBuilder();
        hexBuilder.MoveTo(hex[0]);
        for (int i = 1; i < 6; i++) hexBuilder.LineTo(hex[i]);
        hexBuilder.Close();
        using var hexPath = hexBuilder.Detach();

        using (var dieBodyFill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill })
        using (var dieGrad = SKShader.CreateLinearGradient(
            hex[0],
            hex[3],
            [new SKColor(0x16, 0x30, 0x56, 240), new SKColor(0x0B, 0x18, 0x30, 246)],
            null,
            SKShaderTileMode.Clamp))
        {
            dieBodyFill.Shader = dieGrad;
            canvas.DrawPath(hexPath, dieBodyFill);
        }

        // Facet Struts
        if (lod != IconLodTier.Micro)
        {
            DrawNeonSegment(canvas, tri[0], hex[0], NeonSkyCyan, 8.5f * strokeMult, 14f);
            DrawNeonSegment(canvas, tri[0], hex[1], NeonCoral, 8.2f * strokeMult, 14f);
            DrawNeonSegment(canvas, tri[0], hex[5], NeonEmerald, 8.2f * strokeMult, 14f);

            DrawNeonSegment(canvas, tri[1], hex[1], NeonGold, 8.2f * strokeMult, 14f);
            DrawNeonSegment(canvas, tri[1], hex[2], NeonGold, 8.2f * strokeMult, 14f);
            DrawNeonSegment(canvas, tri[1], hex[3], NeonSkyCyan, 8.2f * strokeMult, 14f);

            DrawNeonSegment(canvas, tri[2], hex[5], NeonViolet, 8.2f * strokeMult, 14f);
            DrawNeonSegment(canvas, tri[2], hex[4], NeonViolet, 8.2f * strokeMult, 14f);
            DrawNeonSegment(canvas, tri[2], hex[3], NeonEmerald, 8.2f * strokeMult, 14f);
        }
        else
        {
            DrawNeonSegment(canvas, tri[0], hex[0], NeonSkyCyan, 9.5f * strokeMult, 12f);
            DrawNeonSegment(canvas, tri[1], hex[2], NeonGold, 9.5f * strokeMult, 12f);
            DrawNeonSegment(canvas, tri[2], hex[4], NeonViolet, 9.5f * strokeMult, 12f);
        }

        // Clockwise Suite-Matched Multi-Neon Prism Perimeter (mirrors the Partner Mind Map color wheel!)
        // Top-Left: Emerald, Top-Right: Coral, Right: Gold, Bottom-Right: Sky-Cyan, Bottom-Left: Emerald, Left: Violet
        SKColor[] perimeterColors =
        [
            NeonCoral,   // hex[0] -> hex[1] (Top-Right)
            NeonGold,    // hex[1] -> hex[2] (Right)
            NeonSkyCyan, // hex[2] -> hex[3] (Bottom-Right)
            NeonEmerald, // hex[3] -> hex[4] (Bottom-Left)
            NeonViolet,  // hex[4] -> hex[5] (Left)
            NeonEmerald  // hex[5] -> hex[0] (Top-Left)
        ];

        for (int i = 0; i < 6; i++)
        {
            DrawNeonSegment(canvas, hex[i], hex[(i + 1) % 6], perimeterColors[i], 12.0f * strokeMult, 20f);
        }

        // Central Hero Equilateral Triangle (Blazing Ice-White + Cyan Bloom — identical focal signature to Partner Icon!)
        using var triBuilder = new SKPathBuilder();
        triBuilder.MoveTo(tri[0]);
        triBuilder.LineTo(tri[1]);
        triBuilder.LineTo(tri[2]);
        triBuilder.Close();
        using var triPath = triBuilder.Detach();

        using (var triFill = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Color = new SKColor(0x1B, 0x3D, 0x68, 155)
        })
        {
            canvas.DrawPath(triPath, triFill);
        }

        using (var cyanHalo = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeJoin = SKStrokeJoin.Round,
            Color = NeonSkyCyan.WithAlpha(195),
            StrokeWidth = 26f * strokeMult,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 17f)
        })
        {
            canvas.DrawPath(triPath, cyanHalo);
        }

        DrawNeonPath(canvas, triPath, NeonIceWhite, 13.5f * strokeMult, 16f);

        // Optically centered "20" Numeral on the Hero Central Face (>= 128px)
        if (lod == IconLodTier.Hero && targetPixelSize >= 128)
        {
            using var typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyleWeight.Black, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
            using var font = new SKFont(typeface, 52f) { Subpixel = true, Edging = SKFontEdging.Antialias };

            float textY = center.Y + 21f;
            using var textGlow = new SKPaint
            {
                IsAntialias = true,
                Color = NeonSkyCyan.WithAlpha(200),
                MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 8f)
            };
            using var textFill = new SKPaint
            {
                IsAntialias = true,
                Color = NeonIceWhite
            };

            canvas.DrawText("20", center.X, textY, SKTextAlign.Center, font, textGlow);
            canvas.DrawText("20", center.X, textY, SKTextAlign.Center, font, textFill);
        }
    }

    private static void DrawPerspectiveTacticalRing(
        SKCanvas canvas,
        SKPoint center,
        float rx,
        float ry,
        SKColor neonColor,
        float strokeMult,
        bool simplified = false)
    {
        var outerRect = new SKRect(center.X - rx, center.Y - ry, center.X + rx, center.Y + ry);

        using (var glow = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            Color = neonColor.WithAlpha(155),
            StrokeWidth = 18f * strokeMult,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 11f)
        })
        {
            canvas.DrawOval(outerRect, glow);
        }

        using (var darkCase = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            Color = DarkCasing,
            StrokeWidth = (simplified ? 15f : 16.5f) * strokeMult
        })
        {
            canvas.DrawOval(outerRect, darkCase);
        }

        using (var ringStroke = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            Color = neonColor,
            StrokeWidth = (simplified ? 9.5f : 9.0f) * strokeMult
        })
        {
            canvas.DrawOval(outerRect, ringStroke);
        }

        if (!simplified)
        {
            var innerRect = new SKRect(center.X - rx * 0.42f, center.Y - ry * 0.42f, center.X + rx * 0.42f, center.Y + ry * 0.42f);
            using var innerDark = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = DarkCasing };
            canvas.DrawOval(innerRect, innerDark);

            var pipRect = new SKRect(center.X - rx * 0.22f, center.Y - ry * 0.22f, center.X + rx * 0.22f, center.Y + ry * 0.22f);
            using var pipFill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = neonColor };
            canvas.DrawOval(pipRect, pipFill);
        }
    }

    private static void DrawDashedNeonRay(SKCanvas canvas, SKPoint a, SKPoint b, SKColor color, float width)
    {
        using var dashEffect = SKPathEffect.CreateDash([20f, 16f], 0f);

        using var glow = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeCap = SKStrokeCap.Round,
            Color = color.WithAlpha(135),
            StrokeWidth = width * 2.1f,
            PathEffect = dashEffect,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 9f)
        };
        canvas.DrawLine(a, b, glow);

        using var core = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeCap = SKStrokeCap.Round,
            Color = color,
            StrokeWidth = width,
            PathEffect = dashEffect
        };
        canvas.DrawLine(a, b, core);
    }

    private static void DrawNeonPath(SKCanvas canvas, SKPath path, SKColor color, float coreWidth, float blurSigma)
    {
        using (var glow = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeJoin = SKStrokeJoin.Round,
            StrokeCap = SKStrokeCap.Round,
            Color = color.WithAlpha(155),
            StrokeWidth = coreWidth * 2.1f,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, blurSigma)
        })
        {
            canvas.DrawPath(path, glow);
        }

        using (var casing = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeJoin = SKStrokeJoin.Round,
            StrokeCap = SKStrokeCap.Round,
            Color = DarkCasing.WithAlpha(210),
            StrokeWidth = coreWidth + 5.5f
        })
        {
            canvas.DrawPath(path, casing);
        }

        using (var core = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeJoin = SKStrokeJoin.Round,
            StrokeCap = SKStrokeCap.Round,
            Color = color,
            StrokeWidth = coreWidth
        })
        {
            canvas.DrawPath(path, core);
        }
    }

    private static void DrawNeonSegment(SKCanvas canvas, SKPoint a, SKPoint b, SKColor color, float coreWidth, float blurSigma)
    {
        using var pb = new SKPathBuilder();
        pb.MoveTo(a);
        pb.LineTo(b);
        using var path = pb.Detach();
        DrawNeonPath(canvas, path, color, coreWidth, blurSigma);
    }

    private static void DrawRadialGlow(SKCanvas canvas, SKPoint center, float radius, SKColor centerColor)
    {
        using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        using var shader = SKShader.CreateRadialGradient(
            center,
            radius,
            [centerColor, SKColors.Transparent],
            [0.0f, 1.0f],
            SKShaderTileMode.Clamp);
        paint.Shader = shader;
        canvas.DrawCircle(center, radius, paint);
    }

    private static SKPoint Lerp(SKPoint a, SKPoint b, float t) =>
        new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

    public static string BuildMasterSvg()
    {
        return """
               <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1024 1024" width="1024" height="1024">
                 <defs>
                   <radialGradient id="bgGrad" cx="50%" cy="44%" r="60%">
                     <stop offset="0%" stop-color="#12203B"/>
                     <stop offset="58%" stop-color="#0A1121"/>
                     <stop offset="100%" stop-color="#070C18"/>
                   </radialGradient>
                   <linearGradient id="rimGrad" x1="0%" y1="0%" x2="0%" y2="100%">
                     <stop offset="0%" stop-color="#36A2D8"/>
                     <stop offset="100%" stop-color="#288BBE"/>
                   </linearGradient>
                   <linearGradient id="holoCone" x1="0%" y1="100%" x2="0%" y2="0%">
                     <stop offset="0%" stop-color="#C084FC" stop-opacity="0.24"/>
                     <stop offset="60%" stop-color="#38BDF8" stop-opacity="0.10"/>
                     <stop offset="100%" stop-color="#38BDF8" stop-opacity="0"/>
                   </linearGradient>
                   <filter id="neonGlow" x="-25%" y="-25%" width="150%" height="150%">
                     <feGaussianBlur stdDeviation="10" result="blur"/>
                     <feMerge>
                       <feMergeNode in="blur"/>
                       <feMergeNode in="SourceGraphic"/>
                     </feMerge>
                   </filter>
                 </defs>

                 <!-- Squircle Backdrop -->
                 <rect x="48" y="48" width="928" height="928" rx="215" fill="url(#bgGrad)" stroke="url(#rimGrad)" stroke-width="14"/>
                 <rect x="64" y="64" width="896" height="896" rx="200" fill="none" stroke="#18304E" stroke-width="3"/>

                 <!-- Isometric Tabletop Battlemap Screen -->
                 <polygon points="146,852 878,852 863,878 161,878" fill="#0D1E36" stroke="#36A2D8" stroke-width="7.8" stroke-linejoin="round"/>
                 <polygon points="246,636 778,636 878,852 146,852" fill="#0E2338" stroke="#38BDF8" stroke-width="10" stroke-linejoin="round" filter="url(#neonGlow)"/>
                 <g stroke="#38E08F" stroke-opacity="0.76" stroke-width="3.6">
                   <line x1="379" y1="636" x2="329" y2="852"/>
                   <line x1="512" y1="636" x2="512" y2="852"/>
                   <line x1="645" y1="636" x2="695" y2="852"/>
                   <line x1="216" y1="701" x2="808" y2="701"/>
                   <line x1="181" y1="775" x2="843" y2="775"/>
                 </g>
                 <line x1="464" y1="865" x2="560" y2="865" stroke="#38E08F" stroke-width="6.8" stroke-linecap="round" filter="url(#neonGlow)"/>

                 <!-- 3 Concentric Tactical Suite Rings -->
                 <ellipse cx="306" cy="774" rx="48" ry="22" fill="#060B15" stroke="#FF6E7F" stroke-width="9" filter="url(#neonGlow)"/>
                 <ellipse cx="306" cy="774" rx="10" ry="5" fill="#FF6E7F"/>
                 <ellipse cx="718" cy="774" rx="48" ry="22" fill="#060B15" stroke="#FBBF24" stroke-width="9" filter="url(#neonGlow)"/>
                 <ellipse cx="718" cy="774" rx="10" ry="5" fill="#FBBF24"/>
                 <ellipse cx="512" cy="724" rx="60" ry="27" fill="#060B15" stroke="#C084FC" stroke-width="9" filter="url(#neonGlow)"/>
                 <ellipse cx="512" cy="724" rx="12" ry="6" fill="#C084FC"/>

                 <!-- Holographic Projection -->
                 <polygon points="452,720 318,464 706,464 572,720" fill="url(#holoCone)"/>
                 <line x1="450" y1="714" x2="324" y2="472" stroke="#C084FC" stroke-width="7.8" stroke-linecap="round" stroke-dasharray="20 16" filter="url(#neonGlow)"/>
                 <line x1="574" y1="714" x2="700" y2="472" stroke="#38E08F" stroke-width="7.8" stroke-linecap="round" stroke-dasharray="20 16" filter="url(#neonGlow)"/>

                 <!-- Floating Multi-Neon Prism Icosahedron (d20) -->
                 <polygon points="512,128 706,240 706,464 512,576 318,464 318,240" fill="#112442"/>
                 <g stroke-width="12" stroke-linecap="round" filter="url(#neonGlow)">
                   <line x1="512" y1="128" x2="706" y2="240" stroke="#FF6E7F"/>
                   <line x1="706" y1="240" x2="706" y2="464" stroke="#FBBF24"/>
                   <line x1="706" y1="464" x2="512" y2="576" stroke="#38BDF8"/>
                   <line x1="512" y1="576" x2="318" y2="464" stroke="#38E08F"/>
                   <line x1="318" y1="464" x2="318" y2="240" stroke="#C084FC"/>
                   <line x1="318" y1="240" x2="512" y2="128" stroke="#38E08F"/>
                 </g>
                 <polygon points="512,220 626.5,418 397.5,418" fill="#1B3D68" fill-opacity="0.6" stroke="#F0FCFF" stroke-width="13.5" stroke-linejoin="round" filter="url(#neonGlow)"/>
                 <text x="512" y="373" text-anchor="middle" font-family="Segoe UI, sans-serif" font-weight="900" font-size="52" fill="#F0FCFF" filter="url(#neonGlow)">20</text>
               </svg>
               """;
    }
}
