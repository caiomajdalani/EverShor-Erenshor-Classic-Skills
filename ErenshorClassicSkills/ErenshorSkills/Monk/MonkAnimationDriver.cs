using System;
using System.Collections.Generic;
using UnityEngine;

namespace ErenshorSkills.Monk
{
    /// <summary>
    /// Drives visible, full-body martial motion on Erenshor's humanoid player
    /// rig. License-approved authored joint motion is retargeted onto the live
    /// player avatar in LateUpdate, with native Erenshor animation retained as
    /// a fail-safe. VFX are released at the authored contact frame instead of
    /// when the skill button is first pressed.
    /// </summary>
    // Preserve the established guard-then-strike LateUpdate order after the
    // guard was moved behind Erenshor's weapon-only upper-arm writer.
    [DefaultExecutionOrder(31000)]
    internal sealed class MonkAnimationDriver : MonoBehaviour
    {
        private const float BlendInSeconds = 0.09f;
        private const float RecoveryBlendSeconds = 0.32f;
        private const float ContinuitySampleSeconds = 0.11f;

        private enum Motion
        {
            None,
            Jab,
            ShoulderCheck,
            TigerClaw,
            StoneThrow,
            RoundKick,
            CrescentHeel,
            CounteringPalm,
            EagleStrike,
            FallingRain,
            Pummel,
            FlyingKick,
            SweepingCrescent,
            PressurePoint,
            WhirlwindKick,
            DragonPunch,
            QuiveringPalm,
            GaleSplittingPalm,
            VaultingTempest,
            SkybreakerRush,
            AutoRight,
            AutoLeft
        }

        private struct MotionTiming
        {
            internal float Duration;
            internal float Impact;
            internal string Label;

            internal MotionTiming(float duration, float impact, string label)
            {
                Duration = duration;
                Impact = impact;
                Label = label;
            }
        }

        private Animator _animator;
        private Transform _caster;
        private Transform _target;
        private MonkTechniqueDefinition _definition;
        private Motion _motion;
        private MotionTiming _timing;
        private float _started;
        private bool _active;
        private bool _playVfx;
        private bool _impactReleased;
        private bool _delayedKickTriggered;
        private bool _castAttackStarted;
        private int _comboStage;
        private RuntimeAnimatorController _originalController;
        private AnimatorOverrideController _punchController;
        private float _originalAnimatorSpeed = 1f;

        private Transform _hips;
        private Transform _spine;
        private Transform _chest;
        private Transform _upperChest;
        private Transform _rightUpperArm;
        private Transform _rightHand;
        private readonly Transform[] _motionBones =
            new Transform[(int)MonkMotionJoint.Count];
        private readonly Vector3[] _authoredPose =
            new Vector3[(int)MonkMotionJoint.Count];
        private readonly Quaternion[] _autoLegPose =
            new Quaternion[(int)MonkMotionJoint.Count];
        private MonkAuthoredMotionClip _authoredClip;
        private bool _mirrorAuthored;
        private bool _autoLegPoseReady;
        private float _bodyHeight = 1.8f;
        private bool _humanoidReady;
        private MonkFistPoseRig _fistPose;
        private Vector3 _hipsAnchorLocal;
        private float _leftFootAnchorLocalY;
        private float _rightFootAnchorLocalY;
        private bool _groundAnchorReady;
        private readonly Quaternion[] _qaPreviousRotations =
            new Quaternion[(int)MonkMotionJoint.Count];
        private bool _qaPreviousPoseReady;
        private Vector3 _qaPreviousHipsLocal;
        private float _qaMaxRecoveryAngularSpeed;
        private float _qaMaxRecoveryHipSpeedRatio;
        private int _qaRecoverySamples;
        private bool _qaAwaitingHandoffSample;
        private MonkAttackVfxDriver _attackVfx;
        private bool _skybreakerRootMotionReady;
        private Vector3 _skybreakerRushStart;
        private Vector3 _skybreakerRushEnd;
        private float _skybreakerRushDistance;
        private float _skybreakerMaximumAdvance;
        private float _vaultCrouchPeak;

        private static bool _rigResultLogged;
        private static bool _punchClipResolved;
        private static AnimationClip _punchClip;
        private static bool _nextAutoPunchRight = true;
        private static readonly HashSet<string> _authoredMappingsLogged =
            new HashSet<string>(StringComparer.Ordinal);

        internal static string LastAutoPunchSide { get; private set; } =
            "none";
        internal static int AutoRightPlayCount { get; private set; }
        internal static int AutoLeftPlayCount { get; private set; }
        internal static float LastSkybreakerRushDistance { get; private set; }
        internal static float LastVaultingCrouchRatio { get; private set; }

        internal bool IsActive => _active;
        internal float RigOwnershipWeight
        {
            get
            {
                if (!_active) return 0f;
                float duration = Mathf.Max(_timing.Duration, 0.01f);
                float normalized = Mathf.Clamp01(
                    (Time.time - _started) / duration);
                return PoseBlendWeight(normalized);
            }
        }

        internal static bool Play(MonkTechniqueDefinition definition,
            Transform caster, Transform target = null, bool playVfx = true)
        {
            if (definition == null || caster == null) return false;
            Motion motion = ResolveMotion(definition.Key);
            if (motion == Motion.None)
            {
                if (playVfx) MonkVfx.Play(definition, caster, target);
                return false;
            }
            return Begin(caster, target, definition, motion, playVfx);
        }

        internal static bool PlayCounter(MonkTechniqueDefinition definition,
            Transform caster, Transform target)
        {
            if (definition == null || caster == null) return false;
            return Begin(caster, target, definition, Motion.CounteringPalm,
                true);
        }

        internal static bool PlayAutoPunch(Transform caster, Transform target,
            bool mainHand)
        {
            if (caster == null) return false;
            // The physical weapon hand still determines damage and delay, but
            // a martial artist does not throw the same straight forever when
            // only one fist weapon is equipped. Alternate the visible lead
            // hand across every accepted autoattack.
            Motion motion = _nextAutoPunchRight
                ? Motion.AutoRight : Motion.AutoLeft;
            if (!Begin(caster, target, null, motion, false)) return false;
            _nextAutoPunchRight = !_nextAutoPunchRight;
            LastAutoPunchSide = motion == Motion.AutoRight ? "right" : "left";
            if (motion == Motion.AutoRight) AutoRightPlayCount++;
            else AutoLeftPlayCount++;
            return true;
        }

        internal static void ResetAutoPunchPattern()
        {
            _nextAutoPunchRight = true;
            LastAutoPunchSide = "none";
            AutoRightPlayCount = 0;
            AutoLeftPlayCount = 0;
        }

        internal static bool SupportsBodyMotion(Transform caster)
        {
            Animator animator = FindAnimator(caster);
            if (animator == null || !animator.isHuman) return false;
            try
            {
                return animator.GetBoneTransform(HumanBodyBones.Hips) != null &&
                    animator.GetBoneTransform(HumanBodyBones.RightHand) != null &&
                    animator.GetBoneTransform(HumanBodyBones.RightFoot) != null;
            }
            catch
            {
                return false;
            }
        }

        internal static float GetImpactTime(MonkTechniqueDefinition definition)
        {
            if (definition == null) return 0.12f;
            return TimingFor(ResolveMotion(definition.Key)).Impact;
        }

        internal static float GetEffectImpactTime(
            MonkTechniqueDefinition definition)
        {
            if (definition == null) return 0.12f;
            Motion motion = ResolveMotion(definition.Key);
            MotionTiming timing = TimingFor(motion);
            return motion == Motion.StoneThrow ||
                motion == Motion.GaleSplittingPalm
                ? Mathf.Min(timing.Duration - 0.05f,
                    timing.Impact + (motion == Motion.GaleSplittingPalm
                        ? 0.22f : 0.20f))
                : timing.Impact;
        }

        internal static float GetCaptureTime(
            MonkTechniqueDefinition definition)
        {
            MotionTiming timing = definition == null
                ? new MotionTiming(0.5f, 0.2f, "none")
                : TimingFor(ResolveMotion(definition.Key));
            float impact = definition == null
                ? timing.Impact
                : GetEffectImpactTime(definition);
            return Mathf.Min(timing.Duration - 0.035f,
                impact + 0.015f);
        }

        internal static float GetDuration(MonkTechniqueDefinition definition)
        {
            if (definition == null) return 0.5f;
            return TimingFor(ResolveMotion(definition.Key)).Duration;
        }

        internal static string GetMotionName(
            MonkTechniqueDefinition definition)
        {
            if (definition == null) return "none";
            return TimingFor(ResolveMotion(definition.Key)).Label;
        }

        internal static float GetAutoImpactTime() =>
            TimingFor(Motion.AutoRight).Impact;

        internal static float GetAutoDuration() =>
            TimingFor(Motion.AutoRight).Duration;

        private static bool Begin(Transform caster, Transform target,
            MonkTechniqueDefinition definition, Motion motion, bool playVfx)
        {
            Animator animator = FindAnimator(caster);
            if (animator == null)
            {
                if (playVfx && definition != null)
                    MonkVfx.Play(definition, caster, target);
                return false;
            }
            MonkAnimationDriver driver =
                animator.GetComponent<MonkAnimationDriver>();
            if (driver == null)
                driver = animator.gameObject.AddComponent<MonkAnimationDriver>();
            if (driver._active)
            {
                bool autoPunch = motion == Motion.AutoRight ||
                    motion == Motion.AutoLeft;
                if (autoPunch && driver._definition != null) return false;
                driver.Finish();
            }
            driver.BeginInternal(animator, caster, target, definition, motion,
                playVfx);
            return driver._humanoidReady;
        }

        private void BeginInternal(Animator animator, Transform caster,
            Transform target, MonkTechniqueDefinition definition,
            Motion motion, bool playVfx)
        {
            FinishBaseAnimation();
            _animator = animator;
            _caster = caster;
            _target = target;
            _definition = definition;
            _motion = motion;
            _timing = TimingFor(motion);
            _started = Time.time;
            _active = true;
            _playVfx = playVfx;
            _impactReleased = false;
            _delayedKickTriggered = false;
            _castAttackStarted = false;
            // Clear a cast state left behind by an earlier technique so a
            // stranded Monk recovers on the next strike instead of staying
            // locked in the casting pose.
            if (_animator != null)
            {
                _animator.ResetTrigger("CastAtk");
                _animator.SetTrigger("CastAtkEnd");
            }
            _comboStage = 0;
            _qaPreviousPoseReady = false;
            _qaMaxRecoveryAngularSpeed = 0f;
            _qaMaxRecoveryHipSpeedRatio = 0f;
            _qaRecoverySamples = 0;
            _qaAwaitingHandoffSample = false;
            _skybreakerRootMotionReady = false;
            _skybreakerRushDistance = 0f;
            _skybreakerMaximumAdvance = 0f;
            _vaultCrouchPeak = 0f;
            if (motion == Motion.SkybreakerRush)
                LastSkybreakerRushDistance = 0f;
            else if (motion == Motion.VaultingTempest)
                LastVaultingCrouchRatio = 0f;
            CacheBones();
            CaptureAutoPunchLegPose();
            PrepareSignatureRootMotion();
            ResolveAuthoredMotion();
            StartBaseAnimation();
            if (_playVfx && definition != null && definition.IsOffensive)
            {
                _attackVfx = MonkVfx.BeginAttack(definition, animator,
                    caster, target, _timing.Duration, _timing.Impact);
                MonkAudio.PlayWindup(definition, caster);
            }
            enabled = true;
        }

        private static Animator FindAnimator(Transform caster)
        {
            if (caster == null) return null;
            Animator animator = caster.GetComponent<Animator>();
            return animator != null ? animator :
                caster.GetComponentInChildren<Animator>();
        }

