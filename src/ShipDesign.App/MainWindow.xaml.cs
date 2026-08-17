using System;
using System.Windows;
using System.Windows.Controls;
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

        public MainWindow()
        {
            InitializeComponent();

            var viewModel = new ViewModels.MainViewModel();
            _viewModel = viewModel;
            DataContext = viewModel;
            viewModel.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ViewModels.MainViewModel.ShipModel))
                    Dispatcher.BeginInvoke(new Action(() => Viewport.ZoomExtents()));
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

            if (state == _lastCamera) return;
            _lastCamera = state;

            ProjectAnchors(camera);
        }

        private void ProjectAnchors(PerspectiveCamera camera)
        {
            var anchors = _viewModel?.Anchors;
            if (anchors is null)
            {
                TestPin.Visibility = Visibility.Collapsed;
                return;
            }

            // Point3DtoPoint2D answers in the *inner* Viewport3D's coordinate space, which lives
            // inside HelixViewport3D's template -- not in the overlay's. The two are siblings, so
            // TransformToVisual (not TransformToAncestor) is what maps between them. Computed once
            // per frame rather than per marker.
            var inner = Viewport.Viewport;
            var toOverlay = inner.TransformToVisual(AnchorOverlay);

            Place(TestPin, anchors.Wing, camera, inner, toOverlay);
        }

        private static void Place(
            FrameworkElement pin, ShipAnchor anchor, PerspectiveCamera camera,
            Viewport3D inner, GeneralTransform toOverlay)
        {
            var world = new Point3D(
                anchor.X * VoxelShipGrower.VoxelSize,
                anchor.Y * VoxelShipGrower.VoxelSize,
                anchor.Z * VoxelShipGrower.VoxelSize);

            // Behind the camera has to be rejected before projecting, not after: for a point behind
            // the eye the projection returns a plausible-looking mirrored position rather than
            // anything obviously wrong, which is far harder to notice. LookDirection is normalised
            // first because nothing guarantees it is a unit vector -- StepAutoRotate below sets it
            // from a position, and the XAML default camera is not unit either.
            var forward = camera.LookDirection;
            forward.Normalize();
            var depth = Vector3D.DotProduct(world - camera.Position, forward);
            if (depth <= camera.NearPlaneDistance)
            {
                pin.Visibility = Visibility.Collapsed;
                return;
            }

            var projected = Viewport3DHelper.Point3DtoPoint2D(inner, world);
            var point = toOverlay.Transform(projected);

            // Canvas.SetLeft(NaN) does not throw; it arranges at zero and parks the marker in the
            // corner, which reads as a placement bug rather than as a bad projection.
            if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
            {
                pin.Visibility = Visibility.Collapsed;
                return;
            }

            pin.Visibility = Visibility.Visible;

            // Measured explicitly: DesiredSize is stale on the first frame after the marker becomes
            // visible, so centring on it would place the first one using the previous size.
            pin.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

            // Rounded, or a hairline border at fractional coordinates shimmers as the view turns.
            Canvas.SetLeft(pin, Math.Round(point.X - pin.DesiredSize.Width / 2));
            Canvas.SetTop(pin, Math.Round(point.Y - pin.DesiredSize.Height / 2));
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
