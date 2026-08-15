namespace ShipDesign.Core.Procedural;

/// <summary>
/// Grows a ship as filled voxels in a VoxelGrid: a random-walk hull envelope (instead of a
/// fixed profile curve -- the whole point is that two ships with the same class and different
/// seeds come out with genuinely different silhouettes, not just scaled copies of one another),
/// plus wings/engines/nacelles/superstructure/turrets/cockpit/greebles as further voxel fills
/// on top of it. Everything is mirrored across X for a symmetric ship.
/// </summary>
public static class VoxelShipGrower
{
    /// <summary>World units per voxel -- tuned so a default-sized ship (length ~14) comes out
    /// to a few thousand triangles, a reasonable game-asset budget for culled voxel meshing.</summary>
    public const float VoxelSize = 0.55f;

    public static VoxelGrid Grow(ShipParameters p, HullClassPreset preset, out int lengthVoxels)
    {
        var random = new Random(p.Seed);
        var grid = new VoxelGrid();

        lengthVoxels = Math.Max(10, (int)MathF.Round(p.Length / VoxelSize));
        var beamVoxels = Math.Max(5, (int)MathF.Round(p.Beam / VoxelSize));
        var maxHalfWidth = Math.Max(2, beamVoxels / 2);
        var maxHalfHeight = Math.Max(1, (int)MathF.Round(maxHalfWidth * preset.HeightRatio));

        var (halfWidthAt, halfHeightAt) = GrowHullEnvelope(random, p, preset, lengthVoxels, maxHalfWidth, maxHalfHeight);
        FillHull(grid, halfWidthAt, halfHeightAt, lengthVoxels);

        if (p.Greebles)
            GrowGreebles(grid, random, p, halfWidthAt, halfHeightAt, lengthVoxels);

        GrowCockpit(grid, p, preset, halfWidthAt, halfHeightAt, lengthVoxels);

        if (p.WingStyle != WingStyle.None)
            GrowWings(grid, p, halfWidthAt, lengthVoxels);

        GrowEngines(grid, random, p, halfWidthAt, halfHeightAt, lengthVoxels);

        if (p.Nacelles)
            GrowNacelles(grid, p, halfWidthAt, lengthVoxels);

        if (p.Superstructure && p.HullClass != HullClass.Fighter)
            GrowSuperstructure(grid, p, halfWidthAt, halfHeightAt, lengthVoxels);

        if (p.TurretCount > 0)
            GrowTurrets(grid, p, halfWidthAt, halfHeightAt, lengthVoxels);

        return grid;
    }

    /// <summary>The 0..1 base envelope shape before noise: a rise through the nose taper, a
    /// flat body, then a taper toward the tail that doesn't fully close (matching the
    /// mesh-hull's "engines mask the open tail" convention from the earlier revision).</summary>
    private static float EnvelopeShape(float u, HullClassPreset preset)
    {
        if (u < preset.NoseFraction)
            return MathF.Pow(u / preset.NoseFraction, 0.7f);
        if (u < preset.TailFraction)
            return 1f;
        var t = (u - preset.TailFraction) / (1f - preset.TailFraction);
        return 1f - t * 0.5f;
    }

    private static (int[] HalfWidth, int[] HalfHeight) GrowHullEnvelope(
        Random random, ShipParameters p, HullClassPreset preset, int lengthVoxels, int maxHalfWidth, int maxHalfHeight)
    {
        var halfWidthAt = new int[lengthVoxels];
        var halfHeightAt = new int[lengthVoxels];
        float w = 0f, h = 0f;

        for (var z = 0; z < lengthVoxels; z++)
        {
            var u = z / (float)(lengthVoxels - 1);
            var shape = EnvelopeShape(u, preset);
            var targetW = shape * maxHalfWidth;
            var targetH = shape * maxHalfHeight;

            // Random-walk toward the target envelope rather than snapping to it -- the noise
            // term is what makes the silhouette differ between seeds, not just the class shape.
            var noiseW = ((float)random.NextDouble() - 0.5f) * 2f * preset.Jaggedness;
            var noiseH = ((float)random.NextDouble() - 0.5f) * 2f * preset.Jaggedness * 0.6f;
            w = Math.Clamp(w + (targetW - w) * 0.45f + noiseW, 0f, maxHalfWidth);
            h = Math.Clamp(h + (targetH - h) * 0.45f + noiseH, 0f, maxHalfHeight);

            halfWidthAt[z] = (int)MathF.Round(w);
            halfHeightAt[z] = (int)MathF.Round(h);
        }

        return (halfWidthAt, halfHeightAt);
    }

