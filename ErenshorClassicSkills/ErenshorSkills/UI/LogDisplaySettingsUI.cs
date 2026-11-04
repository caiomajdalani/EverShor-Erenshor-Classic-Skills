using System;
using System.Collections.Generic;
using ErenshorSkills.Features;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using FontStyle = TMPro.FontStyles;

namespace ErenshorSkills.UI
{
    /// <summary>
    /// Native-styled in-game editor for EverQuest-style chat and combat-log
    /// presentation. Changes are written through the native Lunaris
    /// config entries immediately; no separate save format is introduced.
    /// </summary>
    internal sealed class LogDisplaySettingsUI : MonoBehaviour
    {
        private sealed class CategoryRow
        {
            internal int Index;
            internal RectTransform Root;
            internal Image Background;
            internal Image Selection;
            internal Image Swatch;
            internal TextMeshProUGUI NativeMarker;
            internal TextMeshProUGUI Label;
        }

        private static LogDisplaySettingsUI _instance;
        private static bool _showWindow;

        private readonly List<CategoryRow> _rows =
            new List<CategoryRow>();

        private GameObject _canvasObject;
        private RectTransform _window;
        private RectTransform _leftPage;
        private RectTransform _rightPage;
        private RectTransform _categoryContent;
        private ScrollRect _categoryScroll;
        private Toggle _colorsToggle;
        private Toggle _damageToggle;
        private TextMeshProUGUI _selectedTitle;
        private TextMeshProUGUI _sampleText;
        private TextMeshProUGUI _hexText;
        private Image _sampleSwatch;
        private Slider _redSlider;
        private Slider _greenSlider;
        private Slider _blueSlider;
        private TextMeshProUGUI _redValue;
        private TextMeshProUGUI _greenValue;
        private TextMeshProUGUI _blueValue;
        private TextMeshProUGUI _statusText;
        private bool _built;
        private bool _refreshing;
        private int _selectedIndex;
        private float _nextExternalRefresh;
        private float _statusUntil;

        private const float WindowWidth = 770f;
        private const float WindowHeight = 650f;
        private const float LeftWidth = 350f;
        private const float RightWidth = WindowWidth - LeftWidth;
        private const float TitleHeight = 52f;
        private const float CategoryRowHeight = 34f;
        private const float CategoryGap = 2f;
        private const float CategoryEdgePadding = 2f;
        private const int FullyVisibleCategoryRows = 16;
        private const float CategoryListHeight = CategoryEdgePadding +
            FullyVisibleCategoryRows * (CategoryRowHeight + CategoryGap);

        private static readonly Color WindowFallback =
            new Color(0.08f, 0.12f, 0.15f, 0.97f);
        private static readonly Color PanelFallback =
            new Color(0.10f, 0.16f, 0.20f, 0.94f);
        private static readonly Color InputFallback =
            new Color(0.04f, 0.07f, 0.09f, 0.90f);
        private static readonly Color BorderColor =
            new Color(0.22f, 0.36f, 0.40f, 0.95f);
        private static readonly Color GoldColor =
            new Color(1f, 0.84f, 0f, 1f);
        private static readonly Color DimColor =
            new Color(0.62f, 0.70f, 0.72f, 1f);
        private static readonly Color GreenColor =
            new Color(0.30f, 0.69f, 0.31f, 1f);
        private static readonly Color RedColor =
            new Color(0.94f, 0.33f, 0.31f, 1f);

        private static readonly string[] PresetColors =
        {
            "#F2F2F2", "#BDBDBD", "#FF6666", "#FF9D3D",
            "#FFD24A", "#66F28A", "#55E0A4", "#55E6E6",
            "#84C8FF", "#8080FF", "#CAA8FF", "#FF75D8",
            "#B030B0", "#50C878", "#FF704F", "#D5A6FF"
        };

        internal static bool IsOpen => _showWindow;
        internal static int CategoryRowCountForTesting =>
            _instance ? _instance._rows.Count : 0;
        internal static int SelectedIndexForTesting =>
            _instance ? _instance._selectedIndex : -1;

        internal static void Open()
        {
            EnsureInstance();
            _showWindow = true;
            SkillsUI.CloseWindow();
            if (_instance)
            {
                _instance.TryBuildWhenReady();
                _instance.SetVisible(true);
                _instance.RefreshAll();
            }
        }

        internal static void Close()
        {
            _showWindow = false;
            if (_instance) _instance.SetVisible(false);
        }

        internal static void Shutdown()
        {
            _showWindow = false;
            LogDisplaySettingsUI value = _instance;
            _instance = null;
            if (value && value.gameObject != SkillsPlugin.Instance?.gameObject)
                Destroy(value.gameObject);
        }

        private static void EnsureInstance()
        {
            if (_instance) return;
            GameObject host = SkillsPlugin.Instance?.gameObject;
            if (host)
            {
                _instance = host.GetComponent<LogDisplaySettingsUI>();
                if (!_instance)
                    _instance = host.AddComponent<LogDisplaySettingsUI>();
                return;
            }
            var standalone = new GameObject("ClassicSkillsLogSettingsHost");
            DontDestroyOnLoad(standalone);
            _instance = standalone.AddComponent<LogDisplaySettingsUI>();
        }

