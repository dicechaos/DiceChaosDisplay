using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DiceChaosDisplay.Models;
using SkiaSharp;

namespace DiceChaosDisplay.Services;

/// <summary>
/// High-performance zero-copy Fog of War engine.
/// Renders anti-aliased strokes, soft-feathered brushes, boxes, and room polygons directly
/// into a resolution-capped Pbgra32 WriteableBitmap BackBuffer via SkiaSharp SKSurface.
/// </summary>
public sealed class FogEngine
{
    public const int MaxMaskDimension = 2048;
    public const int MaxUndoSteps = 25;

    private readonly int _mapPixelWidth;
    private readonly int _mapPixelHeight;
    private readonly int _maskWidth;
    private readonly int _maskHeight;
    private readonly double _scaleX;
    private readonly double _scaleY;

    private readonly Stack<byte[]> _undoStack = new();
    private readonly Stack<byte[]> _redoStack = new();

    private Point _lastStrokeMaskPt;
    private bool _isStrokeActive;

    public WriteableBitmap FogBitmap { get; }

    public int MaskWidth => _maskWidth;
    public int MaskHeight => _maskHeight;
    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    public event EventHandler? UndoRedoStateChanged;

    public FogEngine(int mapPixelWidth, int mapPixelHeight, string? initialPngBase64 = null)
    {
        _mapPixelWidth = Math.Max(1, mapPixelWidth);
        _mapPixelHeight = Math.Max(1, mapPixelHeight);

        double ratio = Math.Min(
            1.0,
            Math.Min((double)MaxMaskDimension / _mapPixelWidth, (double)MaxMaskDimension / _mapPixelHeight));

        _maskWidth = Math.Max(1, (int)Math.Round(_mapPixelWidth * ratio));
        _maskHeight = Math.Max(1, (int)Math.Round(_mapPixelHeight * ratio));
        _scaleX = (double)_maskWidth / _mapPixelWidth;
        _scaleY = (double)_maskHeight / _mapPixelHeight;

        FogBitmap = new WriteableBitmap(_maskWidth, _maskHeight, 96.0, 96.0, PixelFormats.Pbgra32, null);

        if (!string.IsNullOrWhiteSpace(initialPngBase64) && TryImportPngBase64(initialPngBase64))
        {
            return;
        }

        FillInternal(revealed: false);
    }

    public void RevealAll()
    {
        PushUndoSnapshot();
        FillInternal(revealed: true);
    }

    public void HideAll()
    {
        PushUndoSnapshot();
        FillInternal(revealed: false);
    }

    public void BeginBrushStroke(Point mapPoint, double mapBrushRadius, FogPaintOperation op, double softnessFraction)
    {
        PushUndoSnapshot();
        _isStrokeActive = true;
        _lastStrokeMaskPt = ToMaskPoint(mapPoint);
        DrawBrushSegment(_lastStrokeMaskPt, _lastStrokeMaskPt, mapBrushRadius, op, softnessFraction);
    }

    public void ContinueBrushStroke(Point mapPoint, double mapBrushRadius, FogPaintOperation op, double softnessFraction)
    {
        if (!_isStrokeActive) return;
        Point nextMaskPt = ToMaskPoint(mapPoint);
        double dx = nextMaskPt.X - _lastStrokeMaskPt.X;
        double dy = nextMaskPt.Y - _lastStrokeMaskPt.Y;
        if (dx * dx + dy * dy < 0.5) return;

        DrawBrushSegment(_lastStrokeMaskPt, nextMaskPt, mapBrushRadius, op, softnessFraction);
        _lastStrokeMaskPt = nextMaskPt;
    }

    public void EndBrushStroke()
    {
        _isStrokeActive = false;
    }

