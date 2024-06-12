using CustomGenerator.Utility;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices.WindowsRuntime;
using UnityEngine;

using static CustomGenerator.ExtConfig;
namespace CustomGenerator.Generators
{

    [HarmonyPatch]
    class PlaceMonuments_Process
    {
        private static MethodBase TargetMethod() { return AccessTools.Method(typeof(PlaceMonuments), nameof(PlaceMonuments.Process)); }
        private static void Prefix(PlaceMonuments __instance) {
            if (!Config.Monuments.Enabled) return;
            Debug.Log("[CGen PlaceMonuments.");
            var matchMonuments = Config.Monuments.monuments.Where(x => x.Folder == __instance.ResourceFolder);
            if (!matchMonuments.Any()) return;
            
            var monument = matchMonuments.First();
            if (!monument.Generate) return;

            Debug.Log(__instance.Description);
            Debug.Log(__instance.ResourceFolder);

            __instance.DistanceDifferentType = monument.distanceDifferent;
            __instance.DistanceSameType = monument.distanceSame;
            __instance.MinWorldSize = monument.MinWorldSize;
            __instance.MinDistanceDifferentType = monument.MinDistanceDifferentType;
            __instance.MinDistanceSameType = monument.MinDistanceSameType;
            Debug.Log("[CGen] Changed values.");

            if (monument.Filter.Enabled) {

                __instance.Filter = new SpawnFilter {
                    BiomeType =   monument.Filter.BiomeType.Count == 0 ? (TerrainBiome.Enum)(-1) :    (TerrainBiome.Enum)EnumParser.GetFilterEnum("BiomeType", monument.Filter.BiomeType),
                    SplatType =   monument.Filter.BiomeType.Count == 0 ? (TerrainSplat.Enum)(-1) :    (TerrainSplat.Enum)EnumParser.GetFilterEnum("SplatType", monument.Filter.SplatType),
                    TopologyAll = monument.Filter.BiomeType.Count == 0 ? (TerrainTopology.Enum)(0) :  (TerrainTopology.Enum)EnumParser.GetFilterEnum("TopologyAll", monument.Filter.TopologyAll),
                    TopologyAny = monument.Filter.BiomeType.Count == 0 ? (TerrainTopology.Enum)(-1) : (TerrainTopology.Enum)EnumParser.GetFilterEnum("TopologyAny", monument.Filter.TopologyAny),
                    TopologyNot = monument.Filter.BiomeType.Count == 0 ? (TerrainTopology.Enum)(0) :  (TerrainTopology.Enum)EnumParser.GetFilterEnum("TopologyNot", monument.Filter.TopologyNot),
                };
                Debug.Log("[CGen] Changed filter.");
            }
        }
    }

    //[HarmonyPatch]
    //public static class WorldSetup_InitCoroutine
    //{
    //    private static MethodBase TargetMethod() { return AccessTools.Method(AccessTools.Inner(typeof(WorldSetup), "<InitCoroutine>d__19"), "MoveNext"); }
    //    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
    //        List<CodeInstruction> codes = instructions.ToList();
    //        MethodInfo targetMethod = typeof(Component).GetMethod("GetComponentsInChildren", new[] { typeof(bool) }).MakeGenericMethod(typeof(ProceduralComponent));

    //        for (int i = 0; i < codes.Count; i++) {
    //            if (!(codes[i].opcode == OpCodes.Call && codes[i].operand as MethodInfo == targetMethod)) continue;
    //            if (codes[i + 1].opcode != OpCodes.Stfld) continue;

    //            var components = codes[i + 1].operand;
    //            codes.Insert(i + 2, new CodeInstruction(OpCodes.Ldarg_0));
    //            codes.Insert(i + 3, new CodeInstruction(OpCodes.Ldloc_1));
    //            codes.Insert(i + 4, new CodeInstruction(OpCodes.Ldarg_0));
    //            codes.Insert(i + 5, new CodeInstruction(OpCodes.Ldfld, components));
    //            codes.Insert(i + 6, new CodeInstruction(OpCodes.Call, typeof(WorldSetup_InitCoroutine).GetMethod(nameof(ModifyComponents))));
    //            codes.Insert(i + 7, new CodeInstruction(OpCodes.Stfld, components));
    //            break;
    //        }
    //        return codes;
    //    }

    //    public static ProceduralComponent[] ModifyComponents(ProceduralComponent[] components) {
    //        FileLog.Log("components:");
    //        foreach (var comp in components) {
    //            FileLog.Log(comp.GetType().Name);
    //        }
    //        return components;
    //    }
    //}

}
