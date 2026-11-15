using CustomGenerator.Utility;
using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

using static CustomGenerator.ExtConfig;
namespace CustomGenerator.Patches
{
    // "Decor density": more or fewer icebergs, rocks, ores, trees... by folder. The decor placers spread their prefabs
    // by ObjectDensity (per km², white/value noise) or ObjectDistance (grid step, uniform).
    [HarmonyPatch]
    internal static class DecorDensity
    {
        private static readonly string[] Types = { "PlaceDecorUniform", "PlaceDecorWhiteNoise", "PlaceDecorValueNoise" };

        private static IEnumerable<MethodBase> TargetMethods() {
            foreach (var name in Types) {
                var type = AccessTools.TypeByName(name);
                var method = type == null ? null : AccessTools.Method(type, "Process");
                if (method != null) yield return method;
            }
        }

        private static bool Prefix(ProceduralComponent __instance) {
            var rules = Config.Generator.DecorDensity;
            if (rules == null || rules.Count == 0) return true;
            var t = Traverse.Create(__instance);
            string folder = t.Field("ResourceFolder").GetValue<string>() ?? "";
            var rule = rules.FirstOrDefault(x => !string.IsNullOrEmpty(x.Key) && folder.IndexOf(x.Key, System.StringComparison.OrdinalIgnoreCase) >= 0);
            if (rule.Key == null || Mathf.Approximately(rule.Value, 1f)) return true;
            if (rule.Value <= 0f) { Logging.Generation($"{__instance.Description} ({folder}): density 0, skipped"); return false; }

            var density = t.Field("ObjectDensity");
            var distance = t.Field("ObjectDistance");
            if (density.FieldExists()) density.SetValue(density.GetValue<float>() * rule.Value);
            // Uniform places one per grid cell: x times as many objects is a step sqrt(x) times smaller
            else if (distance.FieldExists()) distance.SetValue(distance.GetValue<float>() / Mathf.Sqrt(rule.Value));
            Logging.Generation($"{__instance.Description} ({folder}): density x{rule.Value}");
            return true;
        }
    }
}
