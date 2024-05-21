using HarmonyLib;
using Newtonsoft.Json;
using System;
using System.IO;

namespace CustomGenerator
{
    public class ExtConfig
    {
        public static void CheckConfig() {
            if (Config == null || tempData == null) 
                LoadConfig();
        }
        public static void LoadConfig() {
            tempData = new TempData();

            if (!Directory.Exists("HarmonyConfig")) {
                Directory.CreateDirectory("HarmonyConfig");
            }
            if (!File.Exists(Location)) {
                LoadDefaultConfig();
            }
            else {
                try {
                    Config = JsonConvert.DeserializeObject<ConfigData>(File.ReadAllText(Location));
                }
                catch {
                    LoadDefaultConfig();
                }
            }
        }

        private static void LoadDefaultConfig()
        {
            Config = new ConfigData();
            File.WriteAllText(Location, JsonConvert.SerializeObject(Config));
        }

        public static ConfigData Config;
        public static TempData tempData;
        private static readonly string Location = Path.Combine("HarmonyConfig", "CustomGeneratorCFG.json");

        public class ConfigData {
            public bool SkipAssetWarmup = true; // useless at this time
            public bool OverallocateSizes = true; // Generate maps over 6000 or smaller 1000
            public bool OverallocateFolder = true; // Saves maps to /maps/ in root
            public bool GenerateNewMapEverytime = true;

            public bool GenerateRoadRing = true;
            public bool GenerateRoadsizeMonuments = false;

            public bool GenerateRailRing = false;
            public bool GenerateRailsideMonuments = false;

        }
        public class TempData {
            public uint mapsize = 0;
            public uint mapseed = 0;
            public bool mapGenerated = false;
            public TerrainTexturing terrainTexturing;
        }
    }
}