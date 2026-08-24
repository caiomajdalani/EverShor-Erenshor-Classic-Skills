using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace ErenshorSkills
{
    /// <summary>
    /// Loads icons and model files from embedded DLL resources.
    /// Resources are embedded at build time from Resources/Icons/ and Resources/Models/.
    /// Resource names follow the pattern: ErenshorSkills.Resources.Icons.{filename}
    /// Filenames preserve spaces, apostrophes, and parentheses exactly.
    /// </summary>
    public static class EmbeddedResources
    {
        private static Assembly _assembly;
        private static Dictionary<string, string> _resourceMap;
        private static Dictionary<string, Sprite> _iconCache;

        private static Assembly Asm => _assembly ?? (_assembly = Assembly.GetExecutingAssembly());

        /// <summary>
        /// Build a lookup map of all embedded resource names.
        /// Resource names preserve spaces, apostrophes, and parens in .NET SDK projects.
        /// </summary>
        private static void BuildResourceMap()
        {
            if (_resourceMap != null) return;
            _resourceMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in Asm.GetManifestResourceNames())
            {
                // Resources are named like:
                //   ErenshorSkills.Resources.Icons.Copper Rivets.png
                //   ErenshorSkills.Resources.Models.BrewBarrel.obj
                _resourceMap[name] = name;
            }
            SkillsPlugin.Log.LogInfo($"EmbeddedResources: {_resourceMap.Count} resources found in DLL.");
        }

        /// <summary>Get raw bytes of an embedded resource by its full name.</summary>
        public static byte[] GetBytes(string resourceName)
        {
            using (var stream = Asm.GetManifestResourceStream(resourceName))
            {
                if (stream == null) return null;
                using (var output = new MemoryStream())
                {
                    stream.CopyTo(output);
                    return output.ToArray();
                }
            }
        }

        /// <summary>Get raw bytes of an embedded resource by searching for a filename match.</summary>
        public static byte[] GetBytesByFilename(string filename)
        {
            BuildResourceMap();
            // Try exact match first
            foreach (var kvp in _resourceMap)
            {
                if (kvp.Key.EndsWith("." + filename, StringComparison.OrdinalIgnoreCase) ||
                    kvp.Key.EndsWith("." + filename.Replace(" ", "_").Replace("-", "_"), StringComparison.OrdinalIgnoreCase))
                    return GetBytes(kvp.Key);
            }
            // Try with spaces replaced by underscores (MSBuild convention)
            string mangled = filename.Replace(" ", "_").Replace("-", "_");
            foreach (var kvp in _resourceMap)
            {
                if (kvp.Key.EndsWith("." + mangled, StringComparison.OrdinalIgnoreCase))
                    return GetBytes(kvp.Key);
            }
            return null;
        }

        /// <summary>Get string content of an embedded resource.</summary>
        public static string GetText(string filename)
        {
            var data = GetBytesByFilename(filename);
            if (data == null) return null;
            return System.Text.Encoding.UTF8.GetString(data);
        }

        /// <summary>
        /// Load an embedded PNG as a runtime texture. VFX assets use bilinear
        /// filtering by default while icons retain their dedicated point-
        /// filtered loading path below.
        /// </summary>
        public static Texture2D LoadTexture(string filename, string textureName,
            FilterMode filterMode = FilterMode.Bilinear,
            TextureWrapMode wrapMode = TextureWrapMode.Clamp,
            bool linear = false)
        {
            byte[] data = GetBytesByFilename(filename);
            if (data == null)
            {
                SkillsPlugin.Log.LogWarning(
                    $"EmbeddedResources: Texture '{filename}' was not found.");
                return null;
            }

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false,
                linear)
            {
                name = textureName,
                filterMode = filterMode,
                wrapMode = wrapMode,
                hideFlags = HideFlags.HideAndDontSave
            };
            try
            {
                if (!ImageConversion.LoadImage(texture, data))
                {
                    UnityEngine.Object.Destroy(texture);
                    SkillsPlugin.Log.LogWarning(
                        $"EmbeddedResources: Invalid texture data for '{filename}'.");
                    return null;
                }
                texture.filterMode = filterMode;
                texture.wrapMode = wrapMode;
                texture.Apply(false, true);
                return texture;
            }
            catch (Exception ex)
            {
                UnityEngine.Object.Destroy(texture);
                SkillsPlugin.Log.LogWarning(
                    $"EmbeddedResources: Failed to load texture '{filename}': " +
                    ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Load all embedded icon PNGs into the icon cache.
        /// Returns a dictionary of item name -> Sprite.
        /// The result is shared: six call sites (Necromancer and Monk catalog,
        /// gear and aura registration) each used to hold an independent decode
        /// of the whole icon set, which meant six copies of every texture
        /// resident for the session. Callers only ever read from it.
        /// </summary>
        public static Dictionary<string, Sprite> LoadAllIcons()
        {
            if (_iconCache != null) return _iconCache;

            BuildResourceMap();
            var icons = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
            string prefix = "ErenshorSkills.Resources.Icons.";

            foreach (var name in _resourceMap.Keys)
            {
                if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                if (!name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) continue;

                try
                {
                    // Extract item name: remove prefix and .png suffix
                    // Resource names preserve spaces/apostrophes/parens exactly
                    string itemPart = name.Substring(prefix.Length);
                    string itemName = itemPart.Substring(0, itemPart.Length - 4); // remove .png

                    byte[] data = GetBytes(name);
                    if (data == null) continue;

                    Texture2D tex = new Texture2D(64, 64, TextureFormat.RGBA32, false);
                    tex.filterMode = FilterMode.Point;
                    if (ImageConversion.LoadImage(tex, data))
                    {
                        Sprite sprite = Sprite.Create(tex,
                            new Rect(0, 0, tex.width, tex.height),
                            new Vector2(0.5f, 0.5f), 100f);
                        icons[itemName] = sprite;
                    }
                    else
                    {
                        UnityEngine.Object.Destroy(tex);
                        SkillsPlugin.Log.LogWarning(
                            $"EmbeddedResources: Invalid PNG data for '{name}'.");
                    }
                }
                catch (Exception ex)
                {
                    SkillsPlugin.Log.LogWarning($"EmbeddedResources: Failed to load icon '{name}': {ex.Message}");
                }
            }

            SkillsPlugin.Log.LogInfo($"EmbeddedResources: Loaded {icons.Count} icons from DLL.");
            _iconCache = icons;
            return _iconCache;
        }

        /// <summary>
        /// Extract a model file (obj/mtl/png) from embedded resources to a temp directory.
        /// Returns the path to the extracted file, or null if not found.
        /// ObjLoader needs file paths, so we extract to disk on first use.
        /// </summary>
        private static string _tempModelDir;
        public static string ExtractModelFile(string filename)
        {
            if (string.IsNullOrWhiteSpace(filename) || Path.IsPathRooted(filename) ||
                !string.Equals(filename, Path.GetFileName(filename),
                    StringComparison.Ordinal))
            {
                SkillsPlugin.Log.LogWarning("EmbeddedResources: Rejected an unsafe model filename.");
                return null;
            }

            if (_tempModelDir == null)
            {
                _tempModelDir = Path.Combine(Application.temporaryCachePath, "ClassicSkills_Models");
                if (!Directory.Exists(_tempModelDir))
                    Directory.CreateDirectory(_tempModelDir);
            }

            string outPath = Path.Combine(_tempModelDir, filename);
            byte[] data = GetBytesByFilename(filename);
            if (data == null) return null;

            if (File.Exists(outPath))
            {
                try
                {
                    byte[] existing = File.ReadAllBytes(outPath);
                    if (BytesEqual(existing, data)) return outPath;
                }
                catch { }
            }

            string tempPath = outPath + ".tmp." + Guid.NewGuid().ToString("N");
            try
            {
                using (var stream = new FileStream(tempPath, FileMode.CreateNew,
                    FileAccess.Write, FileShare.None))
                {
                    stream.Write(data, 0, data.Length);
                    stream.Flush(true);
                }
                if (File.Exists(outPath))
                {
                    string backup = outPath + ".bak";
                    File.Replace(tempPath, outPath, backup, true);
                    try { if (File.Exists(backup)) File.Delete(backup); } catch { }
                }
                else
                {
                    File.Move(tempPath, outPath);
                }
                tempPath = null;
            }
            catch (PlatformNotSupportedException)
            {
                File.Copy(tempPath, outPath, true);
                File.Delete(tempPath);
                tempPath = null;
            }
            catch (IOException) when (!string.IsNullOrEmpty(tempPath) &&
                File.Exists(tempPath))
            {
                File.Copy(tempPath, outPath, true);
                File.Delete(tempPath);
                tempPath = null;
            }
            finally
            {
                if (!string.IsNullOrEmpty(tempPath))
                {
                    try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                }
            }
            return outPath;
        }

        private static bool BytesEqual(byte[] left, byte[] right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left == null || right == null || left.Length != right.Length) return false;
            for (int i = 0; i < left.Length; i++)
                if (left[i] != right[i]) return false;
            return true;
        }

        /// <summary>Check if the DLL has embedded resources (vs being a lightweight build).</summary>
        public static bool HasEmbeddedResources()
        {
            BuildResourceMap();
            return _resourceMap.Count > 5; // At minimum we'd have icons
        }
    }
}
