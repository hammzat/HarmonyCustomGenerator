using System;
using System.Collections.Generic;

namespace CustomGenerator.Shared
{
    // "Land shape" math on a heightmap in meters (sea level = 0), row-major, z = row from south to north.
    // No Unity in here: the mod runs it on the real heightmap right after GenerateHeight, the launcher on a small one
    // for the preview, so both give the same shape. Random choices are made in map coordinates (0..1), not in cells,
    // so a preview at a lower resolution puts islands and the volcano where the real map gets them.
    // This file is compiled into CustomGenerator and CustomGenerator.Launcher.
    public sealed class ShapeOptions
    {
        public float LandLevel, MountainHeight = 1f, BeachHeight, ShelfDepth;
        public bool FillInlandSea;
        public int IslandCount;
        public float IslandSize = 180f, IslandDistance = 300f;
        public float VolcanoHeight, VolcanoRadius = 450f;
        // Seed of the height step, so islands change with the map seed
        public uint Seed;
    }

    public sealed class ShapeResult
    {
        public float LandBefore, LandAfter, InlandFilled;
        public int Islands;
        public bool Volcano;
        public float VolcanoX, VolcanoZ, VolcanoRadius;     // map coordinates 0..1 and meters
    }

    public static class HeightShape
    {
        public static ShapeResult Apply(float[] h, int res, float cell, ShapeOptions o) {
            var result = new ShapeResult();
            int n = res * res;
            // The map edge stays as deep as it was: the land level fades in over the outer 8%
            float fade = Math.Max(1f, res * 0.08f);
            int before = 0;
            for (int z = 0; z < res; z++) {
                for (int x = 0; x < res; x++) {
                    int i = z * res + x;
                    float v = h[i];
                    if (v > 0f) before++;
                    if (o.LandLevel != 0f) {
                        int edge = Math.Min(Math.Min(x, z), Math.Min(res - 1 - x, res - 1 - z));
                        v += o.LandLevel * SmoothStep(edge / fade);
                    }
                    if (v > 0f) v *= o.MountainHeight;
                    h[i] = v;
                }
            }
            result.LandBefore = 100f * before / n;

            if (o.IslandCount > 0) result.Islands = Islands(h, res, cell, o);

            for (int i = 0; i < n; i++) {
                float v = h[i];
                // Below BeachHeight the slope goes from 0 at the waterline to 1 at the top: v²/B keeps the height and slope continuous at B
                if (v > 0f) { if (o.BeachHeight > 0f && v < o.BeachHeight) h[i] = v * v / o.BeachHeight; }
                else if (o.ShelfDepth > 0f && v > -o.ShelfDepth) h[i] = -v * v / o.ShelfDepth;
            }

            if (o.FillInlandSea) result.InlandFilled = 100f * FillInlandSea(h, res) / n;
            if (o.VolcanoHeight > 0f) Volcano(h, res, cell, o.VolcanoHeight, o.VolcanoRadius, result);

            int after = 0;
            for (int i = 0; i < n; i++) if (h[i] > 0f) after++;
            result.LandAfter = 100f * after / n;
            return result;
        }

        // Small islands in the sea along the coasts: a random spot in the water whose gap to the coast is between 40 m
        // and IslandDistance, away from the map edge and from the other islands
        private static int Islands(float[] h, int res, float cell, ShapeOptions o) {
            var land = new bool[h.Length];
            for (int i = 0; i < h.Length; i++) land[i] = h[i] > 0f;
            var toLand = Distance(land, res);
            uint rng = o.Seed * 2654435761u ^ 0x5bd1e995u;
            if (rng == 0) rng = 1;
            float r = o.IslandSize;
            var placed = new List<(float x, float z, float r)>();
            // The outline reaches up to Wobble x the radius; the outer 3% of the map stays open sea
            float edge = 0.03f * res * cell;
            for (int tries = 0; tries < 6000 && placed.Count < o.IslandCount; tries++) {
                float u = Next01(ref rng), v = Next01(ref rng);
                float size = r * (0.7f + 0.6f * Next01(ref rng));
                int i = Cell(u, res) + Cell(v, res) * res;
                if (land[i]) continue;
                // Shrinks to fit the spot, down to 60% of the size it rolled: at least 40 m of sea to the coast and to the edge
                float toEdge = Math.Min(Math.Min(u, 1f - u), Math.Min(v, 1f - v)) * res * cell - edge;
                float room = Math.Min((toLand[i] * cell - 40f) / Wobble, toEdge / Wobble);
                if (room < size * 0.6f) continue;
                size = Math.Min(size, room);
                if (toLand[i] * cell - size * Wobble > o.IslandDistance) continue;
                bool crowded = false;
                foreach (var p in placed) {
                    float dx = (p.x - u) * res * cell, dz = (p.z - v) * res * cell;
                    if (dx * dx + dz * dz < Sq((size + p.r) * Wobble + 80f)) { crowded = true; break; }
                }
                if (crowded) continue;
                placed.Add((u, v, size));
                Island(h, res, cell, u, v, size, ref rng);
            }
            return placed.Count;
        }

