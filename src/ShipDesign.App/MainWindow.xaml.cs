using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using HelixToolkit.Wpf;
using ShipDesign.Core.Procedural;

namespace ShipDesign.App
{
    public partial class MainWindow : Window
    {
        private readonly DispatcherTimer _autoRotateTimer = new() { Interval = TimeSpan.FromMilliseconds(30) };
        private double _autoRotateAngle;

        private ViewModels.MainViewModel? _viewModel;
        private TimeSpan _lastRenderTime = TimeSpan.MinValue;
        private CameraState _lastCamera;

        /// <summary>Top-left of the open panel, in overlay coordinates. Worked out once when the
        /// panel opens and then left alone: the settings inside it move the very part it is anchored
        /// to, so a panel that followed its anchor would slide out from under the pointer in the
        /// middle of the drag that was moving it.</summary>
        private Point? _panelOrigin;

        /// <summary>Allocated once and mutated in place. The leader is redrawn every frame the
        /// camera moves, and handing the Polyline a new PointCollection each time would churn.</summary>
        private readonly PointCollection _leaderPoints = new()
        {
            new Point(), new Point(), new Point(), new Point(),
        };

        /// <summary>Forces the next frame to re-project even though the camera has not moved.
        /// Opening a panel is exactly that case: the leader has to be drawn for the first time
        /// against a camera that is standing perfectly still, and the dirty check would skip it.</summary>
        private bool _overlayDirty = true;

        private bool IsPanelOpen => _panelOrigin is not null;

        public MainWindow()
        {
            InitializeComponent();

            var viewModel = new ViewModels.MainViewModel();
            _viewModel = viewModel;
            DataContext = viewModel;
            viewModel.PropertyChanged += (_, e) =>
            {
                // Re-framing on every rebuild is invisible today, but with an anchored panel it is
                // not: releasing a slider would teleport the anchor under a panel that is standing
                // still, and the leader would swing across the view for no reason the user caused.
                if (e.PropertyName == nameof(ViewModels.MainViewModel.ShipModel) && !IsPanelOpen)
                    Dispatcher.BeginInvoke(new Action(() => Viewport.ZoomExtents()));

                // The dirty check watches the camera, and a rebuild moves the anchors without
                // moving the eye. Without this the markers keep describing the previous ship: the
                // wings would change planform under a marker still sitting where the old tip was.
                if (e.PropertyName == nameof(ViewModels.MainViewModel.Anchors))
                    _overlayDirty = true;
            };

            // The initial ship is assembled synchronously in the view model's constructor,
            // i.e. before the PropertyChanged subscription above can catch it — so zoom once more here.
            Loaded += (_, _) => Viewport.ZoomExtents();

            _autoRotateTimer.Tick += (_, _) => StepAutoRotate();
            AutoRotateToggle.Checked += (_, _) => _autoRotateTimer.Start();
            AutoRotateToggle.Unchecked += (_, _) => _autoRotateTimer.Stop();

            // Per frame rather than on CameraChanged. The camera moves from five independent
            // sources -- the orbit timer, a drag, Helix's own inertia, ZoomExtents and a window
            // resize -- and the orbit timer mutates camera.Position in place instead of reassigning
            // the dependency property, so the change notification never fires for it.
            //
            // The event is static and holds a strong reference to this window, hence the
            // unsubscribe on Closed. Deactivated/Activated as well, so a window sitting behind
            // another one is not driving the composition loop for markers nobody can see.
            CompositionTarget.Rendering += OnFrame;
            Closed += (_, _) => CompositionTarget.Rendering -= OnFrame;
            Deactivated += (_, _) => CompositionTarget.Rendering -= OnFrame;
            Activated += (_, _) =>
            {
                CompositionTarget.Rendering -= OnFrame;
                CompositionTarget.Rendering += OnFrame;
            };

            BuildPins();
            LeaderLine.Points = _leaderPoints;

            // Preview, so it is caught before a control inside the panel can claim it.
            PreviewKeyDown += (_, e) =>
            {
                if (e.Key != Key.Escape || !IsPanelOpen) return;
                ClosePanel();
                e.Handled = true;
            };

            // Two ways the panel can end up outside the view without having moved: it grows when a
            // conditional block inside it appears, and the window gets resized. Both re-constrain
            // rather than re-place -- nudging it back inside is not the same as recentring it, and
            // recentring is exactly what freezing the origin exists to prevent.
            AnchorPanel.SizeChanged += (_, _) => ConstrainPanel();
            AnchorOverlay.SizeChanged += (_, _) =>
            {
                AnchorPanel.MaxHeight = Math.Max(140, AnchorOverlay.ActualHeight - 32);
                ConstrainPanel();
            };
        }

