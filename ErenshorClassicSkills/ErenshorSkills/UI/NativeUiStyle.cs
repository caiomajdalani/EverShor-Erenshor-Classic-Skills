using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ErenshorSkills.UI
{
    /// <summary>
    /// Borrows the active Erenshor installation's own UI font, materials, and
    /// sliced sprites at runtime. No game artwork is copied into the addon.
    /// Fallback colors keep the windows usable while the menu/scene is still
    /// constructing native UI objects.
    /// </summary>
    internal static class NativeUiStyle
    {
        internal enum Role
        {
            Window,
            Panel,
            Title,
            Button,
            Input,
            Tab,
            Progress,
            Slot,
            Plus
        }

        private sealed class ImageStyle
        {
            internal Sprite Sprite;
            internal Material Material;
            internal Color Color = Color.white;
            internal Image.Type Type = Image.Type.Simple;
            internal bool PreserveAspect;
        }

        private static readonly Dictionary<Role, ImageStyle> Images =
            new Dictionary<Role, ImageStyle>();
        private static TMP_FontAsset _font;
        private static Material _fontMaterial;
        private static bool _resolved;
        private static bool _logged;

        internal static bool Apply(Image image, Role role, Color fallback)
        {
            if (!image) return false;
            Resolve();
            if (!Images.TryGetValue(role, out ImageStyle style) ||
                style == null || !style.Sprite)
            {
                image.color = fallback;
                return false;
            }

            image.sprite = style.Sprite;
            image.material = style.Material;
            image.color = style.Color;
            image.type = style.Type;
            image.preserveAspect = style.PreserveAspect;
            return true;
        }

        internal static void Apply(TextMeshProUGUI text)
        {
            if (!text) return;
            Resolve();
            if (_font) text.font = _font;
            if (_fontMaterial) text.fontSharedMaterial = _fontMaterial;
            text.richText = true;
            text.raycastTarget = false;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Overflow;
        }

        internal static void Reset()
        {
            Images.Clear();
            _font = null;
            _fontMaterial = null;
            _resolved = false;
            _logged = false;
        }

        internal static bool NativeSourcesAvailable =>
            GameData.PlayerInv?.InvWindow &&
            GameData.PlayerSkillBook?.Skillbook;

        private static void Resolve()
        {
            if (_resolved && HasLiveTheme()) return;
            if (_resolved) Images.Clear();
            _resolved = true;

            try
            {
                TextMeshProUGUI nativeText = FindTitleText(
                    GameData.PlayerSkillBook?.Skillbook) ??
                    FindTitleText(GameData.PlayerSpellBook?.Spellbook) ??
                    GameData.PlayerInv?.GoldTXT;
                if (!nativeText && GameData.VendorWindow)
                    nativeText = GameData.VendorWindow.LootSource;
                if (!nativeText)
                    nativeText = UnityEngine.Object.FindObjectOfType<TextMeshProUGUI>();
                if (nativeText)
                {
                    _font = nativeText.font;
                    _fontMaterial = nativeText.fontSharedMaterial;
                }

                var roots = GetNativeRoots();
                CaptureNamedSprite(Role.Window, roots,
                    "cosmetics_and_essentials_carv 1",
                    "cosmetics_and_essentials_carv", "options_carv 1");
                CaptureNamedSprite(Role.Panel, roots,
                    "cosmetics_and_essentials_carv 1",
                    "options_carv 1", "cosmetics_and_essentials_carv");
                CaptureNamedSprite(Role.Title, roots,
                    "dd_options_form", "dd_form 1");
                CaptureNamedSprite(Role.Button, roots, "dd_form 1");
                CaptureNamedSprite(Role.Input, roots,
                    "dd_options_form", "dd_form 1");
                CaptureNamedSprite(Role.Tab, roots, "left_tab_h 1");
                CaptureNamedSprite(Role.Progress, roots, "Xp");
                CaptureNamedSprite(Role.Slot, roots,
                    "ActionBar_Slot_Border_Small",
                    "ActionBar_Slot_Border_Big");
                CaptureNamedSprite(Role.Plus, roots, "CSL_Plus");

                if (!Images.ContainsKey(Role.Window))
                    Capture(Role.Window, FindRootOrLargest(
                        GameData.PlayerSkillBook?.Skillbook) ??
                        FindRootOrLargest(GameData.PlayerInv?.InvWindow) ??
                        FindRootOrLargest(GameData.VendorWindow?.WindowParent));
                CopyFallback(Role.Panel, Role.Window);
                CopyFallback(Role.Title, Role.Button);
                CopyFallback(Role.Button, Role.Panel);
                CopyFallback(Role.Input, Role.Panel);
                CopyFallback(Role.Tab, Role.Button);
                CopyFallback(Role.Progress, Role.Input);
                CopyFallback(Role.Slot, Role.Button);
                CopyFallback(Role.Plus, Role.Button);

                if (!_logged && _font && Images.Count >= 3)
                {
                    _logged = true;
                    SkillsPlugin.Log?.LogInfo(
                        $"Native UI theme resolved: font={_font.name}, " +
                        $"window={Describe(Role.Window)}, panel={Describe(Role.Panel)}, " +
                        $"button={Describe(Role.Button)}, tab={Describe(Role.Tab)}, " +
                        $"progress={Describe(Role.Progress)}");
                }
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log?.LogWarning(
                    "Native UI theme discovery deferred: " + ex.Message);
            }
        }

        private static bool HasLiveTheme()
        {
            return _font && Has(Role.Window) && Has(Role.Panel) &&
                Has(Role.Button) && Has(Role.Tab) && Has(Role.Progress);
        }

        private static bool Has(Role role)
        {
            return Images.TryGetValue(role, out ImageStyle style) &&
                style != null && style.Sprite;
        }

        private static List<GameObject> GetNativeRoots()
        {
            var roots = new List<GameObject>();
            AddRoot(roots, GameData.PlayerSkillBook?.Skillbook);
            AddRoot(roots, GameData.PlayerSpellBook?.Spellbook);
            AddRoot(roots, GameData.PlayerInv?.InvWindow);
            AddRoot(roots, GameData.PlayerInv?.StatWindow);
            AddRoot(roots, GameData.QuestLog?.QuestWindow);
            AddRoot(roots, GameData.VendorWindow?.WindowParent);
            return roots;
        }

        private static void AddRoot(List<GameObject> roots, GameObject root)
        {
            if (root && !roots.Contains(root)) roots.Add(root);
        }

        private static void CaptureNamedSprite(Role role,
            IEnumerable<GameObject> roots, params string[] spriteNames)
        {
            foreach (string spriteName in spriteNames)
            {
                foreach (GameObject root in roots)
                {
                    if (!root) continue;
                    foreach (Image image in root.GetComponentsInChildren<Image>(true))
                    {
                        if (!image || !image.sprite ||
                            !string.Equals(image.sprite.name, spriteName,
                                StringComparison.OrdinalIgnoreCase)) continue;
                        Capture(role, image);
                        return;
                    }
                }
            }
        }

        private static TextMeshProUGUI FindTitleText(GameObject root)
        {
            if (!root) return null;
            TextMeshProUGUI best = null;
            float bestSize = 0f;
            foreach (TextMeshProUGUI text in
                root.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                if (!text || !text.font || text.fontSize <= bestSize) continue;
                best = text;
                bestSize = text.fontSize;
            }
            return best;
        }

        private static void CopyFallback(Role destination, Role source)
        {
            if (Images.ContainsKey(destination) || !Images.TryGetValue(source,
                    out ImageStyle value) || value == null) return;
            Images[destination] = new ImageStyle
            {
                Sprite = value.Sprite,
                Material = value.Material,
                Color = value.Color,
                Type = value.Type,
                PreserveAspect = value.PreserveAspect
            };
        }

        private static string Describe(Role role)
        {
            return Images.TryGetValue(role, out ImageStyle style) && style?.Sprite
                ? style.Sprite.name + "/" +
                    (style.Material ? style.Material.name : "default-material")
                : "fallback";
        }

        private static void Capture(Role role, Image source)
        {
            if (!source || !source.sprite) return;
            Images[role] = new ImageStyle
            {
                Sprite = source.sprite,
                Material = source.material,
                Color = source.color,
                Type = source.type,
                PreserveAspect = source.preserveAspect
            };
        }

        private static Image FindImage(GameObject root)
        {
            if (!root) return null;
            Image direct = root.GetComponent<Image>();
            if (direct && direct.sprite) return direct;
            foreach (Image image in root.GetComponentsInChildren<Image>(true))
                if (image && image.sprite) return image;
            return null;
        }

        private static Image FindRootOrLargest(GameObject root)
        {
            if (!root) return null;
            Image direct = root.GetComponent<Image>();
            if (direct && direct.sprite) return direct;

            Image best = null;
            float bestArea = 0f;
            foreach (Image image in root.GetComponentsInChildren<Image>(true))
            {
                if (!image || !image.sprite) continue;
                RectTransform rect = image.rectTransform;
                float area = Mathf.Abs(rect.rect.width * rect.rect.height);
                if (area <= bestArea) continue;
                best = image;
                bestArea = area;
            }
            return best;
        }

        private static Image FindNamed(GameObject root, params string[] tokens)
        {
            if (!root) return null;
            Image best = null;
            float bestArea = 0f;
            foreach (Image image in root.GetComponentsInChildren<Image>(true))
            {
                if (!image || !image.sprite) continue;
                string name = image.transform.name ?? "";
                bool matches = false;
                foreach (string token in tokens)
                {
                    if (name.IndexOf(token, StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                    matches = true;
                    break;
                }
                if (!matches) continue;
                float area = Mathf.Abs(
                    image.rectTransform.rect.width * image.rectTransform.rect.height);
                if (area <= bestArea) continue;
                best = image;
                bestArea = area;
            }
            return best;
        }
    }
}
