using Newtonsoft.Json;
using System.ComponentModel;


namespace ClutterOwnershipProject
{

    [JsonObject]
    public class ManualOwnershipRule
    {
        [DisplayName("Cell")]
        [Description("Substring match against the cell's EditorID.")]
        [JsonProperty]
        public string Cell { get; set; } = "";

        [DisplayName("Owner")]
        [Description("EditorID of the NPC or Faction to assign as owner.")]
        [JsonProperty]
        public string Owner { get; set; } = "";
    }

    [JsonObject]
    public class Settings
    {

        [DisplayName("Owners to never assign")]
        [Description("Match against the owning NPC or Faction's EditorID. Already-owned objects whose owner matches one of these terms are ignored entirely when tallying votes — they can never influence or become the majority owner. Useful for excluding pseudo-ownership markers (like the Player or PlayerFaction) that don't represent a real household/shop owner.")]
        [JsonProperty]
        public List<string> ExcludeOwnerNames { get; set; } =
        [
            "Player", "PlayerFaction", "CW", "Bandit", "Hagraven", "Fort", "Hunter", "Draugr", "JobMerchantFaction", "Fake", 
        ];

        [DisplayName("Minimum owned objects required for a majority")]
        [Description("A cell needs at least this many already-owned objects before its majority owner is trusted and applied to the unowned ones in that cell. Set to 1 to trust even a single owned object.")]
        [JsonProperty]
        public int MinimumOwnedObjectsForMajority { get; set; } = 1;
        
        [DisplayName("Manual cell ownership")]
        [Description("Cells listed here skip the majority-owner vote entirely: every unowned eligible object in a matching cell (substring match against the cell's EditorID) is assigned the given owner (exact EditorID of an NPC or Faction). These rules also override the cell and location-type exclusion rules, though name and plugin exclusions still apply.")]
        [JsonProperty]
        public List<ManualOwnershipRule> ManualCellOwners { get; set; } =
        [
            new() { Cell = "SolitudeBluePalace", Owner = "SolitudeBluePalaceFaction" },
            new() { Cell = "WhiterunDragonsreach", Owner = "WhiterunJarlOwnershipFaction" },
            new() { Cell = "WBPT", Owner = "SolitudeBluePalaceFaction" },
            new() { Cell = "Dawnguard", Owner = "DLC1DawnguardFaction" },
            new() { Cell = "MarkarthSide", Owner = "TownMarkarthSideFaction" },
            new() { Cell = "Nightgate", Owner = "Hadring" },
        //  new() { Cell = "BlackBriarLodge", Owner = "RiftenBlackBriarHouseFaction" },
        ];
        
        [DisplayName("Names to exclude")]
        [Description("Match against the placed object's Base EditorID. Loose items you never want touched regardless of cell ownership (e.g. Gold, Lockpicks, quest-critical keys).")]
        [JsonProperty]
        public List<string> ExcludeNameTerms { get; set; } =
        [
            "Bandit", "Treas", "Dummy", "Test", "Merchant", "_chest", "EmptySkoomaBottle", "RuinedBook", "BYOH", "BurnedBook", "CWR", "BottleEmpty", "Empty",
        ];

        [DisplayName("Plugins to exclude")]
        [Description("Match against the plugin's name. Plugins matching any of these terms will be ignored entirely.")]
        [JsonProperty]
        public List<string> ExcludePlugins { get; set; } =
        [
            "Vigilant", "SkyrimUnderground", "HearthFire", "Glenmoril", "GodsAndWorship",
        ];

        [DisplayName("Cells to exclude")]
        [Description("Match against the cell's EditorID. Cells matching any of these terms will be ignored entirely.")]
        [JsonProperty]
        public List<string> ExcludeCellRules { get; set; } =
        [
            "BYOH", "Helgen", "GuardianStones", "RiftenThievesGuildHeadquarters", "DrelasCottage", "BlackBriarLodge", "Goldenglow",
        ];

        [DisplayName("Location Types to exclude")]
        [Description("Matched only against the location's LocType-prefixed keywords (e.g. LocTypeDungeon) — unrelated keyword data like Civil War or world-interaction flags is deliberately ignored.")]
        [JsonProperty]
        public List<string> ExcludeLocTypeRules { get; set; } =
        [
            "Dungeon", "AnimalDen", "Bandit", "Dragonlair", "Draugr", "Dwarven",
            "Falmer", "GiantCamp", "Hagraven", "Spriggan", "Vampire", "Warlock",
            "Werewolf", "Forsworn", "Cave", "Ruin", "PlayerHouse", "Lair", "Fort", 
        ];
    }
}
