using System;
using UnityEngine;
using UnityEngine.UI;

namespace ErenshorSkills.UI
{
    /// <summary>
    /// Custom trade confirmation panel for SimCraft whisper trades.
    /// Shows item name, quantity, gold cost, and Accept/Decline buttons.
    /// Styled to match the game's dark teal UI.
    /// </summary>
    public static class SimCraftTradeUI
    {
        private static GameObject _canvas;
        private static GameObject _panel;
        private static Text _titleText;
        private static Text _itemText;
        private static Text _costText;
        private static Text _simText;
        private static bool _isOpen;

        private static Action _onAccept;
        private static Action _onDecline;

        static readonly Color C_Panel  = new Color(0.08f, 0.13f, 0.16f, 0.95f);
        static readonly Color C_Title  = new Color(0.06f, 0.10f, 0.13f, 0.98f);
        static readonly Color C_Border = new Color(0.22f, 0.36f, 0.40f, 0.9f);
        static readonly Color C_Gold   = new Color(1f, 0.84f, 0f);
        static readonly Color C_Green  = new Color(0.3f, 0.69f, 0.31f);
        static readonly Color C_Red    = new Color(0.94f, 0.33f, 0.31f);

        public static bool IsOpen => _isOpen;

        public static void Show(string simName, string itemName, int quantity, int totalGold,
            Action onAccept, Action onDecline)
        {
            _onAccept = onAccept;
            _onDecline = onDecline;
            EnsureUI();

            string qtyStr = quantity > 1 ? $"{quantity}x " : "";
            _titleText.text = "Trade Offer";
            _simText.text = $"{simName} wants to trade with you:";
            _itemText.text = $"{qtyStr}{itemName}";
            _costText.text = $"Cost: {totalGold} gold";

            _panel.SetActive(true);
            _isOpen = true;
            try { GameData.PlayerInv.ForceOpenInv(); } catch { }
        }

        public static void Hide()
        {
            if (_panel != null)
                _panel.SetActive(false);
            _isOpen = false;
            _onAccept = null;
            _onDecline = null;
        }

        public static void Shutdown()
        {
            Hide();
            if (_canvas != null)
                UnityEngine.Object.Destroy(_canvas);
            _canvas = null;
            _panel = null;
            _titleText = null;
            _itemText = null;
            _costText = null;
            _simText = null;
        }

        private static void EnsureUI()
        {
            if (_panel != null) return;

            _canvas = new GameObject("SimCraftTradeCanvas");
            UnityEngine.Object.DontDestroyOnLoad(_canvas);
            var canvas = _canvas.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 150;
            _canvas.AddComponent<CanvasScaler>();
            _canvas.AddComponent<GraphicRaycaster>();

            float pw = 320f, ph = 200f;
            _panel = MakePanel(_canvas.transform, pw, ph);
            var panelRect = _panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.anchoredPosition = Vector2.zero;

            // Border
            var border = new GameObject("Border");
            border.transform.SetParent(_panel.transform, false);
            var borderImg = border.AddComponent<Image>();
            borderImg.color = C_Border;
            var borderRect = border.GetComponent<RectTransform>();
            borderRect.anchorMin = Vector2.zero;
            borderRect.anchorMax = Vector2.one;
            borderRect.offsetMin = new Vector2(-2, -2);
            borderRect.offsetMax = new Vector2(2, 2);
            border.transform.SetAsFirstSibling();

            // Title bar
            var titleBar = MakeRect(_panel.transform, "TitleBar", 0, 0, pw, 32);
            var titleBg = titleBar.AddComponent<Image>();
            titleBg.color = C_Title;
            _titleText = MakeText(titleBar.transform, "TitleText", "Trade Offer",
                16, TextAnchor.MiddleCenter, Color.white, 0, 0, pw, 32);

            // Sim name
            _simText = MakeText(_panel.transform, "SimText", "",
                13, TextAnchor.MiddleCenter, new Color(0.7f, 0.7f, 0.7f), 10, -40, pw - 20, 24);

            // Item name
            _itemText = MakeText(_panel.transform, "ItemText", "",
                18, TextAnchor.MiddleCenter, C_Gold, 10, -68, pw - 20, 30);
            _itemText.fontStyle = FontStyle.Bold;

            // Cost
            _costText = MakeText(_panel.transform, "CostText", "",
                15, TextAnchor.MiddleCenter, new Color(0.9f, 0.9f, 0.9f), 10, -100, pw - 20, 24);

            // Buttons
            float btnW = 120f, btnH = 36f, btnY = -150f;
            float spacing = 20f;
            float totalBtnW = btnW * 2 + spacing;
            float startX = (pw - totalBtnW) / 2f;

            MakeButton(_panel.transform, "AcceptBtn", "Accept", C_Green,
                startX, btnY, btnW, btnH, () => { var cb = _onAccept; Hide(); cb?.Invoke(); });

            MakeButton(_panel.transform, "DeclineBtn", "Decline", C_Red,
                startX + btnW + spacing, btnY, btnW, btnH, () => { var cb = _onDecline; Hide(); cb?.Invoke(); });

            _panel.SetActive(false);
        }

        private static GameObject MakePanel(Transform parent, float w, float h)
        {
            var go = new GameObject("SimCraftPanel");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = C_Panel;
            var rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(w, h);
            return go;
        }

        private static GameObject MakeRect(Transform parent, string name, float x, float y, float w, float h)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(w, h);
            return go;
        }

        private static Text MakeText(Transform parent, string name, string text,
            int size, TextAnchor align, Color color, float x, float y, float w, float h)
        {
            var go = MakeRect(parent, name, x, y, w, h);
            var t = go.AddComponent<Text>();
            t.text = text;
            t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            t.fontSize = size;
            t.alignment = align;
            t.color = color;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        private static void MakeButton(Transform parent, string name, string label,
            Color bgColor, float x, float y, float w, float h, Action onClick)
        {
            var go = MakeRect(parent, name, x, y, w, h);
            var img = go.AddComponent<Image>();
            img.color = bgColor;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.normalColor = bgColor;
            colors.highlightedColor = bgColor * 1.2f;
            colors.pressedColor = bgColor * 0.8f;
            colors.selectedColor = bgColor;
            btn.colors = colors;
            btn.onClick.AddListener(() => onClick?.Invoke());

            var text = MakeText(go.transform, "Label", label,
                15, TextAnchor.MiddleCenter, Color.white, 0, 0, w, h);
            text.fontStyle = FontStyle.Bold;
            var textRect = text.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            textRect.anchoredPosition = Vector2.zero;
        }
    }
}
