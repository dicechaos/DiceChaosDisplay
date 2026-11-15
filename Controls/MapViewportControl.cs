using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DiceChaosDisplay.Models;
using DiceChaosDisplay.Services;
using DiceChaosDisplay.ViewModels;

namespace DiceChaosDisplay.Controls;

public sealed class MapViewportControl : FrameworkElement
{
    private static readonly Typeface BadgeTypeface = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(
            nameof(ViewModel),
            typeof(MainViewModel),
            typeof(MapViewportControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnViewModelChanged));

    public static readonly DependencyProperty IsDmViewProperty =
        DependencyProperty.Register(
            nameof(IsDmView),
            typeof(bool),
            typeof(MapViewportControl),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public MainViewModel? ViewModel
    {
        get => (MainViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public bool IsDmView
    {
        get => (bool)GetValue(IsDmViewProperty);
        set => SetValue(IsDmViewProperty, value);
    }

    // Interaction state (DM View only)
    private bool _isMiddlePanning;
    private bool _isCtrlMiddlePanningTv;
    private bool _isLeftCanvasPanning;
    private Point _lastPanScreenPt;

    private bool _isDraggingTvFrame;
    private Vector _tvFrameGrabMapOffset;
    private Rect? _tvBadgeMapHitRect;

    private bool _isPaintingFogBrush;
    private FogPaintOperation _activeBrushOp;

    private bool _isDraggingFogBox;
    private Point _fogBoxStartMapPt;
    private Point _fogBoxCurrentMapPt;

    private readonly List<Point2D> _polygonDraftPoints = new();

    private PencilStrokeModel? _activePencilStroke;

    private TokenMarkerModel? _draggingToken;
    private AoeTemplateModel? _draggingAoeOrigin;
    private AoeTemplateModel? _rotatingAoe;
    private DmPinModel? _draggingPin;
    private Vector _dragElementOffset;
    private Point _elementDragStartPt;
    private double _aoeRotateStartDeg;
    private bool _isNewPlacementDrag;

    private AoeTemplateModel? _hoveredAoe;

    // 3x3 Grid Alignment Box state
    private Rect _align3x3Rect;
    private bool _align3x3Initialized;
    private int _align3x3DragMode; // 0=none, 1=move, 2=resizeBR, 3=resizeTL
    private Point _align3x3DragStartMap;
    private Rect _align3x3StartRect;

    private Point? _hoverMapPoint;
    private bool _isRenderingHooked;

    // Cached hit regions for clickable Room Badges (rebuilt on DM render)
    private readonly List<(Rect MapHitRect, FogRoomRegion Room)> _roomBadgeHits = new();

    public MapViewportControl()
    {
        ClipToBounds = true;
        Focusable = true;
        UseLayoutRounding = false;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.Linear);

        SizeChanged += OnViewportSizeChanged;
        Unloaded += (_, _) => SetAnimationLoop(false);
    }

    private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ctrl = (MapViewportControl)d;
        if (e.OldValue is MainViewModel oldVm)
        {
            oldVm.ViewportRepaintRequested -= ctrl.OnRepaintRequested;
            oldVm.RequestFitDmCamera -= ctrl.OnRequestFitDmCamera;
        }
        if (e.NewValue is MainViewModel newVm)
        {
            newVm.ViewportRepaintRequested += ctrl.OnRepaintRequested;
            newVm.RequestFitDmCamera += ctrl.OnRequestFitDmCamera;
            ctrl.PushSizeToViewModel();
        }
    }

    private void OnRepaintRequested(object? sender, EventArgs e)
    {
        if (ViewModel is not null && ViewModel.ActivePings.Count > 0)
        {
            SetAnimationLoop(true);
        }
        if (ViewModel?.ActiveTool != DmToolMode.GridAlign3x3)
        {
            _align3x3Initialized = false;
        }
        InvalidateVisual();
    }

    private void OnRequestFitDmCamera(object? sender, EventArgs e)
    {
        if (!IsDmView || ViewModel?.StagedScene is null) return;
        FitDmToCurrentScene();
    }

    public void FitDmToCurrentScene()
    {
        var vm = ViewModel;
        var scene = vm?.StagedScene;
        if (vm is null || scene is null || RenderSize.Width < 5 || RenderSize.Height < 5) return;
        vm.DmCamera = ViewportMath.FitMapToViewport(scene.PixelWidth, scene.PixelHeight, RenderSize, 0.0, 0.94);
    }

    public void ZoomByStep(double factor)
    {
        var vm = ViewModel;
        if (vm is null || RenderSize.Width < 5 || RenderSize.Height < 5) return;
        Point center = new(RenderSize.Width * 0.5, RenderSize.Height * 0.5);
        vm.DmCamera = ViewportMath.ZoomAroundScreenPoint(vm.DmCamera, RenderSize, center, factor);
    }

    public void SetZoomOneToOne()
    {
        var vm = ViewModel;
        if (vm is null) return;
        vm.DmCamera = vm.DmCamera with { Zoom = 1.0 };
    }

    public void CenterTvAtCursorOrViewCenter()
    {
        var vm = ViewModel;
        if (vm is null || vm.StagedScene is null) return;
        Point target = _hoverMapPoint ?? new Point(vm.DmCamera.CenterMapX, vm.DmCamera.CenterMapY);
        vm.CenterTvAtMapPoint(target);
    }

    private void OnViewportSizeChanged(object sender, SizeChangedEventArgs e)
    {
        PushSizeToViewModel();
        InvalidateVisual();
    }

    private void PushSizeToViewModel()
    {
        if (ViewModel is null || RenderSize.Width <= 0 || RenderSize.Height <= 0) return;
        if (IsDmView) ViewModel.DmViewportSize = RenderSize;
        else ViewModel.TvViewportSize = RenderSize;
    }

    private void SetAnimationLoop(bool enable)
    {
        if (enable && !_isRenderingHooked)
        {
            CompositionTarget.Rendering += OnCompositionRendering;
            _isRenderingHooked = true;
        }
        else if (!enable && _isRenderingHooked)
        {
            CompositionTarget.Rendering -= OnCompositionRendering;
            _isRenderingHooked = false;
        }
    }

    private void OnCompositionRendering(object? sender, EventArgs e)
    {
        if (ViewModel is null || ViewModel.ActivePings.Count == 0)
        {
            SetAnimationLoop(false);
            return;
        }

        DateTime now = DateTime.UtcNow;
        bool anyAlive = false;
        foreach (var p in ViewModel.ActivePings)
        {
            if ((now - p.StartedUtc).TotalSeconds <= p.DurationSeconds)
            {
                anyAlive = true;
                break;
            }
        }

        InvalidateVisual();
        if (!anyAlive) SetAnimationLoop(false);
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (IsDmView) _tvBadgeMapHitRect = null;

        var vm = ViewModel;
        Color bgColor = IsDmView ? Color.FromRgb(16, 19, 26) : Colors.Black;
        var bgBrush = new SolidColorBrush(bgColor);
        bgBrush.Freeze();
        dc.DrawRectangle(bgBrush, null, new Rect(0, 0, RenderSize.Width, RenderSize.Height));

        if (vm is null) return;

        SceneViewModel? scene = IsDmView ? vm.StagedScene : vm.LiveTvScene;
        if (scene is null)
        {
            if (IsDmView)
            {
                DrawEmptyStateOverlay(dc);
            }
            return;
        }

        if (!IsDmView && vm.IsTvBlackedOut)
        {
            return;
        }

        CameraState cam = IsDmView ? vm.DmCamera : vm.TvCamera;
        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        double invZoom = 1.0 / Math.Max(0.03, cam.Zoom);

        Rect fullMapRect = new(0, 0, scene.PixelWidth, scene.PixelHeight);
        Rect visibleMapRect = ViewportMath.GetVisibleMapBounds(cam, RenderSize);
        Matrix mapToScreen = ViewportMath.BuildMapToScreenMatrix(cam, RenderSize);

        dc.PushTransform(new MatrixTransform(mapToScreen));

        // 1. Base Map Layer (Overview + Frustum-Culled 2K High-Res Tiles OR Video Map)
        DrawBaseMapLayer(dc, scene, fullMapRect, visibleMapRect, cam.Zoom * pixelsPerDip);

        // 2. Tactical Grid Layer (Square / Hex Flat / Hex Pointy)
        GridRenderer.DrawGrid(dc, scene.Grid, fullMapRect, visibleMapRect, cam.Zoom);

        // 3. Tactical Elements Under Fog (Physical-Mini Condition/Aura Rings, Tokens, AoE Templates)
        if (vm.ShowAoeLayer)
        {
            TacticalOverlayRenderer.DrawAoeTemplates(
                dc,
                scene.AoeTemplates,
                scene.Grid,
                IsDmView,
                vm.SelectedAoeTemplate,
                cam.Zoom,
                pixelsPerDip,
                _hoveredAoe,
                vm.ShowAoeLabelsAlways);
        }

        if (vm.ShowTokensLayer)
        {
            TacticalOverlayRenderer.DrawTokensAndRings(
                dc,
                scene.Tokens,
                scene.Grid,
                IsDmView,
                vm.SelectedToken,
                cam.Zoom,
                pixelsPerDip);
        }

        // 4. Zero-Copy Fog of War Layer (Player TV ALWAYS renders fog even if DM peeks underneath!)
        if (scene.FogEngine is not null)
        {
            if (IsDmView)
            {
                if (vm.ShowFogOnDm)
                {
                    dc.PushOpacity(vm.DmFogOpacity);
                    dc.DrawImage(scene.FogEngine.FogBitmap, fullMapRect);
                    dc.Pop();
                }
            }
            else
            {
                dc.DrawImage(scene.FogEngine.FogBitmap, fullMapRect);
            }
        }

        // 5. Shared Overlays Above Fog (Pencil Drawings, Distance Ruler, Sonar Pings)
        bool sameSceneAsStaged = ReferenceEquals(scene, vm.StagedScene);
        if (vm.ShowPencilLayer)
        {
            TacticalOverlayRenderer.DrawPencilStrokes(
                dc,
                scene.PencilStrokes,
                IsDmView ? _activePencilStroke : (sameSceneAsStaged ? _activePencilStroke : null),
                cam.Zoom);
        }

        if (vm.IsRulerActive && (IsDmView || (vm.ShowRulerOnTv && sameSceneAsStaged)))
        {
            TacticalOverlayRenderer.DrawRuler(dc, vm.RulerStartMapPt, vm.RulerEndMapPt, scene.Grid, cam.Zoom, pixelsPerDip);
        }

        if (sameSceneAsStaged && vm.ActivePings.Count > 0)
        {
            TacticalOverlayRenderer.DrawPings(dc, vm.ActivePings, DateTime.UtcNow, cam.Zoom);
        }

        // 6. DM-Only Overlays (NEVER rendered on the Player TV!)
        if (IsDmView)
        {
            DrawDmExclusiveOverlays(dc, vm, scene, fullMapRect, cam.Zoom, invZoom, pixelsPerDip);
        }

        dc.Pop(); // Pop mapToScreen matrix
    }

    private static void DrawBaseMapLayer(
        DrawingContext dc,
        SceneViewModel scene,
        Rect fullMapRect,
        Rect visibleMapRect,
        double physicalPixelsPerMapPixel)
    {
        if (scene.MediaType == SceneMediaType.VideoMap && scene.VideoInstance is not null)
        {
            if (scene.VideoInstance.VideoBrush is not null)
            {
                dc.DrawRectangle(scene.VideoInstance.VideoBrush, null, fullMapRect);
            }
            else if (scene.VideoInstance.VideoBitmap is not null)
            {
                dc.DrawImage(scene.VideoInstance.VideoBitmap, fullMapRect);
            }
            return;
        }

        var asset = scene.LoadedAsset;
        if (asset is null) return;

        // Always draw Overview first for zero pop-in
        dc.DrawImage(asset.OverviewBitmap, fullMapRect);

        // Mount high-res 2048x2048 tiles ONLY when zoomed past overview fidelity AND inside frustum!
        if (asset.UsesTiledHighRes)
        {
            double overviewRatio = (double)asset.OverviewBitmap.PixelWidth / Math.Max(1, asset.PixelWidth);
            if (physicalPixelsPerMapPixel > overviewRatio * 0.85)
            {
                dc.PushClip(new RectangleGeometry(fullMapRect));
                foreach (var tile in asset.HighResTiles)
                {
                    if (tile.DestMapRect.IntersectsWith(visibleMapRect))
                    {
                        dc.DrawImage(tile.Bitmap, tile.DestMapRect);
                    }
                }
                dc.Pop();
            }
        }
    }

    private void DrawDmExclusiveOverlays(
        DrawingContext dc,
        MainViewModel vm,
        SceneViewModel scene,
        Rect fullMapRect,
        double zoom,
        double invZoom,
        double pixelsPerDip)
    {
        // Map border outline
        var borderPen = new Pen(new SolidColorBrush(Color.FromArgb(110, 120, 140, 180)), 1.5 * invZoom);
        borderPen.Freeze();
        dc.DrawRectangle(null, borderPen, fullMapRect);

        // Optional UVTT Wall & Door Guides
        if (vm.ShowUvttWallGuides && (scene.Model.UvttWalls.Count > 0 || scene.Model.UvttPortals.Count > 0))
        {
            var wallPen = new Pen(new SolidColorBrush(Color.FromArgb(145, 0, 229, 255)), 2.0 * invZoom);
            wallPen.Freeze();

            var wallGeom = new StreamGeometry();
            using (var ctx = wallGeom.Open())
            {
                foreach (var poly in scene.Model.UvttWalls)
                {
                    if (poly.Count < 2) continue;
                    ctx.BeginFigure(new Point(poly[0].X, poly[0].Y), false, false);
                    for (int i = 1; i < poly.Count; i++)
                        ctx.LineTo(new Point(poly[i].X, poly[i].Y), true, true);
                }
            }
            wallGeom.Freeze();
            dc.DrawGeometry(null, wallPen, wallGeom);

            var doorPen = new Pen(new SolidColorBrush(Color.FromArgb(215, 255, 190, 11)), 3.8 * invZoom);
            doorPen.Freeze();
            foreach (var portal in scene.Model.UvttPortals)
            {
                if (portal.Count >= 2)
                {
                    dc.DrawLine(doorPen, new Point(portal[0].X, portal[0].Y), new Point(portal[1].X, portal[1].Y));
                }
            }
        }

        // Pre-drawn Clickable Fog Rooms & 1-Click Badges
        _roomBadgeHits.Clear();
        if (vm.ShowDmRoomBadges && scene.Rooms.Count > 0)
        {
            foreach (var room in scene.Rooms)
            {
                if (room.Points.Count < 3) continue;

                Color rc = room.IsRevealed ? Color.FromRgb(46, 213, 115) : Color.FromRgb(255, 165, 2);
                var polyPen = new Pen(new SolidColorBrush(Color.FromArgb(165, rc.R, rc.G, rc.B)), 1.8 * invZoom)
                {
                    DashStyle = DashStyles.Dash
                };
                polyPen.Freeze();

                var rGeom = new StreamGeometry();
                using (var ctx = rGeom.Open())
                {
                    ctx.BeginFigure(new Point(room.Points[0].X, room.Points[0].Y), false, true);
                    for (int i = 1; i < room.Points.Count; i++)
                        ctx.LineTo(new Point(room.Points[i].X, room.Points[i].Y), true, true);
                }
                rGeom.Freeze();
                dc.DrawGeometry(null, polyPen, rGeom);

                Point2D c = room.Centroid;
                Point center = new(c.X, c.Y);
                string badgeText = (room.IsRevealed ? "OPEN • " : "LOCKED • ") + room.Name;

                var ft = new FormattedText(
                    badgeText,
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    BadgeTypeface,
                    11.5 * invZoom,
                    Brushes.White,
                    pixelsPerDip);

                double px = 8.5 * invZoom, py = 4.0 * invZoom;
                Rect hitRect = new(center.X - ft.Width * 0.5 - px, center.Y - ft.Height * 0.5 - py, ft.Width + px * 2, ft.Height + py * 2);
                _roomBadgeHits.Add((hitRect, room));

                var pillBg = new SolidColorBrush(Color.FromArgb(232, 18, 23, 34));
                pillBg.Freeze();
                var pillBorder = new Pen(new SolidColorBrush(rc), 2.0 * invZoom);
                pillBorder.Freeze();

                dc.DrawRoundedRectangle(pillBg, pillBorder, hitRect, 5.5 * invZoom, 5.5 * invZoom);
                dc.DrawText(ft, new Point(center.X - ft.Width * 0.5, center.Y - ft.Height * 0.5));
            }
        }

        // DM-Only Secret Note Pins
        if (vm.ShowDmPinsLayer)
        {
            foreach (var pin in scene.DmPins)
            {
                Point pt = new(pin.MapX, pin.MapY);
                bool sel = ReferenceEquals(pin, vm.SelectedDmPin);
                Color pc = GridRenderer.ParseColorOrDefault(pin.ColorHex, Color.FromRgb(255, 209, 102));

                var fill = new SolidColorBrush(pc);
                fill.Freeze();
                var ringPen = new Pen(sel ? Brushes.White : Brushes.Black, (sel ? 3.2 : 2.0) * invZoom);
                ringPen.Freeze();

                double r = 13.0 * invZoom;
                dc.DrawEllipse(fill, ringPen, pt, r, r);

                var ft = new FormattedText(
                    pin.Code,
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    BadgeTypeface,
                    11.0 * invZoom,
                    Brushes.Black,
                    pixelsPerDip);
                dc.DrawText(ft, new Point(pt.X - ft.Width * 0.5, pt.Y - ft.Height * 0.5));
            }
        }

        // Interactive 3x3 Grid Alignment Box
        if (vm.ActiveTool == DmToolMode.GridAlign3x3)
        {
            Ensure3x3BoxInitialized(scene);
            Draw3x3AlignmentOverlay(dc, invZoom, pixelsPerDip);
        }

        // Active Fog Box / Polygon / Brush Previews
        if (_isDraggingFogBox)
        {
            Rect box = new(_fogBoxStartMapPt, _fogBoxCurrentMapPt);
            Color bc = vm.FogOperation == FogPaintOperation.Reveal ? Color.FromRgb(46, 213, 115) : Color.FromRgb(255, 107, 53);
            var fill = new SolidColorBrush(Color.FromArgb(45, bc.R, bc.G, bc.B));
            fill.Freeze();
            var pen = new Pen(new SolidColorBrush(bc), 2.2 * invZoom) { DashStyle = DashStyles.Dash };
            pen.Freeze();
            dc.DrawRectangle(fill, pen, box);
        }

        if (_polygonDraftPoints.Count > 0)
        {
            Color pc = vm.ActiveTool == DmToolMode.RoomPrep
                ? Color.FromRgb(0, 229, 255)
                : (vm.FogOperation == FogPaintOperation.Reveal ? Color.FromRgb(46, 213, 115) : Color.FromRgb(255, 107, 53));

            var pen = new Pen(new SolidColorBrush(pc), 2.4 * invZoom);
            pen.Freeze();

            var geom = new StreamGeometry();
            using (var ctx = geom.Open())
            {
                ctx.BeginFigure(new Point(_polygonDraftPoints[0].X, _polygonDraftPoints[0].Y), true, true);
                for (int i = 1; i < _polygonDraftPoints.Count; i++)
                    ctx.LineTo(new Point(_polygonDraftPoints[i].X, _polygonDraftPoints[i].Y), true, true);
                if (_hoverMapPoint is Point hp)
                    ctx.LineTo(hp, true, true);
            }
            geom.Freeze();
            var fill = new SolidColorBrush(Color.FromArgb(38, pc.R, pc.G, pc.B));
            fill.Freeze();
            dc.DrawGeometry(fill, pen, geom);

            foreach (var v in _polygonDraftPoints)
            {
                dc.DrawEllipse(Brushes.White, pen, new Point(v.X, v.Y), 4.5 * invZoom, 4.5 * invZoom);
            }
        }

        if (vm.ActiveTool == DmToolMode.FogBrush && _hoverMapPoint is Point brushPt)
        {
            Color bc = vm.FogOperation == FogPaintOperation.Reveal ? Color.FromRgb(46, 213, 115) : Color.FromRgb(255, 107, 53);
            var pen = new Pen(new SolidColorBrush(Color.FromArgb(220, bc.R, bc.G, bc.B)), 2.0 * invZoom);
            pen.Freeze();
            dc.DrawEllipse(null, pen, brushPt, vm.FogBrushRadius, vm.FogBrushRadius);
        }

        // Draggable Green/Aqua TV Viewport Frame (shown whenever StagedScene == LiveTvScene & enabled)
        if (vm.ShowTvFrameGuide && ReferenceEquals(scene, vm.LiveTvScene) && vm.TvViewportSize.Width > 10 && vm.TvViewportSize.Height > 10)
        {
            DrawTvViewportIndicator(dc, vm, invZoom, pixelsPerDip);
        }
    }

    private void DrawTvViewportIndicator(DrawingContext dc, MainViewModel vm, double invZoom, double pixelsPerDip)
    {
        Size tvSize = vm.TvViewportSize;
        CameraState tvCam = vm.TvCamera;

        // Project the 4 corners of the TV window into Map Coordinates (handles 0/90/180/270 rotation seamlessly!)
        Point topLeft = ViewportMath.ScreenToMap(new Point(0, 0), tvCam, tvSize);
        Point topRight = ViewportMath.ScreenToMap(new Point(tvSize.Width, 0), tvCam, tvSize);
        Point bottomRight = ViewportMath.ScreenToMap(new Point(tvSize.Width, tvSize.Height), tvCam, tvSize);
        Point bottomLeft = ViewportMath.ScreenToMap(new Point(0, tvSize.Height), tvCam, tvSize);

        bool isTvOn = vm.IsPlayerWindowOpen;
        Color frameColor = !isTvOn
            ? Color.FromRgb(124, 196, 250) // Cool blueprint sky-blue while TV is OFF (pre-aiming)
            : (vm.TvProfile.SyncMode == TvSyncMode.PhysicalOneInch
                ? Color.FromRgb(0, 245, 212) // Aqua-emerald for 1" Physical Lock
                : Color.FromRgb(46, 213, 115)); // LimeGreen

        var glowPen = new Pen(new SolidColorBrush(Color.FromArgb(isTvOn ? (byte)65 : (byte)28, frameColor.R, frameColor.G, frameColor.B)), (isTvOn ? 7.0 : 4.5) * invZoom);
        glowPen.Freeze();
        var borderPen = new Pen(new SolidColorBrush(frameColor), (isTvOn ? 2.8 : 2.1) * invZoom);
        if (!isTvOn || vm.TvProfile.SyncMode == TvSyncMode.MirrorDm)
            borderPen.DashStyle = DashStyles.Dash;
        borderPen.Freeze();

        var quad = new StreamGeometry();
        using (var ctx = quad.Open())
        {
            ctx.BeginFigure(topLeft, false, true);
            ctx.LineTo(topRight, true, false);
            ctx.LineTo(bottomRight, true, false);
            ctx.LineTo(bottomLeft, true, false);
        }
        quad.Freeze();

        dc.DrawGeometry(null, glowPen, quad);
        dc.DrawGeometry(null, borderPen, quad);

        // Draggable Header Pill along the TV's actual Top Edge — grabbable from ANY active tool!
        Point topEdgeMid = new((topLeft.X + topRight.X) * 0.5, (topLeft.Y + topRight.Y) * 0.5);
        string syncShort = vm.TvProfile.SyncMode switch
        {
            TvSyncMode.PhysicalOneInch => "1\" LOCK",
            TvSyncMode.IndependentTv => "DRAG",
            _ => "MIRRORED"
        };
        string statusPrefix = !isTvOn
            ? "OFF • PRE-AIM"
            : (vm.IsPlayerWindowPreviewMode ? "PREVIEW" : "LIVE");
        string modeTag = $"✥ TV VIEW ({statusPrefix} • {syncShort} • {vm.TvProfile.RotationAngle:0}°)";

        Rect pillRect = TacticalOverlayRenderer.DrawScreenScaledPill(dc, topEdgeMid, modeTag, frameColor, invZoom, pixelsPerDip);
        pillRect.Inflate(5.0 * invZoom, 4.0 * invZoom);
        _tvBadgeMapHitRect = pillRect;
    }

    private void Ensure3x3BoxInitialized(SceneViewModel scene)
    {
        if (_align3x3Initialized) return;
        double cell = Math.Max(20.0, scene.Grid.PixelsPerCell);
        double span = cell * 3.0;
        double cx = ViewModel?.DmCamera.CenterMapX ?? scene.PixelWidth * 0.5;
        double cy = ViewModel?.DmCamera.CenterMapY ?? scene.PixelHeight * 0.5;
        _align3x3Rect = new Rect(cx - span * 0.5, cy - span * 0.5, span, span);
        _align3x3Initialized = true;
    }

    private void Draw3x3AlignmentOverlay(DrawingContext dc, double invZoom, double pixelsPerDip)
    {
        Color cyan = Color.FromRgb(0, 229, 255);
        var fill = new SolidColorBrush(Color.FromArgb(28, 0, 229, 255));
        fill.Freeze();
        var outerPen = new Pen(new SolidColorBrush(cyan), 2.8 * invZoom);
        outerPen.Freeze();
        var innerPen = new Pen(new SolidColorBrush(Color.FromArgb(200, 0, 229, 255)), 1.6 * invZoom)
        {
            DashStyle = DashStyles.Dash
        };
        innerPen.Freeze();

        dc.DrawRectangle(fill, outerPen, _align3x3Rect);

        for (int i = 1; i <= 2; i++)
        {
            double x = _align3x3Rect.Left + (_align3x3Rect.Width / 3.0) * i;
            double y = _align3x3Rect.Top + (_align3x3Rect.Height / 3.0) * i;
            dc.DrawLine(innerPen, new Point(x, _align3x3Rect.Top), new Point(x, _align3x3Rect.Bottom));
            dc.DrawLine(innerPen, new Point(_align3x3Rect.Left, y), new Point(_align3x3Rect.Right, y));
        }

        double hr = 7.5 * invZoom;
        dc.DrawEllipse(Brushes.White, outerPen, _align3x3Rect.TopLeft, hr, hr);
        dc.DrawEllipse(Brushes.Gold, outerPen, _align3x3Rect.BottomRight, hr, hr);

        TacticalOverlayRenderer.DrawScreenScaledPill(
            dc,
            new Point(_align3x3Rect.Left + _align3x3Rect.Width * 0.5, _align3x3Rect.Top - 18 * invZoom),
            "Drag Top-Left to an intersection • Drag Gold Bottom-Right over 3×3 squares",
            cyan,
            invZoom,
            pixelsPerDip);
    }

    private void DrawEmptyStateOverlay(DrawingContext dc)
    {
        double ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var title = new FormattedText(
            "Load or Drop a 4K/8K Battlemap, .dd2vtt/.uvtt, or MP4/WEBM Video Map",
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            BadgeTypeface,
            18,
            new SolidColorBrush(Color.FromRgb(190, 200, 220)),
            ppd);

        var sub = new FormattedText(
            "Middle-Drag to Pan  •  Wheel to Zoom  •  Alt+Click to Ping TV  •  C to Center TV  •  F1 for Shortcuts",
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            13,
            new SolidColorBrush(Color.FromRgb(120, 132, 155)),
            ppd);

        dc.DrawText(title, new Point((RenderSize.Width - title.Width) * 0.5, RenderSize.Height * 0.45));
        dc.DrawText(sub, new Point((RenderSize.Width - sub.Width) * 0.5, RenderSize.Height * 0.45 + 32));
    }

    #region Hit-Testing Helpers

    private bool TryGrabAoeRotationHandle(SceneViewModel scene, Point mapPt, double invZoom)
    {
        if (ViewModel is null || !ViewModel.ShowAoeLayer) return false;
        if (ViewModel.SelectedAoeTemplate is not { } selAoe || selAoe.Shape is not (AoeShape.Cone or AoeShape.Line or AoeShape.Cube))
            return false;

        double pxPerFoot = Math.Max(1.0, scene.Grid.PixelsPerCell) / Math.Max(1.0, scene.Grid.FeetPerCell);
        double sizePx = selAoe.SizeFeet * pxPerFoot;
        double dist = selAoe.Shape == AoeShape.Cube ? sizePx * 0.65 : sizePx;
        double rad = selAoe.DirectionDegrees * Math.PI / 180.0;
        Point handlePt = new(selAoe.OriginX + Math.Cos(rad) * dist, selAoe.OriginY + Math.Sin(rad) * dist);
        if ((mapPt - handlePt).Length <= 15.0 * invZoom)
        {
            _rotatingAoe = selAoe;
            _aoeRotateStartDeg = selAoe.DirectionDegrees;
            _isNewPlacementDrag = false;
            return true;
        }
        return false;
    }

    private DmPinModel? HitTestDmPin(SceneViewModel scene, Point mapPt, double invZoom)
    {
        if (ViewModel is null || !ViewModel.ShowDmPinsLayer) return null;
        for (int i = scene.DmPins.Count - 1; i >= 0; i--)
        {
            var pin = scene.DmPins[i];
            if ((mapPt - new Point(pin.MapX, pin.MapY)).Length <= 16.5 * invZoom)
                return pin;
        }
        return null;
    }

    private TokenMarkerModel? HitTestToken(SceneViewModel scene, Point mapPt, double invZoom)
    {
        if (ViewModel is null || !ViewModel.ShowTokensLayer) return null;
        double cellPx = Math.Max(8.0, scene.Grid.PixelsPerCell);
        for (int i = scene.Tokens.Count - 1; i >= 0; i--)
        {
            var tok = scene.Tokens[i];
            double hitR = Math.Max(14.0 * invZoom, tok.SizeCells * cellPx * 0.52);
            if ((mapPt - new Point(tok.MapX, tok.MapY)).Length <= hitR)
                return tok;
        }
        return null;
    }

    private AoeTemplateModel? HitTestAoeOrigin(SceneViewModel scene, Point mapPt, double invZoom)
    {
        if (ViewModel is null || !ViewModel.ShowAoeLayer) return null;
        for (int i = scene.AoeTemplates.Count - 1; i >= 0; i--)
        {
            var aoe = scene.AoeTemplates[i];
            if ((mapPt - new Point(aoe.OriginX, aoe.OriginY)).Length <= 24.0 * invZoom)
                return aoe;
        }
        return null;
    }

    private AoeTemplateModel? HitTestHoveredAoe(SceneViewModel scene, Point mapPt, double invZoom)
    {
        if (ViewModel is null || !ViewModel.ShowAoeLayer || scene.AoeTemplates.Count == 0) return null;
        double pxPerFoot = Math.Max(1.0, scene.Grid.PixelsPerCell) / Math.Max(1.0, scene.Grid.FeetPerCell);

        // First prioritize hovering directly over an AoE origin anchor
        var byOrigin = HitTestAoeOrigin(scene, mapPt, invZoom);
        if (byOrigin is not null) return byOrigin;

        // Otherwise check top-most AoE body
        for (int i = scene.AoeTemplates.Count - 1; i >= 0; i--)
        {
            var aoe = scene.AoeTemplates[i];
            Point origin = new(aoe.OriginX, aoe.OriginY);
            double sizePx = aoe.SizeFeet * pxPerFoot;
            Vector v = mapPt - origin;

            switch (aoe.Shape)
            {
                case AoeShape.Sphere:
                    if (v.Length <= sizePx) return aoe;
                    break;
                case AoeShape.Cube:
                {
                    double rad = -aoe.DirectionDegrees * Math.PI / 180.0;
                    double lx = v.X * Math.Cos(rad) - v.Y * Math.Sin(rad);
                    double ly = v.X * Math.Sin(rad) + v.Y * Math.Cos(rad);
                    if (Math.Abs(lx) <= sizePx * 0.5 && Math.Abs(ly) <= sizePx * 0.5) return aoe;
                    break;
                }
                case AoeShape.Cone:
                {
                    if (v.Length <= sizePx && v.Length > 0.1)
                    {
                        double rad = aoe.DirectionDegrees * Math.PI / 180.0;
                        Vector dir = new(Math.Cos(rad), Math.Sin(rad));
                        double dot = (v.X * dir.X + v.Y * dir.Y) / v.Length;
                        // cos(atan(0.5)) = 1 / sqrt(1.25) ~= 0.8944
                        if (dot >= 0.89) return aoe;
                    }
                    break;
                }
                case AoeShape.Line:
                {
                    double rad = -aoe.DirectionDegrees * Math.PI / 180.0;
                    double lx = v.X * Math.Cos(rad) - v.Y * Math.Sin(rad);
                    double ly = v.X * Math.Sin(rad) + v.Y * Math.Cos(rad);
                    double halfW = Math.Max(5.0, aoe.WidthFeet * pxPerFoot) * 0.5;
                    if (lx >= 0 && lx <= sizePx && Math.Abs(ly) <= halfW) return aoe;
                    break;
                }
            }
        }
        return null;
    }

    #endregion

    #region Mouse & Keyboard Interaction (DM View)

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (!IsDmView || ViewModel is null) return;

        e.Handled = true;
        Point cursorScreen = e.GetPosition(this);

        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
        {
            if (ViewModel.ActiveTool == DmToolMode.FogBrush)
            {
                double factor = e.Delta > 0 ? 1.15 : (1.0 / 1.15);
                ViewModel.FogBrushRadius *= factor;
                return;
            }
            if (ViewModel.SelectedAoeTemplate is not null)
            {
                ViewModel.SelectedAoeTemplate.DirectionDegrees = (ViewModel.SelectedAoeTemplate.DirectionDegrees + (e.Delta > 0 ? 15 : -15)) % 360;
                return;
            }
        }

        double zoomFactor = e.Delta > 0 ? 1.16 : (1.0 / 1.16);
        ViewModel.DmCamera = ViewportMath.ZoomAroundScreenPoint(ViewModel.DmCamera, RenderSize, cursorScreen, zoomFactor);
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        if (!IsDmView || ViewModel is null) return;
        Focus();

        Point screenPt = e.GetPosition(this);

        // Middle mouse button ALWAYS pans (Ctrl+Middle pans TV directly!)
        if (e.ChangedButton == MouseButton.Middle)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0 && ReferenceEquals(ViewModel.StagedScene, ViewModel.LiveTvScene))
            {
                if (ViewModel.TvProfile.SyncMode == TvSyncMode.MirrorDm)
                    ViewModel.TvProfile.SyncMode = TvSyncMode.IndependentTv;
                _isCtrlMiddlePanningTv = true;
                Cursor = Cursors.SizeAll;
            }
            else
            {
                _isMiddlePanning = true;
                Cursor = Cursors.ScrollAll;
            }
            _lastPanScreenPt = screenPt;
            CaptureMouse();
            e.Handled = true;
            return;
        }

        var scene = ViewModel.StagedScene;
        if (scene is null) return;

        Point mapPt = ViewportMath.ScreenToMap(screenPt, ViewModel.DmCamera, RenderSize);
        double invZoom = 1.0 / Math.Max(0.04, ViewModel.DmCamera.Zoom);

        // Alt + LeftClick triggers Ping from ANY tool!
        if (e.ChangedButton == MouseButton.Left && (Keyboard.Modifiers & ModifierKeys.Alt) != 0)
        {
            ViewModel.TriggerPing(mapPt);
            SetAnimationLoop(true);
            e.Handled = true;
            return;
        }

        // Space + LeftClick pans from ANY tool!
        if (e.ChangedButton == MouseButton.Left && Keyboard.IsKeyDown(Key.Space))
        {
            _isLeftCanvasPanning = true;
            _lastPanScreenPt = screenPt;
            Cursor = Cursors.ScrollAll;
            CaptureMouse();
            e.Handled = true;
            return;
        }

        // Direct TV VIEW Top Pill Badge Drag — works from ANY active tool without switching!
        if (e.ChangedButton == MouseButton.Left
            && ViewModel.ShowTvFrameGuide
            && ReferenceEquals(scene, ViewModel.LiveTvScene)
            && _tvBadgeMapHitRect is Rect tvBadgeRect
            && tvBadgeRect.Contains(mapPt))
        {
            if (ViewModel.TvProfile.SyncMode == TvSyncMode.MirrorDm)
                ViewModel.TvProfile.SyncMode = TvSyncMode.IndependentTv;

            _isDraggingTvFrame = true;
            _tvFrameGrabMapOffset = new Point(ViewModel.TvCamera.CenterMapX, ViewModel.TvCamera.CenterMapY) - mapPt;
            Cursor = Cursors.SizeAll;
            CaptureMouse();
            e.Handled = true;
            return;
        }

        // Check clickable 1-Click Room Badges first!
        if (ViewModel.ShowDmRoomBadges && ViewModel.ActiveTool != DmToolMode.RoomPrep)
        {
            for (int i = _roomBadgeHits.Count - 1; i >= 0; i--)
            {
                if (_roomBadgeHits[i].MapHitRect.Contains(mapPt))
                {
                    var room = _roomBadgeHits[i].Room;
                    if (e.ChangedButton == MouseButton.Left)
                    {
                        scene.ToggleRoomReveal(room, ViewModel.FogFeatherSoftness);
                        ViewModel.HasUnsavedChanges = true;
                        ViewModel.NotifyUndoRedoChanged();
                        e.Handled = true;
                        return;
                    }
                    if (e.ChangedButton == MouseButton.Right)
                    {
                        ShowRoomContextMenu(scene, room);
                        e.Handled = true;
                        return;
                    }
                }
            }
        }

        // Universal Right-Click Context Menu (except when FogBrush right-drags inverse fog or Polygon draft is closing)
        if (e.ChangedButton == MouseButton.Right)
        {
            if (ViewModel.ActiveTool == DmToolMode.FogBrush)
            {
                // Handled below in FogBrush switch case
            }
            else if (ViewModel.ActiveTool is DmToolMode.FogPolygon or DmToolMode.RoomPrep && _polygonDraftPoints.Count > 0)
            {
                CommitPolygonDraft(scene);
                e.Handled = true;
                return;
            }
            else
            {
                ShowCanvasOrElementContextMenu(scene, mapPt, invZoom);
                e.Handled = true;
                return;
            }
        }

        // Dispatch by ActiveTool
        switch (ViewModel.ActiveTool)
        {
            case DmToolMode.PanSelect:
                HandlePanSelectMouseDown(e, screenPt, mapPt, invZoom, scene);
                break;

            case DmToolMode.FogBrush:
                if (e.ChangedButton is MouseButton.Left or MouseButton.Right && scene.FogEngine is not null)
                {
                    _isPaintingFogBrush = true;
                    _activeBrushOp = e.ChangedButton == MouseButton.Right
                        ? (ViewModel.FogOperation == FogPaintOperation.Reveal ? FogPaintOperation.Hide : FogPaintOperation.Reveal)
                        : ViewModel.FogOperation;

                    scene.FogEngine.BeginBrushStroke(mapPt, ViewModel.FogBrushRadius, _activeBrushOp, ViewModel.FogFeatherSoftness);
                    scene.RecordFogHistoryStep("Fog Brush");
                    ViewModel.HasUnsavedChanges = true;
                    ViewModel.NotifyUndoRedoChanged();
                    CaptureMouse();
                    InvalidateVisual();
                    e.Handled = true;
                }
                break;

            case DmToolMode.FogBox:
                if (e.ChangedButton == MouseButton.Left)
                {
                    _isDraggingFogBox = true;
                    _fogBoxStartMapPt = mapPt;
                    _fogBoxCurrentMapPt = mapPt;
                    CaptureMouse();
                    InvalidateVisual();
                    e.Handled = true;
                }
                break;

            case DmToolMode.FogPolygon:
            case DmToolMode.RoomPrep:
                HandlePolygonToolMouseDown(e, mapPt, invZoom, scene);
                break;

            case DmToolMode.GridAlign3x3:
                if (e.ChangedButton == MouseButton.Left)
                {
                    Ensure3x3BoxInitialized(scene);
                    double handleR = 14.0 * invZoom;
                    if ((mapPt - _align3x3Rect.BottomRight).Length <= handleR)
                        _align3x3DragMode = 2;
                    else if ((mapPt - _align3x3Rect.TopLeft).Length <= handleR)
                        _align3x3DragMode = 3;
                    else if (_align3x3Rect.Contains(mapPt))
                        _align3x3DragMode = 1;
                    else
                    {
                        _align3x3Rect = new Rect(mapPt.X, mapPt.Y, _align3x3Rect.Width, _align3x3Rect.Height);
                        _align3x3DragMode = 1;
                        Apply3x3BoxToGrid(scene);
                    }

                    _align3x3DragStartMap = mapPt;
                    _align3x3StartRect = _align3x3Rect;
                    CaptureMouse();
                    InvalidateVisual();
                    e.Handled = true;
                }
                break;

            case DmToolMode.Pencil:
                if (e.ChangedButton == MouseButton.Left)
                {
                    _activePencilStroke = new PencilStrokeModel
                    {
                        ColorHex = ViewModel.PencilColorHex,
                        Thickness = ViewModel.PencilThickness,
                        Points = [new Point2D(mapPt.X, mapPt.Y)]
                    };
                    CaptureMouse();
                    InvalidateVisual();
                    e.Handled = true;
                }
                break;

            case DmToolMode.Ping:
                if (e.ChangedButton == MouseButton.Left)
                {
                    ViewModel.TriggerPing(mapPt);
                    SetAnimationLoop(true);
                    e.Handled = true;
                }
                break;

            case DmToolMode.Ruler:
                if (e.ChangedButton == MouseButton.Left)
                {
                    ViewModel.RulerStartMapPt = mapPt;
                    ViewModel.RulerEndMapPt = mapPt;
                    ViewModel.IsRulerActive = true;
                    CaptureMouse();
                    e.Handled = true;
                }
                break;

            case DmToolMode.PlaceAoe:
                if (e.ChangedButton == MouseButton.Left)
                {
                    if (TryGrabAoeRotationHandle(scene, mapPt, invZoom))
                    {
                        CaptureMouse();
                        e.Handled = true;
                        return;
                    }

                    if (HitTestAoeOrigin(scene, mapPt, invZoom) is { } existingAoe)
                    {
                        ViewModel.SelectedAoeTemplate = existingAoe;
                        ViewModel.SelectedToken = null;
                        ViewModel.SelectedDmPin = null;
                        _draggingAoeOrigin = existingAoe;
                        _elementDragStartPt = new Point(existingAoe.OriginX, existingAoe.OriginY);
                        _dragElementOffset = _elementDragStartPt - mapPt;
                        _isNewPlacementDrag = false;
                        CaptureMouse();
                        e.Handled = true;
                        return;
                    }

                    var created = ViewModel.PlaceNewAoeAt(mapPt);
                    _rotatingAoe = created;
                    _aoeRotateStartDeg = created.DirectionDegrees;
                    _isNewPlacementDrag = true;
                    CaptureMouse();
                    e.Handled = true;
                }
                break;

            case DmToolMode.PlaceToken:
                if (e.ChangedButton == MouseButton.Left)
                {
                    if (HitTestToken(scene, mapPt, invZoom) is { } existingTok)
                    {
                        ViewModel.SelectedToken = existingTok;
                        ViewModel.SelectedAoeTemplate = null;
                        ViewModel.SelectedDmPin = null;
                        _draggingToken = existingTok;
                        _elementDragStartPt = new Point(existingTok.MapX, existingTok.MapY);
                        _dragElementOffset = _elementDragStartPt - mapPt;
                        _isNewPlacementDrag = false;
                        CaptureMouse();
                        e.Handled = true;
                        return;
                    }

                    var tok = ViewModel.PlaceNewTokenAt(mapPt);
                    _draggingToken = tok;
                    _elementDragStartPt = new Point(tok.MapX, tok.MapY);
                    _dragElementOffset = new Vector(0, 0);
                    _isNewPlacementDrag = true;
                    CaptureMouse();
                    e.Handled = true;
                }
                break;

            case DmToolMode.PlaceDmPin:
                if (e.ChangedButton == MouseButton.Left)
                {
                    if (HitTestDmPin(scene, mapPt, invZoom) is { } existingPin)
                    {
                        ViewModel.SelectedDmPin = existingPin;
                        ViewModel.SelectedToken = null;
                        ViewModel.SelectedAoeTemplate = null;
                        _draggingPin = existingPin;
                        _elementDragStartPt = new Point(existingPin.MapX, existingPin.MapY);
                        _dragElementOffset = _elementDragStartPt - mapPt;
                        _isNewPlacementDrag = false;
                        CaptureMouse();
                        e.Handled = true;
                        return;
                    }

                    var pin = ViewModel.PlaceNewDmPinAt(mapPt);
                    _draggingPin = pin;
                    _elementDragStartPt = new Point(pin.MapX, pin.MapY);
                    _dragElementOffset = new Vector(0, 0);
                    _isNewPlacementDrag = true;
                    CaptureMouse();
                    e.Handled = true;
                }
                break;
        }
    }

    private void HandlePanSelectMouseDown(MouseButtonEventArgs e, Point screenPt, Point mapPt, double invZoom, SceneViewModel scene)
    {
        var vm = ViewModel!;
        if (e.ChangedButton != MouseButton.Left) return;

        // 1. Check Selected AoE rotation handle
        if (TryGrabAoeRotationHandle(scene, mapPt, invZoom))
        {
            CaptureMouse();
            e.Handled = true;
            return;
        }

        // 2. Check DM Pins
        if (HitTestDmPin(scene, mapPt, invZoom) is { } pin)
        {
            vm.SelectedDmPin = pin;
            vm.SelectedToken = null;
            vm.SelectedAoeTemplate = null;
            _draggingPin = pin;
            _elementDragStartPt = new Point(pin.MapX, pin.MapY);
            _dragElementOffset = _elementDragStartPt - mapPt;
            _isNewPlacementDrag = false;
            CaptureMouse();
            e.Handled = true;
            return;
        }

        // 3. Check Tokens / Rings
        if (HitTestToken(scene, mapPt, invZoom) is { } tok)
        {
            vm.SelectedToken = tok;
            vm.SelectedAoeTemplate = null;
            vm.SelectedDmPin = null;
            _draggingToken = tok;
            _elementDragStartPt = new Point(tok.MapX, tok.MapY);
            _dragElementOffset = _elementDragStartPt - mapPt;
            _isNewPlacementDrag = false;
            CaptureMouse();
            e.Handled = true;
            return;
        }

        // 4. Check AoE Template origins (or body if hovered)
        var aoeHit = HitTestAoeOrigin(scene, mapPt, invZoom) ?? HitTestHoveredAoe(scene, mapPt, invZoom);
        if (aoeHit is not null)
        {
            vm.SelectedAoeTemplate = aoeHit;
            vm.SelectedToken = null;
            vm.SelectedDmPin = null;
            _draggingAoeOrigin = aoeHit;
            _elementDragStartPt = new Point(aoeHit.OriginX, aoeHit.OriginY);
            _dragElementOffset = _elementDragStartPt - mapPt;
            _isNewPlacementDrag = false;
            CaptureMouse();
            e.Handled = true;
            return;
        }

        // 5. Check Draggable Green TV Viewport Frame when TV is in 1-Inch Lock or Independent mode!
        if (vm.ShowTvFrameGuide && vm.TvProfile.SyncMode != TvSyncMode.MirrorDm && ReferenceEquals(scene, vm.LiveTvScene))
        {
            Rect tvBounds = ViewportMath.GetVisibleMapBounds(vm.TvCamera, vm.TvViewportSize);
            if (tvBounds.Contains(mapPt))
            {
                _isDraggingTvFrame = true;
                _tvFrameGrabMapOffset = new Point(vm.TvCamera.CenterMapX, vm.TvCamera.CenterMapY) - mapPt;
                Cursor = Cursors.SizeAll;
                CaptureMouse();
                e.Handled = true;
                return;
            }
        }

        // 6. Otherwise pan DM viewport
        vm.SelectedToken = null;
        vm.SelectedAoeTemplate = null;
        vm.SelectedDmPin = null;
        _isLeftCanvasPanning = true;
        _lastPanScreenPt = screenPt;
        CaptureMouse();
        e.Handled = true;
    }

    private void HandlePolygonToolMouseDown(MouseButtonEventArgs e, Point mapPt, double invZoom, SceneViewModel scene)
    {
        if (e.ChangedButton != MouseButton.Left) return;

        if (e.ClickCount >= 2 && _polygonDraftPoints.Count >= 3)
        {
            CommitPolygonDraft(scene);
            e.Handled = true;
            return;
        }

        // Close if clicking near first vertex
        if (_polygonDraftPoints.Count >= 3)
        {
            Point first = new(_polygonDraftPoints[0].X, _polygonDraftPoints[0].Y);
            if ((mapPt - first).Length <= 14.0 * invZoom)
            {
                CommitPolygonDraft(scene);
                e.Handled = true;
                return;
            }
        }

        _polygonDraftPoints.Add(new Point2D(mapPt.X, mapPt.Y));
        InvalidateVisual();
        e.Handled = true;
    }

    private void CommitPolygonDraft(SceneViewModel scene)
    {
        var vm = ViewModel;
        if (vm is null || _polygonDraftPoints.Count < 3)
        {
            _polygonDraftPoints.Clear();
            InvalidateVisual();
            return;
        }

        var copy = _polygonDraftPoints.ToList();
        _polygonDraftPoints.Clear();

        if (vm.ActiveTool == DmToolMode.RoomPrep)
        {
            int nextNum = scene.Rooms.Count + 1;
            var room = new FogRoomRegion
            {
                Name = $"Room {nextNum}",
                Points = copy,
                IsRevealed = false
            };
            scene.Rooms.Add(room);
            scene.RecordCustomHistoryStep(
                $"Create {room.Name}",
                undoAction: () => scene.Rooms.Remove(room),
                redoAction: () => scene.Rooms.Add(room));
            vm.StatusMessage = $"Created clickable '{room.Name}'. Click its badge anytime to reveal/hide!";
        }
        else if (scene.FogEngine is not null)
        {
            scene.FogEngine.ApplyPolygon(copy, vm.FogOperation, vm.FogFeatherSoftness);
            scene.RecordFogHistoryStep("Fog Polygon");
            vm.NotifyUndoRedoChanged();
        }

        vm.HasUnsavedChanges = true;
        InvalidateVisual();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!IsDmView || ViewModel is null) return;

        Point screenPt = e.GetPosition(this);

        if (_isCtrlMiddlePanningTv)
        {
            Vector delta = screenPt - _lastPanScreenPt;
            _lastPanScreenPt = screenPt;
            if (ViewModel.StagedScene is { } s)
            {
                double dmZoom = Math.Max(0.05, ViewModel.DmCamera.Zoom);
                ViewModel.TvCamera = ViewModel.TvCamera with
                {
                    CenterMapX = Math.Clamp(ViewModel.TvCamera.CenterMapX + delta.X / dmZoom, 0, s.PixelWidth),
                    CenterMapY = Math.Clamp(ViewModel.TvCamera.CenterMapY + delta.Y / dmZoom, 0, s.PixelHeight)
                };
            }
            return;
        }

        if (_isMiddlePanning || _isLeftCanvasPanning)
        {
            Vector delta = screenPt - _lastPanScreenPt;
            _lastPanScreenPt = screenPt;
            ViewModel.DmCamera = ViewportMath.PanByScreenDelta(ViewModel.DmCamera, RenderSize, delta);
            return;
        }

        var scene = ViewModel.StagedScene;
        if (scene is null) return;

        Point mapPt = ViewportMath.ScreenToMap(screenPt, ViewModel.DmCamera, RenderSize);
        _hoverMapPoint = mapPt;

        if (_isDraggingTvFrame)
        {
            Point newTvCenter = mapPt + _tvFrameGrabMapOffset;
            ViewModel.TvCamera = ViewModel.TvCamera with
            {
                CenterMapX = Math.Clamp(newTvCenter.X, 0, scene.PixelWidth),
                CenterMapY = Math.Clamp(newTvCenter.Y, 0, scene.PixelHeight)
            };
            return;
        }

        if (_isPaintingFogBrush && scene.FogEngine is not null)
        {
            scene.FogEngine.ContinueBrushStroke(mapPt, ViewModel.FogBrushRadius, _activeBrushOp, ViewModel.FogFeatherSoftness);
            InvalidateVisual();
            return;
        }

        if (_isDraggingFogBox)
        {
            _fogBoxCurrentMapPt = mapPt;
            InvalidateVisual();
            return;
        }

        if (_align3x3DragMode != 0)
        {
            Vector d = mapPt - _align3x3DragStartMap;
            if (_align3x3DragMode == 1)
            {
                _align3x3Rect = new Rect(_align3x3StartRect.X + d.X, _align3x3StartRect.Y + d.Y, _align3x3StartRect.Width, _align3x3StartRect.Height);
            }
            else if (_align3x3DragMode == 2)
            {
                double size = Math.Max(24.0, Math.Max(mapPt.X - _align3x3StartRect.Left, mapPt.Y - _align3x3StartRect.Top));
                _align3x3Rect = new Rect(_align3x3StartRect.Left, _align3x3StartRect.Top, size, size);
            }
            else if (_align3x3DragMode == 3)
            {
                double newLeft = Math.Min(_align3x3StartRect.Right - 24.0, mapPt.X);
                double newTop = Math.Min(_align3x3StartRect.Bottom - 24.0, mapPt.Y);
                _align3x3Rect = new Rect(new Point(newLeft, newTop), _align3x3StartRect.BottomRight);
            }
            Apply3x3BoxToGrid(scene);
            InvalidateVisual();
            return;
        }

        if (_activePencilStroke is not null)
        {
            var last = _activePencilStroke.Points[^1];
            double dx = mapPt.X - last.X, dy = mapPt.Y - last.Y;
            if (dx * dx + dy * dy >= 4.0)
            {
                _activePencilStroke.Points.Add(new Point2D(mapPt.X, mapPt.Y));
                InvalidateVisual();
            }
            return;
        }

        if (ViewModel.IsRulerActive && e.LeftButton == MouseButtonState.Pressed)
        {
            ViewModel.RulerEndMapPt = mapPt;
            return;
        }

        if (_draggingToken is not null)
        {
            Point raw = mapPt + _dragElementOffset;
            Point target = ViewModel.SnapTokensToGrid
                ? ViewModel.SnapPointToCellCenter(raw, scene.Grid, _draggingToken.SizeCells)
                : raw;
            _draggingToken.MapX = target.X;
            _draggingToken.MapY = target.Y;
            return;
        }

        if (_draggingAoeOrigin is not null)
        {
            Point target = mapPt + _dragElementOffset;
            _draggingAoeOrigin.OriginX = target.X;
            _draggingAoeOrigin.OriginY = target.Y;
            return;
        }

        if (_rotatingAoe is not null)
        {
            Vector v = mapPt - new Point(_rotatingAoe.OriginX, _rotatingAoe.OriginY);
            if (v.Length > 3.0)
            {
                _rotatingAoe.DirectionDegrees = Math.Atan2(v.Y, v.X) * 180.0 / Math.PI;
            }
            return;
        }

        if (_draggingPin is not null)
        {
            Point target = mapPt + _dragElementOffset;
            _draggingPin.MapX = target.X;
            _draggingPin.MapY = target.Y;
            return;
        }

        // Hover feedback: cursor on TV VIEW badge + hovered AoE pill
        double invZoom = 1.0 / Math.Max(0.04, ViewModel.DmCamera.Zoom);
        bool overTvBadge = _tvBadgeMapHitRect is Rect bRect && bRect.Contains(mapPt);
        Cursor = overTvBadge ? Cursors.SizeAll : Cursors.Arrow;

        var newHoverAoe = HitTestHoveredAoe(scene, mapPt, invZoom);
        bool hoverChanged = !ReferenceEquals(_hoveredAoe, newHoverAoe);
        _hoveredAoe = newHoverAoe;

        if (hoverChanged || ViewModel.ActiveTool is DmToolMode.FogBrush or DmToolMode.FogPolygon or DmToolMode.RoomPrep)
        {
            InvalidateVisual();
        }
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (!IsDmView || ViewModel is null) return;

        if (e.ChangedButton == MouseButton.Middle && (_isMiddlePanning || _isCtrlMiddlePanningTv))
        {
            _isMiddlePanning = false;
            _isCtrlMiddlePanningTv = false;
            Cursor = Cursors.Arrow;
            ReleaseMouseCapture();
            e.Handled = true;
            return;
        }

        if (_isLeftCanvasPanning)
        {
            _isLeftCanvasPanning = false;
            Cursor = Cursors.Arrow;
            ReleaseMouseCapture();
        }

        if (_isDraggingTvFrame)
        {
            _isDraggingTvFrame = false;
            Cursor = Cursors.Arrow;
            ViewModel.HasUnsavedChanges = true;
            ReleaseMouseCapture();
        }

        if (_isPaintingFogBrush)
        {
            _isPaintingFogBrush = false;
            ViewModel.StagedScene?.FogEngine?.EndBrushStroke();
            ReleaseMouseCapture();
            InvalidateVisual();
        }

        if (_isDraggingFogBox)
        {
            _isDraggingFogBox = false;
            ReleaseMouseCapture();
            var rect = new Rect(_fogBoxStartMapPt, _fogBoxCurrentMapPt);
            if (ViewModel.StagedScene?.FogEngine is not null && rect.Width > 2 && rect.Height > 2)
            {
                ViewModel.StagedScene.FogEngine.ApplyRectangle(rect, ViewModel.FogOperation, ViewModel.FogFeatherSoftness);
                ViewModel.StagedScene.RecordFogHistoryStep("Fog Box");
                ViewModel.HasUnsavedChanges = true;
                ViewModel.NotifyUndoRedoChanged();
            }
            InvalidateVisual();
        }

        if (_align3x3DragMode != 0)
        {
            _align3x3DragMode = 0;
            ReleaseMouseCapture();
        }

        if (_activePencilStroke is not null)
        {
            var finished = _activePencilStroke;
            _activePencilStroke = null;
            ReleaseMouseCapture();
            ViewModel.AddPencilStrokeWithHistory(finished);
        }

        if (_draggingToken is { } movedTok)
        {
            _draggingToken = null;
            Point start = _elementDragStartPt;
            Point end = new(movedTok.MapX, movedTok.MapY);
            if (!_isNewPlacementDrag && (end - start).Length > 1.0 && ViewModel.StagedScene is { } s)
            {
                s.RecordCustomHistoryStep(
                    "Move Ring/Token",
                    undoAction: () => { movedTok.MapX = start.X; movedTok.MapY = start.Y; },
                    redoAction: () => { movedTok.MapX = end.X; movedTok.MapY = end.Y; });
            }
            _isNewPlacementDrag = false;
            ReleaseMouseCapture();
        }

        if (_draggingAoeOrigin is { } movedAoe)
        {
            _draggingAoeOrigin = null;
            Point start = _elementDragStartPt;
            Point end = new(movedAoe.OriginX, movedAoe.OriginY);
            if (!_isNewPlacementDrag && (end - start).Length > 1.0 && ViewModel.StagedScene is { } s)
            {
                s.RecordCustomHistoryStep(
                    "Move Spell AoE",
                    undoAction: () => { movedAoe.OriginX = start.X; movedAoe.OriginY = start.Y; },
                    redoAction: () => { movedAoe.OriginX = end.X; movedAoe.OriginY = end.Y; });
            }
            _isNewPlacementDrag = false;
            ReleaseMouseCapture();
        }

        if (_rotatingAoe is { } rotAoe)
        {
            _rotatingAoe = null;
            double startDeg = _aoeRotateStartDeg;
            double endDeg = rotAoe.DirectionDegrees;
            if (!_isNewPlacementDrag && Math.Abs(endDeg - startDeg) > 0.5 && ViewModel.StagedScene is { } s)
            {
                s.RecordCustomHistoryStep(
                    "Rotate Spell AoE",
                    undoAction: () => rotAoe.DirectionDegrees = startDeg,
                    redoAction: () => rotAoe.DirectionDegrees = endDeg);
            }
            _isNewPlacementDrag = false;
            ReleaseMouseCapture();
        }

        if (_draggingPin is { } movedPin)
        {
            _draggingPin = null;
            Point start = _elementDragStartPt;
            Point end = new(movedPin.MapX, movedPin.MapY);
            if (!_isNewPlacementDrag && (end - start).Length > 1.0 && ViewModel.StagedScene is { } s)
            {
                s.RecordCustomHistoryStep(
                    "Move DM Pin",
                    undoAction: () => { movedPin.MapX = start.X; movedPin.MapY = start.Y; },
                    redoAction: () => { movedPin.MapX = end.X; movedPin.MapY = end.Y; });
            }
            _isNewPlacementDrag = false;
            ReleaseMouseCapture();
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _hoverMapPoint = null;
        _hoveredAoe = null;
        if (IsDmView) InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!IsDmView || ViewModel is null) return;

        if (e.Key == Key.Escape)
        {
            if (_polygonDraftPoints.Count > 0)
            {
                _polygonDraftPoints.Clear();
                InvalidateVisual();
                e.Handled = true;
            }
            else if (ViewModel.IsRulerActive)
            {
                ViewModel.IsRulerActive = false;
                e.Handled = true;
            }
            else
            {
                ViewModel.SelectedToken = null;
                ViewModel.SelectedAoeTemplate = null;
                ViewModel.SelectedDmPin = null;
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Enter && _polygonDraftPoints.Count >= 3 && ViewModel.StagedScene is not null)
        {
            CommitPolygonDraft(ViewModel.StagedScene);
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            ViewModel.DeleteSelectedElement();
            e.Handled = true;
        }
        else if (e.Key == Key.OemOpenBrackets)
        {
            ViewModel.FogBrushRadius *= 0.85;
            e.Handled = true;
        }
        else if (e.Key == Key.OemCloseBrackets)
        {
            ViewModel.FogBrushRadius *= 1.18;
            e.Handled = true;
        }
    }

    private void Apply3x3BoxToGrid(SceneViewModel scene)
    {
        double cellPx = Math.Max(8.0, _align3x3Rect.Width / 3.0);
        scene.Grid.IsVisible = true;
        scene.Grid.PixelsPerCell = cellPx;
        scene.Grid.OffsetX = ((_align3x3Rect.Left % cellPx) + cellPx) % cellPx;
        scene.Grid.OffsetY = ((_align3x3Rect.Top % cellPx) + cellPx) % cellPx;

        if (ViewModel is not null)
        {
            ViewModel.HasUnsavedChanges = true;
            if (ViewModel.TvProfile.SyncMode == TvSyncMode.PhysicalOneInch)
                ViewModel.UpdateTvCameraSync();
            ViewModel.StatusMessage = $"Calibrated Grid: {cellPx:0.1} px/square (Offset {scene.Grid.OffsetX:0.1}, {scene.Grid.OffsetY:0.1}).";
        }
    }

    private void ShowRoomContextMenu(SceneViewModel scene, FogRoomRegion room)
    {
        var vm = ViewModel;
        var menu = new ContextMenu();

        var toggleItem = new MenuItem { Header = room.IsRevealed ? "Hide Room Fog" : "Reveal Room Fog" };
        toggleItem.Click += (_, _) =>
        {
            scene.ToggleRoomReveal(room, vm?.FogFeatherSoftness ?? 0.06);
            if (vm is not null) vm.HasUnsavedChanges = true;
        };

        var centerTvItem = new MenuItem { Header = "Center TV View on Room" };
        centerTvItem.Click += (_, _) => vm?.CenterTvAtMapPoint(new Point(room.Centroid.X, room.Centroid.Y));

        var deleteItem = new MenuItem { Header = $"Delete '{room.Name}'" };
        deleteItem.Click += (_, _) => vm?.DeleteRoom(room);

        menu.Items.Add(toggleItem);
        menu.Items.Add(centerTvItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(deleteItem);
        menu.IsOpen = true;
    }

    private void ShowCanvasOrElementContextMenu(SceneViewModel scene, Point mapPt, double invZoom)
    {
        var vm = ViewModel;
        if (vm is null) return;

        var menu = new ContextMenu();

        if (HitTestDmPin(scene, mapPt, invZoom) is { } pin)
        {
            vm.SelectedDmPin = pin;
            var inspectItem = new MenuItem { Header = $"Inspect Pin '{pin.Code}: {pin.Title}'" };
            inspectItem.Click += (_, _) => vm.IsRightInspectorExpanded = true;

            var centerItem = new MenuItem { Header = "Center TV View Here" };
            centerItem.Click += (_, _) => vm.CenterTvAtMapPoint(new Point(pin.MapX, pin.MapY));

            var delItem = new MenuItem { Header = "Delete Secret Pin" };
            delItem.Click += (_, _) => vm.DeleteDmPin(pin);

            menu.Items.Add(inspectItem);
            menu.Items.Add(centerItem);
            menu.Items.Add(new Separator());
            menu.Items.Add(delItem);
            menu.IsOpen = true;
            return;
        }

        if (HitTestToken(scene, mapPt, invZoom) is { } tok)
        {
            vm.SelectedToken = tok;

            var visItem = new MenuItem { Header = "Visible to Players on TV", IsCheckable = true, IsChecked = tok.VisibleToPlayers };
            visItem.Click += (_, _) => tok.VisibleToPlayers = visItem.IsChecked;

            var presetSub = new MenuItem { Header = "Condition Preset" };
            foreach (var (key, label) in new[]
            {
                ("GlowOnly", "Glow Only (No Label)"),
                ("Concentrating", "Concentrating (Cyan)"),
                ("Bloodied", "Bloodied (Crimson)"),
                ("Blessed", "Blessed (Gold)"),
                ("Hexed", "Hexed / Hunter's Mark (Purple)"),
                ("Poisoned", "Poisoned (Emerald)"),
                ("Prone", "Prone (Amber)"),
                ("Aura10", "10ft Aura (Gold)"),
                ("Aura15", "15ft Spirit Aura (Aqua)")
            })
            {
                var sub = new MenuItem { Header = label };
                string captured = key;
                sub.Click += (_, _) => vm.ApplyConditionPreset(captured);
                presetSub.Items.Add(sub);
            }

            var centerItem = new MenuItem { Header = "Center TV View Here" };
            centerItem.Click += (_, _) => vm.CenterTvAtMapPoint(new Point(tok.MapX, tok.MapY));

            var delItem = new MenuItem { Header = "Delete Ring / Token", InputGestureText = "Del" };
            delItem.Click += (_, _) => vm.DeleteToken(tok);

            var clearAllItem = new MenuItem { Header = $"Clear All Rings & Tokens ({scene.Tokens.Count})" };
            clearAllItem.Click += (_, _) => vm.ClearAllTokens();

            menu.Items.Add(visItem);
            menu.Items.Add(presetSub);
            menu.Items.Add(centerItem);
            menu.Items.Add(new Separator());
            menu.Items.Add(delItem);
            menu.Items.Add(clearAllItem);
            menu.IsOpen = true;
            return;
        }

        var hitAoe = HitTestAoeOrigin(scene, mapPt, invZoom) ?? HitTestHoveredAoe(scene, mapPt, invZoom);
        if (hitAoe is not null)
        {
            vm.SelectedAoeTemplate = hitAoe;

            var visItem = new MenuItem { Header = "Visible to Players on TV", IsCheckable = true, IsChecked = hitAoe.VisibleToPlayers };
            visItem.Click += (_, _) => hitAoe.VisibleToPlayers = visItem.IsChecked;

            var centerItem = new MenuItem { Header = "Center TV View Here" };
            centerItem.Click += (_, _) => vm.CenterTvAtMapPoint(new Point(hitAoe.OriginX, hitAoe.OriginY));

            var delItem = new MenuItem { Header = $"Delete {hitAoe.Label}", InputGestureText = "Del" };
            delItem.Click += (_, _) => vm.DeleteAoe(hitAoe);

            var clearAllItem = new MenuItem { Header = $"Clear All Spell AoEs ({scene.AoeTemplates.Count})" };
            clearAllItem.Click += (_, _) => vm.ClearAllAoes();

            menu.Items.Add(visItem);
            menu.Items.Add(centerItem);
            menu.Items.Add(new Separator());
            menu.Items.Add(delItem);
            menu.Items.Add(clearAllItem);
            menu.IsOpen = true;
            return;
        }

        // Empty map canvas right-click menu
        var centerHere = new MenuItem { Header = "Center TV View Here", InputGestureText = "C" };
        centerHere.Click += (_, _) => vm.CenterTvAtMapPoint(mapPt);

        var pingHere = new MenuItem { Header = "Sonar Ping Here", InputGestureText = "Alt+Click" };
        pingHere.Click += (_, _) => { vm.TriggerPing(mapPt); SetAnimationLoop(true); };

        var fitItem = new MenuItem { Header = "Fit Map to DM View", InputGestureText = "F" };
        fitItem.Click += (_, _) => FitDmToCurrentScene();

        var revealAll = new MenuItem { Header = "Reveal All Fog" };
        revealAll.Click += (_, _) => vm.RevealAllFog();

        var hideAll = new MenuItem { Header = "Reset / Hide All Fog" };
        hideAll.Click += (_, _) => vm.HideAllFog();

        menu.Items.Add(centerHere);
        menu.Items.Add(pingHere);
        menu.Items.Add(fitItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(revealAll);
        menu.Items.Add(hideAll);
        menu.IsOpen = true;
    }

    #endregion
}
