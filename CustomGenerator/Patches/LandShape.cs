using CustomGenerator.Utility;
using HarmonyLib;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

using static CustomGenerator.ExtConfig;
namespace CustomGenerator.Patches
{
    // "Land shape": reworks the base heightmap right after GenerateHeight (native code), before erosion, cliffs,
    // rivers and monuments, so everything later is placed on the new shape. Heights are in meters, sea level = 0.
    [HarmonyPatch(typeof(GenerateHeight), nameof(GenerateHeight.Process))]
    internal static class LandShape
    {
        private static readonly AccessTools.FieldRef<TerrainMap<short>, NativeArray<short>> Dst = AccessTools.FieldRefAccess<TerrainMap<short>, NativeArray<short>>("dst");
        private static readonly AccessTools.FieldRef<TerrainMap, int> Res = AccessTools.FieldRefAccess<TerrainMap, int>("res");

        private static void Postfix() {
            var s = Config.Generator.LandShape;
            if (s == null || !s.HasChanges()) return;

            var map = TerrainMeta.HeightMap;
            var heights = Dst(map);
            int res = Res(map);
            float posY = TerrainMeta.Position.y, sizeY = TerrainMeta.Size.y;
            // The map edge stays as deep as it was: the land level fades in over the outer 8%
            float fade = Mathf.Max(1f, res * 0.08f);

            int landBefore = 0, landAfter = 0;
            for (int z = 0; z < res; z++) {
                for (int x = 0; x < res; x++) {
                    int i = z * res + x;
                    float h = posY + BitUtility.Short2Float(heights[i]) * sizeY;
                    if (h > 0f) landBefore++;

                    if (s.LandLevel != 0f) {
                        int edge = Mathf.Min(Mathf.Min(x, z), Mathf.Min(res - 1 - x, res - 1 - z));
                        h += s.LandLevel * Mathf.SmoothStep(0f, 1f, edge / fade);
                    }
                    if (h > 0f) {
                        h *= s.MountainHeight;
                        // Below BeachHeight the slope goes from 0 at the waterline to 1 at the top: h²/B keeps the height and slope continuous at B
                        if (s.BeachHeight > 0f && h < s.BeachHeight) h = h * h / s.BeachHeight;
                    } else if (s.ShelfDepth > 0f && h > -s.ShelfDepth) {
                        h = -h * h / s.ShelfDepth;
                    }

                    heights[i] = BitUtility.Float2Short(Mathf.Clamp01((h - posY) / sizeY));
                    if (h > 0f) landAfter++;
                }
            }

            int filled = s.FillInlandSea ? FillInlandSea(heights, res, posY, sizeY) : 0;
            float total = res * (float)res / 100f;
            Logging.Generation($"Land shape: land {landBefore / total:0.#}% -> {(landAfter + filled) / total:0.#}%"
                + (s.FillInlandSea ? $", {filled / total:0.##}% of inland sea filled" : ""));
            if (s.VolcanoHeight > 0f) Volcano(heights, res, posY, sizeY, s.VolcanoHeight, s.VolcanoRadius);
        }

        // A cone with a crater on the land point furthest from any water, so it fits on the biggest landmass
        private static void Volcano(NativeArray<short> heights, int res, float posY, float sizeY, float height, float radius) {
            short sea = BitUtility.Float2Short(Mathf.Clamp01((0f - posY) / sizeY));
            // Distance to water in cells (4-neighbour steps), from every water cell at once
            var dist = new int[res * res];
            var queue = new int[res * res];
            int head = 0, tail = 0;
            for (int i = 0; i < dist.Length; i++) {
                if (heights[i] <= sea) { dist[i] = 0; queue[tail++] = i; } else dist[i] = int.MaxValue;
            }
            int best = -1;
            while (head < tail) {
                int i = queue[head++], x = i % res, z = i / res, d = dist[i] + 1;
                best = i;
                if (x > 0 && dist[i - 1] > d) { dist[i - 1] = d; queue[tail++] = i - 1; }
                if (x < res - 1 && dist[i + 1] > d) { dist[i + 1] = d; queue[tail++] = i + 1; }
                if (z > 0 && dist[i - res] > d) { dist[i - res] = d; queue[tail++] = i - res; }
                if (z < res - 1 && dist[i + res] > d) { dist[i + res] = d; queue[tail++] = i + res; }
            }
            if (best < 0 || dist[best] == 0 || dist[best] == int.MaxValue) { Logging.Generation("Volcano: no land for it"); return; }

            float cell = TerrainMeta.Size.x / res;
            // Fit the cone on the land: no wider than the way to the water
            float r = Mathf.Min(radius, dist[best] * cell * 1.2f);
            int cx = best % res, cz = best / res, rc = Mathf.CeilToInt(r / cell);
            float craterR = r * 0.18f, craterDepth = height * 0.3f;
            float Cone(float t) => height * Mathf.Pow(1f - t, 1.6f);
            float rim = Cone(craterR / r);
            for (int z = Mathf.Max(0, cz - rc); z <= Mathf.Min(res - 1, cz + rc); z++) {
                for (int x = Mathf.Max(0, cx - rc); x <= Mathf.Min(res - 1, cx + rc); x++) {
                    float d = Mathf.Sqrt((x - cx) * (x - cx) + (z - cz) * (z - cz)) * cell;
                    if (d >= r) continue;
                    float add = d < craterR ? rim - craterDepth * (1f - d * d / (craterR * craterR)) : Cone(d / r);
                    int i = z * res + x;
                    float h = posY + BitUtility.Short2Float(heights[i]) * sizeY + add;
                    heights[i] = BitUtility.Float2Short(Mathf.Clamp01((h - posY) / sizeY));
                }
            }
            Vector3 world = TerrainMeta.Position + new Vector3((cx + 0.5f) * cell, 0f, (cz + 0.5f) * cell);
            VolcanoAt = world; VolcanoRadius = r;
            Logging.Generation($"Volcano: {height:0} m high, {r:0} m radius at ({world.x:0}, {world.z:0})");
        }

