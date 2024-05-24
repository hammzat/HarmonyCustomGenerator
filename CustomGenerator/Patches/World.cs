using HarmonyLib;
using Rust.Demo;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using UnityEngine;

using static CustomGenerator.ExtConfig;
namespace CustomGenerator.Generators
{
    [HarmonyPatch(typeof(World), nameof(World.InitSize), new Type[] { typeof(uint) })]
    internal static class World_InitSize {
        private static uint _size = 0;
        private static void Prefix(ref uint size) {
            CheckConfig();
            if (!Config.OverallocateSizes) return;
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
            if (!Config.OverallocateSizes) return;
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
            if (!Config.OverallocateFolder) return;
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
            if (!Config.GenerateNewMapEverytime) return;
            __result = false;
        }
    }
}