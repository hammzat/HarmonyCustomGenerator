using CustomGenerator.Utility;
using CustomGenerator.Shared;
using HarmonyLib;
using System;
using System.IO;
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

        private static void Postfix(uint seed) {
            var s = Config.Generator.LandShape;
            if (s == null || !s.HasChanges()) return;

            var map = TerrainMeta.HeightMap;
            var heights = Dst(map);
            int res = Res(map);
            float posY = TerrainMeta.Position.y, sizeY = TerrainMeta.Size.y;
            var h = new float[res * res];
            for (int i = 0; i < h.Length; i++) h[i] = posY + BitUtility.Short2Float(heights[i]) * sizeY;

            if (!string.IsNullOrWhiteSpace(s.Heightmap)) {
                string path = HeightmapPath(s.Heightmap);
                try {
                    var file = HeightmapFile.Read(File.ReadAllBytes(path), path);
                    file.Fill(h, res, s.HeightmapMin, s.HeightmapMax);
                    Logging.Generation($"Heightmap {s.Heightmap}: {file.Width}x{file.Height}, {file.Bits} bit, {s.HeightmapMin:0} to {s.HeightmapMax:0} m");
                } catch (Exception ex) {
                    Logging.Error($"Heightmap {path} not used: {ex.Message}");
                }
            }

            var result = HeightShape.Apply(h, res, TerrainMeta.Size.x / res, s.Options(seed));
            for (int i = 0; i < h.Length; i++) heights[i] = BitUtility.Float2Short(Mathf.Clamp01((h[i] - posY) / sizeY));

            Logging.Generation($"Land shape: land {result.LandBefore:0.#}% -> {result.LandAfter:0.#}%"
                + (s.FillInlandSea ? $", {result.InlandFilled:0.##}% of inland sea filled" : "")
                + (s.IslandCount > 0 ? $", {result.Islands} of {s.IslandCount} islands near the coast" : ""));
            if (s.VolcanoHeight > 0f) {
                if (!result.Volcano) { Logging.Generation("Volcano: no land for it"); return; }
                Vector3 world = TerrainMeta.Position + new Vector3(result.VolcanoX * TerrainMeta.Size.x, 0f, result.VolcanoZ * TerrainMeta.Size.z);
                VolcanoAt = world; VolcanoRadius = result.VolcanoRadius;
                Logging.Generation($"Volcano: {s.VolcanoHeight:0} m high, {result.VolcanoRadius:0} m radius at ({world.x:0}, {world.z:0})");
            }
        }

        // A bare file name is in heightmaps/, anything with a folder is relative to the mod's folder
        internal static string HeightmapPath(string value) =>
            value.IndexOfAny(new[] { '/', '\\' }) >= 0 || Path.IsPathRooted(value) ? Paths.Resolve(value) : Paths.Get("heightmaps", value);

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
    }
}
