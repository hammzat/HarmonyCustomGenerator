using HarmonyLib;
using System.Reflection;
using CustomGenerator.Utility;
using UnityEngine;
using System;
using System.IO;

using static CustomGenerator.ExtConfig;
namespace CustomGenerator.Patches {
    [HarmonyPatch]
    internal static class TerrainMeta_Init
    {
        private static MethodBase TargetMethod() { return AccessTools.Method(typeof(TerrainMeta), nameof(TerrainMeta.Init)); }

        private static PropertyInfo _terrainPath = AccessTools.TypeByName("TerrainMeta").GetProperty("Path", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        private static PropertyInfo _terrainTexturing = AccessTools.TypeByName("TerrainMeta").GetProperty("Texturing", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

        private static void Postfix(TerrainMeta __instance) {
            CheckConfig();

            tempData.terrainMeta = __instance;
            tempData.terrainTexturing = (TerrainTexturing)_terrainTexturing.GetValue(__instance);
            tempData.terrainPath = (TerrainPath)_terrainPath.GetValue(__instance);

            if (tempData.terrainPath == null || tempData.terrainTexturing == null || tempData.terrainMeta == null)
                Debug.Log("[CGen] One of components is null!");
            Debug.Log("[CGen] Saved TerrainTexturing instance!");
        }
    }
    [HarmonyPatch]
    internal static class LoadingScreen_Update {
        private static MethodBase TargetMethod() { return AccessTools.Method(typeof(LoadingScreen), "Update", new Type[] { typeof(string) }); }
        private static void Prefix(ref string strType) {
            CheckConfig();
            if (tempData.terrainTexturing == null || strType != "DONE")  return;

            Debug.Log($"SIZE: {tempData.mapsize} | SEED: {tempData.mapseed}");

            if (Config.Swap.Enabled)
            {
                string path = Path.GetFullPath("maps") + "\\" + string.Format(Config.mapSettings.MapName, tempData.mapsize, tempData.mapseed) + (!Config.mapSettings.MapName.EndsWith(".map") ? ".map" : "");

                SwapMonument.Initiate(path);
            }

            MapImage.RenderMap(tempData.terrainTexturing, 0.75f, 150);

            //Rust.Application.Quit();
            Application.Quit();
            return;
        }
    }

    [HarmonyPatch]
    internal static class Timing_Start
    {
        private static MethodBase TargetMethod() { return AccessTools.Method(typeof(Timing), "Start", new Type[] { typeof(string) }); }
        private static void Prefix(ref string name)
        {
            CheckConfig();
            if (name != "Processing World") return;
            if (!Config.Generator.ModifyPercentages) return;

            LoadPercentages();
            Debug.Log($"[CGen] Changing tier percentages...");
        }
        static void LoadPercentages()
        {
            float sum1 = Config.Generator.Tier.Tier0 + Config.Generator.Tier.Tier1 + Config.Generator.Tier.Tier2;
            if (sum1 >= 100f)
            {
                World.Config.PercentageTier0 = Config.Generator.Tier.Tier0 / sum1;
                World.Config.PercentageTier1 = Config.Generator.Tier.Tier1 / sum1;
                World.Config.PercentageTier2 = Config.Generator.Tier.Tier2 / sum1;
            }
            else
            {
                Debug.Log("Tier perc. summs lower than 100! Set default.");
                World.Config.PercentageTier0 = 0.3f;
                World.Config.PercentageTier1 = 0.3f;
                World.Config.PercentageTier2 = 0.4f;
            }
            float sum2 = Config.Generator.Biom.Arid + Config.Generator.Biom.Arctic + Config.Generator.Biom.Temperate + Config.Generator.Biom.Tundra;
            if (sum2 >= 100f)
            {
                World.Config.PercentageBiomeArid = Config.Generator.Biom.Arid / sum2;
                World.Config.PercentageBiomeArctic = Config.Generator.Biom.Arctic / sum2;
                World.Config.PercentageBiomeTemperate = Config.Generator.Biom.Temperate / sum2;
                World.Config.PercentageBiomeTundra = Config.Generator.Biom.Tundra / sum2;
            }
            else
            {
                Debug.Log("Biom perc. summs lower than 100! Set default.");
                World.Config.PercentageBiomeArctic = 0.3f;
                World.Config.PercentageBiomeArid = 0.4f;
                World.Config.PercentageBiomeTundra = 0.15f;
                World.Config.PercentageBiomeTemperate = 0.15f;
            }
        }
    }
}