        internal static Vector3? VolcanoAt;
        internal static float VolcanoRadius;

        // The volcano would get the snow or grass of its height and biome: bare rock on the slopes, gravel elsewhere,
        // stones in the crater, blended into the ground around it. Right before GenerateTextures saves the splat map
        [HarmonyPatch(typeof(GenerateTextures), nameof(GenerateTextures.Process))]
        private static class VolcanoGround
        {
            private static void Prefix() {
                if (VolcanoAt == null || World.Cached) return;
                var splat = TerrainMeta.SplatMap;
                if (splat == null) return;
                int res = Res(splat);
                Vector3 c = VolcanoAt.Value, pos = TerrainMeta.Position, size = TerrainMeta.Size;
                float r = VolcanoRadius;
                int rock = TerrainSplat.TypeToIndex((int)TerrainSplat.Enum.Rock), gravel = TerrainSplat.TypeToIndex((int)TerrainSplat.Enum.Gravel),
                    stones = TerrainSplat.TypeToIndex((int)TerrainSplat.Enum.Stones);
                int x0 = Mathf.Max(0, Mathf.FloorToInt((c.x - r - pos.x) / size.x * res)), x1 = Mathf.Min(res - 1, Mathf.CeilToInt((c.x + r - pos.x) / size.x * res));
                int z0 = Mathf.Max(0, Mathf.FloorToInt((c.z - r - pos.z) / size.z * res)), z1 = Mathf.Min(res - 1, Mathf.CeilToInt((c.z + r - pos.z) / size.z * res));
                splat.Push();
                for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++) {
                    float nx = (x + 0.5f) / res, nz = (z + 0.5f) / res;
                    float d = Vector2.Distance(new Vector2(pos.x + nx * size.x, pos.z + nz * size.z), new Vector2(c.x, c.z));
                    if (d >= r) continue;
                    float blend = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(r * 0.7f, r, d));
                    var w = new float[8];
                    for (int i = 0; i < 8; i++) w[i] = splat.GetSplat(x, z, TerrainSplat.IndexToType(i)) * (1f - blend);
                    if (d < r * 0.18f) w[stones] += blend * 0.6f;
                    float steep = Mathf.InverseLerp(15f, 30f, TerrainMeta.HeightMap.GetSlope(nx, nz));
                    w[rock] += blend * (d < r * 0.18f ? 0.4f : steep);
                    w[gravel] += blend * (d < r * 0.18f ? 0f : 1f - steep);
                    splat.SetSplatRaw(x, z, new Vector4(w[0], w[1], w[2], w[3]), new Vector4(w[4], w[5], w[6], w[7]), 1f);
                }
                splat.Pop();
                Logging.Generation("Volcano: rock and gravel ground");
            }
        }

        // Flood fill the sea from the map edge; anything below the sea that the fill doesn't reach is an inland sea
        private static int FillInlandSea(NativeArray<short> heights, int res, float posY, float sizeY) {
            short sea = BitUtility.Float2Short(Mathf.Clamp01((0f - posY) / sizeY));
            short raised = BitUtility.Float2Short(Mathf.Clamp01((0.5f - posY) / sizeY));
            var ocean = new bool[res * res];
            var queue = new Queue<int>();
            void Visit(int i) {
                if (ocean[i] || heights[i] > sea) return;
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
            for (int i = 0; i < heights.Length; i++) {
                if (ocean[i] || heights[i] > sea) continue;
                heights[i] = raised;
                filled++;
            }
            return filled;
        }
    }
}
