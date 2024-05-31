using CompanionServer.Handlers;
using ProtoBuf;
using Rust.Ai;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using static ProtoBuf.AppMap;

public class SwapMonument {
    private static WorldSerialization _mainMap = new WorldSerialization();
    private static WorldSerialization _swapMap = new WorldSerialization();
    private static List<Monument> monuments = new List<Monument>();
    
    public static void Initiate(string mapPath) {
        _mainMap.Load(mapPath);
        Log(_mainMap.world.prefabs.Count);

        LoadMonuments();
        _mainMap.world.prefabs.Where(x => );
        foreach (Monument monument in monuments) {
            Log(monument.prefabShortname.ToString());
            Log(monument.path);
            _swapMap.Load(monument.path);
            _mainMap.world.prefabs.AddRange(
                MapHander.CreatePrefabFromMap(_swapMap.world.prefabs)
            );
        }
        
        _mainMap.Save(mapPath);
    }
    private static void LoadMonuments() {
        if (!Directory.Exists("maps/prefabs")) Directory.CreateDirectory("maps/prefabs");

        string[] files = Directory.GetFiles("maps/prefabs");
        foreach (string file in files) {
            if (!Path.GetFileName(file).EndsWith(".map")) continue;

            string prefabShortname = Path.GetFileNameWithoutExtension(file);
            monuments.Add(new Monument(prefabShortname, file));
        }
    }

    class Monument {
        public string prefabShortname;
        public string path;

        public Monument(string prefabShortname, string path) {
            this.prefabShortname = prefabShortname;
            this.path = path;
        }
    }

    static void Log(object obj) => Debug.Log("[SWAP MONUMENT] " + obj);
}

public class MapHander
{
    private static PrefabData CreatePrefab(uint PrefabID, VectorData posistion, VectorData rotation, string category = ":\\test black:1:")
    {
        var prefab = new PrefabData() {
            category = category,
            id = PrefabID,
            position = posistion,
            rotation = rotation,
            scale = new VectorData(1, 1, 1)
        };
        return prefab;
    }


    private static VectorData CalculateLocalPos(VectorData placePos, VectorData globalpos)
    {
        VectorData localpos = new VectorData(globalpos.x - placePos.x, globalpos.y - placePos.y, globalpos.z - placePos.z);
        Console.WriteLine($"[LCL] -> X: {localpos.x}, Y: {localpos.y}, Z: {localpos.z}");
        return localpos;
    }

    private static VectorData CalculateGlobalPosition(VectorData startPos, int col, int row, VectorData scale, int meterSize = 1)
    {
        VectorData nextSize;
        float Increment = meterSize * scale.x;

        float nextX = startPos.x + (col - 1) * Increment;      // Работает
        float nextY = startPos.y;                              // Работает
        float nextZ = startPos.z + (row - 1) * Increment;      // Работает

        nextSize = new VectorData(nextX, nextY, nextZ);
        return nextSize;
    }

    public static List<PrefabData> CreatePrefabFromMap(List<PrefabData> prefabs, int col = 1, int row = 1, string category = "Monument")
    {
        List<PrefabData> createdPrefabs = new List<PrefabData>();
        bool first = true;
        VectorData position = new VectorData(20, 100, 0);
        VectorData startPos = new VectorData(0, 100, 0);

        foreach (var prefab in prefabs) {
            if (first) {
                first = false;
                position = CalculateGlobalPosition(startPos, col, row, prefab.scale, 1);
            }
            createdPrefabs.Add(
                CreatePrefab(
                    prefab.id,
                    Calculate(position, prefab.position, prefab.scale, 1, col, row),
                    prefab.rotation,
                    category
            ));
        }
        return createdPrefabs;
    }

    private static VectorData Calculate(VectorData globalpos, VectorData position, VectorData scale, int meterSize, int col = 1, int row = 1, int s = 0) {
        VectorData nextSize;
        VectorData localpos = CalculateLocalPos(new Vector3(0, 100, 0), position);
        float Increment = meterSize * scale.x; 

        float nextX = globalpos.x + localpos.x;
        float nextY = globalpos.y + localpos.y;
        float nextZ = globalpos.z + localpos.z;

        nextSize = new VectorData(nextX, nextY, nextZ);
        return nextSize;
    }
}
