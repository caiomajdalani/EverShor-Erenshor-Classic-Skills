using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using HarmonyLib;
using UnityEngine;

namespace ErenshorSkills.Items
{
    /// <summary>
    /// Character bag identities are intentionally local to one save. Bags in
    /// Erenshor's shared bank therefore use a separate identity range and a
    /// shared, validated sidecar until they are withdrawn by any character.
    /// </summary>
    internal static class BankBagStorage
    {
        internal const int GlobalIdentityFloor = 1000000;
        private const int MaxGlobalBankBags = 3167;
        private const string FileName = "ClassicSkillsBankBags.json";
        private static readonly System.Reflection.FieldInfo BankPageField =
            AccessTools.Field(typeof(GlobalBank), "page");

        internal static string GetPath()
        {
            return Path.Combine(Application.persistentDataPath,
                "ESSaveData", FileName);
        }

        internal static string CreateEmptySerialized()
        {
            return BagSystem.SerializeEnvelope(new BagSystem.BagSaveEnvelope
            {
                NextBagCounter = GlobalIdentityFloor
            });
        }

        internal static bool IsSerializedDataValid(string json)
        {
            return TryParse(json, out _, out _);
        }

        internal static bool TryLoad(out BagSystem.BagSaveEnvelope envelope,
            out string serialized, out string error)
        {
            envelope = null;
            serialized = null;
            error = null;
            string path = GetPath();
            try
            {
                if (!File.Exists(path) && !File.Exists(path + ".bak"))
                {
                    serialized = CreateEmptySerialized();
                    if (!SafeFileIO.TryWriteAllTextAtomic(path, serialized,
                        out error))
                    {
                        serialized = null;
                        return false;
                    }
                }

                if (!SafeFileIO.TryReadAllTextWithBackup(path,
                    IsSerializedDataValid, out serialized,
                    out bool recovered, out error))
                    return false;
                if (!TryParse(serialized, out envelope, out error))
                    return false;
                if (recovered)
                    SkillsPlugin.Log.LogWarning(
                        "Recovered shared bank-bag contents from backup.");
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                envelope = null;
                serialized = null;
                return false;
            }
        }

        internal static bool TryParse(string json,
            out BagSystem.BagSaveEnvelope envelope, out string error)
        {
            envelope = null;
            if (!BagSystem.TryParseEnvelope(json, out envelope, out error))
                return false;
            if (envelope.NextBagCounter < GlobalIdentityFloor)
            {
                error = "global bank-bag counter is outside its reserved range";
                envelope = null;
                return false;
            }
            if (envelope.Bags == null ||
                envelope.Bags.Count > MaxGlobalBankBags)
            {
                error = "global bank-bag record count is invalid";
                envelope = null;
                return false;
            }

            foreach (BagSystem.BagSaveRecord record in envelope.Bags)
            {
                if (!TryReadIdentity(record?.Id, out int identity) ||
                    identity < GlobalIdentityFloor)
                {
                    error = $"bank-bag identity '{record?.Id}' is outside " +
                        "the shared range";
                    envelope = null;
                    return false;
                }
                if (BagSystem.IsCorpseBag(record.Name))
                {
                    error = "Corpse Bags cannot be stored in the shared bank";
                    envelope = null;
                    return false;
                }
                foreach (BagSystem.BagSlotData item in record.Items)
                {
                    if (BagSystem.IsBag(item?.ItemName))
                    {
                        error = $"{record.Id} contains a nested bag";
                        envelope = null;
                        return false;
                    }
                }
            }
            return true;
        }

