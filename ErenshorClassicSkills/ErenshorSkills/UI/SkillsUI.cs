using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using ErenshorSkills.UI;
using FontStyle = TMPro.FontStyles;

namespace ErenshorSkills
{
    /// <summary>
    /// uGUI Canvas-based Classic Skills window (v3).
    /// All positioning is manual — no LayoutGroups or ContentSizeFitter.
    /// Each row is placed at an explicit Y offset inside the scroll content,
    /// and the content height is set to the total of all rows.
    /// </summary>
    public class SkillsUI : MonoBehaviour
    {
        static SkillsUI _instance;
        static bool _showWindow;
        public static bool IsWindowVisible => _showWindow;

        GameObject _canvasGO;
        RectTransform _windowPanel;
        RectTransform _leftPage;
        RectTransform _rightPage;
        RectTransform _contentParent;
        RectTransform _bonusContentParent;
        TextMeshProUGUI _titleText;
        TextMeshProUGUI _noBonusesText;

        readonly List<SkillRowUI> _utilRows = new List<SkillRowUI>();
        readonly List<CombatRowUI> _combatRows = new List<CombatRowUI>();
        readonly List<SkillRowUI> _tradeRows = new List<SkillRowUI>();
        readonly List<CombatRowUI> _magicRows = new List<CombatRowUI>();
        readonly List<BonusRowUI> _bonusRows = new List<BonusRowUI>();
        bool _built;

        internal static int BonusRowCountForTesting =>
            _instance ? _instance._bonusRows.Count : 0;

        internal static int VisibleBonusRowCountForTesting => _instance
            ? _instance._bonusRows.FindAll(row =>
                row.root && row.root.gameObject.activeSelf).Count
            : 0;

        internal static bool HasCompleteBonusCoverageForTesting
        {
            get
            {
                if (!_instance) return false;
                string[] required = {
                    "catch_bonus", "forage_cooldown", "swim", "bind",
                    "med", "food", "beg_chance",
                    "combat:1H Slashing", "combat:1H Blunt",
                    "combat:Piercing", "combat:2H Slashing",
                    "combat:2H Blunt", "combat:Archery", "combat:Wands",
                    "combat:Hand to Hand", "magic:Evocation",
                    "magic:Abjuration", "magic:Alteration",
                    "magic:Conjuration"
                };
                foreach (string key in required)
                    if (!_instance._bonusRows.Exists(row => row.key == key))
                        return false;
                return true;
            }
        }

        // ── Colors (matched to game's inventory panel style) ─────────
        static readonly Color C_WinBg    = c(0.08f, 0.12f, 0.15f, 0.96f);  // Dark teal-blue
        static readonly Color C_Panel    = c(0.10f, 0.16f, 0.20f, 0.92f);  // Slightly lighter teal
        static readonly Color C_Title    = c(0.06f, 0.10f, 0.13f, 0.98f);  // Darkest teal for title
        static readonly Color C_Border   = c(0.22f, 0.36f, 0.40f, 0.9f);   // Teal/cyan border
        static readonly Color C_BarBg    = c(0.04f, 0.07f, 0.09f, 0.85f);  // Very dark teal bar bg
        static readonly Color C_Gold     = c(1f, 0.84f, 0f);
        static readonly Color C_Dim      = c(0.55f, 0.55f, 0.55f);
        static readonly Color C_Combat   = c(1f, 0.43f, 0.25f);
        static readonly Color C_Trade    = c(1f, 0.6f, 0f);
        static readonly Color C_Magic    = c(0.6f, 0.4f, 1f);
        static Color c(float r, float g, float b, float a = 1) => new Color(r, g, b, a);

        static readonly Color[] UtilC = {
            c(0.55f,0.76f,0.29f), c(0.31f,0.76f,0.97f), c(1f,0.65f,0.15f),
            c(0.94f,0.33f,0.31f), c(0.81f,0.58f,0.85f), c(1f,0.44f,0.26f),
            c(1f,0.67f,0.25f),    c(0.74f,0.67f,0.64f),
        };

        const float W = 610, H = 660, TITLE = 56, PAD = 6;
        const float ROW = 46, CROW = 46, HDR = 24, GAP = 3;

        // ══════════════════════════════════════════════════════════════
        // PUBLIC API
        // ══════════════════════════════════════════════════════════════

        public static void ToggleWindow()
        {
            _showWindow = !_showWindow;
            if (_instance) _instance.SetVis(_showWindow);
            SkillsPlugin.Log.LogInfo($"SkillsUI: toggled {_showWindow}");
        }

        public static void CloseWindow()
        {
            _showWindow = false;
            if (_instance) _instance.SetVis(false);
        }

        // ══════════════════════════════════════════════════════════════
        // LIFECYCLE
        // ══════════════════════════════════════════════════════════════

        void Awake() => _instance = this;

        void Start()
        {
            TryBuildWhenNativeUiIsReady();
        }

        void LateUpdate()
        {
            if (!_built) TryBuildWhenNativeUiIsReady();
            if (_built && _showWindow && _canvasGO && _canvasGO.activeSelf)
            {
                Refresh();
                UpdateTooltip();
            }
        }

        void TryBuildWhenNativeUiIsReady()
        {
            if (_built || !SkillsSaveManager.HasLoaded ||
                !NativeUiStyle.NativeSourcesAvailable ||
                !NativeEventSystemGate.IsReady) return;
            try
            {
                NativeUiStyle.Reset();
                Build();
                _built = true;
                SetVis(_showWindow);
                SkillsPlugin.Log.LogInfo(
                    $"SkillsUI: native book built. util={_utilRows.Count} " +
                    $"combat={_combatRows.Count} trade={_tradeRows.Count}");
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogError($"SkillsUI build: {ex}");
            }
        }

        void OnDestroy()
        {
            if (_canvasGO) Destroy(_canvasGO);
            if (ReferenceEquals(_instance, this)) _instance = null;
            _showWindow = false;
            _tooltipGO = null;
            _tooltipText = null;
            _tooltipRT = null;
            if (_ws) Destroy(_ws);
            if (_wt) Destroy(_wt);
            _ws = null;
            _wt = null;
        }

        // ══════════════════════════════════════════════════════════════
        // BUILD
        // ══════════════════════════════════════════════════════════════

