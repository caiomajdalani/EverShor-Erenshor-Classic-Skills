using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.AI;

namespace ErenshorSkills.Necromancer
{
    internal sealed class NecromancerPetMarker : MonoBehaviour
    {
        internal Character Owner;
        internal int Tier;
        internal int BasePetHp;
        internal int BasePetArmor;
        internal int BasePetDamage;
        internal int BasePetOffhandDamage;
        internal int BaseInventoryHp;
        internal int BaseInventoryAc;
        internal int BaseInventoryMana;
        internal int BaseInventoryStr;
        internal int BaseInventoryEnd;
        internal int BaseInventoryDex;
        internal int BaseInventoryAgi;
        internal int BaseInventoryInt;
        internal int BaseInventoryWis;
        internal int BaseInventoryCha;
        internal int BaseInventoryRes;
        internal int BaseInventoryMr;
        internal int BaseInventoryEr;
        internal int BaseInventoryPr;
        internal int BaseInventoryVr;
        internal float NextTauntAt;
        internal int SuccessfulTaunts;
        internal Avatar NativePetAvatar;
        internal RuntimeAnimatorController NativePetController;
        internal Transform PlayerAvatarRig;
        internal Transform PlayerRigLeftHand;
        internal Transform PlayerRigRightHand;
        internal int PlayerRigSourceBoneCount;
        internal int PlayerRigMappedBoneCount;
        internal NecromancerPlayerEquipmentRig PlayerEquipmentRig;
        internal bool AddedDualWieldSkill;
        internal float BaseAttackRange;

        private NavMeshAgent _navigation;

        private void Awake()
        {
            _navigation = GetComponent<NavMeshAgent>();
        }

        internal void SynchronizeMovementSpeed()
        {
            if (Owner?.MyStats == null) return;
            if (_navigation == null)
                _navigation = GetComponent<NavMeshAgent>();
            if (_navigation == null || !_navigation.enabled) return;

            // Native companion following deliberately drops to half speed
            // inside thirteen metres. Override only the movement rate after
            // the NPC has updated, while preserving rooted/stopped states and
            // its existing destination.
            float ownerSpeed = Mathf.Max(0f, Owner.MyStats.actualRunSpeed);
            _navigation.speed = ownerSpeed;
            _navigation.acceleration = Mathf.Max(
                _navigation.acceleration, ownerSpeed * 4f);
        }

        internal bool ValidateMovementSpeed(out string detail)
        {
            if (Owner?.MyStats == null)
            {
                detail = "pet owner is unavailable";
                return false;
            }
            if (_navigation == null)
                _navigation = GetComponent<NavMeshAgent>();
            float expected = Mathf.Max(0f, Owner.MyStats.actualRunSpeed);
            bool valid = _navigation != null && _navigation.enabled &&
                Mathf.Approximately(_navigation.speed, expected);
            detail = _navigation == null
                ? "pet navigation is unavailable"
                : $"pet speed {_navigation.speed:0.###}; " +
                  $"owner speed {expected:0.###}";
            return valid;
        }

        private void LateUpdate()
        {
            SynchronizeMovementSpeed();
        }

        private void OnDestroy()
        {
            NecromancerPets.ReleasePetReference(GetComponent<NPC>(), "dismissed");
        }
    }

    /// <summary>
    /// Keeps a transform-only clone of the live player's humanoid equipment
    /// rig aligned with the pet's visible native skeleton. Held item models
    /// can therefore use the exact player hand hierarchy and authored offsets
    /// without rebinding the skeleton mesh to incompatible bind poses.
    /// </summary>
    internal sealed class NecromancerPlayerEquipmentRig : MonoBehaviour
    {
        private Transform _nativeRoot;
        private Transform _playerRoot;
        private Transform[] _nativeBones = Array.Empty<Transform>();
        private Transform[] _playerBones = Array.Empty<Transform>();

        internal int Configure(Transform nativeRoot, Transform playerRoot,
            IReadOnlyDictionary<string, Transform> playerBones)
        {
            _nativeRoot = nativeRoot;
            _playerRoot = playerRoot;
            var native = new List<Transform>();
            var player = new List<Transform>();
            foreach (KeyValuePair<string, Transform> pair in playerBones)
            {
                Transform nativeBone = pair.Key.Length == 0
                    ? nativeRoot : nativeRoot.Find(pair.Key);
                if (nativeBone == null || pair.Value == null) continue;
                native.Add(nativeBone);
                player.Add(pair.Value);
            }
            _nativeBones = native.ToArray();
            _playerBones = player.ToArray();
            SyncNow();
            return _nativeBones.Length;
        }

        internal bool IsFollowing(Transform nativeRoot, Transform playerRoot)
        {
            return enabled && _nativeRoot == nativeRoot &&
                _playerRoot == playerRoot &&
                _nativeBones.Length > 0 &&
                _nativeBones.Length == _playerBones.Length;
        }

        internal void SyncNow()
        {
            int count = Mathf.Min(_nativeBones.Length, _playerBones.Length);
            for (int index = 0; index < count; index++)
            {
                Transform native = _nativeBones[index];
                Transform player = _playerBones[index];
                if (native == null || player == null) continue;
                player.localPosition = native.localPosition;
                player.localRotation = native.localRotation;
                player.localScale = native.localScale;
            }
        }

        private void LateUpdate()
        {
            SyncNow();
        }
    }

    /// <summary>
    /// Supplies a readable two-hand bow draw to summoned skeletons whose
    /// native controller has no usable FireBow state. The Animator restores
    /// its normal pose every frame; this driver layers only the draw/release
    /// pose in LateUpdate and therefore does not replace the native controller
    /// or avatar.
    /// </summary>
    internal sealed class NecromancerBowAnimationDriver : MonoBehaviour
    {
        private const float DurationSeconds = 0.72f;
        private const float ContactNormalized = 0.50f;

        private NPC _pet;
        private Character _target;
        private Animator _animator;
        private Transform _chest;
        private Transform _leftUpperArm;
        private Transform _leftLowerArm;
        private Transform _leftHand;
        private Transform _rightUpperArm;
        private Transform _rightLowerArm;
        private Transform _rightHand;
        private float _startedAt;
        private bool _active;
        private bool _rigReady;

        internal bool IsActive => _active;
        internal bool RigReady => _rigReady;
        internal int PlayCount { get; private set; }

        internal void Play(NPC pet, Character target)
        {
            _pet = pet;
            _target = target;
            _animator = pet?.GetComponent<Animator>();
            CacheRig();
            _startedAt = Time.time;
            _active = _rigReady;
            if (_active)
            {
                PlayCount++;
                enabled = true;
            }
        }

        internal void ResetAudit()
        {
            PlayCount = 0;
        }

        private void CacheRig()
        {
            _rigReady = false;
            if (_animator == null || !_animator.isHuman) return;
            try
            {
                _chest = _animator.GetBoneTransform(HumanBodyBones.Chest) ??
                    _animator.GetBoneTransform(HumanBodyBones.Spine);
                _leftUpperArm = _animator.GetBoneTransform(
                    HumanBodyBones.LeftUpperArm);
                _leftLowerArm = _animator.GetBoneTransform(
                    HumanBodyBones.LeftLowerArm);
                _leftHand = _animator.GetBoneTransform(
                    HumanBodyBones.LeftHand);
                _rightUpperArm = _animator.GetBoneTransform(
                    HumanBodyBones.RightUpperArm);
                _rightLowerArm = _animator.GetBoneTransform(
                    HumanBodyBones.RightLowerArm);
                _rightHand = _animator.GetBoneTransform(
                    HumanBodyBones.RightHand);
                _rigReady = _leftUpperArm != null &&
                    _leftLowerArm != null && _leftHand != null &&
                    _rightUpperArm != null && _rightLowerArm != null &&
                    _rightHand != null;
            }
            catch
            {
                _rigReady = false;
            }
        }

        private void LateUpdate()
        {
            if (!_active || !_rigReady || _pet == null)
            {
                enabled = false;
                return;
            }
            float time = (Time.time - _startedAt) / DurationSeconds;
            if (time >= 1f)
            {
                _active = false;
                enabled = false;
                return;
            }

            Vector3 up = _pet.transform.up;
            Vector3 forward = HorizontalDirection();
            Vector3 right = Vector3.Cross(up, forward).normalized;
            float entry = SmoothStep01(Mathf.Clamp01(time / 0.22f));
            float recovery = 1f - SmoothStep01(Mathf.Clamp01(
                (time - 0.68f) / 0.32f));
            float weight = entry * recovery;
            float draw = time <= ContactNormalized
                ? SmoothStep01(time / ContactNormalized)
                : 1f - SmoothStep01((time - ContactNormalized) /
                    (1f - ContactNormalized));

            // The left hand presents the bow toward the target while the
            // right elbow opens and the right hand draws beside the jaw.
            RotateSegment(_leftUpperArm, _leftLowerArm,
                (forward - right * 0.10f + up * 0.05f).normalized,
                weight * 0.92f);
            RotateSegment(_leftLowerArm, _leftHand,
                (forward + right * 0.08f + up * 0.02f).normalized,
                weight);
            RotateSegment(_rightUpperArm, _rightLowerArm,
                (right * 0.72f - forward * 0.12f - up * 0.10f).normalized,
                weight * draw * 0.94f);
            Vector3 drawForearm = (-right * 0.76f + forward * 0.05f +
                up * 0.24f).normalized;
            Vector3 releaseForearm = (forward - right * 0.20f +
                up * 0.08f).normalized;
            RotateSegment(_rightLowerArm, _rightHand,
                Vector3.Slerp(releaseForearm, drawForearm, draw).normalized,
                weight * 0.96f);
            if (_chest != null)
                _chest.rotation = Quaternion.Slerp(_chest.rotation,
                    Quaternion.AngleAxis(-11f * draw, up) * _chest.rotation,
                    weight * 0.72f);
        }

        private Vector3 HorizontalDirection()
        {
            Vector3 direction = _target != null
                ? _target.transform.position + Vector3.up -
                    _pet.transform.position
                : _pet.transform.forward;
            direction = Vector3.ProjectOnPlane(direction, _pet.transform.up);
            return direction.sqrMagnitude > 0.001f
                ? direction.normalized : _pet.transform.forward;
        }

        private static void RotateSegment(Transform parent, Transform child,
            Vector3 desiredDirection, float weight)
        {
            if (parent == null || child == null || weight <= 0.001f) return;
            Vector3 current = child.position - parent.position;
            if (current.sqrMagnitude <= 0.000001f) return;
            Quaternion target = Quaternion.FromToRotation(
                current.normalized, desiredDirection) * parent.rotation;
            parent.rotation = Quaternion.Slerp(parent.rotation, target,
                Mathf.Clamp01(weight));
        }