        /// <summary>What the projection depends on. Compared frame to frame so a still scene costs
        /// nothing: without this the overlay would re-arrange sixty times a second forever.</summary>
        private readonly record struct CameraState(
            Point3D Position, Vector3D LookDirection, Vector3D UpDirection,
            double FieldOfView, double Width, double Height, bool HasAnchors);

        private void OnFrame(object? sender, EventArgs e)
        {
            // Rendering can fire more than once for the same frame; the timestamp is what tells
            // them apart.
            if (e is RenderingEventArgs args)
            {
                if (args.RenderingTime == _lastRenderTime) return;
                _lastRenderTime = args.RenderingTime;
            }

            if (Viewport.Camera is not PerspectiveCamera camera) return;

            var state = new CameraState(
                camera.Position, camera.LookDirection, camera.UpDirection, camera.FieldOfView,
                AnchorOverlay.ActualWidth, AnchorOverlay.ActualHeight, _viewModel?.Anchors is not null);

            if (state == _lastCamera && !_overlayDirty) return;
            _lastCamera = state;
            _overlayDirty = false;

            ProjectAnchors(camera);
        }

        private void ProjectAnchors(PerspectiveCamera camera)
        {
            var anchors = _viewModel?.Anchors;
            if (anchors is null)
            {
                foreach (var slot in _pins) slot.Button.Visibility = Visibility.Collapsed;
                LeaderLine.Visibility = Visibility.Collapsed;
                return;
            }

            // Point3DtoPoint2D answers in the *inner* Viewport3D's coordinate space, which lives
            // inside HelixViewport3D's template -- not in the overlay's. The two are siblings, so
            // TransformToVisual (not TransformToAncestor) is what maps between them. Computed once
            // per frame rather than per marker.
            var inner = Viewport.Viewport;
            var toOverlay = inner.TransformToVisual(AnchorOverlay);

            var forward = camera.LookDirection;
            forward.Normalize();

            // The ship's own mid-axis, taken from the two anchors that are always on the centreline.
            // Anything further from the eye than this is on the far side of the hull, and its marker
            // is drawn through the ship -- which the overlay cannot help, being 2D over a 3D view,
            // but can at least admit to by fading. A real per-marker depth test every frame would
            // cost far more than it is worth, and the depth is already in hand for the near-plane
            // guard.
            var axisDepth = 0.5 * (
                Depth(World(anchors.Bow), camera, forward) + Depth(World(anchors.Engine), camera, forward));

            Point? openTip = null;
            var placed = new List<Point>(_pins.Length);

            foreach (var slot in _pins)
            {
                var hit = Place(slot.Button, slot.Anchor(anchors), camera, inner, toOverlay, placed);
                if (hit is not { } spot)
                    continue;

                slot.Button.Opacity = spot.Depth > axisDepth ? 0.4 : 1.0;
                if (slot == _openPin) openTip = spot.Position;
            }

            // The leader is redrawn from the marker's live position against the panel's frozen one,
            // which is the whole arrangement: the anchor sweeps as the view turns, the panel does
            // not, and the line is what keeps them legibly attached.
            if (IsPanelOpen && openTip is { } tip)
                DrawLeader(tip);
            else
                LeaderLine.Visibility = Visibility.Collapsed;
        }

        private static Point3D World(ShipAnchor a) => new(
            a.X * VoxelShipGrower.VoxelSize, a.Y * VoxelShipGrower.VoxelSize, a.Z * VoxelShipGrower.VoxelSize);