        void Build()
        {
            _canvasGO = new GameObject("CS_Canvas");
            DontDestroyOnLoad(_canvasGO);
            var cv = _canvasGO.AddComponent<Canvas>();
            cv.renderMode = RenderMode.ScreenSpaceOverlay;
            cv.sortingOrder = 100;
            var sc = _canvasGO.AddComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            sc.referenceResolution = new Vector2(1920, 1080);
            sc.matchWidthOrHeight = 0.5f;
            _canvasGO.AddComponent<GraphicRaycaster>();

            // Two native pages, matching Erenshor's Skills/Spells book layout.
            _windowPanel = Rect(_canvasGO.transform, "ClassicSkillsBook");
            Img(_windowPanel, Color.clear);
            _windowPanel.anchorMin = V(0, 1); _windowPanel.anchorMax = V(0, 1);
            _windowPanel.pivot = V(0, 1);
            _windowPanel.anchoredPosition = V(20, -80);
            _windowPanel.sizeDelta = V(W, H);
            _windowPanel.gameObject.AddComponent<InputBlocker>();

            _leftPage = BuildNativePage(_windowPanel, "BonusPage", 0f);
            _rightPage = BuildNativePage(_windowPanel, "SkillsPage", 310f);

            // Erenshor's native books use title text directly over the carved
            // page, without an addon title strip or red close box.
            var titleRT = Rect(_rightPage, "TitleDrag");
            Img(titleRT, Color.clear);
            StretchH(titleRT, 0, TITLE);
            titleRT.gameObject.AddComponent<WindowDragger>().target = _windowPanel;

            _titleText = Txt(titleRT, "Skills", 30, Color.white,
                TextAnchor.MiddleCenter);
            _titleText.fontStyle = FontStyle.Bold;
            Fill(_titleText.rectTransform);
            _titleText.raycastTarget = false;

            var closeRT = Rect(_rightPage, "Close");
            var closeImage = Img(closeRT, c(0.72f, 0.16f, 0.16f, 0.95f));
            NativeUiStyle.Apply(closeImage, NativeUiStyle.Role.Button,
                c(0.72f, 0.16f, 0.16f, 0.95f));
            closeImage.color = c(0.72f, 0.16f, 0.16f, 0.95f);
            closeRT.anchorMin = V(1, 1); closeRT.anchorMax = V(1, 1);
            closeRT.pivot = V(1, 1);
            closeRT.anchoredPosition = V(-6, -6);
            closeRT.sizeDelta = V(28, 24);
            var closeButton = closeRT.gameObject.AddComponent<Button>();
            closeButton.targetGraphic = closeImage;
            closeButton.navigation = new Navigation { mode = Navigation.Mode.None };
            closeButton.onClick.AddListener(CloseWindow);
            var closeText = Txt(closeRT, "X", 15, Color.white,
                TextAnchor.MiddleCenter);
            closeText.fontStyle = FontStyle.Bold;
            Fill(closeText.rectTransform);

            float y = TITLE + 2;

            // Scroll area dimensions
            float bonusH = H, footerH = 28;
            float scrollH = H - y - footerH - 10;

            // Build scroll
            BuildScroll(y, scrollH);

            // Populate content — track Y cursor inside content
            float cy = 2; // top padding
            cy = PlaceHeader(cy, "Utility Skills", C_Gold);
            var utils = SkillsSaveManager.Data.GetUtilitySkills();
            for (int i = 0; i < utils.Count; i++)
            {
                Color col = i < UtilC.Length ? UtilC[i] : Color.white;
                _utilRows.Add(PlaceSkillRow(ref cy, utils[i], col));
            }

            cy = PlaceHeader(cy, "Tradeskills", C_Trade);
            foreach (var s in SkillsSaveManager.Data.GetTradeskills())
                _tradeRows.Add(PlaceSkillRow(ref cy, s, C_Trade));

            cy = PlaceHeader(cy, "Combat Skills", C_Combat, showBonus: true);
            if (SkillsPlugin.CfgEnableCombatSkills.Value)
            {
                foreach (var info in Skills.CombatSkills.GetActiveCombatSkills())
                    _combatRows.Add(PlaceCombatRow(ref cy, info));
            }
            else
            {
                var ph = Txt(_contentParent, "  Combat skills disabled.", 11, C_Dim, TextAnchor.MiddleLeft);
                PlaceAt(ph.rectTransform, cy, 22); cy += 22 + GAP;
            }

            cy = PlaceHeader(cy, "Magic Skills", C_Magic, showBonus: true, bonusLabel: "Bonus Effect");
            foreach (var s in SkillsSaveManager.Data.GetMagicSkills())
                _magicRows.Add(PlaceCombatRow(ref cy, new Skills.CombatSkills.CombatSkillInfo { Name = s.Name, Skill = s, Bonus = 0 }, C_Magic));

            cy += 2; // bottom padding

            // SET CONTENT HEIGHT — this is the key line
            _contentParent.sizeDelta = new Vector2(0, cy);

            // Build tooltip overlay
            BuildTooltip();

            SkillsPlugin.Log.LogInfo($"SkillsUI: content height = {cy:F0}px, " +
                $"scroll viewport = {scrollH:F0}px");

            // Bonus panel
            BuildBonusPanel(0, bonusH);

            // Footer
            var ft = Txt(_rightPage,
                $"F8 to close  |  /skills in chat",
                13, c(0.62f, 0.74f, 0.77f), TextAnchor.MiddleCenter);
            StretchH(ft.rectTransform, H - footerH - 2, footerH);
        }

        RectTransform BuildNativePage(Transform parent, string name, float x)
        {
            var page = Rect(parent, name);
            var image = Img(page, C_WinBg);
            NativeUiStyle.Apply(image, NativeUiStyle.Role.Window, C_WinBg);
            page.anchorMin = V(0, 1);
            page.anchorMax = V(0, 1);
            page.pivot = V(0, 1);
            page.anchoredPosition = V(x, 0);
            page.sizeDelta = V(300, H);
            return page;
        }

        // ══════════════════════════════════════════════════════════════
        // SCROLL
        // ══════════════════════════════════════════════════════════════

        void BuildScroll(float yTop, float h)
        {
            var sRT = Rect(_rightPage, "Scroll");
            Img(sRT, Color.clear);
            StretchH(sRT, yTop, h, 4, 4);

            var sr = sRT.gameObject.AddComponent<ScrollRect>();
            sr.horizontal = false; sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.scrollSensitivity = 25;

            var vRT = Rect(sRT, "Viewport");
            Img(vRT, Color.clear);
            Fill(vRT);
            vRT.gameObject.AddComponent<RectMask2D>();

            _contentParent = Rect(vRT, "Content");
            _contentParent.anchorMin = V(0, 1);
            _contentParent.anchorMax = V(1, 1);
            _contentParent.pivot = V(0.5f, 1);
            _contentParent.anchoredPosition = Vector2.zero;
            _contentParent.sizeDelta = V(0, 0); // will be set after populating

            sr.viewport = vRT;
            sr.content = _contentParent;

            // Scrollbar
            var sbRT = Rect(sRT, "SB");
            var sbImage = Img(sbRT, c(0.10f, 0.16f, 0.20f, 0.5f));
            NativeUiStyle.Apply(sbImage, NativeUiStyle.Role.Input,
                c(0.10f, 0.16f, 0.20f, 0.5f));
            sbRT.anchorMin = V(1, 0); sbRT.anchorMax = V(1, 1);
            sbRT.pivot = V(1, 0.5f);
            sbRT.sizeDelta = V(6, 0); sbRT.anchoredPosition = V(3, 0);

            var sb = sbRT.gameObject.AddComponent<Scrollbar>();
            sb.direction = Scrollbar.Direction.BottomToTop;

            var area = Rect(sbRT, "Area"); Fill(area);
            var handle = Rect(area, "Handle");
            var handleImage = Img(handle, c(0.30f, 0.45f, 0.50f, 0.7f));
            NativeUiStyle.Apply(handleImage, NativeUiStyle.Role.Button,
                c(0.30f, 0.45f, 0.50f, 0.7f));
            Fill(handle);
            sb.handleRect = handle;
            sb.targetGraphic = handle.GetComponent<Image>();
            sr.verticalScrollbar = sb;
            sr.verticalScrollbarVisibility =
                ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
        }

        // ══════════════════════════════════════════════════════════════
        // PLACE ROWS (manual Y positioning inside content rect)
        // ══════════════════════════════════════════════════════════════

