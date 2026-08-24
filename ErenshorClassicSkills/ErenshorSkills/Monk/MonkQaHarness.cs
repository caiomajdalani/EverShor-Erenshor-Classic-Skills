using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.AI;

namespace ErenshorSkills.Monk
{
    /// <summary>
    /// Opt-in, John EverQuest-only validation for newly added Monk content.
    /// It deliberately does not rerun the legacy crafted-gear or Necromancer
    /// sweeps when the Monk catalog changes.
    /// </summary>
    internal static class MonkQaHarness
    {
        private static bool _previewRunning;
        private static bool _animationRunning;
        private static bool _captureRunning;
        private static bool _mechanicsRunning;
        private static bool _autoattackRunning;

        internal static void Handle(string arguments)
        {
            string remainder = (arguments ?? "").Trim();
            string verb = TakeToken(ref remainder).ToLowerInvariant();
            MonkCatalog.TryInitialize();
            switch (verb)
            {
                case "":
                case "help": ShowHelp(); return;
                case "status": ShowStatus(); return;
                case "class": SetQaClass(); return;
                case "level": SetLevel(remainder); return;
                case "learn": LearnAll(); return;
                case "skills": ShowSkills(); return;
                case "starter": ValidateStarterKit(); return;
                case "icons": ValidateIcons(); return;
                case "vfx": StartVfx(remainder); return;
                case "animations":
                case "animation": StartAnimations(remainder); return;
                case "showcase": StartShowcase(); return;
                case "capture": StartCapture(remainder); return;
                case "motion": StartMotionCapture(remainder); return;
                case "motions": StartMotionSweep(remainder); return;
                case "autoattack":
                case "autoattacks": StartAutoattackVisual(remainder); return;
                case "inventoryclicks": TestInventoryClickRecovery(); return;
                case "ready":
                case "readyidle": StartReadyIdleTest(); return;
                case "use": UseTechnique(remainder); return;
                case "mana": TestManaContract(); return;
                case "mechanics": StartMechanics(remainder); return;
                case "dps":
                case "benchmark": StartDps(remainder); return;
                case "gear": TestGear(remainder); return;
                case "wraps": StartHandwrapCapture(); return;
                case "aura":
                case "auras": TestAuras(remainder); return;
                case "ascension":
                case "ascensions": TestAscensions(); return;
                case "vendor": TestVendor(remainder); return;
                case "smoke": StartSmoke(); return;
                default:
                    ChatHelper.Send(
                        $"<color=#EF5350>[Monk QA]</color> Unknown command " +
                        $"'{verb}'. Use /cstest monk help.");
                    return;
            }
        }

        private static void ShowHelp()
        {
            ChatHelper.Send("<color=#E7C76F>Monk QA — John EverQuest only</color>");
            ChatHelper.Send("/cstest monk status|class|level 1-35|learn|skills|starter|icons");
            ChatHelper.Send("/cstest monk vfx [NAME|all] — preview each bespoke effect without damage");
            ChatHelper.Send("/cstest monk animation [NAME|all] — preview one technique or the full animation/VFX sweep");
            ChatHelper.Send("/cstest monk showcase — perform all Monk abilities in a presentation-ready sequence");
            ChatHelper.Send("/cstest monk capture &lt;NAME|strikes|all&gt; — save wind-up, attack-direction, and impact evidence");
            ChatHelper.Send("/cstest monk motion NAME — save a nine-frame body-animation storyboard without obscuring VFX");
            ChatHelper.Send("/cstest monk motions [side] — capture wind-up/contact/recovery frames for every offensive technique");
            ChatHelper.Send("/cstest monk autoattack [side] — exercise the real native main/off-hand triple-attack path and capture it");
            ChatHelper.Send("/cstest monk inventoryclicks — reproduce unequipped/null-slot inventory clicks without leaking errors or state");
            ChatHelper.Send("/cstest monk ready — capture fist guard, strike handoff, and recovery");
            ChatHelper.Send("/cstest monk use NAME — exercise one real technique on the current target");
            ChatHelper.Send("/cstest monk mana — verify insufficient-mana rejection and exact successful deduction");
            ChatHelper.Send("/cstest monk mechanics [NAME|all] — exercise one or all techniques against an isolated native test dummy");
            ChatHelper.Send("/cstest monk dps &lt;quick|timescale|audit|retune&gt; — compare Monk across budget, progression, and optimized gear");
            ChatHelper.Send("/cstest monk gear [verify] — all twenty-seven Monk gear items, including eight trainer, eight boss-only, and six tailored handwraps");
            ChatHelper.Send("/cstest monk wraps — audit all six skinned families, materials, guard/contact/recovery deformation, and both hands");
            ChatHelper.Send("/cstest monk aura [verify]|ascension|vendor [open|capture]|smoke");
        }

        private static void ShowStatus()
        {
            if (!RequireWorld(false)) return;
            bool catalog = MonkCatalog.ValidateCatalog(
                out string catalogDetail);
            bool balance = MonkCatalog.ValidateBalance(
                out string balanceDetail);
            bool vfx = MonkVfx.Validate(out string vfxDetail);
            bool vendor = MonkVendors.ValidateCurrentSceneVendor(
                out string vendorDetail);
            bool pass = catalog && balance && vfx && vendor;
            SendResult("status", pass,
                $"catalog={(catalog ? "PASS" : "FAIL")}: {catalogDetail}; " +
                $"balance={(balance ? "PASS" : "FAIL")}: " +
                $"{balanceDetail}; VFX={(vfx ? "PASS" : "FAIL")}: " +
                $"{vfxDetail}; vendor={(vendor ? "PASS" : "FAIL")}: " +
                vendorDetail);
        }

        private static void SetQaClass()
        {
            if (!RequireQaCharacter()) return;
            Stats stats = GameData.PlayerStats;
            stats.CharacterClass = MonkCatalog.MonkClass;
            GameData.CurrentCharacterSlot.CharClass = MonkCatalog.ClassName;
            try { stats.CalcStats(); } catch { }
            GameData.GM?.SaveGameData(wScene: false);
            bool pass = MonkCatalog.IsMonkPlayer();
            SendResult("class selection/save", pass,
                pass ? "John EverQuest is now a Monk through the native character save field"
                    : "the runtime or save class did not become Monk");
        }

        private static void SetLevel(string text)
        {
            if (!RequireQaMonk()) return;
            if (!int.TryParse((text ?? "").Trim(), out int level) ||
                level < 1 || level > 35)
            {
                ChatHelper.Send(
                    "<color=#EF5350>[Monk QA]</color> Level must be 1-35.");
                return;
            }
            GameData.PlayerStats.Level = level;
            GameData.CurrentCharacterSlot.CharLevel = level;
            try { GameData.PlayerStats.CalcStats(); } catch { }
            GameData.PlayerStats.CurrentHP =
                GameData.PlayerStats.CurrentMaxHP;
            GameData.GM?.SaveGameData(wScene: false);
            SendResult("level/save", GameData.PlayerStats.Level == level,
                $"John EverQuest is Monk level {level}");
        }

        private static void LearnAll()
        {
            if (!RequireQaMonk()) return;
            List<Skill> knownSkills = GameData.PlayerControl?.Myself?.MySkills?
                .KnownSkills;
            List<Spell> knownSpells = GameData.PlayerControl?.Myself?.MySpells?
                .KnownSpells;
            if (knownSkills == null || knownSpells == null)
            {
                SendResult("learn", false,
                    "native KnownSkills or KnownSpells is unavailable");
                return;
            }
            var expectedSkills = new List<Skill>(
                MonkSharedSkills.All.Select(value =>
                GameData.SkillDatabase?.GetSkillByID(value.SkillId))
                .Where(value => value != null));
            expectedSkills.AddRange(MonkCatalog.AllSkills.Where(skill =>
                MonkCatalog.TryGetDefinition(skill,
                    out MonkTechniqueDefinition definition) &&
                definition.Kind == MonkTechniqueKind.Passive));
            var expectedSpells = new List<Spell>(MonkCatalog.AllSpells);
            int added = 0;
            foreach (Skill skill in expectedSkills)
                if (!knownSkills.Contains(skill))
                {
                    knownSkills.Add(skill);
                    added++;
                }
            foreach (Spell spell in expectedSpells)
                if (!knownSpells.Contains(spell))
                {
                    knownSpells.Add(spell);
                    added++;
                }
            GameData.GM?.SaveGameData(wScene: false);
            bool pass = expectedSkills.Count +
                    expectedSpells.Count ==
                MonkSkillData.ExpectedTotalCount &&
                expectedSkills.All(knownSkills.Contains) &&
                expectedSpells.All(knownSpells.Contains);
            SendResult("learn/save", pass,
                $"added {added}; known Monk kit=" +
                $"{expectedSpells.Count} spellbook techniques + " +
                $"{expectedSkills.Count} shared/passive skills = " +
                MonkSkillData.ExpectedTotalCount);
        }

        private static void ShowSkills()
        {
            if (!RequireWorld(false)) return;
            string values = string.Join(", ", MonkSkillData.All.Select(value =>
                $"L{value.Level} {value.Name}"));
            ChatHelper.Send(
                $"<color=#E7C76F>[Monk QA]</color> " +
                $"{MonkSkillData.All.Count} unique techniques + " +
                $"{MonkSharedSkills.ExpectedCount} shared martial skills = " +
                $"{MonkSkillData.ExpectedTotalCount}.");
            ChatHelper.Send(values);
        }

        private static void StartDps(string text)
        {
            if (!RequireQaMonk()) return;
            if (_previewRunning || _animationRunning || _captureRunning ||
                _mechanicsRunning || _autoattackRunning ||
                Testing.Level35DpsBenchmark.IsRunning)
            {
                ChatHelper.Send(
                    "<color=#FFD54F>[Monk QA]</color> Another Monk visual, mechanics, or DPS run is already active.");
                return;
            }
            if (GameData.InCombat || GameData.Autoattacking)
            {
                ChatHelper.Send(
                    "<color=#EF5350>[Monk QA]</color> Leave combat and stop autoattack before starting the DPS audit.");
                return;
            }
            if (GameData.PlayerControl?.Myself?.MyCharmedNPC != null)
            {
                ChatHelper.Send(
                    "<color=#EF5350>[Monk QA]</color> Dismiss or reclaim the active pet before starting the controlled DPS audit.");
                return;
            }

            string requested = (text ?? "").Trim().ToLowerInvariant();
            string mode;
            switch (requested)
            {
                case "":
                case "quick":
                    mode = "monk-quick";
                    break;
                case "timescale":
                case "calibration":
                    mode = "monk-timescale";
                    break;
                case "audit":
                case "full":
                    mode = "monk-audit";
                    break;
                case "retune":
                case "verify":
                    mode = "monk-retune";
                    break;
                default:
                    ChatHelper.Send(
                        "<color=#EF5350>[Monk QA]</color> DPS mode must be quick, timescale, audit, or retune.");
                    return;
            }
            SkillsPlugin.Instance.StartCoroutine(
                Testing.Level35DpsBenchmark.Run(mode));
        }

        private static void ValidateStarterKit()
        {
            if (!RequireWorld(false)) return;
            bool pass = MonkCharacterCreation.ValidateStartingSkillContract(
                out string detail);
            SendResult("starter kit", pass, detail);
        }

        private static void ValidateIcons()
        {
            if (!RequireWorld(false)) return;
            var sprites = new List<Sprite>();
            sprites.AddRange(MonkCatalog.AllSkills.Select(value =>
                value?.SkillIcon));
            sprites.AddRange(MonkCatalog.AllTomes.Select(value =>
                value?.ItemIcon));
            sprites.AddRange(MonkAuras.AllSpells.Select(value =>
                value?.SpellIcon));
            sprites.AddRange(MonkAuras.AllItems.Select(value =>
                value?.ItemIcon));
            sprites.AddRange(MonkGear.AllItems.Select(value =>
                value?.ItemIcon));
            sprites.Add(MonkCatalog.ClassIcon);
            int expected = MonkSkillData.ExpectedCustomCount * 2 +
                MonkAuras.ExpectedCount * 2 + MonkGear.ExpectedCount + 1;
            int nonNull = sprites.Count(value => value != null);
            int unique = sprites.Where(value => value != null)
                .Select(value => value.GetInstanceID()).Distinct().Count();
            bool pass = sprites.Count == expected && nonNull == expected &&
                unique == expected;
            SendResult("icons", pass,
                $"{unique}/{expected} runtime sprites are present and unique " +
                "across techniques, tomes, auras, new gear, and class crest");
        }

        private static void StartVfx(string text)
        {
            if (!RequireQaMonk()) return;
            if (_previewRunning || _animationRunning || _captureRunning ||
                _mechanicsRunning || _autoattackRunning)
            {
                ChatHelper.Send(
                    "<color=#FFD54F>[Monk QA]</color> A Monk visual preview is already active.");
                return;
            }
            string query = (text ?? "").Trim();
            MonkTechniqueDefinition one = string.IsNullOrWhiteSpace(query) ||
                string.Equals(query, "all", StringComparison.OrdinalIgnoreCase)
                ? null : ResolveDefinition(query);
            if (!string.IsNullOrWhiteSpace(query) &&
                !string.Equals(query, "all", StringComparison.OrdinalIgnoreCase) &&
                one == null) return;
            SkillsPlugin.Instance.StartCoroutine(PreviewVfx(one));
        }

        private static IEnumerator PreviewVfx(MonkTechniqueDefinition one)
        {
            _previewRunning = true;
            GameObject target = null;
            int invoked = 0;
            MonkTechniqueDefinition[] definitions = one != null
                ? new[] { one } : MonkSkillData.All.ToArray();
            int expectedImpacts = definitions.Count(value =>
                value.IsOffensive);
            int expectedUtilities = definitions.Length - expectedImpacts;
            MonkAudio.GetPlaybackCounts(out _, out int impactsBefore,
                out int utilitiesBefore, out int sourcesBefore);
            int amplifiedBefore = MonkVfx.AmplifiedImpactCount;
            try
            {
                Transform player = GameData.PlayerControl?.transform;
                if (player == null) yield break;
                target = new GameObject("CS Monk VFX Preview Target");
                target.transform.position = player.position +
                    player.forward * 3.1f;
                foreach (MonkTechniqueDefinition definition in definitions)
                {
                    ChatHelper.SendCombat(
                        "Monk VFX preview: " + definition.Name, "#E7C76F");
                    MonkVfx.Play(definition, player,
                        definition.IsOffensive ? target.transform : null);
                    invoked++;
                    yield return new WaitForSeconds(one != null ? 1.8f : 1.25f);
                }
            }
            finally
            {
                if (target != null) UnityEngine.Object.Destroy(target);
                _previewRunning = false;
                MonkAudio.GetPlaybackCounts(out _, out int impactsAfter,
                    out int utilitiesAfter, out int sourcesAfter);
                int impacts = impactsAfter - impactsBefore;
                int utilities = utilitiesAfter - utilitiesBefore;
                int amplified =
                    MonkVfx.AmplifiedImpactCount - amplifiedBefore;
                int sources = sourcesAfter - sourcesBefore;
                bool pass = invoked == definitions.Length &&
                    impacts == expectedImpacts &&
                    utilities == expectedUtilities &&
                    amplified == expectedImpacts;
                SendResult(one == null ? "VFX/audio sweep" :
                    "VFX/audio preview", pass,
                    $"{invoked}/{definitions.Length} bespoke effects; " +
                    $"amplified impacts={amplified}/{expectedImpacts}, " +
                    $"impact sounds={impacts}/{expectedImpacts}, " +
                    $"focus sounds={utilities}/{expectedUtilities}, " +
                    $"audible transient sources={sources}");
            }
        }

        private static void StartAnimations(string text)
        {
            if (!RequireQaMonk()) return;
            if (_previewRunning || _animationRunning || _captureRunning ||
                _mechanicsRunning || _autoattackRunning)
            {
                ChatHelper.Send(
                    "<color=#FFD54F>[Monk QA]</color> A Monk visual preview is already active.");
                return;
            }
            string query = (text ?? "").Trim();
            MonkTechniqueDefinition one = string.IsNullOrWhiteSpace(query) ||
                string.Equals(query, "all", StringComparison.OrdinalIgnoreCase)
                ? null : ResolveDefinition(query);
            if (!string.IsNullOrWhiteSpace(query) &&
                !string.Equals(query, "all", StringComparison.OrdinalIgnoreCase) &&
                one == null) return;
            SkillsPlugin.Instance.StartCoroutine(PreviewAnimations(one));
        }

