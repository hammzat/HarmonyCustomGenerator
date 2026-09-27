using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace CustomGenerator.Launcher
{
    internal sealed class Run { public int Size; public uint Seed; }

    // Runs RustDedicated once per map (the mod generates, saves and quits), one after another.
    // Workspace mode: the launcher's folder keeps the mod's files, the server only gets CustomGenerator.dll for the time of a run.
    internal sealed class Generator
    {
        // Not the default ports, so a live server on the same machine doesn't clash
        private const string Ports = "+server.port 28915 +server.queryport 28916 +rcon.port 28918 +app.port 28919";
        // Its own identity in workspace mode: that folder is removed after the run, if the launcher created it
        private string Identity => Workspace ? "cgen_workspace" : "cgen_launcher";
        private const string ModFile = "CustomGenerator.dll";
        // Next to the DLL in the server's HarmonyMods while it's ours: tells a copy we put there from one installed by hand
        private const string MarkerFile = "CustomGenerator.dll.launcher";

        private readonly string _root;
        private readonly object _lock = new object();
        private readonly Queue<Run> _queue = new Queue<Run>();
        private readonly List<Dictionary<string, object>> _done = new List<Dictionary<string, object>>();
        private Process _process;
        private Run _current;
        private string _extra = "", _log;
        private DateTime _started;
        private bool _stopping;

        public Generator(string root, string server) {
            _root = root;
            // The launcher sits in the server folder: the old way, the mod is installed there and uses that folder
            Workspace = FindExecutable(root) == null;
            ServerDir = Workspace ? server : root;
        }

        private string _serverDir;
        public bool Workspace { get; }
        public string ServerDir { get => _serverDir; set => _serverDir = string.IsNullOrEmpty(value) ? value : LongPath(value); }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern uint GetLongPathName(string shortPath, StringBuilder longPath, uint size);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inherit, int processId);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        // Executable of a process; works for 64-bit processes too, unlike Process.MainModule from a 32-bit process
        private static string ProcessPath(Process process) {
            IntPtr handle = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, process.Id);
            if (handle == IntPtr.Zero) return null;
            try {
                var buffer = new StringBuilder(1024);
                int size = buffer.Capacity;
                return QueryFullProcessImageName(handle, 0, buffer, ref size) ? buffer.ToString() : null;
            } finally { CloseHandle(handle); }
        }

        // Full path with 8.3 short names (like ARISTO~1) expanded, so two paths of the same folder compare equal
        public static string LongPath(string path) {
            string full = Path.GetFullPath(path);
            var buffer = new StringBuilder(1024);
            return GetLongPathName(full, buffer, (uint)buffer.Capacity) > 0 ? buffer.ToString() : full;
        }
        public string Executable => FindExecutable(ServerDir);
        public bool Running { get { lock (_lock) return _process != null; } }

        public static string FindExecutable(string folder) =>
            string.IsNullOrEmpty(folder) ? null : new[] { "RustDedicated.exe", "RustDedicated" }.Select(x => Path.Combine(folder, x)).FirstOrDefault(File.Exists);

        public string Start(List<Run> runs, string extra) {
            lock (_lock) {
                if (_process != null) return "Generation is already running";
                if (Executable == null) return Workspace ? "Set the server folder first: RustDedicated.exe wasn't found in " + (ServerDir ?? "(not set)") : "RustDedicated not found in " + _root;
                string problem = Workspace ? CheckServer() : null;
                if (problem != null) return problem;
                _queue.Clear();
                foreach (var run in runs) _queue.Enqueue(run);
                _done.Clear();
                _extra = extra ?? "";
                _stopping = false;
                return StartNext();
            }
        }

        public void Stop() {
            lock (_lock) {
                _stopping = true;
                _queue.Clear();
                try { _process?.Kill(); } catch { }
            }
        }

        // Workspace mode must never touch a running server or a mod someone installed by hand
        private string CheckServer() {
            if (!File.Exists(Path.Combine(_root, "HarmonyMods", ModFile)))
                return $"HarmonyMods/{ModFile} is missing in the launcher folder {_root}";
            string target = Path.Combine(ServerDir, "HarmonyMods", ModFile);
            if (File.Exists(target) && !File.Exists(Path.Combine(ServerDir, "HarmonyMods", MarkerFile)))
                return $"The server already has {ModFile} in HarmonyMods, installed by hand. Remove it: the launcher puts it there only for the time of a run";
            if (ServerProcessRunning())
                return "A server is running from " + ServerDir + ". Stop it first: the launcher would put the generator into its HarmonyMods";
            return null;
        }

        private bool ServerProcessRunning() {
            string exe = LongPath(Executable);
            foreach (var process in Process.GetProcessesByName("RustDedicated")) {
                string path = ProcessPath(process);
                if (path != null && string.Equals(LongPath(path), exe, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private void InstallMod() {
            string mods = Path.Combine(ServerDir, "HarmonyMods");
            Directory.CreateDirectory(mods);
            bool identityExisted = Directory.Exists(Path.Combine(ServerDir, "server", Identity));
            File.WriteAllLines(Path.Combine(mods, MarkerFile), new[] {
                "Put here by CustomGeneratorLauncher from " + _root + " for a map generation, removed after it.",
                "created-identity=" + (identityExisted ? "no" : "yes"),
            });
            File.Copy(Path.Combine(_root, "HarmonyMods", ModFile), Path.Combine(mods, ModFile), true);
        }

        // Removes our DLL and the run's identity folder from the server; also after a launcher crash (called on start)
        public void Cleanup() {
            if (!Workspace || string.IsNullOrEmpty(ServerDir)) return;
            string mods = Path.Combine(ServerDir, "HarmonyMods");
            string marker = Path.Combine(mods, MarkerFile);
            if (!File.Exists(marker)) return;
            try {
                bool createdIdentity = File.ReadAllLines(marker).Contains("created-identity=yes");
                File.Delete(Path.Combine(mods, ModFile));
                File.Delete(marker);
                // Only a folder this launcher created: never someone's own server identity
                string identity = Path.Combine(ServerDir, "server", Identity);
                if (createdIdentity && Directory.Exists(identity)) Directory.Delete(identity, true);
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Removed {ModFile} from {mods}");
            } catch (Exception ex) {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Can't remove {ModFile} from {mods}: {ex.Message}. Remove it by hand before starting that server!");
            }
        }

        // Called under the lock
        private string StartNext() {
            _current = null;
            if (_queue.Count == 0) return null;
            var run = _queue.Dequeue();
            string logs = Path.Combine(_root, "HarmonyConfig", "logs");
            Directory.CreateDirectory(logs);
            _log = Path.Combine(logs, $"launcher_{run.Size}_{run.Seed}.log");
            try { File.Delete(_log); } catch { }

            string workspace = Workspace ? " -cgen.workspace " + Quote(_root) : "";
            var info = new ProcessStartInfo(Executable,
                $"-batchmode -nographics -logfile {Quote(_log)}{workspace} +server.identity {Identity} +server.level \"Procedural Map\" " +
                $"+server.worldsize {run.Size} +server.seed {run.Seed} {Ports} {_extra}".TrimEnd()) {
                WorkingDirectory = ServerDir, UseShellExecute = false, CreateNoWindow = true,
            };
            try {
                if (Workspace) InstallMod();
                _process = Process.Start(info);
                _process.EnableRaisingEvents = true;
                _process.Exited += (s, e) => OnExit();
                _current = run;
                _started = DateTime.Now;
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Generating {run.Size} / {run.Seed}");
                return null;
            } catch (Exception ex) {
                _process = null;
                Cleanup();
                return "Failed to start RustDedicated: " + ex.Message;
            }
        }

        private void OnExit() {
            lock (_lock) {
                if (_current != null) {
                    int code = 0;
                    try { code = _process.ExitCode; } catch { }
                    _done.Add(new Dictionary<string, object> {
                        ["size"] = _current.Size, ["seed"] = _current.Seed, ["exitCode"] = code, ["stopped"] = _stopping,
                        ["seconds"] = (int)(DateTime.Now - _started).TotalSeconds,
                    });
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Finished {_current.Size} / {_current.Seed} ({(_stopping ? "stopped" : "exit code " + code)})");
                }
                _process = null;
                Cleanup();
                if (!_stopping) StartNext();
            }
        }

        // A path as one command line argument: a trailing backslash would escape the closing quote
        private static string Quote(string path) => "\"" + path.TrimEnd('\\', '/') + "\"";

        public Dictionary<string, object> Status() {
            lock (_lock) {
                return new Dictionary<string, object> {
                    ["running"] = _process != null,
                    ["current"] = _current == null ? null : new Dictionary<string, object> {
                        ["size"] = _current.Size, ["seed"] = _current.Seed, ["seconds"] = (int)(DateTime.Now - _started).TotalSeconds,
                    },
                    ["queued"] = _queue.Select(x => new Dictionary<string, object> { ["size"] = x.Size, ["seed"] = x.Seed }).ToArray(),
                    ["done"] = _done.ToArray(),
                    ["log"] = Tail(_log, 400),
                };
            }
        }

        // Last lines of the server log, which RustDedicated keeps open for writing
        private static string[] Tail(string path, int lines) {
            if (path == null || !File.Exists(path)) return new string[0];
            try {
                using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) {
                    long start = Math.Max(0, file.Length - 256 * 1024);
                    file.Seek(start, SeekOrigin.Begin);
                    var bytes = new byte[file.Length - start];
                    int read = file.Read(bytes, 0, bytes.Length);
                    var all = Encoding.UTF8.GetString(bytes, 0, read).Replace("\r", "").Split('\n');
                    return all.Where(x => x.Trim().Length > 0).Skip(Math.Max(0, all.Length - lines)).ToArray();
                }
            } catch { return new string[0]; }
        }
    }
}
