using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CustomGenerator.Shared;
using HarmonyLib;
using UnityEngine;

namespace CustomGenerator.Server
{
    // Connects IO entities placed in RustEdit monuments and applies their settings. The entities themselves are spawned
    // by the game from the map's prefabs; they're found by prefab and position. Safe to run on every start:
    // connections that already exist (e.g. loaded from the save) are left as they are.
    internal static class IOModule
    {
        private const float MatchDistance = 1f;

        private static readonly FieldInfo CounterTarget = AccessTools.Field(typeof(PowerCounter), "targetCounterNumber");

        public static void Apply(List<IOEntityInfo> io) {
            if (io == null || io.Count == 0) return;

            var index = BuildIndex(io);
            var found = new List<(IOEntityInfo Info, BaseEntity Entity)>();
            int missing = 0;
            foreach (var info in io) {
                var entity = Find(index, info.Prefab, info.Position);
                if (entity == null) {
                    if (missing++ < 5) Log.Warning($"IO: no {info.Prefab} at {Format(info.Position)}");
                    continue;
                }
                found.Add((info, entity));
                ApplySettings(entity, info);
            }

            int connected = 0, existing = 0, failed = 0;
            var touched = new HashSet<IOEntity>();
            foreach (var (info, entity) in found) {
                if (!(entity is IOEntity source)) continue;
                for (int slot = 0; slot < info.Outputs.Count; slot++) {
                    var link = info.Outputs[slot];
                    if (link == null) continue;
                    var target = Find(index, link.Prefab, link.Position) as IOEntity;
                    // Slot counts come from the prefab at the time the monument was made; a Rust update may have changed them
                    string problem = target == null ? $"{Short(link.Prefab)} not found at {Format(link.Position)}"
                        : slot >= source.outputs.Length ? $"{Short(info.Prefab)} has {source.outputs.Length} outputs now, the monument uses output {slot}"
                        : link.Slot < 0 || link.Slot >= target.inputs.Length ? $"{Short(link.Prefab)} has {target.inputs.Length} inputs now, the monument uses input {link.Slot}"
                        : null;
                    if (problem != null) {
                        if (failed++ < 10) Log.Warning($"IO: can't connect {Short(info.Prefab)} output {slot} to {Short(link.Prefab)} input {link.Slot}: {problem}");
                        continue;
                    }
                    var output = source.outputs[slot];
                    var input = target.inputs[link.Slot];
                    if (output.connectedTo.Get() == target && output.connectedToSlot == link.Slot) { existing++; continue; }

                    output.connectedTo.Set(target);
                    output.connectedToSlot = link.Slot;
                    output.connectedTo.Init();
                    input.connectedTo.Set(source);
                    input.connectedToSlot = slot;
                    input.connectedTo.Init();
                    touched.Add(source);
                    touched.Add(target);
                    connected++;
                }
            }
            foreach (var entity in touched) {
                entity.MarkDirtyForceUpdateOutputs();
                entity.SendNetworkUpdate();
            }

            Log.Info($"IO: {found.Count}/{io.Count} entities found, {connected} connections made" +
                     (existing > 0 ? $", {existing} already connected" : "") + (failed > 0 ? $", {failed} failed" : "") + (missing > 0 ? $", {missing} not found" : ""));

            // Power settles over a few ticks; a quick check that the circuits actually work
            var entities = found.Select(x => x.Entity).OfType<IOEntity>().ToList();
            ServerMgr.Instance.Invoke(() => {
                int powered = entities.Count(x => x != null && !x.IsDestroyed && x.IsPowered());
                Log.Info($"IO: {powered}/{entities.Count} entities have power");
            }, 5f);
        }

        // Every spawned entity of the prefabs we need, by prefab path
        private static Dictionary<string, List<BaseEntity>> BuildIndex(List<IOEntityInfo> io) {
            var prefabs = new HashSet<string>(io.Select(x => x.Prefab)
                .Concat(io.SelectMany(x => x.Inputs.Concat(x.Outputs)).Where(x => x != null).Select(x => x.Prefab)));
            var index = prefabs.ToDictionary(x => x, x => new List<BaseEntity>());
            foreach (var networkable in BaseNetworkable.serverEntities) {
                if (networkable is BaseEntity entity && index.TryGetValue(entity.PrefabName, out var list)) list.Add(entity);
            }
            return index;
        }

        private static BaseEntity Find(Dictionary<string, List<BaseEntity>> index, string prefab, float[] position) {
            if (prefab == null || position == null || !index.TryGetValue(prefab, out var list)) return null;
            var point = new Vector3(position[0], position[1], position[2]);
            BaseEntity best = null;
            float bestDistance = MatchDistance * MatchDistance;
            foreach (var entity in list) {
                float distance = (entity.transform.position - point).sqrMagnitude;
                if (distance <= bestDistance) { best = entity; bestDistance = distance; }
            }
            return best;
        }

        private static void ApplySettings(BaseEntity entity, IOEntityInfo info) {
            switch (entity) {
                case TimerSwitch timer when info.TimerLength > 0f:
                    timer.timerLength = info.TimerLength;
                    break;
                case RFBroadcaster broadcaster when info.Frequency > 0:
                    broadcaster.SetFrequency(info.Frequency);
                    break;
                case RFReceiver receiver when info.Frequency > 0:
                    receiver.SetFrequency(info.Frequency);
                    break;
                case ElectricalBranch branch when info.BranchAmount > 0:
                    branch.branchAmount = info.BranchAmount;
                    break;
                case PowerCounter counter:
                    if (info.TargetCounterNumber > 0) CounterTarget?.SetValue(counter, info.TargetCounterNumber);
                    counter.SetFlagLocal(PowerCounter.Flag_ShowPassthrough, info.CounterPassthrough);
                    break;
                case CardReader reader when info.AccessLevel > 0:
                    reader.accessLevel = info.AccessLevel;
                    reader.SetFlagLocal(reader.AccessLevel1, info.AccessLevel == 1);
                    reader.SetFlagLocal(reader.AccessLevel2, info.AccessLevel == 2);
                    reader.SetFlagLocal(reader.AccessLevel3, info.AccessLevel == 3);
                    break;
            }

            if (!string.IsNullOrEmpty(info.RcIdentifier))
                AccessTools.Method(entity.GetType(), "UpdateIdentifier", new[] { typeof(string), typeof(bool) })?.Invoke(entity, new object[] { info.RcIdentifier, false });
            if (!string.IsNullOrEmpty(info.PhoneName)) {
                var controller = entity.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(x => x.FieldType == typeof(PhoneController))?.GetValue(entity) as PhoneController;
                if (controller != null) controller.PhoneName = info.PhoneName;
            }
            if (info.PeaceKeeper) AccessTools.Method(entity.GetType(), "SetPeacekeepermode", new[] { typeof(bool) })?.Invoke(entity, new object[] { true });

            entity.SendNetworkUpdate();
        }

        private static string Short(string prefab) => prefab == null ? "?" : System.IO.Path.GetFileNameWithoutExtension(prefab);
        private static string Format(float[] p) => p == null ? "?" : $"{p[0]:0.0}, {p[1]:0.0}, {p[2]:0.0}";
    }
}
