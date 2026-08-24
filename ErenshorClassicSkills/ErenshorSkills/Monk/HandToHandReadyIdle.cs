using System;
using UnityEngine;

namespace ErenshorSkills.Monk
{
    /// <summary>
    /// Equipment-driven ready stance for every class. Erenshor normally puts
    /// all one-handed weapons into its sword-ready idle; fist weapons instead
    /// get a planted guard which blends out while the authored hand-to-hand
    /// strike driver owns the rig.
    /// </summary>
    internal static class HandToHandReadyIdle
    {
        private static string _lastDiagnostic;
        private static float _nextDiagnosticAt;

        internal static void Tick()
        {
            PlayerControl player = GameData.PlayerControl;
            Animator animator = ResolvePlayerAnimator(player);
            if (animator == null)
            {
                LogStateChange("animator=missing");
                return;
            }
            bool compatibleFistLoadout = HasCompatibleFistLoadout();
            if (compatibleFistLoadout)
                ClearNativeHeldWeaponStance(animator);
            bool requested = ShouldUseReadyIdle(player,
                compatibleFistLoadout);
            HandToHandReadyIdleDriver driver =
                animator.GetComponent<HandToHandReadyIdleDriver>();
            if (driver == null && requested)
                driver = animator.gameObject.AddComponent<
                    HandToHandReadyIdleDriver>();
            if (driver != null) driver.Requested = requested;

            LogStateChange(
                $"animator=ok requested={requested} " +
                $"autoattack={GameData.Autoattacking} " +
                $"swimming={player?.Swimming == true} " +
                $"loadout={compatibleFistLoadout} " +
                $"driver={(driver != null ? "attached" : "none")} " +
                $"rig={(driver != null ? driver.RigReady.ToString() : "-")} " +
                $"weight={(driver != null ? driver.BlendWeight.ToString("0.00") : "-")} " +
                $"effective={(driver != null ? driver.EffectiveWeight.ToString("0.00") : "-")}");
        }

        /// <summary>
        /// One line whenever the guard-stance gate chain changes state, so a
        /// play session's log shows exactly which link kept the stance from
        /// engaging. Throttled and change-gated; silent when nothing moves.
        /// </summary>
        private static void LogStateChange(string state)
        {
            if (SkillsPlugin.CfgEnableTestMode?.Value != true) return;
            if (string.Equals(state, _lastDiagnostic, StringComparison.Ordinal) ||
                Time.unscaledTime < _nextDiagnosticAt) return;
            _lastDiagnostic = state;
            _nextDiagnosticAt = Time.unscaledTime + 1f;
            SkillsPlugin.Log?.LogInfo("[Guard Stance] " + state);
        }

        /// <summary>
        /// The player's Animator lives on the character model, not on the
        /// PlayerControl object — Character.MyAnim is an inspector-assigned
        /// reference to that child. A plain GetComponent&lt;Animator&gt;() on
        /// PlayerControl therefore returned null, Tick bailed before it could
        /// attach the driver, and the guard stance never appeared in any
        /// loadout. Resolve it the way the attack driver already does.
        /// </summary>
        private static Animator ResolvePlayerAnimator(PlayerControl player)
        {
            if (player == null) return null;
            Animator animator = player.Myself?.GetMyAnim();
            if (animator != null) return animator;
            animator = player.GetComponent<Animator>();
            return animator != null
                ? animator : player.GetComponentInChildren<Animator>();
        }

        internal static bool IsFistWeapon(Item item)
        {
            if (item == null || item == GameData.PlayerInv?.Empty ||
                item.WeaponDmg <= 0 || item.IsBow || item.IsWand ||
                item.Shield)
                return false;
            return Skills.CombatSkills.ClassifyWeapon(item, false) ==
                Skills.CombatSkills.WeaponType.HandToHand;
        }

        internal static bool HasCompatibleFistLoadout()
        {
            Item main = GameData.PlayerInv?.MH?.MyItem;
            Item off = GameData.PlayerInv?.OH?.MyItem;
            bool mainFist = IsFistWeapon(main);
            bool offFist = IsFistWeapon(off);
            bool mainOtherWeapon = IsOtherWeapon(main);
            bool offOtherWeapon = IsOtherWeapon(off);
            // Bare hands are the Monk's own stance, not an absence of one. The
            // guard used to require an actual fist weapon, so a Monk who had
            // not bought handwraps yet — every new Monk — auto-attacked from
            // the generic empty-handed idle instead of a fighting stance.
            bool emptyHanded = MonkCatalog.IsMonkPlayer() &&
                !mainOtherWeapon && !offOtherWeapon &&
                !mainFist && !offFist;
            return (mainFist || offFist || emptyHanded) &&
                (!mainOtherWeapon || mainFist) &&
                (!offOtherWeapon || offFist);
        }