        private static float SmoothStep01(float value)
        {
            value = Mathf.Clamp01(value);
            return value * value * (3f - 2f * value);
        }
    }

    /// <summary>
    /// Configures the native Fallen Skeleton prefab as a tiered Necromancer
    /// companion, layers a cloned player equipment rig over its intact
    /// skeletal body, and projects persistent escrow gear onto it. The
    /// transient NPC never owns the items themselves.
    /// </summary>
    internal static class NecromancerPets
    {
        private const string VisualRootName = "CS_NecromancerPetEquipment";
        private const float MaxPetLightRange = 1.75f;
        private const float MaxPetLightIntensity = 0.22f;
        private const float MinimumBowAttackRange = 14f;
        // Twelve feet, in the metre-scaled world units Erenshor uses (chest
        // height is 1.5, melee reach 4). Clear separation from the caster while
        // still landing just inside their own melee reach.
        private const float SummonStandOffDistance = 12f * 0.3048f;
        internal const float GraveChallengeCooldownSeconds = 6f;
        private const int MinimumTauntHateLead = 25;
        private static readonly MethodInfo NpcCheckForBowMethod =
            typeof(NPC).GetMethod("CheckForBow",
                BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly int[] TierUnlockLevel = { 0, 1, 8, 18, 24, 32 };
        private static readonly int[] TierBaseHp = { 0, 175, 800, 1500, 3800, 10500 };
        private static readonly int[] TierHpPerExtraLevel = { 0, 24, 50, 85, 150, 285 };
        private static readonly int[] TierBaseDamage = { 0, 5, 8, 11, 14, 18 };
        private static readonly int[] TierAttackDelay = { 0, 180, 160, 130, 115, 100 };
        private static readonly int[] TierArmorBonus = { 0, 10, 25, 45, 70, 100 };
        private static readonly int[] TierStrength = { 0, 5, 13, 14, 130, 340 };
        private static readonly int[] TierEndurance = { 0, 18, 32, 35, 130, 190 };
        private static readonly int[] TierDexterity = { 0, 28, 160, 430, 480, 360 };
        private static readonly int[] TierAgility = { 0, 6, 28, 20, 140, 140 };
        private static readonly int[] TierResist = { 0, 0, 0, 0, 55, 80 };
        private static readonly int[] TierWeaponCap = { 0, 3, 5, 8, 11, 15 };
        private static readonly int[] TierOffhandCap = { 0, 2, 4, 6, 8, 10 };
        private static readonly int[] TierShieldCap = { 0, 12, 30, 55, 85, 120 };
        internal static int BowAttackAttemptCount { get; private set; }
        internal static int BowProjectileLaunchCount { get; private set; }
        internal static int BowProjectileDeliveryCount { get; private set; }
        internal static int BowDamageDelivered { get; private set; }
        internal static int MeleeAttackAttemptCount { get; private set; }

        internal static NPC ActivePet
        {
            get
            {
                NPC pet = GameData.PlayerControl?.Myself?.MyCharmedNPC;
                return pet != null && pet.GetComponent<NecromancerPetMarker>() != null
                    ? pet : null;
            }
        }

        internal static void ConfigureSummonedPet(Character owner, int tier,
            NecromancerSpellDefinition definition = null)
        {
            if (owner == null || SkillsPlugin.Instance == null) return;
            SkillsPlugin.Instance.StartCoroutine(
                ConfigureWhenNativePetIsReady(owner, Mathf.Clamp(tier, 1, 5),
                    definition));
        }

        private static IEnumerator ConfigureWhenNativePetIsReady(Character owner,
            int tier, NecromancerSpellDefinition definition)
        {
            int waitFrames = 0;
            while (owner != null && owner.MyCharmedNPC == null &&
                waitFrames++ < 60)
                yield return null;
            NPC pet = owner?.MyCharmedNPC;
            if (pet == null)
            {
                SkillsPlugin.Log.LogError(
                    "Necromancer native skeleton summon did not assign a pet within 60 frames.");
                yield break;
            }
            NecromancerPetMarker marker = pet.GetComponent<NecromancerPetMarker>();
            if (marker == null)
                marker = pet.gameObject.AddComponent<NecromancerPetMarker>();
            marker.Owner = owner;
            marker.Tier = tier;
            // Erenshor summons pets onto the caster's own navmesh point, which
            // leaves the thrall standing inside the Necromancer. Step it out in
            // front before anything else reads its position.
            PlaceSummonedPet(owner, pet);
            // Raise the sigil the moment the skeleton exists so the symbol and
            // its eruption read as the thrall clawing its way up, rather than
            // trailing the summon by the two frames the stat pass needs. It
            // anchors to the pet, so the move above carries it along.
            if (definition != null)
                NecromancerVfx.PlaySummonResolve(definition, pet.transform);
            yield return ConfigureAfterNativeStart(owner, pet, marker.Tier);
        }

        /// <summary>
        /// Moves a freshly summoned thrall out to arm's length in front of the
        /// Necromancer. The native Pet branch places it on
        /// GetSafeNavMeshPoint(caster.position), so caster and thrall occupy the
        /// same spot and the summoning sigil ends up under the player's feet.
        /// </summary>
        private static void PlaceSummonedPet(Character owner, NPC pet)
        {
            if (owner == null || pet == null) return;
            try
            {
                Transform caster = owner.transform;
                Vector3 forward = Vector3.ProjectOnPlane(caster.forward,
                    Vector3.up);
                forward = forward.sqrMagnitude > 0.0001f
                    ? forward.normalized : caster.forward.normalized;

                Vector3 desired = caster.position +
                    forward * SummonStandOffDistance;
                Vector3 placement = desired;
                // Snap to the navmesh so the thrall can path from where it
                // rises. Sampling generously here still yields a point near the
                // caster if the chosen spot is inside geometry.
                if (NavMesh.SamplePosition(desired, out NavMeshHit hit,
                        SummonStandOffDistance, NavMesh.AllAreas))
                    placement = hit.position;
                else if (NavMesh.SamplePosition(caster.position,
                        out NavMeshHit fallback, SummonStandOffDistance * 2f,
                        NavMesh.AllAreas))
                    placement = fallback.position;

                // MyNav is private on NPC, so reach the agent directly. Warp is
                // required: assigning transform.position under an active agent
                // is silently reverted.
                NavMeshAgent agent = pet.GetComponent<NavMeshAgent>();
                if (agent != null && agent.enabled && agent.isOnNavMesh)
                    agent.Warp(placement);
                else
                    pet.transform.position = placement;

                // Rise facing the same way as its master rather than inheriting
                // the spell vessel's rotation.
                pet.transform.rotation =
                    Quaternion.LookRotation(forward, Vector3.up);
            }
            catch (Exception exception)
            {
                SkillsPlugin.Log?.LogWarning(
                    "Bone Thrall summon placement failed: " +
                    exception.Message);
            }
        }

        private static IEnumerator ConfigureAfterNativeStart(Character owner,
            NPC pet, int tier)
        {
            // The native NPC and Stats Start methods apply level randomization,
            // server balance multipliers, nameplates, and max-health setup.
            // Configure after those one-time transforms so they cannot overwrite
            // the Necromancer tier or its escrow-derived equipment stats.
            yield return null;
            yield return null;
            if (owner == null || pet == null || owner.MyCharmedNPC != pet)
                yield break;
            Character character = pet.GetComponent<Character>();
            Stats stats = pet.GetComponent<Stats>();
            Inventory inventory = pet.GetComponent<Inventory>();
            if (character == null || stats == null || inventory == null)
            {
                SkillsPlugin.Log.LogError(
                    "Necromancer skeleton prefab is missing required components.");
                yield break;
            }

            NecromancerPetMarker marker = pet.GetComponent<NecromancerPetMarker>();
            if (marker == null)
                marker = pet.gameObject.AddComponent<NecromancerPetMarker>();
            marker.Owner = owner;
            marker.Tier = Mathf.Clamp(tier, 1, 5);

            int ownerLevel = Mathf.Max(1, owner.MyStats?.Level ?? 1);
            int ascensionRank = NecromancerAscensions.GetRank(owner.MySkills,
                NecromancerAscensions.BoneSovereigntyId);
            int petLevel = Mathf.Max(1,
                ownerLevel - (marker.Tier >= 4 ? 0 : 1) + ascensionRank);
            GetBaseProfile(marker.Tier, ownerLevel, ascensionRank,
                out int baseHp, out int baseDamage);
            stats.Level = petLevel;
            stats.BaseHP = baseHp;
            stats.BaseAC = 5;
            stats.BaseMana = 0;
            stats.BaseStr = TierStrength[marker.Tier];
            stats.BaseEnd = TierEndurance[marker.Tier];
            stats.BaseDex = TierDexterity[marker.Tier];
            stats.BaseAgi = TierAgility[marker.Tier];
            stats.BaseInt = 3;
            stats.BaseWis = 3;
            stats.BaseCha = 3;
            stats.BaseRes = 0;
            stats.BaseMR = TierResist[marker.Tier];
            stats.BaseER = TierResist[marker.Tier];
            stats.BasePR = TierResist[marker.Tier];
            stats.BaseVR = TierResist[marker.Tier];
            stats.BaseLifesteal = 0f;
            stats.BaseMHAtkDelay = TierAttackDelay[marker.Tier];
            stats.OverrideHPforNPC = true;
            stats.CurrentHP = stats.BaseHP;

            pet.BaseAtkDmg = baseDamage;
            pet.OHAtkDmg = 0;
            pet.HardSetAC = petLevel * 15 + TierArmorBonus[marker.Tier];
            pet.DamageMult = 1f;
            pet.ArmorPenMult = 0.75f;
            pet.NPCProcOnHit = null;
            pet.NPCProcOnHitChance = 0;
            pet.SummonedByPlayer = true;
            pet.noClick = false;
            pet.NPCName = $"{owner.MyStats.MyName}'s Bone Thrall";
            pet.gameObject.name = pet.NPCName;
            stats.MyName = pet.NPCName;
            if (pet.NamePlateTxt != null)
                pet.NamePlateTxt.text = pet.NPCName;
            pet.CurrentAggroTarget = null;
            pet.AggroTable?.Clear();
            marker.NextTauntAt = 0f;
            marker.SuccessfulTaunts = 0;
            character.Master = owner;
            character.MyFaction = owner.MyFaction;
            character.BaseFaction = owner.MyFaction;
            character.TempFaction = owner.MyFaction;
            character.AggressiveTowards?.Clear();
            character.Allies?.Clear();
            if (character.Allies != null) character.Allies.Add(owner.MyFaction);
            owner.MyCharmedNPC = pet;

            marker.BasePetHp = stats.BaseHP;
            marker.BasePetArmor = pet.HardSetAC;
            marker.BasePetDamage = pet.BaseAtkDmg;
            marker.BasePetOffhandDamage = pet.OHAtkDmg;
            marker.BaseAttackRange = Mathf.Max(4f, character.AttackRange);
            CaptureInventoryBaseline(marker, inventory);

            if (!NecromancerPetEquipment.IsLoaded)
                NecromancerPetEquipment.Load();
            ApplyEscrowToPet(pet);
            stats.CurrentHP = stats.CurrentMaxHP;
            SkillsPlugin.Log.LogInfo(
                $"Necromancer pet configured: tier={marker.Tier}, level={petLevel}, " +
                $"ascension={ascensionRank}, hp={stats.CurrentMaxHP}, " +
                $"damage={pet.BaseAtkDmg}, prefab={NecromancerCatalog.PetPrefabName}.");
        }

        internal static void GetBaseProfile(int tier, int ownerLevel,
            int ascensionRank, out int hp, out int damage)
        {
            tier = Mathf.Clamp(tier, 1, 5);
            ownerLevel = Mathf.Max(1, ownerLevel);
            ascensionRank = Mathf.Clamp(ascensionRank, 0, 3);
            int levelsAboveUnlock = Mathf.Max(0,
                ownerLevel - TierUnlockLevel[tier]);
            hp = TierBaseHp[tier] +
                levelsAboveUnlock * TierHpPerExtraLevel[tier];
            hp = Mathf.RoundToInt(hp * (1f + ascensionRank * 0.05f));
            damage = TierBaseDamage[tier] + levelsAboveUnlock / 8 +
                ascensionRank;
        }

        internal static bool ValidateBalanceCurve(out string detail)
        {
            int[] druidHp = { 0, 300, 1000, 1550, 4500, 12500 };
            int[] druidDamage = { 0, 8, 10, 12, 14, 18 };
            var errors = new List<string>();
            var rows = new List<string>();
            for (int tier = 1; tier <= 5; tier++)
            {
                GetBaseProfile(tier, TierUnlockLevel[tier], 0,
                    out int hp, out int damage);
                float hpRatio = (float)hp / druidHp[tier];
                float damageRatio = (float)damage / druidDamage[tier];
                if (hpRatio < 0.55f || hpRatio > 1.05f)
                    errors.Add($"tier {tier} HP ratio {hpRatio:F2}");
                if (damageRatio < 0.55f || damageRatio > 1.01f)
                    errors.Add($"tier {tier} damage ratio {damageRatio:F2}");
                rows.Add($"T{tier} L{TierUnlockLevel[tier]} " +
                    $"HP {hp}/{druidHp[tier]} AC+{TierArmorBonus[tier]} " +
                    $"DMG {damage}/{druidDamage[tier]}");
            }
            detail = errors.Count == 0
                ? "Bone Thralls use the durable taunting-companion envelope " +
                  $"(Grave Challenge {GraveChallengeCooldownSeconds:F0}s) " +
                  "while staying at or below comparable Druid pet damage: " +
                  string.Join(", ", rows)
                : string.Join("; ", errors);
            return errors.Count == 0;
        }

        /// <summary>
        /// Grave Challenge is an automatic, single-target pet taunt. It only
        /// peels an enemy from the Necromancer who owns the thrall; it will not
        /// steal aggro from another player or SimPlayer tank in a group.
        /// </summary>
        internal static bool TryGraveChallenge(Character target,
            Character attacker, bool bypassCooldown, out string detail)
        {
            detail = null;
            NecromancerPetMarker marker = attacker != null
                ? attacker.GetComponent<NecromancerPetMarker>() : null;
            NPC enemy = target?.MyNPC;
            if (marker == null || marker.Owner == null || enemy == null ||
                attacker == null || !attacker.Alive || !target.Alive ||
                target.MyStats == null || target.MyStats.Charmed)
            {
                detail = "invalid pet or hostile target";
                return false;
            }
            if (!bypassCooldown && enemy.NeverAggro)
            {
                detail = "target cannot be taunted";
                return false;
            }

            Character currentTarget = enemy.CurrentAggroTarget;
            if (!bypassCooldown && currentTarget != null &&
                currentTarget != marker.Owner && currentTarget != attacker)
            {
                detail = "protected another tank's aggro";
                return false;
            }
            if (!bypassCooldown && Time.time < marker.NextTauntAt)
            {
                detail = "Grave Challenge is cooling down";
                return false;
            }
            if (!bypassCooldown)
                marker.NextTauntAt = Time.time + GraveChallengeCooldownSeconds;

            if (enemy.AggroTable == null)
                enemy.AggroTable = new List<AggroSlot>();
            int highestHate = enemy.AggroTable
                .Where(slot => slot != null && slot.Player != null &&
                    slot.Player.Alive)
                .Select(slot => slot.Hate)
                .DefaultIfEmpty(0).Max();
            int hateLead = Mathf.Max(MinimumTauntHateLead,
                (attacker.MyStats?.Level ?? 1) * 3);
            AggroSlot petSlot = enemy.GetAggroSlot(attacker);
            int previousHate = petSlot?.Hate ?? 0;
            int desiredHate = Mathf.Max(previousHate, highestHate + hateLead);
            if (petSlot == null)
                enemy.AggroTable.Add(new AggroSlot(attacker, desiredHate));
            else
                petSlot.Hate = desiredHate;
            enemy.ForceAggroOn(attacker);
            attacker.UnderThreat = Mathf.Max(attacker.UnderThreat, 60f);
            bool succeeded = enemy.CurrentAggroTarget == attacker &&
                enemy.GetAggroSlot(attacker)?.Hate >= desiredHate;
            if (succeeded) marker.SuccessfulTaunts++;
            detail = $"hate {previousHate}->{desiredHate}, highest was " +
                $"{highestHate}, target switched={succeeded}";
            return succeeded;
        }

        private static void CaptureInventoryBaseline(NecromancerPetMarker marker,
            Inventory inv)
        {
            marker.BaseInventoryHp = inv.ItemHP;
            marker.BaseInventoryAc = inv.ItemAC;
            marker.BaseInventoryMana = inv.ItemMana;
            marker.BaseInventoryStr = inv.ItemStr;
            marker.BaseInventoryEnd = inv.ItemEnd;
            marker.BaseInventoryDex = inv.ItemDex;
            marker.BaseInventoryAgi = inv.ItemAgi;
            marker.BaseInventoryInt = inv.ItemInt;
            marker.BaseInventoryWis = inv.ItemWis;
            marker.BaseInventoryCha = inv.ItemCha;
            marker.BaseInventoryRes = inv.ItemRes;
            marker.BaseInventoryMr = inv.ItemMR;
            marker.BaseInventoryEr = inv.ItemER;
            marker.BaseInventoryPr = inv.ItemPR;
            marker.BaseInventoryVr = inv.ItemVR;
        }

        internal static void ApplyEscrowToActivePet()
        {
            ApplyEscrowToPet(ActivePet);
        }

        internal static void ResetWeaponAuditCounters()
        {
            BowAttackAttemptCount = 0;
            BowProjectileLaunchCount = 0;
            BowProjectileDeliveryCount = 0;
            BowDamageDelivered = 0;
            MeleeAttackAttemptCount = 0;
            ActivePet?.GetComponent<NecromancerBowAnimationDriver>()?
                .ResetAudit();
        }

        internal static bool UsesEquippedBow(NPC pet)
        {
            if (pet == null ||
                pet.GetComponent<NecromancerPetMarker>() == null)
                return false;
            Item primary = NecromancerPetEquipment.Resolve(
                NecromancerPetEquipment.Primary);
            return primary != null &&
                (primary.IsBow ||
                 primary.ThisWeaponType == Item.WeaponType.TwoHandBow);
        }

        internal static void RecordMeleeAttackAttempt(NPC pet)
        {
            if (pet != null &&
                pet.GetComponent<NecromancerPetMarker>() != null)
                MeleeAttackAttemptCount++;
        }

        /// <summary>
        /// True when the thrall is fighting with bare hands — no weapon and no
        /// bow projected from the Thrall Bag.
        /// </summary>
        internal static bool IsUnarmed(NPC pet)
        {
            if (pet == null ||
                pet.GetComponent<NecromancerPetMarker>() == null) return false;
            Inventory inventory = pet.GetComponent<Inventory>();
            return inventory != null && !inventory.PrimaryWeapon &&
                !inventory.PrimaryBow;
        }


        internal static bool TryLaunchEquippedBowAttack(NPC pet,
            Character target, out string detail)
        {
            detail = null;
            BowAttackAttemptCount++;
            Item bow = NecromancerPetEquipment.Resolve(
                NecromancerPetEquipment.Primary);
            Character source = pet?.GetChar();
            Stats stats = source?.MyStats;
            GameObject arrowPrefab = GameData.Misc?.ArcheryArrows != null &&
                GameData.Misc.ArcheryArrows.Count > 0
                    ? GameData.Misc.ArcheryArrows[0] : null;
            if (pet == null || source == null || stats == null ||
                target == null || !target.Alive || bow == null ||
                (!bow.IsBow &&
                 bow.ThisWeaponType != Item.WeaponType.TwoHandBow) ||
                arrowPrefab == null)
            {
                detail = "pet, target, equipped bow, or native arrow prefab is unavailable";
                return false;
            }

            try
            {
                Animator animator = source.GetMyAnim();
                if (animator != null && animator.parameters.Any(parameter =>
                        parameter.type ==
                            AnimatorControllerParameterType.Trigger &&
                        string.Equals(parameter.name, "FireBow",
                            StringComparison.Ordinal)))
                    animator.SetTrigger("FireBow");
                NecromancerBowAnimationDriver bowAnimation =
                    pet.GetComponent<NecromancerBowAnimationDriver>();
                if (bowAnimation == null)
                    bowAnimation = pet.gameObject
                        .AddComponent<NecromancerBowAnimationDriver>();
                bowAnimation.Play(pet, target);
                GameObject arrowObject = UnityEngine.Object.Instantiate(
                    arrowPrefab,
                    pet.transform.position + Vector3.up +
                        pet.transform.forward,
                    pet.transform.rotation);
                WandBolt arrow = arrowObject?.GetComponent<WandBolt>();
                if (arrow == null)
                {
                    if (arrowObject != null)
                        UnityEngine.Object.Destroy(arrowObject);
                    detail = "native arrow prefab has no WandBolt";
                    return false;
                }
                Spell proc = bow.BowEffect != null &&
                    UnityEngine.Random.Range(0, 100) < bow.BowProcChance
                        ? bow.BowEffect : stats.CheckSEProcsOnly();
                int damage = Mathf.Max(1, pet.BaseAtkDmg);
                float speed = bow.BowArrowSpeed > 0f
                    ? bow.BowArrowSpeed : 35f;
                arrow.LoadArrow(damage, proc, target, source, speed,
                    GameData.DamageType.Physical, bow.BowAttackSound);
                BowProjectileLaunchCount++;
                detail = $"launched {bow.ItemName} for base damage {damage} " +
                    $"at speed {speed:F1}";
                return true;
            }
            catch (Exception exception)
            {
                detail = exception.GetBaseException().Message;
                SkillsPlugin.Log?.LogError(
                    "Bone Thrall bow attack failed: " + exception);
                return false;
            }
        }

        internal static void RecordBowProjectileDelivery(WandBolt arrow,
            int targetHealthBefore)
        {
            Character source = arrow?.SourceChar;
            if (source?.GetComponent<NecromancerPetMarker>() == null) return;
            BowProjectileDeliveryCount++;
            int currentHealth = arrow.TargetChar?.MyStats?.CurrentHP ??
                targetHealthBefore;
            BowDamageDelivered += Mathf.Max(0,
                targetHealthBefore - currentHealth);
        }

        /// <summary>
        /// Applies a visible, fully statted QA weapon without mutating the
        /// character-scoped pet escrow. The progression DPS harness uses this
        /// to model a realistically equipped Bone Thrall at every test level
        /// while keeping the player's durable item state completely untouched.
        /// </summary>
        internal static bool ApplyTransientBenchmarkWeapon(NPC pet,
            Item primary, int quality, int expectedItemLevel,
            out string detail)
        {
            detail = null;
            NecromancerPetMarker marker = pet != null
                ? pet.GetComponent<NecromancerPetMarker>() : null;
            Stats stats = pet != null ? pet.GetComponent<Stats>() : null;
            Inventory inv = pet != null ? pet.GetComponent<Inventory>() : null;
            bool oneHanded = primary != null &&
                (primary.ThisWeaponType == Item.WeaponType.OneHandMelee ||
                 primary.ThisWeaponType == Item.WeaponType.OneHandDagger) &&
                (primary.RequiredSlot == Item.SlotType.Primary ||
                 primary.RequiredSlot == Item.SlotType.PrimaryOrSecondary);
            int ownerLevel = marker?.Owner?.MyStats?.Level ?? 0;
            int ownerProgressionCeiling = ownerLevel == 35
                ? 32 : Mathf.Max(1, ownerLevel - 3);
            if (marker == null || stats == null || inv == null ||
                !oneHanded || primary.Shield || primary.WeaponDmg <= 0 ||
                expectedItemLevel < 1 ||
                expectedItemLevel > ownerProgressionCeiling ||
                primary.ItemLevel != expectedItemLevel)
            {
                detail = $"the transient pet loadout requires a visible " +
                    $"level-{expectedItemLevel} one-handed weapon within the " +
                    $"level-{ownerProgressionCeiling} progression ceiling " +
                    $"for its level-{ownerLevel} owner";
                return false;
            }

            float healthRatio = stats.CurrentMaxHP > 0
                ? Mathf.Clamp01((float)stats.CurrentHP / stats.CurrentMaxHP) : 1f;
            RestoreInventoryBaseline(marker, inv);
            stats.BaseHP = marker.BasePetHp;
            pet.HardSetAC = marker.BasePetArmor;
            pet.BaseAtkDmg = marker.BasePetDamage;
            pet.OHAtkDmg = marker.BasePetOffhandDamage;
            ApplyItemStats(inv, primary, quality, marker.Tier);
            pet.HardSetAC += Mathf.Max(0,
                inv.ItemAC - marker.BaseInventoryAc);
            int weaponContribution = Mathf.Clamp(primary.CalcDmg(
                primary.WeaponDmg, quality), 0, TierWeaponCap[marker.Tier]);
            pet.BaseAtkDmg += weaponContribution;
            stats.BaseHP = marker.BasePetHp + Mathf.Max(0,
                inv.ItemHP - marker.BaseInventoryHp);
            RebuildEquipmentVisuals(pet, primary, null);
            stats.CalcStats();
            stats.CurrentHP = Mathf.Clamp(Mathf.RoundToInt(
                stats.CurrentMaxHP * healthRatio), 1, stats.CurrentMaxHP);
            NormalizePetLighting(pet);

            string expectedName = "CS_PetGear_" +
                (primary.EquipmentToActivate ?? "");
            Transform visual = string.IsNullOrEmpty(primary.EquipmentToActivate)
                ? null : pet.GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(value => string.Equals(value.name,
                        expectedName, StringComparison.Ordinal));
            bool rendered = visual != null && visual.gameObject.activeInHierarchy &&
                visual.GetComponentsInChildren<Renderer>(true).Any(renderer =>
                    renderer != null && renderer.enabled &&
                    renderer.gameObject.activeInHierarchy);
            detail = $"{primary.ItemName} L{primary.ItemLevel} q{quality}, " +
                $"weapon {primary.WeaponDmg} capped to +{weaponContribution}, " +
                $"pet damage {marker.BasePetDamage}->{pet.BaseAtkDmg}, " +
                $"model={primary.EquipmentToActivate ?? "none"}, rendered={rendered}";
            return rendered;
        }

        private static void ApplyEscrowToPet(NPC pet)
        {
            if (pet == null) return;
            NecromancerPetMarker marker = pet.GetComponent<NecromancerPetMarker>();
            Stats stats = pet.GetComponent<Stats>();
            Inventory inv = pet.GetComponent<Inventory>();
            if (marker == null || stats == null || inv == null) return;

            float healthRatio = stats.CurrentMaxHP > 0
                ? Mathf.Clamp01((float)stats.CurrentHP / stats.CurrentMaxHP) : 1f;
            RestoreInventoryBaseline(marker, inv);
            stats.BaseHP = marker.BasePetHp;
            pet.HardSetAC = marker.BasePetArmor;
            pet.BaseAtkDmg = marker.BasePetDamage;
            pet.OHAtkDmg = marker.BasePetOffhandDamage;

            Item primary = NecromancerPetEquipment.Resolve(
                NecromancerPetEquipment.Primary);
            Item secondary = NecromancerPetEquipment.Resolve(
                NecromancerPetEquipment.Secondary);
            if (primary != null)
            {
                int quality = NecromancerPetEquipment.Primary.Quality;
                ApplyItemStats(inv, primary, quality, marker.Tier);
                if (primary.WeaponDmg > 0)
                    pet.BaseAtkDmg += Mathf.Clamp(primary.CalcDmg(
                        primary.WeaponDmg, quality), 0,
                        TierWeaponCap[marker.Tier]);
            }
            if (secondary != null)
            {
                int quality = NecromancerPetEquipment.Secondary.Quality;
                ApplyItemStats(inv, secondary, quality, marker.Tier);
                if (!secondary.Shield && secondary.WeaponDmg > 0)
                    pet.OHAtkDmg = Mathf.Clamp(secondary.CalcDmg(
                        secondary.WeaponDmg, quality), 0,
                        TierOffhandCap[marker.Tier]);
            }

            // NPC CalcStats uses HardSetAC instead of Inventory.ItemAC. Add
            // the aggregate projected AC from both equipped items exactly
            // once so weapon AC and shield AC both become live mitigation.
            pet.HardSetAC += Mathf.Max(0,
                inv.ItemAC - marker.BaseInventoryAc);

            stats.BaseHP = marker.BasePetHp + Mathf.Max(0,
                inv.ItemHP - marker.BaseInventoryHp);
            RebuildEquipmentVisuals(pet, primary, secondary);
            stats.CalcStats();
            stats.CurrentHP = Mathf.Clamp(Mathf.RoundToInt(
                stats.CurrentMaxHP * healthRatio), 1, stats.CurrentMaxHP);
            NormalizePetLighting(pet);
        }

        private static void NormalizePetLighting(NPC pet)
        {
            if (pet == null) return;
            int adjusted = 0;
            foreach (Light light in pet.GetComponentsInChildren<Light>(true))
            {
                if (light == null) continue;
                float range = Mathf.Clamp(light.range, 0f, MaxPetLightRange);
                float intensity = Mathf.Clamp(light.intensity, 0f,
                    MaxPetLightIntensity);
                if (!Mathf.Approximately(light.range, range) ||
                    !Mathf.Approximately(light.intensity, intensity) ||
                    light.shadows != LightShadows.None)
                    adjusted++;
                light.range = range;
                light.intensity = intensity;
                light.shadows = LightShadows.None;
            }
            if (adjusted > 0)
                SkillsPlugin.Log.LogInfo(
                    $"Necromancer pet lighting normalized on {adjusted} light(s).");
        }

        private static void RestoreInventoryBaseline(NecromancerPetMarker marker,
            Inventory inv)
        {
            inv.ItemHP = marker.BaseInventoryHp;
            inv.ItemAC = marker.BaseInventoryAc;
            inv.ItemMana = marker.BaseInventoryMana;
            inv.ItemStr = marker.BaseInventoryStr;
            inv.ItemEnd = marker.BaseInventoryEnd;
            inv.ItemDex = marker.BaseInventoryDex;
            inv.ItemAgi = marker.BaseInventoryAgi;
            inv.ItemInt = marker.BaseInventoryInt;
            inv.ItemWis = marker.BaseInventoryWis;
            inv.ItemCha = marker.BaseInventoryCha;
            inv.ItemRes = marker.BaseInventoryRes;
            inv.ItemMR = marker.BaseInventoryMr;
            inv.ItemER = marker.BaseInventoryEr;
            inv.ItemPR = marker.BaseInventoryPr;
            inv.ItemVR = marker.BaseInventoryVr;
        }

        private static void ApplyItemStats(Inventory inv, Item item, int quality,
            int tier)
        {
            tier = Mathf.Clamp(tier, 1, 5);
            int hpCap = 50 * tier * tier;
            int acCap = item.Shield
                ? TierShieldCap[tier] : 10 * tier * tier;
            int statCap = 5 * tier;
            int resistCap = 5 * tier;
            inv.ItemHP += Mathf.Clamp(item.CalcACHPMC(item.HP, quality),
                -hpCap, hpCap);
            inv.ItemAC += Mathf.Clamp(item.CalcAC(item.AC, quality),
                -acCap, acCap);
            inv.ItemMana += Mathf.Clamp(item.CalcACHPMC(item.Mana, quality),
                -hpCap, hpCap);
            inv.ItemStr += Mathf.Clamp(item.CalcStat(item.Str, quality),
                -statCap, statCap);
            inv.ItemEnd += Mathf.Clamp(item.CalcStat(item.End, quality),
                -statCap, statCap);
            inv.ItemDex += Mathf.Clamp(item.CalcStat(item.Dex, quality),
                -statCap, statCap);
            inv.ItemAgi += Mathf.Clamp(item.CalcStat(item.Agi, quality),
                -statCap, statCap);
            inv.ItemInt += Mathf.Clamp(item.CalcStat(item.Int, quality),
                -statCap, statCap);
            inv.ItemWis += Mathf.Clamp(item.CalcStat(item.Wis, quality),
                -statCap, statCap);
            inv.ItemCha += Mathf.Clamp(item.CalcStat(item.Cha, quality),
                -statCap, statCap);
            inv.ItemRes += Mathf.Clamp(item.CalcRes(item.Res, quality),
                -resistCap, resistCap);
            inv.ItemMR += Mathf.Clamp(item.CalcResists(item.MR, quality),
                -resistCap, resistCap);
            inv.ItemER += Mathf.Clamp(item.CalcResists(item.ER, quality),
                -resistCap, resistCap);
            inv.ItemPR += Mathf.Clamp(item.CalcResists(item.PR, quality),
                -resistCap, resistCap);
            inv.ItemVR += Mathf.Clamp(item.CalcResists(item.VR, quality),
                -resistCap, resistCap);
        }

        internal static void ApplyPersistentStatCurve(Stats stats)
        {
            NecromancerPetMarker marker = stats?.GetComponent<NecromancerPetMarker>();
            Inventory inv = stats?.MyInv;
            if (marker == null || marker.BasePetHp <= 0 || inv == null ||
                !stats.OverrideHPforNPC) return;
            int gearHp = Mathf.Max(0, inv.ItemHP - marker.BaseInventoryHp);
            int buffHp = 0;
            if (stats.StatusEffects != null)
            {
                foreach (StatusEffect effect in stats.StatusEffects)
                {
                    Spell spell = effect?.Effect;
                    if (spell == null || !NecromancerCatalog.TryGetDefinition(
                        spell, out NecromancerSpellDefinition definition) ||
                        definition.Kind != NecromancerSpellKind.PetBuff) continue;
                    buffHp += Mathf.Max(0, spell.HP);
                }
            }
            int maximum = Mathf.Max(1, marker.BasePetHp + gearHp + buffHp);
            stats.BaseHP = maximum;
            stats.CurrentMaxHP = maximum;
            stats.CurrentHP = Mathf.Clamp(stats.CurrentHP, 0, maximum);
        }

        private static void RebuildEquipmentVisuals(NPC pet, Item primary,
            Item secondary)
        {
            foreach (NecromancerPetVisualTag oldVisual in
                pet.GetComponentsInChildren<NecromancerPetVisualTag>(true))
                if (oldVisual != null)
                    UnityEngine.Object.Destroy(oldVisual.gameObject);
            Transform old = pet.transform.Find(VisualRootName);
            if (old != null) UnityEngine.Object.Destroy(old.gameObject);
            var root = new GameObject(VisualRootName);
            root.transform.SetParent(pet.transform, false);

            ApplyPlayerCompatibleWeaponRig(pet, primary, secondary);
            if (primary != null)
                AttachVisual(root.transform,
                    GetEquipmentHand(pet, !UsesLeftHand(primary, true)),
                    primary, true);
            if (secondary != null)
                AttachVisual(root.transform,
                    GetEquipmentHand(pet,
                        !UsesLeftHand(secondary, false)),
                    secondary, false);
            // The summoned-skeleton prefab ships with its own spectral sword.
            // A thrall's armament comes from the Thrall Bag and nowhere else,
            // so keep the prefab weapon hidden in every case: bag weapon means
            // that weapon, empty bag means bare hands.
            SetNativeRightHandWeaponVisible(pet, false);
        }

        private static void ApplyPlayerCompatibleWeaponRig(NPC pet,
            Item primary, Item secondary)
        {
            if (pet == null) return;
            Animator petAnimator = pet.GetComponent<Animator>();
            Animator playerAnimator =
                GameData.PlayerControl?.Myself?.GetMyAnim();
            NecromancerPetMarker marker =
                pet.GetComponent<NecromancerPetMarker>();
            if (petAnimator == null || playerAnimator == null ||
                marker == null) return;

            if (!EnsurePlayerAvatarRig(pet, petAnimator, playerAnimator,
                    marker, out string rigDetail))
            {
                SkillsPlugin.Log?.LogWarning(
                    "Bone Thrall player-avatar rig unavailable: " +
                    rigDetail);
                return;
            }

            // Never replace the native Animator avatar/controller. Its skin
            // bind poses are coupled to them; assigning the player's avatar
            // makes the skeleton disappear even though renderer bounds remain.
            // The separate player equipment rig follows this native pose.
            marker.PlayerEquipmentRig?.SyncNow();

            bool dagger = primary != null &&
                primary.ThisWeaponType == Item.WeaponType.OneHandDagger;
            bool twoHandMelee = primary != null &&
                primary.ThisWeaponType == Item.WeaponType.TwoHandMelee;
            bool twoHandStaff = primary != null &&
                primary.ThisWeaponType == Item.WeaponType.TwoHandStaff;
            bool bow = primary != null &&
                (primary.IsBow ||
                 primary.ThisWeaponType == Item.WeaponType.TwoHandBow);
            bool oneHand = primary != null &&
                primary.ThisWeaponType == Item.WeaponType.OneHandMelee;
            bool offhandWeapon = secondary != null &&
                !secondary.Shield && secondary.WeaponDmg > 0;

            // NPC.Start turns 1HSmall on for every NPC, armed or not, so this
            // is the stance every other empty-handed NPC in Erenshor uses and
            // the one that routes their MeleeSwing trigger to a real animation.
            // The thrall follows the same rule; its weapon visual is hidden
            // separately when the Thrall Bag is empty.
            SetAnimatorBool(petAnimator, "1HSmall",
                primary == null || oneHand);
            SetAnimatorBool(petAnimator, "1HDagger", dagger);
            // Erenshor's ItemIcon equip path uses 1HSmall for one-handed
            // swords; 1HSword is retained by the controller but not selected
            // by native item equip code.
            SetAnimatorBool(petAnimator, "1HSword", false);
            SetAnimatorBool(petAnimator, "2HMelee", twoHandMelee);
            SetAnimatorBool(petAnimator, "2HStaff", twoHandStaff);
            SetAnimatorBool(petAnimator, "DoubleAttack", false);
            SetAnimatorBool(petAnimator, "DualWield", false);
            SetAnimatorBool(petAnimator, "OHDoubleAttack", false);

            pet.OneHand = oneHand || dagger;
            pet.TwoHandSword = twoHandMelee;
            pet.TwoHandStaff = twoHandStaff;
            pet.DualWield = offhandWeapon;
            Character petCharacter = pet.GetChar();
            if (petCharacter != null)
            {
                float baseRange = marker.BaseAttackRange > 0f
                    ? marker.BaseAttackRange : 4f;
                petCharacter.AttackRange = bow
                    ? ResolveBowAttackRange(primary)
                    : baseRange;
            }

            Inventory inventory = pet.GetComponent<Inventory>();
            if (inventory != null)
            {
                inventory.PrimaryWeapon =
                    primary != null && primary.WeaponDmg > 0;
                inventory.PrimaryBow = bow;
                inventory.TwoHandPrimary =
                    twoHandMelee || twoHandStaff || bow;
                inventory.SecondaryWeapon = offhandWeapon;
                inventory.SecondaryShield =
                    secondary != null && secondary.Shield;
                try { NpcCheckForBowMethod?.Invoke(pet, null); }
                catch (TargetInvocationException exception)
                {
                    SkillsPlugin.Log?.LogWarning(
                        "Bone Thrall bow-pose refresh failed: " +
                        (exception.InnerException ?? exception).Message);
                }
            }

            UseSkill skills = pet.GetComponent<UseSkill>();
            Skill dualWield = GameData.SkillDatabase?
                .GetSkillByName("Dual Wield");
            if (skills?.KnownSkills != null && dualWield != null)
            {
                if (offhandWeapon &&
                    !skills.KnownSkills.Contains(dualWield))
                {
                    skills.KnownSkills.Add(dualWield);
                    marker.AddedDualWieldSkill = true;
                }
                else if (!offhandWeapon && marker.AddedDualWieldSkill)
                {
                    skills.KnownSkills.Remove(dualWield);
                    marker.AddedDualWieldSkill = false;
                }
            }
        }

        private static float ResolveBowAttackRange(Item bow)
        {
            float authored = bow?.BowRange ?? 0f;
            return Mathf.Clamp(Mathf.Max(MinimumBowAttackRange, authored),
                MinimumBowAttackRange, 30f);
        }

        private static bool EnsurePlayerAvatarRig(NPC pet,
            Animator petAnimator, Animator playerAnimator,
            NecromancerPetMarker marker, out string detail)
        {
            marker.NativePetAvatar ??= petAnimator.avatar;
            marker.NativePetController ??=
                petAnimator.runtimeAnimatorController;
            if (marker.PlayerAvatarRig != null)
            {
                detail = $"ready with {marker.PlayerRigMappedBoneCount}/" +
                    $"{marker.PlayerRigSourceBoneCount} mapped bone(s)";
                return true;
            }

            Transform nativeRoot = pet.transform.Find("Root");
            Transform playerRoot = playerAnimator.transform.Find("Root");
            if (nativeRoot == null || playerRoot == null)
            {
                detail = "the native pet or live player Root is unavailable";
                return false;
            }

            var requiredSource = new HashSet<Transform> { playerRoot };
            for (int index = 0;
                index < (int)HumanBodyBones.LastBone; index++)
            {
                Transform source =
                    playerAnimator.GetBoneTransform((HumanBodyBones)index);
                if (IsTransformUnder(source, playerRoot))
                    AddRigAncestors(requiredSource, source, playerRoot);
            }
            Transform playerLeft =
                playerAnimator.GetBoneTransform(HumanBodyBones.LeftHand);
            Transform playerRight =
                playerAnimator.GetBoneTransform(HumanBodyBones.RightHand);
            string leftPath = GetRelativeRigPath(playerLeft, playerRoot);
            string rightPath = GetRelativeRigPath(playerRight, playerRoot);
            if (leftPath == null || rightPath == null)
            {
                detail = "the live player avatar has no usable hand bones";
                return false;
            }

            var rigObject = new GameObject("CS_PlayerEquipmentRig");
            rigObject.transform.SetParent(pet.transform, false);
            var clonedByPath = new Dictionary<string, Transform>(
                StringComparer.Ordinal);
            Transform clonedRoot = CloneRigTransforms(playerRoot,
                rigObject.transform, requiredSource, clonedByPath, "");
            if (clonedRoot == null)
            {
                UnityEngine.Object.Destroy(rigObject);
                detail = "the player avatar hierarchy could not be cloned";
                return false;
            }

            if (!clonedByPath.TryGetValue(leftPath,
                    out Transform clonedLeft) ||
                !clonedByPath.TryGetValue(rightPath,
                    out Transform clonedRight) ||
                FindRigPath(nativeRoot, leftPath) == null ||
                FindRigPath(nativeRoot, rightPath) == null)
            {
                UnityEngine.Object.Destroy(rigObject);
                detail = "the player and native skeleton hand paths differ";
                return false;
            }

            var follower =
                rigObject.AddComponent<NecromancerPlayerEquipmentRig>();
            int mapped = follower.Configure(nativeRoot, clonedRoot,
                clonedByPath);
            if (mapped < 10)
            {
                UnityEngine.Object.Destroy(rigObject);
                detail = $"only {mapped}/{clonedByPath.Count} player " +
                    "avatar bones map onto the native skeleton";
                return false;
            }

            marker.PlayerAvatarRig = clonedRoot;
            marker.PlayerRigLeftHand = clonedLeft;
            marker.PlayerRigRightHand = clonedRight;
            marker.PlayerRigSourceBoneCount = clonedByPath.Count;
            marker.PlayerRigMappedBoneCount = mapped;
            marker.PlayerEquipmentRig = follower;
            detail = $"cloned the live player humanoid rig and mapped " +
                $"{mapped}/{clonedByPath.Count} bone(s) onto the intact " +
                "native skeleton";
            SkillsPlugin.Log?.LogInfo(
                "Bone Thrall player-avatar rig ready: " + detail + ".");
            return true;
        }

        private static Transform CloneRigTransforms(Transform source,
            Transform parent, HashSet<Transform> required,
            Dictionary<string, Transform> clonedByPath, string path)
        {
            if (source == null || !required.Contains(source)) return null;
            var cloneObject = new GameObject(source.name);
            cloneObject.layer = parent.gameObject.layer;
            Transform clone = cloneObject.transform;
            clone.SetParent(parent, false);
            clone.localPosition = source.localPosition;
            clone.localRotation = source.localRotation;
            clone.localScale = source.localScale;
            clonedByPath[path] = clone;

            foreach (Transform child in source)
            {
                string childPath = string.IsNullOrEmpty(path)
                    ? child.name : path + "/" + child.name;
                CloneRigTransforms(child, clone, required, clonedByPath,
                    childPath);
            }
            return clone;
        }

        private static void AddRigAncestors(HashSet<Transform> required,
            Transform value, Transform root)
        {
            for (Transform current = value; current != null;
                current = current.parent)
            {
                required.Add(current);
                if (current == root) break;
            }
        }

        private static string GetRelativeRigPath(Transform value,
            Transform root)
        {
            if (value == null || root == null) return null;
            if (value == root) return "";
            var parts = new List<string>();
            Transform current = value;
            while (current != null && current != root)
            {
                parts.Add(current.name);
                current = current.parent;
            }
            if (current != root) return null;
            parts.Reverse();
            return string.Join("/", parts);
        }

        private static Transform FindRigPath(Transform root, string path)
        {
            if (root == null || path == null) return null;
            return path.Length == 0 ? root : root.Find(path);
        }

        private static bool IsTransformUnder(Transform value,
            Transform root)
        {
            return value != null && root != null &&
                (value == root || value.IsChildOf(root));
        }

        private static void SetAnimatorBool(Animator animator, string name,
            bool value)
        {
            if (animator == null || string.IsNullOrEmpty(name)) return;
            if (!animator.parameters.Any(parameter =>
                    parameter.type ==
                        AnimatorControllerParameterType.Bool &&
                    string.Equals(parameter.name, name,
                        StringComparison.Ordinal)))
                return;
            animator.SetBool(name, value);
        }

        internal static bool ValidateActivePetEquipment(out string detail)
        {
            NPC pet = ActivePet;
            NecromancerPetMarker marker = pet != null
                ? pet.GetComponent<NecromancerPetMarker>() : null;
            if (pet == null || marker == null)
            {
                detail = "no active player-owned bone thrall";
                return false;
            }

            Item primary = NecromancerPetEquipment.Resolve(
                NecromancerPetEquipment.Primary);
            Item secondary = NecromancerPetEquipment.Resolve(
                NecromancerPetEquipment.Secondary);
            bool primaryVisual = ValidateAttachedVisual(pet, primary, true,
                out string primaryDetail);
            bool secondaryVisual = ValidateAttachedVisual(pet, secondary, false,
                out string secondaryDetail);
            bool primaryStats = primary == null || primary.WeaponDmg <= 0 ||
                pet.BaseAtkDmg > marker.BasePetDamage;
            bool secondaryStats = secondary == null ||
                (secondary.Shield
                    ? pet.HardSetAC > marker.BasePetArmor
                    : secondary.WeaponDmg <= 0 || pet.OHAtkDmg > 0);
            bool itemBonuses = ValidateProjectedItemBonuses(pet, marker,
                primary, secondary, out string bonusDetail);
            bool serialized = NecromancerPetEquipment.IsSerializedDataValid(
                NecromancerPetEquipment.Serialize());
            Animator petAnimator = pet.GetComponent<Animator>();
            Animator playerAnimator =
                GameData.PlayerControl?.Myself?.GetMyAnim();
            bool playerEquipmentRig = HasPlayerAvatarEquipmentRig(pet,
                petAnimator, marker, out string playerRigDetail);
            bool playerRig = playerAnimator != null &&
                playerEquipmentRig;
            Light[] lights = pet.GetComponentsInChildren<Light>(true);
            Light[] activeLights = lights.Where(light => light != null &&
                light.enabled && light.gameObject.activeInHierarchy).ToArray();
            float maximumLightRange = activeLights.Length == 0 ? 0f :
                activeLights.Max(light => light.range);
            float maximumLightIntensity = activeLights.Length == 0 ? 0f :
                activeLights.Max(light => light.intensity);
            bool lighting = maximumLightRange <= MaxPetLightRange + 0.001f &&
                maximumLightIntensity <= MaxPetLightIntensity + 0.001f &&
                activeLights.All(light => light.shadows == LightShadows.None);
            bool valid = primaryVisual && secondaryVisual && primaryStats &&
                secondaryStats && itemBonuses && serialized && playerRig &&
                lighting;
            detail = $"primary={primaryDetail}; secondary={secondaryDetail}; " +
                $"damage={marker.BasePetDamage}->{pet.BaseAtkDmg}; " +
                $"armor={marker.BasePetArmor}->{pet.HardSetAC}; " +
                $"bonuses={bonusDetail}; " +
                $"serialized={serialized}; player-rig={playerRig} " +
                $"[{playerRigDetail}]; " +
                $"lighting={lighting} " +
                $"({activeLights.Length} active, max range " +
                $"{maximumLightRange:F2}, intensity {maximumLightIntensity:F2})";
            if (!valid)
                SkillsPlugin.Log?.LogWarning(
                    "Necromancer pet equipment validation failed: " +
                    detail);
            return valid;
        }

        private static bool ValidateProjectedItemBonuses(NPC pet,
            NecromancerPetMarker marker, Item primary, Item secondary,
            out string detail)
        {
            Inventory inv = pet?.GetComponent<Inventory>();
            if (inv == null || marker == null)
            {
                detail = "pet inventory unavailable";
                return false;
            }

            int tier = Mathf.Clamp(marker.Tier, 1, 5);
            int hpCap = 50 * tier * tier;
            int statCap = 5 * tier;
            int resistCap = 5 * tier;
            int primaryQuality = primary != null
                ? NecromancerPetEquipment.Primary.Quality : 1;
            int secondaryQuality = secondary != null
                ? NecromancerPetEquipment.Secondary.Quality : 1;

            int Pair(Func<Item, int, int> projection) =>
                (primary != null ? projection(primary, primaryQuality) : 0) +
                (secondary != null ? projection(secondary,
                    secondaryQuality) : 0);
            int Ac(Item item, int quality)
            {
                int cap = item.Shield
                    ? TierShieldCap[tier] : 10 * tier * tier;
                return Mathf.Clamp(item.CalcAC(item.AC, quality), -cap, cap);
            }

            bool valid =
                inv.ItemHP - marker.BaseInventoryHp == Pair((item, quality) =>
                    Mathf.Clamp(item.CalcACHPMC(item.HP, quality),
                        -hpCap, hpCap)) &&
                inv.ItemAC - marker.BaseInventoryAc == Pair(Ac) &&
                pet.HardSetAC == marker.BasePetArmor + Mathf.Max(0, Pair(Ac)) &&
                inv.ItemMana - marker.BaseInventoryMana == Pair((item, quality) =>
                    Mathf.Clamp(item.CalcACHPMC(item.Mana, quality),
                        -hpCap, hpCap)) &&
                inv.ItemStr - marker.BaseInventoryStr == Pair((item, quality) =>
                    Mathf.Clamp(item.CalcStat(item.Str, quality),
                        -statCap, statCap)) &&
                inv.ItemEnd - marker.BaseInventoryEnd == Pair((item, quality) =>
                    Mathf.Clamp(item.CalcStat(item.End, quality),
                        -statCap, statCap)) &&
                inv.ItemDex - marker.BaseInventoryDex == Pair((item, quality) =>
                    Mathf.Clamp(item.CalcStat(item.Dex, quality),
                        -statCap, statCap)) &&
                inv.ItemAgi - marker.BaseInventoryAgi == Pair((item, quality) =>
                    Mathf.Clamp(item.CalcStat(item.Agi, quality),
                        -statCap, statCap)) &&
                inv.ItemInt - marker.BaseInventoryInt == Pair((item, quality) =>
                    Mathf.Clamp(item.CalcStat(item.Int, quality),
                        -statCap, statCap)) &&
                inv.ItemWis - marker.BaseInventoryWis == Pair((item, quality) =>
                    Mathf.Clamp(item.CalcStat(item.Wis, quality),
                        -statCap, statCap)) &&
                inv.ItemCha - marker.BaseInventoryCha == Pair((item, quality) =>
                    Mathf.Clamp(item.CalcStat(item.Cha, quality),
                        -statCap, statCap)) &&
                inv.ItemRes - marker.BaseInventoryRes == Pair((item, quality) =>
                    Mathf.Clamp(item.CalcRes(item.Res, quality),
                        -resistCap, resistCap)) &&
                inv.ItemMR - marker.BaseInventoryMr == Pair((item, quality) =>
                    Mathf.Clamp(item.CalcResists(item.MR, quality),
                        -resistCap, resistCap)) &&
                inv.ItemER - marker.BaseInventoryEr == Pair((item, quality) =>
                    Mathf.Clamp(item.CalcResists(item.ER, quality),
                        -resistCap, resistCap)) &&
                inv.ItemPR - marker.BaseInventoryPr == Pair((item, quality) =>
                    Mathf.Clamp(item.CalcResists(item.PR, quality),
                        -resistCap, resistCap)) &&
                inv.ItemVR - marker.BaseInventoryVr == Pair((item, quality) =>
                    Mathf.Clamp(item.CalcResists(item.VR, quality),
                        -resistCap, resistCap));

            detail = valid
                ? "HP/mana/AC, seven attributes and five resist fields match both equipped item qualities and tier caps"
                : $"projection mismatch: HP={inv.ItemHP - marker.BaseInventoryHp}, " +
                  $"mana={inv.ItemMana - marker.BaseInventoryMana}, " +
                  $"AC={inv.ItemAC - marker.BaseInventoryAc}, " +
                  $"liveAC={pet.HardSetAC - marker.BasePetArmor}";
            return valid;
        }

        private static bool HasPlayerAvatarEquipmentRig(NPC pet,
            Animator animator, NecromancerPetMarker marker,
            out string detail)
        {
            Transform rig = marker?.PlayerAvatarRig;
            Transform nativeRoot = pet?.transform.Find("Root");
            Transform left = marker?.PlayerRigLeftHand;
            Transform right = marker?.PlayerRigRightHand;
            bool hands = IsTransformUnder(left, rig) &&
                IsTransformUnder(right, rig) &&
                ReferenceEquals(GetEquipmentHand(pet, false), left) &&
                ReferenceEquals(GetEquipmentHand(pet, true), right);
            SkinnedMeshRenderer[] body = GetPetBodyRenderers(
                    pet?.transform)
                .OfType<SkinnedMeshRenderer>()
                .ToArray();
            int nativeBound = body.Count(renderer =>
                renderer.rootBone != null &&
                IsTransformUnder(renderer.rootBone, nativeRoot) &&
                (renderer.bones ?? Array.Empty<Transform>())
                    .Where(bone => bone != null)
                    .All(bone => IsTransformUnder(bone, nativeRoot)));
            bool following = marker?.PlayerEquipmentRig != null &&
                marker.PlayerEquipmentRig.IsFollowing(nativeRoot, rig);
            bool nativeAnimator = animator != null &&
                marker?.NativePetAvatar != null &&
                marker.NativePetController != null &&
                ReferenceEquals(animator.avatar,
                    marker.NativePetAvatar) &&
                ReferenceEquals(animator.runtimeAnimatorController,
                    marker.NativePetController);
            bool valid = rig != null && rig.gameObject.activeInHierarchy &&
                nativeRoot != null && nativeRoot.gameObject.activeInHierarchy &&
                hands && following && nativeAnimator && body.Length > 0 &&
                nativeBound == body.Length &&
                marker.PlayerRigMappedBoneCount >= 10 &&
                marker.PlayerRigMappedBoneCount <=
                    marker.PlayerRigSourceBoneCount;
            detail = $"root={(rig != null ? rig.name : "missing")}, " +
                $"hands={hands}, following={following}, " +
                $"mapped={marker?.PlayerRigMappedBoneCount ?? 0}/" +
                $"{marker?.PlayerRigSourceBoneCount ?? 0}, " +
                $"native-animator={nativeAnimator}, " +
                $"native-body={nativeBound}/{body.Length}";
            return valid;
        }

        private static bool ValidateAttachedVisual(NPC pet, Item item,
            bool primary, out string detail)
        {
            NecromancerPetGearItem state = primary
                ? NecromancerPetEquipment.Primary
                : NecromancerPetEquipment.Secondary;
            if (state == null)
            {
                detail = "empty";
                return item == null;
            }
            if (item == null || string.IsNullOrEmpty(item.EquipmentToActivate))
            {
                detail = "saved item or model is unresolved";
                return false;
            }

            string expectedName = "CS_PetGear_" + item.EquipmentToActivate;
            Transform visual = pet.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(value => string.Equals(value.name, expectedName,
                    StringComparison.Ordinal));
            Transform hand = GetEquipmentHand(pet,
                !UsesLeftHand(item, primary));
            bool attached = visual != null && hand != null &&
                visual.IsChildOf(hand);
            ModularParts sourceParts = GameData.PlayerModularParent?.Male;
            bool sourceLeft = UsesLeftHand(item, primary);
            Transform source = FindNamedChild(sourceLeft
                    ? sourceParts?.WeaponL : sourceParts?.WeaponR,
                item.EquipmentToActivate);
            Vector3 expectedLocalScale = source != null && hand != null
                ? DivideScale(source.lossyScale, hand.lossyScale)
                : Vector3.one;
            Vector3 expectedLocalPosition =
                source != null && source.parent != null && hand != null
                ? ConvertLocalPositionBetweenParents(source.localPosition,
                    source.parent.lossyScale, hand.lossyScale)
                : Vector3.zero;
            bool authoredPose = visual != null && source != null &&
                Vector3.Distance(visual.localPosition,
                    expectedLocalPosition) <= 0.0001f &&
                Quaternion.Angle(visual.localRotation,
                    source.localRotation) <= 0.01f &&
                RelativeScaleError(visual.localScale,
                    expectedLocalScale) <= 0.001f;
            bool rendered = visual != null && visual.gameObject.activeInHierarchy &&
                visual.GetComponentsInChildren<Renderer>(true).Any(renderer =>
                    renderer != null && renderer.enabled &&
                    renderer.gameObject.activeInHierarchy);
            bool scaled = visual != null &&
                visual.lossyScale.sqrMagnitude > 0.0001f;
            Renderer[] visualRenderers =
                visual?.GetComponentsInChildren<Renderer>(true) ??
                Array.Empty<Renderer>();
            float visualSize = GetLargestRenderedDimension(visualRenderers);
            Renderer[] bodyRenderers = GetPetBodyRenderers(pet.transform);
            float bodyHeight = GetRenderedHeight(bodyRenderers);
            GetVisualRatioRange(item, out _, out float minimumRatio,
                out float maximumRatio);
            float sizeRatio = bodyHeight > 0.001f
                ? visualSize / bodyHeight : float.PositiveInfinity;
            bool proportionate = visualSize > 0.001f &&
                sizeRatio >= minimumRatio && sizeRatio <= maximumRatio;
            float handDistance = GetRenderedBounds(visualRenderers,
                out Bounds visualBounds) && hand != null
                ? Mathf.Sqrt(visualBounds.SqrDistance(hand.position))
                : float.PositiveInfinity;
            float handDistanceRatio = bodyHeight > 0.001f
                ? handDistance / bodyHeight : float.PositiveInfinity;
            bool positioned = handDistanceRatio <= 0.12f;
            detail = $"{state.ItemName} q{state.Quality} model=" +
                $"{item.EquipmentToActivate} attached={attached} " +
                $"authored-pose={authoredPose} rendered={rendered} " +
                $"scaled={scaled} size/body={sizeRatio:F2} " +
                $"range=[{minimumRatio:F2},{maximumRatio:F2}] " +
                $"proportionate={proportionate} " +
                $"hand-gap/body={handDistanceRatio:F2} max=0.12 " +
                $"positioned={positioned} " +
                $"(visual={visualSize:F2}m body={bodyHeight:F2}m " +
                $"from {bodyRenderers.Length} body renderer(s))";
            return attached && authoredPose && rendered && scaled && proportionate &&
                positioned;
        }

        private static float RelativeScaleError(Vector3 actual,
            Vector3 expected)
        {
            return Mathf.Max(
                RelativeScaleAxisError(actual.x, expected.x),
                Mathf.Max(
                    RelativeScaleAxisError(actual.y, expected.y),
                    RelativeScaleAxisError(actual.z, expected.z)));
        }

        private static float RelativeScaleAxisError(float actual,
            float expected)
        {
            float denominator = Mathf.Max(Mathf.Abs(expected), 0.00001f);
            return Mathf.Abs(actual - expected) / denominator;
        }

        private static void GetVisualRatioRange(Item item,
            out float target, out float minimum, out float maximum)
        {
            if (item != null && item.ThisWeaponType ==
                Item.WeaponType.OneHandDagger)
            {
                target = 0.35f;
                minimum = 0.24f;
                maximum = 0.50f;
                return;
            }
            if (item != null && item.Shield)
            {
                target = 0.50f;
                minimum = 0.35f;
                maximum = 0.70f;
                return;
            }
            if (item != null &&
                (item.ThisWeaponType == Item.WeaponType.TwoHandMelee ||
                 item.ThisWeaponType == Item.WeaponType.TwoHandStaff ||
                 item.ThisWeaponType == Item.WeaponType.TwoHandBow))
            {
                target = 0.78f;
                minimum = 0.55f;
                maximum = 1.15f;
                return;
            }
            target = 0.52f;
            minimum = 0.35f;
            maximum = 0.75f;
        }

        private static float GetLargestRenderedDimension(
            IEnumerable<Renderer> renderers)
        {
            if (!GetRenderedBounds(renderers, out Bounds bounds)) return 0f;
            return Mathf.Max(bounds.size.x,
                Mathf.Max(bounds.size.y, bounds.size.z));
        }

        private static float GetRenderedHeight(IEnumerable<Renderer> renderers)
        {
            return GetRenderedBounds(renderers, out Bounds bounds)
                ? bounds.size.y : 0f;
        }

        private static bool GetRenderedBounds(IEnumerable<Renderer> renderers,
            out Bounds bounds)
        {
            bool found = false;
            bounds = default;
            foreach (Renderer renderer in renderers ?? Array.Empty<Renderer>())
            {
                if (renderer == null || !renderer.enabled ||
                    !renderer.gameObject.activeInHierarchy) continue;
                if (!found)
                {
                    bounds = renderer.bounds;
                    found = true;
                }
                else bounds.Encapsulate(renderer.bounds);
            }
            return found;
        }

        private static void AttachVisual(Transform visualRoot, Transform hand,
            Item item, bool primary)
        {
            if (hand == null || item == null ||
                string.IsNullOrEmpty(item.EquipmentToActivate)) return;
            ModularParts sourceParts = GameData.PlayerModularParent?.Male;
            bool sourceLeft = UsesLeftHand(item, primary);
            Transform sourceAnchor = sourceLeft
                ? sourceParts?.WeaponL : sourceParts?.WeaponR;
            Transform source = FindNamedChild(sourceAnchor, item.EquipmentToActivate);
            if (source == null && sourceLeft)
                source = FindNamedChild(sourceParts?.WeaponR, item.EquipmentToActivate);
            if (source == null) return;

            GameObject clone = UnityEngine.Object.Instantiate(source.gameObject, hand);
            clone.name = "CS_PetGear_" + item.EquipmentToActivate;
            clone.transform.SetParent(hand, false);
            // Preserve the live player's authored model pose on the cloned
            // player hand. Erenshor's modular player uses 0.01 import units,
            // so both size and local offset must be converted to the pet
            // hand's world scale.
            clone.transform.localScale = DivideScale(
                source.lossyScale, hand.lossyScale);
            clone.transform.localPosition = ConvertLocalPositionBetweenParents(
                source.localPosition, source.parent.lossyScale,
                hand.lossyScale);
            clone.transform.localRotation = source.localRotation;
            clone.SetActive(true);
            foreach (Transform child in clone.GetComponentsInChildren<Transform>(true))
                child.gameObject.SetActive(true);
            foreach (Collider collider in clone.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
            // Keep a logical grouping even though the visible clone must be a
            // direct descendant of the animated hand bone.
            var tag = clone.AddComponent<NecromancerPetVisualTag>();
            tag.VisualRoot = visualRoot;
        }

        private static bool UsesLeftHand(Item item, bool primaryState)
        {
            if (!primaryState) return true;
            return item != null &&
                (item.IsBow ||
                 item.ThisWeaponType == Item.WeaponType.TwoHandBow);
        }

        private static Transform FindNativeRightHandWeaponPose(Transform hand)
        {
            if (hand == null) return null;
            string[] names = { "SpectralSword", "Sword" };
            foreach (string name in names)
            {
                Transform match = hand.GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(value =>
                        value.GetComponentInParent<NecromancerPetVisualTag>() ==
                            null &&
                        string.Equals(value.name, name,
                            StringComparison.OrdinalIgnoreCase));
                if (match != null) return match;
            }
            return null;
        }

        private static void SetNativeRightHandWeaponVisible(NPC pet,
            bool visible)
        {
            Transform rightHand = GetNativeEquipmentHand(pet, true);
            Transform native = FindNativeRightHandWeaponPose(rightHand);
            if (native != null) native.gameObject.SetActive(visible);
        }

        private static void NormalizeAttachedVisual(Transform visual,
            Transform petRoot, Item item)
        {
            if (visual == null || petRoot == null || item == null) return;
            Renderer[] visualRenderers =
                visual.GetComponentsInChildren<Renderer>(true);
            float visualSize = GetLargestRenderedDimension(visualRenderers);
            Renderer[] bodyRenderers = GetPetBodyRenderers(petRoot);
            float bodyHeight = GetRenderedHeight(bodyRenderers);
            if (!float.IsFinite(visualSize) || visualSize <= 0.001f ||
                !float.IsFinite(bodyHeight) || bodyHeight <= 0.001f) return;

            GetVisualRatioRange(item, out float targetRatio, out _, out _);
            float currentRatio = visualSize / bodyHeight;
            if (!float.IsFinite(currentRatio) || currentRatio <= 0.001f)
                return;
            float correction = Mathf.Clamp(
                targetRatio / currentRatio, 0.02f, 50f);
            visual.localScale *= correction;
        }

        private static Renderer[] GetPetBodyRenderers(Transform petRoot)
        {
            if (petRoot == null) return Array.Empty<Renderer>();
            Renderer[] skinned = petRoot
                .GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(renderer => renderer != null && renderer.enabled &&
                    renderer.gameObject.activeInHierarchy &&
                    renderer.GetComponentInParent<NecromancerPetVisualTag>() ==
                        null)
                .Cast<Renderer>()
                .ToArray();
            if (skinned.Length > 0) return skinned;

            // Particle and trail bounds are deliberately oversized so effects
            // do not disappear at the edge of the camera. They cannot be used
            // as a character-height reference or every held item becomes
            // enormous after a summon VFX plays.
            return petRoot.GetComponentsInChildren<Renderer>(true)
                .Where(renderer => renderer != null && renderer.enabled &&
                    renderer.gameObject.activeInHierarchy &&
                    !(renderer is ParticleSystemRenderer) &&
                    !(renderer is TrailRenderer) &&
                    !(renderer is LineRenderer) &&
                    renderer.GetComponentInParent<NecromancerPetVisualTag>() ==
                        null)
                .ToArray();
        }

        private static void AlignHeldWeaponFromHand(Transform visual,
            Transform hand, Transform petRoot, Item item)
        {
            if (visual == null || hand == null || petRoot == null ||
                item == null || item.Shield || item.WeaponDmg <= 0) return;
            if (!TryGetFarthestMeshPoint(visual, hand.position,
                    out Vector3 farthest)) return;
            Vector3 current = farthest - hand.position;
            if (!float.IsFinite(current.x) || !float.IsFinite(current.y) ||
                !float.IsFinite(current.z) ||
                current.sqrMagnitude <= 0.0001f) return;

            // The native skeleton idles with its arms down. Aim the long end
            // of every held weapon toward the floor so blades, daggers,
            // staves, and bows are readable from the front instead of pointing
            // through the camera. The resulting world correction becomes a
            // local hand-bone pose and follows all native attack animations.
            Vector3 desired = -petRoot.up;
            visual.rotation = Quaternion.FromToRotation(
                current.normalized, desired.normalized) * visual.rotation;
        }

        private static bool TryGetFarthestMeshPoint(Transform visual,
            Vector3 pivot, out Vector3 point)
        {
            point = pivot;
            float bestDistance = 0f;
            foreach (MeshFilter filter in
                visual.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = filter?.sharedMesh;
                if (mesh == null) continue;
                ConsiderBoundsEndpoints(filter.transform, mesh.bounds,
                    pivot, ref point, ref bestDistance);
            }
            foreach (SkinnedMeshRenderer renderer in
                visual.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (renderer == null || renderer.sharedMesh == null) continue;
                ConsiderBoundsEndpoints(renderer.transform,
                    renderer.localBounds, pivot, ref point,
                    ref bestDistance);
            }
            return bestDistance > 0.0001f;
        }

        private static void ConsiderBoundsEndpoints(Transform transform,
            Bounds bounds, Vector3 pivot, ref Vector3 point,
            ref float bestDistance)
        {
            if (transform == null) return;
            Vector3 extents = bounds.extents;
            Vector3 axis;
            float length;
            if (extents.x >= extents.y && extents.x >= extents.z)
            {
                axis = Vector3.right;
                length = extents.x;
            }
            else if (extents.y >= extents.z)
            {
                axis = Vector3.up;
                length = extents.y;
            }
            else
            {
                axis = Vector3.forward;
                length = extents.z;
            }
            for (int sign = -1; sign <= 1; sign += 2)
            {
                Vector3 candidate = transform.TransformPoint(
                    bounds.center + axis * (length * sign));
                float distance = (candidate - pivot).sqrMagnitude;
                if (distance <= bestDistance) continue;
                bestDistance = distance;
                point = candidate;
            }
        }

        private static Vector3 DivideScale(Vector3 desiredWorld,
            Vector3 parentWorld)
        {
            return new Vector3(
                SafeScaleRatio(desiredWorld.x, parentWorld.x),
                SafeScaleRatio(desiredWorld.y, parentWorld.y),
                SafeScaleRatio(desiredWorld.z, parentWorld.z));
        }

        private static Vector3 ConvertLocalPositionBetweenParents(
            Vector3 localPosition, Vector3 sourceParentWorld,
            Vector3 destinationParentWorld)
        {
            return Vector3.Scale(localPosition,
                DivideScale(sourceParentWorld, destinationParentWorld));
        }

        private static float SafeScaleRatio(float desired, float parent)
        {
            if (!float.IsFinite(desired) || !float.IsFinite(parent) ||
                Mathf.Abs(parent) < 0.00001f) return 1f;
            // Negative bone scales preserve mirrored equipment orientation.
            return Mathf.Clamp(desired / parent, -25f, 25f);
        }

        private static Transform FindNamedChild(Transform root, string name)
        {
            if (root == null) return null;
            return root.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(value => value != root &&
                    string.Equals(value.name, name, StringComparison.Ordinal));
        }

        private static Transform FindHandBone(Transform root, bool right)
        {
            string[] exact = right
                ? new[] { "RightHand", "mixamorig:RightHand", "Hand_R", "hand_r",
                    "R_Hand", "Bip01 R Hand" }
                : new[] { "LeftHand", "mixamorig:LeftHand", "Hand_L", "hand_l",
                    "L_Hand", "Bip01 L Hand" };
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            foreach (string name in exact)
            {
                Transform match = all.FirstOrDefault(value =>
                    string.Equals(value.name, name, StringComparison.OrdinalIgnoreCase));
                if (match != null) return match;
            }
            string side = right ? "right" : "left";
            return all.FirstOrDefault(value =>
            {
                string normalized = (value.name ?? "").Replace("_", "")
                    .Replace(" ", "").ToLowerInvariant();
                return normalized.Contains(side) && normalized.Contains("hand");
            });
        }

        private static Transform GetEquipmentHand(NPC pet, bool right)
        {
            if (pet == null) return null;
            NecromancerPetMarker marker =
                pet.GetComponent<NecromancerPetMarker>();
            Transform playerHand = right
                ? marker?.PlayerRigRightHand : marker?.PlayerRigLeftHand;
            if (IsTransformUnder(playerHand, marker?.PlayerAvatarRig))
                return playerHand;
            return GetNativeEquipmentHand(pet, right);
        }

        private static Transform GetNativeEquipmentHand(NPC pet, bool right)
        {
            if (pet == null) return null;
            Animator animator = pet.GetComponent<Animator>();
            Transform humanoid = animator?.GetBoneTransform(right
                ? HumanBodyBones.RightHand
                : HumanBodyBones.LeftHand);
            if (humanoid != null) return humanoid;
            return FindHandBone(pet.transform, right);
        }

        internal static void OnPetDied(Character character)
        {
            NecromancerPetMarker marker = character?.GetComponent<NecromancerPetMarker>();
            if (marker == null) return;
            ReleasePetReference(character.MyNPC, "died");
            ChatHelper.SendCombat(
                "Your bone thrall has fallen. Its weapon and shield remain safely stored for the next thrall.",
                "#C8D0B4");
        }

        internal static void ReleasePetReference(NPC pet, string reason)
        {
            if (pet == null) return;
            NecromancerPetMarker marker = pet.GetComponent<NecromancerPetMarker>();
            Character owner = marker?.Owner ?? pet.GetComponent<Character>()?.Master;
            if (owner != null && owner.MyCharmedNPC == pet)
                owner.MyCharmedNPC = null;
            SkillsPlugin.Log?.LogInfo(
                $"Necromancer pet reference cleared ({reason}); gear remains escrowed.");
        }

        internal static void ResetRuntime()
        {
            // The NPC is scene-bound. Persistent gear is reset separately by
            // NecromancerPetEquipment after its character-scoped save flush.
        }
    }

    internal sealed class NecromancerPetVisualTag : MonoBehaviour
    {
        internal Transform VisualRoot;
    }

    [HarmonyLib.HarmonyPatch(typeof(Stats), nameof(Stats.CalcStats))]
    internal static class Patch_NecromancerPetStatCurve
    {
        private static void Postfix(Stats __instance)
        {
            NecromancerPets.ApplyPersistentStatCurve(__instance);
        }
    }
}