    private static void FillHull(VoxelGrid grid, int[] halfWidthAt, int[] halfHeightAt, int lengthVoxels)
    {
        for (var z = 0; z < lengthVoxels; z++)
        {
            var hw = halfWidthAt[z];
            var hh = halfHeightAt[z];
            for (var x = 0; x <= hw; x++)
            for (var y = -hh; y <= hh; y++)
                grid.SetMirrored(x, y, z, VoxelMaterial.Hull);
        }
    }

    /// <summary>Scatters single-voxel surface bumps (the voxel equivalent of the old
    /// small-panel greebles), seeded so the same seed always gives the same detailing.</summary>
    private static void GrowGreebles(VoxelGrid grid, Random random, ShipParameters p, int[] halfWidthAt, int[] halfHeightAt, int lengthVoxels)
    {
        var count = (int)MathF.Round(p.GreebleDensity * lengthVoxels * 1.5f);
        for (var i = 0; i < count; i++)
        {
            var z = random.Next(lengthVoxels);
            var hw = halfWidthAt[z];
            var hh = halfHeightAt[z];
            if (hw < 1 || hh < 1)
                continue;

            // Pick a random point on the hull's rectangular envelope boundary at this z-slice.
            var onSide = random.Next(2) == 0;
            var x = onSide ? hw : random.Next(0, hw + 1);
            var y = onSide ? random.Next(-hh, hh + 1) : hh;
            grid.SetMirrored(x + (onSide ? 1 : 0), y + (onSide ? 0 : 1), z, VoxelMaterial.Accent);
        }
    }

    private static void GrowCockpit(VoxelGrid grid, ShipParameters p, HullClassPreset preset, int[] halfWidthAt, int[] halfHeightAt, int lengthVoxels)
    {
        if (p.CockpitStyle == CockpitStyle.None)
            return;

        var size = Math.Max(1, (int)MathF.Round(p.CockpitSize));
        var z = Math.Clamp((int)MathF.Round(preset.NoseFraction * 0.7f * (lengthVoxels - 1)), 0, lengthVoxels - 1);
        var hh = halfHeightAt[z];

        for (var dz = 0; dz < Math.Max(1, size); dz++)
        {
            var zz = Math.Min(z + dz, lengthVoxels - 1);
            for (var dy = 1; dy <= size; dy++)
                grid.SetMirrored(0, hh + dy, zz, VoxelMaterial.Cockpit);
        }
    }

    private static void GrowWings(VoxelGrid grid, ShipParameters p, int[] halfWidthAt, int lengthVoxels)
    {
        var spanVoxels = Math.Max(1, (int)MathF.Round(p.WingSpan / VoxelSize));
        var bandCenterZ = (int)MathF.Round(0.55f * (lengthVoxels - 1));
        var bandHalfLen = Math.Max(1, lengthVoxels / 9);
        var sweepBias = p.WingSweepDegrees / 60f; // 0..1, shifts the wide part of the band aft

        for (var dz = -bandHalfLen; dz <= bandHalfLen; dz++)
        {
            var z = bandCenterZ + dz;
            if (z < 0 || z >= lengthVoxels)
                continue;

            var t = (dz + bandHalfLen) / (float)(bandHalfLen * 2); // 0 at fore edge, 1 at aft edge
            var taper = 1f - MathF.Abs(t - (0.5f + sweepBias * 0.4f)) * 1.6f;
            taper = Math.Clamp(taper, 0f, 1f);

            var hw = halfWidthAt[z];
            var extend = (int)MathF.Round(spanVoxels * taper);
            for (var x = hw; x <= hw + extend; x++)
                grid.SetMirrored(x, 0, z, VoxelMaterial.Accent);
        }
    }

