using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;
using Microsoft.Win32;
using ShipDesign.App.Rendering;
using ShipDesign.Core.Procedural;

namespace ShipDesign.App
{
    /// <summary>
    /// A turnaround sheet: the hero three-quarter view over front, side, top and back elevations,
    /// with the palette and voxel dimensions. The four small views are orthographic on purpose --
    /// an elevation with perspective in it is not an elevation, and the whole value of the sheet is
    /// being able to read proportions straight off it.
    /// </summary>
    public partial class SheetWindow : Window
    {
        private readonly string _designation;

        public SheetWindow(ShipParameters parameters, string designation)
        {
            InitializeComponent();

            _designation = designation;
            DesignationLabel.Text = designation;

            Compose(parameters);
        }

        private sealed record Swatch(string Name, Brush Brush);

        private void Compose(ShipParameters parameters)
        {
            var grid = ProceduralShipBuilder.BuildVoxels(parameters);
            var result = StudioMeshBuilder.Build(grid, VoxelShipGrower.VoxelSize, parameters);

            // Frozen so one set of meshes can be shared by five viewports. Without this each view
            // would need its own copy of a 30k-triangle model, which is both slow to build and
            // pointless -- they are all looking at the same ship.
            result.Solid.Freeze();
            result.Emissive.Freeze();

            var bounds = result.Bounds;
            var centre = new Point3D(
                bounds.X + bounds.SizeX / 2,
                bounds.Y + bounds.SizeY / 2,
                bounds.Z + bounds.SizeZ / 2);

            SetupHero(result, centre, bounds);
            SetupElevation(FrontViewport, result, centre, bounds, new Vector3D(0, 0, 1), new Vector3D(0, 1, 0));
            SetupElevation(SideViewport, result, centre, bounds, new Vector3D(-1, 0, 0), new Vector3D(0, 1, 0));
            SetupElevation(TopViewport, result, centre, bounds, new Vector3D(0, -1, 0), new Vector3D(0, 0, 1));
            SetupElevation(BackViewport, result, centre, bounds, new Vector3D(0, 0, -1), new Vector3D(0, 1, 0));

            PaletteStrip.ItemsSource = result.Palette.Swatches
                .Select(s => new Swatch(s.Name, new SolidColorBrush(s.Colour)))
                .ToList();

            var keys = grid.Voxels.Keys;
            var w = keys.Max(k => k.X) - keys.Min(k => k.X) + 1;
            var h = keys.Max(k => k.Y) - keys.Min(k => k.Y) + 1;
            var d = keys.Max(k => k.Z) - keys.Min(k => k.Z) + 1;
            DimensionsLabel.Text = $"{w} × {h} × {d}";
            StatusLabel.Text = $"{grid.Voxels.Count:N0} voxels";
        }

        private void SetupHero(StudioMeshBuilder.Result result, Point3D centre, Rect3D bounds)
        {
            var extent = Math.Max(bounds.SizeX, Math.Max(bounds.SizeY, bounds.SizeZ));
            var radius = extent * 1.75;

            var lit = new Model3DGroup();
            lit.Children.Add(result.Solid);
            AddLights(lit);

            HeroViewport.Children.Clear();
            HeroViewport.Children.Add(new ModelVisual3D { Content = lit });

            HeroGlowViewport.Children.Clear();
            if (result.Emissive.Children.Count > 0)
                HeroGlowViewport.Children.Add(new ModelVisual3D { Content = result.Emissive });

            var position = new Point3D(
                centre.X + radius * 0.72,
                centre.Y + extent * 0.42,
                centre.Z - radius * 0.72);

            foreach (var viewport in new[] { HeroViewport, HeroGlowViewport })
                viewport.Camera = new PerspectiveCamera
                {
                    Position = position,
                    LookDirection = centre - position,
                    UpDirection = new Vector3D(0, 1, 0),
                    FieldOfView = 40,
                };
        }

        /// <summary>
        /// One orthographic elevation. <paramref name="direction"/> is the way the camera looks;
        /// the width is taken from the two axes actually across the view, so a long ship seen from
        /// the front is not framed as if it were as wide as it is long.
        /// </summary>
        private static void SetupElevation(
            HelixViewport3D viewport, StudioMeshBuilder.Result result,
            Point3D centre, Rect3D bounds, Vector3D direction, Vector3D up)
        {
            var lit = new Model3DGroup();
            lit.Children.Add(result.Solid);
            AddLights(lit);

            viewport.Children.Clear();
            viewport.Children.Add(new ModelVisual3D { Content = lit });

            var right = Vector3D.CrossProduct(direction, up);
            double Span(Vector3D axis) =>
                Math.Abs(axis.X) * bounds.SizeX + Math.Abs(axis.Y) * bounds.SizeY + Math.Abs(axis.Z) * bounds.SizeZ;

            var across = Span(right);
            var vertical = Span(up);

            // The viewports are wider than they are tall, so a view whose vertical extent dominates
            // still has to be framed by height or it would overflow top and bottom.
            var width = Math.Max(across, vertical * 1.6) * 1.15;

            var distance = Math.Max(bounds.SizeX, Math.Max(bounds.SizeY, bounds.SizeZ)) * 2;
            viewport.Camera = new OrthographicCamera
            {
                Position = centre - direction * distance,
                LookDirection = direction,
                UpDirection = up,
                Width = width,
                NearPlaneDistance = 0.01,
                FarPlaneDistance = distance * 4,
            };
        }