        private static IEnumerator PreviewAnimations(
            MonkTechniqueDefinition one)
        {
            _animationRunning = true;
            GameObject target = null;
            int previewed = 0;
            MonkAudio.GetPlaybackCounts(out int windupsBefore,
                out int impactsBefore, out _, out int sourcesBefore);
            int impactsVfxBefore = MonkVfx.AmplifiedImpactCount;
            MonkTechniqueDefinition[] definitions = one != null
                ? new[] { one }
                : MonkSkillData.All.Where(value => value.IsOffensive).ToArray();
            int expected = definitions.Count(value => value.IsOffensive);
            Transform player = GameData.PlayerControl?.transform;
            Vector3 originalPlayerPosition = player?.position ?? Vector3.zero;
            try
            {
                if (player == null ||
                    !MonkAnimationDriver.SupportsBodyMotion(player))
                {
                    SendResult("animation sweep", false,
                        "John EverQuest's humanoid hands, feet, hips, and torso were not available");
                    yield break;
                }
                target = new GameObject("CS Monk Animation Preview Target");
                target.transform.position = player.position +
                    player.forward * 3.1f;
                foreach (MonkTechniqueDefinition definition in definitions
                    .Where(value => value.IsOffensive))
                {
                    ChatHelper.SendCombat(
                        "Monk animation preview: " + definition.Name,
                        "#E7C76F");
                    if (MonkAnimationDriver.Play(definition, player,
                        target.transform)) previewed++;
                    yield return new WaitForSeconds(
                        MonkAnimationDriver.GetDuration(definition) + 0.50f);
                }
            }
            finally
            {
                if (target != null) UnityEngine.Object.Destroy(target);
                RestoreQaPlayerPosition(player, originalPlayerPosition);
                _animationRunning = false;
                MonkAudio.GetPlaybackCounts(out int windupsAfter,
                    out int impactsAfter, out _, out int sourcesAfter);
                int windups = windupsAfter - windupsBefore;
                int impacts = impactsAfter - impactsBefore;
                int sources = sourcesAfter - sourcesBefore;
                int amplified =
                    MonkVfx.AmplifiedImpactCount - impactsVfxBefore;
                bool presentation = windups == expected &&
                    impacts == expected && amplified == expected;
                SendResult(one == null ? "animation sweep" :
                    "animation preview",
                    previewed == expected && presentation,
                    $"{previewed}/{expected} offensive techniques invoked " +
                    "full-body humanoid motion; contact presentation=" +
                    $"{amplified}/{expected} amplified VFX, " +
                    $"{windups}/{expected} wind-up sounds, " +
                    $"{impacts}/{expected} impact sounds, " +
                    $"{sources} audible transient sources");
            }
        }

        private static void StartShowcase()
        {
            if (!RequireQaMonk()) return;
            if (_previewRunning || _animationRunning || _captureRunning ||
                _mechanicsRunning || _autoattackRunning)
            {
                ChatHelper.Send(
                    "<color=#FFD54F>[Monk QA]</color> A Monk visual preview is already active.");
                return;
            }
            SkillsPlugin.Instance.StartCoroutine(PreviewShowcase());
        }

        private static IEnumerator PreviewShowcase()
        {
            _animationRunning = true;
            GameObject target = null;
            GameObject overlayObject = null;
            int shown = 0;
            MonkTechniqueDefinition[] definitions =
                MonkSkillData.All.OrderBy(value => value.Level).ToArray();
            Transform player = GameData.PlayerControl?.transform;
            Vector3 originalPlayerPosition = player?.position ?? Vector3.zero;
            try
            {
                if (player == null ||
                    !MonkAnimationDriver.SupportsBodyMotion(player))
                {
                    SendResult("showcase", false,
                        "John EverQuest's humanoid rig is unavailable");
                    yield break;
                }

                Animator animator = player.GetComponent<Animator>();
                GameObject dummyPrefab =
                    Resources.Load<GameObject>("NPCs/TestDummy");
                Vector3 captureDirection = (player.forward +
                    player.right * 0.55f).normalized;
                Vector3 capturePosition = player.position +
                    captureDirection * 2.65f;
                target = dummyPrefab != null
                    ? UnityEngine.Object.Instantiate(dummyPrefab,
                        capturePosition,
                        Quaternion.LookRotation(-captureDirection, player.up))
                    : new GameObject("CS Monk Showcase Target");
                target.name = "CS Monk Showcase Target";
                target.transform.position = capturePosition;
                foreach (MonoBehaviour behaviour in
                    target.GetComponentsInChildren<MonoBehaviour>(true))
                    behaviour.enabled = false;
                NavMeshAgent navigation = target.GetComponent<NavMeshAgent>();
                if (navigation != null && navigation.enabled)
                    navigation.enabled = false;

                overlayObject = new GameObject("CS Monk Showcase Overlay");
                MonkShowcaseOverlay overlay =
                    overlayObject.AddComponent<MonkShowcaseOverlay>();
                overlay.Set("MONK — COMPLETE ARSENAL",
                    $"{definitions.Length} ABILITIES • LIVE IN-GAME ACTION");
                yield return new WaitForSeconds(2.50f);

                foreach (MonkTechniqueDefinition definition in definitions)
                {
                    string category = definition.IsOffensive
                        ? "MARTIAL TECHNIQUE"
                        : definition.Kind.ToString().ToUpperInvariant();
                    overlay.Set(definition.Name.ToUpperInvariant(),
                        $"LEVEL {definition.Level:00} • {category}");
                    ChatHelper.SendCombat(
                        "Monk showcase: " + definition.Name, "#E7C76F");
                    if (definition.IsOffensive)
                    {
                        MonkAnimationDriver.Play(definition, player,
                            target.transform);
                    }
                    else
                    {
                        if (animator != null && !string.IsNullOrEmpty(
                            definition.Animation))
                            animator.SetTrigger(definition.Animation);
                        MonkVfx.Play(definition, player);
                    }
                    shown++;
                    yield return new WaitForSeconds(2.15f);
                }

                overlay.Set("ARSENAL COMPLETE",
                    "ERENSHOR CLASSIC SKILLS • MONK");
                yield return new WaitForSeconds(2.50f);
            }
            finally
            {
                if (target != null) UnityEngine.Object.Destroy(target);
                if (overlayObject != null)
                    UnityEngine.Object.Destroy(overlayObject);
                RestoreQaPlayerPosition(player, originalPlayerPosition);
                _animationRunning = false;
                SendResult("showcase", shown == definitions.Length,
                    $"presented {shown}/{definitions.Length} Monk abilities");
            }
        }

        private static void StartCapture(string text)
        {
            if (!RequireQaMonk()) return;
            if (_previewRunning || _animationRunning || _captureRunning ||
                _mechanicsRunning || _autoattackRunning)
            {
                ChatHelper.Send(
                    "<color=#FFD54F>[Monk QA]</color> A Monk visual preview is already active.");
                return;
            }
            string query = (text ?? "").Trim();
            IEnumerable<MonkTechniqueDefinition> definitions;
            if (string.IsNullOrWhiteSpace(query) || string.Equals(query,
                    "strikes", StringComparison.OrdinalIgnoreCase))
                definitions = MonkSkillData.All.Where(value =>
                    value.IsOffensive).ToArray();
            else if (string.Equals(query, "all",
                StringComparison.OrdinalIgnoreCase))
                definitions = MonkSkillData.All.ToArray();
            else
            {
                MonkTechniqueDefinition one = ResolveDefinition(query);
                if (one == null) return;
                definitions = new[] { one };
            }
            SkillsPlugin.Instance.StartCoroutine(CaptureVfx(
                definitions.ToArray()));
        }

        private static IEnumerator CaptureVfx(
            IReadOnlyList<MonkTechniqueDefinition> definitions)
        {
            _captureRunning = true;
            GameObject target = null;
            string directory = Path.Combine(RuntimePaths.DataDirectory,
                "TestReports", "MonkVfx",
                DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") +
                "-John_EverQuest");
            Directory.CreateDirectory(directory);
            int captured = 0;
            int expectedFrames = definitions.Sum(value =>
                value.IsOffensive ? 3 : 1);
            Transform player = GameData.PlayerControl?.transform;
            Vector3 originalPlayerPosition = player?.position ?? Vector3.zero;
            try
            {
                Animator animator = player?.GetComponent<Animator>();
                if (player == null) yield break;
                GameObject dummyPrefab =
                    Resources.Load<GameObject>("NPCs/TestDummy");
                Vector3 captureDirection = (player.forward +
                    player.right * 0.55f).normalized;
                Vector3 capturePosition = player.position +
                    captureDirection * 2.65f;
                target = dummyPrefab != null
                    ? UnityEngine.Object.Instantiate(dummyPrefab,
                        capturePosition,
                        Quaternion.LookRotation(-captureDirection, player.up))
                    : new GameObject("CS Monk Capture Target");
                target.name = "CS Monk Isolated VFX Target";
                target.transform.position = capturePosition;
                foreach (MonoBehaviour behaviour in
                    target.GetComponentsInChildren<MonoBehaviour>(true))
                    behaviour.enabled = false;
                NavMeshAgent dummyNavigation =
                    target.GetComponent<NavMeshAgent>();
                if (dummyNavigation != null && dummyNavigation.enabled)
                    dummyNavigation.enabled = false;
                foreach (ParticleSystem existing in
                    target.GetComponentsInChildren<ParticleSystem>(true))
                    existing.Stop(true,
                        ParticleSystemStopBehavior.StopEmittingAndClear);
                for (int index = 0; index < definitions.Count; index++)
                {
                    MonkTechniqueDefinition definition = definitions[index];
                    ChatHelper.SendCombat(
                        "Capturing Monk effect: " + definition.Name,
                        "#E7C76F");
                    if (definition.IsOffensive)
                    {
                        float duration =
                            MonkAnimationDriver.GetDuration(definition);
                        float impact =
                            MonkAnimationDriver.GetImpactTime(definition);
                        float[] phaseTimes =
                        {
                            Mathf.Max(0.05f, impact * 0.46f),
                            Mathf.Max(0.08f, impact * 0.82f),
                            // Capture the readable contact hold, not its tail.
                            // Screenshot file flushes consume real frames
                            // between phases; sampling another 0.17s after
                            // impact mislabeled the recovery as the impact
                            // frame on quick strikes such as Jab. The delayed
                            // hot core and family silhouette are both active
                            // by +0.08s.
                            Mathf.Min(duration - 0.05f, impact + 0.08f)
                        };
                        string[] phaseNames =
                        {
                            "windup", "direction", "impact"
                        };
                        for (int phase = 0; phase < phaseTimes.Length;
                            phase++)
                        {
                            // Capture each phase from its own deterministic
                            // replay. Unity permits only one outstanding
                            // ScreenCapture write reliably; waiting for that
                            // write during one continuous replay shifts every
                            // later phase, while queueing multiple writes can
                            // drop the earlier PNG entirely.
                            float phaseStarted = Time.time;
                            MonkAnimationDriver.Play(definition, player,
                                target.transform);
                            yield return new WaitForSeconds(phaseTimes[phase]);
                            string phasePath = Path.Combine(directory,
                                $"{index + 1:00}-L{definition.Level:00}-" +
                                $"{definition.Key}-{phaseNames[phase]}.png");
                            ScreenCapture.CaptureScreenshot(phasePath);
                            int phaseWait = 0;
                            while (!File.Exists(phasePath) &&
                                phaseWait++ < 180)
                                yield return null;
                            if (File.Exists(phasePath)) captured++;

                            float phaseRemaining = duration -
                                (Time.time - phaseStarted) + 0.20f;
                            if (phaseRemaining > 0f)
                                yield return new WaitForSeconds(
                                    phaseRemaining);
                        }
                        yield return new WaitForSeconds(0.25f);
                    }
                    else
                    {
                        if (animator != null && !string.IsNullOrEmpty(
                            definition.Animation))
                            animator.SetTrigger(definition.Animation);
                        MonkVfx.Play(definition, player);
                        yield return new WaitForSeconds(0.12f);
                        string path = Path.Combine(directory,
                            $"{index + 1:00}-L{definition.Level:00}-" +
                            definition.Key + ".png");
                        ScreenCapture.CaptureScreenshot(path);
                        int wait = 0;
                        while (!File.Exists(path) && wait++ < 180)
                            yield return null;
                        if (File.Exists(path)) captured++;
                        yield return new WaitForSeconds(1.05f);
                    }
                }
            }
            finally
            {
                if (target != null) UnityEngine.Object.Destroy(target);
                RestoreQaPlayerPosition(player, originalPlayerPosition);
                _captureRunning = false;
                bool pass = captured == expectedFrames;
                SendResult("VFX capture", pass,
                    $"captured {captured}/{expectedFrames} wind-up, " +
                    $"direction, and impact frames to {directory}");
            }
        }

        private static void StartMotionCapture(string text)
        {
            if (!RequireQaMonk()) return;
            if (_previewRunning || _animationRunning || _captureRunning ||
                _mechanicsRunning || _autoattackRunning)
            {
                ChatHelper.Send(
                    "<color=#FFD54F>[Monk QA]</color> A Monk visual preview is already active.");
                return;
            }
            MonkTechniqueDefinition definition = ResolveDefinition(
                (text ?? "").Trim());
            if (definition == null) return;
            if (!definition.IsOffensive)
            {
                ChatHelper.Send(
                    "<color=#EF5350>[Monk QA]</color> Motion storyboards require an offensive punch or kick technique.");
                return;
            }
            SkillsPlugin.Instance.StartCoroutine(CaptureMotion(definition));
        }

        private static IEnumerator CaptureMotion(
            MonkTechniqueDefinition definition)
        {
            _captureRunning = true;
            GameObject target = null;
            string directory = Path.Combine(RuntimePaths.DataDirectory,
                "TestReports", "MonkMotion",
                DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" +
                definition.Key + "-John_EverQuest");
            Directory.CreateDirectory(directory);
            int captured = 0;
            const int frameCount = 9;
            var capturePaths = new List<string>(frameCount);
            var captureFrames = new List<Texture2D>(frameCount);
            Transform player = GameData.PlayerControl?.transform;
            Vector3 originalPlayerPosition = player?.position ?? Vector3.zero;
            try
            {
                if (player == null ||
                    !MonkAnimationDriver.SupportsBodyMotion(player))
                    yield break;
                target = new GameObject("CS Monk Motion Capture Target");
                target.transform.position = player.position +
                    player.forward * 2.35f;
                float duration = MonkAnimationDriver.GetDuration(definition);
                float started = Time.time;
                if (!MonkAnimationDriver.Play(definition, player,
                    target.transform, false)) yield break;
                for (int index = 0; index < frameCount; index++)
                {
                    float desired = duration * (index + 1f) /
                        (frameCount + 1f);
                    float waitFor = desired - (Time.time - started);
                    if (waitFor > 0f) yield return new WaitForSeconds(waitFor);
                    string path = Path.Combine(directory,
                        $"{index + 1:00}-{desired:0.000}s-{definition.Key}.png");
                    capturePaths.Add(path);
                    yield return new WaitForEndOfFrame();
                    // Keep the GPU readback in memory while the motion is
                    // active. PNG encoding and disk writes are intentionally
                    // deferred until all nine poses have been sampled: polling
                    // asynchronous files advanced game time, while overlapping
                    // CaptureScreenshot requests could silently drop a frame.
                    var frame = new Texture2D(Screen.width, Screen.height,
                        TextureFormat.RGB24, false);
                    frame.ReadPixels(new Rect(0f, 0f, Screen.width,
                        Screen.height), 0, 0, false);
                    frame.Apply(false, false);
                    captureFrames.Add(frame);
                }
                for (int index = 0; index < captureFrames.Count; index++)
                {
                    Texture2D frame = captureFrames[index];
                    File.WriteAllBytes(capturePaths[index],
                        frame.EncodeToPNG());
                    UnityEngine.Object.Destroy(frame);
                    captureFrames[index] = null;
                    if (File.Exists(capturePaths[index])) captured++;
                }
                yield return new WaitForSeconds(0.25f);
            }
            finally
            {
                foreach (Texture2D frame in captureFrames)
                    if (frame != null) UnityEngine.Object.Destroy(frame);
                if (target != null) UnityEngine.Object.Destroy(target);
                RestoreQaPlayerPosition(player, originalPlayerPosition);
                _captureRunning = false;
                bool signaturePass = definition.Key == "skybreaker_rush"
                    ? MonkAnimationDriver.LastSkybreakerRushDistance >= 0.55f
                    : definition.Key == "vaulting_tempest"
                        ? MonkAnimationDriver.LastVaultingCrouchRatio >= 0.82f
                        : true;
                string signatureDetail = definition.Key == "skybreaker_rush"
                    ? $"; root rush=" +
                      $"{MonkAnimationDriver.LastSkybreakerRushDistance:0.00}m"
                    : definition.Key == "vaulting_tempest"
                        ? $"; crouch peak=" +
                          $"{MonkAnimationDriver.LastVaultingCrouchRatio:0.00}"
                        : string.Empty;
                SendResult("motion storyboard",
                    captured == frameCount && signaturePass,
                    $"captured {captured}/{frameCount} full-body frames for " +
                    $"{definition.Name} ({MonkAnimationDriver.GetMotionName(definition)})" +
                    signatureDetail + $"; report={directory}");
            }
        }

