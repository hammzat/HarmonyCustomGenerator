using HarmonyLib;
using UnityEngine;

namespace CustomGenerator {

    //[HarmonyPatch]
    //internal static class Bootstrap_DedicatedServerStartup_p
    //{
    //    private static MethodBase TargetMethod() { return AccessTools.Method(AccessTools.Inner(typeof(Bootstrap), "<DedicatedServerStartup>d__18"), "MoveNext"); }

    //    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    //    {
    //        List<CodeInstruction> list = instructions.ToList<CodeInstruction>();
    //        bool fnd = false;
    //        for (int i = 0; i < list.Count; i++)
    //        {
    //            var code = list[i];
    //            var stringc = code.ToString();
    //            _.Puts(stringc);
    //            if (stringc != null && code.opcode == OpCodes.Call && (stringc.Contains("GetAssetList") || stringc.Contains("FileSystem_Warmup::Run")))
    //            {
    //                code.opcode = OpCodes.Nop;
    //            }
    //            if (fnd && code.opcode == OpCodes.Ldarg_0) {
    //                endIndex = i - 1; break;
    //            }
    //            if (code.opcode == OpCodes.Ldarg_0)
    //                startIndex = i;
    //            if (code.opcode == OpCodes.Ldstr && (code.operand as string) == "Asset Warmup ({0}/{1})")
    //                fnd = true;
    //        }
    //        return list;
    //    }
    //}

    [HarmonyPatch(typeof(Bootstrap), "StartupShared")]
    internal static class Bootstrap_StartupShared {
        [HarmonyPrefix]
        private static void Prefix() {
            Debug.Log($"CustomGenerator by [aristocratos]");
            Debug.Log(new string('-', 30) + "\nUSE ONLY FOR MAP GENERATING! \nDONT USE ON LIVE SERVER!!! \n" + new string('-', 30));
        }
    }

    //[HarmonyPatch(typeof(Bootstrap), "Init_Tier0")]
    //internal static class Bootstrap_Init_Tier0 {
    //    [HarmonyPrefix]
    //    private static void Prefix() {
            
    //    }
    //}
}
