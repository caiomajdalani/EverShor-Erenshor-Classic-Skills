using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using HarmonyLib;

namespace ErenshorSkills.Stations
{
    /// <summary>
    /// CRAFTING STATION INTERACTION SYSTEM
    ///
    /// In EverQuest, tradeskill containers were world objects you interacted
    /// with: Forges, Brew Barrels, Looms, Pottery Wheels, Fletching Tables,
    /// and Jeweler's Kits were scattered throughout cities. You'd walk up
    /// and click on them to open the tradeskill window.
    ///
    /// Erenshor already has Forges (left-click to open SmithingUI). This
    /// system does three things:
    ///
    /// 1. HOOKS THE FORGE — When the player left-clicks the existing Forge,
    ///    we intercept it and ALSO open our Smithing tradeskill window
    ///    alongside the game's native smithing UI.
    ///
    /// 2. SPAWNS NEW STATIONS — Places interactable crafting station objects
    ///    in appropriate zones (brew barrels near taverns, looms near
    ///    merchants, etc.). These are simple Unity GameObjects with
    ///    colliders and name labels.
    ///
    /// 3. PROXIMITY DETECTION — Monitors the player's left-click target
    ///    for any objects named as crafting stations, and opens the
    ///    appropriate tradeskill window.
    ///
    /// Station locations are placed to feel natural in Erenshor's world:
    /// - Port Azure (the hub): All station types near the market area
    /// - Stowaway's Step: Basic forge (already exists) + oven
    /// - Other zones: Situational placement based on zone theme
    /// </summary>
    public static class CraftingStations
    {
        internal static int StationMappingCountForTesting => StationMap.Count;
        internal static int StationPlacementCountForTesting => StationPlacements.Length;
        // Station type → tradeskill name mapping
        private static readonly Dictionary<string, string> StationMap
            = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Forge",           "Smithing" },
            { "Furnace",         "Smithing" },
            { "Anvil",           "Smithing" },
            { "Oven",            "Baking" },
            { "Cooking Fire",    "Baking" },
            { "Campfire",        "Baking" },
            { "Brew Barrel",     "Brewing" },
            { "Brewing Kettle",  "Brewing" },
            { "Fletching Table", "Fletching" },
            { "Workbench",       "Fletching" },
            { "Jeweler's Kit",   "Jewelcraft" },
            { "Gem Table",       "Jewelcraft" },
            { "Loom",            "Tailoring" },
            { "Sewing Kit",      "Tailoring" },
            // Specific game objects that work as tradeskill stations
            { "Baking Oven",     "Baking" },
            { "oven1",           "Baking" },
            { "oven2",           "Baking" },
            { "SM_Prop_Pizza_Oven_01",      "Baking" },
            { "SM_Prop_Stove_01",           "Baking" },
            { "SM_Prop_Stove_02",           "Baking" },
            { "SM_Prop_Camp_Stove_01",      "Baking" },

            { "SM_Prop_Barrel_Dispenser_Large_01", "Brewing" },
            { "SM_Prop_Keg_01",             "Brewing" },
            { "SM_Prop_Workbench_01",       "Fletching" },
            { "SM_Prop_Workbench_01_Preset", "Fletching" },
        };

        // Spawned station GameObjects we've placed in the world
        private static List<GameObject> _spawnedStations = new List<GameObject>();
        private static List<StationInteractor> _taggedStations =
            new List<StationInteractor>();
        private const float InteractionRange = 5f;

        // ═════════════════════════════════════════════════════════════
        // Station definitions per zone
        // ═════════════════════════════════════════════════════════════

        /// <summary>
        /// Defines where to spawn crafting stations in each zone.
        /// Positions are approximate and should be tuned in-game.
        /// </summary>
        private struct StationSpawn
        {
            public string Name;
            public string Zone;
            public Vector3 Position;
            public string Tradeskill;

            public StationSpawn(string name, string zone, Vector3 pos, string skill)
            {
                Name = name;
                Zone = zone;
                Position = pos;
                Tradeskill = skill;
            }
        }

