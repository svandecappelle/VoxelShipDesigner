using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ShipDesign.App.Rendering;

/// <summary>
/// Composites a lit render with a blurred copy of its emissive-only pass, adding them rather than
/// blending them, and rolls the result through a filmic curve.
///
/// The live view can only fake this: WPF has no additive blend mode, so on screen a blurred copy is
/// laid over with alpha, which *replaces* what is underneath and reads more like fog than light.
/// Once the render is a bitmap the arithmetic is ours to do, so the exported image gets the real
/// thing -- light accumulating on top of the surface it spills across, then tone mapped so a bright
/// core rolls off instead of clipping flat white.
/// </summary>
public static class BloomCompositor
{
    /// <summary>
    /// Composites two renders of the *same* area: the lit scene, and the emissive-only pass. They
    /// are taken separately by the caller because producing the emissive pass means hiding most of
    /// the tree, which only the window that owns it can do safely.
    /// </summary>
    public static BitmapSource Composite(
        byte[] scenePixels, byte[] glowPixels, int width, int height, double scale,
        float bloomStrength = 1.35f, int blurRadius = 18)
    {
        // Two passes of a separable box blur approximate a gaussian closely enough at this radius,
        // and cost a fraction of a true gaussian over a multi-megapixel image.
        Blur(glowPixels, width, height, blurRadius);
        Blur(glowPixels, width, height, Math.Max(2, blurRadius / 3));

        Add(scenePixels, glowPixels, bloomStrength);

        var result = BitmapSource.Create(width, height, 96 * scale, 96 * scale,
            PixelFormats.Bgra32, null, scenePixels, width * 4);
        result.Freeze();
        return result;
    }

    public static byte[] Render(Visual visual, Rect bounds, int width, int height, double scale)
    {
        var target = new RenderTargetBitmap(width, height, 96 * scale, 96 * scale, PixelFormats.Pbgra32);

        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
            context.DrawRectangle(new VisualBrush(visual), null, new Rect(bounds.Location, bounds.Size));
        target.Render(drawing);

        var pixels = new byte[width * height * 4];
        target.CopyPixels(pixels, width * 4, 0);
        return pixels;
    }

    /// <summary>Separable box blur, horizontal then vertical, on the BGRA buffer in place.</summary>
    private static void Blur(byte[] pixels, int width, int height, int radius)
    {
        if (radius < 1) return;

        var temp = new byte[pixels.Length];
        BlurAxis(pixels, temp, width, height, radius, horizontal: true);
        BlurAxis(temp, pixels, width, height, radius, horizontal: false);
    }

    private static void BlurAxis(byte[] source, byte[] destination, int width, int height, int radius, bool horizontal)
    {
        var outer = horizontal ? height : width;
        var inner = horizontal ? width : height;
        var window = radius * 2 + 1;

        for (var o = 0; o < outer; o++)
        {
            int sumB = 0, sumG = 0, sumR = 0, sumA = 0;

            // Prime the running sum with the first window, clamping at the edge so the blur does
            // not darken toward the borders.
            for (var k = -radius; k <= radius; k++)
            {
                var i = Math.Clamp(k, 0, inner - 1);
                var p = Index(o, i, width, horizontal);
                sumB += source[p]; sumG += source[p + 1]; sumR += source[p + 2]; sumA += source[p + 3];
            }

            for (var i = 0; i < inner; i++)
            {
                var p = Index(o, i, width, horizontal);
                destination[p] = (byte)(sumB / window);
                destination[p + 1] = (byte)(sumG / window);
                destination[p + 2] = (byte)(sumR / window);
                destination[p + 3] = (byte)(sumA / window);

                var leaving = Index(o, Math.Clamp(i - radius, 0, inner - 1), width, horizontal);
                var entering = Index(o, Math.Clamp(i + radius + 1, 0, inner - 1), width, horizontal);

                sumB += source[entering] - source[leaving];
                sumG += source[entering + 1] - source[leaving + 1];
                sumR += source[entering + 2] - source[leaving + 2];
                sumA += source[entering + 3] - source[leaving + 3];
            }
        }
    }

    private static int Index(int outer, int inner, int width, bool horizontal) =>
        horizontal ? (outer * width + inner) * 4 : (inner * width + outer) * 4;

    /// <summary>
    /// Adds the blurred glow onto the scene and tone maps. The glow's own alpha weights the
    /// contribution, so the transparent backdrop of the emissive pass contributes nothing rather
    /// than adding a grey wash over the whole frame.
    /// </summary>
    private static void Add(byte[] scene, byte[] glow, float strength)
    {
        for (var i = 0; i < scene.Length; i += 4)
        {
            var weight = glow[i + 3] / 255f * strength;
            if (weight > 0f)
            {
                scene[i] = ToneMap(scene[i] / 255f + glow[i] / 255f * weight);
                scene[i + 1] = ToneMap(scene[i + 1] / 255f + glow[i + 1] / 255f * weight);
                scene[i + 2] = ToneMap(scene[i + 2] / 255f + glow[i + 2] / 255f * weight);
            }
            scene[i + 3] = 255;
        }
    }

    /// <summary>
    /// Reinhard-style roll-off. Without it every pixel the bloom pushes past 1 clips to flat white
    /// and the halo turns into a paper cut-out; with it the core stays bright while keeping its
    /// colour, which is what makes an engine read as hot rather than as a white blob.
    /// </summary>
    private static byte ToneMap(float value)
    {
        var mapped = value / (1f + value) * 1.28f;
        return (byte)Math.Clamp(mapped * 255f, 0f, 255f);
    }
}
