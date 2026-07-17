using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;
using Newtonsoft.Json;


namespace ClutterOwnershipSynthesisPatcher
{
    public class Program
    {
        // ------------------------------------------------------------------
        // Settings load/save
        // ------------------------------------------------------------------

        private static readonly JsonSerializerSettings SettingsJsonOptions = new()
        {
            ObjectCreationHandling = ObjectCreationHandling.Replace,
        };

        static Lazy<Settings> LazySettings = new();
        static Settings Settings => LazySettings.Value;

        // ------------------------------------------------------------------
        // Console output helpers
        // ------------------------------------------------------------------

        private static bool _lastWasDivider = false;

        private static void PrintDivider()
        {
            if (_lastWasDivider) return;
            Console.WriteLine("------------------------------------------------------------------------------------------------------------------------");
            _lastWasDivider = true;
        }

        private static void PrintShortDivider()
        {
            if (_lastWasDivider) return;
            Console.WriteLine("------------------------------------------------------------");
            _lastWasDivider = true;
        }

        private static void ConsoleWriteLine(string text)
        {
            Console.WriteLine(text);
            _lastWasDivider = false;
        }

        // ------------------------------------------------------------------
        // Small utility helpers
        // ------------------------------------------------------------------

        private static bool IsPluginExcluded(string pluginName)
        {
            return Settings.ExcludePlugins.Any(pattern =>
                pluginName.Contains(pattern, StringComparison.OrdinalIgnoreCase));
        }

        private static bool RuleMatchesCell(string rule, string cellEdid)
        {
            return cellEdid.Contains(rule, StringComparison.OrdinalIgnoreCase);
        }

