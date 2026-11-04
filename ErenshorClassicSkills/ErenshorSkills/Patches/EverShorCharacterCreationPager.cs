using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ErenshorSkills.Patches
{
    /// <summary>
    /// Provides an EverShor class page without resizing or moving Erenshor's
    /// character-creation panel. The pager uses the free row below the native
    /// class grid, leaving proficiency allocation and Save Character intact.
    /// </summary>
    internal static class EverShorCharacterCreationPager
    {
        private const string PageButtonName = "CS_EverShorClassPageButton";
        private const string NecromancerButtonName = "CS_NecromancerClassButton";
        private const string MonkButtonName = "CS_MonkClassButton";
        private const string FatePagerButtonName = "FOTF_CC_PageCycle";
        private const string EverShorPageLabel = "EverShor";
        private const string NativePageLabel = "Native";

        private static readonly HashSet<string> NativeClassLabels =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Arcanist", "Windblade", "Druid", "Paladin",
                "Stormcaller", "Reaver"
            };

        private static readonly HashSet<string> FateClassLabels =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Blightcaller", "Emberweaver", "Lightbringer"
            };

        private static readonly Dictionary<CharSelectManager, PageState> States =
            new Dictionary<CharSelectManager, PageState>();

        private sealed class PageState
        {
            internal Transform Container;
            internal Button PageButton;
            internal Button NecromancerButton;
            internal Button MonkButton;
            internal Vector2 NativePageButtonPosition;
            internal Vector2 EverShorPageButtonPosition;
            internal bool EverShorPage;
            internal readonly Dictionary<Button, bool> SavedButtonStates =
                new Dictionary<Button, bool>();
        }

        internal static void Install(CharSelectManager manager)
        {
            if (manager?.CharCreate == null) return;
            foreach (CharSelectManager stale in States.Keys.Where(key => key == null).ToList())
                States.Remove(stale);

            if (SkillsPlugin.CfgEnableNecromancer?.Value == true)
            {
                Necromancer.NecromancerCatalog.TryInitialize();
                Necromancer.NecromancerCatalog.EnsureClass();
            }
            if (SkillsPlugin.CfgEnableMonk?.Value == true)
            {
                Monk.MonkCatalog.TryInitialize();
                Monk.MonkCatalog.EnsureClass();
            }

            bool necromancerEnabled = SkillsPlugin.CfgEnableNecromancer?.Value == true &&
                Necromancer.NecromancerCatalog.NecromancerClass != null;
            bool monkEnabled = SkillsPlugin.CfgEnableMonk?.Value == true &&
                Monk.MonkCatalog.MonkClass != null;
            if (!necromancerEnabled && !monkEnabled) return;

            if (States.TryGetValue(manager, out PageState existing) &&
                existing?.PageButton != null)
            {
                RefreshClassButtons(existing, necromancerEnabled, monkEnabled);
                return;
            }

            Button source = manager.CharCreate
                .GetComponentsInChildren<Button>(true)
                .FirstOrDefault(button => button != null &&
                    string.Equals(GetLabel(button), "Reaver",
                        StringComparison.OrdinalIgnoreCase));
            if (source == null || source.transform.parent == null) return;

            Transform container = source.transform.parent;
            RectTransform sourceRect = source.GetComponent<RectTransform>();
            if (sourceRect == null) return;

            RemoveLegacyButton(container, PageButtonName);
            RemoveLegacyButton(container, NecromancerButtonName);
            RemoveLegacyButton(container, MonkButtonName);

            var state = new PageState { Container = container };
            state.PageButton = CreateButton(source, container, PageButtonName,
                EverShorPageLabel, () => TogglePage(manager));
            if (necromancerEnabled)
                state.NecromancerButton = CreateButton(source, container,
                    NecromancerButtonName,
                    Necromancer.NecromancerCatalog.ClassName,
                    () => Necromancer.NecromancerCharacterCreation
                        .SelectNecromancer(manager));
            if (monkEnabled)
                state.MonkButton = CreateButton(source, container,
                    MonkButtonName, Monk.MonkCatalog.ClassName,
                    () => Monk.MonkCharacterCreation.SelectMonk(manager));

            List<RectTransform> nativeRects = manager.CharCreate
                .GetComponentsInChildren<Button>(true)
                .Where(button => button != null && button.transform.parent == container &&
                    NativeClassLabels.Contains(GetLabel(button)))
                .Select(button => button.GetComponent<RectTransform>())
                .Where(rect => rect != null).ToList();
            if (nativeRects.Count < NativeClassLabels.Count)
            {
                DestroyButtons(state);
                SkillsPlugin.Log?.LogWarning(
                    "EverShor class pager could not identify the native class grid.");
                return;
            }

            PositionButtons(state, nativeRects, sourceRect);
            RefreshClassButtons(state, necromancerEnabled, monkEnabled);
            SetNativePage(manager, state);
            States[manager] = state;
            SkillsPlugin.Log?.LogInfo(
                "EverShor class page added to character creation.");
        }

        internal static void Shutdown()
        {
            foreach (var pair in States)
            {
                PageState state = pair.Value;
                if (pair.Key != null && state.PageButton != null)
                    SetNativePage(pair.Key, state);
                foreach (Button button in new[] { state.PageButton,
                    state.NecromancerButton, state.MonkButton })
                    if (button != null) UnityEngine.Object.Destroy(button.gameObject);
            }
            States.Clear();
        }

        /// <summary>Called by the optional Fate of the Fallen pager bridge.</summary>
        internal static void SetFatePage(CharSelectManager manager,
            bool nativePage)
        {
            if (manager == null ||
                !States.TryGetValue(manager, out PageState state) ||
                state?.PageButton == null) return;

            if (state.EverShorPage) SetNativePage(manager, state);
            state.PageButton.gameObject.SetActive(nativePage);
        }

        private static void RefreshClassButtons(PageState state,
            bool necromancerEnabled, bool monkEnabled)
        {
            if (state.NecromancerButton != null)
                state.NecromancerButton.gameObject.SetActive(
                    necromancerEnabled && state.EverShorPage);
            if (state.MonkButton != null)
                state.MonkButton.gameObject.SetActive(
                    monkEnabled && state.EverShorPage);
            if (state.PageButton != null)
                SetLabel(state.PageButton, state.EverShorPage
                    ? NativePageLabel : EverShorPageLabel);
        }

        private static void TogglePage(CharSelectManager manager)
        {
            if (manager == null || !States.TryGetValue(manager, out PageState state) ||
                state?.PageButton == null) return;
            if (state.EverShorPage) SetNativePage(manager, state);
            else SetEverShorPage(manager, state);
        }

        private static void SetEverShorPage(CharSelectManager manager,
            PageState state)
        {
            state.SavedButtonStates.Clear();
            foreach (Button button in state.Container.GetComponentsInChildren<Button>(true)
                .Where(button => button != null && button.transform.parent == state.Container))
            {
                if (button == state.PageButton ||
                    button == state.NecromancerButton || button == state.MonkButton)
                    continue;

                string label = GetLabel(button);
                bool isClassOption = NativeClassLabels.Contains(label) ||
                    FateClassLabels.Contains(label) ||
                    button.gameObject.name == FatePagerButtonName;
                if (!isClassOption) continue;

                state.SavedButtonStates[button] = button.gameObject.activeSelf;
                button.gameObject.SetActive(false);
            }

            state.EverShorPage = true;
            if (state.NecromancerButton != null)
                state.NecromancerButton.gameObject.SetActive(
                    SkillsPlugin.CfgEnableNecromancer?.Value == true);
            if (state.MonkButton != null)
                state.MonkButton.gameObject.SetActive(
                    SkillsPlugin.CfgEnableMonk?.Value == true);
            SetLabel(state.PageButton, NativePageLabel);
            state.PageButton.GetComponent<RectTransform>().anchoredPosition =
                state.EverShorPageButtonPosition;
            state.PageButton.transform.SetAsLastSibling();
            SelectForNavigation(state.NecromancerButton ?? state.MonkButton);
        }

        private static void SetNativePage(CharSelectManager manager,
            PageState state)
        {
            state.EverShorPage = false;
            // CreateButton starts inactive. The EverShor page toggle must be
            // visible on the initial native page so players can reach the
            // custom classes; Fate compatibility hides it on Fate's page.
            if (state.PageButton != null)
                state.PageButton.gameObject.SetActive(true);
            if (state.NecromancerButton != null)
                state.NecromancerButton.gameObject.SetActive(false);
            if (state.MonkButton != null)
                state.MonkButton.gameObject.SetActive(false);

            foreach (KeyValuePair<Button, bool> saved in state.SavedButtonStates.ToList())
                if (saved.Key != null)
                    saved.Key.gameObject.SetActive(saved.Value);
            state.SavedButtonStates.Clear();

            SetLabel(state.PageButton, EverShorPageLabel);
            state.PageButton.GetComponent<RectTransform>().anchoredPosition =
                state.NativePageButtonPosition;
            state.PageButton.transform.SetAsLastSibling();
            SelectForNavigation(state.PageButton);
        }

        private static Button CreateButton(Button source, Transform parent,
            string name, string label, UnityEngine.Events.UnityAction onClick)
        {
            GameObject clone = UnityEngine.Object.Instantiate(
                source.gameObject, parent, false);
            clone.name = name;
            Button button = clone.GetComponent<Button>();
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(onClick);
            SetLabel(button, label);
            clone.SetActive(false);
            return button;
        }

        private static void PositionButtons(PageState state,
            List<RectTransform> native, RectTransform source)
        {
            float topY = native.Max(rect => rect.anchoredPosition.y);
            float bottomY = native.Min(rect => rect.anchoredPosition.y);
            List<RectTransform> topRow = native.Where(rect =>
                Mathf.Abs(rect.anchoredPosition.y - topY) <= 4f).ToList();
            List<RectTransform> bottomRow = native.Where(rect =>
                Mathf.Abs(rect.anchoredPosition.y - bottomY) <= 4f).ToList();
            List<float> rows = native.Select(rect => rect.anchoredPosition.y)
                .OrderByDescending(y => y)
                .Aggregate(new List<float>(), (distinct, y) =>
                {
                    if (distinct.All(existing => Mathf.Abs(existing - y) > 4f))
                        distinct.Add(y);
                    return distinct;
                });

            float spacing = rows.Count > 1
                ? rows.Zip(rows.Skip(1), (upper, lower) => upper - lower).Average()
                : source.rect.height + 2f;
            spacing = Mathf.Max(source.rect.height + 1f, spacing);

            float left = topRow.Count >= 2
                ? topRow.Min(rect => rect.anchoredPosition.x)
                : -source.rect.width * 0.52f;
            float right = topRow.Count >= 2
                ? topRow.Max(rect => rect.anchoredPosition.x)
                : source.rect.width * 0.52f;
            float center = bottomRow.Count > 0
                ? bottomRow.Average(rect => rect.anchoredPosition.x) : 0f;

            if (state.NecromancerButton != null)
                state.NecromancerButton.GetComponent<RectTransform>()
                    .anchoredPosition = new Vector2(left, topY);
            if (state.MonkButton != null)
                state.MonkButton.GetComponent<RectTransform>()
                    .anchoredPosition = new Vector2(right, topY);

            // Keep the page toggle in the existing gap between the class grid
            // and appearance controls. No panel sizing or child offsets change.
            state.NativePageButtonPosition = new Vector2(center,
                bottomY - spacing);
            state.EverShorPageButtonPosition = new Vector2(center,
                topY - spacing);
            state.PageButton.GetComponent<RectTransform>().anchoredPosition =
                state.NativePageButtonPosition;
        }

        private static void DestroyButtons(PageState state)
        {
            if (state.PageButton != null)
                UnityEngine.Object.Destroy(state.PageButton.gameObject);
            if (state.NecromancerButton != null)
                UnityEngine.Object.Destroy(state.NecromancerButton.gameObject);
            if (state.MonkButton != null)
                UnityEngine.Object.Destroy(state.MonkButton.gameObject);
        }

        private static void RemoveLegacyButton(Transform parent, string name)
        {
            Transform existing = parent.Find(name);
            if (existing != null) UnityEngine.Object.Destroy(existing.gameObject);
        }

        private static string GetLabel(Button button) =>
            button?.GetComponentInChildren<TextMeshProUGUI>(true)?.text?.Trim() ??
            string.Empty;

        private static void SetLabel(Button button, string text)
        {
            TextMeshProUGUI label = button?.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label == null) return;
            label.text = text;
            label.enableWordWrapping = false;
            label.enableAutoSizing = true;
            label.fontSizeMin = 10f;
            label.fontSizeMax = label.fontSize;
        }

        private static void SelectForNavigation(Button button)
        {
            if (button == null) return;
            try
            {
                var eventSystem = UnityEngine.EventSystems.EventSystem.current;
                if (eventSystem != null)
                    eventSystem.SetSelectedGameObject(button.gameObject);
            }
            catch { }
        }
    }
}
