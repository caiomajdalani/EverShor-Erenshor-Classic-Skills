using UnityEngine;
using UnityEngine.EventSystems;

namespace ErenshorSkills.UI
{
    /// <summary>
    /// Addon windows must share Erenshor's scene-owned EventSystem. Creating a
    /// persistent replacement here survives scene changes and conflicts with
    /// the next native UI scene's EventSystem.
    /// </summary>
    internal static class NativeEventSystemGate
    {
        internal static bool IsReady =>
            EventSystem.current != null ||
            Object.FindObjectOfType<EventSystem>() != null;

        internal static int ActiveCount =>
            Object.FindObjectsOfType<EventSystem>().Length;
    }
}