        private static double Depth(Point3D world, PerspectiveCamera camera, Vector3D unitForward) =>
            Vector3D.DotProduct(world - camera.Position, unitForward);

        // ---- Panel -------------------------------------------------------------------------

        /// <summary>One marker and the panel behind it. A fixed array rather than anything bound:
        /// the markers must never be re-templated, since re-creating them would drop the open one's
        /// checked state, and eight declared elements cannot be rebuilt by accident.</summary>
        private sealed record PinSlot(
            ToggleButton Button, string Title, string TemplateKey, Func<ShipAnchors, ShipAnchor> Anchor);

        private PinSlot[] _pins = Array.Empty<PinSlot>();
        private PinSlot? _openPin;

        /// <summary>Guards the moment one marker is opened while another is open: unchecking the old
        /// one raises Unchecked, which would close the panel that is in the middle of opening.</summary>
        private bool _switchingPins;

        private void BuildPins() => _pins = new[]
        {
            new PinSlot(ShapePin, "FORME", "Panel.Shape", a => a.Bow),
            new PinSlot(DimensionsPin, "DIMENSIONS", "Panel.Dimensions", a => a.HullMid),
            new PinSlot(WingPin, "AILES", "Panel.Wings", a => a.Wing),
            new PinSlot(PropulsionPin, "PROPULSION", "Panel.Propulsion", a => a.Engine),
            new PinSlot(CockpitPin, "COCKPIT", "Panel.Cockpit", a => a.Cockpit),
            new PinSlot(TowerPin, "TOURELLE", "Panel.Tower", a => a.Tower),
            new PinSlot(NacellePin, "NACELLES", "Panel.Nacelles", a => a.Nacelle),
            new PinSlot(SurfacePin, "SURFACE", "Panel.Surface", a => a.Surface),
        };

        private void OnPinToggled(object sender, RoutedEventArgs e)
        {
            if (_switchingPins || sender is not ToggleButton button) return;

            var slot = Array.Find(_pins, p => p.Button == button);
            if (slot is null) return;

            if (button.IsChecked == true) OpenPanel(slot);
            else if (_openPin == slot) ClosePanel();
        }

        private void OnClosePanel(object sender, RoutedEventArgs e) => ClosePanel();

