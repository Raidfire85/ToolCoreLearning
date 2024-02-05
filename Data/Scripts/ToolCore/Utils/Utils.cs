using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Blocks;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using System.Collections.Generic;
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
}
