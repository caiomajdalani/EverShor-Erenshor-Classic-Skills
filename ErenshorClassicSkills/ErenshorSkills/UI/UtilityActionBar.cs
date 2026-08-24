using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ErenshorSkills
{
    /// <summary>
    /// Three compact utility-skill icons beside the existing Skills button.
    /// Each icon is the same 42x42 size, can drag the group, shows its current
    /// keybind, and places a numeric cooldown directly over the artwork.
    /// </summary>
    public sealed class UtilityActionBar : MonoBehaviour
    {
        internal const float IconSizeForTesting = 42f;
        private const float IconGap = 5f;
        private const float RefreshInterval = 0.1f;

        private static readonly Color ButtonColor =
            new Color(0.025f, 0.055f, 0.065f, 0.98f);
        private static readonly Color KeyBadgeColor =
            new Color(0f, 0f, 0f, 0.82f);
        private static readonly Color CooldownOverlayColor =
            new Color(0f, 0f, 0f, 0.64f);
        private static readonly Color ForageBorder =
            new Color(0.36f, 0.85f, 0.32f, 0.95f);
        private static readonly Color BindBorder =
            new Color(0.94f, 0.35f, 0.30f, 0.95f);
        private static readonly Color BegBorder =
            new Color(0.96f, 0.74f, 0.24f, 0.95f);

        private GameObject _canvasGO;
        private RectTransform _rootRT;
        private ActionIcon _forage;
        private ActionIcon _bindWound;
        private ActionIcon _beg;
        private bool _built;
        private int _lastVisibleMask = -1;
        private float _nextRefresh;
        private string _positionFile;

        internal bool IsBuiltForTesting => _built && _canvasGO != null &&
            _rootRT != null && IsIconBuilt(_forage) &&
            IsIconBuilt(_bindWound) && IsIconBuilt(_beg);

        internal bool IsVisibleForTesting =>
            _canvasGO != null && _canvasGO.activeSelf;

        internal bool IconAssetsLoadedForTesting =>
            _forage?.Sprite != null && _bindWound?.Sprite != null &&
            _beg?.Sprite != null;

        internal bool CooldownOverlaysBuiltForTesting =>
            _forage?.CooldownOverlay != null &&
            _bindWound?.CooldownOverlay != null &&
            _beg?.CooldownOverlay != null;

        private void Start()
        {
            try
            {
                _positionFile = Path.Combine(
                    RuntimePaths.DataDirectory, "utility_action_pos.txt");
                Build();
                _built = true;
                SkillsPlugin.Log.LogInfo(
                    "UtilityActionBar: Forage, Bind Wound, and Begging icons built successfully");
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogError($"UtilityActionBar build failed: {ex}");
                DestroyUi();
            }
        }

        private void Update()
        {
            if (!_built || _canvasGO == null || _rootRT == null) return;

            bool forageVisible = SkillsPlugin.CfgEnableForaging?.Value == true;
            bool bindVisible = SkillsPlugin.CfgEnableBindWound?.Value == true;
            bool begVisible = SkillsPlugin.CfgEnableBegging?.Value == true;
            bool shouldShow = SkillsPlugin.CfgShowUtilityActionButtons?.Value == true &&
                (forageVisible || bindVisible || begVisible) && IsInPlayableWorld();

            if (_canvasGO.activeSelf != shouldShow)
                _canvasGO.SetActive(shouldShow);
            if (!shouldShow) return;

            ConfigureLayout(forageVisible, bindVisible, begVisible);

            if (Time.unscaledTime >= _nextRefresh)
            {
                _nextRefresh = Time.unscaledTime + RefreshInterval;
                UpdateIconStates();
            }
        }

        private void Build()
        {
            _canvasGO = new GameObject("CS_UtilityActionCanvas");
            DontDestroyOnLoad(_canvasGO);
            Canvas canvas = _canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 99;
            CanvasScaler scaler = _canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            _canvasGO.AddComponent<GraphicRaycaster>();

            GameObject root = new GameObject("CS_UtilityActions");
            root.transform.SetParent(_canvasGO.transform, false);
            _rootRT = root.AddComponent<RectTransform>();
            _rootRT.anchorMin = new Vector2(0.5f, 0f);
            _rootRT.anchorMax = new Vector2(0.5f, 0f);
            _rootRT.pivot = new Vector2(0.5f, 0.5f);
            _rootRT.anchoredPosition = new Vector2(424f, 29f);
            _rootRT.sizeDelta = new Vector2(
                IconSizeForTesting * 3f + IconGap * 2f,
                IconSizeForTesting);

            _forage = CreateActionIcon(root, "Forage", ForageBorder,
                "Wild Herb.png", new Color(0.4f, 0.8f, 0.3f),
                OnForageClicked);
            _bindWound = CreateActionIcon(root, "BindWound", BindBorder,
                "Patchwork Bandages.png", new Color(0.9f, 0.7f, 0.6f),
                OnBindWoundClicked);
            _beg = CreateActionIcon(root, "Begging", BegBorder,
                "Barnacle-Encrusted Coin.png", new Color(0.95f, 0.75f, 0.2f),
                OnBegClicked);

            ConfigureLayout(true, true, true, true);
            LoadPosition();
            UpdateIconStates();

            // Update() enables the canvas only after a character is loaded.
            _canvasGO.SetActive(false);
        }

        private ActionIcon CreateActionIcon(GameObject parent, string name,
            Color borderColor, string iconFilename, Color fallbackColor,
            Action clicked)
        {
            var action = new ActionIcon();
            action.Root = new GameObject($"CS_{name}Action");
            action.Root.transform.SetParent(parent.transform, false);
            RectTransform rt = action.Root.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(IconSizeForTesting, IconSizeForTesting);

            Image background = action.Root.AddComponent<Image>();
            background.color = ButtonColor;
            background.raycastTarget = true;
            Outline border = action.Root.AddComponent<Outline>();
            border.effectColor = borderColor;
            border.effectDistance = new Vector2(1f, -1f);
            Button button = action.Root.AddComponent<Button>();
            button.targetGraphic = background;
            button.transition = Selectable.Transition.ColorTint;
            ColorBlock colors = button.colors;
            colors.normalColor = ButtonColor;
            colors.highlightedColor = Brighten(ButtonColor, 0.12f);
            colors.pressedColor = Brighten(ButtonColor, -0.02f);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = new Color(0.08f, 0.09f, 0.10f, 0.75f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.06f;
            button.colors = colors;

            GameObject iconGO = new GameObject("Icon");
            iconGO.transform.SetParent(action.Root.transform, false);
            action.Icon = iconGO.AddComponent<Image>();
            action.Icon.preserveAspect = true;
            action.Icon.raycastTarget = false;
            SetFill(action.Icon.rectTransform, 3f);
            LoadActionIcon(action, iconFilename, fallbackColor);

            action.CooldownOverlay = new GameObject("CooldownOverlay");
            action.CooldownOverlay.transform.SetParent(
                action.Root.transform, false);
            Image overlay = action.CooldownOverlay.AddComponent<Image>();
            overlay.color = CooldownOverlayColor;
            overlay.raycastTarget = false;
            SetFill(overlay.rectTransform, 1f);
            action.CooldownText = CreateText(action.CooldownOverlay,
                "Cooldown", 20, FontStyle.Bold);
            action.CooldownText.color = Color.white;
            action.CooldownText.raycastTarget = false;
            Outline cooldownOutline =
                action.CooldownText.gameObject.AddComponent<Outline>();
            cooldownOutline.effectColor = Color.black;
            cooldownOutline.effectDistance = new Vector2(1f, -1f);

            GameObject badge = new GameObject("KeyBadge");
            badge.transform.SetParent(action.Root.transform, false);
            RectTransform badgeRT = badge.AddComponent<RectTransform>();
            badgeRT.anchorMin = new Vector2(1f, 0f);
            badgeRT.anchorMax = new Vector2(1f, 0f);
            badgeRT.pivot = new Vector2(1f, 0f);
            badgeRT.anchoredPosition = new Vector2(-1f, 1f);
            badgeRT.sizeDelta = new Vector2(23f, 11f);
            Image badgeImage = badge.AddComponent<Image>();
            badgeImage.color = KeyBadgeColor;
            badgeImage.raycastTarget = false;
            action.KeyText = CreateText(badge, "Key", 9, FontStyle.Bold);
            action.KeyText.color = new Color(0.93f, 0.96f, 0.96f, 1f);
            action.KeyText.raycastTarget = false;

            ActionIconHandler handler =
                action.Root.AddComponent<ActionIconHandler>();
            handler.Target = _rootRT;
            handler.ClampPosition = ClampToCanvas;
            handler.DragEnded = SavePosition;
            handler.Clicked = clicked;
            return action;
        }

        private static Text CreateText(GameObject parent, string name,
            int fontSize, FontStyle style)
        {
            GameObject textGO = new GameObject(name);
            textGO.transform.SetParent(parent.transform, false);
            Text text = textGO.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            SetFill(text.rectTransform, 0f);
            return text;
        }

        private static void SetFill(RectTransform rt, float inset)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
        }

        private void LoadActionIcon(ActionIcon action, string filename,
            Color fallbackColor)
        {
            try
            {
                byte[] data = null;
                string diskPath = Path.Combine(
                    RuntimePaths.AssetDirectory, "Icons", filename);
                if (File.Exists(diskPath)) data = File.ReadAllBytes(diskPath);
                if (data == null)
                    data = EmbeddedResources.GetBytesByFilename(filename);
                if (data == null)
                    throw new FileNotFoundException(
                        $"Icon was not found on disk or in the DLL: {filename}");

                var texture = new Texture2D(
                    2, 2, TextureFormat.RGBA32, false);
                texture.filterMode = FilterMode.Bilinear;
                if (!ImageConversion.LoadImage(texture, data))
                {
                    Destroy(texture);
                    throw new InvalidDataException(
                        $"Icon is not a valid image: {filename}");
                }
                var sprite = Sprite.Create(texture,
                    new Rect(0f, 0f, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f));
                action.Texture = texture;
                action.Sprite = sprite;
                action.Icon.sprite = sprite;
                action.Icon.color = Color.white;
            }
            catch (Exception ex)
            {
                action.Icon.color = fallbackColor;
                SkillsPlugin.Log.LogError(
                    $"UtilityActionBar icon load failed ({filename}): {ex.Message}");
            }
        }

        private void ConfigureLayout(bool forageVisible, bool bindVisible,
            bool begVisible, bool force = false)
        {
            int mask = (forageVisible ? 1 : 0) |
                (bindVisible ? 2 : 0) | (begVisible ? 4 : 0);
            if (!force && mask == _lastVisibleMask) return;
            _lastVisibleMask = mask;

            _forage.Root.SetActive(forageVisible);
            _bindWound.Root.SetActive(bindVisible);
            _beg.Root.SetActive(begVisible);

            var active = new List<ActionIcon>(3);
            if (forageVisible) active.Add(_forage);
            if (bindVisible) active.Add(_bindWound);
            if (begVisible) active.Add(_beg);
            if (active.Count == 0) return;

            float width = active.Count * IconSizeForTesting +
                (active.Count - 1) * IconGap;
            _rootRT.sizeDelta = new Vector2(width, IconSizeForTesting);
            float firstCenter = -width * 0.5f + IconSizeForTesting * 0.5f;
            for (int i = 0; i < active.Count; i++)
            {
                RectTransform rt =
                    active[i].Root.GetComponent<RectTransform>();
                rt.anchoredPosition = new Vector2(
                    firstCenter + i * (IconSizeForTesting + IconGap), 0f);
            }
        }

        private void UpdateIconStates()
        {
            UpdateIconState(_forage,
                SkillsPlugin.CfgForageKey?.Value ?? KeyCode.F9,
                Skills.ForagingSkill.CooldownRemaining);
            UpdateIconState(_bindWound,
                SkillsPlugin.CfgBindWoundKey?.Value ?? KeyCode.F10,
                Skills.BindWoundSkill.CooldownRemaining);
            UpdateIconState(_beg,
                SkillsPlugin.CfgBegKey?.Value ?? KeyCode.Semicolon,
                Skills.BeggingSkill.CooldownRemaining);
        }

        private static void UpdateIconState(ActionIcon icon, KeyCode key,
            float cooldownRemaining)
        {
            string cooldown =
                FormatCooldownTextForTesting(cooldownRemaining);
            icon.CooldownText.text = cooldown;
            icon.CooldownOverlay.SetActive(cooldown.Length > 0);
            icon.KeyText.text = key == KeyCode.None
                ? "--" : FormatKeyForDisplay(key);
        }

        private void OnForageClicked()
        {
            if (!CanInvokeFromHud()) return;
            Skills.ForagingSkill.TryForage();
            UpdateIconStates();
        }

        private void OnBindWoundClicked()
        {
            if (!CanInvokeFromHud()) return;
            Skills.BindWoundSkill.TryBindWound();
            UpdateIconStates();
        }

        private void OnBegClicked()
        {
            if (!CanInvokeFromHud()) return;
            Skills.BeggingSkill.TryBeg();
            UpdateIconStates();
        }

        private static bool CanInvokeFromHud()
        {
            try
            {
                return SkillsSaveManager.HasLoaded &&
                    !GameData.InCharSelect && !GameData.PlayerTyping &&
                    !SkillsPlugin.IsEscapeMenuOpen;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsInPlayableWorld()
        {
            try
            {
                string scene = UnityEngine.SceneManagement.SceneManager
                    .GetActiveScene().name;
                return SkillsSaveManager.HasLoaded && !GameData.InCharSelect &&
                    scene != "Menu" && scene != "LoadScene" &&
                    !SkillsPlugin.IsEscapeMenuOpen &&
                    GameData.PlayerStats != null &&
                    !string.IsNullOrEmpty(GameData.PlayerStats.MyName);
            }
            catch
            {
                return false;
            }
        }

        internal static string FormatCooldownTextForTesting(float remaining)
        {
            if (float.IsNaN(remaining) || float.IsInfinity(remaining) ||
                remaining <= 0.05f) return string.Empty;
            return Mathf.CeilToInt(Mathf.Max(0f, remaining)).ToString();
        }

        internal static string FormatKeyForDisplay(KeyCode key)
        {
            if (key == KeyCode.None) return "UNBOUND";
            if (key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9)
                return ((int)key - (int)KeyCode.Alpha0).ToString();
            if (key >= KeyCode.Keypad0 && key <= KeyCode.Keypad9)
                return "NUM " + ((int)key - (int)KeyCode.Keypad0);
            switch (key)
            {
                case KeyCode.Semicolon: return ";";
                case KeyCode.Comma: return ",";
                case KeyCode.Period: return ".";
                case KeyCode.Slash: return "/";
                case KeyCode.Backslash: return "\\";
                case KeyCode.Minus: return "-";
                case KeyCode.Equals: return "=";
                default: return key.ToString().ToUpperInvariant();
            }
        }

        private static Color Brighten(Color color, float amount)
        {
            return new Color(
                Mathf.Clamp01(color.r + amount),
                Mathf.Clamp01(color.g + amount),
                Mathf.Clamp01(color.b + amount), color.a);
        }

        private bool ClampToCanvas()
        {
            if (_rootRT == null) return false;
            Vector2 old = _rootRT.anchoredPosition;
            _rootRT.anchoredPosition = new Vector2(
                Mathf.Clamp(old.x, -3000f, 3000f),
                Mathf.Clamp(old.y, -3000f, 3000f));
            return (_rootRT.anchoredPosition - old).sqrMagnitude > 0.001f;
        }

        private void SavePosition()
        {
            if (_rootRT == null || string.IsNullOrEmpty(_positionFile)) return;
            try
            {
                Vector2 p = _rootRT.anchoredPosition;
                string value = p.x.ToString("R",
                    System.Globalization.CultureInfo.InvariantCulture) + "," +
                    p.y.ToString("R",
                    System.Globalization.CultureInfo.InvariantCulture);
                if (!SafeFileIO.TryWriteAllTextAtomic(
                    _positionFile, value, out string error))
                    throw new IOException(error);
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogWarning(
                    $"UtilityActionBar save position: {ex.Message}");
            }
        }

        private void LoadPosition()
        {
            if (_rootRT == null || string.IsNullOrEmpty(_positionFile)) return;
            try
            {
                if (!File.Exists(_positionFile) &&
                    !File.Exists(_positionFile + ".bak")) return;
                if (!SafeFileIO.TryReadAllTextWithBackup(_positionFile,
                    IsPositionValid, out string text, out bool recovered,
                    out string error))
                {
                    SkillsPlugin.Log.LogWarning(
                        $"UtilityActionBar position ignored: {error}");
                    return;
                }
                TryParsePosition(text, out float x, out float y);
                _rootRT.anchoredPosition = new Vector2(
                    Mathf.Clamp(x, -3000f, 3000f),
                    Mathf.Clamp(y, -3000f, 3000f));
                if (recovered)
                    SkillsPlugin.Log.LogWarning(
                        "UtilityActionBar recovered position from backup.");
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogWarning(
                    $"UtilityActionBar load position: {ex.Message}");
            }
        }

        private static bool IsPositionValid(string text)
        {
            return TryParsePosition(text, out float x, out float y) &&
                Mathf.Abs(x) <= 10000f && Mathf.Abs(y) <= 10000f;
        }

        private static bool TryParsePosition(string text,
            out float x, out float y)
        {
            x = y = 0f;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string[] parts = text.Trim().Split(',');
            if (parts.Length != 2) return false;
            bool valid = float.TryParse(parts[0],
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out x) &&
                float.TryParse(parts[1],
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out y);
            return valid && !float.IsNaN(x) && !float.IsInfinity(x) &&
                !float.IsNaN(y) && !float.IsInfinity(y);
        }

        private static bool IsIconBuilt(ActionIcon icon)
        {
            return icon?.Root != null && icon.Icon != null &&
                icon.CooldownOverlay != null && icon.CooldownText != null &&
                icon.KeyText != null;
        }

        private void OnDestroy()
        {
            DestroyUi();
        }

        private void DestroyUi()
        {
            DestroyOwnedIcon(_forage);
            DestroyOwnedIcon(_bindWound);
            DestroyOwnedIcon(_beg);
            if (_canvasGO != null) Destroy(_canvasGO);
            _canvasGO = null;
            _rootRT = null;
            _built = false;
        }

        private static void DestroyOwnedIcon(ActionIcon icon)
        {
            if (icon == null) return;
            if (icon.Sprite != null) Destroy(icon.Sprite);
            if (icon.Texture != null) Destroy(icon.Texture);
            icon.Sprite = null;
            icon.Texture = null;
        }

        private sealed class ActionIcon
        {
            internal GameObject Root;
            internal Image Icon;
            internal GameObject CooldownOverlay;
            internal Text CooldownText;
            internal Text KeyText;
            internal Sprite Sprite;
            internal Texture2D Texture;
        }

        private sealed class ActionIconHandler : MonoBehaviour,
            IPointerEnterHandler, IPointerExitHandler,
            IPointerDownHandler, IPointerUpHandler,
            IBeginDragHandler, IDragHandler
        {
            internal RectTransform Target;
            internal Func<bool> ClampPosition;
            internal Action Clicked;
            internal Action DragEnded;
            private bool _inside;
            private bool _dragging;
            private Vector2 _dragOffset;

            public void OnPointerEnter(PointerEventData eventData)
            {
                if (_inside) return;
                _inside = true;
                AddonInputState.Enter();
            }

            public void OnPointerExit(PointerEventData eventData)
            {
                ReleasePointerBlock();
            }

            public void OnPointerDown(PointerEventData eventData)
            {
                _dragging = false;
            }

            public void OnBeginDrag(PointerEventData eventData)
            {
                if (Target == null) return;
                _dragging = true;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    Target.parent as RectTransform, eventData.position,
                    eventData.pressEventCamera, out Vector2 localPoint);
                _dragOffset = Target.anchoredPosition - localPoint;
            }

            public void OnDrag(PointerEventData eventData)
            {
                if (!_dragging || Target == null) return;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    Target.parent as RectTransform, eventData.position,
                    eventData.pressEventCamera, out Vector2 localPoint);
                Target.anchoredPosition = localPoint + _dragOffset;
                ClampPosition?.Invoke();
            }

            public void OnPointerUp(PointerEventData eventData)
            {
                bool wasDragging = _dragging;
                _dragging = false;
                if (wasDragging)
                {
                    ClampPosition?.Invoke();
                    DragEnded?.Invoke();
                }
                else
                    Clicked?.Invoke();
            }

            private void ReleasePointerBlock()
            {
                if (!_inside) return;
                _inside = false;
                AddonInputState.Exit();
            }

            private void OnDisable()
            {
                _dragging = false;
                ReleasePointerBlock();
            }

            private void OnDestroy()
            {
                ReleasePointerBlock();
            }
        }
    }
}
