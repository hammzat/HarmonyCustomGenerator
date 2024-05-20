using HarmonyLib;
using System.Reflection;
using CustomGenerator.Utilities;
using UnityEngine;

using static CustomGenerator.ExtConfig;
using System;
namespace CustomGenerator.Patches {
    [HarmonyPatch]
    internal static class TerrainMeta_Init
    {
        private static MethodBase TargetMethod() { return AccessTools.Method(typeof(TerrainMeta), nameof(TerrainMeta.Init)); }

        private static void Postfix(TerrainMeta __instance) {
            CheckConfig();
            if (__instance.GetComponent<TerrainTexturing>() == null) {
                Debug.Log("Wrong initialized saving terrain texturing!"); return;
            }
            tempData.terrainTexturing = __instance.GetComponent<TerrainTexturing>();
            Debug.Log("[CGen] Saved TerrainTexturing instance!");
        }
    }
    [HarmonyPatch]
    internal static class LoadingScreen_Update {
        private static MethodBase TargetMethod() { return AccessTools.Method(typeof(LoadingScreen), "Update", new Type[] { typeof(string) }); }
        private static void Prefix(ref string strType) {
            CheckConfig();
            if (tempData.terrainTexturing == null || strType != "DONE")  return;

            MapImage.RenderMap(tempData.terrainTexturing, 1, 150);
            tempData.mapGenerated = true;
            //Rust.Application.Quit();
            return;
        }
    }
}
