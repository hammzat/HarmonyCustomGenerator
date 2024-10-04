using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace CustomGenerator
{
    public class ExtConfig {
        public const bool EN = true;
        public static ConfigData Config;
        public static TempData tempData;
        private static string CurrentVersion = "0.0.7";

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

            [JsonProperty(EN ? "Monuments" : "Монументы")]
            public MonumentSettings Monuments = new MonumentSettings();

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
            public UniqueEnviroment UniqueEnviroment = new UniqueEnviroment();

            [JsonProperty(EN ? "Remove Car Wrecks" : "Удалить разбитые префабы машин около дороги")]
            public bool RemoveCarWrecks = false;
            [JsonProperty(EN ? "Remove Rivers" : "Удалить реки")]
            public bool RemoveRivers = false;
            //[JsonProperty("Remove large powerlines")]
            //public bool RemovePowerlines = false;
            [JsonProperty(EN ? "Remove tunnel entrances" : "Удалить входы в туннели")]
            public bool RemoveTunnelsEntrances = false;

            //[JsonProperty("Remove underground tunnels")]
            //public bool RemoveTunnels = false;

            [JsonProperty(EN ? "Change percentages" : "Изменить проценты")]
            public bool ModifyPercentages = false;
            [JsonProperty(EN ? "Tier Percentages (100 in total)" : "Проценты Тиров (всего 100)")]
            public TierSettings Tier = new TierSettings();
            [JsonProperty(EN ? "Bioms Percentages (100 in total)" : "Проценты Биомов (всего 100)")]
            public BiomSettings Biom = new BiomSettings();
        }

        public class SwapSettings {
            [JsonProperty(EN ? "Enabled" : "Включить")]
            public bool Enabled = false;
            [JsonProperty(EN ? "Save both maps (with swap and without)" : "Сохранить обе карты (с заменой и без)")]
            public bool SaveBothMaps = false;
        }

        public class MonumentSettings
        {
            [JsonProperty(EN ? "Enabled" : "Включить")]
            public bool Enabled = false;
            [JsonProperty(EN ? "MonumentList" : "Лист монументов")] // Place Monuments
            public List<Monument> monuments = new List<Monument>()
            {
                new Monument { Description = "Mountains", Folder = "mountain", distanceDifferent = PlaceMonuments.DistanceMode.Max, distanceSame = PlaceMonuments.DistanceMode.Any, MinDistanceDifferentType = 500, TargetCount = 2, MinWorldSize = 5000 },
                new Monument { Description = "Harbors", Folder = "monument/harbor", distanceDifferent = PlaceMonuments.DistanceMode.Max, distanceSame = PlaceMonuments.DistanceMode.Any, MinDistanceDifferentType = 500 },
                new Monument { Description = "Fishing Villages", Folder = "monument/fishing_village", distanceDifferent = PlaceMonuments.DistanceMode.Max, distanceSame = PlaceMonuments.DistanceMode.Any, MinDistanceDifferentType = 500, MinDistanceSameType = 50, TargetCount = 2 },
                new Monument { Description = "Desert Military", Folder = "monument/military_bases", distanceDifferent = PlaceMonuments.DistanceMode.Max, distanceSame = PlaceMonuments.DistanceMode.Any, MinDistanceDifferentType = 250, MinDistanceSameType = 0, TargetCount = 1 },
                new Monument { Description = "Arctic Bases", Folder = "monument/arctic_bases", distanceDifferent = PlaceMonuments.DistanceMode.Max, distanceSame = PlaceMonuments.DistanceMode.Any, MinDistanceDifferentType = 250, MinDistanceSameType = 0, TargetCount = 1 },
                new Monument { Description = "Main Monuments", Folder = "monument/xlarge,monument/large,monument/medium,monument/small", distanceDifferent = PlaceMonuments.DistanceMode.Max, distanceSame = PlaceMonuments.DistanceMode.Max, MinDistanceDifferentType = 250, MinDistanceSameType = 500 },
                new Monument { Description = "Rail Monuments", Folder = "monument/railside", distanceDifferent = PlaceMonuments.DistanceMode.Any, distanceSame = PlaceMonuments.DistanceMode.Max, MinDistanceDifferentType = 50, MinDistanceSameType = 500 },
                
                new Monument { Description = "Tiny Monuments", Folder = "monument/tiny", distanceDifferent = PlaceMonuments.DistanceMode.Any, distanceSame = PlaceMonuments.DistanceMode.Max, MinDistanceDifferentType = 0, MinDistanceSameType = 500 },
                new Monument { Description = "Swamps", Folder = "monument/swamp", distanceDifferent = PlaceMonuments.DistanceMode.Any, distanceSame = PlaceMonuments.DistanceMode.Max, MinDistanceDifferentType = 0, MinDistanceSameType = 500 },
                new Monument { Description = "Ice Lakes", Folder = "monument/ice_lakes", distanceDifferent = PlaceMonuments.DistanceMode.Any, distanceSame = PlaceMonuments.DistanceMode.Max, MinDistanceDifferentType = 0, MinDistanceSameType = 500 },
                
                new Monument { Description = "Caves", Folder = "monument/cave", distanceDifferent = PlaceMonuments.DistanceMode.Any, distanceSame = PlaceMonuments.DistanceMode.Max, MinDistanceDifferentType = 0, MinDistanceSameType = 250, TargetCount = 10 },
                new Monument { Description = "Underwater Labs", Folder = "monument/underwater_lab", distanceDifferent = PlaceMonuments.DistanceMode.Any, distanceSame = PlaceMonuments.DistanceMode.Max, MinDistanceDifferentType = 250, MinDistanceSameType = 1500, TargetCount = 1 },

                new Monument { Description = "Lighthouses", Folder = "monument/lighthouse", distanceDifferent = PlaceMonuments.DistanceMode.Any, distanceSame = PlaceMonuments.DistanceMode.Max, MinDistanceDifferentType = 100, MinDistanceSameType = 100 },
            };
        }

        public class Monument {
            public bool Generate = true;
            public string Description;
            public string Folder;

            public int MinWorldSize = 0;
            public int TargetCount = 0;

            [JsonConverter(typeof(StringEnumConverter))]
            public PlaceMonuments.DistanceMode distanceSame = PlaceMonuments.DistanceMode.Max;
            public int MinDistanceSameType = 500;

            [JsonConverter(typeof(StringEnumConverter))]
            public PlaceMonuments.DistanceMode distanceDifferent = PlaceMonuments.DistanceMode.Any;
            public int MinDistanceDifferentType = 0;

            public SpawnFilterCfg Filter = new SpawnFilterCfg();
        }
        public class SpawnFilterCfg
        {
            public bool Enabled = false;
            public List<string> SplatType = new List<string>();
            public List<string> BiomeType = new List<string>();
            public List<string> TopologyAny = new List<string>();
            public List<string> TopologyAll = new List<string>();
            public List<string> TopologyNot = new List<string>();
        }
        public class SimplePath {
            public bool Enabled = true;
            public bool GenerateRing = true;
            public bool GenerateSideMonuments = true;
            public bool GenerateSideObjects = false;
        }
        public class UniqueEnviroment {
            public bool ShouldChange = true;
            public bool GenerateOasis = true;
            public bool GenerateCanyons = true;
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