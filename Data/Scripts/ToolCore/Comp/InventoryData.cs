using Sandbox.Game;
using System.Collections.Concurrent;
using System.Threading;
using ToolCore.Session;
using VRage;
using VRage.Game;
using VRage.Game.Entity;

namespace ToolCore.Comp
{
    internal class InventoryData
    {
        private readonly ToolSession _session;

        internal MyInventory Inventory;

        internal readonly ConcurrentDictionary<uint, InventoryItem> Items = new ConcurrentDictionary<uint, InventoryItem>();

        internal bool Full;
        internal bool Empty;
        internal int LastCheck;

        internal InventoryData(MyInventory inventory, ToolSession session)
        {
            _session = session;

            Inventory = inventory;

            Inventory.InventoryContentChanged += OnContentsChanged;

            var items = Inventory.GetItems();
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var coreItem = _session.InventoryItemPool.Get();

                coreItem.Item = item;
                coreItem.Content = item.Content;
                coreItem.DefId = item.Content.GetId();
                coreItem.Amount = (int)item.Amount;

                Items.TryAdd(item.ItemId, coreItem);
            }
        }

        private void OnContentsChanged(MyInventoryBase _, MyPhysicalInventoryItem item, MyFixedPoint amount)
        {
            InventoryItem cacheItem;
            if (!Items.TryGetValue(item.ItemId, out cacheItem))
            {
                var coreItem = _session.InventoryItemPool.Get();

                coreItem.Item = item;
                coreItem.Content = item.Content;
                coreItem.DefId = item.Content.GetId();
                coreItem.Amount = (int)amount;

                Items.TryAdd(item.ItemId, coreItem);

                return;
            }

            if (cacheItem.Amount + amount > 0)
            {
                cacheItem.Amount += (int)amount;
            }
            else
            {
                InventoryItem removedItem;
                if (Items.TryRemove(item.ItemId, out removedItem))
                    _session.InventoryItemPool.Return(removedItem);
            }
        }

        internal void Close()
        {
            Inventory.InventoryContentChanged -= OnContentsChanged;

            Items.Clear();
        }
    }

    internal class InventoryItem
    {
        private int _amount;
        internal MyObjectBuilder_PhysicalObject Content;
        internal MyPhysicalInventoryItem Item;
        internal MyDefinitionId DefId;

        internal int Amount
        {
            get { return _amount; }
            set { Interlocked.Exchange(ref _amount, value); }
        }
    }
}
