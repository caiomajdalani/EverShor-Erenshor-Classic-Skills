using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace ErenshorSkills
{
    /// <summary>
    /// Dumps the game's runtime databases (items, spells, skills) to CSV files
    /// for reference. Activated by /dumpdb chat command.
    /// </summary>
    public static class DatabaseDumper
    {
        public static void DumpAll()
        {
            string dir = Path.Combine(RuntimePaths.DataDirectory, "dumps");
            Directory.CreateDirectory(dir);

            int items = DumpItems(Path.Combine(dir, "items.csv"));
            int spells = DumpSpells(Path.Combine(dir, "spells.csv"));
            int skills = DumpSkills(Path.Combine(dir, "skills.csv"));
            int npcs = DumpNPCs(Path.Combine(dir, "npcs.csv"));
            int acquisition = DumpSpellAcquisition(Path.Combine(dir,
                "spell_acquisition.csv"));

            ChatHelper.Send($"<color=#00FF00>[DB Dump]</color> Exported {items} items, {spells} spells, {skills} skills, {npcs} live NPCs, and {acquisition} spell-acquisition rows to:");
            ChatHelper.Send($"<color=#AAAAAA>{dir}</color>");
        }

        static string Esc(string s) => s == null ? "" :
            "\"" + s.Replace("\"", "\"\"").Replace("\n", " ").Replace("\r", "") + "\"";

        static int DumpItems(string path)
        {
            var db = GameData.ItemDB?.ItemDB;
            if (db == null || db.Length == 0) return 0;
            var sb = new StringBuilder();
            sb.AppendLine("Id,Name,ItemLevel,Slot,WeaponType,WeaponDmg,WeaponDly," +
                "AC,HP,Mana,Str,End,Dex,Agi,Int,Wis,Cha,Res,MR,ER,PR,VR," +
                "Stackable,Disposable,Value,Classes,Lore");
            int count = 0;
            foreach (var item in db)
            {
                if (item == null) continue;
                string classes = "";
                if (item.Classes != null && item.Classes.Count > 0)
                {
                    var names = new System.Collections.Generic.List<string>();
                    foreach (var c in item.Classes)
                        if (c != null) names.Add(c.ClassName ?? "?");
                    classes = string.Join(";", names);
                }

                sb.AppendLine($"{Esc(item.Id)},{Esc(item.ItemName)},{item.ItemLevel}," +
                    $"{item.RequiredSlot},{item.ThisWeaponType},{item.WeaponDmg},{item.WeaponDly}," +
                    $"{item.AC},{item.HP},{item.Mana}," +
                    $"{item.Str},{item.End},{item.Dex},{item.Agi}," +
                    $"{item.Int},{item.Wis},{item.Cha},{item.Res}," +
                    $"{item.MR},{item.ER},{item.PR},{item.VR}," +
                    $"{item.Stackable},{item.Disposable},{item.ItemValue}," +
                    $"{Esc(classes)},{Esc(item.Lore)}");
                count++;
            }
            File.WriteAllText(path, sb.ToString());
            return count;
        }

        static int DumpSpells(string path)
        {
            var db = GameData.SpellDatabase?.SpellDatabase;
            if (db == null || db.Length == 0) return 0;
            var sb = new StringBuilder();
            sb.AppendLine("Id,Name,Type,Line,RequiredLevel,ManaCost,Cooldown," +
                "TargetDamage,TargetHealing,CasterHealing,ShieldingAmt," +
                "DurationTicks,ChargeTime,Aggro," +
                "HP,AC,Mana,Str,End,Dex,Agi,Int,Wis,Cha,MR,ER,PR,VR," +
                "MovementSpeed,SelfOnly,InstantEffect," +
                "Classes,Description");
            int count = 0;
            foreach (var sp in db)
            {
                if (sp == null) continue;
                string classes = "";
                if (sp.UsedBy != null && sp.UsedBy.Count > 0)
                {
                    var names = new System.Collections.Generic.List<string>();
                    foreach (var c in sp.UsedBy)
                        if (c != null) names.Add(c.ClassName ?? "?");
                    classes = string.Join(";", names);
                }

                sb.AppendLine($"{Esc(sp.Id)},{Esc(sp.SpellName)},{sp.Type},{sp.Line}," +
                    $"{sp.RequiredLevel},{sp.ManaCost},{sp.Cooldown}," +
                    $"{sp.TargetDamage},{sp.TargetHealing},{sp.CasterHealing},{sp.ShieldingAmt}," +
                    $"{sp.SpellDurationInTicks},{sp.SpellChargeTime},{sp.Aggro}," +
                    $"{sp.HP},{sp.AC},{sp.Mana},{sp.Str},{sp.End},{sp.Dex},{sp.Agi}," +
                    $"{sp.Int},{sp.Wis},{sp.Cha},{sp.MR},{sp.ER},{sp.PR},{sp.VR}," +
                    $"{sp.MovementSpeed},{sp.SelfOnly},{sp.InstantEffect}," +
                    $"{Esc(classes)},{Esc(sp.SpellDesc)}");
                count++;
            }
            File.WriteAllText(path, sb.ToString());
            return count;
        }

        static int DumpSkills(string path)
        {
            var db = GameData.SkillDatabase?.SkillDatabase;
            if (db == null || db.Length == 0) return 0;
            var sb = new StringBuilder();
            sb.AppendLine("Id,Name,Type,Cooldown,SkillRange,SkillPower,PercentDmg," +
                "DuelistLvl,PaladinLvl,ArcanistLvl,DruidLvl,StormcallerLvl,ReaverLvl," +
                "Interrupt,RequireBehind,Require2H,RequireDW,RequireBow,RequireShield," +
                "Description");
            int count = 0;
            foreach (var sk in db)
            {
                if (sk == null) continue;
                sb.AppendLine($"{Esc(sk.Id)},{Esc(sk.SkillName)},{sk.TypeOfSkill},{sk.Cooldown}," +
                    $"{sk.SkillRange},{sk.SkillPower},{sk.PercentDmg}," +
                    $"{sk.DuelistRequiredLevel},{sk.PaladinRequiredLevel}," +
                    $"{sk.ArcanistRequiredLevel},{sk.DruidRequiredLevel}," +
                    $"{sk.StormcallerRequiredLevel},{sk.ReaverRequiredLevel}," +
                    $"{sk.Interrupt},{sk.RequireBehind},{sk.Require2H}," +
                    $"{sk.RequireDW},{sk.RequireBow},{sk.RequireShield}," +
                    $"{Esc(sk.SkillDesc)}");
                count++;
            }
            File.WriteAllText(path, sb.ToString());
            return count;
        }

        static int DumpNPCs(string path)
        {
            var liveNPCs = NPCTable.LiveNPCs;
            if (liveNPCs == null || liveNPCs.Count == 0) return 0;
            var sb = new StringBuilder();
            sb.AppendLine("Name,Level,Faction,SimPlayer,Zone," +
                "MaxHP,AC,BaseStr,BaseEnd,BaseDex,BaseAgi,BaseInt,BaseWis,BaseCha," +
                "BaseAtkDmg,MinAtkDmg,OHAtkDmg,AggroRange," +
                "IsVendor,VendorDesc,VendorItems," +
                "GuaranteedDrops,CommonDrops,UncommonDrops,RareDrops,LegendaryDrops,UltraRareDrops," +
                "MaxDrops,MaxGold,MinGold,GuildName," +
                "BuffSpells,AttackSpells,HealSpells");
            int count = 0;
            var seen = new System.Collections.Generic.HashSet<string>();


            foreach (var npc in liveNPCs)
            {
                if (npc == null) continue;
                try
                {
                    var ch = npc.GetComponent<Character>();
                    var stats = npc.GetComponent<Stats>();
                    var loot = npc.GetComponent<LootTable>();
                    var vendor = npc.GetComponent<VendorInventory>();
                    if (ch == null || stats == null) continue;

                    string npcName = stats.MyName;
                    if (string.IsNullOrEmpty(npcName)) npcName = npc.NPCName;
                    if (string.IsNullOrEmpty(npcName)) npcName = npc.transform.name;
                    // Deduplicate by name+level
                    string key = $"{npcName}_{stats.Level}";
                    if (seen.Contains(key)) continue;
                    seen.Add(key);

                    string zone = npc.gameObject.scene.name ?? "";

                    // Loot table
                    string guaranteed = "", common = "", uncommon = "";
                    string rare = "", legendary = "", ultraRare = "";
                    int maxDrops = 0, maxGold = 0, minGold = 0;
                    if (loot != null)
                    {
                        guaranteed = ItemListStr(loot.GuaranteeOneDrop);
                        common = ItemListStr(loot.CommonDrop);
                        uncommon = ItemListStr(loot.UncommonDrop);
                        rare = ItemListStr(loot.RareDrop);
                        legendary = ItemListStr(loot.LegendaryDrop);
                        ultraRare = ItemListStr(loot.UltraRareDrop);
                        maxDrops = loot.MaxNumberDrops;
                        maxGold = loot.MaxGold;
                        minGold = loot.MinGold;
                    }

                    // Vendor
                    bool isVendor = vendor != null;
                    string vendorDesc = vendor?.VendorDesc ?? "";
                    string vendorItems = "";
                    if (vendor?.ItemsForSale != null)
                        vendorItems = ItemListStr(vendor.ItemsForSale);

                    // Spells
                    string buffs = SpellListStr(npc.MyBuffSpells);
                    string attacks = SpellListStr(npc.MyAttackSpells);
                    string heals = SpellListStr(npc.MyHealSpells);

                    sb.AppendLine(
                        $"{Esc(npcName)},{stats.Level},{ch.MyFaction},{npc.SimPlayer},{Esc(zone)}," +
                        $"{stats.CurrentMaxHP},{stats.BaseAC}," +
                        $"{stats.BaseStr},{stats.BaseEnd},{stats.BaseDex},{stats.BaseAgi}," +
                        $"{stats.BaseInt},{stats.BaseWis},{stats.BaseCha}," +
                        $"{npc.BaseAtkDmg},{npc.MinAtkDmg},{npc.OHAtkDmg},{ch.AggroRange}," +
                        $"{isVendor},{Esc(vendorDesc)},{Esc(vendorItems)}," +
                        $"{Esc(guaranteed)},{Esc(common)},{Esc(uncommon)}," +
                        $"{Esc(rare)},{Esc(legendary)},{Esc(ultraRare)}," +
                        $"{maxDrops},{maxGold},{minGold},{Esc(npc.GuildName)}," +
                        $"{Esc(buffs)},{Esc(attacks)},{Esc(heals)}");
                    count++;
                }
                catch { }
            }
            File.WriteAllText(path, sb.ToString());
            return count;
        }

        /// <summary>
        /// Audits every Resources/NPCs prefab, not only the currently loaded
        /// zone. This gives the add-on a source-backed reference for which
        /// native spell scrolls are merchant stock, mob drops, or raid drops.
        /// </summary>
        static int DumpSpellAcquisition(string path)
        {
            Item[] itemDb = GameData.ItemDB?.ItemDB;
            if (itemDb == null || itemDb.Length == 0) return 0;

            var vendorSources = new Dictionary<string, List<string>>(
                StringComparer.OrdinalIgnoreCase);
            var lootSources = new Dictionary<string, List<string>>(
                StringComparer.OrdinalIgnoreCase);
            GameObject[] prefabs = Resources.LoadAll<GameObject>("NPCs");
            foreach (GameObject prefab in prefabs)
            {
                if (prefab == null) continue;
                NPC npc = prefab.GetComponent<NPC>();
                Stats stats = prefab.GetComponent<Stats>();
                string npcName = npc?.NPCName;
                if (string.IsNullOrWhiteSpace(npcName))
                    npcName = stats?.MyName;
                if (string.IsNullOrWhiteSpace(npcName))
                    npcName = prefab.name;
                int level = stats?.Level ?? 0;
                string source = npcName + " (L" + level + ")";

                VendorInventory vendor = prefab.GetComponent<VendorInventory>();
                if (vendor?.ItemsForSale != null)
                    foreach (Item item in vendor.ItemsForSale)
                        AddAcquisitionSource(vendorSources, item, source);

                LootTable loot = prefab.GetComponent<LootTable>();
                if (loot == null) continue;
                AddAcquisitionSources(lootSources, loot.GuaranteeOneDrop,
                    source + " guaranteed");
                AddAcquisitionSources(lootSources, loot.CommonDrop,
                    source + " common");
                AddAcquisitionSources(lootSources, loot.UncommonDrop,
                    source + " uncommon");
                AddAcquisitionSources(lootSources, loot.RareDrop,
                    source + " rare");
                AddAcquisitionSources(lootSources, loot.LegendaryDrop,
                    source + " legendary");
                AddAcquisitionSources(lootSources, loot.UltraRareDrop,
                    source + " ultra-rare");
            }

            var output = new StringBuilder();
            output.AppendLine("ItemId,Scroll,Spell,RequiredLevel,Value,Classes,VendorSources,LootSources");
            int count = 0;
            foreach (Item item in itemDb)
            {
                if (item?.TeachSpell == null) continue;
                string classes = "";
                if (item.Classes != null)
                {
                    var names = new List<string>();
                    foreach (Class value in item.Classes)
                        if (value != null) names.Add(value.ClassName ?? "?");
                    classes = string.Join(";", names);
                }
                string key = AcquisitionKey(item);
                vendorSources.TryGetValue(key, out List<string> vendors);
                lootSources.TryGetValue(key, out List<string> drops);
                output.AppendLine($"{Esc(item.Id)},{Esc(item.ItemName)}," +
                    $"{Esc(item.TeachSpell.SpellName)}," +
                    $"{item.TeachSpell.RequiredLevel},{item.ItemValue}," +
                    $"{Esc(classes)},{Esc(JoinSources(vendors))}," +
                    $"{Esc(JoinSources(drops))}");
                count++;
            }
            File.WriteAllText(path, output.ToString());
            return count;
        }

        static void AddAcquisitionSources(
            Dictionary<string, List<string>> destination, List<Item> items,
            string source)
        {
            if (items == null) return;
            foreach (Item item in items)
                AddAcquisitionSource(destination, item, source);
        }

        static void AddAcquisitionSource(
            Dictionary<string, List<string>> destination, Item item,
            string source)
        {
            if (item?.TeachSpell == null) return;
            string key = AcquisitionKey(item);
            if (!destination.TryGetValue(key, out List<string> sources))
            {
                sources = new List<string>();
                destination[key] = sources;
            }
            if (!sources.Contains(source)) sources.Add(source);
        }

        static string AcquisitionKey(Item item)
        {
            if (item == null) return "";
            return !string.IsNullOrWhiteSpace(item.Id)
                ? item.Id
                : item.ItemName ?? "";
        }

        static string JoinSources(List<string> sources)
        {
            if (sources == null || sources.Count == 0) return "";
            sources.Sort(StringComparer.OrdinalIgnoreCase);
            return string.Join(";", sources);
        }

        static string ItemListStr(List<Item> items)
        {
            if (items == null || items.Count == 0) return "";
            var names = new System.Collections.Generic.List<string>();
            foreach (var i in items)
                if (i != null) names.Add(i.ItemName ?? i.Id ?? "?");
            return string.Join(";", names);
        }

        static string SpellListStr(List<Spell> spells)
        {
            if (spells == null || spells.Count == 0) return "";
            var names = new System.Collections.Generic.List<string>();
            foreach (var s in spells)
                if (s != null) names.Add(s.SpellName ?? s.Id ?? "?");
            return string.Join(";", names);
        }
    }
}