        float PlaceHeader(float y, string text, Color color, bool showBonus = false, string bonusLabel = "Bonus DMG")
        {
            var t = Txt(_contentParent, text, 14, color, TextAnchor.MiddleCenter);
            t.fontStyle = FontStyle.Bold;
            PlaceAt(t.rectTransform, y, HDR);
            y += HDR;

            // Column headers
            var hdrRT = Rect(_contentParent, "ColHdr");
            PlaceAt(hdrRT, y, 14);

            var nameH = Txt(hdrRT, "Skill", 9, C_Dim, TextAnchor.MiddleLeft);
            nameH.rectTransform.anchorMin = V(0, 0);
            nameH.rectTransform.anchorMax = V(0.65f, 1);
            nameH.rectTransform.offsetMin = V(PAD, 0);
            nameH.rectTransform.offsetMax = V(0, 0);

            if (showBonus)
            {
                // Skill | Bonus column | Level
                var bonH = Txt(hdrRT, bonusLabel, 9, C_Dim, TextAnchor.MiddleRight);
                bonH.rectTransform.anchorMin = V(0.65f, 0);
                bonH.rectTransform.anchorMax = V(0.82f, 1);
                bonH.rectTransform.offsetMin = V(0, 0);
                bonH.rectTransform.offsetMax = V(0, 0);

                var lvlH = Txt(hdrRT, "Level", 9, C_Dim, TextAnchor.MiddleRight);
                lvlH.rectTransform.anchorMin = V(0.82f, 0);
                lvlH.rectTransform.anchorMax = V(1, 1);
                lvlH.rectTransform.offsetMin = V(0, 0);
                lvlH.rectTransform.offsetMax = V(-PAD, 0);
            }
            else
            {
                // Utility/Trade: Skill | Level (right-aligned)
                var lvlH = Txt(hdrRT, "Level", 9, C_Dim, TextAnchor.MiddleRight);
                lvlH.rectTransform.anchorMin = V(0.65f, 0);
                lvlH.rectTransform.anchorMax = V(1, 1);
                lvlH.rectTransform.offsetMin = V(0, 0);
                lvlH.rectTransform.offsetMax = V(-PAD, 0);
            }

            return y + 14 + GAP;
        }

        SkillRowUI PlaceSkillRow(ref float y, SkillEntry skill, Color accent)
        {
            var row = new SkillRowUI();
            row.skill = skill;

            // Container
            var cRT = Rect(_contentParent, skill.Name);
            var rowImage = Img(cRT, Color.white);
            NativeUiStyle.Apply(rowImage, NativeUiStyle.Role.Tab, C_Panel);
            PlaceAt(cRT, y, ROW);

            // Attach tooltip hover
            AttachTooltip(cRT, skill.Name);

            // Name (top-left, top 20px of row)
            row.nameText = Txt(cRT, skill.Name, 12, accent, TextAnchor.MiddleLeft);
            row.nameText.fontStyle = FontStyle.Bold;
            row.nameText.rectTransform.anchorMin = V(0, 1);
            row.nameText.rectTransform.anchorMax = V(0.55f, 1);
            row.nameText.rectTransform.pivot = V(0, 1);
            row.nameText.rectTransform.offsetMin = V(PAD, -20);
            row.nameText.rectTransform.offsetMax = V(0, -2);

            // Train "+" button (top-right, before level)
            var btnRT = Rect(cRT, "Train");
            var trainImage = Img(btnRT, c(0.56f, 0.66f, 0.69f, 1f));
            NativeUiStyle.Apply(trainImage, NativeUiStyle.Role.Plus,
                c(0.12f, 0.40f, 0.30f, 0.9f));
            btnRT.anchorMin = V(0.55f, 1); btnRT.anchorMax = V(0.55f, 1);
            btnRT.pivot = V(0, 1);
            btnRT.anchoredPosition = V(0, -1); btnRT.sizeDelta = V(22, 22);
            row.trainBtn = btnRT.gameObject.AddComponent<Button>();
            row.trainBtn.targetGraphic = btnRT.GetComponent<Image>();
            var btnTxt = Txt(btnRT, "", 11, Color.white, TextAnchor.MiddleCenter);
            btnTxt.fontStyle = FontStyle.Bold; Fill(btnTxt.rectTransform);
            var thisRow = row;
            row.trainBtn.onClick.AddListener(() => OnTrainClick(thisRow.skill));

            // Level (top-right)
            row.levelText = Txt(cRT, "", 12, Color.white, TextAnchor.MiddleRight);
            row.levelText.fontStyle = FontStyle.Bold;
            row.levelText.rectTransform.anchorMin = V(0.65f, 1);
            row.levelText.rectTransform.anchorMax = V(1, 1);
            row.levelText.rectTransform.pivot = V(1, 1);
            row.levelText.rectTransform.offsetMin = V(0, -20);
            row.levelText.rectTransform.offsetMax = V(-PAD, -2);

            // XP bar (middle band: y=22 to y=36 from top)
            var barRT = Rect(cRT, "Bar");
            var barImage = Img(barRT, C_BarBg);
            NativeUiStyle.Apply(barImage, NativeUiStyle.Role.Progress, C_BarBg);
            barRT.anchorMin = V(0, 1);
            barRT.anchorMax = V(1, 1);
            barRT.pivot = V(0.5f, 1);
            barRT.offsetMin = V(PAD, -36);
            barRT.offsetMax = V(-PAD, -22);

            row.barFill = CreateProgressFill(barRT, "Fill",
                accent * 0.7f, 5, 4);

            // XP text overlay on bar
            row.xpText = Txt(barRT, "", 9, Color.white, TextAnchor.MiddleCenter);
            row.xpText.fontStyle = FontStyle.Bold;
            Fill(row.xpText.rectTransform);

            y += ROW + GAP;
            return row;
        }