        private void OpenPanel(PinSlot slot)
        {
            // Only ever one panel. Unchecking the outgoing marker raises Unchecked, so the guard
            // stops that from closing the panel this call is opening.
            _switchingPins = true;
            foreach (var other in _pins)
                if (other != slot) other.Button.IsChecked = false;
            _switchingPins = false;

            _openPin = slot;
            AnchorPanelTitle.Text = slot.Title;
            AnchorPanelBody.ContentTemplate = (DataTemplate)FindResource(slot.TemplateKey);

            AnchorPanel.MaxHeight = Math.Max(140, AnchorOverlay.ActualHeight - 32);
            AnchorPanel.Visibility = Visibility.Visible;

            // Measured explicitly before placing. DesiredSize is whatever the previous layout pass
            // left behind, so on the first open it is zero and on later ones it belongs to the
            // panel as it was last time -- either way the placement below would use the wrong
            // height and the panel would sit off-centre until something else forced a re-measure.
            AnchorPanel.Measure(new Size(AnchorPanel.Width, AnchorPanel.MaxHeight));
            var size = AnchorPanel.DesiredSize;

            var anchor = new Point(
                Canvas.GetLeft(slot.Button) + slot.Button.Width / 2,
                Canvas.GetTop(slot.Button) + slot.Button.Height / 2);

            if (!double.IsFinite(anchor.X) || !double.IsFinite(anchor.Y))
                anchor = new Point(AnchorOverlay.ActualWidth / 2, AnchorOverlay.ActualHeight / 2);

            // Outward, toward the nearer edge of the view. Sending it the other way -- "away from
            // the anchor, into the free half" -- is the tempting rule and is wrong: the ship sits in
            // the middle of the view, so the free half is exactly where the ship is, and the panel
            // landed square on top of the thing it describes.
            const double gap = 46;
            var left = anchor.X > AnchorOverlay.ActualWidth / 2
                ? anchor.X + gap
                : anchor.X - gap - size.Width;

            _panelOrigin = new Point(left, anchor.Y - size.Height / 2);
            ConstrainPanel();

            // The panel has been measured but not arranged, so ActualWidth/Height are still zero and
            // the leader would have nothing to attach to. Re-constraining once the layout pass has
            // run settles both the position and the line.
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                ConstrainPanel();
                _overlayDirty = true;
            }));

            // Helix's CameraController takes focus on click and then steers the camera with the
            // arrow keys, which would fight every slider in the panel.
            AnchorPanel.Focus();

            // Greyed rather than switched off, so the suspension is visible and the user's own
            // preference survives it. Its own Checked/Unchecked handlers are never raised by this.
            AutoRotateToggle.IsEnabled = false;
            AutoRotateToggle.ToolTip = "Suspendue tant qu'un panneau de réglages est ouvert";

            _overlayDirty = true;
        }

        private void ClosePanel()
        {
            _panelOrigin = null;
            AnchorPanel.Visibility = Visibility.Collapsed;
            LeaderLine.Visibility = Visibility.Collapsed;
            _switchingPins = true;
            foreach (var slot in _pins) slot.Button.IsChecked = false;
            _switchingPins = false;
            _openPin = null;

            AutoRotateToggle.IsEnabled = true;
            AutoRotateToggle.ToolTip = null;
            Viewport.Focus();
            _overlayDirty = true;
        }

        /// <summary>Nudges the panel back inside the overlay without moving it otherwise. Monotone
        /// on purpose: it only ever acts when part of the panel is outside.</summary>
        private void ConstrainPanel()
        {
            if (_panelOrigin is not { } origin) return;

            const double margin = 12;
            var w = AnchorPanel.ActualWidth > 0 ? AnchorPanel.ActualWidth : AnchorPanel.Width;
            var h = AnchorPanel.ActualHeight > 0 ? AnchorPanel.ActualHeight : AnchorPanel.DesiredSize.Height;

            var maxLeft = Math.Max(margin, AnchorOverlay.ActualWidth - w - margin);
            var maxTop = Math.Max(margin, AnchorOverlay.ActualHeight - h - margin);

            var placed = new Point(
                Math.Clamp(origin.X, margin, maxLeft),
                Math.Clamp(origin.Y, margin, maxTop));

            _panelOrigin = placed;
            Canvas.SetLeft(AnchorPanel, Math.Round(placed.X));
            Canvas.SetTop(AnchorPanel, Math.Round(placed.Y));
        }

        /// <summary>
        /// An elbow from the marker to the panel's near edge. An elbow rather than a straight line
        /// because with the panel frozen and the anchor sweeping, a straight line eventually runs
        /// along the panel's border or cuts across its corner.
        /// </summary>
        private void DrawLeader(Point tip)
        {
            var left = Canvas.GetLeft(AnchorPanel);
            var top = Canvas.GetTop(AnchorPanel);
            var w = AnchorPanel.ActualWidth;
            var h = AnchorPanel.ActualHeight;
            if (!double.IsFinite(left) || !double.IsFinite(top) || w <= 0 || h <= 0)
            {
                LeaderLine.Visibility = Visibility.Collapsed;
                return;
            }

            // Attach to whichever vertical edge faces the marker, and keep the attachment inside
            // that edge's own span -- an unclamped Y puts the join above or below the panel, and
            // the line then crosses the panel to reach it.
            var attachRight = tip.X > left + w / 2;
            var attachX = attachRight ? left + w : left;
            var attachY = Math.Clamp(tip.Y, top + 14, top + h - 14);

            var midX = (tip.X + attachX) / 2;

            _leaderPoints[0] = new Point(Math.Round(tip.X), Math.Round(tip.Y));
            _leaderPoints[1] = new Point(Math.Round(midX), Math.Round(tip.Y));
            _leaderPoints[2] = new Point(Math.Round(midX), Math.Round(attachY));
            _leaderPoints[3] = new Point(Math.Round(attachX), Math.Round(attachY));

            LeaderLine.Visibility = Visibility.Visible;
        }

        private readonly record struct PinSpot(Point Position, double Depth);

        /// <summary>
        /// Positions one marker and reports where it landed, or null if it is not on screen this
        /// frame. <paramref name="placed"/> carries the markers already positioned, and grows.
        /// </summary>
        private static PinSpot? Place(
            FrameworkElement pin, ShipAnchor anchor, PerspectiveCamera camera,
            Viewport3D inner, GeneralTransform toOverlay, List<Point> placed)
        {
            var world = World(anchor);

            // Behind the camera has to be rejected before projecting, not after: for a point behind
            // the eye the projection returns a plausible-looking mirrored position rather than
            // anything obviously wrong, which is far harder to notice. LookDirection is normalised
            // first because nothing guarantees it is a unit vector -- StepAutoRotate below sets it
            // from a position, and the XAML default camera is not unit either.
            var forward = camera.LookDirection;
            forward.Normalize();
            var depth = Depth(world, camera, forward);
            if (depth <= camera.NearPlaneDistance)
            {
                pin.Visibility = Visibility.Collapsed;
                return null;
            }

            var projected = Viewport3DHelper.Point3DtoPoint2D(inner, world);
            var point = toOverlay.Transform(projected);

            // Canvas.SetLeft(NaN) does not throw. It arranges at zero and parks the marker in the
            // corner, which reads as a placement bug rather than a bad projection.
            if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
            {
                pin.Visibility = Visibility.Collapsed;
                return null;
            }

            // Head-on or from directly above, several anchors project to nearly the same spot and
            // the markers stack into an unreadable pile. Nudging downward off whichever markers are
            // already placed spreads them. Deliberately in a fixed order and by a fixed step: a
            // solver free to re-decide every frame would leave the whole cluster trembling as the
            // view turns, which is worse than the overlap.
            const double clearance = 30;
            for (var attempt = 0; attempt < placed.Count; attempt++)
            {
                var clash = placed.FindIndex(q =>
                    Math.Abs(q.X - point.X) < clearance && Math.Abs(q.Y - point.Y) < clearance);
                if (clash < 0) break;
                point.Y = placed[clash].Y + clearance;
            }

            placed.Add(point);
            pin.Visibility = Visibility.Visible;

            // The marker has a fixed size, which is what lets it be centred on the anchor without a
            // measure pass -- and, more to the point, what stops its hover label from shifting the
            // dot off the part it is pointing at.
            //
            // Rounded, or a hairline stroke at fractional coordinates shimmers as the view turns.
            Canvas.SetLeft(pin, Math.Round(point.X - pin.Width / 2));
            Canvas.SetTop(pin, Math.Round(point.Y - pin.Height / 2));

            return new PinSpot(new Point(Math.Round(point.X), Math.Round(point.Y)), depth);
        }

        private void OnOpenStudio(object sender, RoutedEventArgs e)
        {
            if (DataContext is not ViewModels.MainViewModel viewModel)
                return;

            // Opened non-modally and owned, so the parameter panel stays usable behind it. The
            // studio takes a snapshot of the parameters as they are now: it rebuilds its own
            // geometry, which is far heavier than the main viewport's, so following every slider
            // move live would make the sliders crawl.
            new StudioWindow(viewModel.Parameters, viewModel.Designation) { Owner = this }.Show();
        }

        private void StepAutoRotate()
        {
            // Suspended, not switched off. Writing AutoRotateToggle.IsChecked = false here would
            // raise Unchecked and destroy the user's own preference, so they would have to turn the
            // orbit back on every time they closed a panel.
            if (IsPanelOpen)
                return;

            if (Viewport.Camera is not PerspectiveCamera camera)
                return;

            var position = camera.Position;
            var radius = Math.Sqrt(position.X * position.X + position.Z * position.Z);
            if (radius < 0.01)
                radius = 5;

            _autoRotateAngle += 0.006;
            var x = radius * Math.Cos(_autoRotateAngle);
            var z = radius * Math.Sin(_autoRotateAngle);
            camera.Position = new Point3D(x, position.Y, z);
            camera.LookDirection = new Vector3D(-x, -position.Y, -z);
        }
    }
}
