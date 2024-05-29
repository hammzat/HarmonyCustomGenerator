using HarmonyLib;
using System.Reflection;
using UnityEngine;

using static CustomGenerator.ExtConfig;
namespace CustomGenerator {
    [HarmonyPatch(typeof(Bootstrap), "StartupShared")]
    internal static class Bootstrap_StartupShared {
        [HarmonyPrefix]
        private static void Prefix() {
            CheckConfig();

            Debug.Log($"CustomGenerator by [aristocratos]");
            Debug.Log(new string('-', 30) + $"\nUSE ONLY FOR MAP GENERATING! \nDONT USE ON LIVE SERVER!!! \nConfig version: {Config.Version}" + new string('-', 30));

            if (Config.SkipAssetWarmup) {
                ConVar.Global.skipAssetWarmup_crashes = true;
                Debug.Log("[CGen] Skipping asset warmup...");
            }

            Rust.Ai.AiManager.nav_disable = true;
            Rust.Ai.AiManager.nav_wait = false;
        }
    }
}
