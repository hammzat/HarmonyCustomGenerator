using CustomGenerator.Utility;
using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

using static CustomGenerator.ExtConfig;
namespace CustomGenerator.Patches
{
    // GenerateRiverLayout keeps at most 2 rivers (3 on maps over 4000, jungle rivers don't count) and keeps them
    // 520 m apart (270400 = 520²). Both are constants in Process: the limit is a local set from 3 or 2, the distance
    // a float literal. The transpiler routes them through the config.
    [HarmonyPatch]
    internal static class RiverCount
    {
        private const float VanillaSpacingSq = 270400f;

        private static MethodBase TargetMethod() => AccessTools.Method(typeof(GenerateRiverLayout), "Process");

        public static int MaxRivers(int vanilla) {
            int max = Config.Generator.MaxRivers;
            return max > 0 ? max : vanilla;
        }

        public static float SpacingSq() {
            float spacing = Config.Generator.RiverSpacing;
            return spacing > 0f ? spacing * spacing : VanillaSpacingSq;
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
            var codes = instructions.ToList();
            var maxRivers = AccessTools.Method(typeof(RiverCount), nameof(MaxRivers));
            var spacing = AccessTools.Method(typeof(RiverCount), nameof(SpacingSq));

            // The limit: the first local stored right after "ldc.i4.3"
            object limit = null;
            for (int i = 0; i < codes.Count - 1 && limit == null; i++)
                if (codes[i].opcode == OpCodes.Ldc_I4_3 && codes[i + 1].IsStloc()) limit = codes[i + 1].operand ?? codes[i + 1].opcode;

            int reads = 0, distances = 0;
            for (int i = 0; i < codes.Count; i++) {
                var code = codes[i];
                if (limit != null && code.IsLdloc() && SameLocal(code, limit)) {
                    codes.Insert(++i, new CodeInstruction(OpCodes.Call, maxRivers));
                    reads++;
                }
                else if (code.opcode == OpCodes.Ldc_R4 && code.operand is float f && f == VanillaSpacingSq) {
                    codes[i] = new CodeInstruction(OpCodes.Call, spacing).MoveLabelsFrom(code);
                    distances++;
                }
            }
            if (reads == 0 || distances == 0)
                Logging.Warning($"River count: the game's river code changed ({reads} limit reads, {distances} distances found), some river settings won't work");
            return codes;
        }

        // A local is an operand (LocalBuilder or index) or, for stloc.0-3/ldloc.0-3, the opcode itself
        private static bool SameLocal(CodeInstruction load, object stored) {
            if (stored is OpCode op) {
                return (op == OpCodes.Stloc_0 && load.opcode == OpCodes.Ldloc_0) || (op == OpCodes.Stloc_1 && load.opcode == OpCodes.Ldloc_1)
                    || (op == OpCodes.Stloc_2 && load.opcode == OpCodes.Ldloc_2) || (op == OpCodes.Stloc_3 && load.opcode == OpCodes.Ldloc_3);
            }
            if (load.operand == null) return false;
            if (stored is LocalBuilder a && load.operand is LocalBuilder b) return a.LocalIndex == b.LocalIndex;
            return Equals(load.operand, stored);
        }
    }
}