    private static void GrowEngines(VoxelGrid grid, Random random, ShipParameters p, int[] halfWidthAt, int[] halfHeightAt, int lengthVoxels)
    {
        var tailZ = lengthVoxels - 1;
        var tailHalfWidth = Math.Max(1, halfWidthAt[tailZ]);
        var tailHalfHeight = Math.Max(1, halfHeightAt[tailZ]);
        var count = Math.Max(1, p.EngineCount);

        var positions = new List<(int x, int y)>();
        if (count == 1)
        {
            positions.Add((0, 0));
        }
        else
        {
            for (var i = 0; i < count; i++)
            {
                var angle = (float)i / count * MathF.PI * 2f + MathF.PI / 4f;
                positions.Add((
                    (int)MathF.Round(MathF.Cos(angle) * tailHalfWidth * 0.55f),
                    (int)MathF.Round(MathF.Sin(angle) * tailHalfHeight * 0.55f)));
            }
        }

        var depth = p.EngineStyle == EngineStyle.Ring ? 1 : 2;
        foreach (var (x, y) in positions)
        {
            for (var dz = 0; dz < depth; dz++)
            {
                var z = tailZ + dz;
                var material = dz == depth - 1 ? VoxelMaterial.Glow : VoxelMaterial.Hull;
                grid.SetMirrored(x, y, z, material);
                // A slightly wider mount ring so the nozzle reads as more than a single column.
                grid.SetMirrored(x + 1, y, z, material);
                grid.SetMirrored(x, y + 1, z, material);
            }
        }
    }

    private static void GrowNacelles(VoxelGrid grid, ShipParameters p, int[] halfWidthAt, int lengthVoxels)
    {
        var size = Math.Max(1, (int)MathF.Round(p.NacelleSize * 2f));
        var lengthSpan = Math.Max(3, (int)MathF.Round(lengthVoxels * 0.4f * p.NacelleSize));
        var centerZ = (int)MathF.Round(0.62f * (lengthVoxels - 1));
        var hw = halfWidthAt[Math.Clamp(centerZ, 0, lengthVoxels - 1)];
        var nacelleX = hw + 3;

        // Pylon: a thin single-voxel bridge from the hull surface out to the nacelle.
        for (var x = hw; x <= nacelleX; x++)
            grid.SetMirrored(x, -1, centerZ, VoxelMaterial.Hull);

        var halfLen = lengthSpan / 2;
        for (var dz = -halfLen; dz <= halfLen; dz++)
        {
            var z = centerZ + dz;
            if (z < 0 || z >= lengthVoxels)
                continue;
            for (var dx = -size; dx <= size; dx++)
            for (var dy = -size; dy <= size; dy++)
            {
                if (dx * dx + dy * dy > size * size + 1)
                    continue;
                grid.SetMirrored(nacelleX + dx, dy, z, VoxelMaterial.Accent);
            }
        }
    }

    private static void GrowSuperstructure(VoxelGrid grid, ShipParameters p, int[] halfWidthAt, int[] halfHeightAt, int lengthVoxels)
    {
        var size = Math.Max(1, (int)MathF.Round(p.SuperstructureSize * 2f));
        var z = (int)MathF.Round(0.68f * (lengthVoxels - 1));
        z = Math.Clamp(z, 0, lengthVoxels - 1);
        var baseY = halfHeightAt[z];
        var halfWidth = Math.Max(1, halfWidthAt[z] / 2);

        for (var tier = 0; tier < 2; tier++)
        {
            var tierSize = Math.Max(1, size - tier);
            var tierHalfWidth = Math.Max(1, halfWidth - tier);
            for (var dz = -tierSize; dz <= tierSize; dz++)
            {
                var zz = z + dz;
                if (zz < 0 || zz >= lengthVoxels)
                    continue;
                for (var x = 0; x <= tierHalfWidth; x++)
                for (var dy = 0; dy < tierSize; dy++)
                    grid.SetMirrored(x, baseY + tier * tierSize + dy + 1, zz, VoxelMaterial.Hull);
            }
        }
    }

    private static void GrowTurrets(VoxelGrid grid, ShipParameters p, int[] halfWidthAt, int[] halfHeightAt, int lengthVoxels)
    {
        for (var i = 0; i < p.TurretCount; i++)
        {
            var t = (i + 0.5f) / p.TurretCount;
            var z = Math.Clamp((int)MathF.Round(t * (lengthVoxels - 1)), 0, lengthVoxels - 1);
            var hh = halfHeightAt[z];
            var hw = halfWidthAt[z];
            if (hw < 1)
                continue;

            var x = Math.Max(1, hw / 2);
            grid.SetMirrored(x, hh + 1, z, VoxelMaterial.Accent);
        }
    }
}
