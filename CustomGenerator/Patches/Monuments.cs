using HarmonyLib;
using System;
using System.Reflection;
using UnityEngine;

using static CustomGenerator.ExtConfig;
namespace CustomGenerator.Generators
{

    [HarmonyPatch]
    class Prefab_getParameters
    {
        private static MethodBase TargetMethod() { return AccessTools.Method(typeof(Prefab), nameof(Prefab.Spawn), new Type[] { typeof(Vector3), typeof(Quaternion), typeof(Vector3), typeof(bool) } ); }
        private static void Prefix(Prefab __instance) {
            CheckConfig();
            if (__instance == null) return;
            if (__instance.Name.Contains("/decor/")) return;

            Debug.Log(new string('-', 30));
            Debug.Log($"{__instance.Name} | {__instance.ID}");
            if (__instance.Parameters == null)
                Debug.Log("Parameters null!");
            else Debug.Log(__instance.Parameters.Priority.ToString());

        }
    }
}
