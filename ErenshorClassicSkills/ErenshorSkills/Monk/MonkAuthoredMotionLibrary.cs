using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace ErenshorSkills.Monk
{
    internal enum MonkMotionJoint
    {
        Hips,
        Spine,
        Chest,
        Neck,
        Head,
        LeftShoulder,
        LeftUpperArm,
        LeftLowerArm,
        LeftHand,
        RightShoulder,
        RightUpperArm,
        RightLowerArm,
        RightHand,
        LeftUpperLeg,
        LeftLowerLeg,
        LeftFoot,
        LeftToes,
        RightUpperLeg,
        RightLowerLeg,
        RightFoot,
        RightToes,
        Count
    }

    /// <summary>
    /// One source-independent, normalized humanoid motion. Frames contain joint
    /// positions relative to the hips, so the runtime can pose any Erenshor
    /// humanoid without importing or redistributing the original FBX rig.
    /// </summary>
    internal sealed class MonkAuthoredMotionClip
    {
        private static readonly MonkMotionJoint[] MirrorSourceJoint =
        {
            MonkMotionJoint.Hips,
            MonkMotionJoint.Spine,
            MonkMotionJoint.Chest,
            MonkMotionJoint.Neck,
            MonkMotionJoint.Head,
            MonkMotionJoint.RightShoulder,
            MonkMotionJoint.RightUpperArm,
            MonkMotionJoint.RightLowerArm,
            MonkMotionJoint.RightHand,
            MonkMotionJoint.LeftShoulder,
            MonkMotionJoint.LeftUpperArm,
            MonkMotionJoint.LeftLowerArm,
            MonkMotionJoint.LeftHand,
            MonkMotionJoint.RightUpperLeg,
            MonkMotionJoint.RightLowerLeg,
            MonkMotionJoint.RightFoot,
            MonkMotionJoint.RightToes,
            MonkMotionJoint.LeftUpperLeg,
            MonkMotionJoint.LeftLowerLeg,
            MonkMotionJoint.LeftFoot,
            MonkMotionJoint.LeftToes
        };

        internal string Name { get; }
        internal float SourceContact { get; }
        internal int FrameCount => _hips.Length;

        private readonly Vector3[] _hips;
        private readonly Vector3[][] _poses;

        internal MonkAuthoredMotionClip(string name, float sourceContact,
            Vector3[] hips, Vector3[][] poses)
        {
            Name = name;
            SourceContact = Mathf.Clamp01(sourceContact);
            _hips = hips;
            _poses = poses;
        }

        internal void Sample(float normalizedTime, float desiredContact,
            bool mirror, Vector3[] pose, out Vector3 hips)
        {
            float sourceTime = WarpContact(Mathf.Clamp01(normalizedTime),
                Mathf.Clamp(desiredContact, 0.05f, 0.95f));
            float exactFrame = sourceTime * (_hips.Length - 1);
            int first = Mathf.Clamp(Mathf.FloorToInt(exactFrame), 0,
                _hips.Length - 1);
            int second = Mathf.Min(first + 1, _hips.Length - 1);
            float amount = exactFrame - first;
            hips = Vector3.LerpUnclamped(_hips[first], _hips[second], amount);
            if (mirror) hips.x = -hips.x;
            int jointCount = (int)MonkMotionJoint.Count;
            for (int index = 0; index < jointCount; index++)
            {
                int sourceIndex = mirror
                    ? (int)MirrorSourceJoint[index]
                    : index;
                Vector3 value = Vector3.LerpUnclamped(
                    _poses[first][sourceIndex],
                    _poses[second][sourceIndex], amount);
                if (mirror) value.x = -value.x;
                pose[index] = value;
            }
        }

        private float WarpContact(float time, float desiredContact)
        {
            if (time <= desiredContact)
            {
                return SourceContact * time /
                    Mathf.Max(desiredContact, 0.001f);
            }
            return SourceContact + (1f - SourceContact) *
                (time - desiredContact) /
                Mathf.Max(1f - desiredContact, 0.001f);
        }
    }

    internal static class MonkAuthoredMotionLibrary
    {
        private const string ResourceFilename = "MonkAuthoredMotions.mkm";
        private const ushort FormatVersion = 2;
        private const int MaximumClips = 64;
        private const int MaximumFrames = 240;

        private static readonly string[] ExpectedJoints =
        {
            "hips", "spine", "chest", "neck", "head",
            "left_shoulder", "left_upper_arm", "left_lower_arm", "left_hand",
            "right_shoulder", "right_upper_arm", "right_lower_arm", "right_hand",
            "left_upper_leg", "left_lower_leg", "left_foot", "left_toes",
            "right_upper_leg", "right_lower_leg", "right_foot", "right_toes"
        };

        private static Dictionary<string, MonkAuthoredMotionClip> _clips;
        private static bool _loadAttempted;

        internal static bool TryGet(string name,
            out MonkAuthoredMotionClip clip)
        {
            EnsureLoaded();
            if (_clips != null && !string.IsNullOrEmpty(name))
                return _clips.TryGetValue(name, out clip);
            clip = null;
            return false;
        }

        private static void EnsureLoaded()
        {
            if (_loadAttempted) return;
            _loadAttempted = true;
            try
            {
                byte[] bytes = EmbeddedResources.GetBytesByFilename(
                    ResourceFilename);
                if (bytes == null || bytes.Length < 16)
                    throw new InvalidDataException(
                        "embedded motion resource is missing or truncated");
                using (var stream = new MemoryStream(bytes, false))
                using (var reader = new BinaryReader(stream, Encoding.UTF8,
                    false))
                {
                    string magic = Encoding.ASCII.GetString(
                        reader.ReadBytes(4));
                    if (!string.Equals(magic, "MKM2",
                        StringComparison.Ordinal))
                        throw new InvalidDataException(
                            "unexpected motion-library signature");
                    ushort version = reader.ReadUInt16();
                    if (version != FormatVersion)
                        throw new InvalidDataException(
                            "unsupported motion-library version " + version);
                    ushort jointCount = reader.ReadUInt16();
                    if (jointCount != ExpectedJoints.Length)
                        throw new InvalidDataException(
                            "unexpected humanoid joint count " + jointCount);
                    for (int index = 0; index < jointCount; index++)
                    {
                        string joint = reader.ReadString();
                        if (!string.Equals(joint, ExpectedJoints[index],
                            StringComparison.Ordinal))
                            throw new InvalidDataException(
                                "joint map mismatch at index " + index);
                    }
                    ushort clipCount = reader.ReadUInt16();
                    if (clipCount == 0 || clipCount > MaximumClips)
                        throw new InvalidDataException(
                            "invalid authored clip count " + clipCount);
                    var clips = new Dictionary<string,
                        MonkAuthoredMotionClip>(StringComparer.Ordinal);
                    for (int clipIndex = 0; clipIndex < clipCount;
                        clipIndex++)
                    {
                        string name = reader.ReadString();
                        ushort frameCount = reader.ReadUInt16();
                        float contact = reader.ReadSingle();
                        if (string.IsNullOrWhiteSpace(name) || frameCount < 2 ||
                            frameCount > MaximumFrames ||
                            float.IsNaN(contact) || float.IsInfinity(contact))
                            throw new InvalidDataException(
                                "invalid authored clip header at index " +
                                clipIndex);
                        var hips = new Vector3[frameCount];
                        var poses = new Vector3[frameCount][];
                        for (int frame = 0; frame < frameCount; frame++)
                        {
                            hips[frame] = ReadVector(reader);
                            poses[frame] = new Vector3[jointCount];
                            for (int joint = 0; joint < jointCount; joint++)
                                poses[frame][joint] = ReadVector(reader);
                        }
                        clips.Add(name, new MonkAuthoredMotionClip(name,
                            contact, hips, poses));
                    }
                    if (stream.Position != stream.Length)
                        throw new InvalidDataException(
                            "unexpected trailing motion-library data");
                    _clips = clips;
                }
                SkillsPlugin.Log?.LogInfo(
                    $"Monk animation: loaded {_clips.Count} retargetable authored martial clips.");
            }
            catch (Exception exception)
            {
                _clips = null;
                SkillsPlugin.Log?.LogWarning(
                    "Monk animation: authored motion library unavailable; " +
                    "native fallback remains active. " + exception.Message);
            }
        }

        private static Vector3 ReadVector(BinaryReader reader)
        {
            float x = reader.ReadSingle();
            float y = reader.ReadSingle();
            float z = reader.ReadSingle();
            if (float.IsNaN(x) || float.IsNaN(y) || float.IsNaN(z) ||
                float.IsInfinity(x) || float.IsInfinity(y) ||
                float.IsInfinity(z))
                throw new InvalidDataException(
                    "non-finite authored motion sample");
            return new Vector3(x, y, z);
        }
    }
}
