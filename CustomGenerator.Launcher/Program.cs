using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using CustomGenerator.Shared;

namespace CustomGenerator.Launcher
{
    // Double-click in its own folder (unpacked release) and set the server folder in the editor, or put it next to
    // RustDedicated.exe. Serves the config editor on http://127.0.0.1:<port>/, saves the config and runs map generation
    // from the browser. Listens on loopback only.
    internal static class Program
    {
        public static string Root;
        public static int Port;
        public static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        public static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);
        private static Generator _generator;

        private static int Main(string[] args) {
            // No trailing separator: it would escape the closing quote when the path is passed on a command line
            Root = Generator.LongPath(Arg(args, "--root") ?? AppDomain.CurrentDomain.BaseDirectory).TrimEnd('\\', '/');
            int port = int.TryParse(Arg(args, "--port"), out int p) ? p : 28190;
            string server = Arg(args, "--server");
            _generator = new Generator(Root, server != null ? Path.GetFullPath(server) : LoadSettings().TryGetValue("server", out var s) ? s as string : null);
            if (server != null) SaveSettings(_generator.ServerDir);
            // A copy of the mod left in the server by a crashed launcher must not stay there
            _generator.Cleanup();

            TcpListener listener = null;
            for (int i = 0; i < 20 && listener == null; i++) {
                try { listener = new TcpListener(IPAddress.Loopback, port + i); listener.Start(); Port = port + i; }
                catch (SocketException) { listener = null; }
            }
            if (listener == null) { Console.WriteLine($"No free port in {port}-{port + 19}, use --port"); return 1; }

            string url = $"http://127.0.0.1:{Port}/";
            Console.WriteLine("CustomGenerator launcher");
            Console.WriteLine($"  Files:         {Root}");
            Console.WriteLine(_generator.Executable != null ? $"  Rust server:   {_generator.Executable}" : "  Rust server:   not set - set the server folder in the editor (Generate tab)");
            Console.WriteLine($"  Editor:        {url}");
            Console.WriteLine("Keep this window open while you use the editor. Ctrl+C to quit.");
            if (!args.Contains("--no-browser")) {
                try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
            }

            while (true) {
                var client = listener.AcceptTcpClient();
                ThreadPool.QueueUserWorkItem(_ => Handle(client));
            }
        }