        private void CacheBones()
        {
            _humanoidReady = false;
            _groundAnchorReady = false;
            if (_animator == null || !_animator.isHuman)
            {
                LogRigResult(false);
                return;
            }
            try
            {
                _hips = _animator.GetBoneTransform(HumanBodyBones.Hips);
                _spine = _animator.GetBoneTransform(HumanBodyBones.Spine);
                _chest = _animator.GetBoneTransform(HumanBodyBones.Chest);
                _upperChest = _animator.GetBoneTransform(
                    HumanBodyBones.UpperChest);
                _rightUpperArm = _animator.GetBoneTransform(
                    HumanBodyBones.RightUpperArm);
                _rightHand = _animator.GetBoneTransform(
                    HumanBodyBones.RightHand);
                SetMotionBone(MonkMotionJoint.Hips, HumanBodyBones.Hips);
                SetMotionBone(MonkMotionJoint.Spine, HumanBodyBones.Spine);
                _motionBones[(int)MonkMotionJoint.Chest] = Torso;
                SetMotionBone(MonkMotionJoint.Neck, HumanBodyBones.Neck);
                SetMotionBone(MonkMotionJoint.Head, HumanBodyBones.Head);
                SetMotionBone(MonkMotionJoint.LeftShoulder,
                    HumanBodyBones.LeftShoulder);
                SetMotionBone(MonkMotionJoint.LeftUpperArm,
                    HumanBodyBones.LeftUpperArm);
                SetMotionBone(MonkMotionJoint.LeftLowerArm,
                    HumanBodyBones.LeftLowerArm);
                SetMotionBone(MonkMotionJoint.LeftHand,
                    HumanBodyBones.LeftHand);
                SetMotionBone(MonkMotionJoint.RightShoulder,
                    HumanBodyBones.RightShoulder);
                SetMotionBone(MonkMotionJoint.RightUpperArm,
                    HumanBodyBones.RightUpperArm);
                SetMotionBone(MonkMotionJoint.RightLowerArm,
                    HumanBodyBones.RightLowerArm);
                SetMotionBone(MonkMotionJoint.RightHand,
                    HumanBodyBones.RightHand);
                SetMotionBone(MonkMotionJoint.LeftUpperLeg,
                    HumanBodyBones.LeftUpperLeg);
                SetMotionBone(MonkMotionJoint.LeftLowerLeg,
                    HumanBodyBones.LeftLowerLeg);
                SetMotionBone(MonkMotionJoint.LeftFoot,
                    HumanBodyBones.LeftFoot);
                SetMotionBone(MonkMotionJoint.LeftToes,
                    HumanBodyBones.LeftToes);
                SetMotionBone(MonkMotionJoint.RightUpperLeg,
                    HumanBodyBones.RightUpperLeg);
                SetMotionBone(MonkMotionJoint.RightLowerLeg,
                    HumanBodyBones.RightLowerLeg);
                SetMotionBone(MonkMotionJoint.RightFoot,
                    HumanBodyBones.RightFoot);
                SetMotionBone(MonkMotionJoint.RightToes,
                    HumanBodyBones.RightToes);
                _humanoidReady = RequiredMotionBonesReady();
                if (_humanoidReady)
                {
                    _fistPose = new MonkFistPoseRig(_animator);
                    Transform head = MotionBone(MonkMotionJoint.Head);
                    Transform leftFoot = MotionBone(MonkMotionJoint.LeftFoot);
                    Transform rightFoot = MotionBone(MonkMotionJoint.RightFoot);
                    Vector3 feet = (leftFoot.position + rightFoot.position) *
                        0.5f;
                    _bodyHeight = Mathf.Clamp(Vector3.Distance(head.position,
                        feet), 0.8f, 3.2f);
                    if (_caster != null && _hips != null)
                    {
                        _hipsAnchorLocal =
                            _caster.InverseTransformPoint(_hips.position);
                        _leftFootAnchorLocalY = _caster
                            .InverseTransformPoint(leftFoot.position).y;
                        _rightFootAnchorLocalY = _caster
                            .InverseTransformPoint(rightFoot.position).y;
                        _groundAnchorReady = true;
                    }
                }
            }
            catch
            {
                _humanoidReady = false;
            }
            LogRigResult(_humanoidReady);
        }

        private void SetMotionBone(MonkMotionJoint joint,
            HumanBodyBones humanBone)
        {
            _motionBones[(int)joint] = _animator.GetBoneTransform(humanBone);
        }

        private Transform MotionBone(MonkMotionJoint joint) =>
            _motionBones[(int)joint];

        private bool RequiredMotionBonesReady()
        {
            MonkMotionJoint[] required =
            {
                MonkMotionJoint.Hips, MonkMotionJoint.Spine,
                MonkMotionJoint.Chest, MonkMotionJoint.Head,
                MonkMotionJoint.LeftShoulder,
                MonkMotionJoint.LeftUpperArm,
                MonkMotionJoint.LeftLowerArm, MonkMotionJoint.LeftHand,
                MonkMotionJoint.RightShoulder,
                MonkMotionJoint.RightUpperArm,
                MonkMotionJoint.RightLowerArm, MonkMotionJoint.RightHand,
                MonkMotionJoint.LeftUpperLeg,
                MonkMotionJoint.LeftLowerLeg, MonkMotionJoint.LeftFoot,
                MonkMotionJoint.RightUpperLeg,
                MonkMotionJoint.RightLowerLeg, MonkMotionJoint.RightFoot
            };
            foreach (MonkMotionJoint joint in required)
                if (MotionBone(joint) == null) return false;
            return true;
        }

        private static void LogRigResult(bool ready)
        {
            if (_rigResultLogged) return;
            _rigResultLogged = true;
            if (ready)
                SkillsPlugin.Log?.LogInfo(
                    "Monk animation rig: full humanoid retarget chain ready.");
            else
                SkillsPlugin.Log?.LogWarning(
                    "Monk animation rig: humanoid bones unavailable; native animation fallback will be used.");
        }

        private Transform Torso => _upperChest != null ? _upperChest :
            (_chest != null ? _chest : _spine);

        private void StartBaseAnimation()
        {
            if (_animator == null) return;
            if (_authoredClip != null)
            {
                // Keep the native controller in its current stable locomotion
                // state. The authored clip owns the martial pose in LateUpdate.
                // Layering Erenshor's Kick state underneath an unrelated clip
                // made its eventual native transition visible as an end jerk.
                _animator.ResetTrigger("CastAtk");
                _animator.ResetTrigger("CastAtkEnd");
                _animator.ResetTrigger("Kick");
                _animator.ResetTrigger("Jump");
                _animator.ResetTrigger("MeleeSwing");
                _animator.ResetTrigger("DualWield");
                return;
            }
            if (IsKick(_motion))
            {
                if (_motion == Motion.FlyingKick)
                    _animator.SetTrigger("Jump");
                else
                    _animator.SetTrigger("Kick");
                return;
            }
            if (StartAuthoredPunch()) return;
            // A martial technique must never fall back to the spellcasting
            // state. It reads as casting, and because CastAtk only leaves on a
            // paired CastAtkEnd, any missed finish strands the Monk in the cast
            // pose. MeleeSwing is the trigger every ordinary attack uses and it
            // ends on its own.
            _animator.ResetTrigger("CastAtk");
            _animator.ResetTrigger("CastAtkEnd");
            _animator.SetTrigger("MeleeSwing");
            _castAttackStarted = false;
        }

        private bool StartAuthoredPunch()
        {
            AnimationClip punch = ResolvePunchClip();
            if (punch == null || _animator?.runtimeAnimatorController == null)
                return false;
            _originalController = _animator.runtimeAnimatorController;
            _originalAnimatorSpeed = _animator.speed;
            var controller = new AnimatorOverrideController(
                _originalController);
            var overrides = new List<KeyValuePair<AnimationClip,
                AnimationClip>>(controller.overridesCount);
            controller.GetOverrides(overrides);
            bool replaced = false;
            for (int index = 0; index < overrides.Count; index++)
            {
                AnimationClip source = overrides[index].Key;
                if (source == null || !string.Equals(source.name,
                    "Armed-Attack-Kick-R1", StringComparison.Ordinal))
                    continue;
                overrides[index] = new KeyValuePair<AnimationClip,
                    AnimationClip>(source, punch);
                replaced = true;
            }
            if (!replaced) return false;
            controller.ApplyOverrides(overrides);
            _punchController = controller;
            _animator.runtimeAnimatorController = controller;
            // PunchSwingRight's authored contact is roughly 0.62 seconds into
            // the source clip. Scale playback so light jabs land quickly while
            // committed finishers visibly wind up before their later impact.
            _animator.speed = _motion == Motion.FallingRain ? 3f :
                Mathf.Clamp(0.62f / Mathf.Max(_timing.Impact, 0.14f),
                    1f, 4.4f);
            _animator.ResetTrigger("CastAtk");
            _animator.ResetTrigger("CastAtkEnd");
            _animator.SetTrigger("Kick");
            return true;
        }