        /// <summary>The same three-point rig the studio uses, so an elevation and the hero shot
        /// read as the same object under the same light rather than as two different renders.</summary>
        private static void AddLights(Model3DGroup group)
        {
            group.Children.Add(new AmbientLight(Color.FromRgb(28, 34, 44)));
            group.Children.Add(new DirectionalLight(Color.FromRgb(255, 244, 224), new Vector3D(-0.55, -0.72, -0.42)));
            group.Children.Add(new DirectionalLight(Color.FromRgb(74, 104, 140), new Vector3D(0.68, -0.24, 0.55)));
            group.Children.Add(new DirectionalLight(Color.FromRgb(96, 140, 170), new Vector3D(0.12, 0.55, 0.82)));
        }

        private void OnSavePng(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Title = "Enregistrer la planche",
                FileName = $"{_designation}-planche.png",
                Filter = "Image PNG|*.png",
                DefaultExt = ".png",
            };

            if (dialog.ShowDialog(this) != true)
                return;

            try
            {
                var path = dialog.FileName;
                var size = SaveSheet(path);
                StatusLabel.Text = $"Planche enregistrée — {size.Width} × {size.Height} px";
            }
            catch (Exception ex)
            {
                StatusLabel.Text = $"Échec de l'enregistrement : {ex.Message}";
            }
        }

        /// <summary>
        /// Renders the sheet at twice its on-screen size and writes it out. The supersampling is
        /// the point: voxel geometry is nothing but high-contrast straight edges, which is the
        /// worst case for aliasing, and rendering at 2x then letting the image be viewed smaller
        /// is the only anti-aliasing available without changing renderer.
        /// </summary>
        private Size SaveSheet(string path)
        {
            const double scale = 2.0;

            // Sized from the union of the descendants' bounds, not from ActualHeight. The two are
            // not the same: a child that overflows its arranged slot is still inside the descendant
            // bounds but outside ActualHeight, and rendering at ActualHeight silently cropped the
            // palette panel's last row off the bottom of the file while the window looked fine.
            var bounds = VisualTreeHelper.GetDescendantBounds(SheetRoot);
            if (bounds.IsEmpty)
                bounds = new Rect(0, 0, SheetRoot.ActualWidth, SheetRoot.ActualHeight);

            var width = (int)Math.Ceiling(bounds.Width * scale);
            var height = (int)Math.Ceiling(bounds.Height * scale);

            // Two passes over the same area, staged here rather than inside the compositor because
            // producing an emissive-only frame means hiding most of the tree, which only this
            // window can do safely.
            var scene = RenderPass(bounds, width, height, scale, emissiveOnly: false);
            var glow = RenderPass(bounds, width, height, scale, emissiveOnly: true);

            var composed = BloomCompositor.Composite(scene, glow, width, height, scale);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(composed));

            using var stream = File.Create(path);
            encoder.Save(stream);

            return new Size(width, height);
        }

        /// <summary>
        /// Renders the sheet either normally or as the emissive lamps alone on black. The glow
        /// overlay is taken out of the lit pass so the halo is not counted twice -- once faked by
        /// the on-screen blur and once added properly by the compositor.
        /// </summary>
        private byte[] RenderPass(Rect bounds, int width, int height, double scale, bool emissiveOnly)
        {
            var backdrop = HeroBackdrop.Background;
            var rootBackground = SheetRoot.Background;

            HeroGlowViewport.Visibility = Visibility.Collapsed;

            if (emissiveOnly)
            {
                // Black rather than transparent: the compositor weights the glow by its alpha, and
                // an unlit backdrop that is black adds nothing wherever there is no lamp.
                HeroBackdrop.Background = Brushes.Black;
                SheetRoot.Background = Brushes.Black;
                HeroViewport.Visibility = Visibility.Collapsed;
                Elevations.Visibility = Visibility.Collapsed;
                HeroGlowViewport.Effect = null;
                HeroGlowViewport.Visibility = Visibility.Visible;
            }

            UpdateLayout();
            var pixels = BloomCompositor.Render(SheetRoot, bounds, width, height, scale);

            HeroBackdrop.Background = backdrop;
            SheetRoot.Background = rootBackground;
            HeroViewport.Visibility = Visibility.Visible;
            Elevations.Visibility = Visibility.Visible;
            HeroGlowViewport.Effect = new BlurEffect { Radius = 24, KernelType = KernelType.Gaussian };
            HeroGlowViewport.Visibility = Visibility.Visible;
            UpdateLayout();

            return pixels;
        }
    }
}