        internal static bool TryStore(BagSystem.BagSaveEnvelope envelope,
            BagSystem.BagSaveRecord localRecord, out int globalIdentity,
            out string error)
        {
            globalIdentity = -1;
            error = null;
            if (envelope?.Bags == null || localRecord == null ||
                string.IsNullOrEmpty(localRecord.Name) ||
                localRecord.Items == null)
            {
                error = "the bag deposit record is incomplete";
                return false;
            }
            if (BagSystem.IsCorpseBag(localRecord.Name))
            {
                error = "Corpse Bags cannot be banked";
                return false;
            }
            if (envelope.Bags.Count >= MaxGlobalBankBags)
            {
                error = "the shared bank-bag store is full";
                return false;
            }

            var used = new HashSet<int>();
            foreach (BagSystem.BagSaveRecord existing in envelope.Bags)
                if (TryReadIdentity(existing?.Id, out int number))
                    used.Add(number);
            int candidate = Math.Max(GlobalIdentityFloor,
                envelope.NextBagCounter);
            while (candidate < int.MaxValue && used.Contains(candidate))
                candidate++;
            if (candidate >= int.MaxValue)
            {
                error = "no shared bank-bag identities remain";
                return false;
            }

            BagSystem.BagSaveRecord stored = CloneRecord(localRecord);
            stored.Id = $"bag{candidate}";
            int previousCounter = envelope.NextBagCounter;
            envelope.Bags.Add(stored);
            envelope.NextBagCounter = candidate + 1;
            string serialized = BagSystem.SerializeEnvelope(envelope);
            if (!IsSerializedDataValid(serialized))
            {
                envelope.Bags.Remove(stored);
                envelope.NextBagCounter = previousCounter;
                error = "the resulting shared bank-bag data failed validation";
                return false;
            }
            globalIdentity = candidate;
            return true;
        }

        internal static bool TryTake(BagSystem.BagSaveEnvelope envelope,
            int globalIdentity, out BagSystem.BagSaveRecord record,
            out string error)
        {
            record = null;
            error = null;
            if (envelope?.Bags == null ||
                globalIdentity < GlobalIdentityFloor)
            {
                error = "the shared bank-bag identity is invalid";
                return false;
            }
            for (int i = 0; i < envelope.Bags.Count; i++)
            {
                BagSystem.BagSaveRecord candidate = envelope.Bags[i];
                if (!TryReadIdentity(candidate?.Id, out int number) ||
                    number != globalIdentity) continue;
                record = CloneRecord(candidate);
                record.Id = "";
                envelope.Bags.RemoveAt(i);
                return true;
            }
            error = $"shared contents for bag{globalIdentity} are missing";
            return false;
        }

        internal static bool TryGet(BagSystem.BagSaveEnvelope envelope,
            int globalIdentity, out BagSystem.BagSaveRecord record)
        {
            record = null;
            if (envelope?.Bags == null) return false;
            foreach (BagSystem.BagSaveRecord candidate in envelope.Bags)
            {
                if (!TryReadIdentity(candidate?.Id, out int number) ||
                    number != globalIdentity) continue;
                record = CloneRecord(candidate);
                return true;
            }
            return false;
        }

        internal static bool TrySerialize(BagSystem.BagSaveEnvelope envelope,
            out string serialized, out string error)
        {
            serialized = BagSystem.SerializeEnvelope(envelope);
            if (IsSerializedDataValid(serialized))
            {
                error = null;
                return true;
            }
            error = "shared bank-bag serialization failed validation";
            serialized = null;
            return false;
        }

