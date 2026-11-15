using CustomGenerator.Utility;
using HarmonyLib;
using System.Reflection;

using static CustomGenerator.ExtConfig;
namespace CustomGenerator.Patches
{
    // "No rail link to the metro": when there are surface rails, GenerateDungeonGrid swaps some metro tunnel cells for
    // "upwards" cells that climb to the surface rail. With an empty upwards list nothing gets swapped and the metro
    // stays a closed network underground; the surface rail is untouched.
    [HarmonyPatch]
    internal static class RailLinks
    {
        private static string _upwards;

        [HarmonyPatch(typeof(GenerateDungeonGrid), nameof(GenerateDungeonGrid.Process))]
        private static class Dungeon {
            private static void Prefix(GenerateDungeonGrid __instance) {
                if (!Config.Generator.RemoveRailLinks || __instance.ConnectionType != InfrastructureType.Tunnel || string.IsNullOrEmpty(__instance.UpwardsFolder)) return;
                _upwards = "assets/bundled/prefabs/autospawn/" + __instance.UpwardsFolder;
            }

            private static void Postfix() => _upwards = null;
        }

        private static MethodBase TargetMethod() => AccessTools.Method(typeof(Prefab), "FindPrefabNames");

        private static void Postfix(string strPrefab, ref string[] __result) {
            if (_upwards == null || __result == null || __result.Length == 0 || strPrefab != _upwards) return;
            Logging.Generation($"No rail link to the metro: {__result.Length} surface link pieces left out");
            __result = new string[0];
        }
    }
}
