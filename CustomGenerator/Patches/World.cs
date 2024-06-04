using HarmonyLib;
using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

using static CustomGenerator.ExtConfig;
namespace CustomGenerator.Generators
{
    [HarmonyPatch(typeof(World), nameof(World.InitSize), new Type[] { typeof(uint) })]
    internal static class World_InitSize {
        private static uint _size = 0;
        private static void Prefix(ref uint size) {
            CheckConfig();
            if (!Config.mapSettings.OverrideSizes) return;
            tempData.mapsize = size;
            _size = size;

            Debug.Log("[CGen] Writed size to convars...");
            if (size > 6000U || size < 1000U) {
                Debug.Log($"[CGen - WORLD] ({_size}) - Using size bigger or smaller than default, rewriting limits...");
            }
        }
    }

    [HarmonyPatch(typeof(World), nameof(World.InitSeed), new Type[] { typeof(uint) })]
    internal static class World_InitSeed {
        private static void Prefix(ref uint seed) {
            CheckConfig();
            tempData.mapseed = seed;
            Debug.Log("[CGen] Writed seed to convars...");
        }
    }

    [HarmonyPatch(typeof(World), "get_Size")]
    public static class World_getSize {
        public static void Postfix(ref uint __result) {
            CheckConfig();
            if (!Config.mapSettings.OverrideSizes) return;
            if (tempData.mapsize == 0) { Debug.Log("map size == 0!"); return; }
            __result = tempData.mapsize;
        }
    }
    [HarmonyPatch(typeof(World), "get_MapFolderName")]
    public static class World_getMapFolderName {
        static readonly string FolderName = "maps";
        static readonly string FolderLocation = Path.GetFullPath(FolderName);
        public static void Postfix(ref string __result) {
            CheckConfig();
            if (!Config.mapSettings.OverrideFolder) return;
            if (!Directory.Exists(FolderName))
                Directory.CreateDirectory(FolderName);

            Debug.Log($"Override save folder to {FolderLocation}");
            __result = FolderLocation;
        }
    }

    [HarmonyPatch(typeof(World), nameof(World.CanLoadFromDisk))]
    public static class GetSizePatch {
        public static void Postfix(ref bool __result) {
            CheckConfig();
            if (!Config.mapSettings.GenerateNewMapEverytime) return;
            __result = false;

            if (Config.Generator.ModifyPercentages) LoadPercentages();
        }

        static void LoadPercentages()
        {
            float sum1 = Config.Generator.Tier.Tier0 + Config.Generator.Tier.Tier1 + Config.Generator.Tier.Tier2;
            if (sum1 >= 100f) {
                World.Config.PercentageTier0 = Config.Generator.Tier.Tier0 / sum1;
                World.Config.PercentageTier1 = Config.Generator.Tier.Tier1 / sum1;
                World.Config.PercentageTier2 = Config.Generator.Tier.Tier2 / sum1;
            }
            else {
                Debug.Log("Tier perc. summs lower than 100! Set default.");
                World.Config.PercentageTier0 = 0.3f;
                World.Config.PercentageTier1 = 0.3f;
                World.Config.PercentageTier2 = 0.4f;
            }
            float sum2 = Config.Generator.Biom.Arid + Config.Generator.Biom.Arctic + Config.Generator.Biom.Temperate + Config.Generator.Biom.Tundra;
            if (sum2 >= 100f) {
                World.Config.PercentageBiomeArid = Config.Generator.Biom.Arid / sum2;
                World.Config.PercentageBiomeArctic = Config.Generator.Biom.Arctic / sum2;
                World.Config.PercentageBiomeTemperate = Config.Generator.Biom.Temperate / sum2;
                World.Config.PercentageBiomeTundra = Config.Generator.Biom.Tundra / sum2;
            }
            else {
                Debug.Log("Biom perc. summs lower than 100! Set default.");
                World.Config.PercentageBiomeArctic = 0.3f;
                World.Config.PercentageBiomeArid = 0.4f;
                World.Config.PercentageBiomeTundra = 0.15f;
                World.Config.PercentageBiomeTemperate = 0.15f;
            }
        }
    }

    [HarmonyPatch(typeof(World), "get_MapFileName")]
    public static class World_getMapFileName {
        public static void Postfix(ref string __result) {
            CheckConfig();
            if (!Config.mapSettings.OverrideName) return;
            string mapName = string.Format(Config.mapSettings.MapName, tempData.mapsize, tempData.mapseed) + (!Config.mapSettings.MapName.EndsWith(".map") ? ".map" : "");
            Debug.Log($"Override map name to {mapName}");
            __result = mapName;
        }
    }
}