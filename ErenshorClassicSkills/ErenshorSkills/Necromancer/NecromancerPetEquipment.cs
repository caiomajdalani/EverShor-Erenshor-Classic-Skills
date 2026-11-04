using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace ErenshorSkills.Necromancer
{
    [Serializable]
    public sealed class NecromancerPetGearItem
    {
        public string ItemId = "";
        public string ItemName = "";
        // Erenshor and LootRarity encode the exact blessed/rarity state in the
        // non-stackable inventory slot's Quantity field. It is intentionally
        // opaque and is never normalized or capped.
        public int Quality = 1;
        // A deterministic snapshot of every scalar field on the concrete Item
        // instance. This preserves upgrades made by other mods even when those
        // mods mutate item stats instead of encoding them in Quantity.
        public List<NecromancerPetGearScalar> ScalarState =
            new List<NecromancerPetGearScalar>();
    }

    [Serializable]
    public sealed class NecromancerPetGearScalar
    {
        public string Key = "";
        public string Type = "";
        public string Value = "";
    }

    [Serializable]
    public sealed class NecromancerPetGearEnvelope
    {
        public int Version = 2;
        public NecromancerPetGearItem Primary;
        public NecromancerPetGearItem Secondary;
        public bool ManagedByThrallBag;
        public string ThrallBagId = "";
    }

    // Unity 2021's live JsonUtility silently drops nested serializable objects
    // and List<T> fields from plugin assemblies. Keep the outer save human-
    // inspectable JSON, but carry each item snapshot in a length-delimited
    // binary payload so a successful save can never become empty on reload.
    [Serializable]
    public sealed class NecromancerPetGearWireEnvelope
    {
        public int Version = 2;
        public string PrimaryPayload = "";
        public string SecondaryPayload = "";
        public bool ManagedByThrallBag;
        public string ThrallBagId = "";
    }

    /// <summary>
    /// Persistent equipment escrow for the player's skeletal servant. Gear is
    /// not owned by the transient pet GameObject, so death, dismissal, zoning,
    /// or a crash cannot silently destroy it. The exact Item ID and unmodified
    /// quality integer are retained. Version 2 also snapshots scalar fields on
    /// the concrete Item object so stat-mutating gear-upgrade mods round-trip.
    /// </summary>
    internal static class NecromancerPetEquipment
    {
        private const int PayloadMagic = 0x4750454E; // "NEPG"
        private const int PayloadVersion = 1;
        private const int MaxPayloadBytes = 196608;
        private const int MaxIdentityBytes = 2048;
        private const int MaxKeyBytes = 4096;
        private const int MaxTypeBytes = 256;
        private const int MaxValueBytes = 16384;
        private static readonly UTF8Encoding StrictUtf8 =
            new UTF8Encoding(false, true);

        private static NecromancerPetGearEnvelope _gear =
            new NecromancerPetGearEnvelope();
        private static bool _loaded;
        private static Items.BagSystem.MutationSnapshot _tradeSnapshot;

        internal static NecromancerPetGearItem Primary => _gear.Primary;
        internal static NecromancerPetGearItem Secondary => _gear.Secondary;
        internal static bool HasGear => _gear.Primary != null ||
            _gear.Secondary != null;
        internal static bool IsLoaded => _loaded;

        internal static string GetPath()
        {
            if (string.IsNullOrEmpty(SkillsSaveManager.CharName)) return null;
            return Path.Combine(RuntimePaths.DataDirectory,
                SkillsSaveManager.CharName + "_necromancer_pet_gear.json");
        }

        internal static bool Load()
        {
            _gear = new NecromancerPetGearEnvelope();
            _loaded = false;
            string path = GetPath();
            if (string.IsNullOrEmpty(path)) return false;
            try
            {
                if (!File.Exists(path) && !File.Exists(path + ".bak"))
                {
                    _loaded = true;
                    return true;
                }
                if (!SafeFileIO.TryReadAllTextWithBackup(path,
                    IsSerializedDataValid, out string json, out bool recovered,
                    out string error))
                    throw new InvalidDataException(error);
                _gear = Deserialize(json);
                _loaded = true;
                if (recovered)
                    SkillsPlugin.Log.LogWarning(
                        "Recovered Necromancer pet equipment from backup.");
                SkillsPlugin.Log.LogInfo(
                    $"Necromancer pet gear loaded: {Describe()}.");
                if (!SynchronizeFromCarriedThrallBag(out string syncDetail))
                    SkillsPlugin.Log.LogWarning(
                        "Thrall Bag synchronization deferred: " + syncDetail);
                else if (!string.IsNullOrEmpty(syncDetail))
                    SkillsPlugin.Log.LogInfo(
                        "Thrall Bag synchronization: " + syncDetail);
                return true;
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogError(
                    $"Necromancer pet gear load failed; escrow disabled: {ex.Message}");
                ChatHelper.SendCombat(
                    "Pet equipment data could not be validated. Gear trading is disabled to protect your items.",
                    "#EF5350");
                return false;
            }
        }

        internal static bool Save()
        {
            if (!_loaded) return false;
            string path = GetPath();
            if (string.IsNullOrEmpty(path)) return false;
            try
            {
                string json = Serialize();
                return SafeFileIO.TryWriteAllTextAtomic(path, json,
                    out string error) ? true : LogSaveFailure(error);
            }
            catch (Exception ex)
            {
                return LogSaveFailure(ex.Message);
            }
        }

        private static bool LogSaveFailure(string error)
        {
            SkillsPlugin.Log.LogError(
                "Necromancer pet gear save failed: " + error);
            return false;
        }

        internal static string Serialize()
        {
            var output = new NecromancerPetGearWireEnvelope
            {
                Version = 2,
                PrimaryPayload = EncodeItem(_gear.Primary),
                SecondaryPayload = EncodeItem(_gear.Secondary),
                ManagedByThrallBag = _gear.ManagedByThrallBag,
                ThrallBagId = _gear.ThrallBagId ?? ""
            };
            return JsonUtility.ToJson(output, true);
        }

        private static NecromancerPetGearEnvelope Deserialize(string json)
        {
            var wire = JsonUtility.FromJson<NecromancerPetGearWireEnvelope>(json);
            if (wire == null || wire.Version != 2)
                throw new InvalidDataException("unsupported pet gear format");
            var value = new NecromancerPetGearEnvelope
            {
                Version = 2,
                Primary = DecodeItem(wire.PrimaryPayload),
                Secondary = DecodeItem(wire.SecondaryPayload),
                ManagedByThrallBag = wire.ManagedByThrallBag,
                ThrallBagId = wire.ThrallBagId ?? ""
            };
            ValidateItem(value.Primary);
            ValidateItem(value.Secondary);
            if (value.ManagedByThrallBag &&
                (string.IsNullOrWhiteSpace(value.ThrallBagId) ||
                 value.ThrallBagId.Length > 64 ||
                 !System.Text.RegularExpressions.Regex.IsMatch(
                     value.ThrallBagId, "^bag[0-9]+$")))
                throw new InvalidDataException(
                    "managed pet gear has an invalid Thrall Bag identity");
            return value;
        }

        private static string EncodeItem(NecromancerPetGearItem item)
        {
            if (item == null) return "";
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, StrictUtf8, true))
            {
                writer.Write(PayloadMagic);
                writer.Write(PayloadVersion);
                WritePayloadString(writer, item.ItemId, MaxIdentityBytes);
                WritePayloadString(writer, item.ItemName, MaxIdentityBytes);
                writer.Write(item.Quality);
                List<NecromancerPetGearScalar> fields = item.ScalarState ??
                    new List<NecromancerPetGearScalar>();
                writer.Write(fields.Count);
                foreach (NecromancerPetGearScalar field in fields)
                {
                    WritePayloadString(writer, field?.Key, MaxKeyBytes);
                    WritePayloadString(writer, field?.Type, MaxTypeBytes);
                    WritePayloadString(writer, field?.Value, MaxValueBytes);
                }
                writer.Flush();
                if (stream.Length > MaxPayloadBytes)
                    throw new InvalidDataException(
                        "pet gear state payload is too large");
                return Convert.ToBase64String(stream.ToArray());
            }
        }

        private static NecromancerPetGearItem DecodeItem(string encoded)
        {
            if (string.IsNullOrEmpty(encoded)) return null;
            byte[] bytes;
            try { bytes = Convert.FromBase64String(encoded); }
            catch (FormatException ex)
            {
                throw new InvalidDataException(
                    "pet gear payload is not valid base64", ex);
            }
            if (bytes.Length == 0 || bytes.Length > MaxPayloadBytes)
                throw new InvalidDataException(
                    "pet gear state payload has an invalid size");
            using (var stream = new MemoryStream(bytes, false))
            using (var reader = new BinaryReader(stream, StrictUtf8, true))
            {
                if (reader.ReadInt32() != PayloadMagic ||
                    reader.ReadInt32() != PayloadVersion)
                    throw new InvalidDataException(
                        "pet gear state payload has an unsupported header");
                var item = new NecromancerPetGearItem
                {
                    ItemId = ReadPayloadString(reader, MaxIdentityBytes),
                    ItemName = ReadPayloadString(reader, MaxIdentityBytes),
                    Quality = reader.ReadInt32(),
                    ScalarState = new List<NecromancerPetGearScalar>()
                };
                int count = reader.ReadInt32();
                if (count < 0 || count > 512)
                    throw new InvalidDataException(
                        "pet gear state payload has an invalid field count");
                for (int i = 0; i < count; i++)
                {
                    item.ScalarState.Add(new NecromancerPetGearScalar
                    {
                        Key = ReadPayloadString(reader, MaxKeyBytes),
                        Type = ReadPayloadString(reader, MaxTypeBytes),
                        Value = ReadPayloadString(reader, MaxValueBytes)
                    });
                }
                if (stream.Position != stream.Length)
                    throw new InvalidDataException(
                        "pet gear state payload has trailing data");
                return item;
            }
        }

        private static void WritePayloadString(BinaryWriter writer,
            string value, int maximumBytes)
        {
            byte[] bytes = StrictUtf8.GetBytes(value ?? "");
            if (bytes.Length > maximumBytes)
                throw new InvalidDataException(
                    "pet gear state contains an oversized text field");
            writer.Write(bytes.Length);
            writer.Write(bytes);
        }

        private static string ReadPayloadString(BinaryReader reader,
            int maximumBytes)
        {
            int length = reader.ReadInt32();
            if (length < 0 || length > maximumBytes ||
                reader.BaseStream.Length - reader.BaseStream.Position < length)
                throw new InvalidDataException(
                    "pet gear state contains an invalid text field");
            byte[] bytes = reader.ReadBytes(length);
            return StrictUtf8.GetString(bytes);
        }

        internal static bool IsSerializedDataValid(string json)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(json) || json.Length > 262144)
                    return false;
                NecromancerPetGearEnvelope value = Deserialize(json);
                if (value.Primary != null && value.Secondary != null &&
                    string.Equals(value.Primary.ItemId, value.Secondary.ItemId,
                        StringComparison.OrdinalIgnoreCase) &&
                    value.Primary.Quality == value.Secondary.Quality)
                {
                    // Two genuinely identical weapons are valid. Do not reject
                    // the state merely because their deterministic IDs match.
                }
                return true;
            }
            catch { return false; }
        }

        private static void ValidateItem(NecromancerPetGearItem item)
        {
            if (item == null) return;
            if (string.IsNullOrWhiteSpace(item.ItemId) || item.ItemId.Length > 512 ||
                string.IsNullOrWhiteSpace(item.ItemName) || item.ItemName.Length > 512 ||
                item.Quality <= 0)
                throw new InvalidDataException("pet gear contains an invalid item state");
            if (item.ScalarState == null) return;
            if (item.ScalarState.Count > 512)
                throw new InvalidDataException("pet gear has too many state fields");
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (NecromancerPetGearScalar field in item.ScalarState)
            {
                if (field == null || string.IsNullOrWhiteSpace(field.Key) ||
                    field.Key.Length > 1024 || string.IsNullOrWhiteSpace(field.Type) ||
                    field.Type.Length > 128 || (field.Value?.Length ?? 0) > 8192 ||
                    !keys.Add(field.Key))
                    throw new InvalidDataException(
                        "pet gear contains an invalid or duplicate state field");
                // Unity JsonUtility represents serialized empty strings as
                // null on some runtime versions. They are equivalent in this
                // scalar wire format and normalize deterministically here.
                if (field.Value == null) field.Value = "";
            }
        }

        internal static Item Resolve(NecromancerPetGearItem state)
        {
            if (state == null) return null;
            Item item = Items.ItemFactory.GetItemById(state.ItemId);
            if (item == null || item == GameData.PlayerInv?.Empty)
                item = GameData.ItemDB?.GetItemByID(state.ItemId);
            if ((item == null || item == GameData.PlayerInv?.Empty) &&
                !string.IsNullOrEmpty(state.ItemName))
                item = Items.ItemFactory.ResolveItem(state.ItemName);
            if (item == null || item == GameData.PlayerInv?.Empty) return null;
            if (state.ScalarState == null || state.ScalarState.Count == 0)
                return item;

            // Never overwrite a shared database asset. A private clone carries
            // the exact captured stats back onto the pet or into inventory.
            Item restored = UnityEngine.Object.Instantiate(item);
            restored.name = item.name + " [Necromancer Pet Gear Snapshot]";
            restored.hideFlags = HideFlags.HideAndDontSave;
            ApplyScalarState(restored, state.ScalarState);
            return restored;
        }

        internal static bool TryValidateExactStatePayload(string payload,
            string expectedId, string expectedName, int expectedQuality,
            out string error)
        {
            error = null;
            try
            {
                if (string.IsNullOrWhiteSpace(payload))
                    throw new InvalidDataException("the payload is empty");
                NecromancerPetGearItem state = DecodeItem(payload);
                ValidateItem(state);
                if (state == null)
                    throw new InvalidDataException("the payload has no item");
                if (!string.Equals(state.ItemId ?? "", expectedId ?? "",
                        StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(state.ItemName ?? "", expectedName ?? "",
                        StringComparison.OrdinalIgnoreCase) ||
                    state.Quality != expectedQuality)
                    throw new InvalidDataException(
                        "the payload identity or quality does not match its bag slot");
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        internal static Item ResolveExactStatePayload(string payload)
        {
            try
            {
                NecromancerPetGearItem state = DecodeItem(payload);
                ValidateItem(state);
                return Resolve(state);
            }
            catch { return null; }
        }

        internal static Item ResolveThrallBagItem(
            Items.BagSystem.BagSlotData slot)
        {
            if (slot == null) return null;
            if (!string.IsNullOrWhiteSpace(slot.ExactState))
            {
                if (!TryValidateExactStatePayload(slot.ExactState,
                    slot.ItemId, slot.ItemName, slot.Quantity, out _))
                    return null;
                return ResolveExactStatePayload(slot.ExactState);
            }
            Item item = Items.ItemFactory.GetItemById(slot.ItemId);
            if (item == null || item == GameData.PlayerInv?.Empty)
                item = GameData.ItemDB?.GetItemByID(slot.ItemId);
            if (item == null || item == GameData.PlayerInv?.Empty)
                item = Items.ItemFactory.ResolveItem(slot.ItemName);
            return item == GameData.PlayerInv?.Empty ? null : item;
        }

        internal static bool TryCreateThrallBagSlot(Item item, int quality,
            out Items.BagSystem.BagSlotData entry, out string reason)
        {
            entry = null;
            reason = null;
            if (!_loaded)
            {
                reason = "pet equipment safety data is not loaded";
                return false;
            }
            if (!NecromancerCatalog.IsNecromancerClass(
                GameData.PlayerStats?.CharacterClass))
            {
                reason = "only a Necromancer can bind gear to a thrall";
                return false;
            }
            if (quality <= 0)
            {
                reason = "that item has an invalid quality value";
                return false;
            }
            if (item.Stackable || (item.RequiredSlot == Item.SlotType.General &&
                item.Aura == null))
            {
                reason = "the Thrall accepts equipment only; general and stackable items do not fit gear slots";
                return false;
            }
            NecromancerPetGearItem state = Capture(item, quality);
            string payload;
            try { payload = EncodeItem(state); }
            catch (Exception ex)
            {
                reason = "the exact item state could not be captured: " +
                    ex.Message;
                return false;
            }
            entry = new Items.BagSystem.BagSlotData
            {
                ItemId = state.ItemId,
                ItemName = state.ItemName,
                Quantity = state.Quality,
                IsStackable = false,
                ExactState = payload
            };
            return true;
        }

        /// <summary>
        /// Rebuild the live pet escrow from one authoritative Thrall Bag.
        /// This mutates memory only; BagSystem journals and persists the bag,
        /// inventory, and returned before/after escrow strings atomically.
        /// </summary>
        internal static bool TryApplyThrallBagContents(string bagId,
            IList<Items.BagSystem.BagSlotData> contents,
            out string before, out string after, out string reason)
        {
            before = null;
            after = null;
            reason = null;
            if (!_loaded)
            {
                reason = "pet equipment safety data is not loaded";
                return false;
            }
            if (string.IsNullOrWhiteSpace(bagId))
            {
                reason = "the Thrall Bag has no durable identity";
                return false;
            }
            if (!NecromancerCatalog.IsNecromancerClass(
                GameData.PlayerStats?.CharacterClass))
            {
                reason = "only a Necromancer can equip a thrall";
                return false;
            }
            if (!TryBuildThrallBagLoadout(bagId, contents,
                out NecromancerPetGearEnvelope planned, out reason))
                return false;

            before = Serialize();
            NecromancerPetGearEnvelope old = _gear;
            try
            {
                _gear = planned;
                after = Serialize();
                if (!IsSerializedDataValid(after))
                    throw new InvalidDataException(
                        "the projected pet equipment failed validation");
                return true;
            }
            catch (Exception ex)
            {
                _gear = old;
                before = null;
                after = null;
                reason = ex.Message;
                return false;
            }
        }

        internal static void RestoreSerializedInMemory(string serialized)
        {
            if (string.IsNullOrWhiteSpace(serialized)) return;
            try
            {
                _gear = Deserialize(serialized);
                _loaded = true;
                NecromancerPets.ApplyEscrowToActivePet();
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogError(
                    "Could not restore pet equipment memory after rollback: " +
                    ex.Message);
            }
        }

        /// <summary>
        /// Synchronize the runtime/stat/visual escrow with the one Thrall Bag
        /// currently in direct inventory. A banked bag carries its exact state
        /// in the shared bag record, so absence clears only bag-managed runtime
        /// gear; legacy pre-bag escrow is retained for one-time migration.
        /// </summary>
        internal static bool SynchronizeFromCarriedThrallBag(out string detail)
        {
            detail = null;
            if (!_loaded)
            {
                detail = "pet equipment safety data is not loaded";
                return false;
            }
            bool necromancer = NecromancerCatalog.IsNecromancerClass(
                GameData.PlayerStats?.CharacterClass);
            bool found = Items.BagSystem.TryGetSingleCarriedThrallBag(
                out string bagId,
                out List<Items.BagSystem.BagSlotData> contents,
                out string findReason);
            if (!found)
            {
                if (findReason != null &&
                    findReason.IndexOf("only one", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    detail = findReason;
                    return false;
                }
                if (_gear.ManagedByThrallBag)
                {
                    _gear = new NecromancerPetGearEnvelope
                    {
                        Version = 2,
                        ManagedByThrallBag = true,
                        ThrallBagId = string.IsNullOrWhiteSpace(_gear.ThrallBagId)
                            ? "bag100" : _gear.ThrallBagId
                    };
                    if (!Save())
                    {
                        detail = "banked/absent Thrall Bag runtime gear cleared, " +
                            "but its cache file could not be updated";
                        NecromancerPets.ApplyEscrowToActivePet();
                        return false;
                    }
                    NecromancerPets.ApplyEscrowToActivePet();
                    detail = "no carried Thrall Bag; runtime gear unequipped";
                }
                return true;
            }

            if (!necromancer)
            {
                if (_gear.ManagedByThrallBag && HasGear)
                {
                    _gear.Primary = null;
                    _gear.Secondary = null;
                    _gear.ThrallBagId = bagId;
                    Save();
                    NecromancerPets.ApplyEscrowToActivePet();
                }
                detail = "Thrall Bag retained without equipping it on a non-Necromancer";
                return true;
            }

            // Migrate pre-Thrall-Bag escrow without resolving gear back through
            // a lossy database item. Exact captured payloads become the bag's
            // durable source of truth in the same crash-safe transaction.
            if (contents.Count == 0 && !_gear.ManagedByThrallBag && HasGear)
            {
                Items.BagSystem.MutationSnapshot mutation =
                    Items.BagSystem.BeginExternalMutation();
                string oldGear = Serialize();
                if (_gear.Primary != null)
                    contents.Add(CreateThrallBagSlot(_gear.Primary));
                if (_gear.Secondary != null)
                    contents.Add(CreateThrallBagSlot(_gear.Secondary));
                if (!TryApplyThrallBagContents(bagId, contents,
                    out _, out string migratedGear, out string migrateReason) ||
                    !Items.BagSystem.CommitExternalMutation(
                        "migrate-thrall-bag", mutation, GetPath(),
                        oldGear, migratedGear))
                {
                    RestoreSerializedInMemory(oldGear);
                    Items.BagSystem.RollbackExternalMutation(
                        mutation, GetPath(), oldGear);
                    detail = "legacy gear migration failed: " + migrateReason;
                    return false;
                }
                NecromancerPets.ApplyEscrowToActivePet();
                Items.BagWindow.RefreshAll();
                detail = "migrated legacy pet gear into the Thrall Bag";
                return true;
            }

            if (!TryApplyThrallBagContents(bagId, contents,
                out string before, out _, out string reason))
            {
                detail = reason;
                return false;
            }
            if (!Save())
            {
                RestoreSerializedInMemory(before);
                detail = "the synchronized Thrall Bag cache could not be saved";
                return false;
            }
            NecromancerPets.ApplyEscrowToActivePet();
            detail = contents.Count == 0
                ? "Thrall inventory is empty"
                : $"synchronized {contents.Count} Thrall inventory item(s)";
            return true;
        }

        private static Items.BagSystem.BagSlotData CreateThrallBagSlot(
            NecromancerPetGearItem state)
        {
            NecromancerPetGearItem copy = Clone(state);
            return new Items.BagSystem.BagSlotData
            {
                ItemId = copy.ItemId,
                ItemName = copy.ItemName,
                Quantity = copy.Quality,
                IsStackable = false,
                ExactState = EncodeItem(copy)
            };
        }

        private static bool TryBuildThrallBagLoadout(string bagId,
            IList<Items.BagSystem.BagSlotData> contents,
            out NecromancerPetGearEnvelope planned, out string reason)
        {
            planned = null;
            reason = null;
            if (contents == null)
            {
                reason = "the Thrall Bag contents are unavailable";
                return false;
            }
            if (contents.Count > Items.BagSystem.GetBagSlots(
                Items.BagSystem.ThrallBagName))
            {
                reason = "the Thrall inventory is full";
                return false;
            }

            var states = new List<NecromancerPetGearItem>(2);
            var items = new List<Item>(2);
            var occupiedSlots = new Dictionary<Item.SlotType, int>();
            foreach (Items.BagSystem.BagSlotData slot in contents)
            {
                if (slot == null || slot.Quantity <= 0 ||
                    string.IsNullOrWhiteSpace(slot.ItemName))
                {
                    reason = "an inventory item has invalid identity or quantity";
                    return false;
                }
                Item item;
                NecromancerPetGearItem state = null;
                if (!string.IsNullOrWhiteSpace(slot.ExactState))
                {
                    if (!TryValidateExactStatePayload(slot.ExactState,
                        slot.ItemId, slot.ItemName, slot.Quantity,
                        out string payloadError))
                    {
                        reason = "an exact item snapshot is invalid: " + payloadError;
                        return false;
                    }
                    try { state = DecodeItem(slot.ExactState); }
                    catch (Exception ex)
                    {
                        reason = "an item snapshot could not be decoded: " + ex.Message;
                        return false;
                    }
                    item = Resolve(state);
                }
                else
                {
                    item = Items.ItemFactory.GetItemById(slot.ItemId);
                    if (item == null || item == GameData.PlayerInv?.Empty)
                        item = GameData.ItemDB?.GetItemByID(slot.ItemId);
                    if (item == null || item == GameData.PlayerInv?.Empty)
                        item = Items.ItemFactory.ResolveItem(slot.ItemName);
                }
                if (item == null || item == GameData.PlayerInv?.Empty)
                {
                    reason = $"{slot.ItemName} could not be resolved";
                    return false;
                }
                if (item.Stackable || (item.RequiredSlot == Item.SlotType.General &&
                    item.Aura == null))
                    continue;

                // Only hand weapons and shields occupy the combat loadout.
                // Other wearable items stay in inventory and add their own stats.
                if (item.WeaponDmg > 0 || item.Shield)
                {
                    if (!IsThrallBagEligible(item, out reason)) return false;
                    if (state == null)
                        state = Capture(item, slot.Quantity);
                    states.Add(state);
                    items.Add(item);
                }
                else
                {
                    Item.SlotType itemSlot = item.Aura != null
                        ? Item.SlotType.Aura : item.RequiredSlot;
                    int capacity = GetThrallEquipmentSlotCapacity(itemSlot);
                    occupiedSlots.TryGetValue(itemSlot,
                        out int equippedCount);
                    if (equippedCount >= capacity)
                    {
                        reason = $"the thrall already has an item in the " +
                            $"{itemSlot} slot";
                        return false;
                    }
                    occupiedSlots[itemSlot] = equippedCount + 1;
                }
            }

            if (items.Count > 2)
            {
                reason = "the thrall can equip only one primary and one offhand " +
                    "weapon, or one two-handed weapon";
                return false;
            }

            NecromancerPetGearItem primary = null;
            NecromancerPetGearItem secondary = null;
            if (items.Count == 1)
            {
                if (IsSecondaryOnly(items[0]))
                    secondary = states[0];
                else
                    primary = states[0];
            }
            else if (items.Count == 2)
            {
                if (items.Any(IsTwoHanded))
                {
                    reason = "a two-handed weapon or bow must be the only item " +
                        "in the Thrall Bag";
                    return false;
                }
                int primaryIndex = -1;
                for (int candidate = 0; candidate < 2; candidate++)
                {
                    int other = 1 - candidate;
                    if (CanUseAsPrimary(items[candidate]) &&
                        CanUseAsOffhand(items[other]))
                    {
                        primaryIndex = candidate;
                        break;
                    }
                }
                if (primaryIndex < 0)
                {
                    reason = "use one one-handed primary weapon with one shield " +
                        "or offhand-compatible one-handed weapon";
                    return false;
                }
                primary = states[primaryIndex];
                secondary = states[1 - primaryIndex];
            }

            planned = new NecromancerPetGearEnvelope
            {
                Version = 2,
                Primary = Clone(primary),
                Secondary = Clone(secondary),
                ManagedByThrallBag = true,
                ThrallBagId = bagId
            };
            return true;
        }

        private static int GetThrallEquipmentSlotCapacity(Item.SlotType type)
        {
            List<ItemIcon> slots = Items.BagSystem
                .GetThrallEquipmentSlots();
            if (type == Item.SlotType.PrimaryOrSecondary)
                return Math.Max(1, slots.Count(slot => slot != null &&
                    (slot.ThisSlotType == Item.SlotType.Primary ||
                     slot.ThisSlotType == Item.SlotType.Secondary)));
            return Math.Max(1, slots.Count(slot => slot != null &&
                slot.ThisSlotType == type));
        }

        internal static List<(Item item, int quality)> GetAdditionalThrallGear()
        {
            var result = new List<(Item item, int quality)>();
            if (!Items.BagSystem.TryGetSingleCarriedThrallBag(out _,
                out List<Items.BagSystem.BagSlotData> contents, out _) ||
                contents == null) return result;
            foreach (Items.BagSystem.BagSlotData slot in contents)
            {
                if (slot == null || slot.IsStackable ||
                    string.IsNullOrWhiteSpace(slot.ExactState)) continue;
                NecromancerPetGearItem state;
                try { state = DecodeItem(slot.ExactState); }
                catch { continue; }
                Item item = Resolve(state);
                if (item == null || item == GameData.PlayerInv?.Empty ||
                    item.Stackable || (item.RequiredSlot == Item.SlotType.General &&
                    item.Aura == null))
                    continue;
                if ((Primary != null && string.Equals(state.ItemId, Primary.ItemId,
                        StringComparison.OrdinalIgnoreCase) && state.Quality == Primary.Quality) ||
                    (Secondary != null && string.Equals(state.ItemId, Secondary.ItemId,
                        StringComparison.OrdinalIgnoreCase) && state.Quality == Secondary.Quality))
                    continue;
                result.Add((item, state.Quality));
            }
            return result;
        }

        private static bool IsThrallBagEligible(Item item, out string reason)
        {
            reason = null;
            if (item == null || item.Stackable)
            {
                reason = "only a non-stackable weapon or shield can be equipped";
                return false;
            }
            bool weapon = item.WeaponDmg > 0 &&
                (IsOneHanded(item) || IsTwoHanded(item));
            bool shield = item.Shield;
            if (!weapon && !shield)
            {
                reason = "only weapons and shields can be placed in the Thrall Bag";
                return false;
            }
            if (string.IsNullOrWhiteSpace(item.EquipmentToActivate))
            {
                reason = $"{item.ItemName} has no visible equipment model";
                return false;
            }
            if (shield && item.RequiredSlot != Item.SlotType.Secondary &&
                item.RequiredSlot != Item.SlotType.PrimaryOrSecondary)
            {
                reason = $"{item.ItemName} is not an offhand-compatible shield";
                return false;
            }
            if (weapon && item.RequiredSlot != Item.SlotType.Primary &&
                item.RequiredSlot != Item.SlotType.Secondary &&
                item.RequiredSlot != Item.SlotType.PrimaryOrSecondary)
            {
                reason = $"{item.ItemName} is not a hand-equipped weapon";
                return false;
            }
            return true;
        }

        private static bool IsOneHanded(Item item)
        {
            return item != null &&
                (item.ThisWeaponType == Item.WeaponType.OneHandMelee ||
                 item.ThisWeaponType == Item.WeaponType.OneHandDagger);
        }

        private static bool IsTwoHanded(Item item)
        {
            return item != null &&
                (item.ThisWeaponType == Item.WeaponType.TwoHandMelee ||
                 item.ThisWeaponType == Item.WeaponType.TwoHandStaff ||
                 item.ThisWeaponType == Item.WeaponType.TwoHandBow);
        }

        private static bool IsSecondaryOnly(Item item)
        {
            return item != null &&
                (item.Shield || item.RequiredSlot == Item.SlotType.Secondary);
        }

        private static bool CanUseAsPrimary(Item item)
        {
            return item != null && !item.Shield && IsOneHanded(item) &&
                (item.RequiredSlot == Item.SlotType.Primary ||
                 item.RequiredSlot == Item.SlotType.PrimaryOrSecondary);
        }

        private static bool CanUseAsOffhand(Item item)
        {
            return item != null &&
                (item.Shield ||
                 item.RequiredSlot == Item.SlotType.Secondary ||
                 (IsOneHanded(item) &&
                  item.RequiredSlot == Item.SlotType.PrimaryOrSecondary));
        }

        internal static bool TryAcceptTrade(TradeWindow window, out string detail)
        {
            detail = null;
            if (!_loaded)
            {
                detail = "pet equipment escrow is not safely loaded";
                return false;
            }
            if (window?.LootSlots == null)
            {
                detail = "trade window is unavailable";
                return false;
            }
            if (_tradeSnapshot == null)
            {
                detail = "the pre-trade safety snapshot is missing; cancel and retry";
                return false;
            }

            var offered = new List<(Item item, int quality)>();
            foreach (ItemIcon slot in window.LootSlots)
            {
                if (slot?.MyItem == null || slot.MyItem == GameData.PlayerInv?.Empty ||
                    slot.Quantity <= 0) continue;
                offered.Add((slot.MyItem, slot.Quantity));
            }
            if (offered.Count == 0)
            {
                detail = "place a weapon or shield in the trade window first";
                return false;
            }
            if (Items.BagSystem.TryGetSingleCarriedThrallBag(
                out string thrallBagId,
                out List<Items.BagSystem.BagSlotData> thrallContents,
                out _))
                return TryAcceptTradeIntoThrallBag(window, offered,
                    thrallBagId, thrallContents, out detail);

            NecromancerPetGearItem newPrimary = Clone(_gear.Primary);
            NecromancerPetGearItem newSecondary = Clone(_gear.Secondary);
            var accepted = new List<(Item item, int quality)>();
            var rejected = new List<(Item item, int quality)>();
            foreach (var entry in offered)
            {
                if (!IsEligible(entry.item))
                {
                    rejected.Add(entry);
                    continue;
                }
                bool shield = entry.item.Shield ||
                    entry.item.RequiredSlot == Item.SlotType.Secondary;
                bool twoHanded = entry.item.ThisWeaponType == Item.WeaponType.TwoHandMelee ||
                    entry.item.ThisWeaponType == Item.WeaponType.TwoHandStaff ||
                    entry.item.ThisWeaponType == Item.WeaponType.TwoHandBow;
                if (shield)
                {
                    if (newSecondary == null)
                    {
                        newSecondary = Capture(entry.item, entry.quality);
                        accepted.Add(entry);
                    }
                    else rejected.Add(entry);
                }
                else if (newPrimary == null)
                {
                    newPrimary = Capture(entry.item, entry.quality);
                    accepted.Add(entry);
                    if (twoHanded && newSecondary != null)
                    {
                        // A two-handed weapon cannot silently evict existing
                        // secondary gear. Reject it unless the slot is empty.
                        newPrimary = Clone(_gear.Primary);
                        accepted.RemoveAt(accepted.Count - 1);
                        rejected.Add(entry);
                    }
                }
                else if (!twoHanded && newSecondary == null &&
                    entry.item.RequiredSlot == Item.SlotType.PrimaryOrSecondary)
                {
                    newSecondary = Capture(entry.item, entry.quality);
                    accepted.Add(entry);
                }
                else rejected.Add(entry);
            }
            if (accepted.Count == 0)
            {
                detail = "no eligible empty pet gear slot was available";
                return false;
            }

            var mutation = _tradeSnapshot;
            string before = Serialize();
            NecromancerPetGearEnvelope old = _gear;
            try
            {
                foreach (var entry in rejected)
                    if (!GameData.PlayerInv.AddItemToInv(entry.item, entry.quality))
                        throw new InvalidOperationException(
                            $"inventory could not safely return {entry.item.ItemName}");

                _gear = new NecromancerPetGearEnvelope
                {
                    Version = 2,
                    Primary = newPrimary,
                    Secondary = newSecondary
                };
                string after = Serialize();
                ClearTradeSlots(window);
                if (!Items.BagSystem.CommitExternalMutation(
                    "necromancer-pet-equip", mutation, GetPath(), before, after))
                    throw new InvalidOperationException(
                        "the crash-safe inventory transaction did not commit");
                NecromancerPets.ApplyEscrowToActivePet();
                _tradeSnapshot = null;
                detail = $"accepted {accepted.Count} item(s); {rejected.Count} returned";
                return true;
            }
            catch (Exception ex)
            {
                _gear = old;
                Items.BagSystem.RollbackExternalMutation(mutation,
                    GetPath(), before);
                _tradeSnapshot = null;
                // The pre-drag snapshot restores every offered item exactly.
                // Empty and
                // close the native trade window so CancelTrade cannot return
                // the same objects a second time.
                ClearTradeSlots(window);
                detail = ex.Message;
                return false;
            }
        }

        private static bool TryAcceptTradeIntoThrallBag(TradeWindow window,
            IList<(Item item, int quality)> offered, string bagId,
            List<Items.BagSystem.BagSlotData> contents, out string detail)
        {
            detail = null;
            Items.BagSystem.MutationSnapshot mutation = _tradeSnapshot;
            string gearBefore = Serialize();
            var accepted = new List<(Item item, int quality)>();
            var rejected = new List<(Item item, int quality)>();
            try
            {
                foreach (var entry in offered)
                {
                    if (!TryCreateThrallBagSlot(entry.item, entry.quality,
                        out Items.BagSystem.BagSlotData slot, out _))
                    {
                        rejected.Add(entry);
                        continue;
                    }
                    contents.Add(slot);
                    if (!TryApplyThrallBagContents(bagId, contents,
                        out _, out _, out _))
                    {
                        contents.RemoveAt(contents.Count - 1);
                        rejected.Add(entry);
                        continue;
                    }
                    accepted.Add(entry);
                }
                if (accepted.Count == 0)
                {
                    RestoreSerializedInMemory(gearBefore);
                    detail = "no offered item forms a legal open Thrall Bag loadout";
                    return false;
                }

                foreach (var entry in rejected)
                    if (!GameData.PlayerInv.AddItemToInv(
                        entry.item, entry.quality))
                        throw new InvalidOperationException(
                            $"inventory could not safely return {entry.item.ItemName}");

                string gearAfter = Serialize();
                ClearTradeSlots(window);
                if (!Items.BagSystem.CommitExternalMutation(
                    "thrall-bag-trade-equip", mutation, GetPath(),
                    gearBefore, gearAfter))
                    throw new InvalidOperationException(
                        "the crash-safe Thrall Bag transaction did not commit");
                NecromancerPets.ApplyEscrowToActivePet();
                Items.BagWindow.RefreshAll();
                _tradeSnapshot = null;
                detail = $"placed {accepted.Count} item(s) in the Thrall Bag; " +
                    $"{rejected.Count} returned";
                return true;
            }
            catch (Exception ex)
            {
                RestoreSerializedInMemory(gearBefore);
                Items.BagSystem.RollbackExternalMutation(
                    mutation, GetPath(), gearBefore);
                _tradeSnapshot = null;
                ClearTradeSlots(window);
                detail = ex.Message;
                return false;
            }
        }

        internal static bool ReturnAllToPlayer(out string detail)
        {
            detail = null;
            if (!_loaded)
            {
                detail = "pet equipment escrow is not safely loaded";
                return false;
            }
            if (Items.BagSystem.TryGetSingleCarriedThrallBag(
                out string thrallBagId,
                out List<Items.BagSystem.BagSlotData> thrallContents,
                out _))
                return ReturnThrallBagContents(
                    thrallBagId, thrallContents, out detail);
            var entries = new List<(Item item, int quality)>();
            AddResolved(entries, _gear.Primary);
            AddResolved(entries, _gear.Secondary);
            if (entries.Count == 0)
            {
                detail = "the pet equipment escrow is empty";
                return true;
            }

            int needed = entries.Count;
            int emptySlots = GameData.PlayerInv?.StoredSlots?.Count(slot =>
                slot != null && !slot.TrashSlot &&
                slot.MyItem == GameData.PlayerInv.Empty) ?? 0;
            if (emptySlots < needed)
            {
                detail = $"free {needed - emptySlots} more inventory slot(s) first";
                return false;
            }

            var mutation = Items.BagSystem.BeginExternalMutation();
            string before = Serialize();
            NecromancerPetGearEnvelope old = _gear;
            try
            {
                foreach (var entry in entries)
                {
                    int beforeCount = CountInventoryCopies(entry.item,
                        entry.quality);
                    if (!GameData.PlayerInv.AddItemToInv(entry.item, entry.quality))
                        throw new InvalidOperationException(
                            $"inventory rejected {entry.item.ItemName}");
                    int afterCount = CountInventoryCopies(entry.item,
                        entry.quality);
                    if (afterCount != beforeCount + 1)
                        throw new InvalidOperationException(
                            $"inventory did not retain {entry.item.ItemName} " +
                            $"at exact quality {entry.quality}");
                }
                _gear = new NecromancerPetGearEnvelope();
                string after = Serialize();
                if (!Items.BagSystem.CommitExternalMutation(
                    "necromancer-pet-gear-return", mutation,
                    GetPath(), before, after))
                    throw new InvalidOperationException(
                        "the crash-safe inventory transaction did not commit");
                NecromancerPets.ApplyEscrowToActivePet();
                detail = "returned " + string.Join(", ", entries.Select(entry =>
                    $"{entry.item.ItemName} (quality {entry.quality})"));
                return true;
            }
            catch (Exception ex)
            {
                _gear = old;
                Items.BagSystem.RollbackExternalMutation(mutation,
                    GetPath(), before);
                detail = ex.Message;
                return false;
            }
        }

        private static bool ReturnThrallBagContents(string bagId,
            List<Items.BagSystem.BagSlotData> contents, out string detail)
        {
            detail = null;
            if (contents == null || contents.Count == 0)
            {
                detail = "the Thrall Bag is empty";
                return true;
            }
            var entries = new List<(Item item, int quality)>();
            foreach (Items.BagSystem.BagSlotData slot in contents)
            {
                Item item = ResolveThrallBagItem(slot);
                if (item == null)
                {
                    detail = $"{slot?.ItemName ?? "an item"} could not be " +
                        "resolved from its exact snapshot";
                    return false;
                }
                entries.Add((item, Mathf.Max(1, slot.Quantity)));
            }
            int emptySlots = GameData.PlayerInv?.StoredSlots?.Count(slot =>
                slot != null && !slot.TrashSlot &&
                slot.MyItem == GameData.PlayerInv.Empty) ?? 0;
            if (emptySlots < entries.Count)
            {
                detail = $"free {entries.Count - emptySlots} more inventory slot(s) first";
                return false;
            }

            Items.BagSystem.MutationSnapshot mutation =
                Items.BagSystem.BeginExternalMutation();
            string gearBefore = Serialize();
            var originalContents = new List<Items.BagSystem.BagSlotData>(contents);
            try
            {
                contents.Clear();
                if (!TryApplyThrallBagContents(bagId, contents,
                    out _, out string gearAfter, out string applyReason))
                    throw new InvalidOperationException(applyReason);
                foreach (var entry in entries)
                    if (!GameData.PlayerInv.AddItemToInv(
                        entry.item, entry.quality))
                        throw new InvalidOperationException(
                            $"inventory rejected {entry.item.ItemName}");
                if (!Items.BagSystem.CommitExternalMutation(
                    "thrall-bag-return-all", mutation, GetPath(),
                    gearBefore, gearAfter))
                    throw new InvalidOperationException(
                        "the crash-safe Thrall Bag return did not commit");
                NecromancerPets.ApplyEscrowToActivePet();
                Items.BagWindow.RefreshAll();
                detail = "returned " + string.Join(", ", entries.Select(entry =>
                    $"{entry.item.ItemName} (quality {entry.quality})"));
                return true;
            }
            catch (Exception ex)
            {
                contents.Clear();
                contents.AddRange(originalContents);
                RestoreSerializedInMemory(gearBefore);
                Items.BagSystem.RollbackExternalMutation(
                    mutation, GetPath(), gearBefore);
                detail = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Projects a QA loadout onto the live pet without writing the sidecar.
        /// The caller owns restoring the serialized in-memory state afterward.
        /// </summary>
        internal static bool ApplyTransientTestLoadout(Item primary,
            int primaryQuality, Item secondary, int secondaryQuality,
            out string detail)
        {
            detail = null;
            if (!_loaded)
            {
                detail = "pet equipment escrow is not safely loaded";
                return false;
            }
            if (NecromancerPets.ActivePet == null)
            {
                detail = "summon a Bone Thrall before applying the QA loadout";
                return false;
            }
            if (!IsEligible(primary) || primary.Shield ||
                primary.RequiredSlot == Item.SlotType.Secondary)
            {
                detail = "the QA primary item is not an eligible main-hand weapon";
                return false;
            }
            bool twoHanded = primary.ThisWeaponType ==
                    Item.WeaponType.TwoHandMelee ||
                primary.ThisWeaponType == Item.WeaponType.TwoHandStaff ||
                primary.ThisWeaponType == Item.WeaponType.TwoHandBow;
            if (twoHanded && secondary != null)
            {
                detail = "the QA two-handed primary item must be used alone";
                return false;
            }
            if (secondary != null &&
                (!IsEligible(secondary) || !CanUseAsOffhand(secondary)))
            {
                detail = "the QA secondary item is not an eligible offhand";
                return false;
            }
            if (primaryQuality <= 0 ||
                (secondary != null && secondaryQuality <= 0))
            {
                detail = "QA gear quality values must be positive";
                return false;
            }

            _gear = new NecromancerPetGearEnvelope
            {
                Version = 2,
                Primary = Capture(primary, primaryQuality),
                Secondary = secondary != null
                    ? Capture(secondary, secondaryQuality) : null
            };
            NecromancerPets.ApplyEscrowToActivePet();
            bool valid = NecromancerPets.ValidateActivePetEquipment(
                out string validation);
            detail = validation;
            return valid;
        }

        /// <summary>
        /// Installs a deterministic QA-only loadout through the same durable
        /// sidecar transaction used by player trading. The test harness uses
        /// this when Proton rejects synthetic pointer clicks, so the real
        /// persistence, stat projection, visual attachment, return, and
        /// duplicate-prevention paths can still be exercised in game.
        /// </summary>
        internal static bool InstallTestLoadout(Item primary,
            int primaryQuality, Item secondary, int secondaryQuality,
            out string detail)
        {
            detail = null;
            if (!_loaded)
            {
                detail = "pet equipment escrow is not safely loaded";
                return false;
            }
            if (HasGear)
            {
                detail = "pet equipment escrow must be empty before the QA loadout";
                return false;
            }
            if (Items.BagSystem.TryGetSingleCarriedThrallBag(
                out string thrallBagId,
                out List<Items.BagSystem.BagSlotData> thrallContents,
                out _))
                return InstallTestLoadoutIntoThrallBag(primary,
                    primaryQuality, secondary, secondaryQuality,
                    thrallBagId, thrallContents, out detail);
            if (!IsEligible(primary) || primary.Shield ||
                primary.RequiredSlot == Item.SlotType.Secondary)
            {
                detail = "the QA primary item is not an eligible main-hand weapon";
                return false;
            }
            bool twoHanded = primary.ThisWeaponType ==
                    Item.WeaponType.TwoHandMelee ||
                primary.ThisWeaponType == Item.WeaponType.TwoHandStaff ||
                primary.ThisWeaponType == Item.WeaponType.TwoHandBow;
            if (twoHanded && secondary != null)
            {
                detail = "the QA two-handed primary item must be used alone";
                return false;
            }
            if (secondary != null &&
                (!IsEligible(secondary) || !CanUseAsOffhand(secondary)))
            {
                detail = "the QA secondary item is not an eligible offhand";
                return false;
            }
            if (primaryQuality <= 0 ||
                (secondary != null && secondaryQuality <= 0))
            {
                detail = "QA gear quality values must be positive";
                return false;
            }
            if (NecromancerPets.ActivePet == null)
            {
                detail = "summon a Bone Thrall before installing the QA loadout";
                return false;
            }

            var mutation = Items.BagSystem.BeginExternalMutation();
            string before = Serialize();
            NecromancerPetGearEnvelope old = _gear;
            try
            {
                _gear = new NecromancerPetGearEnvelope
                {
                    Version = 2,
                    Primary = Capture(primary, primaryQuality),
                    Secondary = secondary != null
                        ? Capture(secondary, secondaryQuality) : null
                };
                string after = Serialize();
                if (!Items.BagSystem.CommitExternalMutation(
                    "necromancer-pet-qa-loadout", mutation,
                    GetPath(), before, after))
                    throw new InvalidOperationException(
                        "the crash-safe QA equipment transaction did not commit");
                NecromancerPets.ApplyEscrowToActivePet();
                if (!NecromancerPets.ValidateActivePetEquipment(
                    out string validation))
                    throw new InvalidOperationException(
                        "live pet validation failed: " + validation);
                detail = $"installed {primary.ItemName} (quality " +
                    $"{primaryQuality})" +
                    (secondary != null
                        ? $" and {secondary.ItemName} (quality " +
                          $"{secondaryQuality})"
                        : "") +
                    $"; {validation}";
                return true;
            }
            catch (Exception ex)
            {
                _gear = old;
                Items.BagSystem.RollbackExternalMutation(mutation,
                    GetPath(), before);
                NecromancerPets.ApplyEscrowToActivePet();
                detail = ex.Message;
                return false;
            }
        }

        private static bool InstallTestLoadoutIntoThrallBag(Item primary,
            int primaryQuality, Item secondary, int secondaryQuality,
            string bagId, List<Items.BagSystem.BagSlotData> contents,
            out string detail)
        {
            detail = null;
            if (contents == null || contents.Count != 0)
            {
                detail = "the Thrall Bag must be empty before the QA loadout";
                return false;
            }
            if (NecromancerPets.ActivePet == null)
            {
                detail = "summon a Bone Thrall before installing the QA loadout";
                return false;
            }
            string primaryReason = null;
            string secondaryReason = null;
            if (!TryCreateThrallBagSlot(primary, primaryQuality,
                    out Items.BagSystem.BagSlotData primarySlot,
                    out primaryReason))
            {
                detail = primaryReason;
                return false;
            }
            Items.BagSystem.BagSlotData secondarySlot = null;
            if (secondary != null &&
                !TryCreateThrallBagSlot(secondary, secondaryQuality,
                    out secondarySlot, out secondaryReason))
            {
                detail = secondaryReason;
                return false;
            }

            Items.BagSystem.MutationSnapshot mutation =
                Items.BagSystem.BeginExternalMutation();
            string gearBefore = Serialize();
            try
            {
                contents.Add(primarySlot);
                if (secondarySlot != null) contents.Add(secondarySlot);
                if (!TryApplyThrallBagContents(bagId, contents,
                    out _, out string gearAfter, out string applyReason))
                    throw new InvalidOperationException(applyReason);
                if (!Items.BagSystem.CommitExternalMutation(
                    "thrall-bag-qa-loadout", mutation, GetPath(),
                    gearBefore, gearAfter))
                    throw new InvalidOperationException(
                        "the crash-safe QA Thrall Bag transaction did not commit");
                NecromancerPets.ApplyEscrowToActivePet();
                Items.BagWindow.RefreshAll();
                if (!NecromancerPets.ValidateActivePetEquipment(
                    out string validation))
                    throw new InvalidOperationException(
                        "live pet validation failed: " + validation);
                detail = $"installed {primary.ItemName}" +
                    (secondary != null
                        ? $" and {secondary.ItemName}" : "") +
                    $" through the Thrall Bag; {validation}";
                return true;
            }
            catch (Exception ex)
            {
                contents.Clear();
                RestoreSerializedInMemory(gearBefore);
                Items.BagSystem.RollbackExternalMutation(
                    mutation, GetPath(), gearBefore);
                detail = ex.Message;
                return false;
            }
        }

        private static int CountInventoryCopies(Item item, int quality)
        {
            Inventory inventory = GameData.PlayerInv;
            if (inventory?.StoredSlots == null || item == null) return 0;
            return inventory.StoredSlots.Count(slot => slot != null &&
                !slot.TrashSlot && slot.MyItem != null &&
                slot.MyItem != inventory.Empty && !slot.MyItem.Stackable &&
                slot.Quantity == quality &&
                (ReferenceEquals(slot.MyItem, item) ||
                 (!string.IsNullOrEmpty(item.Id) &&
                  string.Equals(slot.MyItem.Id, item.Id,
                      StringComparison.Ordinal)) ||
                 string.Equals(slot.MyItem.ItemName, item.ItemName,
                     StringComparison.Ordinal)));
        }

        private static bool IsEligible(Item item)
        {
            if (item == null || item.Stackable || item.RequiredSlot == Item.SlotType.General)
                return false;
            if (item.Shield) return true;
            switch (item.RequiredSlot)
            {
                case Item.SlotType.Primary:
                case Item.SlotType.Secondary:
                case Item.SlotType.PrimaryOrSecondary:
                    return item.WeaponDmg > 0 ||
                        !string.IsNullOrEmpty(item.EquipmentToActivate);
                default:
                    return false;
            }
        }

        private static NecromancerPetGearItem Capture(Item item, int quality)
        {
            return new NecromancerPetGearItem
            {
                ItemId = item.Id ?? item.ItemName,
                ItemName = item.ItemName,
                Quality = quality,
                ScalarState = CaptureScalarState(item)
            };
        }

        private static NecromancerPetGearItem Clone(NecromancerPetGearItem item)
        {
            return item == null ? null : new NecromancerPetGearItem
            {
                ItemId = item.ItemId,
                ItemName = item.ItemName,
                Quality = item.Quality,
                ScalarState = item.ScalarState == null
                    ? new List<NecromancerPetGearScalar>()
                    : item.ScalarState.Select(CloneScalar).ToList()
            };
        }

        private static NecromancerPetGearScalar CloneScalar(
            NecromancerPetGearScalar field)
        {
            return field == null ? null : new NecromancerPetGearScalar
            {
                Key = field.Key,
                Type = field.Type,
                Value = field.Value
            };
        }

        private static List<NecromancerPetGearScalar> CaptureScalarState(
            Item item)
        {
            var output = new List<NecromancerPetGearScalar>();
            if (item == null) return output;
            foreach (FieldInfo field in GetPublicInstanceFields(item.GetType())
                .OrderBy(FieldKey, StringComparer.Ordinal))
            {
                try
                {
                    object value = field.GetValue(item);
                    if (!TryFormatScalar(field.FieldType, value,
                        out string type, out string formatted)) continue;
                    output.Add(new NecromancerPetGearScalar
                    {
                        Key = FieldKey(field),
                        Type = type,
                        Value = formatted
                    });
                }
                catch (Exception ex)
                {
                    SkillsPlugin.Log.LogWarning(
                        $"Skipped pet gear state field {FieldKey(field)}: {ex.Message}");
                }
            }
            return output;
        }

        private static void ApplyScalarState(Item item,
            IEnumerable<NecromancerPetGearScalar> state)
        {
            if (item == null || state == null) return;
            List<FieldInfo> fields = GetPublicInstanceFields(item.GetType())
                .ToList();
            var exact = fields.ToDictionary(FieldKey, StringComparer.Ordinal);
            foreach (NecromancerPetGearScalar saved in state)
            {
                if (saved == null) continue;
                if (!exact.TryGetValue(saved.Key, out FieldInfo field))
                {
                    string fieldName = saved.Key.Substring(
                        saved.Key.LastIndexOf('|') + 1);
                    FieldInfo[] sameName = fields.Where(candidate =>
                        string.Equals(candidate.Name, fieldName,
                            StringComparison.Ordinal)).ToArray();
                    if (sameName.Length != 1) continue;
                    field = sameName[0];
                }
                if (TryParseScalar(field.FieldType, saved.Type, saved.Value,
                    out object restored))
                    field.SetValue(item, restored);
            }
        }

        private static IEnumerable<FieldInfo> GetPublicInstanceFields(Type type)
        {
            var fields = new List<FieldInfo>();
            for (Type current = type; current != null &&
                 current != typeof(ScriptableObject);
                 current = current.BaseType)
            {
                fields.AddRange(current.GetFields(BindingFlags.Instance |
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly).Where(field =>
                        !field.IsStatic && !field.IsInitOnly &&
                        !field.IsNotSerialized && (field.IsPublic ||
                         field.GetCustomAttribute<SerializeField>() != null)));
            }
            return fields;
        }

        private static string FieldKey(FieldInfo field) =>
            (field.DeclaringType?.FullName ?? "Item") + "|" + field.Name;

        private static bool TryFormatScalar(Type type, object value,
            out string typeName, out string formatted)
        {
            typeName = "";
            formatted = "";
            if (type == typeof(string))
            {
                typeName = value == null ? "string-null" : "string";
                formatted = value as string ?? "";
                return true;
            }
            if (type.IsEnum)
            {
                typeName = "enum";
                formatted = Enum.Format(type, value, "D");
                return true;
            }
            if (type == typeof(Color))
            {
                Color color = (Color)value;
                typeName = "color";
                formatted = JoinFloats(color.r, color.g, color.b, color.a);
                return true;
            }
            if (type == typeof(Color32))
            {
                Color32 color = (Color32)value;
                typeName = "color32";
                formatted = string.Join("|", color.r, color.g, color.b, color.a);
                return true;
            }
            if (type == typeof(Vector2))
            {
                Vector2 vector = (Vector2)value;
                typeName = "vector2";
                formatted = JoinFloats(vector.x, vector.y);
                return true;
            }
            if (type == typeof(Vector3))
            {
                Vector3 vector = (Vector3)value;
                typeName = "vector3";
                formatted = JoinFloats(vector.x, vector.y, vector.z);
                return true;
            }
            if (type == typeof(Vector4))
            {
                Vector4 vector = (Vector4)value;
                typeName = "vector4";
                formatted = JoinFloats(vector.x, vector.y, vector.z, vector.w);
                return true;
            }
            if (type == typeof(Quaternion))
            {
                Quaternion value4 = (Quaternion)value;
                typeName = "quaternion";
                formatted = JoinFloats(value4.x, value4.y, value4.z, value4.w);
                return true;
            }

            TypeCode code = Type.GetTypeCode(type);
            switch (code)
            {
                case TypeCode.Boolean:
                case TypeCode.Byte:
                case TypeCode.SByte:
                case TypeCode.Int16:
                case TypeCode.UInt16:
                case TypeCode.Int32:
                case TypeCode.UInt32:
                case TypeCode.Int64:
                case TypeCode.UInt64:
                case TypeCode.Single:
                case TypeCode.Double:
                case TypeCode.Decimal:
                case TypeCode.Char:
                    typeName = code.ToString().ToLowerInvariant();
                    formatted = Convert.ToString(value,
                        CultureInfo.InvariantCulture);
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryParseScalar(Type targetType, string savedType,
            string value, out object parsed)
        {
            parsed = null;
            try
            {
                if (targetType == typeof(string))
                {
                    if (savedType != "string" && savedType != "string-null")
                        return false;
                    parsed = savedType == "string-null" ? null : value;
                    return true;
                }
                if (targetType.IsEnum)
                {
                    if (savedType != "enum") return false;
                    parsed = Enum.Parse(targetType, value);
                    return true;
                }
                string expected;
                if (targetType == typeof(Color)) expected = "color";
                else if (targetType == typeof(Color32)) expected = "color32";
                else if (targetType == typeof(Vector2)) expected = "vector2";
                else if (targetType == typeof(Vector3)) expected = "vector3";
                else if (targetType == typeof(Vector4)) expected = "vector4";
                else if (targetType == typeof(Quaternion)) expected = "quaternion";
                else expected = Type.GetTypeCode(targetType).ToString()
                    .ToLowerInvariant();
                if (!string.Equals(savedType, expected,
                    StringComparison.Ordinal)) return false;

                string[] parts;
                switch (expected)
                {
                    case "color":
                        parts = Split(value, 4);
                        parsed = new Color(ParseFloat(parts[0]),
                            ParseFloat(parts[1]), ParseFloat(parts[2]),
                            ParseFloat(parts[3]));
                        return true;
                    case "color32":
                        parts = Split(value, 4);
                        parsed = new Color32(byte.Parse(parts[0]),
                            byte.Parse(parts[1]), byte.Parse(parts[2]),
                            byte.Parse(parts[3]));
                        return true;
                    case "vector2":
                        parts = Split(value, 2);
                        parsed = new Vector2(ParseFloat(parts[0]),
                            ParseFloat(parts[1]));
                        return true;
                    case "vector3":
                        parts = Split(value, 3);
                        parsed = new Vector3(ParseFloat(parts[0]),
                            ParseFloat(parts[1]), ParseFloat(parts[2]));
                        return true;
                    case "vector4":
                        parts = Split(value, 4);
                        parsed = new Vector4(ParseFloat(parts[0]),
                            ParseFloat(parts[1]), ParseFloat(parts[2]),
                            ParseFloat(parts[3]));
                        return true;
                    case "quaternion":
                        parts = Split(value, 4);
                        parsed = new Quaternion(ParseFloat(parts[0]),
                            ParseFloat(parts[1]), ParseFloat(parts[2]),
                            ParseFloat(parts[3]));
                        return true;
                    default:
                        parsed = Convert.ChangeType(value, targetType,
                            CultureInfo.InvariantCulture);
                        return true;
                }
            }
            catch { return false; }
        }

        private static string JoinFloats(params float[] values) =>
            string.Join("|", values.Select(value =>
                value.ToString("R", CultureInfo.InvariantCulture)));

        private static string[] Split(string value, int expected)
        {
            string[] parts = (value ?? "").Split('|');
            if (parts.Length != expected)
                throw new FormatException("unexpected component count");
            return parts;
        }

        private static float ParseFloat(string value) => float.Parse(value,
            NumberStyles.Float, CultureInfo.InvariantCulture);

        private static void AddResolved(List<(Item item, int quality)> entries,
            NecromancerPetGearItem state)
        {
            if (state == null) return;
            Item item = Resolve(state);
            if (item == null)
                throw new InvalidDataException(
                    $"pet gear item '{state.ItemId}' is not currently resolvable");
            entries.Add((item, state.Quality));
        }

        private static void ClearTradeSlots(TradeWindow window)
        {
            foreach (ItemIcon slot in window.LootSlots)
            {
                slot.MyItem = GameData.PlayerInv.Empty;
                slot.Quantity = 0;
                slot.UpdateSlotImage();
            }
            window.CloseWindow(null);
        }

        internal static string Describe()
        {
            string primary = _gear.Primary == null ? "empty" :
                $"{_gear.Primary.ItemName} (quality {_gear.Primary.Quality})";
            string secondary = _gear.Secondary == null ? "empty" :
                $"{_gear.Secondary.ItemName} (quality {_gear.Secondary.Quality})";
            return $"primary={primary}, secondary={secondary}, source=" +
                (_gear.ManagedByThrallBag
                    ? $"Thrall Bag {_gear.ThrallBagId}" : "legacy escrow");
        }

        internal static bool RunSerializationSelfTest(out string detail)
        {
            Item original = ScriptableObject.CreateInstance<Item>();
            Item probe = ScriptableObject.CreateInstance<Item>();
            original.name = "Pet Gear State Probe";
            original.Id = "probe-primary";
            original.ItemName = "Probe Primary";
            original.EquipmentToActivate = "ProbeBlade";
            original.RequiredSlot = Item.SlotType.Primary;
            original.ThisWeaponType = Item.WeaponType.OneHandMelee;
            original.HP = 137;
            original.AC = 42;
            original.WeaponDmg = 19;
            original.WeaponDly = 2.375f;
            original.Str = 11;
            original.RareItem = true;
            original.ItemPrimaryColor = new Color(0.125f, 0.5f, 0.875f, 1f);

            NecromancerPetGearItem primary = Capture(original, 8);
            var sample = new NecromancerPetGearEnvelope
            {
                Version = 2,
                Primary = primary,
                Secondary = new NecromancerPetGearItem
                {
                    ItemId = "probe-secondary", ItemName = "Probe Secondary",
                    Quality = 15,
                    ScalarState = primary.ScalarState.Select(CloneScalar).ToList()
                }
            };
            string json = JsonUtility.ToJson(
                new NecromancerPetGearWireEnvelope
                {
                    Version = 2,
                    PrimaryPayload = EncodeItem(sample.Primary),
                    SecondaryPayload = EncodeItem(sample.Secondary)
                }, true);
            NecromancerPetGearEnvelope restored;
            try { restored = Deserialize(json); }
            catch (Exception ex)
            {
                detail = ex.Message;
                UnityEngine.Object.Destroy(original);
                UnityEngine.Object.Destroy(probe);
                return false;
            }

            ApplyScalarState(probe, restored.Primary?.ScalarState);
            bool roundTrip = IsSerializedDataValid(json) &&
                restored.Primary?.Quality == 8 &&
                restored.Secondary?.Quality == 15;
            bool exactState = probe.Id == original.Id &&
                probe.ItemName == original.ItemName &&
                probe.EquipmentToActivate == original.EquipmentToActivate &&
                probe.RequiredSlot == original.RequiredSlot &&
                probe.ThisWeaponType == original.ThisWeaponType &&
                probe.HP == 137 && probe.AC == 42 && probe.WeaponDmg == 19 &&
                Mathf.Approximately(probe.WeaponDly, 2.375f) &&
                probe.Str == 11 && probe.RareItem &&
                probe.ItemPrimaryColor == original.ItemPrimaryColor;

            string invalidVersion = JsonUtility.ToJson(
                new NecromancerPetGearWireEnvelope { Version = 3 }, true);
            string invalidQuality = JsonUtility.ToJson(
                new NecromancerPetGearWireEnvelope
                {
                    Version = 2,
                    PrimaryPayload = EncodeItem(new NecromancerPetGearItem
                    {
                        ItemId = "x", ItemName = "x", Quality = 0
                    })
                }, true);
            string corruptPayload = JsonUtility.ToJson(
                new NecromancerPetGearWireEnvelope
                {
                    Version = 2,
                    PrimaryPayload = "not-base64"
                }, true);
            bool guards = !IsSerializedDataValid(invalidVersion) &&
                !IsSerializedDataValid(invalidQuality) &&
                !IsSerializedDataValid(corruptPayload) &&
                IsSerializedDataValid(JsonUtility.ToJson(
                    new NecromancerPetGearWireEnvelope { Version = 2 }, true));
            bool valid = roundTrip && exactState && guards;
            int restoredFieldCount = restored.Primary?.ScalarState?.Count ?? -1;
            NecromancerPetGearScalar restoredFirst = restored.Primary?
                .ScalarState?.FirstOrDefault();
            detail = valid
                ? $"qualities 8/15 and {primary.ScalarState.Count} scalar fields round-trip; invalid version/quality/payload rejected"
                : $"roundTrip={roundTrip}; exactState={exactState}; guards={guards}; " +
                  $"fields={primary.ScalarState.Count}/{restoredFieldCount}; " +
                  $"restored={restored.Primary?.ItemId ?? "<null>"}/" +
                  $"{restored.Primary?.Quality.ToString() ?? "<null>"}; " +
                  $"first={restoredFirst?.Key ?? "<null>"}:" +
                  $"{restoredFirst?.Type ?? "<null>"}:" +
                  $"{restoredFirst?.Value ?? "<null>"}; probe=" +
                  $"id:{probe.Id},name:{probe.ItemName},visual:{probe.EquipmentToActivate}," +
                  $"slot:{probe.RequiredSlot},weapon:{probe.ThisWeaponType},hp:{probe.HP}," +
                  $"ac:{probe.AC},dmg:{probe.WeaponDmg},delay:{probe.WeaponDly:R}," +
                  $"str:{probe.Str},rare:{probe.RareItem},color:{probe.ItemPrimaryColor}";
            UnityEngine.Object.Destroy(original);
            UnityEngine.Object.Destroy(probe);
            return valid;
        }

        internal static void ResetRuntime()
        {
            _gear = new NecromancerPetGearEnvelope();
            _loaded = false;
            _tradeSnapshot = null;
        }

        internal static void CaptureTradeStart(ItemIcon source)
        {
            if (!_loaded || source == null ||
                !Items.BagSystem.IsDirectInventorySlot(source) ||
                source.MyItem == null || source.MyItem == GameData.PlayerInv?.Empty)
                return;
            NecromancerPetMarker marker = null;
            if (GameData.Trading)
                marker = GameData.TradeWindow?.GetTradee()?
                    .GetComponentInParent<NecromancerPetMarker>();
            else
                marker = GameData.PlayerControl?.CurrentTarget?
                    .GetComponent<NecromancerPetMarker>();
            if (marker == null || marker.Owner != GameData.PlayerControl?.Myself)
                return;
            // A new first drag supersedes any abandoned cursor snapshot. Once
            // the native trade window is open, retain the original snapshot as
            // more items are moved into it.
            if (!GameData.Trading || _tradeSnapshot == null)
                _tradeSnapshot = Items.BagSystem.BeginExternalMutation();
        }

        internal static void ClearTradeSnapshot()
        {
            _tradeSnapshot = null;
        }
    }
}
