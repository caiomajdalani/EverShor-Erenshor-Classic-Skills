using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ErenshorSkills.Monk
{
    /// <summary>
    /// Additive Monk class catalog. All custom objects use deterministic IDs so
    /// Erenshor's native character, hotbar, equipment, and item saves can resolve
    /// them after a restart or a Lunaris live enable.
    /// </summary>
    public static class MonkCatalog
    {
        public const string ClassName = "Monk";
        public const string ClassDescription =
            "The Monk is a disciplined close-range fighter who uses handwraps, " +
            "paired one-handed blunt weapons, punches, kicks, and precise " +
            "defensive timing.\n\nMonks rely on " +
            "avoidance, Mend, and Feign Death while channeling mana through " +
            "their martial spells. Positioning, resource management, and spell " +
            "timing are vital.\n\nA Monk should focus on Strength, Dexterity, " +
            "Agility, Endurance, and Wisdom.";

        private static readonly Dictionary<string, Skill> SkillsByKey =
            new Dictionary<string, Skill>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<Skill, MonkTechniqueDefinition> Definitions =
            new Dictionary<Skill, MonkTechniqueDefinition>();
        private static readonly Dictionary<string, Spell> SpellsByKey =
            new Dictionary<string, Spell>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<Spell, MonkTechniqueDefinition>
            SpellDefinitions =
                new Dictionary<Spell, MonkTechniqueDefinition>();
        private static readonly Dictionary<string, Item> TomesByKey =
            new Dictionary<string, Item>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Spell> EffectsByKey =
            new Dictionary<string, Spell>(StringComparer.OrdinalIgnoreCase);
        private static Dictionary<string, Sprite> _embeddedIcons;
        private static Sprite _classIcon;
        private static bool _skillsRegistered;
        private static bool _spellsRegistered;
        private static bool _tomesRegistered;
        private static bool _effectsRegistered;
        private static bool _equipmentEligibilityApplied;
        private const int ExpectedEffectCount = 13;

        public static Class MonkClass { get; private set; }
        public static bool IsReady => MonkClass != null && _skillsRegistered &&
            _spellsRegistered && _tomesRegistered && _effectsRegistered &&
            MonkAuras.IsReady && MonkGear.IsReady;
        public static IReadOnlyCollection<Skill> AllSkills => Definitions.Keys;
        public static IReadOnlyCollection<Spell> AllSpells =>
            SpellDefinitions.Keys;
        public static IReadOnlyCollection<Item> AllTomes => TomesByKey.Values;
        internal static Sprite ClassIcon => LoadClassIcon();

        internal static void TryInitialize()
        {
            if (SkillsPlugin.CfgEnableMonk?.Value != true) return;
            try
            {
                EnsureClass();
                if (MonkClass == null) return;

                if (!_skillsRegistered &&
                    GameData.SkillDatabase?.SkillDatabase != null)
                    RegisterSkills(GameData.SkillDatabase);
                else if (_skillsRegistered)
                    EnsureSkillsInCurrentDatabase(GameData.SkillDatabase);

                if ((!_effectsRegistered || !_spellsRegistered) &&
                    GameData.SpellDatabase?.SpellDatabase != null)
                    RegisterSpellsAndUtilityEffects(GameData.SpellDatabase);
                else if (_spellsRegistered && _effectsRegistered)
                    EnsureSpellsInCurrentDatabase(GameData.SpellDatabase);

                if (_spellsRegistered && _effectsRegistered)
                    ApplySilentSpellEffects();

                if (_skillsRegistered && !_tomesRegistered &&
                    Items.ItemFactory.Initialized)
                    RegisterTomes();

                if (GameData.ItemDB?.ItemDB != null)
                    MonkSharedSkills.ReconcileSkillBooks();

                if (GameData.SkillDatabase?.AscensionDatabase != null)
                    MonkAscensions.Register();

                if (_effectsRegistered && _tomesRegistered &&
                    Items.ItemFactory.Initialized)
                {
                    MonkAuras.TryRegister(GameData.SpellDatabase);
                    MonkGear.TryRegister();
                    MonkHandwrapLoot.InjectCurrentSceneLootTables();
                }

                if (!_equipmentEligibilityApplied &&
                    GameData.ItemDB?.ItemDB != null)
                    ApplyEquipmentEligibility();
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log?.LogError(
                    $"Monk initialization failed safely: {ex}");
            }
        }

        internal static void EnsureClass()
        {
            if (MonkClass != null || GameData.ClassDB == null) return;
            var value = ScriptableObject.CreateInstance<Class>();
            value.name = "CS - Monk";
            value.ClassName = ClassName;
            value.DisplayName = ClassName;
            value.MitigationBonus = 1f;
            value.AggroMod = 0.85f;
            // Every native class spends exactly 52 benefit points.
            value.StrBenefit = 11;
            value.EndBenefit = 8;
            value.DexBenefit = 10;
            value.AgiBenefit = 12;
            value.IntBenefit = 3;
            value.WisBenefit = 5;
            value.ChaBenefit = 3;
            value.hideFlags = HideFlags.HideAndDontSave;
            MonkClass = value;
            SkillsPlugin.Log?.LogInfo(
                "Monk class registered (52-point native budget: STR 11, AGI 12, DEX 10; light martial profile). ");
        }

        private static void RegisterSkills(SkillDB database)
        {
            SkillsByKey.Clear();
            Definitions.Clear();
            foreach (MonkTechniqueDefinition definition in MonkSkillData.All)
            {
                Skill skill = CreateSkill(definition);
                SkillsByKey.Add(definition.Key, skill);
                Definitions.Add(skill, definition);
            }

            var merged = new List<Skill>(database.SkillDatabase.Length +
                SkillsByKey.Count);
            merged.AddRange(database.SkillDatabase.Where(skill => skill != null &&
                !IsMonkSkillId(skill.Id)));
            merged.AddRange(SkillsByKey.Values);
            database.SkillDatabase = merged.ToArray();
            _skillsRegistered = true;
            SkillsPlugin.Log?.LogInfo(
                $"Monk: registered {SkillsByKey.Count} custom techniques.");
        }

        /// <summary>
        /// SkillDB and SpellDB are MonoBehaviours, so each scene brings its own
        /// arrays. Registering once left later scenes without the Monk kit,
        /// which made GetSkillByID/GetSpellByID return null on a character
        /// switch — CharSelectManager then dropped every saved technique and
        /// the next save persisted the empty list. Reattach the existing
        /// instances so KnownSkills and KnownSpells keep matching references.
        /// </summary>
        private static void EnsureSkillsInCurrentDatabase(SkillDB database)
        {
            if (database?.SkillDatabase == null || SkillsByKey.Count == 0)
                return;
            var present = new HashSet<string>(
                database.SkillDatabase.Where(skill => skill != null)
                    .Select(skill => skill.Id ?? string.Empty),
                StringComparer.OrdinalIgnoreCase);
            List<Skill> missing = SkillsByKey.Values
                .Where(skill => skill != null &&
                    !present.Contains(skill.Id ?? string.Empty))
                .ToList();
            if (missing.Count == 0) return;

            var merged = new List<Skill>(database.SkillDatabase.Where(
                skill => skill != null));
            merged.AddRange(missing);
            database.SkillDatabase = merged.ToArray();
            SkillsPlugin.Log?.LogInfo(
                $"Monk: reattached {missing.Count} skill(s) to the active " +
                "scene skill database.");
        }

        private static void EnsureSpellsInCurrentDatabase(SpellDB database)
        {
            if (database?.SpellDatabase == null) return;
            var present = new HashSet<string>(
                database.SpellDatabase.Where(spell => spell != null)
                    .Select(spell => spell.Id ?? string.Empty),
                StringComparer.OrdinalIgnoreCase);
            List<Spell> missing = EffectsByKey.Values.Concat(SpellsByKey.Values)
                .Where(spell => spell != null &&
                    !present.Contains(spell.Id ?? string.Empty))
                .ToList();
            if (missing.Count == 0) return;

            var merged = new List<Spell>(database.SpellDatabase.Where(
                spell => spell != null));
            merged.AddRange(missing);
            database.SpellDatabase = merged.ToArray();
            SkillsPlugin.Log?.LogInfo(
                $"Monk: reattached {missing.Count} spell(s) to the active " +
                "scene spell database.");
        }

        private static GameObject _silentEffectPrefab;

        internal static GameObject SilentEffectPrefab => _silentEffectPrefab;

        /// <summary>
        /// Monk technique spells never assigned effect indices, so both
        /// defaulted to 0 and Erenshor instantiated whatever sits first in the
        /// effect database — the rainbow burst seen on Mend and on struck
        /// enemies. Worse, the Beneficial resolve branch attaches no destroy
        /// timer and sets no stop action, so that effect stayed on the ground
        /// forever. Monk techniques draw their own VFX through MonkVfx, so
        /// point both indices at a do-nothing entry instead.
        /// </summary>
        private static int EnsureSilentSpellEffectIndex()
        {
            List<GameObject> effects = GameData.EffectDB?.SpellEffects;
            if (effects == null) return -1;

            if (!_silentEffectPrefab)
            {
                var root = new GameObject("CS Monk Silent Technique Effect");
                root.hideFlags = HideFlags.HideAndDontSave;
                root.transform.position = new Vector3(0f, -10000f, 0f);
                UnityEngine.Object.DontDestroyOnLoad(root);

                ParticleSystem system = root.AddComponent<ParticleSystem>();
                ParticleSystem.MainModule main = system.main;
                main.duration = 0.1f;
                main.loop = false;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.01f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.001f);
                main.maxParticles = 1;
                main.stopAction = ParticleSystemStopAction.None;
                main.cullingMode = ParticleSystemCullingMode.Automatic;

                ParticleSystem.EmissionModule emission = system.emission;
                emission.enabled = false;
                emission.rateOverTime = 0f;
                emission.rateOverDistance = 0f;
                emission.SetBursts(Array.Empty<ParticleSystem.Burst>());

                ParticleSystemRenderer renderer =
                    system.GetComponent<ParticleSystemRenderer>();
                if (renderer != null) renderer.enabled = false;

                root.AddComponent<MonkSilentEffect>();
                system.Stop(true,
                    ParticleSystemStopBehavior.StopEmittingAndClear);
                _silentEffectPrefab = root;
            }

            // SpellEffectDB is scene-local, so rebind on every scene.
            int index = effects.IndexOf(_silentEffectPrefab);
            if (index < 0)
            {
                index = effects.Count;
                effects.Add(_silentEffectPrefab);
            }
            return index;
        }

        private static void ApplySilentSpellEffects()
        {
            int index = EnsureSilentSpellEffectIndex();
            if (index < 0) return;
            foreach (Spell spell in
                EffectsByKey.Values.Concat(SpellsByKey.Values))
            {
                if (spell == null) continue;
                spell.SpellChargeFXIndex = index;
                spell.SpellResolveFXIndex = index;
            }
        }

        private static Skill CreateSkill(MonkTechniqueDefinition definition)
        {
            var skill = ScriptableObject.CreateInstance<Skill>();
            skill.name = "CS MONK - " + definition.Name;
            skill.Id = BuildSkillId(definition.Key);
            skill.SkillName = definition.Name;
            // Offensive Monk techniques use the authored full-body driver.
            // Leaving the native trigger populated layered a second animation
            // below it, producing a visible snap when that native state ended.
            // The driver still provides its own native fallback if authored
            // motion cannot be retargeted.
            skill.SkillAnimName = definition.IsOffensive
                ? string.Empty : definition.Animation;
            skill.SkillIcon = LoadIcon("Monk Skill " + definition.Key);
            skill.Cooldown = definition.Cooldown;
            skill.SkillDesc = definition.Description;
            // Custom-class learning is bridged through Duelist only during the
            // real book-use call. The live player class is restored afterward.
            skill.DuelistRequiredLevel = definition.Level;
            skill.PaladinRequiredLevel = 0;
            skill.ArcanistRequiredLevel = 0;
            skill.DruidRequiredLevel = 0;
            skill.StormcallerRequiredLevel = 0;
            skill.ReaverRequiredLevel = 0;
            skill.Interrupt = definition.Interrupt;
            skill.TypeOfSkill = definition.Kind == MonkTechniqueKind.Passive
                ? Skill.SkillType.Innate
                : definition.Kind == MonkTechniqueKind.StoneThrow
                    ? Skill.SkillType.Ranged
                    : definition.IsAttack ? Skill.SkillType.Attack
                    : Skill.SkillType.Utility;
            skill.AffectPlayer = !definition.IsOffensive;
            skill.AffectTarget = definition.IsOffensive;
            skill.PlayerUses = "You use " + definition.Name + " on";
            skill.NPCUses = "uses " + definition.Name + " on";
            skill.SkillRange = definition.Kind == MonkTechniqueKind.StoneThrow
                ? 18f : 0f;
            skill.SkillPower = definition.Power;
            skill.PercentDmg = 0f;
            skill.DmgType = GameData.DamageType.Physical;
            skill.ScaleOffWeapon = false;
            skill.AutomateAttack = definition.IsOffensive;
            skill.AESkill = definition.AreaEffect;
            skill.SimPlayersAutolearn = false;
            skill.hideFlags = HideFlags.HideAndDontSave;
            return skill;
        }

        private static void RegisterSpellsAndUtilityEffects(SpellDB database)
        {
            EffectsByKey.Clear();
            SpellsByKey.Clear();
            SpellDefinitions.Clear();
            CreateUtilityEffect("feign_death", "Feign Death", 4,
                (Spell.SpellLine)92);
            CreateUtilityEffect("stone_stance", "Stone Stance", 4,
                (Spell.SpellLine)93);
            CreateUtilityEffect("hundred_fists", "Hundred Fists", 7,
                (Spell.SpellLine)94);
            CreateUtilityEffect("centered_breath", "Centered Breath", 3,
                (Spell.SpellLine)95);
            CreateUtilityEffect("countering_palm", "Countering Palm", 3,
                (Spell.SpellLine)96);
            CreateUtilityEffect("breath_of_harmony", "Breath of Harmony", 5,
                (Spell.SpellLine)97);
            Spell silentStep = CreateUtilityEffect("silent_step",
                "Silent Step", 4, (Spell.SpellLine)98);
            silentStep.MovementSpeed = 0.5f;
            silentStep.GrantInvisibility = true;
            silentStep.BreakOnAnyAction = true;
            CreateUtilityEffect("reed_in_the_wind", "Reed in the Wind", 4,
                (Spell.SpellLine)99);
            CreateUtilityEffect("empty_body", "Empty Body", 4,
                (Spell.SpellLine)100);
            CreateUtilityEffect("five_point_focus", "Five-Point Focus", 4,
                (Spell.SpellLine)101);

            Spell crescent = CreateDetrimentalEffect("crescent_heel",
                "Crescent Heel", 2, Spell.SpellLine.Attack_Speed_Debuff);
            crescent.MovementSpeed = -0.5f;
            crescent.Haste = -15f;
            Spell pressure = CreateDetrimentalEffect("pressure_point",
                "Pressure Point", 1, Spell.SpellLine.Stun);
            pressure.StunTarget = true;
            pressure.CrowdControlSpell = true;
            Spell risingDragon = CreateDetrimentalEffect("dragon_punch",
                "Rising Dragon Fist", 1, Spell.SpellLine.Stun);
            risingDragon.StunTarget = true;
            risingDragon.CrowdControlSpell = true;

            foreach (MonkTechniqueDefinition definition in MonkSkillData.All)
            {
                if (string.IsNullOrEmpty(definition.EffectKey)) continue;
                Skill skill = GetSkill(definition.Key);
                if (skill != null)
                    skill.EffectToApply = GetEffect(definition.EffectKey);
            }

            foreach (MonkTechniqueDefinition definition in MonkSkillData.All)
            {
                // Passives are innate skills, not castable spells. Giving them
                // a spellbook entry put Open Hand Discipline in the spell list
                // with a mana cost and a cast, which is not what a passive is.
                // Their Skill is registered in RegisterSkills as Innate and the
                // tome teaches that instead.
                if (definition.Kind == MonkTechniqueKind.Passive) continue;
                Spell spell = CreateTechniqueSpell(definition);
                SpellsByKey.Add(definition.Key, spell);
                SpellDefinitions.Add(spell, definition);
            }

            var merged = new List<Spell>(database.SpellDatabase.Length +
                EffectsByKey.Count + SpellsByKey.Count);
            merged.AddRange(database.SpellDatabase.Where(spell => spell != null &&
                !IsMonkEffectId(spell.Id) && !IsMonkSpellId(spell.Id)));
            merged.AddRange(EffectsByKey.Values);
            merged.AddRange(SpellsByKey.Values);
            database.SpellDatabase = merged.ToArray();
            _effectsRegistered = true;
            _spellsRegistered = true;
            SkillsPlugin.Log?.LogInfo(
                $"Monk: registered {SpellsByKey.Count} spellbook techniques " +
                $"plus {EffectsByKey.Count} internal status effects.");
        }

        private static Spell CreateTechniqueSpell(
            MonkTechniqueDefinition definition)
        {
            var spell = ScriptableObject.CreateInstance<Spell>();
            int manaCost = ResolveTechniqueManaCost(definition);
            string classification =
                definition.Kind == MonkTechniqueKind.Passive
                    ? "Passive Monk Spell"
                    : "Monk Spell";
            spell.name = "CS MONK SPELL - " + definition.Name;
            spell.Id = BuildSpellId(definition.Key);
            spell.UsedBy = new List<Class> { MonkClass };
            spell.Type = definition.IsOffensive
                ? Spell.SpellType.Damage
                : Spell.SpellType.Beneficial;
            spell.Line = Spell.SpellLine.Global_Buff;
            spell.RequiredLevel = definition.Level;
            spell.SpellName = definition.Name;
            spell.SpellDesc = definition.Description;
            spell.SpecialDescriptor =
                "<color=#E7C76F>" + classification + "</color>\n" +
                (manaCost > 0 ? "Mana: " + manaCost + "\n" : "") +
                definition.Description;
            // Erenshor's own swoosh and melee-hit clips, played by the native
            // cast pipeline. Monk techniques previously left these null and
            // relied entirely on a private AudioSource, which put every strike
            // outside the game's audio path and is why they sounded thin next
            // to native combat.
            spell.ChargeVariations = new List<AudioClip>();
            spell.CompleteVariations = new List<AudioClip>();
            spell.ChargeSound = MonkAudio.NativeCharge(definition.Key);
            spell.CompleteSound = MonkAudio.NativeComplete(definition);
            spell.SpellChargeTime = 0f;
            spell.SpellDurationInTicks = 0;
            spell.ManaCost = manaCost;
            spell.TargetDamage = 0;
            spell.Cooldown = definition.Cooldown / 60f;
            // CastSpell gates on "SpellRange > distance". A self-cast measures
            // zero distance, so a range of zero fails that test and the game
            // answers "You're too far away to do that!" — which is exactly what
            // Mend and every other self-only technique were doing. Any positive
            // range clears it.
            // Offensive reach sits just past the native 4-unit melee range:
            // the test is strictly greater-than, so a flat 4 was refused at
            // exactly the distance an auto-attack still connects from.
            spell.SpellRange = definition.Kind ==
                MonkTechniqueKind.StoneThrow ? 18f :
                definition.IsOffensive ? 5f : 5f;
            spell.SelfOnly = !definition.IsOffensive;
            spell.CanHitPlayers = true;
            spell.NoResonate = true;
            spell.AutomateAttack = definition.IsOffensive;
            spell.MyDamageType = GameData.DamageType.Physical;
            // Sim Monks are a DPS role. Utility techniques currently carry
            // player-facing messages and short-lived state owned by the local
            // player runtime, so AI may use only offensive techniques. This
            // still gives Sim Monks their full authored strike progression
            // without leaking "You..." messages or shared stance timers.
            spell.SimUsable = definition.IsOffensive;
            spell.SpellIcon = GetSkill(definition.Key)?.SkillIcon;
            spell.StatusEffectToApply =
                string.IsNullOrEmpty(definition.EffectKey)
                    ? null : GetEffect(definition.EffectKey);
            spell.ChargeVariations = new List<AudioClip>();
            spell.CompleteVariations = new List<AudioClip>();
            spell.StatusEffectMessageOnPlayer =
                "use " + definition.Name + ".";
            spell.StatusEffectMessageOnNPC =
                "is struck by " + definition.Name + ".";
            spell.hideFlags = HideFlags.HideAndDontSave;
            return spell;
        }

        internal static int ResolveTechniqueManaCost(
            MonkTechniqueDefinition definition)
        {
            if (definition == null ||
                definition.Kind == MonkTechniqueKind.Passive)
                return 0;

            // Level carries most of the progression cost. Offensive spell
            // power adds a smaller premium, while long-cooldown utility spells
            // receive a modest baseline. Round to native-looking increments.
            //
            // The former curve started at a flat 12 base with a 25 floor, so a
            // level-one Jab cost 25 from a level-one Monk's tiny mana pool —
            // three or four strikes and the class was spent, which is why a
            // level-one Monk could not finish a level-one spider. A Monk is a
            // martial class: low-level techniques have to be sustainable, and
            // the cost should climb with level rather than start high.
            float raw = 2f + definition.Level * 2.8f +
                Mathf.Max(0, definition.Power) * 2f +
                (definition.IsOffensive ? 0f : 4f);
            return Mathf.Max(5, Mathf.RoundToInt(raw / 5f) * 5);
        }

        private static Spell CreateUtilityEffect(string key, string name,
            int durationTicks, Spell.SpellLine line)
        {
            var spell = ScriptableObject.CreateInstance<Spell>();
            spell.name = "CS MONK EFFECT - " + name;
            spell.Id = BuildEffectId(key);
            spell.UsedBy = new List<Class> { MonkClass };
            spell.Type = Spell.SpellType.Beneficial;
            spell.Line = line;
            spell.RequiredLevel = GetDefinition(key)?.Level ?? 1;
            spell.SpellName = name;
            spell.SpellDesc = GetDefinition(key)?.Description ?? name;
            spell.SpellDurationInTicks = durationTicks;
            spell.SpellChargeTime = 0f;
            spell.SpellIcon = GetSkill(key)?.SkillIcon;
            spell.SelfOnly = true;
            spell.CanHitPlayers = true;
            spell.NoResonate = true;
            spell.SimUsable = false;
            spell.StatusEffectMessageOnPlayer = "enter " + name + ".";
            spell.StatusEffectMessageOnNPC = "enters " + name + ".";
            spell.hideFlags = HideFlags.HideAndDontSave;
            EffectsByKey[key] = spell;
            return spell;
        }

        private static Spell CreateDetrimentalEffect(string key,
            string name, int durationTicks, Spell.SpellLine line)
        {
            Spell spell = CreateUtilityEffect(key, name, durationTicks, line);
            spell.Type = Spell.SpellType.StatusEffect;
            spell.SelfOnly = false;
            spell.NoResonate = true;
            spell.MyDamageType = GameData.DamageType.Physical;
            spell.StatusEffectMessageOnPlayer = "are hindered by " + name + ".";
            spell.StatusEffectMessageOnNPC = "is hindered by " + name + ".";
            return spell;
        }

        private static void RegisterTomes()
        {
            TomesByKey.Clear();
            foreach (MonkTechniqueDefinition definition in MonkSkillData.All)
            {
                // A passive tome teaches the innate Skill; everything else
                // teaches its spellbook technique.
                bool passive = definition.Kind == MonkTechniqueKind.Passive;
                Spell spell = passive ? null : GetSpell(definition.Key);
                Skill skill = passive ? GetSkill(definition.Key) : null;
                var item = ScriptableObject.CreateInstance<Item>();
                item.name = "CS MONK TOME - " + definition.Name;
                item.Id = BuildTomeId(definition.Key);
                item.ItemName = "Technique Tome: " + definition.Name;
                item.ItemLevel = definition.Level;
                item.ItemValue = TomePrice(definition.Level);
                item.ItemIcon = LoadIcon("Monk Tome " + definition.Key);
                item.RequiredSlot = Item.SlotType.General;
                item.Classes = new List<Class> { MonkClass };
                item.TeachSpell = spell;
                item.TeachSkill = skill;
                // Same overlap as the Necromancer scrolls: ItemInfoWindow puts
                // the teaching detail in ReqLvl and leaves Lore untouched, so
                // any Lore text draws over it. SpellDesc already carries
                // definition.Description.
                item.Lore = string.Empty;
                item.Stackable = false;
                item.Disposable = false;
                // Teaching items follow the native vendor/trash/bank rules so
                // unwanted or already-known tomes never become permanent.
                item.NoTradeNoDestroy = false;
                item.PlayerCannotSell = false;
                item.SimPlayersCantGet = true;
                item.hideFlags = HideFlags.HideAndDontSave;
                TomesByKey[definition.Key] = item;
                Items.ItemFactory.RegisterExternalItem(item.ItemName, item);
            }
            _tomesRegistered = true;
            SkillsPlugin.Log?.LogInfo(
                $"Monk: registered {TomesByKey.Count} unique technique tomes.");
        }

        private static int TomePrice(int level) =>
            Mathf.Max(1, Mathf.RoundToInt(level * level * 0.70f + level * 2f));

        private static Sprite LoadIcon(string name)
        {
            if (_embeddedIcons == null)
                _embeddedIcons = EmbeddedResources.LoadAllIcons();
            if (_embeddedIcons.TryGetValue(name, out Sprite icon) && icon)
            {
                icon.name = name;
                return icon;
            }
            SkillsPlugin.Log?.LogError(
                $"Missing required unique Monk icon '{name}'.");
            return null;
        }

        private static Sprite LoadClassIcon()
        {
            if (_classIcon) return _classIcon;
            _classIcon = LoadIcon("Monk Class");
            return _classIcon;
        }

        private static void ApplyEquipmentEligibility()
        {
            if (GameData.ClassDB?.Duelist == null || MonkClass == null)
                return;
            int lightArmorChanged = 0;
            int oneHandBluntChanged = 0;
            foreach (Item item in GameData.ItemDB.ItemDB)
            {
                bool alreadyEligible =
                    item?.Classes?.Any(IsMonkClass) == true;
                if (EnsureEquipmentEligibility(item) && !alreadyEligible)
                {
                    if (IsMonkOneHandBlunt(item)) oneHandBluntChanged++;
                    else lightArmorChanged++;
                }
            }
            _equipmentEligibilityApplied = true;
            int consumables =
                Items.NativeConsumableCompatibility.AddClass(MonkClass);
            SkillsPlugin.Log?.LogInfo(
                $"Monk: enabled {lightArmorChanged} native light-armor/accessory " +
                $"items, {oneHandBluntChanged} one-handed blunt weapons, and " +
                $"{consumables} universal native consumables; other weapon " +
                "inheritance remains excluded.");
        }

        /// <summary>
        /// Reconciles one item against the same Monk equipment rules used by
        /// the catalog-wide initialization pass. Tooltip rendering can occur
        /// after another mod has rebuilt an item's class list, so this method
        /// is intentionally idempotent and safe to call immediately before a
        /// tooltip is displayed.
        /// </summary>
        internal static bool EnsureEquipmentEligibility(Item item)
        {
            Class duelist = GameData.ClassDB?.Duelist;
            Class druid = GameData.ClassDB?.Druid;
            Class stormcaller = GameData.ClassDB?.Stormcaller;
            if (item?.Classes == null || duelist == null ||
                MonkClass == null)
                return false;

            bool explicitlyAllowed = item.Classes.Any(IsMonkClass);
            item.Classes.RemoveAll(value => IsMonkClass(value) &&
                !ReferenceEquals(value, MonkClass));
            if (explicitlyAllowed && !item.Classes.Contains(MonkClass))
                item.Classes.Add(MonkClass);
            if (item.Classes.Contains(MonkClass)) return true;

            if (item.TeachSpell != null || item.TeachSkill != null ||
                item.RequiredSlot == Item.SlotType.General)
                return false;
            // Unrestricted auras and charms carry no class entries and stay
            // usable by every class; class-restricted ones remain excluded
            // because the Monk runs its own aura progression and class charm.
            if (item.RequiredSlot == Item.SlotType.Aura ||
                item.RequiredSlot == Item.SlotType.Charm)
                return item.Classes.Count == 0;

            bool heldWeapon = item.RequiredSlot == Item.SlotType.Primary ||
                item.RequiredSlot == Item.SlotType.Secondary ||
                item.RequiredSlot == Item.SlotType.PrimaryOrSecondary;
            if (heldWeapon)
            {
                // Empty class lists are already universally usable; adding only
                // Monk would accidentally turn those items into class-restricted
                // gear. Class-restricted one-handed blunt weapons are the sole
                // native weapon family the Monk inherits. Their native
                // OneHandMelee flag is what permits a learned Dual Wield skill
                // to place them in the secondary slot.
                if (item.Classes.Count == 0 || !IsMonkOneHandBlunt(item))
                    return false;
                item.Classes.Add(MonkClass);
                return true;
            }

            bool lightMartial = item.Classes.Contains(duelist) &&
                ((druid != null && item.Classes.Contains(druid)) ||
                 (stormcaller != null &&
                    item.Classes.Contains(stormcaller)));
            if (!lightMartial) return false;
            item.Classes.Add(MonkClass);
            return true;
        }

        /// <summary>
        /// Authoritative Monk weapon gate. Requiring both the native one-hand
        /// melee flag and the addon's blunt classifier keeps two-hand hammers,
        /// daggers, shields, bows, wands, and name-only false positives out.
        /// </summary>
        internal static bool IsMonkOneHandBlunt(Item item)
        {
            if (item == null || item.Shield || item.IsBow || item.IsWand ||
                item.ThisWeaponType != Item.WeaponType.OneHandMelee)
                return false;
            if (item.RequiredSlot != Item.SlotType.Primary &&
                item.RequiredSlot != Item.SlotType.Secondary &&
                item.RequiredSlot != Item.SlotType.PrimaryOrSecondary)
                return false;
            return Skills.CombatSkills.ClassifyWeapon(item, false) ==
                Skills.CombatSkills.WeaponType.OneHandBlunt;
        }

        internal static bool ValidateOneHandBluntAccess(out string detail)
        {
            Item[] database = GameData.ItemDB?.ItemDB ?? Array.Empty<Item>();
            List<Item> candidates = database.Where(item =>
                    item?.Classes != null && item.Classes.Count > 0 &&
                    IsMonkOneHandBlunt(item))
                .ToList();
            List<string> missing = candidates.Where(item =>
                    !item.Classes.Contains(MonkClass))
                .Select(item => item.ItemName)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            bool offHandCandidate = candidates.Any(item =>
                item.RequiredSlot == Item.SlotType.Secondary ||
                item.RequiredSlot == Item.SlotType.PrimaryOrSecondary);
            bool dualWieldAtTwo = MonkSharedSkills.All.Any(definition =>
                string.Equals(definition.SkillName, "Dual Wield",
                    StringComparison.OrdinalIgnoreCase) &&
                definition.RequiredLevel == 2);

            detail = $"one-hand blunt weapons={candidates.Count}, " +
                $"off-hand capable={offHandCandidate}, Dual Wield@2=" +
                $"{dualWieldAtTwo}, missing Monk eligibility=[" +
                string.Join(", ", missing) + "]";
            return candidates.Count > 0 && offHandCandidate && dualWieldAtTwo &&
                missing.Count == 0;
        }

        /// <summary>
        /// Deterministic regression check for native aura/charm eligibility:
        /// auras and charms without any class entry stay unrestricted, must
        /// remain eligible for the Monk, and must not be turned into
        /// class-restricted gear by the reconciliation pass.
        /// </summary>
        internal static bool ValidateUnrestrictedAuraCharmAccess(
            out string detail)
        {
            Item[] database = GameData.ItemDB?.ItemDB ?? Array.Empty<Item>();
            List<Item> unrestricted = database.Where(item =>
                    item?.Classes != null && item.Classes.Count == 0 &&
                    item.TeachSpell == null && item.TeachSkill == null &&
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

        public static bool IsMonkClass(Class value)
        {
            return value != null && (ReferenceEquals(value, MonkClass) ||
                string.Equals(value.ClassName, ClassName,
                    StringComparison.OrdinalIgnoreCase));
        }

        public static bool IsMonkPlayer()
        {
            return IsMonkClass(GameData.PlayerStats?.CharacterClass) ||
                string.Equals(GameData.CurrentCharacterSlot?.CharClass,
                    ClassName, StringComparison.OrdinalIgnoreCase);
        }

        internal static bool TryGetDefinition(Skill skill,
            out MonkTechniqueDefinition definition)
        {
            return Definitions.TryGetValue(skill, out definition);
        }

        internal static bool TryGetDefinition(Spell spell,
            out MonkTechniqueDefinition definition)
        {
            return SpellDefinitions.TryGetValue(spell, out definition);
        }

        internal static MonkTechniqueDefinition GetDefinition(string key)
        {
            return MonkSkillData.All.FirstOrDefault(value => string.Equals(
                value.Key, key, StringComparison.OrdinalIgnoreCase));
        }

        public static Skill GetSkill(string key)
        {
            SkillsByKey.TryGetValue(key ?? "", out Skill value);
            return value;
        }

        public static Spell GetSpell(string key)
        {
            SpellsByKey.TryGetValue(key ?? "", out Spell value);
            return value;
        }

        public static Item GetTome(string key)
        {
            TomesByKey.TryGetValue(key ?? "", out Item value);
            return value;
        }

        internal static Spell GetEffect(string key)
        {
            EffectsByKey.TryGetValue(key ?? "", out Spell value);
            return value;
        }

        // The trainer teaches at every tier but outfits beginners only:
        // ClassMerchantStock keeps weapons and armor above level 10 off the
        // shelf while techniques, auras, and the class charm stay available.
        internal static IEnumerable<Item> GetVendorStock()
        {
            IEnumerable<Item> shared = MonkSharedSkills.ResolveBooks();
            return TomesByKey.Values.Concat(shared).Concat(
                    MonkAuras.GetItemsByLevel()).Concat(MonkGear.VendorItems)
                .Where(ClassMerchantStock.IsStockable)
                .OrderBy(GetRequiredLevel)
                .ThenBy(item => item.RequiredSlot == Item.SlotType.Aura ? 0 : 1)
                .ThenBy(item => item.ItemName,
                    StringComparer.OrdinalIgnoreCase);
        }

        private static int GetRequiredLevel(Item item)
        {
            if (item == null) return int.MaxValue;
            if (item.ItemLevel > 0) return item.ItemLevel;
            if (MonkSharedSkills.TryGetDefinition(item, out var shared))
                return shared.RequiredLevel;
            return 0;
        }

        internal static int RestoreKnownSkillsAfterHotLoad()
        {
            if (!_skillsRegistered || !_spellsRegistered || !IsMonkPlayer() ||
                GameData.CurrentCharacterSlot == null ||
                GameData.PlayerControl?.Myself?.MySkills?.KnownSkills == null ||
                GameData.PlayerControl?.Myself?.MySpells?.KnownSpells == null)
                return 0;
            List<Skill> knownSkills =
                GameData.PlayerControl.Myself.MySkills.KnownSkills;
            List<Spell> knownSpells =
                GameData.PlayerControl.Myself.MySpells.KnownSpells;
            int restored = 0;
            int preservedPassives = 0;
            foreach (string id in GameData.CurrentCharacterSlot.CharacterSpells ??
                new List<string>())
            {
                if (!IsMonkSpellId(id)) continue;
                Spell spell = SpellsByKey.Values.FirstOrDefault(value =>
                    string.Equals(value.Id, id,
                        StringComparison.OrdinalIgnoreCase));
                if (spell != null && !knownSpells.Contains(spell))
                {
                    knownSpells.Add(spell);
                    restored++;
                }
            }

            var legacySkills = new List<Skill>();
            foreach (string id in GameData.CurrentCharacterSlot.CharacterSkills ??
                new List<string>())
            {
                if (!IsMonkSkillId(id)) continue;
                Skill skill = SkillsByKey.Values.FirstOrDefault(value =>
                    string.Equals(value.Id, id,
                        StringComparison.OrdinalIgnoreCase));
                if (skill == null ||
                    !TryGetDefinition(skill,
                        out MonkTechniqueDefinition definition))
                    continue;
                if (definition.Kind == MonkTechniqueKind.Passive)
                {
                    if (!knownSkills.Contains(skill))
                    {
                        knownSkills.Add(skill);
                        preservedPassives++;
                    }
                    continue;
                }
                legacySkills.Add(skill);
                Spell spell = GetSpell(definition.Key);
                if (spell != null && !knownSpells.Contains(spell))
                {
                    knownSpells.Add(spell);
                    restored++;
                }
            }

            foreach (Skill skill in knownSkills.ToArray())
            {
                if (!TryGetDefinition(skill,
                        out MonkTechniqueDefinition definition))
                    continue;
                if (definition.Kind == MonkTechniqueKind.Passive) continue;
                Spell spell = GetSpell(definition.Key);
                if (spell != null && !knownSpells.Contains(spell))
                {
                    knownSpells.Add(spell);
                    restored++;
                }
                legacySkills.Add(skill);
            }
            foreach (Skill skill in legacySkills.Distinct())
                knownSkills.Remove(skill);

            List<string> savedSpells =
                GameData.CurrentCharacterSlot.CharacterSpells ??
                (GameData.CurrentCharacterSlot.CharacterSpells =
                    new List<string>());
            foreach (Spell spell in knownSpells.Where(value =>
                value != null && IsMonkSpellId(value.Id)))
                if (!savedSpells.Contains(spell.Id,
                        StringComparer.OrdinalIgnoreCase))
                    savedSpells.Add(spell.Id);
            List<string> savedSkills =
                GameData.CurrentCharacterSlot.CharacterSkills ??
                (GameData.CurrentCharacterSlot.CharacterSkills =
                    new List<string>());
            foreach (Skill skill in knownSkills)
            {
                if (!TryGetDefinition(skill,
                        out MonkTechniqueDefinition definition) ||
                    definition.Kind != MonkTechniqueKind.Passive)
                    continue;
                if (!savedSkills.Contains(skill.Id,
                        StringComparer.OrdinalIgnoreCase))
                {
                    savedSkills.Add(skill.Id);
                    preservedPassives++;
                }
            }
            savedSkills.RemoveAll(id =>
            {
                if (!IsMonkSkillId(id)) return false;
                Skill skill = SkillsByKey.Values.FirstOrDefault(value =>
                    string.Equals(value.Id, id,
                        StringComparison.OrdinalIgnoreCase));
                return skill == null ||
                    !TryGetDefinition(skill,
                        out MonkTechniqueDefinition definition) ||
                    definition.Kind != MonkTechniqueKind.Passive;
            });

            RestoreSpellHotkeys(GameData.CurrentCharacterSlot.HKSpells,
                GameData.GM?.HKManager?.FirstHotkeys);
            RestoreSpellHotkeys(GameData.CurrentCharacterSlot.SecHKSpells,
                GameData.GM?.HKManager?.SecondHotkeys);
            ConvertLegacyHotkeys(GameData.CurrentCharacterSlot.HKSkills,
                GameData.CurrentCharacterSlot.HKSpells,
                GameData.GM?.HKManager?.FirstHotkeys);
            ConvertLegacyHotkeys(GameData.CurrentCharacterSlot.SecHKSkills,
                GameData.CurrentCharacterSlot.SecHKSpells,
                GameData.GM?.HKManager?.SecondHotkeys);
            if (restored > 0 || legacySkills.Count > 0 ||
                preservedPassives > 0)
                SkillsPlugin.Log?.LogWarning(
                    $"Monk: migrated {restored} learned spellbook " +
                    $"technique(s) and removed {legacySkills.Distinct().Count()} " +
                    $"legacy skillbook entries; preserved {preservedPassives} " +
                    "passive skill reference(s).");
            return restored;
        }

        private static void RestoreSpellHotkeys(string[] ids,
            IList<Hotkeys> hotkeys)
        {
            if (ids == null || hotkeys == null) return;
            int count = Mathf.Min(ids.Length, hotkeys.Count);
            for (int index = 0; index < count; index++)
            {
                if (!IsMonkSpellId(ids[index]) || hotkeys[index] == null)
                    continue;
                Spell spell = SpellsByKey.Values.FirstOrDefault(value =>
                    string.Equals(value.Id, ids[index],
                        StringComparison.OrdinalIgnoreCase));
                if (spell != null) hotkeys[index].AssignSpellFromBook(spell);
            }
        }

        private static void ConvertLegacyHotkeys(string[] skillIds,
            string[] spellIds, IList<Hotkeys> hotkeys)
        {
            if (skillIds == null || hotkeys == null) return;
            int count = Mathf.Min(skillIds.Length, hotkeys.Count);
            for (int index = 0; index < count; index++)
            {
                string id = skillIds[index];
                if (!IsMonkSkillId(id) || hotkeys[index] == null) continue;
                Skill skill = SkillsByKey.Values.FirstOrDefault(value =>
                    string.Equals(value.Id, id,
                        StringComparison.OrdinalIgnoreCase));
                if (skill == null ||
                    !TryGetDefinition(skill,
                        out MonkTechniqueDefinition definition))
                    continue;
                Spell spell = GetSpell(definition.Key);
                if (spell == null) continue;
                hotkeys[index].AssignSpellFromBook(spell);
                if (spellIds != null && index < spellIds.Length)
                    spellIds[index] = spell.Id;
                skillIds[index] = "";
            }
        }

        internal static string BuildSkillId(string key) =>
            "CS_MONK_SKILL_" + NormalizeKey(key);
        internal static string BuildSpellId(string key) =>
            "CS_MONK_SPELL_" + NormalizeKey(key);
        internal static string BuildTomeId(string key) =>
            "CS_MONK_TOME_" + NormalizeKey(key);
        internal static string BuildEffectId(string key) =>
            "CS_MONK_EFFECT_" + NormalizeKey(key);
        internal static bool IsMonkSkillId(string id) =>
            !string.IsNullOrEmpty(id) && id.StartsWith("CS_MONK_SKILL_",
                StringComparison.OrdinalIgnoreCase);
        internal static bool IsMonkSpellId(string id) =>
            !string.IsNullOrEmpty(id) && id.StartsWith("CS_MONK_SPELL_",
                StringComparison.OrdinalIgnoreCase);
        private static bool IsMonkEffectId(string id) =>
            !string.IsNullOrEmpty(id) && id.StartsWith("CS_MONK_EFFECT_",
                StringComparison.OrdinalIgnoreCase);
        private static string NormalizeKey(string key) =>
            new string((key ?? "").ToUpperInvariant().Where(character =>
                char.IsLetterOrDigit(character) || character == '_').ToArray());

        internal static void ResetRuntimeReferences()
        {
            MonkRuntime.ResetCharacterState();
        }

        internal static bool ValidateCatalog(out string detail)
        {
            var errors = new List<string>();
            if (MonkClass == null) errors.Add("class missing");
            if (!ClassIcon) errors.Add("class icon missing");
            if (SkillsByKey.Count != MonkSkillData.ExpectedCustomCount)
                errors.Add("skill count " + SkillsByKey.Count);
            // Passives are skills only, so the spellbook is short by exactly
            // the passive count.
            if (SpellsByKey.Count != MonkSkillData.ExpectedSpellCount)
                errors.Add("spellbook technique count " + SpellsByKey.Count);
            if (TomesByKey.Count != MonkSkillData.ExpectedCustomCount)
                errors.Add("tome count " + TomesByKey.Count);
            if (EffectsByKey.Count != ExpectedEffectCount)
                errors.Add("utility effect count " + EffectsByKey.Count);

            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var icons = new HashSet<int>();
            if (ClassIcon) icons.Add(ClassIcon.GetInstanceID());
            foreach (MonkTechniqueDefinition definition in MonkSkillData.All)
            {
                string description = definition.Description ?? string.Empty;
                if (description.IndexOf("animation",
                        StringComparison.OrdinalIgnoreCase) >= 0 ||
                    description.IndexOf("masterwork handwrap",
                        StringComparison.OrdinalIgnoreCase) >= 0 ||
                    description.IndexOf("primary starting attack",
                        StringComparison.OrdinalIgnoreCase) >= 0)
                    errors.Add("player-facing description " +
                        definition.Name);
                Skill skill = GetSkill(definition.Key);
                Spell spell = GetSpell(definition.Key);
                Item tome = GetTome(definition.Key);
                bool passive =
                    definition.Kind == MonkTechniqueKind.Passive;
                if (skill == null || tome == null || (!passive && spell == null))
                {
                    errors.Add("missing " + definition.Name);
                    continue;
                }
                if (!ids.Add(skill.Id) || !ids.Add(tome.Id) ||
                    (!passive && !ids.Add(spell.Id)))
                    errors.Add("duplicate ID " + definition.Name);
                if (!names.Add(passive ? skill.SkillName : spell.SpellName) ||
                    !names.Add(tome.ItemName))
                    errors.Add("duplicate name " + definition.Name);
                Sprite techniqueIcon =
                    passive ? skill.SkillIcon : spell.SpellIcon;
                if (!techniqueIcon ||
                    !icons.Add(techniqueIcon.GetInstanceID()))
                    errors.Add("missing/duplicate spellbook icon " +
                        definition.Name);
                if (!tome.ItemIcon ||
                    !icons.Add(tome.ItemIcon.GetInstanceID()))
                    errors.Add("missing/duplicate tome icon " +
                        definition.Name);
                if (ReferenceEquals(techniqueIcon, tome.ItemIcon))
                    errors.Add("tome reuses spellbook icon " +
                        definition.Name);
                bool commonContract =
                    skill.DuelistRequiredLevel != definition.Level ||
                    tome.Classes == null || tome.Classes.Count != 1 ||
                    !tome.Classes.Contains(MonkClass);
                bool passiveContract = passive &&
                    (spell != null || tome.TeachSpell != null ||
                     !ReferenceEquals(tome.TeachSkill, skill));
                bool activeContract = !passive &&
                    (spell.RequiredLevel != definition.Level ||
                     Mathf.Abs(spell.Cooldown -
                         definition.Cooldown / 60f) > 0.001f ||
                     spell.UsedBy == null ||
                     spell.UsedBy.Count != 1 ||
                     !spell.UsedBy.Contains(MonkClass) ||
                     !ReferenceEquals(tome.TeachSpell, spell) ||
                     tome.TeachSkill != null);
                if (commonContract || passiveContract || activeContract)
                    errors.Add("level/class contract " + definition.Name);
                int expectedMana = ResolveTechniqueManaCost(definition);
                if (passive
                    ? expectedMana != 0
                    : spell.ManaCost != expectedMana ||
                      spell.ManaCost <= 0)
                    errors.Add("mana contract " + definition.Name);
                if (!passive)
                {
                    Spell.SpellType expectedType = definition.IsOffensive
                        ? Spell.SpellType.Damage
                        : Spell.SpellType.Beneficial;
                    if (spell.Type != expectedType ||
                        spell.SpecialDescriptor?.IndexOf("Monk Spell",
                            StringComparison.OrdinalIgnoreCase) < 0)
                        errors.Add("spell classification " + definition.Name);
                }
                if (!ReferenceEquals(
                    GameData.SkillDatabase?.GetSkillByID(skill.Id), skill))
                    errors.Add("skill database " + definition.Name);
                if (!ReferenceEquals(
                    Items.ItemFactory.GetItemById(tome.Id), tome))
                    errors.Add("item database " + definition.Name);
                if (!passive &&
                    GameData.SpellDatabase?.SpellDatabase?.Contains(spell) !=
                        true)
                    errors.Add("spell database " + definition.Name);
                if (!string.IsNullOrEmpty(definition.EffectKey) &&
                    !ReferenceEquals(skill.EffectToApply,
                        GetEffect(definition.EffectKey)))
                    errors.Add("effect binding " + definition.Name);
                if (!passive &&
                    !string.IsNullOrEmpty(definition.EffectKey) &&
                    !ReferenceEquals(spell.StatusEffectToApply,
                        GetEffect(definition.EffectKey)))
                    errors.Add("spell effect binding " + definition.Name);
            }

            if (!MonkSharedSkills.Validate(out string sharedDetail))
                errors.Add("shared: " + sharedDetail);
            if (!ValidateOneHandBluntAccess(out string bluntDetail))
                errors.Add("one-hand blunt: " + bluntDetail);
            if (!ValidateUnrestrictedAuraCharmAccess(out string auraCharmDetail))
                errors.Add("unrestricted auras/charms: " + auraCharmDetail);
            if (!MonkAuras.ValidateCatalog(out string auraDetail))
                errors.Add("auras: " + auraDetail);
            if (!MonkGear.ValidateCatalog(out string gearDetail))
                errors.Add("gear: " + gearDetail);
            if (!MonkHandwrapArt.ValidateCatalog(out string handwrapArtDetail))
                errors.Add("handwrap art: " + handwrapArtDetail);
            if (!MonkHandwrapLoot.RunAcquisitionSelfTest(out string lootDetail))
                errors.Add("boss handwrap loot: " + lootDetail);
            if (!MonkAscensions.Validate(out string ascensionDetail))
                errors.Add("ascensions: " + ascensionDetail);
            if (!MonkVfx.Validate(out string vfxDetail))
                errors.Add("VFX: " + vfxDetail);

            detail = errors.Count == 0
                ? $"{SpellsByKey.Count} custom spellbook techniques backed " +
                  $"by {SkillsByKey.Count} save-compatible mechanics + " +
                  $"{MonkSharedSkills.ExpectedCount} native martial skills = " +
                  $"{MonkSkillData.ExpectedTotalCount}; {TomesByKey.Count} " +
                  $"unique tomes, {MonkAuras.ExpectedCount} auras, " +
                  $"{MonkGear.AllItems.Count} new gear items, class icon, " +
                  $"handwrap art ({handwrapArtDetail}), " +
                  $"deterministic save IDs; {sharedDetail}; {auraDetail}; " +
                  $"{bluntDetail}; {auraCharmDetail}; {gearDetail}; " +
                  $"{lootDetail}; {ascensionDetail}; {vfxDetail}"
                : string.Join("; ", errors);
            return errors.Count == 0;
        }

        internal static bool ValidateBalance(out string detail)
        {
            var errors = new List<string>();
            int benefitTotal = MonkClass == null ? 0 :
                MonkClass.StrBenefit + MonkClass.EndBenefit +
                MonkClass.DexBenefit + MonkClass.AgiBenefit +
                MonkClass.IntBenefit + MonkClass.WisBenefit +
                MonkClass.ChaBenefit;
            if (benefitTotal != 52)
                errors.Add("class benefit budget " + benefitTotal);

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int previousLevel = 0;
            foreach (MonkTechniqueDefinition definition in MonkSkillData.All)
            {
                if (!names.Add(definition.Name))
                    errors.Add("duplicate name " + definition.Name);
                if (definition.Level < previousLevel || definition.Level < 1 ||
                    definition.Level > 35)
                    errors.Add("progression level " + definition.Name);
                previousLevel = definition.Level;
                if (definition.IsOffensive && (definition.Power < 2 ||
                    definition.Power > 8 || definition.Cooldown < 240f ||
                    definition.Cooldown > 3600f))
                    errors.Add("attack envelope " + definition.Name);
                if (!definition.IsOffensive && definition.Kind !=
                        MonkTechniqueKind.Passive &&
                    (definition.Cooldown < 1800f ||
                     definition.Cooldown > 5400f))
                    errors.Add("utility cooldown " + definition.Name);
                int manaCost = ResolveTechniqueManaCost(definition);
                if (definition.Kind == MonkTechniqueKind.Passive
                    ? manaCost != 0
                    : manaCost < 5 || manaCost > 160)
                    errors.Add("mana envelope " + definition.Name);
                if (EndsWithRankSuffix(definition.Name))
                    errors.Add("ranked name " + definition.Name);
            }
            if (MonkSkillData.All.Count + MonkSharedSkills.ExpectedCount !=
                MonkSkillData.ExpectedTotalCount)
                errors.Add("total ability count");
            if (!MonkAuras.ValidateNativeBudgetMirror(out string auraBudget))
                errors.Add("aura budget: " + auraBudget);
            if (!MonkGear.ValidateNativeBudget(out string gearBudget))
                errors.Add("gear budget: " + gearBudget);
            if (!MonkVfx.Validate(out string vfxBudget))
                errors.Add("VFX: " + vfxBudget);

            detail = errors.Count == 0
                ? $"{MonkSkillData.ExpectedTotalCount}-ability level 1-35 " +
                  "progression; unique names; custom " +
                  "strikes remain within native SkillPower 2-8 and 4-60 second " +
                  "cooldowns; utility cooldowns are 30-90 seconds; active " +
                  "Monk spells cost 5-160 mana while the passive is free; class " +
                  "benefit budget=52; " + auraBudget + "; " + gearBudget
                : string.Join("; ", errors);
            return errors.Count == 0;
        }

        private static bool EndsWithRankSuffix(string name)
        {
            string[] suffixes =
            {
                " I", " II", " III", " IV", " V", " VI",
                " VII", " VIII", " IX", " X"
            };
            return suffixes.Any(suffix => name.EndsWith(suffix,
                StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// Retires clones of the silent technique effect on their first frame.
    /// Instantiate copies this component, so the authored template must be left
    /// alone or the registered index would point at a destroyed object.
    /// </summary>
    internal sealed class MonkSilentEffect : MonoBehaviour
    {
        private void Update()
        {
            if (ReferenceEquals(gameObject, MonkCatalog.SilentEffectPrefab))
            {
                enabled = false;
                return;
            }
            Destroy(gameObject);
        }
    }
}