        private static string Arg(string[] args, string name) {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        private static void Handle(TcpClient client) {
            using (client) {
                try {
                    var stream = client.GetStream();
                    stream.ReadTimeout = 15000;
                    var request = Request.Read(stream);
                    if (request == null) return;
                    Response response;
                    try { response = Route(request); }
                    catch (Exception ex) { response = Response.Error(500, ex.Message); }
                    response.Write(stream);
                } catch (IOException) { } catch (SocketException) { }
            }
        }

        private static Response Route(Request request) {
            // Only pages opened from this launcher: blocks DNS rebinding (Host) and other sites posting here (custom header needs CORS)
            string host = request.Header("Host");
            if (host != $"127.0.0.1:{Port}" && host != $"localhost:{Port}") return Response.Error(403, "Bad host");
            if (request.Method != "GET" && request.Header("X-CG") != "1") return Response.Error(403, "Missing X-CG header");

            switch ($"{request.Method} {request.Path}") {
                case "GET /":
                case "GET /index.html":
                    return new Response(200, "text/html; charset=utf-8", Utf8.GetBytes(Editor()));
                case "GET /api/state":
                    return Response.RawJson(State(request.Query("custom")));
                case "GET /api/maps":
                    return Response.RawJson(Maps());
                case "GET /api/files":
                    return Response.RawJson(Json.Serialize(Files(request.Query("custom"))));
                case "PUT /api/config":
                    return SaveConfig(request.Body);
                case "POST /api/generate":
                    return Generate(request.Body);
                case "PUT /api/settings":
                    return SetServer(request.Body);
                case "GET /api/generation":
                    return Response.RawJson(Json.Serialize(_generator.Status()));
                case "POST /api/stop":
                    _generator.Stop();
                    return Response.RawJson("{\"ok\":true}");
                case "POST /api/preview":
                    return PreviewMap(request.Body);
                case "PUT /api/heightmap":
                    return UploadHeightmap(request.Query("name"), request.Body);
                case "GET /api/profiles":
                    return Response.RawJson(Json.Serialize(Profiles()));
                case "GET /api/profile":
                    return LoadProfile(request.Query("name"));
                case "PUT /api/profile":
                    return SaveProfile(request.Query("name"), request.Body);
                case "DELETE /api/profile":
                    return DeleteProfile(request.Query("name"));
            }
            if (request.Method == "GET" && request.Path.StartsWith("/files/")) return ServeFile(Uri.UnescapeDataString(request.Path.Substring(7)));
            return Response.Error(404, "Not found");
        }

        private static string Editor() {
            using (var stream = typeof(Program).Assembly.GetManifestResourceStream("ConfigEditor.html"))
            using (var reader = new StreamReader(stream)) return reader.ReadToEnd();
        }

        private static string ConfigDir => Path.Combine(Root, "HarmonyConfig");
        private static string ConfigPath => Path.Combine(ConfigDir, "CustomGenerator.json");

        // Raw file contents glued together, so values reach the page exactly as the mod wrote them
        private static string State(string customFolder) {
            var json = new StringBuilder("{");
            json.Append("\"schema\":").Append(ReadJson(Path.Combine(ConfigDir, "CustomGenerator.schema.json")));
            json.Append(",\"config\":").Append(ReadJson(ConfigPath));
            json.Append(",\"lastRun\":").Append(ReadJson(Path.Combine(ConfigDir, "CustomGenerator.lastrun.json")));
            json.Append(",\"prefabs\":").Append(ReadJson(Path.Combine(ConfigDir, "CustomGenerator.prefabs.json")));
            json.Append(",\"files\":").Append(Json.Serialize(Files(customFolder)));
            json.Append(",\"generation\":").Append(Json.Serialize(_generator.Status()));
            json.Append(",\"rustFound\":").Append(_generator.Executable != null ? "true" : "false");
            json.Append(",\"launcher\":").Append(Json.Serialize(new Dictionary<string, object> {
                ["workspace"] = _generator.Workspace, ["root"] = Root, ["server"] = _generator.ServerDir,
            }));
            return json.Append('}').ToString();
        }

        // Generated maps for the editor's gallery, newest first: the summary the mod wrote next to each map, raw
        private static string Maps() {
            string folder = Path.Combine(Root, "maps");
            if (!Directory.Exists(folder)) return "[]";
            var items = new List<string>();
            foreach (var map in new DirectoryInfo(folder).GetFiles("*.map").OrderByDescending(x => x.LastWriteTimeUtc)) {
                string name = Path.GetFileNameWithoutExtension(map.Name);
                string image = File.Exists(Path.Combine(Root, "mapimages", name + ".png")) ? "mapimages/" + name + ".png" : null;
                string report = File.Exists(Path.ChangeExtension(map.FullName, ".report.txt")) ? "maps/" + name + ".report.txt" : null;
                items.Add("{\"file\":" + Json.Serialize(map.Name) + ",\"image\":" + Json.Serialize(image) + ",\"report\":" + Json.Serialize(report)
                    + ",\"mb\":" + (map.Length / 1048576.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)
                    + ",\"modified\":" + Json.Serialize(map.LastWriteTime.ToString("yyyy-MM-dd HH:mm"))
                    + ",\"summary\":" + ReadJson(Path.ChangeExtension(map.FullName, ".summary.json")) + "}");
            }
            return "[" + string.Join(",", items) + "]";
        }

        private static string ReadJson(string path) {
            if (!File.Exists(path)) return "null";
            string text = File.ReadAllText(path).TrimStart('﻿');
            try { Json.DeserializeObject(text); return text; } catch { return "null"; }
        }

        private static Dictionary<string, object> Files(string customFolder) => new Dictionary<string, object> {
            ["custom"] = List(customFolder ?? "maps/custom", ".map", ".prefab"),
            ["swap"] = List("maps/prefabs", ".map"),
            ["heightmaps"] = List("heightmaps", HeightmapFile.Extensions),
        };

        private static string[] List(string folder, params string[] extensions) {
            string full = Inside(folder);
            if (full == null || !Directory.Exists(full)) return new string[0];
            return Directory.GetFiles(full).Where(x => extensions.Any(e => x.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
                .Select(Path.GetFileName).OrderBy(x => x).ToArray();
        }

        // Full path of a server-relative path, null if it points outside the server folder
        private static string Inside(string relative) {
            try {
                string full = Path.GetFullPath(Path.Combine(Root, relative));
                return full.StartsWith(Root.TrimEnd('\\', '/') + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? full : null;
            } catch { return null; }
        }

        private static string SettingsPath => Path.Combine(ConfigDir, "launcher.json");

        private static Dictionary<string, object> LoadSettings() {
            try { return Json.DeserializeObject(File.ReadAllText(SettingsPath)) as Dictionary<string, object> ?? new Dictionary<string, object>(); }
            catch { return new Dictionary<string, object>(); }
        }

        private static void SaveSettings(string server) {
            Directory.CreateDirectory(ConfigDir);
            File.WriteAllText(SettingsPath, Json.Serialize(new Dictionary<string, object> { ["server"] = server }), Utf8);
        }

        // Server folder for workspace mode: must have RustDedicated in it
        private static Response SetServer(byte[] body) {
            if (!_generator.Workspace) return Response.Error(400, "The launcher is in the server folder, it always uses that server");
            if (_generator.Running) return Response.Error(409, "Generation is running");
            var data = Json.DeserializeObject(Utf8.GetString(body)) as Dictionary<string, object>;
            string path = (data != null && data.TryGetValue("server", out var v) ? v as string : null)?.Trim().Trim('"');
            if (string.IsNullOrEmpty(path)) return Response.Error(400, "Enter the server folder");
            try { path = Generator.LongPath(path); } catch { return Response.Error(400, "Not a valid path: " + path); }
            if (Generator.FindExecutable(path) == null) return Response.Error(400, "RustDedicated.exe wasn't found in " + path);
            if (string.Equals(path.TrimEnd('\\', '/'), Root.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)) return Response.Error(400, "That's the launcher's own folder");
            _generator.Cleanup();
            _generator.ServerDir = path;
            SaveSettings(path);
            return Response.RawJson("{\"ok\":true}");
        }

        private static Response SaveConfig(byte[] body) {
            if (_generator.Running) return Response.Error(409, "Generation is running, the mod rewrites the config when it starts. Stop it or wait.");
            string text = Utf8.GetString(body);
            try { if (!(Json.DeserializeObject(text) is Dictionary<string, object>)) throw new FormatException("not an object"); }
            catch (Exception ex) { return Response.Error(400, "Invalid JSON: " + ex.Message); }
            Directory.CreateDirectory(ConfigDir);
            if (File.Exists(ConfigPath)) File.Copy(ConfigPath, ConfigPath + ".bak", true);
            File.WriteAllText(ConfigPath, text, Utf8);
            return Response.RawJson("{\"ok\":true}");
        }

        private static Response Generate(byte[] body) {
            var data = Json.DeserializeObject(Utf8.GetString(body)) as Dictionary<string, object>;
            var runs = new List<Run>();
            var list = data != null && data.TryGetValue("runs", out var r) ? r as object[] : null;
            foreach (var item in list ?? new object[0]) {
                if (!(item is Dictionary<string, object> run)) continue;
                int size = Convert.ToInt32(run["size"]);
                long seed = Convert.ToInt64(run["seed"]);
                if (size < 1000 || size > 9000) return Response.Error(400, $"Size {size} must be 1000-9000");
                if (seed < 0 || seed > uint.MaxValue) return Response.Error(400, $"Seed {seed} must be 0-{uint.MaxValue}");
                runs.Add(new Run { Size = size, Seed = (uint)seed });
            }
            if (runs.Count == 0) return Response.Error(400, "Nothing to generate");
            string extra = data.TryGetValue("args", out var a) ? a as string ?? "" : "";
            string error = _generator.Start(runs, extra);
            return error == null ? Response.RawJson("{\"ok\":true}") : Response.Error(409, error);
        }

        // ---------- terrain preview ----------
        private static Response PreviewMap(byte[] body) {
            var data = Json.DeserializeObject(Utf8.GetString(body)) as Dictionary<string, object>;
            if (data == null) return Response.Error(400, "Invalid JSON");
            int size = Convert.ToInt32(data["size"]);
            long seed = Convert.ToInt64(data["seed"]);
            if (size < 1000 || size > 9000) return Response.Error(400, $"Size {size} must be 1000-9000");
            if (seed < 0 || seed > uint.MaxValue) return Response.Error(400, $"Seed {seed} must be 0-{uint.MaxValue}");
            var r = new Preview.Request { Size = size, Seed = (uint)seed };
            // Same normalization as WorldConfig: the parts are scaled to add up to 1
            var tiers = Floats(data, "tiers");
            if (tiers != null && tiers.Length == 3) r.Tiers = Normalize(tiers, new[] { 0f, 1f, 0f });
            var biomes = Floats(data, "biomes");
            if (biomes != null && biomes.Length == 4) r.Biomes = Normalize(biomes, new[] { 0f, 1f, 0f, 0f });
            if (data.TryGetValue("jungle", out var j) && j != null) r.Jungle = Convert.ToSingle(j) / 100f;
            if (data.TryGetValue("shape", out var sh) && sh is Dictionary<string, object> shape) {
                float F(string k, float d) => shape.TryGetValue(k, out var v) && v != null ? Convert.ToSingle(v) : d;
                r.Shape = new ShapeOptions {
                    LandLevel = F("LandLevel", 0f), MountainHeight = F("MountainHeight", 1f), BeachHeight = F("BeachHeight", 0f), ShelfDepth = F("ShelfDepth", 0f),
                    FillInlandSea = shape.TryGetValue("FillInlandSea", out var fill) && fill is bool b && b,
                    IslandCount = (int)F("IslandCount", 0f), IslandSize = F("IslandSize", 180f), IslandDistance = F("IslandDistance", 300f),
                    VolcanoHeight = F("VolcanoHeight", 0f), VolcanoRadius = F("VolcanoRadius", 450f), Seed = r.Seed + 1,
                };
                r.HasShape = true;
                r.Heightmap = shape.TryGetValue("Heightmap", out var hm) ? hm as string : null;
                r.HeightmapMin = F("HeightmapMin", -60f);
                r.HeightmapMax = F("HeightmapMax", 240f);
            }
            try { return Response.RawJson(Json.Serialize(Preview.Render(_generator.ServerDir, Root, r))); }
            catch (Exception ex) when (!(ex is OutOfMemoryException)) { return Response.Error(400, ex.Message); }
        }

        private static float[] Floats(Dictionary<string, object> data, string key) =>
            data.TryGetValue(key, out var v) && v is object[] list ? list.Select(x => x == null ? 0f : Math.Max(0f, Convert.ToSingle(x))).ToArray() : null;

        private static float[] Normalize(float[] values, float[] fallback) {
            float sum = values.Sum();
            return sum > 0f ? values.Select(x => x / sum).ToArray() : fallback;
        }

        // ---------- heightmaps ----------
        private static Response UploadHeightmap(string name, byte[] body) {
            name = Path.GetFileName(name ?? "").Trim();
            if (name.Length == 0 || name.StartsWith(".") || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return Response.Error(400, "Bad file name");
            if (!HeightmapFile.Extensions.Contains(Path.GetExtension(name).ToLowerInvariant())) return Response.Error(400, "Only .png, .raw and .r16 heightmaps");
            HeightmapFile file;
            try { file = HeightmapFile.Read(body, name); } catch (Exception ex) { return Response.Error(400, ex.Message); }
            string folder = Path.Combine(Root, "heightmaps");
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, name), body);
            return Response.RawJson(Json.Serialize(new Dictionary<string, object> {
                ["name"] = name, ["width"] = file.Width, ["height"] = file.Height, ["bits"] = file.Bits, ["files"] = List("heightmaps", HeightmapFile.Extensions),
            }));
        }

        // ---------- profiles: named copies of the config in HarmonyConfig/profiles ----------
        // The editor stores them with code names instead of the localized keys, so a profile works in either language
        private static string ProfileDir => Path.Combine(ConfigDir, "profiles");
        private static readonly System.Text.RegularExpressions.Regex ProfileName = new System.Text.RegularExpressions.Regex(@"^[\p{L}\p{N}][\p{L}\p{N} _\-.()]{0,59}$");

        private static string ProfilePath(string name) {
            name = name?.Trim();
            return name != null && ProfileName.IsMatch(name) && !name.EndsWith(".") ? Path.Combine(ProfileDir, name + ".json") : null;
        }

        private static List<Dictionary<string, object>> Profiles() {
            var list = new List<Dictionary<string, object>>();
            if (!Directory.Exists(ProfileDir)) return list;
            foreach (var file in new DirectoryInfo(ProfileDir).GetFiles("*.json").OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase))
                list.Add(new Dictionary<string, object> { ["name"] = Path.GetFileNameWithoutExtension(file.Name), ["modified"] = file.LastWriteTime.ToString("yyyy-MM-dd HH:mm") });
            return list;
        }

        private static Response LoadProfile(string name) {
            string path = ProfilePath(name);
            if (path == null || !File.Exists(path)) return Response.Error(404, "No such profile");
            string json = ReadJson(path);
            return json == "null" ? Response.Error(400, "The profile file isn't valid JSON") : Response.RawJson(json);
        }

        private static Response SaveProfile(string name, byte[] body) {
            string path = ProfilePath(name);
            if (path == null) return Response.Error(400, "Use letters, digits, spaces and - _ . ( ) in the name, up to 60");
            string text = Utf8.GetString(body);
            try { if (!(Json.DeserializeObject(text) is Dictionary<string, object>)) throw new FormatException("not an object"); }
            catch (Exception ex) { return Response.Error(400, "Invalid JSON: " + ex.Message); }
            Directory.CreateDirectory(ProfileDir);
            File.WriteAllText(path, text, Utf8);
            return Response.RawJson(Json.Serialize(Profiles()));
        }

        private static Response DeleteProfile(string name) {
            string path = ProfilePath(name);
            if (path == null || !File.Exists(path)) return Response.Error(404, "No such profile");
            File.Delete(path);
            return Response.RawJson(Json.Serialize(Profiles()));
        }

        private static Response ServeFile(string relative) {
            string full = Inside(relative);
            string ext = Path.GetExtension(relative).ToLowerInvariant();
            string type = ext == ".png" ? "image/png" : ext == ".txt" || ext == ".log" ? "text/plain; charset=utf-8" : null;
            bool allowed = relative.StartsWith("maps/") || relative.StartsWith("mapimages/") || relative.StartsWith("HarmonyConfig/logs/");
            if (full == null || type == null || !allowed || !File.Exists(full)) return Response.Error(404, "Not found");
            using (var file = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) {
                var bytes = new byte[file.Length];
                file.Read(bytes, 0, bytes.Length);
                return new Response(200, type, bytes);
            }
        }
    }
}
