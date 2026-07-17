using Newtonsoft.Json;
using System.ComponentModel;


namespace ClutterOwnershipSynthesisPatcher
{

    [JsonObject]
    public class Settings
    {

        [DisplayName("Minimum owned objects required for a majority")]
        [Description("A cell needs at least this many already-owned MISC/CONT/ALCH objects before its majority owner is trusted and applied to the unowned ones in that cell. Set to 1 to trust even a single owned object.")]
        [JsonProperty]
        public int MinimumOwnedObjectsForMajority { get; set; } = 1;

        [DisplayName("Names to exclude")]
        [Description("Substring match against the placed object's Base EditorID. Loose items you never want touched regardless of cell ownership (e.g. Gold, Lockpicks, quest-critical keys).")]
        [JsonProperty]
        public List<string> ExcludeNameTerms { get; set; } =
        [

        ];

        [DisplayName("Plugins to exclude")]
        [Description("ExcludePlugins")]
        [JsonProperty]
        public List<string> ExcludePlugins { get; set; } =
        [
            "Vigilant", "Underground", "HearthFire", "Glenmoril", "Sewers",
        ];

        [DisplayName("Cells to exclude")]
        [Description("ExcludeCellRules")]
        [JsonProperty]
        public List<string> ExcludeCellRules { get; set; } =
        [
            "BYOH", "Helgen",
        ];
    }
}