        private static void StartMotionSweep(string text)
        {
            if (!RequireQaMonk()) return;
            if (_previewRunning || _animationRunning || _captureRunning ||
                _mechanicsRunning || _autoattackRunning)
            {
                ChatHelper.Send(
                    "<color=#FFD54F>[Monk QA]</color> A Monk visual preview is already active.");
                return;
            }
            string view = (text ?? string.Empty).Trim();
            bool sideView = string.Equals(view, "side",
                StringComparison.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(view) && !sideView)
            {
                ChatHelper.Send(
                    "<color=#EF5350>[Monk QA]</color> Motion view must be empty or 'side'.");
                return;
            }
            SkillsPlugin.Instance.StartCoroutine(CaptureMotionSweep(
                sideView));
        }

        private static IEnumerator CaptureMotionSweep(bool sideView)
        {
            _captureRunning = true;
            GameObject target = null;
            MonkTechniqueDefinition[] definitions = MonkSkillData.All
                .Where(value => value.IsOffensive).ToArray();
            string directory = Path.Combine(RuntimePaths.DataDirectory,
                "TestReports", "MonkMotion",
                DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") +
                (sideView ? "-side-strikes-" : "-all-strikes-") +
                "John_EverQuest");
            Directory.CreateDirectory(directory);
            int captured = 0;
            int poseFailures = 0;
            int continuityFailures = 0;
            var manifest = new StringBuilder();
            manifest.AppendLine("Monk full-body animation sweep");
            manifest.AppendLine("QA player: John EverQuest");
            manifest.AppendLine("Frames: wind-up, contact, recovery");
            manifest.AppendLine("View: " +
                (sideView ? "side profile" : "normal rear gameplay"));
            try
            {
                Transform player = GameData.PlayerControl?.transform;
                bool rigReady = player != null &&
                    MonkAnimationDriver.SupportsBodyMotion(player);
                manifest.AppendLine("Humanoid rig ready: " + rigReady);
                if (!rigReady) yield break;
                target = new GameObject("CS Monk Motion Sweep Target");
                target.transform.position = player.position +
                    (sideView ? player.right : player.forward) * 2.35f;
                for (int definitionIndex = 0;
                    definitionIndex < definitions.Length; definitionIndex++)
                {
                    MonkTechniqueDefinition definition =
                        definitions[definitionIndex];
                    float duration = MonkAnimationDriver.GetDuration(
                        definition);
                    float impact = MonkAnimationDriver.GetImpactTime(
                        definition);
                    float[] frameTimes =
                    {
                        Mathf.Max(0.04f, impact * 0.52f),
                        Mathf.Min(duration - 0.04f, impact + 0.015f),
                        Mathf.Min(duration - 0.04f,
                            impact + (duration - impact) * 0.52f)
                    };
                    float started = Time.time;
                    bool played = MonkAnimationDriver.Play(definition,
                        player, target.transform, false);
                    manifest.AppendLine(
                        $"{definition.Key}\t{MonkAnimationDriver.GetMotionName(definition)}\tplayed={played}\timpact={impact:0.000}s");
                    for (int frameIndex = 0; frameIndex < frameTimes.Length;
                        frameIndex++)
                    {
                        float waitFor = frameTimes[frameIndex] -
                            (Time.time - started);
                        if (waitFor > 0f)
                            yield return new WaitForSeconds(waitFor);
                        string phase = frameIndex == 0 ? "windup" :
                            frameIndex == 1 ? "contact" : "recovery";
                        if (frameIndex == 1 &&
                            !RecordPoseMetrics(player, definition.Key,
                                manifest))
                            poseFailures++;
                        string path = Path.Combine(directory,
                            $"{definitionIndex + 1:00}-{definition.Key}-" +
                            $"{phase}-{frameTimes[frameIndex]:0.000}s.png");
                        ScreenCapture.CaptureScreenshot(path);
                        int wait = 0;
                        while (!File.Exists(path) && wait++ < 180)
                            yield return null;
                        if (File.Exists(path)) captured++;
                    }
                    float remaining = duration - (Time.time - started) +
                        0.22f;
                    if (remaining > 0f)
                        yield return new WaitForSeconds(remaining);
                    if (!RecordRecoveryMetrics(player, definition.Key,
                        manifest))
                        continuityFailures++;
                }
                for (int autoIndex = 0; autoIndex < 2; autoIndex++)
                {
                    bool mainHand = autoIndex == 0;
                    string key = mainHand ? "handwrap_auto_main" :
                        "handwrap_auto_off";
                    float duration = MonkAnimationDriver.GetAutoDuration();
                    float impact = MonkAnimationDriver.GetAutoImpactTime();
                    float[] frameTimes =
                    {
                        impact * 0.52f,
                        impact + 0.015f,
                        impact + (duration - impact) * 0.52f
                    };
                    float started = Time.time;
                    bool played = MonkAnimationDriver.PlayAutoPunch(player,
                        target.transform, mainHand);
                    manifest.AppendLine(
                        $"{key}\t{(mainHand ? "authored main-hand punch" : "authored off-hand punch")}\tplayed={played}\timpact={impact:0.000}s");
                    for (int frameIndex = 0; frameIndex < frameTimes.Length;
                        frameIndex++)
                    {
                        float waitFor = frameTimes[frameIndex] -
                            (Time.time - started);
                        if (waitFor > 0f)
                            yield return new WaitForSeconds(waitFor);
                        string phase = frameIndex == 0 ? "windup" :
                            frameIndex == 1 ? "contact" : "recovery";
                        if (frameIndex == 1 &&
                            !RecordPoseMetrics(player, key, manifest))
                            poseFailures++;
                        string path = Path.Combine(directory,
                            $"{definitions.Length + autoIndex + 1:00}-" +
                            $"{key}-{phase}-{frameTimes[frameIndex]:0.000}s.png");
                        ScreenCapture.CaptureScreenshot(path);
                        int wait = 0;
                        while (!File.Exists(path) && wait++ < 180)
                            yield return null;
                        if (File.Exists(path)) captured++;
                    }
                    float remaining = duration - (Time.time - started) +
                        0.22f;
                    if (remaining > 0f)
                        yield return new WaitForSeconds(remaining);
                    if (!RecordRecoveryMetrics(player, key, manifest))
                        continuityFailures++;
                }
            }
            finally
            {
                if (target != null) UnityEngine.Object.Destroy(target);
                int expected = (definitions.Length + 2) * 3;
                bool pass = captured == expected && poseFailures == 0 &&
                    continuityFailures == 0;
                manifest.AppendLine($"Result: {(pass ? "PASS" : "FAIL")}");
                manifest.AppendLine($"Captured: {captured}/{expected}");
                manifest.AppendLine($"Pose metric failures: {poseFailures}");
                manifest.AppendLine(
                    $"Recovery continuity failures: {continuityFailures}");
                File.WriteAllText(Path.Combine(directory, "manifest.txt"),
                    manifest.ToString());
                _captureRunning = false;
                SendResult("full-body motion sweep", pass,
                    $"captured {captured}/{expected} wind-up/contact/recovery " +
                    $"frames across {definitions.Length} offensive techniques " +
                    $"and both handwrap auto-attacks to {directory}");
            }
        }

        private static bool RecordPoseMetrics(Transform player, string key,
            StringBuilder manifest)
        {
            MonkAnimationDriver driver = player == null ? null :
                player.GetComponentInChildren<MonkAnimationDriver>();
            if (driver == null || !driver.TryGetQaPoseMetrics(
                out bool aimRequired, out float aimDot,
                out float groundErrorRatio,
                out float horizontalAnchorErrorRatio))
            {
                manifest.AppendLine(
                    $"{key}\tpose_metrics=unavailable\tpass=False");
                return false;
            }
            bool aimPass = !aimRequired || aimDot >= 0.72f;
            bool groundPass = groundErrorRatio <= 0.025f;
            bool horizontalPass = horizontalAnchorErrorRatio <= 0.01f;

            // The three full-body finishers intentionally violate the idle
            // strike invariants. Validate their authored silhouette instead
            // of reporting a false failure for leaving the ground or moving
            // the visual hips forward while the gameplay collider stays safe.
            if (key == "dragon_punch")
            {
                groundPass = groundErrorRatio >= 0.035f &&
                    groundErrorRatio <= 0.40f;
                horizontalPass = horizontalAnchorErrorRatio <= 0.06f;
            }
            else if (key == "vaulting_tempest")
            {
                groundPass = groundErrorRatio >= 0.35f &&
                    groundErrorRatio <= 1.15f;
                horizontalPass = horizontalAnchorErrorRatio <= 0.12f;
            }
            else if (key == "skybreaker_rush")
            {
                groundPass = groundErrorRatio <= 0.05f;
                horizontalPass = horizontalAnchorErrorRatio >= 0.08f &&
                    horizontalAnchorErrorRatio <= 0.30f;
            }
            bool pass = aimPass && groundPass && horizontalPass;
            manifest.AppendLine(
                $"{key}\tpose_metrics\taim=" +
                $"{(aimRequired ? aimDot.ToString("0.000") : "n/a")}" +
                $"\tground={groundErrorRatio:0.0000}h" +
                $"\thorizontal={horizontalAnchorErrorRatio:0.0000}h" +
                $"\tpass={pass}");
            return pass;
        }

        private static bool RecordRecoveryMetrics(Transform player,
            string key, StringBuilder manifest)
        {
            MonkAnimationDriver driver = player == null ? null :
                player.GetComponentInChildren<MonkAnimationDriver>();
            if (driver == null || !driver.TryGetQaRecoveryMetrics(
                out float maxAngularSpeed,
                out float maxHipSpeedRatio,
                out int samples))
            {
                manifest.AppendLine(
                    $"{key}\trecovery_metrics=unavailable\tpass=False");
                return false;
            }
            const float MaxAngularSpeed = 1200f;
            const float MaxHipSpeedRatio = 1.50f;
            bool pass = maxAngularSpeed <= MaxAngularSpeed &&
                maxHipSpeedRatio <= MaxHipSpeedRatio;
            manifest.AppendLine(
                $"{key}\trecovery_metrics\tangular=" +
                $"{maxAngularSpeed:0.0}deg/s\thip=" +
                $"{maxHipSpeedRatio:0.000}h/s\tsamples={samples}" +
                $"\tpass={pass}");
            return pass;
        }

        private static void UseTechnique(string text)
        {
            if (!RequireQaMonk()) return;
            MonkTechniqueDefinition definition = ResolveDefinition(text);
            if (definition == null) return;
            if (definition.Kind == MonkTechniqueKind.Passive)
            {
                SendResult("use " + definition.Name, false,
                    "passive Monk spells are always active and spend no mana");
                return;
            }
            Spell spell = MonkCatalog.GetSpell(definition.Key);
            Character user = GameData.PlayerControl?.Myself;
            Character target = definition.IsOffensive
                ? GameData.PlayerControl?.CurrentTarget
                : user;
            if (definition.IsOffensive && target == null)
            {
                ChatHelper.Send(
                    "<color=#EF5350>[Monk QA]</color> Select a live target first.");
                return;
            }
            int manaBefore = user?.MyStats?.GetCurrentMana() ?? 0;
            bool used = user?.MySpells?
                .StartSpell(spell, target?.MyStats) == true;
            int manaAfter = user?.MyStats?.GetCurrentMana() ?? manaBefore;
            int spent = manaBefore - manaAfter;
            bool pass = used && spent == spell.ManaCost;
            SendResult("use " + definition.Name, pass,
                used
                    ? $"native spell call accepted; mana {manaBefore} -> " +
                      $"{manaAfter} (cost {spell.ManaCost})"
                    : $"native spell call rejected; mana {manaBefore} -> " +
                      $"{manaAfter}; check mana, target, level, and cooldown");
        }

        private static void TestManaContract()
        {
            if (!RequireQaMonk()) return;
            Character user = GameData.PlayerControl?.Myself;
            Stats stats = user?.MyStats;
            Spell spell = MonkCatalog.GetSpell("mend");
            if (user?.MySpells == null || stats == null || spell == null)
            {
                SendResult("mana contract", false,
                    "player spell component, stats, or Mend spell is unavailable");
                return;
            }

            int cost = spell.ManaCost;
            int maximumMana = stats.GetCurrentMaxMana();
            if (cost <= 0 || maximumMana < cost)
            {
                SendResult("mana contract", false,
                    $"invalid Mend cost/pool: cost={cost}, max={maximumMana}");
                return;
            }

            int originalHealth = stats.CurrentHP;
            int originalMana = stats.CurrentMana;
            var cooldowns = new List<KeyValuePair<Hotkeys, float>>();
            foreach (Hotkeys hotkey in
                GameData.GM?.HKManager?.AllHotkeys ??
                new List<Hotkeys>())
            {
                if (hotkey != null &&
                    ReferenceEquals(hotkey.AssignedSpell, spell))
                    cooldowns.Add(new KeyValuePair<Hotkeys, float>(
                        hotkey, hotkey.Cooldown));
            }

            bool lowManaBlocked = false;
            bool fundedAccepted = false;
            int fundedBefore = 0;
            int fundedAfter = 0;
            try
            {
                int damagedHealth = Mathf.Max(1,
                    stats.CurrentMaxHP -
                    Mathf.Max(10, stats.CurrentMaxHP / 2));
                stats.CurrentHP = damagedHealth;
                stats.CurrentMana = Mathf.Max(0, cost - 1);
                int lowManaHealth = stats.CurrentHP;
                int lowManaBefore = stats.CurrentMana;
                bool lowManaAccepted =
                    user.MySpells.StartSpell(spell, stats);
                lowManaBlocked = !lowManaAccepted &&
                    stats.CurrentMana == lowManaBefore &&
                    stats.CurrentHP == lowManaHealth;

                stats.CurrentHP = damagedHealth;
                stats.CurrentMana = Mathf.Min(maximumMana,
                    Mathf.Max(cost, originalMana));
                fundedBefore = stats.CurrentMana;
                fundedAccepted = user.MySpells.StartSpell(spell, stats);
                fundedAfter = stats.CurrentMana;
            }
            finally
            {
                stats.CurrentHP = originalHealth;
                stats.CurrentMana = originalMana;
                foreach (KeyValuePair<Hotkeys, float> entry in cooldowns)
                    if (entry.Key != null)
                        entry.Key.Cooldown = entry.Value;
            }

            bool exactDeduction = fundedAccepted &&
                fundedBefore - fundedAfter == cost;
            SendResult("mana contract",
                lowManaBlocked && exactDeduction,
                $"Mend cost={cost}; insufficient mana " +
                $"{(lowManaBlocked ? "blocked without side effects" : "FAILED")}; " +
                $"funded cast={(fundedAccepted ? "accepted" : "rejected")}, " +
                $"mana {fundedBefore} -> {fundedAfter}; QA health/mana restored");
        }

        private static void StartAutoattackVisual(string text)
        {
            if (!RequireQaMonk()) return;
            if (_previewRunning || _animationRunning || _captureRunning ||
                _mechanicsRunning || _autoattackRunning ||
                Testing.Level35DpsBenchmark.IsRunning)
            {
                ChatHelper.Send(
                    "<color=#FFD54F>[Monk QA]</color> Another Monk visual, mechanics, or DPS run is already active.");
                return;
            }
            string view = (text ?? string.Empty).Trim();
            bool sideView = string.Equals(view, "side",
                StringComparison.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(view) && !sideView)
            {
                ChatHelper.Send(
                    "<color=#EF5350>[Monk QA]</color> Autoattack view must be empty or 'side'.");
                return;
            }
            SkillsPlugin.Instance.StartCoroutine(
                CaptureRealHandwrapAutoattacks(sideView));
        }

