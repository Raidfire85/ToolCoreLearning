using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Blocks;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using System.Collections;
using System.Collections.Generic;
using VRage;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace ToolCore.Utils
{
    internal static class Utils
    {
        internal static T CastHax<T>(T typeRef, object castObj) => (T)castObj;

        private static readonly List<MyPhysicalInventoryItem> _tmpItemList = new List<MyPhysicalInventoryItem>();

        internal static void EmptyBlockInventories(MyCubeBlock block, MyInventory toolInventory)
        {
            for (int i = 0; i < block.InventoryCount; i++)
            {
                MyInventory inventory = block.GetInventory(i);
                if (!inventory.Empty())
                {
                    _tmpItemList.AddRange(inventory.GetItems());
                    foreach (var item in _tmpItemList)
                    {
                        MyInventory.Transfer(inventory, toolInventory, item.ItemId, -1, null, false);
                    }
                    _tmpItemList.Clear();
                }
            }
        }

        internal static bool IsWithinWorldLimits(out string failedBlockType, long ownerID, string blockName, int pcuToBuild, int blocksToBuild = 0, int blocksCount = 0, Dictionary<string, int> blocksPerType = null)
        {
            var sessionSettings = MyAPIGateway.Session.SessionSettings;
            var blockLimitsEnabled = sessionSettings.BlockLimitsEnabled;
            failedBlockType = null;
            if (blockLimitsEnabled == MyBlockLimitsEnabledEnum.NONE)
            {
                return true;
            }

            ulong steamId = MyAPIGateway.Players.TryGetSteamId(ownerID);
            if (steamId != 0UL && MyAPIGateway.Session.IsUserAdmin(steamId))
            {
                return MyAPIGateway.Session.IsUserIgnorePCULimit(steamId);
            }

            if (sessionSettings.MaxGridSize != 0 && blocksCount + blocksToBuild > sessionSettings.MaxGridSize)
            {
                return false;
            }

            return true;
        }
    }

    internal static class Extensions
    {
        internal static bool WillBecomeFunctional(this IMySlimBlock slim, float increase)
        {
            var def = slim.BlockDefinition as MyCubeBlockDefinition;

            var current = slim.Integrity > slim.MaxIntegrity * def.CriticalIntegrityRatio;
            if (current) return false;

            var proposed = slim.Integrity + increase * def.IntegrityPointsPerSec > slim.MaxIntegrity * def.CriticalIntegrityRatio;
            return proposed;
        }

        internal static bool TryGetSubpartRecursive(this MyEntity entity, string name, out MyEntitySubpart subpart)
        {
            if (string.IsNullOrEmpty(name))
            {
                subpart = null;
                return false;
            }

            if (entity.TryGetSubpart(name, out subpart))
                return true;

            foreach (var part in entity.Subparts.Values)
            {
                if (TryGetSubpartRecursive(part, name, out subpart))
                    return true;
            }

            return false;
        }

        internal static bool TryGetDummy(this MyEntity entity, string name, out IMyModelDummy dummy, out MyEntity parent)
        {
            parent = entity;

            if (string.IsNullOrEmpty(name))
            {
                dummy = null;
                return false;
            }

            var dummies = new Dictionary<string, IMyModelDummy>();
            (entity as IMyEntity).Model.GetDummies(dummies);
            if (dummies.TryGetValue(name, out dummy))
                return true;

            var parts = entity.Subparts;
            if (parts == null || parts.Count == 0)
                return false;

            foreach (var part in parts.Values)
            {
                if (TryGetDummy(part, name, out dummy, out parent))
                    return true;
            }
            
            return false;
        }

    }

    internal class FastLookupCachingList<T> : IEnumerable<T>, IReadOnlyList<T>, IReadOnlyCollection<T>
    {
        private readonly List<T> _list = new List<T>();

        private readonly List<T> _toAdd = new List<T>();

        private readonly List<T> _toRemove = new List<T>();

        private readonly Dictionary<T, int> _lookup = new Dictionary<T, int>();

        private readonly FastResourceLock _cacheLock = new FastResourceLock();

        private bool _dirty;

        public bool Contains(T item)
        {
            return _lookup.ContainsKey(item);
        }

        public void Add(T item)
        {
            using (_cacheLock.AcquireSharedUsing())
            {
                if (_toRemove.Contains(item))
                {
                    _toRemove.Remove(item);
                    return;
                }

                _toAdd.Add(item);
                _dirty = true;
            }
        }

        public void Remove(T item)
        {
            using (_cacheLock.AcquireSharedUsing())
            {
                if (!_toAdd.Remove(item))
                {
                    _toRemove.Add(item);
                }
            }
            _dirty = true;
        }

        public void RemoveImmediate(T item)
        {
            int index;
            if (_lookup.TryGetValue(item, out index))
            {
                _list.RemoveAtFast(index);
                _lookup.Remove(item);
                if (_list.Count == index)
                    return;

                _lookup[_list[index]] = index;
            }
        }

        public void ApplyChanges()
        {
            if (_dirty)
            {
                _dirty = false;
                ApplyAdditions();
                ApplyRemovals();
            }
        }

        public void ApplyAdditions()
        {
            using (_cacheLock.AcquireSharedUsing())
            {
                var start = _list.Count;
                for (int i = 0; i < _toAdd.Count; i++)
                {
                    var item = _toAdd[i];
                    if (item == null)
                    {
                        Logs.WriteLine("Item null in ApplyAdditions()");
                        continue;
                    }

                    if (_lookup.ContainsKey(item))
                        continue;

                    _list.Add(item);
                    _lookup[item] = start + i;
                }

                _toAdd.Clear();
            }
        }

        public void ApplyRemovals()
        {
            using (_cacheLock.AcquireSharedUsing())
            {
                for (int i = 0; i < _toRemove.Count; i++)
                {
                    var item = _toRemove[i];
                    if (item == null)
                    {
                        Logs.WriteLine("Item null in ApplyRemovals()");
                        continue;
                    }

                    int index;
                    if (!_lookup.TryGetValue(item, out index))
                        continue;

                    _list.RemoveAtFast(index);
                    _lookup.Remove(item);
                    if (_list.Count == index)
                        continue;

                    _lookup[_list[index]] = index;
                }

                _toRemove.Clear();
            }
        }

        public void Clear()
        {
            using ( _cacheLock.AcquireSharedUsing())
            {
                _toAdd.Clear();
                _toRemove.Clear();
            }
            _cacheLock.Dispose();

            _lookup.Clear();
            _list.Clear();
        }

        public T this[int index] => ((IReadOnlyList<T>)_list)[index];

        public int Count => ((IReadOnlyCollection<T>)_list).Count;

        public IEnumerator<T> GetEnumerator()
        {
            return ((IEnumerable<T>)_list).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable)_list).GetEnumerator();
        }
    }

}
