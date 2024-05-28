using HarmonyLib;
using System.Reflection;
using CustomGenerator.Utility;
using UnityEngine;
using System;

using static CustomGenerator.ExtConfig;
using System.Collections.Generic;
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
            {
                Debug.Log("[CGen] PIZDEC!");
            }
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
            MapImage.RenderMap(tempData.terrainTexturing, 0.75f, 150);

            tempData.mapGenerated = true;
            //Rust.Application.Quit();
            Application.Quit();
            return;
        }
    }
}
