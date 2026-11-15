using System;
using System.Collections.Generic;
using System.Linq;

using static CustomGenerator.ExtConfig;
namespace CustomGenerator.Utility
{
    // "Map Requirements": monuments the map must have. Without them the launcher generates again with
    // another seed (the server quits with RerollExitCode), or the map is kept with a warning.
    internal static class MapRequirements
    {
        // The launcher passes the attempt number, so a plain server start can't reroll (nothing would restart it)
        public const string AttemptArg = "-cgen.attempt";
        public const int RerollExitCode = 3;

        public sealed class Missing { public string Name; public int Need, Have; }

        public sealed class Result {
            public readonly List<Missing> Missing = new List<Missing>();
            public int Attempt, MaxAttempts;
            public bool Reroll;
            public bool Met => Missing.Count == 0;
        }

        public static int? LauncherAttempt {
            get {
                var args = Environment.GetCommandLineArgs();
                int i = Array.IndexOf(args, AttemptArg);
                return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out int n) ? n : (int?)null;
            }
        }

        // null when there are no requirements
        public static Result Check(WorldSummary summary) {
            var cfg = Config.Requirements;
            if (cfg == null || !cfg.Enabled || cfg.Monuments.Count == 0) return null;

            int? attempt = LauncherAttempt;
            var result = new Result { Attempt = attempt ?? 1, MaxAttempts = cfg.MaxAttempts };
            foreach (var rule in cfg.Monuments) {
                int have = summary.Monuments.Where(x => x.Key.IndexOf(rule.Key, StringComparison.OrdinalIgnoreCase) >= 0).Sum(x => x.Value);
                if (have < rule.Value) result.Missing.Add(new Missing { Name = rule.Key, Need = rule.Value, Have = have });
            }
            if (result.Met) {
                Logging.Generation($"Map requirements met on attempt {result.Attempt}");
                return result;
            }

            string missing = Describe(result);
            if (cfg.OnMissing == "Reroll" && attempt == null)
                Logging.Warning($"Map requirements: missing {missing}. Reroll works only with the launcher, the map is kept");
            else if (cfg.OnMissing == "Reroll" && result.Attempt < cfg.MaxAttempts) {
                result.Reroll = true;
                Logging.Generation($"Map requirements: missing {missing}, attempt {result.Attempt}/{cfg.MaxAttempts}, generating again with another seed");
            }
            else if (cfg.OnMissing == "Reroll")
                Logging.Warning($"Map requirements: missing {missing} after {cfg.MaxAttempts} attempts, the last map is kept");
            else
                Logging.Warning($"Map requirements: missing {missing}");
            return result;
        }

        public static string Describe(Result result) =>
            string.Join(", ", result.Missing.Select(x => x.Need > 1 ? $"{x.Name} {x.Have}/{x.Need}" : x.Name));
    }
}