        // Station placements — Port Azure has all types, other zones
        // have contextually appropriate ones
        private static readonly StationSpawn[] StationPlacements =
        {
            // ── Port Azure — Main crafting hub ──────────────────────
            // Near the existing forge / Parmalee Gemlyn's market area
            // Forge is around X:252, Y:26, Z:188 based on player coords
            new StationSpawn("Oven",            "Azure", new Vector3(285.83f, 26.16f, 230.22f), "Baking"),
            new StationSpawn("Brew Barrel",     "Azure", new Vector3(286.78f, 26.14f, 237.12f), "Brewing"),
            new StationSpawn("Fletching Table", "Azure", new Vector3(229.27f, 26.06f, 188.42f), "Fletching"),
            new StationSpawn("Jeweler's Kit",   "Azure", new Vector3(259.14f, 26.03f, 194.91f), "Jewelcraft"),
            new StationSpawn("Loom",            "Azure", new Vector3(255.36f, 26.03f, 207.07f), "Tailoring"),

            // ── Stowaway's Step — Starter zone, town area ──────────
            new StationSpawn("Oven",            "Stowaway", new Vector3(711f, 21.3f, 515f), "Baking"),
            new StationSpawn("Brew Barrel",     "Stowaway", new Vector3(714f, 21.3f, 519f), "Brewing"),
            new StationSpawn("Fletching Table", "Stowaway", new Vector3(716f, 21.3f, 515f), "Fletching"),
            new StationSpawn("Jeweler's Kit",   "Stowaway", new Vector3(711f, 21.3f, 519f), "Jewelcraft"),
            new StationSpawn("Loom",            "Stowaway", new Vector3(716f, 21.3f, 519f), "Tailoring"),

            // Other zones: stations disabled until positions are confirmed in-game
            // Windwashed, Silkengrass, Braxonian, Soluna, Ripper — TODO
        };

        // ═════════════════════════════════════════════════════════════
        // Station spawning
        // ═════════════════════════════════════════════════════════════

        /// <summary>
        /// Called when a zone loads. Spawns appropriate crafting stations.
        /// </summary>
        public static void OnZoneLoad(string sceneName)
        {
            // Clean up old stations
            DespawnAll();
            _taggedStations.RemoveAll(interactor => interactor == null);

            foreach (var spawn in StationPlacements)
            {
                if (spawn.Zone != sceneName) continue;

                try
                {
                    SpawnStation(spawn);
                }
                catch (Exception ex)
                {
                    SkillsPlugin.Log.LogError(
                        $"Failed to spawn station '{spawn.Name}' in {sceneName}: {ex.Message}");
                }
            }

            // Scan for known game objects and tag them as tradeskill stations.
            // Uses exact name prefix matching against a whitelist.
            try
            {
                int tagged = 0;
                var tagRules = new[] {
                    // Baking: campfires, ovens, stoves, cooking racks
                    new { Prefix = "SM_Prop_CampFire_01",    Station = "Campfire",      Skill = "Baking" },
                    new { Prefix = "SM_Env_CampFire_01",     Station = "Campfire",      Skill = "Baking" },
                    new { Prefix = "TFF_Camp_Fire_01A",      Station = "Campfire",      Skill = "Baking" },
                    new { Prefix = "SM_Prop_Bonfire_01",     Station = "Campfire",      Skill = "Baking" },
                    new { Prefix = "Baking Oven",            Station = "Baking Oven",   Skill = "Baking" },
                    new { Prefix = "oven1",                  Station = "Oven",          Skill = "Baking" },
                    new { Prefix = "oven2",                  Station = "Oven",          Skill = "Baking" },
                    new { Prefix = "SM_Prop_Pizza_Oven_01",  Station = "Oven",          Skill = "Baking" },
                    new { Prefix = "SM_Prop_Stove_01",       Station = "Oven",          Skill = "Baking" },
                    new { Prefix = "SM_Prop_Stove_02",       Station = "Oven",          Skill = "Baking" },
                    new { Prefix = "SM_Prop_Camp_Stove_01",  Station = "Oven",          Skill = "Baking" },
                    // SM_Prop_Cooking_Rack_01 excluded — too common as decoration
                    // Brewing: barrel dispensers, kegs
                    new { Prefix = "SM_Prop_Barrel_Dispenser_Large_01", Station = "Brew Barrel", Skill = "Brewing" },
                    new { Prefix = "SM_Prop_Keg_01",        Station = "Brew Barrel",   Skill = "Brewing" },
                    // Fletching: workbenches (NOT alchemy workbenches)
                    new { Prefix = "SM_Prop_Workbench_01",   Station = "Workbench",     Skill = "Fletching" },
                };

                var allObjects = GameObject.FindObjectsOfType<GameObject>();
                foreach (var obj in allObjects)
                {
                    if (obj == null) continue;
                    if (obj.GetComponent<StationInteractor>() != null) continue;
                    string name = obj.name;

                    foreach (var rule in tagRules)
                    {
                        // Match exact name or name with Unity clone suffix like " (1)"
                        if (name == rule.Prefix || name.StartsWith(rule.Prefix + " ") ||
                            name.StartsWith(rule.Prefix + "("))
                        {
                            // Exclude alchemy workbenches
                            if (name.Contains("Alchemy")) break;
                            // Exclude sub-parts (doors, drawers, flues, pipes, wheels)
                            if (name.Contains("_Door_") || name.Contains("_Drawer_") ||
                                name.Contains("_Flue") || name.Contains("_Pipe") ||
                                name.Contains("_Guard_") || name.Contains("_Brazier_") ||
                                name.Contains("_Wheel")) break;

                            var interactor = obj.AddComponent<StationInteractor>();
                            interactor.StationName = rule.Station;
                            interactor.TradeskillName = rule.Skill;
                            _taggedStations.Add(interactor);
                            tagged++;
                            break;
                        }
                    }
                }
                if (tagged > 0)
                    SkillsPlugin.Log.LogInfo($"Tagged {tagged} game objects as tradeskill stations");
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogWarning($"Station scan error: {ex.Message}");
            }
        }