        CombatRowUI PlaceCombatRow(ref float y, Skills.CombatSkills.CombatSkillInfo info, Color? overrideColor = null)
        {
            var row = new CombatRowUI();
            row.skill = info.Skill;
            Color accent = overrideColor ?? C_Combat;

            var cRT = Rect(_contentParent, info.Name);
            var rowImage = Img(cRT, Color.white);
            NativeUiStyle.Apply(rowImage, NativeUiStyle.Role.Tab, C_Panel);
            PlaceAt(cRT, y, ROW);

            // Attach tooltip hover
            AttachTooltip(cRT, info.Name);

            // Name (top-left)
            row.nameText = Txt(cRT, info.Name, 12, accent, TextAnchor.MiddleLeft);
            row.nameText.fontStyle = FontStyle.Bold;
            row.nameText.rectTransform.anchorMin = V(0, 1);
            row.nameText.rectTransform.anchorMax = V(0.50f, 1);
            row.nameText.rectTransform.pivot = V(0, 1);
            row.nameText.rectTransform.offsetMin = V(PAD, -20);
            row.nameText.rectTransform.offsetMax = V(0, -2);

            // Train "+" button
            var btnRT = Rect(cRT, "Train");
            var trainImage = Img(btnRT, c(0.56f, 0.66f, 0.69f, 1f));
            NativeUiStyle.Apply(trainImage, NativeUiStyle.Role.Plus,
                c(0.12f, 0.40f, 0.30f, 0.9f));
            btnRT.anchorMin = V(0.50f, 1); btnRT.anchorMax = V(0.50f, 1);
            btnRT.pivot = V(0, 1);
            btnRT.anchoredPosition = V(0, -1); btnRT.sizeDelta = V(22, 22);
            row.trainBtn = btnRT.gameObject.AddComponent<Button>();
            row.trainBtn.targetGraphic = btnRT.GetComponent<Image>();
            var btnTxt = Txt(btnRT, "", 11, Color.white, TextAnchor.MiddleCenter);
            btnTxt.fontStyle = FontStyle.Bold; Fill(btnTxt.rectTransform);
            var thisRow = row;
            row.trainBtn.onClick.AddListener(() => OnTrainClick(thisRow.skill));

            // Bonus (before level, matching utility skill alignment)
            row.bonusText = Txt(cRT, "", 12, accent, TextAnchor.MiddleRight);
            row.bonusText.fontStyle = FontStyle.Bold;
            row.bonusText.rectTransform.anchorMin = V(0.65f, 1);
            row.bonusText.rectTransform.anchorMax = V(0.82f, 1);
            row.bonusText.rectTransform.pivot = V(1, 1);
            row.bonusText.rectTransform.offsetMin = V(0, -20);
            row.bonusText.rectTransform.offsetMax = V(0, -2);

            // Level (far right, same position as utility skills)
            row.levelText = Txt(cRT, "", 12, Color.white, TextAnchor.MiddleRight);
            row.levelText.fontStyle = FontStyle.Bold;
            row.levelText.rectTransform.anchorMin = V(0.82f, 1);
            row.levelText.rectTransform.anchorMax = V(1, 1);
            row.levelText.rectTransform.pivot = V(1, 1);
            row.levelText.rectTransform.offsetMin = V(0, -20);
            row.levelText.rectTransform.offsetMax = V(-PAD, -2);

            // XP bar (middle band: y=22 to y=36 from top)
            var barRT = Rect(cRT, "Bar");
            var barImage = Img(barRT, C_BarBg);
            NativeUiStyle.Apply(barImage, NativeUiStyle.Role.Progress, C_BarBg);
            barRT.anchorMin = V(0, 1);
            barRT.anchorMax = V(1, 1);
            barRT.pivot = V(0.5f, 1);
            barRT.offsetMin = V(PAD, -36);
            barRT.offsetMax = V(-PAD, -22);

            row.barFill = CreateProgressFill(barRT, "Fill",
                accent * 0.6f, 5, 4);

            // XP text on bar
            row.hitsText = Txt(barRT, "", 9, Color.white, TextAnchor.MiddleCenter);
            row.hitsText.fontStyle = FontStyle.Bold;
            Fill(row.hitsText.rectTransform);

            y += ROW + GAP;
            return row;
        }

        /// <summary>Place a rect at a Y offset from the top of _contentParent, stretching full width.</summary>
        void PlaceAt(RectTransform rt, float y, float h)
        {
            rt.anchorMin = V(0, 1); rt.anchorMax = V(1, 1);
            rt.pivot = V(0.5f, 1);
            rt.offsetMin = V(2, -(y + h));
            rt.offsetMax = V(-2, -y);
        }

        RectTransform CreateProgressFill(RectTransform track, string name,
            Color color, float horizontalInset, float verticalInset)
        {
            var clip = Rect(track, name + "Clip");
            Img(clip, Color.clear).raycastTarget = false;
            Fill(clip);
            clip.offsetMin = V(horizontalInset, verticalInset);
            clip.offsetMax = V(-horizontalInset, -verticalInset);
            clip.gameObject.AddComponent<RectMask2D>();

            var fill = Rect(clip, name);
            Img(fill, color).raycastTarget = false;
            fill.pivot = V(0, 0.5f);
            SetProgressFill(fill, 0f);
            return fill;
        }

        static void SetProgressFill(RectTransform fill, float value)
        {
            if (!fill) return;
            fill.anchorMin = V(0, 0);
            fill.anchorMax = V(Mathf.Clamp01(value), 1);
            fill.offsetMin = Vector2.zero;
            fill.offsetMax = Vector2.zero;
        }

        // ══════════════════════════════════════════════════════════════
        // BONUS PANEL
        // ══════════════════════════════════════════════════════════════

        void BuildBonusPanel(float yTop, float h)
        {
            var pRT = Rect(_leftPage, "BonusContent");
            Img(pRT, Color.clear);
            Fill(pRT);

            float by = 12;
            var hdr = Txt(pRT, "Active Bonuses", 28, Color.white,
                TextAnchor.MiddleCenter);
            hdr.fontStyle = FontStyle.Bold;
            BonusPlace(hdr.rectTransform, by, 42); by += 58;

            var scrollRT = Rect(pRT, "BonusScroll");
            Img(scrollRT, Color.clear);
            scrollRT.anchorMin = V(0, 1); scrollRT.anchorMax = V(1, 1);
            scrollRT.pivot = V(0.5f, 1);
            scrollRT.offsetMin = V(4, -(h - 8));
            scrollRT.offsetMax = V(-4, -by);

            var scroll = scrollRT.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 25;

            var viewport = Rect(scrollRT, "Viewport");
            Img(viewport, Color.clear);
            Fill(viewport);
            viewport.offsetMax = V(-8, 0);
            viewport.gameObject.AddComponent<RectMask2D>();

            _bonusContentParent = Rect(viewport, "Content");
            _bonusContentParent.anchorMin = V(0, 1);
            _bonusContentParent.anchorMax = V(1, 1);
            _bonusContentParent.pivot = V(0.5f, 1);
            _bonusContentParent.anchoredPosition = Vector2.zero;
            _bonusContentParent.sizeDelta = Vector2.zero;
            scroll.viewport = viewport;
            scroll.content = _bonusContentParent;

            var sbRT = Rect(scrollRT, "SB");
            var sbImage = Img(sbRT, c(0.10f, 0.16f, 0.20f, 0.5f));
            NativeUiStyle.Apply(sbImage, NativeUiStyle.Role.Input,
                c(0.10f, 0.16f, 0.20f, 0.5f));
            sbRT.anchorMin = V(1, 0); sbRT.anchorMax = V(1, 1);
            sbRT.pivot = V(1, 0.5f);
            sbRT.sizeDelta = V(6, 0); sbRT.anchoredPosition = V(3, 0);
            var scrollbar = sbRT.gameObject.AddComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            var area = Rect(sbRT, "Area"); Fill(area);
            var handle = Rect(area, "Handle");
            var handleImage = Img(handle, c(0.30f, 0.45f, 0.50f, 0.7f));
            NativeUiStyle.Apply(handleImage, NativeUiStyle.Role.Button,
                c(0.30f, 0.45f, 0.50f, 0.7f));
            Fill(handle);
            scrollbar.handleRect = handle;
            scrollbar.targetGraphic = handleImage;
            scroll.verticalScrollbar = scrollbar;
            scroll.verticalScrollbarVisibility =
                ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;

            float rowY = 0;
            AddBLine(ref rowY, "Fishing Catch Chance", "catch_bonus");
            AddBLine(ref rowY, "Foraging Cooldown", "forage_cooldown");
            AddBLine(ref rowY, "Swimming Speed", "swim");
            AddBLine(ref rowY, "Bind Wound Heal / CD", "bind");
            AddBLine(ref rowY, "Meditate Mana / tick", "med");
            AddBLine(ref rowY, "Food Buff Duration", "food");
            AddBLine(ref rowY, "Begging Success Chance", "beg_chance");

            foreach (var info in Skills.CombatSkills.GetActiveCombatSkills())
                AddBLine(ref rowY, info.Name + " Damage",
                    "combat:" + info.Name);

            AddBLine(ref rowY, "Evocation Spell Damage", "magic:Evocation");
            AddBLine(ref rowY, "Abjuration Buff Duration", "magic:Abjuration");
            AddBLine(ref rowY, "Alteration Healing", "magic:Alteration");
            AddBLine(ref rowY, "Conjuration DoT / Pet Damage",
                "magic:Conjuration");

            _noBonusesText = Txt(_bonusContentParent,
                "No earned skill bonuses yet.", 15, C_Dim,
                TextAnchor.MiddleCenter);
            PlaceBonusRow(_noBonusesText.rectTransform, 8, 32, 4);
            _bonusContentParent.sizeDelta = V(0, rowY + 4);
        }

