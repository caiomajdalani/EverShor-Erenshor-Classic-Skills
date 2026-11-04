using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace ErenshorSkills
{
    /// <summary>
    /// Resolves paths for native Lunaris installation. Durable character data
    /// remains in the existing plugins/config/ClassicSkills directory; legacy
    /// BepInEx save locations are still detected to avoid losing existing data.
    /// </summary>
    internal static class RuntimePaths
    {
        private static string _pluginDirectory;
        private static string _configRoot;

        internal static bool IsLunaris
        {
            get
            {
                try
                {
                    return AppDomain.CurrentDomain.GetAssemblies().Any(assembly =>
                        string.Equals(assembly.GetName().Name, "Lunaris",
                            StringComparison.OrdinalIgnoreCase));
                }
                catch { return false; }
            }
        }

        internal static string LoaderName => "Lunaris (native API)";

        internal static string PluginDirectory
        {
            get
            {
                if (!string.IsNullOrEmpty(_pluginDirectory)) return _pluginDirectory;

                // Lunaris documents <Erenshor>/plugins as the native plugin
                // directory. Use that canonical path instead of Assembly.Location,
                // which may point at a collectible hot-reload load context.
                try
                {
                    string plugins = Path.Combine(GameRoot, "plugins");
                    Directory.CreateDirectory(plugins);
                    return _pluginDirectory = plugins;
                }
                catch
                {
                    return _pluginDirectory = GameRoot;
                }
            }
        }

        internal static string ConfigRoot
        {
            get
            {
                if (!string.IsNullOrEmpty(_configRoot)) return _configRoot;

                string current = Path.GetFullPath(
                    Path.Combine(GameRoot, "plugins", "config"));

                var candidates = new List<string>
                {
                    current,
                    Path.Combine(GameRoot, "BepInEx", "config"),
                    Path.Combine(GameRoot, "plugins", "config")
                };

                // Keep the active loader's root when it already contains saves.
                // Otherwise follow the first alternate root with durable addon
                // JSON. This prevents a loader switch from looking like data loss.
                string selected = HasDurableClassicSkillsData(current)
                    ? current
                    : candidates.FirstOrDefault(candidate =>
                        !PathsEqual(candidate, current) &&
                        HasDurableClassicSkillsData(candidate)) ?? current;

                try { Directory.CreateDirectory(selected); }
                catch { }
                return _configRoot = selected;
            }
        }

        internal static string DataDirectory =>
            Path.Combine(ConfigRoot, "ClassicSkills");

        internal static void Reset()
        {
            _pluginDirectory = null;
            _configRoot = null;
        }

        internal static string AssetDirectory =>
            Path.Combine(PluginDirectory, "ClassicSkills");

        internal static string GameRoot
        {
            get
            {
                try
                {
                    string dataPath = Application.dataPath;
                    if (!string.IsNullOrWhiteSpace(dataPath))
                    {
                        var parent = Directory.GetParent(Path.GetFullPath(dataPath));
                        if (parent != null) return parent.FullName;
                    }
                }
                catch { }
                try { return Path.GetFullPath(AppContext.BaseDirectory); }
                catch { return "."; }
            }
        }

        internal static bool Validate(out string detail)
        {
            try
            {
                string plugin = PluginDirectory;
                string config = ConfigRoot;
                string data = DataDirectory;
                Directory.CreateDirectory(data);
                bool valid = Path.IsPathRooted(plugin) &&
                    Path.IsPathRooted(config) && Directory.Exists(plugin) &&
                    Directory.Exists(config) && Directory.Exists(data);
                detail = $"loader={LoaderName}; plugin={plugin}; config={config}; data={data}";
                return valid;
            }
            catch (Exception ex)
            {
                detail = ex.Message;
                return false;
            }
        }

        private static bool HasDurableClassicSkillsData(string root)
        {
            if (string.IsNullOrWhiteSpace(root)) return false;
            try
            {
                string directory = Path.Combine(root, "ClassicSkills");
                if (!Directory.Exists(directory)) return false;
                return Directory.EnumerateFiles(directory, "*.json",
                    SearchOption.TopDirectoryOnly).Any();
            }
            catch { return false; }
        }

        private static bool PathsEqual(string left, string right)
        {
            if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right)) return false;
            try
            {
                return string.Equals(Path.GetFullPath(left).TrimEnd(
                        Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    Path.GetFullPath(right).TrimEnd(
                        Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch { return string.Equals(left, right,
                StringComparison.OrdinalIgnoreCase); }
        }
    }
}