        /// <summary>
        /// Create a crafting station GameObject in the world.
        /// Uses a simple primitive shape with a text label, collider,
        /// and a custom component for interaction.
        /// </summary>
        private static void SpawnStation(StationSpawn spawn)
        {
            // Try to load a 3D model for this station type
            string modelName = GetModelName(spawn.Tradeskill);
            string modelsDir = System.IO.Path.Combine(
                RuntimePaths.AssetDirectory, "Models");
            string objPath = System.IO.Path.Combine(modelsDir, modelName + ".obj");
            string texPath = System.IO.Path.Combine(modelsDir, modelName + ".png");

            // Try embedded resources first if disk files don't exist
            if (!System.IO.File.Exists(objPath))
            {
                string embeddedObj = EmbeddedResources.ExtractModelFile(modelName + ".obj");
                if (embeddedObj != null) objPath = embeddedObj;
                string embeddedMtl = EmbeddedResources.ExtractModelFile(modelName + ".mtl");
                // mtl is auto-discovered by ObjLoader
            }
            if (!System.IO.File.Exists(texPath))
            {
                string embeddedTex = EmbeddedResources.ExtractModelFile(modelName + ".png");
                if (embeddedTex != null) texPath = embeddedTex;
            }

            GameObject station = null;
            if (System.IO.File.Exists(objPath))
            {
                station = ObjLoader.Load(objPath, texPath);
            }

            // Fallback to primitive cube if model not found
            if (station == null)
            {
                station = GameObject.CreatePrimitive(PrimitiveType.Cube);
                var renderer = station.GetComponent<Renderer>();
                if (renderer != null)
                {
                    Material mat = new Material(Shader.Find("Standard"));
                    mat.color = GetStationColor(spawn.Tradeskill);
                    renderer.sharedMaterial = mat;
                    var assets = station.AddComponent<RuntimeGeneratedAssets>();
                    assets.Track(mat);
                }
            }

            station.name = spawn.Name;
            station.transform.localScale = GetModelScale(spawn.Tradeskill);

            // Position the station. OBJ models from Meshy have their pivot at center,
            // so we offset upward to place the bottom of the mesh at ground level.
            var mf = station.GetComponent<MeshFilter>();
            if (mf != null && mf.mesh != null)
            {
                var bounds = mf.mesh.bounds;
                // bounds.min.y is negative (bottom of mesh below origin)
                // Multiply by scale to get world-space offset
                float bottomOffset = -bounds.min.y * station.transform.localScale.y;
                // Small lift to prevent z-fighting with ground
                bottomOffset += 0.05f;
                station.transform.position = spawn.Position + new Vector3(0, bottomOffset, 0);
                SkillsPlugin.Log.LogInfo(
                    $"Station '{spawn.Name}' bounds: min.y={bounds.min.y:F3} max.y={bounds.max.y:F3} " +
                    $"scale={station.transform.localScale.y:F2} offset={bottomOffset:F3}");
            }
            else
            {
                station.transform.position = spawn.Position;
            }

            // Set the layer to the interactable layer
            station.layer = LayerMask.NameToLayer("Default");

            // Add our interaction component
            var interactor = station.AddComponent<StationInteractor>();
            interactor.StationName = spawn.Name;
            interactor.TradeskillName = spawn.Tradeskill;

            // Add a label above the station
            CreateStationLabel(station, spawn.Name);

            // Add the interactable light beam (same as the game forge)
            AddLightBeam(station);

            _spawnedStations.Add(station);

            SkillsPlugin.Log.LogInfo(
                $"Spawned crafting station '{spawn.Name}' ({spawn.Tradeskill}) " +
                $"at {spawn.Position}");
        }

