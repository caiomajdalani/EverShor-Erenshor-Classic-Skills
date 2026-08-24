using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ErenshorSkills.Features
{
    /// <summary>
    /// Opt-in, crash-recoverable corpse runs.  The native character save is the
    /// commit authority; this journal records both sides of each mutation so an
    /// interrupted death or recovery can be classified without guessing.
    /// </summary>
    internal static class CorpseRunSystem
    {
        private const int SaveFormatVersion = 2;
        private const int PayloadMagic = 0x43525031; // CRP1
        private const int MaxSlotsPerArea = 256;
        private const int MaxItemTextBytes = 2048;
        private const int MaxPayloadBytes = 4 * 1024 * 1024;
        private const int MaxBagSnapshotBytes = 16 * 1024 * 1024;
        private const int MaxVisualSnapshotBytes = 8 * 1024 * 1024;
        private const int MaxJournalChars = 96 * 1024 * 1024;
        private const int VisualSnapshotMagic = 0x43565331; // CVS1
        private const int VisualSnapshotVersion = 1;
        private const int MaxVisualNodes = 8192;
        private const int MaxVisualRenderers = 4096;
        private const int MaxVisualPathDepth = 64;
        private const float ActivationDistance = 4.25f;
        private const string StatePrepared = "Prepared";
        private const string StateStored = "Stored";
        private const string StateRecovering = "Recovering";

        private static readonly FieldInfo MouseHomeSlotField =
            typeof(ItemIcon).GetField("MouseHomeSlot",
                BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly string[] VisualColorProperties =
        {
            "_Color_Primary",
            "_Color_Secondary",
            "_Color_Metal_Primary",
            "_Color_Leather_Primary",
            "_Color_Metal_Dark",
            "_Color_Metal_Secondary",
            "_Color_Skin",
            "_Color_Leather_Secondary",
            "_Color_Hair",
            "_Color_Stubble"
        };

        private static readonly int[] VisualColorPropertyIds =
            CreateVisualColorPropertyIds();

        [Serializable]
        private sealed class CorpseSaveRecord
        {
            public int Version = SaveFormatVersion;
            public string TransactionId = "";
            public string Character = "";
            public string State = StatePrepared;
            public string Zone = "";
            public float PositionX;
            public float PositionY;
            public float PositionZ;
            public float RotationX;
            public float RotationY;
            public float RotationZ;
            public float RotationW = 1f;
            public string CreatedUtc = "";
            public string PayloadBase64 = "";
            public string BagSnapshotBase64 = "";
            public string VisualSnapshotBase64 = "";
            public string OriginalFingerprint = "";
            public string StrippedFingerprint = "";
            public string RecoveryBeforeFingerprint = "";
            public string RecoveryAfterFingerprint = "";
            public string RecoveryBeforePayloadBase64 = "";
            public string RecoveryAfterPayloadBase64 = "";
            public string RecoveryBeforeBagsBase64 = "";
            public string RecoveryAfterBagsBase64 = "";
            public string RecoveryBeforeBagFingerprint = "";
            public string RecoveryAfterBagFingerprint = "";
            public string RecoveryBagId = "";
        }

        private sealed class SlotRecord
        {
            internal string ItemId = "";
            internal string ItemName = "";
            internal int Quantity = 1;
            internal bool IsEmpty => string.IsNullOrEmpty(ItemId) &&
                string.IsNullOrEmpty(ItemName);

            internal SlotRecord Clone()
            {
                return new SlotRecord
                {
                    ItemId = ItemId ?? "",
                    ItemName = ItemName ?? "",
                    Quantity = Quantity
                };
            }
        }

        private sealed class InventoryRecord
        {
            internal readonly List<SlotRecord> Stored = new List<SlotRecord>();
            internal readonly List<SlotRecord> Equipment = new List<SlotRecord>();
            internal readonly List<SlotRecord> Transmog = new List<SlotRecord>();
            internal SlotRecord Aura = EmptySlot();
            internal SlotRecord Charm = EmptySlot();
            internal int Gold;

            internal InventoryRecord Clone()
            {
                var clone = new InventoryRecord { Gold = Gold };
                foreach (SlotRecord slot in Stored) clone.Stored.Add(slot.Clone());
                foreach (SlotRecord slot in Equipment) clone.Equipment.Add(slot.Clone());
                foreach (SlotRecord slot in Transmog) clone.Transmog.Add(slot.Clone());
                clone.Aura = Aura.Clone();
                clone.Charm = Charm.Clone();
                return clone;
            }
        }

        private sealed class RuntimeSnapshot
        {
            internal InventoryRecord Record;
            internal readonly List<Item> StoredItems = new List<Item>();
            internal readonly List<Item> EquipmentItems = new List<Item>();
            internal readonly List<Item> TransmogItems = new List<Item>();
            internal Item AuraItem;
            internal Item CharmItem;

            internal RuntimeSnapshot Clone()
            {
                var clone = new RuntimeSnapshot
                {
                    Record = Record.Clone(),
                    AuraItem = AuraItem,
                    CharmItem = CharmItem
                };
                clone.StoredItems.AddRange(StoredItems);
                clone.EquipmentItems.AddRange(EquipmentItems);
                clone.TransmogItems.AddRange(TransmogItems);
                return clone;
            }
        }

        private enum SlotArea
        {
            Stored,
            Equipment,
            Transmog,
            Aura,
            Charm
        }

        private sealed class Placement
        {
            internal SlotArea SourceArea;
            internal int SourceIndex;
            internal SlotArea TargetArea;
            internal int TargetIndex;
        }

        private enum ReconcileResult
        {
            Original,
            Stripped,
            Recovered,
            Partial,
            Ambiguous
        }

        private static CorpseSaveRecord _record;
        private static GameObject _marker;
        private static TextMeshPro _prompt;
        private static bool _loadLocked;
        private static bool _deathCaptureInProgress;
        private static bool _recoveryInProgress;
        private static int _nativeSaveSequence;
        private static bool _lastNativeSaveSucceeded;
        private static float _nextMarkerAttempt;
        private static bool _markerUsesCharacterVisual;
        private static int _markerVisibleRendererCount;
        private static int _markerMissingVisualPaths;
        private static readonly List<Mesh> MarkerOwnedMeshes =
            new List<Mesh>();

        internal static bool HasOutstandingCorpse => _record != null;
        internal static bool IsRecoveryLocked => _loadLocked;

        internal static void ObserveNativeSave(bool succeeded)
        {
            _lastNativeSaveSucceeded = succeeded;
            if (_nativeSaveSequence < int.MaxValue) _nativeSaveSequence++;
        }

        internal static void OnPlayerDeath(Character player)
        {
            if (_deathCaptureInProgress || _recoveryInProgress || player == null ||
                player.isNPC || player.Alive ||
                !ReferenceEquals(player, GameData.PlayerControl?.Myself) ||
                SkillsPlugin.CfgEnableCorpseRun?.Value != true) return;

            _deathCaptureInProgress = true;
            try
            {
                if (!SkillsSaveManager.HasLoaded || GameData.PlayerInv == null)
                {
                    Warn("Corpse Run skipped this death because character data was not safely loaded. Your inventory was left untouched.");
                    return;
                }
                if (_loadLocked)
                {
                    Warn("Corpse Run is safety-locked because an earlier journal could not be reconciled. Your inventory was left untouched.");
                    return;
                }
                if (_record != null)
                {
                    Warn($"Your existing corpse remains in {_record.Zone}. This death did not remove any additional belongings.");
                    return;
                }
                if (Items.BagSystem.HasPendingTransaction)
                {
                    Warn("A bag move was still being saved, so this death did not remove any belongings.");
                    return;
                }

                Items.BagWindow.CloseAll();
                if (!TryCaptureLive(out RuntimeSnapshot original, out string captureError))
                {
                    Warn("Corpse Run could not safely capture this inventory: " + captureError + ". Nothing was removed.");
                    return;
                }
                if (!HasPossessions(original.Record))
                {
                    Info("You had no inventory, equipment, cosmetics, aura, charm, or gold to leave behind.");
                    return;
                }

                string bagSnapshot = Items.BagSystem.SerializeBags();
                if (!Items.BagSystem.IsSerializedDataValid(bagSnapshot))
                {
                    Warn("Bag data did not validate, so this death did not remove any belongings.");
                    return;
                }
                List<int> bagIds = CollectBagNumbers(original.Record);
                if (!Items.BagSystem.ValidateCorpseBagSnapshot(
                    bagSnapshot, bagIds, out string bagDetail))
                {
                    Warn("Corpse Run could not verify crafted bags: " + bagDetail + ". Nothing was removed.");
                    return;
                }

                byte[] payload = EncodeInventory(original.Record);
                InventoryRecord strippedRecord = CreateStrippedRecord(original.Record);
                string originalFingerprint = Fingerprint(payload);
                string strippedFingerprint = Fingerprint(
                    EncodeInventory(strippedRecord));
                if (string.Equals(originalFingerprint, strippedFingerprint,
                    StringComparison.Ordinal))
                {
                    Warn("Corpse Run found no recoverable belongings. Nothing was removed.");
                    return;
                }

                string visualSnapshot = "";
                if (!TryCaptureCharacterVisualSnapshot(out visualSnapshot,
                    out string visualError))
                {
                    SkillsPlugin.Log.LogWarning(
                        "Character corpse appearance could not be captured; " +
                        "the safe marker fallback will be used: " + visualError);
                }

                Vector3 position = player.transform.position;
                Quaternion rotation = HorizontalRotation(
                    player.transform.rotation);
                var prepared = new CorpseSaveRecord
                {
                    TransactionId = Guid.NewGuid().ToString("N"),
                    Character = SkillsSaveManager.CharName,
                    State = StatePrepared,
                    Zone = SceneManager.GetActiveScene().name,
                    PositionX = position.x,
                    PositionY = position.y,
                    PositionZ = position.z,
                    RotationX = rotation.x,
                    RotationY = rotation.y,
                    RotationZ = rotation.z,
                    RotationW = rotation.w,
                    CreatedUtc = DateTime.UtcNow.ToString("o"),
                    PayloadBase64 = Convert.ToBase64String(payload),
                    BagSnapshotBase64 = Convert.ToBase64String(
                        Encoding.UTF8.GetBytes(bagSnapshot)),
                    VisualSnapshotBase64 = visualSnapshot,
                    OriginalFingerprint = originalFingerprint,
                    StrippedFingerprint = strippedFingerprint
                };
                if (!TryWriteRecord(prepared, out string journalError))
                {
                    Warn("The corpse journal could not be saved, so nothing was removed: " + journalError);
                    return;
                }

                try
                {
                    ApplyStrippedInventory(strippedRecord);
                    if (!TryPersistAll(out string saveError))
                        throw new IOException(saveError);

                    CorpseSaveRecord stored = CloneRecord(prepared);
                    stored.State = StateStored;
                    if (!TryWriteRecord(stored, out string stateError))
                    {
                        // The durable Prepared record plus the stripped native
                        // inventory is sufficient for deterministic login repair.
                        _record = stored;
                        SkillsPlugin.Log.LogError(
                            "Corpse state commit will be reconciled on next load: " + stateError);
                    }
                    SpawnMarkerIfNeeded(force: true);
                    Info($"You left {CountPossessionSlots(original.Record)} item stack(s)" +
                        (original.Record.Gold > 0 ? $" and {original.Record.Gold} gold" : "") +
                        $" on your corpse in {prepared.Zone}. Return and press {InputManager.Loot} to recover everything.");
                }
                catch (Exception ex)
                {
                    SkillsPlugin.Log.LogError("Corpse death transaction failed: " + ex);
                    string restoreError = null;
                    string rollbackSaveError = null;
                    bool restored = TryApplyRuntime(original, out restoreError) &&
                        TryPersistAll(out rollbackSaveError);
                    if (restored)
                    {
                        DeleteRecordFiles(out _);
                        _record = null;
                        Warn("Corpse Run could not commit, so the entire inventory was restored immediately. " + ex.Message);
                    }
                    else
                    {
                        _loadLocked = true;
                        Warn("Corpse Run encountered a save failure. The recovery journal was retained and no further corpse changes will be made this session. " +
                            (restoreError ?? rollbackSaveError ?? ex.Message));
                    }
                }
            }
            catch (Exception ex)
            {
                _loadLocked = true;
                SkillsPlugin.Log.LogError("Corpse Run death handler failed safely: " + ex);
                Warn("Corpse Run stopped before making further changes. See the log for details.");
            }
            finally
            {
                _deathCaptureInProgress = false;
            }
        }

        internal static void OnCharacterLoaded()
        {
            DestroyMarker();
            _record = null;
            _loadLocked = false;
            _deathCaptureInProgress = false;
            _recoveryInProgress = false;

            if (!TryLoadRecord(out CorpseSaveRecord loaded, out string error))
            {
                _loadLocked = true;
                Warn("Corpse data and its backup could not be validated. The file was retained and corpse transactions are disabled: " + error);
                return;
            }
            if (loaded == null) return;
            if (!string.Equals(loaded.Character, SkillsSaveManager.CharName,
                StringComparison.Ordinal))
            {
                _loadLocked = true;
                Warn("A corpse journal belonged to a different character and was retained without changes.");
                return;
            }

            _record = loaded;
            if (!TryCaptureLive(out RuntimeSnapshot current,
                out string captureError))
            {
                _loadLocked = true;
                Warn("Corpse reconciliation could not inspect the current inventory: " + captureError);
                return;
            }
            string currentFingerprint = Fingerprint(
                EncodeInventory(current.Record));

            if (string.Equals(loaded.State, StatePrepared,
                StringComparison.Ordinal))
            {
                ReconcileResult result = Classify(loaded, currentFingerprint);
                if (result == ReconcileResult.Original)
                {
                    string repairError = null;
                    string deleteError = null;
                    if (TryPersistAll(out repairError) &&
                        DeleteRecordFiles(out deleteError))
                    {
                        _record = null;
                        SkillsPlugin.Log.LogWarning(
                            "Rolled back an interrupted corpse creation; native inventory was unchanged.");
                        return;
                    }
                    _loadLocked = true;
                    Warn("An interrupted corpse creation was rolled back, but its cleanup could not be saved: " +
                        (repairError ?? deleteError));
                    return;
                }
                if (result == ReconcileResult.Stripped)
                {
                    CorpseSaveRecord stored = CloneRecord(loaded);
                    stored.State = StateStored;
                    if (!TryWriteRecord(stored, out string stateError))
                    {
                        _loadLocked = true;
                        Warn("The corpse exists, but its interrupted transaction could not be finalized: " + stateError);
                        return;
                    }
                    RepairAddonSidecars();
                    loaded = stored;
                }
                else
                {
                    _loadLocked = true;
                    Warn("An interrupted corpse creation did not match either safe inventory state. Nothing will be added or removed automatically.");
                    return;
                }
            }
            else if (string.Equals(loaded.State, StateRecovering,
                StringComparison.Ordinal))
            {
                string currentBags = Items.BagSystem.SerializeBags();
                string currentBagFingerprint = Fingerprint(
                    Encoding.UTF8.GetBytes(currentBags));
                ReconcileResult result = ClassifyRecovery(loaded,
                    currentFingerprint, currentBagFingerprint);
                if (result == ReconcileResult.Recovered)
                {
                    string repairError = null;
                    string deleteError = null;
                    if (TryPersistAll(out repairError) &&
                        DeleteRecordFiles(out deleteError))
                    {
                        _record = null;
                        Info("An interrupted corpse recovery had already completed; its journal was safely cleared.");
                        return;
                    }
                    _loadLocked = true;
                    Warn("Recovered items are present, but the completed journal could not be cleared: " +
                        (repairError ?? deleteError));
                    return;
                }
                if (result == ReconcileResult.Original)
                {
                    CorpseSaveRecord stored = CloneRecord(loaded);
                    stored.State = StateStored;
                    ClearRecoveryFields(stored);
                    if (!TryWriteRecord(stored, out string stateError))
                    {
                        _loadLocked = true;
                        Warn("The interrupted recovery was rolled back, but its journal could not be updated: " + stateError);
                        return;
                    }
                    RepairAddonSidecars();
                    loaded = stored;
                }
                else if (result == ReconcileResult.Partial)
                {
                    if (!TryRollbackInterruptedRecovery(loaded, current,
                        currentBags,
                        out string rollbackError))
                    {
                        _loadLocked = true;
                        Warn("An interrupted corpse recovery was only partly saved and could not be rolled back safely: " +
                            rollbackError + ". No automatic item changes will be made.");
                        return;
                    }
                    CorpseSaveRecord stored = CloneRecord(loaded);
                    stored.State = StateStored;
                    ClearRecoveryFields(stored);
                    if (!TryWriteRecord(stored, out string stateError))
                    {
                        _loadLocked = true;
                        Warn("The interrupted recovery was rolled back, but its journal could not be finalized: " + stateError);
                        return;
                    }
                    loaded = stored;
                    Info("An interrupted Corpse Bag creation was rolled back without losing or duplicating belongings. Your corpse remains recoverable.");
                }
                else
                {
                    _loadLocked = true;
                    Warn("An interrupted corpse recovery did not match either safe inventory state. Nothing will be added or removed automatically.");
                    return;
                }
            }

            _record = loaded;
            SpawnMarkerIfNeeded(force: true);
            Info($"Your corpse is waiting in {loaded.Zone}. Use /corpserun summon only if its location is unreachable.");
        }

        internal static void OnSceneLoaded(string sceneName)
        {
            DestroyMarker();
            _nextMarkerAttempt = Time.unscaledTime + 0.5f;
            if (string.Equals(sceneName, "Menu", StringComparison.Ordinal) ||
                string.Equals(sceneName, "LoadScene", StringComparison.Ordinal))
            {
                _record = null;
                _loadLocked = false;
            }
        }

        internal static void Tick()
        {
            if (_record == null || _loadLocked ||
                !string.Equals(_record.State, StateStored,
                    StringComparison.Ordinal)) return;

            string scene = SceneManager.GetActiveScene().name;
            if (!string.Equals(scene, _record.Zone, StringComparison.Ordinal))
            {
                DestroyMarker();
                return;
            }
            SpawnMarkerIfNeeded(force: false);
            if (_marker == null || GameData.PlayerControl == null) return;

            float distance = Vector3.Distance(GameData.PlayerControl.transform.position,
                _marker.transform.position);
            bool nearby = distance <= ActivationDistance;
            if (_prompt != null)
            {
                if (_prompt.gameObject.activeSelf != nearby)
                    _prompt.gameObject.SetActive(nearby);
                if (nearby && Camera.main != null)
                    _prompt.transform.rotation = Camera.main.transform.rotation;
            }
            if (!nearby || _recoveryInProgress || GameData.PlayerTyping ||
                SkillsPlugin.IsEscapeMenuOpen || AddonInputState.PointerOverAddon ||
                GameData.PlayerControl.Myself == null ||
                !GameData.PlayerControl.Myself.Alive) return;

            if (Input.GetKeyDown(InputManager.Loot)) TryRecover();
        }

        internal static bool ShouldSuppressNativeLoot(Character candidate)
        {
            try
            {
                if (_record == null || _marker == null || _loadLocked ||
                    !string.Equals(_record.State, StateStored,
                        StringComparison.Ordinal) || GameData.PlayerControl == null)
                    return false;
                float corpseDistance = Vector3.Distance(
                    GameData.PlayerControl.transform.position,
                    _marker.transform.position);
                if (corpseDistance > ActivationDistance) return false;
                if (candidate == null) return true;
                float nativeDistance = Vector3.Distance(
                    GameData.PlayerControl.transform.position,
                    candidate.transform.position);
                return corpseDistance <= nativeDistance + 0.25f;
            }
            catch { return false; }
        }

        internal static void HandleChatCommand(string raw)
        {
            string args = (raw ?? "").Trim();
            if (args.StartsWith("/corpserun", StringComparison.OrdinalIgnoreCase))
                args = args.Substring(10).Trim();
            string verb = args.ToLowerInvariant();
            if (string.IsNullOrEmpty(verb) || verb == "status")
            {
                ShowStatus();
                return;
            }
            if (verb == "on" || verb == "off")
            {
                bool enabled = verb == "on";
                try
                {
                    SkillsPlugin.CfgEnableCorpseRun.Value = enabled;
                    SkillsPlugin.Instance?.Config.Save();
                    Info("Corpse Run is now " + (enabled ? "ON" : "OFF") + "." +
                        (!enabled && _record != null
                            ? " Your existing corpse remains recoverable."
                            : ""));
                }
                catch (Exception ex)
                {
                    Warn("The setting could not be saved: " + ex.Message);
                }
                return;
            }
            if (verb == "summon" || verb == "rescue")
            {
                SummonCorpse();
                return;
            }
            ChatHelper.Send("<color=#FFD54F>[Corpse Run]</color> /corpserun &lt;on|off|status|summon&gt;");
        }

        internal static void TriggerDeathForTesting(bool allowOutstandingCorpse = false)
        {
            if (SkillsPlugin.CfgEnableTestMode?.Value != true)
            {
                Warn("Controlled death is available only in test mode.");
                return;
            }
            if (SkillsPlugin.CfgEnableCorpseRun?.Value != true)
            {
                Warn("Enable Corpse Run first with /corpserun on.");
                return;
            }
            if (_record != null && !allowOutstandingCorpse)
            {
                Warn("Recover the existing corpse first, or use /cstest corpse redie to test the second-death safeguard.");
                return;
            }
            Character player = GameData.PlayerControl?.Myself;
            if (!SkillsSaveManager.HasLoaded || player == null || !player.Alive)
            {
                Warn("A living, fully loaded character is required.");
                return;
            }
            Info(allowOutstandingCorpse
                ? "Controlled second death triggered with an outstanding corpse. The production safeguard must leave current belongings untouched."
                : "Controlled corpse-run death triggered. This uses the game's real death path.");
            player.MyStats.CurrentHP = 0;
        }

        internal static bool RunSafetySelfTest(out string detail)
        {
            detail = null;
            bool passed = false;
            string bagsBefore = null;
            try
            {
                var corpse = new InventoryRecord { Gold = 77 };
                corpse.Stored.Add(TestSlot("item-a", "Item A", 3));
                corpse.Stored.Add(EmptySlot());
                corpse.Stored.Add(EmptySlot());
                corpse.Stored.Add(EmptySlot());
                corpse.Equipment.Add(TestSlot("q4", "Mod Gear +4", 4));
                corpse.Equipment.Add(TestSlot("q8", "Mod Gear +8", 8));
                corpse.Equipment.Add(TestSlot("q15", "Smithed Gear +5", 15));
                corpse.Equipment.Add(TestSlot("qwide", "Wide Mod Gear",
                    987654321));
                corpse.Aura = TestSlot("aura", "Aura", 1);

                byte[] encoded = EncodeInventory(corpse);
                InventoryRecord decoded = DecodeInventory(encoded);
                bool roundTrip = Fingerprint(encoded) == Fingerprint(
                    EncodeInventory(decoded));
                bool exactGearState = decoded.Equipment.Count == 4 &&
                    decoded.Equipment[0].Quantity == 4 &&
                    decoded.Equipment[1].Quantity == 8 &&
                    decoded.Equipment[2].Quantity == 15 &&
                    decoded.Equipment[3].Quantity == 987654321;
                bool strippedDiffers = Fingerprint(encoded) != Fingerprint(
                    EncodeInventory(CreateStrippedRecord(corpse)));

                var current = CreateStrippedRecord(corpse);
                current.Gold = 5;
                current.Stored[0] = TestSlot("new", "New Item", 1);
                var safe = new List<bool> { true, true, true, true };
                bool oneSlotGate = FindEmptyRecoverySlotIndex(current, safe) == 1 &&
                    current.Stored[0].ItemId == "new";

                var full = current.Clone();
                for (int i = 0; i < full.Stored.Count; i++)
                    full.Stored[i] = TestSlot("full-" + i, "Full " + i, 1);
                string fullBefore = Fingerprint(EncodeInventory(full));
                bool correctlyBlocked = FindEmptyRecoverySlotIndex(full,
                    safe) < 0 && fullBefore == Fingerprint(EncodeInventory(full));

                bagsBefore = Items.BagSystem.SerializeBags();
                int recoveryBagNumber = Items.BagSystem.NextBagNumber();
                string recoveryBagId = $"bag{recoveryBagNumber}";
                var recovered = new List<Items.BagSystem.BagSlotData>
                {
                    new Items.BagSystem.BagSlotData { ItemId = "q4",
                        ItemName = "Mod Gear +4", Quantity = 4,
                        IsStackable = false },
                    new Items.BagSystem.BagSlotData { ItemId = "q8",
                        ItemName = "Mod Gear +8", Quantity = 8,
                        IsStackable = false },
                    new Items.BagSystem.BagSlotData { ItemId = "q15",
                        ItemName = "Smithed Gear +5", Quantity = 15,
                        IsStackable = false },
                    new Items.BagSystem.BagSlotData { ItemId = "qwide",
                        ItemName = "Wide Mod Gear", Quantity = 987654321,
                        IsStackable = false }
                };
                bool seeded = Items.BagSystem.TrySeedCorpseBag(
                    recoveryBagNumber, recovered, out string seedError);
                string seededJson = Items.BagSystem.SerializeBags();
                string containerError = null;
                bool containerValidated = seeded;
                if (containerValidated)
                    containerValidated = Items.BagSystem
                        .ValidateRecoveryContainerSnapshot(
                            seededJson, recoveryBagId, recovered.Count,
                            out containerError);
                if (containerValidated)
                    containerValidated = Items.BagSystem.DeserializeBags(
                        seededJson);
                List<Items.BagSystem.BagSlotData> loadedContents =
                    Items.BagSystem.GetBagContents(recoveryBagId);
                bool bagRoundTrip = containerValidated &&
                    loadedContents.Count == recovered.Count;
                for (int i = 0; bagRoundTrip && i < recovered.Count; i++)
                    bagRoundTrip = loadedContents[i].Quantity ==
                        recovered[i].Quantity;
                bool withdrawalOnly = Items.BagSystem.IsWithdrawalOnly(
                    recoveryBagId, Items.BagSystem.CorpseBagName) &&
                    Items.BagSystem.GetVisibleSlots(recoveryBagId,
                        Items.BagSystem.CorpseBagName) == recovered.Count;

                var syntheticRecord = new CorpseSaveRecord
                {
                    OriginalFingerprint = "original",
                    StrippedFingerprint = "stripped",
                    RecoveryBeforeFingerprint = "before",
                    RecoveryAfterFingerprint = "after",
                    RecoveryBeforeBagFingerprint = "bags-before",
                    RecoveryAfterBagFingerprint = "bags-after"
                };
                bool reconciliation =
                    Classify(syntheticRecord, "original") == ReconcileResult.Original &&
                    Classify(syntheticRecord, "stripped") == ReconcileResult.Stripped &&
                    Classify(syntheticRecord, "other") == ReconcileResult.Ambiguous &&
                    ClassifyRecovery(syntheticRecord, "before", "bags-before") ==
                        ReconcileResult.Original &&
                    ClassifyRecovery(syntheticRecord, "after", "bags-after") ==
                        ReconcileResult.Recovered &&
                    ClassifyRecovery(syntheticRecord, "after", "bags-before") ==
                        ReconcileResult.Partial &&
                    ClassifyRecovery(syntheticRecord, "before", "bags-after") ==
                        ReconcileResult.Partial &&
                    ClassifyRecovery(syntheticRecord, "other", "other") ==
                        ReconcileResult.Ambiguous;

                bool visualSnapshot =
                    TryCaptureCharacterVisualSnapshot(
                        out string encodedVisual, out string visualError) &&
                    TryValidateVisualSnapshot(Convert.FromBase64String(
                        encodedVisual), out visualError);
                Quaternion flattened = HorizontalRotation(
                    Quaternion.Euler(72f, 137f, 31f));
                Vector3 flattenedForward = flattened * Vector3.forward;
                Vector3 flattenedUp = flattened * Vector3.up;
                bool horizontalMarker =
                    Mathf.Abs(flattenedForward.y) < 0.0001f &&
                    Vector3.Dot(flattenedUp, Vector3.up) > 0.9999f;

                passed = roundTrip && exactGearState && strippedDiffers &&
                    oneSlotGate && correctlyBlocked && bagRoundTrip &&
                    withdrawalOnly && reconciliation && visualSnapshot &&
                    horizontalMarker;
                detail = $"serialization={(roundTrip ? "pass" : "fail")}; " +
                    $"mod-gear-state={(exactGearState && bagRoundTrip ? "exact" : seedError ?? containerError ?? "fail")}; " +
                    $"corpse-bag={(withdrawalOnly ? "withdrawal-only" : "fail")}; " +
                    $"one-slot-gate={(oneSlotGate ? "pass" : "fail")}; " +
                    $"full-inventory={(correctlyBlocked ? "blocked-without-mutation" : "fail")}; " +
                    $"crash-reconciliation={(reconciliation ? "pass" : "fail")}; " +
                    $"character-visual={(visualSnapshot ? "round-trip" : visualError ?? "fail")}; " +
                    $"corpse-orientation={(horizontalMarker ? "horizontal" : "fail")}";
            }
            catch (Exception ex)
            {
                detail = ex.Message;
                passed = false;
            }
            finally
            {
                if (bagsBefore != null &&
                    (!Items.BagSystem.DeserializeBags(bagsBefore) ||
                     !string.Equals(Items.BagSystem.SerializeBags(), bagsBefore,
                        StringComparison.Ordinal)))
                {
                    passed = false;
                    detail = (detail ?? "") +
                        "; original bag state did not restore exactly";
                }
            }
            return passed;
        }

        internal static string GetStatusForTesting()
        {
            return _record == null ? "none" :
                $"{_record.State}:{_record.Zone}:{_record.TransactionId}:" +
                $"visual={(_markerUsesCharacterVisual ? "character" : "fallback")}:" +
                $"renderers={_markerVisibleRendererCount}:" +
                $"missing={_markerMissingVisualPaths}";
        }

        internal static void Shutdown()
        {
            DestroyMarker();
            _record = null;
            _loadLocked = false;
            _deathCaptureInProgress = false;
            _recoveryInProgress = false;
        }

        private static void TryRecover()
        {
            if (_record == null || _loadLocked || _recoveryInProgress ||
                !string.Equals(_record.State, StateStored,
                    StringComparison.Ordinal)) return;
            _recoveryInProgress = true;
            RuntimeSnapshot before = null;
            string beforeBags = null;
            try
            {
                if (Items.BagSystem.HasPendingTransaction)
                    throw new InvalidOperationException(
                        "another bag transaction is still being saved");
                if (!TryCaptureLive(out before, out string captureError))
                    throw new InvalidOperationException(captureError);
                InventoryRecord corpseRecord = DecodeInventory(
                    Convert.FromBase64String(_record.PayloadBase64));
                string bagSnapshot = Encoding.UTF8.GetString(
                    Convert.FromBase64String(_record.BagSnapshotBase64));
                List<int> bagIds = CollectBagNumbers(corpseRecord);
                if (!Items.BagSystem.ValidateCorpseBagSnapshot(
                    bagSnapshot, bagIds, out string bagDetail))
                    throw new InvalidOperationException(
                        "crafted bag validation failed: " + bagDetail);

                RuntimeSnapshot corpseRuntime = ResolveRuntime(corpseRecord);
                List<Items.BagSystem.BagSlotData> recoveredItems =
                    BuildCorpseBagContents(corpseRecord, corpseRuntime);
                RuntimeSnapshot after = before.Clone();
                if (corpseRecord.Gold > int.MaxValue - after.Record.Gold)
                    throw new InvalidOperationException(
                        "recovering the corpse gold would overflow the native gold value");
                after.Record.Gold += corpseRecord.Gold;

                beforeBags = Items.BagSystem.SerializeBags();
                if (!Items.BagSystem.IsSerializedDataValid(beforeBags))
                    throw new InvalidOperationException(
                        "current bag data failed validation");
                string recoveryBagId = "";
                if (recoveredItems.Count > 0)
                {
                    int targetIndex = FindEmptyRecoverySlotIndex(before);
                    if (targetIndex < 0)
                        throw new InvalidOperationException(
                            "one empty direct inventory slot is required for the Corpse Bag");
                    Item corpseBagItem = Items.ItemFactory.GetItem(
                        Items.BagSystem.CorpseBagName);
                    if (corpseBagItem == null ||
                        string.IsNullOrWhiteSpace(corpseBagItem.Id))
                        throw new InvalidOperationException(
                            "the Corpse Bag item is unavailable");

                    int bagNumber = Items.BagSystem.NextBagNumber();
                    recoveryBagId = $"bag{bagNumber}";
                    if (!Items.BagSystem.TrySeedCorpseBag(bagNumber,
                        recoveredItems, out string seedError))
                        throw new InvalidOperationException(seedError);

                    after.Record.Stored[targetIndex] = new SlotRecord
                    {
                        ItemId = corpseBagItem.Id,
                        ItemName = corpseBagItem.ItemName,
                        Quantity = bagNumber
                    };
                    after.StoredItems[targetIndex] = corpseBagItem;
                }

                string afterBags = Items.BagSystem.SerializeBags();
                if (!Items.BagSystem.IsSerializedDataValid(afterBags))
                    throw new InvalidOperationException(
                        "the Corpse Bag state failed validation");
                // Keep the live state unchanged until both complete outcomes
                // have been written to the durable recovery journal.
                if (!Items.BagSystem.TryRestoreSerializedState(beforeBags,
                    out string restorePlanningError))
                    throw new InvalidOperationException(restorePlanningError);

                byte[] beforePayload = EncodeInventory(before.Record);
                byte[] afterPayload = EncodeInventory(after.Record);
                string beforeFingerprint = Fingerprint(
                    beforePayload);
                string afterFingerprint = Fingerprint(
                    afterPayload);
                CorpseSaveRecord recovering = CloneRecord(_record);
                recovering.State = StateRecovering;
                recovering.RecoveryBeforeFingerprint = beforeFingerprint;
                recovering.RecoveryAfterFingerprint = afterFingerprint;
                recovering.RecoveryBeforePayloadBase64 =
                    Convert.ToBase64String(beforePayload);
                recovering.RecoveryAfterPayloadBase64 =
                    Convert.ToBase64String(afterPayload);
                recovering.RecoveryBeforeBagsBase64 = EncodeText(beforeBags);
                recovering.RecoveryAfterBagsBase64 = EncodeText(afterBags);
                recovering.RecoveryBeforeBagFingerprint = Fingerprint(
                    Encoding.UTF8.GetBytes(beforeBags));
                recovering.RecoveryAfterBagFingerprint = Fingerprint(
                    Encoding.UTF8.GetBytes(afterBags));
                recovering.RecoveryBagId = recoveryBagId;
                if (!TryWriteRecord(recovering, out string journalError))
                    throw new IOException(
                        "recovery journal could not be saved: " + journalError);

                if (!Items.BagSystem.TryRestoreSerializedState(afterBags,
                    out string applyBagError))
                    throw new InvalidOperationException(applyBagError);
                if (!TryApplyRuntime(after, out string applyError))
                    throw new InvalidOperationException(applyError);
                if (!TryPersistAll(out string saveError))
                    throw new IOException(saveError);

                bool deleted = DeleteRecordFiles(out string deleteError);
                _record = deleted ? null : recovering;
                DestroyMarker();
                if (!deleted)
                    SkillsPlugin.Log.LogWarning(
                        "Recovered inventory; completed journal cleanup deferred: " + deleteError);
                if (recoveredItems.Count > 0)
                    Info($"Recovered {recoveredItems.Count} item stack(s) into a withdrawal-only Corpse Bag" +
                        (corpseRecord.Gold > 0 ? $" and returned {corpseRecord.Gold} gold" : "") +
                        ". The bag disappears after its final item is removed.");
                else
                    Info($"Recovered {corpseRecord.Gold} gold. The corpse held no items, so no Corpse Bag was needed.");
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogError("Corpse recovery stopped: " + ex);
                string rollbackError = null;
                string rollbackSaveError = null;
                bool bagsRestored = beforeBags == null ||
                    Items.BagSystem.TryRestoreSerializedState(beforeBags,
                        out rollbackError);
                bool rollbackApplied = bagsRestored && before != null &&
                    TryApplyRuntime(before, out rollbackError);
                bool rollbackSaved = rollbackApplied &&
                    TryPersistAll(out rollbackSaveError);
                if (rollbackSaved && _record != null)
                {
                    CorpseSaveRecord stored = CloneRecord(_record);
                    stored.State = StateStored;
                    ClearRecoveryFields(stored);
                    if (!TryWriteRecord(stored, out string stateError))
                    {
                        _loadLocked = true;
                        SkillsPlugin.Log.LogError(
                            "Corpse rollback journal update failed: " + stateError);
                    }
                }
                else if (before != null)
                {
                    _loadLocked = true;
                    SkillsPlugin.Log.LogError("Corpse rollback save failed: " +
                        (rollbackError ?? rollbackSaveError ?? "unknown error"));
                }
                Warn("Recovery made no committed inventory changes: " + ex.Message +
                    ". Keep one direct inventory slot free, or use /corpserun summon if the marker is inaccessible.");
            }
            finally
            {
                _recoveryInProgress = false;
            }
        }

        private static void SummonCorpse()
        {
            if (_record == null)
            {
                Warn("You do not have an outstanding corpse.");
                return;
            }
            if (_loadLocked || !string.Equals(_record.State, StateStored,
                StringComparison.Ordinal) || GameData.PlayerControl == null ||
                !SkillsSaveManager.HasLoaded)
            {
                Warn("That corpse cannot be safely relocated in its current transaction state.");
                return;
            }
            Vector3 position = GameData.PlayerControl.transform.position +
                GameData.PlayerControl.transform.forward * 1.5f;
            Quaternion rotation = HorizontalRotation(
                GameData.PlayerControl.transform.rotation);
            CorpseSaveRecord moved = CloneRecord(_record);
            moved.Zone = SceneManager.GetActiveScene().name;
            moved.PositionX = position.x;
            moved.PositionY = position.y;
            moved.PositionZ = position.z;
            moved.RotationX = rotation.x;
            moved.RotationY = rotation.y;
            moved.RotationZ = rotation.z;
            moved.RotationW = rotation.w;
            if (!TryWriteRecord(moved, out string error))
            {
                Warn("The corpse location was not changed because the journal could not be saved: " + error);
                return;
            }
            DestroyMarker();
            SpawnMarkerIfNeeded(force: true);
            Info("The corpse marker was safely relocated nearby. Press " +
                InputManager.Loot + " at the marker to recover your belongings.");
        }

        private static void ShowStatus()
        {
            string enabled = SkillsPlugin.CfgEnableCorpseRun?.Value == true
                ? "ON" : "OFF";
            if (_loadLocked)
            {
                ChatHelper.Send($"<color=#EF5350>[Corpse Run]</color> {enabled}; safety-locked. The retained journal requires review.");
                return;
            }
            if (_record == null)
            {
                ChatHelper.Send($"<color=#FFD54F>[Corpse Run]</color> {enabled}; no outstanding corpse.");
                return;
            }
            try
            {
                InventoryRecord inventory = DecodeInventory(
                    Convert.FromBase64String(_record.PayloadBase64));
                ChatHelper.Send($"<color=#FFD54F>[Corpse Run]</color> {enabled}; " +
                    $"corpse={_record.State}, zone={_record.Zone}, " +
                    $"item stacks={CountPossessionSlots(inventory)}, gold={inventory.Gold}, " +
                    $"visual={(_markerUsesCharacterVisual ? "saved character" : "safe fallback")} " +
                    $"({_markerVisibleRendererCount} visible meshes).");
            }
            catch
            {
                ChatHelper.Send($"<color=#EF5350>[Corpse Run]</color> {enabled}; corpse metadata could not be displayed safely.");
            }
        }

        private static bool TryCaptureLive(out RuntimeSnapshot snapshot,
            out string error)
        {
            snapshot = null;
            error = null;
            try
            {
                Inventory inventory = GameData.PlayerInv;
                if (inventory?.StoredSlots == null ||
                    inventory.EquipmentSlots == null ||
                    inventory.TransmogSlots == null ||
                    inventory.AuraSlot == null || inventory.CharmSlot == null ||
                    inventory.Empty == null)
                    throw new InvalidOperationException(
                        "one or more native inventory areas are unavailable");
                if (inventory.StoredSlots.Count > MaxSlotsPerArea ||
                    inventory.EquipmentSlots.Count > MaxSlotsPerArea ||
                    inventory.TransmogSlots.Count > MaxSlotsPerArea)
                    throw new InvalidOperationException(
                        "native inventory slot counts are outside the supported range");
                if (!NormalizeCursor(out string cursorError))
                    throw new InvalidOperationException(cursorError);

                var runtime = new RuntimeSnapshot
                {
                    Record = new InventoryRecord { Gold = inventory.Gold }
                };
                if (runtime.Record.Gold < 0)
                    throw new InvalidDataException("gold cannot be negative");
                CaptureArea(inventory.StoredSlots, inventory,
                    runtime.Record.Stored, runtime.StoredItems);
                CaptureArea(inventory.EquipmentSlots, inventory,
                    runtime.Record.Equipment, runtime.EquipmentItems);
                CaptureArea(inventory.TransmogSlots, inventory,
                    runtime.Record.Transmog, runtime.TransmogItems);
                runtime.Record.Aura = CaptureSlot(inventory.AuraSlot,
                    inventory, out Item aura);
                runtime.Record.Charm = CaptureSlot(inventory.CharmSlot,
                    inventory, out Item charm);
                runtime.AuraItem = aura;
                runtime.CharmItem = charm;
                snapshot = runtime;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                snapshot = null;
                return false;
            }
        }

        private static void CaptureArea(IList<ItemIcon> slots,
            Inventory inventory, List<SlotRecord> records, List<Item> items)
        {
            foreach (ItemIcon slot in slots)
            {
                if (slot == null)
                    throw new InvalidDataException("native inventory contains a null slot");
                records.Add(CaptureSlot(slot, inventory, out Item item));
                items.Add(item);
            }
        }

        private static SlotRecord CaptureSlot(ItemIcon slot,
            Inventory inventory, out Item runtimeItem)
        {
            runtimeItem = slot?.MyItem;
            if (runtimeItem == null || ReferenceEquals(runtimeItem, inventory.Empty))
            {
                runtimeItem = inventory.Empty;
                return EmptySlot();
            }
            string id = runtimeItem.Id ?? "";
            string name = runtimeItem.ItemName ?? "";
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name) ||
                Encoding.UTF8.GetByteCount(id) > MaxItemTextBytes ||
                Encoding.UTF8.GetByteCount(name) > MaxItemTextBytes)
                throw new InvalidDataException(
                    "an item has no safe persistent ID or name");
            int quantity = slot.Quantity;
            if (quantity <= 0 || (runtimeItem.Stackable && quantity > 1000000))
                throw new InvalidDataException(
                    $"{name} has an invalid quantity or quality ({quantity})");
            if (Items.BagSystem.IsBag(runtimeItem) && quantity < 100)
                throw new InvalidDataException(
                    $"{name} does not yet have a persistent bag identity");
            return new SlotRecord
            {
                ItemId = id,
                ItemName = name,
                Quantity = quantity
            };
        }

        private static bool NormalizeCursor(out string error)
        {
            error = null;
            try
            {
                Inventory inventory = GameData.PlayerInv;
                ItemIcon mouse = GameData.MouseSlot;
                if (mouse == null || mouse.MyItem == null ||
                    ReferenceEquals(mouse.MyItem, inventory.Empty)) return true;

                ItemIcon home = null;
                if (GameData.ItemOnCursor != null &&
                    !ReferenceEquals(GameData.ItemOnCursor, mouse))
                    home = GameData.ItemOnCursor;
                if (home == null)
                    home = MouseHomeSlotField?.GetValue(mouse) as ItemIcon;
                if (!IsPersistedSlot(home, inventory) || !IsEmpty(home, inventory))
                {
                    home = null;
                    foreach (ItemIcon candidate in inventory.StoredSlots)
                    {
                        if (Items.BagSystem.IsDirectInventorySlot(candidate) &&
                            IsEmpty(candidate, inventory))
                        {
                            home = candidate;
                            break;
                        }
                    }
                }
                if (home == null)
                {
                    error = "an item is on the cursor and no safe slot is available";
                    return false;
                }

                home.MyItem = mouse.MyItem;
                home.Quantity = Math.Max(1, mouse.Quantity);
                mouse.MyItem = inventory.Empty;
                mouse.Quantity = 1;
                mouse.dragging = false;
                MouseHomeSlotField?.SetValue(mouse, null);
                GameData.ItemOnCursor = null;
                GameData.HighlightedItem = null;
                home.UpdateSlotImage();
                mouse.UpdateSlotImage();
                inventory.UpdatePlayerInventory();
                return true;
            }
            catch (Exception ex)
            {
                error = "the cursor item could not be normalized: " + ex.Message;
                return false;
            }
        }

        private static bool IsPersistedSlot(ItemIcon slot, Inventory inventory)
        {
            if (slot == null || inventory == null) return false;
            return inventory.StoredSlots.Contains(slot) ||
                inventory.EquipmentSlots.Contains(slot) ||
                inventory.TransmogSlots.Contains(slot) ||
                ReferenceEquals(slot, inventory.AuraSlot) ||
                ReferenceEquals(slot, inventory.CharmSlot);
        }

        private static bool IsEmpty(ItemIcon slot, Inventory inventory)
        {
            return slot != null && (slot.MyItem == null ||
                ReferenceEquals(slot.MyItem, inventory.Empty));
        }

        private static RuntimeSnapshot ResolveRuntime(InventoryRecord record)
        {
            var runtime = new RuntimeSnapshot { Record = record.Clone() };
            foreach (SlotRecord slot in record.Stored)
                runtime.StoredItems.Add(ResolveItem(slot));
            foreach (SlotRecord slot in record.Equipment)
                runtime.EquipmentItems.Add(ResolveItem(slot));
            foreach (SlotRecord slot in record.Transmog)
                runtime.TransmogItems.Add(ResolveItem(slot));
            runtime.AuraItem = ResolveItem(record.Aura);
            runtime.CharmItem = ResolveItem(record.Charm);
            return runtime;
        }

        private static int FindEmptyRecoverySlotIndex(RuntimeSnapshot snapshot)
        {
            Inventory inventory = GameData.PlayerInv;
            if (snapshot?.Record?.Stored == null ||
                inventory?.StoredSlots == null ||
                snapshot.Record.Stored.Count != inventory.StoredSlots.Count)
                return -1;
            var safe = new List<bool>(inventory.StoredSlots.Count);
            foreach (ItemIcon slot in inventory.StoredSlots)
                safe.Add(Items.BagSystem.IsDirectInventorySlot(slot));
            return FindEmptyRecoverySlotIndex(snapshot.Record, safe);
        }

        private static int FindEmptyRecoverySlotIndex(InventoryRecord record,
            IList<bool> safeSlots)
        {
            if (record?.Stored == null || safeSlots == null ||
                record.Stored.Count != safeSlots.Count) return -1;
            for (int i = 0; i < record.Stored.Count; i++)
                if (safeSlots[i] && record.Stored[i].IsEmpty) return i;
            return -1;
        }

        private static List<Items.BagSystem.BagSlotData>
            BuildCorpseBagContents(InventoryRecord record,
                RuntimeSnapshot runtime)
        {
            if (record == null || runtime == null)
                throw new ArgumentNullException(nameof(record));
            var contents = new List<Items.BagSystem.BagSlotData>();
            AppendCorpseBagArea(record.Stored, runtime.StoredItems, contents);
            AppendCorpseBagArea(record.Equipment, runtime.EquipmentItems, contents);
            AppendCorpseBagArea(record.Transmog, runtime.TransmogItems, contents);
            AppendCorpseBagItem(record.Aura, runtime.AuraItem, contents);
            AppendCorpseBagItem(record.Charm, runtime.CharmItem, contents);
            if (contents.Count != CountPossessionSlots(record))
                throw new InvalidDataException(
                    "the Corpse Bag did not capture every occupied slot");
            return contents;
        }

        private static void AppendCorpseBagArea(IList<SlotRecord> records,
            IList<Item> items,
            List<Items.BagSystem.BagSlotData> destination)
        {
            if (records == null || items == null || records.Count != items.Count)
                throw new InvalidDataException(
                    "a corpse inventory area changed shape during recovery");
            for (int i = 0; i < records.Count; i++)
                AppendCorpseBagItem(records[i], items[i], destination);
        }

        private static void AppendCorpseBagItem(SlotRecord record, Item item,
            List<Items.BagSystem.BagSlotData> destination)
        {
            if (record == null || record.IsEmpty) return;
            if (item == null || item == GameData.PlayerInv?.Empty ||
                !string.Equals(item.Id ?? "", record.ItemId ?? "",
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(item.ItemName ?? "", record.ItemName ?? "",
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    $"saved item '{record.ItemName}' no longer resolves to its exact native item");
            destination.Add(new Items.BagSystem.BagSlotData
            {
                ItemId = record.ItemId,
                ItemName = record.ItemName,
                // Quantity is the complete native instance state for stacks and
                // gear quality.  Preserve it byte-for-byte as a signed Int32;
                // positive mod-defined values are never normalized.
                Quantity = record.Quantity,
                IsStackable = item.Stackable
            });
        }

        private static Item ResolveItem(SlotRecord slot)
        {
            Inventory inventory = GameData.PlayerInv;
            if (slot == null || slot.IsEmpty) return inventory.Empty;
            Item item = null;
            try { item = GameData.ItemDB?.GetItemByID(slot.ItemId); }
            catch { }
            if (item == null || ReferenceEquals(item, inventory.Empty))
                item = Items.ItemFactory.ResolveItem(slot.ItemName);
            if (item == null || ReferenceEquals(item, inventory.Empty) ||
                !string.Equals(item.ItemName, slot.ItemName,
                    StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrEmpty(item.Id) &&
                    !string.Equals(item.Id, slot.ItemId,
                        StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException(
                    $"saved item '{slot.ItemName}' ({slot.ItemId}) cannot be resolved exactly");
            if (item.Stackable && slot.Quantity > 1000000)
                throw new InvalidDataException(
                    $"saved stack '{slot.ItemName}' is too large");
            return item;
        }

        private static bool TryBuildRecoveryProjection(InventoryRecord current,
            InventoryRecord corpse, IList<bool> safeStored,
            out InventoryRecord projected, out List<Placement> placements,
            out string error)
        {
            projected = current?.Clone();
            placements = new List<Placement>();
            error = null;
            try
            {
                if (current == null || corpse == null || safeStored == null ||
                    safeStored.Count != current.Stored.Count)
                    throw new InvalidOperationException(
                        "the live inventory shape changed during recovery");
                if (corpse.Gold < 0 || current.Gold < 0 ||
                    current.Gold > int.MaxValue - corpse.Gold)
                    throw new InvalidOperationException(
                        "recovering corpse gold would overflow the native gold counter");

                var pending = new List<(SlotArea area, int index)>();
                PlaceAreaDesired(corpse.Stored, SlotArea.Stored,
                    projected, safeStored, pending, placements);
                PlaceAreaDesired(corpse.Equipment, SlotArea.Equipment,
                    projected, safeStored, pending, placements);
                PlaceAreaDesired(corpse.Transmog, SlotArea.Transmog,
                    projected, safeStored, pending, placements);
                PlaceSingleDesired(corpse.Aura, SlotArea.Aura,
                    projected, pending, placements);
                PlaceSingleDesired(corpse.Charm, SlotArea.Charm,
                    projected, pending, placements);

                foreach (var source in pending)
                {
                    int fallback = FindEmptySafeStored(projected, safeStored);
                    if (fallback < 0)
                        throw new InvalidOperationException(
                            $"recovery needs {pending.Count} fallback inventory slot(s), but not enough safe slots are empty");
                    SlotRecord item = GetSlot(corpse, source.area, source.index);
                    projected.Stored[fallback] = item.Clone();
                    placements.Add(new Placement
                    {
                        SourceArea = source.area,
                        SourceIndex = source.index,
                        TargetArea = SlotArea.Stored,
                        TargetIndex = fallback
                    });
                }
                projected.Gold = current.Gold + corpse.Gold;
                return true;
            }
            catch (Exception ex)
            {
                projected = current?.Clone();
                placements.Clear();
                error = ex.Message;
                return false;
            }
        }

        private static void PlaceAreaDesired(IList<SlotRecord> source,
            SlotArea area, InventoryRecord projected, IList<bool> safeStored,
            List<(SlotArea area, int index)> pending,
            List<Placement> placements)
        {
            for (int i = 0; i < source.Count; i++)
            {
                SlotRecord item = source[i];
                if (item == null || item.IsEmpty) continue;
                IList<SlotRecord> target = GetArea(projected, area);
                bool safeTarget = i < target.Count && target[i].IsEmpty &&
                    (area != SlotArea.Stored || safeStored[i]);
                if (!safeTarget)
                {
                    pending.Add((area, i));
                    continue;
                }
                target[i] = item.Clone();
                placements.Add(new Placement
                {
                    SourceArea = area,
                    SourceIndex = i,
                    TargetArea = area,
                    TargetIndex = i
                });
            }
        }

        private static void PlaceSingleDesired(SlotRecord item, SlotArea area,
            InventoryRecord projected,
            List<(SlotArea area, int index)> pending,
            List<Placement> placements)
        {
            if (item == null || item.IsEmpty) return;
            SlotRecord target = GetSlot(projected, area, 0);
            if (!target.IsEmpty)
            {
                pending.Add((area, 0));
                return;
            }
            SetSlot(projected, area, 0, item.Clone());
            placements.Add(new Placement
            {
                SourceArea = area,
                SourceIndex = 0,
                TargetArea = area,
                TargetIndex = 0
            });
        }

        private static int FindEmptySafeStored(InventoryRecord projected,
            IList<bool> safeStored)
        {
            for (int i = 0; i < projected.Stored.Count; i++)
                if (safeStored[i] && projected.Stored[i].IsEmpty) return i;
            return -1;
        }

        private static RuntimeSnapshot ApplyPlacementsToRuntime(
            RuntimeSnapshot before, RuntimeSnapshot corpse,
            InventoryRecord projected, IList<Placement> placements)
        {
            RuntimeSnapshot after = before.Clone();
            after.Record = projected.Clone();
            foreach (Placement placement in placements)
            {
                Item item = GetRuntimeItem(corpse, placement.SourceArea,
                    placement.SourceIndex);
                SetRuntimeItem(after, placement.TargetArea,
                    placement.TargetIndex, item);
            }
            return after;
        }

        private static Item GetRuntimeItem(RuntimeSnapshot snapshot,
            SlotArea area, int index)
        {
            switch (area)
            {
                case SlotArea.Stored: return snapshot.StoredItems[index];
                case SlotArea.Equipment: return snapshot.EquipmentItems[index];
                case SlotArea.Transmog: return snapshot.TransmogItems[index];
                case SlotArea.Aura: return snapshot.AuraItem;
                case SlotArea.Charm: return snapshot.CharmItem;
                default: throw new ArgumentOutOfRangeException(nameof(area));
            }
        }

        private static void SetRuntimeItem(RuntimeSnapshot snapshot,
            SlotArea area, int index, Item item)
        {
            switch (area)
            {
                case SlotArea.Stored: snapshot.StoredItems[index] = item; break;
                case SlotArea.Equipment: snapshot.EquipmentItems[index] = item; break;
                case SlotArea.Transmog: snapshot.TransmogItems[index] = item; break;
                case SlotArea.Aura: snapshot.AuraItem = item; break;
                case SlotArea.Charm: snapshot.CharmItem = item; break;
                default: throw new ArgumentOutOfRangeException(nameof(area));
            }
        }

        private static bool TryApplyRuntime(RuntimeSnapshot snapshot,
            out string error)
        {
            error = null;
            try
            {
                Inventory inventory = GameData.PlayerInv;
                if (snapshot == null || inventory == null ||
                    snapshot.StoredItems.Count != inventory.StoredSlots.Count ||
                    snapshot.EquipmentItems.Count != inventory.EquipmentSlots.Count ||
                    snapshot.TransmogItems.Count != inventory.TransmogSlots.Count)
                    throw new InvalidOperationException(
                        "the native inventory shape changed during the transaction");
                ApplyArea(inventory.StoredSlots, snapshot.StoredItems,
                    snapshot.Record.Stored, inventory.Empty);
                ApplyArea(inventory.EquipmentSlots, snapshot.EquipmentItems,
                    snapshot.Record.Equipment, inventory.Empty);
                ApplyArea(inventory.TransmogSlots, snapshot.TransmogItems,
                    snapshot.Record.Transmog, inventory.Empty);
                ApplySlot(inventory.AuraSlot, snapshot.AuraItem,
                    snapshot.Record.Aura, inventory.Empty);
                ApplySlot(inventory.CharmSlot, snapshot.CharmItem,
                    snapshot.Record.Charm, inventory.Empty);
                inventory.Gold = snapshot.Record.Gold;
                inventory.UpdatePlayerInventory();
                foreach (ItemIcon slot in inventory.TransmogSlots)
                    slot.UpdateSlotImage();
                Items.BagWindow.RefreshAll();
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static void ApplyStrippedInventory(InventoryRecord stripped)
        {
            Inventory inventory = GameData.PlayerInv;
            ApplyEmptyArea(inventory.StoredSlots, inventory.Empty);
            ApplyEmptyArea(inventory.EquipmentSlots, inventory.Empty);
            ApplyEmptyArea(inventory.TransmogSlots, inventory.Empty);
            ApplySlot(inventory.AuraSlot, inventory.Empty, EmptySlot(),
                inventory.Empty);
            ApplySlot(inventory.CharmSlot, inventory.Empty, EmptySlot(),
                inventory.Empty);
            inventory.Gold = stripped.Gold;
            inventory.UpdatePlayerInventory();
            foreach (ItemIcon slot in inventory.TransmogSlots)
                slot.UpdateSlotImage();
            Items.BagWindow.RefreshAll();
        }

        private static void ApplyEmptyArea(IList<ItemIcon> slots, Item empty)
        {
            foreach (ItemIcon slot in slots)
                ApplySlot(slot, empty, EmptySlot(), empty);
        }

        private static void ApplyArea(IList<ItemIcon> targets,
            IList<Item> items, IList<SlotRecord> records, Item empty)
        {
            for (int i = 0; i < targets.Count; i++)
                ApplySlot(targets[i], items[i], records[i], empty);
        }

        private static void ApplySlot(ItemIcon target, Item item,
            SlotRecord record, Item empty)
        {
            if (target == null) throw new InvalidDataException("a native slot disappeared");
            bool blank = record == null || record.IsEmpty;
            target.MyItem = blank || item == null ? empty : item;
            target.Quantity = blank ? 1 : record.Quantity;
            target.UpdateSlotImage();
        }

        private static bool TryPersistAll(out string error)
        {
            error = null;
            try
            {
                if (GameData.GM == null || GameData.CurrentCharacterSlot == null)
                    throw new InvalidOperationException(
                        "the native save manager is unavailable");
                int sequence = _nativeSaveSequence;
                GameData.GM.SaveGameData(wScene: false);
                if (_nativeSaveSequence == sequence)
                    throw new IOException(
                        "the native save result could not be observed");
                if (!_lastNativeSaveSucceeded)
                    throw new IOException("the native character save failed");
                Items.ItemFactory.SaveCustomInventory();
                if (!SkillsSaveManager.Save())
                    throw new IOException(
                        "one or more Classic Skills sidecars failed to save");
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static void RepairAddonSidecars()
        {
            try
            {
                Items.ItemFactory.SaveCustomInventory();
                if (!SkillsSaveManager.Save())
                    SkillsPlugin.Log.LogError(
                        "Corpse reconciliation could not refresh addon sidecars.");
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogError(
                    "Corpse sidecar reconciliation failed: " + ex.Message);
            }
        }

        private static InventoryRecord CreateStrippedRecord(
            InventoryRecord shape)
        {
            var stripped = new InventoryRecord { Gold = 0 };
            for (int i = 0; i < shape.Stored.Count; i++)
                stripped.Stored.Add(EmptySlot());
            for (int i = 0; i < shape.Equipment.Count; i++)
                stripped.Equipment.Add(EmptySlot());
            for (int i = 0; i < shape.Transmog.Count; i++)
                stripped.Transmog.Add(EmptySlot());
            stripped.Aura = EmptySlot();
            stripped.Charm = EmptySlot();
            return stripped;
        }

        private static byte[] EncodeInventory(InventoryRecord record)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                writer.Write(PayloadMagic);
                writer.Write(record.Gold);
                WriteArea(writer, record.Stored);
                WriteArea(writer, record.Equipment);
                WriteArea(writer, record.Transmog);
                WriteSlot(writer, record.Aura);
                WriteSlot(writer, record.Charm);
                writer.Flush();
                if (stream.Length <= 0 || stream.Length > MaxPayloadBytes)
                    throw new InvalidDataException(
                        "corpse inventory payload is outside the safe size limit");
                return stream.ToArray();
            }
        }

        private static InventoryRecord DecodeInventory(byte[] payload)
        {
            if (payload == null || payload.Length <= 0 ||
                payload.Length > MaxPayloadBytes)
                throw new InvalidDataException(
                    "corpse inventory payload is outside the safe size limit");
            using (var stream = new MemoryStream(payload, false))
            using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
            {
                if (reader.ReadInt32() != PayloadMagic)
                    throw new InvalidDataException(
                        "corpse inventory payload has an unknown format");
                var record = new InventoryRecord { Gold = reader.ReadInt32() };
                if (record.Gold < 0)
                    throw new InvalidDataException("corpse gold cannot be negative");
                ReadArea(reader, stream, record.Stored);
                ReadArea(reader, stream, record.Equipment);
                ReadArea(reader, stream, record.Transmog);
                record.Aura = ReadSlot(reader, stream);
                record.Charm = ReadSlot(reader, stream);
                if (stream.Position != stream.Length)
                    throw new InvalidDataException(
                        "corpse inventory payload contains trailing data");
                ValidateInventoryRecord(record);
                return record;
            }
        }

        private static void WriteArea(BinaryWriter writer,
            IList<SlotRecord> slots)
        {
            if (slots == null || slots.Count > MaxSlotsPerArea)
                throw new InvalidDataException("corpse slot count is invalid");
            writer.Write(slots.Count);
            foreach (SlotRecord slot in slots) WriteSlot(writer, slot);
        }

        private static void ReadArea(BinaryReader reader, Stream stream,
            List<SlotRecord> slots)
        {
            EnsureRemaining(stream, sizeof(int));
            int count = reader.ReadInt32();
            if (count < 0 || count > MaxSlotsPerArea)
                throw new InvalidDataException("corpse slot count is invalid");
            for (int i = 0; i < count; i++)
                slots.Add(ReadSlot(reader, stream));
        }

        private static void WriteSlot(BinaryWriter writer, SlotRecord slot)
        {
            slot = slot ?? EmptySlot();
            WriteBoundedString(writer, slot.ItemId ?? "");
            WriteBoundedString(writer, slot.ItemName ?? "");
            writer.Write(slot.IsEmpty ? 1 : slot.Quantity);
        }

        private static SlotRecord ReadSlot(BinaryReader reader, Stream stream)
        {
            string id = ReadBoundedString(reader, stream);
            string name = ReadBoundedString(reader, stream);
            EnsureRemaining(stream, sizeof(int));
            int quantity = reader.ReadInt32();
            var slot = new SlotRecord
            {
                ItemId = id,
                ItemName = name,
                Quantity = quantity
            };
            ValidateSlot(slot);
            return slot;
        }

        private static void WriteBoundedString(BinaryWriter writer, string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value ?? "");
            if (bytes.Length > MaxItemTextBytes)
                throw new InvalidDataException("corpse item text is too long");
            writer.Write(bytes.Length);
            writer.Write(bytes);
        }

        private static string ReadBoundedString(BinaryReader reader,
            Stream stream)
        {
            EnsureRemaining(stream, sizeof(int));
            int length = reader.ReadInt32();
            if (length < 0 || length > MaxItemTextBytes)
                throw new InvalidDataException("corpse item text length is invalid");
            EnsureRemaining(stream, length);
            byte[] bytes = reader.ReadBytes(length);
            if (bytes.Length != length)
                throw new EndOfStreamException();
            return Encoding.UTF8.GetString(bytes);
        }

        private static void EnsureRemaining(Stream stream, long needed)
        {
            if (needed < 0 || stream.Length - stream.Position < needed)
                throw new EndOfStreamException(
                    "corpse inventory payload ended unexpectedly");
        }

        private static void ValidateInventoryRecord(InventoryRecord record)
        {
            foreach (SlotRecord slot in record.Stored) ValidateSlot(slot);
            foreach (SlotRecord slot in record.Equipment) ValidateSlot(slot);
            foreach (SlotRecord slot in record.Transmog) ValidateSlot(slot);
            ValidateSlot(record.Aura);
            ValidateSlot(record.Charm);
        }

        private static void ValidateSlot(SlotRecord slot)
        {
            if (slot == null) throw new InvalidDataException("corpse slot is null");
            bool hasId = !string.IsNullOrWhiteSpace(slot.ItemId);
            bool hasName = !string.IsNullOrWhiteSpace(slot.ItemName);
            if (hasId != hasName)
                throw new InvalidDataException(
                    "corpse slot has incomplete item identity");
            if (!hasId)
            {
                if (slot.Quantity != 1)
                    throw new InvalidDataException(
                        "empty corpse slot has an invalid quantity");
                return;
            }
            if (slot.Quantity <= 0 ||
                Encoding.UTF8.GetByteCount(slot.ItemId) > MaxItemTextBytes ||
                Encoding.UTF8.GetByteCount(slot.ItemName) > MaxItemTextBytes)
                throw new InvalidDataException(
                    "corpse slot has invalid item data");
        }

        private static string Fingerprint(byte[] payload)
        {
            using (SHA256 sha = SHA256.Create())
                return Convert.ToBase64String(sha.ComputeHash(payload));
        }

        private static bool TryWriteRecord(CorpseSaveRecord record,
            out string error)
        {
            error = null;
            try
            {
                string path = GetRecordPath();
                if (string.IsNullOrEmpty(path))
                    throw new InvalidOperationException(
                        "the current character has no safe corpse path");
                string json = JsonUtility.ToJson(record, true);
                if (!TryParseRecord(json, out CorpseSaveRecord validated,
                    out _, out _, out string validationError))
                    throw new InvalidDataException(validationError);
                if (!string.Equals(validated.Character,
                    SkillsSaveManager.CharName, StringComparison.Ordinal))
                    throw new InvalidDataException(
                        "corpse character identity does not match the loaded character");
                if (!SafeFileIO.TryWriteAllTextAtomic(path, json,
                    out string writeError)) throw new IOException(writeError);
                _record = record;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static bool TryLoadRecord(out CorpseSaveRecord record,
            out string error)
        {
            record = null;
            error = null;
            string path = GetRecordPath();
            if (string.IsNullOrEmpty(path))
            {
                error = "the current character has no safe corpse path";
                return false;
            }
            if (!File.Exists(path))
            {
                // A backup without a primary is from a completed transaction;
                // never resurrect an already recovered corpse from it.
                try
                {
                    if (File.Exists(path + ".bak")) File.Delete(path + ".bak");
                }
                catch (Exception ex)
                {
                    SkillsPlugin.Log.LogWarning(
                        "Could not remove stale corpse backup: " + ex.Message);
                }
                return true;
            }
            if (!SafeFileIO.TryReadAllTextWithBackup(path,
                IsRecordJsonValid, out string json, out bool recovered,
                out string readError))
            {
                error = readError;
                return false;
            }
            if (!TryParseRecord(json, out record, out _, out _,
                out string parseError))
            {
                error = parseError;
                return false;
            }
            if (recovered)
                SkillsPlugin.Log.LogWarning(
                    "Recovered corpse journal from its verified backup.");
            return true;
        }

        private static bool IsRecordJsonValid(string json)
        {
            return TryParseRecord(json, out _, out _, out _, out _);
        }

        private static bool TryParseRecord(string json,
            out CorpseSaveRecord record, out InventoryRecord inventory,
            out string bagSnapshot, out string error)
        {
            record = null;
            inventory = null;
            bagSnapshot = null;
            error = null;
            try
            {
                if (string.IsNullOrWhiteSpace(json) ||
                    json.Length > MaxJournalChars)
                    throw new InvalidDataException(
                        "corpse journal is empty or too large");
                record = JsonUtility.FromJson<CorpseSaveRecord>(json);
                if (record == null || record.Version != SaveFormatVersion ||
                    string.IsNullOrWhiteSpace(record.TransactionId) ||
                    record.TransactionId.Length > 128 ||
                    string.IsNullOrWhiteSpace(record.Character) ||
                    record.Character.Length > 256 ||
                    string.IsNullOrWhiteSpace(record.Zone) ||
                    record.Zone.Length > 512 ||
                    string.IsNullOrWhiteSpace(record.CreatedUtc) ||
                    !IsKnownState(record.State) ||
                    !Finite(record.PositionX) || !Finite(record.PositionY) ||
                    !Finite(record.PositionZ) || !Finite(record.RotationX) ||
                    !Finite(record.RotationY) || !Finite(record.RotationZ) ||
                    !Finite(record.RotationW))
                    throw new InvalidDataException(
                        "corpse journal metadata is incomplete");
                byte[] payload = Convert.FromBase64String(
                    record.PayloadBase64 ?? "");
                inventory = DecodeInventory(payload);
                byte[] bagBytes = Convert.FromBase64String(
                    record.BagSnapshotBase64 ?? "");
                if (bagBytes.Length > MaxBagSnapshotBytes)
                    throw new InvalidDataException(
                        "corpse bag snapshot is too large");
                bagSnapshot = Encoding.UTF8.GetString(bagBytes);
                if (!Items.BagSystem.IsSerializedDataValid(bagSnapshot))
                    throw new InvalidDataException(
                        "corpse bag snapshot failed validation");
                if (!string.IsNullOrEmpty(record.VisualSnapshotBase64))
                {
                    if (record.VisualSnapshotBase64.Length >
                        MaxVisualSnapshotBytes * 2)
                        throw new InvalidDataException(
                            "corpse visual snapshot is too large");
                    byte[] visualBytes = Convert.FromBase64String(
                        record.VisualSnapshotBase64);
                    if (!TryValidateVisualSnapshot(visualBytes,
                        out string visualError))
                        throw new InvalidDataException(
                            "corpse visual snapshot failed validation: " +
                            visualError);
                }
                string original = Fingerprint(payload);
                string stripped = Fingerprint(EncodeInventory(
                    CreateStrippedRecord(inventory)));
                if (!string.Equals(record.OriginalFingerprint, original,
                        StringComparison.Ordinal) ||
                    !string.Equals(record.StrippedFingerprint, stripped,
                        StringComparison.Ordinal) ||
                    string.Equals(original, stripped, StringComparison.Ordinal))
                    throw new InvalidDataException(
                        "corpse inventory fingerprints do not match the payload");
                if (string.Equals(record.State, StateRecovering,
                    StringComparison.Ordinal))
                {
                    if (string.IsNullOrWhiteSpace(
                            record.RecoveryBeforeFingerprint) ||
                        string.IsNullOrWhiteSpace(
                            record.RecoveryAfterFingerprint) ||
                        string.Equals(record.RecoveryBeforeFingerprint,
                            record.RecoveryAfterFingerprint,
                            StringComparison.Ordinal) ||
                        string.IsNullOrWhiteSpace(
                            record.RecoveryBeforeBagFingerprint) ||
                        string.IsNullOrWhiteSpace(
                            record.RecoveryAfterBagFingerprint))
                        throw new InvalidDataException(
                            "corpse recovery fingerprints are incomplete");

                    byte[] recoveryBefore = Convert.FromBase64String(
                        record.RecoveryBeforePayloadBase64 ?? "");
                    byte[] recoveryAfter = Convert.FromBase64String(
                        record.RecoveryAfterPayloadBase64 ?? "");
                    InventoryRecord beforeInventory =
                        DecodeInventory(recoveryBefore);
                    InventoryRecord afterInventory =
                        DecodeInventory(recoveryAfter);
                    if (!HaveSameShape(beforeInventory, afterInventory) ||
                        !string.Equals(Fingerprint(recoveryBefore),
                            record.RecoveryBeforeFingerprint,
                            StringComparison.Ordinal) ||
                        !string.Equals(Fingerprint(recoveryAfter),
                            record.RecoveryAfterFingerprint,
                            StringComparison.Ordinal))
                        throw new InvalidDataException(
                            "corpse recovery inventory snapshots do not match their fingerprints");

                    string recoveryBeforeBags = DecodeText(
                        record.RecoveryBeforeBagsBase64);
                    string recoveryAfterBags = DecodeText(
                        record.RecoveryAfterBagsBase64);
                    if (!Items.BagSystem.IsSerializedDataValid(
                            recoveryBeforeBags) ||
                        !Items.BagSystem.IsSerializedDataValid(
                            recoveryAfterBags) ||
                        !string.Equals(Fingerprint(Encoding.UTF8.GetBytes(
                                recoveryBeforeBags)),
                            record.RecoveryBeforeBagFingerprint,
                            StringComparison.Ordinal) ||
                        !string.Equals(Fingerprint(Encoding.UTF8.GetBytes(
                                recoveryAfterBags)),
                            record.RecoveryAfterBagFingerprint,
                            StringComparison.Ordinal))
                        throw new InvalidDataException(
                            "corpse recovery bag snapshots do not match their fingerprints");
                    bool hasRecoveryBag = !string.IsNullOrEmpty(
                        record.RecoveryBagId);
                    if (hasRecoveryBag != !string.Equals(
                            record.RecoveryBeforeBagFingerprint,
                            record.RecoveryAfterBagFingerprint,
                            StringComparison.Ordinal))
                        throw new InvalidDataException(
                            "corpse recovery container metadata is inconsistent");
                    int corpseItemCount = CountPossessionSlots(inventory);
                    if (hasRecoveryBag)
                    {
                        string containerError = null;
                        bool validContainer = TryGetBagNumber(
                            record.RecoveryBagId, out int recoveryBagNumber) &&
                            CountMatchingBagSlots(afterInventory,
                                recoveryBagNumber) == 1;
                        if (validContainer)
                            validContainer = Items.BagSystem
                                .ValidateRecoveryContainerSnapshot(
                                    recoveryAfterBags, record.RecoveryBagId,
                                    corpseItemCount, out containerError);
                        if (!validContainer)
                            throw new InvalidDataException(
                                "corpse recovery container validation failed: " +
                                (containerError ?? "native slot identity changed"));
                    }
                    else if (corpseItemCount != 0 ||
                        !string.Equals(record.RecoveryBeforeBagFingerprint,
                            record.RecoveryAfterBagFingerprint,
                            StringComparison.Ordinal))
                    {
                        throw new InvalidDataException(
                            "an item-bearing recovery has no Corpse Bag");
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                record = null;
                inventory = null;
                bagSnapshot = null;
                return false;
            }
        }

        private static bool IsKnownState(string state)
        {
            return string.Equals(state, StatePrepared, StringComparison.Ordinal) ||
                string.Equals(state, StateStored, StringComparison.Ordinal) ||
                string.Equals(state, StateRecovering, StringComparison.Ordinal);
        }

        private static bool DeleteRecordFiles(out string error)
        {
            error = null;
            try
            {
                string path = GetRecordPath();
                if (string.IsNullOrEmpty(path))
                    throw new InvalidOperationException(
                        "the corpse journal path is unavailable");
                if (File.Exists(path)) File.Delete(path);
                if (File.Exists(path + ".bak")) File.Delete(path + ".bak");
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static string GetRecordPath()
        {
            if (string.IsNullOrEmpty(SkillsSaveManager.CharName)) return null;
            return Path.Combine(RuntimePaths.DataDirectory,
                SkillsSaveManager.CharName + "_corpse.json");
        }

        private static CorpseSaveRecord CloneRecord(CorpseSaveRecord source)
        {
            return new CorpseSaveRecord
            {
                Version = source.Version,
                TransactionId = source.TransactionId,
                Character = source.Character,
                State = source.State,
                Zone = source.Zone,
                PositionX = source.PositionX,
                PositionY = source.PositionY,
                PositionZ = source.PositionZ,
                RotationX = source.RotationX,
                RotationY = source.RotationY,
                RotationZ = source.RotationZ,
                RotationW = source.RotationW,
                CreatedUtc = source.CreatedUtc,
                PayloadBase64 = source.PayloadBase64,
                BagSnapshotBase64 = source.BagSnapshotBase64,
                VisualSnapshotBase64 = source.VisualSnapshotBase64,
                OriginalFingerprint = source.OriginalFingerprint,
                StrippedFingerprint = source.StrippedFingerprint,
                RecoveryBeforeFingerprint = source.RecoveryBeforeFingerprint,
                RecoveryAfterFingerprint = source.RecoveryAfterFingerprint,
                RecoveryBeforePayloadBase64 =
                    source.RecoveryBeforePayloadBase64,
                RecoveryAfterPayloadBase64 =
                    source.RecoveryAfterPayloadBase64,
                RecoveryBeforeBagsBase64 = source.RecoveryBeforeBagsBase64,
                RecoveryAfterBagsBase64 = source.RecoveryAfterBagsBase64,
                RecoveryBeforeBagFingerprint =
                    source.RecoveryBeforeBagFingerprint,
                RecoveryAfterBagFingerprint =
                    source.RecoveryAfterBagFingerprint,
                RecoveryBagId = source.RecoveryBagId
            };
        }

        private static ReconcileResult Classify(CorpseSaveRecord record,
            string currentFingerprint)
        {
            if (string.Equals(currentFingerprint,
                record.OriginalFingerprint, StringComparison.Ordinal))
                return ReconcileResult.Original;
            if (string.Equals(currentFingerprint,
                record.StrippedFingerprint, StringComparison.Ordinal))
                return ReconcileResult.Stripped;
            return ReconcileResult.Ambiguous;
        }

        private static ReconcileResult ClassifyRecovery(CorpseSaveRecord record,
            string inventoryFingerprint, string bagFingerprint)
        {
            bool inventoryBefore = string.Equals(inventoryFingerprint,
                record.RecoveryBeforeFingerprint, StringComparison.Ordinal);
            bool inventoryAfter = string.Equals(inventoryFingerprint,
                record.RecoveryAfterFingerprint, StringComparison.Ordinal);
            bool bagsBefore = string.Equals(bagFingerprint,
                record.RecoveryBeforeBagFingerprint, StringComparison.Ordinal);
            bool bagsAfter = string.Equals(bagFingerprint,
                record.RecoveryAfterBagFingerprint, StringComparison.Ordinal);
            if (inventoryAfter && bagsAfter) return ReconcileResult.Recovered;
            if (inventoryBefore && bagsBefore) return ReconcileResult.Original;
            if ((inventoryBefore || inventoryAfter) &&
                (bagsBefore || bagsAfter)) return ReconcileResult.Partial;
            return ReconcileResult.Ambiguous;
        }

        private static bool TryRollbackInterruptedRecovery(
            CorpseSaveRecord record, RuntimeSnapshot current,
            string currentBags, out string error)
        {
            error = null;
            try
            {
                InventoryRecord beforeRecord = DecodeInventory(
                    Convert.FromBase64String(
                        record.RecoveryBeforePayloadBase64 ?? ""));
                RuntimeSnapshot before = ResolveRuntime(beforeRecord);
                string beforeBags = DecodeText(
                    record.RecoveryBeforeBagsBase64);
                if (!Items.BagSystem.TryRestoreSerializedState(beforeBags,
                    out string bagError))
                    throw new InvalidDataException(bagError);
                if (!TryApplyRuntime(before, out string applyError))
                    throw new InvalidOperationException(applyError);
                if (!TryPersistAll(out string saveError))
                    throw new IOException(saveError);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                // Restore the exact state observed at load so a failed repair
                // cannot become a third, unjournaled outcome in memory.
                try
                {
                    if (!string.IsNullOrEmpty(currentBags))
                        Items.BagSystem.TryRestoreSerializedState(currentBags,
                            out _);
                    if (current != null) TryApplyRuntime(current, out _);
                }
                catch { }
                return false;
            }
        }

        private static void ClearRecoveryFields(CorpseSaveRecord record)
        {
            record.RecoveryBeforeFingerprint = "";
            record.RecoveryAfterFingerprint = "";
            record.RecoveryBeforePayloadBase64 = "";
            record.RecoveryAfterPayloadBase64 = "";
            record.RecoveryBeforeBagsBase64 = "";
            record.RecoveryAfterBagsBase64 = "";
            record.RecoveryBeforeBagFingerprint = "";
            record.RecoveryAfterBagFingerprint = "";
            record.RecoveryBagId = "";
        }

        private static bool HaveSameShape(InventoryRecord left,
            InventoryRecord right)
        {
            return left != null && right != null &&
                left.Stored.Count == right.Stored.Count &&
                left.Equipment.Count == right.Equipment.Count &&
                left.Transmog.Count == right.Transmog.Count;
        }

        private static bool TryGetBagNumber(string bagId, out int number)
        {
            number = 0;
            return !string.IsNullOrEmpty(bagId) && bagId.Length > 3 &&
                bagId.StartsWith("bag", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(bagId.Substring(3), out number) && number >= 100 &&
                number < int.MaxValue;
        }

        private static int CountMatchingBagSlots(InventoryRecord record,
            int bagNumber)
        {
            int count = 0;
            CountMatchingBagSlots(record.Stored, bagNumber, ref count);
            CountMatchingBagSlots(record.Equipment, bagNumber, ref count);
            CountMatchingBagSlots(record.Transmog, bagNumber, ref count);
            if (IsMatchingBagSlot(record.Aura, bagNumber)) count++;
            if (IsMatchingBagSlot(record.Charm, bagNumber)) count++;
            return count;
        }

        private static void CountMatchingBagSlots(IEnumerable<SlotRecord> slots,
            int bagNumber, ref int count)
        {
            foreach (SlotRecord slot in slots)
                if (IsMatchingBagSlot(slot, bagNumber)) count++;
        }

        private static bool IsMatchingBagSlot(SlotRecord slot, int bagNumber)
        {
            return slot != null && !slot.IsEmpty &&
                slot.Quantity == bagNumber &&
                Items.BagSystem.IsCorpseBag(slot.ItemName);
        }

        private static string EncodeText(string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value ?? "");
            if (bytes.Length > MaxBagSnapshotBytes)
                throw new InvalidDataException(
                    "a recovery bag snapshot is too large");
            return Convert.ToBase64String(bytes);
        }

        private static string DecodeText(string encoded)
        {
            byte[] bytes = Convert.FromBase64String(encoded ?? "");
            if (bytes.Length > MaxBagSnapshotBytes)
                throw new InvalidDataException(
                    "a recovery bag snapshot is too large");
            return Encoding.UTF8.GetString(bytes);
        }

        private static void SpawnMarkerIfNeeded(bool force)
        {
            if (_record == null || _loadLocked || _marker != null ||
                !string.Equals(_record.State, StateStored,
                    StringComparison.Ordinal) ||
                !string.Equals(SceneManager.GetActiveScene().name,
                    _record.Zone, StringComparison.Ordinal) ||
                (!force && Time.unscaledTime < _nextMarkerAttempt)) return;
            _nextMarkerAttempt = Time.unscaledTime + 2f;
            try
            {
                Vector3 position = new Vector3(_record.PositionX,
                    _record.PositionY, _record.PositionZ);
                Quaternion rotation = HorizontalRotation(new Quaternion(
                    _record.RotationX, _record.RotationY,
                    _record.RotationZ, _record.RotationW));
                _marker = new GameObject("ClassicSkills_PlayerCorpse");
                _marker.transform.position = position;
                _marker.transform.rotation = rotation;
                _markerUsesCharacterVisual =
                    TryCreateCharacterCorpseVisual(_marker.transform,
                        out string visualDetail);
                if (!_markerUsesCharacterVisual)
                {
                    bool visualCopied = false;
                    GameObject prefab = GameData.Misc?.CorpseChest;
                    if (prefab != null)
                    {
                        var visualRoot = new GameObject("NativeCorpseVisual");
                        visualRoot.transform.SetParent(_marker.transform, false);
                        visualCopied = CopyVisualHierarchy(prefab.transform,
                            visualRoot.transform, true) > 0;
                    }
                    if (!visualCopied)
                        CreateFallbackMarker(_marker.transform);
                    SkillsPlugin.Log.LogWarning(
                        "Corpse marker used the chest fallback: " +
                        visualDetail);
                }
                else
                {
                    SkillsPlugin.Log.LogInfo(
                        "Corpse marker rebuilt as the saved character: " +
                        visualDetail);
                }
                CreatePrompt(_marker.transform);
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogWarning(
                    "Corpse marker spawn failed; retrying: " + ex.Message);
                DestroyMarker();
            }
        }

        private static int[] CreateVisualColorPropertyIds()
        {
            var ids = new int[VisualColorProperties.Length];
            for (int i = 0; i < ids.Length; i++)
                ids[i] = Shader.PropertyToID(VisualColorProperties[i]);
            return ids;
        }

        private static bool TryCaptureCharacterVisualSnapshot(
            out string encoded, out string error)
        {
            encoded = "";
            error = null;
            try
            {
                Transform root = GameData.PlayerInv?.Modulars?.transform;
                if (root == null || !root.gameObject.activeInHierarchy)
                    throw new InvalidOperationException(
                        "the active player model is unavailable");

                Transform[] nodes = root.GetComponentsInChildren<Transform>(true);
                Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
                if (nodes.Length <= 0 || nodes.Length > MaxVisualNodes ||
                    renderers.Length <= 0 ||
                    renderers.Length > MaxVisualRenderers)
                    throw new InvalidDataException(
                        "the player model hierarchy is outside safe bounds");

                byte[] bytes;
                using (var stream = new MemoryStream())
                using (var writer = new BinaryWriter(stream, Encoding.UTF8,
                    true))
                {
                    writer.Write(VisualSnapshotMagic);
                    writer.Write(VisualSnapshotVersion);
                    writer.Write(nodes.Length);
                    foreach (Transform node in nodes)
                    {
                        WriteVisualPath(writer, root, node);
                        writer.Write(node.gameObject.activeSelf);
                    }

                    writer.Write(renderers.Length);
                    var block = new MaterialPropertyBlock();
                    foreach (Renderer renderer in renderers)
                    {
                        WriteVisualPath(writer, root, renderer.transform);
                        Renderer[] peers =
                            renderer.transform.GetComponents<Renderer>();
                        int componentIndex = Array.IndexOf(peers, renderer);
                        if (componentIndex < 0 || componentIndex > 16)
                            throw new InvalidDataException(
                                "a corpse renderer index is invalid");
                        writer.Write(componentIndex);
                        writer.Write(renderer.enabled);

                        block.Clear();
                        renderer.GetPropertyBlock(block);
                        int colorCount = 0;
                        for (int i = 0; i < VisualColorPropertyIds.Length; i++)
                            if (block.HasColor(VisualColorPropertyIds[i]))
                                colorCount++;
                        writer.Write(colorCount);
                        for (int i = 0; i < VisualColorPropertyIds.Length; i++)
                        {
                            if (!block.HasColor(VisualColorPropertyIds[i]))
                                continue;
                            Color color = block.GetColor(
                                VisualColorPropertyIds[i]);
                            if (!Finite(color.r) || !Finite(color.g) ||
                                !Finite(color.b) || !Finite(color.a))
                                throw new InvalidDataException(
                                    "a corpse material color is invalid");
                            writer.Write(i);
                            writer.Write(color.r);
                            writer.Write(color.g);
                            writer.Write(color.b);
                            writer.Write(color.a);
                        }
                    }
                    writer.Flush();
                    if (stream.Length <= 0 ||
                        stream.Length > MaxVisualSnapshotBytes)
                        throw new InvalidDataException(
                            "the corpse visual snapshot is too large");
                    bytes = stream.ToArray();
                }

                if (!TryValidateVisualSnapshot(bytes, out error)) return false;
                encoded = Convert.ToBase64String(bytes);
                SkillsPlugin.Log.LogInfo(
                    $"Captured corpse appearance: nodes={nodes.Length}, " +
                    $"renderers={renderers.Length}, bytes={bytes.Length}.");
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                encoded = "";
                return false;
            }
        }

        private static void WriteVisualPath(BinaryWriter writer,
            Transform root, Transform target)
        {
            var indices = new List<int>();
            Transform current = target;
            while (current != null && current != root)
            {
                indices.Add(current.GetSiblingIndex());
                current = current.parent;
            }
            if (current != root || indices.Count > MaxVisualPathDepth)
                throw new InvalidDataException(
                    "a corpse visual path is outside the player model");
            writer.Write((byte)indices.Count);
            for (int i = indices.Count - 1; i >= 0; i--)
                writer.Write(indices[i]);
        }

        private static int[] ReadVisualPath(BinaryReader reader)
        {
            int depth = reader.ReadByte();
            if (depth < 0 || depth > MaxVisualPathDepth)
                throw new InvalidDataException(
                    "a corpse visual path is too deep");
            var path = new int[depth];
            for (int i = 0; i < depth; i++)
            {
                path[i] = reader.ReadInt32();
                if (path[i] < 0 || path[i] > MaxVisualNodes)
                    throw new InvalidDataException(
                        "a corpse visual child index is invalid");
            }
            return path;
        }

        private static Transform ResolveVisualPath(Transform root, int[] path)
        {
            Transform current = root;
            for (int i = 0; current != null && i < path.Length; i++)
            {
                int childIndex = path[i];
                if (childIndex < 0 || childIndex >= current.childCount)
                    return null;
                current = current.GetChild(childIndex);
            }
            return current;
        }

        private static bool TryValidateVisualSnapshot(byte[] bytes,
            out string error)
        {
            error = null;
            try
            {
                if (bytes == null || bytes.Length <= 0 ||
                    bytes.Length > MaxVisualSnapshotBytes)
                    throw new InvalidDataException(
                        "visual snapshot size is invalid");
                using (var stream = new MemoryStream(bytes, false))
                using (var reader = new BinaryReader(stream, Encoding.UTF8,
                    true))
                {
                    if (reader.ReadInt32() != VisualSnapshotMagic ||
                        reader.ReadInt32() != VisualSnapshotVersion)
                        throw new InvalidDataException(
                            "visual snapshot header is invalid");
                    int nodeCount = reader.ReadInt32();
                    if (nodeCount <= 0 || nodeCount > MaxVisualNodes)
                        throw new InvalidDataException(
                            "visual snapshot node count is invalid");
                    for (int i = 0; i < nodeCount; i++)
                    {
                        ReadVisualPath(reader);
                        reader.ReadBoolean();
                    }

                    int rendererCount = reader.ReadInt32();
                    if (rendererCount <= 0 ||
                        rendererCount > MaxVisualRenderers)
                        throw new InvalidDataException(
                            "visual snapshot renderer count is invalid");
                    for (int i = 0; i < rendererCount; i++)
                    {
                        ReadVisualPath(reader);
                        int componentIndex = reader.ReadInt32();
                        if (componentIndex < 0 || componentIndex > 16)
                            throw new InvalidDataException(
                                "visual snapshot renderer index is invalid");
                        reader.ReadBoolean();
                        int colorCount = reader.ReadInt32();
                        if (colorCount < 0 ||
                            colorCount > VisualColorProperties.Length)
                            throw new InvalidDataException(
                                "visual snapshot color count is invalid");
                        var seen = new HashSet<int>();
                        for (int j = 0; j < colorCount; j++)
                        {
                            int propertyIndex = reader.ReadInt32();
                            if (propertyIndex < 0 ||
                                propertyIndex >= VisualColorProperties.Length ||
                                !seen.Add(propertyIndex))
                                throw new InvalidDataException(
                                    "visual snapshot color property is invalid");
                            float r = reader.ReadSingle();
                            float g = reader.ReadSingle();
                            float b = reader.ReadSingle();
                            float a = reader.ReadSingle();
                            if (!Finite(r) || !Finite(g) || !Finite(b) ||
                                !Finite(a))
                                throw new InvalidDataException(
                                    "visual snapshot color is invalid");
                        }
                    }
                    if (stream.Position != stream.Length)
                        throw new InvalidDataException(
                            "visual snapshot has trailing data");
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static bool TryCreateCharacterCorpseVisual(Transform parent,
            out string detail)
        {
            detail = "the saved character appearance is unavailable";
            GameObject visualRoot = null;
            var createdMeshes = new List<Mesh>();
            try
            {
                if (_record == null ||
                    string.IsNullOrEmpty(_record.VisualSnapshotBase64))
                    return false;
                Transform source = GameData.PlayerInv?.Modulars?.transform;
                if (source == null)
                {
                    detail = "the active character model is unavailable";
                    return false;
                }
                byte[] bytes = Convert.FromBase64String(
                    _record.VisualSnapshotBase64);
                if (!TryValidateVisualSnapshot(bytes,
                    out string validationError))
                {
                    detail = validationError;
                    return false;
                }

                visualRoot = new GameObject("SavedCharacterCorpseVisual");
                visualRoot.transform.SetParent(parent, false);
                visualRoot.transform.localPosition =
                    new Vector3(0f, 0.20f, 0f);
                visualRoot.transform.localRotation =
                    Quaternion.Euler(90f, 0f, 0f);
                // BakeMesh(useScale: true) preserves the rig's character-space
                // deformation. The modular root scale converts those model
                // units back to normal world size exactly once.
                visualRoot.transform.localScale = source.lossyScale;

                if (!TryBuildBakedCharacterCorpse(source,
                    visualRoot.transform, bytes, createdMeshes,
                    out int visibleRenderers, out int missingPaths,
                    out string buildError))
                    throw new InvalidDataException(buildError);
                if (visibleRenderers <= 0)
                    throw new InvalidDataException(
                        "the reconstructed body has no visible meshes");
                if (!TryGetCharacterCorpseBounds(visualRoot,
                    out Bounds corpseBounds))
                    throw new InvalidDataException(
                        "the reconstructed body has no render bounds");
                ValidateCharacterCorpseBounds(corpseBounds);
                MarkerOwnedMeshes.AddRange(createdMeshes);
                createdMeshes.Clear();
                _markerVisibleRendererCount = visibleRenderers;
                _markerMissingVisualPaths = missingPaths;
                string boundsDetail = GetCharacterCorpseBoundsDetail(
                    visualRoot.transform.parent.position, corpseBounds);
                detail = $"renderers={visibleRenderers}, " +
                    $"missing-paths={missingPaths}, {boundsDetail}";
                return true;
            }
            catch (Exception ex)
            {
                detail = ex.Message;
                if (visualRoot != null)
                    UnityEngine.Object.Destroy(visualRoot);
                foreach (Mesh mesh in createdMeshes)
                    if (mesh != null) UnityEngine.Object.Destroy(mesh);
                _markerVisibleRendererCount = 0;
                _markerMissingVisualPaths = 0;
                return false;
            }
        }

        private static bool TryBuildBakedCharacterCorpse(Transform sourceRoot,
            Transform destinationRoot, byte[] bytes, IList<Mesh> createdMeshes,
            out int visibleRenderers, out int missingPaths, out string error)
        {
            visibleRenderers = 0;
            missingPaths = 0;
            error = null;
            try
            {
                var activeNodes = new Dictionary<string, bool>(
                    StringComparer.Ordinal);
                using (var stream = new MemoryStream(bytes, false))
                using (var reader = new BinaryReader(stream, Encoding.UTF8,
                    true))
                {
                    if (reader.ReadInt32() != VisualSnapshotMagic ||
                        reader.ReadInt32() != VisualSnapshotVersion)
                        throw new InvalidDataException(
                            "visual snapshot header changed");
                    int nodeCount = reader.ReadInt32();
                    for (int i = 0; i < nodeCount; i++)
                    {
                        int[] path = ReadVisualPath(reader);
                        bool active = reader.ReadBoolean();
                        activeNodes[VisualPathKey(path, path.Length)] = active;
                    }

                    int rendererCount = reader.ReadInt32();
                    for (int i = 0; i < rendererCount; i++)
                    {
                        int[] path = ReadVisualPath(reader);
                        int componentIndex = reader.ReadInt32();
                        bool enabled = reader.ReadBoolean();
                        int colorCount = reader.ReadInt32();
                        var properties = new int[colorCount];
                        var colors = new Color[colorCount];
                        for (int j = 0; j < colorCount; j++)
                        {
                            properties[j] = reader.ReadInt32();
                            colors[j] = new Color(reader.ReadSingle(),
                                reader.ReadSingle(), reader.ReadSingle(),
                                reader.ReadSingle());
                        }

                        if (!enabled ||
                            !IsSavedVisualPathActive(activeNodes, path))
                            continue;
                        Transform target = ResolveVisualPath(sourceRoot, path);
                        Renderer[] renderers = target == null ? null :
                            target.GetComponents<Renderer>();
                        if (renderers == null || componentIndex < 0 ||
                            componentIndex >= renderers.Length)
                        {
                            missingPaths++;
                            continue;
                        }
                        Renderer sourceRenderer = renderers[componentIndex];
                        int partsBefore = destinationRoot.childCount;
                        if (!TryBakeRenderer(sourceRoot, destinationRoot,
                            sourceRenderer, properties, colors, createdMeshes,
                            out string rendererError))
                            throw new InvalidDataException(rendererError);
                        if (destinationRoot.childCount > partsBefore)
                            visibleRenderers++;
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static string VisualPathKey(int[] path, int length)
        {
            if (length <= 0) return "";
            var key = new StringBuilder(length * 4);
            for (int i = 0; i < length; i++)
            {
                if (i > 0) key.Append('/');
                key.Append(path[i]);
            }
            return key.ToString();
        }

        private static bool IsSavedVisualPathActive(
            IDictionary<string, bool> activeNodes, int[] path)
        {
            for (int length = 0; length <= path.Length; length++)
            {
                if (!activeNodes.TryGetValue(
                    VisualPathKey(path, length), out bool active) || !active)
                    return false;
            }
            return true;
        }

        private static bool TryBakeRenderer(Transform sourceRoot,
            Transform destinationRoot, Renderer sourceRenderer,
            int[] properties, Color[] colors, IList<Mesh> createdMeshes,
            out string error)
        {
            error = null;
            try
            {
                Mesh mesh = null;
                bool ownsMesh = false;
                Vector3 partScale;
                if (sourceRenderer is SkinnedMeshRenderer skinned)
                {
                    if (skinned.sharedMesh == null) return true;
                    mesh = new Mesh
                    {
                        name = "Corpse_" + skinned.sharedMesh.name
                    };
                    skinned.BakeMesh(mesh, true);
                    if (mesh.vertexCount <= 0)
                    {
                        UnityEngine.Object.Destroy(mesh);
                        throw new InvalidDataException(
                            "a skinned corpse mesh baked empty");
                    }
                    createdMeshes.Add(mesh);
                    ownsMesh = true;
                    partScale = Vector3.one;
                }
                else if (sourceRenderer is MeshRenderer)
                {
                    mesh = sourceRenderer.GetComponent<MeshFilter>()?.sharedMesh;
                    if (mesh == null) return true;
                    Vector3 sourceScale = sourceRoot.lossyScale;
                    Vector3 rendererScale =
                        sourceRenderer.transform.lossyScale;
                    partScale = new Vector3(
                        SafeScaleRatio(rendererScale.x, sourceScale.x),
                        SafeScaleRatio(rendererScale.y, sourceScale.y),
                        SafeScaleRatio(rendererScale.z, sourceScale.z));
                }
                else
                {
                    return true;
                }

                var part = new GameObject("CorpsePart_" +
                    sourceRenderer.gameObject.name);
                part.transform.SetParent(destinationRoot, false);
                part.transform.localPosition = sourceRoot.InverseTransformPoint(
                    sourceRenderer.transform.position);
                part.transform.localRotation = Quaternion.Inverse(
                    sourceRoot.rotation) * sourceRenderer.transform.rotation;
                part.transform.localScale = partScale;
                part.layer = sourceRenderer.gameObject.layer;

                MeshFilter filter = part.AddComponent<MeshFilter>();
                filter.sharedMesh = mesh;
                MeshRenderer renderer = part.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = sourceRenderer.sharedMaterials;
                renderer.shadowCastingMode = sourceRenderer.shadowCastingMode;
                renderer.receiveShadows = sourceRenderer.receiveShadows;
                if (properties.Length > 0)
                {
                    var block = new MaterialPropertyBlock();
                    for (int i = 0; i < properties.Length; i++)
                        block.SetColor(VisualColorPropertyIds[properties[i]],
                            colors[i]);
                    renderer.SetPropertyBlock(block);
                }
                if (ownsMesh)
                    mesh.RecalculateBounds();
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static float SafeScaleRatio(float value, float divisor)
        {
            return Mathf.Abs(divisor) < 0.0001f ? value : value / divisor;
        }

        private static Quaternion HorizontalRotation(Quaternion rotation)
        {
            Vector3 forward = rotation * Vector3.forward;
            forward.y = 0f;
            if (!Finite(forward.x) || !Finite(forward.z) ||
                forward.sqrMagnitude < 0.0001f)
                return Quaternion.identity;
            return Quaternion.LookRotation(forward.normalized, Vector3.up);
        }

        private static bool TryGetCharacterCorpseBounds(
            GameObject visualRoot, out Bounds combined)
        {
            Renderer[] renderers =
                visualRoot.GetComponentsInChildren<Renderer>(true);
            bool found = false;
            combined = default;
            foreach (Renderer renderer in renderers)
            {
                if (!renderer.enabled) continue;
                if (!found)
                {
                    combined = renderer.bounds;
                    found = true;
                }
                else
                {
                    combined.Encapsulate(renderer.bounds);
                }
            }
            return found;
        }

        private static void ValidateCharacterCorpseBounds(Bounds bounds)
        {
            Vector3 size = bounds.size;
            if (!Finite(bounds.center.x) || !Finite(bounds.center.y) ||
                !Finite(bounds.center.z) || !Finite(size.x) ||
                !Finite(size.y) || !Finite(size.z))
                throw new InvalidDataException(
                    "the reconstructed body bounds are invalid");
            float largest = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            float smallest = Mathf.Min(size.x, Mathf.Min(size.y, size.z));
            if (largest < 0.65f || largest > 4.5f || smallest < 0.05f)
                throw new InvalidDataException(
                    $"the reconstructed body scale is unsafe " +
                    $"({size.x:0.00}, {size.y:0.00}, {size.z:0.00})");
        }

        private static string GetCharacterCorpseBoundsDetail(
            Vector3 markerPosition, Bounds combined)
        {
            Vector3 offset = combined.center - markerPosition;
            return $"bounds-offset=({offset.x:0.00},{offset.y:0.00}," +
                $"{offset.z:0.00}), bounds-size=({combined.size.x:0.00}," +
                $"{combined.size.y:0.00},{combined.size.z:0.00})";
        }

        private static int CopyVisualHierarchy(Transform source,
            Transform destination, bool root)
        {
            int copied = 0;
            Transform target = destination;
            if (!root)
            {
                var child = new GameObject(source.name);
                target = child.transform;
                target.SetParent(destination, false);
                target.localPosition = source.localPosition;
                target.localRotation = source.localRotation;
                target.localScale = source.localScale;
            }
            else
            {
                target.localScale = source.localScale;
            }

            MeshFilter sourceFilter = source.GetComponent<MeshFilter>();
            MeshRenderer sourceRenderer = source.GetComponent<MeshRenderer>();
            if (sourceFilter != null && sourceRenderer != null &&
                sourceFilter.sharedMesh != null)
            {
                MeshFilter filter = target.gameObject.AddComponent<MeshFilter>();
                filter.sharedMesh = sourceFilter.sharedMesh;
                MeshRenderer renderer = target.gameObject.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = sourceRenderer.sharedMaterials;
                renderer.enabled = sourceRenderer.enabled;
                copied++;
            }
            for (int i = 0; i < source.childCount; i++)
                copied += CopyVisualHierarchy(source.GetChild(i), target, false);
            return copied;
        }

        private static void CreateFallbackMarker(Transform parent)
        {
            GameObject chest = GameObject.CreatePrimitive(PrimitiveType.Cube);
            chest.name = "CorpseMarkerFallback";
            chest.transform.SetParent(parent, false);
            chest.transform.localPosition = new Vector3(0f, 0.35f, 0f);
            chest.transform.localScale = new Vector3(0.9f, 0.55f, 1.15f);
            Collider collider = chest.GetComponent<Collider>();
            if (collider != null) UnityEngine.Object.Destroy(collider);
            Renderer renderer = chest.GetComponent<Renderer>();
            Renderer nativeRenderer = GameData.Misc?.CorpseChest?
                .GetComponentInChildren<Renderer>(true);
            if (renderer != null && nativeRenderer != null)
                renderer.sharedMaterials = nativeRenderer.sharedMaterials;
        }

        private static void CreatePrompt(Transform parent)
        {
            var promptObject = new GameObject("CorpseRecoveryPrompt");
            promptObject.transform.SetParent(parent, false);
            promptObject.transform.localPosition = new Vector3(0f, 1.35f, 0f);
            _prompt = promptObject.AddComponent<TextMeshPro>();
            TextMeshPro native = GameData.Misc?.LootableTxt?
                .GetComponentInChildren<TextMeshPro>(true);
            if (native != null)
            {
                _prompt.font = native.font;
                _prompt.fontSharedMaterial = native.fontSharedMaterial;
                _prompt.fontSize = native.fontSize;
                _prompt.color = native.color;
                _prompt.alignment = native.alignment;
                _prompt.outlineColor = native.outlineColor;
                _prompt.outlineWidth = native.outlineWidth;
            }
            else
            {
                _prompt.fontSize = 3f;
                _prompt.color = Color.white;
                _prompt.alignment = TextAlignmentOptions.Center;
            }
            _prompt.text = InputManager.Loot + " - Recover belongings";
            _prompt.enableWordWrapping = false;
            _prompt.gameObject.SetActive(false);
        }

        private static void DestroyMarker()
        {
            _prompt = null;
            if (_marker != null)
            {
                try { UnityEngine.Object.Destroy(_marker); }
                catch { }
            }
            _marker = null;
            foreach (Mesh mesh in MarkerOwnedMeshes)
            {
                try
                {
                    if (mesh != null) UnityEngine.Object.Destroy(mesh);
                }
                catch { }
            }
            MarkerOwnedMeshes.Clear();
            _markerUsesCharacterVisual = false;
            _markerVisibleRendererCount = 0;
            _markerMissingVisualPaths = 0;
        }

        private static IList<SlotRecord> GetArea(InventoryRecord record,
            SlotArea area)
        {
            switch (area)
            {
                case SlotArea.Stored: return record.Stored;
                case SlotArea.Equipment: return record.Equipment;
                case SlotArea.Transmog: return record.Transmog;
                default: throw new ArgumentOutOfRangeException(nameof(area));
            }
        }

        private static SlotRecord GetSlot(InventoryRecord record,
            SlotArea area, int index)
        {
            switch (area)
            {
                case SlotArea.Stored: return record.Stored[index];
                case SlotArea.Equipment: return record.Equipment[index];
                case SlotArea.Transmog: return record.Transmog[index];
                case SlotArea.Aura: return record.Aura;
                case SlotArea.Charm: return record.Charm;
                default: throw new ArgumentOutOfRangeException(nameof(area));
            }
        }

        private static void SetSlot(InventoryRecord record, SlotArea area,
            int index, SlotRecord value)
        {
            switch (area)
            {
                case SlotArea.Stored: record.Stored[index] = value; break;
                case SlotArea.Equipment: record.Equipment[index] = value; break;
                case SlotArea.Transmog: record.Transmog[index] = value; break;
                case SlotArea.Aura: record.Aura = value; break;
                case SlotArea.Charm: record.Charm = value; break;
                default: throw new ArgumentOutOfRangeException(nameof(area));
            }
        }

        private static List<int> CollectBagNumbers(InventoryRecord record)
        {
            var numbers = new List<int>();
            var seen = new HashSet<int>();
            CollectBagNumbers(record.Stored, numbers, seen);
            CollectBagNumbers(record.Equipment, numbers, seen);
            CollectBagNumbers(record.Transmog, numbers, seen);
            CollectBagNumber(record.Aura, numbers, seen);
            CollectBagNumber(record.Charm, numbers, seen);
            return numbers;
        }

        private static void CollectBagNumbers(IEnumerable<SlotRecord> slots,
            List<int> numbers, HashSet<int> seen)
        {
            foreach (SlotRecord slot in slots)
                CollectBagNumber(slot, numbers, seen);
        }

        private static void CollectBagNumber(SlotRecord slot,
            List<int> numbers, HashSet<int> seen)
        {
            if (slot == null || slot.IsEmpty || slot.Quantity < 100) return;
            Item item = null;
            try { item = Items.ItemFactory.GetItemById(slot.ItemId); }
            catch { }
            if (item == null) item = Items.ItemFactory.ResolveItem(slot.ItemName);
            if (!Items.BagSystem.IsBag(item)) return;
            if (!seen.Add(slot.Quantity))
                throw new InvalidDataException(
                    $"crafted bag identity bag{slot.Quantity} appears more than once");
            numbers.Add(slot.Quantity);
        }

        private static bool HasPossessions(InventoryRecord record)
        {
            return record.Gold > 0 || CountPossessionSlots(record) > 0;
        }

        private static int CountPossessionSlots(InventoryRecord record)
        {
            int count = 0;
            foreach (SlotRecord slot in record.Stored) if (!slot.IsEmpty) count++;
            foreach (SlotRecord slot in record.Equipment) if (!slot.IsEmpty) count++;
            foreach (SlotRecord slot in record.Transmog) if (!slot.IsEmpty) count++;
            if (!record.Aura.IsEmpty) count++;
            if (!record.Charm.IsEmpty) count++;
            return count;
        }

        private static SlotRecord EmptySlot()
        {
            return new SlotRecord { ItemId = "", ItemName = "", Quantity = 1 };
        }

        private static SlotRecord TestSlot(string id, string name, int quantity)
        {
            return new SlotRecord
            {
                ItemId = id,
                ItemName = name,
                Quantity = quantity
            };
        }

        private static bool Finite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static void Info(string message)
        {
            ChatHelper.Send("<color=#FFD54F>[Corpse Run]</color> " + message);
            SkillsPlugin.Log.LogInfo("[Corpse Run] " + message);
        }

        private static void Warn(string message)
        {
            ChatHelper.Send("<color=#EF5350>[Corpse Run]</color> " + message);
            SkillsPlugin.Log.LogWarning("[Corpse Run] " + message);
        }
    }
}