        void AddBLine(ref float y, string label, string key)
        {
            var row = Rect(_bonusContentParent, "Bonus_" + key.Replace(':', '_'));
            PlaceBonusRow(row, y, 32, 2);

            var lbl = Txt(row, label, 15,
                c(0.72f, 0.70f, 0.88f), TextAnchor.MiddleLeft);
            var lRT = lbl.rectTransform;
            lRT.anchorMin = V(0, 0); lRT.anchorMax = V(0.70f, 1);
            lRT.offsetMin = V(10, 0); lRT.offsetMax = V(0, 0);

            var val = Txt(row, "\u2014", 15, Color.white,
                TextAnchor.MiddleRight);
            val.fontStyle = FontStyle.Bold;
            var vRT = val.rectTransform;
            vRT.anchorMin = V(0.70f, 0); vRT.anchorMax = V(1, 1);
            vRT.offsetMin = V(0, 0); vRT.offsetMax = V(-10, 0);

            _bonusRows.Add(new BonusRowUI
            {
                key = key,
                root = row,
                labelText = lbl,
                valueText = val
            });
            row.gameObject.SetActive(false);
            y += 38;
        }

        static void PlaceBonusRow(RectTransform rt, float y, float height,
            float horizontalInset)
        {
            rt.anchorMin = V(0, 1); rt.anchorMax = V(1, 1);
            rt.pivot = V(0.5f, 1);
            rt.offsetMin = V(horizontalInset, -(y + height));
            rt.offsetMax = V(-horizontalInset, -y);
        }

        void BonusPlace(RectTransform rt, float y, float h)
        {
            rt.anchorMin = V(0, 1); rt.anchorMax = V(1, 1);
            rt.pivot = V(0.5f, 1);
            rt.offsetMin = V(8, -(y + h));
            rt.offsetMax = V(-8, -y);
        }

        // ══════════════════════════════════════════════════════════════
        // REFRESH
        // ══════════════════════════════════════════════════════════════

        void Refresh()
        {
            var data = SkillsSaveManager.Data;

            // Check for new training points from leveling up
            data.CheckForNewTrainingPoints();

            // Update title with training points
            if (_titleText != null)
            {
                if (data.TrainingPoints > 0)
                    _titleText.text = $"Skills  <color=#00FF00>[{data.TrainingPoints} TP]</color>";
                else
                    _titleText.text = "Skills";
            }

            var utils = data.GetUtilitySkills();
            for (int i = 0; i < _utilRows.Count && i < utils.Count; i++)
                UpdRow(_utilRows[i], utils[i]);

            var tr = data.GetTradeskills();
            for (int i = 0; i < _tradeRows.Count && i < tr.Count; i++)
                UpdRow(_tradeRows[i], tr[i]);

            if (SkillsPlugin.CfgEnableCombatSkills.Value)
            {
                var cb = Skills.CombatSkills.GetActiveCombatSkills();
                for (int i = 0; i < _combatRows.Count && i < cb.Count; i++)
                    UpdCombat(_combatRows[i], cb[i]);
            }

            var mag = data.GetMagicSkills();
            for (int i = 0; i < _magicRows.Count && i < mag.Count; i++)
            {
                var s = mag[i];
                float bonus = 0f;
                switch (s.Name)
                {
                    case "Evocation":   bonus = (Skills.MagicSkills.GetSpellDamageMultiplier() - 1f) * 100f; break;
                    case "Abjuration":  bonus = (Skills.MagicSkills.GetBuffDurationMultiplier() - 1f) * 100f; break;
                    case "Alteration":  bonus = (Skills.MagicSkills.GetHealMultiplier() - 1f) * 100f; break;
                    case "Conjuration": bonus = (Skills.MagicSkills.GetConjurationMultiplier() - 1f) * 100f; break;
                }
                var info = new Skills.CombatSkills.CombatSkillInfo { Name = s.Name, Skill = s, Bonus = bonus };
                UpdCombat(_magicRows[i], info, C_Magic);
            }

            var combatBonuses = Skills.CombatSkills.GetActiveCombatSkills();
            RefreshBonusRows(combatBonuses);
        }

        void UpdRow(SkillRowUI r, SkillEntry s)
        {
            r.skill = s; // Keep reference in sync with loaded data
            UpdateTrainButton(r.trainBtn, s);
            if (!s.IsUnlocked)
            {
                r.levelText.text = $"<color=#666666>0/{s.SkillCap}</color>";
                SetProgressFill(r.barFill, 0f);
                r.xpText.text = "";
            }
            else if (s.IsAtCap)
            {
                r.levelText.text = $"{s.Level}/{s.SkillCap}";
                SetProgressFill(r.barFill, 1f);
                r.xpText.text = "";
            }
            else
            {
                r.levelText.text = $"{s.Level}/{s.SkillCap}";
                float f = Mathf.Clamp01(s.LevelProgress);
                SetProgressFill(r.barFill, f);
                r.xpText.text = "";
            }
        }

        void UpdCombat(CombatRowUI r, Skills.CombatSkills.CombatSkillInfo info, Color? bonusColor = null)
        {
            var s = info.Skill;
            Color accent = bonusColor ?? C_Combat;
            r.skill = s;
            UpdateTrainButton(r.trainBtn, s);
            if (!s.IsUnlocked)
            {
                r.levelText.text = $"<color=#666666>0/{s.SkillCap}</color>";
                r.bonusText.text = "\u2014";
                r.bonusText.color = Color.gray;
                SetProgressFill(r.barFill, 0f);
                r.hitsText.text = "";
            }
            else if (s.IsAtCap)
            {
                r.levelText.text = $"{s.Level}/{s.SkillCap}";
                r.bonusText.text = info.Bonus > 0.01f ? $"+{info.Bonus:F1}%" : "\u2014";
                r.bonusText.color = info.Bonus > 0.01f ? accent : Color.gray;
                SetProgressFill(r.barFill, 1f);
                r.hitsText.text = "";
            }
            else
            {
                r.levelText.text = $"{s.Level}/{s.SkillCap}";
                r.bonusText.text = info.Bonus > 0.01f ? $"+{info.Bonus:F1}%" : "\u2014";
                r.bonusText.color = info.Bonus > 0.01f ? accent : Color.gray;
                float f = Mathf.Clamp01(s.LevelProgress);
                SetProgressFill(r.barFill, f);
                r.hitsText.text = "";
            }
        }

        void OnTrainClick(SkillEntry skill)
        {
            var data = SkillsSaveManager.Data;
            if (data.TrainingPoints <= 0)
            {
                ChatHelper.Send(
                    "<color=#FF6666>[Skills]</color> No training points available.");
                return;
            }
            if (skill.IsAtCap)
            {
                ChatHelper.Send(
                    $"<color=#FF6666>[Skills]</color> {skill.Name} is already at its skill cap.");
                return;
            }

            if (data.SpendTrainingPoint(skill))
            {
                ChatHelper.Send(
                    $"<color=#FFD700>[Skills]</color> " +
                    $"Trained <color=#FFFFFF>{skill.Name}</color> to level {skill.Level}! " +
                    $"<color=#AAAAAA>({data.TrainingPoints} TP remaining)</color>");
                SkillsSaveManager.Save();
            }
        }