        private static void TestInventoryClickRecovery()
        {
            if (!RequireQaMonk()) return;
            Inventory inventory = GameData.PlayerInv;
            ItemIcon slot = inventory?.StoredSlots?.FirstOrDefault(value =>
                value != null && Items.BagSystem.IsDirectInventorySlot(value));
            ItemIcon mouse = GameData.MouseSlot;
            if (inventory?.Empty == null || slot == null || mouse == null)
            {
                SendResult("inventory click null-slot recovery", false,
                    "native inventory, direct slot, mouse slot, or Empty item unavailable");
                return;
            }

            Item originalSlotItem = slot.MyItem;
            int originalSlotQuantity = slot.Quantity;
            Item originalMouseItem = mouse.MyItem;
            int originalMouseQuantity = mouse.Quantity;
            ItemIcon originalCursor = GameData.ItemOnCursor;
            List<Class> originalEmptyClasses = inventory.Empty.Classes;
            bool normalizedNullSlots = false;
            bool normalizedClasses = false;
            bool nativeCompleted = false;
            string error = string.Empty;
            try
            {
                // Reproduce the transient state reported after removing the
                // native gloves: both a clicked inventory slot and the cursor
                // slot temporarily contain null instead of Inventory.Empty.
                slot.MyItem = null;
                slot.Quantity = 0;
                mouse.MyItem = null;
                mouse.Quantity = 0;
                GameData.ItemOnCursor = null;
                var pointer = new UnityEngine.EventSystems.PointerEventData(
                    UnityEngine.EventSystems.EventSystem.current)
                {
                    button = UnityEngine.EventSystems.PointerEventData
                        .InputButton.Left
                };
                slot.OnPointerDown(pointer);
                slot.OnPointerUp(pointer);
                nativeCompleted = true;
                normalizedNullSlots = slot.MyItem != null &&
                    mouse.MyItem != null;

                // Independently prove the second native dereference hazard:
                // an item whose class collection has not yet been populated.
                inventory.Empty.Classes = null;
                slot.MyItem = inventory.Empty;
                Patches.Patch_BagStoreOnRightClick.Prefix(slot, pointer);
                normalizedClasses = inventory.Empty.Classes != null;
            }
            catch (Exception ex)
            {
                error = ex.GetBaseException().GetType().Name + ": " +
                    ex.GetBaseException().Message;
            }
            finally
            {
                slot.MyItem = originalSlotItem ?? inventory.Empty;
                slot.Quantity = originalSlotQuantity;
                mouse.MyItem = originalMouseItem ?? inventory.Empty;
                mouse.Quantity = originalMouseQuantity;
                GameData.ItemOnCursor = originalCursor;
                inventory.Empty.Classes = originalEmptyClasses ??
                    new List<Class>();
                try
                {
                    slot.UpdateSlotImage();
                    mouse.UpdateSlotImage();
                    inventory.UpdatePlayerInventory();
                }
                catch { }
            }
            bool pass = nativeCompleted && normalizedNullSlots &&
                normalizedClasses && string.IsNullOrEmpty(error);
            SendResult("inventory click null-slot recovery", pass,
                $"native OnPointerUp completed={nativeCompleted}; null slots normalized=" +
                $"{normalizedNullSlots}; null Classes normalized={normalizedClasses}; " +
                (string.IsNullOrEmpty(error) ? "QA state restored" : error));
        }

        private static void StartReadyIdleTest()
        {
            if (!RequireQaCharacter()) return;
            if (_previewRunning || _animationRunning || _captureRunning ||
                _mechanicsRunning || _autoattackRunning ||
                Testing.Level35DpsBenchmark.IsRunning)
            {
                ChatHelper.Send(
                    "<color=#FFD54F>[Monk QA]</color> Another Monk visual, mechanics, or DPS run is already active.");
                return;
            }
            SkillsPlugin.Instance.StartCoroutine(
                CaptureReadyIdleTransition());
        }

        private static IEnumerator CaptureReadyIdleTransition()
        {
            _autoattackRunning = true;
            bool originalAutoattacking = GameData.Autoattacking;
            bool originalTyping = GameData.PlayerTyping;
            Inventory inventory = GameData.PlayerInv;
            ItemIcon mainSlot = inventory?.MH;
            ItemIcon offSlot = inventory?.OH;
            Item originalMain = mainSlot?.MyItem;
            Item originalOff = offSlot?.MyItem;
            int originalMainQuantity = mainSlot?.Quantity ?? 0;
            int originalOffQuantity = offSlot?.Quantity ?? 0;
            ItemIcon scratchSlot = inventory?.StoredSlots?.FirstOrDefault(
                value => value != null && !value.TrashSlot);
            Item originalScratch = scratchSlot?.MyItem;
            int originalScratchQuantity = scratchSlot?.Quantity ?? 0;
            ItemIcon mouseSlot = GameData.MouseSlot;
            Item originalMouse = mouseSlot?.MyItem;
            int originalMouseQuantity = mouseSlot?.Quantity ?? 0;
            bool originalMouseDragging = mouseSlot?.dragging == true;
            ItemIcon originalCursor = GameData.ItemOnCursor;
            ItemIcon originalHighlight = GameData.HighlightedItem;
            FieldInfo mouseHomeField = typeof(ItemIcon).GetField(
                "MouseHomeSlot", BindingFlags.Instance |
                BindingFlags.NonPublic);
            ItemIcon originalMouseHome = mouseHomeField?.GetValue(mouseSlot)
                as ItemIcon;
            Item auditWrap = MonkGear.AllHandwraps.FirstOrDefault(value =>
                value != null && value.ItemLevel >= 30) ??
                MonkGear.AllHandwraps.FirstOrDefault();
            Transform player = GameData.PlayerControl?.transform;
            Animator animator = GameData.PlayerControl?.Myself?.GetMyAnim() ??
                player?.GetComponent<Animator>() ??
                player?.GetComponentInChildren<Animator>();
            string directory = Path.Combine(RuntimePaths.DataDirectory,
                "TestReports", "MonkReadyIdle",
                DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") +
                "-John_EverQuest");
            Directory.CreateDirectory(directory);
            int captured = 0;
            bool guardReady = false;
            bool guardGeometry = false;
            bool nativeEquip = false;
            bool nativeEquipFlagsCleared = false;
            bool attackHandoff = false;
            bool recoveryReady = false;
            bool recoveryGeometry = false;
            bool nativeUnequip = false;
            bool nativeUnequipFlagsCleared = false;
            bool unequippedReady = false;
            bool unequippedGeometry = false;
            float guardWeight = 0f;
            float attackWeight = 0f;
            float recoveryWeight = 0f;
            float unequippedWeight = 0f;
            string guardGeometryDetail = "not sampled";
            string recoveryGeometryDetail = "not sampled";
            string unequippedGeometryDetail = "not sampled";
            int classProbes = 0;
            int classPasses = 0;
            Stats playerStats = GameData.PlayerStats;
            Class originalClass = playerStats?.CharacterClass;
            try
            {
                if (player == null || animator == null || inventory == null ||
                    mainSlot == null || offSlot == null || scratchSlot == null ||
                    mouseSlot == null || auditWrap == null)
                    yield break;
                // Reproduce the user's ordering exactly: engage autoattack while
                // bare, then quick-equip through ItemIcon.OnPointerUp. Assigning
                // MH.MyItem directly misses the native 1HSmall animator write
                // which caused this regression and previously gave a false pass.
                mainSlot.MyItem = inventory.Empty;
                mainSlot.Quantity = 1;
                offSlot.MyItem = inventory.Empty;
                offSlot.Quantity = 1;
                scratchSlot.MyItem = auditWrap;
                scratchSlot.Quantity = 1;
                mouseSlot.MyItem = inventory.Empty;
                mouseSlot.Quantity = 1;
                mouseSlot.dragging = false;
                GameData.ItemOnCursor = null;
                mainSlot.UpdateSlotImage();
                offSlot.UpdateSlotImage();
                scratchSlot.UpdateSlotImage();
                mouseSlot.UpdateSlotImage();
                inventory.UpdatePlayerInventory();
                GameData.PlayerStats?.CalcStats();
                MonkHandwrapVisuals.Tick();
                GameData.PlayerTyping = true;
                GameData.Autoattacking = true;
                HandToHandReadyIdle.Tick();
                yield return new WaitForSecondsRealtime(0.28f);

                var equipPointer = new UnityEngine.EventSystems
                    .PointerEventData(UnityEngine.EventSystems
                        .EventSystem.current)
                {
                    button = UnityEngine.EventSystems.PointerEventData
                        .InputButton.Right
                };
                GameData.HighlightedItem = scratchSlot;
                scratchSlot.OnPointerDown(equipPointer);
                scratchSlot.OnPointerUp(equipPointer);
                nativeEquip = ReferenceEquals(mainSlot.MyItem, auditWrap) &&
                    ReferenceEquals(scratchSlot.MyItem, inventory.Empty);
                HandToHandReadyIdle.Tick();
                yield return new WaitForSecondsRealtime(0.28f);
                nativeEquipFlagsCleared = !ReadAnimatorBool(animator,
                    "1HSmall") && !ReadAnimatorBool(animator, "1HDagger") &&
                    !ReadAnimatorBool(animator, "2HMelee") &&
                    !ReadAnimatorBool(animator, "2HStaff");
                guardReady = HandToHandReadyIdle.TryGetRuntimeState(
                    out bool guardRig, out bool guardRequested,
                    out guardWeight, out float guardEffective) &&
                    guardRig && guardRequested && guardWeight >= 0.90f &&
                    guardEffective >= 0.80f;
                guardGeometry = MeasureReadyGuardGeometry(animator,
                    out guardGeometryDetail);
                guardReady &= guardGeometry;
                string guardPath = Path.Combine(directory,
                    "01-fist-guard.png");
                CapturePlayerModel(guardPath);
                yield return null;

                bool invoked = MonkAnimationDriver.PlayAutoPunch(
                    player, null, true);
                float contact = MonkAnimationDriver.GetAutoImpactTime();
                float strikeSample = Mathf.Min(
                    MonkAnimationDriver.GetAutoDuration() - 0.03f,
                    contact + 0.015f);
                yield return new WaitForSecondsRealtime(
                    Mathf.Max(0.05f, strikeSample));
                MonkAnimationDriver attack =
                    animator.GetComponent<MonkAnimationDriver>();
                HandToHandReadyIdle.TryGetRuntimeState(out _,
                    out _, out _, out float attackEffective);
                attackWeight = attack?.RigOwnershipWeight ?? 0f;
                attackHandoff = invoked && attack?.IsActive == true &&
                    attackWeight > 0.35f &&
                    attackEffective < guardEffective;
                string strikePath = Path.Combine(directory,
                    "02-fist-strike-handoff.png");
                CapturePlayerModel(strikePath);
                yield return null;

                float remaining = MonkAnimationDriver.GetAutoDuration() -
                    strikeSample + 0.22f;
                if (remaining > 0f)
                    yield return new WaitForSecondsRealtime(remaining);
                HandToHandReadyIdle.TryGetRuntimeState(out bool recoveryRig,
                    out bool recoveryRequested, out recoveryWeight,
                    out float recoveryEffective);
                recoveryReady = recoveryRig && recoveryRequested &&
                    recoveryWeight >= 0.90f &&
                    recoveryEffective >= 0.80f;
                recoveryGeometry = MeasureReadyGuardGeometry(animator,
                    out recoveryGeometryDetail);
                recoveryReady &= recoveryGeometry;

                Class[] classes =
                {
                    GameData.ClassDB?.Arcanist,
                    GameData.ClassDB?.Druid,
                    GameData.ClassDB?.Duelist,
                    GameData.ClassDB?.Paladin,
                    GameData.ClassDB?.Stormcaller,
                    GameData.ClassDB?.Reaver,
                    Necromancer.NecromancerCatalog.NecromancerClass,
                    MonkCatalog.MonkClass
                };
                foreach (Class characterClass in classes
                    .Where(value => value != null).Distinct())
                {
                    classProbes++;
                    if (playerStats != null)
                        playerStats.CharacterClass = characterClass;
                    HandToHandReadyIdle.Tick();
                    yield return null;
                    if (HandToHandReadyIdle.TryGetRuntimeState(
                            out bool classRig, out bool classRequested,
                            out float classBlend, out _) &&
                        classRig && classRequested && classBlend >= 0.90f)
                        classPasses++;
                }
                if (playerStats != null)
                    playerStats.CharacterClass = originalClass;

                string recoveryPath = Path.Combine(directory,
                    "03-fist-guard-recovered.png");
                CapturePlayerModel(recoveryPath);
                yield return null;

                // Exercise the matching user-facing unequip path. The native
                // empty-slot image branch historically left 1HSmall latched,
                // so the approved bare-hand guard must survive after the item
                // moves to the cursor as well.
                var unequipPointer = new UnityEngine.EventSystems
                    .PointerEventData(UnityEngine.EventSystems
                        .EventSystem.current)
                {
                    button = UnityEngine.EventSystems.PointerEventData
                        .InputButton.Left
                };
                GameData.HighlightedItem = mainSlot;
                mainSlot.OnPointerDown(unequipPointer);
                mainSlot.OnPointerUp(unequipPointer);
                nativeUnequip = ReferenceEquals(mainSlot.MyItem,
                        inventory.Empty) &&
                    ReferenceEquals(mouseSlot.MyItem, auditWrap);
                HandToHandReadyIdle.Tick();
                yield return new WaitForSecondsRealtime(0.28f);
                nativeUnequipFlagsCleared = !ReadAnimatorBool(animator,
                    "1HSmall") && !ReadAnimatorBool(animator, "1HDagger") &&
                    !ReadAnimatorBool(animator, "2HMelee") &&
                    !ReadAnimatorBool(animator, "2HStaff");
                unequippedReady = HandToHandReadyIdle.TryGetRuntimeState(
                    out bool unequippedRig, out bool unequippedRequested,
                    out unequippedWeight, out float unequippedEffective) &&
                    unequippedRig && unequippedRequested &&
                    unequippedWeight >= 0.90f &&
                    unequippedEffective >= 0.80f;
                unequippedGeometry = MeasureReadyGuardGeometry(animator,
                    out unequippedGeometryDetail);
                unequippedReady &= unequippedGeometry;
                string unequippedPath = Path.Combine(directory,
                    "04-fist-guard-after-native-unequip.png");
                CapturePlayerModel(unequippedPath);
                yield return null;

                string[] paths =
                {
                    guardPath, strikePath, recoveryPath, unequippedPath
                };
                foreach (string path in paths)
                {
                    int wait = 0;
                    while (!File.Exists(path) && wait++ < 180)
                        yield return null;
                    if (File.Exists(path)) captured++;
                }
            }
            finally
            {
                if (playerStats != null)
                    playerStats.CharacterClass = originalClass;
                if (mainSlot != null)
                {
                    mainSlot.MyItem = originalMain ?? inventory?.Empty;
                    mainSlot.Quantity = originalMainQuantity;
                    try { mainSlot.UpdateSlotImage(); } catch { }
                }
                if (offSlot != null)
                {
                    offSlot.MyItem = originalOff ?? inventory?.Empty;
                    offSlot.Quantity = originalOffQuantity;
                    try { offSlot.UpdateSlotImage(); } catch { }
                }
                if (scratchSlot != null)
                {
                    scratchSlot.MyItem = originalScratch ?? inventory?.Empty;
                    scratchSlot.Quantity = originalScratchQuantity;
                    try { scratchSlot.UpdateSlotImage(); } catch { }
                }
                if (mouseSlot != null)
                {
                    mouseSlot.MyItem = originalMouse ?? inventory?.Empty;
                    mouseSlot.Quantity = originalMouseQuantity;
                    mouseSlot.dragging = originalMouseDragging;
                    try { mouseSlot.UpdateSlotImage(); } catch { }
                    try
                    {
                        mouseHomeField?.SetValue(mouseSlot,
                            originalMouseHome);
                    }
                    catch { }
                }
                GameData.ItemOnCursor = originalCursor;
                GameData.HighlightedItem = originalHighlight;
                try
                {
                    inventory?.UpdatePlayerInventory();
                    GameData.PlayerStats?.CalcStats();
                    MonkHandwrapVisuals.Tick();
                }
                catch { }
                GameData.Autoattacking = originalAutoattacking;
                GameData.PlayerTyping = originalTyping;
                HandToHandReadyIdle.Tick();
                _autoattackRunning = false;
                bool pass = nativeEquip && nativeEquipFlagsCleared &&
                    guardReady && attackHandoff &&
                    recoveryReady && guardGeometry && recoveryGeometry &&
                    nativeUnequip && nativeUnequipFlagsCleared &&
                    unequippedReady && unequippedGeometry &&
                    classProbes == 8 &&
                    classPasses == classProbes && captured == 4;
                SendResult("fist ready idle", pass,
                    $"native equip={nativeEquip}, transition=" +
                    $"{auditWrap?.ItemName ?? "missing"}, weapon flags cleared=" +
                    $"{nativeEquipFlagsCleared}; " +
                    $"guard={guardReady} ({guardWeight:0.00}), " +
                    $"guard geometry=[{guardGeometryDetail}], " +
                    $"strike handoff={attackHandoff} " +
                    $"({attackWeight:0.00}), recovery={recoveryReady} " +
                    $"({recoveryWeight:0.00}), recovery geometry=[" +
                    $"{recoveryGeometryDetail}]; native unequip=" +
                    $"{nativeUnequip}, weapon flags cleared=" +
                    $"{nativeUnequipFlagsCleared}, bare guard=" +
                    $"{unequippedReady} ({unequippedWeight:0.00}), " +
                    $"bare geometry=[{unequippedGeometryDetail}], classes=" +
                    $"{classPasses}/{classProbes}, captures={captured}/4; " +
                    $"report={directory}");
            }
        }

