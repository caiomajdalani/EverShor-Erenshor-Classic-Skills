using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ErenshorSkills.UI
{
    /// <summary>
    /// Builds addon buttons as real children of Erenshor's native
    /// RadioButtonPar menu bar. This keeps scale, spacing, hover behavior, and
    /// scene visibility identical to the game's own menu buttons.
    /// </summary>
    internal static class NativeMenuBarButtonFactory
    {
        internal const string NativeBarName = "RadioButtonPar";

        internal static bool TryBuild(string objectName,
            string[] preferredSpriteNames, Action clicked,
            out GameObject buttonObject, out Image image, out Button button,
            out string detail)
        {
            buttonObject = null;
            image = null;
            button = null;
            detail = "native menu bar is not available";

            if (!TryFindTemplate(out Button template,
                    out Image templateImage, out RectTransform parentRect))
                return false;

            Sprite icon = FindLoadedSprite(preferredSpriteNames);
            if (!icon)
            {
                detail = "native icon was not loaded (" +
                    string.Join(", ", preferredSpriteNames) + ")";
                return false;
            }

            try
            {
                buttonObject = new GameObject(objectName);
                buttonObject.transform.SetParent(parentRect, false);
                RectTransform targetRect =
                    buttonObject.AddComponent<RectTransform>();
                CopyNativeRect(template.transform as RectTransform,
                    targetRect, parentRect);

                image = buttonObject.AddComponent<Image>();
                image.sprite = icon;
                image.material = templateImage.material;
                image.color = templateImage.color;
                image.type = templateImage.type;
                image.preserveAspect = templateImage.preserveAspect;
                image.fillCenter = templateImage.fillCenter;
                image.raycastTarget = true;

                button = buttonObject.AddComponent<Button>();
                button.targetGraphic = image;
                button.transition = template.transition;
                button.colors = template.colors;
                button.spriteState = template.spriteState;
                button.animationTriggers = template.animationTriggers;
                button.navigation = new Navigation
                {
                    mode = Navigation.Mode.None
                };
                if (clicked != null) button.onClick.AddListener(() => clicked());

                CopyLayoutElement(template.gameObject, buttonObject,
                    targetRect);
                buttonObject.transform.SetAsLastSibling();
                LayoutRebuilder.ForceRebuildLayoutImmediate(parentRect);

                detail = icon.name + " in " + GetHierarchyPath(parentRect);
                return true;
            }
            catch (Exception ex)
            {
                if (buttonObject) UnityEngine.Object.Destroy(buttonObject);
                buttonObject = null;
                image = null;
                button = null;
                detail = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        internal static bool IsAttached(GameObject buttonObject)
        {
            return buttonObject && buttonObject.transform.parent &&
                string.Equals(buttonObject.transform.parent.name,
                    NativeBarName, StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryFindTemplate(out Button template,
            out Image templateImage, out RectTransform parentRect)
        {
            template = null;
            templateImage = null;
            parentRect = null;
            foreach (Button candidate in
                UnityEngine.Object.FindObjectsOfType<Button>())
            {
                if (!candidate || !candidate.gameObject.activeInHierarchy)
                    continue;
                Image candidateImage = candidate.targetGraphic as Image ??
                    candidate.GetComponent<Image>();
                if (!candidateImage || !candidateImage.sprite ||
                    !candidateImage.sprite.name.EndsWith("_eq_icon_r",
                        StringComparison.OrdinalIgnoreCase)) continue;
                Transform parent = candidate.transform.parent;
                if (!parent || !string.Equals(parent.name, NativeBarName,
                        StringComparison.OrdinalIgnoreCase)) continue;
                RectTransform rect = parent as RectTransform;
                if (!rect) continue;
                template = candidate;
                templateImage = candidateImage;
                parentRect = rect;
                return true;
            }
            return false;
        }

        private static Sprite FindLoadedSprite(string[] preferredNames)
        {
            Sprite[] sprites = Resources.FindObjectsOfTypeAll<Sprite>();
            foreach (string preferredName in preferredNames)
                foreach (Sprite sprite in sprites)
                    if (sprite && string.Equals(sprite.name, preferredName,
                            StringComparison.OrdinalIgnoreCase))
                        return sprite;
            return null;
        }

        private static void CopyNativeRect(RectTransform templateRect,
            RectTransform targetRect, RectTransform parentRect)
        {
            if (!templateRect)
            {
                targetRect.sizeDelta = new Vector2(42f, 42f);
                return;
            }

            RectTransform placementSource = FindRightmostButtonRect(
                parentRect) ?? templateRect;
            targetRect.anchorMin = placementSource.anchorMin;
            targetRect.anchorMax = placementSource.anchorMax;
            targetRect.pivot = placementSource.pivot;
            targetRect.sizeDelta = placementSource.sizeDelta;
            targetRect.localScale = placementSource.localScale;
            targetRect.localRotation = placementSource.localRotation;
            targetRect.anchoredPosition3D = placementSource.anchoredPosition3D;

            if (!HasAutomaticLayout(parentRect))
            {
                float step = ResolveHorizontalStep(parentRect,
                    placementSource);
                Vector3 local = placementSource.localPosition;
                targetRect.localPosition = new Vector3(local.x + step,
                    local.y, local.z);
            }
        }

        private static RectTransform FindRightmostButtonRect(
            RectTransform parentRect)
        {
            RectTransform rightmost = null;
            float rightmostX = float.MinValue;
            for (int i = 0; i < parentRect.childCount; i++)
            {
                Transform child = parentRect.GetChild(i);
                if (!child || !child.gameObject.activeSelf ||
                    !child.GetComponent<Button>()) continue;
                RectTransform rect = child as RectTransform;
                if (!rect || rect.localPosition.x <= rightmostX) continue;
                rightmost = rect;
                rightmostX = rect.localPosition.x;
            }
            return rightmost;
        }

        private static bool HasAutomaticLayout(RectTransform parentRect)
        {
            return parentRect.GetComponent<HorizontalLayoutGroup>() ||
                parentRect.GetComponent<GridLayoutGroup>();
        }

        private static float ResolveHorizontalStep(RectTransform parentRect,
            RectTransform placementSource)
        {
            var positions = new List<float>();
            for (int i = 0; i < parentRect.childCount; i++)
            {
                Transform child = parentRect.GetChild(i);
                if (!child || !child.gameObject.activeSelf ||
                    !child.GetComponent<Button>()) continue;
                RectTransform rect = child as RectTransform;
                if (rect) positions.Add(rect.localPosition.x);
            }
            positions.Sort();
            var deltas = new List<float>();
            for (int i = 1; i < positions.Count; i++)
            {
                float delta = positions[i] - positions[i - 1];
                if (delta > 1f) deltas.Add(delta);
            }
            deltas.Sort();
            if (deltas.Count > 0) return deltas[deltas.Count / 2];

            float width = placementSource.rect.width *
                Mathf.Abs(placementSource.localScale.x);
            return Mathf.Max(50f, width + 8f);
        }

        private static void CopyLayoutElement(GameObject templateObject,
            GameObject targetObject, RectTransform targetRect)
        {
            LayoutElement source = templateObject.GetComponent<LayoutElement>();
            if (!source && !HasAutomaticLayout(
                    targetRect.parent as RectTransform)) return;

            LayoutElement target = targetObject.AddComponent<LayoutElement>();
            if (source)
            {
                target.ignoreLayout = source.ignoreLayout;
                target.minWidth = source.minWidth;
                target.minHeight = source.minHeight;
                target.preferredWidth = source.preferredWidth;
                target.preferredHeight = source.preferredHeight;
                target.flexibleWidth = source.flexibleWidth;
                target.flexibleHeight = source.flexibleHeight;
                target.layoutPriority = source.layoutPriority;
                return;
            }
            target.preferredWidth = targetRect.rect.width;
            target.preferredHeight = targetRect.rect.height;
            target.flexibleWidth = 0f;
            target.flexibleHeight = 0f;
        }

        private static string GetHierarchyPath(Transform transform)
        {
            string path = transform ? transform.name : "<missing>";
            Transform current = transform ? transform.parent : null;
            while (current)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }
            return path;
        }
    }
}
