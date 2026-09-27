using System;
using System.IO;
using System.Linq;

namespace CustomGenerator.Utility
{
    // Folder the mod keeps its files in (config, logs, maps, previews, monuments). The launcher starts the server with
    // -cgen.workspace <its folder>, so nothing but the DLL touches the server; otherwise it's the server folder as before.
    internal static class Paths
    {
        public const string WorkspaceArg = "-cgen.workspace";

        public static readonly string Root = Resolve(out IsWorkspace);
        public static readonly bool IsWorkspace;

        public static string Get(params string[] parts) => Path.Combine(new[] { Root }.Concat(parts).ToArray());

        // A path from the config: relative ones are relative to Root, absolute ones stay
        public static string Resolve(string path) => Path.Combine(Root, path ?? "");

        // Relative to Root with forward slashes, for the editor page and the launcher
        public static string Relative(string path) {
            string full = Path.GetFullPath(path), root = Root.TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            return (full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full.Substring(root.Length) : full).Replace('\\', '/');
        }

        private static string Resolve(out bool workspace) {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, WorkspaceArg);
            workspace = i >= 0 && i + 1 < args.Length && Directory.Exists(args[i + 1]);
            // Logger isn't usable yet (it gets its folder from here)
            if (i >= 0 && !workspace)
                UnityEngine.Debug.LogError($"[CGen] {WorkspaceArg} folder not found: '{(i + 1 < args.Length ? args[i + 1] : "")}'. Files go to the server folder instead");
            return Path.GetFullPath(workspace ? args[i + 1] : ".");
        }
    }
}
