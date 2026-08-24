using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using FontStyle = TMPro.FontStyles;

namespace ErenshorSkills.UI
{
    /// <summary>
    /// uGUI Canvas-based Tradeskill Crafting Window (v2).
    /// Matches the SkillsUI style — dark panels, gold accents,
    /// proper scroll/drag via uGUI event system.
    /// </summary>
    public class TradeskillWindow : MonoBehaviour
    {
        static TradeskillWindow _instance;
        static bool _showWindow;
        static string _activeTradeskill = "";
        static int _selectedRecipeIndex = -1;
        static string _searchText = "";
        static bool _showOnlyAvailable = false;
        static bool _qaShowAllRecipes;
        static bool _qaScrollBottomPending;
        static bool _isCombining;
        static float _combineStartTime;
        const float COMBINE_DUR = 2.5f;

        // Cached filtered recipes
        static List<Skills.Tradeskills.Recipe> _filtered
            = new List<Skills.Tradeskills.Recipe>();

        // uGUI references
        GameObject _canvasGO;
        RectTransform _windowPanel;
        RectTransform _leftPage;
        RectTransform _rightPage;
        ScrollRect _recipeScroll;
        Scrollbar _recipeScrollbar;
        RectTransform _recipeContent;
        RectTransform _detailPanel;
        TextMeshProUGUI _titleText, _skillLevelText, _combineStatusText, _footerText;
        TextMeshProUGUI _detailName, _detailDesc, _detailReqSkill, _detailTrivial;
        TextMeshProUGUI _detailChance, _detailValue;
        Image _detailIcon;
        RectTransform _detailIconRT;
        RectTransform _xpBarFill, _combineBarFill;
        TextMeshProUGUI _xpBarText, _combineBarText;
        Button _combineBtn;
        TextMeshProUGUI _combineBtnText;
        Button _deconBtn;
        TextMeshProUGUI _deconBtnText;
        TMP_InputField _searchField;
        Image _craftableToggleImage;
        TextMeshProUGUI _craftableToggleLabel;

        RectTransform _matsContainer;
        bool _built;

        // Colors matching game's native UI palette
        static readonly Color C_WinBg  = c(0.08f, 0.12f, 0.15f, 0.96f);  // Dark teal-blue
        static readonly Color C_Panel  = c(0.10f, 0.16f, 0.20f, 0.92f);  // Slightly lighter teal
        static readonly Color C_Title  = c(0.06f, 0.10f, 0.13f, 0.98f);  // Darkest teal for title
        static readonly Color C_Border = c(0.22f, 0.36f, 0.40f, 0.9f);   // Teal/cyan border
        static readonly Color C_BarBg  = c(0.04f, 0.07f, 0.09f, 0.85f);  // Very dark teal bar bg
        static readonly Color C_Gold   = c(1f, 0.84f, 0f);
        static readonly Color C_Accent = c(1f, 0.6f, 0f);
        static readonly Color C_Dim    = c(0.55f, 0.55f, 0.55f);
        static readonly Color C_Green  = c(0.3f, 0.69f, 0.31f);
        static readonly Color C_Red    = c(0.94f, 0.33f, 0.31f);
        static Color c(float r, float g, float b, float a = 1) => new Color(r, g, b, a);

        const float W = 610, H = 620, TITLE_H = 58, PAD = 6;
        const float MAX_STATION_DIST = 5f;
        static Vector3 _openPosition;

        // ══════════════════════════════════════════════════════════
        // PUBLIC API
        // ══════════════════════════════════════════════════════════

        public static void Open(string tradeskillName)
        {
            if (_instance == null)
            {
                var go = new GameObject("TradeskillWindowUI");
                _instance = go.AddComponent<TradeskillWindow>();
                DontDestroyOnLoad(go);
            }

            if (_showWindow && _activeTradeskill == tradeskillName)
            {
                Close();
                return;
            }

            _qaShowAllRecipes = false;
            _qaScrollBottomPending = false;
            _activeTradeskill = tradeskillName;
            _showWindow = true;
            _selectedRecipeIndex = -1;
            _searchText = "";
            _isCombining = false;
            try { _openPosition = GameData.PlayerControl.transform.position; } catch { }
            RefreshRecipeList();
            _instance.SetVis(true);
            if (_instance._built)
            {
                _instance.ClearRecipeRows();   // Force full rebuild
                _instance.ClearMaterials();
                if (_instance._searchField) _instance._searchField.text = "";
                _instance.Refresh();
            }
        }

        public static void Close()
        {
            _showWindow = false;
            _qaShowAllRecipes = false;
            _qaScrollBottomPending = false;
            _isCombining = false;
            if (_instance) _instance.SetVis(false);
        }

        /// <summary>
        /// Opens a non-persistent QA preview with the full recipe list visible,
        /// selects a real row through the same state used by its click handler,
        /// and optionally moves the real ScrollRect to its bottom boundary.
        /// No recipe knowledge or inventory state is changed.
        /// </summary>
        internal static bool OpenQaPreview(string tradeskillName,
            bool bottom, out string detail)
        {
            detail = null;
            if (SkillsPlugin.CfgEnableTestMode?.Value != true)
            {
                detail = "test mode is disabled";
                return false;
            }

            if (_showWindow) Close();
            Open(tradeskillName);
            _qaShowAllRecipes = true;
            RefreshRecipeList();
            if (_filtered.Count <= 0)
            {
                detail = $"no {tradeskillName} recipes were available";
                return false;
            }

            _selectedRecipeIndex = bottom ? _filtered.Count - 1 : 0;
            _qaScrollBottomPending = bottom;
            if (_instance && _instance._built)
            {
                _instance.ClearRecipeRows();
                _instance.Refresh();
            }
            detail = $"{tradeskillName}: selected " +
                $"'{_filtered[_selectedRecipeIndex].Name}' at " +
                $"{(bottom ? "bottom" : "top")} of {_filtered.Count} recipes";
            return true;
        }