        /// <summary>
        /// GlobalBank.LoadBank calls ItemDatabase.GetItemByID from a method that
        /// the runtime can inline before Harmony patches it. Re-resolve add-on
        /// IDs from the validated native save so custom bank items cannot turn
        /// into Empty references after a reload.
        /// </summary>
        internal static bool TryRehydrateCustomItems(GlobalBank bank,
            out int restored, out string error)
        {
            restored = 0;
            error = null;
            if (bank?.StoredItems == null || bank.Quantities == null)
            {
                error = "global bank memory is unavailable";
                return false;
            }

            try
            {
                string path = BagTransferSafety.GetBankPath();
                if (!File.Exists(path))
                {
                    RefreshVisibleSlots(bank);
                    return true;
                }

                string json = File.ReadAllText(path);
                if (!Skills.Tradeskills.IsBankJsonValid(json))
                {
                    error = "native bank data failed validation";
                    return false;
                }
                BankSaveData data = JsonUtility.FromJson<BankSaveData>(json);
                if (data?.BankData == null ||
                    data.BankData.Count > bank.StoredItems.Count)
                {
                    error = "native bank item records do not match bank memory";
                    return false;
                }

                for (int i = 0; i < data.BankData.Count; i++)
                {
                    string id = data.BankData[i];
                    if (string.IsNullOrEmpty(id)) continue;
                    Item custom = ItemFactory.GetItemById(id);
                    if (custom == null) continue;
                    if (!string.Equals(custom.Id, id,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        error = $"custom bank item '{id}' resolved to a different ID";
                        return false;
                    }
                    if (!ReferenceEquals(bank.StoredItems[i], custom))
                    {
                        bank.StoredItems[i] = custom;
                        restored++;
                    }
                }

                RefreshVisibleSlots(bank);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// GlobalBank.DisplayBankPage performs the same inlinable item lookup.
        /// StoredItems already contains the authoritative resolved references,
        /// so copy those references directly into the 32 visible slots.
        /// </summary>
        internal static void RefreshVisibleSlots(GlobalBank bank)
        {
            if (bank?.StoredItems == null || bank.Quantities == null ||
                bank.BankSlots == null || BankPageField == null) return;
            int page = Mathf.Clamp((int)BankPageField.GetValue(bank), 1, 98);
            int offset = (page - 1) * 32;
            int count = Math.Min(32, bank.BankSlots.Length);
            for (int i = 0; i < count; i++)
            {
                int index = offset + i;
                if (index >= bank.StoredItems.Count ||
                    index >= bank.Quantities.Count) break;
                ItemIcon slot = bank.BankSlots[i];
                if (slot == null) continue;
                slot.MyItem = bank.StoredItems[index] ??
                    GameData.PlayerInv?.Empty;
                slot.Quantity = Math.Max(1, bank.Quantities[index]);
                slot.UpdateSlotImage();
            }
        }

        /// <summary>
        /// Keep GlobalBank's backing page lists aligned with a transaction that
        /// has already committed its visible slots to GBDATA. This prevents a
        /// later UI-only DisplayBankPage call from resurrecting the pre-move
        /// page image before the native bank performs its next SaveBank.
        /// </summary>
        internal static void SyncVisibleSlotsToStored(GlobalBank bank)
        {
            if (bank?.StoredItems == null || bank.Quantities == null ||
                bank.BankSlots == null || BankPageField == null) return;
            int page = Mathf.Clamp((int)BankPageField.GetValue(bank), 1, 98);
            int offset = (page - 1) * 32;
            int count = Math.Min(32, bank.BankSlots.Length);
            for (int i = 0; i < count; i++)
            {
                int index = offset + i;
                if (index >= bank.StoredItems.Count ||
                    index >= bank.Quantities.Count) break;
                ItemIcon slot = bank.BankSlots[i];
                if (slot == null) continue;
                bank.StoredItems[index] = slot.MyItem ??
                    GameData.PlayerInv?.Empty;
                bank.Quantities[index] = Math.Max(1, slot.Quantity);
            }
        }

        internal static bool AuditLoadedBank(GlobalBank bank,
            out string detail)
        {
            detail = null;
            if (BagSystem.HasUnresolvedGlobalBankTransaction(
                out string transactionReason))
            {
                detail = transactionReason;
                return false;
            }
            if (bank?.StoredItems == null || bank.Quantities == null)
            {
                detail = "global bank memory is unavailable";
                return false;
            }
            if (!TryLoad(out BagSystem.BagSaveEnvelope envelope,
                out _, out string loadError))
            {
                detail = "shared sidecar could not be loaded: " + loadError;
                return false;
            }

            var records = new Dictionary<int, BagSystem.BagSaveRecord>();
            foreach (BagSystem.BagSaveRecord record in envelope.Bags)
            {
                TryReadIdentity(record.Id, out int identity);
                records[identity] = record;
            }
            var physicalShared = new HashSet<int>();
            var physicalLegacy = new HashSet<int>();
            int count = Math.Min(bank.StoredItems.Count,
                bank.Quantities.Count);
            for (int i = 0; i < count; i++)
            {
                Item item = bank.StoredItems[i];
                int identity = bank.Quantities[i];
                bool isBag = item != null &&
                    item != GameData.PlayerInv?.Empty &&
                    BagSystem.IsBag(item);
                if (!isBag && !records.ContainsKey(identity)) continue;
                if (!isBag)
                {
                    detail = $"bag{identity} does not have a physical bag item";
                    return false;
                }
                if (identity < 100)
                    continue; // Identity-less legacy empty bag.
                if (identity < GlobalIdentityFloor)
                {
                    if (!physicalLegacy.Add(identity) ||
                        !BagSystem.HasMatchingLocalBagRecord(identity, item))
                    {
                        detail = $"legacy bag{identity} is duplicated or has no " +
                            "matching contents on this character";
                        return false;
                    }
                    continue;
                }
                if (!records.TryGetValue(identity,
                    out BagSystem.BagSaveRecord record))
                {
                    detail = $"bank slot {i} has an untracked shared bag identity";
                    return false;
                }
                if (!physicalShared.Add(identity))
                {
                    detail = $"bag{identity} appears in more than one bank slot";
                    return false;
                }
                if (!isBag || !string.Equals(record.Name,
                    item.ItemName, StringComparison.OrdinalIgnoreCase))
                {
                    detail = $"bag{identity} does not match its physical bank item";
                    return false;
                }
            }
            if (physicalShared.Count != records.Count)
            {
                detail = $"{records.Count - physicalShared.Count} shared bag record(s) " +
                    "have no physical bank item";
                return false;
            }
            detail = $"validated {records.Count} shared and " +
                $"{physicalLegacy.Count} legacy bank bag(s)";
            return true;
        }

        internal static bool RecordsEqual(BagSystem.BagSaveRecord left,
            BagSystem.BagSaveRecord right)
        {
            if (left == null || right == null ||
                !string.Equals(left.Name ?? "", right.Name ?? "",
                    StringComparison.OrdinalIgnoreCase) ||
                left.Items == null || right.Items == null ||
                left.Items.Count != right.Items.Count) return false;
            for (int i = 0; i < left.Items.Count; i++)
            {
                BagSystem.BagSlotData a = left.Items[i];
                BagSystem.BagSlotData b = right.Items[i];
                if (a == null || b == null ||
                    !string.Equals(a.ItemId ?? "", b.ItemId ?? "",
                        StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(a.ItemName ?? "", b.ItemName ?? "",
                        StringComparison.OrdinalIgnoreCase) ||
                    a.Quantity != b.Quantity ||
                    a.IsStackable != b.IsStackable)
                    return false;
            }
            return true;
        }

        private static BagSystem.BagSaveRecord CloneRecord(
            BagSystem.BagSaveRecord source)
        {
            var clone = new BagSystem.BagSaveRecord
            {
                Id = source?.Id ?? "",
                Name = source?.Name ?? "",
                Items = new List<BagSystem.BagSlotData>()
            };
            foreach (BagSystem.BagSlotData item in
                source?.Items ?? new List<BagSystem.BagSlotData>())
            {
                if (item == null) continue;
                clone.Items.Add(new BagSystem.BagSlotData
                {
                    ItemId = item.ItemId ?? "",
                    ItemName = item.ItemName ?? "",
                    Quantity = item.Quantity,
                    IsStackable = item.IsStackable
                });
            }
            return clone;
        }

        private static bool TryReadIdentity(string id, out int identity)
        {
            identity = -1;
            Match match = Regex.Match(id ?? "", "^bag([0-9]+)$",
                RegexOptions.IgnoreCase);
            return match.Success &&
                int.TryParse(match.Groups[1].Value, out identity);
        }
    }

    [HarmonyPatch(typeof(GlobalBank), nameof(GlobalBank.LoadBank))]
    internal static class Patch_GlobalBankBagIntegrity
    {
        [HarmonyPostfix]
        private static void Postfix(GlobalBank __instance)
        {
            if (!BankBagStorage.TryRehydrateCustomItems(__instance,
                out int restored, out string rehydrateError))
            {
                SkillsPlugin.Log.LogError(
                    "Shared bank-bag rehydration failed closed: " +
                    rehydrateError);
                return;
            }
            if (restored > 0)
                SkillsPlugin.Log.LogInfo(
                    $"Rehydrated {restored} custom global-bank item(s).");
            if (BankBagStorage.AuditLoadedBank(__instance,
                out string detail))
            {
                SkillsPlugin.Log.LogInfo(
                    "Shared bank-bag integrity: " + detail);
                return;
            }
            SkillsPlugin.Log.LogError(
                "Shared bank-bag integrity failed closed: " + detail);
        }
    }

    [HarmonyPatch(typeof(GlobalBank), nameof(GlobalBank.DisplayBankPage))]
    internal static class Patch_GlobalBankCustomItemDisplay
    {
        [HarmonyPostfix]
        private static void Postfix(GlobalBank __instance)
        {
            BankBagStorage.RefreshVisibleSlots(__instance);
        }
    }
}