        // Walks up the placed-object's context chain to find its containing cell, re-resolving through
        // the link cache to guarantee the fully-merged winning override (rather than a minimal stub
        // from whichever plugin owns the placed reference).
        private static ICellGetter? FindContainingCell(
            IModContext<ISkyrimMod, ISkyrimModGetter, IPlacedObject, IPlacedObjectGetter> context,
            ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
        {
            var current = context.Parent;
            while (current != null)
            {
                if (current.Record is ICellGetter cell)
                {
                    if (linkCache.TryResolve<ICellGetter>(cell.FormKey, out var winningCell))
                        return winningCell;

                    return cell;
                }

                current = current.Parent;
            }

            return null;
        }

        // ------------------------------------------------------------------
        // Base record classification (MISC / CONT / ALCH / AMMO / BOOK / SCRL / other)
        // ------------------------------------------------------------------

        private enum RecordKind
        {
            Other,
            MiscItem,
            Container,
            Ingestible,
            Ammunition,
            Book,
            Scroll,
        }

        private readonly record struct BaseInfo(RecordKind Kind, string? EditorID);

        // Resolves a Base FormKey exactly once and classifies it. Callers cache the result per
        // FormKey (see baseInfoCache in RunPatch) since a huge number of placed objects share the
        // same Base record — no reason to resolve/classify the same one repeatedly.
        private static BaseInfo ClassifyBase(FormKey baseFormKey, ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
        {
            if (!linkCache.TryResolve<IMajorRecordGetter>(baseFormKey, out var baseRecord))
                return new BaseInfo(RecordKind.Other, null);

            var kind = baseRecord switch
            {
                IMiscItemGetter => RecordKind.MiscItem,
                IContainerGetter => RecordKind.Container,
                IIngestibleGetter => RecordKind.Ingestible,
                IAmmunitionGetter => RecordKind.Ammunition,
                IBookGetter => RecordKind.Book,
                IScrollGetter => RecordKind.Scroll,
                _ => RecordKind.Other,
            };

            return new BaseInfo(kind, baseRecord.EditorID);
        }

        // ------------------------------------------------------------------
        // Majority-owner resolution
        // ------------------------------------------------------------------

        // Picks the most common owner FormKey from a cell's tally. Ties are broken by preferring
        // a Faction owner over an NPC owner, if one of the tied candidates is a Faction; if the tie
        // is between owners of the same kind (or the Faction check can't resolve either), the first
        // encountered candidate wins, deterministically (Dictionary enumeration order is stable for
        // a given set of insertions within a single run).
        private static FormKey PickMajorityOwner(
            Dictionary<FormKey, int> ownerCounts,
            ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
        {
            int maxCount = ownerCounts.Values.Max();
            var topOwners = ownerCounts.Where(kv => kv.Value == maxCount).Select(kv => kv.Key).ToList();

            if (topOwners.Count == 1)
                return topOwners[0];

            foreach (var formKey in topOwners)
            {
                if (linkCache.TryResolve<IMajorRecordGetter>(formKey, out var rec) && rec is IFactionGetter)
                    return formKey;
            }

            return topOwners[0];
        }

        // Picks the most common FactionRank recorded alongside a given (cell, owner) pairing. Falls
        // back to 0 (matching the crop/animal patchers' convention) if nothing was recorded.
        private static int PickRepresentativeRank(Dictionary<int, int>? rankCounts)
        {
            if (rankCounts == null || rankCounts.Count == 0)
                return 0;

            return rankCounts.OrderByDescending(kv => kv.Value).First().Key;
        }

        // ------------------------------------------------------------------
        // Main patching pass
        // ------------------------------------------------------------------

        public static void RunPatch(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            var settings = LoadRunSettings(state);

            var baseInfoCache = new Dictionary<FormKey, BaseInfo>();
            var ownerEdidCache = new Dictionary<FormKey, string?>();
            var seen = new HashSet<FormKey>();

            // Tallies, keyed by the containing cell's FormKey.
            var ownerCountsByCell = new Dictionary<FormKey, Dictionary<FormKey, int>>();
            var rankCountsByCellOwner = new Dictionary<(FormKey Cell, FormKey Owner), Dictionary<int, int>>();

            // Unowned candidates of an eligible record type, bucketed by containing cell, waiting for pass 2.
            var candidatesByCell = new Dictionary<FormKey, List<(IModContext<ISkyrimMod, ISkyrimModGetter, IPlacedObject, IPlacedObjectGetter> Context, string EditorID)>>();

            int alreadyOwnedCount = 0;
            int excludedOwnerVotesCount = 0;
            int excludedCount = 0;
            var excludedCropsByPlugin = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var excludedCellsByRule = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var excludedLocTypesByRule = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var excludedNamesByRule = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            PrintShortDivider();
            ConsoleWriteLine("SCANNING...".PadLeft(35));
            PrintShortDivider();

            // ---- Pass 1: classify every placed object, tally existing ownership, and collect
            // unowned candidates of eligible record types per cell. ----
            foreach (var context in state.LoadOrder.PriorityOrder.PlacedObject().WinningContextOverrides(state.LinkCache))
            {
                var placedObject = context.Record;

                if (!seen.Add(placedObject.FormKey))
                    continue;

                var baseFormKeyNullable = placedObject.Base.FormKeyNullable;
                if (baseFormKeyNullable is not { } baseFormKey)
                    continue;

                if (!baseInfoCache.TryGetValue(baseFormKey, out var baseInfo))
                {
                    baseInfo = ClassifyBase(baseFormKey, state.LinkCache);
                    baseInfoCache[baseFormKey] = baseInfo;
                }

                if (baseInfo.Kind == RecordKind.Other || baseInfo.EditorID == null)
                    continue;

                var itemEdid = baseInfo.EditorID;
                string pluginName = placedObject.FormKey.ModKey.FileName;

                var containingCell = FindContainingCell(context, state.LinkCache);
                if (containingCell == null)
                    continue; // Can't group without knowing the cell.

                var cellEdid = containingCell.EditorID ?? "Unknown cell";
                var cellFormKey = containingCell.FormKey;

                // Cell exclusion.
                bool cellExcluded = false;
                foreach (var rule in settings.ExcludeCellRules)
                {
                    if (RuleMatchesCell(rule, cellEdid))
                    {
                        cellExcluded = true;
                        if (!excludedCellsByRule.TryGetValue(rule, out var cellList))
                            excludedCellsByRule[rule] = cellList = [];

                        cellList.Add(itemEdid);
                        break;
                    }
                }

                // Location-type exclusion (matched against LocType-prefixed keywords only, e.g.
                // LocTypeDungeon — deliberately ignoring unrelated keyword data like Civil War or
                // world-interaction flags that can share vocabulary with these terms).
                if (!cellExcluded && settings.ExcludeLocTypeRules.Count > 0)
                {
                    var location = containingCell.Location.TryResolve(state.LinkCache);
                    var keywordEdids = location?.Keywords?
                        .Select(k => k.TryResolve(state.LinkCache)?.EditorID)
                        .Where(e => e != null && e.StartsWith("LocType", StringComparison.OrdinalIgnoreCase))
                        .Select(e => e!)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    if (keywordEdids != null && keywordEdids.Count > 0)
                    {
                        foreach (var rule in settings.ExcludeLocTypeRules)
                        {
                            if (keywordEdids.Any(k => k.Contains(rule, StringComparison.OrdinalIgnoreCase)))
                            {
                                cellExcluded = true;
                                if (!excludedLocTypesByRule.TryGetValue(rule, out var list))
                                    excludedLocTypesByRule[rule] = list = [];

                                list.Add(itemEdid);
                                break;
                            }
                        }
                    }
                }

                if (cellExcluded)
                {
                    excludedCount++;
                    continue;
                }

                // Plugin exclusion.
                if (IsPluginExcluded(pluginName))
                {
                    if (!excludedCropsByPlugin.TryGetValue(pluginName, out var pluginList))
                        excludedCropsByPlugin[pluginName] = pluginList = [];

                    pluginList.Add(itemEdid);
                    excludedCount++;
                    continue;
                }

                // Name exclusion.
                var matchedNameTerm = settings.ExcludeNameTerms
                    .FirstOrDefault(term => itemEdid.Contains(term, StringComparison.OrdinalIgnoreCase));
                if (matchedNameTerm != null)
                {
                    if (!excludedNamesByRule.TryGetValue(matchedNameTerm, out var nameList))
                        excludedNamesByRule[matchedNameTerm] = nameList = [];

                    nameList.Add(itemEdid);
                    excludedCount++;
                    continue;
                }

                if (!placedObject.Owner.IsNull)
                {
                    alreadyOwnedCount++;

                    var ownerFormKeyNullable = placedObject.Owner.FormKeyNullable;
                    if (ownerFormKeyNullable is { } ownerFormKey)
                    {
                        if (!ownerEdidCache.TryGetValue(ownerFormKey, out var ownerEdid))
                        {
                            ownerEdid = state.LinkCache.TryResolve<IMajorRecordGetter>(ownerFormKey, out var ownerRec)
                                ? ownerRec.EditorID
                                : null;
                            ownerEdidCache[ownerFormKey] = ownerEdid;
                        }

                        bool ownerIsExcluded = ownerEdid != null
                            && settings.ExcludeOwnerNames.Any(term => ownerEdid.Contains(term, StringComparison.OrdinalIgnoreCase));

                        if (!ownerIsExcluded)
                        {
                            if (!ownerCountsByCell.TryGetValue(cellFormKey, out var ownerCounts))
                                ownerCountsByCell[cellFormKey] = ownerCounts = [];

                            ownerCounts.TryGetValue(ownerFormKey, out var count);
                            ownerCounts[ownerFormKey] = count + 1;

                            var rankKey = (cellFormKey, ownerFormKey);
                            if (!rankCountsByCellOwner.TryGetValue(rankKey, out var rankCounts))
                                rankCountsByCellOwner[rankKey] = rankCounts = [];

                            var factionRank = placedObject.FactionRank ?? 0;
                            rankCounts.TryGetValue(factionRank, out var rankCount);
                            rankCounts[factionRank] = rankCount + 1;
                        }
                        else
                        {
                            excludedOwnerVotesCount++;
                        }
                    }

                    continue;
                }

                // Unowned candidate of an eligible record type — queued for pass 2.
                if (!candidatesByCell.TryGetValue(cellFormKey, out var candidateList))
                    candidatesByCell[cellFormKey] = candidateList = [];

                candidateList.Add((context, itemEdid));
            }

            // ---- Pass 2: for each cell with unowned candidates, determine the majority owner (if
            // any) and apply it. ----
            PrintShortDivider();
            ConsoleWriteLine("PATCHING...".PadLeft(35));
            PrintShortDivider();

            int patchedCount = 0;
            int noOwnershipDataCount = 0;
            int belowThresholdCount = 0;
            var patchedItemsByCell = new Dictionary<string, List<(string Item, string Plugin, string OwnerLabel)>>(StringComparer.OrdinalIgnoreCase);
            var patchedItemTypeCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var cellVoteInfo = new Dictionary<string, (int WinningVotes, int TotalVotes)>(StringComparer.OrdinalIgnoreCase);

            foreach (var (cellFormKey, candidates) in candidatesByCell)
            {
                if (!ownerCountsByCell.TryGetValue(cellFormKey, out var ownerCounts) || ownerCounts.Count == 0)
                {
                    noOwnershipDataCount += candidates.Count;
                    continue;
                }

                int totalOwnedInCell = ownerCounts.Values.Sum();
                if (totalOwnedInCell < settings.MinimumOwnedObjectsForMajority)
                {
                    belowThresholdCount += candidates.Count;
                    continue;
                }

                var majorityOwnerFormKey = PickMajorityOwner(ownerCounts, state.LinkCache);

                if (!state.LinkCache.TryResolve<IOwnerGetter>(majorityOwnerFormKey, out var ownerRecord))
                {
                    // The winning owner FormKey didn't resolve to something ownable — shouldn't
                    // normally happen, but skip defensively rather than throw mid-patch.
                    noOwnershipDataCount += candidates.Count;
                    continue;
                }

                rankCountsByCellOwner.TryGetValue((cellFormKey, majorityOwnerFormKey), out var rankCounts);
                int rankToApply = PickRepresentativeRank(rankCounts);

                var ownerLabel = (ownerRecord as IMajorRecordGetter)?.EditorID ?? majorityOwnerFormKey.ToString();
                var cellLabelForReport = state.LinkCache.TryResolve<ICellGetter>(cellFormKey, out var reportCell)
                    ? (reportCell.EditorID ?? "Unknown cell")
                    : "Unknown cell";

                // How many of the cell's owned eligible objects actually voted for the winning
                // owner, out of how many owned objects (that counted as votes) were in the cell at all.
                ownerCounts.TryGetValue(majorityOwnerFormKey, out var winningVotes);
                cellVoteInfo[cellLabelForReport] = (winningVotes, totalOwnedInCell);

                foreach (var (context, itemEdid) in candidates)
                {
                    // Defensive re-check: candidates should already be guaranteed unowned by pass 1
                    // (owned objects hit `continue` before ever being queued), but this guards against
                    // a future change accidentally breaking that invariant — we never want to overwrite
                    // an existing owner, so if this ever fires, skip rather than risk clobbering data.
                    if (!context.Record.Owner.IsNull)
                        continue;

                    var patchObject = context.GetOrAddAsOverride(state.PatchMod);
                    patchObject.Owner.SetTo(ownerRecord);
                    patchObject.FactionRank = rankToApply;

                    patchedCount++;
                    patchedItemTypeCounts.TryGetValue(itemEdid, out var itemCount);
                    patchedItemTypeCounts[itemEdid] = itemCount + 1;

                    if (!patchedItemsByCell.TryGetValue(cellLabelForReport, out var cellList))
                        patchedItemsByCell[cellLabelForReport] = cellList = [];

                    cellList.Add((itemEdid, context.Record.FormKey.ModKey.FileName, ownerLabel));
                }
            }

            PrintReport(
                patchedItemsByCell,
                patchedItemTypeCounts,
                cellVoteInfo,
                excludedCropsByPlugin,
                excludedCellsByRule,
                excludedLocTypesByRule,
                excludedNamesByRule,
                patchedCount,
                alreadyOwnedCount,
                excludedOwnerVotesCount,
                noOwnershipDataCount,
                belowThresholdCount,
                excludedCount);
        }

        // Loads (or generates) the settings file used for this run.
        private static Settings LoadRunSettings(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            string[] tryNames = ["Settings.json", "settings.json"];
            string? configContent = null;

            foreach (var name in tryNames)
            {
                try
                {
                    configContent = state.RetrieveConfigFile(name);
                    break;
                }
                catch (FileNotFoundException)
                {
                    // try next name
                }
            }

            if (configContent is null)
            {
                var defaultSettings = LazySettings.Value;
                configContent = JsonConvert.SerializeObject(defaultSettings, Newtonsoft.Json.Formatting.Indented);
                try
                {
                    var outPath = Path.Combine(Environment.CurrentDirectory, tryNames[0]);
                    File.WriteAllText(outPath, configContent);
                    ConsoleWriteLine($"Generated default config file: {tryNames[0]}");
                }
                catch (IOException ioEx)
                {
                    ConsoleWriteLine($"WARNING: Failed to write default config file: {ioEx.Message}");
                }
            }

            try
            {
                return JsonConvert.DeserializeObject<Settings>(configContent!, SettingsJsonOptions) ?? LazySettings.Value;
            }
            catch (Newtonsoft.Json.JsonException)
            {
                ConsoleWriteLine("WARNING: Could not parse Settings File; using defaults.");
                return LazySettings.Value;
            }
        }

        // ------------------------------------------------------------------
        // Reporting
        // ------------------------------------------------------------------

        private static void PrintReport(
            Dictionary<string, List<(string Item, string Plugin, string OwnerLabel)>> patchedItemsByCell,
            Dictionary<string, int> patchedItemTypeCounts,
            Dictionary<string, (int WinningVotes, int TotalVotes)> cellVoteInfo,
            Dictionary<string, List<string>> excludedItemsByPlugin,
            Dictionary<string, List<string>> excludedCellsByRule,
            Dictionary<string, List<string>> excludedLocTypesByRule,
            Dictionary<string, List<string>> excludedNamesByRule,
            int patchedCount,
            int alreadyOwnedCount,
            int excludedOwnerVotesCount,
            int noOwnershipDataCount,
            int belowThresholdCount,
            int excludedCount)
        {
            var totalPatched = patchedItemsByCell.Values.SelectMany(v => v).Count();

            _lastWasDivider = false;
            PrintShortDivider();
            ConsoleWriteLine("PATCHED BY CELL".PadLeft(36));
            ConsoleWriteLine($"Total patched: {totalPatched}".PadLeft(37));
            PrintShortDivider();

            foreach (var kvp in patchedItemsByCell.OrderByDescending(k => k.Value.Count))
            {
                var cellLabel = kvp.Key;
                var items = kvp.Value;

                var voteLabel = cellVoteInfo.TryGetValue(cellLabel, out var votes)
                    ? $"   [decided by {votes.WinningVotes}/{votes.TotalVotes} owned objects]"
                    : "";

                ConsoleWriteLine($"{cellLabel}   ({items.Count} patched){voteLabel}");

                var byOwner = items
                    .GroupBy(a => a.OwnerLabel)
                    .Select(g => new { Owner = g.Key, Count = g.Count(), Items = g.ToList() })
                    .OrderByDescending(o => o.Count);

                foreach (var ownerGroup in byOwner)
                {
                    ConsoleWriteLine($"     now owned by: {ownerGroup.Owner}   ({ownerGroup.Count})");

                    var byItem = ownerGroup.Items
                        .GroupBy(a => a.Item)
                        .Select(g => new { Item = g.Key, Count = g.Count() })
                        .OrderByDescending(a => a.Count);

                    foreach (var entry in byItem)
                    {
                        ConsoleWriteLine($"          {entry.Count} {entry.Item}(s)");
                    }
                }

                PrintDivider();
            }

            _lastWasDivider = false;
            PrintShortDivider();
            ConsoleWriteLine("EXCLUSION SUMMARY".PadLeft(37));
            PrintShortDivider();

            var combined = new List<(string Rule, int Count, string Type)>();

            foreach (var kv in excludedItemsByPlugin)
            {
                if (kv.Value.Count > 0)
                    combined.Add((kv.Key, kv.Value.Count, "plugin"));
            }

            foreach (var kv in excludedCellsByRule)
            {
                if (kv.Value.Count > 0)
                    combined.Add((kv.Key, kv.Value.Count, "cell"));
            }

            foreach (var kv in excludedLocTypesByRule)
            {
                if (kv.Value.Count > 0)
                    combined.Add((kv.Key, kv.Value.Count, "loctype"));
            }

            foreach (var kv in excludedNamesByRule)
            {
                if (kv.Value.Count > 0)
                    combined.Add((kv.Key, kv.Value.Count, "name"));
            }

            foreach (var entry in combined.OrderByDescending(e => e.Count))
            {
                ConsoleWriteLine($"The rule: {entry.Rule} ({entry.Type}) excluded {entry.Count} object(s)");
            }

            _lastWasDivider = false;
            PrintShortDivider();
            ConsoleWriteLine("GENERAL SUMMARY".PadLeft(35));
            PrintShortDivider();

            var summaryLines = new List<(string Label, int Count, bool ShowItems)>
            {
                ("Objects have been assigned owners", patchedCount, true),
                ("Objects were already owned", alreadyOwnedCount, false),
                ("Owned objects were excluded from voting by ExcludeOwnerNames", excludedOwnerVotesCount, false),
                ("Objects were in a cell with no ownership data at all", noOwnershipDataCount, false),
                ("Objects were in a cell below the minimum-owned threshold", belowThresholdCount, false),
                ("Objects were excluded by rules", excludedCount, false),
            };

            foreach (var (label, count, showItems) in summaryLines.OrderByDescending(l => l.Count))
            {
                ConsoleWriteLine($"{count} {label}");

                if (showItems)
                {
                    foreach (var kvp in patchedItemTypeCounts.OrderByDescending(k => k.Value))
                    {
                        ConsoleWriteLine($"    {kvp.Value}  {kvp.Key}");
                    }
                }
            }

            PrintDivider();
            ConsoleWriteLine("Patching is complete! Scroll up to read a report on what was patched and why anything was skipped.");
            ConsoleWriteLine("\"No ownership data\" means the cell had zero already-owned MISC/CONT/ALCH/AMMO/BOOK/SCRL objects to learn a pattern from.");
            ConsoleWriteLine("\"Below threshold\" means the cell had SOME ownership data, but fewer owned objects than MinimumOwnedObjectsForMajority.");
            PrintDivider();
        }

        // ------------------------------------------------------------------
        // Entry point
        // ------------------------------------------------------------------

        public static async Task<int> Main(string[] args)
        {
            return await SynthesisPipeline.Instance
                .SetAutogeneratedSettings(
                    "Settings",
                    "settings.json",
                    out LazySettings)
                .AddPatch<ISkyrimMod, ISkyrimModGetter>(RunPatch)
                .SetTypicalOpen(GameRelease.SkyrimSE, "ClutterOwnership.esp")
                .Run(args);
        }
    }
}
