using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ErenshorSkills.Monk
{
    /// <summary>
    /// Closes the live humanoid finger chains into a readable fist. Imported
    /// Erenshor avatars do not all use the same local finger axis, so each hand
    /// evaluates the six signed local axes once and keeps whichever produces
    /// the smallest fingertip-to-palm aperture.
    /// </summary>
    internal sealed class MonkFistPoseRig
    {
        private sealed class HandRig
        {
            internal Transform Hand;
            internal readonly List<Transform[]> Fingers =
                new List<Transform[]>();
            internal Transform[] Thumb;
            internal Vector3 CurlAxis = Vector3.forward;
            internal bool Calibrated;
            internal float OpenAperture;
            internal float ClosedAperture;
        }

        private static readonly Vector3[] CandidateAxes =
        {
            Vector3.right, Vector3.left,
            Vector3.up, Vector3.down,
            Vector3.forward, Vector3.back
        };

        private readonly HandRig _left;
        private readonly HandRig _right;

        internal bool Ready =>
            _left?.Hand != null && _left.Fingers.Count == 4 &&
            _right?.Hand != null && _right.Fingers.Count == 4;

        internal float ClosureRatio
        {
            get
            {
                float open = (_left?.OpenAperture ?? 0f) +
                    (_right?.OpenAperture ?? 0f);
                float closed = (_left?.ClosedAperture ?? 0f) +
                    (_right?.ClosedAperture ?? 0f);
                return open > 0.001f
                    ? Mathf.Clamp01(1f - closed / open) : 0f;
            }
        }

        internal MonkFistPoseRig(Animator animator)
        {
            if (animator == null || !animator.isHuman) return;
            try
            {
                _left = BuildHand(animator, true);
                _right = BuildHand(animator, false);
            }
            catch (Exception exception)
            {
                SkillsPlugin.Log?.LogWarning(
                    "Monk fist pose could not cache humanoid fingers: " +
                    exception.Message);
            }
        }

        internal void Apply(float weight)
        {
            float value = Mathf.Clamp01(weight);
            if (value <= 0.001f || !Ready) return;
            ApplyHand(_left, value);
            ApplyHand(_right, value);
        }

        private static HandRig BuildHand(Animator animator, bool left)
        {
            var hand = new HandRig
            {
                Hand = animator.GetBoneTransform(left
                    ? HumanBodyBones.LeftHand
                    : HumanBodyBones.RightHand)
            };
            hand.Thumb = Chain(animator,
                left ? HumanBodyBones.LeftThumbProximal :
                    HumanBodyBones.RightThumbProximal,
                left ? HumanBodyBones.LeftThumbIntermediate :
                    HumanBodyBones.RightThumbIntermediate,
                left ? HumanBodyBones.LeftThumbDistal :
                    HumanBodyBones.RightThumbDistal);
            hand.Fingers.Add(Chain(animator,
                left ? HumanBodyBones.LeftIndexProximal :
                    HumanBodyBones.RightIndexProximal,
                left ? HumanBodyBones.LeftIndexIntermediate :
                    HumanBodyBones.RightIndexIntermediate,
                left ? HumanBodyBones.LeftIndexDistal :
                    HumanBodyBones.RightIndexDistal));
            hand.Fingers.Add(Chain(animator,
                left ? HumanBodyBones.LeftMiddleProximal :
                    HumanBodyBones.RightMiddleProximal,
                left ? HumanBodyBones.LeftMiddleIntermediate :
                    HumanBodyBones.RightMiddleIntermediate,
                left ? HumanBodyBones.LeftMiddleDistal :
                    HumanBodyBones.RightMiddleDistal));
            hand.Fingers.Add(Chain(animator,
                left ? HumanBodyBones.LeftRingProximal :
                    HumanBodyBones.RightRingProximal,
                left ? HumanBodyBones.LeftRingIntermediate :
                    HumanBodyBones.RightRingIntermediate,
                left ? HumanBodyBones.LeftRingDistal :
                    HumanBodyBones.RightRingDistal));
            hand.Fingers.Add(Chain(animator,
                left ? HumanBodyBones.LeftLittleProximal :
                    HumanBodyBones.RightLittleProximal,
                left ? HumanBodyBones.LeftLittleIntermediate :
                    HumanBodyBones.RightLittleIntermediate,
                left ? HumanBodyBones.LeftLittleDistal :
                    HumanBodyBones.RightLittleDistal));
            hand.Fingers.RemoveAll(chain =>
                chain == null || Array.Exists(chain, bone => bone == null));
            return hand;
        }

        private static Transform[] Chain(Animator animator,
            HumanBodyBones proximal, HumanBodyBones intermediate,
            HumanBodyBones distal) =>
            new[]
            {
                animator.GetBoneTransform(proximal),
                animator.GetBoneTransform(intermediate),
                animator.GetBoneTransform(distal)
            };

        private static void ApplyHand(HandRig hand, float weight)
        {
            if (!hand.Calibrated) Calibrate(hand);
            foreach (Transform[] chain in hand.Fingers)
                CurlChain(chain, hand.CurlAxis, weight);
            CurlThumb(hand.Thumb, hand.CurlAxis, weight);
            hand.ClosedAperture = MeasureAperture(hand);
        }

        private static void Calibrate(HandRig hand)
        {
            hand.Calibrated = true;
            hand.OpenAperture = MeasureAperture(hand);
            if (hand.Hand == null || hand.Fingers.Count == 0) return;

            var bones = hand.Fingers
                .SelectMany(chain => chain).ToArray();
            var original = bones.Select(bone => bone.localRotation).ToArray();
            float best = float.MaxValue;
            Vector3 bestAxis = Vector3.forward;
            foreach (Vector3 axis in CandidateAxes)
            {
                Restore(bones, original);
                foreach (Transform[] chain in hand.Fingers)
                    CurlChain(chain, axis, 1f);
                float aperture = MeasureAperture(hand);
                if (aperture < best)
                {
                    best = aperture;
                    bestAxis = axis;
                }
            }
            Restore(bones, original);
            hand.CurlAxis = bestAxis;
            hand.ClosedAperture = best;
            SkillsPlugin.Log?.LogInfo(
                $"Monk fist pose calibrated {hand.Hand.name}: axis=" +
                $"{bestAxis}, aperture={hand.OpenAperture:0.000}->" +
                $"{best:0.000}.");
        }

        private static void CurlChain(Transform[] chain, Vector3 axis,
            float weight)
        {
            if (chain == null || chain.Length < 3) return;
            RotateLocal(chain[0], axis, 58f * weight);
            RotateLocal(chain[1], axis, 78f * weight);
            RotateLocal(chain[2], axis, 48f * weight);
        }

        private static void CurlThumb(Transform[] chain, Vector3 axis,
            float weight)
        {
            if (chain == null || chain.Length < 3 ||
                Array.Exists(chain, bone => bone == null))
                return;
            RotateLocal(chain[0], axis, 28f * weight);
            RotateLocal(chain[1], axis, 44f * weight);
            RotateLocal(chain[2], axis, 26f * weight);
        }

        private static void RotateLocal(Transform bone, Vector3 axis,
            float degrees)
        {
            if (bone == null) return;
            bone.localRotation *= Quaternion.AngleAxis(degrees, axis);
        }

        private static float MeasureAperture(HandRig hand)
        {
            if (hand?.Hand == null || hand.Fingers.Count == 0) return 0f;
            float total = 0f;
            int count = 0;
            foreach (Transform[] chain in hand.Fingers)
            {
                Transform distal = chain?[2];
                if (distal == null) continue;
                total += Vector3.Distance(hand.Hand.position,
                    distal.position);
                count++;
            }
            return count > 0 ? total / count : 0f;
        }

        private static void Restore(Transform[] bones,
            Quaternion[] rotations)
        {
            for (int index = 0; index < bones.Length; index++)
                if (bones[index] != null)
                    bones[index].localRotation = rotations[index];
        }
    }
}