        internal static bool Validate(out string detail)
        {
            bool starter = IsFistWeapon(MonkGear.StarterWeapon);
            bool endgame = IsFistWeapon(MonkGear.EndgameWeapon);
            bool valid = MonkGear.IsReady && starter && endgame;
            detail = valid
                ? "all-class equipment-driven fist guard with planted feet, " +
                  "0.18s blend, and authored-strike handoff"
                : "fist ready-idle contract did not recognize the starter and grandmaster handwrap bookends";
            return valid;
        }

        internal static bool TryGetRuntimeState(out bool rigReady,
            out bool requested, out float blendWeight,
            out float effectiveWeight)
        {
            rigReady = false;
            requested = false;
            blendWeight = 0f;
            effectiveWeight = 0f;
            Animator animator =
                ResolvePlayerAnimator(GameData.PlayerControl);
            HandToHandReadyIdleDriver driver =
                animator?.GetComponent<HandToHandReadyIdleDriver>();
            if (driver == null) return false;
            rigReady = driver.RigReady;
            requested = driver.Requested;
            blendWeight = driver.BlendWeight;
            effectiveWeight = driver.EffectiveWeight;
            return true;
        }

        internal static bool TryGetFistState(out bool fistsReady,
            out float closureRatio)
        {
            fistsReady = false;
            closureRatio = 0f;
            Animator animator =
                ResolvePlayerAnimator(GameData.PlayerControl);
            HandToHandReadyIdleDriver driver =
                animator?.GetComponent<HandToHandReadyIdleDriver>();
            if (driver == null) return false;
            fistsReady = driver.FistsReady;
            closureRatio = driver.FistClosureRatio;
            return true;
        }

        /// <summary>
        /// ItemIcon.UpdateSlotImage maps every one-handed melee item to the
        /// native 1HSmall sword stance. It sets that flag while equipping, but
        /// its empty-slot branch never clears it while unequipping. Handwraps
        /// therefore switched the animator into sword-ready and bare hands
        /// inherited that state afterward. A fist loadout owns presentation,
        /// so discard every native held-weapon stance flag before animation is
        /// evaluated; equipping a real non-fist weapon sets the appropriate
        /// flag again because this method then stops running.
        /// </summary>
        private static void ClearNativeHeldWeaponStance(Animator animator)
        {
            if (animator == null) return;
            animator.SetBool("1HSmall", false);
            animator.SetBool("1HDagger", false);
            animator.SetBool("2HMelee", false);
            animator.SetBool("2HStaff", false);
        }

        private static bool ShouldUseReadyIdle(PlayerControl player,
            bool compatibleFistLoadout)
        {
            Character character = player?.Myself;
            return GameData.Autoattacking && !player.Swimming &&
                character?.Alive == true && compatibleFistLoadout;
        }

        private static bool IsOtherWeapon(Item item)
        {
            if (item == null || item == GameData.PlayerInv?.Empty)
                return false;
            return item.WeaponDmg > 0 && !item.Shield &&
                !item.IsBow && !item.IsWand;
        }
    }

    // Native weapon presentation writes the upper arms in LateUpdate only
    // when a weapon is equipped. Run after those writers, while still running
    // immediately before MonkAnimationDriver, so handwraps cannot overwrite
    // the approved guard and authored strikes retain their existing handoff.
    [DefaultExecutionOrder(30000)]
    internal sealed class HandToHandReadyIdleDriver : MonoBehaviour
    {
        private const float BlendSeconds = 0.18f;
        internal bool Requested;

        private Animator _animator;
        private Transform _hips;
        private Transform _chest;
        private Transform _leftUpperArm;
        private Transform _leftLowerArm;
        private Transform _leftHand;
        private Transform _rightUpperArm;
        private Transform _rightLowerArm;
        private Transform _rightHand;
        private float _weight;
        private bool _ready;
        private float _effectiveWeight;
        private MonkFistPoseRig _fistPose;

        internal bool RigReady => _ready;
        internal bool FistsReady => _fistPose?.Ready == true;
        internal float FistClosureRatio => _fistPose?.ClosureRatio ?? 0f;
        internal float BlendWeight => _weight;
        internal float EffectiveWeight => _effectiveWeight;

        private void Awake()
        {
            _animator = GetComponent<Animator>();
            CacheRig();
        }

        private void Update()
        {
            bool desired = Requested && _ready;
            float step = Time.deltaTime / BlendSeconds;
            _weight = Mathf.MoveTowards(_weight,
                desired ? 1f : 0f, step);
        }

