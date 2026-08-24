using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using UnityEngine;

namespace ErenshorSkills
{
    /// <summary>
    /// Resolves addon paths without assuming a specific loader layout.
    /// BepInEx normally uses BepInEx/plugins + BepInEx/config, while Lunaris
    /// uses plugins + plugins/config and remaps Assembly.Location back to the
    /// original plugin file. Durable character data follows an existing
    /// ClassicSkills data directory when switching between those loaders.
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

        internal static string LoaderName => IsLunaris ? "Lunaris" : "BepInEx";

        internal static string PluginDirectory
        {
            get
            {
                if (!string.IsNullOrEmpty(_pluginDirectory)) return _pluginDirectory;

                // Lunaris remaps the hot-reload cache assembly location back to
                // the manually installed source DLL, making this the most
                // reliable loader-neutral location.
                try
                {
                    string assemblyLocation = Assembly.GetExecutingAssembly().Location;
                    if (!string.IsNullOrWhiteSpace(assemblyLocation))
                    {
                        string full = Path.GetFullPath(assemblyLocation);
                        string directory = File.Exists(full)
                            ? Path.GetDirectoryName(full)
                            : (Directory.Exists(full) ? full : Path.GetDirectoryName(full));
                        if (!string.IsNullOrEmpty(directory))
                            return _pluginDirectory = directory;
                    }
                }
                catch { }

                try
                {
                    string infoLocation = SkillsPlugin.Instance?.Info?.Location;
                    if (!string.IsNullOrWhiteSpace(infoLocation))
                    {
                        string full = Path.GetFullPath(infoLocation);
                        string directory = Directory.Exists(full)
                            ? full : Path.GetDirectoryName(full);
                        if (!string.IsNullOrEmpty(directory))
                            return _pluginDirectory = directory;
                    }
                }
                catch { }

                try
                {
                    if (!string.IsNullOrWhiteSpace(Paths.PluginPath))
                        return _pluginDirectory = Path.GetFullPath(Paths.PluginPath);
                }
                catch { }

                return _pluginDirectory = GameRoot;
            }
        }

        internal static string ConfigRoot
        {
            get
            {
                if (!string.IsNullOrEmpty(_configRoot)) return _configRoot;

                string current = null;
                try
                {
                    if (!string.IsNullOrWhiteSpace(Paths.ConfigPath))
                        current = Path.GetFullPath(Paths.ConfigPath);
                }
                catch { }
                if (string.IsNullOrEmpty(current))
                    current = Path.Combine(GameRoot, IsLunaris
                        ? Path.Combine("plugins", "config")
                        : Path.Combine("BepInEx", "config"));

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