        public static void Shutdown()
        {
            _showWindow = false;
            _isCombining = false;
            _filtered.Clear();
            _selectedRecipeIndex = -1;
            _searchText = "";
            _showOnlyAvailable = false;
            _qaShowAllRecipes = false;
            _qaScrollBottomPending = false;

            var instance = _instance;
            _instance = null;
            if (instance)
                UnityEngine.Object.Destroy(instance.gameObject);

            foreach (var sprite in _ownedIconSprites)
            {
                if (!sprite) continue;
                var texture = sprite.texture;
                UnityEngine.Object.Destroy(sprite);
                if (texture) UnityEngine.Object.Destroy(texture);
            }
            _ownedIconSprites.Clear();
            _iconCache.Clear();
            _iconDir = null;
            if (_ws) UnityEngine.Object.Destroy(_ws);
            if (_wt) UnityEngine.Object.Destroy(_wt);
            _ws = null;
            _wt = null;
        }

        public static bool IsOpen => _showWindow;

        // ══════════════════════════════════════════════════════════
        // LIFECYCLE
        // ══════════════════════════════════════════════════════════

        void Start()
        {
            TryBuildWhenNativeUiIsReady();
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        }

        void TryBuildWhenNativeUiIsReady()
        {
            if (_built || !NativeEventSystemGate.IsReady) return;
            try
            {
                Build();
                _built = true;
                SetVis(_showWindow);
                if (_showWindow)
                    Refresh();
            }
            catch (Exception ex) { SkillsPlugin.Log.LogError($"TradeskillWindow build: {ex}"); }
        }

        void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            if (_showWindow) Close();
        }

        void LateUpdate()
        {
            if (!_built) TryBuildWhenNativeUiIsReady();
            if (!_built || !_showWindow || !_canvasGO || !_canvasGO.activeSelf) return;

            // Close if player moved too far from where they opened
            try
            {
                if (GameData.PlayerControl != null)
                {
                    float dist = Vector3.Distance(
                        GameData.PlayerControl.transform.position, _openPosition);
                    if (dist > MAX_STATION_DIST)
                    {
                        ChatHelper.Send(
                            "<color=#FF9800>[Tradeskills]</color> " +
                            "You moved too far from the station.");
                        Close();
                        return;
                    }
                }
                else
                {
                    // Player control is null — character exited or scene changed
                    Close();
                    return;
                }
            }
            catch
            {
                // If anything fails checking player, close to be safe
                Close();
                return;
            }

            // Escape key closes the window
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Close();
                return;
            }

            if (_isCombining && Time.time - _combineStartTime >= COMBINE_DUR)
            {
                _isCombining = false;
                ExecuteCombine();
            }