        private static AnimationClip ResolvePunchClip()
        {
            if (_punchClipResolved) return _punchClip;
            _punchClipResolved = true;
            try
            {
                GameObject prefab = Resources.Load<GameObject>(
                    "NPCs/Summoned Treant");
                Animator source = prefab?.GetComponentInChildren<Animator>();
                AnimationClip[] clips = source?.runtimeAnimatorController?
                    .animationClips;
                if (clips != null)
                {
                    foreach (AnimationClip clip in clips)
                    {
                        if (clip != null && string.Equals(clip.name,
                            "PunchSwingRight", StringComparison.Ordinal))
                        {
                            _punchClip = clip;
                            break;
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                SkillsPlugin.Log?.LogWarning(
                    "Monk animation: could not resolve authored punch clip: " +
                    exception.Message);
            }
            if (_punchClip != null)
                SkillsPlugin.Log?.LogInfo(
                    "Monk animation: retargeting authored humanoid PunchSwingRight clip to player rig.");
            else
                SkillsPlugin.Log?.LogWarning(
                    "Monk animation: authored punch clip unavailable; using native unarmed fallback.");
            return _punchClip;
        }

        private void Update()
        {
            if (!_active) return;
            float elapsed = Time.time - _started;
            if (_authoredClip == null && _motion == Motion.FlyingKick &&
                !_delayedKickTriggered &&
                elapsed >= 0.14f)
            {
                _delayedKickTriggered = true;
                _animator?.SetTrigger("Kick");
            }
            if (_authoredClip == null && _motion == Motion.FallingRain &&
                _punchController != null)
            {
                if (_comboStage == 0 && elapsed >= 0.28f)
                {
                    _comboStage = 1;
                    _animator.Play("Base Layer.Armed-Attack-Kick-R1", 0, 0f);
                }
                else if (_comboStage == 1 && elapsed >= 0.56f)
                {
                    _comboStage = 2;
                    _animator.Play("Base Layer.Armed-Attack-Kick-R1", 0, 0f);
                }
            }
            float effectImpact = _motion == Motion.StoneThrow ||
                _motion == Motion.GaleSplittingPalm
                ? Mathf.Min(_timing.Duration - 0.05f,
                    _timing.Impact + (_motion == Motion.GaleSplittingPalm
                        ? 0.22f : 0.20f))
                : _timing.Impact;
            if (!_impactReleased && elapsed >= effectImpact)
            {
                _impactReleased = true;
                if ((_motion == Motion.AutoRight ||
                    _motion == Motion.AutoLeft) && _caster != null)
                {
                    MonkAudio.PlayAutoImpact(_caster, _target,
                        _motion == Motion.AutoRight);
                }
                else if (_playVfx && _definition != null && _caster != null)
                    MonkVfx.Play(_definition, _caster, _target);
            }
            if (elapsed >= _timing.Duration &&
                (!_humanoidReady || _caster == null))
                Finish();
        }

        private void LateUpdate()
        {
            if (!_active)
            {
                if (_qaAwaitingHandoffSample)
                {
                    RecordQaFrame(true);
                    _qaAwaitingHandoffSample = false;
                }
                return;
            }
            if (!_humanoidReady || _caster == null) return;
            float elapsed = Time.time - _started;
            float normalized = Mathf.Clamp01(elapsed / _timing.Duration);
            float impactTime = _timing.Impact / _timing.Duration;
            float poseBlend = PoseBlendWeight(normalized);
            UpdateSignatureRootMotion(normalized, impactTime);
            RestoreHipAnchor(poseBlend);
            if (_authoredClip != null)
                ApplyAuthoredMotion(normalized, impactTime, poseBlend);
            else
                ApplyMotion(normalized, impactTime);
            RestoreAutoPunchLegPose(poseBlend);
            PlantSupportFoot(poseBlend);
            if (!UsesOpenPalmPose(_motion))
                _fistPose?.Apply(poseBlend);
            RecordQaContinuity(normalized);

            // Update runs before LateUpdate. Finishing there skipped the exact
            // zero-weight recovery pose and made the last authored frame snap
            // straight back to the native controller. Apply one fully settled
            // frame first, then relinquish the rig.
            if (elapsed >= _timing.Duration)
                Finish();
        }

        private static bool UsesOpenPalmPose(Motion motion) =>
            motion == Motion.StoneThrow ||
            motion == Motion.CounteringPalm ||
            motion == Motion.QuiveringPalm;

        private void ResolveAuthoredMotion()
        {
            _authoredClip = null;
            _mirrorAuthored = false;
            if (!_humanoidReady) return;
            string clipName;
            switch (_motion)
            {
                case Motion.Jab:
                    // Jab is the Monk's harder activated straight. A planted
                    // cross gives it a committed shoulder line distinct from
                    // the compact alternating jabs used by autoattack.
                    clipName = "ual_cross";
                    break;
                case Motion.ShoulderCheck:
                    // Shield_Dash begins and ends in a travelling shield pose.
                    // Removing its root translation leaves the avatar in a
                    // deep, one-knee tackle that reads as a broken animation.
                    // Cross supplies a planted martial stance; the authored
                    // shoulder-drive pass below tucks the arm and leads with
                    // the body so this remains distinct from a punch.
                    clipName = "ual_cross";
                    break;
                case Motion.TigerClaw:
                    clipName = "ual_hook";
                    break;
                case Motion.StoneThrow:
                    clipName = "mocap_hadouken";
                    break;
                case Motion.RoundKick:
                    clipName = "mocap_roundhouse";
                    break;
                case Motion.CrescentHeel:
                    clipName = "ual_kick";
                    _mirrorAuthored = true;
                    break;
                case Motion.CounteringPalm:
                    clipName = "ual_cross";
                    break;
                case Motion.EagleStrike:
                    clipName = "ual_hook";
                    _mirrorAuthored = true;
                    break;
                case Motion.FallingRain:
                    clipName = "ual_combo";
                    break;
                case Motion.Pummel:
                    clipName = "ual_combo";
                    break;
                case Motion.FlyingKick:
                    clipName = "mocap_front_kick";
                    break;
                case Motion.SweepingCrescent:
                    clipName = "mocap_roundhouse";
                    _mirrorAuthored = true;
                    break;
                case Motion.PressurePoint:
                    clipName = "ual_jab";
                    break;
                case Motion.WhirlwindKick:
                    clipName = "mocap_roundhouse";
                    break;
                case Motion.DragonPunch:
                    clipName = "ual_uppercut";
                    break;
                case Motion.QuiveringPalm:
                    // ShadowBoxing's straight has a wide side chamber that
                    // reads as attacking beside the target from the gameplay
                    // camera. The forward cross keeps the committed wind-up
                    // and contact in the target plane at finisher timing.
                    clipName = "ual_cross";
                    break;
                case Motion.GaleSplittingPalm:
                    clipName = "mocap_hadouken";
                    break;
                case Motion.VaultingTempest:
                    clipName = "mocap_front_kick";
                    break;
                case Motion.SkybreakerRush:
                    clipName = "ual_cross";
                    break;
                case Motion.AutoRight:
                    // Keep the clean target-facing jab hand path and supply
                    // its missing weight with the authored body-drive pass.
                    // Cross fights the mirrored arm aim on this rig and makes
                    // alternating fists visibly miss their contact line.
                    clipName = "ual_jab";
                    _mirrorAuthored = true;
                    break;
                case Motion.AutoLeft:
                    clipName = "ual_jab";
                    break;
                default:
                    return;
            }
            if (!MonkAuthoredMotionLibrary.TryGet(clipName,
                out _authoredClip))
                return;
            string mapping = _motion + "->" + clipName +
                (_mirrorAuthored ? "(mirrored)" : string.Empty);
            if (_authoredMappingsLogged.Add(mapping))
                SkillsPlugin.Log?.LogInfo(
                    "Monk animation authored mapping: " + mapping);
        }

        private void ApplyAuthoredMotion(float time, float impactTime,
            float blend)
        {
            if (_authoredClip == null || !_humanoidReady) return;
            float sampleTime = _motion == Motion.WhirlwindKick
                ? WhirlwindSampleTime(time, impactTime)
                : IsSignatureMotion(_motion)
                    ? SignatureSampleTime(time, impactTime)
                    : StrikeSampleTime(time, impactTime);
            _authoredClip.Sample(sampleTime, impactTime, _mirrorAuthored,
                _authoredPose, out _);
            float contact = ContactPulse(time, impactTime);
            if (_motion == Motion.SweepingCrescent)
                ApplyLowSweepPose(contact);
            float yaw = AuthoredYaw(time, impactTime);
            Quaternion yawRotation = Quaternion.AngleAxis(yaw, Up);
            Vector3 targetForward = Forward;
            Vector3 neutralForward = HorizontalDirection(null, _caster);
            float targetFacingWeight = time <= impactTime ? 1f :
                1f - SmoothStep01(Mathf.Clamp01(
                    (time - impactTime) /
                    Mathf.Max(1f - impactTime, 0.01f)));
            Vector3 recoveredForward = Vector3.Slerp(neutralForward,
                targetForward, targetFacingWeight).normalized;
            Vector3 recoveredRight =
                Vector3.Cross(Up, recoveredForward).normalized;
            Vector3 authoredForward = yawRotation * recoveredForward;
            Vector3 authoredRight = yawRotation * recoveredRight;
            bool upperBodyOnly = _motion == Motion.AutoRight ||
                _motion == Motion.AutoLeft;

            // A body transform must be solved once as a frame. Rotating the
            // pelvis independently toward the spine and both legs (and doing
            // the same to the chest for both shoulders and the neck) causes
            // each correction to invalidate the previous one. That was the
            // source of the sideways/cartwheel poses seen in the live pass.
            // Auto attacks borrow the jab's arm action, not its stepping
            // footwork. Keeping the native pelvis and legs planted prevents
            // a raised-knee recovery from leaking into every fist swing.
            if (!upperBodyOnly)
            {
                AlignBodyFrame(_hips,
                    MonkMotionJoint.Hips, MonkMotionJoint.Chest,
                    MonkMotionJoint.LeftUpperLeg,
                    MonkMotionJoint.RightUpperLeg,
                    authoredRight, Up, authoredForward, blend * 0.92f);
            }
            AlignBodyFrame(MotionBone(MonkMotionJoint.Chest),
                MonkMotionJoint.Chest, MonkMotionJoint.Head,
                MonkMotionJoint.LeftShoulder,
                MonkMotionJoint.RightShoulder,
                authoredRight, Up, authoredForward, blend * 0.90f);

            if (MotionBone(MonkMotionJoint.Neck) != null)
            {
                OrientJoint(MonkMotionJoint.Neck, MonkMotionJoint.Head,
                    authoredRight, Up, authoredForward, blend * 0.45f);
            }

            OrientJoint(MonkMotionJoint.LeftShoulder,
                MonkMotionJoint.LeftUpperArm, authoredRight, Up,
                authoredForward, blend * 0.72f);
            OrientJoint(MonkMotionJoint.LeftUpperArm,
                MonkMotionJoint.LeftLowerArm, authoredRight, Up,
                authoredForward, blend);
            OrientJoint(MonkMotionJoint.LeftLowerArm,
                MonkMotionJoint.LeftHand, authoredRight, Up,
                authoredForward, blend);
            OrientJoint(MonkMotionJoint.RightShoulder,
                MonkMotionJoint.RightUpperArm, authoredRight, Up,
                authoredForward, blend * 0.72f);
            OrientJoint(MonkMotionJoint.RightUpperArm,
                MonkMotionJoint.RightLowerArm, authoredRight, Up,
                authoredForward, blend);
            OrientJoint(MonkMotionJoint.RightLowerArm,
                MonkMotionJoint.RightHand, authoredRight, Up,
                authoredForward, blend);
            if (!upperBodyOnly)
            {
                OrientJoint(MonkMotionJoint.LeftUpperLeg,
                    MonkMotionJoint.LeftLowerLeg, authoredRight, Up,
                    authoredForward, blend);
                OrientJoint(MonkMotionJoint.LeftLowerLeg,
                    MonkMotionJoint.LeftFoot, authoredRight, Up,
                    authoredForward, blend);
                OrientJoint(MonkMotionJoint.LeftFoot,
                    MonkMotionJoint.LeftToes, authoredRight, Up,
                    authoredForward, blend);
                OrientJoint(MonkMotionJoint.RightUpperLeg,
                    MonkMotionJoint.RightLowerLeg, authoredRight, Up,
                    authoredForward, blend);
                OrientJoint(MonkMotionJoint.RightLowerLeg,
                    MonkMotionJoint.RightFoot, authoredRight, Up,
                    authoredForward, blend);
                OrientJoint(MonkMotionJoint.RightFoot,
                    MonkMotionJoint.RightToes, authoredRight, Up,
                    authoredForward, blend);
            }
            if (_motion == Motion.Jab || _motion == Motion.AutoRight ||
                _motion == Motion.AutoLeft)
                ApplyCommittedPunchDrive(time, impactTime, blend);
            float aimWeight = contact;
            if (_motion == Motion.QuiveringPalm)
            {
                // Once the finisher commits, keep its striking arm in the
                // target plane until the authored pose has fully blended out.
                // This prevents the source wind-up/recovery from leaking a
                // one-arm reach toward the player's side.
                float commitment = SmoothStep01(time /
                    Mathf.Max(impactTime * 0.52f, 0.01f));
                aimWeight = Mathf.Max(aimWeight, commitment * 0.90f);
            }
            if (_motion == Motion.StoneThrow)
                ApplyAuthoredHadouken(time, impactTime, blend);
            else if (_motion == Motion.GaleSplittingPalm)
                ApplyGaleSplittingPalm(time, impactTime, blend);
            else
                AimAuthoredStrike(aimWeight * blend);
            if (_motion == Motion.ShoulderCheck)
                ApplyAuthoredShoulderDrive(time, impactTime, blend);
            else if (_motion == Motion.VaultingTempest)
                ApplyVaultingTempest(time, impactTime, blend);
            else if (_motion == Motion.SkybreakerRush)
                ApplySkybreakerRush(time, impactTime, blend);
        }

        private void ApplyCommittedPunchDrive(float time,
            float impactTime, float blend)
        {
            bool jab = _motion == Motion.Jab;
            float side = _motion == Motion.AutoLeft ? -1f : 1f;
            float windup = WindupPulse(time, impactTime) *
                Mathf.Clamp01(blend);
            float contact = Mathf.Pow(ContactPulse(time, impactTime), 0.72f) *
                Mathf.Clamp01(blend);
            float followCenter = impactTime + (1f - impactTime) * 0.16f;
            float follow = time <= impactTime ? 0f : SmoothPulse(time,
                followCenter, (1f - impactTime) * 0.23f) *
                Mathf.Clamp01(blend);

            // Weight comes from contrast and a kinetic chain, not arm speed.
            // Counter-load the ribs, turn through contact, then overshoot the
            // key briefly before recovery. The visual chest advance carries
            // both shoulders toward the target without translating the player
            // collider or sliding the planted feet.
            float hipYaw = side * (-(jab ? 6f : 4f) * windup +
                (jab ? 10f : 8f) * contact +
                (jab ? 3f : 2f) * follow);
            float torsoYaw = side * (-(jab ? 15f : 11f) * windup +
                (jab ? 34f : 28f) * contact +
                (jab ? 10f : 8f) * follow);
            RotateAround(_hips, Up, hipYaw);
            RotateAround(Torso, Up, torsoYaw);
            RotateAround(Torso, Right,
                -(jab ? 3f : 2f) * windup +
                (jab ? 14f : 11f) * contact +
                (jab ? 5f : 4f) * follow);
            if (Torso != null)
            {
                float advance = -(jab ? 0.008f : 0.006f) * windup +
                    (jab ? 0.028f : 0.022f) * contact +
                    (jab ? 0.012f : 0.009f) * follow;
                Torso.position += Forward * (_bodyHeight * advance);
            }
        }

        private void ApplyGaleSplittingPalm(float time,
            float impactTime, float blend)
        {
            float weight = Mathf.Clamp01(blend);
            float release = SmootherStep01(
                ContactPulse(time, impactTime)) * weight;

            // Preserve the authored projectile wind-up. Forcing only the upper
            // arms into a crossed chamber left the elbows and wrists behind,
            // which produced a visibly broken guard. At contact, turn the
            // complete arm chains outward into the broad two-fist sweep that
            // releases the gale disc.
            RotateAround(Torso, Up, -8f * release);
            RotateAround(Torso, Right, 3f * release);
            Transform rightUpper = MotionBone(MonkMotionJoint.RightUpperArm);
            Transform rightLower = MotionBone(MonkMotionJoint.RightLowerArm);
            Transform rightHand = MotionBone(MonkMotionJoint.RightHand);
            if (rightUpper != null && rightHand != null)
            {
                RotateToward(rightUpper,
                    rightHand.position - rightUpper.position,
                    (Forward * 0.66f + Right * 0.72f +
                        Up * 0.04f).normalized, release);
            }
            if (rightLower != null && rightHand != null)
                RotateToward(rightLower,
                    rightHand.position - rightLower.position,
                    (Forward * 0.70f + Right * 0.71f).normalized,
                    release * 0.96f);
            Transform leftUpper = MotionBone(MonkMotionJoint.LeftUpperArm);
            Transform leftLower = MotionBone(MonkMotionJoint.LeftLowerArm);
            Transform leftHand = MotionBone(MonkMotionJoint.LeftHand);
            if (leftUpper != null && leftHand != null)
            {
                RotateToward(leftUpper,
                    leftHand.position - leftUpper.position,
                    (Forward * 0.66f - Right * 0.72f +
                        Up * 0.04f).normalized, release);
            }
            if (leftLower != null && leftHand != null)
                RotateToward(leftLower,
                    leftHand.position - leftLower.position,
                    (Forward * 0.70f - Right * 0.71f).normalized,
                    release * 0.96f);
        }

        private void ApplyVaultingTempest(float time,
            float impactTime, float blend)
        {
            float weight = Mathf.Clamp01(blend);
            float crouchIn = SmootherStep01(Mathf.InverseLerp(0.025f, 0.14f,
                time));
            float crouchOut = 1f - SmootherStep01(Mathf.InverseLerp(0.28f,
                0.38f, time));
            float crouch = crouchIn * crouchOut * weight;
            _vaultCrouchPeak = Mathf.Max(_vaultCrouchPeak, crouch);
            LastVaultingCrouchRatio = _vaultCrouchPeak;
            float legDrive = SmootherStep01(Mathf.InverseLerp(0.27f, 0.36f,
                time)) * (1f - SmootherStep01(Mathf.InverseLerp(0.43f,
                0.52f, time))) * weight;
            float launch = SmootherStep01(Mathf.InverseLerp(0.29f, 0.43f,
                time));
            float airPhase = SmootherStep01(Mathf.InverseLerp(
                0.29f, 0.91f, time));
            float air = Mathf.Sin(airPhase * Mathf.PI);
            float spin = SmootherStep01(Mathf.InverseLerp(0.30f, 0.82f,
                time));
            float chamber = SmootherStep01(Mathf.InverseLerp(0.27f, 0.36f,
                time)) * (1f - SmootherStep01(Mathf.InverseLerp(0.44f,
                0.52f, time))) * weight;
            float kickSweep = SmootherStep01(Mathf.InverseLerp(0.36f,
                impactTime, time)) * (1f - SmootherStep01(
                Mathf.InverseLerp(0.64f, 0.79f, time))) * weight;
            float kickExtension = SmootherStep01(Mathf.InverseLerp(0.41f,
                impactTime + 0.035f, time)) * (1f - SmootherStep01(
                Mathf.InverseLerp(0.63f, 0.76f, time))) * weight;
            float tuck = SmootherStep01(Mathf.InverseLerp(0.32f, 0.43f,
                time)) * (1f - SmootherStep01(Mathf.InverseLerp(0.73f,
                0.86f, time))) * weight;
            float landingCrouch = SmootherStep01(Mathf.InverseLerp(0.82f,
                0.91f, time)) * (1f - SmootherStep01(Mathf.InverseLerp(
                0.96f, 1f, time))) * weight;

            // Begin from a held, unmistakable low charge. The planted leg then
            // extends through the floor before the pelvis leaves the launch
            // plane. That sequencing is what makes the body propel the heel;
            // moving the heel first reads like the leg is being pulled upward.
            if (_hips != null)
                _hips.position -= Up * (_bodyHeight * 0.37f * crouch);
            RotateAround(_hips, Right, 13f * crouch);
            RotateAround(Torso, Right, -10f * crouch);
            ApplyVaultingCrouchLeg(MonkMotionJoint.LeftUpperLeg,
                MonkMotionJoint.LeftLowerLeg, MonkMotionJoint.LeftFoot,
                crouch);
            ApplyVaultingCrouchLeg(MonkMotionJoint.RightUpperLeg,
                MonkMotionJoint.RightLowerLeg, MonkMotionJoint.RightFoot,
                crouch);
            ApplyVaultingCrouchGuard(crouch);
            ApplyVaultingLaunchLeg(legDrive);
            ApplyVaultingKickChamber(chamber);

            // The pelvis rises and begins the somersault before the kicking
            // knee opens. The attack leg then sweeps through the same rotating
            // body frame, with the lower leg extending a few frames after the
            // thigh. This preserves a continuous kinetic chain from floor to
            // hip to heel instead of snapping a straight leg toward the sky.
            if (_hips != null)
                _hips.position += Up * (_bodyHeight * 0.64f * air * weight) +
                    Forward * (_bodyHeight * 0.07f * launch *
                        (1f - spin));
            RotateAround(_hips, Right, -360f * spin);
            ApplyVaultingKickSweep(kickSweep, kickExtension);
            ApplyVaultingTuckedLeg(tuck);
            RotateAround(Torso, Up, -10f * kickSweep);

            // Absorb the return with both knees and the hips rather than
            // popping directly back to the standing source pose.
            if (_hips != null)
                _hips.position -= Up * (_bodyHeight * 0.16f *
                    landingCrouch);
            ApplyVaultingCrouchLeg(MonkMotionJoint.LeftUpperLeg,
                MonkMotionJoint.LeftLowerLeg, MonkMotionJoint.LeftFoot,
                landingCrouch * 0.72f);
            ApplyVaultingCrouchLeg(MonkMotionJoint.RightUpperLeg,
                MonkMotionJoint.RightLowerLeg, MonkMotionJoint.RightFoot,
                landingCrouch * 0.72f);
            ApplyVaultingCrouchGuard(landingCrouch * 0.62f);
            KeepVaultingFeetAboveGround(air, weight);
        }

        private void ApplyVaultingLaunchLeg(float drive)
        {
            if (drive <= 0.001f) return;
            Transform upper = MotionBone(MonkMotionJoint.LeftUpperLeg);
            Transform lower = MotionBone(MonkMotionJoint.LeftLowerLeg);
            Transform foot = MotionBone(MonkMotionJoint.LeftFoot);
            Vector3 pushLine = (-Up * 0.98f - Forward * 0.16f -
                Right * 0.05f).normalized;
            if (upper != null && lower != null)
                RotateToward(upper, lower.position - upper.position,
                    pushLine, drive * 0.96f);
            if (lower != null && foot != null)
                RotateToward(lower, foot.position - lower.position,
                    pushLine, drive * 0.98f);
        }

        private void ApplyVaultingKickChamber(float chamber)
        {
            if (chamber <= 0.001f) return;
            Transform upper = MotionBone(MonkMotionJoint.RightUpperLeg);
            Transform lower = MotionBone(MonkMotionJoint.RightLowerLeg);
            Transform foot = MotionBone(MonkMotionJoint.RightFoot);
            Vector3 kneeLine = (Forward * 0.74f + Up * 0.64f +
                Right * 0.10f).normalized;
            Vector3 heelLine = (-Forward * 0.50f - Up * 0.85f +
                Right * 0.08f).normalized;
            if (upper != null && lower != null)
                RotateToward(upper, lower.position - upper.position,
                    kneeLine, chamber * 0.96f);
            if (lower != null && foot != null)
                RotateToward(lower, foot.position - lower.position,
                    heelLine, chamber * 0.94f);
        }

        private void ApplyVaultingKickSweep(float sweep, float extension)
        {
            if (sweep <= 0.001f && extension <= 0.001f) return;
            Transform upper = MotionBone(MonkMotionJoint.RightUpperLeg);
            Transform lower = MotionBone(MonkMotionJoint.RightLowerLeg);
            Transform foot = MotionBone(MonkMotionJoint.RightFoot);

            // The thigh leads the rising arc. The shin only straightens after
            // the pelvis is airborne and rotating, preventing an isolated
            // vertical-leg snap at the beginning of the move.
            Vector3 thighLine = (Up * 0.88f + Forward * 0.46f +
                Right * 0.07f).normalized;
            Vector3 heelLine = (Up * 0.82f + Forward * 0.56f +
                Right * 0.06f).normalized;
            if (upper != null && lower != null)
                RotateToward(upper, lower.position - upper.position,
                    thighLine, sweep * 0.96f);
            if (lower != null && foot != null)
                RotateToward(lower, foot.position - lower.position,
                    heelLine, extension * 0.98f);
        }

        private void ApplyVaultingTuckedLeg(float tuck)
        {
            if (tuck <= 0.001f) return;
            Transform upper = MotionBone(MonkMotionJoint.LeftUpperLeg);
            Transform lower = MotionBone(MonkMotionJoint.LeftLowerLeg);
            Transform foot = MotionBone(MonkMotionJoint.LeftFoot);
            Vector3 bodyUp = _hips != null ? _hips.up : Up;
            Vector3 bodyForward = _hips != null ? _hips.forward : Forward;
            Vector3 kneeLine = (bodyForward * 0.94f + bodyUp * 0.22f -
                Right * 0.12f).normalized;
            Vector3 heelLine = (-bodyForward * 0.90f + bodyUp * 0.32f +
                Right * 0.08f).normalized;
            if (upper != null && lower != null)
                RotateToward(upper, lower.position - upper.position,
                    kneeLine, tuck * 0.96f);
            if (lower != null && foot != null)
                RotateToward(lower, foot.position - lower.position,
                    heelLine, tuck * 0.96f);
        }

        private void KeepVaultingFeetAboveGround(float air, float weight)
        {
            if (!_groundAnchorReady || _caster == null || _hips == null ||
                weight <= 0.001f) return;
            Transform left = MotionBone(MonkMotionJoint.LeftFoot);
            Transform right = MotionBone(MonkMotionJoint.RightFoot);
            if (left == null || right == null) return;

            // The flip rotates the entire visual rig around the hips. Keep its
            // lowest foot above the launch plane so the tucked leg cannot cut
            // through terrain while the gameplay collider remains rooted.
            float clearance = _bodyHeight * 0.018f *
                SmootherStep01(Mathf.Clamp01(air * 3f));
            float leftLift = _leftFootAnchorLocalY + clearance - _caster
                .InverseTransformPoint(left.position).y;
            float rightLift = _rightFootAnchorLocalY + clearance - _caster
                .InverseTransformPoint(right.position).y;
            float requiredLift = Mathf.Max(0f, Mathf.Max(leftLift, rightLift));
            if (requiredLift > 0f)
                _hips.position += Up * requiredLift;
        }

        private void ApplyVaultingCrouchLeg(MonkMotionJoint upperJoint,
            MonkMotionJoint lowerJoint, MonkMotionJoint footJoint,
            float crouch)
        {
            if (crouch <= 0.001f) return;
            Transform upper = MotionBone(upperJoint);
            Transform lower = MotionBone(lowerJoint);
            Transform foot = MotionBone(footJoint);
            bool left = upperJoint == MonkMotionJoint.LeftUpperLeg;
            Vector3 outward = left ? -Right : Right;
            if (upper != null && lower != null)
                RotateToward(upper, lower.position - upper.position,
                    (outward * 0.56f + Forward * 0.18f -
                        Up * 0.81f).normalized,
                    crouch * 0.96f);
            if (lower != null && foot != null)
                RotateToward(lower, foot.position - lower.position,
                    (-outward * 0.12f - Forward * 0.30f -
                        Up * 0.95f).normalized,
                    crouch * 0.94f);
        }

        private void ApplyVaultingCrouchGuard(float crouch)
        {
            if (crouch <= 0.001f) return;
            Transform rightUpper = MotionBone(MonkMotionJoint.RightUpperArm);
            Transform rightLower = MotionBone(MonkMotionJoint.RightLowerArm);
            Transform rightHand = MotionBone(MonkMotionJoint.RightHand);
            Transform leftUpper = MotionBone(MonkMotionJoint.LeftUpperArm);
            Transform leftLower = MotionBone(MonkMotionJoint.LeftLowerArm);
            Transform leftHand = MotionBone(MonkMotionJoint.LeftHand);
            Vector3 rightElbow = (Forward * 0.18f + Right * 0.66f -
                Up * 0.36f).normalized;
            Vector3 rightFist = (Forward * 0.38f - Right * 0.30f +
                Up * 0.87f).normalized;
            Vector3 leftElbow = (Forward * 0.22f - Right * 0.62f -
                Up * 0.42f).normalized;
            Vector3 leftFist = (Forward * 0.42f + Right * 0.26f +
                Up * 0.87f).normalized;
            if (rightUpper != null && rightLower != null)
                RotateToward(rightUpper,
                    rightLower.position - rightUpper.position, rightElbow,
                    crouch * 0.92f);
            if (rightLower != null && rightHand != null)
                RotateToward(rightLower,
                    rightHand.position - rightLower.position, rightFist,
                    crouch * 0.90f);
            if (leftUpper != null && leftLower != null)
                RotateToward(leftUpper,
                    leftLower.position - leftUpper.position, leftElbow,
                    crouch * 0.92f);
            if (leftLower != null && leftHand != null)
                RotateToward(leftLower,
                    leftHand.position - leftLower.position, leftFist,
                    crouch * 0.90f);
        }

        private void ApplySkybreakerRush(float time,
            float impactTime, float blend)
        {
            float weight = Mathf.Clamp01(blend);
            float dash = time <= impactTime
                ? SmootherStep01(Mathf.InverseLerp(0.10f, impactTime, time))
                : 1f - SmootherStep01(Mathf.InverseLerp(impactTime, 0.94f,
                    time));
            float contact = SmootherStep01(ContactPulse(time, impactTime));
            float committedPunch = SmootherStep01(Mathf.InverseLerp(
                impactTime * 0.70f, impactTime, time)) *
                (1f - SmootherStep01(Mathf.InverseLerp(
                    impactTime + 0.10f, 0.93f, time)));
            float drive = Mathf.Max(contact, committedPunch * 0.96f) *
                weight;
            ApplySkybreakerRunCycle(time, impactTime, weight);
            if (drive <= 0.001f) return;
            if (_hips != null)
                _hips.position += Forward * (_bodyHeight * 0.18f * dash *
                    weight) - Up * (_bodyHeight * 0.075f * dash * weight);
            RotateAround(_hips, Right, 16f * dash);
            RotateAround(Torso, Right, 38f * drive);
            RotateAround(Torso, Up, -24f * drive);
            RedirectAuthoredPunch((Forward - Up * 0.015f).normalized,
                drive);

            Transform rightLower = MotionBone(
                MonkMotionJoint.RightLowerArm);
            Transform rightHand = MotionBone(MonkMotionJoint.RightHand);
            if (rightLower != null && rightHand != null)
                RotateToward(rightLower,
                    rightHand.position - rightLower.position,
                    Forward, drive * 0.98f);

            // Chamber the rear fist beside the ribs during the advancing
            // punch. A high guard hid the striking arm from the gameplay
            // camera and read like an overhead pose instead of a committed
            // point-blank finisher.
            Transform leftUpper = MotionBone(
                MonkMotionJoint.LeftUpperArm);
            Transform leftHand = MotionBone(MonkMotionJoint.LeftHand);
            if (leftUpper != null && leftHand != null)
                RotateToward(leftUpper,
                    leftHand.position - leftUpper.position,
                    (Forward * 0.30f - Right * 0.48f -
                        Up * 0.12f).normalized, drive * 0.92f);
        }

        private void ApplySkybreakerRunCycle(float time,
            float impactTime, float weight)
        {
            if (!_skybreakerRootMotionReady || weight <= 0.001f) return;
            float runIn = SmootherStep01(Mathf.InverseLerp(
                0.045f, 0.13f, time));
            float plant = SmootherStep01(Mathf.InverseLerp(
                impactTime * 0.72f, impactTime * 0.94f, time));
            float run = runIn * (1f - plant) * weight;
            if (run > 0.001f)
            {
                // Three alternating footfalls cover the authored dash. The
                // stride is tied to normalized travel time, so faster move
                // timing speeds both the legs and root motion together.
                float strideTime = Mathf.InverseLerp(0.05f,
                    impactTime * 0.82f, time);
                float stride = Mathf.Sin(strideTime * Mathf.PI * 3f);
                ApplySkybreakerStrideLeg(
                    MonkMotionJoint.LeftUpperLeg,
                    MonkMotionJoint.LeftLowerLeg,
                    MonkMotionJoint.LeftFoot, stride, run);
                ApplySkybreakerStrideLeg(
                    MonkMotionJoint.RightUpperLeg,
                    MonkMotionJoint.RightLowerLeg,
                    MonkMotionJoint.RightFoot, -stride, run);
                if (_hips != null)
                {
                    float footfall = 1f - Mathf.Abs(Mathf.Sin(
                        strideTime * Mathf.PI * 3f));
                    _hips.position += Up * (_bodyHeight * 0.022f *
                        footfall * run);
                }
                RotateAround(_hips, Right, 11f * run);
                RotateAround(_hips, Up, stride * 8f * run);
            }

            // Finish in a wide opposite-foot lead instead of letting the
            // collider slide beneath a static standing pose.
            float planted = plant * weight;
            ApplySkybreakerPlantedLeg(
                MonkMotionJoint.LeftUpperLeg,
                MonkMotionJoint.LeftLowerLeg,
                MonkMotionJoint.LeftFoot, true, planted);
            ApplySkybreakerPlantedLeg(
                MonkMotionJoint.RightUpperLeg,
                MonkMotionJoint.RightLowerLeg,
                MonkMotionJoint.RightFoot, false, planted);
        }

        private void ApplySkybreakerStrideLeg(MonkMotionJoint upperJoint,
            MonkMotionJoint lowerJoint, MonkMotionJoint footJoint,
            float stride, float weight)
        {
            Transform upper = MotionBone(upperJoint);
            Transform lower = MotionBone(lowerJoint);
            Transform foot = MotionBone(footJoint);
            bool left = upperJoint == MonkMotionJoint.LeftUpperLeg;
            Vector3 outward = left ? -Right : Right;
            float forwardSwing = stride * 0.82f;
            float kneeLift = Mathf.Max(0f, stride);
            if (upper != null && lower != null)
                RotateToward(upper, lower.position - upper.position,
                    (Forward * forwardSwing + outward * 0.10f - Up *
                        Mathf.Lerp(0.99f, 0.64f, kneeLift)).normalized,
                    weight * 0.98f);
            if (lower != null && foot != null)
                RotateToward(lower, foot.position - lower.position,
                    (-Forward * (kneeLift * 0.78f) - Up *
                        Mathf.Lerp(0.99f, 0.63f, kneeLift)).normalized,
                    weight * 0.98f);
        }

        private void ApplySkybreakerPlantedLeg(
            MonkMotionJoint upperJoint, MonkMotionJoint lowerJoint,
            MonkMotionJoint footJoint, bool lead, float weight)
        {
            if (weight <= 0.001f) return;
            Transform upper = MotionBone(upperJoint);
            Transform lower = MotionBone(lowerJoint);
            Transform foot = MotionBone(footJoint);
            Vector3 thigh = lead
                ? (Forward * 0.46f - Up * 0.89f).normalized
                : (-Forward * 0.34f - Up * 0.94f).normalized;
            Vector3 shin = lead
                ? (-Forward * 0.24f - Up * 0.97f).normalized
                : (Forward * 0.08f - Up * 0.997f).normalized;
            if (upper != null && lower != null)
                RotateToward(upper, lower.position - upper.position,
                    thigh, weight * 0.98f);
            if (lower != null && foot != null)
                RotateToward(lower, foot.position - lower.position,
                    shin, weight * 0.96f);
        }

        private void PrepareSignatureRootMotion()
        {
            if (_motion != Motion.SkybreakerRush || _caster == null ||
                _target == null || GameData.PlayerControl == null)
                return;
            Transform playerRoot = GameData.PlayerControl.transform;
            bool playerCaster = _caster == playerRoot ||
                _caster.IsChildOf(playerRoot) || playerRoot.IsChildOf(_caster);
            if (!playerCaster) return;
            Vector3 toward = _target.position - _caster.position;
            toward -= _caster.up * Vector3.Dot(toward, _caster.up);
            float distance = toward.magnitude;
            if (distance <= 0.88f || toward.sqrMagnitude <= 0.001f) return;
            Vector3 direction = toward.normalized;
            float advance = Mathf.Clamp(distance - 0.62f, 0f, 3.35f);
            if (advance <= 0.02f) return;
            _skybreakerRushStart = _caster.position;
            _skybreakerRushEnd = _skybreakerRushStart + direction * advance;
            _skybreakerMaximumAdvance = advance;
            _skybreakerRootMotionReady = true;
        }

        private void UpdateSignatureRootMotion(float time,
            float impactTime)
        {
            if (_motion != Motion.SkybreakerRush ||
                !_skybreakerRootMotionReady || _caster == null)
                return;
            // Cover most of the gap during the guarded rush, then carry the
            // final ten percent through the planted punch. This produces the
            // characteristic short post-contact slide without passing through
            // the target collider.
            float rush = SmootherStep01(Mathf.InverseLerp(0.10f,
                impactTime * 0.84f, time)) * 0.88f;
            float carry = SmootherStep01(Mathf.InverseLerp(
                impactTime * 0.84f, impactTime + 0.12f, time)) * 0.12f;
            float progress = Mathf.Clamp01(rush + carry);
            Vector3 desired = Vector3.Lerp(_skybreakerRushStart,
                _skybreakerRushEnd, progress);
            Vector3 delta = desired - _caster.position;
            delta -= _caster.up * Vector3.Dot(delta, _caster.up);
            if (delta.sqrMagnitude > 0.000001f)
            {
                CharacterController controller =
                    _caster.GetComponent<CharacterController>() ??
                    _caster.GetComponentInParent<CharacterController>();
                if (controller != null && controller.enabled)
                    controller.Move(delta);
                else
                    _caster.position += delta;
            }
            _skybreakerRushDistance = Mathf.Max(_skybreakerRushDistance,
                Vector3.Project(_caster.position - _skybreakerRushStart,
                    (_skybreakerRushEnd - _skybreakerRushStart).normalized)
                    .magnitude);
            LastSkybreakerRushDistance = _skybreakerRushDistance;
        }

        private static float WhirlwindSampleTime(float time,
            float impactTime)
        {
            // Reach the roundhouse contact pose smoothly, hold that real
            // extended-leg silhouette through most of the body turn, then
            // recover. The support-foot solver below keeps the pivot planted.
            float holdStart = Mathf.Max(0.28f, impactTime * 0.82f);
            float holdEnd = Mathf.Min(0.74f, impactTime + 0.24f);
            if (time <= holdStart)
                return impactTime * SmoothStep01(time /
                    Mathf.Max(holdStart, 0.01f));
            if (time <= holdEnd) return impactTime;
            return impactTime + (1f - impactTime) * SmoothStep01(
                (time - holdEnd) / Mathf.Max(1f - holdEnd, 0.01f));
        }

        private float SignatureSampleTime(float time, float impactTime)
        {
            // Ease into contact, preserve the readable strike silhouette for
            // a few frames, then ease back to neutral. This removes the
            // constant-speed interpolation that made the three showcase
            // techniques look like separate poses snapping together.
            float hold = _motion == Motion.SkybreakerRush ? 0.070f :
                _motion == Motion.GaleSplittingPalm ? 0.045f : 0.035f;
            if (time <= impactTime)
                return impactTime * SmootherStep01(time /
                    Mathf.Max(impactTime, 0.01f));
            float holdEnd = Mathf.Min(0.92f, impactTime + hold);
            if (time <= holdEnd) return impactTime;
            return impactTime + (1f - impactTime) * SmootherStep01(
                (time - holdEnd) / Mathf.Max(1f - holdEnd, 0.01f));
        }

        private float StrikeSampleTime(float time, float impactTime)
        {
            // Retargeted clips used to advance at a constant rate. Even when
            // their individual poses were correct, that made the wind-up,
            // contact, and recovery feel like equally weighted keyframes.
            // Ease both halves around the authoritative gameplay impact and
            // retain a very short hit-stop silhouette for single strikes.
            // Multi-hit chains keep their authored internal cadence.
            if (_motion == Motion.Pummel ||
                _motion == Motion.FallingRain)
                return time;

            float hold = _motion == Motion.Jab ? 0.075f :
                _motion == Motion.AutoRight || _motion == Motion.AutoLeft
                    ? 0.090f :
                _motion == Motion.DragonPunch ? 0.032f :
                _motion == Motion.FlyingKick ||
                _motion == Motion.SweepingCrescent ? 0.024f : 0.018f;
            if (time <= impactTime)
                return impactTime * SmootherStep01(time /
                    Mathf.Max(impactTime, 0.01f));
            float holdEnd = Mathf.Min(0.90f, impactTime + hold);
            if (time <= holdEnd) return impactTime;
            return impactTime + (1f - impactTime) * SmootherStep01(
                (time - holdEnd) / Mathf.Max(1f - holdEnd, 0.01f));
        }

        private void ApplyAuthoredHadouken(float time,
            float impactTime, float blend)
        {
            float release = Mathf.Pow(ContactPulse(time, impactTime), 0.72f) *
                Mathf.Clamp01(blend);
            if (release <= 0.001f) return;

            Transform leftUpper = MotionBone(
                MonkMotionJoint.LeftUpperArm);
            Transform leftHand = MotionBone(MonkMotionJoint.LeftHand);
            Transform rightUpper = MotionBone(
                MonkMotionJoint.RightUpperArm);
            Transform rightHand = MotionBone(MonkMotionJoint.RightHand);
            Vector3 leftDirection = (Forward - Right * 0.055f +
                Up * 0.08f).normalized;
            Vector3 rightDirection = (Forward + Right * 0.055f +
                Up * 0.08f).normalized;

            if (leftUpper != null && leftHand != null)
                RotateToward(leftUpper,
                    leftHand.position - leftUpper.position,
                    leftDirection, Mathf.Clamp01(release * 0.92f));
            if (rightUpper != null && rightHand != null)
                RotateToward(rightUpper,
                    rightHand.position - rightUpper.position,
                    rightDirection, Mathf.Clamp01(release * 0.92f));
            RotateAround(Torso, Right, 5f * release);
        }

        private void ApplyAuthoredShoulderDrive(float time,
            float impactTime, float blend)
        {
            float drive = Mathf.Pow(ContactPulse(time, impactTime), 0.72f) *
                Mathf.Clamp01(blend);
            if (drive <= 0.001f) return;

            // A compact shoulder check is a planted body strike: the chest
            // folds and turns into contact while the leading elbow stays
            // tucked. Do not translate the rig or borrow a running pose.
            RotateAround(_hips, Right, 4f * drive);
            RotateAround(Torso, Right, 14f * drive);
            RotateAround(Torso, Up, -18f * drive);

            Transform upperArm = MotionBone(
                MonkMotionJoint.RightUpperArm);
            Transform lowerArm = MotionBone(
                MonkMotionJoint.RightLowerArm);
            Transform hand = MotionBone(MonkMotionJoint.RightHand);
            if (upperArm == null || lowerArm == null || hand == null)
                return;

            Vector3 tuckedUpper = (-Up * 0.82f + Forward * 0.34f -
                Right * 0.12f).normalized;
            RotateToward(upperArm,
                lowerArm.position - upperArm.position,
                tuckedUpper, Mathf.Clamp01(drive * 0.92f));

            Vector3 guardedForearm = (Up * 0.62f + Forward * 0.18f -
                Right * 0.32f).normalized;
            RotateToward(lowerArm, hand.position - lowerArm.position,
                guardedForearm, Mathf.Clamp01(drive * 0.88f));
        }

        private void AimAuthoredStrike(float weight)
        {
            if (!TryGetStrikeArm(out Transform upperArm,
                out Transform hand, out Vector3 desired) ||
                weight <= 0.001f) return;
            // Authored clips keep their real elbow and wrist motion, but the
            // whole arm chain must finish toward the selected target. Without
            // this correction, side-oriented source punches (especially
            // Quivering Palm) can visibly miss an enemy in front of the player.
            RotateToward(upperArm, hand.position - upperArm.position,
                desired, Mathf.Clamp01(weight * 0.96f));
        }

        private bool TryGetStrikeArm(out Transform upperArm,
            out Transform hand, out Vector3 desired)
        {
            bool left;
            float vertical = 0f;
            switch (_motion)
            {
                case Motion.Jab:
                case Motion.TigerClaw:
                case Motion.CounteringPalm:
                case Motion.FallingRain:
                case Motion.Pummel:
                case Motion.QuiveringPalm:
                case Motion.SkybreakerRush:
                case Motion.AutoRight:
                    left = false;
                    break;
                case Motion.EagleStrike:
                    left = true;
                    vertical = -0.12f;
                    break;
                case Motion.PressurePoint:
                    left = true;
                    break;
                case Motion.DragonPunch:
                    left = true;
                    vertical = 0.55f;
                    break;
                case Motion.AutoLeft:
                    left = true;
                    break;
                case Motion.StoneThrow:
                    left = false;
                    vertical = 0.08f;
                    break;
                default:
                    upperArm = null;
                    hand = null;
                    desired = Vector3.zero;
                    return false;
            }
            upperArm = MotionBone(left
                ? MonkMotionJoint.LeftUpperArm
                : MonkMotionJoint.RightUpperArm);
            hand = MotionBone(left
                ? MonkMotionJoint.LeftHand
                : MonkMotionJoint.RightHand);
            desired = (Forward + Up * vertical).normalized;
            return upperArm != null && hand != null;
        }

        private float PoseBlendWeight(float normalized)
        {
            float duration = Mathf.Max(_timing.Duration, 0.01f);
            bool signature = IsSignatureMotion(_motion);
            float entrySeconds = signature ? 0.13f : BlendInSeconds;
            float entryWindow = Mathf.Clamp(
                entrySeconds / duration, 0.08f, 0.24f);
            float postContactHold = 0.04f;
            bool autoPunch = _motion == Motion.AutoRight ||
                _motion == Motion.AutoLeft;
            float maximumRecoveryWindow = Mathf.Clamp(
                1f - (_timing.Impact + postContactHold) / duration,
                0.16f, autoPunch ? 0.62f : 0.52f);
            float recoverySeconds = _motion == Motion.EagleStrike
                ? 0.32f
                : signature ? 0.34f : RecoveryBlendSeconds;
            float recoveryWindow = Mathf.Clamp(
                recoverySeconds / duration, 0.16f,
                maximumRecoveryWindow);
            float entry = signature
                ? SmootherStep01(normalized / entryWindow)
                : SmoothStep01(normalized / entryWindow);
            float recovery = signature
                ? SmootherStep01((1f - normalized) / recoveryWindow)
                : SmoothStep01((1f - normalized) / recoveryWindow);
            return Mathf.Min(entry, recovery);
        }

        private static bool IsSignatureMotion(Motion motion) =>
            motion == Motion.GaleSplittingPalm ||
            motion == Motion.VaultingTempest ||
            motion == Motion.SkybreakerRush;

        private float ContinuityStartNormalized()
        {
            float duration = Mathf.Max(_timing.Duration, 0.01f);
            return 1f - Mathf.Clamp(
                ContinuitySampleSeconds / duration, 0.10f, 0.28f);
        }

        private void RestoreHipAnchor(float weight)
        {
            if (!_groundAnchorReady || _hips == null || _caster == null)
                return;
            _hips.position = Vector3.Lerp(_hips.position,
                _caster.TransformPoint(_hipsAnchorLocal),
                Mathf.Clamp01(weight));
        }

        private void CaptureAutoPunchLegPose()
        {
            _autoLegPoseReady = false;
            if (_motion != Motion.AutoRight && _motion != Motion.AutoLeft)
                return;
            for (int joint = (int)MonkMotionJoint.LeftUpperLeg;
                joint <= (int)MonkMotionJoint.RightToes; joint++)
            {
                Transform bone = _motionBones[joint];
                if (bone == null) return;
                _autoLegPose[joint] = bone.localRotation;
            }
            _autoLegPoseReady = true;
        }

        private void RestoreAutoPunchLegPose(float poseBlend)
        {
            if (!_autoLegPoseReady) return;
            // Hold the grounded pose through contact and most of recovery,
            // then ease back to the native controller before handoff.
            float weight = SmootherStep01(Mathf.Clamp01(poseBlend * 1.9f));
            for (int joint = (int)MonkMotionJoint.LeftUpperLeg;
                joint <= (int)MonkMotionJoint.RightToes; joint++)
            {
                Transform bone = _motionBones[joint];
                if (bone == null) continue;
                bone.localRotation = Quaternion.Slerp(bone.localRotation,
                    _autoLegPose[joint], weight);
            }
        }

        private void PlantSupportFoot(float weight)
        {
            // Rising Dragon Fist deliberately leaves the ground for its
            // uppercut silhouette. The visual rig returns to its anchored
            // position during recovery while the gameplay collider remains
            // safely rooted.
            if (_motion == Motion.DragonPunch ||
                _motion == Motion.VaultingTempest ||
                _motion == Motion.SkybreakerRush) return;
            if (!TryGetSupportFoot(out Transform supportFoot,
                out float anchorLocalY)) return;
            float currentLocalY = _caster
                .InverseTransformPoint(supportFoot.position).y;
            _hips.position += Up * ((anchorLocalY - currentLocalY) *
                Mathf.Clamp01(weight));
        }

        private bool TryGetSupportFoot(out Transform supportFoot,
            out float anchorLocalY)
        {
            supportFoot = null;
            anchorLocalY = 0f;
            if (!_groundAnchorReady || _caster == null || _hips == null)
                return false;

            bool useRight;
            switch (_motion)
            {
                case Motion.CrescentHeel:
                case Motion.SweepingCrescent:
                case Motion.EagleStrike:
                case Motion.PressurePoint:
                case Motion.DragonPunch:
                case Motion.AutoLeft:
                    useRight = true;
                    break;
                case Motion.RoundKick:
                case Motion.FlyingKick:
                case Motion.WhirlwindKick:
                case Motion.Jab:
                case Motion.ShoulderCheck:
                case Motion.TigerClaw:
                case Motion.StoneThrow:
                case Motion.CounteringPalm:
                case Motion.FallingRain:
                case Motion.QuiveringPalm:
                case Motion.AutoRight:
                    useRight = false;
                    break;
                default:
                    Transform left = MotionBone(MonkMotionJoint.LeftFoot);
                    Transform right = MotionBone(MonkMotionJoint.RightFoot);
                    if (left == null || right == null) return false;
                    float leftError = Mathf.Abs(_caster
                        .InverseTransformPoint(left.position).y -
                        _leftFootAnchorLocalY);
                    float rightError = Mathf.Abs(_caster
                        .InverseTransformPoint(right.position).y -
                        _rightFootAnchorLocalY);
                    useRight = rightError < leftError;
                    break;
            }
            supportFoot = MotionBone(useRight
                ? MonkMotionJoint.RightFoot : MonkMotionJoint.LeftFoot);
            anchorLocalY = useRight
                ? _rightFootAnchorLocalY : _leftFootAnchorLocalY;
            return supportFoot != null;
        }

        internal bool TryGetQaPoseMetrics(out bool aimRequired,
            out float aimDot, out float groundErrorRatio,
            out float horizontalAnchorErrorRatio)
        {
            aimRequired = TryGetStrikeArm(out Transform upperArm,
                out Transform hand, out Vector3 desired);
            aimDot = aimRequired
                ? Vector3.Dot((hand.position - upperArm.position).normalized,
                    desired.normalized)
                : 1f;
            if (!TryGetSupportFoot(out Transform supportFoot,
                out float anchorLocalY))
            {
                groundErrorRatio = float.PositiveInfinity;
                horizontalAnchorErrorRatio = float.PositiveInfinity;
                return false;
            }
            float scale = Mathf.Max(_bodyHeight, 0.01f);
            groundErrorRatio = Mathf.Abs(_caster
                .InverseTransformPoint(supportFoot.position).y -
                anchorLocalY) / scale;
            Vector3 currentHipsLocal =
                _caster.InverseTransformPoint(_hips.position);
            horizontalAnchorErrorRatio = new Vector2(
                currentHipsLocal.x - _hipsAnchorLocal.x,
                currentHipsLocal.z - _hipsAnchorLocal.z).magnitude / scale;
            return true;
        }

        internal bool TryGetQaRecoveryMetrics(
            out float maxAngularSpeed,
            out float maxHipSpeedRatio,
            out int samples)
        {
            maxAngularSpeed = _qaMaxRecoveryAngularSpeed;
            maxHipSpeedRatio = _qaMaxRecoveryHipSpeedRatio;
            samples = _qaRecoverySamples;
            return samples > 0;
        }

        private void RecordQaContinuity(float normalized)
        {
            RecordQaFrame(normalized >= ContinuityStartNormalized());
        }

        private void RecordQaFrame(bool measure)
        {
            if (_hips == null || _caster == null) return;
            Vector3 hipsLocal =
                _caster.InverseTransformPoint(_hips.position);
            float deltaTime = Time.deltaTime;
            if (_qaPreviousPoseReady && measure &&
                deltaTime > 0.001f && deltaTime <= 0.05f)
            {
                float maxAngle = 0f;
                for (int index = 0; index < _motionBones.Length; index++)
                {
                    Transform bone = _motionBones[index];
                    if (bone == null) continue;
                    maxAngle = Mathf.Max(maxAngle, Quaternion.Angle(
                        _qaPreviousRotations[index], bone.rotation));
                }
                _qaMaxRecoveryAngularSpeed = Mathf.Max(
                    _qaMaxRecoveryAngularSpeed, maxAngle / deltaTime);
                _qaMaxRecoveryHipSpeedRatio = Mathf.Max(
                    _qaMaxRecoveryHipSpeedRatio,
                    Vector3.Distance(_qaPreviousHipsLocal, hipsLocal) /
                    Mathf.Max(_bodyHeight * deltaTime, 0.001f));
                _qaRecoverySamples++;
            }

            for (int index = 0; index < _motionBones.Length; index++)
            {
                Transform bone = _motionBones[index];
                if (bone != null)
                    _qaPreviousRotations[index] = bone.rotation;
            }
            _qaPreviousHipsLocal = hipsLocal;
            _qaPreviousPoseReady = true;
        }

        private float AuthoredYaw(float time, float impactTime)
        {
            float impactProgress = SmoothStep01(Mathf.Clamp01(time /
                Mathf.Max(impactTime, 0.01f)));
            if (_motion == Motion.RoundKick)
            {
                if (time <= impactTime)
                    return -80f * impactProgress +
                        AnticipationYaw(time, impactTime, 1f);
                float recovery = SmoothStep01(Mathf.Clamp01(
                    (time - impactTime) /
                    Mathf.Max(1f - impactTime, 0.01f)));
                return -80f * (1f - recovery);
            }
            if (_motion == Motion.SweepingCrescent)
            {
                if (time <= impactTime)
                    return 80f * impactProgress +
                        AnticipationYaw(time, impactTime, -1f);
                float recovery = SmoothStep01(Mathf.Clamp01(
                    (time - impactTime) /
                    Mathf.Max(1f - impactTime, 0.01f)));
                return 80f + 280f * recovery;
            }
            if (_motion == Motion.WhirlwindKick)
            {
                // One eased, uninterrupted revolution avoids the stop-start
                // jerk at contact while the authored kick pose is held.
                return 360f * SmoothStep01(Mathf.InverseLerp(
                    0.18f, 0.80f, time));
            }
            // Straight punches and palms carry no authored yaw of their own.
            // A small shoulder-line counter-rotation before the release gives
            // the eye the coil it needs to read the strike as a strike rather
            // than an arm extending on a timer.
            return AnticipationYaw(time, impactTime, 1f);
        }

        /// <summary>
        /// Counter-rotation during the anticipation window, signed so it always
        /// opposes the direction the technique is about to travel.
        /// </summary>
        private float AnticipationYaw(float time, float impactTime,
            float strikeSign)
        {
            if (_motion == Motion.WhirlwindKick ||
                _motion == Motion.VaultingTempest ||
                _motion == Motion.SkybreakerRush)
                return 0f;
            return -strikeSign * AnticipationDegrees *
                AnticipationPulse(time, impactTime);
        }

        private void ApplyLowSweepPose(float weight)
        {
            // Sweeping Crescent mirrors the authored roundhouse, making the
            // left leg the striking leg. Preserve the horizontal arc while
            // lowering the knee, foot, and toes into a true ankle-height
            // sweep instead of reusing the source clip's head-height contact.
            int lowerIndex = (int)MonkMotionJoint.LeftLowerLeg;
            int footIndex = (int)MonkMotionJoint.LeftFoot;
            int toesIndex = (int)MonkMotionJoint.LeftToes;
            float footDrop = Mathf.Min(0f,
                -0.58f - _authoredPose[footIndex].y) *
                Mathf.Clamp01(weight);
            _authoredPose[lowerIndex].y += footDrop * 0.55f;
            _authoredPose[footIndex].y += footDrop;
            _authoredPose[toesIndex].y += footDrop;
        }

        private void AlignBodyFrame(Transform transform,
            MonkMotionJoint originJoint, MonkMotionJoint topJoint,
            MonkMotionJoint leftJoint, MonkMotionJoint rightJoint,
            Vector3 right, Vector3 up, Vector3 forward, float weight)
        {
            Transform origin = MotionBone(originJoint);
            Transform top = MotionBone(topJoint);
            Transform left = MotionBone(leftJoint);
            Transform rightBone = MotionBone(rightJoint);
            if (transform == null || origin == null || top == null ||
                left == null || rightBone == null) return;
            Vector3 desiredUp = AuthoredWorldVector(
                _authoredPose[(int)topJoint] -
                _authoredPose[(int)originJoint], right, up, forward);
            Vector3 desiredRight = AuthoredWorldVector(
                _authoredPose[(int)rightJoint] -
                _authoredPose[(int)leftJoint], right, up, forward);
            RotateFrameToward(transform, top.position - origin.position,
                rightBone.position - left.position, desiredUp, desiredRight,
                weight);
        }

        private void OrientJoint(MonkMotionJoint parentJoint,
            MonkMotionJoint childJoint, Vector3 right, Vector3 up,
            Vector3 forward, float weight)
        {
            Transform parent = MotionBone(parentJoint);
            Transform child = MotionBone(childJoint);
            if (parent == null || child == null) return;
            Vector3 desired = AuthoredWorldVector(
                _authoredPose[(int)childJoint] -
                _authoredPose[(int)parentJoint], right, up, forward);
            RotateToward(parent, child.position - parent.position,
                desired, weight);
        }

        private static Vector3 AuthoredWorldVector(Vector3 authored,
            Vector3 right, Vector3 up, Vector3 forward)
        {
            return right * authored.x + up * authored.y +
                forward * authored.z;
        }

        private static void RotateFrameToward(Transform transform,
            Vector3 currentUp, Vector3 currentRight, Vector3 desiredUp,
            Vector3 desiredRight, float weight)
        {
            if (transform == null || currentUp.sqrMagnitude < 0.000001f ||
                currentRight.sqrMagnitude < 0.000001f ||
                desiredUp.sqrMagnitude < 0.000001f ||
                desiredRight.sqrMagnitude < 0.000001f) return;

            currentUp.Normalize();
            desiredUp.Normalize();
            currentRight = Vector3.ProjectOnPlane(currentRight, currentUp);
            desiredRight = Vector3.ProjectOnPlane(desiredRight, desiredUp);
            if (currentRight.sqrMagnitude < 0.000001f ||
                desiredRight.sqrMagnitude < 0.000001f) return;
            currentRight.Normalize();
            desiredRight.Normalize();

            Quaternion swing = Quaternion.FromToRotation(currentUp,
                desiredUp);
            Vector3 swungRight = Vector3.ProjectOnPlane(
                swing * currentRight, desiredUp);
            if (swungRight.sqrMagnitude < 0.000001f) return;
            swungRight.Normalize();
            float twist = Vector3.SignedAngle(swungRight, desiredRight,
                desiredUp);
            Quaternion delta = Quaternion.AngleAxis(twist, desiredUp) *
                swing;
            transform.rotation = Quaternion.Slerp(Quaternion.identity,
                delta, Mathf.Clamp01(weight)) * transform.rotation;
        }

        private void ApplyMotion(float time, float impactTime)
        {
            switch (_motion)
            {
                case Motion.Jab:
                    ApplyPunch(time, impactTime, 0.02f, 0.03f, 20f);
                    break;
                case Motion.ShoulderCheck:
                    ApplyShoulderCheck(time, impactTime);
                    break;
                case Motion.TigerClaw:
                    ApplyPunch(time, impactTime, 0.16f, 0.05f, 18f);
                    RotateAround(_rightHand, Forward, -40f *
                        ContactPulse(time, impactTime));
                    break;
                case Motion.StoneThrow:
                    ApplyThrow(time, impactTime);
                    break;
                case Motion.RoundKick:
                    ApplyKick(time, impactTime, 18f, 0.05f);
                    break;
                case Motion.CrescentHeel:
                    ApplyKick(time, impactTime, -24f, 0.08f);
                    break;
                case Motion.CounteringPalm:
                    ApplyPalm(time, impactTime, -0.08f, 0.08f, -18f);
                    break;
                case Motion.EagleStrike:
                    ApplyPunch(time, impactTime, 0.08f, -0.22f, 15f);
                    RotateAround(_rightHand, Forward, 55f *
                        ContactPulse(time, impactTime));
                    break;
                case Motion.FallingRain:
                    ApplyFallingRain(time);
                    break;
                case Motion.Pummel:
                    ApplyPummel(time);
                    break;
                case Motion.FlyingKick:
                    ApplyFlyingKick(time, impactTime);
                    break;
                case Motion.SweepingCrescent:
                    ApplySweep(time, impactTime);
                    break;
                case Motion.PressurePoint:
                    ApplyPunch(time, impactTime, -0.02f, 0.02f, -10f);
                    break;
                case Motion.WhirlwindKick:
                    ApplyWhirlwind(time, impactTime);
                    break;
                case Motion.DragonPunch:
                    ApplyDragonPunch(time, impactTime);
                    break;
                case Motion.QuiveringPalm:
                    ApplyPalm(time, impactTime, 0.01f, 0.10f, 28f);
                    break;
                case Motion.AutoRight:
                    ApplyPunch(time, impactTime, 0.03f, 0.01f, 14f);
                    break;
                case Motion.AutoLeft:
                    ApplyPunch(time, impactTime, -0.03f, 0.01f, -14f);
                    break;
            }
        }

        private void ApplyPunch(float time, float impactTime, float lateral,
            float vertical, float twistDegrees)
        {
            float contact = ContactPulse(time, impactTime);
            RedirectAuthoredPunch((Forward + Right * lateral +
                Up * vertical).normalized, contact * 0.72f);
            RotateAround(_hips, Up, twistDegrees * contact);
            RotateAround(Torso, Up, twistDegrees * 1.35f * contact);
            RotateAround(Torso, Right, 4f * contact);
            OffsetHips(Forward * (0.07f * contact));
        }

        private void ApplyPalm(float time, float impactTime, float lateral,
            float vertical, float twistDegrees)
        {
            ApplyPunch(time, impactTime, lateral, vertical, twistDegrees);
        }

        private void ApplyShoulderCheck(float time, float impactTime)
        {
            float contact = ContactPulse(time, impactTime);
            float drive = Mathf.Pow(contact, 0.75f);
            OffsetHips(Forward * (0.24f * drive) - Up * (0.05f * drive));
            RotateAround(_hips, Right, 7f * drive);
            RotateAround(Torso, Right, 18f * drive);
            RotateAround(Torso, Up, -22f * drive);
            RedirectAuthoredPunch((Forward * 0.82f + Right * 0.28f +
                Up * 0.12f).normalized, drive * 0.42f);
        }

        private void ApplyThrow(float time, float impactTime)
        {
            float contact = ContactPulse(time, impactTime);
            float windup = WindupPulse(time, impactTime);
            RedirectAuthoredPunch((Forward * 0.92f + Up * 0.38f +
                Right * 0.08f).normalized, contact * 0.58f);
            RotateAround(Torso, Up, 30f * windup - 20f * contact);
        }

        private void ApplyFallingRain(float time)
        {
            float first = SmoothPulse(time, 0.24f, 0.20f);
            float second = SmoothPulse(time, 0.49f, 0.18f);
            float third = SmoothPulse(time, 0.73f, 0.19f);
            float drive = Mathf.Max(first, Mathf.Max(second, third));
            RedirectAuthoredPunch(Forward, drive * 0.58f);
            RotateAround(Torso, Up, -18f * first + 18f * second -
                24f * third);
            OffsetHips(Forward * (0.06f * drive));
        }

        private void ApplyPummel(float time)
        {
            float drive = 0f;
            float twist = 0f;
            for (int index = 0; index < 6; index++)
            {
                float center = 0.18f + index * 0.115f;
                float pulse = SmoothPulse(time, center, 0.105f);
                drive = Mathf.Max(drive, pulse);
                float side = index % 2 == 0 ? -1f : 1f;
                twist += side * pulse * 15f;

                bool left = index % 2 != 0;
                Transform upperArm = MotionBone(left
                    ? MonkMotionJoint.LeftUpperArm
                    : MonkMotionJoint.RightUpperArm);
                Transform hand = MotionBone(left
                    ? MonkMotionJoint.LeftHand
                    : MonkMotionJoint.RightHand);
                if (upperArm != null && hand != null)
                {
                    Vector3 desired = (Forward + Right * side * 0.055f +
                        Up * (0.02f + (index % 3) * 0.035f)).normalized;
                    RotateToward(upperArm,
                        hand.position - upperArm.position, desired,
                        Mathf.Clamp01(pulse * 0.94f));
                }
            }
            RedirectAuthoredPunch(Forward, drive * 0.58f);
            RotateAround(_hips, Up, twist * 0.62f);
            RotateAround(Torso, Up, twist);
            if (_hips != null)
                _hips.position += Forward * (0.075f * drive);
        }

        private void ApplyDragonPunch(float time, float impactTime)
        {
            float contact = ContactPulse(time, impactTime);
            float crouch = WindupPulse(time, impactTime);
            float launchStart = impactTime * 0.68f;
            float launchPeak = Mathf.Min(0.78f, impactTime + 0.14f);
            float launch = time <= launchPeak
                ? SmoothStep01(Mathf.InverseLerp(
                    launchStart, launchPeak, time))
                : 1f - SmoothStep01(Mathf.InverseLerp(
                    launchPeak, 0.96f, time));
            launch = Mathf.Clamp01(launch);

            if (_hips != null)
                _hips.position += -Up * (0.18f * crouch) +
                    Up * (0.46f * launch) +
                    Forward * (0.14f * launch);
            RotateAround(_hips, Right,
                10f * crouch - 8f * launch);
            RotateAround(Torso, Up, 34f * contact);
            RotateAround(Torso, Right,
                18f * crouch - 24f * launch);
            RedirectAuthoredPunch((Forward * 0.40f + Up * 0.91f +
                Right * 0.04f).normalized,
                Mathf.Clamp01(contact * 0.92f + launch * 0.30f));
        }

        private void ApplyKick(float time, float impactTime,
            float spinDegrees, float lift)
        {
            float contact = ContactPulse(time, impactTime);
            RotateAround(_hips, Up, spinDegrees * contact);
            RotateAround(Torso, Up, spinDegrees * 1.45f * contact);
            RotateAround(Torso, Right, -7f * contact);
            OffsetHips(Up * (lift * contact));
        }

        private void ApplyFlyingKick(float time, float impactTime)
        {
            float air = Mathf.Sin(Mathf.PI * time);
            float travel = Mathf.Sin(Mathf.PI * Mathf.Clamp01(time * 0.92f));
            OffsetHips(Up * (0.74f * air) + Forward * (0.58f * travel));
            RotateAround(_hips, Right, -10f * air);
            RotateAround(Torso, Right, 24f * air);
        }

        private void ApplySweep(float time, float impactTime)
        {
            float contact = ContactPulse(time, impactTime);
            float spin = SmoothStep01(Mathf.Clamp01(time /
                Mathf.Max(impactTime * 1.35f, 0.01f)));
            OffsetHips(-Up * (0.28f * contact));
            RotateAround(_hips, Up, 360f * spin);
            RotateAround(Torso, Right, 14f * contact);
        }

        private void ApplyWhirlwind(float time, float impactTime)
        {
            float contact = ContactPulse(time, impactTime);
            float spin = SmoothStep01(time);
            OffsetHips(Up * (0.10f * Mathf.Sin(Mathf.PI * time)));
            RotateAround(_hips, Up, 360f * spin);
            RotateAround(Torso, Right, -9f * contact);
        }

        private static void RotateToward(Transform joint, Vector3 current,
            Vector3 desired, float weight)
        {
            if (joint == null || current.sqrMagnitude < 0.000001f ||
                desired.sqrMagnitude < 0.000001f) return;
            Quaternion delta = Quaternion.FromToRotation(current, desired);
            joint.rotation = Quaternion.Slerp(joint.rotation,
                delta * joint.rotation, weight);
        }

        private void RedirectAuthoredPunch(Vector3 desiredDirection,
            float weight)
        {
            if (_punchController == null || _rightUpperArm == null ||
                _rightHand == null || weight <= 0.001f) return;
            // Rotate the authored upper-arm chain as one piece. The clip keeps
            // its real elbow bend and hand path; only the strike direction is
            // steered toward the current target.
            RotateToward(_rightUpperArm,
                _rightHand.position - _rightUpperArm.position,
                desiredDirection, Mathf.Clamp01(weight));
        }

        private void OffsetHips(Vector3 worldOffset)
        {
            // Humanoid motion is visually rooted to the live player collider.
            // Positional fallback offsets are only safe when grounding could
            // not be initialized.
            if (_hips != null && !_groundAnchorReady)
                _hips.position += worldOffset;
        }

        private static void RotateAround(Transform bone, Vector3 worldAxis,
            float degrees)
        {
            if (bone == null || Mathf.Abs(degrees) < 0.001f) return;
            bone.rotation = Quaternion.AngleAxis(degrees,
                worldAxis.normalized) * bone.rotation;
        }

        private Vector3 Forward => HorizontalDirection(_target, _caster);
        private Vector3 Up => _caster != null ? _caster.up : Vector3.up;
        private Vector3 Right => Vector3.Cross(Up, Forward).normalized;

        private static Vector3 HorizontalDirection(Transform target,
            Transform caster)
        {
            if (caster == null) return Vector3.forward;
            Vector3 forward = caster.forward;
            if (target != null)
            {
                Vector3 toward = target.position - caster.position;
                toward -= caster.up * Vector3.Dot(toward, caster.up);
                if (toward.sqrMagnitude > 0.001f) forward = toward.normalized;
            }
            return forward.sqrMagnitude > 0.001f
                ? forward.normalized : Vector3.forward;
        }

        /// <summary>
        /// Strike timing. This used to smoothstep symmetrically in and out of
        /// contact, which accelerates and decelerates the limb at the same
        /// rate and is why every technique read as floaty and interchangeable.
        /// A real strike coils slowly, accelerates hard into contact, stops
        /// dead, then settles back more slowly than it went out, overshooting
        /// slightly before it comes to rest.
        /// </summary>
        private static float ContactPulse(float time, float impactTime)
        {
            if (time <= impactTime)
            {
                float approach = Mathf.Clamp01(time /
                    Mathf.Max(impactTime, 0.001f));
                // Cubic acceleration: the limb hangs in the coil and then
                // covers most of its travel in the last few frames before
                // contact, instead of easing in and out at the same rate.
                return approach * approach * approach;
            }

            float settle = Mathf.Clamp01((time - impactTime) /
                Mathf.Max(1f - impactTime, 0.001f));
            // Follow-through: carry a little past the contact pose, then ease
            // back. Recovery uses the slower quintic curve on purpose so the
            // strike reads as a hard stop followed by a settle.
            float overshoot = Mathf.Sin(settle * Mathf.PI) *
                FollowThroughDepth;
            return (1f - SmootherStep01(settle)) + overshoot;
        }

        /// <summary>
        /// Pre-strike coil, kept separate from <see cref="ContactPulse"/>
        /// because several callers raise that value to a fractional power and
        /// a negative base would return NaN straight into a bone rotation.
        /// Returns 0 outside the anticipation window and peaks mid-coil.
        /// </summary>
        private static float AnticipationPulse(float time, float impactTime)
        {
            float approach = Mathf.Clamp01(time /
                Mathf.Max(impactTime, 0.001f));
            if (approach >= AnticipationWindow) return 0f;
            return Mathf.Sin(approach / AnticipationWindow * Mathf.PI);
        }

        // Fraction of the pre-contact window spent coiling away from the
        // strike, how far that coil travels in degrees, and how far the limb
        // carries past the contact pose before settling.
        private const float AnticipationWindow = 0.30f;
        private const float AnticipationDegrees = 7.5f;
        private const float FollowThroughDepth = 0.09f;

        private static float WindupPulse(float time, float impactTime)
        {
            float peak = impactTime * 0.48f;
            if (time >= impactTime) return 0f;
            if (time <= peak)
                return SmoothStep01(time / Mathf.Max(peak, 0.001f));
            return 1f - SmoothStep01((time - peak) /
                Mathf.Max(impactTime - peak, 0.001f));
        }

        private static float SmoothPulse(float time, float center,
            float width)
        {
            float value = 1f - Mathf.Abs(time - center) /
                Mathf.Max(width, 0.001f);
            return SmoothStep01(Mathf.Clamp01(value));
        }

        private static float SmoothStep01(float value)
        {
            value = Mathf.Clamp01(value);
            return value * value * (3f - 2f * value);
        }

        private static float SmootherStep01(float value)
        {
            value = Mathf.Clamp01(value);
            return value * value * value *
                (value * (value * 6f - 15f) + 10f);
        }

        private void Finish()
        {
            if (!_active) return;
            if (!_impactReleased && _playVfx && _definition != null &&
                _caster != null)
                MonkVfx.Play(_definition, _caster, _target);
            _impactReleased = true;
            if (SkillsPlugin.CfgEnableTestMode?.Value == true)
            {
                if (_motion == Motion.SkybreakerRush)
                    SkillsPlugin.Log?.LogInfo(
                        $"[Monk Motion] skybreaker root rush=" +
                        $"{_skybreakerRushDistance:0.00}/" +
                        $"{_skybreakerMaximumAdvance:0.00}m");
                else if (_motion == Motion.VaultingTempest)
                    SkillsPlugin.Log?.LogInfo(
                        $"[Monk Motion] vaulting crouch peak=" +
                        $"{_vaultCrouchPeak:0.00}");
            }
            _active = false;
            if (_attackVfx != null)
            {
                _attackVfx.Release();
                _attackVfx = null;
            }
            FinishBaseAnimation();
            _qaAwaitingHandoffSample =
                _humanoidReady && _qaPreviousPoseReady;
        }

        private void FinishBaseAnimation()
        {
            if (_castAttackStarted && _animator != null)
                _animator.SetTrigger("CastAtkEnd");
            _castAttackStarted = false;
            AnimatorOverrideController disposableController =
                _punchController;
            if (_animator != null && _originalController != null)
            {
                _animator.runtimeAnimatorController = _originalController;
                _animator.speed = _originalAnimatorSpeed;
            }
            _originalController = null;
            _punchController = null;
            if (disposableController != null)
                Destroy(disposableController);
        }

        private void OnDisable()
        {
            if (_attackVfx != null)
            {
                _attackVfx.Release();
                _attackVfx = null;
            }
            FinishBaseAnimation();
            _active = false;
        }

        private static bool IsKick(Motion motion) =>
            motion == Motion.RoundKick || motion == Motion.CrescentHeel ||
            motion == Motion.FlyingKick ||
            motion == Motion.VaultingTempest ||
            motion == Motion.SweepingCrescent ||
            motion == Motion.WhirlwindKick;

        private static Motion ResolveMotion(string key)
        {
            switch (key)
            {
                case "jab": return Motion.Jab;
                case "shoulder_check": return Motion.ShoulderCheck;
                case "tiger_claw": return Motion.TigerClaw;
                case "stone_throw": return Motion.StoneThrow;
                case "round_kick": return Motion.RoundKick;
                case "crescent_heel": return Motion.CrescentHeel;
                case "countering_palm": return Motion.CounteringPalm;
                case "eagle_strike": return Motion.EagleStrike;
                case "fist_of_falling_rain": return Motion.FallingRain;
                case "pummel": return Motion.Pummel;
                case "flying_kick": return Motion.FlyingKick;
                case "sweeping_crescent": return Motion.SweepingCrescent;
                case "pressure_point": return Motion.PressurePoint;
                case "whirlwind_kick": return Motion.WhirlwindKick;
                case "dragon_punch": return Motion.DragonPunch;
                case "quivering_palm": return Motion.QuiveringPalm;
                case "gale_splitting_palm": return Motion.GaleSplittingPalm;
                case "vaulting_tempest": return Motion.VaultingTempest;
                case "skybreaker_rush": return Motion.SkybreakerRush;
                default: return Motion.None;
            }
        }

        private static MotionTiming TimingFor(Motion motion)
        {
            switch (motion)
            {
                case Motion.Jab:
                    return new MotionTiming(0.54f, 0.20f,
                        "committed right straight punch");
                case Motion.ShoulderCheck:
                    return new MotionTiming(0.66f, 0.31f,
                        "full-body shoulder drive");
                case Motion.TigerClaw:
                    return new MotionTiming(0.68f, 0.29f,
                        "raking tiger-claw strike");
                case Motion.StoneThrow:
                    return new MotionTiming(1.18f, 0.78f,
                        "hip-charged two-palm energy projectile");
                case Motion.RoundKick:
                    return new MotionTiming(0.74f, 0.35f,
                        "turning right round kick");
                case Motion.CrescentHeel:
                    return new MotionTiming(0.80f, 0.39f,
                        "left crescent heel kick");
                case Motion.CounteringPalm:
                    return new MotionTiming(0.56f, 0.25f,
                        "retaliatory palm strike");
                case Motion.EagleStrike:
                    return new MotionTiming(0.76f, 0.30f,
                        "descending eagle-hand strike");
                case Motion.FallingRain:
                    return new MotionTiming(0.98f, 0.46f,
                        "three-punch combination");
                case Motion.Pummel:
                    return new MotionTiming(1.18f, 0.40f,
                        "six-strike alternating pummel");
                case Motion.FlyingKick:
                    return new MotionTiming(1.02f, 0.37f,
                        "airborne forward flying kick");
                case Motion.SweepingCrescent:
                    return new MotionTiming(0.86f, 0.41f,
                        "low spinning sweep kick");
                case Motion.PressurePoint:
                    return new MotionTiming(0.62f, 0.28f,
                        "precise point strike");
                case Motion.WhirlwindKick:
                    return new MotionTiming(1.18f, 0.54f,
                        "grounded 360-degree cyclone kick");
                case Motion.DragonPunch:
                    return new MotionTiming(0.98f, 0.48f,
                        "explosive rising dragon uppercut");
                case Motion.QuiveringPalm:
                    return new MotionTiming(1.14f, 0.53f,
                        "committed open-palm finishing strike");
                case Motion.GaleSplittingPalm:
                    return new MotionTiming(0.94f, 0.44f,
                        "crossed-arm release into a traveling crescent wave");
                case Motion.VaultingTempest:
                    return new MotionTiming(1.12f, 0.50f,
                        "low charge crouch into a full rising somersault flash kick");
                case Motion.SkybreakerRush:
                    return new MotionTiming(1.04f, 0.58f,
                        "collision-aware forward rush into a sliding point-blank detonation");
                case Motion.AutoLeft:
                    return new MotionTiming(0.44f, 0.13f,
                        "body-driven off-hand handwrap straight");
                case Motion.AutoRight:
                    return new MotionTiming(0.44f, 0.13f,
                        "body-driven right handwrap straight");
                default:
                    return new MotionTiming(0.50f, 0.20f, "native motion");
            }
        }
    }
}