        // The outline is at most this many radii from the center (sum of the waves below)
        private const float Wobble = 1.26f;

        // A hill with a wavy outline: sea level at about the radius, a shallow ring around it, noisy top
        private static void Island(float[] h, int res, float cell, float u, float v, float r, ref uint rng) {
            float top = Clamp(r * 0.2f, 10f, 60f) * (0.75f + 0.5f * Next01(ref rng));
            float p1 = Next01(ref rng) * 6.2832f, p2 = Next01(ref rng) * 6.2832f, p3 = Next01(ref rng) * 6.2832f;
            uint noiseSeed = Xorshift(ref rng);
            float cx = u * res - 0.5f, cz = v * res - 0.5f;
            int rc = (int)Math.Ceiling(r * 1.6f * Wobble / cell);
            for (int z = Math.Max(0, (int)cz - rc); z <= Math.Min(res - 1, (int)cz + rc); z++) {
                for (int x = Math.Max(0, (int)cx - rc); x <= Math.Min(res - 1, (int)cx + rc); x++) {
                    float dx = (x - cx) * cell, dz = (z - cz) * cell;
                    float d = (float)Math.Sqrt(dx * dx + dz * dz);
                    float a = (float)Math.Atan2(dz, dx);
                    float rr = r * (1f + 0.14f * (float)Math.Sin(2 * a + p1) + 0.08f * (float)Math.Sin(3 * a + p2) + 0.04f * (float)Math.Sin(5 * a + p3));
                    float t = d / rr;
                    if (t >= 1.6f) continue;
                    // Noise in meters of the map, so it's the same at any resolution
                    float wx = (x + 0.5f) * cell, wz = (z + 0.5f) * cell;
                    float bumps = Noise(wx / 90f, wz / 90f, noiseSeed) * 0.65f + Noise(wx / 35f, wz / 35f, noiseSeed + 1) * 0.35f;
                    float height = top * (1f - t * t) + (t < 1f ? top * 0.35f * bumps * (1f - t) : 0f);
                    int i = z * res + x;
                    if (height > h[i]) h[i] = height;
                }
            }
        }

        // A cone with a crater on the land point furthest from any water, so it fits on the biggest landmass
        private static void Volcano(float[] h, int res, float cell, float height, float radius, ShapeResult result) {
            var water = new bool[h.Length];
            for (int i = 0; i < h.Length; i++) water[i] = h[i] <= 0f;
            var toWater = Distance(water, res);
            int best = -1;
            float far = 0f;
            for (int i = 0; i < toWater.Length; i++) if (toWater[i] > far && toWater[i] < float.MaxValue) { far = toWater[i]; best = i; }
            if (best < 0) return;

            // Fit the cone on the land: no wider than the way to the water
            float r = Math.Min(radius, far * cell * 1.2f);
            int cx = best % res, cz = best / res, rc = (int)Math.Ceiling(r / cell);
            float craterR = r * 0.18f, craterDepth = height * 0.3f;
            float Cone(float t) => height * (float)Math.Pow(1f - t, 1.6f);
            float rim = Cone(craterR / r);
            for (int z = Math.Max(0, cz - rc); z <= Math.Min(res - 1, cz + rc); z++) {
                for (int x = Math.Max(0, cx - rc); x <= Math.Min(res - 1, cx + rc); x++) {
                    float d = (float)Math.Sqrt((x - cx) * (x - cx) + (z - cz) * (z - cz)) * cell;
                    if (d >= r) continue;
                    h[z * res + x] += d < craterR ? rim - craterDepth * (1f - d * d / (craterR * craterR)) : Cone(d / r);
                }
            }
            result.Volcano = true;
            result.VolcanoX = (cx + 0.5f) / res;
            result.VolcanoZ = (cz + 0.5f) / res;
            result.VolcanoRadius = r;
        }

