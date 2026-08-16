using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using Microsoft.Win32;
using ShipDesign.App.Rendering;
using ShipDesign.Core.Procedural;

namespace ShipDesign.App
{
    /// <summary>
    /// A separate, higher-fidelity view of the current ship. It deliberately shares nothing with
    /// the main viewport's pipeline: it builds its own geometry from the voxel grid so it can bake
    /// ambient occlusion, and it lights and composites the scene itself.
    /// </summary>
    public partial class StudioWindow : Window
    {
        private readonly DispatcherTimer _spinTimer = new() { Interval = TimeSpan.FromMilliseconds(30) };
        private double _angle;
        private double _radius = 30;
        private double _height = 12;
        private Point3D _target;

        private readonly ShipParameters _parameters;
        private readonly string _designation;

        public StudioWindow(ShipParameters parameters, string designation)
        {
            InitializeComponent();

            _parameters = parameters;
            _designation = designation;

            DataContext = new { Designation = designation };
            Render(parameters);

            // Wired here rather than in XAML: setting IsChecked in markup raises Checked while the
            // XAML is still being parsed, before the viewport fields further down the file have
            // been assigned, and the handler would run against nulls.
            GlowToggle.Checked += OnGlowToggled;
            GlowToggle.Unchecked += OnGlowToggled;
            SpinToggle.Checked += OnSpinToggled;
            SpinToggle.Unchecked += OnSpinToggled;

            // The glow passes have their own cameras, and nothing keeps them aligned by themselves.
            // Mirroring on every composition frame rather than only when the spin timer fires is
            // what makes the halo hold its place when the view is dragged by hand -- otherwise the
            // lit scene turns and the halo stays where it was.
            CompositionTarget.Rendering += OnFrame;
            Closed += (_, _) => CompositionTarget.Rendering -= OnFrame;

            _spinTimer.Tick += (_, _) => Step();
            _spinTimer.Start();
        }

        private void OnFrame(object? sender, EventArgs e) => MirrorCamera();

        private void Render(ShipParameters parameters)
        {
            var grid = ProceduralShipBuilder.BuildVoxels(parameters);
            var result = StudioMeshBuilder.Build(grid, VoxelShipGrower.VoxelSize, parameters);

            var centre = new Point3D(
                result.Bounds.X + result.Bounds.SizeX / 2,
                result.Bounds.Y + result.Bounds.SizeY / 2,
                result.Bounds.Z + result.Bounds.SizeZ / 2);

            var extent = Math.Max(result.Bounds.SizeX, Math.Max(result.Bounds.SizeY, result.Bounds.SizeZ));
            _target = centre;
            _radius = extent * 1.9;
            _height = extent * 0.42;

            var lit = new Model3DGroup();
            lit.Children.Add(result.Solid);
            AddStudioLights(lit, centre, extent);
            MainViewport.Children.Clear();
            MainViewport.Children.Add(new ModelVisual3D { Content = lit });

            // The glow passes get the emissive geometry only, with no lights at all: an emissive
            // material is self-lit, so what lands in these viewports is the lamps and nothing else,
            // which is exactly the bright-pass a bloom needs.
            SetGlowContent(GlowViewport, result.Emissive);
            SetGlowContent(GlowCoreViewport, result.Emissive);

            var triangles = CountTriangles(result.Solid) + CountTriangles(result.Emissive);
            StatusLine.Text = $"{grid.Voxels.Count:N0} voxels — {triangles:N0} triangles — occlusion ambiante cuite, halo composité";

            SyncCamera();
        }

        private static void SetGlowContent(HelixToolkit.Wpf.HelixViewport3D viewport, Model3DGroup emissive)
        {
            viewport.Children.Clear();
            if (emissive.Children.Count > 0)
                viewport.Children.Add(new ModelVisual3D { Content = emissive });
        }