    public void ApplyRectangle(Rect mapRect, FogPaintOperation op, double softnessFraction = 0.0)
    {
        if (mapRect.Width < 0.5 || mapRect.Height < 0.5) return;
        PushUndoSnapshot();

        float left = (float)(mapRect.Left * _scaleX);
        float top = (float)(mapRect.Top * _scaleY);
        float right = (float)(mapRect.Right * _scaleX);
        float bottom = (float)(mapRect.Bottom * _scaleY);
        float sigma = ComputeBlurSigma(Math.Min(right - left, bottom - top) * 0.25f, softnessFraction);

        FogBitmap.Lock();
        try
        {
            using var surface = CreateBackBufferSurface();
            using var paint = CreateFogPaint(op, sigma);
            paint.Style = SKPaintStyle.Fill;
            surface.Canvas.DrawRect(SKRect.Create(left, top, right - left, bottom - top), paint);
            MarkDirty(left - sigma * 3, top - sigma * 3, right + sigma * 3, bottom + sigma * 3);
        }
        finally
        {
            FogBitmap.Unlock();
        }
    }

    public void ApplyPolygon(IReadOnlyList<Point2D> mapPoints, FogPaintOperation op, double softnessFraction = 0.0, bool recordUndo = true)
    {
        if (mapPoints.Count < 3) return;
        if (recordUndo) PushUndoSnapshot();

        using var pathBuilder = new SKPathBuilder();
        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;

        for (int i = 0; i < mapPoints.Count; i++)
        {
            float mx = (float)(mapPoints[i].X * _scaleX);
            float my = (float)(mapPoints[i].Y * _scaleY);
            if (i == 0) pathBuilder.MoveTo(mx, my);
            else pathBuilder.LineTo(mx, my);

            if (mx < minX) minX = mx;
            if (my < minY) minY = my;
            if (mx > maxX) maxX = mx;
            if (my > maxY) maxY = my;
        }
        pathBuilder.Close();
        using var path = pathBuilder.Detach();

        float sigma = ComputeBlurSigma(12f, softnessFraction);

        FogBitmap.Lock();
        try
        {
            using var surface = CreateBackBufferSurface();
            using var paint = CreateFogPaint(op, sigma);
            paint.Style = SKPaintStyle.Fill;
            surface.Canvas.DrawPath(path, paint);
            MarkDirty(minX - sigma * 3 - 2, minY - sigma * 3 - 2, maxX + sigma * 3 + 2, maxY + sigma * 3 + 2);
        }
        finally
        {
            FogBitmap.Unlock();
        }
    }

