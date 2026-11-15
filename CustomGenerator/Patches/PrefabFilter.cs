using CustomGenerator.Utility;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;

using static CustomGenerator.ExtConfig;
namespace CustomGenerator.Patches
{
    // "Blocked prefabs": prefabs matching a pattern never get on the map. Generators pick their prefabs through
    // Prefab.FindPrefabNames, so a blocked one is taken out of the candidates before it shapes the terrain (a blocked
    // cave leaves no hole). World.AddPrefab catches the few placed by id. Custom monuments and the swap aren't affected.
    internal static class PrefabFilter
    {
        private sealed class Rule { public string Pattern; public Regex Regex; public readonly HashSet<string> Candidates = new HashSet<string>(); public int Placements; }
        private static List<Rule> _rules;

        // Patterns: a full path (assets/....prefab) matches exactly, otherwise a part of the path; * is any text. Case doesn't matter
        private static List<Rule> Rules() {
            if (_rules != null) return _rules;
            _rules = new List<Rule>();
            foreach (var raw in (Config.Generator.BlockedPrefabs ?? new List<string>()).Select(x => x?.Trim()).Where(x => !string.IsNullOrEmpty(x)).Distinct(StringComparer.OrdinalIgnoreCase)) {
                string pattern = raw.Replace('\\', '/');
                string body = string.Join(".*", pattern.Split('*').Select(Regex.Escape));
                bool exact = pattern.StartsWith("assets/", StringComparison.OrdinalIgnoreCase) && pattern.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase);
                _rules.Add(new Rule { Pattern = raw, Regex = new Regex(exact ? "^" + body + "$" : body, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) });
            }
            if (_rules.Count > 0) Logging.Generation($"Blocked prefabs: {string.Join(", ", _rules.Select(x => x.Pattern))}");
            return _rules;
        }

        private static Rule Match(string path) => string.IsNullOrEmpty(path) ? null : Rules().FirstOrDefault(x => x.Regex.IsMatch(path));

        [HarmonyPatch]
        private static class FindPrefabNames {
            private static MethodBase TargetMethod() => AccessTools.Method(typeof(Prefab), "FindPrefabNames");

            // After the group rules and the editor's name list (Monuments.cs), so the config editor still sees every name
            [HarmonyPriority(Priority.Last)]
            private static void Postfix(ref string[] __result) {
                if (__result == null || Rules().Count == 0) return;
                __result = __result.Where(path => {
                    var rule = Match(path);
                    rule?.Candidates.Add(System.IO.Path.GetFileNameWithoutExtension(path));
                    return rule == null;
                }).ToArray();
            }
        }

        [HarmonyPatch]
        private static class AddPrefab {
            private static MethodBase TargetMethod() =>
                AccessTools.Method(typeof(World), nameof(World.AddPrefab), new[] { typeof(string), typeof(Prefab), typeof(Vector3), typeof(Quaternion), typeof(Vector3) });

            private static bool Prefix(Prefab prefab) {
                if (prefab == null || Rules().Count == 0) return true;
                var rule = Match(prefab.Name ?? StringPool.Get(prefab.ID));
                if (rule == null) return true;
                rule.Placements++;
                return false;
            }
        }

        // For the report: per pattern, the prefab names taken out of the candidates and the placements stopped
        public static IEnumerable<(string Pattern, List<string> Names, int Placements)> Results() =>
            (_rules ?? new List<Rule>()).Select(x => (x.Pattern, x.Candidates.OrderBy(n => n).ToList(), x.Placements));
    }
}
