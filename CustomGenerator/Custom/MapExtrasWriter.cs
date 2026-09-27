using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml;
using CustomGenerator.Shared;
using CustomGenerator.Utility;
using ProtoBuf;
using UnityEngine;

namespace CustomGenerator.Custom
{
    // Collects the IO data of custom monuments and swapped monuments and writes it to the map's "customgenerator" layer
    internal static class MapExtrasWriter
    {
        private static readonly string[] TerrainLayers = { "terrain", "height", "splat", "biome", "alpha", "topology", "water", "buildingblocks" };

        // IO of custom monuments, filled during generation, written to World.Serialization before the map is saved
        public static readonly List<IOEntityInfo> GeneratedIO = new List<IOEntityInfo>();

        // RustEdit keeps IO as an XML layer (SerializedIOData). Its layer name is tied to the map it was saved in,
        // so the layer is recognised by its content
        public static List<IOEntityInfo> ReadRustEditIO(WorldSerialization world, string source) {
            var result = new List<IOEntityInfo>();
            foreach (var map in world.world.maps ?? new List<MapData>()) {
                if (TerrainLayers.Contains(map.name) || map.name == MapExtras.LayerName || !IsXml(map.data)) continue;
                try {
                    var xml = new XmlDocument();
                    xml.LoadXml(System.Text.Encoding.UTF8.GetString(map.data).TrimStart('﻿'));
                    if (xml.DocumentElement?.Name != "SerializedIOData") continue;
                    foreach (XmlElement entity in xml.DocumentElement.SelectNodes("entities/SerializedIOEntity"))
                        result.Add(ReadEntity(entity));
                } catch (Exception ex) {
                    Logging.Warning($"{source}: can't read RustEdit layer {map.name}: {ex.Message}");
                }
            }
            return result;
        }

        private static bool IsXml(byte[] data) => data != null && data.Length > 5 && data[0] == '<' && data[1] == '?';

        private static IOEntityInfo ReadEntity(XmlElement e) => new IOEntityInfo {
            Prefab = Text(e, "fullPath"),
            Position = ReadVector(e["position"]),
            Inputs = ReadConnections(e["inputs"]),
            Outputs = ReadConnections(e["outputs"]),
            AccessLevel = Int(e, "accessLevel"),
            DoorEffect = Int(e, "doorEffect", -1),
            TimerLength = Float(e, "timerLength"),
            Frequency = Int(e, "frequency"),
            UnlimitedAmmo = Bool(e, "unlimitedAmmo"),
            PeaceKeeper = Bool(e, "peaceKeeper"),
            AutoTurretWeapon = Text(e, "autoTurretWeapon"),
            BranchAmount = Int(e, "branchAmount"),
            TargetCounterNumber = Int(e, "targetCounterNumber"),
            RcIdentifier = Text(e, "rcIdentifier"),
            CounterPassthrough = Bool(e, "counterPassthrough"),
            Floors = Int(e, "floors", 1),
            PhoneName = Text(e, "phoneName"),
        };

        // Element order is the slot order, xsi:nil entries are unconnected slots
        private static List<IOConnectionInfo> ReadConnections(XmlElement list) {
            var result = new List<IOConnectionInfo>();
            if (list == null) return result;
            foreach (XmlElement c in list.ChildNodes.OfType<XmlElement>()) {
                string path = Text(c, "fullPath");
                result.Add(string.IsNullOrEmpty(path) ? null : new IOConnectionInfo {
                    Prefab = path, Position = ReadVector(c["position"]), Slot = Int(c, "connectedTo"), Type = Int(c, "type"),
                });
            }
            return result;
        }

        private static string Text(XmlElement e, string name) { var t = e[name]?.InnerText; return string.IsNullOrEmpty(t) ? null : t; }
        private static int Int(XmlElement e, string name, int fallback = 0) => int.TryParse(Text(e, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : fallback;
        private static float Float(XmlElement e, string name) => float.TryParse(Text(e, name), NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : 0f;
        private static bool Bool(XmlElement e, string name) => Text(e, name) == "true";
        private static float[] ReadVector(XmlElement v) => v == null ? new float[3] : new[] { Float(v, "x"), Float(v, "y"), Float(v, "z") };

        // Copy of an entity with every position (its own and of its connections) mapped by transform
        public static IOEntityInfo Transform(IOEntityInfo source, Func<Vector3, Vector3> transform) {
            var copy = Newtonsoft.Json.JsonConvert.DeserializeObject<IOEntityInfo>(Newtonsoft.Json.JsonConvert.SerializeObject(source));
            copy.Position = ToArray(transform(ToVector(source.Position)));
            copy.Inputs = source.Inputs.Select(c => MapConnection(c, transform)).ToList();
            copy.Outputs = source.Outputs.Select(c => MapConnection(c, transform)).ToList();
            return copy;
        }

        private static IOConnectionInfo MapConnection(IOConnectionInfo c, Func<Vector3, Vector3> transform) =>
            c == null ? null : new IOConnectionInfo { Prefab = c.Prefab, Position = ToArray(transform(ToVector(c.Position))), Slot = c.Slot, Type = c.Type };

        public static Vector3 ToVector(float[] v) => v == null || v.Length < 3 ? Vector3.zero : new Vector3(v[0], v[1], v[2]);
        public static float[] ToArray(Vector3 v) => new[] { v.x, v.y, v.z };

        // Adds IO to the map's layer, keeping what's already there (custom monuments written during generation, then the swap)
        public static void Write(WorldSerialization world, IEnumerable<IOEntityInfo> io, string what) {
            var list = io.ToList();
            if (list.Count == 0) return;
            var existing = world.world.maps.FirstOrDefault(x => x.name == MapExtras.LayerName);
            MapExtras extras;
            try { extras = MapExtras.FromBytes(existing?.data); }
            catch (Exception ex) { Logging.Warning($"Map extras layer is broken, rewriting it: {ex.Message}"); extras = new MapExtras(); }
            extras.IO.AddRange(list);
            if (existing != null) world.world.maps.Remove(existing);
            world.world.maps.Add(new MapData { name = MapExtras.LayerName, data = extras.ToBytes() });
            Logging.Info($"{what}: {list.Count} IO entities saved to the map for CustomGenerator.Server ({extras.IO.Count} in total)");
        }
    }
}
