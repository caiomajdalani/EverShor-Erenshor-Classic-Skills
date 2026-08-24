using System;
using UnityEngine;
using UnityEngine.UI;

namespace ErenshorSkills
{
    /// <summary>
    /// Adds Skills directly to Erenshor's native menu-button bar using the
    /// game's own Shield icon and interaction styling.
    /// </summary>
    public sealed class MenuBarButton : MonoBehaviour
    {
        private static readonly string[] IconNames =
        {
            "Shield_eq_icon_r"
        };

        private GameObject _buttonObject;
        private Image _image;
        private Button _button;
        private float _nextBuildAttempt;

        internal bool IsAttachedToNativeBarForTesting =>
            UI.NativeMenuBarButtonFactory.IsAttached(_buttonObject);

        internal bool UsesNativeHudStyleForTesting =>
            IsAttachedToNativeBarForTesting && _image && _image.sprite &&
            string.Equals(_image.sprite.name, "Shield_eq_icon_r",
                StringComparison.OrdinalIgnoreCase) && _button;

        internal string IconSpriteNameForTesting =>
            _image && _image.sprite ? _image.sprite.name : "";

        internal int SiblingIndexForTesting =>
            _buttonObject ? _buttonObject.transform.GetSiblingIndex() : -1;

        private void Start()
        {
            TryBuild();
        }

        private void Update()
        {
            if (_buttonObject &&
                UI.NativeMenuBarButtonFactory.IsAttached(_buttonObject)) return;
            _buttonObject = null;
            _image = null;
            _button = null;
            if (Time.unscaledTime < _nextBuildAttempt) return;
            _nextBuildAttempt = Time.unscaledTime + 1f;
            TryBuild();
        }

        private void TryBuild()
        {
            if (_buttonObject) return;
            if (!UI.NativeMenuBarButtonFactory.TryBuild(
                    "CS_SkillsMenuButton", IconNames,
                    SkillsUI.ToggleWindow, out _buttonObject, out _image,
                    out _button, out string detail)) return;
            SkillsPlugin.Log.LogInfo(
                "Skills added to native menu bar (" + detail + ").");
        }

        private void OnDestroy()
        {
            if (_buttonObject) Destroy(_buttonObject);
            _buttonObject = null;
            _image = null;
            _button = null;
        }
    }
}
