using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ErenshorSkills
{
    /// <summary>
    /// Looks up Erenshor's own spell AudioClips by name.
    ///
    /// The add-on previously silenced the native ChargeSound/CompleteSound
    /// channel and played its own AudioSource instead. That bypassed the game's
    /// entire audio path — its volume curve, its SpellVol handling, and the
    /// character AudioSource the rest of the game mixes against — which is why
    /// add-on abilities sounded thin next to native ones no matter how the
    /// samples were mastered. Borrowing the game's clips and letting the native
    /// pipeline play them means an add-on spell is mixed exactly like a stock
    /// spell, for free.
    ///
    /// Clips are harvested from the loaded spell database rather than a
    /// Resources path, so only assets the game actually ships can be resolved.
    /// </summary>
    internal static class NativeAudioLibrary
    {
        private static Dictionary<string, AudioClip> _byName;

        internal static int ClipCount => _byName?.Count ?? 0;

        private static void Build()
        {
            if (_byName != null) return;
            _byName = new Dictionary<string, AudioClip>(
                StringComparer.OrdinalIgnoreCase);
            Spell[] database = GameData.SpellDatabase?.SpellDatabase;
            if (database == null) return;
            foreach (Spell spell in database)
            {
                if (spell == null) continue;
                Add(spell.ChargeSound);
                Add(spell.CompleteSound);
                if (spell.ChargeVariations != null)
                    foreach (AudioClip clip in spell.ChargeVariations)
                        Add(clip);
                if (spell.CompleteVariations != null)
                    foreach (AudioClip clip in spell.CompleteVariations)
                        Add(clip);
            }
        }

        private static void Add(AudioClip clip)
        {
            if (clip == null || string.IsNullOrEmpty(clip.name)) return;
            if (!_byName.ContainsKey(clip.name)) _byName[clip.name] = clip;
        }

        /// <summary>
        /// Resolves the first name that exists, so a mapping can list a
        /// preferred clip and a fallback and still work if a game update
        /// renames or drops one.
        /// </summary>
        internal static AudioClip Resolve(params string[] names)
        {
            Build();
            if (_byName.Count == 0 || names == null) return null;
            foreach (string name in names)
            {
                if (string.IsNullOrEmpty(name)) continue;
                if (_byName.TryGetValue(name, out AudioClip exact))
                    return exact;
                // Names in the shipped database may or may not carry the
                // original file extension.
                string trimmed = name.EndsWith(".ogg",
                    StringComparison.OrdinalIgnoreCase)
                    ? name.Substring(0, name.Length - 4) : name + ".ogg";
                if (_byName.TryGetValue(trimmed, out AudioClip variant))
                    return variant;
            }
            return null;
        }

        /// <summary>
        /// Erenshor's generic melee hit clips. For a martial class these are
        /// the correct source: they are what the player already hears on every
        /// swing, so a technique landing on one reads as part of the game's
        /// combat rather than as an add-on layered over it.
        /// </summary>
        internal static AudioClip HitSound(int index)
        {
            List<AudioClip> hits = GameData.Misc?.HitSoundsGeneric;
            if (hits == null || hits.Count == 0) return null;
            return hits[Mathf.Abs(index) % hits.Count];
        }

        internal static int HitSoundCount =>
            GameData.Misc?.HitSoundsGeneric?.Count ?? 0;

        /// <summary>Reset for a scene or database reload.</summary>
        internal static void Invalidate() => _byName = null;

        internal static IEnumerable<string> KnownNames =>
            _byName?.Keys.OrderBy(value => value) ?? Enumerable.Empty<string>();
    }
}
