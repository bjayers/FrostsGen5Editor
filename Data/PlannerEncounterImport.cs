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
                var levels = new Dictionary<string, Tuple<byte, byte>>(StringComparer.OrdinalIgnoreCase);
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

                var nameToId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < VersionConstants.BW2_RouteEncounterPoolNames.Count; i++)
                    nameToId[VersionConstants.BW2_RouteEncounterPoolNames[i]] = i;

                var skippedTypeSet = new HashSet<string>();
                var unmappedLoc = new HashSet<string>();
                var tables = new Dictionary<string, Dictionary<int, short>>();

                if (!root.TryGetProperty("placements", out var placements))
                    throw new Exception("JSON has no placements array.");

                foreach (var p in placements.EnumerateArray())
                {
                    string type = p.GetProperty("type").GetString();
                    if (!ImportTypes.Contains(type))
                    {
                        skippedTypeSet.Add(type);
                        continue;
                    }

                    string locId = p.TryGetProperty("locationId", out var locEl) ? locEl.GetString() : "";
                    string season = p.GetProperty("season").GetString();
                    int slot = p.GetProperty("slot").GetInt32();
                    int species = p.GetProperty("speciesId").GetInt32();

                    int nameId = -1;
                    if (p.TryGetProperty("tableIndex", out var idxEl) && idxEl.ValueKind == JsonValueKind.Number)
                        nameId = idxEl.GetInt32();
                    else if (!string.IsNullOrEmpty(locId) && nameToId.ContainsKey(locId))
                        nameId = nameToId[locId];

                    if (nameId < 0 || nameId >= VersionConstants.BW2_RouteEncounterPoolNames.Count)
                    {
                        unmappedLoc.Add(string.IsNullOrEmpty(locId) ? ("index " + nameId) : locId);
                        continue;
                    }

                    string key = nameId + "|" + season + "|" + type + "|" + locId;
                    if (!tables.ContainsKey(key)) tables[key] = new Dictionary<int, short>();
                    tables[key][slot] = (short)species;
                }

                result.SkippedTypes = skippedTypeSet.OrderBy(s => s).ToList();
                result.UnmappedLocations = unmappedLoc.OrderBy(s => s).ToList();

                foreach (var kv in tables)
                {
                    var parts = kv.Key.Split('|');
                    int nameId = int.Parse(parts[0]);
                    string seasonName = parts[1];
                    string type = parts[2];
                    string locName = parts.Length > 3 ? parts[3] : VersionConstants.BW2_RouteEncounterPoolNames[nameId];
                    int season = SeasonIndex.ContainsKey(seasonName) ? SeasonIndex[seasonName] : 0;

                    EncounterEntry entry = FindOrCreateSeasonEntry(narc, nameId, season, result);
                    if (entry == null) continue;

                    string levelKey = locName + ":" + seasonName + ":" + type;
                    byte minLv = 1, maxLv = 1;
                    if (levels.ContainsKey(levelKey))
                    {
                        minLv = levels[levelKey].Item1;
                        maxLv = levels[levelKey].Item2;
                    }

                    bool land;
                    int destIndex;
                    if (!TryTableIndex(type, out land, out destIndex)) continue;

                    EncounterSlot[] dest = land ? entry.landSlots[destIndex] : entry.waterSlots[destIndex];
                    foreach (var slotKv in kv.Value)
                    {
                        int slot = slotKv.Key;
                        if (slot < 0 || slot >= dest.Length)
                        {
                            result.Warnings.Add(locName + " " + type + " slot " + slot + " out of range");
                            continue;
                        }
                        dest[slot] = new EncounterSlot(slotKv.Value, 0, minLv, maxLv, dest[slot].rate);
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
            if (mains.Count == 0)
            {
                result.MissingPools.Add(VersionConstants.BW2_RouteEncounterPoolNames[nameId] + " (nameID " + nameId + ")");
                return null;
            }
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
            land = true;
            index = 0;
            switch (type)
            {
                case "grass":
                case "cave":
                    land = true; index = 0; return true;
                case "darkGrass":
                    land = true; index = 1; return true;
                case "rustling":
                    land = true; index = 2; return true;
                case "surf":
                    land = false; index = 0; return true;
                case "ripples":
                    land = false; index = 1; return true;
                case "fishing":
                    land = false; index = 2; return true;
                case "fishRipples":
                    land = false; index = 3; return true;
                default:
                    return false;
            }
        }

        static byte ClampLevel(int v)
        {
            if (v < 1) return 1;
            if (v > 100) return 100;
            return (byte)v;
        }
    }
}