        // Flood fill the sea from the map edge; anything below the sea that the fill doesn't reach is an inland sea.
        // Returns how many cells were raised just above the sea
        private static int FillInlandSea(float[] h, int res) {
            var ocean = new bool[h.Length];
            var queue = new Queue<int>();
            void Visit(int i) {
                if (ocean[i] || h[i] > 0f) return;
                ocean[i] = true;
                queue.Enqueue(i);
            }
            for (int k = 0; k < res; k++) {
                Visit(k); Visit((res - 1) * res + k); Visit(k * res); Visit(k * res + res - 1);
            }
            while (queue.Count > 0) {
                int i = queue.Dequeue(), x = i % res, z = i / res;
                if (x > 0) Visit(i - 1);
                if (x < res - 1) Visit(i + 1);
                if (z > 0) Visit(i - res);
                if (z < res - 1) Visit(i + res);
            }
            int filled = 0;
            for (int i = 0; i < h.Length; i++) {
                if (ocean[i] || h[i] > 0f) continue;
                h[i] = 0.5f;
                filled++;
            }
            return filled;
        }

        // Distance in cells from every cell to the nearest "from" cell (0 on them): two-pass chamfer, close to Euclidean
        private static float[] Distance(bool[] from, int res) {
            const float Straight = 1f, Diagonal = 1.4142f;
            var d = new float[from.Length];
            for (int i = 0; i < d.Length; i++) d[i] = from[i] ? 0f : float.MaxValue;
            for (int z = 0; z < res; z++) {
                for (int x = 0; x < res; x++) {
                    int i = z * res + x;
                    float v = d[i];
                    if (x > 0) v = Math.Min(v, d[i - 1] + Straight);
                    if (z > 0) {
                        v = Math.Min(v, d[i - res] + Straight);
                        if (x > 0) v = Math.Min(v, d[i - res - 1] + Diagonal);
                        if (x < res - 1) v = Math.Min(v, d[i - res + 1] + Diagonal);
                    }
                    d[i] = v;
                }
            }
            for (int z = res - 1; z >= 0; z--) {
                for (int x = res - 1; x >= 0; x--) {
                    int i = z * res + x;
                    float v = d[i];
                    if (x < res - 1) v = Math.Min(v, d[i + 1] + Straight);
                    if (z < res - 1) {
                        v = Math.Min(v, d[i + res] + Straight);
                        if (x < res - 1) v = Math.Min(v, d[i + res + 1] + Diagonal);
                        if (x > 0) v = Math.Min(v, d[i + res - 1] + Diagonal);
                    }
                    d[i] = v;
                }
            }
            return d;
        }

        // Smooth value noise in -1..1 on a grid of 1
        private static float Noise(float x, float z, uint seed) {
            int x0 = (int)Math.Floor(x), z0 = (int)Math.Floor(z);
            float fx = SmoothStep(x - x0), fz = SmoothStep(z - z0);
            float a = Hash(x0, z0, seed), b = Hash(x0 + 1, z0, seed), c = Hash(x0, z0 + 1, seed), d = Hash(x0 + 1, z0 + 1, seed);
            return (a + (b - a) * fx) * (1f - fz) + (c + (d - c) * fx) * fz;
        }

        private static float Hash(int x, int z, uint seed) {
            uint k = (uint)x * 374761393u + (uint)z * 668265263u + seed * 2246822519u;
            k = (k ^ (k >> 13)) * 1274126177u;
            k ^= k >> 16;
            return (k & 0xffffff) / (float)0x800000 - 1f;
        }

        private static uint Xorshift(ref uint x) { x ^= x << 13; x ^= x >> 17; x ^= x << 5; return x; }
        private static float Next01(ref uint x) => (Xorshift(ref x) & 0xffffff) / (float)0x1000000;
        private static int Cell(float u, int res) => Math.Min(res - 1, Math.Max(0, (int)(u * res)));
        private static float SmoothStep(float t) { t = Clamp(t, 0f, 1f); return t * t * (3f - 2f * t); }
        private static float Clamp(float v, float min, float max) => v < min ? min : v > max ? max : v;
        private static float Sq(float v) => v * v;
    }
}
