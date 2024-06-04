using Newtonsoft.Json;
using System.IO;
using UnityEngine;
using static CustomGenerator.ExtConfig;

namespace CustomGenerator
{
    public class ExtConfig {
        public const bool EN = true;
        public static ConfigData Config;
        public static TempData tempData;
        private static string CurrentVersion = "0.0.4";

        private static readonly string Location = Path.Combine("HarmonyConfig", "CustomGeneratorCFG.json");

        public class ConfigData {
            [JsonProperty(EN ? "Skip Asset Warmup" : "Пропустить Asset Warmup")]
            public bool SkipAssetWarmup = true;

            [JsonProperty(EN ? "Map Settings" : "Настройки Карты")]
            public MapSettings mapSettings = new MapSettings();

            [JsonProperty(EN ? "Main Generator" : "Основной Генератор")]
            public GeneratorSettings Generator = new GeneratorSettings();

            [JsonProperty(EN ? "Swap Monuments" : "Замена Монументов")]
            public SwapSettings Swap = new SwapSettings();

            public string Version = CurrentVersion;
        }
        public class MapSettings {
            [JsonProperty(EN ? "Generate new map everytime" : "Генерировать новую карту каждый раз")]
            public bool GenerateNewMapEverytime = true;
            [JsonProperty(EN ? "Override Map Sizes (9000 not be changed to 6000)" : "Принудительный размер карты (карта 9000 не сменится на 6000)")]
            public bool OverrideSizes = true;
            [JsonProperty(EN ? "Override Map Folder (saves to <Server Root>/maps/)" : "Перезаписать папку с картой")]
            public bool OverrideFolder = true;
            [JsonProperty(EN ? "Override Map Name" : "Перезаписать название карты")]
            public bool OverrideName = true;
            [JsonProperty(EN ? "Map Name ({0} - size, {1} - seed)" : "Название карты ({0} - размер, {1} - сид)")]
            public string MapName = "Map{0}_{1}.CGEN";
        }

        public class GeneratorSettings {
            public SimplePath Road = new SimplePath();
            public SimplePath Rail = new SimplePath();

            //[JsonProperty("Tier Percentages (100 in total)")]
            //public TierSettings Tier = new TierSettings();
            //[JsonProperty("Bioms Percentages (100 in total)")]
            //public BiomSettings Biom = new BiomSettings();

            //[JsonProperty("Remove large powerlines")]
            //public bool RemovePowerlines = false;
            //[JsonProperty("Remove underground tunnels")]
            //public bool RemoveTunnels = false;

        }
        public class SwapSettings {
            [JsonProperty(EN ? "Enabled" : "Включить")]
            public bool Enabled = false;
            [JsonProperty(EN ? "Save both maps (with swap and without)" : "Сохранить обе карты (с заменой и без)")]
            public bool SaveBothMaps = false;
        }

        public class SimplePath {
            public bool Enabled = true;
            public bool GenerateRing = true;
            public bool GenerateSideMonuments = true;
            public bool GenerateSideObjects = false;
        }

        public class TierSettings {
            public float Tier0 = 30f;
            public float Tier1 = 30f;
            public float Tier2 = 40f;
        }

        public class BiomSettings {
            public float Arid = 40f;
            public float Temperate = 15f;
            public float Tundra = 15f;
            public float Arctic = 30f;
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