using CustomGenerator.Shared;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace CustomGenerator.Launcher
{
    // Fast terrain preview without starting the server: calls the same native code Rust's GenerateHeight and
    // GenerateBiome call (RustNative.dll of the chosen server), at a lower resolution, then the mod's land shape.
    // Rivers, mountains, canyons and monuments come later in a real generation, so it's the shape of the land only.
    internal static class Preview
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct Vec3 { public float X, Y, Z; public Vec3(float x, float y, float z) { X = x; Y = y; Z = z; } }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void HeightFn(IntPtr map, int res, Vec3 pos, Vec3 size, uint seed, float lootAngle, float tier0, float tier1, float tier2,
            float biomeAngle, float arid, float temperate, float tundra, float arctic);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void BiomeFn(IntPtr map, int res, Vec3 pos, Vec3 size, uint seed, float lootAngle, float tier0, float tier1, float tier2,
            float biomeAngle, float arid, float temperate, float tundra, float arctic, float jungle, IntPtr heightmap, int heightRes);

        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)] private static extern IntPtr LoadLibrary(string path);
        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)] private static extern bool SetDllDirectory(string path);
        [DllImport("kernel32", CharSet = CharSet.Ansi)] private static extern IntPtr GetProcAddress(IntPtr module, string name);

        // Positions of GenerateHeight and GenerateBiome in Rust's list of steps: each step gets World.Seed + its index
        private const uint HeightStep = 1, BiomeStep = 2;
        // Rust's terrain: 1000 m tall from -500, so sea level is at 0.5
        private const float Bottom = -500f, Top = 1000f;
        private const int MaxRes = 1025, BiomeRes = 512;

        private static readonly object Lock = new object();
        private static string _loaded;
        private static HeightFn _height;
        private static BiomeFn _biome;
        // The native heightmap of the last request: sliders of the land shape don't need it again
        private static string _cacheKey;
        private static float[] _cache;

        public sealed class Request
        {
            public int Size;
            public uint Seed;
            public float[] Tiers = { 0.3f, 0.3f, 0.4f };
            public float[] Biomes = { 0.4f, 0.15f, 0.15f, 0.3f };
            public float Jungle = 0.5f;
            public ShapeOptions Shape = new ShapeOptions();
            public bool HasShape;
            public string Heightmap;
            public float HeightmapMin = -60f, HeightmapMax = 240f;
        }

        public static Dictionary<string, object> Render(string serverDir, string root, Request r) {
            var watch = Stopwatch.StartNew();
            int res = Math.Min(MaxRes, ClosestPowerOfTwo(r.Size) + 1);
            float cell = r.Size / (float)res;
            var pos = new Vec3(-r.Size / 2f, Bottom, -r.Size / 2f);
            var size = new Vec3(r.Size, Top, r.Size);
            // TerrainMeta: loot and biome axes from the world seed
            uint s = r.Seed;
            float loot = (int)(Xorshift(ref s) % 4u) * 90;
            int tilt = -45 + (int)(Xorshift(ref s) % 91u);
            int sign = Xorshift(ref s) % 2 != 0 ? -1 : 1;
            float biomeAngle = loot + tilt + sign * 90;

            float[] h;
            string heightmapNote = null;
            lock (Lock) {
                if (!string.IsNullOrWhiteSpace(r.Heightmap)) {
                    string path = HeightmapPath(root, r.Heightmap);
                    if (path == null || !File.Exists(path)) throw new FileNotFoundException("Heightmap not found: " + r.Heightmap);
                    var file = HeightmapFile.Read(File.ReadAllBytes(path), path);
                    h = new float[res * res];
                    file.Fill(h, res, r.HeightmapMin, r.HeightmapMax);
                    heightmapNote = $"{file.Width}x{file.Height}, {file.Bits} bit";
                } else {
                    Load(serverDir);
                    string key = $"{r.Size}/{r.Seed}/{string.Join(",", r.Tiers)}/{string.Join(",", r.Biomes)}";
                    if (key != _cacheKey) {
                        var raw = new short[res * res];
                        var pin = GCHandle.Alloc(raw, GCHandleType.Pinned);
                        try {
                            _height(pin.AddrOfPinnedObject(), res, pos, size, r.Seed + HeightStep, loot, r.Tiers[0], r.Tiers[1], r.Tiers[2],
                                biomeAngle, r.Biomes[0], r.Biomes[1], r.Biomes[2], r.Biomes[3]);
                        } finally { pin.Free(); }
                        _cache = new float[raw.Length];
                        for (int i = 0; i < raw.Length; i++) _cache[i] = Bottom + raw[i] * 3.051944E-05f * Top;
                        _cacheKey = key;
                    }
                    h = (float[])_cache.Clone();
                }
            }

            var shape = r.HasShape ? HeightShape.Apply(h, res, cell, r.Shape) : null;
            byte[] biomes = null;
            lock (Lock) {
                if (_biome != null) {
                    var shorts = new short[h.Length];
                    for (int i = 0; i < h.Length; i++) shorts[i] = (short)(Clamp01((h[i] - Bottom) / Top) * 32766f + 0.5f);
                    biomes = new byte[5 * BiomeRes * BiomeRes];
                    var pinMap = GCHandle.Alloc(biomes, GCHandleType.Pinned);
                    var pinHeight = GCHandle.Alloc(shorts, GCHandleType.Pinned);
                    try {
                        _biome(pinMap.AddrOfPinnedObject(), BiomeRes, pos, size, r.Seed + BiomeStep, loot, r.Tiers[0], r.Tiers[1], r.Tiers[2],
                            biomeAngle, r.Biomes[0], r.Biomes[1], r.Biomes[2], r.Biomes[3], r.Jungle, pinHeight.AddrOfPinnedObject(), res);
                    } finally { pinMap.Free(); pinHeight.Free(); }
                }
            }

            int land = 0;
            foreach (float v in h) if (v > 0f) land++;
            var result = new Dictionary<string, object> {
                ["image"] = "data:image/png;base64," + Convert.ToBase64String(Draw(h, res, cell, biomes, shape)),
                ["land"] = Math.Round(100.0 * land / h.Length, 1),
                ["res"] = res,
                ["ms"] = watch.ElapsedMilliseconds,
                ["heightmap"] = heightmapNote,
            };
            if (shape != null) {
                result["landBefore"] = Math.Round(shape.LandBefore, 1);
                result["islands"] = shape.Islands;
                if (shape.Volcano) result["volcano"] = new Dictionary<string, object> {
                    ["x"] = Math.Round(shape.VolcanoX * r.Size - r.Size / 2f), ["z"] = Math.Round(shape.VolcanoZ * r.Size - r.Size / 2f), ["radius"] = Math.Round(shape.VolcanoRadius),
                };
            }
            return result;
        }

        public static string HeightmapPath(string root, string value) {
            string full;
            try { full = Path.GetFullPath(value.IndexOfAny(new[] { '/', '\\' }) >= 0 || Path.IsPathRooted(value) ? Path.Combine(root, value) : Path.Combine(root, "heightmaps", value)); }
            catch { return null; }
            return full.StartsWith(root.TrimEnd('\\', '/') + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? full : null;
        }

        // A copy of RustNative.dll, so the server's own file isn't locked while the launcher runs (Steam updates it)
        private static void Load(string serverDir) {
            if (string.IsNullOrEmpty(serverDir)) throw new InvalidOperationException("Set the Rust server folder on the Generate tab: the preview uses its RustNative.dll");
            string plugins = Path.Combine(serverDir, "RustDedicated_Data", "Plugins", "x86_64");
            string dll = Path.Combine(plugins, "RustNative.dll");
            if (!File.Exists(dll)) {
                plugins = Path.Combine(serverDir, "RustDedicated_Data", "Plugins");
                dll = Path.Combine(plugins, "RustNative.dll");
            }
            if (!File.Exists(dll)) throw new FileNotFoundException("RustNative.dll wasn't found in the server folder");
            var info = new FileInfo(dll);
            string copy = Path.Combine(Path.GetTempPath(), "CustomGenerator", $"RustNative-{info.Length}-{info.LastWriteTimeUtc.Ticks}.dll");
            if (_loaded == copy) return;
            Directory.CreateDirectory(Path.GetDirectoryName(copy));
            if (!File.Exists(copy)) File.Copy(dll, copy);
            // Its own dependencies (VC runtime) are next to the original
            SetDllDirectory(plugins);
            IntPtr module = LoadLibrary(copy);
            if (module == IntPtr.Zero) throw new InvalidOperationException($"Can't load RustNative.dll (error {Marshal.GetLastWin32Error()})");
            IntPtr height = GetProcAddress(module, "generate_height"), biome = GetProcAddress(module, "generate_biome");
            if (height == IntPtr.Zero) throw new InvalidOperationException("This Rust version's RustNative.dll has no generate_height");
            _height = (HeightFn)Marshal.GetDelegateForFunctionPointer(height, typeof(HeightFn));
            _biome = biome == IntPtr.Zero ? null : (BiomeFn)Marshal.GetDelegateForFunctionPointer(biome, typeof(BiomeFn));
            _loaded = copy;
            _cacheKey = null;
        }

        // Rust map colors, roughly: sea by depth, land by biome and height, lit from the north-west
        private static readonly Color[] BiomeColors = {
            Color.FromArgb(206, 182, 120),  // Arid
            Color.FromArgb(118, 150, 82),   // Temperate
            Color.FromArgb(132, 128, 88),   // Tundra
            Color.FromArgb(226, 233, 238),  // Arctic
            Color.FromArgb(62, 120, 70),    // Jungle
        };

        private static byte[] Draw(float[] h, int res, float cell, byte[] biomes, ShapeResult shape) {
            int side = res - 1;
            // The mod paints the volcano with gravel and bare rock, blended into the ground from 70% of its radius
            bool volcano = shape != null && shape.Volcano;
            float vx = volcano ? shape.VolcanoX * res : 0f, vz = volcano ? shape.VolcanoZ * res : 0f, vr = volcano ? shape.VolcanoRadius / cell : 0f;
            using (var bitmap = new Bitmap(side, side, PixelFormat.Format24bppRgb)) {
                var data = bitmap.LockBits(new Rectangle(0, 0, side, side), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
                var row = new byte[data.Stride];
                for (int py = 0; py < side; py++) {
                    int z = side - 1 - py;  // north up
                    for (int x = 0; x < side; x++) {
                        int i = z * res + x;
                        float v = h[i];
                        Color c;
                        if (v <= 0f) {
                            float t = Clamp01(-v / 60f);
                            c = Mix(Color.FromArgb(96, 170, 190), Color.FromArgb(28, 62, 98), (float)Math.Sqrt(t));
                        } else {
                            c = BiomeColors[1];
                            if (biomes != null) {
                                int bx = Math.Min(BiomeRes - 1, x * BiomeRes / res), bz = Math.Min(BiomeRes - 1, z * BiomeRes / res), best = 0, max = -1;
                                for (int k = 0; k < 5; k++) {
                                    int w = biomes[(k * BiomeRes + bz) * BiomeRes + bx];
                                    if (w > max) { max = w; best = k; }
                                }
                                c = BiomeColors[best];
                            }
                            if (volcano) {
                                float d = (float)Math.Sqrt((x + 0.5f - vx) * (x + 0.5f - vx) + (z + 0.5f - vz) * (z + 0.5f - vz));
                                if (d < vr) c = Mix(c, d < vr * 0.18f ? Color.FromArgb(92, 86, 82) : Color.FromArgb(128, 118, 106), 1f - SmoothStep((d / vr - 0.7f) / 0.3f));
                            }
                            if (v < 2.5f) c = Mix(Color.FromArgb(222, 205, 160), c, v / 2.5f);
                            if (v > 120f) c = Mix(c, Color.FromArgb(140, 134, 124), Clamp01((v - 120f) / 160f) * 0.7f);
                            // Slope towards the light (north-west) brightens, away darkens
                            float dx = h[i + 1] - h[i], dz = h[i + res] - h[i];
                            float light = 1f + Clamp((-dx + dz) / cell * 0.9f, -0.45f, 0.35f);
                            c = Color.FromArgb(Byte(c.R * light), Byte(c.G * light), Byte(c.B * light));
                        }
                        row[x * 3] = c.B; row[x * 3 + 1] = c.G; row[x * 3 + 2] = c.R;
                    }
                    Marshal.Copy(row, 0, data.Scan0 + py * data.Stride, data.Stride);
                }
                bitmap.UnlockBits(data);
                using (var stream = new MemoryStream()) {
                    bitmap.Save(stream, ImageFormat.Png);
                    return stream.ToArray();
                }
            }
        }

        private static Color Mix(Color a, Color b, float t) =>
            Color.FromArgb(Byte(a.R + (b.R - a.R) * t), Byte(a.G + (b.G - a.G) * t), Byte(a.B + (b.B - a.B) * t));
        private static int Byte(float v) => v < 0f ? 0 : v > 255f ? 255 : (int)v;
        private static float SmoothStep(float t) { t = Clamp01(t); return t * t * (3f - 2f * t); }
        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
        private static float Clamp(float v, float min, float max) => v < min ? min : v > max ? max : v;
        private static uint Xorshift(ref uint x) { x ^= x << 13; x ^= x >> 17; x ^= x << 5; return x; }

        // Mathf.ClosestPowerOfTwo, as TerrainGenerator.GetHeightMapRes uses it
        private static int ClosestPowerOfTwo(int v) {
            int lower = 1;
            while (lower * 2 <= v) lower *= 2;
            return v - lower < lower * 2 - v ? lower : lower * 2;
        }
    }
}
