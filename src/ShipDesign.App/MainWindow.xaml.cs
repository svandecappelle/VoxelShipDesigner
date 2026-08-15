using System;
using System.Windows;
using System.Windows.Media.Media3D;
using System.Windows.Threading;

namespace ShipDesign.App
{
    public partial class MainWindow : Window
    {
        private readonly DispatcherTimer _autoRotateTimer = new() { Interval = TimeSpan.FromMilliseconds(30) };
        private double _autoRotateAngle;

        public MainWindow()
        {
            InitializeComponent();

            var viewModel = new ViewModels.MainViewModel();
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
