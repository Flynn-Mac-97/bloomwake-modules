using UnityEngine;

namespace Flynn.Feel
{
    /// <summary>
    /// Pure inventory model — no UI, no Unity objects. The rules live here so the view only
    /// animates results. Tool law: tools always land on their pinned tool-bar slot; any move
    /// touching the tool bar is refused (pinned hotkeys never shuffle).
    /// </summary>
    public class HUDInventory
    {
        public class Slot
        {
            public HUDItemDef item;
            public int count;
            public bool IsEmpty => item == null || count <= 0;
            public void Clear() { item = null; count = 0; }
        }

        public enum AddResult  { Refused, Placed, Stacked }
        public enum MoveResult { Refused, Moved, Swapped, Merged }

        public readonly Slot[] Main;
        public readonly Slot[] Tools;

        public HUDInventory(int mainCount, int toolCount)
        {
            Main  = NewSlots(mainCount);
            Tools = NewSlots(toolCount);
        }

        static Slot[] NewSlots(int n)
        {
            var s = new Slot[n];
            for (int i = 0; i < n; i++) s[i] = new Slot();
            return s;
        }

        public Slot Get(bool tool, int index) => (tool ? Tools : Main)[index];

        /// <summary>Pickup. Tools go to their pinned slot; items stack first, then first empty.</summary>
        public AddResult TryAdd(HUDItemDef def, out bool toTool, out int index)
        {
            toTool = def.isTool;
            index = -1;
            if (def.isTool)
            {
                index = Mathf.Clamp(def.pinnedToolSlot, 0, Tools.Length - 1);
                if (!Tools[index].IsEmpty) return AddResult.Refused;   // already carrying it
                Tools[index].item = def;
                Tools[index].count = 1;
                return AddResult.Placed;
            }
            for (int i = 0; i < Main.Length; i++)
                if (!Main[i].IsEmpty && Main[i].item == def && Main[i].count < def.maxStack)
                { Main[i].count++; index = i; return AddResult.Stacked; }
            for (int i = 0; i < Main.Length; i++)
                if (Main[i].IsEmpty)
                { Main[i].item = def; Main[i].count = 1; index = i; return AddResult.Placed; }
            return AddResult.Refused;                                   // pockets full
        }

        /// <summary>Drag between slots. Tool bar is immutable: any move touching it refuses.</summary>
        public MoveResult TryMove(bool fromTool, int from, bool toTool, int to)
        {
            if (fromTool || toTool) return MoveResult.Refused;          // pinned law
            var a = Main[from];
            var b = Main[to];
            if (a.IsEmpty || (fromTool == toTool && from == to)) return MoveResult.Refused;

            if (b.IsEmpty)
            {
                b.item = a.item; b.count = a.count; a.Clear();
                return MoveResult.Moved;
            }
            if (b.item == a.item && b.count < b.item.maxStack)
            {
                int room = b.item.maxStack - b.count;
                int take = Mathf.Min(room, a.count);
                b.count += take; a.count -= take;
                if (a.count <= 0) a.Clear();
                return MoveResult.Merged;
            }
            (a.item, b.item) = (b.item, a.item);
            (a.count, b.count) = (b.count, a.count);
            return MoveResult.Swapped;
        }

        public bool RemoveOne(bool tool, int index, out HUDItemDef def)
        {
            var s = Get(tool, index);
            def = s.item;
            if (s.IsEmpty) return false;
            s.count--;
            if (s.count <= 0) s.Clear();
            return true;
        }

        public bool RemoveAll(bool tool, int index, out HUDItemDef def, out int count)
        {
            var s = Get(tool, index);
            def = s.item; count = s.count;
            if (s.IsEmpty) return false;
            s.Clear();
            return true;
        }
    }
}
