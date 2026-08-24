using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace ErenshorSkills.Items
{
    /// <summary>
    /// Keeps crafted containers out of native quantity-based transfer paths.
    /// Erenshor treats Quantity on a general-slot item as a stack count, while
    /// BagSystem uses values 100+ as a permanent bag identity. Every external
    /// route therefore needs an explicit one-physical-bag transaction.
    /// </summary>
    internal static class BagTransferSafety
    {
        private static readonly System.Reflection.FieldInfo BankPageField =
            AccessTools.Field(typeof(GlobalBank), "page");
        private static readonly System.Reflection.FieldInfo SwapWithField =
            AccessTools.Field(typeof(ItemIcon), "SwapWith");

        internal static bool TryHandleSimBagReturn(SimTradeWindow window,
            string action, bool notify = true)
        {
            ItemIcon tradeSlot = window?.LootSlot;
            if (tradeSlot?.MyItem == null ||
                !BagSystem.IsBag(tradeSlot.MyItem)) return false;

            ItemIcon destination = FindEmptyInventorySlot();
            if (destination == null)
            {
                if (notify)
                    ChatHelper.Send(
                        "<color=#EF5350>[Bag]</color> The SimPlayer cannot use bags, " +
                        "and your inventory has no safe slot to return this one. " +
                        "Make room and try again.");
                return true;
            }

            BagSystem.MutationSnapshot mutation =
                BagSystem.BeginExternalMutation();
            Item bag = tradeSlot.MyItem;
            int identity = Mathf.Max(1, tradeSlot.Quantity);
            destination.MyItem = bag;
            destination.Quantity = identity;
            destination.UpdateSlotImage();
            GameData.PlayerInv.UpdatePlayerInventory();

            if (!BagSystem.CommitExternalMutation(
                "sim-bag-" + action, mutation))
            {
                BagSystem.RollbackExternalMutation(mutation);
                if (notify)
                    ChatHelper.Send(
                        "<color=#EF5350>[Bag]</color> The bag stayed in the trade " +
                        "window because its safe return could not be saved.");
                return true;
            }

            tradeSlot.MyItem = GameData.PlayerInv.Empty;
            tradeSlot.Quantity = 1;
            tradeSlot.UpdateSlotImage();
            GameData.Trading = false;
            if (window.WindowParent != null)
                window.WindowParent.SetActive(false);
            if (notify)
                ChatHelper.Send(
                    "<color=#FFD54F>[Bag]</color> SimPlayers cannot use crafted " +
                    "bags. Your one original bag was returned.");
            return true;
        }

        internal static bool TryHandleVendorBagSale(VendorWindow window,
            ItemIcon source = null, bool notify = true)
        {
            source ??= GameData.SlotActiveForVendor;
            if (source?.MyItem == null || source.VendorSlot ||
                !BagSystem.IsBag(source.MyItem)) return false;

            if (!BagSystem.IsDirectInventorySlot(source))
            {
                if (notify)
                    ChatHelper.Send(
                        "<color=#EF5350>[Bag]</color> Move the bag into your direct " +
                        "inventory before selling it.");
                return true;
            }
            if (!BagSystem.CanMovePhysicalBagOutsideInventory(source,
                out string reason))
            {
                if (notify) SendBlocked(reason);
                return true;
            }
            if (window?.BuyBack == null || source.MyItem.ItemValue <= 0)
            {
                if (notify)
                    ChatHelper.Send(
                        "<color=#EF5350>[Bag]</color> This merchant cannot buy that bag.");
                return true;
            }

            BagSystem.MutationSnapshot mutation =
                BagSystem.BeginExternalMutation();
            Item bag = source.MyItem;
            int identity = Mathf.Max(1, source.Quantity);
            Item previousBuyBack = window.BuyBack.MyItem;
            int previousBuyBackQuantity = window.BuyBack.Quantity;
            int payment = Mathf.RoundToInt(bag.ItemValue * 0.65f) + 1;
            if (!BagSystem.TryRetireEmptyBagIdentity(identity,
                out string retireError))
            {
                if (notify) SendBlocked(retireError);
                return true;
            }

            window.BuyBack.MyItem = bag;
            // Vendor buy-back is deliberately identity-less. It is an empty
            // bag and receives a fresh collision-free identity when reopened.
            window.BuyBack.Quantity = 1;
            window.BuyBack.UpdateSlotImage();
            source.MyItem = GameData.PlayerInv.Empty;
            source.Quantity = 1;
            source.UpdateSlotImage();
            GameData.PlayerInv.Gold += payment;
            if (GameData.PlayerInv.GoldTXT != null)
                GameData.PlayerInv.GoldTXT.text =
                    GameData.PlayerInv.Gold.ToString();
            GameData.PlayerInv.UpdatePlayerInventory();

            if (!BagSystem.CommitExternalMutation("sell-empty-bag", mutation))
            {
                BagSystem.RollbackExternalMutation(mutation);
                window.BuyBack.MyItem = previousBuyBack ??
                    GameData.PlayerInv.Empty;
                window.BuyBack.Quantity =
                    Mathf.Max(1, previousBuyBackQuantity);
                window.BuyBack.UpdateSlotImage();
                if (notify)
                    ChatHelper.Send(
                        "<color=#EF5350>[Bag]</color> The sale was rolled back " +
                        "because it could not be saved safely.");
                return true;
            }

            if (identity >= 100)
                BagWindow.CloseOne($"bag{identity}");
            if (notify)
                ChatHelper.Send(
                    $"<color=#4CAF50>[Bag]</color> Sold one {bag.ItemName} for " +
                    $"{payment} gold.");
            return true;
        }

        internal static bool CanExternalize(ItemIcon slot)
        {
            if (BagSystem.CanMovePhysicalBagOutsideInventory(slot,
                out string reason)) return true;
            SendBlocked(reason);
            return false;
        }

        internal static bool CanBank(ItemIcon slot)
        {
            if (BagSystem.CanBankPhysicalBag(slot,
                out string reason)) return true;
            SendBlocked(reason);
            return false;
        }

        internal static bool TryCaptureSharedBankState(
            out string path, out string serialized)
        {
            path = BankBagStorage.GetPath();
            serialized = null;
            if (BankBagStorage.TryLoad(out _, out serialized,
                out string error)) return true;
            SendBlocked("shared bank-bag data could not be loaded safely: " +
                error);
            return false;
        }

        internal static bool CanWithdrawBankBag(Item item, int identity,
            string bankBagsBefore, out string reason)
        {
            reason = null;
            if (BagSystem.HasUnresolvedGlobalBankTransaction(out reason))
                return false;
            if (item == null || !BagSystem.IsBag(item))
            {
                reason = "the selected bank item is not a crafted bag";
                return false;
            }
            if (BagSystem.IsCorpseBag(item.ItemName))
            {
                reason = "Corpse Bags cannot be withdrawn through the global bank";
                return false;
            }
            if (identity < 100) return true;
            if (identity < BankBagStorage.GlobalIdentityFloor)
            {
                if (BagSystem.HasMatchingLocalBagRecord(identity, item))
                    return true;
                reason = $"legacy bag{identity} has no contents record on this " +
                    "character; withdraw it on the character that banked it";
                return false;
            }
            if (!BankBagStorage.TryParse(bankBagsBefore,
                out BagSystem.BagSaveEnvelope envelope,
                out string parseError))
            {
                reason = "shared bank-bag data is invalid: " + parseError;
                return false;
            }
            if (!BankBagStorage.TryGet(envelope, identity,
                out BagSystem.BagSaveRecord record))
            {
                reason = $"shared contents for bag{identity} are missing";
                return false;
            }
            if (!string.Equals(record.Name, item.ItemName,
                StringComparison.OrdinalIgnoreCase))
            {
                reason = $"bag{identity} is recorded as {record.Name}, not " +
                    item.ItemName;
                return false;
            }
            return true;
        }

        internal static void SendTradeBlocked()
        {
            ChatHelper.Send(
                "<color=#EF5350>[Bag]</color> Crafted bags cannot be traded. " +
                "Their contents are bound to your character.");
        }

        internal static void SendBankRouteBlocked()
        {
            ChatHelper.Send(
                "<color=#EF5350>[Bag]</color> Withdraw the bag into an empty " +
                "inventory slot before moving, selling, or destroying it.");
        }

        internal static bool FinalizeExternalMove(Item item, int identity,
            ItemIcon target, BagSystem.MutationSnapshot mutation,
            bool bankMove, string bankPath, string bankBefore)
        {
            if (item == null || target == null || target.MyItem != item ||
                target.Quantity != identity) return true;
            if (!BagSystem.TryRetireEmptyBagIdentity(identity,
                out string reason))
            {
                RollbackExternalMove(mutation, bankMove, bankPath, bankBefore);
                SendBlocked(reason);
                return false;
            }

            target.Quantity = 1;
            target.UpdateSlotImage();
            string bankAfter = bankMove ? BuildBankJson(
                GameData.BankUI?.Bank) : null;
            bool committed = BagSystem.CommitExternalMutation(
                bankMove ? "bank-empty-bag" : "destroy-empty-bag",
                mutation, bankMove ? bankPath : null,
                bankMove ? bankBefore : null, bankAfter);
            if (!committed)
            {
                RollbackExternalMove(mutation, bankMove, bankPath, bankBefore);
                ChatHelper.Send(
                    "<color=#EF5350>[Bag]</color> The move was rolled back " +
                    "because it could not be saved safely.");
                return false;
            }
            if (identity >= 100)
                BagWindow.CloseOne($"bag{identity}");
            return true;
        }

        internal static bool FinalizeBankDeposit(Item item, int localIdentity,
            ItemIcon target, BagSystem.MutationSnapshot mutation,
            string bankPath, string bankBefore, string bankBagsPath,
            string bankBagsBefore,
            bool forceFailureAfterBankWriteForAudit = false)
        {
            if (item == null || target == null || target.MyItem != item ||
                target.Quantity != localIdentity) return true;
            if (string.IsNullOrWhiteSpace(item.Id) ||
                !ReferenceEquals(ItemFactory.GetItemById(item.Id), item))
            {
                RollbackExternalMove(mutation, true, bankPath, bankBefore,
                    bankBagsPath, bankBagsBefore);
                SendBlocked(
                    "this bag does not have a reloadable persistent item ID");
                return false;
            }
            if (!BankBagStorage.TryParse(bankBagsBefore,
                out BagSystem.BagSaveEnvelope envelope,
                out string parseError))
            {
                RollbackExternalMove(mutation, true, bankPath, bankBefore,
                    bankBagsPath, bankBagsBefore);
                SendBlocked("shared bank-bag data is invalid: " + parseError);
                return false;
            }
            if (!BagSystem.TryDetachBagForBank(localIdentity, item,
                out BagSystem.BagSaveRecord localRecord,
                out string detachError))
            {
                RollbackExternalMove(mutation, true, bankPath, bankBefore,
                    bankBagsPath, bankBagsBefore);
                SendBlocked(detachError);
                return false;
            }
            if (!BankBagStorage.TryStore(envelope, localRecord,
                out int globalIdentity, out string storeError))
            {
                RollbackExternalMove(mutation, true, bankPath, bankBefore,
                    bankBagsPath, bankBagsBefore);
                SendBlocked(storeError);
                return false;
            }
            if (!BankBagStorage.TrySerialize(envelope,
                out string bankBagsAfter, out string serializeError))
            {
                RollbackExternalMove(mutation, true, bankPath, bankBefore,
                    bankBagsPath, bankBagsBefore);
                SendBlocked(serializeError);
                return false;
            }

            target.Quantity = globalIdentity;
            target.UpdateSlotImage();
            string bankAfter = BuildBankJson(GameData.BankUI?.Bank);
            bool committed = BagSystem.CommitExternalMutation(
                localRecord.Items.Count > 0
                    ? "bank-filled-bag" : "bank-empty-bag",
                mutation, bankPath, bankBefore, bankAfter,
                bankBagsPath, bankBagsBefore, bankBagsAfter,
                forceFailureAfterBankWriteForAudit);
            if (!committed)
            {
                RollbackExternalMove(mutation, true, bankPath, bankBefore,
                    bankBagsPath, bankBagsBefore);
                ChatHelper.Send(
                    "<color=#EF5350>[Bag]</color> The bank deposit was rolled " +
                    "back because every save could not be committed safely.");
                return false;
            }
            BankBagStorage.SyncVisibleSlotsToStored(GameData.BankUI?.Bank);
            if (localIdentity >= 100)
                BagWindow.CloseOne($"bag{localIdentity}");
            if (BagSystem.IsThrallBag(item.ItemName))
                Necromancer.NecromancerPetEquipment
                    .SynchronizeFromCarriedThrallBag(out _);
            return true;
        }

        internal static bool FinalizeBankWithdrawal(Item item,
            int bankIdentity, ItemIcon target,
            BagSystem.MutationSnapshot mutation, string bankPath,
            string bankBefore, string bankBagsPath,
            string bankBagsBefore)
        {
            if (item == null || target == null || target.MyItem != item ||
                target.Quantity != bankIdentity) return true;
            if (!BankBagStorage.TryParse(bankBagsBefore,
                out BagSystem.BagSaveEnvelope envelope,
                out string parseError))
            {
                RollbackExternalMove(mutation, true, bankPath, bankBefore,
                    bankBagsPath, bankBagsBefore);
                SendBlocked("shared bank-bag data is invalid: " + parseError);
                return false;
            }

            int localIdentity = bankIdentity;
            bool legacyLocal = bankIdentity >= 100 &&
                bankIdentity < BankBagStorage.GlobalIdentityFloor;
            if (bankIdentity >= BankBagStorage.GlobalIdentityFloor)
            {
                if (!BankBagStorage.TryTake(envelope, bankIdentity,
                    out BagSystem.BagSaveRecord record,
                    out string takeError))
                {
                    RollbackExternalMove(mutation, true, bankPath, bankBefore,
                        bankBagsPath, bankBagsBefore);
                    SendBlocked(takeError);
                    return false;
                }
                if (!BagSystem.TryAttachBagFromBank(record, item,
                    out localIdentity, out string attachError))
                {
                    RollbackExternalMove(mutation, true, bankPath, bankBefore,
                        bankBagsPath, bankBagsBefore);
                    SendBlocked(attachError);
                    return false;
                }
            }
            else if (legacyLocal)
            {
                if (!BagSystem.HasMatchingLocalBagRecord(bankIdentity, item))
                {
                    RollbackExternalMove(mutation, true, bankPath, bankBefore,
                        bankBagsPath, bankBagsBefore);
                    SendBlocked($"legacy bag{bankIdentity} has no matching " +
                        "contents record on this character");
                    return false;
                }
            }
            else
            {
                var emptyRecord = new BagSystem.BagSaveRecord
                {
                    Name = item.ItemName,
                    Items = new List<BagSystem.BagSlotData>()
                };
                if (!BagSystem.TryAttachBagFromBank(emptyRecord, item,
                    out localIdentity, out string attachError))
                {
                    RollbackExternalMove(mutation, true, bankPath, bankBefore,
                        bankBagsPath, bankBagsBefore);
                    SendBlocked(attachError);
                    return false;
                }
            }

            if (!BankBagStorage.TrySerialize(envelope,
                out string bankBagsAfter, out string serializeError))
            {
                RollbackExternalMove(mutation, true, bankPath, bankBefore,
                    bankBagsPath, bankBagsBefore);
                SendBlocked(serializeError);
                return false;
            }
            target.Quantity = localIdentity;
            target.UpdateSlotImage();
            string bankAfter = BuildBankJson(GameData.BankUI?.Bank);
            bool committed = BagSystem.CommitExternalMutation(
                legacyLocal ? "withdraw-legacy-bag" : "withdraw-bank-bag",
                mutation, bankPath, bankBefore, bankAfter,
                bankBagsPath, bankBagsBefore, bankBagsAfter);
            if (!committed)
            {
                RollbackExternalMove(mutation, true, bankPath, bankBefore,
                    bankBagsPath, bankBagsBefore);
                ChatHelper.Send(
                    "<color=#EF5350>[Bag]</color> The bank withdrawal was rolled " +
                    "back because every save could not be committed safely.");
                return false;
            }
            BankBagStorage.SyncVisibleSlotsToStored(GameData.BankUI?.Bank);
            if (BagSystem.IsThrallBag(item.ItemName))
                Necromancer.NecromancerPetEquipment
                    .SynchronizeFromCarriedThrallBag(out _);
            return true;
        }

        internal static string GetBankPath()
        {
            string root = Application.persistentDataPath;
            string current = Path.Combine(root, "ESSaveData", "GBDATA");
            string legacy = Path.Combine(root, "GBDATA");
            return File.Exists(current) || !File.Exists(legacy)
                ? current : legacy;
        }

        internal static string BuildBankJson(GlobalBank bank,
            ItemIcon overrideSlot = null, Item overrideItem = null,
            int overrideQuantity = 1)
        {
            if (bank?.StoredItems == null || bank.Quantities == null ||
                bank.BankSlots == null || bank.TabNames == null ||
                BankPageField == null) return null;
            int page = Mathf.Max(1, (int)BankPageField.GetValue(bank));
            var items = new List<Item>(bank.StoredItems);
            var quantities = new List<int>(bank.Quantities);
            while (items.Count < 3168)
                items.Add(GameData.PlayerInv.Empty);
            while (quantities.Count < items.Count)
                quantities.Add(1);
            int offset = (page - 1) * 32;
            for (int i = 0; i < Mathf.Min(32, bank.BankSlots.Length); i++)
            {
                bool useOverride = overrideSlot != null &&
                    ReferenceEquals(bank.BankSlots[i], overrideSlot);
                items[offset + i] = useOverride
                    ? overrideItem ?? GameData.PlayerInv.Empty
                    : bank.BankSlots[i].MyItem ?? GameData.PlayerInv.Empty;
                quantities[offset + i] = useOverride
                    ? Mathf.Max(1, overrideQuantity)
                    : Mathf.Max(1, bank.BankSlots[i].Quantity);
            }

            var data = new BankSaveData();
            data.BankData.Clear();
            data.BankCount.Clear();
            data.TabNames.Clear();
            data.GEc = bank.GEc;
            for (int i = 0; i < 3167; i++)
            {
                Item item = items[i] ?? GameData.PlayerInv.Empty;
                data.BankData.Add(item == GameData.PlayerInv.Empty ||
                    string.IsNullOrEmpty(item.Id) ? "" : item.Id);
                data.BankCount.Add(Mathf.Max(1, quantities[i]));
            }
            for (int i = 0; i < 98; i++)
                data.TabNames.Add(i < bank.TabNames.Count
                    ? bank.TabNames[i] : $"Tab {i + 1}");
            return JsonUtility.ToJson(data, true);
        }

        internal static bool RunConservationAudit(out string detail)
        {
            var checks = new List<string>();
            var errors = new List<string>();
            var inventory = GameData.PlayerInv;
            Item bag = ItemFactory.GetItem("Silken Pouch");
            Item sample = ItemFactory.GetItem("Tangled String");
            Item qualityItem = ItemFactory.GetItem("Meadow Silk Robe");
            if (qualityItem == null || qualityItem.Stackable ||
                BagSystem.IsBag(qualityItem))
                foreach (Item candidate in
                    ItemFactory.GetRegisteredItemsForTesting())
                    if (candidate != null && !candidate.Stackable &&
                        !BagSystem.IsBag(candidate))
                    {
                        qualityItem = candidate;
                        break;
                    }
            SimTradeWindow simTrade = GameData.SimTrade;
            VendorWindow vendor = GameData.VendorWindow;
            GlobalBank bank = GameData.BankUI?.Bank;
            if (bank != null)
            {
                try { bank.LoadBank(); }
                catch (Exception ex)
                {
                    detail = "native global bank initialization failed: " +
                        ex.Message;
                    return false;
                }
            }
            ItemIcon scratch = null;
            ItemIcon trash = null;
            if (inventory?.StoredSlots != null)
                foreach (ItemIcon slot in inventory.StoredSlots)
                {
                    if (scratch == null &&
                        BagSystem.IsDirectInventorySlot(slot) &&
                        (slot.MyItem == null || slot.MyItem == inventory.Empty))
                        scratch = slot;
                    if (trash == null && slot?.TrashSlot == true)
                        trash = slot;
                }
            int originalBankPage = bank != null && BankPageField != null
                ? Mathf.Max(1, (int)BankPageField.GetValue(bank)) : 1;
            ItemIcon bankSlot = FindOrDisplayEmptyBankSlot(bank);
            if (inventory == null || bag == null || sample == null ||
                qualityItem == null || qualityItem.Stackable ||
                simTrade?.LootSlot == null || vendor?.BuyBack == null ||
                scratch == null || trash == null || bankSlot == null ||
                GameData.MouseSlot == null || SwapWithField == null)
            {
                detail = "required live fixtures unavailable: " +
                    $"inventory={inventory != null}; bag={bag != null}; " +
                    $"sample={sample != null}; qualityItem={qualityItem != null}; " +
                    $"qualityNonStack={qualityItem != null && !qualityItem.Stackable}; " +
                    $"simTrade={simTrade?.LootSlot != null}; " +
                    $"vendor={vendor?.BuyBack != null}; scratch={scratch != null}; " +
                    $"trash={trash != null}; bank={bank != null}; " +
                    $"emptyBankSlot={bankSlot != null}; " +
                    $"mouseSlot={GameData.MouseSlot != null}; " +
                    $"nativeDragField={SwapWithField != null}";
                if (bank != null && BankPageField != null)
                {
                    BankPageField.SetValue(bank, originalBankPage);
                    bank.DisplayBankPage();
                }
                return false;
            }

            string bankPath = GetBankPath();
            string bankBeforeOuter = BuildBankJson(bank);
            if (string.IsNullOrEmpty(bankBeforeOuter) ||
                !Skills.Tradeskills.IsBankJsonValid(bankBeforeOuter) ||
                !TryCaptureSharedBankState(out string bankBagsPath,
                    out string bankBagsBeforeOuter))
            {
                detail = "validated global-bank transaction fixtures are unavailable";
                return false;
            }
            BagSystem.MutationSnapshot outer =
                BagSystem.BeginExternalMutation();
            string bagsBefore = BagSystem.SerializeBags();
            string inventoryBefore = InventoryFingerprint();
            Item simItemBefore = simTrade.LootSlot.MyItem;
            int simQuantityBefore = simTrade.LootSlot.Quantity;
            bool simWindowBefore = simTrade.WindowParent != null &&
                simTrade.WindowParent.activeSelf;
            Item buyBackBefore = vendor.BuyBack.MyItem;
            int buyBackQuantityBefore = vendor.BuyBack.Quantity;
            ItemIcon activeVendorBefore = GameData.SlotActiveForVendor;
            bool tradingBefore = GameData.Trading;
            Item trashItemBefore = trash.MyItem;
            int trashQuantityBefore = trash.Quantity;
            try
            {
                int identity = BagSystem.NextBagNumber();
                string bagId = $"bag{identity}";
                BagSystem.GetBagContents(bagId).Clear();
                scratch.MyItem = bag;
                scratch.Quantity = identity;
                scratch.UpdateSlotImage();
                if (!BagSystem.TryAddItemToBag(bagId, bag.ItemName,
                    sample, 7, true) ||
                    !BagSystem.TryAddItemToBag(bagId, bag.ItemName,
                        qualityItem, 137, false) ||
                    !BagSystem.TryGetBagRecord(identity,
                        out BagSystem.BagSaveRecord expectedRecord))
                    throw new InvalidOperationException(
                        "could not seed exact filled-bag audit records");

                bool bankAllowed = BagSystem.CanBankPhysicalBag(
                    scratch, out _);
                bool unsafeRouteAllowed =
                    BagSystem.CanMovePhysicalBagOutsideInventory(
                        scratch, out _);
                if (!bankAllowed || unsafeRouteAllowed)
                    errors.Add("filled-bag route admission is incorrect");
                else
                    checks.Add(
                        "filled bags admitted to bank but blocked from sell/trash");

                // Reproduce the native Sim return precondition: a general-slot
                // item with Quantity=100+ has left its source slot and sits in
                // the one-item Sim trade window.
                scratch.MyItem = inventory.Empty;
                scratch.Quantity = 1;
                simTrade.LootSlot.MyItem = bag;
                simTrade.LootSlot.Quantity = identity;
                bool simHandled = TryHandleSimBagReturn(
                    simTrade, "audit", false);
                int returned = CountInventoryIdentity(bag, identity);
                if (!simHandled || returned != 1 ||
                    simTrade.LootSlot.MyItem != inventory.Empty)
                    errors.Add(
                        $"Sim return conservation failed (handled={simHandled}, " +
                        $"returned={returned})");
                else
                    checks.Add("Sim rejection returned exactly one bag");

                ItemIcon returnedSlot = FindInventoryIdentity(bag, identity);
                if (returnedSlot == null)
                    throw new InvalidOperationException(
                        "returned audit bag could not be located");
                BagSystem.MutationSnapshot depositMutation =
                    BagSystem.BeginExternalMutation();
                string depositBankBefore = BuildBankJson(bank);
                if (!TryCaptureSharedBankState(out string depositBagsPath,
                    out string depositBagsBefore))
                    throw new InvalidOperationException(
                        "could not capture shared bag state for deposit");
                returnedSlot.MyItem = inventory.Empty;
                returnedSlot.Quantity = 1;
                returnedSlot.UpdateSlotImage();
                bankSlot.MyItem = bag;
                bankSlot.Quantity = identity;
                bankSlot.UpdateSlotImage();
                bool deposited = FinalizeBankDeposit(
                    bag, identity, bankSlot, depositMutation,
                    bankPath, depositBankBefore, depositBagsPath,
                    depositBagsBefore);
                int globalIdentity = bankSlot.Quantity;
                bool localDetached =
                    !BagSystem.TryGetBagRecord(identity, out _);
                if (!deposited ||
                    globalIdentity < BankBagStorage.GlobalIdentityFloor ||
                    !localDetached ||
                    CountBankIdentity(bank, bag, globalIdentity) != 1)
                    errors.Add(
                        "filled-bag deposit did not move one physical bag and " +
                        "its character record into shared storage");
                else
                    checks.Add(
                        "filled-bag deposit moved one physical bag atomically");
                if (CountStoredBankIdentity(
                    bank, bag, globalIdentity) != 1)
                    errors.Add(
                        "committed deposit did not synchronize the bank backing page");
                else
                    checks.Add(
                        "committed deposit synchronized visible and backing bank state");

                bank.LoadBank();
                bankSlot = FindBankIdentity(bank, bag, globalIdentity);
                bool persisted = bankSlot != null;
                string persistenceFailure = bankSlot == null
                    ? "physical bag missing after reload" : null;
                BagSystem.BagSaveEnvelope persistedEnvelope = null;
                string persistedJson = null;
                if (persisted && !BankBagStorage.TryLoad(
                        out persistedEnvelope, out persistedJson,
                        out persistenceFailure))
                    persisted = false;
                if (persisted &&
                    (!BankBagStorage.TryGet(persistedEnvelope,
                        globalIdentity,
                        out BagSystem.BagSaveRecord persistedRecord) ||
                     !BankBagStorage.RecordsEqual(expectedRecord,
                         persistedRecord) ||
                     !BankBagStorage.IsSerializedDataValid(persistedJson)))
                {
                    persisted = false;
                    persistenceFailure = "shared contents changed after reload";
                }
                if (persisted &&
                    !BankBagStorage.AuditLoadedBank(bank,
                        out persistenceFailure))
                    persisted = false;
                if (!persisted)
                    errors.Add(
                        "restart-style bank reload changed or orphaned bag " +
                        $"contents ({persistenceFailure ?? "integrity mismatch"})");
                else
                    checks.Add(
                        "bank reload preserved stack counts and raw gear quality");

                if (bankSlot == null)
                    throw new InvalidOperationException(
                        "persisted audit bag could not be found in the bank");
                BagSystem.MutationSnapshot withdrawalMutation =
                    BagSystem.BeginExternalMutation();
                string withdrawalBankBefore = BuildBankJson(bank);
                if (!TryCaptureSharedBankState(
                    out string withdrawalBagsPath,
                    out string withdrawalBagsBefore))
                    throw new InvalidOperationException(
                        "could not capture shared bag state for withdrawal");
                bankSlot.MyItem = inventory.Empty;
                bankSlot.Quantity = 1;
                bankSlot.UpdateSlotImage();
                scratch.MyItem = bag;
                scratch.Quantity = globalIdentity;
                scratch.UpdateSlotImage();
                bool withdrawn = FinalizeBankWithdrawal(
                    bag, globalIdentity, scratch, withdrawalMutation,
                    bankPath, withdrawalBankBefore, withdrawalBagsPath,
                    withdrawalBagsBefore);
                int withdrawnIdentity = scratch.Quantity;
                bool exactWithdrawal =
                    withdrawn && withdrawnIdentity >= 100 &&
                    withdrawnIdentity != identity &&
                    withdrawnIdentity <
                        BankBagStorage.GlobalIdentityFloor &&
                    BagSystem.TryGetBagRecord(withdrawnIdentity,
                        out BagSystem.BagSaveRecord withdrawnRecord) &&
                    BankBagStorage.RecordsEqual(expectedRecord,
                        withdrawnRecord) &&
                    BankBagStorage.TryLoad(
                        out BagSystem.BagSaveEnvelope afterWithdrawal,
                        out _, out _) &&
                    !BankBagStorage.TryGet(afterWithdrawal,
                        globalIdentity, out _) &&
                    CountInventoryIdentity(bag, withdrawnIdentity) == 1;
                if (!exactWithdrawal)
                    errors.Add(
                        "withdrawal did not create one fresh character-local " +
                        "bag with exact contents");
                else
                    checks.Add(
                        "cross-character-style withdrawal used a fresh local identity");
                if (CountStoredBankIdentity(
                    bank, bag, globalIdentity) != 0)
                    errors.Add(
                        "committed withdrawal left the bag in the bank backing page");
                else
                    checks.Add(
                        "committed withdrawal cleared visible and backing bank state");

                bank.LoadBank();
                if (!BankBagStorage.AuditLoadedBank(bank,
                    out string postWithdrawalAudit))
                    errors.Add(
                        "post-withdrawal shared-bank audit failed: " +
                        postWithdrawalAudit);
                else
                    checks.Add(
                        "withdrawal removed the shared record without an orphan");

                bankSlot = FindEmptyBankSlot(bank);
                if (bankSlot == null)
                    throw new InvalidOperationException(
                        "no empty bank slot remained for rollback QA");
                string rollbackBagsBefore = BagSystem.SerializeBags();
                string rollbackInventoryBefore = InventoryFingerprint();
                string rollbackBankBefore = BuildBankJson(bank);
                if (!TryCaptureSharedBankState(
                    out string rollbackBagsPath,
                    out string rollbackBankBagsBefore))
                    throw new InvalidOperationException(
                        "could not capture shared state for rollback QA");
                BagSystem.MutationSnapshot rollbackMutation =
                    BagSystem.BeginExternalMutation();
                scratch.MyItem = inventory.Empty;
                scratch.Quantity = 1;
                scratch.UpdateSlotImage();
                bankSlot.MyItem = bag;
                bankSlot.Quantity = withdrawnIdentity;
                bankSlot.UpdateSlotImage();
                bool injectedDeposit = FinalizeBankDeposit(
                    bag, withdrawnIdentity, bankSlot,
                    rollbackMutation, bankPath, rollbackBankBefore,
                    rollbackBagsPath, rollbackBankBagsBefore, true);
                bool rollbackExact =
                    !injectedDeposit &&
                    scratch.MyItem == bag &&
                    scratch.Quantity == withdrawnIdentity &&
                    bankSlot.MyItem == inventory.Empty &&
                    string.Equals(BagSystem.SerializeBags(),
                        rollbackBagsBefore, StringComparison.Ordinal) &&
                    string.Equals(InventoryFingerprint(),
                        rollbackInventoryBefore, StringComparison.Ordinal) &&
                    File.Exists(bankPath) &&
                    string.Equals(File.ReadAllText(bankPath),
                        rollbackBankBefore, StringComparison.Ordinal) &&
                    File.Exists(rollbackBagsPath) &&
                    string.Equals(File.ReadAllText(rollbackBagsPath),
                        rollbackBankBagsBefore, StringComparison.Ordinal) &&
                    !BagSystem.HasPendingTransaction;
                if (!rollbackExact)
                    errors.Add(
                        "forced partial-write rollback did not restore all " +
                        "four primary transaction states exactly");
                else
                    checks.Add(
                        "forced partial write rolled back inventory, character " +
                        "bags, bank, and shared bags exactly");

                // Exercise the same live ItemIcon.InteractItemSlot path used by
                // a real click-drag. This includes the Harmony capture, native
                // cursor move, bank drop, and reverse withdrawal hooks.
                bankSlot = FindEmptyBankSlot(bank);
                if (bankSlot == null || GameData.MouseSlot == null ||
                    SwapWithField == null)
                    throw new InvalidOperationException(
                        "native drag fixtures became unavailable");
                BagSystem.ClearCursorAfterExternalRollback();
                scratch.InteractItemSlot();
                SwapWithField.SetValue(GameData.MouseSlot, bankSlot);
                GameData.MouseSlot.InteractItemSlot();
                SwapWithField.SetValue(GameData.MouseSlot, null);
                int nativeGlobalIdentity = bankSlot.Quantity;
                bool nativeDeposit =
                    scratch.MyItem == inventory.Empty &&
                    bankSlot.MyItem == bag &&
                    nativeGlobalIdentity >=
                        BankBagStorage.GlobalIdentityFloor &&
                    BankBagStorage.TryLoad(
                        out BagSystem.BagSaveEnvelope nativeDepositEnvelope,
                        out _, out _) &&
                    BankBagStorage.TryGet(nativeDepositEnvelope,
                        nativeGlobalIdentity,
                        out BagSystem.BagSaveRecord nativeDepositRecord) &&
                    BankBagStorage.RecordsEqual(expectedRecord,
                        nativeDepositRecord) &&
                    CountStoredBankIdentity(
                        bank, bag, nativeGlobalIdentity) == 1 &&
                    GameData.ItemOnCursor == null &&
                    !GameData.MouseSlot.dragging;
                if (!nativeDeposit)
                    errors.Add(
                        "live native drag did not deposit exactly one filled bag");
                else
                    checks.Add(
                        "live native drag deposited one filled bag through the UI hook");

                bank.LoadBank();
                bankSlot = FindBankIdentity(
                    bank, bag, nativeGlobalIdentity);
                if (bankSlot == null)
                    throw new InvalidOperationException(
                        "native-drag bag did not survive bank reload");
                bankSlot.InteractItemSlot();
                SwapWithField.SetValue(GameData.MouseSlot, scratch);
                GameData.MouseSlot.InteractItemSlot();
                SwapWithField.SetValue(GameData.MouseSlot, null);
                withdrawnIdentity = scratch.Quantity;
                bool nativeWithdrawal =
                    scratch.MyItem == bag &&
                    withdrawnIdentity >= 100 &&
                    withdrawnIdentity <
                        BankBagStorage.GlobalIdentityFloor &&
                    BagSystem.TryGetBagRecord(withdrawnIdentity,
                        out BagSystem.BagSaveRecord nativeWithdrawnRecord) &&
                    BankBagStorage.RecordsEqual(expectedRecord,
                        nativeWithdrawnRecord) &&
                    BankBagStorage.TryLoad(
                        out BagSystem.BagSaveEnvelope nativeWithdrawEnvelope,
                        out _, out _) &&
                    !BankBagStorage.TryGet(nativeWithdrawEnvelope,
                        nativeGlobalIdentity, out _) &&
                    CountStoredBankIdentity(
                        bank, bag, nativeGlobalIdentity) == 0 &&
                    GameData.ItemOnCursor == null &&
                    !GameData.MouseSlot.dragging;
                if (!nativeWithdrawal)
                    errors.Add(
                        "live native drag did not withdraw one exact filled bag");
                else
                    checks.Add(
                        "live native drag withdrew one exact filled bag through the UI hook");

                int filledGoldBefore = inventory.Gold;
                Item filledBuyBackBefore = vendor.BuyBack.MyItem;
                int filledBuyBackQuantityBefore =
                    vendor.BuyBack.Quantity;
                bool filledSaleHandled = TryHandleVendorBagSale(
                    vendor, scratch, false);
                if (!filledSaleHandled || inventory.Gold != filledGoldBefore ||
                    scratch.MyItem != bag ||
                    vendor.BuyBack.MyItem != filledBuyBackBefore ||
                    vendor.BuyBack.Quantity != filledBuyBackQuantityBefore)
                    errors.Add("filled-bag sale guard changed transaction state");
                else
                    checks.Add("filled bags remain protected from merchant sale");

                BagSystem.GetBagContents(
                    $"bag{withdrawnIdentity}").Clear();
                int goldBefore = inventory.Gold;
                int expectedPayment =
                    Mathf.RoundToInt(bag.ItemValue * 0.65f) + 1;
                bool saleHandled = TryHandleVendorBagSale(
                    vendor, scratch, false);
                int remaining = CountInventoryIdentity(
                    bag, withdrawnIdentity);
                if (!saleHandled || remaining != 0 ||
                    vendor.BuyBack.MyItem != bag ||
                    vendor.BuyBack.Quantity != 1 ||
                    inventory.Gold - goldBefore != expectedPayment)
                    errors.Add(
                        "vendor sale did not remove/pay for exactly one empty bag");
                else
                    checks.Add("vendor sale conserved one bag and one payment");

                vendor.BuyBack.MyItem = buyBackBefore ??
                    inventory.Empty;
                vendor.BuyBack.Quantity =
                    Mathf.Max(1, buyBackQuantityBefore);
                vendor.BuyBack.UpdateSlotImage();

                int destroyIdentity = BagSystem.NextBagNumber();
                string destroyBagId = $"bag{destroyIdentity}";
                BagSystem.GetBagContents(destroyBagId).Clear();
                scratch.MyItem = bag;
                scratch.Quantity = destroyIdentity;
                BagSystem.MutationSnapshot destroyMutation =
                    BagSystem.BeginExternalMutation();
                scratch.MyItem = inventory.Empty;
                scratch.Quantity = 1;
                trash.MyItem = bag;
                trash.Quantity = destroyIdentity;
                bool destroyed = FinalizeExternalMove(
                    bag, destroyIdentity, trash, destroyMutation, false,
                    null, null);
                if (!destroyed || trash.MyItem != bag ||
                    trash.Quantity != 1 ||
                    BagSystem.SerializeBags().Contains(
                        $"\"{destroyBagId}\""))
                    errors.Add("trash transfer did not normalize one empty bag");
                else
                    checks.Add("destroy route normalized identity without duplication");

                string bankJson = BuildBankJson(GameData.BankUI?.Bank);
                if (string.IsNullOrEmpty(bankJson) ||
                    !Skills.Tradeskills.IsBankJsonValid(bankJson))
                    errors.Add("global-bank transaction image is invalid");
                else
                    checks.Add("global-bank before/after image validates");
            }
            catch (Exception ex)
            {
                errors.Add("audit exception: " + ex.Message);
            }
            finally
            {
                simTrade.LootSlot.MyItem = simItemBefore ??
                    inventory.Empty;
                simTrade.LootSlot.Quantity =
                    Mathf.Max(1, simQuantityBefore);
                simTrade.LootSlot.UpdateSlotImage();
                if (simTrade.WindowParent != null)
                    simTrade.WindowParent.SetActive(simWindowBefore);
                vendor.BuyBack.MyItem = buyBackBefore ??
                    inventory.Empty;
                vendor.BuyBack.Quantity =
                    Mathf.Max(1, buyBackQuantityBefore);
                vendor.BuyBack.UpdateSlotImage();
                trash.MyItem = trashItemBefore ?? inventory.Empty;
                trash.Quantity = Mathf.Max(1, trashQuantityBefore);
                trash.UpdateSlotImage();
                GameData.SlotActiveForVendor = activeVendorBefore;
                GameData.Trading = tradingBefore;
                BagSystem.RollbackExternalMutation(outer,
                    bankPath, bankBeforeOuter,
                    bankBagsPath, bankBagsBeforeOuter);
                BagSystem.ClearCursorAfterExternalRollback();
                try { bank.LoadBank(); }
                catch (Exception ex)
                {
                    errors.Add("final bank reload failed: " + ex.Message);
                }
                if (BankPageField != null)
                {
                    try
                    {
                        BankPageField.SetValue(bank, originalBankPage);
                        bank.DisplayBankPage();
                    }
                    catch (Exception ex)
                    {
                        errors.Add("bank page restore failed: " + ex.Message);
                    }
                }
            }

            if (!string.Equals(BagSystem.SerializeBags(), bagsBefore,
                StringComparison.Ordinal))
                errors.Add("bag sidecar did not restore after audit");
            if (!string.Equals(InventoryFingerprint(), inventoryBefore,
                StringComparison.Ordinal))
                errors.Add("native inventory did not restore after audit");
            if (!File.Exists(bankPath) ||
                !string.Equals(File.ReadAllText(bankPath),
                    bankBeforeOuter, StringComparison.Ordinal))
                errors.Add("global bank did not restore after audit");
            if (!File.Exists(bankBagsPath) ||
                !string.Equals(File.ReadAllText(bankBagsPath),
                    bankBagsBeforeOuter, StringComparison.Ordinal))
                errors.Add("shared bank-bag sidecar did not restore after audit");
            detail = $"checks={checks.Count}: {string.Join("; ", checks)}" +
                (errors.Count == 0 ? "" :
                    $" | errors={errors.Count}: {string.Join("; ", errors)}");
            return errors.Count == 0;
        }

        private static void RollbackExternalMove(
            BagSystem.MutationSnapshot mutation, bool bankMove,
            string bankPath, string bankBefore,
            string bankBagsPath = null, string bankBagsBefore = null)
        {
            BagSystem.RollbackExternalMutation(mutation,
                bankMove ? bankPath : null,
                bankMove ? bankBefore : null,
                bankMove ? bankBagsPath : null,
                bankMove ? bankBagsBefore : null);
            BagSystem.ClearCursorAfterExternalRollback();
            if (bankMove && GameData.BankUI?.Bank != null)
            {
                try { GameData.BankUI.Bank.LoadBank(); }
                catch (Exception ex)
                {
                    SkillsPlugin.Log.LogError(
                        $"Bag bank UI rollback failed: {ex.Message}");
                }
            }
        }

        private static ItemIcon FindEmptyInventorySlot()
        {
            var inventory = GameData.PlayerInv;
            if (inventory?.StoredSlots == null) return null;
            foreach (ItemIcon slot in inventory.StoredSlots)
                if (BagSystem.IsDirectInventorySlot(slot) &&
                    (slot.MyItem == null || slot.MyItem == inventory.Empty))
                    return slot;
            return null;
        }

        private static ItemIcon FindEmptyBankSlot(GlobalBank bank)
        {
            if (bank?.BankSlots == null) return null;
            foreach (ItemIcon slot in bank.BankSlots)
                if (slot != null && IsEmptyBankItem(slot.MyItem))
                    return slot;
            return null;
        }

        private static ItemIcon FindOrDisplayEmptyBankSlot(GlobalBank bank)
        {
            ItemIcon current = FindEmptyBankSlot(bank);
            if (current != null || bank?.StoredItems == null ||
                BankPageField == null) return current;
            // GlobalBank exposes 98 pages of 32 visible slots. Its serialized
            // list contains 31 unused tail entries that must never be selected
            // as a display page.
            int limit = Mathf.Min(98 * 32, bank.StoredItems.Count);
            for (int index = 0; index < limit; index++)
            {
                Item item = bank.StoredItems[index];
                if (!IsEmptyBankItem(item)) continue;
                int page = index / 32 + 1;
                BankPageField.SetValue(bank, page);
                bank.DisplayBankPage();
                return FindEmptyBankSlot(bank);
            }
            return null;
        }

        private static bool IsEmptyBankItem(Item item)
        {
            Item empty = GameData.PlayerInv?.Empty;
            return item == null || item == empty ||
                string.IsNullOrEmpty(item.Id) ||
                (empty != null && string.Equals(item.Id, empty.Id,
                    StringComparison.OrdinalIgnoreCase));
        }

        private static ItemIcon FindBankIdentity(GlobalBank bank,
            Item item, int identity)
        {
            if (bank?.BankSlots == null) return null;
            foreach (ItemIcon slot in bank.BankSlots)
                if (slot != null && slot.MyItem == item &&
                    slot.Quantity == identity)
                    return slot;
            return null;
        }

        private static int CountBankIdentity(GlobalBank bank,
            Item item, int identity)
        {
            int count = 0;
            if (bank?.BankSlots == null) return count;
            foreach (ItemIcon slot in bank.BankSlots)
                if (slot != null && slot.MyItem == item &&
                    slot.Quantity == identity)
                    count++;
            return count;
        }

        private static int CountStoredBankIdentity(GlobalBank bank,
            Item item, int identity)
        {
            int count = 0;
            if (bank?.StoredItems == null || bank.Quantities == null)
                return count;
            int limit = Math.Min(bank.StoredItems.Count,
                bank.Quantities.Count);
            for (int i = 0; i < limit; i++)
                if (bank.StoredItems[i] == item &&
                    bank.Quantities[i] == identity)
                    count++;
            return count;
        }

        private static ItemIcon FindInventoryIdentity(Item item, int identity)
        {
            var slots = GameData.PlayerInv?.StoredSlots;
            if (slots == null) return null;
            foreach (ItemIcon slot in slots)
                if (BagSystem.IsDirectInventorySlot(slot) &&
                    slot.MyItem == item && slot.Quantity == identity)
                    return slot;
            return null;
        }

        private static int CountInventoryIdentity(Item item, int identity)
        {
            int count = 0;
            var slots = GameData.PlayerInv?.StoredSlots;
            if (slots == null) return count;
            foreach (ItemIcon slot in slots)
                if (BagSystem.IsDirectInventorySlot(slot) &&
                    slot.MyItem == item && slot.Quantity == identity)
                    count++;
            return count;
        }

        private static string InventoryFingerprint()
        {
            var inventory = GameData.PlayerInv;
            if (inventory?.StoredSlots == null) return "unavailable";
            var value = new StringBuilder();
            value.Append("G:").Append(inventory.Gold).Append(';');
            foreach (ItemIcon slot in inventory.StoredSlots)
            {
                Item item = slot?.MyItem;
                string id = item?.Id ?? item?.ItemName ?? "<null>";
                value.Append(id.Length).Append(':').Append(id).Append(':')
                    .Append(slot == null ? 1 : Mathf.Max(1, slot.Quantity))
                    .Append(';');
            }
            return value.ToString();
        }

        internal static void SendBlocked(string reason)
        {
            ChatHelper.Send(
                "<color=#EF5350>[Bag]</color> " +
                (string.IsNullOrWhiteSpace(reason)
                    ? "That bag cannot be moved safely." : reason) + ".");
        }
    }

    [HarmonyPatch(typeof(SimTradeWindow),
        nameof(SimTradeWindow.CompleteTrade))]
    internal static class Patch_SimTradeBagComplete
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(SimTradeWindow __instance)
        {
            return !BagTransferSafety.TryHandleSimBagReturn(
                __instance, "complete");
        }
    }

    [HarmonyPatch(typeof(SimTradeWindow),
        nameof(SimTradeWindow.CancelTrade))]
    internal static class Patch_SimTradeBagCancel
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(SimTradeWindow __instance)
        {
            return !BagTransferSafety.TryHandleSimBagReturn(
                __instance, "cancel");
        }
    }

    [HarmonyPatch(typeof(VendorWindow),
        nameof(VendorWindow.Transaction))]
    internal static class Patch_VendorBagSale
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(VendorWindow __instance)
        {
            return !BagTransferSafety.TryHandleVendorBagSale(__instance);
        }
    }
}
