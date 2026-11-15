using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CustomGenerator.Utility
{
    // What ended up on the map: land, biomes, islands, paths, natural features and monuments.
    // Taken once the map is done, for the report, the config editor and the monument requirements.
    internal sealed class WorldSummary
    {
        public float LandPercent;
        public int Islands;
        public readonly Dictionary<string, float> Biomes = new Dictionary<string, float>();
        public int Rivers, Roads, Rails, Powerlines;
        // Natural features and monument classes, in display order
        public readonly List<KeyValuePair<string, int>> Counts = new List<KeyValuePair<string, int>>();
        // Every placed monument prefab by file name (airfield_1, harbor_2, ...) and custom monuments by Name
        public readonly Dictionary<string, int> Monuments = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private const string Autospawn = "assets/bundled/prefabs/autospawn/";

        // Folders under autospawn/ and the class they count as. Large/Small/Tiny are a rough split by the game's folders
        private static readonly (string Folder, string Name)[] FolderClasses = {
            ("monument/xlarge/", "Large monuments"), ("monument/large/", "Large monuments"), ("monument/medium/", "Large monuments"),
            ("monument/harbor/", "Large monuments"), ("monument/military_bases/", "Large monuments"), ("monument/arctic_bases/", "Large monuments"),
            ("monument/offshore/", "Large monuments"),
            ("monument/small/", "Small monuments"), ("monument/roadside/", "Small monuments"), ("monument/railside/", "Small monuments"),
            ("monument/fishing_village/", "Small monuments"), ("monument/lighthouse/", "Small monuments"), ("monument/jungle_ruins/", "Small monuments"),
            ("monument/underwater_lab/", "Small monuments"),
            ("monument/tiny/", "Tiny monuments"),
            ("monument/cave/", "Caves"), ("monument/swamp/", "Swamps"), ("monument/ice_lakes/", "Ice lakes"),
            ("unique_environment/lake/", "Lakes"), ("unique_environment/oasis/", "Oases"), ("unique_environment/canyon/", "Canyons"),
            ("unique_environment/jungle/", "Jungle environments"),
            ("decor/iceberg/", "Icebergs"),
        };
        private static readonly string[] Order = {
            "Large monuments", "Small monuments", "Tiny monuments", "Custom monuments", "Safe zones",
            "Caves", "Mountains", "Lakes", "Ice lakes", "Swamps", "Oases", "Canyons", "Jungle environments", "Icebergs",
        };
        // Monuments that aren't places: caves, lakes and the like are counted above instead of listed
        private static readonly string[] NotMonuments = { "monument/cave/", "monument/swamp/", "monument/ice_lakes/", "monument/maintainables/" };

        private static readonly AccessTools.FieldRef<TerrainPath, List<PathList>> RiversRef = AccessTools.FieldRefAccess<TerrainPath, List<PathList>>("Rivers");
        private static readonly AccessTools.FieldRef<TerrainPath, List<PathList>> RoadsRef = AccessTools.FieldRefAccess<TerrainPath, List<PathList>>("Roads");
        private static readonly AccessTools.FieldRef<TerrainPath, List<PathList>> RailsRef = AccessTools.FieldRefAccess<TerrainPath, List<PathList>>("Rails");
        private static readonly AccessTools.FieldRef<TerrainPath, List<PathList>> PowerlinesRef = AccessTools.FieldRefAccess<TerrainPath, List<PathList>>("Powerlines");
        private static readonly AccessTools.FieldRef<TerrainPath, List<MonumentInfo>> MonumentsRef = AccessTools.FieldRefAccess<TerrainPath, List<MonumentInfo>>("Monuments");

        public static WorldSummary Collect(IEnumerable<string> customMonuments) {
            var summary = new WorldSummary();
            var counts = Order.ToDictionary(x => x, x => 0);
            try { summary.Terrain(); }
            catch (Exception ex) { Logging.Error("World summary: terrain stats failed", ex); }

            var path = TerrainMeta.Path;
            if (path != null) {
                summary.Rivers = RiversRef(path)?.Count ?? 0;
                summary.Roads = RoadsRef(path)?.Count ?? 0;
                summary.Rails = RailsRef(path)?.Count ?? 0;
                summary.Powerlines = PowerlinesRef(path)?.Count ?? 0;
                foreach (var monument in MonumentsRef(path) ?? new List<MonumentInfo>()) {
                    if (monument == null) continue;
                    if (monument.Type == MonumentType.Mountain) counts["Mountains"]++;
                    if (monument.IsSafeZone) counts["Safe zones"]++;
                }
            }

            foreach (var prefab in World.Serialization?.world?.prefabs ?? new List<ProtoBuf.PrefabData>()) {
                string name = (StringPool.Get(prefab.id) ?? "").ToLowerInvariant();
                if (!name.StartsWith(Autospawn)) continue;
                string local = name.Substring(Autospawn.Length);
                foreach (var cls in FolderClasses)
                    if (local.StartsWith(cls.Folder)) { counts[cls.Name]++; break; }
                if (local.StartsWith("monument/") && !NotMonuments.Any(local.StartsWith))
                    summary.Add(System.IO.Path.GetFileNameWithoutExtension(local));
            }

            foreach (var custom in customMonuments) {
                summary.Add(custom);
                counts["Custom monuments"]++;
            }
            summary.Counts.AddRange(Order.Select(x => new KeyValuePair<string, int>(x, counts[x])));
            return summary;
        }

        private void Add(string monument) => Monuments[monument] = Monuments.TryGetValue(monument, out int n) ? n + 1 : 1;

        // Land is above sea level (lakes included). A grid of ~8 m cells, at most 1024 x 1024
        private void Terrain() {
            var heights = TerrainMeta.HeightMap;
            var biomes = TerrainMeta.BiomeMap;
            if (heights == null) return;

            int res = Mathf.Clamp((int)(World.Size / 8f), 256, 1024);
            float cell = World.Size / (float)res;
            var land = new bool[res * res];
            var biomeCells = new Dictionary<int, int>();
            int landCells = 0;
            for (int z = 0; z < res; z++) {
                float nz = (z + 0.5f) / res;
                for (int x = 0; x < res; x++) {
                    float nx = (x + 0.5f) / res;
                    if (heights.GetHeight(nx, nz) <= 0f) continue;
                    land[z * res + x] = true;
                    landCells++;
                    if (biomes == null) continue;
                    int biome = biomes.GetBiomeMaxType(nx, nz);
                    biomeCells[biome] = biomeCells.TryGetValue(biome, out int n) ? n + 1 : 1;
                }
            }

            LandPercent = 100f * landCells / (res * res);
            foreach (var biome in biomeCells.OrderByDescending(x => x.Value))
                Biomes[((TerrainBiome.Enum)biome.Key).ToString()] = landCells == 0 ? 0f : 100f * biome.Value / landCells;

            // Land masses of at least 100 x 100 m; the biggest one is the mainland, the rest are islands
            int minCells = Mathf.Max(4, (int)(10000f / (cell * cell)));
            var masses = LandMasses(land, res).Where(x => x >= minCells).ToList();
            Islands = Math.Max(0, masses.Count - 1);
        }

        private static List<int> LandMasses(bool[] land, int res) {
            var seen = new bool[land.Length];
            var sizes = new List<int>();
            var stack = new Stack<int>();
            for (int start = 0; start < land.Length; start++) {
                if (!land[start] || seen[start]) continue;
                int size = 0;
                seen[start] = true;
                stack.Push(start);
                while (stack.Count > 0) {
                    int i = stack.Pop();
                    size++;
                    int x = i % res, z = i / res;
                    if (x > 0) Visit(i - 1);
                    if (x < res - 1) Visit(i + 1);
                    if (z > 0) Visit(i - res);
                    if (z < res - 1) Visit(i + res);
                }
                sizes.Add(size);
            }
            return sizes;

            void Visit(int i) {
                if (!land[i] || seen[i]) return;
                seen[i] = true;
                stack.Push(i);
            }
        }
    }
}
