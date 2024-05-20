using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;

using static CustomGenerator.ExtConfig;
namespace CustomGenerator.Generators {

    [HarmonyPatch]
    class GenerateRoadRing_Process
    {
        private static MethodBase TargetMethod() { return AccessTools.Method(typeof(GenerateRoadRing), "Process"); }
        private static AccessTools.FieldRef<GenerateRoadRing, int> MinSize = AccessTools.FieldRefAccess<GenerateRoadRing, int>("MinWorldSize");
        private static void Prefix(GenerateRoadRing __instance, ref int seed) {
            CheckConfig();
            if (!Config.GenerateRoadRing) return;

            MinSize(__instance) = 0;
            Debug.Log($"[CGen - ROAD] MinWorldSize changed to 0!");
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
            List<CodeInstruction> list = instructions.ToList();
            CheckConfig();
            if (!Config.GenerateRoadRing) return list;

            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].opcode == OpCodes.Ldc_I4)
                {
                    var value = list[i].operand;
                    if (ulong.TryParse(value.ToString(), out ulong size)) {
                        if (size != 5000) continue;

                        list[i].operand = 0;
                        break;
                    }
                }
            }
            return list;
        }
    }
    
    [HarmonyPatch]
    class PlaceMonumentsRoadside_Process
    {
        private static MethodBase TargetMethod() { return AccessTools.Method(typeof(PlaceMonumentsRoadside), "Process"); }
        private static AccessTools.FieldRef<PlaceMonumentsRoadside, int> MinSize = AccessTools.FieldRefAccess<PlaceMonumentsRoadside, int>("MinWorldSize");
        private static void Prefix(PlaceMonumentsRoadside __instance, ref int seed) {
            CheckConfig();
            if (Config.GenerateRoadsizeMonuments) return;

            MinSize(__instance) = 99999;
            Debug.Log($"[CGen - ROADside] MinWorldSize changed to 99999!");
        }
    }
}
