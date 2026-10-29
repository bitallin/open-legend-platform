using System;
using System.Collections.Generic;

namespace Legend.ClientCore
{
    public readonly struct InventorySlotView
    {
        public InventorySlotView(string itemId, int count)
        {
            ItemId = itemId;
            Count = count;
        }
        public string ItemId { get; }
        public int Count { get; }
    }

    /// <summary>Simple inventory snapshot for HUD binding (no protobuf dependency).</summary>
    public sealed class InventoryViewModel
    {
        private readonly List<InventorySlotView> _items = new List<InventorySlotView>();
        public IReadOnlyList<InventorySlotView> Items => _items;
        public int Gold { get; private set; }

        public void Replace(IEnumerable<(string ItemId, int Count)> items, int gold)
        {
            _items.Clear();
            foreach (var (id, count) in items)
            {
                if (string.IsNullOrWhiteSpace(id) || count <= 0) continue;
                _items.Add(new InventorySlotView(id, count));
            }
            Gold = Math.Max(0, gold);
        }

        public void SetGold(int gold) => Gold = Math.Max(0, gold);
    }
}
