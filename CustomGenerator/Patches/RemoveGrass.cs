using CustomGenerator.Utility;
using HarmonyLib;
using System.Reflection;
using UnityEngine;

using static CustomGenerator.ExtConfig;
namespace CustomGenerator.Patches
{
    // "Remove grass": the grass ground texture becomes dirt, and grass grows only where that texture is.
    // Done right before GenerateTextures writes the splat map into the save.
    [HarmonyPatch]
    internal static class RemoveGrass
    {
        private static readonly AccessTools.FieldRef<TerrainMap, int> Res = AccessTools.FieldRefAccess<TerrainMap, int>("res");

        private static MethodBase TargetMethod() => AccessTools.Method(typeof(GenerateTextures), nameof(GenerateTextures.Process));

        private static void Prefix() {
            if (!Config.Generator.RemoveGrass || World.Cached) return;
            var splat = TerrainMeta.SplatMap;
            if (splat == null) { Logging.Error("Remove grass: the splat map isn't ready, grass kept"); return; }

            int grass = (int)TerrainSplat.Enum.Grass, dirt = (int)TerrainSplat.Enum.Dirt;
            int res = Res(splat), changed = 0;
            // Writes go to a copy (dst) while reads come from the original (src); Pop copies them back
            splat.Push();
            for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++) {
                float g = splat.GetSplat(x, z, grass);
                if (g <= 0f) continue;
                // Raw weights of all 8 channels in index order, grass moved over to dirt
                var w = new float[8];
                for (int i = 0; i < 8; i++) w[i] = splat.GetSplat(x, z, TerrainSplat.IndexToType(i));
                w[TerrainSplat.TypeToIndex(dirt)] += w[TerrainSplat.TypeToIndex(grass)];
                w[TerrainSplat.TypeToIndex(grass)] = 0f;
                splat.SetSplatRaw(x, z, new Vector4(w[0], w[1], w[2], w[3]), new Vector4(w[4], w[5], w[6], w[7]), 1f);
                changed++;
            }
            splat.Pop();
            Logging.Generation($"Remove grass: grass ground turned into dirt on {100f * changed / (res * (float)res):0}% of the map");
        }
    }
}