        void UpdateTrainButton(Button btn, SkillEntry skill)
        {
            if (btn == null) return;
            var data = SkillsSaveManager.Data;
            bool canTrain = data.TrainingPoints > 0 && !skill.IsAtCap;
            btn.gameObject.SetActive(canTrain);
        }

        void RefreshBonusRows(
            List<Skills.CombatSkills.CombatSkillInfo> combatBonuses)
        {
            float y = 0f;
            int visible = 0;
            foreach (BonusRowUI row in _bonusRows)
            {
                if (!TryGetEarnedBonus(row, combatBonuses, out string value))
                {
                    if (row.root) row.root.gameObject.SetActive(false);
                    continue;
                }

                row.root.gameObject.SetActive(true);
                row.valueText.text = value;
                row.valueText.color = Color.white;
                PlaceBonusRow(row.root, y, 32, 2);
                y += 38f;
                visible++;
            }

            if (_noBonusesText)
                _noBonusesText.gameObject.SetActive(visible == 0);
            if (_bonusContentParent)
                _bonusContentParent.sizeDelta = V(0,
                    visible > 0 ? y + 4f : 48f);
        }

        bool TryGetEarnedBonus(BonusRowUI b,
            List<Skills.CombatSkills.CombatSkillInfo> combatBonuses,
            out string value)
        {
            value = "";
            var data = SkillsSaveManager.Data;
            switch (b.key) {
                case "catch_bonus":
                    if (SkillsPlugin.CfgEnableFishing.Value &&
                        data.Fishing.Level > 0)
                    {
                        value = $"+{Skills.FishingSkill.CatchBonus:F1}%";
                        return true;
                    }
                    return false;
                case "forage_cooldown":
                    if (SkillsPlugin.CfgEnableForaging.Value &&
                        data.Foraging.Level > 0)
                    {
                        float cooldown = Mathf.Max(90f -
                            data.Foraging.Level * 0.9f, 45f);
                        value = $"{cooldown:F0}s";
                        return true;
                    }
                    return false;
                case "swim":
                    if (SkillsPlugin.CfgEnableSwimming.Value &&
                        data.Swimming.Level > 0)
                    {
                        value = $"+{(Skills.SwimmingSkill.SpeedMultiplier-1f)*100f:F0}%";
                        return true;
                    }
                    return false;
                case "bind":
                    if (SkillsPlugin.CfgEnableBindWound.Value &&
                        data.BindWound.Level > 0)
                    {
                        float cooldown = Mathf.Max(45f -
                            data.BindWound.Level * 0.5f, 15f);
                        value = $"{Skills.BindWoundSkill.HealPercent:F1}% / {cooldown:F0}s";
                        return true;
                    }
                    return false;
                case "med":
                    if (SkillsPlugin.CfgEnableMeditate.Value &&
                        data.Meditate.Level > 0)
                    {
                        value = $"+{Skills.MeditateSkill.BonusManaPerTick:F1}";
                        return true;
                    }
                    return false;
                case "food":
                    if (SkillsPlugin.CfgEnableFoodTolerance.Value &&
                        data.FoodTolerance.Level > 0)
                    {
                        value = $"+{Skills.FoodToleranceSkill.DurationBonus:F0}%";
                        return true;
                    }
                    return false;
                case "beg_chance":
                    if (SkillsPlugin.CfgEnableBegging.Value &&
                        data.Begging.Level > 0)
                    {
                        float chance = Mathf.Clamp((30f +
                            data.Begging.Level * 1.2f) / 1.5f,
                            5f, 95f);
                        value = $"{chance:F0}%";
                        return true;
                    }
                    return false;
                case "magic:Evocation":
                    if (SkillsPlugin.CfgEnableMagicSkills.Value &&
                        data.Evocation.Level > 0)
                    {
                        value = $"+{(Skills.MagicSkills.GetSpellDamageMultiplier()-1f)*100f:F1}%";
                        return true;
                    }
                    return false;
                case "magic:Abjuration":
                    if (SkillsPlugin.CfgEnableMagicSkills.Value &&
                        data.Abjuration.Level > 0)
                    {
                        value = $"+{(Skills.MagicSkills.GetBuffDurationMultiplier()-1f)*100f:F1}%";
                        return true;
                    }
                    return false;
                case "magic:Alteration":
                    if (SkillsPlugin.CfgEnableMagicSkills.Value &&
                        data.Alteration.Level > 0)
                    {
                        value = $"+{(Skills.MagicSkills.GetHealMultiplier()-1f)*100f:F1}%";
                        return true;
                    }
                    return false;
                case "magic:Conjuration":
                    if (SkillsPlugin.CfgEnableMagicSkills.Value &&
                        data.Conjuration.Level > 0)
                    {
                        value = $"+{(Skills.MagicSkills.GetConjurationMultiplier()-1f)*100f:F1}%";
                        return true;
                    }
                    return false;
                default:
                    if (b.key.StartsWith("combat:",
                        StringComparison.Ordinal) &&
                        SkillsPlugin.CfgEnableCombatSkills.Value)
                    {
                        string name = b.key.Substring("combat:".Length);
                        foreach (var info in combatBonuses)
                            if (string.Equals(info.Name, name,
                                StringComparison.Ordinal) &&
                                info.Skill != null && info.Skill.Level > 0)
                            {
                                value = $"+{info.Bonus:F1}%";
                                return true;
                            }
                    }
                    return false;
            }
        }

        // ══════════════════════════════════════════════════════════════
        // VISIBILITY
        // ══════════════════════════════════════════════════════════════

        void SetVis(bool v)
        {
            if (_canvasGO) _canvasGO.SetActive(v);
        }

        // ══════════════════════════════════════════════════════════════
        // PRIMITIVES
        // ══════════════════════════════════════════════════════════════

        static Texture2D _wt; static Sprite _ws;
        static Sprite WS { get {
            if (!_ws) { if (!_wt) { _wt = new Texture2D(4,4);
                var p = new Color[16]; for (int i=0;i<16;i++) p[i]=Color.white;
                _wt.SetPixels(p); _wt.Apply(); }
                _ws = Sprite.Create(_wt, new UnityEngine.Rect(0,0,4,4), V(0.5f,0.5f)); }
            return _ws; } }

        static Vector2 V(float x, float y) => new Vector2(x, y);

        static RectTransform Rect(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.AddComponent<RectTransform>();
        }

        static Image Img(RectTransform rt, Color color)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = WS; img.color = color;
            img.type = Image.Type.Simple; img.raycastTarget = true;
            return img;
        }

        static TextMeshProUGUI Txt(Transform parent, string text, int size,
            Color color, TextAnchor anchor)
        {
            var rt = Rect(parent, "T");
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = size; t.color = color;
            t.alignment = ToTmpAlignment(anchor);
            NativeUiStyle.Apply(t);
            return t;
        }

        static TextAlignmentOptions ToTmpAlignment(TextAnchor anchor)
        {
            switch (anchor)
            {
                case TextAnchor.MiddleLeft: return TextAlignmentOptions.MidlineLeft;
                case TextAnchor.MiddleRight: return TextAlignmentOptions.MidlineRight;
                case TextAnchor.UpperLeft: return TextAlignmentOptions.TopLeft;
                default: return TextAlignmentOptions.Center;
            }
        }

