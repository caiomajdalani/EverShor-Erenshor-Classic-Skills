using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace ErenshorSkills.Necromancer
{
    /// <summary>
    /// Creates the custom class, its spell ScriptableObjects, teaching scrolls,
    /// and class-compatible equipment without replacing any native database.
    /// Every object uses a deterministic ID so native save files can resolve it
    /// through ItemFactory and SpellDB after a restart.
    /// </summary>
    public static class NecromancerCatalog
    {
        public const string ClassName = "Necromancer";
        public const string ClassDescription =
            "The Necromancer commands death through afflictions, lifetaps, " +
            "fear, and a taunting skeletal servant.\n\nNecromancers heal with " +
            "lifetaps and sacrifice health to recover mana. Their damage takes " +
            "time to mature.\n\nA Necromancer should focus on Intelligence, " +
            "Charisma, and Endurance.";

        private static readonly Dictionary<string, Spell> SpellsByKey =
            new Dictionary<string, Spell>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<Spell, NecromancerSpellDefinition> Definitions =
            new Dictionary<Spell, NecromancerSpellDefinition>();
        private static readonly Dictionary<string, Item> ScrollsByKey =
            new Dictionary<string, Item>(StringComparer.OrdinalIgnoreCase);
        private static Dictionary<string, Sprite> _embeddedIcons;
        private static Sprite _classIcon;
        private static bool _spellsRegistered;
        private static bool _scrollsRegistered;
        private static bool _equipmentEligibilityApplied;
        private static string _petPrefabName = "unresolved";

        public static Class NecromancerClass { get; private set; }
        public static bool IsReady => NecromancerClass != null &&
            _spellsRegistered && _scrollsRegistered &&
            NecromancerAuras.IsReady && NecromancerGear.IsReady;
        public static int LearnableSpellCount => Definitions.Values.Count(d => !d.HiddenEffect);
        public static IReadOnlyCollection<Spell> AllSpells => Definitions.Keys;
        public static IReadOnlyCollection<Item> AllScrolls => ScrollsByKey.Values;
        internal static string PetPrefabName => _petPrefabName;
        internal static Sprite ClassIcon => LoadClassIcon();

        internal static void TryInitialize()
        {
            if (SkillsPlugin.CfgEnableNecromancer?.Value != true) return;
            try
            {
                EnsureClass();
                if (NecromancerClass == null) return;

                if (_spellsRegistered &&
                    NecromancerVfx.EnsureCurrentDatabaseBindings())
                    RefreshSpellEffectBindings();

                if (GameData.ItemDB?.ItemDB != null)
                    NecromancerSharedSkills.ReconcileSkillBooks();
                Items.SimPlayerCraftedItems
                    .RefreshCraftedEquipmentEligibility();

                if (GameData.SkillDatabase?.AscensionDatabase != null)
                    NecromancerAscensions.Register();

                if (!_spellsRegistered && GameData.SpellDatabase?.SpellDatabase != null &&
                    GameData.SpellDatabase.SpellDatabase.Length > 0 &&
                    GameData.EffectDB?.SpellEffects != null)
                    RegisterSpells(GameData.SpellDatabase);
                else if (_spellsRegistered)
                    EnsureSpellsInCurrentDatabase(GameData.SpellDatabase);

                if (_spellsRegistered && !_scrollsRegistered &&
                    Items.ItemFactory.Initialized)
                    RegisterScrolls();

                if (_scrollsRegistered)
                    NecromancerSpellLoot.InjectCurrentSceneLootTables();

                if (_spellsRegistered && _scrollsRegistered &&
                    Items.ItemFactory.Initialized)
                {
                    NecromancerAuras.TryRegister(GameData.SpellDatabase);
                    NecromancerGear.TryRegister();
                }

                if (!_equipmentEligibilityApplied && GameData.ItemDB?.ItemDB != null)
                    ApplyEquipmentEligibility();
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogError(
                    $"Necromancer initialization failed safely: {ex}");
            }
        }

        internal static void EnsureClass()
        {
            if (NecromancerClass != null || GameData.ClassDB == null) return;
            var value = ScriptableObject.CreateInstance<Class>();
            value.name = "CS - Necromancer";
            value.ClassName = ClassName;
            value.DisplayName = ClassName;
            value.MitigationBonus = 1f;
            value.AggroMod = 1.25f;
            // Native classes all spend the same 52-point class-benefit budget.
            // This distribution keeps the Necromancer caster-focused without
            // exceeding the Arcanist or Reaver's total starting power.
            value.StrBenefit = 3;
            value.EndBenefit = 9;
            value.DexBenefit = 4;
            value.AgiBenefit = 6;
            value.IntBenefit = 14;
            value.WisBenefit = 7;
            value.ChaBenefit = 9;
            value.hideFlags = HideFlags.HideAndDontSave;
            NecromancerClass = value;
            SkillsPlugin.Log.LogInfo(
                "Necromancer class registered (52-point native budget: INT 14, CHA 9, END 9; cloth caster profile).");
        }

        private static void RegisterSpells(SpellDB database)
        {
            Spell[] native = database.SpellDatabase;
            // Erenshor ships a fully animated, mostly bare skeleton specifically
            // for summoning. Prefer it over the armored Fallen Warrior while
            // retaining the older prefab as a compatibility fallback.
            Spell nativePet = native.FirstOrDefault(s => s != null &&
                string.Equals(s.SpellName, "Summon Skeleton",
                    StringComparison.OrdinalIgnoreCase));
            if (nativePet?.PetToSummon == null)
                nativePet = native.FirstOrDefault(s => s != null &&
                    (string.Equals(s.SpellName, "Summon Fallen Skeleton",
                         StringComparison.OrdinalIgnoreCase) ||
                     (s.name ?? "").IndexOf("Summon Fallen Warrior",
                         StringComparison.OrdinalIgnoreCase) >= 0));
            if (nativePet?.PetToSummon == null)
            {
                SkillsPlugin.Log.LogWarning(
                    "Necromancer spells are waiting for an Erenshor skeleton prefab.");
                return;
            }
            _petPrefabName = nativePet.PetToSummon.name ?? nativePet.SpellName;

            NecromancerVfx.RegisterEffects();
            if (!NecromancerVfx.IsReady) return;
            // Build every procedural clip during catalog initialization so
            // the first use of a spell never synthesizes audio mid-combat.
            NecromancerAudio.Prewarm();

            SpellsByKey.Clear();
            Definitions.Clear();
            foreach (NecromancerSpellDefinition definition in NecromancerSpellData.All)
            {
                Spell spell = CreateSpell(definition, native, nativePet);
                SpellsByKey.Add(definition.Key, spell);
                Definitions.Add(spell, definition);
            }

            foreach (NecromancerSpellDefinition definition in NecromancerSpellData.All)
            {
                if (string.IsNullOrEmpty(definition.AppliedEffectKey)) continue;
                SpellsByKey[definition.Key].StatusEffectToApply =
                    SpellsByKey[definition.AppliedEffectKey];
            }

            var merged = new List<Spell>(native.Length + SpellsByKey.Count);
            merged.AddRange(native.Where(s => s != null &&
                !IsNecromancerSpellId(s.Id)));
            merged.AddRange(SpellsByKey.Values);
            database.SpellDatabase = merged.ToArray();
            _spellsRegistered = true;
            SkillsPlugin.Log.LogInfo(
                $"Necromancer: registered {LearnableSpellCount} learnable spells " +
                $"and {SpellsByKey.Count - LearnableSpellCount} internal effects " +
                $"using pet prefab '{_petPrefabName}'.");
        }

        /// <summary>
        /// SpellDB is a MonoBehaviour, so every scene supplies its own instance
        /// with its own array. Registering once left later scenes without our
        /// spells, which made GetSpellByID return null for them — so
        /// CharSelectManager.LoadSpellsAndSkills silently dropped every saved
        /// Necromancer spell on a character switch, and the next save wrote the
        /// empty list back to disk. Re-attach the existing instances (never new
        /// ones, or KnownSpells would hold stale references) whenever the
        /// active database is missing them.
        /// </summary>
        private static void EnsureSpellsInCurrentDatabase(SpellDB database)
        {
            if (database?.SpellDatabase == null || SpellsByKey.Count == 0)
                return;
            var present = new HashSet<string>(
                database.SpellDatabase.Where(spell => spell != null)
                    .Select(spell => spell.Id ?? string.Empty),
                StringComparer.OrdinalIgnoreCase);
            List<Spell> missing = SpellsByKey.Values
                .Where(spell => spell != null &&
                    !present.Contains(spell.Id ?? string.Empty))
                .ToList();
            if (missing.Count == 0) return;

            var merged = new List<Spell>(database.SpellDatabase.Where(
                spell => spell != null));
            merged.AddRange(missing);
            database.SpellDatabase = merged.ToArray();
            SkillsPlugin.Log?.LogInfo(
                $"Necromancer: reattached {missing.Count} spell(s) to the " +
                "active scene spell database.");
        }

        private static Spell CreateSpell(NecromancerSpellDefinition definition,
            Spell[] native, Spell nativePet)
        {
            var spell = ScriptableObject.CreateInstance<Spell>();
            spell.name = "CS NEC - " + definition.Name;
            spell.Id = BuildSpellId(definition.Key);
            spell.UsedBy = definition.HiddenEffect
                ? new List<Class>()
                : new List<Class> { NecromancerClass };
            spell.Type = definition.Type;
            spell.Line = definition.Line;
            RegisterMagicSchool(spell, definition);
            spell.RequiredLevel = definition.HiddenEffect ? 0 : definition.Level;
            spell.SpellName = definition.Name;
            spell.SpellDesc = definition.Description;
            spell.SpellChargeTime = definition.CastSeconds * 60f;
            spell.SpellDurationInTicks = definition.DurationTicks;
            spell.ManaCost = definition.ManaCost;
            spell.Aggro = definition.Aggro;
            spell.TargetDamage = definition.TargetDamage;
            spell.Cooldown = definition.CooldownSeconds;
            spell.HP = definition.Hp;
            spell.AC = definition.Ac;
            spell.Str = definition.Str;
            spell.PR = definition.Pr;
            spell.VR = definition.Vr;
            spell.DamageShield = definition.DamageShield;
            spell.MovementSpeed = definition.MovementSpeed;
            spell.Lifetap = definition.Lifetap;
            spell.FearTarget = definition.Fear;
            spell.RootTarget = definition.Root;
            spell.SelfOnly = definition.SelfOnly;
            spell.BreakOnAnyAction = definition.BreakOnAnyAction;
            spell.GrantInvisibility = definition.GrantInvisibility;
            spell.CrowdControlSpell = definition.Fear || definition.Root;
            spell.MyDamageType = definition.DamageType;
            spell.ResistModifier = definition.ResistModifier;
            spell.SpellRange = definition.Range;
            // Hostile Necromancer spells must never damage players, while
            // self/pet/utility spells still need to be allowed to resolve on
            // player-owned Stats through SpellVessel's native target guard.
            // PBAE spells also target their caster internally; their area
            // collector, not that vessel target, selects nearby enemies.
            spell.CanHitPlayers = definition.Type == Spell.SpellType.Misc ||
                definition.Type == Spell.SpellType.Pet || definition.SelfOnly ||
                definition.Pbaoe;
            spell.NoResonate = definition.Kind != NecromancerSpellKind.Native ||
                definition.Type == Spell.SpellType.Pet;
            spell.SimUsable = !definition.HiddenEffect;
            spell.color = definition.VfxColor;
            spell.StatusEffectMessageOnPlayer = PlayerEffectMessage(definition);
            spell.StatusEffectMessageOnNPC = NpcEffectMessage(definition);
            // NecromancerAudio owns both channels. Leaving the native lists
            // empty prevents the five broad template families from being
            // layered under—and muddying—each spell's authored signature. The
            // base cast pipeline still calls PlayOneShot unconditionally, so a
            // one-sample silent clip avoids a Unity warning on every cast.
            // Erenshor's own charge and resolve clips, played by the native
            // cast pipeline. These used to be set to a one-sample silent clip
            // so the add-on could play its own AudioSource instead, which took
            // every Necromancer spell out of the game's audio path and is why
            // they sounded thin beside stock spells. Falling back to silence
            // only if a clip cannot be resolved keeps the old no-warning
            // behaviour for a database that lacks it.
            spell.ChargeVariations = new List<AudioClip>();
            spell.CompleteVariations = new List<AudioClip>();
            spell.ChargeSound =
                NecromancerAudio.NativeCharge(definition.Key) ??
                NecromancerAudio.NativeSilence;
            spell.CompleteSound =
                NecromancerAudio.NativeComplete(definition.Key) ??
                NecromancerAudio.NativeSilence;
            spell.SpellIcon = LoadIcon(definition, native);
            spell.SpellChargeFXIndex = NecromancerVfx.GetChargeIndex(definition.Key);
            spell.SpellResolveFXIndex = NecromancerVfx.GetResolveIndex(definition.Key);
            if (definition.Kind == NecromancerSpellKind.SummonPet)
                spell.PetToSummon = nativePet.PetToSummon;
            spell.hideFlags = HideFlags.HideAndDontSave;
            return spell;
        }

        private static Spell FindSoundTemplate(Spell[] native,
            NecromancerSpellDefinition definition)
        {
            string hint;
            switch (definition.VfxStyle)
            {
                case NecromancerVfxStyle.Bone: hint = "Summon Fallen"; break;
                case NecromancerVfxStyle.Fear: hint = "Fear"; break;
                case NecromancerVfxStyle.Disease: hint = "Poison"; break;
                case NecromancerVfxStyle.Fire: hint = "Burn"; break;
                default: hint = "Theft of Life"; break;
            }
            return native.FirstOrDefault(s => s != null &&
                       ((s.SpellName ?? "").IndexOf(hint,
                            StringComparison.OrdinalIgnoreCase) >= 0 ||
                        (s.name ?? "").IndexOf(hint,
                            StringComparison.OrdinalIgnoreCase) >= 0)) ??
                   native.FirstOrDefault(s => s != null &&
                       s.ChargeVariations != null && s.CompleteVariations != null);
        }

        private static Sprite LoadIcon(NecromancerSpellDefinition definition,
            Spell[] native)
        {
            if (_embeddedIcons == null)
                _embeddedIcons = EmbeddedResources.LoadAllIcons();
            string generatedName = "Necromancer Spell " + definition.Key;
            if (_embeddedIcons.TryGetValue(generatedName, out Sprite icon) && icon)
            {
                icon.name = generatedName;
                return icon;
            }

            SkillsPlugin.Log.LogWarning(
                $"Missing custom Necromancer spell icon '{generatedName}'. " +
                "Using a native fallback; catalog validation will fail until " +
                "the unique asset is restored.");

            string hint;
            switch (definition.IconTheme)
            {
                case "Bone Thrall": hint = "Summon Fallen Skeleton"; break;
                case "Fear": hint = "Fear"; break;
                case "Disease": hint = "Weak Poison"; break;
                case "Grave Fire": hint = "Burning Chains"; break;
                case "Shadow": hint = "Call of the Void"; break;
                default: hint = "Theft of Life"; break;
            }
            return native.FirstOrDefault(s => s != null && s.SpellIcon &&
                ((s.SpellName ?? "").IndexOf(hint,
                     StringComparison.OrdinalIgnoreCase) >= 0 ||
                 (s.name ?? "").IndexOf(hint,
                     StringComparison.OrdinalIgnoreCase) >= 0))?.SpellIcon ??
                native.FirstOrDefault(s => s != null && s.SpellIcon)?.SpellIcon;
        }

        private static Sprite LoadScrollIcon(
            NecromancerSpellDefinition definition, Sprite fallback)
        {
            if (_embeddedIcons == null)
                _embeddedIcons = EmbeddedResources.LoadAllIcons();
            string generatedName = "Necromancer Scroll " + definition.Key;
            if (_embeddedIcons.TryGetValue(generatedName, out Sprite icon) && icon)
            {
                icon.name = generatedName;
                return icon;
            }
            SkillsPlugin.Log.LogWarning(
                $"Missing custom Necromancer scroll icon '{generatedName}'. " +
                "Using its spell icon as a safe fallback; catalog validation " +
                "will fail until the unique asset is restored.");
            return fallback;
        }

        private static Sprite LoadClassIcon()
        {
            if (_classIcon) return _classIcon;
            if (_embeddedIcons == null)
                _embeddedIcons = EmbeddedResources.LoadAllIcons();
            const string generatedName = "Necromancer Class";
            if (_embeddedIcons.TryGetValue(generatedName, out Sprite icon) && icon)
            {
                icon.name = generatedName;
                _classIcon = icon;
            }
            return _classIcon;
        }

        private static string PlayerEffectMessage(NecromancerSpellDefinition d)
        {
            if (d.Kind == NecromancerSpellKind.PetBuff) return "feel your bones harden.";
            if (d.Kind == NecromancerSpellKind.Harmshield) return "are sealed from harm.";
            if (d.Kind == NecromancerSpellKind.FeignDeath) return "appear lifeless.";
            if (d.Fear) return "are overcome by terror.";
            if (d.TargetDamage > 0) return "are afflicted by " + d.Name + ".";
            return "are affected by " + d.Name + ".";
        }

        private static string NpcEffectMessage(NecromancerSpellDefinition d)
        {
            if (d.Kind == NecromancerSpellKind.PetBuff) return "is bound more tightly to death.";
            if (d.Fear) return "flees in terror.";
            if (d.TargetDamage > 0) return "is afflicted by " + d.Name + ".";
            return "is affected by " + d.Name + ".";
        }

        /// <summary>
        /// Spells whose behaviour lives in NecromancerRuntime are registered as
        /// SpellType.Misc, which MagicSkills would otherwise file under
        /// Abjuration regardless of what they do — Mend Bones would train buff
        /// duration and gain nothing from Alteration. Declare the real school
        /// for the kinds where the native type is misleading.
        /// </summary>
        private static void RegisterMagicSchool(Spell spell,
            NecromancerSpellDefinition definition)
        {
            if (spell == null || definition == null) return;
            Skills.MagicSkills.MagicSchool school;
            switch (definition.Kind)
            {
                case NecromancerSpellKind.PetHeal:
                    school = Skills.MagicSkills.MagicSchool.Alteration;
                    break;
                case NecromancerSpellKind.ReclaimPet:
                    school = Skills.MagicSkills.MagicSchool.Conjuration;
                    break;
                default:
                    // Wards, feigns and pet buffs are genuinely Abjuration, and
                    // damage/DoT/pet spells already carry a truthful SpellType.
                    return;
            }
            Skills.MagicSkills.RegisterSchoolOverride(spell.Id, school);
        }

        private static void RegisterScrolls()
        {
            ScrollsByKey.Clear();
            foreach (NecromancerSpellDefinition definition in NecromancerSpellData.All)
            {
                if (definition.HiddenEffect) continue;
                Spell spell = SpellsByKey[definition.Key];
                var item = ScriptableObject.CreateInstance<Item>();
                item.name = "Spell: " + definition.Name;
                item.ItemName = "Spell: " + definition.Name;
                item.Id = BuildScrollId(definition.Key);
                item.ItemLevel = definition.Level;
                item.ItemValue = ScrollPrice(definition.Level);
                item.ItemIcon = LoadScrollIcon(definition, spell.SpellIcon);
                item.RequiredSlot = Item.SlotType.General;
                item.Classes = new List<Class> { NecromancerClass };
                item.TeachSpell = spell;
                // ItemInfoWindow renders a teaching scroll's detail into the
                // ReqLvl label (required level, mana cost, spell type and
                // SpellDesc) but only clears the Lore label for spells flagged
                // SimsNeedHelpToLearn. Anything left in Lore therefore draws on
                // top of that block. Our SpellDesc is already definition
                // .Description and Usable already names the class, so this text
                // was pure duplication laid over the real tooltip.
                item.Lore = string.Empty;
                item.Stackable = false;
                item.Disposable = false;
                // Native quick-sell requires both restrictions clear and a
                // positive value; duplicate scrolls must remain disposable.
                item.NoTradeNoDestroy = false;
                item.PlayerCannotSell = false;
                item.SimPlayersCantGet = true;
                item.hideFlags = HideFlags.HideAndDontSave;
                NecromancerSpellLoot.ApplyScrollMetadata(definition.Key, item);
                ScrollsByKey.Add(definition.Key, item);
                Items.ItemFactory.RegisterExternalItem(item.ItemName, item);
            }
            _scrollsRegistered = true;
            SkillsPlugin.Log.LogInfo(
                $"Necromancer: registered {ScrollsByKey.Count} teaching scrolls.");
        }

        private static int ScrollPrice(int level)
        {
            return Mathf.Max(1, Mathf.RoundToInt(level * level * 0.75f + level * 2f));
        }

        private static void ApplyEquipmentEligibility()
        {
            if (GameData.ClassDB?.Arcanist == null ||
                NecromancerClass == null)
                return;
            int changed = 0;
            foreach (Item item in GameData.ItemDB.ItemDB)
            {
                bool alreadyEligible =
                    item?.Classes?.Any(IsNecromancerClass) == true;
                if (EnsureEquipmentEligibility(item) && !alreadyEligible)
                    changed++;
            }
            _equipmentEligibilityApplied = true;
            int consumables =
                Items.NativeConsumableCompatibility.AddClass(
                    NecromancerClass);
            SkillsPlugin.Log.LogInfo(
                $"Necromancer: enabled {changed} native Arcanist-compatible " +
                $"equipment items and {consumables} universal native consumables.");
        }

        /// <summary>
        /// Reconciles one item against the Necromancer's Arcanist-compatible
        /// equipment rules. This is safe to call during tooltip rendering,
        /// including after a live reload or another mod has rebuilt class
        /// lists.
        /// </summary>
        internal static bool EnsureEquipmentEligibility(Item item)
        {
            Class arcanist = GameData.ClassDB?.Arcanist;
            if (item?.Classes == null || arcanist == null ||
                NecromancerClass == null)
                return false;

            bool customItem = Items.ItemFactory.IsCustomItem(item);
            bool explicitlyAllowed =
                item.Classes.Any(IsNecromancerClass);
            item.Classes.RemoveAll(value => IsNecromancerClass(value) &&
                !ReferenceEquals(value, NecromancerClass));
            if (explicitlyAllowed &&
                !item.Classes.Contains(NecromancerClass))
                item.Classes.Add(NecromancerClass);

            // Necromancer has its own fully budgeted aura progression. Strip
            // stale inheritance only from native auras; addon auras explicitly
            // carrying Necromancer remain valid.
            if (item.RequiredSlot == Item.SlotType.Aura)
            {
                if (!customItem)
                    item.Classes.RemoveAll(IsNecromancerClass);
                // Unrestricted auras carry no class entries and stay usable
                // by every class; only class-restricted auras need the
                // explicit Necromancer entry.
                return item.Classes.Count == 0 ||
                    item.Classes.Contains(NecromancerClass);
            }
            if (item.Classes.Contains(NecromancerClass)) return true;

            // Unrestricted charms carry no class entries and stay universally
            // usable; adding the class would turn them into restricted gear.
            if (item.RequiredSlot == Item.SlotType.Charm &&
                item.Classes.Count == 0)
                return true;

            if (item.TeachSpell != null ||
                item.RequiredSlot == Item.SlotType.General ||
                !item.Classes.Contains(arcanist))
                return false;
            item.Classes.Add(NecromancerClass);
            return true;
        }

        public static bool IsNecromancerClass(Class value)
        {
            return value != null && (ReferenceEquals(value, NecromancerClass) ||
                string.Equals(value.ClassName, ClassName,
                    StringComparison.OrdinalIgnoreCase));
        }

        public static bool IsNecromancerPlayer()
        {
            return IsNecromancerClass(GameData.PlayerStats?.CharacterClass) ||
                string.Equals(GameData.CurrentCharacterSlot?.CharClass, ClassName,
                    StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Deterministic regression check for native aura/charm eligibility:
        /// auras and charms without any class entry stay unrestricted, must
        /// remain eligible for the Necromancer, and must not be turned into
        /// class-restricted gear by the reconciliation pass.
        /// </summary>
        internal static bool ValidateUnrestrictedAuraCharmAccess(
            out string detail)
        {
            Item[] database = GameData.ItemDB?.ItemDB ?? Array.Empty<Item>();
            List<Item> unrestricted = database.Where(item =>
                    item?.Classes != null && item.Classes.Count == 0 &&
                    item.TeachSpell == null &&
                    (item.RequiredSlot == Item.SlotType.Aura ||
                     item.RequiredSlot == Item.SlotType.Charm))
                .ToList();
            List<string> blocked = unrestricted
                .Where(item => !EnsureEquipmentEligibility(item))
                .Select(item => item.ItemName)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            List<string> reRestricted = unrestricted
                .Where(item => item.Classes.Count != 0)
                .Select(item => item.ItemName)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            detail = $"unrestricted aura/charm items={unrestricted.Count}, " +
                $"blocked=[" + string.Join(", ", blocked) + "], " +
                $"re-restricted=[" + string.Join(", ", reRestricted) + "]";
            return unrestricted.Count > 0 && blocked.Count == 0 &&
                reRestricted.Count == 0;
        }

        internal static bool TryGetDefinition(Spell spell,
            out NecromancerSpellDefinition definition)
        {
            if (spell != null && Definitions.TryGetValue(spell, out definition))
                return true;

            // Fresh characters can carry a Spell instance created in character
            // select across the world-scene transition. Resolve it by stable ID
            // so runtime behavior never depends on ScriptableObject identity.
            if (spell != null && IsNecromancerSpellId(spell.Id))
            {
                Spell canonical = SpellsByKey.Values.FirstOrDefault(candidate =>
                    candidate != null && string.Equals(candidate.Id, spell.Id,
                        StringComparison.OrdinalIgnoreCase));
                if (canonical != null &&
                    Definitions.TryGetValue(canonical, out definition))
                    return true;
            }

            definition = null;
            return false;
        }

        internal static bool EnsureRuntimeSpellBindings(Spell spell,
            out string error)
        {
            error = null;
            if (!TryGetDefinition(spell,
                    out NecromancerSpellDefinition definition))
            {
                error = "spell definition is unavailable";
                return false;
            }
            if (!NecromancerVfx.EnsureCurrentDatabaseBindings())
            {
                error = "spell effects are still loading";
                return false;
            }

            int charge = NecromancerVfx.GetChargeIndex(definition.Key);
            int resolve = NecromancerVfx.GetResolveIndex(definition.Key);
            List<GameObject> effects = GameData.EffectDB?.SpellEffects;
            if (effects == null || charge < 0 || charge >= effects.Count ||
                resolve < 0 || resolve >= effects.Count ||
                !effects[charge] || !effects[resolve])
            {
                error = "spell effect binding is invalid";
                return false;
            }

            spell.SpellChargeFXIndex = charge;
            spell.SpellResolveFXIndex = resolve;
            Spell canonical = SpellsByKey.TryGetValue(definition.Key,
                out Spell value) ? value : null;
            if (canonical != null)
            {
                canonical.SpellChargeFXIndex = charge;
                canonical.SpellResolveFXIndex = resolve;
            }
            return true;
        }

        private static void RefreshSpellEffectBindings()
        {
            foreach (var pair in Definitions)
            {
                pair.Key.SpellChargeFXIndex =
                    NecromancerVfx.GetChargeIndex(pair.Value.Key);
                pair.Key.SpellResolveFXIndex =
                    NecromancerVfx.GetResolveIndex(pair.Value.Key);
            }
        }

        public static Spell GetSpell(string key)
        {
            SpellsByKey.TryGetValue(key ?? "", out Spell value);
            return value;
        }

        public static Spell GetSpellByName(string name)
        {
            return Definitions.Keys.FirstOrDefault(s => s != null &&
                string.Equals(s.SpellName, name, StringComparison.OrdinalIgnoreCase));
        }

        public static Item GetScroll(string key)
        {
            ScrollsByKey.TryGetValue(key ?? "", out Item value);
            return value;
        }

        public static IEnumerable<Spell> GetLearnableSpells()
        {
            return Definitions.Where(pair => !pair.Value.HiddenEffect)
                .OrderBy(pair => pair.Value.Level)
                .ThenBy(pair => pair.Value.Name, StringComparer.OrdinalIgnoreCase)
                .Select(pair => pair.Key);
        }

        public static IEnumerable<Item> GetVendorScrolls()
        {
            return ScrollsByKey
                .Where(pair => !NecromancerSpellLoot.IsDropOnly(pair.Key))
                .Select(pair => new
                {
                    Item = pair.Value,
                    Definition = NecromancerSpellData.All.First(d =>
                        string.Equals(d.Key, pair.Key, StringComparison.OrdinalIgnoreCase))
                })
                .OrderBy(value => value.Definition.Level)
                .ThenBy(value => value.Definition.Name, StringComparer.OrdinalIgnoreCase)
                .Select(value => value.Item);
        }

        // Scrolls and auras carry the whole level range, but the same
        // ClassMerchantStock rule the Monk trainer uses guards this list so no
        // weapon or armor piece above level 10 can ever reach the shelf.
        public static IEnumerable<Item> GetVendorStock()
        {
            return GetVendorScrolls().Concat(NecromancerAuras.GetItemsByLevel())
                .Where(ClassMerchantStock.IsStockable)
                .OrderBy(item => item.ItemLevel)
                .ThenBy(item => item.RequiredSlot == Item.SlotType.Aura ? 0 : 1)
                .ThenBy(item => item.ItemName,
                    StringComparer.OrdinalIgnoreCase);
        }

        internal static int RestoreKnownSpellsAfterHotLoad()
        {
            if (!IsReady || GameData.CurrentCharacterSlot == null ||
                !IsNecromancerPlayer() || GameData.GM?.PlayerSpells?.KnownSpells == null)
                return 0;
            int restored = 0;
            foreach (string id in GameData.CurrentCharacterSlot.CharacterSpells ??
                new List<string>())
            {
                if (!IsNecromancerSpellId(id)) continue;
                Spell spell = Definitions.Keys.FirstOrDefault(candidate =>
                    string.Equals(candidate?.Id, id, StringComparison.OrdinalIgnoreCase));
                if (spell != null && !GameData.GM.PlayerSpells.KnownSpells.Contains(spell))
                {
                    GameData.GM.PlayerSpells.KnownSpells.Add(spell);
                    restored++;
                }
            }
            RestoreHotkeyRow(GameData.CurrentCharacterSlot.HKSpells,
                GameData.GM.HKManager?.FirstHotkeys);
            RestoreHotkeyRow(GameData.CurrentCharacterSlot.SecHKSpells,
                GameData.GM.HKManager?.SecondHotkeys);
            if (restored > 0)
                SkillsPlugin.Log.LogWarning(
                    $"Necromancer: restored {restored} known spell(s) after live loader enable.");
            return restored;
        }

        private static void RestoreHotkeyRow(string[] ids, IList<Hotkeys> hotkeys)
        {
            if (ids == null || hotkeys == null) return;
            int count = Mathf.Min(ids.Length, hotkeys.Count);
            for (int i = 0; i < count; i++)
            {
                string id = ids[i];
                if (!IsNecromancerSpellId(id) || hotkeys[i] == null) continue;
                Spell spell = Definitions.Keys.FirstOrDefault(candidate =>
                    string.Equals(candidate?.Id, id, StringComparison.OrdinalIgnoreCase));
                if (spell != null) hotkeys[i].AssignSpellFromBook(spell);
            }
        }

        internal static string BuildSpellId(string key) =>
            "CS_NECRO_SPELL_" + NormalizeKey(key);
        internal static string BuildScrollId(string key) =>
            "CS_NECRO_SCROLL_" + NormalizeKey(key);
        internal static bool IsNecromancerSpellId(string id) =>
            !string.IsNullOrEmpty(id) && id.StartsWith("CS_NECRO_SPELL_",
                StringComparison.OrdinalIgnoreCase);

        private static string NormalizeKey(string key)
        {
            return new string((key ?? "").ToUpperInvariant()
                .Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
        }

        internal static void ResetRuntimeReferences()
        {
            NecromancerRuntime.ResetCharacterState();
            NecromancerPetEquipment.ResetRuntime();
        }

        internal static bool ValidateCatalog(out string detail)
        {
            var errors = new List<string>();
            Spell[] databaseSpells = GameData.SpellDatabase?.SpellDatabase ??
                Array.Empty<Spell>();
            if (NecromancerClass == null) errors.Add("class missing");
            Sprite classIcon = LoadClassIcon();
            if (!classIcon) errors.Add("unique class icon missing");
            var uniqueVisuals = new HashSet<int>();
            if (classIcon) uniqueVisuals.Add(classIcon.GetInstanceID());
            if (LearnableSpellCount != NecromancerSpellData.ExpectedLearnableSpellCount)
                errors.Add($"learnable count {LearnableSpellCount}");
            if (ScrollsByKey.Count != NecromancerSpellData.ExpectedLearnableSpellCount)
                errors.Add($"scroll count {ScrollsByKey.Count}");
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in Definitions)
            {
                Spell spell = pair.Key;
                NecromancerSpellDefinition definition = pair.Value;
                if (spell == null || !ids.Add(spell?.Id ?? ""))
                    errors.Add($"duplicate/null spell {definition.Key}");
                if (!spell.SpellIcon) errors.Add($"icon missing {definition.Name}");
                else if (!uniqueVisuals.Add(spell.SpellIcon.GetInstanceID()))
                    errors.Add($"duplicate spell icon {definition.Name}");
                if (!NecromancerVfx.ValidateSpellVfx(spell, out string vfxError))
                    errors.Add($"VFX {definition.Name}: {vfxError}");
                if (!definition.HiddenEffect &&
                    (spell.UsedBy == null || !spell.UsedBy.Contains(NecromancerClass)))
                    errors.Add($"class restriction {definition.Name}");
                if (!definition.HiddenEffect && databaseSpells.Any(other =>
                        other != null && !ReferenceEquals(other, spell) &&
                        string.Equals(other.SpellName, spell.SpellName,
                            StringComparison.OrdinalIgnoreCase)))
                    errors.Add($"game-wide spell name collision {definition.Name}");
                if (definition.Kind == NecromancerSpellKind.SummonPet &&
                    (spell.PetToSummon == null ||
                     !string.Equals(spell.PetToSummon.name, _petPrefabName,
                         StringComparison.Ordinal)))
                    errors.Add($"pet prefab {definition.Name}");
                if (!definition.HiddenEffect)
                {
                    Item scroll = GetScroll(definition.Key);
                    if (scroll == null || !scroll.ItemIcon)
                        errors.Add($"scroll icon missing {definition.Name}");
                    else
                    {
                        if (!uniqueVisuals.Add(scroll.ItemIcon.GetInstanceID()))
                            errors.Add($"duplicate spell/scroll icon " +
                                definition.Name);
                        if (ReferenceEquals(scroll.ItemIcon, spell.SpellIcon))
                            errors.Add($"scroll reuses spell icon " +
                                definition.Name);
                    }
                }
            }
            bool auraCatalog = NecromancerAuras.ValidateCatalog(
                out string auraDetail);
            if (!auraCatalog) errors.Add("Auras: " + auraDetail);
            foreach (Spell aura in NecromancerAuras.AllSpells)
            {
                if (aura?.SpellIcon == null)
                    errors.Add("aura effect icon missing " +
                        (aura?.SpellName ?? "<null>"));
                else if (!uniqueVisuals.Add(aura.SpellIcon.GetInstanceID()))
                    errors.Add("aura effect icon collision " + aura.SpellName);
            }
            foreach (Item aura in NecromancerAuras.AllItems)
            {
                if (aura?.ItemIcon == null)
                    errors.Add("aura gem icon missing " +
                        (aura?.ItemName ?? "<null>"));
                else if (!uniqueVisuals.Add(aura.ItemIcon.GetInstanceID()))
                    errors.Add("aura gem icon collision " + aura.ItemName);
            }
            bool gearCatalog = NecromancerGear.ValidateCatalog(
                out string gearDetail);
            if (!gearCatalog) errors.Add("Gear: " + gearDetail);
            foreach (Item gear in NecromancerGear.AllItems)
            {
                if (gear?.ItemIcon == null)
                    errors.Add("gear icon missing " +
                        (gear?.ItemName ?? "<null>"));
                else if (!uniqueVisuals.Add(gear.ItemIcon.GetInstanceID()))
                    errors.Add("gear icon collision " + gear.ItemName);
            }
            if (_petPrefabName.IndexOf("Summoned Skeleton",
                StringComparison.OrdinalIgnoreCase) < 0)
                errors.Add($"barer skeleton not selected ({_petPrefabName})");
            bool audio = NecromancerAudio.Validate(out string audioDetail);
            if (!audio) errors.Add("Audio: " + audioDetail);
            if (!NecromancerAscensions.Validate(out string ascensionDetail))
                errors.Add("Ascensions: " + ascensionDetail);
            if (!ValidateUnrestrictedAuraCharmAccess(out string auraCharmDetail))
                errors.Add("Unrestricted auras/charms: " + auraCharmDetail);
            detail = errors.Count == 0
                ? $"{LearnableSpellCount} spells, {ScrollsByKey.Count} scrolls, " +
                  $"{NecromancerAuras.AllItems.Count} auras, " +
                  $"{NecromancerGear.AllItems.Count} gear/mold items, " +
                  $"{uniqueVisuals.Count} unique spell/scroll/aura/gear/class icons " +
                  $"and all VFX present; pet={_petPrefabName}; " +
                  $"{audioDetail}; {auraDetail}; {gearDetail}; " +
                  $"{ascensionDetail}; {auraCharmDetail}"
                : string.Join("; ", errors);
            return errors.Count == 0;
        }

        internal static bool ValidateBalance(out string detail)
        {
            var errors = new List<string>();
            List<NecromancerSpellDefinition> learnable = NecromancerSpellData.All
                .Where(value => !value.HiddenEffect).ToList();
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int previousLevel = 0;
            foreach (NecromancerSpellDefinition value in learnable)
            {
                if (!keys.Add(value.Key) || !names.Add(value.Name))
                    errors.Add("duplicate spell key/name " + value.Name);
                if (NecromancerSpellData.DisallowedRankSuffixes.Any(suffix =>
                        value.Name.EndsWith(suffix,
                            StringComparison.OrdinalIgnoreCase)))
                    errors.Add("ranked spell name " + value.Name);
                if (value.Level < 1 || value.Level > 35 ||
                    value.Level < previousLevel)
                    errors.Add("invalid progression level " + value.Name);
                previousLevel = value.Level;
                if (value.ManaCost < 0 || value.ManaCost > 2000 ||
                    value.CastSeconds < 0f || value.CastSeconds > 5f ||
                    value.CooldownSeconds < 0f || value.CooldownSeconds > 180f)
                    errors.Add("resource/timing envelope " + value.Name);
                if (value.Type == Spell.SpellType.Damage &&
                    (value.TargetDamage <= 0 || value.ManaCost <= 0 ||
                     (float)value.TargetDamage / value.ManaCost > 2.5f))
                    errors.Add("direct-damage efficiency " + value.Name);
                int offensiveValue = GetDefinitionOffensiveValue(value);
                if (offensiveValue > 0 && value.ManaCost > 0 &&
                    (float)offensiveValue / value.ManaCost > 12f)
                    errors.Add("total-damage efficiency " + value.Name);
                if (value.Kind == NecromancerSpellKind.HealthToMana &&
                    (value.Hp >= 0 || value.CustomAmount <= 0 ||
                     value.DurationTicks <= 0 ||
                     value.Lifetap ||
                     (float)value.CustomAmount / Mathf.Abs(value.Hp) > 5f))
                    errors.Add("health-to-mana efficiency " + value.Name);
                if (value.Lifetap &&
                    (value.Kind != NecromancerSpellKind.Native ||
                     value.Type != Spell.SpellType.Damage ||
                     value.TargetDamage <= 0))
                    errors.Add("invalid lifetap contract " + value.Name);
            }
            int[] petTiers = learnable
                .Where(value => value.Kind == NecromancerSpellKind.SummonPet)
                .Select(value => value.PetTier).OrderBy(value => value).ToArray();
            if (!petTiers.SequenceEqual(new[] { 1, 2, 3, 4, 5 }))
                errors.Add("pet tier progression");
            if (!NecromancerPets.ValidateBalanceCurve(out string petBalanceDetail))
                errors.Add("pet curve: " + petBalanceDetail);

            // Compare every playable level to the median strongest raw
            // single-target spell available to Erenshor's three closest
            // caster/attrition peers. A 50%-150% band deliberately leaves room
            // for the Necromancer's always-on pet, lifetaps, and delayed damage
            // without allowing a dead progression tier or an outlier nuke.
            Class[] benchmarkClasses = { GameData.ClassDB?.Arcanist,
                GameData.ClassDB?.Druid, GameData.ClassDB?.Reaver };
            Spell[] nativeSpells = GameData.SpellDatabase?.SpellDatabase ??
                Array.Empty<Spell>();
            float minimumRatio = float.MaxValue;
            float maximumRatio = float.MinValue;
            int comparedLevels = 0;
            for (int level = 1; level <= 35; level++)
            {
                int necromancerValue = learnable
                    .Where(value => value.Level <= level)
                    .Select(GetDefinitionOffensiveValue)
                    .DefaultIfEmpty(0).Max();
                List<int> peerValues = benchmarkClasses
                    .Where(value => value != null)
                    .Select(value => nativeSpells.Where(spell => spell != null &&
                            spell.RequiredLevel >= 1 &&
                            spell.RequiredLevel <= level &&
                            spell.UsedBy?.Contains(value) == true)
                        .Select(GetSpellOffensiveValue)
                        .DefaultIfEmpty(0).Max())
                    .Where(value => value > 0).OrderBy(value => value).ToList();
                if (peerValues.Count == 0 || necromancerValue <= 0)
                {
                    errors.Add($"level {level} has no offensive benchmark");
                    continue;
                }
                float median = peerValues.Count % 2 == 1
                    ? peerValues[peerValues.Count / 2]
                    : (peerValues[peerValues.Count / 2 - 1] +
                       peerValues[peerValues.Count / 2]) * 0.5f;
                float ratio = necromancerValue / median;
                minimumRatio = Mathf.Min(minimumRatio, ratio);
                maximumRatio = Mathf.Max(maximumRatio, ratio);
                comparedLevels++;
                if (ratio < 0.50f || ratio > 1.50f)
                    errors.Add($"level {level} offense {necromancerValue} vs " +
                        $"peer median {median:F0} ({ratio:P0})");
            }

            int reaverCount = GameData.SpellDatabase?.SpellDatabase?.Count(spell =>
                spell != null && spell.RequiredLevel > 0 && spell.UsedBy != null &&
                GameData.ClassDB?.Reaver != null &&
                spell.UsedBy.Contains(GameData.ClassDB.Reaver)) ?? 0;
            if (reaverCount > 0 && learnable.Count != reaverCount)
                errors.Add($"spell count differs from Reaver ({reaverCount})");

            Class[] native = { GameData.ClassDB?.Arcanist, GameData.ClassDB?.Paladin,
                GameData.ClassDB?.Duelist, GameData.ClassDB?.Druid,
                GameData.ClassDB?.Stormcaller, GameData.ClassDB?.Reaver };
            List<int> benefitTotals = native.Where(value => value != null)
                .Select(value => value.StrBenefit + value.EndBenefit +
                    value.DexBenefit + value.AgiBenefit + value.IntBenefit +
                    value.WisBenefit + value.ChaBenefit).ToList();
            int necroTotal = NecromancerClass == null ? 0 :
                NecromancerClass.StrBenefit + NecromancerClass.EndBenefit +
                NecromancerClass.DexBenefit + NecromancerClass.AgiBenefit +
                NecromancerClass.IntBenefit + NecromancerClass.WisBenefit +
                NecromancerClass.ChaBenefit;
            if (benefitTotals.Count > 0 && (necroTotal < benefitTotals.Min() ||
                necroTotal > benefitTotals.Max()))
                errors.Add($"class benefit total outside native range " +
                    $"({necroTotal}; native {benefitTotals.Min()}-{benefitTotals.Max()})");

            bool ascensions = NecromancerAscensions.Validate(
                out string ascensionBalanceDetail);
            if (!ascensions) errors.Add("Ascension power set: " +
                ascensionBalanceDetail);

            detail = errors.Count == 0
                ? $"{learnable.Count} spells (Reaver={reaverCount}); all " +
                  $"{comparedLevels}/35 level checkpoints remain " +
                  $"{minimumRatio:P0}-{maximumRatio:P0} of the native peer median; " +
                  $"{petBalanceDetail}; class-benefit total={necroTotal}; " +
                  $"{ascensionBalanceDetail}"
                : string.Join("; ", errors);
            return errors.Count == 0;
        }

        private static int GetDefinitionOffensiveValue(
            NecromancerSpellDefinition value)
        {
            if (value == null) return 0;
            int output;
            switch (value.Type)
            {
                case Spell.SpellType.StatusEffect:
                    output = value.TargetDamage *
                        Mathf.Max(1, value.DurationTicks);
                    break;
                case Spell.SpellType.Damage:
                case Spell.SpellType.AE:
                case Spell.SpellType.PBAE:
                    output = value.TargetDamage;
                    break;
                default:
                    output = 0;
                    break;
            }
            if (!string.IsNullOrEmpty(value.AppliedEffectKey))
            {
                NecromancerSpellDefinition applied = NecromancerSpellData.All
                    .FirstOrDefault(candidate => string.Equals(candidate.Key,
                        value.AppliedEffectKey,
                        StringComparison.OrdinalIgnoreCase));
                if (applied != null && !ReferenceEquals(applied, value))
                    output += GetDefinitionOffensiveValue(applied);
            }
            return output;
        }

        private static int GetSpellOffensiveValue(Spell spell)
        {
            if (spell == null || spell.TargetDamage <= 0) return 0;
            switch (spell.Type)
            {
                case Spell.SpellType.StatusEffect:
                    return spell.TargetDamage *
                        Mathf.Max(1, spell.SpellDurationInTicks);
                case Spell.SpellType.Damage:
                case Spell.SpellType.AE:
                case Spell.SpellType.PBAE:
                    return spell.TargetDamage;
                default:
                    return 0;
            }
        }
    }
}
