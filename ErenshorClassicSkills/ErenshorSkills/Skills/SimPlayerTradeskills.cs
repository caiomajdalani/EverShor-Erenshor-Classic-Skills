using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.AI;

namespace ErenshorSkills.Skills
{
    /// <summary>
    /// Simulates tradeskill behavior for SimPlayers via the game's native
    /// whisper system. Players whisper a sim to request crafting, negotiate
    /// price, and receive items through the trade window.
    ///
    /// Flow:
    ///   1. Player: /whisper SimName craft 5 Copper Rivets
    ///   2. Sim whispers back with success/fail + gold price quote
    ///   3. Player: /whisper SimName yes  (or no)
    ///   4. Sim says "On my way!" and walks to the player
    ///   5. Sim opens trade window with the crafted items
    ///
    /// Virtual tradeskill levels for sims:
    ///   Class specialty: (simLevel * 5) + 5, capped at 180
    ///   Other tradeskills: 30% of specialty cap
    /// </summary>
    public static class SimPlayerTradeskills
    {
        private static float _nextChatTime = 0f;


        // ═══════════════════════════════════════════════════════════════
        // PENDING CRAFT OFFERS — tracks active negotiations
        // ═══════════════════════════════════════════════════════════════

        /// <summary>A pending craft offer from a sim waiting for yes/no.</summary>
        public class PendingCraftOffer
        {
            public string SimName;
            public Tradeskills.Recipe Recipe;
            public string Tradeskill;
            public int Quantity;
            public int TotalGoldCost;
            public bool CraftSucceeded;
            public ActiveWTS SourceWts;
            public float ExpiresAt; // Time.time when offer expires

            public PendingCraftOffer(string simName, Tradeskills.Recipe recipe, string tradeskill,
                int quantity, int totalGold, bool succeeded,
                ActiveWTS sourceWts = null)
            {
                SimName = simName;
                Recipe = recipe;
                Tradeskill = tradeskill;
                Quantity = quantity;
                TotalGoldCost = totalGold;
                CraftSucceeded = succeeded;
                SourceWts = sourceWts;
                ExpiresAt = Time.time + 120f; // 2 minute window to respond
            }

            public bool IsExpired => Time.time > ExpiresAt;
        }

        // One pending offer per sim name (case-insensitive key)
        private static readonly Dictionary<string, PendingCraftOffer> _pendingOffers
            = new Dictionary<string, PendingCraftOffer>(StringComparer.OrdinalIgnoreCase);

        // Fail cooldowns: key = "SimName|RecipeName" (lowercase), value = Time.time when cooldown expires
        private static readonly Dictionary<string, float> _failCooldowns
            = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

        // Sim-learned recipes: key = "SimName" (case-insensitive), value = set of recipe names
        // Persisted to disk so sims remember recipes across sessions
        private static Dictionary<string, HashSet<string>> _simLearnedRecipes
            = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        private const int SimRecipeSaveVersion = 2;
        private static bool _simRecipePersistenceHealthy = true;

        [Serializable]
        private class SimRecipeSaveEnvelope
        {
            public int Version = SimRecipeSaveVersion;
            public List<SimRecipeSaveRecord> Sims = new List<SimRecipeSaveRecord>();
        }

        [Serializable]
        private class SimRecipeSaveRecord
        {
            public string Name = "";
            public List<string> Recipes = new List<string>();
        }

        // ═══════════════════════════════════════════════════════════════
        // WTB TRACKING — tracks what sims are actively buying
        // ═══════════════════════════════════════════════════════════════

        /// <summary>An active WTB request from a sim that the player can sell to.</summary>
        public class ActiveWTB
        {
            public string SimName;
            public string ItemName;
            public int GoldPrice;
            public float ExpiresAt;
            public bool Fulfilled;

            public ActiveWTB(string simName, string itemName, int goldPrice, float expirySeconds)
            {
                SimName = simName;
                ItemName = itemName;
                GoldPrice = goldPrice;
                ExpiresAt = Time.time + expirySeconds;
                Fulfilled = false;
            }

            public bool IsExpired => Time.time > ExpiresAt;
        }

        // Active WTB requests: key = SimName (case-insensitive)
        private static readonly Dictionary<string, ActiveWTB> _activeWTBs
            = new Dictionary<string, ActiveWTB>(StringComparer.OrdinalIgnoreCase);

        // Tracks sims that are walking to the player for a WTB sale
        private static readonly HashSet<string> _wtbPendingSale
            = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // ═══════════════════════════════════════════════════════════════
        // WTS TRACKING — tracks what sims are actively selling
        // ═══════════════════════════════════════════════════════════════

        /// <summary>An active WTS listing from a sim the player can buy from.</summary>
        public class ActiveWTS
        {
            public string SimName;
            public Tradeskills.Recipe Recipe;
            public string Tradeskill;
            public int GoldPrice;
            public float ExpiresAt;

            public ActiveWTS(string simName, Tradeskills.Recipe recipe, string tradeskill, int goldPrice, float expirySeconds)
            {
                SimName = simName;
                Recipe = recipe;
                Tradeskill = tradeskill;
                GoldPrice = goldPrice;
                ExpiresAt = Time.time + expirySeconds;
            }

            public bool IsExpired => Time.time > ExpiresAt;
        }

        // Active WTS listings: key = SimName (case-insensitive)
        private static readonly Dictionary<string, ActiveWTS> _activeWTSs
            = new Dictionary<string, ActiveWTS>(StringComparer.OrdinalIgnoreCase);

        private static void RestoreWtsListing(PendingCraftOffer offer)
        {
            ActiveWTS listing = offer?.SourceWts;
            if (listing == null || listing.IsExpired ||
                !SkillsPlugin.CfgEnableSimWTS.Value) return;
            if (!_activeWTSs.ContainsKey(listing.SimName))
                _activeWTSs[listing.SimName] = listing;
        }

        private static string GetCooldownKey(string simName, string recipeName)
            => $"{simName}|{recipeName}".ToLowerInvariant();

        private static bool IsOnCooldown(string simName, string recipeName)
        {
            string key = GetCooldownKey(simName, recipeName);
            return _failCooldowns.TryGetValue(key, out float expires) && Time.time < expires;
        }

        private static void SetCooldown(string simName, string recipeName)
        {
            string key = GetCooldownKey(simName, recipeName);
            _failCooldowns[key] = Time.time + SkillsPlugin.CfgSimCraftFailCooldown.Value;
        }

        // ═══════════════════════════════════════════════════════════════
        // VIRTUAL TRADESKILL LEVELS
        // ═══════════════════════════════════════════════════════════════

        public static int GetSimTradeskillLevel(Character simChar, string tradeskill)
        {
            try
            {
                int level = simChar.MyStats?.Level ?? 0;
                if (level <= 0) return 0;
                string className = simChar.MyStats?.CharacterClass?.ClassName ?? "";
                string specialty = GetClassTradeskill(className);
                if (tradeskill == specialty)
                    return Mathf.Clamp(level * 5 + 5, 0, 180);
                int specCap = Mathf.Clamp(level * 5 + 5, 0, 180);
                return Mathf.RoundToInt(specCap * 0.3f);
            }
            catch { return 0; }
        }

        /// <summary>Overload for SimPlayerTracking (used in whisper context where
        /// we only have tracking data, not the live Character component).</summary>
        public static int GetSimTradeskillLevelFromTracking(SimPlayerTracking sim, string tradeskill)
        {
            try
            {
                int level = sim.Level;
                if (level <= 0) return 0;
                string className = sim.ClassName ?? "";
                string specialty = GetClassTradeskill(className);
                if (tradeskill == specialty)
                {
                    // Specialty: full (level * 5) + 5, cap 180
                    return Mathf.Clamp(level * 5 + 5, 0, 180);
                }
                // Non-specialty: 30% of specialty cap, capped at 54
                int specCap = Mathf.Clamp(level * 5 + 5, 0, 180);
                return Mathf.RoundToInt(specCap * 0.3f);
            }
            catch { return 0; }
        }