        static void Fill(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        }

        /// <summary>Stretch horizontally across parent at yTop from top.</summary>
        static void StretchH(RectTransform rt, float y, float h,
            float padL = 0, float padR = 0)
        {
            rt.anchorMin = V(0, 1); rt.anchorMax = V(1, 1);
            rt.pivot = V(0.5f, 1);
            rt.offsetMin = V(padL, -(y + h));
            rt.offsetMax = V(-padR, -y);
        }

        /// <summary>Set anchor offsets explicitly: anchors define the relative region, offsets fine-tune.</summary>
        static void Anchor(RectTransform rt,
            float aMinX, float aMinY, float aMaxX, float aMaxY,
            float oMinX, float oMinY, float oMaxX, float oMaxY)
        {
            rt.anchorMin = V(aMinX, aMinY);
            rt.anchorMax = V(aMaxX, aMaxY);
            rt.offsetMin = V(oMinX, oMinY);
            rt.offsetMax = V(oMaxX, oMaxY);
        }

        // ── Data structs ──────────────────────────────────────────────

        class SkillRowUI {
            public TextMeshProUGUI nameText, levelText, xpText;
            public RectTransform barFill;
            public Button trainBtn;
            public SkillEntry skill;
        }
        class CombatRowUI {
            public TextMeshProUGUI nameText, levelText, bonusText, hitsText;
            public RectTransform barFill;
            public Button trainBtn;
            public SkillEntry skill;
        }
        class BonusRowUI
        {
            public string key;
            public RectTransform root;
            public TextMeshProUGUI labelText, valueText;
        }

        // ── Tooltip system ────────────────────────────────────────────

        static GameObject _tooltipGO;
        static TextMeshProUGUI _tooltipText;
        static RectTransform _tooltipRT;

        static readonly Dictionary<string, string> SkillTooltips = new Dictionary<string, string>
        {
            // Utility
            {"Fishing", "Levels from catching fish.\nHigher skill = more nibbles per cast = better catch rate.\nAlso awards Raw Fish, Moongill Trout, and Golden Carp for Baking recipes."},
            {"Foraging", "Search the ground for herbs, roots, and materials.\nHigher skill unlocks rarer finds and reduces failures."},
            {"Swimming", "Levels up by swimming in water while moving.\n+0.3% swim speed per level."},
            {"Bind Wound", "Heal yourself out of combat with Patchwork Bandages.\n+0.15% max HP healed per level."},
            {"Meditate", "Sit safely out of combat to regenerate extra mana.\nCasting, taking damage, typing, or entering combat pauses meditation."},
            {"Food Tolerance", "Levels when you consume food or drink buffs.\n+0.556% buff duration per level."},
            {"Begging", "Press ; to ask nearby NPCs for handouts.\nCan receive gold, items, or lore tidbits."},
            // Combat
            {"1H Slashing", "Swords, scimitars, axes.\n+0.085% melee damage per level.\nLevels from landing hits with 1H slashing weapons."},
            {"1H Blunt", "Maces, hammers, clubs.\n+0.085% melee damage per level.\nLevels from landing hits with 1H blunt weapons."},
            {"Piercing", "Daggers, dirks, rapiers.\n+0.085% melee damage per level.\nLevels from landing hits with piercing weapons."},
            {"2H Slashing", "Greatswords, claymores, halberds.\n+0.085% melee damage per level.\nLevels from landing hits with 2H slashing weapons."},
            {"2H Blunt", "Mauls, warhammers, staves.\n+0.085% melee damage per level.\nLevels from landing hits with 2H blunt weapons."},
            {"Archery", "Bows and ranged weapons.\n+0.085% ranged damage per level.\nLevels from landing hits with bows."},
            {"Wands", "Magical wands.\n+0.085% wand damage per level.\nLevels from landing wand hits."},
            {"Hand to Hand", "Unarmed combat.\n+0.085% melee damage per level.\nLevels when fighting with no weapon equipped."},
            // Tradeskills
            {"Smithing", "Forge weapons, armor, shields, and intermediates.\nCraft at a Forge or Anvil.\n52 recipes from starter to endgame."},
            {"Baking", "Cook food with stat buffs from herbs, meat, and fish.\nCraft at an Oven, Stove, or Campfire.\n55 recipes including fish dishes."},
            {"Brewing", "Brew drinks and potions with stat buffs.\nCraft at a Brew Barrel or Keg.\n46 recipes from grog to cosmic elixirs."},
            {"Fletching", "Craft bows and ranged weapons from wood and sinew.\nCraft at a Fletching Table.\n29 recipes from crude shortbows to endgame."},
            {"Jewelcraft", "Create rings, necklaces, circlets, and charms.\nCraft at a Jeweler's Kit.\n47 recipes using gems and precious metals."},
            {"Tailoring", "Sew cloth and leather armor, cloaks, and bags.\nCraft at a Loom.\n51 recipes including 4 bag sizes."},
            // Magic
            {"Evocation", "Direct damage spells (nukes, AE, PBAE).\n+0.08% spell damage per level.\nMax bonus: +14.4% at skill 180."},
            {"Abjuration", "Buff and ward spells.\n+0.3% buff duration per level.\nMax bonus: +54% at skill 180."},
            {"Alteration", "Healing spells.\n+0.08% heal amount per level.\nMax bonus: +14.4% at skill 180."},
            {"Conjuration", "Pet-summoning, DoT, and status-effect spells.\n+0.08% DoT and summoned-pet damage per level.\nMax bonus: +14.4% at skill 180."},
        };

