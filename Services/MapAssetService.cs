using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SkiaSharp;

namespace DiceChaosDisplay.Services;

public sealed record MapImageTile(
    Rect SourcePixelRect,
    Rect DestMapRect,
    BitmapSource Bitmap);

public sealed class LoadedMapAsset
{
    public required int PixelWidth { get; init; }
    public required int PixelHeight { get; init; }
    public required BitmapSource Thumbnail { get; init; }
    public required BitmapSource OverviewBitmap { get; init; }
    public required IReadOnlyList<MapImageTile> HighResTiles { get; init; }
    public required bool UsesTiledHighRes { get; init; }
}

public static class MapAssetService
{
    public const int OverviewMaxDimension = 2048;
    public const int ThumbnailMaxDimension = 256;
    public const int TileSize = 2048;
    public const int TileOverlap = 2;

    public static Task<LoadedMapAsset> LoadFromFileAsync(string filePath, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var skBitmap = DecodeSkBitmap(stream);
            return BuildPyramid(skBitmap, ct);
        }, ct);
    }

    public static Task<LoadedMapAsset> LoadFromBytesAsync(byte[] encodedBytes, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            using var ms = new MemoryStream(encodedBytes, writable: false);
            using var skBitmap = DecodeSkBitmap(ms);
            return BuildPyramid(skBitmap, ct);
        }, ct);
    }

    public static BitmapSource? LoadSimpleFrozenBitmap(string? filePath, int maxDim = 2048)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return null;

        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var sk = DecodeSkBitmap(stream);
            return CreateScaledFrozenBitmap(sk, maxDim);
        }
        catch
        {
            return null;
        }
    }

    private static SKBitmap DecodeSkBitmap(Stream stream)
    {
        using var codec = SKCodec.Create(stream)
            ?? throw new InvalidDataException("Unsupported or corrupted image format.");

        var info = new SKImageInfo(
            codec.Info.Width,
            codec.Info.Height,
            SKColorType.Bgra8888,
            SKAlphaType.Premul);

        var bitmap = new SKBitmap(info);
        var result = codec.GetPixels(info, bitmap.GetPixels());
        if (result != SKCodecResult.Success && result != SKCodecResult.IncompleteInput)
        {
            bitmap.Dispose();
            throw new InvalidDataException($"Failed to decode image pixels: {result}");
        }

        return bitmap;
    }

    private static LoadedMapAsset BuildPyramid(SKBitmap source, CancellationToken ct)
    {
        int w = source.Width;
        int h = source.Height;

        ct.ThrowIfCancellationRequested();
        BitmapSource thumbnail = CreateScaledFrozenBitmap(source, ThumbnailMaxDimension);

        ct.ThrowIfCancellationRequested();
        bool needsTiles = w > TileSize || h > TileSize;

        if (!needsTiles)
        {
            BitmapSource single = CopyRegionToFrozenBitmap(source, 0, 0, w, h);
            var singleTile = new MapImageTile(new Rect(0, 0, w, h), new Rect(0, 0, w, h), single);
            return new LoadedMapAsset
            {
                PixelWidth = w,
                PixelHeight = h,
                Thumbnail = thumbnail,
                OverviewBitmap = single,
                HighResTiles = [singleTile],
                UsesTiledHighRes = false
            };
        }

        BitmapSource overview = CreateScaledFrozenBitmap(source, OverviewMaxDimension);
        var tiles = new List<MapImageTile>();

        for (int y = 0; y < h; y += TileSize)
        {
            for (int x = 0; x < w; x += TileSize)
            {
                ct.ThrowIfCancellationRequested();

                // Expand by TileOverlap where possible to prevent subpixel GPU bilinear seams
                int srcX = Math.Max(0, x - TileOverlap);
                int srcY = Math.Max(0, y - TileOverlap);
                int srcRight = Math.Min(w, x + TileSize + TileOverlap);
                int srcBottom = Math.Min(h, y + TileSize + TileOverlap);
                int tileW = srcRight - srcX;
                int tileH = srcBottom - srcY;

                BitmapSource tileBmp = CopyRegionToFrozenBitmap(source, srcX, srcY, tileW, tileH);
                var rect = new Rect(srcX, srcY, tileW, tileH);
                tiles.Add(new MapImageTile(rect, rect, tileBmp));
            }
        }

        return new LoadedMapAsset
        {
            PixelWidth = w,
            PixelHeight = h,
            Thumbnail = thumbnail,
            OverviewBitmap = overview,
            HighResTiles = tiles,
            UsesTiledHighRes = true
        };
    }

    private static BitmapSource CreateScaledFrozenBitmap(SKBitmap source, int maxDimension)
    {
        int w = source.Width;
        int h = source.Height;

        if (w <= maxDimension && h <= maxDimension)
        {
            return CopyRegionToFrozenBitmap(source, 0, 0, w, h);
        }

        double scale = Math.Min((double)maxDimension / w, (double)maxDimension / h);
        int dstW = Math.Max(1, (int)Math.Round(w * scale));
        int dstH = Math.Max(1, (int)Math.Round(h * scale));

        var dstInfo = new SKImageInfo(dstW, dstH, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var resized = new SKBitmap(dstInfo);
        source.ScalePixels(resized, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));

        return CopyRegionToFrozenBitmap(resized, 0, 0, dstW, dstH);
    }

    private static unsafe BitmapSource CopyRegionToFrozenBitmap(SKBitmap source, int srcX, int srcY, int width, int height)
    {
        var wb = new WriteableBitmap(width, height, 96.0, 96.0, PixelFormats.Pbgra32, null);
        wb.Lock();
        try
        {
            byte* srcBase = (byte*)source.GetPixels().ToPointer();
            int srcStride = source.RowBytes;

            byte* dstBase = (byte*)wb.BackBuffer.ToPointer();
            int dstStride = wb.BackBufferStride;
            int rowBytes = width * 4;

            for (int row = 0; row < height; row++)
            {
                byte* srcRow = srcBase + (long)(srcY + row) * srcStride + (long)srcX * 4;
                byte* dstRow = dstBase + (long)row * dstStride;
                Buffer.MemoryCopy(srcRow, dstRow, dstStride, rowBytes);
            }

            wb.AddDirtyRect(new Int32Rect(0, 0, width, height));
        }
        finally
        {
            wb.Unlock();
        }

        wb.Freeze();
        return wb;
    }
}
