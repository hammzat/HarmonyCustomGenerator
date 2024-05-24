﻿using HarmonyLib;
using System.Reflection;
using CustomGenerator.Utilities;
using UnityEngine;
using System;

using static CustomGenerator.ExtConfig;
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

            Debug.Log($"SIZE: {tempData.mapsize} | SEED: {tempData.mapseed}");
            MapImage.RenderMap(tempData.terrainTexturing, 0.75f, 150);

            tempData.mapGenerated = true;
            //Rust.Application.Quit();
            Application.Quit();
            return;
        }
    }
}