        private void Awake()
        {
            if (_instance && !ReferenceEquals(_instance, this))
            {
                Destroy(this);
                return;
            }
            _instance = this;
        }

        private void Start()
        {
            TryBuildWhenReady();
            UnityEngine.SceneManagement.SceneManager.sceneLoaded +=
                OnSceneLoaded;
        }

        private void LateUpdate()
        {
            if (!_built) TryBuildWhenReady();
            if (!_built || !_showWindow || !_canvasObject ||
                !_canvasObject.activeSelf) return;

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Close();
                return;
            }

            if (Time.unscaledTime >= _nextExternalRefresh)
            {
                _nextExternalRefresh = Time.unscaledTime + 0.5f;
                RefreshAll();
            }
            if (_statusText && _statusUntil > 0f &&
                Time.unscaledTime >= _statusUntil)
            {
                _statusText.text = "Changes save immediately.";
                _statusText.color = DimColor;
                _statusUntil = 0f;
            }
        }

        private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene,
            UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            if (_showWindow) Close();
        }

        private void OnDestroy()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -=
                OnSceneLoaded;
            if (_canvasObject) Destroy(_canvasObject);
            if (ReferenceEquals(_instance, this)) _instance = null;
            _showWindow = false;
            if (_whiteSprite) Destroy(_whiteSprite);
            if (_whiteTexture) Destroy(_whiteTexture);
            _whiteSprite = null;
            _whiteTexture = null;
        }

        private void TryBuildWhenReady()
        {
            if (_built || !NativeUiStyle.NativeSourcesAvailable ||
                LogDisplayOptions.ColorCategoryCount <= 0 ||
                !NativeEventSystemGate.IsReady) return;
            try
            {
                Build();
                _built = true;
                SetVisible(_showWindow);
                SkillsPlugin.Log.LogInfo(
                    "Log color settings UI built with " + _rows.Count +
                    " selectable categories.");
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogError(
                    "Log color settings UI build failed: " + ex);
            }
        }

        private void Build()
        {
            _canvasObject = new GameObject("CS_LogSettings_Canvas");
            DontDestroyOnLoad(_canvasObject);
            Canvas canvas = _canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 103;
            CanvasScaler scaler = _canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            _canvasObject.AddComponent<GraphicRaycaster>();

            _window = MakeRect(_canvasObject.transform, "LogSettingsBook");
            MakeImage(_window, Color.clear);
            _window.anchorMin = new Vector2(0.5f, 0.5f);
            _window.anchorMax = new Vector2(0.5f, 0.5f);
            _window.pivot = new Vector2(0.5f, 0.5f);
            _window.anchoredPosition = Vector2.zero;
            _window.sizeDelta = new Vector2(WindowWidth, WindowHeight);
            _window.gameObject.AddComponent<InputBlocker>();

            _leftPage = BuildPage(_window, "CategoryPage", 0f, LeftWidth);
            _rightPage = BuildPage(_window, "PickerPage", LeftWidth,
                RightWidth);

            BuildTitle(_leftPage, "Extra Settings", false);
            BuildTitle(_rightPage, "Chat & Combat Log", true);
            BuildCategoryList();
            BuildPicker();

            _selectedIndex = Mathf.Clamp(_selectedIndex, 0,
                Mathf.Max(0, LogDisplayOptions.ColorCategoryCount - 1));
            RefreshAll();
        }

        private RectTransform BuildPage(Transform parent, string name,
            float x, float width)
        {
            RectTransform page = MakeRect(parent, name);
            Image image = MakeImage(page, WindowFallback);
            NativeUiStyle.Apply(image, NativeUiStyle.Role.Window,
                WindowFallback);
            page.anchorMin = new Vector2(0f, 1f);
            page.anchorMax = new Vector2(0f, 1f);
            page.pivot = new Vector2(0f, 1f);
            page.anchoredPosition = new Vector2(x, 0f);
            page.sizeDelta = new Vector2(width, WindowHeight);
            // Native tab sprites and outlines can extend a few pixels past a
            // ScrollRect mask. Clip every page descendant at the actual book
            // edge so no category strip renders below the window border.
            page.gameObject.AddComponent<RectMask2D>();
            return page;
        }

        private void BuildTitle(RectTransform page, string title,
            bool includeClose)
        {
            RectTransform drag = MakeRect(page, "TitleDrag");
            MakeImage(drag, Color.clear);
            PlaceFromTop(drag, 0f, TitleHeight, 0f, 0f);
            WindowDragger dragger = drag.gameObject.AddComponent<WindowDragger>();
            dragger.Target = _window;

            TextMeshProUGUI text = MakeText(drag, title, 27,
                Color.white, TextAlignmentOptions.Center);
            text.fontStyle = FontStyle.Bold;
            Fill(text.rectTransform);

            if (!includeClose) return;
            RectTransform closeRect = MakeRect(page, "Close");
            Image closeImage = MakeImage(closeRect, RedColor);
            NativeUiStyle.Apply(closeImage, NativeUiStyle.Role.Button,
                RedColor);
            closeImage.color = new Color(0.72f, 0.16f, 0.16f, 0.95f);
            closeRect.anchorMin = new Vector2(1f, 1f);
            closeRect.anchorMax = new Vector2(1f, 1f);
            closeRect.pivot = new Vector2(1f, 1f);
            closeRect.anchoredPosition = new Vector2(-7f, -7f);
            closeRect.sizeDelta = new Vector2(28f, 24f);
            Button button = closeRect.gameObject.AddComponent<Button>();
            button.targetGraphic = closeImage;
            button.navigation = NoNavigation();
            button.onClick.AddListener(Close);
            TextMeshProUGUI closeText = MakeText(closeRect, "X", 15,
                Color.white, TextAlignmentOptions.Center);
            closeText.fontStyle = FontStyle.Bold;
            Fill(closeText.rectTransform);
        }

        private void BuildCategoryList()
        {
            RectTransform scrollRoot = MakeRect(_leftPage, "CategoryScroll");
            MakeImage(scrollRoot, Color.clear);
            // Keep the viewport on an exact row boundary. The previous 590 px
            // height exposed the top of row 17 below sixteen complete rows,
            // which made the category list look as though it escaped the page.
            PlaceFromTop(scrollRoot, TitleHeight, CategoryListHeight, 7f, 7f);

            _categoryScroll = scrollRoot.gameObject.AddComponent<ScrollRect>();
            _categoryScroll.horizontal = false;
            _categoryScroll.vertical = true;
            _categoryScroll.movementType = ScrollRect.MovementType.Clamped;
            _categoryScroll.scrollSensitivity = 26f;

            RectTransform viewport = MakeRect(scrollRoot, "Viewport");
            MakeImage(viewport, Color.clear);
            Fill(viewport);
            viewport.offsetMax = new Vector2(-9f, 0f);
            viewport.gameObject.AddComponent<RectMask2D>();

            _categoryContent = MakeRect(viewport, "Content");
            _categoryContent.anchorMin = new Vector2(0f, 1f);
            _categoryContent.anchorMax = new Vector2(1f, 1f);
            _categoryContent.pivot = new Vector2(0.5f, 1f);
            _categoryContent.anchoredPosition = Vector2.zero;

            _rows.Clear();
            float y = CategoryEdgePadding;
            for (int index = 0;
                index < LogDisplayOptions.ColorCategoryCount; index++)
            {
                BuildCategoryRow(index, y);
                y += CategoryRowHeight + CategoryGap;
            }
            _categoryContent.sizeDelta = new Vector2(0f,
                y + CategoryEdgePadding);
            _categoryScroll.viewport = viewport;
            _categoryScroll.content = _categoryContent;

            RectTransform scrollbarRoot = MakeRect(scrollRoot, "Scrollbar");
            Image scrollbarImage = MakeImage(scrollbarRoot, InputFallback);
            NativeUiStyle.Apply(scrollbarImage, NativeUiStyle.Role.Input,
                InputFallback);
            scrollbarRoot.anchorMin = new Vector2(1f, 0f);
            scrollbarRoot.anchorMax = new Vector2(1f, 1f);
            scrollbarRoot.pivot = new Vector2(1f, 0.5f);
            scrollbarRoot.sizeDelta = new Vector2(7f, 0f);
            Scrollbar scrollbar = scrollbarRoot.gameObject
                .AddComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            RectTransform slidingArea = MakeRect(scrollbarRoot, "Area");
            Fill(slidingArea);
            RectTransform handle = MakeRect(slidingArea, "Handle");
            Image handleImage = MakeImage(handle, BorderColor);
            NativeUiStyle.Apply(handleImage, NativeUiStyle.Role.Button,
                BorderColor);
            Fill(handle);
            scrollbar.handleRect = handle;
            scrollbar.targetGraphic = handleImage;
            _categoryScroll.verticalScrollbar = scrollbar;
            _categoryScroll.verticalScrollbarVisibility =
                ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
        }

        private void BuildCategoryRow(int index, float y)
        {
            RectTransform root = MakeRect(_categoryContent,
                "Category_" + index);
            Image background = MakeImage(root, PanelFallback);
            NativeUiStyle.Apply(background, NativeUiStyle.Role.Tab,
                PanelFallback);
            PlaceFromTop(root, y, CategoryRowHeight, 1f, 1f);
            Button button = root.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            button.navigation = NoNavigation();
            int capturedIndex = index;
            button.onClick.AddListener(() => SelectCategory(capturedIndex));

            RectTransform selection = MakeRect(root, "Selected");
            Image selectionImage = MakeImage(selection, GoldColor);
            selectionImage.raycastTarget = false;
            selection.anchorMin = new Vector2(0f, 0f);
            selection.anchorMax = new Vector2(0f, 1f);
            selection.pivot = new Vector2(0f, 0.5f);
            selection.sizeDelta = new Vector2(4f, 0f);

            TextMeshProUGUI label = MakeText(root,
                LogDisplayOptions.GetColorDisplayName(index), 13,
                Color.white, TextAlignmentOptions.MidlineLeft);
            label.rectTransform.anchorMin = new Vector2(0f, 0f);
            label.rectTransform.anchorMax = new Vector2(1f, 1f);
            label.rectTransform.offsetMin = new Vector2(12f, 0f);
            label.rectTransform.offsetMax = new Vector2(-53f, 0f);

            RectTransform swatchRect = MakeRect(root, "Swatch");
            Image swatch = MakeImage(swatchRect, Color.white);
            swatch.raycastTarget = false;
            swatchRect.anchorMin = new Vector2(1f, 0.5f);
            swatchRect.anchorMax = new Vector2(1f, 0.5f);
            swatchRect.pivot = new Vector2(1f, 0.5f);
            swatchRect.anchoredPosition = new Vector2(-9f, 0f);
            swatchRect.sizeDelta = new Vector2(34f, 20f);
            Outline outline = swatchRect.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
            outline.effectDistance = new Vector2(1f, -1f);

            TextMeshProUGUI native = MakeText(swatchRect, "N", 11,
                Color.white, TextAlignmentOptions.Center);
            native.fontStyle = FontStyle.Bold;
            Fill(native.rectTransform);

            _rows.Add(new CategoryRow
            {
                Index = index,
                Root = root,
                Background = background,
                Selection = selectionImage,
                Swatch = swatch,
                NativeMarker = native,
                Label = label
            });
        }

        private void BuildPicker()
        {
            _colorsToggle = BuildToggle(_rightPage, "Use custom colors",
                56f, value =>
                {
                    if (_refreshing ||
                        LogDisplayOptions.EnableCustomColors == null) return;
                    LogDisplayOptions.EnableCustomColors.Value = value;
                    SetStatus(value
                        ? "Custom colors enabled for new log lines."
                        : "Native Erenshor log colors restored.", true);
                });
            _damageToggle = BuildToggle(_rightPage,
                "Show damage type after damage", 88f, value =>
                {
                    if (_refreshing ||
                        LogDisplayOptions.ShowDamageTypes == null) return;
                    LogDisplayOptions.ShowDamageTypes.Value = value;
                    SetStatus(value
                        ? "Damage types enabled for new damage lines."
                        : "Damage type labels disabled.", true);
                });

            _selectedTitle = MakeText(_rightPage, "", 20, GoldColor,
                TextAlignmentOptions.Center);
            _selectedTitle.fontStyle = FontStyle.Bold;
            PlaceFromTop(_selectedTitle.rectTransform, 124f, 27f, 12f, 12f);

            RectTransform samplePanel = MakeRect(_rightPage, "SamplePanel");
            Image samplePanelImage = MakeImage(samplePanel, PanelFallback);
            NativeUiStyle.Apply(samplePanelImage, NativeUiStyle.Role.Panel,
                PanelFallback);
            PlaceFromTop(samplePanel, 154f, 70f, 13f, 13f);

            RectTransform sampleSwatchRect = MakeRect(samplePanel,
                "LargeSwatch");
            _sampleSwatch = MakeImage(sampleSwatchRect, Color.white);
            _sampleSwatch.raycastTarget = false;
            sampleSwatchRect.anchorMin = new Vector2(0f, 0.5f);
            sampleSwatchRect.anchorMax = new Vector2(0f, 0.5f);
            sampleSwatchRect.pivot = new Vector2(0f, 0.5f);
            sampleSwatchRect.anchoredPosition = new Vector2(12f, 0f);
            sampleSwatchRect.sizeDelta = new Vector2(48f, 48f);
            Outline sampleOutline = sampleSwatchRect.gameObject
                .AddComponent<Outline>();
            sampleOutline.effectColor = new Color(0f, 0f, 0f, 0.9f);
            sampleOutline.effectDistance = new Vector2(1f, -1f);

            _sampleText = MakeText(samplePanel,
                "[Preview] New messages use this color.", 14, Color.white,
                TextAlignmentOptions.MidlineLeft);
            _sampleText.enableWordWrapping = true;
            _sampleText.rectTransform.anchorMin = new Vector2(0f, 0f);
            _sampleText.rectTransform.anchorMax = new Vector2(1f, 1f);
            _sampleText.rectTransform.offsetMin = new Vector2(72f, 5f);
            _sampleText.rectTransform.offsetMax = new Vector2(-10f, -5f);

            _hexText = MakeText(_rightPage, "", 15, Color.white,
                TextAlignmentOptions.Center);
            _hexText.fontStyle = FontStyle.Bold;
            PlaceFromTop(_hexText.rectTransform, 230f, 27f, 12f, 12f);

            BuildButton(_rightPage, "UseNative", "Use Native", 14f, 261f,
                187f, 31f, () =>
                {
                    if (LogDisplayOptions.TrySetConfiguredColor(
                            _selectedIndex, "", out string error))
                        SetStatus("This category now uses its native game color.",
                            true);
                    else SetStatus(error, false);
                    RefreshAll();
                });
            BuildButton(_rightPage, "ResetSelected", "Reset Default", 218f,
                261f, 187f, 31f, () =>
                {
                    if (LogDisplayOptions.ResetConfiguredColor(
                            _selectedIndex, out string error))
                        SetStatus("Default color restored for this category.",
                            true);
                    else SetStatus(error, false);
                    RefreshAll();
                });

            _redSlider = BuildRgbSlider(_rightPage, "R", 304f, RedColor,
                out _redValue);
            _greenSlider = BuildRgbSlider(_rightPage, "G", 345f,
                GreenColor, out _greenValue);
            _blueSlider = BuildRgbSlider(_rightPage, "B", 386f,
                new Color(0.35f, 0.60f, 1f, 1f), out _blueValue);
            _redSlider.onValueChanged.AddListener(_ => OnRgbChanged());
            _greenSlider.onValueChanged.AddListener(_ => OnRgbChanged());
            _blueSlider.onValueChanged.AddListener(_ => OnRgbChanged());

            TextMeshProUGUI presetTitle = MakeText(_rightPage,
                "Quick colors", 14, DimColor, TextAlignmentOptions.Center);
            presetTitle.fontStyle = FontStyle.Bold;
            PlaceFromTop(presetTitle.rectTransform, 430f, 22f, 12f, 12f);
            BuildPresets();

            BuildButton(_rightPage, "PreviewLog", "Preview in Logs", 14f,
                542f, 187f, 34f, () =>
                {
                    if (LogDisplayOptions.EmitPreviewLines(_selectedIndex,
                            out string detail))
                        SetStatus("Sent " + detail + ".", true);
                    else SetStatus(detail, false);
                });
            BuildButton(_rightPage, "ResetAll", "Reset All Defaults", 218f,
                542f, 187f, 34f, () =>
                {
                    LogDisplayOptions.ResetAllConfiguredColors();
                    SetStatus("All 23 categories restored to defaults.", true);
                    RefreshAll();
                });

            _statusText = MakeText(_rightPage,
                "Changes save immediately.", 12, DimColor,
                TextAlignmentOptions.Center);
            _statusText.enableWordWrapping = true;
            PlaceFromTop(_statusText.rectTransform, 584f, 38f, 14f, 14f);

            TextMeshProUGUI help = MakeText(_rightPage,
                "N = native color  |  RGB changes apply live  |  Esc closes",
                11, DimColor, TextAlignmentOptions.Center);
            PlaceFromTop(help.rectTransform, 622f, 20f, 10f, 10f);
        }

        private Toggle BuildToggle(Transform parent, string label, float y,
            Action<bool> changed)
        {
            RectTransform root = MakeRect(parent,
                label.Replace(" ", "") + "Toggle");
            PlaceFromTop(root, y, 28f, 14f, 14f);

            RectTransform box = MakeRect(root, "Box");
            Image boxImage = MakeImage(box, InputFallback);
            NativeUiStyle.Apply(boxImage, NativeUiStyle.Role.Input,
                InputFallback);
            box.anchorMin = new Vector2(0f, 0.5f);
            box.anchorMax = new Vector2(0f, 0.5f);
            box.pivot = new Vector2(0f, 0.5f);
            box.anchoredPosition = Vector2.zero;
            box.sizeDelta = new Vector2(25f, 25f);

            RectTransform check = MakeRect(box, "Check");
            Image checkImage = MakeImage(check, GreenColor);
            checkImage.raycastTarget = false;
            Fill(check);
            check.offsetMin = new Vector2(6f, 6f);
            check.offsetMax = new Vector2(-6f, -6f);

            Toggle toggle = root.gameObject.AddComponent<Toggle>();
            toggle.targetGraphic = boxImage;
            toggle.graphic = checkImage;
            toggle.navigation = NoNavigation();
            toggle.onValueChanged.AddListener(value => changed(value));

            TextMeshProUGUI text = MakeText(root, label, 14, Color.white,
                TextAlignmentOptions.MidlineLeft);
            text.rectTransform.anchorMin = new Vector2(0f, 0f);
            text.rectTransform.anchorMax = new Vector2(1f, 1f);
            text.rectTransform.offsetMin = new Vector2(37f, 0f);
            text.rectTransform.offsetMax = Vector2.zero;
            return toggle;
        }

        private Slider BuildRgbSlider(Transform parent, string channel,
            float y, Color accent, out TextMeshProUGUI valueText)
        {
            RectTransform root = MakeRect(parent, channel + "SliderRow");
            PlaceFromTop(root, y, 32f, 14f, 14f);

            TextMeshProUGUI label = MakeText(root, channel, 15, accent,
                TextAlignmentOptions.MidlineLeft);
            label.fontStyle = FontStyle.Bold;
            label.rectTransform.anchorMin = new Vector2(0f, 0f);
            label.rectTransform.anchorMax = new Vector2(0f, 1f);
            label.rectTransform.sizeDelta = new Vector2(25f, 0f);

            RectTransform sliderRoot = MakeRect(root, "Slider");
            sliderRoot.anchorMin = new Vector2(0f, 0.5f);
            sliderRoot.anchorMax = new Vector2(1f, 0.5f);
            sliderRoot.pivot = new Vector2(0.5f, 0.5f);
            sliderRoot.offsetMin = new Vector2(28f, -10f);
            sliderRoot.offsetMax = new Vector2(-52f, 10f);
            Image trackImage = MakeImage(sliderRoot, InputFallback);
            NativeUiStyle.Apply(trackImage, NativeUiStyle.Role.Input,
                InputFallback);

            Slider slider = sliderRoot.gameObject.AddComponent<Slider>();
            slider.minValue = 0f;
            slider.maxValue = 255f;
            slider.wholeNumbers = true;
            slider.direction = Slider.Direction.LeftToRight;
            slider.navigation = NoNavigation();

            RectTransform fillArea = MakeRect(sliderRoot, "FillArea");
            Fill(fillArea);
            fillArea.offsetMin = new Vector2(6f, 6f);
            fillArea.offsetMax = new Vector2(-6f, -6f);
            RectTransform fill = MakeRect(fillArea, "Fill");
            Image fillImage = MakeImage(fill, accent);
            fillImage.raycastTarget = false;
            Fill(fill);
            slider.fillRect = fill;

            RectTransform handleArea = MakeRect(sliderRoot, "HandleArea");
            Fill(handleArea);
            handleArea.offsetMin = new Vector2(7f, 0f);
            handleArea.offsetMax = new Vector2(-7f, 0f);
            RectTransform handle = MakeRect(handleArea, "Handle");
            Image handleImage = MakeImage(handle, Color.white);
            NativeUiStyle.Apply(handleImage, NativeUiStyle.Role.Button,
                Color.white);
            handle.sizeDelta = new Vector2(15f, 25f);
            slider.handleRect = handle;
            slider.targetGraphic = handleImage;

            valueText = MakeText(root, "255", 13, Color.white,
                TextAlignmentOptions.MidlineRight);
            valueText.rectTransform.anchorMin = new Vector2(1f, 0f);
            valueText.rectTransform.anchorMax = new Vector2(1f, 1f);
            valueText.rectTransform.pivot = new Vector2(1f, 0.5f);
            valueText.rectTransform.sizeDelta = new Vector2(44f, 0f);
            return slider;
        }

        private void BuildPresets()
        {
            const float swatchWidth = 42f;
            const float swatchHeight = 30f;
            const float gap = 7f;
            const float left = 17f;
            for (int index = 0; index < PresetColors.Length; index++)
            {
                int row = index / 8;
                int column = index % 8;
                string colorText = PresetColors[index];
                ColorUtility.TryParseHtmlString(colorText, out Color color);
                RectTransform rect = MakeRect(_rightPage,
                    "Preset_" + colorText.Substring(1));
                Image image = MakeImage(rect, color);
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 1f);
                rect.anchoredPosition = new Vector2(left +
                    column * (swatchWidth + gap), -(456f +
                    row * (swatchHeight + 7f)));
                rect.sizeDelta = new Vector2(swatchWidth, swatchHeight);
                Outline outline = rect.gameObject.AddComponent<Outline>();
                outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
                outline.effectDistance = new Vector2(1f, -1f);
                Button button = rect.gameObject.AddComponent<Button>();
                button.targetGraphic = image;
                button.navigation = NoNavigation();
                string captured = colorText;
                button.onClick.AddListener(() => SetSelectedColor(captured));
            }
        }

        private Button BuildButton(Transform parent, string name,
            string label, float x, float y, float width, float height,
            Action clicked)
        {
            RectTransform rect = MakeRect(parent, name);
            Image image = MakeImage(rect, PanelFallback);
            NativeUiStyle.Apply(image, NativeUiStyle.Role.Button,
                PanelFallback);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
            Button button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.navigation = NoNavigation();
            button.onClick.AddListener(() => clicked());
            TextMeshProUGUI text = MakeText(rect, label, 13, Color.white,
                TextAlignmentOptions.Center);
            text.fontStyle = FontStyle.Bold;
            Fill(text.rectTransform);
            return button;
        }

        private void SelectCategory(int index)
        {
            if (index < 0 ||
                index >= LogDisplayOptions.ColorCategoryCount) return;
            _selectedIndex = index;
            RefreshAll();
        }

        private void SetSelectedColor(string color)
        {
            if (LogDisplayOptions.TrySetConfiguredColor(_selectedIndex,
                    color, out string error))
                SetStatus("Selected color saved as " + color + ".", true);
            else SetStatus(error, false);
            RefreshAll();
        }

        private void OnRgbChanged()
        {
            if (_refreshing || !_redSlider || !_greenSlider ||
                !_blueSlider) return;
            Color32 color = new Color32(
                (byte)Mathf.RoundToInt(_redSlider.value),
                (byte)Mathf.RoundToInt(_greenSlider.value),
                (byte)Mathf.RoundToInt(_blueSlider.value), 255);
            string value = "#" + ColorUtility.ToHtmlStringRGB(color);
            if (!LogDisplayOptions.TrySetConfiguredColor(_selectedIndex,
                    value, out string error))
            {
                SetStatus(error, false);
                return;
            }
            RefreshVisuals(color, false);
            RefreshCategoryRows();
        }

        private void RefreshAll()
        {
            if (!_built && !_canvasObject) return;
            _refreshing = true;
            try
            {
                if (_colorsToggle)
                    _colorsToggle.SetIsOnWithoutNotify(
                        LogDisplayOptions.EnableCustomColors?.Value == true);
                if (_damageToggle)
                    _damageToggle.SetIsOnWithoutNotify(
                        LogDisplayOptions.ShowDamageTypes?.Value == true);
                if (!LogDisplayOptions.TryGetPickerColor(_selectedIndex,
                        out Color color)) color = Color.white;
                bool native = LogDisplayOptions.IsUsingNativeColor(
                    _selectedIndex);
                if (_redSlider) _redSlider.SetValueWithoutNotify(
                    Mathf.Round(color.r * 255f));
                if (_greenSlider) _greenSlider.SetValueWithoutNotify(
                    Mathf.Round(color.g * 255f));
                if (_blueSlider) _blueSlider.SetValueWithoutNotify(
                    Mathf.Round(color.b * 255f));
                RefreshVisuals(color, native);
                RefreshCategoryRows();
            }
            finally
            {
                _refreshing = false;
            }
        }

        private void RefreshVisuals(Color color, bool native)
        {
            if (_selectedTitle)
                _selectedTitle.text =
                    LogDisplayOptions.GetColorDisplayName(_selectedIndex);
            if (_sampleSwatch) _sampleSwatch.color = color;
            if (_sampleText)
            {
                _sampleText.text = native
                    ? "[Preview] Native Erenshor color for this category."
                    : "[Preview] New messages use this custom color.";
                _sampleText.color = color;
            }
            string hex = "#" + ColorUtility.ToHtmlStringRGB(color);
            if (_hexText)
            {
                _hexText.text = native
                    ? "Native game color  •  picker starts at " + hex
                    : hex;
                _hexText.color = native ? DimColor : color;
            }
            if (_redValue) _redValue.text =
                Mathf.RoundToInt(color.r * 255f).ToString();
            if (_greenValue) _greenValue.text =
                Mathf.RoundToInt(color.g * 255f).ToString();
            if (_blueValue) _blueValue.text =
                Mathf.RoundToInt(color.b * 255f).ToString();
        }

        private void RefreshCategoryRows()
        {
            foreach (CategoryRow row in _rows)
            {
                bool selected = row.Index == _selectedIndex;
                if (row.Selection)
                    row.Selection.gameObject.SetActive(selected);
                if (row.Label)
                    row.Label.color = selected ? GoldColor : Color.white;
                bool native = LogDisplayOptions.IsUsingNativeColor(row.Index);
                if (!LogDisplayOptions.TryGetPickerColor(row.Index,
                        out Color color)) color = Color.white;
                if (row.Swatch) row.Swatch.color = color;
                if (row.NativeMarker)
                    row.NativeMarker.gameObject.SetActive(native);
            }
        }

        private void SetStatus(string message, bool success)
        {
            if (!_statusText) return;
            _statusText.text = message ?? "";
            _statusText.color = success ? GreenColor : RedColor;
            _statusUntil = Time.unscaledTime + 4f;
        }

        private void SetVisible(bool visible)
        {
            if (_canvasObject) _canvasObject.SetActive(visible);
        }

        internal static bool OpenForTesting(int selectedIndex,
            bool scrollBottom, out string detail)
        {
            Open();
            if (!_instance || !_instance._built)
            {
                detail = "settings UI is waiting for native UI sources";
                return false;
            }
            _instance._selectedIndex = Mathf.Clamp(selectedIndex, 0,
                LogDisplayOptions.ColorCategoryCount - 1);
            _instance.RefreshAll();
            Canvas.ForceUpdateCanvases();
            if (_instance._categoryScroll)
                _instance._categoryScroll.verticalNormalizedPosition =
                    scrollBottom ? 0f : 1f;
            detail = _instance._rows.Count +
                " categories; selected " +
                LogDisplayOptions.GetColorDisplayName(
                    _instance._selectedIndex) +
                (scrollBottom ? "; list bottom" : "; list top");
            return true;
        }

        internal static bool RunSelfTest(out string detail)
        {
            var errors = new List<string>();
            if (!_instance || !_instance._built)
            {
                detail = "settings UI is not built";
                return false;
            }
            if (_instance._rows.Count !=
                LogDisplayOptions.ColorCategoryCount)
                errors.Add("category row count " + _instance._rows.Count);
            if (!_instance._colorsToggle || !_instance._damageToggle)
                errors.Add("feature toggles are missing");
            if (!_instance._redSlider || !_instance._greenSlider ||
                !_instance._blueSlider)
                errors.Add("RGB selectors are missing");
            if (!_instance._categoryScroll ||
                !_instance._categoryContent)
                errors.Add("category scroll is missing");
            else
            {
                Canvas.ForceUpdateCanvases();
                float viewportHeight =
                    _instance._categoryScroll.viewport.rect.height;
                if (Mathf.Abs(viewportHeight - CategoryListHeight) > 0.5f)
                    errors.Add("category viewport height " +
                        viewportHeight.ToString("0.##"));
                float completeRowSpan = CategoryEdgePadding +
                    FullyVisibleCategoryRows *
                    (CategoryRowHeight + CategoryGap);
                if (Mathf.Abs(viewportHeight - completeRowSpan) > 0.5f)
                    errors.Add("category viewport ends on a partial row");
            }
            if (!_instance._leftPage ||
                !_instance._leftPage.GetComponent<RectMask2D>())
                errors.Add("left page boundary mask is missing");

            int testIndex = Mathf.Clamp(_instance._selectedIndex, 0,
                LogDisplayOptions.ColorCategoryCount - 1);
            int originalIndex = _instance._selectedIndex;
            string original = LogDisplayOptions.GetConfiguredColor(testIndex);
            bool originalColors =
                LogDisplayOptions.EnableCustomColors?.Value == true;
            bool originalDamage =
                LogDisplayOptions.ShowDamageTypes?.Value == true;
            const string testColor = "#123456";
            _instance._colorsToggle.isOn = !originalColors;
            if (LogDisplayOptions.EnableCustomColors?.Value != !originalColors)
                errors.Add("custom-color toggle callback");
            _instance._colorsToggle.isOn = originalColors;
            if (LogDisplayOptions.EnableCustomColors?.Value != originalColors)
                errors.Add("custom-color toggle restore");
            _instance._damageToggle.isOn = !originalDamage;
            if (LogDisplayOptions.ShowDamageTypes?.Value != !originalDamage)
                errors.Add("damage-type toggle callback");
            _instance._damageToggle.isOn = originalDamage;
            if (LogDisplayOptions.ShowDamageTypes?.Value != originalDamage)
                errors.Add("damage-type toggle restore");

            _instance._selectedIndex = testIndex;
            _instance.RefreshAll();
            _instance._redSlider.value = 18f;
            _instance._greenSlider.value = 52f;
            _instance._blueSlider.value = 86f;
            if (!string.Equals(LogDisplayOptions.GetConfiguredColor(testIndex),
                    testColor, StringComparison.OrdinalIgnoreCase))
                errors.Add("RGB slider callback");
            if (!LogDisplayOptions.TrySetConfiguredColor(testIndex,
                    original, out string restoreError))
                errors.Add("config restore: " + restoreError);
            if (!string.Equals(LogDisplayOptions.GetConfiguredColor(testIndex),
                    original, StringComparison.OrdinalIgnoreCase))
                errors.Add("config round-trip restore mismatch");
            if (!LogDisplayOptions.TrySetConfiguredColor(testIndex,
                    testColor, out string setError) ||
                !string.Equals(LogDisplayOptions.GetConfiguredColor(testIndex),
                    testColor, StringComparison.OrdinalIgnoreCase))
                errors.Add("config round-trip: " + setError);
            if (!LogDisplayOptions.TrySetConfiguredColor(testIndex,
                    original, out restoreError))
                errors.Add("second config restore: " + restoreError);
            _instance._selectedIndex = originalIndex;
            _instance.RefreshAll();

            detail = errors.Count == 0
                ? "23 category rows with 16 fully visible rows and no " +
                  "bottom clipping, two saved toggles, RGB sliders, 16 " +
                  "presets, native/default resets, live preview, and " +
                  "config round-trip"
                : string.Join("; ", errors);
            return errors.Count == 0;
        }

        private static Texture2D _whiteTexture;
        private static Sprite _whiteSprite;

        private static Sprite WhiteSprite
        {
            get
            {
                if (_whiteSprite) return _whiteSprite;
                _whiteTexture = new Texture2D(4, 4,
                    TextureFormat.RGBA32, false);
                var pixels = new Color[16];
                for (int index = 0; index < pixels.Length; index++)
                    pixels[index] = Color.white;
                _whiteTexture.SetPixels(pixels);
                _whiteTexture.Apply();
                _whiteSprite = Sprite.Create(_whiteTexture,
                    new Rect(0f, 0f, 4f, 4f),
                    new Vector2(0.5f, 0.5f));
                return _whiteSprite;
            }
        }

        private static RectTransform MakeRect(Transform parent, string name)
        {
            var value = new GameObject(name);
            value.transform.SetParent(parent, false);
            return value.AddComponent<RectTransform>();
        }

        private static Image MakeImage(RectTransform rect, Color color)
        {
            Image image = rect.gameObject.AddComponent<Image>();
            image.sprite = WhiteSprite;
            image.type = Image.Type.Simple;
            image.color = color;
            image.raycastTarget = true;
            return image;
        }

        private static TextMeshProUGUI MakeText(Transform parent,
            string text, int size, Color color, TextAlignmentOptions alignment)
        {
            RectTransform rect = MakeRect(parent, "Text");
            TextMeshProUGUI value = rect.gameObject
                .AddComponent<TextMeshProUGUI>();
            value.text = text;
            value.fontSize = size;
            value.color = color;
            value.alignment = alignment;
            NativeUiStyle.Apply(value);
            return value;
        }

        private static void Fill(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void PlaceFromTop(RectTransform rect, float y,
            float height, float left, float right)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(left, -(y + height));
            rect.offsetMax = new Vector2(-right, -y);
        }

        private static Navigation NoNavigation()
        {
            return new Navigation { mode = Navigation.Mode.None };
        }

        private sealed class InputBlocker : MonoBehaviour,
            IPointerEnterHandler, IPointerExitHandler
        {
            private bool _inside;

            public void OnPointerEnter(PointerEventData eventData)
            {
                if (_inside) return;
                _inside = true;
                AddonInputState.Enter();
            }

            public void OnPointerExit(PointerEventData eventData)
            {
                if (!_inside) return;
                _inside = false;
                AddonInputState.Exit();
            }

            private void OnDisable()
            {
                if (_inside) OnPointerExit(null);
            }
        }

        private sealed class WindowDragger : MonoBehaviour,
            IBeginDragHandler, IDragHandler
        {
            internal RectTransform Target;
            private Vector2 _offset;

            public void OnBeginDrag(PointerEventData eventData)
            {
                if (!Target) return;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    Target.parent as RectTransform, eventData.position,
                    eventData.pressEventCamera, out Vector2 local);
                _offset = Target.anchoredPosition - local;
            }

            public void OnDrag(PointerEventData eventData)
            {
                if (!Target) return;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    Target.parent as RectTransform, eventData.position,
                    eventData.pressEventCamera, out Vector2 local);
                Target.anchoredPosition = local + _offset;
            }
        }
    }
}
