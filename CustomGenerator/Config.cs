using Newtonsoft.Json;
using System.IO;
using UnityEngine;

namespace CustomGenerator
{
    public class ExtConfig {
        public static ConfigData Config;
        public static TempData tempData;
        private static readonly string Location = Path.Combine("HarmonyConfig", "CustomGeneratorCFG.json");
        private static string CurrentVersion = "0.0.3";

        public class ConfigData {
            [JsonProperty("Skip Asset Warmup")]
            public bool SkipAssetWarmup = false;

            [JsonProperty("Map Settings")]
            public MapSettings mapSettings = new MapSettings();

            public SimplePath Road = new SimplePath();
            public SimplePath Rail = new SimplePath();

            public string Version = CurrentVersion;
        }

        public class SimplePath {
            public bool Enabled = true;
            public bool GenerateRing = true;
            public bool GenerateSideMonuments = true;
            public bool GenerateSideObjects = false;
        }

        public class MapSettings {
            [JsonProperty("Generate new map everytime")]
            public bool GenerateNewMapEverytime = true;
            [JsonProperty("Override Map Sizes (9000 not be changed to 6000)")]
            public bool OverrideSizes = true;
            [JsonProperty("Override Map Folder (saves to <Server Root>/maps/)")]
            public bool OverrideFolder = true;
            [JsonProperty("Override Map Name")]
            public bool OverrideName = true;
            [JsonProperty("Map Name ({0} - size, {1} - seed)")]
            public string MapName = "Map{0}_{1}.CGEN";
        }

        public class TempData {
            public uint mapsize = 0;
            public uint mapseed = 0;
            public bool mapGenerated = false;
            public TerrainTexturing terrainTexturing;
            public TerrainMeta terrainMeta;
            public TerrainPath terrainPath;
        }



        public static void CheckConfig() {
            if (Config == null || tempData == null)
                LoadConfig();
        }
        public static void LoadConfig() {
            tempData = new TempData();

            if (!Directory.Exists("HarmonyConfig")) Directory.CreateDirectory("HarmonyConfig");
            if (!File.Exists(Location))             LoadDefaultConfig();
            else {
                try {
                    Config = JsonConvert.DeserializeObject<ConfigData>(File.ReadAllText(Location), new JsonSerializerSettings() { Formatting = Formatting.Indented });

                    if (Config.Version != CurrentVersion) {
                        Debug.Log("[CGen Config] Version mismatch! \nCreating backup of current configuration and creating new one!");
                        File.WriteAllText(Location + $"{Config.Version}.backup", JsonConvert.SerializeObject(Config, Formatting.Indented));
                        LoadDefaultConfig();
                    }
                } catch {
                    LoadDefaultConfig();
                }
            }
        }

        private static void LoadDefaultConfig() {
            Config = new ConfigData();
            File.WriteAllText(Location, JsonConvert.SerializeObject(Config, Formatting.Indented));
        }
    }
}