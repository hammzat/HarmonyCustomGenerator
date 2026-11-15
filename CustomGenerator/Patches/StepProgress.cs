using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace CustomGenerator.Patches
{
    // For the launcher's progress bar: "[CGen] Step: <name>" when each generation step starts.
    // Rust only prints "[1.2s] Name" for slow steps, so those lines can't be counted.
    // The total ("Generation steps: N") is logged by WorldSetup_InitCoroutine.
    [HarmonyPatch]
    internal static class StepProgress
    {
        private static IEnumerable<MethodBase> TargetMethods() {
            return typeof(ProceduralComponent).Assembly.GetTypes()
                .Where(t => typeof(ProceduralComponent).IsAssignableFrom(t))
                .Select(t => t.GetMethod("Process", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly, null, new[] { typeof(uint) }, null))
                .Where(m => m != null && !m.IsAbstract)
                .Cast<MethodBase>();
        }

        // An override calling base.Process would log the same step twice
        private static ProceduralComponent _last;

        [HarmonyPriority(Priority.First)]
        private static void Prefix(ProceduralComponent __instance) {
            if (ReferenceEquals(__instance, _last)) return;
            _last = __instance;
            Debug.Log($"[CGen] Step: {__instance.Description}");
        }

        public static int Total() {
            return SingletonComponent<WorldSetup>.Instance.GetComponentsInChildren<ProceduralComponent>(true).Count(c => c && c.ShouldRun());
        }
    }
}