        private void LateUpdate()
        {
            MonkAnimationDriver attack =
                GetComponent<MonkAnimationDriver>();
            float attackWeight = attack?.RigOwnershipWeight ?? 0f;
            _effectiveWeight = Mathf.SmoothStep(0f, 1f, _weight) *
                (1f - Mathf.Clamp01(attackWeight));
            if (_effectiveWeight <= 0.001f || !_ready) return;

            Transform root = GameData.PlayerControl?.transform ?? transform;
            Vector3 up = root.up;
            Vector3 forward = root.forward;
            Vector3 right = root.right;
            float breath = Mathf.Sin(Time.time * 2.6f) * 0.018f;
            float weight = _effectiveWeight;

            RotateAround(_hips, right, 0.75f * weight);
            RotateAround(_chest, right, (1.5f + breath * 12f) * weight);

            // Resolve both arms to a mirrored guard at full ownership. Partial
            // corrections preserved too much of Erenshor's underlying sword
            // idle; that residual pose differed between player rigs and could
            // leave one arm crossed over the chest while the other hung low.
            // Full corrections make the result independent of the native idle:
            // elbows down and out, forearms up and in, fists level and apart.
            Vector3 leftUpper = (forward * 0.30f - right * 0.32f -
                up * (0.48f - breath)).normalized;
            Vector3 rightUpper = (forward * 0.30f + right * 0.32f -
                up * (0.48f + breath)).normalized;
            Vector3 leftForearm = (forward * 0.42f + right * 0.10f +
                up * (0.72f + breath)).normalized;
            Vector3 rightForearm = (forward * 0.42f - right * 0.10f +
                up * (0.72f - breath)).normalized;

            RotateToward(_leftUpperArm, _leftLowerArm, leftUpper, weight);
            RotateToward(_rightUpperArm, _rightLowerArm, rightUpper, weight);
            RotateToward(_leftLowerArm, _leftHand, leftForearm, weight);
            RotateToward(_rightLowerArm, _rightHand, rightForearm, weight);
            RotateAround(_leftHand, forward, -2f * weight);
            RotateAround(_rightHand, forward, 2f * weight);
            if (_fistPose?.Ready == true) _fistPose.Apply(weight);
        }

        private void CacheRig()
        {
            _ready = false;
            if (_animator == null || !_animator.isHuman) return;
            try
            {
                _hips = _animator.GetBoneTransform(HumanBodyBones.Hips);
                _chest = _animator.GetBoneTransform(
                    HumanBodyBones.UpperChest) ??
                    _animator.GetBoneTransform(HumanBodyBones.Chest);
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
                _fistPose = new MonkFistPoseRig(_animator);
                // The guard stance needs the arm chain only. Finger bones are
                // a garnish: Erenshor's stylized avatars map hands but not four
                // finger chains per hand, and requiring MonkFistPoseRig.Ready
                // here held the whole stance hostage to bones the models do
                // not have — arms ready, fists impossible, stance never shown.
                _ready = _hips != null && _chest != null &&
                    _leftUpperArm != null && _leftLowerArm != null &&
                    _leftHand != null && _rightUpperArm != null &&
                    _rightLowerArm != null && _rightHand != null;
                if (_ready)
                    SkillsPlugin.Log?.LogInfo(
                        "Hand-to-hand ready idle: humanoid guard rig ready " +
                        $"(finger clench {(_fistPose.Ready ? "available" : "unavailable; mitten-hand avatar")}).");
            }
            catch (Exception exception)
            {
                SkillsPlugin.Log?.LogWarning(
                    "Hand-to-hand ready idle unavailable: " +
                    exception.Message);
            }
        }

        private static void RotateToward(Transform parent, Transform child,
            Vector3 desired, float weight)
        {
            if (parent == null || child == null ||
                desired.sqrMagnitude <= 0.001f || weight <= 0f)
                return;
            Vector3 current = child.position - parent.position;
            if (current.sqrMagnitude <= 0.001f) return;
            Quaternion correction = Quaternion.FromToRotation(
                current.normalized, desired.normalized);
            parent.rotation = Quaternion.Slerp(Quaternion.identity,
                correction, Mathf.Clamp01(weight)) * parent.rotation;
        }

        private static void RotateAround(Transform bone, Vector3 axis,
            float degrees)
        {
            if (bone == null || axis.sqrMagnitude <= 0.001f ||
                Mathf.Abs(degrees) <= 0.001f)
                return;
            bone.rotation = Quaternion.AngleAxis(degrees,
                axis.normalized) * bone.rotation;
        }
    }
}