    public bool Undo()
    {
        if (_undoStack.Count == 0) return false;
        _redoStack.Push(CaptureCompressedAlpha());
        byte[] prev = _undoStack.Pop();
        RestoreCompressedAlpha(prev);
        UndoRedoStateChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool Redo()
    {
        if (_redoStack.Count == 0) return false;
        _undoStack.Push(CaptureCompressedAlpha());
        byte[] next = _redoStack.Pop();
        RestoreCompressedAlpha(next);
        UndoRedoStateChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Samples the fog alpha [0..255] at a map pixel coordinate (useful for testing & room state).
    /// 0 = completely revealed, 255 = completely hidden.
    /// </summary>
    public unsafe byte SampleAlphaAtMapPoint(double mapX, double mapY)
    {
        int mx = Math.Clamp((int)(mapX * _scaleX), 0, _maskWidth - 1);
        int my = Math.Clamp((int)(mapY * _scaleY), 0, _maskHeight - 1);

        FogBitmap.Lock();
        try
        {
            byte* ptr = (byte*)FogBitmap.BackBuffer.ToPointer();
            int offset = my * FogBitmap.BackBufferStride + mx * 4 + 3;
            return ptr[offset];
        }
        finally
        {
            FogBitmap.Unlock();
        }
    }

    public string ExportPngBase64()
    {
        FogBitmap.Lock();
        try
        {
            var info = new SKImageInfo(_maskWidth, _maskHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var image = SKImage.FromPixels(info, FogBitmap.BackBuffer, FogBitmap.BackBufferStride);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            return Convert.ToBase64String(data.ToArray());
        }
        finally
        {
            FogBitmap.Unlock();
        }
    }

    public unsafe bool TryImportPngBase64(string base64)
    {
        try
        {
            byte[] bytes = Convert.FromBase64String(base64);
            using var ms = new MemoryStream(bytes);
            using var decoded = SKBitmap.Decode(ms);
            if (decoded is null) return false;

            FogBitmap.Lock();
            try
            {
                using var surface = CreateBackBufferSurface();
                surface.Canvas.Clear(SKColors.Transparent);
                var destRect = SKRect.Create(0, 0, _maskWidth, _maskHeight);
                using var img = SKImage.FromBitmap(decoded);
                surface.Canvas.DrawImage(img, destRect, new SKSamplingOptions(SKFilterMode.Linear));
                FogBitmap.AddDirtyRect(new Int32Rect(0, 0, _maskWidth, _maskHeight));
            }
            finally
            {
                FogBitmap.Unlock();
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void PushUndoSnapshot()
    {
        byte[] compressed = CaptureCompressedAlpha();
        if (_undoStack.Count >= MaxUndoSteps)
        {
            // Trim oldest item while preserving stack order
            var arr = _undoStack.ToArray(); // top-first
            _undoStack.Clear();
            for (int i = MaxUndoSteps - 2; i >= 0; i--)
                _undoStack.Push(arr[i]);
        }
        _undoStack.Push(compressed);
        _redoStack.Clear();
        UndoRedoStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private unsafe byte[] CaptureCompressedAlpha()
    {
        byte[] alpha = new byte[_maskWidth * _maskHeight];
        FogBitmap.Lock();
        try
        {
            byte* basePtr = (byte*)FogBitmap.BackBuffer.ToPointer();
            int stride = FogBitmap.BackBufferStride;
            int idx = 0;
            for (int y = 0; y < _maskHeight; y++)
            {
                byte* row = basePtr + y * stride;
                for (int x = 0; x < _maskWidth; x++)
                {
                    alpha[idx++] = row[x * 4 + 3];
                }
            }
        }
        finally
        {
            FogBitmap.Unlock();
        }

        using var ms = new MemoryStream();
        using (var brotli = new BrotliStream(ms, CompressionLevel.Fastest, leaveOpen: true))
        {
            brotli.Write(alpha, 0, alpha.Length);
        }
        return ms.ToArray();
    }

    private unsafe void RestoreCompressedAlpha(byte[] compressed)
    {
        byte[] alpha = new byte[_maskWidth * _maskHeight];
        using (var ms = new MemoryStream(compressed))
        using (var brotli = new BrotliStream(ms, CompressionMode.Decompress))
        {
            int total = 0;
            while (total < alpha.Length)
            {
                int read = brotli.Read(alpha, total, alpha.Length - total);
                if (read == 0) break;
                total += read;
            }
        }

        FogBitmap.Lock();
        try
        {
            byte* basePtr = (byte*)FogBitmap.BackBuffer.ToPointer();
            int stride = FogBitmap.BackBufferStride;
            int idx = 0;
            for (int y = 0; y < _maskHeight; y++)
            {
                uint* row = (uint*)(basePtr + y * stride);
                for (int x = 0; x < _maskWidth; x++)
                {
                    uint a = alpha[idx++];
                    // Premultiplied black (B=0, G=0, R=0, A=a) in little-endian BGRA32 is (a << 24)
                    row[x] = a << 24;
                }
            }
            FogBitmap.AddDirtyRect(new Int32Rect(0, 0, _maskWidth, _maskHeight));
        }
        finally
        {
            FogBitmap.Unlock();
        }
    }

    private unsafe void FillInternal(bool revealed)
    {
        uint pixelValue = revealed ? 0x00000000u : 0xFF000000u;
        FogBitmap.Lock();
        try
        {
            byte* basePtr = (byte*)FogBitmap.BackBuffer.ToPointer();
            int stride = FogBitmap.BackBufferStride;
            for (int y = 0; y < _maskHeight; y++)
            {
                var rowSpan = new Span<uint>(basePtr + y * stride, _maskWidth);
                rowSpan.Fill(pixelValue);
            }
            FogBitmap.AddDirtyRect(new Int32Rect(0, 0, _maskWidth, _maskHeight));
        }
        finally
        {
            FogBitmap.Unlock();
        }
    }

    private void DrawBrushSegment(Point p0, Point p1, double mapRadius, FogPaintOperation op, double softnessFraction)
    {
        float maskRadius = Math.Max(1.0f, (float)(mapRadius * (_scaleX + _scaleY) * 0.5));
        float sigma = ComputeBlurSigma(maskRadius, softnessFraction);

        FogBitmap.Lock();
        try
        {
            using var surface = CreateBackBufferSurface();
            using var paint = CreateFogPaint(op, sigma);
            paint.Style = SKPaintStyle.Stroke;
            paint.StrokeWidth = maskRadius * 2.0f;
            paint.StrokeCap = SKStrokeCap.Round;
            paint.StrokeJoin = SKStrokeJoin.Round;

            if (Math.Abs(p0.X - p1.X) < 0.01 && Math.Abs(p0.Y - p1.Y) < 0.01)
            {
                paint.Style = SKPaintStyle.Fill;
                surface.Canvas.DrawCircle((float)p0.X, (float)p0.Y, maskRadius, paint);
            }
            else
            {
                surface.Canvas.DrawLine((float)p0.X, (float)p0.Y, (float)p1.X, (float)p1.Y, paint);
            }

            float pad = maskRadius + sigma * 3.0f + 3.0f;
            float minX = (float)Math.Min(p0.X, p1.X) - pad;
            float minY = (float)Math.Min(p0.Y, p1.Y) - pad;
            float maxX = (float)Math.Max(p0.X, p1.X) + pad;
            float maxY = (float)Math.Max(p0.Y, p1.Y) + pad;
            MarkDirty(minX, minY, maxX, maxY);
        }
        finally
        {
            FogBitmap.Unlock();
        }
    }

    private SKSurface CreateBackBufferSurface()
    {
        var info = new SKImageInfo(_maskWidth, _maskHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
        return SKSurface.Create(info, FogBitmap.BackBuffer, FogBitmap.BackBufferStride);
    }

    private static SKPaint CreateFogPaint(FogPaintOperation op, float blurSigma)
    {
        var paint = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(0, 0, 0, 255),
            // DstOut multiplies existing fog alpha by (1 - srcAlpha), giving crisp anti-aliased edges and smooth Gaussian feathering!
            BlendMode = op == FogPaintOperation.Reveal ? SKBlendMode.DstOut : SKBlendMode.SrcOver
        };

        if (blurSigma > 0.25f)
        {
            paint.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, blurSigma);
        }

        return paint;
    }

    private static float ComputeBlurSigma(float baseRadius, double softnessFraction)
    {
        double clamped = Math.Clamp(softnessFraction, 0.0, 1.0);
        if (clamped <= 0.01) return 0f;
        return Math.Max(0.5f, (float)(baseRadius * 0.45 * clamped));
    }

    private void MarkDirty(float minX, float minY, float maxX, float maxY)
    {
        int x0 = Math.Clamp((int)Math.Floor(minX), 0, _maskWidth);
        int y0 = Math.Clamp((int)Math.Floor(minY), 0, _maskHeight);
        int x1 = Math.Clamp((int)Math.Ceiling(maxX), 0, _maskWidth);
        int y1 = Math.Clamp((int)Math.Ceiling(maxY), 0, _maskHeight);

        int w = x1 - x0;
        int h = y1 - y0;
        if (w > 0 && h > 0)
        {
            FogBitmap.AddDirtyRect(new Int32Rect(x0, y0, w, h));
        }
    }

    private Point ToMaskPoint(Point mapPt) => new(mapPt.X * _scaleX, mapPt.Y * _scaleY);
}
