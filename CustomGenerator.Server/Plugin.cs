using System;
using System.Reflection;
using CustomGenerator.Shared;
using HarmonyLib;
using UnityEngine;

namespace CustomGenerator.Server
{
    // For the live server (not the generation one): applies the data CustomGenerator saved in the map's
    // "customgenerator" layer, which vanilla Rust ignores. IO wiring for now, more modules later.
    internal class ModHooks : IHarmonyModHooks
    {
        void IHarmonyModHooks.OnLoaded(OnHarmonyModLoadedArgs args) => Log.Info("Loaded");
        void IHarmonyModHooks.OnUnloaded(OnHarmonyModUnloadedArgs args) => Log.Info("Unloaded");
    }

    internal static class Log
    {
        public static void Info(string message) => Debug.Log("[CGen Server] " + message);
        public static void Warning(string message) => Debug.LogWarning("[CGen Server] " + message);
    }

    // Keeps our layer when the map is loaded: World.Serialization may be cleared before the server starts
    [HarmonyPatch]
    internal static class WorldSerialization_Load
    {
        internal static byte[] Layer;

        private static MethodBase TargetMethod() => AccessTools.Method(typeof(WorldSerialization), nameof(WorldSerialization.Load), new[] { typeof(string) });

        private static void Postfix(WorldSerialization __instance) {
            var map = __instance.GetMap(MapExtras.LayerName);
            if (map != null) Layer = map.data;
        }
    }

    // Map entities exist once the server opens connections, on a fresh map and after loading a save
    [HarmonyPatch]
    internal static class ServerMgr_OpenConnection
    {
        private static bool _applied;

        private static MethodBase TargetMethod() => AccessTools.Method(typeof(ServerMgr), "OpenConnection");

        private static void Postfix() {
            if (_applied) return;
            _applied = true;

            byte[] data = WorldSerialization_Load.Layer ?? World.Serialization?.GetMap(MapExtras.LayerName)?.data;
            if (data == null) { Log.Info("This map has no CustomGenerator data, nothing to apply"); return; }

            MapExtras extras;
            try { extras = MapExtras.FromBytes(data); }
            catch (Exception ex) { Log.Warning("Can't read the CustomGenerator map data: " + ex.Message); return; }
            if (extras.Version > MapExtras.CurrentVersion)
                Log.Warning($"The map data is version {extras.Version}, this mod knows up to {MapExtras.CurrentVersion}: update CustomGenerator.Server");

            try { IOModule.Apply(extras.IO); }
            catch (Exception ex) { Log.Warning("IO: failed: " + ex); }
        }
    }
}
