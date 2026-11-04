using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace ErenshorSkills.Patches
{
    /// <summary>
    /// Lets Fate of the Fallen keep ownership of its own class pages while
    /// exposing the EverShor page only when its Native page is selected.
    /// </summary>
    [HarmonyPatch]
    internal static class Patch_FateOfTheFallenCharacterCreationPager
    {
        private const string PagerTypeName =
            "FateOfTheFallen.FOTFCharacterCreationPager";
        private static bool Prepare()
        {
            Type pager = AccessTools.TypeByName(PagerTypeName);
            return pager != null &&
                AccessTools.Method(pager, "ShowNativePage") != null &&
                AccessTools.Method(pager, "ShowFOTFPage") != null;
        }

        private static IEnumerable<MethodBase> TargetMethods()
        {
            Type pager = AccessTools.TypeByName(PagerTypeName);
            if (pager == null) yield break;

            MethodBase showNative = AccessTools.Method(pager, "ShowNativePage");
            MethodBase showFOTF = AccessTools.Method(pager, "ShowFOTFPage");
            if (showNative != null) yield return showNative;
            if (showFOTF != null) yield return showFOTF;
        }

        private static void Postfix(object[] __args, MethodBase __originalMethod)
        {
            if (__originalMethod == null) return;
            CharSelectManager manager = __args?
                .OfType<CharSelectManager>().FirstOrDefault();
            if (manager == null) return;
            EverShorCharacterCreationPager.SetFatePage(manager,
                __originalMethod.Name == "ShowNativePage");
        }
    }
}