        /// <summary>
        /// Rejects a guard whose blend weights look healthy but whose actual
        /// bones still form the crossed, uneven sword-idle silhouette. Values
        /// are normalized by arm length so this works on different player
        /// bodies rather than validating only the QA character's rig.
        /// </summary>
        private static bool MeasureReadyGuardGeometry(Animator animator,
            out string detail)
        {
            detail = "rig unavailable";
            Transform root = GameData.PlayerControl?.transform;
            if (animator == null || !animator.isHuman || root == null)
                return false;
            try
            {
                Transform chest = animator.GetBoneTransform(
                    HumanBodyBones.UpperChest) ??
                    animator.GetBoneTransform(HumanBodyBones.Chest);
                Transform leftUpper = animator.GetBoneTransform(
                    HumanBodyBones.LeftUpperArm);
                Transform leftLower = animator.GetBoneTransform(
                    HumanBodyBones.LeftLowerArm);
                Transform leftHand = animator.GetBoneTransform(
                    HumanBodyBones.LeftHand);
                Transform rightUpper = animator.GetBoneTransform(
                    HumanBodyBones.RightUpperArm);
                Transform rightLower = animator.GetBoneTransform(
                    HumanBodyBones.RightLowerArm);
                Transform rightHand = animator.GetBoneTransform(
                    HumanBodyBones.RightHand);
                if (chest == null || leftUpper == null || leftLower == null ||
                    leftHand == null || rightUpper == null ||
                    rightLower == null || rightHand == null)
                    return false;

                float leftLength = Vector3.Distance(leftUpper.position,
                    leftLower.position) + Vector3.Distance(
                    leftLower.position, leftHand.position);
                float rightLength = Vector3.Distance(rightUpper.position,
                    rightLower.position) + Vector3.Distance(
                    rightLower.position, rightHand.position);
                float scale = (leftLength + rightLength) * 0.5f;
                if (scale <= 0.001f) return false;

                Vector3 up = root.up;
                Vector3 forward = root.forward;
                Vector3 right = root.right;
                Vector3 leftForearm = leftHand.position -
                    leftLower.position;
                Vector3 rightForearm = rightHand.position -
                    rightLower.position;
                float leftRise = Vector3.Dot(leftForearm, up) / scale;
                float rightRise = Vector3.Dot(rightForearm, up) / scale;
                float leftFront = Vector3.Dot(leftForearm, forward) / scale;
                float rightFront = Vector3.Dot(rightForearm, forward) / scale;
                float leftSide = Vector3.Dot(
                    leftHand.position - chest.position, right) / scale;
                float rightSide = Vector3.Dot(
                    rightHand.position - chest.position, right) / scale;
                float separation = Vector3.Dot(
                    rightHand.position - leftHand.position, right) / scale;
                float levelDelta = Mathf.Abs(Vector3.Dot(
                    rightHand.position - leftHand.position, up)) / scale;

                bool pass = leftRise >= 0.16f && rightRise >= 0.16f &&
                    leftFront >= 0.12f && rightFront >= 0.12f &&
                    leftSide <= -0.02f && rightSide >= 0.02f &&
                    separation >= 0.25f && separation <= 1.20f &&
                    levelDelta <= 0.18f;
                detail = $"pass={pass}, rise={leftRise:0.00}/" +
                    $"{rightRise:0.00}, front={leftFront:0.00}/" +
                    $"{rightFront:0.00}, side={leftSide:0.00}/" +
                    $"{rightSide:0.00}, separation={separation:0.00}, " +
                    $"level delta={levelDelta:0.00}";
                return pass;
            }
            catch (Exception ex)
            {
                detail = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Exercises PlayerCombat.PerformAttacks itself with attackCount=2.
        /// That is the exact native path which used to layer MeleeSwing,
        /// DualWield, DoubleAttack, and OHDoubleAttack over the authored
        /// punches. Three main-hand calls prove that one two-handed wrap item
        /// alternates the visible right/left striking hands without creating
        /// an illegal off-hand weapon attack.
        /// </summary>
        private static IEnumerator CaptureRealHandwrapAutoattacks(
            bool sideView)
        {
            _autoattackRunning = true;
            Inventory inventory = GameData.PlayerInv;
            ItemIcon mainSlot = inventory?.MH;
            ItemIcon offSlot = inventory?.OH;
            Item originalMain = mainSlot?.MyItem;
            Item originalOff = offSlot?.MyItem;
            int originalMainQuantity = mainSlot?.Quantity ?? 0;
            int originalOffQuantity = offSlot?.Quantity ?? 0;
            Character player = GameData.PlayerControl?.Myself;
            Transform playerTransform = GameData.PlayerControl?.transform;
            Animator animator = playerTransform?.GetComponent<Animator>();
            Character originalTarget = GameData.PlayerControl?.CurrentTarget;
            bool originalTyping = GameData.PlayerTyping;
            GameObject dummyObject = null;
            Character dummy = null;
            int invoked = 0;
            int captured = 0;
            int boolLeaks = 0;
            int poseFailures = 0;
            int continuityFailures = 0;
            int sideFailures = 0;
            int autoImpactsBefore = MonkAudio.AutoImpactRequestCount;
            string directory = Path.Combine(RuntimePaths.DataDirectory,
                "TestReports", "MonkAutoattack",
                DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") +
                (sideView ? "-side-" : "-rear-") +
                "John_EverQuest");
            Directory.CreateDirectory(directory);
            var manifest = new StringBuilder();
            manifest.AppendLine("Monk real handwrap autoattack regression");
            manifest.AppendLine("QA player: John EverQuest");
            manifest.AppendLine(
                "Native path: PlayerCombat.PerformAttacks(target, attackCount=2, hand)");
            manifest.AppendLine("View: " +
                (sideView ? "side profile" : "normal rear gameplay"));
            manifest.AppendLine(
                "Regression: no MeleeSwing/DualWield trigger and no DoubleAttack/OHDoubleAttack boolean may layer beneath authored punches");
            manifest.AppendLine(
                "attack\texpected_side\tactual_side\tphase\ttime_s\tdouble_attack\toffhand_double_attack\tanimator_clips");

            try
            {
                if (player == null || playerTransform == null ||
                    animator == null || GameData.PlayerCombat == null ||
                    inventory?.Empty == null || mainSlot == null ||
                    offSlot == null)
                {
                    manifest.AppendLine(
                        "setup\tFAIL\tplayer combat or humanoid animator unavailable");
                    yield break;
                }
                Item auditWrap = MonkGear.AllHandwraps.FirstOrDefault(value =>
                    value != null && value.ItemLevel >= 30) ??
                    MonkGear.AllHandwraps.FirstOrDefault();
                if (auditWrap == null)
                {
                    manifest.AppendLine(
                        "setup\tFAIL\tregistered handwrap catalog unavailable");
                    yield break;
                }
                mainSlot.MyItem = auditWrap;
                mainSlot.Quantity = 1;
                offSlot.MyItem = inventory.Empty;
                offSlot.Quantity = 1;
                mainSlot.UpdateSlotImage();
                offSlot.UpdateSlotImage();
                inventory.UpdatePlayerInventory();
                GameData.PlayerStats?.CalcStats();
                MonkHandwrapVisuals.Tick();
                HandToHandReadyIdle.Tick();
                manifest.AppendLine("Self-contained audit loadout: " +
                    auditWrap.ItemName + " (restored after run)");
                yield return null;
                MethodInfo performAttacks = typeof(PlayerCombat)
                    .GetMethods(BindingFlags.Instance | BindingFlags.Public |
                        BindingFlags.NonPublic)
                    .FirstOrDefault(value =>
                        value.Name == "PerformAttacks" &&
                        value.GetParameters().Length == 3);
                if (performAttacks == null)
                {
                    manifest.AppendLine(
                        "setup\tFAIL\tPlayerCombat.PerformAttacks was not found");
                    yield break;
                }

                GameData.PlayerCombat.ForceAttackOff();
                GameData.PlayerTyping = true;
                GameObject prefab = Resources.Load<GameObject>(
                    "NPCs/TestDummy");
                if (prefab == null)
                {
                    manifest.AppendLine(
                        "setup\tFAIL\tnative Resources/NPCs/TestDummy prefab unavailable");
                    yield break;
                }
                Vector3 captureDirection = sideView
                    ? playerTransform.right : playerTransform.forward;
                dummyObject = UnityEngine.Object.Instantiate(prefab,
                    playerTransform.position + captureDirection * 2.2f,
                    Quaternion.LookRotation(-captureDirection, Vector3.up));
                dummyObject.name = "CS Monk Autoattack Test Dummy";
                yield return null;
                yield return null;

                dummy = dummyObject.GetComponent<Character>();
                Stats dummyStats = dummyObject.GetComponent<Stats>();
                NPC dummyNpc = dummyObject.GetComponent<NPC>();
                TestDummy nativeRecorder =
                    dummyObject.GetComponent<TestDummy>();
                if (dummy == null || dummyStats == null || dummyNpc == null)
                {
                    manifest.AppendLine(
                        "setup\tFAIL\tnative test dummy components unavailable");
                    yield break;
                }
                if (nativeRecorder != null) nativeRecorder.enabled = false;
                dummyNpc.NPCName = "Monk Autoattack Test Dummy";
                dummyNpc.enabled = false;
                dummyNpc.Mobile = false;
                dummyNpc.NeverAggro = true;
                dummyNpc.HandSetResistances = true;
                dummyNpc.HardSetAC = 0;
                dummyNpc.CurrentAggroTarget = null;
                dummyNpc.AggroTable?.Clear();
                NavMeshAgent navigation =
                    dummyObject.GetComponent<NavMeshAgent>();
                if (navigation != null && navigation.enabled)
                    navigation.enabled = false;
                dummy.Alive = true;
                dummy.Invulnerable = false;
                dummy.DPSDummy = false;
                dummy.MyFaction = Character.Faction.OtherEvil;
                dummy.BaseFaction = Character.Faction.OtherEvil;
                dummy.TempFaction = Character.Faction.OtherEvil;
                dummy.NearbyEnemies?.Clear();
                dummy.NearbyEnemies?.Add(player);
                dummyStats.PlayerOrSimPlayer = false;
                dummyStats.Level = Mathf.Max(1,
                    GameData.PlayerStats?.Level ?? 35);
                dummyStats.CharacterClass = GameData.ClassDB.Default;
                dummyStats.OverrideHPforNPC = true;
                dummyStats.BaseHP = 5000000;
                dummyStats.BaseAC = 0;
                dummyStats.BaseMR = 0;
                dummyStats.BaseER = 0;
                dummyStats.BasePR = 0;
                dummyStats.BaseVR = 0;
                dummyStats.StopAllRegen = true;
                dummyStats.CalcStats();
                dummyStats.CurrentAC = 0;
                dummyStats.CurrentHP = dummyStats.CurrentMaxHP;
                EnsureCombatRange(dummy);
                GameData.PlayerControl.CurrentTarget = dummy;

                ChatHelper.Send(
                    "<color=#FFD54F>[Monk QA]</color> Real two-handed handwrap autoattack regression started: repeated primary-weapon calls will prove the visible right/left pattern at wind-up, contact, and recovery.");

                MonkAnimationDriver.ResetAutoPunchPattern();
                bool[] attackHands = { true, true, true };
                string[] attackLabels = { "main-1", "main-2", "main-3" };
                string[] expectedSides = { "right", "left", "right" };
                for (int attackIndex = 0;
                    attackIndex < attackHands.Length; attackIndex++)
                {
                    bool mainHand = attackHands[attackIndex];
                    string hand = attackLabels[attackIndex];
                    string expectedSide = expectedSides[attackIndex];
                    GameData.PlayerCombat.ForceAttackOff();
                    MonkRuntime.ResetCharacterState();
                    MonkRuntime.SuppressNativeWeaponAutoAnimation(animator);
                    dummyStats.CurrentHP = dummyStats.CurrentMaxHP;
                    EnsureCombatRange(dummy);

                    float duration =
                        MonkAnimationDriver.GetAutoDuration();
                    float impact =
                        MonkAnimationDriver.GetAutoImpactTime();
                    float[] frameTimes =
                    {
                        Mathf.Max(0.04f, impact * 0.52f),
                        impact + 0.015f,
                        Mathf.Min(duration - 0.03f,
                            impact + (duration - impact) * 0.55f)
                    };
                    string[] phases = { "windup", "contact", "recovery" };
                    var paths = new List<string>();
                    float started = Time.realtimeSinceStartup;
                    Exception invokeError = null;
                    try
                    {
                        performAttacks.Invoke(GameData.PlayerCombat,
                            new object[] { dummy, 2, mainHand });
                        invoked++;
                    }
                    catch (TargetInvocationException ex)
                    {
                        invokeError = ex.InnerException ?? ex;
                    }
                    catch (Exception ex)
                    {
                        invokeError = ex;
                    }
                    if (invokeError != null)
                    {
                        manifest.AppendLine($"{hand}\t{expectedSide}\t" +
                            "invoke-failed\tinvoke\tFAIL\t" +
                            Tsv(invokeError.GetType().Name + ": " +
                                invokeError.Message));
                        continue;
                    }
                    string actualSide =
                        MonkAnimationDriver.LastAutoPunchSide;
                    if (!string.Equals(actualSide, expectedSide,
                        StringComparison.Ordinal))
                        sideFailures++;

                    for (int frameIndex = 0;
                        frameIndex < frameTimes.Length; frameIndex++)
                    {
                        float waitFor = frameTimes[frameIndex] -
                            (Time.realtimeSinceStartup - started);
                        if (waitFor > 0f)
                            yield return new WaitForSecondsRealtime(waitFor);
                        bool doubleAttack = ReadAnimatorBool(animator,
                            "DoubleAttack");
                        bool offDoubleAttack = ReadAnimatorBool(animator,
                            "OHDoubleAttack");
                        if (doubleAttack || offDoubleAttack) boolLeaks++;
                        manifest.Append(hand).Append('\t')
                            .Append(expectedSide).Append('\t')
                            .Append(actualSide).Append('\t')
                            .Append(phases[frameIndex]).Append('\t')
                            .Append(frameTimes[frameIndex].ToString("0.000"))
                            .Append('\t').Append(doubleAttack)
                            .Append('\t').Append(offDoubleAttack)
                            .Append('\t')
                            .AppendLine(Tsv(DescribeAnimatorClips(animator)));
                        if (frameIndex == 1 &&
                            !RecordPoseMetrics(playerTransform,
                                "real_handwrap_auto_" + hand, manifest))
                            poseFailures++;
                        string path = Path.Combine(directory,
                            $"{attackIndex + 1:00}-{hand}-" +
                            $"{phases[frameIndex]}-" +
                            $"{frameTimes[frameIndex]:0.000}s.png");
                        paths.Add(path);
                        ScreenCapture.CaptureScreenshot(path);
                        // CaptureScreenshot writes at frame end. Yield once
                        // without waiting for disk so contact timing remains
                        // faithful to the live animation.
                        yield return null;
                    }
                    float remaining = duration -
                        (Time.realtimeSinceStartup - started) + 0.25f;
                    if (remaining > 0f)
                        yield return new WaitForSecondsRealtime(remaining);
                    if (!RecordRecoveryMetrics(playerTransform,
                        "real_handwrap_auto_" + hand, manifest))
                        continuityFailures++;
                    foreach (string path in paths)
                    {
                        int wait = 0;
                        while (!File.Exists(path) && wait++ < 180)
                            yield return null;
                        if (File.Exists(path)) captured++;
                    }
                }
            }
            finally
            {
                GameData.PlayerCombat?.ForceAttackOff();
                MonkRuntime.SuppressNativeWeaponAutoAnimation(animator);
                MonkRuntime.ResetCharacterState();
                GameData.PlayerControl?.Myself?.NearbyEnemies?.Remove(dummy);
                GameData.PlayerCombat?.MyMeleeRange?.GetNPCsInRange()?
                    .Remove(dummy);
                if (dummyObject != null)
                    UnityEngine.Object.Destroy(dummyObject);
                if (GameData.PlayerControl != null)
                    GameData.PlayerControl.CurrentTarget = originalTarget;
                GameData.PlayerTyping = originalTyping;
                if (mainSlot != null)
                {
                    mainSlot.MyItem = originalMain ?? inventory?.Empty;
                    mainSlot.Quantity = originalMainQuantity;
                    try { mainSlot.UpdateSlotImage(); } catch { }
                }
                if (offSlot != null)
                {
                    offSlot.MyItem = originalOff ?? inventory?.Empty;
                    offSlot.Quantity = originalOffQuantity;
                    try { offSlot.UpdateSlotImage(); } catch { }
                }
                try
                {
                    inventory?.UpdatePlayerInventory();
                    GameData.PlayerStats?.CalcStats();
                    MonkHandwrapVisuals.Tick();
                    HandToHandReadyIdle.Tick();
                }
                catch { }

                bool handCounts =
                    MonkAnimationDriver.AutoRightPlayCount == 2 &&
                    MonkAnimationDriver.AutoLeftPlayCount == 1;
                int autoImpacts = MonkAudio.AutoImpactRequestCount -
                    autoImpactsBefore;
                bool pass = invoked == 3 && captured == 9 &&
                    boolLeaks == 0 && poseFailures == 0 &&
                    continuityFailures == 0 && sideFailures == 0 &&
                    handCounts && autoImpacts == 3;
                manifest.AppendLine($"Invoked: {invoked}/3");
                manifest.AppendLine($"Captured: {captured}/9");
                manifest.AppendLine(
                    $"Native double-attack boolean leaks: {boolLeaks}");
                manifest.AppendLine(
                    $"Visible hand sequence failures: {sideFailures}");
                manifest.AppendLine(
                    "Visible hand counts: right=" +
                    MonkAnimationDriver.AutoRightPlayCount +
                    ", left=" + MonkAnimationDriver.AutoLeftPlayCount);
                manifest.AppendLine(
                    $"Weighted fist impact sounds: {autoImpacts}/3");
                manifest.AppendLine(
                    $"Pose metric failures: {poseFailures}");
                manifest.AppendLine(
                    $"Recovery continuity failures: {continuityFailures}");
                manifest.AppendLine($"Result: {(pass ? "PASS" : "FAIL")}");
                bool wrote = SafeFileIO.TryWriteAllTextAtomic(
                    Path.Combine(directory, "manifest.txt"),
                    manifest.ToString(), out string writeError);
                _autoattackRunning = false;
                SendResult("real handwrap autoattack",
                    pass && wrote,
                    $"native repeated-primary triple-attack calls={invoked}/3, " +
                    $"captures={captured}/9, hand-sequence failures=" +
                    $"{sideFailures}, boolean leaks={boolLeaks}, " +
                    $"weighted hit sounds={autoImpacts}/3, " +
                    $"pose failures={poseFailures}, recovery failures=" +
                    $"{continuityFailures}; " +
                    (wrote ? "report=" + directory :
                        "manifest write failed: " + writeError));
            }
        }

        private static bool ReadAnimatorBool(Animator animator,
            string name)
        {
            if (animator == null || string.IsNullOrEmpty(name)) return false;
            AnimatorControllerParameter parameter = animator.parameters
                .FirstOrDefault(value =>
                    value.type == AnimatorControllerParameterType.Bool &&
                    string.Equals(value.name, name,
                        StringComparison.Ordinal));
            return parameter != null && animator.GetBool(parameter.nameHash);
        }

        private static string DescribeAnimatorClips(Animator animator)
        {
            if (animator == null) return "animator unavailable";
            var names = new List<string>();
            try
            {
                foreach (AnimatorClipInfo info in
                    animator.GetCurrentAnimatorClipInfo(0))
                    if (info.clip != null) names.Add("current:" +
                        info.clip.name);
                foreach (AnimatorClipInfo info in
                    animator.GetNextAnimatorClipInfo(0))
                    if (info.clip != null) names.Add("next:" +
                        info.clip.name);
            }
            catch (Exception ex)
            {
                return ex.GetType().Name + ": " + ex.Message;
            }
            return names.Count == 0 ? "none" :
                string.Join(", ", names.Distinct().ToArray());
        }

        private static void StartMechanics(string query)
        {
            if (!RequireQaMonk()) return;
            if (_previewRunning || _animationRunning || _captureRunning ||
                _mechanicsRunning || _autoattackRunning)
            {
                ChatHelper.Send(
                    "<color=#FFD54F>[Monk QA]</color> A Monk QA sweep is already active.");
                return;
            }
            IReadOnlyList<MonkTechniqueDefinition> definitions;
            if (string.IsNullOrWhiteSpace(query) ||
                string.Equals(query.Trim(), "all",
                    StringComparison.OrdinalIgnoreCase))
            {
                definitions = MonkSkillData.All;
            }
            else
            {
                MonkTechniqueDefinition definition =
                    ResolveDefinition(query);
                if (definition == null) return;
                definitions = new[] { definition };
            }
            SkillsPlugin.Instance.StartCoroutine(RunMechanics(definitions));
        }

        private static IEnumerator RunMechanics(
            IReadOnlyList<MonkTechniqueDefinition> definitions)
        {
            _mechanicsRunning = true;
            Stats playerStats = GameData.PlayerStats;
            Character player = GameData.PlayerControl?.Myself;
            UseSkill skillUser = player?.MySkills;
            List<Skill> known = skillUser?.KnownSkills;
            GameObject dummyObject = null;
            Character dummy = null;
            Stats dummyStats = null;
            Character originalTarget = GameData.PlayerControl?.CurrentTarget;
            bool originalTyping = GameData.PlayerTyping;
            bool originalInvulnerable = player?.Invulnerable ?? false;
            bool originalStopRegen = playerStats?.StopAllRegen ?? false;
            bool originalStunned = playerStats?.Stunned ?? false;
            bool originalFeared = playerStats?.Feared ?? false;
            bool originalInvisible = playerStats?.Invisible ?? false;
            int originalLevel = playerStats?.Level ?? 1;
            int originalBaseHp = playerStats?.BaseHP ?? 0;
            int originalHp = playerStats?.CurrentHP ?? 1;
            int originalStamina = playerStats?.CurrentStamina ?? 0;
            Transform playerTransform = GameData.PlayerControl?.transform;
            Vector3 originalPlayerPosition = playerTransform?.position ??
                Vector3.zero;
            var temporarilyKnown = new List<Skill>();
            int passed = 0;
            int failed = 0;
            string directory = Path.Combine(RuntimePaths.DataDirectory,
                "TestReports", "MonkMechanics",
                DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") +
                "-John_EverQuest");
            var manifest = new StringBuilder();
            manifest.AppendLine(
                "level\ttechnique\tnative_call\tbehavior\tresult\tdetail");

            try
            {
                if (playerStats == null || player == null || skillUser == null ||
                    known == null)
                {
                    SendResult("mechanics sweep", false,
                        "native player skill state is unavailable");
                    yield break;
                }

                foreach (MonkTechniqueDefinition definition in definitions)
                {
                    Skill skill = MonkCatalog.GetSkill(definition.Key);
                    if (skill == null || known.Contains(skill)) continue;
                    known.Add(skill);
                    temporarilyKnown.Add(skill);
                }
                playerStats.Level = 35;
                playerStats.BaseHP = Mathf.Max(100000, playerStats.BaseHP);
                playerStats.StopAllRegen = true;
                playerStats.CalcStats();
                playerStats.CurrentHP = playerStats.CurrentMaxHP;
                player.Alive = true;
                player.Invulnerable = false;
                GameData.PlayerTyping = true;
                MonkRuntime.ResetCharacterState();
                ClearMonkTestEffects(playerStats);

                GameObject prefab = Resources.Load<GameObject>(
                    "NPCs/TestDummy");
                if (prefab == null)
                {
                    SendResult("mechanics sweep", false,
                        "native Resources/NPCs/TestDummy prefab is unavailable");
                    yield break;
                }
                dummyObject = UnityEngine.Object.Instantiate(prefab,
                    playerTransform.position + playerTransform.forward * 2.2f,
                    Quaternion.LookRotation(-playerTransform.forward,
                        Vector3.up));
                dummyObject.name = "CS Monk Mechanics Test Dummy";
                yield return null;
                yield return null;

                dummy = dummyObject.GetComponent<Character>();
                dummyStats = dummyObject.GetComponent<Stats>();
                NPC dummyNpc = dummyObject.GetComponent<NPC>();
                TestDummy nativeRecorder = dummyObject.GetComponent<TestDummy>();
                if (dummy == null || dummyStats == null || dummyNpc == null)
                {
                    SendResult("mechanics sweep", false,
                        "native training dummy lacks Character, Stats, or NPC");
                    yield break;
                }
                if (nativeRecorder != null) nativeRecorder.enabled = false;
                dummyNpc.enabled = false;
                dummyNpc.Mobile = false;
                dummyNpc.NeverAggro = true;
                dummyNpc.HandSetResistances = true;
                dummyNpc.HardSetAC = 0;
                dummyNpc.CurrentAggroTarget = null;
                dummyNpc.AggroTable?.Clear();
                var nav = dummyObject.GetComponent<
                    UnityEngine.AI.NavMeshAgent>();
                if (nav != null && nav.enabled) nav.enabled = false;
                dummy.Alive = true;
                dummy.Invulnerable = false;
                dummy.DPSDummy = false;
                dummy.MyFaction = Character.Faction.OtherEvil;
                dummy.BaseFaction = Character.Faction.OtherEvil;
                dummy.TempFaction = Character.Faction.OtherEvil;
                dummy.NearbyEnemies?.Clear();
                dummy.NearbyEnemies?.Add(player);
                dummyStats.PlayerOrSimPlayer = false;
                dummyStats.Level = 35;
                dummyStats.CharacterClass = GameData.ClassDB.Default;
                dummyStats.OverrideHPforNPC = true;
                dummyStats.BaseHP = 5000000;
                dummyStats.BaseAC = 0;
                dummyStats.BaseMR = 0;
                dummyStats.BaseER = 0;
                dummyStats.BasePR = 0;
                dummyStats.BaseVR = 0;
                dummyStats.StopAllRegen = true;
                dummyStats.CalcStats();
                dummyStats.CurrentAC = 0;
                dummyStats.CurrentHP = dummyStats.CurrentMaxHP;
                EnsureCombatRange(dummy);
                GameData.PlayerControl.CurrentTarget = dummy;

                ChatHelper.Send(
                    "<color=#FFD54F>[Monk QA]</color> Real mechanics " +
                    $"test started: {definitions.Count} technique" +
                    (definitions.Count == 1 ? "" : "s") +
                    " will use Erenshor's native skill pipeline against an " +
                    "isolated native test dummy.");

                foreach (MonkTechniqueDefinition definition in
                    definitions)
                {
                    Skill skill = MonkCatalog.GetSkill(definition.Key);
                    bool invoked = false;
                    bool behavior = false;
                    string detail = "";
                    int targetHpBefore = dummyStats.CurrentHP;
                    int playerHpBefore = playerStats.CurrentHP;
                    int counterDamage = 0;
                    float mainDelayBefore = -1f;
                    float offDelayBefore = -1f;
                    bool detrimentalInjected = false;
                    bool linkedEffectObserved = false;

                    ClearMonkTestEffects(playerStats);
                    ClearMonkTestEffects(dummyStats);
                    MonkRuntime.ResetCharacterState();
                    playerStats.Stunned = false;
                    playerStats.Feared = false;
                    playerStats.Invisible = false;
                    playerStats.CurrentHP = playerStats.CurrentMaxHP;
                    dummyStats.CurrentHP = dummyStats.CurrentMaxHP;
                    dummyStats.Stunned = false;
                    dummyStats.StunCooldown = 0f;
                    GameData.PlayerCombat?.ForceAttackOff();

                    PrepareMechanic(definition, playerStats, player,
                        ref mainDelayBefore, ref offDelayBefore,
                        ref detrimentalInjected);
                    targetHpBefore = dummyStats.CurrentHP;
                    playerHpBefore = playerStats.CurrentHP;

                    Exception callError = null;
                    if (definition.Kind == MonkTechniqueKind.Passive)
                    {
                        invoked = skill != null && known.Contains(skill);
                    }
                    else if (skill == null)
                    {
                        callError = new InvalidOperationException(
                            "registered Skill object is missing");
                    }
                    else
                    {
                        Spell linkedEffect = string.IsNullOrEmpty(
                            definition.EffectKey)
                                ? null
                                : MonkCatalog.GetEffect(definition.EffectKey);
                        try
                        {
                            invoked = skillUser.DoSkill(skill,
                                definition.IsOffensive ? dummy : player);
                            linkedEffectObserved = invoked &&
                                linkedEffect != null &&
                                definition.IsOffensive &&
                                dummyStats.CheckForStatus(linkedEffect);
                        }
                        catch (Exception ex)
                        {
                            callError = ex;
                        }
                    }

                    if (invoked && definition.Kind ==
                            MonkTechniqueKind.CounteringPalm)
                    {
                        int beforeCounter = dummyStats.CurrentHP;
                        try
                        {
                            player.DamageMe(100, true,
                                GameData.DamageType.Physical, dummy, true,
                                false);
                            counterDamage = Mathf.Max(0,
                                beforeCounter - dummyStats.CurrentHP);
                        }
                        catch (Exception ex)
                        {
                            callError = ex;
                        }
                    }

                    if (invoked && (definition.Kind ==
                            MonkTechniqueKind.CenteredBreath ||
                        definition.Kind ==
                            MonkTechniqueKind.BreathOfHarmony))
                        yield return new WaitForSecondsRealtime(3.25f);
                    else if (invoked && definition.IsOffensive)
                    {
                        yield return new WaitForSecondsRealtime(
                            definition.Kind == MonkTechniqueKind.StoneThrow
                                ? MonkAnimationDriver.GetEffectImpactTime(
                                    definition) + 0.18f
                                : definition.FollowUpHits > 0 ? 0.48f : 0.22f);
                        yield return new WaitForFixedUpdate();
                        yield return new WaitForFixedUpdate();
                    }
                    else
                        yield return null;

                    if (callError == null)
                    {
                        try
                        {
                            behavior = ValidateMechanic(definition, invoked,
                                playerStats, player, dummyStats,
                                targetHpBefore, playerHpBefore,
                                counterDamage, mainDelayBefore,
                                offDelayBefore, detrimentalInjected,
                                linkedEffectObserved, out detail);
                        }
                        catch (Exception ex)
                        {
                            detail = ex.GetType().Name + ": " + ex.Message;
                            behavior = false;
                        }
                    }
                    else
                    {
                        detail = callError.GetType().Name + ": " +
                            callError.Message;
                    }

                    bool result = invoked && behavior;
                    if (result) passed++; else failed++;
                    SkillsPlugin.Log?.LogInfo(
                        $"[Monk Mechanics QA] L{definition.Level:00} " +
                        $"{definition.Name}: invoked={invoked}, " +
                        $"behavior={behavior}, detail={detail}");
                    manifest.Append(definition.Level).Append('\t')
                        .Append(Tsv(definition.Name)).Append('\t')
                        .Append(invoked ? "PASS" : "FAIL").Append('\t')
                        .Append(behavior ? "PASS" : "FAIL").Append('\t')
                        .Append(result ? "PASS" : "FAIL").Append('\t')
                        .AppendLine(Tsv(detail));

                    ClearMonkTestEffects(playerStats);
                    ClearMonkTestEffects(dummyStats);
                    MonkRuntime.ResetCharacterState();
                    playerStats.Stunned = false;
                    playerStats.Feared = false;
                    playerStats.Invisible = false;
                    playerStats.ResetMHAtkDelay();
                    playerStats.ResetOHAtkDelay();
                    GameData.PlayerCombat?.ForceAttackOff();
                    yield return null;
                }

                string manifestPath = Path.Combine(directory, "manifest.tsv");
                bool wrote = SafeFileIO.TryWriteAllTextAtomic(manifestPath,
                    manifest.ToString(), out string writeError);
                bool complete = failed == 0 &&
                    passed == definitions.Count && wrote;
                SendResult("mechanics sweep", complete,
                    $"{passed}/{definitions.Count} passed, " +
                    $"{failed} failed; manifest=" +
                    (wrote ? manifestPath : "write failed: " + writeError));
            }
            finally
            {
                ClearMonkTestEffects(playerStats);
                ClearMonkTestEffects(dummyStats);
                MonkRuntime.ResetCharacterState();
                GameData.PlayerCombat?.ForceAttackOff();
                GameData.PlayerControl?.Myself?.NearbyEnemies?.Remove(dummy);
                GameData.PlayerCombat?.MyMeleeRange?.GetNPCsInRange()?
                    .Remove(dummy);
                if (dummyObject != null)
                    UnityEngine.Object.Destroy(dummyObject);
                if (known != null)
                    foreach (Skill skill in temporarilyKnown)
                        known.Remove(skill);
                if (playerStats != null)
                {
                    playerStats.Level = originalLevel;
                    playerStats.BaseHP = originalBaseHp;
                    playerStats.StopAllRegen = originalStopRegen;
                    playerStats.Stunned = originalStunned;
                    playerStats.Feared = originalFeared;
                    playerStats.Invisible = originalInvisible;
                    playerStats.CalcStats();
                    playerStats.CurrentHP = Mathf.Clamp(originalHp, 1,
                        playerStats.CurrentMaxHP);
                    playerStats.CurrentStamina = Mathf.Clamp(originalStamina,
                        0, playerStats.CurrentMaxStamina);
                    playerStats.ResetMHAtkDelay();
                    playerStats.ResetOHAtkDelay();
                }
                if (player != null) player.Invulnerable = originalInvulnerable;
                if (GameData.PlayerControl != null)
                    GameData.PlayerControl.CurrentTarget = originalTarget;
                RestoreQaPlayerPosition(playerTransform,
                    originalPlayerPosition);
                GameData.PlayerTyping = originalTyping;
                _mechanicsRunning = false;
            }
        }

        private static void RestoreQaPlayerPosition(Transform player,
            Vector3 position)
        {
            if (player == null) return;
            CharacterController controller =
                player.GetComponent<CharacterController>() ??
                player.GetComponentInParent<CharacterController>();
            bool wasEnabled = controller != null && controller.enabled;
            if (wasEnabled) controller.enabled = false;
            player.position = position;
            if (wasEnabled) controller.enabled = true;
        }

        private static void PrepareMechanic(
            MonkTechniqueDefinition definition, Stats stats,
            Character player, ref float mainDelayBefore,
            ref float offDelayBefore, ref bool detrimentalInjected)
        {
            switch (definition.Kind)
            {
                case MonkTechniqueKind.Mend:
                case MonkTechniqueKind.CenteredBreath:
                case MonkTechniqueKind.BreathOfHarmony:
                    stats.CurrentHP = Mathf.Max(1,
                        stats.CurrentMaxHP / 2);
                    break;
                case MonkTechniqueKind.FocusedWill:
                    stats.Stunned = true;
                    stats.Feared = true;
                    break;
                case MonkTechniqueKind.PurifyBody:
                {
                    Spell detrimental = MonkCatalog.GetEffect(
                        "crescent_heel");
                    int slot = stats.AddStatusEffect(detrimental, true, 0,
                        player);
                    if (slot >= 0 && slot < stats.StatusEffects.Length)
                    {
                        stats.StatusEffects[slot].Duration = 10f;
                        detrimentalInjected = true;
                    }
                    break;
                }
                case MonkTechniqueKind.HundredFists:
                    stats.ResetMHAtkDelay();
                    stats.ResetOHAtkDelay();
                    mainDelayBefore = MonkRuntime.ReadAttackDelayForTesting(
                        stats, true);
                    offDelayBefore = MonkRuntime.ReadAttackDelayForTesting(
                        stats, false);
                    break;
            }
        }

        private static bool ValidateMechanic(
            MonkTechniqueDefinition definition, bool invoked, Stats stats,
            Character player, Stats dummy, int targetHpBefore,
            int playerHpBefore, int counterDamage, float mainDelayBefore,
            float offDelayBefore, bool detrimentalInjected,
            bool linkedEffectObserved,
            out string detail)
        {
            if (!invoked)
            {
                detail = "native skill call was rejected";
                return false;
            }
            if (definition.IsOffensive)
            {
                int damage = Mathf.Max(0, targetHpBefore - dummy.CurrentHP);
                bool effect = string.IsNullOrEmpty(definition.EffectKey) ||
                    linkedEffectObserved ||
                    dummy.CheckForStatus(MonkCatalog.GetEffect(
                        definition.EffectKey));
                if (string.Equals(definition.Key, "jab",
                        StringComparison.Ordinal))
                {
                    int auto = MonkRuntime.LastJabAutoDamage;
                    int bonus = MonkRuntime.LastJabBonusDamage;
                    int composite = MonkRuntime.LastJabCompositeDamage;
                    bool stacked = auto > 0 && bonus > 0 &&
                        composite > auto;
                    detail = $"real native damage={damage}; jab auto=" +
                        $"{auto}, bonus={bonus}, composite={composite}, " +
                        $"stacked={stacked}; linked effect=" +
                        (string.IsNullOrEmpty(definition.EffectKey)
                            ? "n/a" : effect ? "applied" : "missing");
                    return damage > 0 && effect && stacked;
                }
                detail = $"real native damage={damage}; linked effect=" +
                    (string.IsNullOrEmpty(definition.EffectKey)
                        ? "n/a" : effect ? "applied" : "missing");
                return damage > 0 && effect;
            }

            switch (definition.Kind)
            {
                case MonkTechniqueKind.Passive:
                {
                    bool active = MonkRuntime.HasOpenHandDiscipline();
                    detail = "known passive and handwrap modifier=" + active;
                    return active;
                }
                case MonkTechniqueKind.Mend:
                {
                    int healing = Mathf.Max(0,
                        stats.CurrentHP - playerHpBefore);
                    detail = "healing=" + healing;
                    return healing > 0;
                }
                case MonkTechniqueKind.FocusedWill:
                    detail = $"stunned={stats.Stunned}, feared={stats.Feared}";
                    return !stats.Stunned && !stats.Feared;
                case MonkTechniqueKind.CenteredBreath:
                case MonkTechniqueKind.BreathOfHarmony:
                {
                    int healing = Mathf.Max(0,
                        stats.CurrentHP - playerHpBefore);
                    bool status = stats.CheckForStatus(MonkCatalog.GetEffect(
                        definition.Key));
                    detail = $"first live regen tick={healing}; status={status}";
                    return healing > 0 && status;
                }
                case MonkTechniqueKind.FeignDeath:
                    detail = "feign state=" + MonkRuntime.IsFeigning;
                    return MonkRuntime.IsFeigning;
                case MonkTechniqueKind.CounteringPalm:
                    detail = $"counter damage={counterDamage}; consumed=" +
                        (!MonkRuntime.CounteringPalmActive);
                    return counterDamage > 0 &&
                        !MonkRuntime.CounteringPalmActive;
                case MonkTechniqueKind.SilentStep:
                {
                    bool status = stats.CheckForStatus(
                        MonkCatalog.GetEffect("silent_step"));
                    detail = $"status={status}; invisible={stats.Invisible}";
                    return status && stats.Invisible;
                }
                case MonkTechniqueKind.ReedInTheWind:
                {
                    int physical = MonkRuntime.ModifyIncomingDamage(100,
                        player, GameData.DamageType.Physical, null);
                    detail = $"physical test damage={physical}/100";
                    return MonkRuntime.ReedInTheWindActive && physical == 85;
                }
                case MonkTechniqueKind.StoneStance:
                {
                    int rank = MonkAscensions.GetPlayerRank(
                        MonkAscensions.FlowingDefenseId);
                    float reduction = Mathf.Clamp(0.25f + 0.03f * rank,
                        0f, 0.34f);
                    int expected = Mathf.Max(1, Mathf.RoundToInt(
                        100f * (1f - reduction)));
                    int physical = MonkRuntime.ModifyIncomingDamage(100,
                        player, GameData.DamageType.Physical, null);
                    detail = $"all-damage test={physical}/100, expected=" +
                        expected;
                    return MonkRuntime.StoneStanceActive &&
                        physical == expected;
                }
                case MonkTechniqueKind.EmptyBody:
                {
                    int voidDamage = MonkRuntime.ModifyIncomingDamage(100,
                        player, GameData.DamageType.Void, null);
                    int physical = MonkRuntime.ModifyIncomingDamage(100,
                        player, GameData.DamageType.Physical, null);
                    detail = $"void={voidDamage}/100; physical=" + physical;
                    return MonkRuntime.EmptyBodyActive && voidDamage == 75 &&
                        physical == 100;
                }
                case MonkTechniqueKind.PurifyBody:
                {
                    bool remains = stats.CheckForStatus(
                        MonkCatalog.GetEffect("crescent_heel"));
                    detail = $"detrimental injected={detrimentalInjected}; " +
                        "remains=" + remains;
                    return detrimentalInjected && !remains;
                }
                case MonkTechniqueKind.HundredFists:
                {
                    float main = MonkRuntime.ReadAttackDelayForTesting(stats,
                        true);
                    float off = MonkRuntime.ReadAttackDelayForTesting(stats,
                        false);
                    bool reduced = mainDelayBefore > 0f &&
                        offDelayBefore > 0f && main > 0f && off > 0f &&
                        main <= mainDelayBefore * 0.751f &&
                        off <= offDelayBefore * 0.751f;
                    detail = $"MH {mainDelayBefore:0.##}->{main:0.##}; " +
                        $"OH {offDelayBefore:0.##}->{off:0.##}";
                    return MonkRuntime.HundredFistsActive && reduced;
                }
                case MonkTechniqueKind.FivePointFocus:
                {
                    int rank = MonkAscensions.GetPlayerRank(
                        MonkAscensions.WayOfIronId);
                    int expected = Mathf.Max(1, Mathf.RoundToInt(
                        100f * MonkRuntime.GetTechniqueBalanceMultiplier(
                            stats.Level) *
                        (1f + 0.15f * rank) * 1.15f *
                        Skills.CombatSkills.GetDamageMultiplier(
                            Skills.CombatSkills.WeaponType.HandToHand)));
                    int boosted = MonkRuntime
                        .PreviewTechniqueDamageForTesting(100);
                    detail = $"technique test damage={boosted}/100, " +
                        "expected=" + expected;
                    return MonkRuntime.FivePointFocusActive &&
                        boosted == expected;
                }
                default:
                    detail = "no behavior validator for " + definition.Kind;
                    return false;
            }
        }

        private static void EnsureCombatRange(Character target)
        {
            if (target == null) return;
            List<Character> melee = GameData.PlayerCombat?.MyMeleeRange?
                .GetNPCsInRange();
            if (melee != null && !melee.Contains(target)) melee.Add(target);
            List<Character> nearby = GameData.PlayerControl?.Myself?
                .NearbyEnemies;
            if (nearby != null && !nearby.Contains(target)) nearby.Add(target);
            if (target.NearbyEnemies != null &&
                !target.NearbyEnemies.Contains(GameData.PlayerControl.Myself))
                target.NearbyEnemies.Add(GameData.PlayerControl.Myself);
        }

        private static void ClearMonkTestEffects(Stats stats)
        {
            if (stats?.StatusEffects == null) return;
            for (int index = stats.StatusEffects.Length - 1;
                index >= 0; index--)
            {
                string id = stats.StatusEffects[index]?.Effect?.Id;
                if (!string.IsNullOrEmpty(id) && id.StartsWith(
                        "CS_MONK_EFFECT_",
                        StringComparison.OrdinalIgnoreCase))
                    stats.RemoveStatusEffect(index);
            }
        }

        private static string Tsv(string value) => (value ?? "")
            .Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

        private static void TestGear(string text)
        {
            if (!RequireQaMonk()) return;
            bool verify = string.Equals((text ?? "").Trim(), "verify",
                StringComparison.OrdinalIgnoreCase);
            bool pass = verify
                ? MonkGear.ValidateEquippedPersistence(out string detail)
                : MonkGear.ExerciseEquipmentAndSave(out detail);
            SendResult(verify ? "new gear reload" : "new gear equip/save",
                pass, detail);
        }

        private static void StartHandwrapCapture()
        {
            if (!RequireQaMonk()) return;
            if (_previewRunning || _animationRunning || _captureRunning ||
                _mechanicsRunning || _autoattackRunning)
            {
                ChatHelper.Send(
                    "<color=#FFD54F>[Monk QA]</color> Another Monk visual preview is already active.");
                return;
            }
            SkillsPlugin.Instance.StartCoroutine(CaptureAllHandwraps());
        }

        private static IEnumerator CaptureAllHandwraps()
        {
            _captureRunning = true;
            Inventory inventory = GameData.PlayerInv;
            ItemIcon main = inventory?.MH;
            ItemIcon off = inventory?.OH;
            Item originalMain = main?.MyItem;
            Item originalOff = off?.MyItem;
            int originalMainQuantity = main?.Quantity ?? 0;
            int originalOffQuantity = off?.Quantity ?? 0;
            bool originalAutoattacking = GameData.Autoattacking;
            bool originalTyping = GameData.PlayerTyping;
            string directory = Path.Combine(RuntimePaths.DataDirectory,
                "TestReports", "MonkHandwrapModels",
                DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") +
                "-John_EverQuest");
            Directory.CreateDirectory(directory);
            var manifest = new StringBuilder();
            manifest.AppendLine(
                "index\titem\tlevel\tacquisition\tpose\thand\tview\tresult\tfile_or_detail");
            string[] views =
                { "dorsal", "palm", "outer", "inner", "knuckle", "wrist" };
            Item[] wraps = MonkGear.AllHandwraps.ToArray();
            int expectedPerWrap = 1 + views.Length * 2 + 4 + 2;
            int expected = wraps.Length * expectedPerWrap;
            int captured = 0;
            int fitFailures = 0;
            int motionFailures = 0;
            int deformationFailures = 0;
            GameObject target = null;
            try
            {
                if (GameData.PlayerControl == null)
                    yield break;
                GameData.PlayerTyping = true;
                GameData.Autoattacking = true;
                Transform player = GameData.PlayerControl.transform;
                target = new GameObject("CS Handwrap Motion Audit Target");
                target.hideFlags = HideFlags.HideAndDontSave;
                target.transform.position = player.position +
                    player.forward * 2.2f + Vector3.up * 1.0f;
                bool sourceCoverage =
                    MonkHandwrapVisuals.ValidateQaSourceCoverage(
                        out string sourceCoverageDetail);
                if (!sourceCoverage) fitFailures++;
                if (wraps.Length > 0)
                    AppendHandwrapAuditRow(manifest, -1, wraps[0],
                        "runtime", "setup", "pair", "source-coverage",
                        sourceCoverage, sourceCoverageDetail);
                for (int index = 0; index < wraps.Length; index++)
                {
                    Item wrap = wraps[index];
                    // Equipment UI slots are not guaranteed to exist when the
                    // inventory window is closed. Preview the exact catalog
                    // tier directly without mutating the saved loadout.
                    MonkHandwrapVisuals.SetQaPreviewTier(index);
                    HandToHandReadyIdle.Tick();
                    yield return new WaitForSecondsRealtime(0.34f);
                    MonkHandwrapVisuals.Tick();
                    yield return new WaitForEndOfFrame();
                    string acquisition = MonkGear.IsCrafted(wrap)
                        ? "Tailoring" : MonkGear.IsBossDrop(wrap)
                            ? "Boss-only" : "Trainer/dungeon";
                    string prefix = $"{index + 1:00}-L{wrap.ItemLevel:00}-" +
                        SafeFileName(wrap.ItemName);

                    bool fitted = MonkHandwrapVisuals.ValidateQaPreview(index,
                        out string fitDetail);
                    if (!fitted) fitFailures++;
                    AppendHandwrapAuditRow(manifest, index, wrap,
                        acquisition, "guard", "pair", "fit-check",
                        fitted, fitDetail);

                    string guardFile = prefix + "-guard-full.png";
                    bool guardCaptured = CapturePlayerModel(
                        Path.Combine(directory, guardFile));
                    if (guardCaptured) captured++;
                    AppendHandwrapAuditRow(manifest, index, wrap,
                        acquisition, "guard", "pair", "full",
                        guardCaptured, guardFile);

                    foreach (bool left in new[] { false, true })
                    {
                        string hand = left ? "left" : "right";
                        foreach (string view in views)
                        {
                            string file = prefix + "-guard-" + hand + "-" +
                                view + ".png";
                            bool ok = CaptureHandwrapView(
                                Path.Combine(directory, file), left, view);
                            if (ok) captured++;
                            AppendHandwrapAuditRow(manifest, index, wrap,
                                acquisition, "guard", hand, view, ok, file);
                        }
                    }

                    MonkAnimationDriver.ResetAutoPunchPattern();
                    for (int strike = 0; strike < 2; strike++)
                    {
                        bool left = strike == 1;
                        string hand = left ? "left" : "right";
                        bool played = MonkAnimationDriver.PlayAutoPunch(
                            player, target.transform, true);
                        if (!played)
                        {
                            motionFailures++;
                            AppendHandwrapAuditRow(manifest, index, wrap,
                                acquisition, "contact", hand, "motion",
                                false, "authored auto-punch did not start");
                            continue;
                        }
                        yield return new WaitForSecondsRealtime(
                            MonkAnimationDriver.GetAutoImpactTime() + 0.015f);
                        bool contactDeformation =
                            MonkHandwrapVisuals.ValidateQaPreview(index,
                                out string contactDetail);
                        if (!contactDeformation) deformationFailures++;
                        AppendHandwrapAuditRow(manifest, index, wrap,
                            acquisition, "contact", hand,
                            "deformation-check", contactDeformation,
                            contactDetail);
                        foreach (string view in new[] { "dorsal", "outer" })
                        {
                            string file = prefix + "-contact-" + hand + "-" +
                                view + ".png";
                            bool ok = CaptureHandwrapView(
                                Path.Combine(directory, file), left, view);
                            if (ok) captured++;
                            AppendHandwrapAuditRow(manifest, index, wrap,
                                acquisition, "contact", hand, view, ok, file);
                        }
                        float recovery = MonkAnimationDriver.GetAutoDuration() -
                            MonkAnimationDriver.GetAutoImpactTime() + 0.08f;
                        yield return new WaitForSecondsRealtime(recovery);
                        string recoveryFile = prefix + "-recovery-" + hand +
                            "-dorsal.png";
                        bool recoveryCaptured = CaptureHandwrapView(
                            Path.Combine(directory, recoveryFile), left,
                            "dorsal");
                        if (recoveryCaptured) captured++;
                        AppendHandwrapAuditRow(manifest, index, wrap,
                            acquisition, "recovery", hand, "dorsal",
                            recoveryCaptured, recoveryFile);
                    }
                    yield return null;
                }
                File.WriteAllText(Path.Combine(directory, "manifest.tsv"),
                    manifest.ToString());
            }
            finally
            {
                if (target != null) UnityEngine.Object.Destroy(target);
                MonkRuntime.ResetCharacterState();
                MonkHandwrapVisuals.SetQaPreviewTier(-1);
                if (main != null)
                {
                    main.MyItem = originalMain ?? inventory.Empty;
                    main.Quantity = originalMainQuantity;
                }
                if (off != null)
                {
                    off.MyItem = originalOff ?? inventory.Empty;
                    off.Quantity = originalOffQuantity;
                }
                GameData.Autoattacking = originalAutoattacking;
                GameData.PlayerTyping = originalTyping;
                try { GameData.PlayerStats?.CalcStats(); } catch { }
                MonkHandwrapVisuals.Tick();
                HandToHandReadyIdle.Tick();
                _captureRunning = false;
                bool pass = captured == expected && fitFailures == 0 &&
                    motionFailures == 0 && deformationFailures == 0;
                SendResult("handwrap model catalog",
                    pass,
                    $"captured {captured}/{expected} full-body, six-angle " +
                    $"per-hand guard/contact/recovery renders; fit failures=" +
                    $"{fitFailures}, motion failures={motionFailures}; " +
                    $"deformation failures={deformationFailures}; " +
                    "report=" + directory);
            }
        }

        private static void AppendHandwrapAuditRow(StringBuilder manifest,
            int index, Item wrap, string acquisition, string pose,
            string hand, string view, bool pass, string detail)
        {
            manifest.Append(index + 1).Append('\t')
                .Append(Tsv(wrap?.ItemName ?? "missing")).Append('\t')
                .Append(wrap?.ItemLevel ?? 0).Append('\t')
                .Append(acquisition).Append('\t').Append(pose).Append('\t')
                .Append(hand).Append('\t').Append(view).Append('\t')
                .Append(pass ? "PASS" : "FAIL").Append('\t')
                .AppendLine(Tsv(detail));
        }

        private static bool CaptureHandwrapView(string path, bool left,
            string view)
        {
            Camera camera = ResolveQaCamera();
            if (camera == null || !MonkHandwrapVisuals.TryGetQaHandFrame(
                    left, out Transform frame))
                return false;
            Vector3 direction;
            Vector3 cameraUp = frame.up;
            if (string.Equals(view, "palm", StringComparison.Ordinal))
                direction = -frame.forward;
            else if (string.Equals(view, "outer", StringComparison.Ordinal))
                direction = left ? -frame.right : frame.right;
            else if (string.Equals(view, "inner", StringComparison.Ordinal))
                direction = left ? frame.right : -frame.right;
            else if (string.Equals(view, "knuckle", StringComparison.Ordinal))
            {
                direction = -frame.up;
                cameraUp = frame.forward;
            }
            else if (string.Equals(view, "wrist", StringComparison.Ordinal))
            {
                direction = frame.up;
                cameraUp = frame.forward;
            }
            else direction = frame.forward;
            Dictionary<Renderer, bool> visibility =
                IsolateHandForCapture(left);
            try
            {
                return CaptureFocusedModel(path, camera, frame.position,
                    direction, cameraUp, 0.50f, 31f);
            }
            finally
            {
                foreach (KeyValuePair<Renderer, bool> entry in visibility)
                    if (entry.Key != null) entry.Key.enabled = entry.Value;
            }
        }

        private static Dictionary<Renderer, bool> IsolateHandForCapture(
            bool left)
        {
            var visibility = new Dictionary<Renderer, bool>();
            Transform player = GameData.PlayerControl?.transform;
            if (player == null) return visibility;
            string handMarker = left ? "HAND_LEFT" : "HAND_RIGHT";
            string forearmMarker = left
                ? "ARM_LOWER_LEFT" : "ARM_LOWER_RIGHT";
            foreach (Renderer renderer in
                player.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null) continue;
                visibility[renderer] = renderer.enabled;
                if (!renderer.enabled) continue;
                string hierarchy = RendererPath(renderer.transform, player)
                    .ToUpperInvariant();
                bool keep = hierarchy.IndexOf("CS_",
                        StringComparison.Ordinal) >= 0 ||
                    hierarchy.IndexOf(handMarker,
                        StringComparison.Ordinal) >= 0 ||
                    hierarchy.IndexOf(forearmMarker,
                        StringComparison.Ordinal) >= 0;
                renderer.enabled = keep;
            }
            return visibility;
        }

        private static string RendererPath(Transform value, Transform root)
        {
            string path = value?.name ?? "";
            for (Transform parent = value?.parent;
                parent != null && parent != root; parent = parent.parent)
                path = parent.name + "/" + path;
            return path;
        }

        private static bool CapturePlayerModel(string path)
        {
            Camera camera = ResolveQaCamera();
            Transform player = GameData.PlayerControl?.transform;
            if (camera == null || player == null) return false;
            Vector3 originalPosition = camera.transform.position;
            Quaternion originalRotation = camera.transform.rotation;
            float originalFov = camera.fieldOfView;
            RenderTexture originalTarget = camera.targetTexture;
            RenderTexture originalActive = RenderTexture.active;
            var texture = new RenderTexture(768, 768, 24,
                RenderTextureFormat.ARGB32);
            var image = new Texture2D(768, 768, TextureFormat.RGB24, false);
            try
            {
                Vector3 target = player.position + Vector3.up * 1.18f;
                camera.transform.position = player.position +
                    player.forward * 2.15f + Vector3.up * 1.28f +
                    player.right * 0.30f;
                camera.transform.rotation = Quaternion.LookRotation(
                    target - camera.transform.position, Vector3.up);
                camera.fieldOfView = 41f;
                camera.targetTexture = texture;
                camera.Render();
                RenderTexture.active = texture;
                image.ReadPixels(new Rect(0, 0, 768, 768), 0, 0);
                image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
                return File.Exists(path) && new FileInfo(path).Length > 0;
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogError(
                    "Handwrap player-model capture failed: " + ex);
                return false;
            }
            finally
            {
                camera.targetTexture = originalTarget;
                camera.transform.position = originalPosition;
                camera.transform.rotation = originalRotation;
                camera.fieldOfView = originalFov;
                RenderTexture.active = originalActive;
                UnityEngine.Object.Destroy(texture);
                UnityEngine.Object.Destroy(image);
            }
        }

        private static bool CaptureFocusedModel(string path, Camera camera,
            Vector3 target, Vector3 direction, Vector3 up, float distance,
            float fieldOfView)
        {
            if (camera == null || direction.sqrMagnitude < 0.0001f ||
                up.sqrMagnitude < 0.0001f) return false;
            Vector3 originalPosition = camera.transform.position;
            Quaternion originalRotation = camera.transform.rotation;
            float originalFov = camera.fieldOfView;
            RenderTexture originalTarget = camera.targetTexture;
            RenderTexture originalActive = RenderTexture.active;
            var texture = new RenderTexture(768, 768, 24,
                RenderTextureFormat.ARGB32);
            var image = new Texture2D(768, 768, TextureFormat.RGB24, false);
            try
            {
                Vector3 normalizedDirection = direction.normalized;
                Vector3 normalizedUp = up.normalized;
                if (Mathf.Abs(Vector3.Dot(normalizedDirection,
                        normalizedUp)) > 0.96f)
                    normalizedUp = Vector3.Cross(normalizedDirection,
                        Vector3.right).normalized;
                camera.transform.position = target +
                    normalizedDirection * distance;
                camera.transform.rotation = Quaternion.LookRotation(
                    target - camera.transform.position, normalizedUp);
                camera.fieldOfView = fieldOfView;
                camera.targetTexture = texture;
                camera.Render();
                RenderTexture.active = texture;
                image.ReadPixels(new Rect(0, 0, 768, 768), 0, 0);
                image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
                return File.Exists(path) && new FileInfo(path).Length > 0;
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogError(
                    "Focused handwrap capture failed: " + ex);
                return false;
            }
            finally
            {
                camera.targetTexture = originalTarget;
                camera.transform.position = originalPosition;
                camera.transform.rotation = originalRotation;
                camera.fieldOfView = originalFov;
                RenderTexture.active = originalActive;
                UnityEngine.Object.Destroy(texture);
                UnityEngine.Object.Destroy(image);
            }
        }

        private static Camera ResolveQaCamera()
        {
            try
            {
                Camera gameplay = GameData.CamControl == null ? null :
                    GameData.CamControl.GetComponentInChildren<Camera>();
                if (gameplay != null) return gameplay;
            }
            catch { }
            Camera camera = Camera.main;
            if (camera != null) return camera;
            return UnityEngine.Object.FindObjectsOfType<Camera>()
                .FirstOrDefault(candidate => candidate != null &&
                    candidate.enabled && candidate.gameObject.activeInHierarchy);
        }

        private static string SafeFileName(string value)
        {
            return new string((value ?? "handwrap").Select(character =>
                char.IsLetterOrDigit(character) ? character : '_').ToArray())
                .Trim('_');
        }

        private static void TestAuras(string text)
        {
            if (!RequireQaMonk()) return;
            bool verify = string.Equals((text ?? "").Trim(), "verify",
                StringComparison.OrdinalIgnoreCase);
            bool pass = verify
                ? MonkAuras.ValidateEquippedPersistence(out string detail)
                : MonkAuras.ExerciseEquipmentAndSave(out detail);
            SendResult(verify ? "aura reload" : "aura equip/save", pass,
                detail);
        }

        private static void TestAscensions()
        {
            if (!RequireQaMonk()) return;
            bool pass = MonkAscensions.Validate(out string detail);
            SendResult("ascensions", pass, detail);
        }

        private static void TestVendor(string text)
        {
            if (!RequireWorld(false)) return;
            if (string.Equals((text ?? "").Trim(), "capture",
                StringComparison.OrdinalIgnoreCase))
            {
                bool started = Testing.AddonTestMode.TryCaptureMonkVendor(
                    out string captureDetail);
                SendResult("vendor capture", started, captureDetail);
                return;
            }
            MonkVendorMarker marker = UnityEngine.Object
                .FindObjectOfType<MonkVendorMarker>();
            VendorInventory vendor = marker?.GetComponent<VendorInventory>();
            if (string.Equals((text ?? "").Trim(), "open",
                StringComparison.OrdinalIgnoreCase))
            {
                if (marker == null || vendor == null ||
                    GameData.VendorWindow == null)
                {
                    SendResult("vendor open", false,
                        "the current scene's Monk trainer is not ready");
                    return;
                }
                CharacterController controller = GameData.PlayerControl
                    .GetComponent<CharacterController>();
                if (controller != null) controller.enabled = false;
                GameData.PlayerControl.transform.position =
                    marker.transform.position - marker.transform.forward * 2f;
                if (controller != null) controller.enabled = true;
                GameData.VendorWindow.Page = 0;
                GameData.VendorWindow.LoadWindow(vendor.ItemsForSale, vendor);
            }
            bool pass = MonkVendors.ValidateCurrentSceneVendor(
                out string detail);
            SendResult("vendor", pass, detail);
        }

        private static void StartSmoke()
        {
            if (!RequireQaMonk()) return;
            bool catalog = MonkCatalog.ValidateCatalog(out string catalogText);
            bool balance = MonkCatalog.ValidateBalance(out string balanceText);
            bool vfx = MonkVfx.Validate(out string vfxText);
            bool pass = catalog && balance && vfx;
            SendResult("targeted catalog smoke", pass,
                $"catalog={catalogText}; balance={balanceText}; VFX={vfxText}. " +
                "This scope covers only new Monk content; run monk gear and " +
                "monk aura for their native save/reload exercises.");
            if (pass) StartVfx("all");
        }

        private static MonkTechniqueDefinition ResolveDefinition(string text)
        {
            string query = (text ?? "").Trim();
            MonkTechniqueDefinition exact = MonkSkillData.All.FirstOrDefault(
                value => string.Equals(value.Key, query,
                    StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(value.Name, query,
                        StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;
            List<MonkTechniqueDefinition> matches = MonkSkillData.All
                .Where(value => value.Name.IndexOf(query,
                    StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();
            if (matches.Count == 1) return matches[0];
            ChatHelper.Send(
                matches.Count == 0
                    ? $"<color=#EF5350>[Monk QA]</color> No technique matches '{query}'."
                    : $"<color=#EF5350>[Monk QA]</color> '{query}' is ambiguous: " +
                      string.Join(", ", matches.Select(value => value.Name)));
            return null;
        }

        private static bool RequireQaMonk()
        {
            if (!RequireQaCharacter()) return false;
            if (MonkCatalog.IsMonkPlayer()) return true;
            ChatHelper.Send(
                "<color=#EF5350>[Monk QA]</color> John EverQuest must be a Monk. Run /cstest monk class.");
            return false;
        }

        private static bool RequireQaCharacter()
        {
            if (!RequireWorld(true)) return false;
            string name = GameData.PlayerStats?.MyName ?? "";
            if (string.Equals(name, "John EverQuest",
                StringComparison.Ordinal)) return true;
            ChatHelper.Send(
                $"<color=#EF5350>[Monk QA]</color> Mutating Monk tests are " +
                $"restricted to John EverQuest; current character is " +
                $"'{name}'.");
            return false;
        }

        private static bool RequireWorld(bool requireSave)
        {
            bool ready = GameData.PlayerControl?.Myself != null &&
                GameData.PlayerStats != null &&
                (!requireSave || (GameData.CurrentCharacterSlot != null &&
                    GameData.GM != null));
            if (!ready)
                ChatHelper.Send(
                    "<color=#EF5350>[Monk QA]</color> Load a character world first.");
            return ready;
        }

        private static string TakeToken(ref string text)
        {
            text = (text ?? "").Trim();
            int split = text.IndexOf(' ');
            if (split < 0)
            {
                string value = text;
                text = "";
                return value;
            }
            string token = text.Substring(0, split);
            text = text.Substring(split + 1).Trim();
            return token;
        }

        private static void SendResult(string test, bool pass, string detail)
        {
            ChatHelper.Send(
                $"<color={(pass ? "#4CAF50" : "#EF5350")}>[Monk QA] " +
                $"{test} {(pass ? "PASS" : "FAIL")}: {detail}.</color>");
        }
    }

    internal sealed class MonkShowcaseOverlay : MonoBehaviour
    {
        private string _title = "";
        private string _subtitle = "";
        private GUIStyle _titleStyle;
        private GUIStyle _subtitleStyle;

        internal void Set(string title, string subtitle)
        {
            _title = title ?? "";
            _subtitle = subtitle ?? "";
        }

        private void OnGUI()
        {
            if (_titleStyle == null)
            {
                _titleStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 34,
                    fontStyle = FontStyle.Bold,
                    normal = { textColor = new Color(1f, 0.82f, 0.28f) }
                };
                _subtitleStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 18,
                    fontStyle = FontStyle.Bold,
                    normal = { textColor = new Color(0.82f, 0.94f, 0.92f) }
                };
            }

            float scale = Mathf.Clamp(Screen.height / 1080f, 0.75f, 1.45f);
            Matrix4x4 previous = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float width = Mathf.Min(1120f, Screen.width / scale - 80f);
            Rect panel = new Rect(40f, 72f, width, 112f);
            Color previousColor = GUI.color;
            GUI.color = new Color(0.025f, 0.04f, 0.055f, 0.86f);
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = new Color(0.18f, 0.78f, 0.76f, 1f);
            GUI.DrawTexture(new Rect(panel.x, panel.y, 10f, panel.height),
                Texture2D.whiteTexture);
            GUI.color = previousColor;
            DrawShadowed(new Rect(70f, 82f, width - 60f, 48f), _title,
                _titleStyle);
            DrawShadowed(new Rect(72f, 137f, width - 60f, 30f), _subtitle,
                _subtitleStyle);
            GUI.matrix = previous;
        }

        private static void DrawShadowed(Rect rect, string text,
            GUIStyle style)
        {
            Color color = style.normal.textColor;
            style.normal.textColor = new Color(0f, 0f, 0f, 0.88f);
            GUI.Label(new Rect(rect.x + 3f, rect.y + 3f,
                rect.width, rect.height), text, style);
            style.normal.textColor = color;
            GUI.Label(rect, text, style);
        }
    }
}
