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

        static readonly string[] SeasonNames = { "spring", "summer", "autumn", "winter" };
        static readonly int[] LandRates = { 20, 20, 10, 10, 10, 10, 5, 5, 4, 4, 1, 1 };
        static readonly int[] SurfRates = { 60, 30, 5, 4, 1 };
        static readonly int[] FishRates = { 40, 40, 15, 4, 1 };

        struct SlotWrite
        {
            public short Species;
            public int Form;
            public byte MinLevel;
            public byte MaxLevel;
        }

        public class Result
        {
            public int TablesWritten;
            public int SlotsWritten;
            public List<string> SkippedTypes = new List<string>();
            public List<string> UnmappedLocations = new List<string>();
            public List<string> MissingPools = new List<string>();
            public List<string> Warnings = new List<string>();
        }

        public class ExportResult
        {
            public string Json;
            public int TablesWritten;
            public int SlotsWritten;
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
                var tables = new Dictionary<string, Dictionary<int, SlotWrite>>();

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
                    int form = 0;
                    if (p.TryGetProperty("form", out var formEl) && formEl.ValueKind == JsonValueKind.Number)
                        form = formEl.GetInt32();
                    if (form < 0) form = 0;

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

                    string levelKey = locId + ":" + season + ":" + type;
                    byte minLv = 1, maxLv = 1;
                    if (levels.ContainsKey(levelKey))
                    {
                        minLv = levels[levelKey].Item1;
                        maxLv = levels[levelKey].Item2;
                    }
                    if (p.TryGetProperty("minLevel", out var pMin) && pMin.ValueKind == JsonValueKind.Number)
                        minLv = ClampLevel(pMin.GetInt32());
                    if (p.TryGetProperty("maxLevel", out var pMax) && pMax.ValueKind == JsonValueKind.Number)
                        maxLv = ClampLevel(pMax.GetInt32());
                    if (maxLv < minLv) maxLv = minLv;

                    string key = nameId + "|" + season + "|" + type + "|" + locId;
                    if (!tables.ContainsKey(key)) tables[key] = new Dictionary<int, SlotWrite>();
                    tables[key][slot] = new SlotWrite
                    {
                        Species = (short)species,
                        Form = form,
                        MinLevel = minLv,
                        MaxLevel = maxLv
                    };
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
                        SlotWrite w = slotKv.Value;
                        dest[slot] = new EncounterSlot(w.Species, w.Form, w.MinLevel, w.MaxLevel, dest[slot].rate);
                        result.SlotsWritten++;
                    }

                    WriteSlotsToBytes(entry);
                    entry.EncounterSlotsToGroups();
                    result.TablesWritten++;
                }
            }

            return result;
        }

        public static ExportResult Export(EncounterNARC narc)
        {
            var result = new ExportResult();
            var placements = new List<Dictionary<string, object>>();
            var levels = new Dictionary<string, object>();
            var linked = new Dictionary<string, bool>();
            int placeId = 1;

            var names = VersionConstants.BW2_RouteEncounterPoolNames;
            var locationList = new List<Dictionary<string, object>>();
            for (int i = 0; i < names.Count; i++)
                locationList.Add(new Dictionary<string, object> { { "tableIndex", i }, { "name", names[i] } });

            foreach (EncounterEntry main in narc.mainEncounterPools)
            {
                if (main.nameID < 0 || main.nameID >= names.Count) continue;
                string locName = names[main.nameID];
                bool seasonal = main.season != -1;
                linked[locName] = !seasonal;

                if (!seasonal)
                {
                    // One year-round table. Emit all four season labels from the SAME bytes
                    // without duplicating via IndexOf (that always returned spring).
                    for (int s = 0; s < 4; s++)
                        WriteAllTypes(main, locName, main.nameID, SeasonNames[s], placements, levels, ref placeId, result);
                }
                else
                {
                    WriteAllTypes(main, locName, main.nameID, SeasonName(main.season), placements, levels, ref placeId, result);
                    foreach (var sub in narc.subEncounterPools.Where(e => e.parentPool == main))
                        WriteAllTypes(sub, locName, main.nameID, SeasonName(sub.season), placements, levels, ref placeId, result);
                }
            }

            var root = new Dictionary<string, object>
            {
                { "version", 4 },
                { "name", "White 2 ROM dump" },
                { "savedAt", DateTime.UtcNow.ToString("o") },
                { "source", "FrostsGen5Editor EncounterNARC dump v4" },
                { "evolutionOnly", new int[0] },
                { "staticOnly", new int[0] },
                { "placements", placements },
                { "levels", levels },
                { "linkedSeasons", linked },
                { "hiddenLocations", new string[0] },
                { "notes", new Dictionary<string, string>
                    {
                        { "_export", "v4: per-slot minLevel/maxLevel and form. Year-round pools emit spring-winter from one table. Empty tables omitted." }
                    }
                },
                { "locations", locationList }
            };

            result.Json = JsonSerializer.Serialize(root, new JsonSerializerOptions { WriteIndented = true });
            return result;
        }

        static void WriteAllTypes(EncounterEntry entry, string locName, int nameId, string season,
            List<Dictionary<string, object>> placements, Dictionary<string, object> levels, ref int placeId, ExportResult result)
        {
            WriteLand(entry, 0, WalkingType(locName), locName, nameId, season, placements, levels, ref placeId, result);
            WriteLand(entry, 1, "darkGrass", locName, nameId, season, placements, levels, ref placeId, result);
            WriteLand(entry, 2, "rustling", locName, nameId, season, placements, levels, ref placeId, result);
            WriteWater(entry, 0, "surf", locName, nameId, season, placements, levels, ref placeId, result);
            WriteWater(entry, 1, "ripples", locName, nameId, season, placements, levels, ref placeId, result);
            WriteWater(entry, 2, "fishing", locName, nameId, season, placements, levels, ref placeId, result);
            WriteWater(entry, 3, "fishRipples", locName, nameId, season, placements, levels, ref placeId, result);
        }

        static string SeasonName(int season)
        {
            if (season < 0 || season > 3) return "spring";
            return SeasonNames[season];
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

        static string WalkingType(string locName)
        {
            string n = locName.ToLowerInvariant();
            string[] caveHints = {
                "cave","castle","sewer","ruins","tower","passage","room","laboratory","house",
                "wellspring","chargestone","twist","relic","clay","underground","guidance",
                "celestial","strange","victory road "
            };
            foreach (string h in caveHints)
                if (n.Contains(h)) return "cave";
            if (n.StartsWith("victory road") && n != "victory road") return "cave";
            return "grass";
        }

        static bool TableEmpty(EncounterSlot[] slots)
        {
            if (slots == null) return true;
            for (int i = 0; i < slots.Length; i++)
                if (slots[i] != null && slots[i].pokemonID > 0) return false;
            return true;
        }

        static void WriteLand(EncounterEntry entry, int table, string type, string locName, int nameId, string season,
            List<Dictionary<string, object>> placements, Dictionary<string, object> levels, ref int placeId, ExportResult result)
        {
            EncounterSlot[] slots = entry.landSlots[table];
            if (TableEmpty(slots)) return;
            int min = 100, max = 1;
            for (int i = 0; i < slots.Length; i++)
            {
                var sl = slots[i];
                int rate = sl.rate > 0 ? sl.rate : (i < LandRates.Length ? LandRates[i] : 0);
                byte slotMin = sl.minLevel > 0 ? sl.minLevel : (byte)1;
                byte slotMax = sl.maxLevel > 0 ? sl.maxLevel : slotMin;
                if (slotMax < slotMin) slotMax = slotMin;
                placements.Add(new Dictionary<string, object>
                {
                    { "id", "rom-" + placeId + "-" + season.Substring(0, 2) },
                    { "speciesId", (int)sl.pokemonID },
                    { "locationId", locName },
                    { "tableIndex", nameId },
                    { "season", season },
                    { "type", type },
                    { "slot", i },
                    { "rate", rate },
                    { "minLevel", (int)slotMin },
                    { "maxLevel", (int)slotMax },
                    { "form", sl.pokemonForm }
                });
                placeId++;
                result.SlotsWritten++;
                if (sl.pokemonID > 0)
                {
                    if (slotMin < min) min = slotMin;
                    if (slotMax > max) max = slotMax;
                }
            }
            if (min == 100) min = 1;
            if (max < min) max = min;
            levels[locName + ":" + season + ":" + type] = new Dictionary<string, int> { { "min", min }, { "max", max } };
            result.TablesWritten++;
        }

        static void WriteWater(EncounterEntry entry, int table, string type, string locName, int nameId, string season,
            List<Dictionary<string, object>> placements, Dictionary<string, object> levels, ref int placeId, ExportResult result)
        {
            EncounterSlot[] slots = entry.waterSlots[table];
            if (TableEmpty(slots)) return;
            int[] defaults = (type == "fishing" || type == "fishRipples") ? FishRates : SurfRates;
            int min = 100, max = 1;
            for (int i = 0; i < slots.Length; i++)
            {
                var sl = slots[i];
                int rate = sl.rate > 0 ? sl.rate : (i < defaults.Length ? defaults[i] : 0);
                byte slotMin = sl.minLevel > 0 ? sl.minLevel : (byte)1;
                byte slotMax = sl.maxLevel > 0 ? sl.maxLevel : slotMin;
                if (slotMax < slotMin) slotMax = slotMin;
                placements.Add(new Dictionary<string, object>
                {
                    { "id", "rom-" + placeId + "-" + season.Substring(0, 2) },
                    { "speciesId", (int)sl.pokemonID },
                    { "locationId", locName },
                    { "tableIndex", nameId },
                    { "season", season },
                    { "type", type },
                    { "slot", i },
                    { "rate", rate },
                    { "minLevel", (int)slotMin },
                    { "maxLevel", (int)slotMax },
                    { "form", sl.pokemonForm }
                });
                placeId++;
                result.SlotsWritten++;
                if (sl.pokemonID > 0)
                {
                    if (slotMin < min) min = slotMin;
                    if (slotMax > max) max = slotMax;
                }
            }
            if (min == 100) min = 1;
            if (max < min) max = min;
            levels[locName + ":" + season + ":" + type] = new Dictionary<string, int> { { "min", min }, { "max", max } };
            result.TablesWritten++;
        }
    }
}
