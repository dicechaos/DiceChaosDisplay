using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using DiceChaosDisplay.Models;
using DiceChaosDisplay.Services;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace DiceChaosDisplay.Tests;

public sealed class EngineAnd8KStressTests
{
    private readonly ITestOutputHelper _output;

    public EngineAnd8KStressTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(90.0)]
    [InlineData(180.0)]
    [InlineData(270.0)]
    public void ViewportMath_MapToScreen_And_ScreenToMap_RoundTrip_AllRotations(double rotation)
    {
        var cam = new CameraState(CenterMapX: 3840, CenterMapY: 2160, Zoom: 0.65, RotationDegrees: rotation);
        var viewport = new Size(1920, 1080);
        var originalMapPt = new Point(4125.75, 1890.25);

        Point screenPt = ViewportMath.MapToScreen(originalMapPt, cam, viewport);
        Point roundTrip = ViewportMath.ScreenToMap(screenPt, cam, viewport);

        Assert.Equal(originalMapPt.X, roundTrip.X, precision: 5);
        Assert.Equal(originalMapPt.Y, roundTrip.Y, precision: 5);
    }

    [Fact]
    public void ViewportMath_ZoomAroundCursor_KeepsAnchorPixelStationary()
    {
        var cam = new CameraState(2500, 1800, 0.45, 0);
        var viewport = new Size(1280, 720);
        var cursorScreen = new Point(312.4, 518.9);

        Point mapUnderCursorBefore = ViewportMath.ScreenToMap(cursorScreen, cam, viewport);

        CameraState zoomedIn = ViewportMath.ZoomAroundScreenPoint(cam, viewport, cursorScreen, 1.5);
        Point mapUnderCursorAfterIn = ViewportMath.ScreenToMap(cursorScreen, zoomedIn, viewport);

        CameraState zoomedOut = ViewportMath.ZoomAroundScreenPoint(zoomedIn, viewport, cursorScreen, 0.35);
        Point mapUnderCursorAfterOut = ViewportMath.ScreenToMap(cursorScreen, zoomedOut, viewport);

        Assert.Equal(mapUnderCursorBefore.X, mapUnderCursorAfterIn.X, precision: 5);
        Assert.Equal(mapUnderCursorBefore.Y, mapUnderCursorAfterIn.Y, precision: 5);
        Assert.Equal(mapUnderCursorBefore.X, mapUnderCursorAfterOut.X, precision: 5);
        Assert.Equal(mapUnderCursorBefore.Y, mapUnderCursorAfterOut.Y, precision: 5);
    }

    [Fact]
    public void ViewportMath_Rotated90DegreesTv_SwapsVisibleMapAspectToPortrait()
    {
        var tvSize = new Size(1920, 1080); // Landscape TV
        var landscapeCam = new CameraState(2000, 2000, 1.0, 0.0);
        var portraitCam = new CameraState(2000, 2000, 1.0, 90.0);

        Rect bounds0 = ViewportMath.GetVisibleMapBounds(landscapeCam, tvSize);
        Rect bounds90 = ViewportMath.GetVisibleMapBounds(portraitCam, tvSize);

        Assert.Equal(1920, bounds0.Width, precision: 3);
        Assert.Equal(1080, bounds0.Height, precision: 3);

        // At 90 degrees, TV shows 1080 map px horizontally and 1920 map px vertically!
        Assert.Equal(1080, bounds90.Width, precision: 3);
        Assert.Equal(1920, bounds90.Height, precision: 3);
    }

    [Theory]
    [InlineData(96u)]   // 100% Windows scaling
    [InlineData(120u)]  // 125% Windows scaling
    [InlineData(144u)]  // 150% Windows scaling
    [InlineData(192u)]  // 200% Windows scaling
    public void DisplayCalibration_OneInchPhysicalScale_ExactAcrossAllWindowsDpiSettings(uint windowsEffectiveDpi)
    {
        // 50-inch 4K TV (3840x2160)
        double physicalPpi = DisplayCalibrationService.CalculatePhysicalPpi(3840, 2160, 50.0);
        double mapPixelsPerCell = 140.0;

        double zoom = DisplayCalibrationService.CalculateOneInchZoom(physicalPpi, windowsEffectiveDpi, mapPixelsPerCell);

        // Rendered physical pixels on the TV glass = mapPixelsPerCell * zoom * (windowsEffectiveDpi / 96.0)
        double renderedPhysicalPixelsPerSquare = mapPixelsPerCell * zoom * (windowsEffectiveDpi / 96.0);

        // Must equal exactly 1.0 physical inch (physicalPpi pixels)!
        Assert.Equal(physicalPpi, renderedPhysicalPixelsPerSquare, precision: 5);
    }

    [Fact]
    public void RulerDistance_Supports5eStandard_5_10_5_And_Euclidean()
    {
        var grid = new GridSettings { PixelsPerCell = 100, FeetPerCell = 5 };
        Point a = new(0, 0);
        Point b = new(400, 400); // 4 squares right, 4 squares down

        grid.DiagonalRule = DiagonalRule.Standard5e_5_5_5;
        Assert.Equal(20.0, ViewportMath.MeasureDistanceFeet(a, b, grid), precision: 3);

        grid.DiagonalRule = DiagonalRule.Variant_5_10_5;
        Assert.Equal(30.0, ViewportMath.MeasureDistanceFeet(a, b, grid), precision: 3);

        grid.DiagonalRule = DiagonalRule.Euclidean;
        Assert.Equal(Math.Sqrt(32) * 5.0, ViewportMath.MeasureDistanceFeet(a, b, grid), precision: 3);
    }

    [Fact]
    public void FogEngine_8KStressBenchmark_BrushPolygonFeatherUndoAndPngPersistence()
    {
        RunOnSta(() =>
        {
            var fog = new FogEngine(7680, 4320);

            // Capped proportionally at 2048x1152 for 8K 16:9!
            Assert.Equal(2048, fog.MaskWidth);
            Assert.Equal(1152, fog.MaskHeight);
            Assert.Equal(255, fog.SampleAlphaAtMapPoint(3840, 2160));

            // Benchmark 100 brush segments across 8K map with soft Gaussian feathering
            var sw = Stopwatch.StartNew();
            fog.BeginBrushStroke(new Point(500, 500), mapBrushRadius: 150, FogPaintOperation.Reveal, softnessFraction: 0.35);
            for (int i = 1; i <= 100; i++)
            {
                fog.ContinueBrushStroke(new Point(500 + i * 50, 500 + i * 25), 150, FogPaintOperation.Reveal, 0.35);
            }
            fog.EndBrushStroke();
            sw.Stop();

            double avgMsPerStrokeStep = sw.Elapsed.TotalMilliseconds / 100.0;
            _output.WriteLine($"8K Fog Brush 100 feathered segments total: {sw.Elapsed.TotalMilliseconds:0.2} ms ({avgMsPerStrokeStep:0.3} ms/step)");
            Assert.True(avgMsPerStrokeStep < 5.0, $"Expected <5ms per stroke step, got {avgMsPerStrokeStep:0.2}ms");

            // Center of stroke should be fully revealed (alpha == 0)
            Assert.Equal(0, fog.SampleAlphaAtMapPoint(1500, 1000));

            // Test Undo & Redo
            Assert.True(fog.Undo());
            Assert.Equal(255, fog.SampleAlphaAtMapPoint(1500, 1000));
            Assert.True(fog.Redo());
            Assert.Equal(0, fog.SampleAlphaAtMapPoint(1500, 1000));

            // Test 1-Click Polygon Room Reveal
            var roomPts = new List<Point2D>
            {
                new(6000, 3000),
                new(6800, 3000),
                new(6800, 3800),
                new(6000, 3800)
            };
            fog.ApplyPolygon(roomPts, FogPaintOperation.Reveal, softnessFraction: 0.0);
            Assert.Equal(0, fog.SampleAlphaAtMapPoint(6400, 3400));
            Assert.Equal(255, fog.SampleAlphaAtMapPoint(7200, 3400));

            // Test PNG Base64 round-trip persistence
            string b64 = fog.ExportPngBase64();
            var restored = new FogEngine(7680, 4320, b64);
            Assert.Equal(0, restored.SampleAlphaAtMapPoint(6400, 3400));
            Assert.Equal(255, restored.SampleAlphaAtMapPoint(7200, 3400));
        });
    }

    [Fact]
    public async Task MapAssetService_8KSyntheticImage_Builds2048OverviewAndFrozenHighResTiles()
    {
        // Generate a genuine 8K (7680 x 4320) WebP test map in memory
        byte[] webp8KBytes;
        using (var skBmp = new SKBitmap(new SKImageInfo(7680, 4320, SKColorType.Bgra8888, SKAlphaType.Premul)))
        using (var canvas = new SKCanvas(skBmp))
        {
            canvas.Clear(new SKColor(32, 38, 48));
            using var paint = new SKPaint { Color = new SKColor(210, 175, 95), StrokeWidth = 8, Style = SKPaintStyle.Stroke };
            canvas.DrawRect(200, 200, 7280, 3920, paint);
            using var img = SKImage.FromBitmap(skBmp);
            using var encoded = img.Encode(SKEncodedImageFormat.Webp, 80);
            webp8KBytes = encoded.ToArray();
        }

        var sw = Stopwatch.StartNew();
        LoadedMapAsset asset = await MapAssetService.LoadFromBytesAsync(webp8KBytes);
        sw.Stop();
        _output.WriteLine($"8K (7680x4320) WebP decode + 2K tile pyramid build: {sw.Elapsed.TotalMilliseconds:0.1} ms, Tiles: {asset.HighResTiles.Count}");

        Assert.Equal(7680, asset.PixelWidth);
        Assert.Equal(4320, asset.PixelHeight);
        Assert.True(asset.UsesTiledHighRes);
        Assert.Equal(2048, asset.OverviewBitmap.PixelWidth);
        Assert.Equal(1152, asset.OverviewBitmap.PixelHeight);
        Assert.True(asset.OverviewBitmap.IsFrozen);
        Assert.Equal(12, asset.HighResTiles.Count); // ceil(7680/2048)=4 cols * ceil(4320/2048)=3 rows = 12 tiles
        Assert.All(asset.HighResTiles, t => Assert.True(t.Bitmap.IsFrozen));
    }

    [Fact]
    public async Task UvttAndCampaignStorage_RoundTrip_PreservesWallsGridRoomsAndRelativePaths()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "DiceChaosTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            // Create a small synthetic PNG for UVTT embedding
            string imgB64;
            using (var bmp = new SKBitmap(280, 280))
            using (var canvas = new SKCanvas(bmp))
            {
                canvas.Clear(SKColors.DarkSlateGray);
                using var img = SKImage.FromBitmap(bmp);
                using var data = img.Encode(SKEncodedImageFormat.Png, 90);
                imgB64 = Convert.ToBase64String(data.ToArray());
            }

            var uvttDoc = new UvttDocument
            {
                Format = 0.3,
                Resolution = new UvttResolution
                {
                    MapOrigin = new UvttPoint { X = 0, Y = 0 },
                    MapSize = new UvttPoint { X = 2, Y = 2 },
                    PixelsPerGrid = 140
                },
                LineOfSight =
                [
                    [new UvttPoint { X = 0, Y = 0 }, new UvttPoint { X = 2, Y = 0 }]
                ],
                Portals =
                [
                    new UvttPortal
                    {
                        Bounds = [new UvttPoint { X = 1, Y = 0 }, new UvttPoint { X = 1, Y = 1 }],
                        Closed = true
                    }
                ],
                ImageBase64 = imgB64
            };

            string uvttPath = Path.Combine(tempDir, "GoblinCave.dd2vtt");
            await File.WriteAllTextAsync(uvttPath, JsonSerializer.Serialize(uvttDoc));

            var imported = await UvttImportService.ImportAsync(uvttPath);
            Assert.Equal(140.0, imported.Scene.Grid.PixelsPerCell);
            Assert.Single(imported.Scene.UvttWalls);
            Assert.Single(imported.Scene.UvttPortals);
            Assert.Equal(280.0, imported.Scene.UvttWalls[0][1].X);

            // Save & reload inside a .dicechaos campaign
            imported.Scene.Rooms.Add(new FogRoomRegion
            {
                Name = "Boss Chamber",
                Points = [new(10, 10), new(200, 10), new(200, 200)],
                IsRevealed = true
            });
            imported.Scene.DmPins.Add(new DmPinModel
            {
                Code = "T1",
                Title = "Pit Trap",
                Notes = "DC 15 Perception"
            });

            var project = new CampaignProject
            {
                Name = "Phandelver",
                Scenes = [imported.Scene],
                Handouts =
                [
                    new HandoutItemModel
                    {
                        Title = "Cragmaw Goblin Boss",
                        ImageFilePath = uvttPath
                    }
                ]
            };

            string campaignFile = Path.Combine(tempDir, "Phandelver.dicechaos");
            await CampaignStorageService.SaveCampaignAsync(project, campaignFile);

            // Verify saved file stored a relative path ("GoblinCave.dd2vtt") rather than hardcoded drive letter!
            string rawSavedJson = await File.ReadAllTextAsync(campaignFile);
            Assert.Contains("\"GoblinCave.dd2vtt\"", rawSavedJson);

            var reloaded = await CampaignStorageService.LoadCampaignAsync(campaignFile);
            Assert.Equal("Phandelver", reloaded.Name);
            Assert.Single(reloaded.Scenes);
            Assert.Single(reloaded.Handouts);
            Assert.Equal(uvttPath, reloaded.Scenes[0].MediaFilePath);
            Assert.Equal(uvttPath, reloaded.Handouts[0].ImageFilePath);
            Assert.Equal("Cragmaw Goblin Boss", reloaded.Handouts[0].Title);
            Assert.Equal("Boss Chamber", reloaded.Scenes[0].Rooms[0].Name);
            Assert.Equal("DC 15 Perception", reloaded.Scenes[0].DmPins[0].Notes);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void AppIconGenerator_ProducesValidMultiFrameTransparentIcoAndMasterAssets()
    {
        string repoAssetsDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Assets"));
        Assert.True(File.Exists(Path.Combine(repoAssetsDir, "AppIcon.ico")));

        string tempOutDir = Path.Combine(Path.GetTempPath(), $"DiceChaosIconTest_{Guid.NewGuid():N}");
        try
        {
            AppIconGenerator.GenerateAllProjectAssets(tempOutDir);

            string icoPath = Path.Combine(tempOutDir, "AppIcon.ico");
            string png256Path = Path.Combine(tempOutDir, "AppIcon-256.png");
            string png1024Path = Path.Combine(tempOutDir, "AppIcon-1024.png");
            string svgPath = Path.Combine(tempOutDir, "AppIcon.svg");

            Assert.True(File.Exists(icoPath));
            Assert.True(File.Exists(png256Path));
            Assert.True(File.Exists(png1024Path));
            Assert.True(File.Exists(svgPath));

            // Verify ICO binary directory header & all 7 embedded PNG frames (16, 24, 32, 48, 64, 128, 256)
            byte[] icoBytes = File.ReadAllBytes(icoPath);
            using var ms = new MemoryStream(icoBytes);
            using var br = new BinaryReader(ms);

            Assert.Equal(0, br.ReadUInt16()); // Reserved
            Assert.Equal(1, br.ReadUInt16()); // Type 1 = ICO
            ushort count = br.ReadUInt16();
            Assert.Equal(AppIconGenerator.StandardIcoSizes.Length, count);

            for (int i = 0; i < count; i++)
            {
                int expectedSize = AppIconGenerator.StandardIcoSizes[i];
                byte w = br.ReadByte();
                byte h = br.ReadByte();
                _ = br.ReadByte(); // palette
                _ = br.ReadByte(); // reserved
                Assert.Equal(1, br.ReadUInt16()); // planes
                Assert.Equal(32, br.ReadUInt16()); // 32bpp ARGB
                uint len = br.ReadUInt32();
                uint offset = br.ReadUInt32();

                Assert.Equal(expectedSize >= 256 ? 0 : expectedSize, w);
                Assert.Equal(expectedSize >= 256 ? 0 : expectedSize, h);
                Assert.True(len > 100);

                using var frameBmp = SKBitmap.Decode(icoBytes.AsSpan((int)offset, (int)len));
                Assert.NotNull(frameBmp);
                Assert.Equal(expectedSize, frameBmp.Width);
                Assert.Equal(expectedSize, frameBmp.Height);

                // Outer corner outside the sky-blue squircle MUST be 100% alpha-transparent!
                Assert.Equal(0, frameBmp.GetPixel(0, 0).Alpha);
                Assert.Equal(0, frameBmp.GetPixel(expectedSize - 1, expectedSize - 1).Alpha);
                // Center of icon must be opaque!
                Assert.Equal(255, frameBmp.GetPixel(expectedSize / 2, expectedSize / 2).Alpha);
            }
        }
        finally
        {
            try { Directory.Delete(tempOutDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void UnifiedUndoRedo_Presets_FocusMode_And_AllStudioWindows_WorkCleanly()
    {
        RunOnStaAsync(async () =>
        {
            if (Application.Current is null)
            {
                var app = new App();
                app.InitializeComponent();
            }

            var mainWindow = new MainWindow();
            Assert.NotNull(mainWindow.DataContext);
            Assert.NotNull(mainWindow.Icon);

            var vm = (DiceChaosDisplay.ViewModels.MainViewModel)mainWindow.DataContext;
            var playerWindow = new PlayerWindow(vm);
            var shortcutsWindow = new DiceChaosDisplay.Views.ShortcutsWindow();
            Assert.Same(vm, playerWindow.DataContext);
            Assert.NotNull(playerWindow.Icon);
            Assert.NotNull(shortcutsWindow.Icon);

            // Create an in-memory hydrated scene to test Unified Undo/Redo & Condition Presets
            byte[] pngBytes;
            using (var bmp = new SKBitmap(560, 560))
            using (var c = new SKCanvas(bmp))
            {
                c.Clear(SKColors.DarkSlateBlue);
                using var img = SKImage.FromBitmap(bmp);
                using var enc = img.Encode(SKEncodedImageFormat.Png, 80);
                pngBytes = enc.ToArray();
            }

            string tmpMap = Path.Combine(Path.GetTempPath(), $"Map_{Guid.NewGuid():N}.png");
            await File.WriteAllBytesAsync(tmpMap, pngBytes);
            try
            {
                await vm.AddScenesFromFilesAsync([tmpMap]);
                Assert.NotNull(vm.StagedScene);

                // Default ring is clean Glow-Only (empty label)
                var ring1 = vm.PlaceNewTokenAt(new Point(120, 120));
                Assert.Equal("", ring1.Label);
                Assert.Single(vm.StagedScene!.Tokens);

                // Apply Bloodied preset to selected ring
                vm.ApplyConditionPreset("Bloodied");
                Assert.Equal("Bloodied", ring1.Label);
                Assert.Equal("#FF4757", ring1.ColorHex);

                // Place 2 Spell AoEs, then ClearAllAoes, then Undo & Redo
                vm.PlaceNewAoeAt(new Point(200, 200));
                vm.PlaceNewAoeAt(new Point(300, 300));
                Assert.Equal(2, vm.StagedScene.AoeTemplates.Count);

                vm.ClearAllAoes();
                Assert.Empty(vm.StagedScene.AoeTemplates);

                vm.Undo(); // Restores the 2 AoEs!
                Assert.Equal(2, vm.StagedScene.AoeTemplates.Count);

                vm.Redo(); // Re-clears AoEs!
                Assert.Empty(vm.StagedScene.AoeTemplates);

                // Interleave Fog RevealAll + Undo
                vm.RevealAllFog();
                Assert.Equal(0, vm.StagedScene.FogEngine!.SampleAlphaAtMapPoint(280, 280));
                vm.Undo();
                Assert.Equal(255, vm.StagedScene.FogEngine!.SampleAlphaAtMapPoint(280, 280));

                // Focus mode toggles sidebars cleanly
                Assert.True(vm.IsRightInspectorExpanded);
                Assert.True(vm.IsSceneStripExpanded);
                vm.ToggleFocusMode();
                Assert.False(vm.IsRightInspectorExpanded);
                Assert.False(vm.IsSceneStripExpanded);
                vm.ToggleFocusMode();
                Assert.True(vm.IsRightInspectorExpanded);
                Assert.True(vm.IsSceneStripExpanded);
            }
            finally
            {
                shortcutsWindow.Close();
                playerWindow.Close();
                vm.Dispose();
                try { File.Delete(tmpMap); } catch { }
            }
        });
    }

    [Fact]
    public void PlayerTvToggle_SpoilerBlackout_DecoupledQueue_And_SmartPreview_WorkCleanly()
    {
        RunOnStaAsync(async () =>
        {
            if (Application.Current is null)
            {
                var app = new App();
                app.InitializeComponent();
            }

            var mainWindow = new MainWindow();
            var vm = (DiceChaosDisplay.ViewModels.MainViewModel)mainWindow.DataContext;

            byte[] pngBytes;
            using (var bmp = new SKBitmap(400, 400))
            using (var c = new SKCanvas(bmp))
            {
                c.Clear(SKColors.MidnightBlue);
                using var img = SKImage.FromBitmap(bmp);
                using var enc = img.Encode(SKEncodedImageFormat.Png, 80);
                pngBytes = enc.ToArray();
            }

            string tmpMap = Path.Combine(Path.GetTempPath(), $"ToggleTest_{Guid.NewGuid():N}.png");
            await File.WriteAllBytesAsync(tmpMap, pngBytes);

            try
            {
                // 1. Starts OFF
                Assert.False(vm.IsPlayerWindowOpen);
                Assert.Equal("TV Off", vm.TvPowerButtonText);
                Assert.False(vm.IsTvBlackedOut);

                // 2. Toggling ON before any scene is pushed auto-engages Spoiler-Safe Blackout!
                mainWindow.TogglePlayerWindow();
                Assert.True(vm.IsPlayerWindowOpen);
                Assert.True(vm.IsTvBlackedOut);

                // 3. Toggling OFF cleanly closes PlayerWindow and returns to TV Off
                mainWindow.TogglePlayerWindow();
                Assert.False(vm.IsPlayerWindowOpen);
                Assert.Equal("TV Off", vm.TvPowerButtonText);

                // 4. Pushing a scene while TV is OFF updates the queued scene WITHOUT force-opening TV!
                await vm.AddScenesFromFilesAsync([tmpMap]);
                await vm.PushStagedSceneToTvAsync();
                Assert.False(vm.IsPlayerWindowOpen);
                Assert.True(vm.IsSceneQueuedWhileTvOff);
                Assert.False(vm.IsSceneActivelyBroadcasting);
                Assert.True(vm.StagedScene!.IsQueuedWhileTvOff);
                Assert.False(vm.StagedScene.IsActivelyBroadcasting);

                // 5. Un-blackout, select the DM's own display, and toggle ON -> preserves Blackout=false & enters Smart Floating Preview!
                vm.IsTvBlackedOut = false;
                string? dmDev = DisplayCalibrationService.GetMonitorDeviceNameForWindow(mainWindow);
                var sameMonitor = vm.AvailableMonitors.FirstOrDefault(m =>
                    string.Equals(m.DeviceName, dmDev, StringComparison.OrdinalIgnoreCase))
                    ?? vm.AvailableMonitors.FirstOrDefault();

                if (sameMonitor is not null)
                {
                    vm.SelectedTvMonitor = sameMonitor;
                }

                mainWindow.TogglePlayerWindow();
                Assert.True(vm.IsPlayerWindowOpen);
                Assert.False(vm.IsTvBlackedOut); // Preserved!
                Assert.True(vm.IsSceneActivelyBroadcasting);
                Assert.False(vm.IsSceneQueuedWhileTvOff);

                if (sameMonitor is not null)
                {
                    Assert.True(vm.IsPlayerWindowPreviewMode);
                    Assert.Equal("● TV Preview", vm.TvPowerButtonText);
                }

                // 6. Toggle OFF cleans up preview flag & window cleanly
                mainWindow.TogglePlayerWindow();
                Assert.False(vm.IsPlayerWindowOpen);
                Assert.False(vm.IsPlayerWindowPreviewMode);
                Assert.Equal("TV Off", vm.TvPowerButtonText);
            }
            finally
            {
                mainWindow.ClosePlayerWindow();
                vm.Dispose();
                try { File.Delete(tmpMap); } catch { }
            }
        });
    }

    private static void RunOnSta(Action action)
    {
        Exception? caught = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { caught = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (caught is not null) throw new AggregateException(caught);
    }

    private static void RunOnStaAsync(Func<Task> asyncAction)
    {
        Exception? caught = null;
        var thread = new Thread(() =>
        {
            var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(
                new System.Windows.Threading.DispatcherSynchronizationContext(dispatcher));

            var frame = new System.Windows.Threading.DispatcherFrame();
            _ = ExecuteAsync();
            System.Windows.Threading.Dispatcher.PushFrame(frame);

            async Task ExecuteAsync()
            {
                try { await asyncAction(); }
                catch (Exception ex) { caught = ex; }
                finally { frame.Continue = false; }
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (caught is not null) throw new AggregateException(caught);
    }
}