        /// <summary>
        /// Adds an interactable light beam to a crafting station,
        /// matching the visual indicator on the game's existing Forge.
        /// First tries to clone the game's SpecialLootBeam prefab from
        /// GameManager. If not found, creates a procedural vertical beam
        /// using a Line Renderer with additive blending.
        /// </summary>
        private static void AddLightBeam(GameObject station)
        {
            try
            {
                // Try to find and clone the game's native beam
                var gm = UnityEngine.Object.FindObjectOfType<GameManager>();
                if (gm != null)
                {
                    // SpecialLootBeam is a public GameObject field on GameManager
                    var beamPrefab = gm.SpecialLootBeam;
                    if (beamPrefab != null)
                    {
                        var beam = UnityEngine.Object.Instantiate(beamPrefab,
                            station.transform);
                        beam.transform.localPosition = Vector3.zero;
                        beam.SetActive(true);
                        SkillsPlugin.Log.LogInfo(
                            $"  Added game beam to '{station.name}'");
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogWarning(
                    $"  Could not clone game beam: {ex.Message}");
            }

            // Fallback: procedural vertical beam using LineRenderer
            try
            {
                var beamGO = new GameObject("LightBeam");
                beamGO.transform.SetParent(station.transform);
                beamGO.transform.localPosition = Vector3.zero;

                var lr = beamGO.AddComponent<LineRenderer>();
                lr.positionCount = 2;
                lr.SetPosition(0, station.transform.position);
                lr.SetPosition(1, station.transform.position + Vector3.up * 12f);
                lr.startWidth = 0.15f;
                lr.endWidth = 0.02f;
                lr.useWorldSpace = true;

                // Additive glow material
                var mat = new Material(Shader.Find("Particles/Standard Unlit"));
                if (mat != null)
                {
                    Color beamColor = new Color(0.5f, 0.9f, 1f, 0.3f); // Soft cyan
                    mat.SetColor("_Color", beamColor);
                    mat.SetFloat("_Mode", 1f); // Additive
                    lr.sharedMaterial = mat;
                    var assets = beamGO.AddComponent<RuntimeGeneratedAssets>();
                    assets.Track(mat);
                    lr.startColor = new Color(0.5f, 0.9f, 1f, 0.5f);
                    lr.endColor = new Color(0.5f, 0.9f, 1f, 0.0f);
                }
                else
                {
                    // Absolute fallback: default line material
                    lr.startColor = new Color(0.5f, 0.9f, 1f, 0.4f);
                    lr.endColor = new Color(0.5f, 0.9f, 1f, 0.0f);
                }

                // Add a point light at the base for glow
                var lightGO = new GameObject("StationLight");
                lightGO.transform.SetParent(station.transform);
                lightGO.transform.localPosition = new Vector3(0, 1.5f, 0);
                var light = lightGO.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = new Color(0.5f, 0.9f, 1f);
                light.intensity = 0.8f;
                light.range = 4f;
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogWarning(
                    $"  Fallback beam also failed: {ex.Message}");
            }
        }

        /// <summary>Create a floating text label above a station.</summary>
        private static void CreateStationLabel(GameObject parent, string name)
        {
            try
            {
                // Create a world-space text mesh above the station
                GameObject label = new GameObject($"{name}_Label");
                label.transform.SetParent(parent.transform);
                label.transform.localPosition = new Vector3(0, 1.5f, 0);

                var textMesh = label.AddComponent<TextMesh>();
                textMesh.text = name;
                textMesh.fontSize = 24;
                textMesh.characterSize = 0.08f;
                textMesh.alignment = TextAlignment.Center;
                textMesh.anchor = TextAnchor.MiddleCenter;
                textMesh.color = Color.white;

                // Billboard — always face the camera
                var billboard = label.AddComponent<BillboardLabel>();
            }
            catch { }
        }

        private static void DespawnAll()
        {
            foreach (var station in _spawnedStations)
            {
                if (station != null)
                    GameObject.Destroy(station);
            }
            _spawnedStations.Clear();
        }

        public static void Shutdown()
        {
            DespawnAll();
            foreach (var interactor in _taggedStations)
                if (interactor != null) GameObject.Destroy(interactor);
            _taggedStations.Clear();
        }

        private static string GetModelName(string tradeskill)
        {
            switch (tradeskill)
            {
                case "Baking":     return "Oven";
                case "Brewing":    return "BrewBarrel";
                case "Fletching":  return "FletchingTable";
                case "Jewelcraft": return "JewelerKit";
                case "Tailoring":  return "Loom";
                default:           return "Oven";
            }
        }

        private static Vector3 GetModelScale(string tradeskill)
        {
            // Adjust scale per model to look right in the game world
            switch (tradeskill)
            {
                case "Baking":     return new Vector3(0.8f, 0.8f, 0.8f);
                case "Brewing":    return new Vector3(0.7f, 0.7f, 0.7f);
                case "Fletching":  return new Vector3(0.8f, 0.8f, 0.8f);
                case "Jewelcraft": return new Vector3(0.7f, 0.7f, 0.7f);
                case "Tailoring":  return new Vector3(0.8f, 0.8f, 0.8f);
                default:           return new Vector3(0.8f, 0.8f, 0.8f);
            }
        }

        private static Color GetStationColor(string tradeskill)
        {
            switch (tradeskill)
            {
                case "Smithing":   return new Color(0.4f, 0.3f, 0.2f);  // Brown
                case "Baking":     return new Color(0.6f, 0.45f, 0.2f); // Warm tan
                case "Brewing":    return new Color(0.3f, 0.2f, 0.1f);  // Dark wood
                case "Fletching":  return new Color(0.35f, 0.3f, 0.15f); // Wood tone
                case "Jewelcraft": return new Color(0.25f, 0.25f, 0.35f);// Gem blue
                case "Tailoring":  return new Color(0.35f, 0.2f, 0.3f); // Fabric purple
                default:           return new Color(0.3f, 0.3f, 0.3f);
            }
        }

        // ═════════════════════════════════════════════════════════════
        // Player interaction detection
        // ═════════════════════════════════════════════════════════════

        /// <summary>
        /// Check if a clicked/targeted object is a crafting station.
        /// Called by Harmony patches on the player interaction system.
        /// Returns true if the object was a station and the window opened.
        /// </summary>
        public static bool TryInteractWithStation(GameObject target)
        {
            if (target == null) return false;

            bool isKnownStation = target.GetComponent<StationInteractor>() != null ||
                StationMap.ContainsKey(target.name);
            if (!isKnownStation) return false;
            var player = GameData.PlayerControl;
            if (player == null || Vector3.Distance(
                player.transform.position, target.transform.position) > InteractionRange)
            {
                ChatHelper.Send(
                    "<color=#EF5350>[Tradeskills]</color> You are too far away from that station.");
                return true;
            }

            // Only interact with objects that have our StationInteractor component
            var interactor = target.GetComponent<StationInteractor>();
            if (interactor != null)
            {
                UI.TradeskillWindow.Open(interactor.TradeskillName);
                ChatHelper.Send(
                    $"<color=#FF9800>[{interactor.TradeskillName}]</color> " +
                    $"You open the {interactor.StationName}.");
                return true;
            }

            // Check for exact name matches against StationMap
            // (game objects like "Baking Oven" that exist in the world)
            string name = target.name;
            if (StationMap.TryGetValue(name, out string tradeskill))
            {
                UI.TradeskillWindow.Open(tradeskill);
                ChatHelper.Send(
                    $"<color=#FF9800>[{tradeskill}]</color> " +
                    $"You open the {name}.");
                return true;
            }

            return false;
        }

        /// <summary>
        /// Check if the player is within interaction range of any station.
        /// Used for proximity-based prompts.
        /// </summary>
        public static string GetNearbyStationName(Vector3 playerPos, float range = 4f)
        {
            foreach (var station in _spawnedStations)
            {
                if (station == null) continue;
                float dist = Vector3.Distance(playerPos, station.transform.position);
                if (dist <= range)
                    return station.name;
            }
            return null;
        }
    }

    // ═════════════════════════════════════════════════════════════════
    // Station interactor component (attached to spawned stations)
    // ═════════════════════════════════════════════════════════════════

    /// <summary>
    /// MonoBehaviour attached to spawned crafting station objects.
    /// Handles click detection via Unity's OnMouseDown.
    /// </summary>
    public class StationInteractor : MonoBehaviour
    {
        public string StationName;
        public string TradeskillName;

        private void OnMouseDown()
        {
            CraftingStations.TryInteractWithStation(gameObject);
        }

        private void OnMouseEnter()
        {
            // Show tooltip hint when hovering
            // This uses Unity's cursor hover detection
        }
    }

    /// <summary>
    /// Simple billboard component that makes a label always face the camera.
    /// </summary>
    public class BillboardLabel : MonoBehaviour
    {
        private void Update()
        {
            // Match the game's native NamePlate billboard behavior exactly
            try
            {
                Vector3 camPos;
                if (!GameData.PlayerControl.FPV.gameObject.activeSelf)
                {
                    if (!GameData.PlayerControl.DroneMode)
                        camPos = GameData.GameCamPos.position;
                    else
                        camPos = GameData.PlayerControl.DroneCam.transform.position;
                }
                else
                {
                    camPos = GameData.CamControl.FPV.transform.position;
                }
                // LookAt then flip 180 on Y to un-mirror the text
                transform.LookAt(camPos);
                transform.Rotate(0, 180f, 0);
            }
            catch
            {
                var cam = Camera.main;
                if (cam != null)
                {
                    transform.LookAt(cam.transform);
                    transform.Rotate(0, 180f, 0);
                }
            }
        }
    }

    // ═════════════════════════════════════════════════════════════════
    // Harmony patches for interaction hooks
    // ═════════════════════════════════════════════════════════════════

    /// <summary>
    /// Hook into the Forge/SmithingUI opening to also open our
    /// enhanced Smithing tradeskill window.
    /// When the player left-clicks the existing game Forge, the game
    /// opens its native SmithingUI. We postfix that to ALSO open our
    /// Smithing window so players get both the vanilla forge and our
    /// enhanced recipe system.
    /// </summary>
    [HarmonyPatch(typeof(Smithing), "OpenWindow")]
    public static class Patch_OpenForge
    {
        public static void Postfix()
        {
            try
            {
                UI.TradeskillWindow.Open("Smithing");
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogError($"Forge hook error: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// When the player zones into a new area, spawn the appropriate
    /// crafting stations for that zone.
    /// </summary>
    [HarmonyPatch(typeof(GameManager), "ShowZoneName")]
    public static class Patch_ZoneLoad_Stations
    {
        public static void Postfix(string _name)
        {
            try
            {
                string scene = SceneManager.GetActiveScene().name;
                CraftingStations.OnZoneLoad(scene);

            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogError(
                    $"Station zone load error: {ex.Message}");
            }
        }
    }
}
