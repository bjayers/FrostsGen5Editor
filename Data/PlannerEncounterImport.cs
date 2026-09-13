using NewEditor.Data.NARCTypes;
using NewEditor.Forms;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace NewEditor.Data
{
    public class PlannerEncounterImport
    {
        static readonly HashSet<string> ImportTypes = new HashSet<string>
        {
            "grass", "darkGrass", "rustling", "cave", "surf", "ripples", "fishing", "fishRipples"
        };

        static readonly Dictionary<string, int> SeasonIndex = new Dictionary<string, int>
        {
            { "spring", 0 }, { "summer", 1 }, { "autumn", 2 }, { "winter", 3 }
        };

        static readonly Dictionary<string, string> LocationToPoolName = new Dictionary<string, string>
        {
            { "striaton-city", "Striaton City" }, { "castelia-city", "Castelia City" }, { "icirrus-city", "Icirrus City" },
            { "aspertia-city", "Aspertia City" }, { "virbank-city", "Virbank City" }, { "humilau-city", "Humilau City" },
            { "dreamyard", "Dreamyard 1" }, { "dreamyard-b1f", "Dreamyard 2" },
            { "pinwheel-outside", "Pinwheel Forest 1" }, { "pinwheel-inside", "Pinwheel Forest 2" },
            { "desert-resort-entrance", "Desert Resort 1" }, { "desert-resort", "Desert Resort 2" },
            { "relic-castle-1f", "Relic Castle 1" }, { "relic-castle-b1f", "Relic Castle 2" },
            { "relic-castle-b2f", "Relic Castle 3" }, { "relic-castle-lowest", "Relic Castle 4" },
            { "chargestone-1f", "Chargestone Cave 1" }, { "chargestone-b1f", "Chargestone Cave 2" }, { "chargestone-b2f", "Chargestone Cave 3" },
            { "twist-1f", "Twist Mountain 1" }, { "twist-b1f", "Twist Mountain 2" }, { "twist-b2f", "Twist Mountain 3" }, { "twist-ice", "Twist Mountain 4" },
            { "dragonspiral-outside", "Dragonspiral Tower 1" }, { "dragonspiral-entrance", "Dragonspiral Tower 2" },
            { "dragonspiral-1f", "Dragonspiral Tower 3" }, { "dragonspiral-2f", "Dragonspiral Tower 4" }, { "dragonspiral-interior", "Dragonspiral Tower 4" },
            { "victory-grove", "Victory Road" }, { "victory-entrance", "Victory Road 1" }, { "victory-1f", "Victory Road 2" },
            { "victory-2f", "Victory Road 3" }, { "victory-3f", "Victory Road 4" }, { "victory-ruins", "Victory Road 5" },
            { "victory-7f", "Victory Road 7" }, { "victory-waterfall", "Victory Road 8" },
            { "giant-chasm-outside", "Giant Chasm 1" }, { "giant-chasm-entrance", "Giant Chasm 2" },
            { "giant-chasm-forest", "Giant Chasm 3" }, { "giant-chasm-cave", "Giant Chasm 4" },
            { "castelia-sewers", "Castelia Sewers 1" }, { "castelia-sewers-b1f", "Castelia Sewers 2" },
            { "p2-laboratory", "P2 Laboratory" }, { "undella-bay", "Undella Bay" },
            { "floccesy-ranch-outer", "Floccesy Ranch 1" }, { "floccesy-ranch-inner", "Floccesy Ranch 2" },
            { "virbank-complex-outer", "Virbank Complex 1" }, { "virbank-complex-inner", "Virbank Complex 2" },
            { "reversal-exterior", "Reverse Mountain 1" }, { "reversal-1f", "Reverse Mountain 2" }, { "reversal-b1f", "Reverse Mountain 3" },
            { "reversal-interior-a", "Reverse Mountain 4" }, { "reversal-interior-b", "Reverse Mountain 5" },
            { "strange-house-1f", "Stranger's House 1" }, { "strange-house-b1f", "Stranger's House 2" },
            { "relic-passage-sewers", "Relic Passage 1" }, { "relic-passage-pwt", "Relic Passage 2" }, { "relic-passage-castle", "Relic Passage 3" },
            { "clay-tunnel", "Clay Road 1" }, { "underground-ruins", "Underground Ruins 1" },
            { "rocky-mountain-room", "Rocky Mountain Room" }, { "glacier-room", "Glacier Room" }, { "iron-room", "Iron Room" },
            { "seaside-cave-1f", "Seaside Grotto 1" }, { "seaside-cave-b1f", "Seaside Grotto 2" },
            { "nature-preserve", "Nature Sanctuary" }, { "driftveil-drawbridge", "Driftveil Drawbridge" },
            { "village-bridge", "Village Bridge" }, { "marvelous-bridge", "Marvelous Bridge" },
            { "route-1", "Route 1" }, { "route-2", "Route 2" }, { "route-3", "Route 3" },
            { "wellspring-1f", "Wellspring Cave 1" }, { "wellspring-b1f", "Wellspring Cave 2" },
            { "route-4", "Route 4 1" }, { "route-5", "Route 5" }, { "route-6", "Route 6" },
            { "mistralton-cave-1f", "Mistralton Cave 1" }, { "mistralton-cave-2f", "Mistralton Cave 2" },
            { "guidance-chamber", "Guidance Chamber" }, { "route-7", "Route 7" },
            { "celestial-2f", "Celestial Tower 1" }, { "celestial-3f", "Celestial Tower 2" },
            { "celestial-4f", "Celestial Tower 3" }, { "celestial-5f", "Celestial Tower 4" },
            { "route-8", "Route 8" }, { "moor-of-icirrus", "Moor of Icirrus" }, { "route-9", "Route 9" },
            { "route-11", "Route 11" }, { "route-12", "Route 12" }, { "route-13", "Route 13" }, { "route-14", "Route 14" },
            { "abundant-shrine", "Abundant Shrine" }, { "route-15", "Route 15" }, { "route-16", "Route 16" },
            { "lostlorn-forest", "Lostlorn Forest" }, { "route-18", "Route 18" }, { "route-19", "Route 19" },
            { "route-20", "Route 20" }, { "route-22", "Route 22" }, { "route-23", "Route 23" },
            { "undella-town", "Undella Town" }, { "route-17", "Route 17" }, { "route-21", "Route 21" }
        };

        public class Result
        {
            public int TablesWritten;
            public int SlotsWritten;
            public List<string> SkippedTypes = new List<string>();
            public List<string> UnmappedLocations = new List<string>();
            public List<string> MissingPools = new List<string>();
            public List<string> Warnings = new List<string>();
        }

        public static Result Import(string jsonText, EncounterNARC narc)
        {
            var result = new Result();
            using (JsonDocument doc = JsonDocument.Parse(jsonText))
            {
                var root = doc.RootElement;
                var levels = new Dictionary<string, Tuple<byte, byte>>();
                if (root.TryGetProperty("levels", out var levelsEl) && levelsEl.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in levelsEl.EnumerateObject())
                    {
                        byte min = 1, max = 1;
                        if (prop.Value.TryGetProperty("min", out var minEl)) min = ClampLevel(minEl.GetInt32());
                        if (prop.Value.TryGetProperty("max", out var maxEl)) max = ClampLevel(maxEl.GetInt32());
                        if (max < min) max = min;
                        levels[prop.Name] = Tuple.Create(min, max);
                    }
                }

                var skippedTypeSet = new HashSet<string>();
                var unmappedLoc = new HashSet<string>();
                var tables = new Dictionary<string, Dictionary<int, Placement>>();
                if (!root.TryGetProperty("placements", out var placements))
                    throw new Exception("JSON has no placements array.");

                foreach (var p in placements.EnumerateArray())
                {
                    string type = p.GetProperty("type").GetString();
                    if (!ImportTypes.Contains(type)) { skippedTypeSet.Add(type); continue; }
                    string locId = p.GetProperty("locationId").GetString();
                    string season = p.GetProperty("season").GetString();
                    int slot = p.GetProperty("slot").GetInt32();
                    int species = p.GetProperty("speciesId").GetInt32();
                    if (!LocationToPoolName.ContainsKey(locId)) { unmappedLoc.Add(locId); continue; }
                    string key = locId + "|" + season + "|" + type;
                    if (!tables.ContainsKey(key)) tables[key] = new Dictionary<int, Placement>();
                    tables[key][slot] = new Placement { SpeciesId = (short)species, Slot = slot };
                }

                result.SkippedTypes = skippedTypeSet.OrderBy(s => s).ToList();
                result.UnmappedLocations = unmappedLoc.OrderBy(s => s).ToList();

                var nameToId = new Dictionary<string, int>();
                for (int i = 0; i < VersionConstants.BW2_RouteEncounterPoolNames.Count; i++)
                    nameToId[VersionConstants.BW2_RouteEncounterPoolNames[i]] = i;

                foreach (var kv in tables)
                {
                    var parts = kv.Key.Split('|');
                    string locId = parts[0], seasonName = parts[1], type = parts[2];
                    int season = SeasonIndex.ContainsKey(seasonName) ? SeasonIndex[seasonName] : 0;
                    string poolName = LocationToPoolName[locId];
                    if (!nameToId.ContainsKey(poolName)) { result.MissingPools.Add(poolName); continue; }
                    EncounterEntry entry = FindOrCreateSeasonEntry(narc, nameToId[poolName], season, result);
                    if (entry == null) continue;
                    string levelKey = locId + ":" + seasonName + ":" + type;
                    byte minLv = 1, maxLv = 1;
                    if (levels.ContainsKey(levelKey)) { minLv = levels[levelKey].Item1; maxLv = levels[levelKey].Item2; }
                    int tableIndex; bool land;
                    if (!TryTableIndex(type, out land, out tableIndex)) continue;
                    EncounterSlot[] dest = land ? entry.landSlots[tableIndex] : entry.waterSlots[tableIndex];
                    foreach (var slotKv in kv.Value)
                    {
                        int slot = slotKv.Key;
                        if (slot < 0 || slot >= dest.Length) { result.Warnings.Add(locId + " " + type + " slot " + slot + " out of range"); continue; }
                        dest[slot] = new EncounterSlot(slotKv.Value.SpeciesId, 0, minLv, maxLv, dest[slot].rate);
                        result.SlotsWritten++;
                    }
                    WriteSlotsToBytes(entry);
                    entry.EncounterSlotsToGroups();
                    result.TablesWritten++;
                }
            }
            return result;
        }

        static void WriteSlotsToBytes(EncounterEntry entry)
        {
            int loc = 8;
            for (int x = 0; x < 3; x++)
            {
                for (int y = 0; y < entry.landSlots[x].Length; y++)
                {
                    int id = entry.landSlots[x][y].pokemonID + entry.landSlots[x][y].pokemonForm * 2048;
                    HelperFunctions.WriteShort(entry.bytes, loc, id);
                    entry.bytes[loc + 2] = entry.landSlots[x][y].minLevel;
                    entry.bytes[loc + 3] = entry.landSlots[x][y].maxLevel;
                    loc += 4;
                }
            }
            for (int x = 0; x < 4; x++)
            {
                for (int y = 0; y < entry.waterSlots[x].Length; y++)
                {
                    int id = entry.waterSlots[x][y].pokemonID + entry.waterSlots[x][y].pokemonForm * 2048;
                    HelperFunctions.WriteShort(entry.bytes, loc, id);
                    entry.bytes[loc + 2] = entry.waterSlots[x][y].minLevel;
                    entry.bytes[loc + 3] = entry.waterSlots[x][y].maxLevel;
                    loc += 4;
                }
            }
        }

        static EncounterEntry FindOrCreateSeasonEntry(EncounterNARC narc, int nameId, int season, Result result)
        {
            var mains = narc.mainEncounterPools.Where(e => e.nameID == nameId).ToList();
            if (mains.Count == 0) { result.MissingPools.Add("nameID " + nameId); return null; }
            EncounterEntry main = mains[0];
            if (season <= 0) return main;
            if (main.season == -1)
            {
                for (int i = 1; i <= 3; i++)
                    narc.subEncounterPools.Add(new EncounterEntry(main.bytes.ToArray()) { nameID = main.nameID, season = i, parentPool = main });
                main.season = 0;
                result.Warnings.Add("Enabled seasons on " + main);
            }
            var sub = narc.subEncounterPools.FirstOrDefault(s => s.parentPool == main && s.season == season);
            if (sub != null) return sub;
            if (main.season == season) return main;
            result.Warnings.Add("No season " + season + " pool for " + main);
            return main;
        }

        static bool TryTableIndex(string type, out bool land, out int index)
        {
            land = true; index = 0;
            switch (type)
            {
                case "grass": case "cave": land = true; index = 0; return true;
                case "darkGrass": land = true; index = 1; return true;
                case "rustling": land = true; index = 2; return true;
                case "surf": land = false; index = 0; return true;
                case "ripples": land = false; index = 1; return true;
                case "fishing": land = false; index = 2; return true;
                case "fishRipples": land = false; index = 3; return true;
                default: return false;
            }
        }

        static byte ClampLevel(int v)
        {
            if (v < 1) return 1;
            if (v > 100) return 100;
            return (byte)v;
        }

        class Placement { public short SpeciesId; public int Slot; }
    }
}