        /// <summary>
        /// A three-point rig: a warm key from the upper front-left, a cool fill opposite it to keep
        /// the shadow side readable, and a rim from behind to separate the hull from the backdrop.
        /// The ambient term is deliberately low -- ambient light is what washes baked occlusion out.
        /// </summary>
        private static void AddStudioLights(Model3DGroup group, Point3D centre, double extent)
        {
            group.Children.Add(new AmbientLight(Color.FromRgb(26, 32, 42)));

            group.Children.Add(new DirectionalLight(
                Color.FromRgb(255, 244, 224), new Vector3D(-0.55, -0.72, -0.42)));

            group.Children.Add(new DirectionalLight(
                Color.FromRgb(74, 104, 140), new Vector3D(0.68, -0.24, 0.55)));

            group.Children.Add(new DirectionalLight(
                Color.FromRgb(96, 140, 170), new Vector3D(0.12, 0.55, 0.82)));

            _ = centre;
            _ = extent;
        }

        private static int CountTriangles(Model3DGroup group)
        {
            var count = 0;
            foreach (var child in group.Children)
            {
                switch (child)
                {
                    case GeometryModel3D { Geometry: MeshGeometry3D mesh }:
                        count += mesh.TriangleIndices.Count / 3;
                        break;
                    case Model3DGroup nested:
                        count += CountTriangles(nested);
                        break;
                }
            }
            return count;
        }

        private void Step()
        {
            _angle += 0.005;
            SyncCamera();
        }

        /// <summary>Places the main camera on its orbit. Only the main one: the glow passes are
        /// mirrored from it every frame, so they follow whether the movement came from the spin or
        /// from the user dragging the view.</summary>
        private void SyncCamera()
        {
            if (MainViewport.Camera is not PerspectiveCamera camera)
                return;

            // Orbit around the model's own centre. The voxel grid starts at z=0 and runs forward,
            // so the origin is at the bow rather than the middle -- orbiting the origin would swing
            // the ship in and out of frame instead of turning it on the spot.
            var position = new Point3D(
                _target.X + _radius * Math.Cos(_angle),
                _target.Y + _height,
                _target.Z + _radius * Math.Sin(_angle));

            camera.Position = position;
            camera.LookDirection = _target - position;
            camera.UpDirection = new Vector3D(0, 1, 0);
            camera.FieldOfView = 42;

            MirrorCamera();
        }

        /// <summary>Copies the main camera onto the glow passes. A halo rendered from even a
        /// slightly different angle smears away from the lamp it belongs to.</summary>
        private void MirrorCamera()
        {
            if (MainViewport.Camera is not PerspectiveCamera source)
                return;

            foreach (var viewport in new[] { GlowViewport, GlowCoreViewport })
            {
                if (viewport.Camera is not PerspectiveCamera camera)
                    continue;
                camera.Position = source.Position;
                camera.LookDirection = source.LookDirection;
                camera.UpDirection = source.UpDirection;
                camera.FieldOfView = source.FieldOfView;
                camera.NearPlaneDistance = source.NearPlaneDistance;
                camera.FarPlaneDistance = source.FarPlaneDistance;
            }
        }

        private void OnGlowToggled(object sender, RoutedEventArgs e)
        {
            var visible = GlowToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            GlowViewport.Visibility = visible;
            GlowCoreViewport.Visibility = visible;
        }

        private void OnExportBundle(object sender, RoutedEventArgs e)
        {
            // A folder rather than a file: the point of the bundle is that the mesh arrives with
            // the shader that makes its baked occlusion visible, so they have to stay together.
            var dialog = new SaveFileDialog
            {
                Title = "Dossier du bundle Unity",
                FileName = _designation,
                Filter = "Dossier de bundle|*.",
                OverwritePrompt = false,
            };

            if (dialog.ShowDialog(this) != true)
                return;

            var folder = Path.Combine(
                Path.GetDirectoryName(dialog.FileName) ?? ".",
                Path.GetFileNameWithoutExtension(dialog.FileName));

            try
            {
                var result = UnityBundleExporter.Export(_parameters, folder, _designation);
                StatusLine.Text = $"Bundle écrit dans {result.Folder} — {string.Join(", ", result.Files)} ({result.Triangles:N0} triangles)";
            }
            catch (Exception ex)
            {
                StatusLine.Text = $"Échec de l'export : {ex.Message}";
            }
        }

        private void OnSpinToggled(object sender, RoutedEventArgs e)
        {
            if (SpinToggle.IsChecked == true) _spinTimer.Start();
            else _spinTimer.Stop();
        }
    }
}