        static bool TryGetSkillTooltip(string skillName, out string description)
        {
            if (!SkillTooltips.TryGetValue(skillName, out description))
                return false;

            if (skillName == "Foraging")
            {
                KeyCode key = SkillsPlugin.CfgForageKey?.Value ?? KeyCode.F9;
                string input = key == KeyCode.None
                    ? "Click the leaf icon beside the Skills button"
                    : $"Press {UtilityActionBar.FormatKeyForDisplay(key)} or click the leaf icon beside the Skills button";
                description = $"{input} to search the ground for herbs, roots, and materials.\n" +
                    "Higher skill unlocks rarer finds and reduces failures.";
                return true;
            }
            if (skillName == "Bind Wound")
            {
                KeyCode key = SkillsPlugin.CfgBindWoundKey?.Value ?? KeyCode.F10;
                string input = key == KeyCode.None
                    ? "Click the bandage icon beside the Skills button"
                    : $"Press {UtilityActionBar.FormatKeyForDisplay(key)} or click the bandage icon beside the Skills button";
                description = $"{input} to heal out of combat with Patchwork Bandages.\n" +
                    "+0.15% max HP healed per level.";
                return true;
            }
            if (skillName == "Begging")
            {
                KeyCode key = SkillsPlugin.CfgBegKey?.Value ?? KeyCode.Semicolon;
                string input = key == KeyCode.None
                    ? "Click the coin icon beside the Skills button"
                    : $"Press {UtilityActionBar.FormatKeyForDisplay(key)} or click the coin icon beside the Skills button";
                description = input + " to ask a nearby targeted NPC for handouts.\n" +
                    "Can receive gold, items, or lore tidbits.";
                return true;
            }

            float combatPerLevel = Mathf.Max(0f,
                SkillsPlugin.CfgCombatSkillDamagePerLevel?.Value ?? 0.085f);
            switch (skillName)
            {
                case "1H Slashing": case "1H Blunt": case "Piercing":
                case "2H Slashing": case "2H Blunt": case "Archery":
                case "Wands": case "Hand to Hand":
                    description = description.Replace("+0.085%",
                        $"+{combatPerLevel:0.###}%");
                    return true;
            }

            int maxLevel = Mathf.Max(1,
                SkillsPlugin.CfgGlobalMaxLevel?.Value ?? 180);
            float perLevel;
            string purpose;
            switch (skillName)
            {
                case "Evocation":
                    perLevel = Mathf.Max(0f,
                        SkillsPlugin.CfgEvocationDamagePerLevel?.Value ?? 0.0008f) * 100f;
                    purpose = "spell damage";
                    break;
                case "Abjuration":
                    perLevel = Mathf.Max(0f,
                        SkillsPlugin.CfgAbjurationDurationPerLevel?.Value ?? 0.003f) * 100f;
                    purpose = "buff duration";
                    break;
                case "Alteration":
                    perLevel = Mathf.Max(0f,
                        SkillsPlugin.CfgAlterationHealPerLevel?.Value ?? 0.0008f) * 100f;
                    purpose = "heal amount";
                    break;
                case "Conjuration":
                    perLevel = Mathf.Max(0f,
                        SkillsPlugin.CfgConjurationDamagePerLevel?.Value ?? 0.0008f) * 100f;
                    purpose = "DoT and summoned-pet damage";
                    break;
                default:
                    return true;
            }

            int firstBreak = description.IndexOf('\n');
            string firstLine = firstBreak >= 0
                ? description.Substring(0, firstBreak) : description;
            description = $"{firstLine}\n+{perLevel:0.###}% {purpose} per level." +
                $"\nMax bonus: +{perLevel * maxLevel:0.#}% at skill {maxLevel}.";
            return true;
        }

        void BuildTooltip()
        {
            _tooltipGO = new GameObject("SkillTooltip");
            _tooltipGO.transform.SetParent(_canvasGO.transform, false);
            _tooltipRT = _tooltipGO.AddComponent<RectTransform>();
            _tooltipRT.pivot = V(0, 1);
            _tooltipRT.sizeDelta = V(260, 80);

            var bg = Img(_tooltipRT, c(0.05f, 0.08f, 0.10f, 0.96f));
            bg.raycastTarget = false;
            Fill(bg.rectTransform);
            var outline = _tooltipGO.AddComponent<Outline>();
            outline.effectColor = C_Border;
            outline.effectDistance = new Vector2(1, -1);

            _tooltipText = Txt(_tooltipRT, "", 11, c(0.85f, 0.85f, 0.85f),
                TextAnchor.UpperLeft);
            _tooltipText.enableWordWrapping = true;
            _tooltipText.overflowMode = TextOverflowModes.Overflow;
            _tooltipText.rectTransform.anchorMin = V(0, 0);
            _tooltipText.rectTransform.anchorMax = V(1, 1);
            _tooltipText.rectTransform.offsetMin = V(8, 6);
            _tooltipText.rectTransform.offsetMax = V(-8, -6);

            _tooltipGO.SetActive(false);
        }

        // Store all row RectTransforms with their skill names for mouse tracking
        readonly List<(RectTransform rt, string name)> _tooltipRows = new List<(RectTransform, string)>();
        string _lastTooltipSkill = "";

        void UpdateTooltip()
        {
            if (_tooltipGO == null) return;

            // Check if mouse is over the skills window at all
            Vector2 mouseScreen = Input.mousePosition;
            Vector2 localMouse;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _windowPanel, mouseScreen, null, out localMouse);

            // Check if mouse is within the window bounds
            bool inWindow = _windowPanel.rect.Contains(localMouse);
            if (!inWindow)
            {
                if (_tooltipGO.activeSelf) { _tooltipGO.SetActive(false); _lastTooltipSkill = ""; }
                return;
            }

            // Check each row to see if mouse is over it
            string hoveredSkill = null;
            Vector3 hoveredWorldPos = Vector3.zero;
            foreach (var (rt, name) in _tooltipRows)
            {
                if (rt == null) continue;
                Vector2 rowLocal;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    rt, mouseScreen, null, out rowLocal);
                if (rt.rect.Contains(rowLocal))
                {
                    hoveredSkill = name;
                    hoveredWorldPos = rt.position;
                    break;
                }
            }

            if (hoveredSkill == null)
            {
                if (_tooltipGO.activeSelf) { _tooltipGO.SetActive(false); _lastTooltipSkill = ""; }
                return;
            }

            // Show tooltip for hovered skill
            if (hoveredSkill != _lastTooltipSkill)
            {
                string desc;
                if (!TryGetSkillTooltip(hoveredSkill, out desc))
                {
                    _tooltipGO.SetActive(false); _lastTooltipSkill = ""; return;
                }
                _tooltipText.text = $"<color=#FFD700>{hoveredSkill}</color>\n{desc}";
                _lastTooltipSkill = hoveredSkill;
            }

            // Position tooltip to the right of the window, clamped to screen
            _tooltipRT.anchorMin = V(0, 1);
            _tooltipRT.anchorMax = V(0, 1);

            Vector2 canvasLocal;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _tooltipGO.transform.parent as RectTransform,
                mouseScreen, null, out canvasLocal);

            float tooltipH = Mathf.Max(60, _tooltipText.preferredHeight + 16);
            _tooltipRT.sizeDelta = V(260, tooltipH);

            // Place to right of window
            var winPos = _windowPanel.anchoredPosition;
            float tx = winPos.x + W + 4;
            float ty = canvasLocal.y + 20;

            // Clamp to keep tooltip on screen (canvas coords, top-left origin)
            // Reference resolution is 1920x1080, anchored top-left
            float minY = -(1080f - 20f);  // don't go below screen
            float maxY = -20f;              // don't go above screen
            ty = Mathf.Clamp(ty, minY + tooltipH, maxY);

            _tooltipRT.anchoredPosition = V(tx, ty);
            _tooltipGO.SetActive(true);
        }

        /// <summary>Register a row for tooltip tracking.</summary>
        void AttachTooltip(RectTransform rowContainer, string skillName)
        {
            _tooltipRows.Add((rowContainer, skillName));
        }

        // ── Helper components ─────────────────────────────────────────

        class InputBlocker : MonoBehaviour,
            IPointerEnterHandler, IPointerExitHandler
        {
            bool _inside;
            public void OnPointerEnter(PointerEventData e)
            {
                if (_inside) return;
                _inside = true;
                AddonInputState.Enter();
            }
            public void OnPointerExit(PointerEventData e)
            {
                if (!_inside) return;
                _inside = false;
                AddonInputState.Exit();
            }
            void OnDisable() { if (_inside) OnPointerExit(null); }
        }

        class WindowDragger : MonoBehaviour,
            IDragHandler, IBeginDragHandler
        {
            public RectTransform target;
            Vector2 _off;
            public void OnBeginDrag(PointerEventData e)
            {
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    target.parent as RectTransform, e.position,
                    e.pressEventCamera, out Vector2 lp);
                _off = target.anchoredPosition - lp;
            }
            public void OnDrag(PointerEventData e)
            {
                if (!target) return;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    target.parent as RectTransform, e.position,
                    e.pressEventCamera, out Vector2 lp);
                target.anchoredPosition = lp + _off;
            }
        }
    }
}
