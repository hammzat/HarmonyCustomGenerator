using CustomGenerator.Utility;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.RegularExpressions;
using UnityEngine;

using static CustomGenerator.ExtConfig;
namespace CustomGenerator.Generators
{

    [HarmonyPatch]
    class PlaceMonuments_Process {
        private static AccessTools.FieldRef<PlaceMonuments, PlaceMonuments.DistanceMode> DistanceDifferentType = AccessTools.FieldRefAccess<PlaceMonuments, PlaceMonuments.DistanceMode>("DistanceDifferentType");
        private static AccessTools.FieldRef<PlaceMonuments, PlaceMonuments.DistanceMode> DistanceSameType = AccessTools.FieldRefAccess<PlaceMonuments, PlaceMonuments.DistanceMode>("DistanceSameType");
        private static AccessTools.FieldRef<PlaceMonuments, int> MinWorldSize = AccessTools.FieldRefAccess<PlaceMonuments, int>("MinWorldSize");
        private static AccessTools.FieldRef<PlaceMonuments, int> MinDistanceDifferentType = AccessTools.FieldRefAccess<PlaceMonuments, int>("MinDistanceDifferentType");
        private static AccessTools.FieldRef<PlaceMonuments, int> MinDistanceSameType = AccessTools.FieldRefAccess<PlaceMonuments, int>("MinDistanceSameType");

        private static MethodBase TargetMethod() { return AccessTools.Method(typeof(PlaceMonuments), nameof(PlaceMonuments.Process)); }
        private static bool Prefix(PlaceMonuments __instance) {
            if (Config.Generator.RemoveTunnelsEntrances && __instance.ResourceFolder == "tunnel-entrance")
            {
                Debug.Log("[CGen] Tunnel Entrances off");
                MinWorldSize(__instance) = 999999;
                //return false;
            }
            if (Config.Generator.UniqueEnviroment.ShouldChange && __instance.ResourceFolder.Contains("unique_environment/"))
            {
                switch (__instance.ResourceFolder.Replace("unique_environment/", ""))
                {
                    case "oasis":
                        {
                            Debug.Log($"[UNIQUE ENVIROMENT] Changing generating oasis to {Config.Generator.UniqueEnviroment.GenerateOasis}");
                            if (Config.Generator.UniqueEnviroment.GenerateOasis) MinWorldSize(__instance) = 0;
                            else MinWorldSize(__instance) = 999999;
                            break;
                        }
                    case "canyon":
                        {
                            Debug.Log($"[UNIQUE ENVIROMENT] Changing generating canyon to {Config.Generator.UniqueEnviroment.GenerateCanyons}");
                            if (Config.Generator.UniqueEnviroment.GenerateCanyons) MinWorldSize(__instance) = 0;
                            else MinWorldSize(__instance) = 999999;
                            break;
                        }
                    case "lake":
                        {
                            Debug.Log($"[UNIQUE ENVIROMENT] Changing generating lake to {Config.Generator.UniqueEnviroment.GenerateLakes}");
                            if (Config.Generator.UniqueEnviroment.GenerateLakes) MinWorldSize(__instance) = 0;
                            else MinWorldSize(__instance) = 999999;
                            break;
                        }
                    default: 
                        break;
                }
            }
            if (!Config.Monuments.Enabled) return true;
            Debug.Log("[CGen] PlaceMonuments.");

            var matchMonuments = Config.Monuments.monuments.Where(x => x.Folder == __instance.ResourceFolder);
            if (!matchMonuments.Any()) return true;
            
            var monument = matchMonuments.First();
            if (!monument.Generate) return true;
            Debug.Log(__instance.Description);
            Debug.Log(__instance.ResourceFolder);

            DistanceDifferentType(__instance) = monument.distanceDifferent;
            DistanceSameType(__instance) = monument.distanceSame;
            MinWorldSize(__instance) = monument.MinWorldSize;
            MinDistanceDifferentType(__instance) = monument.MinDistanceDifferentType;
            MinDistanceSameType(__instance) = monument.MinDistanceSameType;

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
            return true;
        }
    }

    [HarmonyPatch]
    class PlaceDecorUniform_Process
    {
        private static MethodBase TargetMethod() { return AccessTools.Method(typeof(PlaceDecorUniform), nameof(PlaceDecorUniform.Process)); }
        private static bool Prefix(PlaceDecorUniform __instance)
        {
            if (!Config.Generator.RemoveCarWrecks) return true;
            if (__instance.Description == "Roadside Wrecks") { Debug.Log("[CGen] Removing wrecks."); return false; }

            return true;
        }
    }

    //[HarmonyPatch]
    //public static class WorldSetup_InitCoroutine
    //{
    //    private static MethodBase TargetMethod() { return AccessTools.Method(AccessTools.Inner(typeof(WorldSetup), "<InitCoroutine>d__19"), "MoveNext"); }
    //    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    //    {
    //        List<CodeInstruction> codes = instructions.ToList();
    //        MethodInfo targetMethod = typeof(Component).GetMethod("GetComponentsInChildren", new[] { typeof(bool) }).MakeGenericMethod(typeof(ProceduralComponent));

    //        for (int i = 0; i < codes.Count; i++)
    //        {
    //            if (!(codes[i].opcode == OpCodes.Call && codes[i].operand as MethodInfo == targetMethod)) continue;
    //            if (codes[i + 1].opcode != OpCodes.Stfld) continue;

    //            var components = codes[i + 1].operand;
    //            var ind = i + 2;
    //            //codes.Insert(ind, new CodeInstruction(OpCodes.Ldarg_0));
    //            //codes.Insert(GetIndex(ref ind, true), new CodeInstruction(OpCodes.Ldloc_1));
    //            //codes.Insert(GetIndex(ref ind, true), new CodeInstruction(OpCodes.Ldarg_0));
    //            codes.Insert(GetIndex(ref ind, false), new CodeInstruction(OpCodes.Ldfld, components));
    //            codes.Insert(GetIndex(ref ind), new CodeInstruction(OpCodes.Call, typeof(WorldSetup_InitCoroutine).GetMethod(nameof(ModifyComponents))));
    //            codes.Insert(GetIndex(ref ind), new CodeInstruction(OpCodes.Stfld, components));
    //            break;
    //        }
    //        return codes;

    //        int GetIndex(ref int ind, bool first = false) { if (first) return ind; ind++; return ind; };
    //    }
    //    public static ProceduralComponent[] ModifyComponents(ProceduralComponent[] components)
    //    {
    //        FileLog.Log("components:");
    //        foreach (var comp in components)
    //        {
    //            FileLog.Log(comp.GetType().Name);
    //        }
    //        return components;
    //    }
    //}

}