        public static string GetClassTradeskill(string className)
        {
            switch (className)
            {
                case "Paladin":     return "Smithing";
                case "Duelist":     return "Fletching";
                case "Reaver":      return "Brewing";
                case "Druid":       return "Baking";
                case "Arcanist":    return "Jewelcraft";
                case "Stormcaller": return "Tailoring";
                default:            return "Smithing";
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // WHISPER INTERCEPTION — called from Harmony patch on
        // SimPlayerMngr.ProcessWhisper (Prefix)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Intercepts a whisper to a sim player. Returns true if we handled
        /// it (craft request or yes/no response), false to let the game
        /// process it normally.
        /// </summary>
        public static bool TryHandleWhisper(SimPlayerTracking whichSim, string incomingMsg)
        {
            if (!SkillsPlugin.CfgEnableSimCraft.Value) return false;
            if (whichSim == null || string.IsNullOrEmpty(incomingMsg)) return false;

            string msg = incomingMsg.Trim().ToLowerInvariant();
            if (SkillsPlugin.CfgEnableDebugCommands?.Value == true)
                SkillsPlugin.Log.LogInfo(
                    $"[SimCraft] TryHandleWhisper: sim='{whichSim.SimName}', msg='{msg}'");

            // ── Check for trade retry yes/no first ────────────────
            if (_retryTrades.ContainsKey(whichSim.SimName))
            {
                if (TryHandleRetryResponse(whichSim, msg))
                    return true;
            }

            // ── Check for yes/no response to a pending offer ──────────
            if (_pendingOffers.ContainsKey(whichSim.SimName))
            {
                var offer = _pendingOffers[whichSim.SimName];
                if (offer.IsExpired)
                {
                    RestoreWtsListing(offer);
                    _pendingOffers.Remove(whichSim.SimName);
                    QueueSimWhisper(whichSim, GetPersonalityLine(whichSim,
                        "That offer expired. Whisper me again if you want something crafted.",
                        "Too slow! Offer's gone. Try again.",
                        "Oh, I already put away my tools. Ask again if you need something."));
                    return true;
                }

                if (msg == "yes" || msg == "y" || msg == "yeah" || msg == "sure" ||
                    msg == "ok" || msg == "deal" || msg == "accept")
                {
                    return HandleAcceptOffer(whichSim, offer);
                }
                else if (msg == "no" || msg == "n" || msg == "nah" || msg == "pass" ||
                         msg == "nope" || msg == "decline" || msg == "nevermind")
                {
                    RestoreWtsListing(offer);
                    _pendingOffers.Remove(whichSim.SimName);
                    QueueSimWhisper(whichSim, GetPersonalityLine(whichSim,
                        "No worries! Let me know if you change your mind.",
                        "Your loss. I'll find another buyer.",
                        "Alright, maybe next time then!"));
                    return true;
                }
                // If they whispered something else, clear the offer and fall through
                // to check if it's a new craft request
                RestoreWtsListing(offer);
                _pendingOffers.Remove(whichSim.SimName);
            }

            // ── Check for sell command (WTB system) ─────────────────
            // Patterns: "sell Iron Sword", "I'll sell you Iron Sword"
            if (msg.Contains("sell"))
            {
                return HandleSellWhisper(whichSim, incomingMsg);
            }

            // ── Check for buy command (WTS system) ─────────────────
            // Patterns: "buy Moonforged Hatchet", "I'll buy Moonforged Hatchet"
            if (msg.Contains("buy"))
            {
                return HandleBuyWhisper(whichSim, incomingMsg);
            }

            // ── Check for craft request ────────────────────────────────
            // Patterns: "craft 5 Copper Rivets", "craft Copper Rivets",
            //           "can you craft 5 Copper Rivets", "make me 3 Iron Sword"
            if (msg.Contains("craft") || msg.Contains("make") || msg.Contains("smith") ||
                msg.Contains("brew") || msg.Contains("bake") || msg.Contains("fletch") ||
                msg.Contains("tailor") || msg.Contains("jewel"))
            {
                return HandleCraftRequest(whichSim, incomingMsg);
            }

            return false; // not handled, let game process normally
        }

        // ═══════════════════════════════════════════════════════════════
        // CRAFT REQUEST HANDLING
        // ═══════════════════════════════════════════════════════════════

        private static bool HandleCraftRequest(SimPlayerTracking whichSim, string rawMsg)
        {
            try
            {
                // Parse: "Craft [X] Item Name" where X is optional quantity
                // Also handles: "can you craft 5 Copper Rivets", "make me 3 Iron Sword"
                string cleaned = rawMsg.Trim();

                // Strip polite preambles
                cleaned = Regex.Replace(cleaned, @"^(can you |could you |please |would you |i need |i want )", "", RegexOptions.IgnoreCase);

                // Strip the action verb + optional "me"/"some"
                cleaned = Regex.Replace(cleaned, @"^(craft|make|smith|brew|bake|fletch|tailor|jewel(craft)?)(\s+(me|some|us))?\s+", "", RegexOptions.IgnoreCase);
                cleaned = cleaned.Trim();

                // Extract quantity: "5 Copper Rivets" → qty=5, name="Copper Rivets"
                // Or just: "Copper Rivets" → qty=1
                int quantity = 1;
                var qtyMatch = Regex.Match(cleaned, @"^(\d+)\s+(.+)$");
                string recipeName;
                if (qtyMatch.Success)
                {
                    if (!int.TryParse(qtyMatch.Groups[1].Value, out quantity))
                        quantity = 20;
                    quantity = Mathf.Clamp(quantity, 1, 20);
                    recipeName = qtyMatch.Groups[2].Value.Trim();
                }
                else
                {
                    recipeName = cleaned;
                }

                if (SkillsPlugin.CfgEnableDebugCommands?.Value == true)
                    SkillsPlugin.Log.LogInfo(
                        $"[SimCraft] Parsed craft request: qty={quantity}, recipe='{recipeName}'");

                if (string.IsNullOrEmpty(recipeName))
                {
                    QueueSimWhisper(whichSim, GetPersonalityLine(whichSim,
                        "What do you want me to craft? Whisper me like: craft 5 Copper Rivets",
                        "Craft what? Be specific. Tell me what and how many.",
                        "Hmm, I need to know what you want! Try: craft Copper Rivets"));
                    return true;
                }

                // Search all tradeskills for this recipe (fuzzy match)
                string[] allTrades = { "Smithing", "Baking", "Brewing", "Fletching", "Jewelcraft", "Tailoring" };
                Tradeskills.Recipe foundRecipe = null;
                string foundTradeskill = null;

                foreach (var ts in allTrades)
                {
                    var r = Tradeskills.FindRecipe(ts, recipeName);
                    if (r != null)
                    {
                        foundRecipe = r;
                        foundTradeskill = ts;
                        break;
                    }
                }

                if (foundRecipe == null)
                {
                    QueueSimWhisper(whichSim, GetPersonalityLine(whichSim,
                        $"I don't know any recipe called '{recipeName}'. You sure about that?",
                        $"'{recipeName}'? Never heard of it. Check your recipe list.",
                        $"Hmm, I don't think '{recipeName}' is a real recipe. Double check?"));
                    return true;
                }

                // Check if sim "knows" this recipe (high-tier recipes are rare knowledge)
                if (!SimKnowsRecipe(whichSim, foundRecipe))
                {
                    QueueSimWhisper(whichSim, GetPersonalityLine(whichSim,
                        $"I haven't learned the recipe for {foundRecipe.Name} yet. Sorry!",
                        $"{foundRecipe.Name}? Don't know that one. Find someone more experienced.",
                        $"Oh, I haven't found the recipe for {foundRecipe.Name}. Maybe someone else knows it?"));
                    return true;
                }

                // Check fail cooldown for this sim + recipe
                if (IsOnCooldown(whichSim.SimName, foundRecipe.Name))
                {
                    QueueSimWhisper(whichSim, GetPersonalityLine(whichSim,
                        $"I'm out of materials for {foundRecipe.Name} right now. Try again in a bit.",
                        $"No can do. Used up all my {foundRecipe.Name} supplies. Give me a minute.",
                        $"Oh, I just used the last of my materials for {foundRecipe.Name}! I need to restock, ask me again soon."));
                    return true;
                }

                // Check sim's virtual tradeskill level
                int simSkill = GetSimTradeskillLevelFromTracking(whichSim, foundTradeskill);
                if (simSkill < foundRecipe.MinSkillLevel)
                {
                    string specialty = GetClassTradeskill(whichSim.ClassName ?? "");
                    if (foundTradeskill == specialty)
                    {
                        QueueSimWhisper(whichSim, GetPersonalityLine(whichSim,
                            $"My {foundTradeskill} isn't high enough for {foundRecipe.Name} yet. I need a few more levels.",
                            $"Can't do {foundRecipe.Name}. My {foundTradeskill} is only {simSkill}. Need {foundRecipe.MinSkillLevel}.",
                            $"I wish I could make {foundRecipe.Name}, but my {foundTradeskill} needs more work."));
                    }
                    else
                    {
                        QueueSimWhisper(whichSim, GetPersonalityLine(whichSim,
                            $"{foundTradeskill} isn't really my thing. Try asking someone who specializes in it.",
                            $"I'm a {specialty} specialist, not {foundTradeskill}. Ask someone else.",
                            $"My {foundTradeskill} is only {simSkill}... you'd want a {foundTradeskill} specialist for that."));
                    }
                    return true;
                }

                // Roll success/fail for each item in the quantity
                float baseChance = 40f + (simSkill - foundRecipe.MinSkillLevel) * 1.5f;
                baseChance = Mathf.Clamp(baseChance, 15f, 95f);

                int successes = 0;
                for (int i = 0; i < quantity; i++)
                {
                    if (UnityEngine.Random.value * 100f < baseChance)
                        successes++;
                }

                if (successes == 0)
                {
                    // Total failure — set cooldown and whisper
                    SetCooldown(whichSim.SimName, foundRecipe.Name);
                    QueueSimWhisper(whichSim, GetPersonalityLine(whichSim,
                        $"I tried to make {quantity}x {foundRecipe.Name} but botched every single one. Sorry, I'm out of materials now.",
                        $"Ugh, failed all {quantity} combines on {foundRecipe.Name}. Wasted all my supplies. Not my day.",
                        $"Oh no... I ruined all {quantity} attempts at {foundRecipe.Name}. I need to get more materials!"));
                    return true;
                }

                // Calculate gold cost: base item value + crafting markup
                int perItemCost = Mathf.Max(foundRecipe.GoldValue, 1);
                int markup = Mathf.RoundToInt(perItemCost * 0.5f); // 50% markup for the service
                int costPerItem = perItemCost + markup;
                int totalCost = costPerItem * successes;

                // 20% guild discount if sim is in the same guild as the player
                bool guildDiscount = false;
                try
                {
                    string playerGuild = GameData.PlayerControl?.MyGuild;
                    string simGuild = whichSim.GuildID;
                    if (!string.IsNullOrEmpty(playerGuild) && !string.IsNullOrEmpty(simGuild) &&
                        playerGuild == simGuild)
                    {
                        guildDiscount = true;
                        totalCost = Mathf.RoundToInt(totalCost * 0.8f);
                    }
                }
                catch { }

                // Store the pending offer
                var offer = new PendingCraftOffer(
                    whichSim.SimName, foundRecipe, foundTradeskill,
                    successes, totalCost, true);
                _pendingOffers[whichSim.SimName] = offer;

                // Build the whisper response
                string itemWord = successes == 1 ? foundRecipe.Name : $"{successes}x {foundRecipe.Name}";
                string failNote = (successes < quantity)
                    ? $" I botched {quantity - successes} of them though."
                    : "";
                string discountNote = guildDiscount ? " Guild discount applied!" : "";

                QueueSimWhisper(whichSim, GetPersonalityLine(whichSim,
                    $"I made {itemWord} for you!{failNote} That'll be {totalCost} gold.{discountNote} Whisper me yes or no.",
                    $"Done. {itemWord}.{failNote} {totalCost} gold, take it or leave it.{discountNote} Yes or no?",
                    $"Yay, {itemWord} came out great!{failNote} I'd need {totalCost} gold for my trouble.{discountNote} Yes or no?"));

                return true;
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogError($"SimCraft whisper error: {ex}");
                return false;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // WTB SYSTEM — player sells to sim
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Called from a Harmony postfix when a sim shouts a WTB message.
        /// Tracks the item so the player can whisper to sell it.
        /// </summary>
        public static void OnSimWTBShout(string simName, string itemName, int goldPrice)
        {
            if (!SkillsPlugin.CfgEnableSimWTB.Value ||
                string.IsNullOrWhiteSpace(simName) ||
                string.IsNullOrWhiteSpace(itemName) || goldPrice <= 0) return;
            float expiry = SkillsPlugin.CfgSimWTBExpiry.Value;
            _activeWTBs[simName] = new ActiveWTB(simName, itemName, goldPrice, expiry);
            SkillsPlugin.Log.LogInfo($"[SimWTB] Tracked: {simName} WTB {itemName} for {goldPrice}g (expires in {expiry}s)");
        }

        /// <summary>Handle "/whisper SimName sell ItemName"</summary>
        private static bool HandleSellWhisper(SimPlayerTracking whichSim, string rawMsg)
        {
            try
            {
                if (!SkillsPlugin.CfgEnableSimWTB.Value)
                {
                    QueueSimWhisper(whichSim, "I'm not looking to buy anything right now.");
                    return true;
                }

                // Parse: "sell Iron Sword", "I'll sell you Iron Sword"
                string cleaned = rawMsg.Trim();
                cleaned = Regex.Replace(cleaned, @"^(i'll |i will |let me |)(sell|give)(\s+(you|ya))?(\s+my)?\s+", "", RegexOptions.IgnoreCase);
                cleaned = cleaned.Trim();

                if (string.IsNullOrEmpty(cleaned))
                {
                    QueueSimWhisper(whichSim, GetPersonalityLine(whichSim,
                        "Sell me what? Tell me the item name.",
                        "Sell what? Be specific.",
                        "What are you trying to sell me?"));
                    return true;
                }

                // Check if this sim has an active WTB via our tracking
                // OR check the live SimPlayer.WTB field as a fallback
                ActiveWTB wtb = null;
                if (_activeWTBs.ContainsKey(whichSim.SimName))
                {
                    wtb = _activeWTBs[whichSim.SimName];
                    if (wtb.IsExpired || wtb.Fulfilled)
                    {
                        _activeWTBs.Remove(whichSim.SimName);
                        wtb = null;
                    }
                }

                // Fallback: read the sim's live WTB field directly
                if (wtb == null)
                {
                    SimPlayer liveSim = FindActiveSimByName(whichSim.SimName);
                    if (liveSim != null && liveSim.WTB != null)
                    {
                        // Create a tracking entry from the live WTB field
                        wtb = new ActiveWTB(whichSim.SimName, liveSim.WTB.ItemName,
                            liveSim.WTB.ItemValue, SkillsPlugin.CfgSimWTBExpiry.Value);
                        _activeWTBs[whichSim.SimName] = wtb;
                    }
                }

                if (wtb == null)
                {
                    QueueSimWhisper(whichSim, GetPersonalityLine(whichSim,
                        "I'm not looking to buy anything right now.",
                        "I didn't ask to buy anything. Check the shout channel.",
                        "Hmm, I don't need anything at the moment."));
                    return true;
                }

                // Check if the item matches what the sim wants (case-insensitive)
                if (!wtb.ItemName.Equals(cleaned, StringComparison.OrdinalIgnoreCase))
                {
                    QueueSimWhisper(whichSim, GetPersonalityLine(whichSim,
                        $"That's not what I'm looking for. I need {wtb.ItemName}.",
                        $"I said I want {wtb.ItemName}, not that.",
                        $"Hmm, I actually need {wtb.ItemName}. Do you have one?"));
                    return true;
                }

                // Match! Sim will come to the player so they can trade via native window
                wtb.Fulfilled = true; // reserve it
                _wtbPendingSale.Add(whichSim.SimName);

                SimPlayer activeSim = FindActiveSimByName(whichSim.SimName);

                if (activeSim != null)
                {
                    Transform playerTransform = GameData.PlayerControl?.transform;
                    float dist = (playerTransform != null)
                        ? Vector3.Distance(activeSim.transform.position, playerTransform.position)
                        : 999f;

                    if (dist < 5f)
                    {
                        QueueSimWhisper(whichSim, GetPersonalityLine(whichSim,
                            $"You have {wtb.ItemName}? Great! I'll pay {wtb.GoldPrice} gold. Trade it to me!",
                            $"Hand it over. {wtb.GoldPrice} gold, as promised.",
                            $"Yay! You have it! Just open a trade with me, I've got {wtb.GoldPrice} gold ready!"));
                    }
                    else
                    {
                        QueueSimWhisper(whichSim, GetPersonalityLine(whichSim,
                            $"You have {wtb.ItemName}? Awesome! I'll come to you. Just trade it to me when I get there.",
                            $"About time someone had one. On my way. {wtb.GoldPrice} gold when I arrive.",
                            $"Oh wonderful! Let me run over to you! I'll pay {wtb.GoldPrice} gold!"));

                        SkillsPlugin.Instance.StartCoroutine(
                            WalkToPlayerForWTB(activeSim, whichSim));
                    }
                }
                else
                {
                    // Sim in another zone
                    string playerScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                    string simZone = FormatZoneName(whichSim.CurScene);
                    QueueSimWhisper(whichSim, GetPersonalityLine(whichSim,
                        $"You have {wtb.ItemName}? I'm over in {simZone} but I'll head your way!",
                        $"I'm in {simZone}. Hang tight, I need to zone over. {wtb.GoldPrice} gold when I arrive.",
                        $"Yay! I'm in {simZone} right now! Let me travel to you!"));

                    whichSim.TravelToZone(playerScene);
                    SkillsPlugin.Instance.StartCoroutine(
                        WaitForSimArrivalForWTB(whichSim));
                }

                return true;
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogError($"SimWTB sell error: {ex}");
                return false;
            }
        }

        /// <summary>Walk sim to player for WTB, then let player trade via native window.</summary>
        private static IEnumerator WalkToPlayerForWTB(SimPlayer sim, SimPlayerTracking tracking)
        {
            NavMeshAgent nav = sim.GetComponent<NavMeshAgent>();
            Transform playerTransform = GameData.PlayerControl?.transform;
            if (nav == null || playerTransform == null)
            {
                _wtbPendingSale.Remove(tracking.SimName);
                yield break;
            }

            bool wasSeekingPlayer = tracking.seekingPlayer;
            bool wasStayInZone = tracking.StayInZone;
            tracking.seekingPlayer = true;
            tracking.StayInZone = true;

            // First walk attempt (30s)
            float timeout = Time.time + 30f;
            while (Time.time < timeout)
            {
                if (playerTransform == null || sim == null)
                {
                    tracking.seekingPlayer = wasSeekingPlayer;
                    tracking.StayInZone = wasStayInZone;
                    _wtbPendingSale.Remove(tracking.SimName);
                    yield break;
                }
                float dist = Vector3.Distance(sim.transform.position, playerTransform.position);
                if (dist < 4f) break;
                try
                {
                    nav.SetDestination(GameData.GetSafeNavMeshPoint(playerTransform.position));
                    nav.isStopped = false;
                    nav.speed = Mathf.Max(nav.speed, 3.5f);
                }
                catch { }
                yield return null;
            }

            // Check if close enough — if not, second attempt
            if (sim != null && playerTransform != null)
            {
                float finalDist = Vector3.Distance(sim.transform.position, playerTransform.position);
                if (finalDist > 8f)
                {
                    QueueSimWhisper(tracking, GetPersonalityLine(tracking,
                        "Almost there, hang on!",
                        "Stuck on something. Give me a sec.",
                        "Wait for me! Almost there!"));

                    float timeout2 = Time.time + 30f;
                    while (Time.time < timeout2)
                    {
                        if (playerTransform == null || sim == null)
                        {
                            tracking.seekingPlayer = wasSeekingPlayer;
                            tracking.StayInZone = wasStayInZone;
                            _wtbPendingSale.Remove(tracking.SimName);
                            yield break;
                        }
                        float d = Vector3.Distance(sim.transform.position, playerTransform.position);
                        if (d < 5f) break;
                        try
                        {
                            nav.SetDestination(GameData.GetSafeNavMeshPoint(playerTransform.position));
                            nav.isStopped = false;
                            nav.speed = Mathf.Max(nav.speed, 3.5f);
                        }
                        catch { }
                        yield return null;
                    }
                }
            }

            // Final distance check — if still too far, give up
            if (sim == null || playerTransform == null)
            {
                tracking.seekingPlayer = wasSeekingPlayer;
                tracking.StayInZone = wasStayInZone;
                _wtbPendingSale.Remove(tracking.SimName);
                yield break;
            }
            if (Vector3.Distance(sim.transform.position, playerTransform.position) > 10f)
            {
                if (nav != null) try { nav.isStopped = false; } catch { }
                tracking.seekingPlayer = wasSeekingPlayer;
                tracking.StayInZone = wasStayInZone;
                _wtbPendingSale.Remove(tracking.SimName);
                if (_activeWTBs.ContainsKey(tracking.SimName))
                    _activeWTBs[tracking.SimName].Fulfilled = false;
                QueueSimWhisper(tracking, GetPersonalityLine(tracking,
                    "I can't seem to reach you. Whisper me again when you're somewhere I can get to.",
                    "Can't get to you. Try again later.",
                    "I'm stuck! Try whispering me again when you move somewhere else."));
                yield break;
            }

            // DON'T restore AI yet — keep sim locked in place while waiting for trade.
            // The game's AI would immediately walk them away otherwise.

            // Sim arrived — player must now open native trade window by clicking on sim.
            UpdateSocialLog.LogAdd(new ChatLogLine(
                $"{tracking.SimName} says: " + GetPersonalityLine(tracking,
                    "I'm here! Trade me the item.",
                    "Here. Give it to me.",
                    "Here I am! Open a trade with me!"),
                ChatLogLine.LogType.Say, "#CCCCCC"));

            // Keep sim stationary and wait for trade to happen or timeout
            float tradeTimeout = Time.time + 120f;
            while (Time.time < tradeTimeout)
            {
                // Keep overriding nav to hold position near player
                if (sim != null && nav != null && playerTransform != null)
                {
                    try
                    {
                        nav.SetDestination(sim.transform.position); // stay put
                        nav.isStopped = true;
                    }
                    catch { }
                }

                // Check if WTB was fulfilled (CompleteTrade clears WTB field)
                SimPlayer freshSim = FindActiveSimByName(tracking.SimName);
                if (freshSim == null || freshSim.WTB == null)
                {
                    // Trade completed! Cleanly release the sim
                    if (nav != null)
                    {
                        try { nav.isStopped = false; } catch { }
                    }
                    tracking.seekingPlayer = wasSeekingPlayer;
                    tracking.StayInZone = wasStayInZone;
                    _wtbPendingSale.Remove(tracking.SimName);
                    _activeWTBs.Remove(tracking.SimName);
                    yield break;
                }
                yield return new WaitForSeconds(0.5f);
            }

            // Timed out or trade done — cleanly release the sim
            if (nav != null)
            {
                try { nav.isStopped = false; } catch { }
            }
            tracking.seekingPlayer = wasSeekingPlayer;
            tracking.StayInZone = wasStayInZone;
            _wtbPendingSale.Remove(tracking.SimName);
            if (_activeWTBs.ContainsKey(tracking.SimName))
            {
                _activeWTBs[tracking.SimName].Fulfilled = false; // un-reserve
            }
            QueueSimWhisper(tracking, GetPersonalityLine(tracking,
                "I guess you changed your mind. Let me know if you find one later.",
                "You wasted my time. Whatever.",
                "Oh... I thought you had it. Maybe next time!"));
        }

        /// <summary>Wait for sim to arrive from another zone for WTB, then walk to player.</summary>
        private static IEnumerator WaitForSimArrivalForWTB(SimPlayerTracking tracking)
        {
            // First wait: 60 seconds
            float timeout = Time.time + 60f;
            SimPlayer arrivedSim = null;
            while (Time.time < timeout)
            {
                arrivedSim = FindActiveSimByName(tracking.SimName);
                if (arrivedSim != null) break;
                yield return new WaitForSeconds(1f);
            }

            if (arrivedSim == null)
            {
                // Halfway message, then wait another 60s
                QueueSimWhisper(tracking, GetPersonalityLine(tracking,
                    "Almost there! Hang on a bit longer.",
                    "Traffic is rough. Still coming.",
                    "I'm running as fast as I can!"));

                float timeout2 = Time.time + 60f;
                while (Time.time < timeout2)
                {
                    arrivedSim = FindActiveSimByName(tracking.SimName);
                    if (arrivedSim != null) break;
                    yield return new WaitForSeconds(1f);
                }
            }

            if (arrivedSim == null)
            {
                _wtbPendingSale.Remove(tracking.SimName);
                if (_activeWTBs.ContainsKey(tracking.SimName))
                    _activeWTBs[tracking.SimName].Fulfilled = false;
                QueueSimWhisper(tracking, GetPersonalityLine(tracking,
                    "I got lost on the way. Whisper me again if you still have it.",
                    "Couldn't find you. Forget it.",
                    "Oh no, I got totally lost! Try again?"));
                yield break;
            }

            // Reset seekingPlayer before handoff so WalkToPlayerForWTB captures
            // wasSeekingPlayer=false and restores properly after trade
            tracking.seekingPlayer = false;
            tracking.StayInZone = false;
            yield return WalkToPlayerForWTB(arrivedSim, tracking);
        }

        // ═══════════════════════════════════════════════════════════════
        // WTS SYSTEM — sim sells to player
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Called from ambient chat to register a WTS listing backed by real items.
        /// </summary>
        public static void RegisterWTSListing(string simName, Tradeskills.Recipe recipe,
            string tradeskill, int goldPrice)
        {
            if (!SkillsPlugin.CfgEnableSimWTS.Value ||
                string.IsNullOrWhiteSpace(simName) || recipe == null ||
                string.IsNullOrWhiteSpace(tradeskill) || goldPrice <= 0) return;
            float expiry = SkillsPlugin.CfgSimWTBExpiry.Value; // same timer
            _activeWTSs[simName] = new ActiveWTS(simName, recipe, tradeskill, goldPrice, expiry);
            SkillsPlugin.Log.LogInfo($"[SimWTS] Tracked: {simName} WTS {recipe.Name} for {goldPrice}g");
        }

        /// <summary>Handle "/whisper SimName buy ItemName"</summary>
        private static bool HandleBuyWhisper(SimPlayerTracking whichSim, string rawMsg)
        {
            try
            {
                if (!SkillsPlugin.CfgEnableSimWTS.Value)
                {
                    QueueSimWhisper(whichSim, "I'm not selling anything right now.");
                    return true;
                }

                // Parse: "buy Moonforged Hatchet", "I'll buy Moonforged Hatchet"
                string cleaned = rawMsg.Trim();
                cleaned = Regex.Replace(cleaned, @"^(i'll |i will |i want to |i wanna |let me |can i |)(buy|purchase|get)(\s+(your|a|the))?\s+", "", RegexOptions.IgnoreCase);
                cleaned = cleaned.Trim();

                if (string.IsNullOrEmpty(cleaned))
                {
                    QueueSimWhisper(whichSim, GetPersonalityLine(whichSim,
                        "Buy what? Tell me the item name.",
                        "Buy what? Spit it out.",
                        "What would you like to buy?"));
                    return true;
                }

                // Check if sim has an active WTS listing for this item
                if (!_activeWTSs.ContainsKey(whichSim.SimName))
                {
                    // No active WTS — but maybe they can craft it on demand?
                    // Fall through to craft system by returning false
                    QueueSimWhisper(whichSim, GetPersonalityLine(whichSim,
                        "I'm not selling anything right now. But I can craft for you! Try: craft " + cleaned,
                        "Nothing for sale. Want me to make it? Whisper: craft " + cleaned,
                        "I don't have stock right now, but I could craft it! Try: craft " + cleaned));
                    return true;
                }

                var wts = _activeWTSs[whichSim.SimName];

                if (wts.IsExpired)
                {
                    _activeWTSs.Remove(whichSim.SimName);
                    QueueSimWhisper(whichSim, GetPersonalityLine(whichSim,
                        "Sorry, I already sold that. Whisper me to craft a new one.",
                        "Sold already. You were too slow.",
                        "Oh, someone already bought it! I can craft another though!"));
                    return true;
                }

                // Check if item name matches the WTS listing
                if (!wts.Recipe.Name.Equals(cleaned, StringComparison.OrdinalIgnoreCase))
                {
                    QueueSimWhisper(whichSim, GetPersonalityLine(whichSim,
                        $"I'm selling {wts.Recipe.Name}, not that. Want to buy it?",
                        $"That's not what I have. I've got {wts.Recipe.Name}. Yes or no?",
                        $"I actually have {wts.Recipe.Name} for sale. Interested?"));
                    return true;
                }

                // Match! Create a pending offer — WTS items are pre-crafted (no fail roll)
                _activeWTSs.Remove(whichSim.SimName);

                // Apply guild discount
                int totalCost = wts.GoldPrice;
                bool guildDiscount = false;
                try
                {
                    string playerGuild = GameData.PlayerControl?.MyGuild;
                    string simGuild = whichSim.GuildID;
                    if (!string.IsNullOrEmpty(playerGuild) && !string.IsNullOrEmpty(simGuild) &&
                        playerGuild == simGuild)
                    {
                        guildDiscount = true;
                        totalCost = Mathf.RoundToInt(totalCost * 0.8f);
                    }
                }
                catch { }

                var offer = new PendingCraftOffer(
                    whichSim.SimName, wts.Recipe, wts.Tradeskill,
                    1, totalCost, true, wts);
                _pendingOffers[whichSim.SimName] = offer;

                string discountNote = guildDiscount ? " Guild discount applied!" : "";
                QueueSimWhisper(whichSim, GetPersonalityLine(whichSim,
                    $"Sure! {wts.Recipe.Name} for {totalCost} gold.{discountNote} Whisper yes to confirm.",
                    $"{wts.Recipe.Name}, {totalCost} gold.{discountNote} Yes or no?",
                    $"Great! {wts.Recipe.Name} is yours for {totalCost} gold!{discountNote} Whisper yes!"));

                return true;
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogError($"SimWTS buy error: {ex}");
                return false;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // TEACH SIM A RECIPE VIA TRADE WINDOW
        // Called from Harmony prefix on SimTradeWindow.CompleteTrade
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Complete addon-tracked WTB sales without the native trade bug that
        /// treats a non-stackable item's quality as a stack count. Item removal
        /// and gold payment share the same crash-recovery journal.
        /// </summary>
        public static bool TryHandleWTBTrade(SimTradeWindow tradeWindow)
        {
            bool recognizedRequest = false;
            try
            {
                if (!SkillsPlugin.CfgEnableSimWTB.Value ||
                    !SkillsSaveManager.HasLoaded) return false;
                var lootSlot = tradeWindow?.LootSlot;
                Item offered = lootSlot?.MyItem;
                if (offered == null || offered == GameData.PlayerInv?.Empty) return false;

                SimPlayer sim = GetTradeSim(tradeWindow);
                if (sim == null || !_activeWTBs.TryGetValue(sim.transform.name,
                    out ActiveWTB request)) return false;
                if (request.IsExpired)
                {
                    _activeWTBs.Remove(sim.transform.name);
                    return false;
                }
                if (!string.Equals(request.ItemName, offered.ItemName,
                    StringComparison.OrdinalIgnoreCase) || sim.WTB == null ||
                    !string.Equals(sim.WTB.ItemName, offered.ItemName,
                        StringComparison.OrdinalIgnoreCase)) return false;
                recognizedRequest = true;

                if (request.GoldPrice <= 0 || GameData.PlayerInv.Gold >
                    int.MaxValue - request.GoldPrice)
                {
                    ChatHelper.Send(
                        "<color=#EF5350>[Sim WTB]</color> The sale cannot complete because the gold total would overflow.");
                    return true;
                }
                if (!TryMoveTradeItemToInventory(tradeWindow,
                    requireNonStackable: false, out ItemIcon inventorySlot)) return true;

                var mutation = Items.BagSystem.BeginExternalMutation();
                try
                {
                    if (offered.Stackable && inventorySlot.Quantity > 1)
                        inventorySlot.Quantity--;
                    else
                    {
                        inventorySlot.MyItem = GameData.PlayerInv.Empty;
                        inventorySlot.Quantity = 1;
                    }
                    inventorySlot.UpdateSlotImage();
                    GameData.PlayerInv.Gold += request.GoldPrice;
                    GameData.PlayerInv.UpdatePlayerInventory();

                    if (!Items.BagSystem.CommitExternalMutation(
                        "sim-wtb-sale", mutation))
                        throw new InvalidOperationException(
                            "The WTB inventory and gold transaction could not be saved.");
                }
                catch (Exception ex)
                {
                    Items.BagSystem.RollbackExternalMutation(mutation);
                    SkillsPlugin.Log.LogError($"Sim WTB sale rolled back: {ex}");
                    ChatHelper.Send(
                        "<color=#EF5350>[Sim WTB]</color> The sale was cancelled; your item and gold were restored.");
                    try { tradeWindow.CloseWindow(new List<ItemIcon>()); } catch { }
                    return true;
                }

                sim.WTB = null;
                request.Fulfilled = true;
                _activeWTBs.Remove(sim.transform.name);
                _wtbPendingSale.Remove(sim.transform.name);
                UpdateSocialLog.LogAdd(new ChatLogLine(
                    sim.transform.name + " says: Thanks! I owe you " +
                    request.GoldPrice + " gold, here it is."));
                tradeWindow.CloseWindow(new List<ItemIcon>());
                return true;
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogError($"Sim WTB trade error: {ex}");
                return recognizedRequest;
            }
        }

        /// <summary>
        /// Checks if the item being traded to a sim is a recipe scroll.
        /// If so, teaches the sim the recipe (or rejects if already known/wrong class).
        /// Returns true if we handled it (caller should skip default trade logic).
        /// </summary>
        public static bool TryHandleRecipeTrade(SimTradeWindow tradeWindow)
        {
            bool recognizedRecipe = false;
            try
            {
                if (!SkillsPlugin.CfgEnableSimCraft.Value) return false;

                var lootSlot = tradeWindow.LootSlot;
                if (lootSlot == null || lootSlot.MyItem == null) return false;

                Item item = lootSlot.MyItem;
                string itemName = item.ItemName ?? "";

                // Only handle recipe scrolls (named "Recipe: XXX")
                if (!itemName.StartsWith("Recipe: ")) return false;

                string recipeName = itemName.Substring(8).Trim(); // strip "Recipe: "
                if (string.IsNullOrEmpty(recipeName)) return false;

                // Get the sim from the trade window's parent transform
                // SimTradeWindow stores the sim's transform in a private 'parent' field
                SimPlayer sim = GetTradeSim(tradeWindow);
                if (sim == null) return false;

                string simName = sim.transform.name;
                int simIndex = sim.myIndex;
                SimPlayerTracking tracking = (simIndex >= 0 && simIndex < GameData.SimMngr.Sims.Count)
                    ? GameData.SimMngr.Sims[simIndex] : null;
                if (tracking == null) return false;

                // Find the actual recipe
                string[] allTrades = { "Smithing", "Baking", "Brewing", "Fletching", "Jewelcraft", "Tailoring" };
                Tradeskills.Recipe foundRecipe = null;
                string foundTradeskill = null;
                foreach (var ts in allTrades)
                {
                    var r = Tradeskills.FindRecipe(ts, recipeName);
                    if (r != null) { foundRecipe = r; foundTradeskill = ts; break; }
                }

                if (foundRecipe == null) return false; // not a real recipe, let normal trade handle it
                recognizedRecipe = true;

                // Check if sim already knows this recipe
                if (SimKnowsRecipe(tracking, foundRecipe))
                {
                    if (!TryReturnRecipeScroll(tradeWindow, out _)) return true;
                    // Reject the trade — return the scroll to the player
                    UpdateSocialLog.LogAdd(new ChatLogLine(
                        $"[WHISPER FROM] {simName}: " + GetPersonalityLine(tracking,
                            $"I already know {foundRecipe.Name}! Here, take it back.",
                            $"I know {foundRecipe.Name} already. Keep your scroll.",
                            $"Oh, I learned {foundRecipe.Name} a while ago! You keep this."),
                        ChatLogLine.LogType.Whisper, "#FF62D1"));

                    tradeWindow.CloseWindow(new List<ItemIcon>());
                    return true; // skip default CompleteTrade
                }

                // Check if the sim's class specialty matches
                string specialty = GetClassTradeskill(tracking.ClassName ?? "");
                if (foundTradeskill != specialty)
                {
                    if (!TryReturnRecipeScroll(tradeWindow, out _)) return true;
                    UpdateSocialLog.LogAdd(new ChatLogLine(
                        $"[WHISPER FROM] {simName}: " + GetPersonalityLine(tracking,
                            $"That's a {foundTradeskill} recipe. I focus on {specialty}. You should find a {foundTradeskill} specialist.",
                            $"I'm a {specialty} crafter, not {foundTradeskill}. Give it to someone else.",
                            $"Oh, that's {foundTradeskill}... I only do {specialty}. Sorry!"),
                        ChatLogLine.LogType.Whisper, "#FF62D1"));

                    tradeWindow.CloseWindow(new List<ItemIcon>());
                    return true;
                }

                if (!_simRecipePersistenceHealthy)
                {
                    if (!TryReturnRecipeScroll(tradeWindow, out _)) return true;
                    UpdateSocialLog.LogAdd(new ChatLogLine(
                        $"[WHISPER FROM] {simName}: My recipe records are unavailable. " +
                        "I returned the scroll; try again after the save issue is repaired.",
                        ChatLogLine.LogType.Whisper, "#FF62D1"));
                    tradeWindow.CloseWindow(new List<ItemIcon>());
                    return true;
                }

                // First move the offered scroll into a normal inventory slot.
                // The shared transaction can then atomically consume it while
                // committing the new sim-recipe sidecar state.
                if (!TryReturnRecipeScroll(tradeWindow, out ItemIcon inventorySlot))
                    return true;

                var mutation = Items.BagSystem.BeginExternalMutation();
                string recipesPath = GetSimRecipesPath();
                string recipesBefore = SerializeSimRecipes();
                bool createdSet = !_simLearnedRecipes.TryGetValue(simName, out var recipes);
                if (createdSet)
                {
                    recipes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    _simLearnedRecipes[simName] = recipes;
                }

                bool committed = false;
                try
                {
                    inventorySlot.MyItem = GameData.PlayerInv.Empty;
                    inventorySlot.Quantity = 1;
                    inventorySlot.UpdateSlotImage();
                    GameData.PlayerInv.UpdatePlayerInventory();

                    if (!recipes.Add(foundRecipe.Name))
                        throw new InvalidOperationException("The recipe was already recorded.");
                    string recipesAfter = SerializeSimRecipes();
                    if (!Items.BagSystem.CommitExternalMutation(
                        "teach-sim-recipe", mutation, recipesPath,
                        recipesBefore, recipesAfter))
                        throw new InvalidOperationException(
                            "The recipe and inventory transaction could not be saved.");
                    committed = true;
                }
                catch (Exception ex)
                {
                    recipes.Remove(foundRecipe.Name);
                    if (createdSet && recipes.Count == 0)
                        _simLearnedRecipes.Remove(simName);
                    Items.BagSystem.RollbackExternalMutation(
                        mutation, recipesPath, recipesBefore);
                    SkillsPlugin.Log.LogError($"Sim recipe teaching rolled back: {ex}");
                    UpdateSocialLog.LogAdd(new ChatLogLine(
                        $"[WHISPER FROM] {simName}: I couldn't safely record that recipe. " +
                        "I returned the scroll; please try again later.",
                        ChatLogLine.LogType.Whisper, "#FF62D1"));
                }

                try { tradeWindow.CloseWindow(new List<ItemIcon>()); }
                catch (Exception closeError)
                {
                    SkillsPlugin.Log.LogWarning($"Sim recipe trade window close failed: {closeError.Message}");
                }
                if (!committed) return true;

                UpdateSocialLog.LogAdd(new ChatLogLine(
                    $"[WHISPER FROM] {simName}: " + GetPersonalityLine(tracking,
                        $"Oh wow, {foundRecipe.Name}! I've been wanting to learn this. Thank you so much!",
                        $"{foundRecipe.Name}, huh? Nice. I can make this now. Don't expect a discount though.",
                        $"Yay! {foundRecipe.Name}! I can't wait to try this recipe! You're the best!"),
                    ChatLogLine.LogType.Whisper, "#FF62D1"));

                ChatHelper.Send(
                    $"<color=#FF9800>[{foundTradeskill}]</color> " +
                    $"<color=#4CAF50>{simName} learned the recipe for {foundRecipe.Name}!</color>");

                return true;
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogError($"SimCraft recipe trade error: {ex}");
                if (recognizedRecipe)
                    ChatHelper.Send("<color=#EF5350>[Sim Craft]</color> The recipe trade was stopped to protect your scroll.");
                return recognizedRecipe;
            }
        }

        private static bool TryReturnRecipeScroll(SimTradeWindow tradeWindow,
            out ItemIcon inventorySlot)
        {
            return TryMoveTradeItemToInventory(tradeWindow,
                requireNonStackable: true, out inventorySlot);
        }

        private static bool TryMoveTradeItemToInventory(SimTradeWindow tradeWindow,
            bool requireNonStackable, out ItemIcon inventorySlot)
        {
            inventorySlot = null;
            var lootSlot = tradeWindow?.LootSlot;
            var inventory = GameData.PlayerInv;
            Item item = lootSlot?.MyItem;
            if (lootSlot == null || inventory?.StoredSlots == null || item == null ||
                item == inventory.Empty || (requireNonStackable && item.Stackable)) return false;

            int quantity = Mathf.Max(1, lootSlot.Quantity);
            if (item.Stackable)
            {
                foreach (var slot in inventory.StoredSlots)
                {
                    if (slot == null || slot.TrashSlot || slot.MyItem != item ||
                        slot.Quantity > int.MaxValue - quantity) continue;
                    slot.Quantity += quantity;
                    slot.UpdateSlotImage();
                    lootSlot.MyItem = inventory.Empty;
                    lootSlot.Quantity = 1;
                    lootSlot.UpdateSlotImage();
                    inventory.UpdatePlayerInventory();
                    inventorySlot = slot;
                    return true;
                }
            }

            foreach (var slot in inventory.StoredSlots)
            {
                if (slot == null || slot.TrashSlot || slot.MyItem != inventory.Empty) continue;
                slot.MyItem = item;
                slot.Quantity = quantity;
                slot.UpdateSlotImage();
                lootSlot.MyItem = inventory.Empty;
                lootSlot.Quantity = 1;
                lootSlot.UpdateSlotImage();
                inventory.UpdatePlayerInventory();
                inventorySlot = slot;
                return true;
            }

            ChatHelper.Send(
                "<color=#EF5350>[Sim Trade]</color> Your inventory is full. " +
                "Make a free slot; the offered item remains in the trade window.");
            return false;
        }

        private static SimPlayer GetTradeSim(SimTradeWindow tradeWindow)
        {
            try
            {
                var parentField = typeof(SimTradeWindow).GetField("parent",
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);
                var parentTransform = parentField?.GetValue(tradeWindow) as Transform;
                return parentTransform?.GetComponent<SimPlayer>();
            }
            catch { return null; }
        }

        // ═══════════════════════════════════════════════════════════════
        // ACCEPT OFFER — sim walks to player and opens trade window
        // ═══════════════════════════════════════════════════════════════

        /// <summary>Active trade session — tracks the current trade in progress
        /// so we can detect when the trade window closes and ask to retry.</summary>
        public class ActiveTradeSession
        {
            public SimPlayerTracking Sim;
            public SimPlayer ActiveSim;
            public PendingCraftOffer Offer;
            public Item CraftedItem;
            public bool TradeWindowOpen;
            public bool TradeCompleted;
            public bool WaitingForRetryAnswer;
            public int DeclineCount;
            public const int MAX_DECLINES = 3;
        }

        // Only one active trade at a time
        public static ActiveTradeSession CurrentTrade = null;

        private static bool HandleAcceptOffer(SimPlayerTracking whichSim, PendingCraftOffer offer)
        {
            try
            {
                _pendingOffers.Remove(whichSim.SimName);

                // Prevent overlapping trades
                if (CurrentTrade != null)
                {
                    RestoreWtsListing(offer);
                    QueueSimWhisper(whichSim, GetPersonalityLine(whichSim,
                        "You're already in a trade with someone else. Finish that first.",
                        "I can see you're busy. Come back later.",
                        "One trade at a time! Finish your current deal first."));
                    return true;
                }

                // Check if sim is in the same zone as player (active in scene)
                SimPlayer activeSim = FindActiveSimByName(whichSim.SimName);

                if (activeSim != null)
                {
                    // Check if sim is already close (e.g. in player's group)
                    Transform playerTransform = GameData.PlayerControl?.transform;
                    float dist = (playerTransform != null)
                        ? Vector3.Distance(activeSim.transform.position, playerTransform.position)
                        : 999f;

                    if (dist < 5f)
                    {
                        // Already close enough — open trade immediately
                        QueueSimWhisper(whichSim, GetPersonalityLine(whichSim,
                            "Sure thing! Here you go.",
                            "Right here. Let's do this.",
                            "Oh you're right here! One sec."));

                        // Small delay so the whisper shows first
                        SkillsPlugin.Instance.StartCoroutine(
                            DelayedOpenTrade(activeSim, whichSim, offer));
                    }
                    else
                    {
                        // Sim is in zone but needs to walk over
                        QueueSimWhisper(whichSim, GetPersonalityLine(whichSim,
                            "Deal! I'm on my way to you now.",
                            "Alright, heading your way. Don't move.",
                            "Great! Coming to you now, hold on!"));  // same zone, no need to mention it

                        SkillsPlugin.Instance.StartCoroutine(
                            WalkToPlayerAndTrade(activeSim, whichSim, offer));
                    }
                }
                else
                {
                    // Sim is in a different zone — use the game's native zone
                    // travel system to bring them to the player's zone.
                    // Once they arrive, the game spawns them at a zone-in point
                    // and whispers "I'm here, heading your way."
                    string playerScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;

                    string simZone = FormatZoneName(whichSim.CurScene);
                    QueueSimWhisper(whichSim, GetPersonalityLine(whichSim,
                        $"Deal! I'm over in {simZone} but I'll head your way. Give me a moment.",
                        $"On it. I'm in {simZone}, gotta zone over first. Hang tight.",
                        $"Sure thing! I'm in {simZone} right now, let me travel to you!"));

                    // Tell the sim to travel to the player's zone
                    whichSim.TravelToZone(playerScene);

                    // Start a coroutine that waits for the sim to arrive
                    // in the scene, then walks them to the player and trades
                    SkillsPlugin.Instance.StartCoroutine(
                        WaitForSimArrivalAndTrade(whichSim, offer));
                }

                return true;
            }
            catch (Exception ex)
            {
                RestoreWtsListing(offer);
                SkillsPlugin.Log.LogError($"SimCraft accept error: {ex}");
                return false;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // WALK TO PLAYER AND OPEN TRADE WINDOW
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Waits for a sim to arrive in the player's zone (via TravelToZone),
        /// then hands off to WalkToPlayerAndTrade.
        /// </summary>
        private static IEnumerator WaitForSimArrivalAndTrade(SimPlayerTracking tracking, PendingCraftOffer offer)
        {
            // Wait up to 60 seconds for the sim to spawn in the scene
            float timeout = Time.time + 60f;

            SimPlayer arrivedSim = null;
            while (Time.time < timeout)
            {
                arrivedSim = FindActiveSimByName(tracking.SimName);
                if (arrivedSim != null)
                    break;

                yield return new WaitForSeconds(1f);
            }

            if (arrivedSim == null)
            {
                // Sim still hasn't arrived — keep waiting another 60s
                QueueSimWhisper(tracking, GetPersonalityLine(tracking,
                    "Almost there! Hang on a bit longer.",
                    "Traffic is rough. Still coming.",
                    "I'm running as fast as I can!"));

                float timeout2 = Time.time + 60f;
                while (Time.time < timeout2)
                {
                    arrivedSim = FindActiveSimByName(tracking.SimName);
                    if (arrivedSim != null)
                        break;
                    yield return new WaitForSeconds(1f);
                }
            }

            if (arrivedSim == null)
            {
                // Last resort — re-trigger travel and give up on this attempt
                QueueSimWhisper(tracking, GetPersonalityLine(tracking,
                    "I can't seem to find my way to you. Whisper me again and I'll try once more.",
                    "Lost my way. Ask me again.",
                    "Oh no, I got completely lost! Try asking me again."));
                RestoreWtsListing(offer);
                yield break;
            }

            // Sim arrived in our zone. Reset seekingPlayer BEFORE WalkToPlayerAndTrade
            // so that wasSeekingPlayer is captured as false, and the sim's AI
            // returns to normal after the trade.
            tracking.seekingPlayer = false;
            tracking.StayInZone = false;

            yield return WalkToPlayerAndTrade(arrivedSim, tracking, offer);
        }

        private static IEnumerator WalkToPlayerAndTrade(SimPlayer sim, SimPlayerTracking tracking, PendingCraftOffer offer)
        {
            NavMeshAgent nav = sim.GetComponent<NavMeshAgent>();
            Transform playerTransform = GameData.PlayerControl?.transform;

            if (nav == null || playerTransform == null)
            {
                RestoreWtsListing(offer);
                yield break;
            }

            // Temporarily tell the sim's AI "you have somewhere to be"
            bool wasSeekingPlayer = tracking.seekingPlayer;
            bool wasStayInZone = tracking.StayInZone;
            tracking.seekingPlayer = true;
            tracking.StayInZone = true;

            SkillsPlugin.Log.LogInfo($"[SimCraft] Walking {tracking.SimName} to player, dist={Vector3.Distance(sim.transform.position, playerTransform.position):F1}");

            // Walk to the player
            float timeout = Time.time + 30f;

            while (Time.time < timeout)
            {
                if (playerTransform == null || sim == null)
                {
                    tracking.seekingPlayer = wasSeekingPlayer;
                    tracking.StayInZone = wasStayInZone;
                    RestoreWtsListing(offer);
                    yield break;
                }

                float dist = Vector3.Distance(sim.transform.position, playerTransform.position);
                if (dist < 4f)
                    break;

                // Override nav every frame to fight the sim's AI
                try
                {
                    Vector3 target = GameData.GetSafeNavMeshPoint(playerTransform.position);
                    nav.SetDestination(target);
                    nav.isStopped = false;
                    nav.speed = Mathf.Max(nav.speed, 3.5f); // Ensure reasonable speed
                }
                catch { }

                yield return null;
            }

            if (sim == null || playerTransform == null)
            {
                tracking.seekingPlayer = wasSeekingPlayer;
                tracking.StayInZone = wasStayInZone;
                RestoreWtsListing(offer);
                yield break;
            }

            // Check distance — if too far, keep trying
            float finalDist = Vector3.Distance(sim.transform.position, playerTransform.position);
            if (finalDist > 8f)
            {
                // Second attempt
                QueueSimWhisper(tracking, GetPersonalityLine(tracking,
                    "Hold on, I'm coming! Almost there.",
                    "Stuck on something. Give me a sec.",
                    "Wait for me! I'm trying to get around this corner."));

                float timeout2 = Time.time + 30f;
                while (Time.time < timeout2)
                {
                    if (playerTransform == null || sim == null)
                    {
                        tracking.seekingPlayer = wasSeekingPlayer;
                        tracking.StayInZone = wasStayInZone;
                        RestoreWtsListing(offer);
                        yield break;
                    }
                    float d = Vector3.Distance(sim.transform.position, playerTransform.position);
                    if (d < 5f) break;
                    try
                    {
                        nav.SetDestination(GameData.GetSafeNavMeshPoint(playerTransform.position));
                        nav.isStopped = false;
                    }
                    catch { }
                    yield return null;
                }
            }

            // Restore AI state
            tracking.seekingPlayer = wasSeekingPlayer;
            tracking.StayInZone = wasStayInZone;

            SkillsPlugin.Log.LogInfo($"[SimCraft] {tracking.SimName} arrived, dist={Vector3.Distance(sim.transform.position, playerTransform.position):F1}");

            // Arrived — resolve item and open trade window
            Item craftedItem = Items.ItemFactory.ResolveItem(offer.Recipe.Name);
            if (craftedItem == null)
            {
                SkillsPlugin.Log.LogError($"[SimCraft] Could not resolve item '{offer.Recipe.Name}'");
                RestoreWtsListing(offer);
                QueueSimWhisper(tracking, "Something went wrong with the item. Whisper me to try again.");
                // AI already restored above
                yield break;
            }

            // Open the trade window
            OpenCraftTradeWindow(sim, tracking, offer, craftedItem);
        }

        /// <summary>Opens our custom trade UI and sets up the active trade session.</summary>
        private static void OpenCraftTradeWindow(SimPlayer sim, SimPlayerTracking tracking,
            PendingCraftOffer offer, Item craftedItem)
        {
            try
            {
                CurrentTrade = new ActiveTradeSession
                {
                    Sim = tracking,
                    ActiveSim = sim,
                    Offer = offer,
                    CraftedItem = craftedItem,
                    TradeWindowOpen = true,
                    TradeCompleted = false,
                    WaitingForRetryAnswer = false
                };

                // Open our custom trade UI
                UI.SimCraftTradeUI.Show(
                    tracking.SimName,
                    offer.Recipe.Name,
                    offer.Quantity,
                    offer.TotalGoldCost,
                    onAccept: () => OnPlayerAcceptedTrade(),
                    onDecline: () => OnPlayerDeclinedTrade());

                SkillsPlugin.Log.LogInfo($"[SimCraft] Opened trade UI: {offer.Quantity}x {offer.Recipe.Name} from {tracking.SimName}");
            }
            catch (Exception ex)
            {
                RestoreWtsListing(offer);
                SkillsPlugin.Log.LogError($"[SimCraft] OpenCraftTradeWindow error: {ex}");
                CurrentTrade = null;
            }
        }

        /// <summary>Player clicked Accept on our trade UI.</summary>
        private static void OnPlayerAcceptedTrade()
        {
            SkillsPlugin.Log.LogInfo($"[SimCraft] OnPlayerAcceptedTrade called, CurrentTrade={(CurrentTrade != null ? CurrentTrade.Sim.SimName : "NULL")}");
            if (CurrentTrade == null) return;
            ActiveTradeSession trade = null;
            try
            {
                trade = CurrentTrade;
                CurrentTrade = null;

                // Check gold at point of sale
                if (GameData.PlayerInv.Gold < trade.Offer.TotalGoldCost)
                {
                    RestoreWtsListing(trade.Offer);
                    QueueSimWhisper(trade.Sim, GetPersonalityLine(trade.Sim,
                        $"You don't have {trade.Offer.TotalGoldCost} gold? Come back when you can pay.",
                        $"You're broke! I need {trade.Offer.TotalGoldCost} gold. Get lost.",
                        $"Oh no, you need {trade.Offer.TotalGoldCost} gold but you don't have it!"));
                    return;
                }

                if (!Items.ItemFactory.CanAddItemToPlayer(
                    trade.CraftedItem, trade.Offer.Quantity))
                {
                    RestoreWtsListing(trade.Offer);
                    QueueSimWhisper(trade.Sim, GetPersonalityLine(trade.Sim,
                        "Your inventory is full. Make some room and ask me again.",
                        "No room, no deal. Clear your inventory first.",
                        "I can't hand these over until you make some inventory space!"));
                    return;
                }

                // Gold and all crafted items are one durable transaction. The
                // old loop charged first and ignored failed grants, leaving a
                // player short on both gold and items when nearly full.
                var mutation = Items.BagSystem.BeginExternalMutation();
                try
                {
                    GameData.PlayerInv.Gold -= trade.Offer.TotalGoldCost;
                    if (!Items.ItemFactory.GiveItemToPlayer(
                        trade.Offer.Recipe.Name, trade.Offer.Quantity))
                        throw new InvalidOperationException("The crafted items could not be added.");
                    if (!Items.BagSystem.CommitExternalMutation(
                        "sim-craft-purchase", mutation))
                        throw new InvalidOperationException("The trade could not be saved.");
                }
                catch (Exception transactionError)
                {
                    Items.BagSystem.RollbackExternalMutation(mutation);
                    RestoreWtsListing(trade.Offer);
                    SkillsPlugin.Log.LogError(
                        $"[SimCraft] purchase rolled back: {transactionError}");
                    QueueSimWhisper(trade.Sim,
                        "The trade did not complete. Your gold and inventory were restored.");
                    return;
                }

                ChatHelper.Send(
                    $"<color=#FF9800>[{trade.Offer.Tradeskill}]</color> " +
                    $"<color=#4CAF50>Received {trade.Offer.Quantity}x {trade.Offer.Recipe.Name} from {trade.Sim.SimName}.</color> " +
                    $"<color=#FFD700>(-{trade.Offer.TotalGoldCost}g)</color>");

                UpdateSocialLog.LogAdd(new ChatLogLine(
                    $"{trade.Sim.SimName} says: " + GetPersonalityLine(trade.Sim,
                        "Pleasure doing business with you!",
                        "Don't spend it all in one place.",
                        "Yay! Enjoy your new stuff!"),
                    ChatLogLine.LogType.Say, "#CCCCCC"));
            }
            catch (Exception ex)
            {
                RestoreWtsListing(trade?.Offer);
                SkillsPlugin.Log.LogError($"[SimCraft] OnPlayerAcceptedTrade error: {ex}");
            }
        }

        /// <summary>Player clicked Decline on our trade UI.</summary>
        private static void OnPlayerDeclinedTrade()
        {
            SkillsPlugin.Log.LogInfo($"[SimCraft] OnPlayerDeclinedTrade called, CurrentTrade={(CurrentTrade != null ? CurrentTrade.Sim.SimName : "NULL")}");
            if (CurrentTrade == null) return;
            try
            {
                var trade = CurrentTrade;
                trade.TradeWindowOpen = false;
                trade.DeclineCount++;

                // Give up after too many declines
                if (trade.DeclineCount >= ActiveTradeSession.MAX_DECLINES)
                {
                    CurrentTrade = null;
                    RestoreWtsListing(trade.Offer);
                    QueueSimWhisper(trade.Sim, GetPersonalityLine(trade.Sim,
                        "Alright, I've offered enough times. Whisper me again if you change your mind later.",
                        "I'm done chasing you around. You know where to find me.",
                        "Okay, I'll stop asking! Just whisper me if you ever want it."));
                    return;
                }

                trade.WaitingForRetryAnswer = true;

                _pendingOffers.Remove(trade.Sim.SimName);

                var retryOffer = new PendingCraftOffer(
                    trade.Sim.SimName, trade.Offer.Recipe, trade.Offer.Tradeskill,
                    trade.Offer.Quantity, trade.Offer.TotalGoldCost, true,
                    trade.Offer.SourceWts);
                retryOffer.ExpiresAt = Time.time + 60f;
                _pendingOffers[trade.Sim.SimName] = retryOffer;

                _retryTrades[trade.Sim.SimName] = trade;

                QueueSimWhisper(trade.Sim, GetPersonalityLine(trade.Sim,
                    "Changed your mind? I've still got it here. Whisper me yes if you want it, or no if you're done.",
                    "You closing on me? I still have the goods. Yes or no?",
                    "Oh! Did you not want it? Whisper yes if you still do, or no and I'll leave."));
            }
            catch (Exception ex)
            {
                RestoreWtsListing(CurrentTrade?.Offer);
                SkillsPlugin.Log.LogError($"[SimCraft] OnPlayerDeclinedTrade error: {ex}");
                CurrentTrade = null;
            }
        }

        // Tracks active retry trade sessions (sim is waiting nearby after cancel)
        private static readonly Dictionary<string, ActiveTradeSession> _retryTrades
            = new Dictionary<string, ActiveTradeSession>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Check if a yes/no response is for a trade retry.</summary>
        private static bool TryHandleRetryResponse(SimPlayerTracking whichSim, string msg)
        {
            if (!_retryTrades.ContainsKey(whichSim.SimName)) return false;

            var trade = _retryTrades[whichSim.SimName];
            _retryTrades.Remove(whichSim.SimName);
            _pendingOffers.Remove(whichSim.SimName);

            if (msg == "yes" || msg == "y" || msg == "yeah" || msg == "sure" ||
                msg == "ok" || msg == "deal" || msg == "accept")
            {
                // Re-find the active sim in case they moved/despawned
                SimPlayer freshSim = FindActiveSimByName(whichSim.SimName);
                if (freshSim != null)
                {
                    trade.ActiveSim = freshSim;

                    // Check distance — if close, reopen immediately; if far, walk over again
                    Transform playerTransform = GameData.PlayerControl?.transform;
                    float dist = (playerTransform != null)
                        ? Vector3.Distance(freshSim.transform.position, playerTransform.position)
                        : 999f;

                    if (dist < 5f)
                    {
                        QueueSimWhisper(whichSim, GetPersonalityLine(whichSim,
                            "Alright, here it is again.",
                            "Make up your mind. Here.",
                            "Okay okay, opening trade again!"));
                        SkillsPlugin.Instance.StartCoroutine(DelayedReopenTrade(trade));
                    }
                    else
                    {
                        QueueSimWhisper(whichSim, GetPersonalityLine(whichSim,
                            "Okay, coming back to you. Hold still this time!",
                            "Fine, I'll walk over again. Stay put.",
                            "On my way back! Don't run off!"));
                        // Walk to player again then open trade
                        SkillsPlugin.Instance.StartCoroutine(
                            WalkToPlayerAndTrade(freshSim, whichSim, trade.Offer));
                    }
                }
                else
                {
                    CurrentTrade = null;
                    RestoreWtsListing(trade.Offer);
                    QueueSimWhisper(whichSim, GetPersonalityLine(whichSim,
                        "I seem to have wandered off... whisper me to craft again.",
                        "I left. Ask me again later.",
                        "Oh no, I'm gone! Whisper me again and I'll come back."));
                }
                return true;
            }
            else if (msg == "no" || msg == "n" || msg == "nah" || msg == "pass" ||
                     msg == "nope" || msg == "decline" || msg == "nevermind")
            {
                CurrentTrade = null;
                RestoreWtsListing(trade.Offer);
                QueueSimWhisper(whichSim, GetPersonalityLine(whichSim,
                    "No problem! Catch you later.",
                    "Fine. Wasted my time. Whatever.",
                    "Okay! Maybe next time. Take care!"));
                return true;
            }

            RestoreWtsListing(trade.Offer);
            return false;
        }

        /// <summary>Short delay before opening trade for nearby sims, so the whisper displays first.</summary>
        private static IEnumerator DelayedOpenTrade(SimPlayer sim, SimPlayerTracking tracking, PendingCraftOffer offer)
        {
            yield return new WaitForSeconds(2f);

            Item craftedItem = Items.ItemFactory.ResolveItem(offer.Recipe.Name);
            if (craftedItem == null)
            {
                RestoreWtsListing(offer);
                QueueSimWhisper(tracking, "Something went wrong with the item. Whisper me to try again.");
                yield break;
            }

            OpenCraftTradeWindow(sim, tracking, offer, craftedItem);
        }

        private static IEnumerator DelayedReopenTrade(ActiveTradeSession trade)
        {
            // Wait a moment for the whisper to display
            yield return new WaitForSeconds(2f);

            if (trade.ActiveSim != null && trade.CraftedItem != null)
            {
                OpenCraftTradeWindow(trade.ActiveSim, trade.Sim, trade.Offer, trade.CraftedItem);
            }
            else
            {
                RestoreWtsListing(trade.Offer);
                CurrentTrade = null;
            }
        }



        // ═══════════════════════════════════════════════════════════════
        // HELPERS
        // ═══════════════════════════════════════════════════════════════

        // ═══════════════════════════════════════════════════════════════
        // SIM RECIPE PERSISTENCE
        // ═══════════════════════════════════════════════════════════════

        internal static string GetSimRecipesPath()
        {
            return System.IO.Path.Combine(RuntimePaths.DataDirectory, "sim_recipes.json");
        }

        internal static string SerializeSimRecipes()
        {
            var simNames = new List<string>(_simLearnedRecipes.Keys);
            simNames.Sort(StringComparer.OrdinalIgnoreCase);
            var json = new System.Text.StringBuilder(128 + simNames.Count * 96);
            json.Append("{\n  \"Version\": ").Append(SimRecipeSaveVersion)
                .Append(",\n  \"Sims\": [");
            bool wroteRecord = false;
            foreach (string simName in simNames)
            {
                var recipes = _simLearnedRecipes[simName];
                if (recipes == null || recipes.Count == 0) continue;
                if (wroteRecord) json.Append(',');
                json.Append("\n    {\n      \"Name\": \"")
                    .Append(SafeFileIO.EscapeJson(simName))
                    .Append("\",\n      \"Recipes\": [");
                var sortedRecipes = new List<string>(recipes);
                sortedRecipes.Sort(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < sortedRecipes.Count; i++)
                {
                    if (i > 0) json.Append(", ");
                    json.Append('"')
                        .Append(SafeFileIO.EscapeJson(sortedRecipes[i]))
                        .Append('"');
                }
                json.Append("]\n    }");
                wroteRecord = true;
            }
            if (wroteRecord) json.Append('\n').Append("  ");
            json.Append("]\n}");
            return json.ToString();
        }

        private static bool SaveSimRecipes()
        {
            try
            {
                string json = SerializeSimRecipes();
                if (!IsSimRecipesJsonValid(json))
                    throw new System.IO.InvalidDataException(
                        "Generated sim recipe data failed validation.");
                if (!SafeFileIO.TryWriteAllTextAtomic(
                    GetSimRecipesPath(), json, out string error))
                    throw new System.IO.IOException(error);
                return true;
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogError($"SaveSimRecipes error: {ex.Message}");
                return false;
            }
        }

        internal static bool IsSimRecipesJsonValid(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return false;
            string value = json.Trim();
            if (!value.StartsWith("{") || !value.EndsWith("}")) return false;
            if (!value.Contains("\"Version\""))
                return TryParseLegacySimRecipes(value, out _);
            return TryParseV2SimRecipes(value, out _);
        }

        /// <summary>Load sim-learned recipes from disk. Call once at startup.</summary>
        public static bool LoadSimRecipes()
        {
            _simLearnedRecipes.Clear();
            _simRecipePersistenceHealthy = true;
            try
            {
                string path = GetSimRecipesPath();
                if (!System.IO.File.Exists(path) && !System.IO.File.Exists(path + ".bak"))
                    return true;
                if (!SafeFileIO.TryReadAllTextWithBackup(path, IsSimRecipesJsonValid,
                    out string loaded, out bool recovered, out string readError))
                    throw new System.IO.InvalidDataException(readError);

                string json = loaded.Trim();
                bool legacy = !json.Contains("\"Version\"");
                if (legacy)
                {
                    if (!TryParseLegacySimRecipes(json, out var parsedLegacy))
                        throw new System.IO.InvalidDataException(
                            "Legacy sim recipe data failed validation.");
                    _simLearnedRecipes = parsedLegacy;
                }
                else
                {
                    if (!TryParseV2SimRecipes(json, out var parsedV2))
                        throw new System.IO.InvalidDataException(
                            "Sim recipe v2 data failed validation.");
                    _simLearnedRecipes = parsedV2;
                }

                int totalRecipes = 0;
                foreach (var kvp in _simLearnedRecipes) totalRecipes += kvp.Value.Count;
                SkillsPlugin.Log.LogInfo($"Loaded {totalRecipes} sim-learned recipes for {_simLearnedRecipes.Count} sims.");
                if (recovered)
                    SkillsPlugin.Log.LogWarning("Recovered sim-learned recipes from backup.");
                if (legacy)
                {
                    if (!SaveSimRecipes())
                        throw new System.IO.IOException(
                            "Loaded legacy sim recipes but could not migrate them safely.");
                    SkillsPlugin.Log.LogInfo("Migrated sim recipe persistence to format v2.");
                }
                return true;
            }
            catch (Exception ex)
            {
                _simLearnedRecipes.Clear();
                _simRecipePersistenceHealthy = false;
                SkillsPlugin.Log.LogError($"LoadSimRecipes error: {ex.Message}");
                return false;
            }
        }

        private static bool TryParseV2SimRecipes(string json,
            out Dictionary<string, HashSet<string>> parsed)
        {
            parsed = new Dictionary<string, HashSet<string>>(
                StringComparer.OrdinalIgnoreCase);
            try
            {
                const string jsonString = "\\\"(?:\\\\.|[^\\\"\\\\])*\\\"";
                string list = "(?:" + jsonString + "(?:\\s*,\\s*" +
                    jsonString + ")*)?";
                string record = "\\{\\s*\\\"Name\\\"\\s*:\\s*(?<sim>" +
                    jsonString + ")\\s*,\\s*\\\"Recipes\\\"\\s*:\\s*\\[\\s*" +
                    "(?<recipes>" + list + ")\\s*\\]\\s*\\}";
                string document = "^\\{\\s*\\\"Version\\\"\\s*:\\s*" +
                    "(?<version>-?[0-9]+)\\s*,\\s*\\\"Sims\\\"\\s*:\\s*\\[\\s*" +
                    "(?:" + record + "(?:\\s*,\\s*" + record + ")*)?" +
                    "\\s*\\]\\s*\\}$";
                Match root = Regex.Match(json, document, RegexOptions.Singleline);
                if (!root.Success || !int.TryParse(root.Groups["version"].Value,
                        out int version) || version != SimRecipeSaveVersion)
                    return false;

                int simCount = 0;
                int totalRecipes = 0;
                foreach (Match match in Regex.Matches(json, record,
                    RegexOptions.Singleline))
                {
                    if (++simCount > 10000) return false;
                    string simName = DecodeJsonString(match.Groups["sim"].Value);
                    if (string.IsNullOrWhiteSpace(simName) || simName.Length > 256 ||
                        parsed.ContainsKey(simName)) return false;
                    var recipes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (Match recipeMatch in Regex.Matches(
                        match.Groups["recipes"].Value, jsonString))
                    {
                        string recipe = DecodeJsonString(recipeMatch.Value);
                        if (string.IsNullOrWhiteSpace(recipe) || recipe.Length > 512 ||
                            !recipes.Add(recipe) || ++totalRecipes > 100000)
                            return false;
                    }
                    if (recipes.Count == 0 || recipes.Count > 10000) return false;
                    parsed.Add(simName, recipes);
                }
                return true;
            }
            catch
            {
                parsed.Clear();
                return false;
            }
        }

        /// <summary>
        /// Exercises non-empty sim recipe persistence entirely in memory. This
        /// guards against Unity silently dropping nested lists again.
        /// </summary>
        internal static bool ProbeSimRecipeRoundTrip(out string error)
        {
            error = null;
            var before = CloneSimRecipes(_simLearnedRecipes);
            bool healthyBefore = _simRecipePersistenceHealthy;
            try
            {
                var expected = CloneSimRecipes(before);
                const string sentinel = "Classic Skills QA Sim";
                expected[sentinel] = new HashSet<string>(
                    new[] { "Pan-Seared Fish", "QA \\\"Escaped\\\" Recipe" },
                    StringComparer.OrdinalIgnoreCase);
                _simLearnedRecipes = CloneSimRecipes(expected);
                string serialized = SerializeSimRecipes();
                if (!TryParseV2SimRecipes(serialized, out var loaded))
                    throw new System.IO.InvalidDataException(
                        "non-empty sim recipe data did not parse");
                if (!SimRecipeMapsEqual(expected, loaded))
                    throw new System.IO.InvalidDataException(
                        "sim recipe data changed during round-trip");
                if (IsSimRecipesJsonValid(serialized.Substring(
                        0, serialized.Length - 1)))
                    throw new System.IO.InvalidDataException(
                        "truncated sim recipe data was accepted");
                string wrongVersion = serialized.Replace(
                    "\"Version\": 2", "\"Version\": 3");
                if (IsSimRecipesJsonValid(wrongVersion))
                    throw new System.IO.InvalidDataException(
                        "unsupported sim recipe version was accepted");
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            finally
            {
                _simLearnedRecipes = before;
                _simRecipePersistenceHealthy = healthyBefore;
            }
        }

        private static Dictionary<string, HashSet<string>> CloneSimRecipes(
            Dictionary<string, HashSet<string>> source)
        {
            var clone = new Dictionary<string, HashSet<string>>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var entry in source)
                clone[entry.Key] = entry.Value == null
                    ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    : new HashSet<string>(entry.Value,
                        StringComparer.OrdinalIgnoreCase);
            return clone;
        }

        private static bool SimRecipeMapsEqual(
            Dictionary<string, HashSet<string>> left,
            Dictionary<string, HashSet<string>> right)
        {
            if (left.Count != right.Count) return false;
            foreach (var entry in left)
            {
                if (!right.TryGetValue(entry.Key, out var recipes) ||
                    entry.Value == null || recipes == null ||
                    !entry.Value.SetEquals(recipes)) return false;
            }
            return true;
        }

        private static bool TryParseLegacySimRecipes(string json,
            out Dictionary<string, HashSet<string>> parsed)
        {
            parsed = new Dictionary<string, HashSet<string>>(
                StringComparer.OrdinalIgnoreCase);
            try
            {
                const string jsonString = "\\\"(?:\\\\.|[^\\\"\\\\])*\\\"";
                string list = "(?:" + jsonString + "(?:\\s*,\\s*" +
                    jsonString + ")*)?";
                string entry = "(?<sim>" + jsonString + ")\\s*:\\s*\\[\\s*(?<recipes>" +
                    list + ")\\s*\\]";
                string document = "^\\{\\s*(?:" + entry +
                    "(?:\\s*,\\s*" + entry + ")*)?\\s*\\}$";
                if (!Regex.IsMatch(json, document, RegexOptions.Singleline)) return false;

                int totalRecipes = 0;
                foreach (Match match in Regex.Matches(json, entry,
                    RegexOptions.Singleline))
                {
                    string simName = DecodeJsonString(match.Groups["sim"].Value);
                    if (string.IsNullOrWhiteSpace(simName) || simName.Length > 256 ||
                        parsed.ContainsKey(simName)) return false;
                    var recipes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (Match recipeMatch in Regex.Matches(
                        match.Groups["recipes"].Value, jsonString))
                    {
                        string recipe = DecodeJsonString(recipeMatch.Value);
                        if (string.IsNullOrWhiteSpace(recipe) || recipe.Length > 512 ||
                            !recipes.Add(recipe) || ++totalRecipes > 100000) return false;
                    }
                    if (recipes.Count > 10000) return false;
                    if (recipes.Count > 0) parsed.Add(simName, recipes);
                }
                return true;
            }
            catch
            {
                parsed.Clear();
                return false;
            }
        }

        private static string DecodeJsonString(string encoded)
        {
            if (string.IsNullOrEmpty(encoded) || encoded.Length < 2) return "";
            var result = new System.Text.StringBuilder(encoded.Length - 2);
            for (int i = 1; i < encoded.Length - 1; i++)
            {
                char c = encoded[i];
                if (c != '\\') { result.Append(c); continue; }
                if (++i >= encoded.Length - 1)
                    throw new FormatException("Incomplete JSON escape.");
                switch (encoded[i])
                {
                    case '"': result.Append('"'); break;
                    case '\\': result.Append('\\'); break;
                    case '/': result.Append('/'); break;
                    case 'b': result.Append('\b'); break;
                    case 'f': result.Append('\f'); break;
                    case 'n': result.Append('\n'); break;
                    case 'r': result.Append('\r'); break;
                    case 't': result.Append('\t'); break;
                    case 'u':
                        if (i + 4 >= encoded.Length)
                            throw new FormatException("Incomplete Unicode escape.");
                        result.Append((char)Convert.ToInt32(
                            encoded.Substring(i + 1, 4), 16));
                        i += 4;
                        break;
                    default: throw new FormatException("Invalid JSON escape.");
                }
            }
            return result.ToString();
        }

        /// <summary>
        /// Determines if a sim "knows" a recipe. Uses a deterministic hash so
        /// the same sim always knows the same recipes across sessions.
        ///
        /// Knowledge chance by recipe trivial level:
        ///   Trivial 1-40:   100% (basic recipes, everyone knows them)
        ///   Trivial 41-80:  90%
        ///   Trivial 81-120: 70%
        ///   Trivial 121-150: 40%
        ///   Trivial 151-165: 20%  (endgame, rare knowledge)
        ///   Trivial 166-180: 10%  (top-tier, very rare)
        /// </summary>
        private static bool SimKnowsRecipe(SimPlayerTracking sim, Tradeskills.Recipe recipe)
        {
            // Check if sim was explicitly taught this recipe
            if (_simLearnedRecipes.ContainsKey(sim.SimName) &&
                _simLearnedRecipes[sim.SimName].Contains(recipe.Name))
                return true;

            int trivial = recipe.TrivialLevel;

            // Basic recipes: everyone knows them
            if (trivial <= 40) return true;

            // Determine knowledge chance based on tier
            float knowChance;
            if (trivial <= 80)       knowChance = 0.90f;
            else if (trivial <= 120) knowChance = 0.70f;
            else if (trivial <= 150) knowChance = 0.40f;
            else if (trivial <= 165) knowChance = 0.20f;
            else                     knowChance = 0.10f;

            // Deterministic "roll" based on sim name + recipe name
            // This ensures the same sim always knows (or doesn't know)
            // the same recipes, so players can learn which sims know what
            uint hash = 2166136261u;
            string key = sim.SimName + "|" + recipe.Name;
            foreach (char c in key)
            {
                hash ^= c;
                hash *= 16777619u;
            }
            float roll = (hash % 10000u) / 10000f;

            return roll < knowChance;
        }

        /// <summary>Formats a scene name for display (adds spaces before capitals).
        /// "StowawayIsle" → "Stowaway Isle", "Azure" → "Azure"</summary>
        private static string FormatZoneName(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName)) return "an unknown zone";
            // Insert space before uppercase letters that follow lowercase
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < sceneName.Length; i++)
            {
                if (i > 0 && char.IsUpper(sceneName[i]) && char.IsLower(sceneName[i - 1]))
                    sb.Append(' ');
                sb.Append(sceneName[i]);
            }
            // Replace underscores with spaces
            return sb.ToString().Replace('_', ' ');
        }

        /// <summary>Find an active (in-scene) SimPlayer by name.</summary>
        private static SimPlayer FindActiveSimByName(string simName)
        {
            try
            {
                var sims = UnityEngine.Object.FindObjectsOfType<SimPlayer>();
                if (sims == null) return null;
                foreach (var s in sims)
                {
                    if (s?.MyStats?.MyName == simName ||
                        s?.transform.name == simName)
                        return s;
                }
            }
            catch { }
            return null;
        }

        /// <summary>Queue a whisper from a sim to the player using the game's
        /// native whisper response system (with delay + sound).</summary>
        private static void QueueSimWhisper(SimPlayerTracking sim, string message)
        {
            try
            {
                if (GameData.SimMngr != null)
                {
                    string fullMsg = "[WHISPER FROM] " + sim.SimName + ": " + message;
                    GameData.SimMngr.Responses.Add(new WhisperData(fullMsg, sim.SimName));
                }
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogError($"QueueSimWhisper error: {ex.Message}");
            }
        }

        /// <summary>Pick a personality-flavored line based on the sim's personality.
        /// 1=Nice, 2=Tryhard, 3=Mean (from SimPlayerTracking.Personality).</summary>
        private static string GetPersonalityLine(SimPlayerTracking sim,
            string niceLine, string meanLine, string defaultLine)
        {
            int personality = sim.Personality;
            if (personality <= 1) return niceLine;       // Nice
            if (personality >= 3) return meanLine;       // Mean/Tryhard
            return defaultLine;                           // Default/middle
        }

        /// <summary>
        /// Called on scene change to clean up all active trade state.
        /// Prevents stale coroutines, UI, and sim references from persisting.
        /// </summary>
        public static void OnSceneChange()
        {
            try
            {
                CurrentTrade = null;
                _pendingOffers.Clear();
                _retryTrades.Clear();
                _activeWTBs.Clear();
                _activeWTSs.Clear();
                _wtbPendingSale.Clear();
                SkillsPlugin.Log.LogInfo("[SimCraft] Scene change — cleared all active trade state.");
            }
            catch { }
        }

        /// <summary>Clean up expired offers and cooldowns. Called periodically from Update.</summary>
        public static void CleanupExpiredOffers()
        {
            try
            {
                var expired = new List<string>();
                foreach (var kvp in _pendingOffers)
                {
                    if (kvp.Value.IsExpired)
                        expired.Add(kvp.Key);
                }
                foreach (var key in expired)
                {
                    if (_pendingOffers.TryGetValue(key, out var expiredOffer))
                        RestoreWtsListing(expiredOffer);
                    _pendingOffers.Remove(key);
                }

                // Clean expired cooldowns
                expired.Clear();
                foreach (var kvp in _failCooldowns)
                {
                    if (Time.time >= kvp.Value)
                        expired.Add(kvp.Key);
                }
                foreach (var key in expired)
                    _failCooldowns.Remove(key);

                // Clean expired WTBs
                expired.Clear();
                foreach (var kvp in _activeWTBs)
                {
                    if (kvp.Value.IsExpired || kvp.Value.Fulfilled)
                        expired.Add(kvp.Key);
                }
                foreach (var key in expired)
                    _activeWTBs.Remove(key);

                // Clean expired WTSs
                expired.Clear();
                foreach (var kvp in _activeWTSs)
                {
                    if (kvp.Value.IsExpired)
                        expired.Add(kvp.Key);
                }
                foreach (var key in expired)
                    _activeWTSs.Remove(key);

                // Clean stale retry trades (if pending offer expired, retry is dead too)
                expired.Clear();
                foreach (var kvp in _retryTrades)
                {
                    if (!_pendingOffers.ContainsKey(kvp.Key))
                        expired.Add(kvp.Key);
                }
                foreach (var key in expired)
                {
                    if (_retryTrades.TryGetValue(key, out var staleTrade))
                        RestoreWtsListing(staleTrade.Offer);
                    _retryTrades.Remove(key);
                    if (CurrentTrade != null && CurrentTrade.Sim != null &&
                        CurrentTrade.Sim.SimName.Equals(key, StringComparison.OrdinalIgnoreCase))
                        CurrentTrade = null;
                }
            }
            catch { }
        }

        // ═══════════════════════════════════════════════════════════════
        // AMBIENT TRADESKILL CHAT
        // ═══════════════════════════════════════════════════════════════

        public static void TryAmbientChat()
        {
            if (!SkillsPlugin.CfgEnableSimPlayerSkills.Value) return;
            if (Time.time < _nextChatTime) return;

            float minInterval = Mathf.Max(60f, SkillsPlugin.CfgSimChatIntervalMin.Value);
            float maxInterval = Mathf.Max(minInterval + 30f, SkillsPlugin.CfgSimChatIntervalMax.Value);
            _nextChatTime = Time.time + UnityEngine.Random.Range(minInterval, maxInterval);

            try
            {
                var sims = UnityEngine.Object.FindObjectsOfType<SimPlayer>();
                if (sims == null || sims.Length == 0) return;

                var sim = sims[UnityEngine.Random.Range(0, sims.Length)];
                if (sim == null || sim.MyStats == null) return;

                string simName = sim.MyStats.MyName ?? sim.transform.name;
                string className = sim.MyStats.CharacterClass?.ClassName ?? "";
                string specialty = GetClassTradeskill(className);
                int simLevel = sim.MyStats.Level;
                int simIndex = sim.myIndex;

                // Pick a random channel: say (60%), shout (25%), guild (15%)
                float roll = UnityEngine.Random.value;

                if (roll < 0.15f)
                {
                    // Guild chat — only if sim is in a guild
                    string guildId = (simIndex >= 0 && simIndex < GameData.SimMngr.Sims.Count)
                        ? GameData.SimMngr.Sims[simIndex].GuildID : null;
                    if (!string.IsNullOrEmpty(guildId))
                    {
                        string msg = GetGuildChatLine(specialty, simName, simLevel);
                        if (!string.IsNullOrEmpty(msg))
                        {
                            UpdateSocialLog.LogAdd(new ChatLogLine(
                                $"{simName} tells the guild: {msg}",
                                ChatLogLine.LogType.Guild, "#40E0D0"));
                            return;
                        }
                    }
                    // Fall through to say if no guild
                }
                else if (roll < 0.40f)
                {
                    // Shout — try WTS with real items first, then fall back to generic
                    if (SkillsPlugin.CfgEnableSimWTS.Value && UnityEngine.Random.value < 0.3f)
                    {
                        string wtsMsg = TryGenerateWTSShout(simName, specialty, simLevel, simIndex);
                        if (!string.IsNullOrEmpty(wtsMsg))
                        {
                            UpdateSocialLog.LogAdd(new ChatLogLine(
                                $"{simName} shouts: {wtsMsg}",
                                ChatLogLine.LogType.Shout, "#FF9000"));
                            return;
                        }
                    }

                    string msg = GetShoutChatLine(specialty, simName, simLevel);
                    if (!string.IsNullOrEmpty(msg))
                    {
                        UpdateSocialLog.LogAdd(new ChatLogLine(
                            $"{simName} shouts: {msg}",
                            ChatLogLine.LogType.Shout, "#FF9000"));
                        return;
                    }
                }

                // Default: /say
                string sayMsg = GetAmbientChatLine(specialty, simName, simLevel, sim.NearForge);
                if (string.IsNullOrEmpty(sayMsg)) return;

                UpdateSocialLog.LogAdd(new ChatLogLine(
                    $"{simName} says: {sayMsg}",
                    ChatLogLine.LogType.Say, "#CCCCCC"));
            }
            catch { }
        }

        /// <summary>
        /// Picks a random recipe this sim can craft and generates a real WTS shout.
        /// Registers the item as an active WTS listing backed by a real trade.
        /// </summary>
        private static string TryGenerateWTSShout(string simName, string specialty, int level, int simIndex)
        {
            try
            {
                // Get the sim's tracking data for recipe knowledge check
                SimPlayerTracking tracking = (simIndex >= 0 && simIndex < GameData.SimMngr.Sims.Count)
                    ? GameData.SimMngr.Sims[simIndex] : null;
                if (tracking == null) return null;

                int simSkill = GetSimTradeskillLevelFromTracking(tracking, specialty);
                if (simSkill <= 0) return null;

                // Get all recipes for this sim's specialty they can make and know
                var candidates = new List<Tradeskills.Recipe>();
                var allRecipes = Tradeskills.GetRecipesForSkillPublic(specialty);
                if (allRecipes == null) return null;

                foreach (var r in allRecipes)
                {
                    if (simSkill >= r.MinSkillLevel && SimKnowsRecipe(tracking, r))
                        candidates.Add(r);
                }

                if (candidates.Count == 0) return null;

                // Pick a random recipe
                var recipe = candidates[UnityEngine.Random.Range(0, candidates.Count)];

                // Price: item value * 1.5 (same as craft markup)
                int price = Mathf.Max(Mathf.RoundToInt(recipe.GoldValue * 1.5f), 1);

                // Register the WTS listing
                RegisterWTSListing(simName, recipe, specialty, price);

                return $"WTS {recipe.Name}, {price} gold. Whisper me.";
            }
            catch { return null; }
        }

        /// <summary>Shout-channel lines — WTS/WTB/trade/economy oriented.</summary>
        private static string GetShoutChatLine(string specialty, string name, int level)
        {
            var lines = new List<string>();

            // WTS lines referencing actual crafted items
            switch (specialty)
            {
                case "Smithing":
                    lines.Add("WTS Copper Rivets, Steel Chain Links, Reinforced Buckles. PST.");
                    lines.Add("WTS Tempered Blade Blanks and Mithril Wire. Crafters whisper me.");
                    if (level >= 20) lines.Add("WTS Moonforged Hatchet! +8 Strength. Taking offers.");
                    if (level >= 30) lines.Add("WTS Sunforged Greatsword. +10 Str +5 Fire Resist. Not cheap.");
                    lines.Add("WTB Chunk of Adamantite Ore. Paying top gold.");
                    lines.Add("WTB Malaroth Scale Fragments. Need 4 for a project.");
                    break;
                case "Baking":
                    lines.Add("WTS food buffs! Firebloom Pepper +5 Str, Chargrilled Wyrm Egg +6 Str.");
                    lines.Add("WTS Plains Wheat Cake and Revival Berry Mash. Cheap food buffs.");
                    lines.Add("WTB Wild Berries and Firebloom Peppers. PST.");
                    break;
                case "Brewing":
                    lines.Add("WTS Dawn Nectar +6 Int, Silkengrass Tea +4 Int. Casters PST.");
                    lines.Add("WTS potions and brews. All level ranges. Whisper me.");
                    lines.Add("WTB Ashbloom Herbs and Duskbloom Petals.");
                    break;
                case "Fletching":
                    lines.Add("WTS bows and arrows. All skill levels. PST.");
                    lines.Add("WTB Nesting Feathers and Dark Bark Strips.");
                    lines.Add("WTB Starmetal Blade Blanks from a smith. Will pay well.");
                    break;
                case "Jewelcraft":
                    lines.Add("WTS rings and amulets. Custom orders welcome. PST.");
                    lines.Add("WTB Windwashed Crystals, Corruption Crystals, Moonstone Pebbles.");
                    lines.Add("WTB Mithril Wire or Silver Wire from a smith.");
                    if (level >= 25) lines.Add("WTS Platinum jewelry. Finest craftsmanship in Erenshor.");
                    break;
                case "Tailoring":
                    lines.Add("WTS cloth and leather armor. All levels. PST.");
                    lines.Add("WTB Spider Silk Strands and Silken Grass Blades.");
                    lines.Add("WTB Steel Chain Links or Soulsteel Chain Links from a smith.");
                    break;
            }

            // General economy shouts
            lines.Add("LF crafter to make me some gear. Will provide mats + tip.");
            lines.Add("Anyone selling Enchanted Roots? Need a stack.");
            lines.Add("WTB rare crafting materials. Paying above vendor price.");
            lines.Add("LF group for farming crafting materials. All welcome.");
            lines.Add("Foraged a Faerie Apple today. Those things are worth a fortune.");

            if (lines.Count == 0) return null;
            return lines[UnityEngine.Random.Range(0, lines.Count)];
        }

        /// <summary>Guild chat lines — more personal, sharing tips with guildmates.</summary>
        private static string GetGuildChatLine(string specialty, string name, int level)
        {
            var lines = new List<string>();

            // Tradeskill tips for guildies
            lines.Add($"My {specialty} just dinged! Making progress.");
            lines.Add("Anyone in guild need anything crafted? Guildies get a discount.");
            lines.Add("Don't forget to level your weapon skills. The damage bonus adds up.");
            lines.Add("Reminder: Foraging gives different items depending on the zone.");
            lines.Add("Pro tip: Bind Wound between fights saves a ton of downtime.");
            lines.Add("Meditation is worth leveling even for melee. Trust me.");
            lines.Add("Has anyone tried the endgame crafting recipes? The materials are insane.");
            lines.Add("Guild crafting night? I can smith if someone else can tailor.");
            lines.Add($"I can craft most {specialty} recipes now. Just whisper me.");
            lines.Add("Swimming skill actually matters in the lake zones. Don't neglect it.");
            lines.Add("Safe Fall saved me from that cliff in the mountains. Level it up.");
            lines.Add("Sense Heading is subtle but useful. You always know which way you're facing.");
            lines.Add("Fishing is relaxing but the skill-ups are painfully slow.");
            lines.Add("Food Tolerance is one of those skills you don't notice until you need it.");
            lines.Add("Cross-crafting tip: Smiths make Mithril Wire that jewelers need.");
            lines.Add("Cross-crafting tip: Smiths make Steel Chain Links that tailors need.");
            lines.Add("Cross-crafting tip: Smiths make Starmetal Blade Blanks for fletchers.");
            lines.Add("Begging actually works on some NPCs. The tips aren't bad either.");

            if (level >= 20)
            {
                lines.Add($"High-level {specialty} recipes are incredible. Worth the grind.");
                lines.Add("Endgame crafting materials drop from the toughest mobs. Bring friends.");
            }

            if (lines.Count == 0) return null;
            return lines[UnityEngine.Random.Range(0, lines.Count)];
        }

        private static string GetAmbientChatLine(string specialty, string name, int level, bool nearForge)
        {
            var lines = new List<string>();

            // ── Near-forge context lines ──
            if (nearForge)
            {
                lines.Add($"Anyone need anything crafted? My {specialty} is getting pretty good. PST.");
                lines.Add($"Just finished a combine. {specialty} skillups are slow at this level...");
                lines.Add($"Working on {specialty}, almost to the next tier.");
                lines.Add($"Ugh, failed combine. Lost my materials too.");
                lines.Add("The forge is hot today. Perfect for working metal.");
                lines.Add("I could craft all day. There's something relaxing about it.");
            }

            // ── Tradeskill-specific lines (reference actual items) ──
            switch (specialty)
            {
                case "Smithing":
                    lines.Add("I need more ore. Anyone know a good mining spot?");
                    lines.Add("Does anyone have Copper Rivets? I'm all out.");
                    lines.Add("WTS crafted weapons and armor, whisper me.");
                    lines.Add("Just smithed a new blade. Not bad for a day's work.");
                    lines.Add("Finally learned how to make Adamantite Rivets. Game changer.");
                    lines.Add("Starmetal Blade Blanks are no joke. Took me forever to get the recipe.");
                    lines.Add("Anyone have Malaroth Scale Fragments? Need them for Hardened Alloy.");
                    lines.Add("I've been making Steel Chain Links all day for a tailor friend.");
                    if (level >= 20) lines.Add("Moonforged Hatchet is my best piece yet. The metal just glows.");
                    if (level >= 30) lines.Add("Working on a Sunforged Greatsword. Two Sunforged Ingots... not cheap.");
                    break;
                case "Baking":
                    lines.Add("I just baked some trail rations. Long adventures need good food.");
                    lines.Add("Nothing beats a fresh batch of herb bread after a dungeon crawl.");
                    lines.Add("WTS baked goods, whisper me if you need food buffs.");
                    lines.Add("Firebloom Peppers make the best food buffs. +5 Strength!");
                    lines.Add("Anyone have Wild Berries? I need them for Revival Berry Mash.");
                    lines.Add("Plains Wheat Cake is easy to bake and gives decent Strength.");
                    lines.Add("Chargrilled Wyrm Egg is the ultimate food. If you can get the eggs...");
                    lines.Add("Garden Ambrosia uses Azynthi Petals. Good luck farming those.");
                    break;
                case "Brewing":
                    lines.Add("Brewed some ale last night. Want a taste?");
                    lines.Add("Anyone tried Ashbloom Sake? Best drink in Erenshor.");
                    lines.Add("WTS potions and brews, whisper me.");
                    lines.Add("Silkengrass Tea gives +4 Intelligence. Great for casters.");
                    lines.Add("Dawn Nectar is liquid gold. Literally. +6 Int for 10 minutes.");
                    lines.Add("Cactus Water Pouch is perfect for the desert zones.");
                    lines.Add("Essence of Eternity... if I could just get the ingredients.");
                    lines.Add("Duskbloom Petals make an excellent base for potions.");
                    break;
                case "Fletching":
                    lines.Add("Running low on arrows again. Time to fletch some more.");
                    lines.Add("My arrows are getting sharper. Fletching practice pays off.");
                    lines.Add("WTS bows and arrows, whisper me for prices.");
                    lines.Add("Nesting Feathers make the best arrow fletchings.");
                    lines.Add("Bramble Thorns are perfect for arrowheads at low levels.");
                    lines.Add("Dark Bark Strips make surprisingly good bow limbs.");
                    lines.Add("Starmetal Blade Blanks from a smith make incredible arrowheads.");
                    break;
                case "Jewelcraft":
                    lines.Add("Found a rough ruby yesterday. Can't wait to cut it.");
                    lines.Add("Jewelcraft takes patience, but the results are worth it.");
                    lines.Add("WTS rings and amulets, whisper me.");
                    lines.Add("Windwashed Crystals are beautiful but hard to find.");
                    lines.Add("Moonstone Pebbles are perfect for beginner jewelry.");
                    lines.Add("I need Mithril Wire from a smith for my next piece.");
                    lines.Add("Corruption Crystals make dark but powerful amulets.");
                    lines.Add("Brine-Crusted Pearls clean up beautifully. Great for rings.");
                    if (level >= 25) lines.Add("Platinum Filigree settings are the finest I've ever worked with.");
                    break;
                case "Tailoring":
                    lines.Add("I need more silk for a new robe I'm working on.");
                    lines.Add("Just stitched a new cloak. Should sell well.");
                    lines.Add("WTS robes and cloth armor, whisper me.");
                    lines.Add("Spider Silk Strands are the best thread for light armor.");
                    lines.Add("Steel Chain Links from a smith make great chainmail inserts.");
                    lines.Add("Soulsteel Chain Links are incredible. Spirit-infused armor!");
                    lines.Add("Silken Grass Blades weave into the softest cloth.");
                    lines.Add("Adamantite Plates from a smith make legendary heavy armor.");
                    break;
            }

            // ── Utility skill chatter ──
            lines.Add("Has anyone tried Foraging in the desert zones? Found some Desert Fig.");
            lines.Add("My Fishing skill is getting up there. Caught something weird yesterday.");
            lines.Add("Swimming across that lake was terrifying. My Swimming skill barely kept me alive.");
            lines.Add("Bind Wound saved my life in the dungeon. Don't skip it.");
            lines.Add("Meditation is so underrated. Mana regen between pulls is huge.");
            lines.Add("I've been working on my Foraging. Wild Herbs everywhere in the plains.");

            // ── Combat skill references ──
            lines.Add("My 1H Slashing is finally getting good. The damage bonus is noticeable.");
            lines.Add("Anyone else notice weapon skills make a real difference at higher levels?");
            lines.Add("Hand to Hand is actually viable now. Monks rejoice.");
            lines.Add("Archery skill takes forever to level but the damage bonus is insane.");

            // ── Magic skill references ──
            lines.Add("Evocation damage scales really well once you level the skill.");
            lines.Add("Abjuration skill makes your wards so much stronger.");
            lines.Add("Alteration heals hit different when your skill is maxed.");

            // ── General tradeskill/economy chatter ──
            lines.Add("Tradeskills are such a grind, but I love it.");
            lines.Add("LF tradeskill materials. Paying good gold.");
            lines.Add("Cross-crafting is the real endgame. Smiths and tailors need each other.");
            lines.Add("Anyone know where to find Faerie Dust? Alchemists pay well for it.");
            lines.Add("Fae-Touched Blossoms are so rare. I've only found two.");
            lines.Add("Enchanted Roots make everything better. Worth the grind.");
            lines.Add("WTB Obsidian Shards. PST.");
            lines.Add("Ashbloom Herbs only grow near volcanic soil. Check the fire zones.");
            lines.Add("Solunarian Flowers are the rarest herb I've ever seen.");

            if (lines.Count == 0) return null;
            return lines[UnityEngine.Random.Range(0, lines.Count)];
        }
    }
}