            Refresh();
            if (_qaScrollBottomPending && _recipeScroll)
            {
                Canvas.ForceUpdateCanvases();
                _recipeScroll.verticalNormalizedPosition = 0f;
                _qaScrollBottomPending = false;
            }
        }

        void OnDestroy()
        {
            if (_canvasGO) Destroy(_canvasGO);
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
            if (ReferenceEquals(_instance, this)) _instance = null;
        }

        // ══════════════════════════════════════════════════════════
        // BUILD — construct the entire uGUI hierarchy
        // ══════════════════════════════════════════════════════════

        void Build()
        {
            _canvasGO = new GameObject("CS_TS_Canvas");
            DontDestroyOnLoad(_canvasGO);
            var cv = _canvasGO.AddComponent<Canvas>();
            cv.renderMode = RenderMode.ScreenSpaceOverlay;
            cv.sortingOrder = 101;
            var sc = _canvasGO.AddComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            sc.referenceResolution = new Vector2(1920, 1080);
            sc.matchWidthOrHeight = 0.5f;
            _canvasGO.AddComponent<GraphicRaycaster>();

            // Two native carved pages, matching Erenshor's Skills/Spells book.
            _windowPanel = MkRect(_canvasGO.transform, "TradeskillBook");
            MkImg(_windowPanel, Color.clear);
            _windowPanel.anchorMin = V(0, 1); _windowPanel.anchorMax = V(0, 1);
            _windowPanel.pivot = V(0, 1);
            _windowPanel.anchoredPosition = V(340, -80);
            _windowPanel.sizeDelta = V(W, H);
            _windowPanel.gameObject.AddComponent<InputBlocker>();

            _leftPage = BuildNativePage(_windowPanel, "RecipePage", 0f);
            _rightPage = BuildNativePage(_windowPanel, "DetailPage", 310f);

            // Native books place title text directly on the carved page. Escape
            // closes this window just like Erenshor's other modal windows.
            var titleRT = MkRect(_rightPage, "TitleDrag");
            MkImg(titleRT, Color.clear);
            StretchH(titleRT, 0, TITLE_H);
            titleRT.gameObject.AddComponent<WindowDragger>().target = _windowPanel;

            _titleText = MkTxt(titleRT, "Tradeskill", 30, Color.white,
                TextAnchor.MiddleCenter);
            _titleText.fontStyle = FontStyle.Bold;
            Fill(_titleText.rectTransform);
            _titleText.raycastTarget = false;

            var recipeTitle = MkTxt(_leftPage, "Recipes", 30, Color.white,
                TextAnchor.MiddleCenter);
            recipeTitle.fontStyle = FontStyle.Bold;
            StretchH(recipeTitle.rectTransform, 0, TITLE_H);
            recipeTitle.raycastTarget = false;

            var closeRT = MkRect(_rightPage, "Close");
            var closeImage = MkImg(closeRT, c(0.72f, 0.16f, 0.16f, 0.95f));
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
            closeButton.onClick.AddListener(Close);
            var closeText = MkTxt(closeRT, "X", 15, Color.white,
                TextAnchor.MiddleCenter);
            closeText.fontStyle = FontStyle.Bold;
            Fill(closeText.rectTransform);

            _skillLevelText = MkTxt(_rightPage, "", 15,
                c(0.62f, 0.74f, 0.77f), TextAnchor.MiddleCenter);
            StretchH(_skillLevelText.rectTransform, 43, 24, 12, 12);

            float y = TITLE_H + 10;

            // Native XP frame on the right page.
            var xpBg = MkRect(_rightPage, "XPBg");
            var xpImage = MkImg(xpBg, C_BarBg);
            NativeUiStyle.Apply(xpImage, NativeUiStyle.Role.Progress, C_BarBg);
            StretchH(xpBg, y, 24, 12, 12);
            _xpBarFill = CreateProgressFill(xpBg, "XPFill",
                C_Accent * 0.7f, 6, 7);
            _xpBarText = MkTxt(xpBg, "", 9, Color.white, TextAnchor.MiddleCenter);
            _xpBarText.fontStyle = FontStyle.Bold; Fill(_xpBarText.rectTransform);
            y += 30;

            // Search and filter live on the left recipe page.
            var searchRow = MkRect(_leftPage, "Search");
            StretchH(searchRow, y, 28, 12, 12);

            // Search input field
            var sfRT = MkRect(searchRow, "SF");
            sfRT.anchorMin = V(0, 0); sfRT.anchorMax = V(0.67f, 1);
            sfRT.offsetMin = V(0, 0); sfRT.offsetMax = V(-4, 0);
            var searchImage = MkImg(sfRT, c(0.10f, 0.16f, 0.22f, 0.8f));
            NativeUiStyle.Apply(searchImage, NativeUiStyle.Role.Input,
                c(0.10f, 0.16f, 0.22f, 0.8f));
            _searchField = sfRT.gameObject.AddComponent<TMP_InputField>();
            var sfText = MkTxt(sfRT, "", 14, Color.white, TextAnchor.MiddleLeft);
            sfText.rectTransform.offsetMin = V(4, 0); sfText.rectTransform.offsetMax = V(-4, 0);
            Fill(sfText.rectTransform);
            sfText.rectTransform.offsetMin = V(4, 0);
            _searchField.textComponent = sfText;
            _searchField.onValueChanged.AddListener(s =>
            {
                _searchText = s;
                RefreshRecipeList();
                if (_built) Refresh();
            });

            // Placeholder
            var phText = MkTxt(sfRT, "Search recipes...", 13, C_Dim, TextAnchor.MiddleLeft);
            Fill(phText.rectTransform); phText.rectTransform.offsetMin = V(4, 0);
            _searchField.placeholder = phText;

            // Craftable toggle
            var togRT = MkRect(searchRow, "Tog");
            togRT.anchorMin = V(0.68f, 0); togRT.anchorMax = V(1, 1);
            togRT.offsetMin = Vector2.zero; togRT.offsetMax = Vector2.zero;
            _craftableToggleImage = MkImg(togRT, c(0.10f, 0.16f, 0.22f, 0.8f));
            NativeUiStyle.Apply(_craftableToggleImage,
                NativeUiStyle.Role.Button, c(0.10f, 0.16f, 0.22f, 0.8f));
            var craftableButton = togRT.gameObject.AddComponent<Button>();
            craftableButton.targetGraphic = _craftableToggleImage;
            craftableButton.navigation = new Navigation { mode = Navigation.Mode.None };
            craftableButton.onClick.AddListener(() =>
            {
                _showOnlyAvailable = !_showOnlyAvailable;
                UpdateCraftableToggleVisual();
                RefreshRecipeList();
                if (_built) Refresh();
            });
            _craftableToggleLabel = MkTxt(togRT, "Ready", 12, C_Dim, TextAnchor.MiddleCenter);
            Fill(_craftableToggleLabel.rectTransform);
            UpdateCraftableToggleVisual();
            y += 34;

            // Two-panel area: recipes (left) + details (right)
            float panelH = H - y - 88;

            // LEFT: Recipe list scroll
            var leftPanel = MkRect(_windowPanel, "Left");
            MkImg(leftPanel, Color.clear);
            leftPanel.anchorMin = V(0, 1); leftPanel.anchorMax = V(0.49f, 1);
            leftPanel.pivot = V(0, 1);
            leftPanel.offsetMin = V(6, -(y + panelH));
            leftPanel.offsetMax = V(0, -y);

            // Recipe scroll
            var rScrollRT = MkRect(leftPanel, "RScroll");
            rScrollRT.anchorMin = V(0, 0); rScrollRT.anchorMax = V(1, 1);
            rScrollRT.offsetMin = V(8, 4); rScrollRT.offsetMax = V(-8, -4);
            var sr = rScrollRT.gameObject.AddComponent<ScrollRect>();
            sr.horizontal = false; sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.scrollSensitivity = 20;

            var vpRT = MkRect(rScrollRT, "VP");
            MkImg(vpRT, Color.clear); Fill(vpRT);
            vpRT.offsetMax = V(-10, 0);
            vpRT.gameObject.AddComponent<RectMask2D>();

            _recipeContent = MkRect(vpRT, "Content");
            _recipeContent.anchorMin = V(0, 1); _recipeContent.anchorMax = V(1, 1);
            _recipeContent.pivot = V(0.5f, 1);
            _recipeContent.anchoredPosition = Vector2.zero;
            _recipeContent.sizeDelta = V(0, 0);
            sr.viewport = vpRT; sr.content = _recipeContent;
            _recipeScroll = sr;

            // Match the Skills book: wheel scrolling plus a visible,
            // draggable native-styled bar whenever the recipe list overflows.
            var sbRT = MkRect(rScrollRT, "SB");
            var sbImage = MkImg(sbRT, c(0.10f, 0.16f, 0.20f, 0.5f));
            NativeUiStyle.Apply(sbImage, NativeUiStyle.Role.Input,
                c(0.10f, 0.16f, 0.20f, 0.5f));
            sbRT.anchorMin = V(1, 0); sbRT.anchorMax = V(1, 1);
            sbRT.pivot = V(1, 0.5f);
            sbRT.sizeDelta = V(8, 0); sbRT.anchoredPosition = Vector2.zero;

            var sb = sbRT.gameObject.AddComponent<Scrollbar>();
            sb.direction = Scrollbar.Direction.BottomToTop;
            var area = MkRect(sbRT, "Area"); Fill(area);
            area.offsetMin = V(1, 1); area.offsetMax = V(-1, -1);
            var handle = MkRect(area, "Handle");
            var handleImage = MkImg(handle, c(0.30f, 0.45f, 0.50f, 0.85f));
            NativeUiStyle.Apply(handleImage, NativeUiStyle.Role.Button,
                c(0.30f, 0.45f, 0.50f, 0.85f));
            Fill(handle);
            sb.handleRect = handle;
            sb.targetGraphic = handleImage;
            sr.verticalScrollbar = sb;
            sr.verticalScrollbarVisibility =
                ScrollRect.ScrollbarVisibility.AutoHide;
            _recipeScrollbar = sb;

            // RIGHT: Detail panel
            _detailPanel = MkRect(_windowPanel, "Right");
            MkImg(_detailPanel, Color.clear);
            _detailPanel.anchorMin = V(0.51f, 1); _detailPanel.anchorMax = V(1, 1);
            _detailPanel.pivot = V(0, 1);
            _detailPanel.offsetMin = V(0, -(y + panelH));
            _detailPanel.offsetMax = V(-6, -y);

            float dy = 4;

            // Item icon (top-left of detail panel)
            _detailIconRT = MkRect(_detailPanel, "DetailIcon");
            var detailFrame = MkImg(_detailIconRT, Color.white);
            NativeUiStyle.Apply(detailFrame, NativeUiStyle.Role.Slot,
                Color.white);
            _detailIconRT.anchorMin = V(0, 1); _detailIconRT.anchorMax = V(0, 1);
            _detailIconRT.pivot = V(0, 1);
            _detailIconRT.anchoredPosition = V(6, -(dy));
            _detailIconRT.sizeDelta = V(58, 58);
            var detailIconInner = MkRect(_detailIconRT, "Icon");
            _detailIcon = MkImg(detailIconInner, Color.clear);
            _detailIcon.raycastTarget = true;
            Fill(detailIconInner);
            detailIconInner.offsetMin = V(5, 5);
            detailIconInner.offsetMax = V(-5, -5);
            // Tooltip will be added dynamically in Refresh

            // Name (to the right of icon)
            _detailName = MkTxt(_detailPanel, "", 13, C_Gold, TextAnchor.UpperLeft);
            _detailName.fontStyle = FontStyle.Bold;
            _detailName.rectTransform.anchorMin = V(0, 1);
            _detailName.rectTransform.anchorMax = V(1, 1);
            _detailName.rectTransform.pivot = V(0.5f, 1);
            _detailName.rectTransform.offsetMin = V(68, -(dy + 40));
            _detailName.rectTransform.offsetMax = V(-6, -dy);
            dy += 62;

            _detailDesc = MkTxt(_detailPanel, "", 10, C_Dim, TextAnchor.UpperLeft);
            _detailDesc.enableWordWrapping = true;
            PlaceIn(_detailDesc.rectTransform, dy, 40, 6, 6); dy += 42;

            _detailReqSkill = MkTxt(_detailPanel, "", 11, Color.white, TextAnchor.MiddleLeft);
            _detailReqSkill.richText = true;
            PlaceIn(_detailReqSkill.rectTransform, dy, 16, 6, 6); dy += 18;

            _detailTrivial = MkTxt(_detailPanel, "", 11, C_Dim, TextAnchor.MiddleLeft);
            _detailTrivial.richText = true;
            PlaceIn(_detailTrivial.rectTransform, dy, 16, 6, 6); dy += 18;

            _detailChance = MkTxt(_detailPanel, "", 11, C_Green, TextAnchor.MiddleLeft);
            PlaceIn(_detailChance.rectTransform, dy, 16, 6, 6); dy += 20;

            // Materials header
            var matHdr = MkTxt(_detailPanel, "Materials", 14,
                c(0.72f, 0.70f, 0.88f), TextAnchor.MiddleCenter);
            matHdr.fontStyle = FontStyle.Bold;
            PlaceIn(matHdr.rectTransform, dy, 16, 6, 6); dy += 18;

            // Materials container (populated dynamically)
            _matsContainer = MkRect(_detailPanel, "Mats");
            _matsContainer.anchorMin = V(0, 1); _matsContainer.anchorMax = V(1, 1);
            _matsContainer.pivot = V(0.5f, 1);
            _matsContainer.offsetMin = V(6, -(dy + 120));
            _matsContainer.offsetMax = V(-6, -dy);
            dy += 122;

            _detailValue = MkTxt(_detailPanel, "", 11, C_Gold, TextAnchor.MiddleLeft);
            PlaceIn(_detailValue.rectTransform, dy, 16, 6, 6);

            // Combine progress bar
            float combineY = y + panelH + 4;
            var cbBg = MkRect(_rightPage, "CombBg");
            var combineProgressImage = MkImg(cbBg, C_BarBg);
            NativeUiStyle.Apply(combineProgressImage,
                NativeUiStyle.Role.Progress, C_BarBg);
            StretchH(cbBg, combineY, 22, 12, 12);
            _combineBarFill = CreateProgressFill(cbBg, "CombFill",
                c(1f, 0.8f, 0.2f, 0.8f), 6, 6);
            _combineBarText = MkTxt(cbBg, "", 9, Color.white, TextAnchor.MiddleCenter);
            _combineBarText.fontStyle = FontStyle.Bold; Fill(_combineBarText.rectTransform);

            // Combine button (left half)
            float btnY = combineY + 28;
            var btnRT = MkRect(_rightPage, "CombBtn");
            var combineImage = MkImg(btnRT, c(0.10f, 0.18f, 0.20f, 0.9f));
            NativeUiStyle.Apply(combineImage, NativeUiStyle.Role.Button,
                c(0.10f, 0.18f, 0.20f, 0.9f));
            btnRT.anchorMin = V(0, 1); btnRT.anchorMax = V(0.52f, 1);
            btnRT.pivot = V(0.5f, 1);
            btnRT.offsetMin = V(6, -(btnY + 30));
            btnRT.offsetMax = V(0, -btnY);
            _combineBtn = btnRT.gameObject.AddComponent<Button>();
            _combineBtn.targetGraphic = btnRT.GetComponent<Image>();
            _combineBtn.onClick.AddListener(OnCombineClick);
            _combineBtnText = MkTxt(btnRT, "Select a recipe", 12, C_Dim, TextAnchor.MiddleCenter);
            _combineBtnText.fontStyle = FontStyle.Bold;
            Fill(_combineBtnText.rectTransform);

            // Deconstruct button (right half)
            var dRT = MkRect(_rightPage, "DeconBtn");
            var deconstructImage = MkImg(dRT, c(0.18f, 0.12f, 0.10f, 0.9f));
            NativeUiStyle.Apply(deconstructImage, NativeUiStyle.Role.Button,
                c(0.18f, 0.12f, 0.10f, 0.9f));
            dRT.anchorMin = V(0.53f, 1); dRT.anchorMax = V(1, 1);
            dRT.pivot = V(0.5f, 1);
            dRT.offsetMin = V(0, -(btnY + 30));
            dRT.offsetMax = V(-6, -btnY);
            _deconBtn = dRT.gameObject.AddComponent<Button>();
            _deconBtn.targetGraphic = dRT.GetComponent<Image>();
            _deconBtn.onClick.AddListener(OnDeconstructClick);
            _deconBtnText = MkTxt(dRT, "Deconstruct", 12, C_Dim, TextAnchor.MiddleCenter);
            _deconBtnText.fontStyle = FontStyle.Bold;
            Fill(_deconBtnText.rectTransform);

            // Status text (between button and footer)
            _combineStatusText = MkTxt(_rightPage, "", 11, C_Dim, TextAnchor.MiddleCenter);
            StretchH(_combineStatusText.rectTransform, btnY + 32, 14, 6, 6);
            _combineStatusText.richText = true;

            // Footer (at the very bottom, no overlap)
            _footerText = MkTxt(_rightPage, "", 9, c(0.35f, 0.45f, 0.48f), TextAnchor.MiddleCenter);
            StretchH(_footerText.rectTransform, H - 16, 14, 6, 6);
        } // end Build()

        RectTransform BuildNativePage(Transform parent, string name, float x)
        {
            var page = MkRect(parent, name);
            var image = MkImg(page, C_WinBg);
            NativeUiStyle.Apply(image, NativeUiStyle.Role.Window, C_WinBg);
            page.anchorMin = V(0, 1);
            page.anchorMax = V(0, 1);
            page.pivot = V(0, 1);
            page.anchoredPosition = V(x, 0);
            page.sizeDelta = V(300, H);
            return page;
        }

        // ══════════════════════════════════════════════════════════
        // REFRESH — update all dynamic text/bars every frame
        // ══════════════════════════════════════════════════════════

        void Refresh()
        {
            var skill = GetSkillEntry();
            if (skill == null) return;
            int max = SkillsPlugin.CfgGlobalMaxLevel.Value;

            _titleText.text = _activeTradeskill;
            _skillLevelText.text = $"Skill: {skill.Level}/{max}";

            // XP bar
            if (skill.IsMaxLevel)
            {
                SetProgressFill(_xpBarFill, 1f);
                _xpBarText.text = "";
            }
            else
            {
                float f = Mathf.Clamp01(skill.LevelProgress);
                SetProgressFill(_xpBarFill, f);
                _xpBarText.text = "";
            }

            // Combine bar
            if (_isCombining)
            {
                float p = Mathf.Clamp01((Time.time - _combineStartTime) / COMBINE_DUR);
                SetProgressFill(_combineBarFill, p);
                _combineBarText.text = "Combining...";
            }
            else
            {
                SetProgressFill(_combineBarFill, 0f);
                _combineBarText.text = "";
            }

            // Populate recipe list
            PopulateRecipeList(skill);

            // Populate detail panel
            if (_selectedRecipeIndex >= 0 && _selectedRecipeIndex < _filtered.Count)
            {
                var r = _filtered[_selectedRecipeIndex];
                bool canAttempt = skill.Level >= r.MinSkillLevel;
                bool trivial = skill.Level >= r.TrivialLevel;

                _detailName.text = r.Name;
                _detailDesc.text = r.ResultDescription ?? "";

                // Set icon
                var iconSprite = GetItemIcon(r.Name);
                if (iconSprite != null)
                {
                    _detailIcon.sprite = iconSprite;
                    _detailIcon.color = Color.white;
                }
                else
                {
                    _detailIcon.sprite = null;
                    _detailIcon.color = Color.clear;
                }
                // Attach/update tooltip on icon
                var tt = _detailIconRT.GetComponent<RecipeTooltip>();
                if (tt == null) tt = _detailIconRT.gameObject.AddComponent<RecipeTooltip>();
                tt.ItemName = r.Name;
                _detailReqSkill.text = canAttempt
                    ? $"Required: <color=#4CAF50>{r.MinSkillLevel}</color>"
                    : $"Required: <color=#EF5350>{r.MinSkillLevel}</color> (need {r.MinSkillLevel - skill.Level} more)";
                _detailTrivial.text = trivial
                    ? $"Trivial: <color=#666666>{r.TrivialLevel} (no XP)</color>"
                    : $"Trivial at: {r.TrivialLevel}";

                if (canAttempt)
                {
                    float ch = Skills.Tradeskills.GetSuccessChance(skill, r);
                    Color cc = ch >= 70f ? C_Green : (ch >= 40f ? C_Accent : C_Red);
                    _detailChance.text = $"Success: {ch:F0}%";
                    _detailChance.color = cc;
                }
                else { _detailChance.text = ""; }

                _detailValue.text = $"Value: {r.GoldValue}g";
                PopulateMaterials(r);

                // Combine button state
                bool hasAll = HasAllMaterials(r);
                bool canCombine = canAttempt && hasAll && !_isCombining;
                _combineBtn.interactable = canCombine;
                _combineBtnText.text = canCombine ? "Combine" : "Cannot Combine";
                _combineBtnText.color = canCombine ? C_Gold : C_Dim;

                // Deconstruct button: need the item in inventory + know the recipe
                bool hasItem = CountPlayerItem(r.Name) > 0;
                _deconBtn.interactable = hasItem && !_isCombining;
                _deconBtnText.text = hasItem ? "Deconstruct" : "No Item";
                _deconBtnText.color = hasItem ? C_Accent : C_Dim;
            }
            else
            {
                _detailName.text = ""; _detailDesc.text = "";
                _detailReqSkill.text = ""; _detailTrivial.text = "";
                _detailChance.text = ""; _detailValue.text = "";
                ClearMaterials();
                _combineBtn.interactable = false;
                _deconBtn.interactable = false;
                _combineBtnText.text = "Select a recipe"; _combineBtnText.color = C_Dim;
                _deconBtnText.text = "Deconstruct"; _deconBtnText.color = C_Dim;
                // Clear icon
                if (_detailIcon != null) { _detailIcon.sprite = null; _detailIcon.color = Color.clear; }
            }

            _footerText.text = "";
        }

        // ══════════════════════════════════════════════════════════
        // RECIPE LIST POPULATION
        // ══════════════════════════════════════════════════════════

        List<GameObject> _recipeRows = new List<GameObject>();
        string _recipeRowsSignature = "";

        // Icon cache for recipe result items
        static Dictionary<string, Sprite> _iconCache = new Dictionary<string, Sprite>();
        static HashSet<Sprite> _ownedIconSprites = new HashSet<Sprite>();
        static string _iconDir;

        static Sprite GetItemIcon(string itemName)
        {
            if (string.IsNullOrEmpty(itemName)) return null;
            if (_iconCache.TryGetValue(itemName, out var cached)) return cached;

            try
            {
                Item item = Items.ItemFactory.GetItem(itemName);
                if (item == null && GameData.ItemDB?.ItemDB != null)
                    item = GameData.ItemDB.ItemDB.FirstOrDefault(candidate =>
                        candidate != null && string.Equals(candidate.ItemName, itemName,
                            StringComparison.OrdinalIgnoreCase));
                if (item?.ItemIcon != null)
                {
                    _iconCache[itemName] = item.ItemIcon;
                    return item.ItemIcon;
                }
            }
            catch { }

            if (_iconDir == null)
                _iconDir = System.IO.Path.Combine(
                    RuntimePaths.AssetDirectory, "Icons");

            string path = System.IO.Path.Combine(_iconDir, itemName + ".png");
            if (!System.IO.File.Exists(path)) return null;

            Texture2D tex = null;
            try
            {
                byte[] data = System.IO.File.ReadAllBytes(path);
                tex = new Texture2D(64, 64, TextureFormat.RGBA32, false);
                tex.filterMode = FilterMode.Bilinear;
                if (!tex.LoadImage(data)) throw new InvalidOperationException("Invalid image data.");
                var sprite = Sprite.Create(tex, new UnityEngine.Rect(0, 0, tex.width, tex.height),
                    new Vector2(0.5f, 0.5f));
                _ownedIconSprites.Add(sprite);
                _iconCache[itemName] = sprite;
                return sprite;
            }
            catch
            {
                if (tex) UnityEngine.Object.Destroy(tex);
                return null;
            }
        }

        void PopulateRecipeList(SkillEntry skill)
        {
            string signature = skill.Level + "|" + _selectedRecipeIndex + "|" +
                string.Join("\u001f", _filtered.Select(recipe => recipe?.Name ?? ""));
            if (_recipeRows.Count == _filtered.Count && _recipeRowsSignature == signature) return;

            ClearRecipeRows();
            float ry = 2;
            for (int i = 0; i < _filtered.Count; i++)
            {
                var recipe = _filtered[i];
                bool canAttempt = skill.Level >= recipe.MinSkillLevel;
                bool trivial = skill.Level >= recipe.TrivialLevel;
                int idx = i; // capture for closure

                var rowRT = MkRect(_recipeContent, recipe.Name);
                var rowImage = MkImg(rowRT, Color.white);
                NativeUiStyle.Apply(rowImage, NativeUiStyle.Role.Tab,
                    Color.white);
                rowImage.color = idx == _selectedRecipeIndex
                    ? c(0.78f, 1f, 0.95f, 1f) : Color.white;
                rowRT.anchorMin = V(0, 1); rowRT.anchorMax = V(1, 1);
                rowRT.pivot = V(0.5f, 1);
                rowRT.offsetMin = V(2, -(ry + 36));
                rowRT.offsetMax = V(-2, -ry);

                // Level tag (right side)
                string lvl = trivial ? "<color=#666>T</color>"
                    : canAttempt ? $"<color=#4CAF50>{recipe.MinSkillLevel}</color>"
                    : $"<color=#EF5350>{recipe.MinSkillLevel}</color>";
                var lvlTxt = MkTxt(rowRT, lvl, 12, Color.white, TextAnchor.MiddleRight);
                lvlTxt.richText = true;
                lvlTxt.rectTransform.anchorMin = V(0.85f, 0);
                lvlTxt.rectTransform.anchorMax = V(1, 1);
                lvlTxt.rectTransform.offsetMin = V(0, 0);
                lvlTxt.rectTransform.offsetMax = V(-2, 0);

                // Recipe name
                Color nameCol = idx == _selectedRecipeIndex ? C_Gold
                    : canAttempt ? Color.white : C_Dim;
                var nameTxt = MkTxt(rowRT, recipe.Name, 13, nameCol, TextAnchor.MiddleLeft);
                if (idx == _selectedRecipeIndex) nameTxt.fontStyle = FontStyle.Bold;
                nameTxt.rectTransform.anchorMin = V(0, 0);
                nameTxt.rectTransform.anchorMax = V(0.85f, 1);
                nameTxt.rectTransform.offsetMin = V(4, 0);
                nameTxt.rectTransform.offsetMax = V(0, 0);

                // Click handler
                var clickBtn = rowRT.gameObject.AddComponent<Button>();
                clickBtn.targetGraphic = rowRT.GetComponent<Image>();
                clickBtn.onClick.AddListener(() =>
                {
                    _selectedRecipeIndex = idx;
                    _combineStatusText.text = "";
                    ClearRecipeRows();
                });

                _recipeRows.Add(rowRT.gameObject);
                ry += 36;
            }

            _recipeContent.sizeDelta = V(0, ry + 2);
            _recipeRowsSignature = signature;
        }

        void ClearRecipeRows()
        {
            foreach (var go in _recipeRows) if (go) Destroy(go);
            _recipeRows.Clear();
            _recipeRowsSignature = "";
        }

        // ══════════════════════════════════════════════════════════
        // MATERIALS DISPLAY
        // ══════════════════════════════════════════════════════════

        List<GameObject> _matRows = new List<GameObject>();

        void PopulateMaterials(Skills.Tradeskills.Recipe recipe)
        {
            ClearMaterials();
            float my = 0;
            for (int i = 0; i < recipe.Materials.Length; i++)
            {
                string matName = recipe.Materials[i];
                int required = recipe.MaterialCounts[i];
                int have = CountPlayerItem(matName);
                bool enough = have >= required;

                // Indicator
                var indTxt = MkTxt(_matsContainer, enough ? "\u2713" : "\u2717", 11,
                    enough ? C_Green : C_Red, TextAnchor.MiddleLeft);
                indTxt.fontStyle = FontStyle.Bold;
                indTxt.rectTransform.anchorMin = V(0, 1);
                indTxt.rectTransform.anchorMax = V(0.08f, 1);
                indTxt.rectTransform.pivot = V(0, 1);
                indTxt.rectTransform.offsetMin = V(0, -(my + 16));
                indTxt.rectTransform.offsetMax = V(0, -my);
                _matRows.Add(indTxt.gameObject);

                // Name
                var nTxt = MkTxt(_matsContainer, matName, 11,
                    enough ? Color.white : C_Red, TextAnchor.MiddleLeft);
                nTxt.rectTransform.anchorMin = V(0.08f, 1);
                nTxt.rectTransform.anchorMax = V(0.78f, 1);
                nTxt.rectTransform.pivot = V(0, 1);
                nTxt.rectTransform.offsetMin = V(0, -(my + 16));
                nTxt.rectTransform.offsetMax = V(0, -my);
                _matRows.Add(nTxt.gameObject);

                // Count
                var cTxt = MkTxt(_matsContainer, $"{have}/{required}", 11,
                    enough ? C_Green : C_Red, TextAnchor.MiddleRight);
                cTxt.fontStyle = FontStyle.Bold;
                cTxt.rectTransform.anchorMin = V(0.78f, 1);
                cTxt.rectTransform.anchorMax = V(1, 1);
                cTxt.rectTransform.pivot = V(1, 1);
                cTxt.rectTransform.offsetMin = V(0, -(my + 16));
                cTxt.rectTransform.offsetMax = V(0, -my);
                _matRows.Add(cTxt.gameObject);

                my += 18;
            }
        }

        void ClearMaterials()
        {
            foreach (var go in _matRows) if (go) Destroy(go);
            _matRows.Clear();
        }

        // ══════════════════════════════════════════════════════════
        // COMBINE LOGIC
        // ══════════════════════════════════════════════════════════

        void OnCombineClick()
        {
            if (_isCombining) return;
            if (_selectedRecipeIndex < 0 || _selectedRecipeIndex >= _filtered.Count) return;

            var recipe = _filtered[_selectedRecipeIndex];
            var skill = GetSkillEntry();
            if (skill == null) return;

            bool canAttempt = skill.Level >= recipe.MinSkillLevel;
            bool hasAll = HasAllMaterials(recipe);

            if (!canAttempt)
            {
                _combineStatusText.text = $"<color=#EF5350>Need {_activeTradeskill} level {recipe.MinSkillLevel}.</color>";
                return;
            }
            if (!hasAll)
            {
                _combineStatusText.text = "<color=#EF5350>Missing required materials.</color>";
                return;
            }

            _isCombining = true;
            _combineStartTime = Time.time;
            _combineStatusText.text = "";
        }

        void ExecuteCombine()
        {
            if (_selectedRecipeIndex < 0 || _selectedRecipeIndex >= _filtered.Count) return;
            var recipe = _filtered[_selectedRecipeIndex];
            Skills.Tradeskills.AttemptCombine(_activeTradeskill, recipe.Name);
            RefreshRecipeList();
            ClearRecipeRows();
            ClearMaterials();
        }

        void OnDeconstructClick()
        {
            if (_isCombining) return;
            if (_selectedRecipeIndex < 0 || _selectedRecipeIndex >= _filtered.Count) return;

            var recipe = _filtered[_selectedRecipeIndex];

            // Check if player has the item
            int have = CountPlayerItem(recipe.Name);
            if (have <= 0)
            {
                _combineStatusText.text = $"<color=#EF5350>You don't have a {recipe.Name} to deconstruct.</color>";
                return;
            }

            Skills.Tradeskills.AttemptDeconstruct(_activeTradeskill, recipe.Name);
            RefreshRecipeList();
            ClearRecipeRows();
            ClearMaterials();
        }

        // ══════════════════════════════════════════════════════════
        // RECIPE FILTERING
        // ══════════════════════════════════════════════════════════

        static void RefreshRecipeList()
        {
            string selectedName = _selectedRecipeIndex >= 0 && _selectedRecipeIndex < _filtered.Count
                ? _filtered[_selectedRecipeIndex]?.Name
                : null;
            _filtered.Clear();
            var all = Skills.Tradeskills.GetRecipesForSkillPublic(_activeTradeskill);
            if (all == null)
            {
                _selectedRecipeIndex = -1;
                if (_instance && _instance._built) _instance.ClearRecipeRows();
                return;
            }

            var skill = GetSkillEntry();
            string search = (_searchText ?? "").ToLowerInvariant();

            foreach (var r in all)
            {
                // Only show recipes the player has learned
                if (!_qaShowAllRecipes &&
                    !SkillsSaveManager.Data.IsRecipeKnown(r.Name)) continue;
                if (search.Length > 0 &&
                    !(r.Name ?? "").ToLowerInvariant().Contains(search)) continue;
                if (_showOnlyAvailable)
                {
                    if (skill == null || skill.Level < r.MinSkillLevel) continue;
                    if (!HasAllMaterials(r)) continue;
                }
                _filtered.Add(r);
            }
            _selectedRecipeIndex = string.IsNullOrEmpty(selectedName)
                ? -1
                : _filtered.FindIndex(recipe => string.Equals(recipe?.Name, selectedName,
                    StringComparison.OrdinalIgnoreCase));
            if (_instance && _instance._built) _instance.ClearRecipeRows();
        }

        void UpdateCraftableToggleVisual()
        {
            if (_craftableToggleLabel)
            {
                _craftableToggleLabel.text = _showOnlyAvailable ? "Ready \u2713" : "Ready";
                _craftableToggleLabel.color = _showOnlyAvailable ? C_Green : C_Dim;
            }
            if (_craftableToggleImage)
                _craftableToggleImage.color = _showOnlyAvailable
                    ? c(0.78f, 1f, 0.82f, 1f)
                    : Color.white;
        }

        // ══════════════════════════════════════════════════════════
        // HELPERS
        // ══════════════════════════════════════════════════════════

        static SkillEntry GetSkillEntry()
        {
            var d = SkillsSaveManager.Data;
            switch (_activeTradeskill)
            {
                case "Smithing":   return d.Smithing;
                case "Baking":     return d.Baking;
                case "Brewing":    return d.Brewing;
                case "Fletching":  return d.Fletching;
                case "Jewelcraft": return d.Jewelcraft;
                case "Tailoring":  return d.Tailoring;
                default: return null;
            }
        }

        RectTransform CreateProgressFill(RectTransform track, string name,
            Color color, float horizontalInset, float verticalInset)
        {
            var clip = MkRect(track, name + "Clip");
            MkImg(clip, Color.clear).raycastTarget = false;
            Fill(clip);
            clip.offsetMin = V(horizontalInset, verticalInset);
            clip.offsetMax = V(-horizontalInset, -verticalInset);
            clip.gameObject.AddComponent<RectMask2D>();

            var fill = MkRect(clip, name);
            MkImg(fill, color).raycastTarget = false;
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

        static string GetIcon()
        {
            switch (_activeTradeskill)
            {
                case "Smithing":   return "\u2692";
                case "Baking":     return "\u25CB";
                case "Brewing":    return "\u2615";
                case "Fletching":  return "\u2191";
                case "Jewelcraft": return "\u25C6";
                case "Tailoring":  return "\u2702";
                default: return "\u2699";
            }
        }

        static int GetRecipeCostUI(int trivialLevel)
        {
            if (trivialLevel <= 20) return 10;
            if (trivialLevel <= 40) return 50;
            if (trivialLevel <= 60) return 150;
            if (trivialLevel <= 80) return 400;
            if (trivialLevel <= 100) return 800;
            if (trivialLevel <= 120) return 1500;
            if (trivialLevel <= 140) return 3000;
            if (trivialLevel <= 160) return 6000;
            return 10000;
        }

        /// <summary>
        /// Count total of an item across inventory, bags (if enabled), and bank (if enabled).
        /// Mirrors the same logic as Tradeskills.CountMaterialAll.
        /// </summary>
        static int CountPlayerItem(string itemName)
        {
            long found = 0;

            // 1. Inventory
            try
            {
                var inv = GameData.PlayerInv;
                if (inv?.StoredSlots != null)
                {
                    foreach (var slot in inv.StoredSlots)
                    {
                        if (slot?.MyItem != null && slot.MyItem.ItemName == itemName)
                            found += slot.MyItem.Stackable ? Mathf.Max(1, slot.Quantity) : 1;
                    }
                }
            }
            catch { }

            // 2. Bags (if enabled)
            if (SkillsPlugin.CfgCraftFromBags.Value)
            {
                try { found += Items.BagSystem.CountItemInAllBags(itemName); }
                catch { }
            }

            // 3. Bank (if enabled) — reads GlobalBank.StoredItems
            if (SkillsPlugin.CfgCraftFromBank.Value)
            {
                try
                {
                    var bankItems = Skills.Tradeskills.GetBankItemsSnapshot();
                    if (bankItems != null)
                    {
                        foreach (var (name, qty) in bankItems)
                        {
                            if (name == itemName)
                                found += qty;
                        }
                    }
                }
                catch { }
            }

            return found > int.MaxValue ? int.MaxValue : (int)found;
        }

        static bool HasAllMaterials(Skills.Tradeskills.Recipe recipe)
        {
            for (int i = 0; i < recipe.Materials.Length; i++)
                if (CountPlayerItem(recipe.Materials[i]) < recipe.MaterialCounts[i])
                    return false;
            return true;
        }

        void SetVis(bool v)
        {
            if (_canvasGO) _canvasGO.SetActive(v);
        }

        // ══════════════════════════════════════════════════════════
        // UI PRIMITIVES (same as SkillsUI)
        // ══════════════════════════════════════════════════════════

        static Texture2D _wt; static Sprite _ws;
        static Sprite WS { get {
            if (!_ws) { if (!_wt) { _wt = new Texture2D(4,4);
                var p = new Color[16]; for (int i=0;i<16;i++) p[i]=Color.white;
                _wt.SetPixels(p); _wt.Apply(); }
                _ws = Sprite.Create(_wt, new UnityEngine.Rect(0,0,4,4), V(0.5f,0.5f)); }
            return _ws; } }

        static Vector2 V(float x, float y) => new Vector2(x, y);

        static RectTransform MkRect(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.AddComponent<RectTransform>();
        }

        static Image MkImg(RectTransform rt, Color color)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = WS; img.color = color;
            img.type = Image.Type.Simple; img.raycastTarget = true;
            return img;
        }

        static TextMeshProUGUI MkTxt(Transform parent, string text, int size,
            Color color, TextAnchor anchor)
        {
            var rt = MkRect(parent, "T");
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

        static void StretchH(RectTransform rt, float y, float h,
            float padL = 0, float padR = 0)
        {
            rt.anchorMin = V(0, 1); rt.anchorMax = V(1, 1);
            rt.pivot = V(0.5f, 1);
            rt.offsetMin = V(padL, -(y + h));
            rt.offsetMax = V(-padR, -y);
        }

        /// <summary>Place rect inside parent at Y from top, stretching width with padding.</summary>
        static void PlaceIn(RectTransform rt, float y, float h, float padL, float padR)
        {
            rt.anchorMin = V(0, 1); rt.anchorMax = V(1, 1);
            rt.pivot = V(0.5f, 1);
            rt.offsetMin = V(padL, -(y + h));
            rt.offsetMax = V(-padR, -y);
        }

        // ── Helper components (reused from SkillsUI pattern) ──────

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

        /// <summary>Uses the game's native ItemInfoWindow to show item tooltips on hover.</summary>
        class RecipeTooltip : MonoBehaviour,
            IPointerEnterHandler, IPointerExitHandler
        {
            public string ItemName;

            public void OnPointerEnter(PointerEventData e)
            {
                try
                {
                    if (GameData.ItemInfoWindow == null) return;
                    if (string.IsNullOrEmpty(ItemName)) return;

                    // Find the Item object — check our custom items first, then game DB
                    Item item = Items.ItemFactory.GetItem(ItemName);
                    if (item == null)
                    {
                        // Search game's ItemDB by name
                        var db = GameData.ItemDB;
                        if (db?.ItemDB != null)
                        {
                            foreach (var gi in db.ItemDB)
                            {
                                if (gi != null && gi.ItemName == ItemName)
                                {
                                    item = gi;
                                    break;
                                }
                            }
                        }
                    }

                    if (item == null) return;

                    // Position tooltip near the icon
                    Vector2 pos = (Vector2)Input.mousePosition + new Vector2(20f, 0f);
                    GameData.ItemInfoWindow.DisplayItem(item, pos, 1);

                    // Bring tooltip canvas to front so it's above our window
                    var tooltipCanvas = GameData.ItemInfoWindow.GetComponentInParent<Canvas>();
                    if (tooltipCanvas != null && tooltipCanvas.sortingOrder <= 101)
                        tooltipCanvas.sortingOrder = 200;
                }
                catch { }
            }

            public void OnPointerExit(PointerEventData e)
            {
                try
                {
                    if (GameData.ItemInfoWindow != null)
                        GameData.ItemInfoWindow.CloseItemWindow();
                }
                catch { }
            }
        }
    }
}
