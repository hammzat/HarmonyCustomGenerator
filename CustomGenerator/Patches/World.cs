using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using UnityEngine;

using static CustomGenerator.ExtConfig;
namespace CustomGenerator.Generators
{
    [HarmonyPatch]
    internal static class World_InitSize {
        private static bool needPatch = false;
        private static uint size = 0;
        private static MethodBase TargetMethod() { return AccessTools.Method(typeof(World), nameof(World.InitSize), new Type[] { typeof(uint) }); }

        private static void Prefix(ref uint size) {
            CheckConfig();
            if (!Config.OverallocateSizes) return;
            tempData.mapsize = size;
            Debug.Log("[CGen] Writed size to convars...");

            if (size > 6000U || size < 1000U) { 
                needPatch = true;
                Debug.Log("[CGen - WORLD] Using size bigger or smaller than default limits, rewriting limits...");
            }
        }
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
            CheckConfig();
            List<CodeInstruction> list = instructions.ToList();
            if (!needPatch) return list;
            if (size == 0) {
                Debug.Log("[CGen - WORLD] Size not set!");
                return list;
            }
            for (int i = 0; i < list.Count; i++) {
                if (list[i].opcode == OpCodes.Ldc_I4) {
                    var value = list[i].operand;
                    if (size > 6000U) {
                        if (ulong.TryParse(value.ToString(), out ulong targetSize))
                        {
                            if (targetSize != 6000) continue;

                            list[i].operand = ulong.MinValue;
                            break;
                        }
                    }
                    else if (size < 1000U) {
                        if (ulong.TryParse(value.ToString(), out ulong targetSize))
                        {
                            if (targetSize != 1000) continue;

                            list[i].operand = ulong.MaxValue;
                            break;
                        }
                    }
                }
            }
            return list;
        }
    }

    [HarmonyPatch]
    internal static class World_InitSeed {
        private static MethodBase TargetMethod() { return AccessTools.Method(typeof(World), nameof(World.InitSeed), new Type[] { typeof(uint) }); }
        private static void Prefix(ref uint seed) {
            CheckConfig();
            tempData.mapseed = seed;
            Debug.Log("[CGen] Writed seed to convars...");
        }
    }
}
