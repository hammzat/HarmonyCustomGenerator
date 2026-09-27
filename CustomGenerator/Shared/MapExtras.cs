using Newtonsoft.Json;
using System.Collections.Generic;
using System.Text;

namespace CustomGenerator.Shared
{
    // Extra map data vanilla Rust doesn't keep (IO wiring now, more sections later), stored as a JSON layer of the .map.
    // CustomGenerator writes it during generation, CustomGenerator.Server applies it on the live server.
    // This file is compiled into both projects.
    public sealed class MapExtras
    {
        public const string LayerName = "customgenerator";
        public const int CurrentVersion = 1;

        public int Version = CurrentVersion;
        public List<IOEntityInfo> IO = new List<IOEntityInfo>();

        [JsonIgnore]
        public bool IsEmpty => IO.Count == 0;

        public static MapExtras FromBytes(byte[] data) =>
            data == null || data.Length == 0 ? new MapExtras() : JsonConvert.DeserializeObject<MapExtras>(Encoding.UTF8.GetString(data)) ?? new MapExtras();

        public byte[] ToBytes() => Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(this, Formatting.None, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Include }));
    }

    // An IO entity placed in RustEdit with its connections and settings, positions in world coordinates of the map.
    // Same fields as RustEdit's IO data, so nothing is lost on the way.
    public sealed class IOEntityInfo
    {
        public string Prefab;
        public float[] Position;
        // Index = slot of this entity; null = the slot isn't connected
        public List<IOConnectionInfo> Inputs = new List<IOConnectionInfo>();
        public List<IOConnectionInfo> Outputs = new List<IOConnectionInfo>();

        public int AccessLevel;
        public int DoorEffect = -1;
        public float TimerLength;
        public int Frequency;
        public bool UnlimitedAmmo;
        public bool PeaceKeeper;
        public string AutoTurretWeapon;
        public int BranchAmount;
        public int TargetCounterNumber;
        public string RcIdentifier;
        public bool CounterPassthrough;
        public int Floors = 1;
        public string PhoneName;
    }

    // The other end of a connection: its prefab, position and slot there
    public sealed class IOConnectionInfo
    {
        public string Prefab;
        public float[] Position;
        public int Slot;
        public int Type;
    }
}
