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
            if (s == null || !s.Changes) return;

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
