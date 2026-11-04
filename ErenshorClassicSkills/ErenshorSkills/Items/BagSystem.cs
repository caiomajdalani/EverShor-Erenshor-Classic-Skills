using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace ErenshorSkills.Items
{
    /// <summary>
    /// EQ-STYLE BAG SYSTEM
    /// Each bag instance gets a permanent unique ID (counter-based).
    /// Moving bags between inventory slots does not lose contents.
    /// Multiple bag windows can be open simultaneously.
    /// Compatible with LootRarity mod (Quantity 4-8 = rarity tiers).
    /// </summary>
    public static class BagSystem
    {
        private static Dictionary<string, List<BagSlotData>> _bags
            = new Dictionary<string, List<BagSlotData>>(StringComparer.OrdinalIgnoreCase);
        private static Dictionary<string, string> _bagNames
            = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static HashSet<int> _reservedBagNumbers = new HashSet<int>();
        private static string _activeTransactionId;
        private static readonly System.Reflection.FieldInfo MouseHomeSlotField =
            typeof(ItemIcon).GetField("MouseHomeSlot",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic);

        private static int _nextBagCounter = 100; // Start at 100 to avoid rarity tier collision
        private const int SaveFormatVersion = 2;
        private const int MaxReasonableItemsPerBag = 1000;
        private const int MaxReasonableStackQuantity = 1000000;
        // Erenshor uses Quantity as equipment quality: vanilla values include
        // 2, 3, and 11-15, and rarity mods use additional values below 100.
        // Bag instance IDs deliberately begin at 100 to keep the domains apart.
        // Equipment quality is an opaque native per-slot value.  Erenshor uses
        // it for blessed/smithed gear and other mods may use values outside the
        // vanilla range, so never normalize or cap a positive value here.
        private const int MaxReasonableItemQuality = int.MaxValue;
        internal const string CorpseBagName = "Corpse Bag";
        internal const string ThrallBagName = "Thrall Bag";
        private const int MaxExactStateLength = 262144;

        /// <summary>Get and reserve the next unused bag number.</summary>
        public static int NextBagNumber()
        {
            for (int attempts = 0;
                attempts < BankBagStorage.GlobalIdentityFloor - 100;
                attempts++)
            {
                if (_nextBagCounter < 100 ||
                    _nextBagCounter >= BankBagStorage.GlobalIdentityFloor)
                    _nextBagCounter = 100;
                int candidate = _nextBagCounter++;
                if (_reservedBagNumbers.Contains(candidate) || _bags.ContainsKey($"bag{candidate}"))
                    continue;
                _reservedBagNumbers.Add(candidate);
                return candidate;
            }
            throw new InvalidOperationException("No free bag identifiers remain.");
        }

        private static readonly Dictionary<string, int> BagDefs
            = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "Silken Pouch", 4 },
            { "Herbalist's Satchel", 6 },
            { "Simple Backpack", 8 },
            { "Adventurer's Backpack", 10 },
            // Thrall Bag capacity is resolved from the player's normal carried
            // inventory at runtime, so it can mirror the character inventory.
            { ThrallBagName, 10 },
            // Recovery containers are seeded internally.  Their visible size
            // expands to the exact number of recovered item records.
            { CorpseBagName, 1 },
        };

        [Serializable]
        public class BagSlotData
        {
            public string ItemId = "";
            public string ItemName = "";
            public int Quantity = 0;
            public bool IsStackable = false;
            // Optional exact-state payload used by the Thrall Bag. Normal bags
            // remain compact. The payload includes modded/upgraded scalar Item
            // fields and moves with a filled bag through the shared bank.
            public string ExactState = "";
        }

        [Serializable]
        public class BagSaveEnvelope
        {
            public int Version = SaveFormatVersion;
            public int NextBagCounter = 100;
            public List<BagSaveRecord> Bags = new List<BagSaveRecord>();
        }

        [Serializable]
        public class BagSaveRecord
        {
            public string Id = "";
            public string Name = "";
            public List<BagSlotData> Items = new List<BagSlotData>();
        }

        [Serializable]
        private class BagTransactionRecord
        {
            public int Version = 2;
            public string TransactionId = "";
            public string Character = "";
            public string Operation = "";
            public string BeforeBags = "";
            public string AfterBags = "";
            public string InventoryBefore = "";
            public string InventoryAfter = "";
            public string BankPath = "";
            public string BankBefore = "";
            public string BankAfter = "";
            public string AuxiliaryPath = "";
            public string AuxiliaryBefore = "";
            public string AuxiliaryAfter = "";
            public string CreatedUtc = "";
        }

        internal sealed class InventorySnapshot
        {
            public readonly List<Item> Items = new List<Item>();
            public readonly List<int> Quantities = new List<int>();
            public int Gold;
            public string Fingerprint = "";
        }

        internal sealed class MutationSnapshot
        {
            internal string BeforeBags;
            internal InventorySnapshot BeforeInventory;
        }

        public static bool IsBag(string n) => BagDefs.ContainsKey(n);
        public static bool IsBag(Item i) => i != null && BagDefs.ContainsKey(i.ItemName);
        public static bool IsCorpseBag(string n) => string.Equals(n,
            CorpseBagName, StringComparison.OrdinalIgnoreCase);
        public static bool IsThrallBag(string n) => string.Equals(n,
            ThrallBagName, StringComparison.OrdinalIgnoreCase);
        public static int GetBagSlots(string n)
        {
            if (IsThrallBag(n))
                return Mathf.Max(1, GetThrallEquipmentSlots().Count);
            return BagDefs.TryGetValue(n, out int s) ? s : 0;
        }

        internal static List<ItemIcon> GetThrallEquipmentSlots()
        {
            var result = new List<ItemIcon>();
            var seen = new HashSet<ItemIcon>();
            Inventory inventory = GameData.PlayerInv;
            if (inventory?.EquipmentSlots != null)
                foreach (ItemIcon slot in inventory.EquipmentSlots)
                    if (slot != null && seen.Add(slot)) result.Add(slot);
            if (inventory?.AuraSlot != null && seen.Add(inventory.AuraSlot))
                result.Add(inventory.AuraSlot);
            if (inventory?.CharmSlot != null && seen.Add(inventory.CharmSlot))
                result.Add(inventory.CharmSlot);
            return result;
        }

        internal static bool IsCorpseBagId(string bagId)
        {
            return !string.IsNullOrEmpty(bagId) &&
                _bagNames.TryGetValue(bagId, out string name) &&
                IsCorpseBag(name);
        }

        internal static bool IsThrallBagId(string bagId)
        {
            return !string.IsNullOrEmpty(bagId) &&
                _bagNames.TryGetValue(bagId, out string name) &&
                IsThrallBag(name);
        }

        internal static bool IsWithdrawalOnly(string bagId, string bagName)
        {
            return IsCorpseBag(bagName) || IsCorpseBagId(bagId);
        }

        /// <summary>
        /// A corpse transaction must never begin while a bag move is between its
        /// journal and commit points.  Waiting one frame is normally sufficient,
        /// but retaining the player's inventory is safer than overlapping two
        /// durable mutations.
        /// </summary>
        internal static bool HasPendingTransaction
        {
            get
            {
                if (!string.IsNullOrEmpty(_activeTransactionId)) return true;
                try
                {
                    string path = GetTransactionPath();
                    return !string.IsNullOrEmpty(path) && File.Exists(path);
                }
                catch { return true; }
            }
        }

        /// <summary>
        /// GBDATA and the bank-bag sidecar are shared by every character. Do not
        /// start another bag-bank move while any character has an interrupted
        /// journal that touches either file; otherwise a later recovery could
        /// overwrite or resurrect the newer move.
        /// </summary>
        internal static bool HasUnresolvedGlobalBankTransaction(
            out string reason)
        {
            reason = null;
            try
            {
                string directory = RuntimePaths.DataDirectory;
                if (string.IsNullOrEmpty(directory) ||
                    !Directory.Exists(directory)) return false;
                foreach (string path in Directory.GetFiles(directory,
                    "*_bags.txn.json", SearchOption.TopDirectoryOnly))
                {
                    if (new FileInfo(path).Length > 16 * 1024 * 1024)
                    {
                        reason = "an oversized add-on transaction journal " +
                            "must be recovered before using banked bags";
                        return true;
                    }
                    BagTransactionRecord record =
                        JsonUtility.FromJson<BagTransactionRecord>(
                            File.ReadAllText(path));
                    if (record == null ||
                        (record.Version != 1 && record.Version != 2))
                    {
                        reason = "an invalid add-on transaction journal must " +
                            "be recovered before using banked bags";
                        return true;
                    }
                    bool touchesBank = IsGlobalBankDataPath(record.BankPath) ||
                        IsSharedBankBagPath(record.AuxiliaryPath);
                    if (!touchesBank) continue;
                    reason = "an interrupted shared-bank transaction for " +
                        $"{(string.IsNullOrEmpty(record.Character)
                            ? "another character" : record.Character)} must " +
                        "recover before banked bags can be moved";
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                reason = "shared-bank transaction journals could not be " +
                    "audited: " + ex.Message;
                return true;
            }
        }

        internal static bool IsDirectInventorySlot(ItemIcon slot)
        {
            var storedSlots = GameData.PlayerInv?.StoredSlots;
            return slot != null && storedSlots != null && storedSlots.Contains(slot) &&
                !slot.BankSlot && !slot.LootSlot && !slot.VendorSlot &&
                !slot.TrashSlot && !slot.MouseSlot && !slot.NotInInventory;
        }

        internal static bool CanMovePhysicalBagOutsideInventory(ItemIcon slot,
            out string reason)
        {
            reason = null;
            if (slot?.MyItem == null || !IsBag(slot.MyItem))
            {
                reason = "the selected item is not a crafted bag";
                return false;
            }
            if (IsCorpseBag(slot.MyItem.ItemName))
            {
                reason = "Corpse Bags are recovery-only and cannot be moved";
                return false;
            }
            if (slot.Quantity >= 100 &&
                _bags.TryGetValue($"bag{slot.Quantity}", out var contents) &&
                contents != null && contents.Count > 0)
            {
                reason = "empty this bag before selling or destroying it";
                return false;
            }
            return true;
        }

        internal static bool CanBankPhysicalBag(ItemIcon slot,
            out string reason)
        {
            reason = null;
            if (HasUnresolvedGlobalBankTransaction(out reason))
                return false;
            if (slot?.MyItem == null || !IsBag(slot.MyItem))
            {
                reason = "the selected item is not a crafted bag";
                return false;
            }
            if (IsCorpseBag(slot.MyItem.ItemName))
            {
                reason = "Corpse Bags are recovery-only and cannot be banked";
                return false;
            }
            if (slot.Quantity >= 100)
            {
                string id = $"bag{slot.Quantity}";
                if (!_bags.TryGetValue(id, out List<BagSlotData> contents) ||
                    contents == null)
                {
                    reason = $"{id} has no validated character-side contents record";
                    return false;
                }
                if (_bagNames.TryGetValue(id, out string savedName) &&
                    !string.IsNullOrEmpty(savedName) &&
                    !string.Equals(savedName, slot.MyItem.ItemName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    reason = $"{id} belongs to {savedName}, not {slot.MyItem.ItemName}";
                    return false;
                }
            }
            return true;
        }

        internal static bool TryRetireEmptyBagIdentity(int number,
            out string reason)
        {
            reason = null;
            if (number < 100) return true;
            string id = $"bag{number}";
            if (_bags.TryGetValue(id, out var contents) && contents != null &&
                contents.Count > 0)
            {
                reason = $"{id} still contains {contents.Count} item record(s)";
                return false;
            }
            _bags.Remove(id);
            _bagNames.Remove(id);
            // Keep the number reserved for this session. A banked or vendor
            // buy-back bag may return before restart, and reusing its former
            // identity would reconnect two physical bags.
            _reservedBagNumbers.Add(number);
            return true;
        }

        internal static bool TryDetachBagForBank(int number, Item bagItem,
            out BagSaveRecord record, out string reason)
        {
            record = null;
            reason = null;
            if (bagItem == null || !IsBag(bagItem))
            {
                reason = "the selected item is not a crafted bag";
                return false;
            }
            if (IsCorpseBag(bagItem.ItemName))
            {
                reason = "Corpse Bags cannot be banked";
                return false;
            }

            if (number < 100)
            {
                record = new BagSaveRecord
                {
                    Name = bagItem.ItemName,
                    Items = new List<BagSlotData>()
                };
                return true;
            }

            string id = $"bag{number}";
            if (!_bags.TryGetValue(id, out List<BagSlotData> contents) ||
                contents == null)
            {
                reason = $"{id} has no validated contents record";
                return false;
            }
            string name = _bagNames.TryGetValue(id, out string savedName) &&
                !string.IsNullOrEmpty(savedName)
                ? savedName : bagItem.ItemName;
            if (!string.Equals(name, bagItem.ItemName,
                StringComparison.OrdinalIgnoreCase))
            {
                reason = $"{id} belongs to {name}, not {bagItem.ItemName}";
                return false;
            }

            record = new BagSaveRecord
            {
                Name = name,
                Items = CloneContents(contents)
            };
            _bags.Remove(id);
            _bagNames.Remove(id);
            _reservedBagNumbers.Add(number);
            return true;
        }

        internal static bool TryAttachBagFromBank(BagSaveRecord record,
            Item bagItem, out int number, out string reason)
        {
            number = -1;
            reason = null;
            if (record == null || bagItem == null || !IsBag(bagItem))
            {
                reason = "the banked bag record is incomplete";
                return false;
            }
            if (IsCorpseBag(bagItem.ItemName) ||
                IsCorpseBag(record.Name))
            {
                reason = "Corpse Bags cannot be withdrawn through the global bank";
                return false;
            }
            if (!string.Equals(record.Name ?? "", bagItem.ItemName ?? "",
                StringComparison.OrdinalIgnoreCase))
            {
                reason = $"bank record '{record.Name}' does not match '{bagItem.ItemName}'";
                return false;
            }

            number = NextBagNumber();
            string id = $"bag{number}";
            _bags[id] = CloneContents(record.Items);
            _bagNames[id] = bagItem.ItemName;
            return true;
        }

        internal static bool HasMatchingLocalBagRecord(int number,
            Item bagItem)
        {
            if (number < 100 || bagItem == null || !IsBag(bagItem))
                return false;
            string id = $"bag{number}";
            if (!_bags.TryGetValue(id, out List<BagSlotData> contents) ||
                contents == null) return false;
            return !_bagNames.TryGetValue(id, out string name) ||
                string.IsNullOrEmpty(name) ||
                string.Equals(name, bagItem.ItemName,
                    StringComparison.OrdinalIgnoreCase);
        }

        internal static bool TryGetBagRecord(int number,
            out BagSaveRecord record)
        {
            record = null;
            if (number < 100) return false;
            string id = $"bag{number}";
            if (!_bags.TryGetValue(id, out List<BagSlotData> contents) ||
                contents == null) return false;
            record = new BagSaveRecord
            {
                Id = id,
                Name = _bagNames.TryGetValue(id, out string name)
                    ? name : "",
                Items = CloneContents(contents)
            };
            return true;
        }

        /// <summary>
        /// Resolve the one physical Thrall Bag carried in direct inventory.
        /// Refusing ambiguous duplicate containers keeps one authoritative pet
        /// loadout and prevents two bags from projecting conflicting gear.
        /// </summary>
        internal static bool TryGetSingleCarriedThrallBag(out string bagId,
            out List<BagSlotData> contents, out string reason)
        {
            bagId = null;
            contents = null;
            reason = null;
            Inventory inventory = GameData.PlayerInv;
            if (inventory?.StoredSlots == null)
            {
                reason = "player inventory is unavailable";
                return false;
            }

            ItemIcon found = null;
            foreach (ItemIcon slot in inventory.StoredSlots)
            {
                if (!IsDirectInventorySlot(slot) || slot.MyItem == null ||
                    slot.MyItem == inventory.Empty ||
                    !IsThrallBag(slot.MyItem.ItemName)) continue;
                if (found != null)
                {
                    reason = "carry only one Thrall Bag at a time";
                    return false;
                }
                found = slot;
            }
            if (found == null)
            {
                reason = "no Thrall Bag is carried in direct inventory";
                return false;
            }

            bagId = EnsureBagIdentity(found);
            if (string.IsNullOrEmpty(bagId) ||
                !_bags.TryGetValue(bagId, out contents) || contents == null)
            {
                bagId = null;
                contents = null;
                reason = "the Thrall Bag identity or contents record is unavailable";
                return false;
            }
            _bagNames[bagId] = ThrallBagName;
            return true;
        }

        public static List<BagSlotData> GetBagContents(string bagId)
        {
            if (!_bags.TryGetValue(bagId, out var c))
            { c = new List<BagSlotData>(); _bags[bagId] = c; }
            ReserveId(bagId);
            return c;
        }

        public static int GetVisibleSlots(string bagId, string bagName)
        {
            int defined = GetBagSlots(bagName);
            int used = _bags.TryGetValue(bagId, out var contents) && contents != null
                ? contents.Count : 0;
            return Mathf.Max(defined, used);
        }

        public static bool TryAddItemToBag(string bagId, string bagName, Item item, int qty, bool stackable)
        {
            if (item == null || qty <= 0 || string.IsNullOrEmpty(bagId)) return false;
            if (IsWithdrawalOnly(bagId, bagName))
            {
                ChatHelper.Send("<color=#EF5350>[Corpse Bag]</color> This recovery bag is withdrawal-only. Items cannot be added to it.");
                return false;
            }
            if (IsThrallBag(bagName) || IsThrallBagId(bagId))
            {
                ChatHelper.Send(
                    "<color=#EF5350>[Thrall Bag]</color> Use the equipped-gear " +
                    "drop path so exact item state can be preserved.");
                return false;
            }
            if ((stackable && qty > MaxReasonableStackQuantity) ||
                (!stackable && qty > MaxReasonableItemQuality))
            {
                ChatHelper.Send("<color=#EF5350>[Bag]</color> That item has an invalid quantity or quality value.");
                return false;
            }
            if (IsBag(item))
            { ChatHelper.Send("<color=#EF5350>[Bag]</color> You can't put a bag inside a bag!"); return false; }
            int max = GetBagSlots(bagName);
            if (max <= 0) return false;
            var contents = GetBagContents(bagId);
            string itemId = item.Id ?? item.ItemName;
            if (stackable)
            {
                foreach (var s in contents)
                    if (s.IsStackable &&
                        string.Equals(s.ItemName, item.ItemName,
                            StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(s.ItemId ?? "", itemId ?? "",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        if (s.Quantity > MaxReasonableStackQuantity - qty)
                        {
                            ChatHelper.Send("<color=#EF5350>[Bag]</color> That stack is too large.");
                            return false;
                        }
                        s.Quantity += qty;
                        return true;
                    }
            }
            if (contents.Count >= max)
            { ChatHelper.Send("<color=#EF5350>[Bag]</color> This bag is full!"); return false; }
            contents.Add(new BagSlotData { ItemId = itemId, ItemName = item.ItemName,
                Quantity = qty, IsStackable = stackable });
            _bagNames[bagId] = bagName;
            return true;
        }

        private static bool TryAddItemToThrallBag(string bagId, string bagName,
            Item item, int quality, out string gearBefore,
            out string gearAfter, out string reason)
        {
            gearBefore = null;
            gearAfter = null;
            reason = null;
            if (!IsThrallBag(bagName) && !IsThrallBagId(bagId))
            {
                reason = "the destination is not a Thrall Bag";
                return false;
            }
            if (FindPhysicalBagSlot(bagId, ThrallBagName) == null)
            {
                reason = "the physical Thrall Bag could not be verified";
                return false;
            }
            if (!Necromancer.NecromancerPetEquipment.TryCreateThrallBagSlot(
                item, quality, out BagSlotData entry, out reason))
                return false;

            List<BagSlotData> contents = GetBagContents(bagId);
            if (entry.IsStackable)
            {
                BagSlotData existing = null;
                foreach (BagSlotData slot in contents)
                    if (slot != null && slot.IsStackable &&
                        string.Equals(slot.ItemId, entry.ItemId,
                            StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(slot.ItemName, entry.ItemName,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        existing = slot;
                        break;
                    }
                if (existing != null)
                {
                    if (existing.Quantity > int.MaxValue - entry.Quantity)
                    {
                        reason = "that stack is too large";
                        return false;
                    }
                    int oldQuantity = existing.Quantity;
                    existing.Quantity += entry.Quantity;
                    if (Necromancer.NecromancerPetEquipment.TryApplyThrallBagContents(
                        bagId, contents, out gearBefore, out gearAfter, out reason))
                        return true;
                    existing.Quantity = oldQuantity;
                    return false;
                }
            }
            if (contents.Count >= GetBagSlots(ThrallBagName))
            {
                reason = "the Thrall Bag is full";
                return false;
            }
            contents.Add(entry);
            _bagNames[bagId] = ThrallBagName;
            if (Necromancer.NecromancerPetEquipment.TryApplyThrallBagContents(
                bagId, contents, out gearBefore, out gearAfter, out reason))
                return true;
            contents.RemoveAt(contents.Count - 1);
            return false;
        }

        public static bool TryStoreFromInventorySlot(ItemIcon slot)
        {
            return TryStoreFromInventorySlot(slot,
                BagWindow.LastFocusedBagId, BagWindow.LastFocusedBagName);
        }

        public static bool TryStoreFromInventorySlot(ItemIcon slot,
            string bagId, string bagName)
        {
            if (slot?.MyItem == null) return false;
            if (!BagWindow.AnyOpen) return false;
            // Bag mutations journal the character's native StoredSlots. Refuse
            // bank, loot, vendor, equipment, cursor, and other foreign slots so
            // an item can never be removed from storage that is outside that
            // transaction boundary.
            if (!IsDirectInventorySlot(slot)) return false;
            if (GameData.ItemOnCursor != null ||
                (GameData.MouseSlot != null && GameData.MouseSlot.dragging))
                return false;
            if (string.IsNullOrEmpty(bagId)) return false;
            var item = slot.MyItem;
            if (item == GameData.PlayerInv.Empty) return false;
            if (IsBag(item))
            { ChatHelper.Send("<color=#EF5350>[Bag]</color> You can't put a bag inside a bag!"); return true; }

            int qty = slot.Quantity; if (qty <= 0) qty = 1;
            string beforeBags = SerializeBags();
            InventorySnapshot beforeInventory = CaptureInventory();
            bool thrallBag = IsThrallBag(bagName) || IsThrallBagId(bagId);
            string gearBefore = null;
            string gearAfter = null;
            string thrallReason = null;
            bool stored = thrallBag
                ? TryAddItemToThrallBag(bagId, bagName, item, qty,
                    out gearBefore, out gearAfter, out thrallReason)
                : TryAddItemToBag(bagId, bagName, item, qty, item.Stackable);
            if (!stored)
            {
                if (thrallBag)
                    ChatHelper.Send("<color=#EF5350>[Thrall Bag]</color> " +
                        thrallReason + ".");
                return true; // The open bag handled the right-click; never consume the item.
            }

            slot.MyItem = GameData.PlayerInv.Empty;
            slot.Quantity = 1;
            slot.UpdateSlotImage();

            bool committed = PersistMutation(
                thrallBag ? "thrall-bag-store" : "store",
                beforeBags, beforeInventory,
                thrallBag
                    ? Necromancer.NecromancerPetEquipment.GetPath() : null,
                thrallBag ? gearBefore : null,
                thrallBag ? gearAfter : null);
            if (!committed)
            {
                if (thrallBag)
                    Necromancer.NecromancerPetEquipment
                        .RestoreSerializedInMemory(gearBefore);
                RestoreAfterFailedMutation(beforeBags, beforeInventory,
                    thrallBag
                        ? Necromancer.NecromancerPetEquipment.GetPath() : null,
                    thrallBag ? gearBefore : null);
                ChatHelper.Send("<color=#EF5350>[Bag]</color> The item was not moved because the save could not be completed.");
                return true;
            }

            if (thrallBag)
                Necromancer.NecromancerPets.ApplyEscrowToActivePet();
            ChatHelper.Send($"<color=#4CAF50>[{(thrallBag ? "Thrall Bag" : "Bag")}]</color> " +
                $"Stored {item.ItemName} in {bagName}.");
            BagWindow.RefreshAll();
            return true;
        }

        /// <summary>
        /// Complete Erenshor's native click-to-cursor move by dropping that
        /// item into a specific crafted bag. The pre-drag inventory snapshot is
        /// reconstructed so the bag sidecar and native save remain one atomic
        /// transaction even though Erenshor emptied the source slot first.
        /// </summary>
        public static bool TryStoreCursorItem(string bagId, string bagName)
        {
            var inventory = GameData.PlayerInv;
            var mouse = GameData.MouseSlot;
            var home = GetMouseHomeSlot(mouse);
            Item item = mouse?.MyItem;
            if (inventory?.StoredSlots == null || mouse == null ||
                item == null || item == inventory.Empty ||
                !mouse.dragging || !IsDirectInventorySlot(home) ||
                string.IsNullOrEmpty(bagId))
                return false;
            if (IsBag(item))
            {
                ChatHelper.Send("<color=#EF5350>[Bag]</color> You can't put a bag inside a bag!");
                return true;
            }

            int quantity = Mathf.Max(1, mouse.Quantity);
            int homeIndex = inventory.StoredSlots.IndexOf(home);
            if (homeIndex < 0) return false;

            string beforeBags = SerializeBags();
            InventorySnapshot beforeInventory = CaptureInventory();
            if (home.MyItem == inventory.Empty || home.MyItem == null)
            {
                beforeInventory.Items[homeIndex] = item;
                beforeInventory.Quantities[homeIndex] = quantity;
            }
            else if (item.Stackable && ReferenceEquals(home.MyItem, item))
            {
                beforeInventory.Items[homeIndex] = item;
                beforeInventory.Quantities[homeIndex] =
                    Mathf.Max(1, home.Quantity) + quantity;
            }
            else
            {
                ChatHelper.Send(
                    "<color=#EF5350>[Bag]</color> The original inventory slot changed; " +
                    "the item remains on the cursor.");
                return true;
            }
            beforeInventory.Fingerprint = GetInventorySnapshotFingerprint(beforeInventory);

            bool thrallBag = IsThrallBag(bagName) || IsThrallBagId(bagId);
            string gearBefore = null;
            string gearAfter = null;
            string thrallReason = null;
            bool stored = thrallBag
                ? TryAddItemToThrallBag(bagId, bagName, item, quantity,
                    out gearBefore, out gearAfter, out thrallReason)
                : TryAddItemToBag(bagId, bagName, item, quantity,
                    item.Stackable);
            if (!stored)
            {
                if (thrallBag)
                    ChatHelper.Send("<color=#EF5350>[Thrall Bag]</color> " +
                        thrallReason + ".");
                return true;
            }

            ClearNativeCursor();
            bool committed = PersistMutation(
                thrallBag ? "thrall-bag-drag-store" : "drag-store",
                beforeBags, beforeInventory,
                thrallBag
                    ? Necromancer.NecromancerPetEquipment.GetPath() : null,
                thrallBag ? gearBefore : null,
                thrallBag ? gearAfter : null);
            if (!committed)
            {
                if (thrallBag)
                    Necromancer.NecromancerPetEquipment
                        .RestoreSerializedInMemory(gearBefore);
                RestoreAfterFailedMutation(beforeBags, beforeInventory,
                    thrallBag
                        ? Necromancer.NecromancerPetEquipment.GetPath() : null,
                    thrallBag ? gearBefore : null);
                ChatHelper.Send(
                    "<color=#EF5350>[Bag]</color> The item was not moved because " +
                    "the save could not be completed.");
                return true;
            }

            if (thrallBag)
                Necromancer.NecromancerPets.ApplyEscrowToActivePet();
            ChatHelper.Send(
                $"<color=#4CAF50>[{(thrallBag ? "Thrall Bag" : "Bag")}]</color> " +
                $"Stored {item.ItemName} in {bagName}.");
            BagWindow.RefreshAll();
            return true;
        }

        public static bool TryTakeFromBag(string bagId, int idx, BagSlotData expected)
        {
            var c = GetBagContents(bagId);
            if (idx < 0 || idx >= c.Count || expected == null || !ReferenceEquals(c[idx], expected))
                return false;

            Item item = ResolveBagItem(expected);
            if (item == null)
            {
                ChatHelper.Send($"<color=#EF5350>[Bag]</color> '{expected.ItemName}' is unavailable; it remains safely in the bag.");
                return false;
            }
            bool consumeCorpseBag = IsCorpseBagId(bagId) && c.Count == 1;
            bool thrallBag = IsThrallBagId(bagId);
            ItemIcon corpseBagSlot = consumeCorpseBag
                ? FindPhysicalBagSlot(bagId, CorpseBagName) : null;
            int itemCount = expected.IsStackable ? Mathf.Max(1, expected.Quantity) : 1;
            if (consumeCorpseBag && corpseBagSlot == null)
            {
                ChatHelper.Send("<color=#EF5350>[Corpse Bag]</color> The physical recovery bag could not be verified. Nothing was moved.");
                return false;
            }
            if (!consumeCorpseBag && !ItemFactory.CanAddItemToPlayer(item, itemCount))
            {
                ChatHelper.Send("<color=#EF5350>[Bag]</color> Your inventory is full. The item remains in the bag.");
                return false;
            }

            string beforeBags = SerializeBags();
            InventorySnapshot beforeInventory = CaptureInventory();
            c.RemoveAt(idx);
            string gearBefore = null;
            string gearAfter = null;
            if (thrallBag &&
                !Necromancer.NecromancerPetEquipment.TryApplyThrallBagContents(
                    bagId, c, out gearBefore, out gearAfter,
                    out string gearReason))
            {
                c.Insert(idx, expected);
                ChatHelper.Send("<color=#EF5350>[Thrall Bag]</color> " +
                    $"Nothing moved: {gearReason}.");
                return false;
            }
            bool added = false;
            try
            {
                if (consumeCorpseBag)
                {
                    // The disappearing bag's own inventory slot is always a
                    // safe destination for the final recovered item, even when
                    // every other slot is occupied.
                    corpseBagSlot.MyItem = item;
                    corpseBagSlot.Quantity = expected.Quantity;
                    corpseBagSlot.UpdateSlotImage();
                    RemoveBagRecord(bagId);
                    GameData.PlayerInv.UpdatePlayerInventory();
                    added = true;
                }
                else
                {
                    added = GameData.PlayerInv.AddItemToInv(item,
                        Mathf.Max(1, expected.Quantity));
                }
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogError($"Bag take failed for '{expected.ItemName}': {ex.Message}");
            }

            bool committed = added && PersistMutation(
                thrallBag ? "thrall-bag-take" : "take",
                beforeBags, beforeInventory,
                thrallBag
                    ? Necromancer.NecromancerPetEquipment.GetPath() : null,
                thrallBag ? gearBefore : null,
                thrallBag ? gearAfter : null);
            if (!committed)
            {
                if (thrallBag)
                    Necromancer.NecromancerPetEquipment
                        .RestoreSerializedInMemory(gearBefore);
                RestoreAfterFailedMutation(beforeBags, beforeInventory,
                    thrallBag
                        ? Necromancer.NecromancerPetEquipment.GetPath() : null,
                    thrallBag ? gearBefore : null);
                ChatHelper.Send("<color=#EF5350>[Bag]</color> The item was not moved because the transaction could not be completed.");
                return false;
            }

            if (thrallBag)
                Necromancer.NecromancerPets.ApplyEscrowToActivePet();
            string bagLabel = _bagNames.TryGetValue(bagId, out string savedBagName)
                ? savedBagName : "Bag";
            ChatHelper.Send($"<color=#4CAF50>[{(thrallBag ? "Thrall Bag" : "Bag")}]</color> " +
                $"Removed {expected.ItemName}{GetRarityTag(expected)} from {bagLabel}.");
            if (consumeCorpseBag) BagWindow.CloseOne(bagId);
            else BagWindow.RefreshAll();
            return true;
        }

        /// <summary>Drag a bag item into an exact direct-inventory slot.</summary>
        public static bool TryTakeFromBagToInventorySlot(string bagId, int idx,
            BagSlotData expected, ItemIcon target)
        {
            if (!IsDirectInventorySlot(target) || target == GameData.MouseSlot)
                return false;
            var inventory = GameData.PlayerInv;
            var contents = GetBagContents(bagId);
            if (inventory == null || idx < 0 || idx >= contents.Count ||
                expected == null || !ReferenceEquals(contents[idx], expected))
                return false;

            Item item = ResolveBagItem(expected);
            if (item == null)
            {
                ChatHelper.Send(
                    $"<color=#EF5350>[Bag]</color> '{expected.ItemName}' is unavailable; " +
                    "it remains safely in the bag.");
                return false;
            }

            int quantity = Mathf.Max(1, expected.Quantity);
            bool consumeCorpseBag = IsCorpseBagId(bagId) && contents.Count == 1;
            bool thrallBag = IsThrallBagId(bagId);
            ItemIcon corpseBagSlot = consumeCorpseBag
                ? FindPhysicalBagSlot(bagId, CorpseBagName) : null;
            if (consumeCorpseBag && corpseBagSlot == null)
            {
                ChatHelper.Send("<color=#EF5350>[Corpse Bag]</color> The physical recovery bag could not be verified. Nothing was moved.");
                return false;
            }
            bool replacingContainer = consumeCorpseBag &&
                ReferenceEquals(target, corpseBagSlot);
            bool targetEmpty = replacingContainer || target.MyItem == null ||
                target.MyItem == inventory.Empty;
            bool stackMatch = !targetEmpty && item.Stackable &&
                ReferenceEquals(target.MyItem, item);
            if (!targetEmpty && !stackMatch)
            {
                ChatHelper.Send(
                    "<color=#EF5350>[Bag]</color> Drop onto an empty slot or a " +
                    "matching stack.");
                return false;
            }
            if (stackMatch && target.Quantity > MaxReasonableStackQuantity - quantity)
            {
                ChatHelper.Send("<color=#EF5350>[Bag]</color> That stack is too large.");
                return false;
            }

            string beforeBags = SerializeBags();
            InventorySnapshot beforeInventory = CaptureInventory();
            contents.RemoveAt(idx);
            string gearBefore = null;
            string gearAfter = null;
            if (thrallBag &&
                !Necromancer.NecromancerPetEquipment.TryApplyThrallBagContents(
                    bagId, contents, out gearBefore, out gearAfter,
                    out string gearReason))
            {
                contents.Insert(idx, expected);
                ChatHelper.Send("<color=#EF5350>[Thrall Bag]</color> " +
                    $"Nothing moved: {gearReason}.");
                return false;
            }
            if (targetEmpty)
            {
                target.MyItem = item;
                target.Quantity = quantity;
            }
            else
            {
                target.Quantity += quantity;
            }
            if (consumeCorpseBag)
            {
                if (!replacingContainer)
                {
                    corpseBagSlot.MyItem = inventory.Empty;
                    corpseBagSlot.Quantity = 1;
                    corpseBagSlot.UpdateSlotImage();
                }
                RemoveBagRecord(bagId);
            }
            target.UpdateSlotImage();
            inventory.UpdatePlayerInventory();

            bool committed = PersistMutation(
                thrallBag ? "thrall-bag-drag-take" : "drag-take",
                beforeBags, beforeInventory,
                thrallBag
                    ? Necromancer.NecromancerPetEquipment.GetPath() : null,
                thrallBag ? gearBefore : null,
                thrallBag ? gearAfter : null);
            if (!committed)
            {
                if (thrallBag)
                    Necromancer.NecromancerPetEquipment
                        .RestoreSerializedInMemory(gearBefore);
                RestoreAfterFailedMutation(beforeBags, beforeInventory,
                    thrallBag
                        ? Necromancer.NecromancerPetEquipment.GetPath() : null,
                    thrallBag ? gearBefore : null);
                ChatHelper.Send(
                    "<color=#EF5350>[Bag]</color> The item was not moved because " +
                    "the transaction could not be completed.");
                return false;
            }

            if (thrallBag)
                Necromancer.NecromancerPets.ApplyEscrowToActivePet();
            string bagLabel = _bagNames.TryGetValue(bagId, out string savedBagName)
                ? savedBagName : "Bag";
            ChatHelper.Send(
                $"<color=#4CAF50>[{(thrallBag ? "Thrall Bag" : "Bag")}]</color> " +
                $"Removed {expected.ItemName}{GetRarityTag(expected)} from {bagLabel}.");
            if (consumeCorpseBag) BagWindow.CloseOne(bagId);
            else BagWindow.RefreshAll();
            return true;
        }

        /// <summary>
        /// Seed a recovery-only bag without persisting it.  CorpseRunSystem owns
        /// the surrounding two-file journal and commits this together with the
        /// native inventory slot that contains the bag.
        /// </summary>
        internal static bool TrySeedCorpseBag(int bagNumber,
            IList<BagSlotData> recoveredItems, out string error)
        {
            error = null;
            try
            {
                if (bagNumber < 100 || bagNumber == int.MaxValue)
                    throw new InvalidDataException("the recovery bag identity is invalid");
                string bagId = $"bag{bagNumber}";
                if (_bags.ContainsKey(bagId))
                    throw new InvalidDataException(
                        $"the recovery bag identity {bagId} is already in use");
                if (recoveredItems == null || recoveredItems.Count <= 0 ||
                    recoveredItems.Count > MaxReasonableItemsPerBag)
                    throw new InvalidDataException(
                        "the recovery bag item count is outside the safe range");

                var contents = new List<BagSlotData>(recoveredItems.Count);
                foreach (BagSlotData source in recoveredItems)
                {
                    if (source == null || string.IsNullOrWhiteSpace(source.ItemId) ||
                        string.IsNullOrWhiteSpace(source.ItemName) ||
                        source.Quantity <= 0 ||
                        (source.IsStackable &&
                         source.Quantity > MaxReasonableStackQuantity))
                        throw new InvalidDataException(
                            "a recovered item does not have valid native slot state");
                    contents.Add(new BagSlotData
                    {
                        ItemId = source.ItemId,
                        ItemName = source.ItemName,
                        // Preserve the raw native quantity/quality exactly.  It
                        // is intentionally not clamped or translated.
                        Quantity = source.Quantity,
                        IsStackable = source.IsStackable
                    });
                }

                _bags.Add(bagId, contents);
                _bagNames[bagId] = CorpseBagName;
                ReserveId(bagId);
                string serialized = SerializeBags();
                string validationError = GetSerializedDataValidationError(serialized);
                if (validationError != null)
                    throw new InvalidDataException(
                        "the seeded recovery bag did not validate: " + validationError);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        internal static bool TryRestoreSerializedState(string json,
            out string error)
        {
            error = null;
            if (DeserializeBags(json)) return true;
            error = GetSerializedDataValidationError(json) ??
                "the bag snapshot could not be restored";
            return false;
        }

        internal static bool ValidateRecoveryContainerSnapshot(string json,
            string bagId, int expectedItemCount, out string error)
        {
            error = null;
            try
            {
                string validationError = GetSerializedDataValidationError(json);
                if (validationError != null)
                    throw new InvalidDataException(validationError);
                if (string.IsNullOrWhiteSpace(bagId) || expectedItemCount <= 0)
                    throw new InvalidDataException(
                        "recovery container metadata is incomplete");
                BagSaveEnvelope envelope = ParseV2Envelope(json);
                BagSaveRecord match = null;
                foreach (BagSaveRecord candidate in envelope.Bags)
                    if (string.Equals(candidate.Id, bagId,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        match = candidate;
                        break;
                    }
                if (match == null || !IsCorpseBag(match.Name) ||
                    match.Items == null ||
                    match.Items.Count != expectedItemCount)
                    throw new InvalidDataException(
                        "the recovery container record or item count changed");
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static ItemIcon FindPhysicalBagSlot(string bagId, string bagName)
        {
            Match match = Regex.Match(bagId ?? "", "^bag([0-9]+)$",
                RegexOptions.IgnoreCase);
            if (!match.Success || !int.TryParse(match.Groups[1].Value,
                out int number)) return null;
            Inventory inventory = GameData.PlayerInv;
            if (inventory?.StoredSlots == null) return null;
            foreach (ItemIcon slot in inventory.StoredSlots)
                if (IsDirectInventorySlot(slot) && slot.MyItem != null &&
                    slot.MyItem != inventory.Empty && slot.Quantity == number &&
                    string.Equals(slot.MyItem.ItemName, bagName,
                        StringComparison.OrdinalIgnoreCase)) return slot;
            return null;
        }

        private static bool IsAccessibleForAggregateUse(string bagId)
        {
            return !IsCorpseBagId(bagId) &&
                _bagNames.TryGetValue(bagId, out string name) &&
                FindPhysicalBagSlot(bagId, name) != null;
        }

        private static void RemoveBagRecord(string bagId)
        {
            _bags.Remove(bagId);
            _bagNames.Remove(bagId);
        }

        /// <summary>Count total quantity of an item across all bags.</summary>
        public static int CountItemInAllBags(string itemName)
        {
            long total = 0;
            foreach (var kvp in _bags)
            {
                // Recovery belongings must be explicitly withdrawn. Also skip
                // orphaned/nested records whose physical bag is not currently
                // in direct inventory; this prevents crafting from changing a
                // bag that is still held by a corpse.
                if (!IsAccessibleForAggregateUse(kvp.Key)) continue;
                foreach (var slot in kvp.Value)
                {
                    if (slot != null && string.Equals(slot.ItemName, itemName,
                        StringComparison.OrdinalIgnoreCase))
                        total += slot.IsStackable ? Mathf.Max(0, slot.Quantity) : 1;
                }
            }
            return total > int.MaxValue ? int.MaxValue : (int)total;
        }

        /// <summary>Remove up to 'amount' of an item from all bags. Returns remaining amount.</summary>
        public static int RemoveItemFromAllBags(string itemName, int amount)
        {
            if (amount <= 0) return 0;
            foreach (var kvp in _bags)
            {
                if (!IsAccessibleForAggregateUse(kvp.Key)) continue;
                var contents = kvp.Value;
                for (int i = contents.Count - 1; i >= 0 && amount > 0; i--)
                {
                    var slot = contents[i];
                    if (slot == null || !string.Equals(slot.ItemName, itemName,
                        StringComparison.OrdinalIgnoreCase)) continue;
                    int quantity = slot.IsStackable ? Mathf.Max(0, slot.Quantity) : 1;
                    if (quantity <= 0)
                    {
                        contents.RemoveAt(i);
                    }
                    else if (quantity <= amount)
                    {
                        amount -= quantity;
                        contents.RemoveAt(i);
                    }
                    else if (slot.IsStackable)
                    {
                        slot.Quantity -= amount;
                        amount = 0;
                    }
                }
            }
            return amount;
        }

        public static string GetRarityTag(BagSlotData s)
        {
            if (s.IsStackable) return "";
            switch (s.Quantity)
            {
                case 2: return " <color=#00FFFF>(Blessed)</color>";
                case 3: return " <color=#FF00FF>(Epic)</color>";
                case 4: return " <color=#00FF00>(Uncommon)</color>";
                case 5: return " <color=#FFFF00>(Rare)</color>";
                case 6: return " <color=#FF3333>(Epic)</color>";
                case 7: return " <color=#FF8800>(Legendary)</color>";
                case 8: return " <color=#666666>(Mythic)</color>";
                default: return "";
            }
        }

        public static string SerializeBags()
        {
            var bagIds = new List<string>(_bags.Keys);
            bagIds.Sort(StringComparer.OrdinalIgnoreCase);
            var envelope = new BagSaveEnvelope
            {
                NextBagCounter = Mathf.Max(100, _nextBagCounter)
            };
            foreach (string bagId in bagIds)
            {
                envelope.Bags.Add(new BagSaveRecord
                {
                    Id = bagId,
                    Name = _bagNames.TryGetValue(bagId, out string name)
                        ? name : "",
                    Items = CloneContents(_bags[bagId])
                });
            }
            return SerializeEnvelope(envelope);
        }

        internal static string SerializeEnvelope(BagSaveEnvelope envelope)
        {
            if (envelope == null) return "";
            var records = new List<BagSaveRecord>(
                envelope.Bags ?? new List<BagSaveRecord>());
            records.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(
                left?.Id ?? "", right?.Id ?? ""));
            var json = new StringBuilder(256 + records.Count * 160);
            json.Append("{\n    \"Version\": ").Append(envelope.Version)
                .Append(",\n    \"NextBagCounter\": ")
                .Append(envelope.NextBagCounter)
                .Append(",\n    \"Bags\": [");
            for (int bagIndex = 0; bagIndex < records.Count; bagIndex++)
            {
                BagSaveRecord record = records[bagIndex] ??
                    new BagSaveRecord();
                if (bagIndex > 0) json.Append(',');
                json.Append("\n        {\n            \"Id\": ");
                AppendJsonString(json, record.Id ?? "");
                json.Append(",\n            \"Name\": ");
                AppendJsonString(json, record.Name ?? "");
                json.Append(",\n            \"Items\": [");
                var clonedContents = CloneContents(record.Items);
                for (int itemIndex = 0; itemIndex < clonedContents.Count; itemIndex++)
                {
                    var item = clonedContents[itemIndex];
                    if (itemIndex > 0) json.Append(',');
                    json.Append("\n                {\n                    \"ItemId\": ");
                    AppendJsonString(json, item.ItemId ?? "");
                    json.Append(",\n                    \"ItemName\": ");
                    AppendJsonString(json, item.ItemName ?? "");
                    json.Append(",\n                    \"Quantity\": ")
                        .Append(item.Quantity)
                        .Append(",\n                    \"IsStackable\": ")
                        .Append(item.IsStackable ? "true" : "false")
                        .Append(",\n                    \"ExactState\": ");
                    AppendJsonString(json, item.ExactState ?? "");
                    json
                        .Append("\n                }");
                }
                if (clonedContents.Count > 0) json.Append('\n').Append("            ");
                json.Append("]\n        }");
            }
            if (records.Count > 0) json.Append('\n').Append("    ");
            json.Append("]\n}");
            return json.ToString();
        }

        internal static bool TryParseEnvelope(string json,
            out BagSaveEnvelope envelope, out string error)
        {
            envelope = null;
            error = GetSerializedDataValidationError(json);
            if (error != null) return false;
            if (string.IsNullOrWhiteSpace(json) ||
                !json.Contains("\"Version\""))
            {
                error = "a versioned bag envelope is required";
                return false;
            }
            try
            {
                envelope = ParseV2Envelope(json.Trim());
                return envelope != null;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                envelope = null;
                return false;
            }
        }

        /// <summary>
        /// Verify that every bag held by a corpse still points at the exact
        /// contents captured at death.  Orphan bag records are intentionally
        /// retained while the physical bag is on a corpse, so any difference is
        /// treated as unsafe and recovery is stopped before inventory changes.
        /// </summary>
        internal static bool ValidateCorpseBagSnapshot(string snapshotJson,
            IEnumerable<int> bagNumbers, out string detail)
        {
            detail = null;
            try
            {
                if (!IsSerializedDataValid(snapshotJson))
                    throw new InvalidDataException("the corpse bag snapshot is invalid");
                string currentJson = SerializeBags();
                if (!IsSerializedDataValid(currentJson))
                    throw new InvalidDataException("the current bag data is invalid");

                BagSaveEnvelope expected = ParseV2Envelope(snapshotJson);
                BagSaveEnvelope current = ParseV2Envelope(currentJson);
                var expectedById = new Dictionary<string, BagSaveRecord>(
                    StringComparer.OrdinalIgnoreCase);
                var currentById = new Dictionary<string, BagSaveRecord>(
                    StringComparer.OrdinalIgnoreCase);
                foreach (BagSaveRecord record in expected.Bags)
                    expectedById[record.Id] = record;
                foreach (BagSaveRecord record in current.Bags)
                    currentById[record.Id] = record;

                int checkedCount = 0;
                var seen = new HashSet<int>();
                foreach (int number in bagNumbers ?? Array.Empty<int>())
                {
                    if (number < 100 || !seen.Add(number))
                        throw new InvalidDataException(
                            $"bag identity {number} is invalid or duplicated");
                    string id = $"bag{number}";
                    if (!expectedById.TryGetValue(id, out BagSaveRecord before) ||
                        !currentById.TryGetValue(id, out BagSaveRecord now))
                        throw new InvalidDataException(
                            $"saved contents for {id} are unavailable");
                    if (!BagRecordsEqual(before, now))
                        throw new InvalidDataException(
                            $"saved contents for {id} changed while it was on the corpse");
                    checkedCount++;
                }
                detail = $"validated {checkedCount} corpse bag record(s)";
                return true;
            }
            catch (Exception ex)
            {
                detail = ex.Message;
                return false;
            }
        }

        private static bool BagRecordsEqual(BagSaveRecord left, BagSaveRecord right)
        {
            if (left == null || right == null ||
                !string.Equals(left.Id, right.Id, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(left.Name ?? "", right.Name ?? "",
                    StringComparison.OrdinalIgnoreCase) ||
                left.Items == null || right.Items == null ||
                left.Items.Count != right.Items.Count) return false;
            for (int i = 0; i < left.Items.Count; i++)
            {
                BagSlotData a = left.Items[i];
                BagSlotData b = right.Items[i];
                if (a == null || b == null ||
                    !string.Equals(a.ItemId ?? "", b.ItemId ?? "",
                        StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(a.ItemName ?? "", b.ItemName ?? "",
                        StringComparison.OrdinalIgnoreCase) ||
                    a.Quantity != b.Quantity ||
                    a.IsStackable != b.IsStackable ||
                    !string.Equals(a.ExactState ?? "", b.ExactState ?? "",
                        StringComparison.Ordinal))
                    return false;
            }
            return true;
        }

        private static void AppendJsonString(StringBuilder destination, string value)
        {
            destination.Append('"');
            foreach (char character in value ?? "")
            {
                switch (character)
                {
                    case '"': destination.Append("\\\""); break;
                    case '\\': destination.Append("\\\\"); break;
                    case '\b': destination.Append("\\b"); break;
                    case '\f': destination.Append("\\f"); break;
                    case '\n': destination.Append("\\n"); break;
                    case '\r': destination.Append("\\r"); break;
                    case '\t': destination.Append("\\t"); break;
                    default:
                        if (character < 0x20)
                            destination.Append("\\u")
                                .Append(((int)character).ToString("x4"));
                        else
                            destination.Append(character);
                        break;
                }
            }
            destination.Append('"');
        }

        // Unity 2021's JsonUtility silently drops nested List<T> fields for
        // these records on the live Erenshor runtime. Keep the save format
        // ordinary JSON, but parse it ourselves so a successful validation can
        // never turn a non-empty bag into an empty one during load.
        private static BagSaveEnvelope ParseV2Envelope(string json)
        {
            return new BagJsonReader(json).ReadEnvelope();
        }

        private sealed class BagJsonReader
        {
            private readonly string _text;
            private int _index;

            internal BagJsonReader(string text)
            {
                _text = text ?? throw new ArgumentNullException(nameof(text));
            }

            internal BagSaveEnvelope ReadEnvelope()
            {
                Expect('{');
                var envelope = new BagSaveEnvelope();
                bool hasVersion = false;
                bool hasCounter = false;
                bool hasBags = false;
                bool first = true;
                while (true)
                {
                    SkipWhitespace();
                    if (TryConsume('}')) break;
                    if (!first) Expect(',');
                    string property = ReadString();
                    Expect(':');
                    switch (property)
                    {
                        case "Version":
                            RejectDuplicate(hasVersion, property);
                            envelope.Version = ReadInt();
                            hasVersion = true;
                            break;
                        case "NextBagCounter":
                            RejectDuplicate(hasCounter, property);
                            envelope.NextBagCounter = ReadInt();
                            hasCounter = true;
                            break;
                        case "Bags":
                            RejectDuplicate(hasBags, property);
                            envelope.Bags = ReadBags();
                            hasBags = true;
                            break;
                        default:
                            Fail($"unknown envelope field '{property}'");
                            break;
                    }
                    first = false;
                }
                if (!hasVersion || !hasCounter || !hasBags)
                    Fail("the v2 envelope is incomplete");
                SkipWhitespace();
                if (_index != _text.Length) Fail("data follows the root object");
                return envelope;
            }

            private List<BagSaveRecord> ReadBags()
            {
                Expect('[');
                var records = new List<BagSaveRecord>();
                bool first = true;
                while (true)
                {
                    SkipWhitespace();
                    if (TryConsume(']')) break;
                    if (!first) Expect(',');
                    records.Add(ReadBag());
                    if (records.Count > 10000) Fail("bag count is unreasonable");
                    first = false;
                }
                return records;
            }

            private BagSaveRecord ReadBag()
            {
                Expect('{');
                var record = new BagSaveRecord();
                bool hasId = false;
                bool hasName = false;
                bool hasItems = false;
                bool first = true;
                while (true)
                {
                    SkipWhitespace();
                    if (TryConsume('}')) break;
                    if (!first) Expect(',');
                    string property = ReadString();
                    Expect(':');
                    switch (property)
                    {
                        case "Id":
                            RejectDuplicate(hasId, property);
                            record.Id = ReadString();
                            hasId = true;
                            break;
                        case "Name":
                            RejectDuplicate(hasName, property);
                            record.Name = ReadString();
                            hasName = true;
                            break;
                        case "Items":
                            RejectDuplicate(hasItems, property);
                            record.Items = ReadItems();
                            hasItems = true;
                            break;
                        default:
                            Fail($"unknown bag field '{property}'");
                            break;
                    }
                    first = false;
                }
                if (!hasId || !hasName || !hasItems)
                    Fail("a bag record is incomplete");
                return record;
            }

            private List<BagSlotData> ReadItems()
            {
                Expect('[');
                var items = new List<BagSlotData>();
                bool first = true;
                while (true)
                {
                    SkipWhitespace();
                    if (TryConsume(']')) break;
                    if (!first) Expect(',');
                    items.Add(ReadItem());
                    if (items.Count > MaxReasonableItemsPerBag)
                        Fail("a bag contains too many item records");
                    first = false;
                }
                return items;
            }

            private BagSlotData ReadItem()
            {
                Expect('{');
                var item = new BagSlotData();
                bool hasId = false;
                bool hasName = false;
                bool hasQuantity = false;
                bool hasStackable = false;
                bool hasExactState = false;
                bool first = true;
                while (true)
                {
                    SkipWhitespace();
                    if (TryConsume('}')) break;
                    if (!first) Expect(',');
                    string property = ReadString();
                    Expect(':');
                    switch (property)
                    {
                        case "ItemId":
                            RejectDuplicate(hasId, property);
                            item.ItemId = ReadString();
                            hasId = true;
                            break;
                        case "ItemName":
                            RejectDuplicate(hasName, property);
                            item.ItemName = ReadString();
                            hasName = true;
                            break;
                        case "Quantity":
                            RejectDuplicate(hasQuantity, property);
                            item.Quantity = ReadInt();
                            hasQuantity = true;
                            break;
                        case "IsStackable":
                            RejectDuplicate(hasStackable, property);
                            item.IsStackable = ReadBool();
                            hasStackable = true;
                            break;
                        case "ExactState":
                            RejectDuplicate(hasExactState, property);
                            item.ExactState = ReadString();
                            hasExactState = true;
                            break;
                        default:
                            Fail($"unknown item field '{property}'");
                            break;
                    }
                    first = false;
                }
                if (!hasId || !hasName || !hasQuantity || !hasStackable)
                    Fail("an item record is incomplete");
                if (!hasExactState) item.ExactState = "";
                return item;
            }

            private string ReadString()
            {
                SkipWhitespace();
                if (_index >= _text.Length || _text[_index] != '"')
                    Fail("expected a JSON string");
                _index++;
                var result = new StringBuilder();
                while (_index < _text.Length)
                {
                    char character = _text[_index++];
                    if (character == '"') return result.ToString();
                    if (character < 0x20) Fail("unescaped control character in string");
                    if (character != '\\')
                    {
                        result.Append(character);
                        continue;
                    }
                    if (_index >= _text.Length) Fail("unterminated string escape");
                    char escaped = _text[_index++];
                    switch (escaped)
                    {
                        case '"': result.Append('"'); break;
                        case '\\': result.Append('\\'); break;
                        case '/': result.Append('/'); break;
                        case 'b': result.Append('\b'); break;
                        case 'f': result.Append('\f'); break;
                        case 'n': result.Append('\n'); break;
                        case 'r': result.Append('\r'); break;
                        case 't': result.Append('\t'); break;
                        case 'u':
                            int codePoint = 0;
                            for (int digit = 0; digit < 4; digit++)
                            {
                                if (_index >= _text.Length)
                                    Fail("incomplete Unicode escape");
                                int value = HexValue(_text[_index++]);
                                if (value < 0) Fail("invalid Unicode escape");
                                codePoint = codePoint * 16 + value;
                            }
                            result.Append((char)codePoint);
                            break;
                        default:
                            Fail($"invalid string escape '\\{escaped}'");
                            break;
                    }
                }
                Fail("unterminated JSON string");
                return null;
            }

            private int ReadInt()
            {
                SkipWhitespace();
                int start = _index;
                if (_index < _text.Length && _text[_index] == '-') _index++;
                int digits = _index;
                while (_index < _text.Length && char.IsDigit(_text[_index])) _index++;
                if (_index == digits) Fail("expected an integer");
                string token = _text.Substring(start, _index - start);
                if (!int.TryParse(token, NumberStyles.AllowLeadingSign,
                        CultureInfo.InvariantCulture, out int value))
                    Fail($"integer '{token}' is outside the supported range");
                return value;
            }

            private bool ReadBool()
            {
                SkipWhitespace();
                if (Matches("true")) { _index += 4; return true; }
                if (Matches("false")) { _index += 5; return false; }
                Fail("expected true or false");
                return false;
            }

            private bool Matches(string value)
            {
                if (_index + value.Length > _text.Length) return false;
                return string.CompareOrdinal(_text, _index, value, 0, value.Length) == 0;
            }

            private void Expect(char expected)
            {
                SkipWhitespace();
                if (_index >= _text.Length || _text[_index] != expected)
                    Fail($"expected '{expected}'");
                _index++;
            }

            private bool TryConsume(char expected)
            {
                SkipWhitespace();
                if (_index >= _text.Length || _text[_index] != expected) return false;
                _index++;
                return true;
            }

            private void SkipWhitespace()
            {
                while (_index < _text.Length && char.IsWhiteSpace(_text[_index])) _index++;
            }

            private void RejectDuplicate(bool alreadySet, string property)
            {
                if (alreadySet) Fail($"duplicate field '{property}'");
            }

            private static int HexValue(char value)
            {
                if (value >= '0' && value <= '9') return value - '0';
                if (value >= 'a' && value <= 'f') return value - 'a' + 10;
                if (value >= 'A' && value <= 'F') return value - 'A' + 10;
                return -1;
            }

            private void Fail(string message)
            {
                throw new InvalidDataException($"{message} at character {_index}");
            }
        }

        public static bool IsSerializedDataValid(string json)
        {
            return GetSerializedDataValidationError(json) == null;
        }

        internal static string GetSerializedDataValidationError(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return "data is empty";
            string trimmed = json.Trim();
            if (!trimmed.StartsWith("{") || !trimmed.EndsWith("}"))
                return "data is not a JSON object";
            if (trimmed == "{}") return null;
            if (trimmed.Contains("\"Version\""))
            {
                try
                {
                    if (Regex.Matches(trimmed, "\\\"Version\\\"\\s*:").Count != 1 ||
                        Regex.Matches(trimmed, "\\\"NextBagCounter\\\"\\s*:").Count != 1 ||
                        Regex.Matches(trimmed, "\\\"Bags\\\"\\s*:").Count != 1)
                        return "v2 envelope fields are missing or duplicated";
                    var parsed = ParseV2Envelope(trimmed);
                    if (parsed == null) return "v2 envelope could not be parsed";
                    if (parsed.Version != SaveFormatVersion)
                        return $"unsupported v2 version {parsed.Version}";
                    if (parsed.NextBagCounter < 100)
                        return $"next bag counter {parsed.NextBagCounter} is below 100";
                    if (parsed.Bags == null) return "v2 bag list is null";
                    if (parsed.Bags.Count > 10000) return "v2 bag count is unreasonable";

                    int largestId = 99;
                    var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var record in parsed.Bags)
                    {
                        if (record == null) return "v2 bag record is null";
                        if (record.Items == null) return $"{record.Id} item list is null";
                        if (record.Items.Count > MaxReasonableItemsPerBag)
                            return $"{record.Id} contains too many item records";
                        if (record.Name == null) return $"{record.Id} name is null";
                        if (record.Name.Length > 256) return $"{record.Id} name is too long";
                        if (!string.IsNullOrEmpty(record.Name) &&
                            !BagDefs.ContainsKey(record.Name))
                            return $"{record.Id} has unknown bag type '{record.Name}'";
                        if (IsCorpseBag(record.Name) && record.Items.Count == 0)
                            return $"{record.Id} is an empty recovery container";

                        var idMatch = Regex.Match(record.Id ?? "", "^bag([0-9]+)$",
                            RegexOptions.IgnoreCase);
                        if (!idMatch.Success) return $"invalid bag identifier '{record.Id}'";
                        if (!int.TryParse(idMatch.Groups[1].Value, out int number))
                            return $"bag identifier '{record.Id}' is too large";
                        if (number < 100 || number == int.MaxValue)
                            return $"bag identifier '{record.Id}' is outside the valid range";
                        if (!ids.Add(record.Id))
                            return $"duplicate bag identifier '{record.Id}'";
                        largestId = Math.Max(largestId, number);

                        foreach (var item in record.Items)
                        {
                            if (item == null) return $"{record.Id} contains a null item";
                            if (string.IsNullOrWhiteSpace(item.ItemName))
                                return $"{record.Id} contains an unnamed item";
                            if (item.ItemName.Length > 512)
                                return $"{record.Id} contains an overlong item name";
                            if (item.ItemId == null)
                                return $"{record.Id}/{item.ItemName} has a null item ID";
                            if (item.ItemId.Length > 512)
                                return $"{record.Id}/{item.ItemName} has an overlong item ID";
                            if (item.Quantity <= 0)
                                return $"{record.Id}/{item.ItemName} has invalid quantity {item.Quantity}";
                            if (item.IsStackable &&
                                item.Quantity > MaxReasonableStackQuantity)
                                return $"{record.Id}/{item.ItemName} stack is unreasonably large";
                            if (!item.IsStackable &&
                                item.Quantity > MaxReasonableItemQuality)
                                return $"{record.Id}/{item.ItemName} quality is outside the valid range";
                            if ((item.ExactState?.Length ?? 0) >
                                MaxExactStateLength)
                                return $"{record.Id}/{item.ItemName} exact state is too large";
                            if (!string.IsNullOrEmpty(item.ExactState))
                            {
                                if (!IsThrallBag(record.Name))
                                    return $"{record.Id}/{item.ItemName} has exact " +
                                        "thrall state in a non-Thrall Bag";
                                if (!Necromancer.NecromancerPetEquipment
                                    .TryValidateExactStatePayload(
                                        item.ExactState, item.ItemId,
                                        item.ItemName, item.Quantity,
                                        out string exactError))
                                    return $"{record.Id}/{item.ItemName} exact " +
                                        $"state is invalid: {exactError}";
                            }
                        }
                    }
                    return parsed.NextBagCounter > largestId
                        ? null
                        : $"next bag counter {parsed.NextBagCounter} does not exceed bag{largestId}";
                }
                catch (Exception ex) { return "v2 parse failed: " + ex.Message; }
            }
            return IsLegacyDataValid(trimmed) ? null : "legacy bag data is malformed";
        }

        public static bool DeserializeBags(string json)
        {
            var oldBags = _bags;
            var oldBagNames = _bagNames;
            var oldReservedBagNumbers = _reservedBagNumbers;
            int oldNextBagCounter = _nextBagCounter;
            if (string.IsNullOrWhiteSpace(json) || json.Trim() == "{}")
            {
                _bags = new Dictionary<string, List<BagSlotData>>(
                    StringComparer.OrdinalIgnoreCase);
                _bagNames = new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase);
                _reservedBagNumbers = new HashSet<int>();
                _nextBagCounter = 100;
                return true;
            }
            try
            {
                string validationError = GetSerializedDataValidationError(json);
                if (validationError != null)
                    throw new InvalidDataException(
                        "Bag save failed structural validation: " + validationError);

                // Build the replacement state separately. If parsing or item
                // validation fails, the live bag state remains untouched.
                _bags = new Dictionary<string, List<BagSlotData>>(
                    StringComparer.OrdinalIgnoreCase);
                _bagNames = new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase);
                _reservedBagNumbers = new HashSet<int>();
                _nextBagCounter = 100;
                if (json.Contains("\"Version\""))
                {
                    var envelope = ParseV2Envelope(json);
                    if (envelope == null || envelope.Version != SaveFormatVersion || envelope.Bags == null)
                        throw new InvalidDataException("Unsupported or incomplete bag save format.");
                    _nextBagCounter = envelope.NextBagCounter;
                    foreach (var record in envelope.Bags)
                    {
                        var idMatch = Regex.Match(record.Id, "^bag([0-9]+)$",
                            RegexOptions.IgnoreCase);
                        string bagId = $"bag{int.Parse(idMatch.Groups[1].Value)}";
                        var contents = new List<BagSlotData>();
                        if (_bags.ContainsKey(bagId))
                            throw new InvalidDataException($"Duplicate bag identifier '{bagId}'.");
                        _bags.Add(bagId, contents);
                        if (!string.IsNullOrEmpty(record.Name)) _bagNames[bagId] = record.Name;
                        AppendValidated(contents, record.Items, bagId);
                        ReserveId(bagId);
                    }
                }
                else
                {
                    DeserializeLegacyBags(json);
                }
                RecalculateCounter();
                return true;
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogWarning($"BagSystem deserialize error: {ex.Message}");
                _bags = oldBags;
                _bagNames = oldBagNames;
                _reservedBagNumbers = oldReservedBagNumbers;
                _nextBagCounter = oldNextBagCounter;
                return false;
            }
        }

        public static string EnsureBagIdentity(ItemIcon requestedSlot)
        {
            if (requestedSlot?.MyItem == null || !IsBag(requestedSlot.MyItem) ||
                !IsDirectInventorySlot(requestedSlot)) return null;
            string beforeBags = SerializeBags();
            InventorySnapshot beforeInventory = CaptureInventory();
            bool changed = ReconcileInventoryBagIdsInternal();
            int number = requestedSlot.Quantity;
            if (number < 100)
            {
                number = NextBagNumber();
                requestedSlot.Quantity = number;
                requestedSlot.UpdateSlotImage();
                changed = true;
            }
            string id = $"bag{number}";
            if (!_bags.ContainsKey(id)) { _bags[id] = new List<BagSlotData>(); changed = true; }
            _bagNames[id] = requestedSlot.MyItem.ItemName;
            ReserveId(id);

            if (changed && !PersistMutation("assign-id", beforeBags, beforeInventory))
            {
                RestoreAfterFailedMutation(beforeBags, beforeInventory);
                return null;
            }
            return id;
        }

        public static void ReconcileInventoryBagIds()
        {
            string beforeBags = SerializeBags();
            InventorySnapshot beforeInventory = CaptureInventory();
            if (!ReconcileInventoryBagIdsInternal()) return;
            if (!PersistMutation("reconcile-ids", beforeBags, beforeInventory))
                RestoreAfterFailedMutation(beforeBags, beforeInventory);
        }

        /// <summary>
        /// Capture inventory and all bag contents for a larger synchronous
        /// operation such as crafting or deconstruction.
        /// </summary>
        internal static MutationSnapshot BeginExternalMutation()
        {
            return new MutationSnapshot
            {
                BeforeBags = SerializeBags(),
                BeforeInventory = CaptureInventory()
            };
        }

        internal static MutationSnapshot BeginExternalBagMove(
            ItemIcon movingSlot)
        {
            MutationSnapshot snapshot = BeginExternalMutation();
            var inventory = GameData.PlayerInv;
            if (snapshot?.BeforeInventory == null || inventory?.StoredSlots == null ||
                movingSlot?.MyItem == null || !IsBag(movingSlot.MyItem))
                return snapshot;

            ItemIcon home = IsDirectInventorySlot(movingSlot)
                ? movingSlot : GetMouseHomeSlot(movingSlot);
            int index = home == null ? -1 : inventory.StoredSlots.IndexOf(home);
            if (index < 0 || index >= snapshot.BeforeInventory.Items.Count)
                return snapshot;

            // During a drag Erenshor has already cleared the physical home
            // slot and put the bag on MouseSlot. Reconstruct the true pre-drag
            // snapshot so a failed bank/trash commit restores the one original
            // bag instead of losing it.
            snapshot.BeforeInventory.Items[index] = movingSlot.MyItem;
            snapshot.BeforeInventory.Quantities[index] =
                Mathf.Max(1, movingSlot.Quantity);
            snapshot.BeforeInventory.Fingerprint =
                GetInventorySnapshotFingerprint(snapshot.BeforeInventory);
            return snapshot;
        }

        internal static bool CommitExternalMutation(string operation,
            MutationSnapshot snapshot, string bankPath = null,
            string bankBefore = null, string bankAfter = null,
            string auxiliaryPath = null, string auxiliaryBefore = null,
            string auxiliaryAfter = null,
            bool forceFailureAfterBankWriteForAudit = false)
        {
            if (snapshot == null) return false;
            return PersistMutation(operation, snapshot.BeforeBags,
                snapshot.BeforeInventory, bankPath, bankBefore, bankAfter,
                auxiliaryPath, auxiliaryBefore, auxiliaryAfter,
                forceFailureAfterBankWriteForAudit);
        }

        internal static void RollbackExternalMutation(MutationSnapshot snapshot,
            string bankPath = null, string bankBefore = null,
            string auxiliaryPath = null, string auxiliaryBefore = null)
        {
            if (snapshot == null) return;
            RestoreAfterFailedMutation(snapshot.BeforeBags,
                snapshot.BeforeInventory, bankPath, bankBefore,
                auxiliaryPath, auxiliaryBefore);
        }

        internal static void ClearCursorAfterExternalRollback()
        {
            ClearNativeCursor();
        }

        /// <summary>
        /// Exercise Erenshor's real AddItemToInv path for one item, then restore
        /// the exact in-memory inventory snapshot before returning. This is used
        /// only by the opt-in smoke tester and never writes the temporary item.
        /// </summary>
        internal static bool ProbeInventoryRoundTrip(Item item, out string error)
        {
            error = null;
            InventorySnapshot snapshot = null;
            bool result = false;
            try
            {
                if (item == null) throw new ArgumentNullException(nameof(item));
                var inventory = GameData.PlayerInv;
                var slots = inventory?.StoredSlots;
                if (inventory == null || slots == null || inventory.Empty == null)
                    throw new InvalidOperationException("player inventory is unavailable");

                ItemIcon scratch = null;
                foreach (var slot in slots)
                {
                    if (IsDirectInventorySlot(slot))
                    {
                        scratch = slot;
                        if (slot.MyItem == inventory.Empty) break;
                    }
                }
                if (scratch == null)
                    throw new InvalidOperationException("no direct inventory slot is available");

                snapshot = CaptureInventory();
                scratch.MyItem = inventory.Empty;
                scratch.Quantity = 1;
                scratch.UpdateSlotImage();

                long before = CountItemReference(item);
                int amount = item.Stackable ? 3 : 1;
                if (!inventory.AddItemToInv(item, amount))
                    throw new InvalidOperationException("AddItemToInv returned false");
                long after = CountItemReference(item);
                if (after - before != amount)
                    throw new InvalidOperationException(
                        $"inventory delta was {after - before}, expected {amount}");
                result = true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }
            finally
            {
                if (snapshot != null)
                {
                    RestoreInventory(snapshot);
                    string restored = GetInventoryFingerprint();
                    if (restored != snapshot.Fingerprint)
                    {
                        result = false;
                        error = string.IsNullOrEmpty(error)
                            ? "inventory fingerprint did not restore exactly"
                            : error + "; inventory fingerprint did not restore exactly";
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// Exercise bag admission, v2 serialization, validation, deserialization,
        /// and ID resolution for one item. The original in-memory bag state is
        /// restored exactly and is never written by this probe.
        /// </summary>
        internal static bool ProbeBagRoundTrip(Item item, out string error)
        {
            error = null;
            string before = SerializeBags();
            bool result = false;
            bool stateMutated = false;
            try
            {
                if (item == null) throw new ArgumentNullException(nameof(item));
                string validationError = GetSerializedDataValidationError(before);
                if (validationError != null)
                    throw new InvalidDataException(
                        "current bag state is invalid: " + validationError);
                if (BagWindow.AnyOpen)
                    throw new InvalidOperationException("close all bag windows before the smoke run");

                // Nested bags are intentionally rejected; that invariant is the
                // successful outcome for testing bag items themselves.
                if (IsBag(item))
                {
                    result = true;
                }
                else
                {
                    stateMutated = true;
                    int number = NextBagNumber();
                    string bagId = $"bag{number}";
                    int quantity = item.Stackable ? 3 : 1;
                    if (!TryAddItemToBag(bagId, "Adventurer's Backpack",
                        item, quantity, item.Stackable))
                        throw new InvalidOperationException("bag admission returned false");

                    string probe = SerializeBags();
                    string probeValidationError =
                        GetSerializedDataValidationError(probe);
                    if (probeValidationError != null)
                        throw new InvalidDataException(
                            "serialized probe failed validation: " +
                            probeValidationError + "; data=" +
                            probe.Replace(Environment.NewLine, " "));
                    if (!DeserializeBags(probe))
                        throw new InvalidDataException("serialized probe failed to load");

                    var contents = GetBagContents(bagId);
                    if (contents.Count != 1)
                        throw new InvalidDataException(
                            $"loaded bag contained {contents.Count} records, expected 1");
                    var record = contents[0];
                    if (!string.Equals(record.ItemName, item.ItemName,
                            StringComparison.Ordinal) ||
                        !string.Equals(record.ItemId ?? "", item.Id ?? item.ItemName,
                            StringComparison.OrdinalIgnoreCase) ||
                        record.Quantity != quantity ||
                        record.IsStackable != item.Stackable)
                        throw new InvalidDataException("loaded item record changed");
                    Item resolved = ResolveBagItem(record);
                    if (resolved == null || resolved == GameData.PlayerInv?.Empty ||
                        !string.Equals(resolved.Id ?? resolved.ItemName,
                            item.Id ?? item.ItemName, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("loaded item ID did not resolve");

                    result = true;
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }
            finally
            {
                bool restored = !stateMutated ||
                    (DeserializeBags(before) &&
                    string.Equals(SerializeBags(), before, StringComparison.Ordinal));
                if (!restored)
                {
                    result = false;
                    error = string.IsNullOrEmpty(error)
                        ? "original bag state did not restore exactly"
                        : error + "; original bag state did not restore exactly";
                }
                BagWindow.RefreshAll();
            }
            return result;
        }

        private static long CountItemReference(Item item)
        {
            long total = 0;
            var slots = GameData.PlayerInv?.StoredSlots;
            if (item == null || slots == null) return total;
            foreach (var slot in slots)
            {
                if (slot?.MyItem != item) continue;
                total += item.Stackable ? Mathf.Max(1, slot.Quantity) : 1;
            }
            return total;
        }

        public static bool RecoverPendingTransaction(string bagsPath)
        {
            string path = GetTransactionPath();
            if (string.IsNullOrEmpty(path)) return true;
            if (!File.Exists(path))
            {
                // A backup without a primary can only be an older journal that
                // was retained after its newer transaction completed.
                try
                {
                    string staleBackup = path + ".bak";
                    if (File.Exists(staleBackup)) File.Delete(staleBackup);
                }
                catch (Exception ex)
                {
                    SkillsPlugin.Log.LogWarning($"Could not remove a stale bag journal backup: {ex.Message}");
                }
                return true;
            }
            try
            {
                string json = File.ReadAllText(path);
                var record = JsonUtility.FromJson<BagTransactionRecord>(json);
                if (record == null ||
                    (record.Version != 1 && record.Version != 2) ||
                    !string.Equals(record.Character, SkillsSaveManager.CharName,
                        StringComparison.Ordinal) ||
                    string.IsNullOrEmpty(record.BeforeBags) ||
                    string.IsNullOrEmpty(record.AfterBags) ||
                    string.IsNullOrEmpty(record.InventoryBefore) ||
                    string.IsNullOrEmpty(record.InventoryAfter) ||
                    record.InventoryBefore == "unavailable" ||
                    record.InventoryAfter == "unavailable" ||
                    !IsSerializedDataValid(record.BeforeBags) ||
                    !IsSerializedDataValid(record.AfterBags))
                    throw new InvalidDataException("Bag transaction journal is incomplete.");

                string current = GetInventoryFingerprint();
                bool inventoryChanged = record.InventoryBefore != record.InventoryAfter;
                bool externalTracked = !string.IsNullOrEmpty(record.BankPath);
                bool auxiliaryTracked = record.Version >= 2 &&
                    !string.IsNullOrEmpty(record.AuxiliaryPath);
                if (!externalTracked &&
                    (!string.IsNullOrEmpty(record.BankBefore) ||
                     !string.IsNullOrEmpty(record.BankAfter)))
                    throw new InvalidDataException(
                        "Bag journal has external data without a tracked path.");
                if (externalTracked && (!IsSafeTrackedPath(record.BankPath) ||
                    !IsTrackedDataValid(record.BankPath, record.BankBefore) ||
                    !IsTrackedDataValid(record.BankPath, record.BankAfter)))
                    throw new InvalidDataException("Bag journal contains invalid external data.");
                if (!auxiliaryTracked && record.Version >= 2 &&
                    (!string.IsNullOrEmpty(record.AuxiliaryBefore) ||
                     !string.IsNullOrEmpty(record.AuxiliaryAfter)))
                    throw new InvalidDataException(
                        "Bag journal has auxiliary data without a tracked path.");
                if (auxiliaryTracked &&
                    (!externalTracked ||
                     !IsSafeTrackedPath(record.AuxiliaryPath) ||
                     !IsTrackedDataValid(record.AuxiliaryPath,
                         record.AuxiliaryBefore) ||
                     !IsTrackedDataValid(record.AuxiliaryPath,
                         record.AuxiliaryAfter) ||
                     string.Equals(Path.GetFullPath(record.BankPath ?? ""),
                         Path.GetFullPath(record.AuxiliaryPath),
                         StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException(
                        "Bag journal contains invalid auxiliary data.");
                bool externalChanged = externalTracked &&
                    record.BankBefore != record.BankAfter;
                bool auxiliaryChanged = auxiliaryTracked &&
                    record.AuxiliaryBefore != record.AuxiliaryAfter;
                bool bagsChanged = record.BeforeBags != record.AfterBags;
                bool completed;
                if (inventoryChanged)
                {
                    if (current == record.InventoryAfter) completed = true;
                    else if (current == record.InventoryBefore) completed = false;
                    else throw new InvalidDataException(
                        "Native inventory does not match either side of the interrupted transaction.");
                }
                else if (externalChanged)
                {
                    string currentExternal = File.Exists(record.BankPath)
                        ? File.ReadAllText(record.BankPath) : "";
                    if (currentExternal == record.BankAfter) completed = true;
                    else if (currentExternal == record.BankBefore) completed = false;
                    else throw new InvalidDataException(
                        "Tracked external data does not match either side of the interrupted transaction.");
                }
                else if (auxiliaryChanged)
                {
                    string currentAuxiliary = File.Exists(record.AuxiliaryPath)
                        ? File.ReadAllText(record.AuxiliaryPath) : "";
                    if (currentAuxiliary == record.AuxiliaryAfter) completed = true;
                    else if (currentAuxiliary == record.AuxiliaryBefore) completed = false;
                    else throw new InvalidDataException(
                        "Tracked auxiliary data does not match either side of the interrupted transaction.");
                }
                else if (bagsChanged)
                {
                    string currentBags = File.Exists(bagsPath)
                        ? File.ReadAllText(bagsPath) : "";
                    if (currentBags == record.AfterBags) completed = true;
                    else if (currentBags == record.BeforeBags) completed = false;
                    else throw new InvalidDataException(
                        "Bag data does not match either side of the interrupted transaction.");
                }
                else
                {
                    completed = true;
                }
                string chosen = completed ? record.AfterBags : record.BeforeBags;
                if (!IsSerializedDataValid(chosen))
                    throw new InvalidDataException("Bag transaction recovery data is invalid.");
                if (!SafeFileIO.TryWriteAllTextAtomic(bagsPath, chosen, out string writeError))
                    throw new IOException(writeError);
                if (externalTracked)
                {
                    string chosenExternal = completed ? record.BankAfter : record.BankBefore;
                    if (!SafeFileIO.TryWriteAllTextAtomic(record.BankPath, chosenExternal,
                        out string externalError))
                        throw new IOException(externalError);
                }
                if (auxiliaryTracked)
                {
                    string chosenAuxiliary = completed
                        ? record.AuxiliaryAfter : record.AuxiliaryBefore;
                    if (!SafeFileIO.TryWriteAllTextAtomic(
                        record.AuxiliaryPath, chosenAuxiliary,
                        out string auxiliaryError))
                        throw new IOException(auxiliaryError);
                }
                DeleteJournalFiles(path);
                _activeTransactionId = null;
                SkillsPlugin.Log.LogWarning($"Recovered interrupted bag transaction '{record.Operation}' using the {(completed ? "completed" : "rolled-back")} state.");
                return true;
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogError($"Bag transaction recovery failed; journal retained: {ex.Message}");
                return false;
            }
        }

        public static void Reset()
        {
            _bags.Clear(); _bagNames.Clear(); _reservedBagNumbers.Clear(); _nextBagCounter = 100;
            BagWindow.CloseAll();
        }

        private static bool ReconcileInventoryBagIdsInternal()
        {
            var inv = GameData.PlayerInv;
            if (inv?.StoredSlots == null) return false;
            bool changed = false;
            var seen = new HashSet<int>();
            // Protect every valid identity already present before assigning an
            // ID to a recovered bag.  This prevents an orphan record chosen for
            // an earlier slot from stealing the contents of a later live bag.
            var liveIds = new HashSet<int>();
            foreach (var candidate in inv.StoredSlots)
            {
                if (candidate?.MyItem == null || candidate.MyItem == inv.Empty ||
                    !IsBag(candidate.MyItem) || candidate.Quantity < 100) continue;
                liveIds.Add(candidate.Quantity);
            }
            foreach (var slot in inv.StoredSlots)
            {
                if (slot?.MyItem == null || slot.MyItem == inv.Empty || !IsBag(slot.MyItem)) continue;
                int number = slot.Quantity;
                if (number < 100 || seen.Contains(number))
                {
                    int old = number;
                    number = FindRecoverableOrphanBagId(
                        slot.MyItem.ItemName, liveIds, seen);
                    if (number < 100) number = NextBagNumber();
                    slot.Quantity = number;
                    slot.UpdateSlotImage();
                    changed = true;
                    if (old >= 100)
                        SkillsPlugin.Log.LogWarning($"Duplicate bag ID bag{old} repaired as bag{number}; the original retains its contents.");
                    else if (_bags.ContainsKey($"bag{number}"))
                        SkillsPlugin.Log.LogWarning(
                            $"Recovered {slot.MyItem.ItemName} identity bag{number} " +
                            "from the validated bag sidecar.");
                }
                seen.Add(number);
                string id = $"bag{number}";
                ReserveId(id);
                if (!_bags.ContainsKey(id)) { _bags[id] = new List<BagSlotData>(); changed = true; }
                _bagNames[id] = slot.MyItem.ItemName;
            }
            return changed;
        }

        private static int FindRecoverableOrphanBagId(string bagName,
            HashSet<int> liveIds, HashSet<int> seen)
        {
            int fallback = -1;
            foreach (string id in _bags.Keys)
            {
                Match match = Regex.Match(id ?? "", "^bag([0-9]+)$",
                    RegexOptions.IgnoreCase);
                if (!match.Success || !int.TryParse(match.Groups[1].Value,
                    out int number) || number < 100 || liveIds.Contains(number) ||
                    seen.Contains(number)) continue;
                if (_bagNames.TryGetValue(id, out string savedName) &&
                    string.Equals(savedName, bagName,
                        StringComparison.OrdinalIgnoreCase)) return number;
                if (fallback < 100 || number < fallback) fallback = number;
            }
            return fallback;
        }

        private static bool PersistMutation(string operation, string beforeBags,
            InventorySnapshot beforeInventory, string bankPath = null,
            string bankBefore = null, string bankAfter = null,
            string auxiliaryPath = null, string auxiliaryBefore = null,
            string auxiliaryAfter = null,
            bool forceFailureAfterBankWriteForAudit = false)
        {
            if (beforeInventory == null || string.IsNullOrEmpty(SkillsSaveManager.CharName)) return false;
            string afterBags = SerializeBags();
            if (string.IsNullOrEmpty(beforeInventory.Fingerprint) ||
                beforeInventory.Fingerprint == "unavailable")
            {
                SkillsPlugin.Log.LogError(
                    "Refusing a transaction because native inventory is unavailable.");
                return false;
            }
            if (!IsSerializedDataValid(beforeBags) || !IsSerializedDataValid(afterBags))
            {
                SkillsPlugin.Log.LogError("Refusing a transaction with invalid bag data.");
                return false;
            }
            bool bankTracked = !string.IsNullOrEmpty(bankPath);
            if (bankTracked && (!IsSafeTrackedPath(bankPath) ||
                !IsTrackedDataValid(bankPath, bankBefore) ||
                !IsTrackedDataValid(bankPath, bankAfter)))
            {
                SkillsPlugin.Log.LogError("Refusing an invalid external-file transaction.");
                return false;
            }
            bool auxiliaryTracked = !string.IsNullOrEmpty(auxiliaryPath);
            if (auxiliaryTracked && (!bankTracked ||
                !IsSafeTrackedPath(auxiliaryPath) ||
                !IsTrackedDataValid(auxiliaryPath, auxiliaryBefore) ||
                !IsTrackedDataValid(auxiliaryPath, auxiliaryAfter) ||
                string.Equals(Path.GetFullPath(bankPath),
                    Path.GetFullPath(auxiliaryPath),
                    StringComparison.OrdinalIgnoreCase)))
            {
                SkillsPlugin.Log.LogError(
                    "Refusing an invalid auxiliary-file transaction.");
                return false;
            }
            if (forceFailureAfterBankWriteForAudit &&
                SkillsPlugin.CfgEnableTestMode?.Value != true)
            {
                SkillsPlugin.Log.LogError(
                    "Refusing transaction failure injection outside test mode.");
                return false;
            }
            string path = GetTransactionPath();
            if (string.IsNullOrEmpty(path) || File.Exists(path) || File.Exists(path + ".bak"))
            {
                SkillsPlugin.Log.LogError("Refusing to overwrite an unresolved bag transaction journal.");
                return false;
            }
            string transactionId = Guid.NewGuid().ToString("N");
            var record = new BagTransactionRecord
            {
                TransactionId = transactionId,
                Character = SkillsSaveManager.CharName,
                Operation = operation,
                BeforeBags = beforeBags,
                AfterBags = afterBags,
                InventoryBefore = beforeInventory.Fingerprint,
                InventoryAfter = GetInventoryFingerprint(),
                BankPath = bankTracked ? bankPath : "",
                BankBefore = bankTracked ? bankBefore : "",
                BankAfter = bankTracked ? bankAfter : "",
                AuxiliaryPath = auxiliaryTracked ? auxiliaryPath : "",
                AuxiliaryBefore = auxiliaryTracked ? auxiliaryBefore : "",
                AuxiliaryAfter = auxiliaryTracked ? auxiliaryAfter : "",
                CreatedUtc = DateTime.UtcNow.ToString("o")
            };
            if (!SafeFileIO.TryWriteAllTextAtomic(path, JsonUtility.ToJson(record), out string journalError))
            {
                SkillsPlugin.Log.LogError($"Bag transaction journal failed: {journalError}");
                return false;
            }
            _activeTransactionId = transactionId;
            if (!SkillsSaveManager.SaveBagsFile()) return false;
            if (!TrySaveNativeInventory()) return false;
            if (bankTracked && !SafeFileIO.TryWriteAllTextAtomic(bankPath,
                bankAfter, out string bankError))
            {
                SkillsPlugin.Log.LogError($"External transaction save failed: {bankError}");
                return false;
            }
            if (forceFailureAfterBankWriteForAudit)
            {
                SkillsPlugin.Log.LogWarning(
                    "Injected a post-bank-write transaction failure for rollback QA.");
                return false;
            }
            if (auxiliaryTracked && !SafeFileIO.TryWriteAllTextAtomic(
                auxiliaryPath, auxiliaryAfter, out string auxiliaryError))
            {
                SkillsPlugin.Log.LogError(
                    $"Auxiliary transaction save failed: {auxiliaryError}");
                return false;
            }
            DeleteJournalFiles(path);
            _activeTransactionId = null;
            return true;
        }

        private static bool TrySaveNativeInventory()
        {
            try
            {
                if (GameData.GM == null) throw new InvalidOperationException("Game save manager is unavailable.");
                GameData.GM.SaveGameData(wScene: false);
                return true;
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogError($"Native inventory save failed: {ex.Message}");
                return false;
            }
        }

        private static void RestoreAfterFailedMutation(string beforeBags,
            InventorySnapshot beforeInventory, string bankPath = null,
            string bankBefore = null, string auxiliaryPath = null,
            string auxiliaryBefore = null)
        {
            DeserializeBags(beforeBags);
            RestoreInventory(beforeInventory);
            bool bagSaved = SkillsSaveManager.SaveBagsFile();
            bool nativeSaved = TrySaveNativeInventory();
            bool bankSaved = true;
            if (!string.IsNullOrEmpty(bankPath))
            {
                string bankError = null;
                bankSaved = IsSafeTrackedPath(bankPath) &&
                    IsTrackedDataValid(bankPath, bankBefore);
                if (bankSaved)
                    bankSaved = SafeFileIO.TryWriteAllTextAtomic(bankPath,
                        bankBefore, out bankError);
                if (!bankSaved)
                    SkillsPlugin.Log.LogError($"External rollback failed: {bankError ?? "unsafe path or data"}");
            }
            bool auxiliarySaved = true;
            if (!string.IsNullOrEmpty(auxiliaryPath))
            {
                string auxiliaryError = null;
                auxiliarySaved = IsSafeTrackedPath(auxiliaryPath) &&
                    IsTrackedDataValid(auxiliaryPath, auxiliaryBefore);
                if (auxiliarySaved)
                    auxiliarySaved = SafeFileIO.TryWriteAllTextAtomic(
                        auxiliaryPath, auxiliaryBefore,
                        out auxiliaryError);
                if (!auxiliarySaved)
                    SkillsPlugin.Log.LogError(
                        $"Auxiliary rollback failed: " +
                        $"{auxiliaryError ?? "unsafe path or data"}");
            }
            if (bagSaved && nativeSaved && bankSaved && auxiliarySaved)
                ClearActiveJournal();
            BagWindow.RefreshAll();
        }

        private static InventorySnapshot CaptureInventory()
        {
            var snapshot = new InventorySnapshot();
            var slots = GameData.PlayerInv?.StoredSlots;
            snapshot.Gold = GameData.PlayerInv?.Gold ?? 0;
            if (slots != null)
            {
                foreach (var slot in slots)
                {
                    snapshot.Items.Add(slot?.MyItem);
                    snapshot.Quantities.Add(slot == null ? 1 : Mathf.Max(1, slot.Quantity));
                }
            }
            snapshot.Fingerprint = GetInventoryFingerprint();
            return snapshot;
        }

        private static void ClearNativeCursor()
        {
            var mouse = GameData.MouseSlot;
            if (mouse != null)
            {
                mouse.MyItem = GameData.PlayerInv.Empty;
                mouse.Quantity = 1;
                mouse.dragging = false;
                MouseHomeSlotField?.SetValue(mouse, null);
                mouse.UpdateSlotImage();
            }
            GameData.ItemOnCursor = null;
            GameData.HighlightedItem = null;
            GameData.PlayerInv.UpdatePlayerInventory();
        }

        private static ItemIcon GetMouseHomeSlot(ItemIcon mouse)
        {
            if (mouse == null) return null;
            if (GameData.ItemOnCursor != null &&
                !ReferenceEquals(GameData.ItemOnCursor, mouse) &&
                IsDirectInventorySlot(GameData.ItemOnCursor))
                return GameData.ItemOnCursor;
            return MouseHomeSlotField?.GetValue(mouse) as ItemIcon;
        }

        private static string GetInventorySnapshotFingerprint(InventorySnapshot snapshot)
        {
            if (snapshot == null || snapshot.Items.Count != snapshot.Quantities.Count)
                return "unavailable";
            var sb = new StringBuilder();
            sb.Append("G:").Append(Mathf.Max(0, snapshot.Gold)).Append(';');
            for (int i = 0; i < snapshot.Items.Count; i++)
            {
                Item item = snapshot.Items[i];
                string id = item?.Id ?? item?.ItemName ?? "<null>";
                int quantity = Mathf.Max(1, snapshot.Quantities[i]);
                sb.Append(id.Length).Append(':').Append(id).Append(':')
                    .Append(quantity).Append(';');
            }
            return sb.ToString();
        }

        private static void RestoreInventory(InventorySnapshot snapshot)
        {
            var slots = GameData.PlayerInv?.StoredSlots;
            if (snapshot == null || slots == null) return;
            GameData.PlayerInv.Gold = Mathf.Max(0, snapshot.Gold);
            int count = Mathf.Min(slots.Count, snapshot.Items.Count);
            for (int i = 0; i < count; i++)
            {
                if (slots[i] == null) continue;
                slots[i].MyItem = snapshot.Items[i] ?? GameData.PlayerInv.Empty;
                slots[i].Quantity = Mathf.Max(1, snapshot.Quantities[i]);
                slots[i].UpdateSlotImage();
            }
            GameData.PlayerInv.UpdatePlayerInventory();
        }

        private static string GetInventoryFingerprint()
        {
            var sb = new System.Text.StringBuilder();
            var slots = GameData.PlayerInv?.StoredSlots;
            if (slots == null) return "unavailable";
            sb.Append("G:").Append(GameData.PlayerInv?.Gold ?? 0).Append(';');
            foreach (var slot in slots)
            {
                string id = slot?.MyItem?.Id ?? slot?.MyItem?.ItemName ?? "<null>";
                int quantity = slot == null ? 1 : Mathf.Max(1, slot.Quantity);
                sb.Append(id.Length).Append(':').Append(id).Append(':').Append(quantity).Append(';');
            }
            return sb.ToString();
        }

        private static string GetTransactionPath()
        {
            if (string.IsNullOrEmpty(SkillsSaveManager.CharName)) return null;
            return Path.Combine(RuntimePaths.DataDirectory,
                $"{SkillsSaveManager.CharName}_bags.txn.json");
        }

        private static bool IsSafeTrackedPath(string path)
        {
            try
            {
                string root = Path.GetFullPath(Application.persistentDataPath)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string full = Path.GetFullPath(path ?? "");
                string currentBank = Path.Combine(root, "ESSaveData", "GBDATA");
                string legacyBank = Path.Combine(root, "GBDATA");
                bool bankPath = string.Equals(full, currentBank,
                    StringComparison.OrdinalIgnoreCase) || string.Equals(full, legacyBank,
                    StringComparison.OrdinalIgnoreCase);
                string bankBags = Path.GetFullPath(
                    BankBagStorage.GetPath());
                string simRecipes = Path.GetFullPath(Path.Combine(
                    RuntimePaths.DataDirectory, "sim_recipes.json"));
                string playerRecipes = Path.GetFullPath(
                    SkillsSaveManager.GetRecipesPath());
                string petGearPath = Necromancer.NecromancerPetEquipment.GetPath();
                string petGear = string.IsNullOrEmpty(petGearPath)
                    ? null : Path.GetFullPath(petGearPath);
                return bankPath ||
                    string.Equals(full, bankBags,
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(full, simRecipes, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(full, playerRecipes, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrEmpty(petGear) && string.Equals(full, petGear,
                        StringComparison.OrdinalIgnoreCase));
            }
            catch { return false; }
        }

        private static bool IsGlobalBankDataPath(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path)) return false;
                string root = Path.GetFullPath(Application.persistentDataPath);
                string full = Path.GetFullPath(path);
                return string.Equals(full, Path.Combine(
                        root, "ESSaveData", "GBDATA"),
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(full, Path.Combine(root, "GBDATA"),
                        StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        private static bool IsSharedBankBagPath(string path)
        {
            try
            {
                return !string.IsNullOrWhiteSpace(path) &&
                    string.Equals(Path.GetFullPath(path),
                        Path.GetFullPath(BankBagStorage.GetPath()),
                        StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        private static bool IsTrackedDataValid(string path, string json)
        {
            if (!IsSafeTrackedPath(path)) return false;
            string full = Path.GetFullPath(path);
            if (string.Equals(Path.GetFileName(full), "GBDATA",
                StringComparison.OrdinalIgnoreCase))
                return Skills.Tradeskills.IsBankJsonValid(json);
            string bankBags = Path.GetFullPath(BankBagStorage.GetPath());
            if (string.Equals(full, bankBags,
                StringComparison.OrdinalIgnoreCase))
                return BankBagStorage.IsSerializedDataValid(json);
            string simRecipes = Path.GetFullPath(Path.Combine(
                RuntimePaths.DataDirectory, "sim_recipes.json"));
            if (string.Equals(full, simRecipes, StringComparison.OrdinalIgnoreCase))
                return Skills.SimPlayerTradeskills.IsSimRecipesJsonValid(json);
            string petGearPath = Necromancer.NecromancerPetEquipment.GetPath();
            if (!string.IsNullOrEmpty(petGearPath) && string.Equals(full,
                Path.GetFullPath(petGearPath), StringComparison.OrdinalIgnoreCase))
                return Necromancer.NecromancerPetEquipment.IsSerializedDataValid(json);
            return SkillsSaveManager.IsRecipesJsonValid(json);
        }

        private static void ClearActiveJournal()
        {
            if (string.IsNullOrEmpty(_activeTransactionId)) return;
            string path = GetTransactionPath();
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    var record = JsonUtility.FromJson<BagTransactionRecord>(File.ReadAllText(path));
                    if (record != null && record.TransactionId == _activeTransactionId)
                        DeleteJournalFiles(path);
                }
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogWarning($"Could not clear rolled-back bag journal: {ex.Message}");
                return;
            }
            _activeTransactionId = null;
        }

        private static void DeleteJournalFiles(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try { if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path); }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogWarning($"Could not delete bag transaction journal: {ex.Message}");
            }
            try
            {
                string backup = path + ".bak";
                if (File.Exists(backup)) File.Delete(backup);
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogWarning($"Could not delete bag journal backup: {ex.Message}");
            }
        }

        private static Item ResolveBagItem(BagSlotData data)
        {
            if (data == null) return null;
            if (!string.IsNullOrEmpty(data.ExactState))
            {
                if (!Necromancer.NecromancerPetEquipment
                    .TryValidateExactStatePayload(data.ExactState,
                        data.ItemId, data.ItemName, data.Quantity,
                        out _))
                    return null;
                return Necromancer.NecromancerPetEquipment
                    .ResolveExactStatePayload(data.ExactState);
            }
            Item item = null;
            if (!string.IsNullOrEmpty(data.ItemId))
                item = GameData.ItemDB?.GetItemByID(data.ItemId);
            if (item == null || item == GameData.PlayerInv?.Empty)
                item = ItemFactory.ResolveItem(data.ItemName);
            if (item == null || item == GameData.PlayerInv?.Empty ||
                !string.Equals(item.ItemName ?? "", data.ItemName ?? "",
                    StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrEmpty(data.ItemId) &&
                 !string.IsNullOrEmpty(item.Id) &&
                 !string.Equals(item.Id, data.ItemId,
                    StringComparison.OrdinalIgnoreCase)))
                return null;
            return item;
        }

        private static List<BagSlotData> CloneContents(List<BagSlotData> source)
        {
            var clone = new List<BagSlotData>();
            if (source == null) return clone;
            foreach (var item in source)
            {
                if (item == null) continue;
                clone.Add(new BagSlotData
                {
                    ItemId = item.ItemId ?? "",
                    ItemName = item.ItemName ?? "",
                    Quantity = item.Quantity,
                    IsStackable = item.IsStackable,
                    ExactState = item.ExactState ?? ""
                });
            }
            return clone;
        }

        private static void AppendValidated(List<BagSlotData> destination,
            List<BagSlotData> source, string bagId)
        {
            if (source == null) return;
            int count = 0;
            foreach (var item in source)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.ItemName)) continue;
                if (++count > MaxReasonableItemsPerBag)
                    throw new InvalidDataException($"{bagId} contains an unreasonable number of records.");
                destination.Add(new BagSlotData
                {
                    ItemId = item.ItemId ?? "",
                    ItemName = item.ItemName,
                    Quantity = item.Quantity,
                    IsStackable = item.IsStackable,
                    ExactState = item.ExactState ?? ""
                });
            }
        }

        private static bool IsLegacyDataValid(string json)
        {
            try
            {
                const string jsonString = "\\\"(?:\\\\.|[^\\\"\\\\])*\\\"";
                string item = "\\{\\s*\\\"i\\\"\\s*:\\s*(" + jsonString + ")\\s*,\\s*" +
                    "\\\"n\\\"\\s*:\\s*(" + jsonString + ")\\s*,\\s*" +
                    "\\\"q\\\"\\s*:\\s*([1-9][0-9]*)\\s*,\\s*" +
                    "\\\"s\\\"\\s*:\\s*([01])\\s*\\}";
                string items = "(?:" + item + "(?:\\s*,\\s*" + item + ")*)?";
                string bag = "\\\"bag([0-9]+)\\\"\\s*:\\s*\\[\\s*(?:" +
                    items + ")\\s*\\]";
                string document = "^\\{\\s*\\\"_ctr\\\"\\s*:\\s*([0-9]+)" +
                    "(?:\\s*,\\s*" + bag + ")*\\s*\\}$";
                var full = Regex.Match(json, document, RegexOptions.IgnoreCase);
                if (!full.Success || !int.TryParse(full.Groups[1].Value, out int counter) ||
                    counter < 100) return false;

                var ids = new HashSet<int>();
                int largestId = 99;
                foreach (Match match in Regex.Matches(json,
                    "\\\"bag([0-9]+)\\\"\\s*:\\s*\\[", RegexOptions.IgnoreCase))
                {
                    if (!int.TryParse(match.Groups[1].Value, out int number) ||
                        number < 100 || number == int.MaxValue || !ids.Add(number)) return false;
                    largestId = Math.Max(largestId, number);
                }
                if (counter <= largestId) return false;

                foreach (Match match in Regex.Matches(json, item))
                {
                    string encodedName = match.Groups[2].Value;
                    string name = DecodeJsonString(encodedName);
                    if (string.IsNullOrWhiteSpace(name) || name.Length > 512 ||
                        !int.TryParse(match.Groups[3].Value, out int quantity) ||
                        quantity <= 0 ||
                        (match.Groups[4].Value == "1" &&
                            quantity > MaxReasonableStackQuantity) ||
                        (match.Groups[4].Value == "0" &&
                            quantity > MaxReasonableItemQuality))
                        return false;
                }

                // Validate the per-bag item cap independently of the full regex.
                foreach (Match bagMatch in Regex.Matches(json,
                    "\\\"bag[0-9]+\\\"\\s*:\\s*\\[(.*?)\\]",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline))
                {
                    if (Regex.Matches(bagMatch.Groups[1].Value, item).Count >
                        MaxReasonableItemsPerBag) return false;
                }
                return true;
            }
            catch { return false; }
        }

        private static void DeserializeLegacyBags(string json)
        {
            var ctr = Regex.Match(json, "\\\"_ctr\\\"\\s*:\\s*(\\d+)");
            if (ctr.Success && int.TryParse(ctr.Groups[1].Value, out int parsedCounter))
                _nextBagCounter = Mathf.Max(100, parsedCounter);

            var matches = Regex.Matches(json,
                "\\\"(bag[0-9]+)\\\"\\s*:\\s*\\[([^\\]]*)\\]",
                RegexOptions.IgnoreCase);
            foreach (Match match in matches)
            {
                string rawId = match.Groups[1].Value;
                string bagId = NormalizeOrAllocateBagId(rawId);
                if (_bags.ContainsKey(bagId))
                    throw new InvalidDataException($"Duplicate legacy bag identifier '{bagId}'.");
                var contents = new List<BagSlotData>();
                foreach (Match item in Regex.Matches(match.Groups[2].Value,
                    "\\{\\s*\\\"i\\\"\\s*:\\s*(\\\"(?:\\\\.|[^\\\"\\\\])*\\\")\\s*,\\s*" +
                    "\\\"n\\\"\\s*:\\s*(\\\"(?:\\\\.|[^\\\"\\\\])*\\\")\\s*,\\s*" +
                    "\\\"q\\\"\\s*:\\s*([1-9][0-9]*)\\s*,\\s*" +
                    "\\\"s\\\"\\s*:\\s*([01])\\s*\\}"))
                {
                    int.TryParse(item.Groups[3].Value, out int quantity);
                    contents.Add(new BagSlotData
                    {
                        ItemId = DecodeJsonString(item.Groups[1].Value),
                        ItemName = DecodeJsonString(item.Groups[2].Value),
                        Quantity = quantity,
                        IsStackable = item.Groups[4].Value == "1"
                    });
                }
                _bags[bagId] = contents;
                ReserveId(bagId);
            }
        }

        private static string DecodeJsonString(string encoded)
        {
            if (string.IsNullOrEmpty(encoded) || encoded.Length < 2) return "";
            var result = new System.Text.StringBuilder(encoded.Length - 2);
            for (int i = 1; i < encoded.Length - 1; i++)
            {
                char c = encoded[i];
                if (c != '\\') { result.Append(c); continue; }
                if (++i >= encoded.Length - 1) throw new InvalidDataException("Incomplete JSON escape.");
                char escape = encoded[i];
                switch (escape)
                {
                    case '"': result.Append('"'); break;
                    case '\\': result.Append('\\'); break;
                    case '/': result.Append('/'); break;
                    case 'b': result.Append('\b'); break;
                    case 'f': result.Append('\f'); break;
                    case 'n': result.Append('\n'); break;
                    case 'r': result.Append('\r'); break;
                    case 't': result.Append('\t'); break;
                    case 'u':
                        if (i + 4 >= encoded.Length)
                            throw new InvalidDataException("Incomplete Unicode escape.");
                        string hex = encoded.Substring(i + 1, 4);
                        result.Append((char)Convert.ToInt32(hex, 16));
                        i += 4;
                        break;
                    default: throw new InvalidDataException("Invalid JSON escape.");
                }
            }
            return result.ToString();
        }

        private static string NormalizeOrAllocateBagId(string candidate)
        {
            if (!string.IsNullOrEmpty(candidate))
            {
                var match = Regex.Match(candidate, "^bag(\\d+)$", RegexOptions.IgnoreCase);
                if (match.Success && int.TryParse(match.Groups[1].Value, out int number) && number >= 100)
                {
                    string normalized = $"bag{number}";
                    ReserveId(normalized);
                    return normalized;
                }
            }
            return $"bag{NextBagNumber()}";
        }

        private static void ReserveId(string bagId)
        {
            var match = Regex.Match(bagId ?? "", "^bag(\\d+)$", RegexOptions.IgnoreCase);
            if (!match.Success || !int.TryParse(match.Groups[1].Value, out int number) || number < 100) return;
            _reservedBagNumbers.Add(number);
            if (number >= _nextBagCounter && number < int.MaxValue) _nextBagCounter = number + 1;
        }

        private static void RecalculateCounter()
        {
            foreach (string id in _bags.Keys) ReserveId(id);
            if (_nextBagCounter < 100) _nextBagCounter = 100;
        }
    }

    public static class BagWindow
    {
        static Canvas _canvas;
        static Dictionary<string, SingleBagWindow> _wins = new Dictionary<string, SingleBagWindow>();
        static string _lastBagId, _lastBagName;
        public static bool AnyOpen
        {
            get { CleanupDeadWindows(); return _wins.Count > 0; }
        }
        public static bool IsOpen => AnyOpen;
        public static string ActiveBagId => _lastBagId;
        public static string ActiveBagName => _lastBagName;
        public static string LastFocusedBagId => _lastBagId;
        public static string LastFocusedBagName => _lastBagName;

        static Canvas GetCanvas()
        {
            if (_canvas != null)
            {
                if (!_canvas.gameObject.activeSelf)
                    _canvas.gameObject.SetActive(true);
                return _canvas;
            }
            var go = new GameObject("BagCanvas");
            UnityEngine.Object.DontDestroyOnLoad(go);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.overrideSorting = true;
            _canvas.sortingOrder = 30000;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;
            go.AddComponent<GraphicRaycaster>();
            go.AddComponent<BagCursorOverlay>();
            return _canvas;
        }

        public static void Open(string bagId, string bagName)
        {
            EnsureOnTop();
            CleanupDeadWindows();
            _lastBagId = bagId; _lastBagName = bagName;
            if (_wins.TryGetValue(bagId, out var w))
            {
                if (w != null && w.IsAlive)
                {
                    w.BringToFront();
                    return;
                }
                _wins.Remove(bagId);
            }
            int slots = BagSystem.GetVisibleSlots(bagId, bagName);
            if (slots <= 0) return;
            int cols = Mathf.Min(slots,
                BagSystem.IsCorpseBag(bagName) ? 8 :
                    (BagSystem.IsThrallBag(bagName) ? 6 : 4));
            int rows = Mathf.CeilToInt((float)slots / cols);
            float ox = 200 + _wins.Count * 30;
            float oy = _wins.Count * -25;
            _wins[bagId] = new SingleBagWindow(GetCanvas().transform,
                bagId, bagName, cols, rows, ox, oy);
        }
        public static void CloseOne(string id)
        {
            if (!_wins.TryGetValue(id, out var w)) return;
            w?.Destroy();
            _wins.Remove(id);
        }
        public static void Close() => CloseAll();
        public static void CloseAll()
        {
            foreach (var window in new List<SingleBagWindow>(_wins.Values))
                window?.Destroy();
            _wins.Clear();
        }
        public static void Shutdown()
        {
            CloseAll();
            if (_canvas != null)
                UnityEngine.Object.Destroy(_canvas.gameObject);
            _canvas = null;
            _lastBagId = null;
            _lastBagName = null;
        }
        public static void RefreshAll()
        {
            CleanupDeadWindows();
            foreach (var window in new List<SingleBagWindow>(_wins.Values))
                if (window != null && window.IsAlive) window.Refresh();
        }
        public static void RefreshIfOpen() => RefreshAll();
        public static void SetFocus(string id, string n)
        {
            _lastBagId = id;
            _lastBagName = n;
            EnsureOnTop();
        }

        public static void EnsureOnTop()
        {
            Canvas canvas = GetCanvas();
            canvas.overrideSorting = true;
            canvas.sortingOrder = 30000;
            canvas.transform.SetAsLastSibling();
        }

        public static bool IsPointerOverAnyBag(Vector2 screenPosition)
        {
            CleanupDeadWindows();
            foreach (var pair in _wins)
                if (pair.Value != null && pair.Value.ContainsScreenPoint(screenPosition))
                    return true;
            return false;
        }

        private static void CleanupDeadWindows()
        {
            if (_wins.Count == 0) return;
            var dead = new List<string>();
            foreach (var pair in _wins)
                if (pair.Value == null || !pair.Value.IsAlive)
                    dead.Add(pair.Key);
            foreach (string id in dead) _wins.Remove(id);
        }

        public static bool TryHandleInventoryDragDrop(ItemIcon source,
            Vector2 screenPosition)
        {
            var target = FindBagSlot(screenPosition);
            if (target == null) return false;
            SetFocus(target.BagId, target.BagName);
            if (target.IsEquipmentTarget &&
                !target.AcceptsEquipmentItem(source?.MyItem))
            {
                ChatHelper.Send(
                    $"<color=#EF5350>[Thrall]</color> That item does not fit the " +
                    $"{target.EquipmentSlotType} slot.");
                return true;
            }
            BagSystem.TryStoreFromInventorySlot(
                source, target.BagId, target.BagName);
            // Returning true means the pointer was released over a bag slot;
            // suppress Erenshor's native cursor pickup even if admission failed.
            return true;
        }

        internal static ItemIcon FindDirectInventorySlot(Vector2 screenPosition)
        {
            foreach (var result in Raycast(screenPosition))
            {
                ItemIcon slot = result.gameObject.GetComponent<ItemIcon>() ??
                    result.gameObject.GetComponentInChildren<ItemIcon>() ??
                    result.gameObject.GetComponentInParent<ItemIcon>();
                if (BagSystem.IsDirectInventorySlot(slot)) return slot;
            }
            return null;
        }

        private static BagSlotInteraction FindBagSlot(Vector2 screenPosition)
        {
            foreach (var result in Raycast(screenPosition))
            {
                var slot = result.gameObject.GetComponentInParent<BagSlotInteraction>();
                if (slot != null) return slot;
            }
            return null;
        }

        private static List<RaycastResult> Raycast(Vector2 screenPosition)
        {
            var results = new List<RaycastResult>();
            if (EventSystem.current == null) return results;
            var data = new PointerEventData(EventSystem.current)
            {
                position = screenPosition
            };
            EventSystem.current.RaycastAll(data, results);
            return results;
        }
    }

    public class SingleBagWindow
    {
        string _bagId, _bagName; int _cols, _rows;
        GameObject _root; RectTransform _panel, _slotsParent;
        List<GameObject> _slotGOs = new List<GameObject>();
        List<GameObject> _sheetGOs = new List<GameObject>();
        bool _isThrall;
        float _backpackWidth;
        static readonly Color C_Bg = new Color(0.08f, 0.12f, 0.15f, 0.96f);    // Dark teal-blue
        static readonly Color C_Title = new Color(0.06f, 0.10f, 0.13f, 0.98f); // Darkest teal
        static readonly Color C_Slot = new Color(0.12f, 0.18f, 0.22f, 0.9f);   // Teal slot with item
        static readonly Color C_Empty = new Color(0.08f, 0.13f, 0.16f, 0.7f);  // Darker empty slot
        static readonly Color C_Border = new Color(0.22f, 0.36f, 0.40f, 0.9f); // Teal border
        const float SLOT = 58f; // pixels per slot
        const float PAD = 3f;

        public SingleBagWindow(Transform parent, string bagId, string bagName,
            int cols, int rows, float ox, float oy)
        {
            _bagId = bagId; _bagName = bagName; _cols = cols; _rows = rows;
            _isThrall = BagSystem.IsThrallBag(bagName);
            _backpackWidth = _isThrall ? 0f :
                cols * SLOT + (cols + 1) * PAD;
            int equipmentSlots = _isThrall
                ? BagSystem.GetThrallEquipmentSlots().Count : 0;
            int equipmentRows = Mathf.CeilToInt(equipmentSlots / 3f);
            float sheetHeight = _isThrall
                ? Mathf.Max(190f, 38f + equipmentRows * 54f) : 0f;
            float w = _isThrall ? 430f : _backpackWidth;
            float h = _isThrall ? sheetHeight + 28f :
                rows * SLOT + (rows + 1) * PAD + 28f;

            _root = new GameObject($"Bag_{bagId}");
            _root.transform.SetParent(parent, false);
            _panel = _root.AddComponent<RectTransform>();
            _root.AddComponent<Image>().color = C_Bg;
            var outline = _root.AddComponent<Outline>();
            outline.effectColor = C_Border;
            outline.effectDistance = new Vector2(1.5f, -1.5f);
            _panel.anchorMin = _panel.anchorMax = _panel.pivot = new Vector2(0.5f, 0.5f);
            _panel.sizeDelta = new Vector2(w, h);
            _panel.anchoredPosition = new Vector2(ox, oy);

            // Title
            var tGO = new GameObject("Title"); tGO.transform.SetParent(_panel, false);
            var tRT = tGO.AddComponent<RectTransform>();
            tGO.AddComponent<Image>().color = C_Title;
            tRT.anchorMin = new Vector2(0, 1); tRT.anchorMax = Vector2.one;
            tRT.pivot = new Vector2(0.5f, 1);
            tRT.offsetMin = new Vector2(0, -28); tRT.offsetMax = Vector2.zero;
            string title = BagSystem.IsCorpseBag(bagName)
                ? "Corpse Bag — take only"
                : (BagSystem.IsThrallBag(bagName)
                    ? "Bone Thrall — Character" : bagName);
            var tt = MkTxt(tRT, title, 13, Color.white, TextAnchor.MiddleCenter);
            tt.fontStyle = FontStyle.Bold;
            tGO.AddComponent<BagWindowDragger>().Target = _panel;
            // Title border line
            var tbGO = new GameObject("TBorder"); tbGO.transform.SetParent(tRT, false);
            var tbRT = tbGO.AddComponent<RectTransform>();
            tbGO.AddComponent<Image>().color = C_Border;
            tbRT.anchorMin = new Vector2(0, 0); tbRT.anchorMax = new Vector2(1, 0);
            tbRT.pivot = new Vector2(0.5f, 0); tbRT.sizeDelta = new Vector2(0, 1);
            tbRT.anchoredPosition = Vector2.zero;
            // Focus on click
            string bid = bagId, bn = bagName;
            var fb = tGO.AddComponent<Button>(); fb.targetGraphic = tGO.GetComponent<Image>();
            fb.onClick.AddListener(() => BagWindow.SetFocus(bid, bn));
            // Close button
            var cGO = new GameObject("X"); cGO.transform.SetParent(tRT, false);
            var cRT = cGO.AddComponent<RectTransform>();
            cGO.AddComponent<Image>().color = new Color(0.6f, 0.15f, 0.15f, 0.9f);
            cRT.anchorMin = new Vector2(1, 0); cRT.anchorMax = Vector2.one;
            cRT.pivot = new Vector2(1, 0.5f);
            cRT.offsetMin = new Vector2(-26, 2); cRT.offsetMax = new Vector2(-2, -2);
            var cb = cGO.AddComponent<Button>(); cb.targetGraphic = cGO.GetComponent<Image>();
            string cid = bagId; cb.onClick.AddListener(() => BagWindow.CloseOne(cid));
            MkTxt(cRT, "\u2715", 11, Color.white, TextAnchor.MiddleCenter);

            // Slots container - absolute positioned below title
            var sGO = new GameObject("Slots"); sGO.transform.SetParent(_panel, false);
            _slotsParent = sGO.AddComponent<RectTransform>();
            _slotsParent.anchorMin = new Vector2(0, 0); _slotsParent.anchorMax = new Vector2(1, 1);
            _slotsParent.offsetMin = Vector2.zero;
            _slotsParent.offsetMax = new Vector2(0, -28);
            Refresh();
        }

        public void BringToFront()
        {
            if (_root)
            {
                if (!_root.activeSelf) _root.SetActive(true);
                _root.transform.SetAsLastSibling();
            }
            BagWindow.SetFocus(_bagId, _bagName);
        }
        public bool IsAlive => _root != null && _panel != null;
        public void Destroy() { if (_root) UnityEngine.Object.Destroy(_root); }
        public bool ContainsScreenPoint(Vector2 screenPosition)
        {
            return IsAlive && _root.activeInHierarchy &&
                RectTransformUtility.RectangleContainsScreenPoint(
                _panel, screenPosition, null);
        }

        public void Refresh()
        {
            foreach (var go in _slotGOs) if (go) UnityEngine.Object.Destroy(go);
            _slotGOs.Clear();
            foreach (var go in _sheetGOs) if (go) UnityEngine.Object.Destroy(go);
            _sheetGOs.Clear();
            int max = BagSystem.GetVisibleSlots(_bagId, _bagName);
            var contents = BagSystem.GetBagContents(_bagId);

            if (_isThrall)
            {
                RefreshThrallSheet(contents);
                return;
            }

            for (int i = 0; i < max; i++)
            {
                int col = i % _cols, row = i / _cols;
                bool has = i < contents.Count && !string.IsNullOrEmpty(contents[i].ItemName);
                BagSystem.BagSlotData slotData = has ? contents[i] : null;
                Item slotItem = null;

                // Build slot from scratch — no cloning, guaranteed sizing
                var go = new GameObject($"S{i}");
                go.transform.SetParent(_slotsParent, false);
                var rt = go.AddComponent<RectTransform>();
                var bg = go.AddComponent<Image>();
                bg.color = has ? C_Slot : C_Empty;
                var slotOutline = go.AddComponent<Outline>();
                slotOutline.effectColor = C_Border;
                slotOutline.effectDistance = new Vector2(1, -1);

                // Absolute pixel positioning
                rt.anchorMin = new Vector2(0, 1);
                rt.anchorMax = new Vector2(0, 1);
                rt.pivot = new Vector2(0, 1);
                rt.sizeDelta = new Vector2(SLOT, SLOT);
                rt.anchoredPosition = new Vector2(
                    PAD + col * (SLOT + PAD),
                    -(PAD + row * (SLOT + PAD)));

                if (has)
                {
                    var s = slotData;
                    if (!string.IsNullOrEmpty(s.ItemId))
                        slotItem = GameData.ItemDB?.GetItemByID(s.ItemId);
                    if (slotItem == null) slotItem = ItemFactory.GetItem(s.ItemName);

                    // Item icon image
                    if (slotItem != null && slotItem.ItemIcon != null)
                    {
                        var iconGO = new GameObject("Icon");
                        iconGO.transform.SetParent(rt, false);
                        var iconRT = iconGO.AddComponent<RectTransform>();
                        iconRT.anchorMin = Vector2.zero; iconRT.anchorMax = Vector2.one;
                        iconRT.offsetMin = new Vector2(2, 2); iconRT.offsetMax = new Vector2(-2, -2);
                        var iconImg = iconGO.AddComponent<Image>();
                        iconImg.sprite = slotItem.ItemIcon;
                        iconImg.preserveAspect = true;
                        iconImg.raycastTarget = false;
                    }

                    // Quantity text (bottom-left)
                    if (s.Quantity > 1 && s.IsStackable)
                    {
                        var qGO = new GameObject("Qty");
                        qGO.transform.SetParent(rt, false);
                        var qRT = qGO.AddComponent<RectTransform>();
                        qRT.anchorMin = new Vector2(0, 0);
                        qRT.anchorMax = new Vector2(0.5f, 0.3f);
                        qRT.offsetMin = Vector2.zero; qRT.offsetMax = Vector2.zero;
                        var qBg = qGO.AddComponent<Image>();
                        qBg.color = new Color(0, 0, 0, 0.7f);
                        var qTxt = new GameObject("QT").AddComponent<Text>();
                        qTxt.transform.SetParent(qRT, false);
                        qTxt.rectTransform.anchorMin = Vector2.zero;
                        qTxt.rectTransform.anchorMax = Vector2.one;
                        qTxt.rectTransform.offsetMin = Vector2.zero;
                        qTxt.rectTransform.offsetMax = Vector2.zero;
                        qTxt.text = s.Quantity.ToString();
                        qTxt.fontSize = 10; qTxt.color = Color.white;
                        qTxt.alignment = TextAnchor.MiddleCenter;
                        qTxt.font = Font.CreateDynamicFontFromOSFont("Arial", 10);
                        qBg.raycastTarget = false;
                        qTxt.raycastTarget = false;
                    }
                }

                // Every cell is a drop target, including empty cells. Filled
                // cells additionally support click-to-withdraw and drag-out.
                var ov = new GameObject("Interaction");
                ov.transform.SetParent(rt, false);
                var ovr = ov.AddComponent<RectTransform>();
                ovr.anchorMin = Vector2.zero; ovr.anchorMax = Vector2.one;
                ovr.offsetMin = ovr.offsetMax = Vector2.zero;
                var oi = ov.AddComponent<Image>();
                oi.color = new Color(0, 0, 0, 0);
                var interaction = ov.AddComponent<BagSlotInteraction>();
                interaction.Configure(_bagId, _bagName, i, slotData,
                    slotItem, slotData?.Quantity ?? 1);
                _slotGOs.Add(go);
            }
        }

        private void RefreshThrallSheet(List<BagSystem.BagSlotData> contents)
        {
            if (_slotsParent == null) return;
            var panel = new GameObject("ThrallCharacterSheet");
            panel.transform.SetParent(_slotsParent, false);
            var panelRect = panel.AddComponent<RectTransform>();
            panelRect.anchorMin = panelRect.anchorMax = new Vector2(0, 1);
            panelRect.pivot = new Vector2(0, 1);
            int equipmentCount = BagSystem.GetThrallEquipmentSlots().Count;
            int equipmentRows = Mathf.CeilToInt(equipmentCount / 3f);
            float panelHeight = Mathf.Max(180f, 34f + equipmentRows * 54f);
            panelRect.sizeDelta = new Vector2(414f, panelHeight);
            panelRect.anchoredPosition = new Vector2(PAD, -PAD);
            var panelImage = panel.AddComponent<Image>();
            panelImage.color = new Color(0.06f, 0.10f, 0.13f, 0.94f);
            var panelOutline = panel.AddComponent<Outline>();
            panelOutline.effectColor = C_Border;
            panelOutline.effectDistance = new Vector2(1, -1);
            _sheetGOs.Add(panel);

            var title = MkTxt(panelRect, "EQUIPMENT", 12,
                new Color(0.82f, 0.70f, 0.48f), TextAnchor.MiddleCenter);
            title.fontStyle = FontStyle.Bold;
            SetTopRect(title.rectTransform, 194, 22, 2, 0);

            var stats = GameData.PlayerControl?.Myself?.MyCharmedNPC;
            var petStats = stats?.GetComponent<Stats>();
            var petInventory = stats?.GetComponent<Inventory>();
            var statRows = new List<string>();
            if (stats != null && petStats != null && petInventory != null)
            {
                statRows.Add($"HP  {petStats.CurrentHP}/{petStats.CurrentMaxHP}   AC  {stats.HardSetAC}");
                statRows.Add($"MP  {petStats.CurrentMana}/{ReadInteger(petStats, "CurrentMaxMana", "MaxMana", "CurrentMaxMP")}");
                statRows.Add($"STR {petStats.BaseStr + petInventory.ItemStr}   END {petStats.BaseEnd + petInventory.ItemEnd}");
                statRows.Add($"DEX {petStats.BaseDex + petInventory.ItemDex}   AGI {petStats.BaseAgi + petInventory.ItemAgi}");
                statRows.Add($"INT {petStats.BaseInt + petInventory.ItemInt}   WIS {petStats.BaseWis + petInventory.ItemWis}");
                statRows.Add($"CHA {petStats.BaseCha + petInventory.ItemCha}   RES {petStats.BaseRes + petInventory.ItemRes}");
                statRows.Add($"MR {petStats.BaseMR + petInventory.ItemMR}   ER {petStats.BaseER + petInventory.ItemER}");
                statRows.Add($"PR {petStats.BasePR + petInventory.ItemPR}   VR {petStats.BaseVR + petInventory.ItemVR}");
                statRows.Add($"Damage  {stats.BaseAtkDmg} / {stats.OHAtkDmg}");
            }
            else statRows.Add("Summon a Bone Thrall to view stats.");

            var statsTitle = MkTxt(panelRect, "THRALL STATS", 12,
                new Color(0.82f, 0.70f, 0.48f), TextAnchor.MiddleCenter);
            statsTitle.fontStyle = FontStyle.Bold;
            SetTopRect(statsTitle.rectTransform, 204, 22, 204, 0);
            float statY = 25f;
            foreach (string row in statRows)
            {
                var text = MkTxt(panelRect, row, 10, Color.white,
                    TextAnchor.MiddleLeft);
                SetTopRect(text.rectTransform, 204, 16, 208, statY);
                statY += 16f;
            }

            var used = new HashSet<int>();
            List<ItemIcon> equipmentSlots = BagSystem.GetThrallEquipmentSlots();
            int equipIndex = 0;
            foreach (ItemIcon equipmentSlot in equipmentSlots)
            {
                if (equipmentSlot == null) continue;
                int col = equipIndex % 3;
                int row = equipIndex / 3;
                float slotX = 5f + col * 64f;
                float slotY = 26f + row * 54f;
                Item.SlotType slotType = equipmentSlot.ThisSlotType;
                int bagIndex = FindThrallEquipment(contents, slotType, used);
                BagSystem.BagSlotData data = bagIndex >= 0
                    ? contents[bagIndex] : null;
                Item item = data != null ? ResolveThrallUiItem(data) : null;
                CreateEquipmentCell(panelRect, slotX, slotY, slotType,
                    bagIndex, data, item);
                equipIndex++;
            }
        }

        private int FindThrallEquipment(List<BagSystem.BagSlotData> contents,
            Item.SlotType slotType, HashSet<int> used)
        {
            if (contents == null) return -1;
            var wanted = slotType == Item.SlotType.Primary
                ? Necromancer.NecromancerPetEquipment.Primary
                : slotType == Item.SlotType.Secondary
                    ? Necromancer.NecromancerPetEquipment.Secondary : null;
            if (wanted != null)
            {
                for (int i = 0; i < contents.Count; i++)
                {
                    BagSystem.BagSlotData entry = contents[i];
                    if (entry == null || used.Contains(i) ||
                        !string.Equals(entry.ItemId, wanted.ItemId,
                            StringComparison.OrdinalIgnoreCase) ||
                        entry.Quantity != wanted.Quality) continue;
                    used.Add(i);
                    return i;
                }
            }
            for (int i = 0; i < contents.Count; i++)
            {
                if (used.Contains(i)) continue;
                Item item = ResolveThrallUiItem(contents[i]);
                if (item == null || item.Stackable) continue;
                Item.SlotType itemSlot = item.Aura != null
                    ? Item.SlotType.Aura : item.RequiredSlot;
                if (itemSlot == Item.SlotType.General) continue;
                bool matches = itemSlot == slotType ||
                    (itemSlot == Item.SlotType.PrimaryOrSecondary &&
                     (slotType == Item.SlotType.Primary ||
                      slotType == Item.SlotType.Secondary));
                if (!matches) continue;
                used.Add(i);
                return i;
            }
            return -1;
        }

        private void CreateEquipmentCell(RectTransform parent, float x,
            float y, Item.SlotType slotType, int bagIndex,
            BagSystem.BagSlotData data, Item item)
        {
            var go = new GameObject("Equipment_" + slotType);
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.sizeDelta = new Vector2(60, 50);
            rect.anchoredPosition = new Vector2(x, -y);
            var bg = go.AddComponent<Image>();
            bg.color = data != null ? C_Slot : C_Empty;
            var outline = go.AddComponent<Outline>();
            outline.effectColor = C_Border;
            outline.effectDistance = new Vector2(1, -1);

            var label = MkTxt(rect, ShortSlotName(slotType), 8,
                new Color(0.80f, 0.84f, 0.86f), TextAnchor.UpperCenter);
            SetTopRect(label.rectTransform, 60, 11, 0, 0);
            if (item?.ItemIcon != null)
            {
                var iconGO = new GameObject("Icon");
                iconGO.transform.SetParent(rect, false);
                var iconRect = iconGO.AddComponent<RectTransform>();
                iconRect.anchorMin = iconRect.anchorMax = new Vector2(0.5f, 0.5f);
                iconRect.pivot = new Vector2(0.5f, 0.5f);
                iconRect.sizeDelta = new Vector2(34, 34);
                iconRect.anchoredPosition = new Vector2(0, -6);
                var icon = iconGO.AddComponent<Image>();
                icon.sprite = item.ItemIcon;
                icon.preserveAspect = true;
                icon.raycastTarget = false;
            }
            var interactionGO = new GameObject("Interaction");
            interactionGO.transform.SetParent(rect, false);
            var interactionRect = interactionGO.AddComponent<RectTransform>();
            interactionRect.anchorMin = Vector2.zero;
            interactionRect.anchorMax = Vector2.one;
            interactionRect.offsetMin = interactionRect.offsetMax = Vector2.zero;
            var hit = interactionGO.AddComponent<Image>();
            hit.color = new Color(0, 0, 0, 0);
            var interaction = interactionGO.AddComponent<BagSlotInteraction>();
            interaction.Configure(_bagId, _bagName, bagIndex, data,
                item, data?.Quantity ?? 1);
            interaction.ConfigureEquipmentTarget(slotType);
            _sheetGOs.Add(go);
        }

        private static Item ResolveThrallUiItem(BagSystem.BagSlotData data)
        {
            if (data == null) return null;
            Item item = !string.IsNullOrEmpty(data.ItemId)
                ? GameData.ItemDB?.GetItemByID(data.ItemId) : null;
            if (item == null || item == GameData.PlayerInv?.Empty)
                item = ItemFactory.ResolveItem(data.ItemName);
            return item == GameData.PlayerInv?.Empty ? null : item;
        }

        private static string ShortSlotName(Item.SlotType slot)
        {
            string value = slot.ToString();
            if (value == "PrimaryOrSecondary") return "Hand";
            return value.Length > 8 ? value.Substring(0, 8) : value;
        }

        private static int ReadInteger(object instance, params string[] names)
        {
            if (instance == null) return 0;
            const BindingFlags flags = BindingFlags.Instance |
                BindingFlags.Public | BindingFlags.NonPublic;
            foreach (string name in names)
            {
                FieldInfo field = instance.GetType().GetField(name, flags);
                if (field != null)
                {
                    try { return Convert.ToInt32(field.GetValue(instance)); }
                    catch { }
                }
                PropertyInfo property = instance.GetType().GetProperty(name, flags);
                if (property != null)
                {
                    try { return Convert.ToInt32(property.GetValue(instance)); }
                    catch { }
                }
            }
            return 0;
        }

        private static void SetTopRect(RectTransform rect, float width,
            float height, float x, float y)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            if (width <= 0) rect.anchorMax = new Vector2(1, 1);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(x, -y);
        }

        static Text MkTxt(RectTransform p, string t, int sz, Color c, TextAnchor a)
        {
            var go = new GameObject("T"); go.transform.SetParent(p, false);
            var tx = go.AddComponent<Text>();
            tx.text = t; tx.fontSize = sz; tx.color = c; tx.alignment = a;
            tx.font = Font.CreateDynamicFontFromOSFont("Arial", sz);
            tx.supportRichText = true;
            var r = tx.rectTransform;
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
            r.offsetMin = r.offsetMax = Vector2.zero;
            return tx;
        }
    }

    public class BagCursorOverlay : MonoBehaviour
    {
        GameObject _root;
        Image _image;
        Item _shownItem;

        void Awake()
        {
            _root = new GameObject("BagCursorItemOverlay");
            _root.transform.SetParent(transform, false);
            var rect = _root.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(54f, 54f);
            _image = _root.AddComponent<Image>();
            _image.preserveAspect = true;
            _image.raycastTarget = false;
            _image.color = new Color(1f, 1f, 1f, 0.92f);
            _root.SetActive(false);
        }

        void Update()
        {
            var mouse = GameData.MouseSlot;
            Item item = mouse?.MyItem;
            bool visible = BagWindow.AnyOpen && mouse != null && mouse.dragging &&
                item != null && GameData.PlayerInv != null &&
                item != GameData.PlayerInv.Empty &&
                BagWindow.IsPointerOverAnyBag(Input.mousePosition);
            if (!visible)
            {
                if (_root != null && _root.activeSelf) _root.SetActive(false);
                return;
            }

            if (!ReferenceEquals(_shownItem, item))
            {
                _shownItem = item;
                _image.sprite = item.ItemIcon;
            }
            _root.SetActive(true);
            _root.transform.position = Input.mousePosition;
            _root.transform.SetAsLastSibling();
        }

        void OnDestroy()
        {
            if (_root != null) Destroy(_root);
        }
    }

    public class BagSlotInteraction : MonoBehaviour, IPointerDownHandler,
        IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler,
        IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public string BagId { get; private set; }
        public string BagName { get; private set; }

        int _index;
        int _tooltipQuantity;
        bool _dragging;
        bool _suppressClick;
        public bool IsEquipmentTarget { get; private set; }
        public Item.SlotType EquipmentSlotType { get; private set; }
        BagSystem.BagSlotData _expected;
        Item _item;
        GameObject _dragGhost;

        public void Configure(string bagId, string bagName, int index,
            BagSystem.BagSlotData expected, Item item, int tooltipQuantity)
        {
            BagId = bagId;
            BagName = bagName;
            _index = index;
            _expected = expected;
            _item = item;
            _tooltipQuantity = Mathf.Max(1, tooltipQuantity);
        }

        internal void ConfigureEquipmentTarget(Item.SlotType slotType)
        {
            IsEquipmentTarget = true;
            EquipmentSlotType = slotType;
        }

        internal bool AcceptsEquipmentItem(Item item)
        {
            if (!IsEquipmentTarget || item == null || item.Stackable) return false;
            Item.SlotType itemSlot = item.Aura != null
                ? Item.SlotType.Aura : item.RequiredSlot;
            if (itemSlot == Item.SlotType.General) return false;
            if (EquipmentSlotType == Item.SlotType.Primary)
                return !item.Shield &&
                    (itemSlot == Item.SlotType.Primary ||
                     itemSlot == Item.SlotType.PrimaryOrSecondary);
            if (EquipmentSlotType == Item.SlotType.Secondary)
                return item.Shield ||
                    itemSlot == Item.SlotType.Secondary ||
                    itemSlot == Item.SlotType.PrimaryOrSecondary;
            return itemSlot == EquipmentSlotType;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            _suppressClick = false;
            BagWindow.SetFocus(BagId, BagName);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            if (_suppressClick)
            {
                _suppressClick = false;
                return;
            }

            bool cursorBusy = GameData.ItemOnCursor != null ||
                (GameData.MouseSlot != null && GameData.MouseSlot.dragging);
            if (cursorBusy)
            {
                if (IsEquipmentTarget &&
                    !AcceptsEquipmentItem(GameData.ItemOnCursor?.MyItem ??
                        GameData.MouseSlot?.MyItem))
                {
                    ChatHelper.Send(
                        $"<color=#EF5350>[Thrall]</color> That item does not fit the " +
                        $"{EquipmentSlotType} slot.");
                    return;
                }
                BagSystem.TryStoreCursorItem(BagId, BagName);
                return;
            }
            if (_expected != null)
                BagSystem.TryTakeFromBag(BagId, _index, _expected);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left ||
                _expected == null || _item == null ||
                GameData.ItemOnCursor != null ||
                (GameData.MouseSlot != null && GameData.MouseSlot.dragging))
                return;

            _dragging = true;
            _suppressClick = true;
            try { GameData.ItemInfoWindow.CloseItemWindow(); } catch { }

            _dragGhost = new GameObject("BagDragGhost");
            _dragGhost.transform.SetParent(transform.root, false);
            var rect = _dragGhost.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(54f, 54f);
            rect.position = eventData.position;
            var image = _dragGhost.AddComponent<Image>();
            image.sprite = _item.ItemIcon;
            image.preserveAspect = true;
            image.color = new Color(1f, 1f, 1f, 0.85f);
            image.raycastTarget = false;
            _dragGhost.transform.SetAsLastSibling();
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (_dragging && _dragGhost != null)
                _dragGhost.transform.position = eventData.position;
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (!_dragging) return;
            _dragging = false;
            if (_dragGhost != null) Destroy(_dragGhost);
            _dragGhost = null;

            ItemIcon target = BagWindow.FindDirectInventorySlot(eventData.position);
            if (target != null)
                BagSystem.TryTakeFromBagToInventorySlot(
                    BagId, _index, _expected, target);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (_item == null || _dragging || GameData.MouseSlot?.dragging == true)
                return;
            try
            {
                var position = transform.position + new Vector3(-200f, 0f, 0f);
                GameData.ItemInfoWindow.DisplayItem(
                    _item, position, _tooltipQuantity);
            }
            catch { }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            try { GameData.ItemInfoWindow.CloseItemWindow(); } catch { }
        }

        void OnDestroy()
        {
            if (_dragGhost != null) Destroy(_dragGhost);
        }
    }

    public class BagWindowDragger : MonoBehaviour, IDragHandler, IBeginDragHandler
    {
        public RectTransform Target;
        Vector2 _off;
        public void OnBeginDrag(PointerEventData e)
        { if (Target) RectTransformUtility.ScreenPointToLocalPointInRectangle(Target, e.position, e.pressEventCamera, out _off); }
        public void OnDrag(PointerEventData e)
        { if (Target) Target.position = e.position - _off; }
    }
}
