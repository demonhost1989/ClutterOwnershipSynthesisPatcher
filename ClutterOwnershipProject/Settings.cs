using Newtonsoft.Json;
using System.ComponentModel;


namespace ClutterOwnershipSynthesisPatcher
{

    [JsonObject]
    public class Settings
    {

        [DisplayName("Owners to never assign")]
        [Description("Substring match against the owning NPC or Faction's EditorID. Already-owned objects whose owner matches one of these terms are ignored entirely when tallying votes — they can never influence or become the majority owner. Useful for excluding pseudo-ownership markers (like the Player or PlayerFaction) that don't represent a real household/shop owner.")]
        [JsonProperty]
        public List<string> ExcludeOwnerNames { get; set; } =
        [
            "Player", "PlayerFaction", "CW", "Bandit", "Hagraven", "Fort", "Hunter", "Draugr", 
        ];

        [DisplayName("Minimum owned objects required for a majority")]
        [Description("A cell needs at least this many already-owned MISC/CONT/ALCH objects before its majority owner is trusted and applied to the unowned ones in that cell. Set to 1 to trust even a single owned object.")]
        [JsonProperty]
        public int MinimumOwnedObjectsForMajority { get; set; } = 1;

        [DisplayName("Names to exclude")]
        [Description("Substring match against the placed object's Base EditorID. Loose items you never want touched regardless of cell ownership (e.g. Gold, Lockpicks, quest-critical keys).")]
        [JsonProperty]
        public List<string> ExcludeNameTerms { get; set; } =
        [
            "Axe01", "weapPickaxe", "Bandit", "Treas", "Dummy", "Test", 
        ];

        [DisplayName("Plugins to exclude")]
        [Description("ExcludePlugins")]
        [JsonProperty]
        public List<string> ExcludePlugins { get; set; } =
        [
            "Vigilant", "SkyrimUnderground", "HearthFire", "Glenmoril",
        ];

        [DisplayName("Cells to exclude")]
        [Description("ExcludeCellRules")]
        [JsonProperty]
        public List<string> ExcludeCellRules { get; set; } =
        [
            "BYOH", "Helgen", "GuardianStones", 
        ];

        [DisplayName("Location Types to exclude")]
        [Description("Matched only against the location's LocType-prefixed keywords (e.g. LocTypeDungeon) — unrelated keyword data like Civil War or world-interaction flags is deliberately ignored.")]
        [JsonProperty]
        public List<string> ExcludeLocTypeRules { get; set; } =
        [
            "Dungeon", "AnimalDen", "Bandit", "Dragonlair", "Draugr", "Dwarven",
            "Falmer", "Giant", "Hagraven", "Spriggan", "Vampire", "Warlock",
            "Werewolf", "Forsworn", "Cave", "Ruin", "PlayerHouse", "Lair",
        ];
    }
}
