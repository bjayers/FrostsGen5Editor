using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NewEditor.Data
{
    /// <summary>
    /// The Following Pokémon runtime's ROM data, read the way the runtime and
    /// Pokeweb read it. Formats match Pokeweb's followingPokemon*.ts decoders.
    /// </summary>
    public class FollowingData
    {
        public const string RegistryPath = "following/runtime-registry.bin";
        public const string NativePath = "following/native.bin";
        public const string InstallPath = "following/install.json";
        public const string PositioningPath = "following/positioning.narc";
        public const string DialoguePath = "following/contextual-dialogues.narc";
        public const string GiftPath = "following/contextual-items.narc";
        public const int ResourceNarcID = 48; // a/0/4/8
        public const int StockRows = 1008;

        public FollowerRegistry Registry;
        public List<FollowerDialogueRule> Dialogues = new List<FollowerDialogueRule>();
        public List<FollowerGiftRule> Gifts = new List<FollowerGiftRule>();
        public FollowerPositioning Positioning;
        public bool Enabled;
        /// <summary>From Pokeweb's install receipt, when the ROM has one.</summary>
        public string Version, Variant;
        /// <summary>Files that exist but could not be read; the rest still loads.</summary>
        public List<string> Problems = new List<string>();

        NDSFileSystem fileSystem;

        /// <summary>
        /// Read the follower data, or null when the runtime isn't installed: no
        /// runtime registry and native config, or a receipt that says removed.
        /// </summary>
        public static FollowingData Load(NDSFileSystem fs)
        {
            byte[] native = ReadFile(fs, NativePath), registry = ReadFile(fs, RegistryPath);
            if (native == null || registry == null) return null;
            var data = new FollowingData { fileSystem = fs };

            byte[] receipt = ReadFile(fs, InstallPath);
            if (receipt != null)
            {
                try
                {
                    using (JsonDocument json = JsonDocument.Parse(receipt))
                    {
                        JsonElement root = json.RootElement;
                        if (root.TryGetProperty("removed", out JsonElement removed) && removed.ValueKind == JsonValueKind.True) return null;
                        if (root.TryGetProperty("version", out JsonElement version)) data.Version = version.GetString();
                        if (root.TryGetProperty("variant", out JsonElement variant)) data.Variant = variant.GetString();
                    }
                }
                catch (Exception ex) { data.Problems.Add("install.json: " + ex.Message); }
            }

            if (native.Length < 32 || ReadU32(native, 0) != 0x544E5746) data.Problems.Add("native.bin is not a follower config.");
            else data.Enabled = ReadU32(native, 4) == 1;

            try { data.Registry = FollowerRegistry.Decode(registry); }
            catch (Exception ex)
            {
                data.Problems.Add("runtime-registry.bin: " + ex.Message);
                return data;
            }

            byte[] positioning = ReadFile(fs, PositioningPath);
            if (positioning != null)
            {
                try { data.Positioning = FollowerPositioning.Decode(positioning, registry, data.Registry); }
                catch (Exception ex) { data.Problems.Add("positioning.narc: " + ex.Message); }
            }
            byte[] dialogues = ReadFile(fs, DialoguePath);
            if (dialogues != null)
            {
                try { data.Dialogues = FollowerRules.DecodeDialogues(dialogues); }
                catch (Exception ex) { data.Problems.Add("contextual-dialogues.narc: " + ex.Message); }
            }
            byte[] gifts = ReadFile(fs, GiftPath);
            if (gifts != null)
            {
                try { data.Gifts = FollowerRules.DecodeGifts(gifts); }
                catch (Exception ex) { data.Problems.Add("contextual-items.narc: " + ex.Message); }
            }
            return data;
        }

        /// <summary>
        /// Replace the dialogue rules in the loaded ROM. Throws with the
        /// reason when a rule is invalid or the archive would exceed 4 KiB;
        /// nothing is changed then. Save the ROM to keep the result.
        /// </summary>
        public void SaveDialogues(List<FollowerDialogueRule> rules)
        {
            byte[] bytes = FollowerRules.EncodeDialogues(rules);
            WriteFile(DialoguePath, bytes);
            UpdateReceipt("dialoguesSha256", bytes);
            Dialogues = rules.Select(r => r.Copy()).ToList();
        }

        /// <summary>Replace the gift rules in the loaded ROM; see SaveDialogues.</summary>
        public void SaveGifts(List<FollowerGiftRule> rules)
        {
            byte[] bytes = FollowerRules.EncodeGifts(rules);
            WriteFile(GiftPath, bytes);
            UpdateReceipt("itemsSha256", bytes);
            Gifts = rules.Select(r => r.Copy()).ToList();
        }

        void WriteFile(string path, byte[] bytes)
        {
            if (!fileSystem.fnt.reverseFileNames.TryGetValue(path, out int id))
                throw new InvalidOperationException(path + " is missing from this ROM; it can't be added without reinstalling the follower runtime.");
            int index = id - fileSystem.fnt.firstFile;
            if (fileSystem.files[index] is NARC narc) narc.byteData = bytes;
            else fileSystem.files[index] = bytes;
        }

        /// <summary>
        /// Keep Pokeweb's install receipt in step, so its page still accepts the
        /// ROM. Only the one hash changes; a receipt without that field is left.
        /// </summary>
        void UpdateReceipt(string field, byte[] bytes)
        {
            byte[] receipt = ReadFile(fileSystem, InstallPath);
            if (receipt == null) return;
            string text = Encoding.UTF8.GetString(receipt);
            string hash;
            using (SHA256 sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
            var pattern = new Regex("(\"" + field + "\"\\s*:\\s*\")[0-9a-f]*(\")");
            if (!pattern.IsMatch(text)) return;
            WriteFile(InstallPath, Encoding.UTF8.GetBytes(pattern.Replace(text, "${1}" + hash + "${2}", 1)));
        }

        /// <summary>A follower sprite (BTX) from a/0/4/8, or null.</summary>
        public byte[] GetResource(int resourceId)
        {
            if (ResourceNarcID >= fileSystem.narcs.Count) return null;
            List<byte> entry = fileSystem.narcs[ResourceNarcID].GetFileEntry(resourceId);
            return entry == null ? null : entry.ToArray();
        }

        /// <summary>The follow distance the runtime uses: the largest of the row's four gaps.</summary>
        public int FollowDistance(FollowerAppearance appearance)
        {
            int row = appearance.DescriptorRow - StockRows;
            if (Positioning == null || row < 0 || row * 12 + 4 > Positioning.Land.Length) return -1;
            int gap = 0;
            for (int i = 0; i < 4; i++) gap = Math.Max(gap, Positioning.Land[row * 12 + i]);
            return gap;
        }

        public static byte[] ReadFile(NDSFileSystem fs, string path)
        {
            if (fs?.fnt == null || !fs.fnt.reverseFileNames.TryGetValue(path, out int id)) return null;
            int index = id - fs.fnt.firstFile;
            if (index < 0 || index >= fs.files.Count) return null;
            object file = fs.files[index];
            return file is byte[] bytes ? bytes : file is NARC narc ? narc.byteData : null;
        }

        /// <summary>The only member of a single-member NARC.</summary>
        public static byte[] SingleMember(byte[] narcBytes, string what)
        {
            byte[] member = Member(narcBytes, 0, out int count);
            if (count != 1 || member == null) throw new InvalidDataException(what + " archive must have one member.");
            return member;
        }

        public static byte[] Member(byte[] narcBytes, int index, out int count)
        {
            count = 0;
            if (narcBytes == null || narcBytes.Length < 32 || narcBytes[0] != 'N' || narcBytes[1] != 'A' || narcBytes[2] != 'R' || narcBytes[3] != 'C') return null;
            var narc = new NARC { byteData = narcBytes };
            count = narc.numFileEntries;
            List<byte> entry = narc.GetFileEntry(index);
            return entry == null ? null : entry.ToArray();
        }

        /// <summary>A one-member NARC laid out exactly as Pokeweb's NARC.save writes it.</summary>
        public static byte[] PackNarc(byte[] member)
        {
            int padded = (member.Length + 3) & ~3;
            var o = new List<byte>();
            void U16(int v) { o.Add((byte)v); o.Add((byte)(v >> 8)); }
            void U32(int v) { U16(v & 0xFFFF); U16(v >> 16 & 0xFFFF); }
            o.AddRange(Encoding.ASCII.GetBytes("NARC")); U16(0xFEFF); U16(1); U32(0); U16(0x10); U16(3);
            o.AddRange(Encoding.ASCII.GetBytes("BTAF")); U32(0x14); U32(1); U32(0); U32(member.Length);
            o.AddRange(Encoding.ASCII.GetBytes("BTNF")); U32(0x10); o.AddRange(new byte[] { 4, 0, 0, 0, 0, 0, 1, 0 });
            o.AddRange(Encoding.ASCII.GetBytes("GMIF")); U32(8 + padded); o.AddRange(member); o.AddRange(new byte[padded - member.Length]);
            byte[] bytes = o.ToArray();
            bytes[8] = (byte)bytes.Length; bytes[9] = (byte)(bytes.Length >> 8); bytes[10] = (byte)(bytes.Length >> 16); bytes[11] = (byte)(bytes.Length >> 24);
            return bytes;
        }

        public static uint ReadU32(byte[] b, int at) => (uint)(b[at] | b[at + 1] << 8 | b[at + 2] << 16 | b[at + 3] << 24);
        public static int ReadU16(byte[] b, int at) => b[at] | b[at + 1] << 8;
        public static int ReadS16(byte[] b, int at) => (short)(b[at] | b[at + 1] << 8);

        /// <summary>Standard CRC-32, the same as zlib's and Pokeweb's followerCrc32.</summary>
        public static uint Crc32(byte[] b, int start, int length)
        {
            uint crc = 0xFFFFFFFF;
            for (int i = start; i < start + length; i++)
            {
                crc ^= b[i];
                for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xEDB88320u : 0);
            }
            return ~crc;
        }
    }

    public class FollowerAppearance
    {
        public int Species, Form, Gender, DescriptorRow, ResourceId, Size, SideGap;
        public bool Shiny, Asymmetric, Placeholder;
        /// <summary>Draw-only sprite offsets; Y is the per-Pokémon height.</summary>
        public int OffsetX, OffsetY, OffsetZ;
        public string Key => Species + ":" + Form + ":" + Gender + ":" + (Shiny ? 1 : 0);
        public static readonly string[] GenderNames = { "male", "female", "genderless" };
    }

    public class FollowerRegistry
    {
        public int Version, DescriptorCount, ResourceCount;
        public List<FollowerAppearance> Entries = new List<FollowerAppearance>();
        public uint Crc;

        /// <summary>FWDB v1–v4: 24-byte appearances (v1/v3) or packed 12-byte ones (v2/v4).</summary>
        public static FollowerRegistry Decode(byte[] b)
        {
            if (b.Length < 32 || Encoding.ASCII.GetString(b, 0, 4) != "FWDB") throw new InvalidDataException("Not a follower registry.");
            int version = FollowingData.ReadU16(b, 4);
            if (version < 1 || version > 4 || FollowingData.ReadU16(b, 6) != 1) throw new InvalidDataException("Unsupported follower registry version.");
            bool compact = version == 2 || version == 4;
            int stride = compact ? 12 : 24, count = FollowingData.ReadU16(b, 12), zones = FollowingData.ReadU16(b, 14);
            if (FollowingData.ReadU32(b, 8) != b.Length || b.Length != 32 + count * stride + zones * 8 ||
                FollowingData.ReadU16(b, 20) != 0x3000 || FollowingData.ReadU16(b, 22) != FollowingData.StockRows)
                throw new InvalidDataException("Invalid follower registry length or bounds.");
            byte[] check = (byte[])b.Clone();
            check[24] = check[25] = check[26] = check[27] = 0;
            uint crc = FollowingData.Crc32(check, 0, check.Length);
            if (crc != FollowingData.ReadU32(b, 24)) throw new InvalidDataException("Follower registry checksum mismatch.");

            var registry = new FollowerRegistry
            {
                Version = version,
                DescriptorCount = FollowingData.ReadU16(b, 16),
                ResourceCount = FollowingData.ReadU16(b, 18),
                Crc = FollowingData.Crc32(b, 0, b.Length)
            };
            for (int i = 0; i < count; i++)
            {
                int at = 32 + i * stride;
                var e = new FollowerAppearance();
                if (compact)
                {
                    uint packed = FollowingData.ReadU32(b, at);
                    int flags = b[at + 8];
                    e.Species = (int)(packed >> 11); e.Form = (int)(packed >> 3) & 255; e.Gender = (int)(packed >> 1) & 3; e.Shiny = (packed & 1) != 0;
                    e.DescriptorRow = FollowingData.ReadU16(b, at + 4); e.ResourceId = FollowingData.ReadU16(b, at + 6);
                    e.Size = (flags & 2) != 0 ? 64 : 32; e.Asymmetric = (flags & 4) != 0; e.Placeholder = (flags & 1) != 0;
                    e.SideGap = version == 4 ? flags >> 3 & 7 : 0;
                    e.OffsetX = (sbyte)b[at + 9]; e.OffsetY = (sbyte)b[at + 10]; e.OffsetZ = (sbyte)b[at + 11];
                }
                else
                {
                    e.Species = FollowingData.ReadU16(b, at); e.Form = b[at + 2]; e.Gender = b[at + 3]; e.Shiny = b[at + 4] != 0;
                    e.Placeholder = b[at + 5] != 0; e.DescriptorRow = FollowingData.ReadU16(b, at + 6); e.ResourceId = FollowingData.ReadU16(b, at + 8);
                    e.Size = b[at + 10]; e.Asymmetric = b[at + 11] != 0;
                    e.OffsetX = (sbyte)b[at + 12]; e.OffsetY = (sbyte)b[at + 13]; e.OffsetZ = (sbyte)b[at + 14];
                    e.SideGap = version == 3 ? b[at + 15] : 0;
                }
                registry.Entries.Add(e);
            }
            return registry;
        }
    }

    public class FollowerPositioning
    {
        /// <summary>Member 0 land rows: 12 bytes per follower descriptor row (4 gaps, 8 rider bytes).</summary>
        public byte[] Land;
        /// <summary>Member 1 zone overrides (FWZO); flag bit 0 swaps the indoor/outdoor rule.</summary>
        public List<(int zone, int flags)> ZoneOverrides = new List<(int, int)>();

        public static FollowerPositioning Decode(byte[] narcBytes, byte[] registryBytes, FollowerRegistry registry)
        {
            byte[] m = FollowingData.Member(narcBytes, 0, out int count);
            if (m == null || m.Length < 24 || FollowingData.ReadU32(m, 0) != 0x4F505746 || FollowingData.ReadU16(m, 4) != 1 || m[10] != 12 || m[11] != 8)
                throw new InvalidDataException("Not a follower positioning archive.");
            int land = FollowingData.ReadU16(m, 6), surf = FollowingData.ReadU16(m, 8);
            if (FollowingData.ReadU32(m, 12) != FollowingData.Crc32(registryBytes, 0, registryBytes.Length))
                throw new InvalidDataException("Positioning does not match the installed follower registry.");
            if (land != registry.DescriptorCount - FollowingData.StockRows || m.Length != 24 + land * 12 + surf * 8 ||
                FollowingData.ReadU32(m, 20) != FollowingData.Crc32(m, 24, m.Length - 24))
                throw new InvalidDataException("Positioning rows are incomplete or damaged.");
            var result = new FollowerPositioning { Land = new byte[land * 12] };
            Array.Copy(m, 24, result.Land, 0, result.Land.Length);

            if (count > 1)
            {
                byte[] zones = FollowingData.Member(narcBytes, 1, out _);
                if (zones != null && zones.Length >= 8 && FollowingData.ReadU32(zones, 0) == 0x4F5A5746 && FollowingData.ReadU16(zones, 4) == 1)
                {
                    int rows = FollowingData.ReadU16(zones, 6);
                    for (int i = 0; i < rows && 8 + i * 4 + 4 <= zones.Length; i++)
                        result.ZoneOverrides.Add((FollowingData.ReadU16(zones, 8 + i * 4), zones[8 + i * 4 + 2]));
                }
            }
            return result;
        }
    }

    /// <summary>Optional inclusive player tile bounds, in matrix tile coordinates.</summary>
    public class FollowerRuleConditions
    {
        public int Zone;
        public int? Species, Form, Type, Hp, Friendship, Status, Facing;
        public int? MinX, MaxX, MinY, MaxY;

        public bool HasBounds => MinX != null || MaxX != null || MinY != null || MaxY != null;

        public bool AppliesTo(int species, ICollection<int> speciesTypes) =>
            (Species == null || Species == species) && (Type == null || speciesTypes.Contains(Type.Value));
    }

    public class FollowerDialogueRule : FollowerRuleConditions
    {
        public int Chance = 100;
        public int? BeforeAnimation, AfterAnimation;
        public string Text = "";
        public FollowerDialogueRule Copy() => (FollowerDialogueRule)MemberwiseClone();
    }

    public class FollowerGiftRule : FollowerRuleConditions
    {
        public int Slot, ItemId = 1, Quantity = 1;
        public string ItemName = "", Text = "";
        public FollowerGiftRule Copy() => (FollowerGiftRule)MemberwiseClone();
    }

    public static class FollowerRules
    {
        // IDs match the twelve motions in the runtime's English FWTK table.
        public static readonly string[] Animations = { "None", "Cry", "Hop", "Shake side to side", "Rock forward/back", "Look left/right",
            "Shuffle right", "Shuffle up", "Shuffle down", "Look around (left)", "Look around (right)", "Look around (up)", "Look around (down)" };
        public static readonly string[] HpConditions = { "Any", "Full HP", "75–99% HP", "50–74% HP", "25–49% HP", "Below 25% HP" };
        public static readonly string[] FriendshipConditions = { "Any", "Maximum (255)", "Very high (200–254)", "High (150–199)", "Friendly (90–149)",
            "Neutral (60–89)", "Low (30–59)", "Very low (1–29)", "Minimum (0)", "90 or higher", "Below 60" };
        // Status values 1–5, 7 and 8; 6 is unused.
        public static readonly string[] StatusConditions = { "Any", "Healthy", "Burned", "Frozen", "Paralyzed", "Poisoned", "", "Any status condition", "Asleep" };
        public static readonly string[] FacingConditions = { "Any", "Facing right", "Facing left", "Facing up", "Facing down" };

        /// <summary>FWCD v1–v3: 20-byte rules, or 28 with tile bounds (v3).</summary>
        public static List<FollowerDialogueRule> DecodeDialogues(byte[] narcBytes)
        {
            if (narcBytes.Length > 4096) throw new InvalidDataException("Archive exceeds the 4 KiB runtime limit.");
            byte[] d = FollowingData.SingleMember(narcBytes, "Dialogue");
            int abi = CheckHeader(d, 0x44435746, 3);
            int size = abi == 3 ? 28 : 20, count = FollowingData.ReadU16(d, 6);
            if (16 + count * size > d.Length) throw new InvalidDataException("Invalid dialogue rule count.");
            var rules = new List<FollowerDialogueRule>();
            for (int i = 0; i < count; i++)
            {
                int at = 16 + i * size;
                var r = new FollowerDialogueRule();
                ReadConditions(d, at, r, at + 4, at + 5, at + 6);
                r.Chance = d[at + 10];
                if (d[at + 11] != 0) r.BeforeAnimation = d[at + 11];
                if (d[at + 18] != 0) r.AfterAnimation = d[at + 18];
                r.Text = ReadText(d, (int)FollowingData.ReadU32(d, at + 12), FollowingData.ReadU16(d, at + 16), 16 + count * size, false);
                if (abi == 3) ReadBounds(d, at + 20, d[at + 19], r);
                rules.Add(r);
            }
            return rules;
        }

        /// <summary>FWTI v1–v2: 28-byte rules, or 36 with tile bounds (v2).</summary>
        public static List<FollowerGiftRule> DecodeGifts(byte[] narcBytes)
        {
            if (narcBytes.Length > 4096) throw new InvalidDataException("Archive exceeds the 4 KiB runtime limit.");
            byte[] d = FollowingData.SingleMember(narcBytes, "Gift");
            int abi = CheckHeader(d, 0x49545746, 2);
            int size = abi == 2 ? 36 : 28, count = FollowingData.ReadU16(d, 6), floor = 16 + count * size;
            if (floor > d.Length) throw new InvalidDataException("Invalid gift rule count.");
            var rules = new List<FollowerGiftRule>();
            for (int i = 0; i < count; i++)
            {
                int at = 16 + i * size;
                var r = new FollowerGiftRule { Slot = d[at + 6], ItemId = FollowingData.ReadU16(d, at + 4), Quantity = d[at + 13] };
                ReadConditions(d, at, r, at + 7, at + 8, at + 9);
                r.ItemName = ReadText(d, (int)FollowingData.ReadU32(d, at + 14), FollowingData.ReadU16(d, at + 18), floor, false);
                r.Text = ReadText(d, (int)FollowingData.ReadU32(d, at + 20), FollowingData.ReadU16(d, at + 24), floor, true);
                if (abi == 2) ReadBounds(d, at + 28, FollowingData.ReadU16(d, at + 26), r);
                rules.Add(r);
            }
            return rules;
        }

        public const int MaxBytes = 4096;

        /// <summary>
        /// Pokeweb's encodeFollowerDialogueNarc: rules without tile bounds keep
        /// the older 20-byte layout (v1, or v2 with animations).
        /// </summary>
        public static byte[] EncodeDialogues(List<FollowerDialogueRule> rules)
        {
            var texts = new List<int[]>();
            for (int i = 0; i < rules.Count; i++)
            {
                var r = rules[i];
                string where = "Dialogue rule " + (i + 1) + ": ";
                CheckConditions(r, where);
                Check(r.Chance, 1, 100, where + "chance");
                if (r.BeforeAnimation != null) Check(r.BeforeAnimation.Value, 1, 12, where + "before animation");
                if (r.AfterAnimation != null) Check(r.AfterAnimation.Value, 1, 12, where + "after animation");
                texts.Add(EncodeText(r.Text, false, where + "dialogue text"));
            }
            bool bounded = rules.Any(r => r.HasBounds);
            int size = bounded ? 28 : 20, abi = bounded ? 3 : rules.Any(r => r.BeforeAnimation != null || r.AfterAnimation != null) ? 2 : 1;
            int length = 16 + rules.Count * size + texts.Sum(t => t.Length * 2);
            var d = new byte[length];
            WriteHeader(d, 0x44435746, abi, rules.Count);
            int cursor = 16 + rules.Count * size;
            for (int i = 0; i < rules.Count; i++)
            {
                var r = rules[i];
                int at = 16 + i * size;
                WriteConditions(d, at, r, at + 4, at + 5, at + 6);
                d[at + 10] = (byte)r.Chance; d[at + 11] = (byte)(r.BeforeAnimation ?? 0);
                W32(d, at + 12, cursor); W16(d, at + 16, texts[i].Length); d[at + 18] = (byte)(r.AfterAnimation ?? 0);
                if (bounded) d[at + 19] = (byte)WriteBounds(d, at + 20, r);
                cursor = WriteText(d, cursor, texts[i]);
            }
            return Finish(d, "Conditional dialogue");
        }

        /// <summary>Pokeweb's encodeFollowerItemNarc: 28-byte rules, or 36 with tile bounds (v2).</summary>
        public static byte[] EncodeGifts(List<FollowerGiftRule> rules)
        {
            var names = new List<int[]>();
            var texts = new List<int[]>();
            for (int i = 0; i < rules.Count; i++)
            {
                var r = rules[i];
                string where = "Gift rule " + (i + 1) + ": ";
                Check(r.Slot, 0, 9, where + "claim slot");
                Check(r.ItemId, 1, 0xFFFE, where + "item");
                Check(r.Quantity, 1, 99, where + "quantity");
                CheckConditions(r, where);
                names.Add(EncodeText(r.ItemName, false, where + "item name"));
                texts.Add(EncodeText(r.Text, true, where + "success message"));
            }
            bool bounded = rules.Any(r => r.HasBounds);
            int size = bounded ? 36 : 28;
            int length = 16 + rules.Count * size + names.Sum(t => t.Length * 2) + texts.Sum(t => t.Length * 2);
            var d = new byte[length];
            WriteHeader(d, 0x49545746, bounded ? 2 : 1, rules.Count);
            int cursor = 16 + rules.Count * size;
            for (int i = 0; i < rules.Count; i++)
            {
                var r = rules[i];
                int at = 16 + i * size;
                WriteConditions(d, at, r, at + 7, at + 8, at + 9);
                W16(d, at + 4, r.ItemId); d[at + 6] = (byte)r.Slot; d[at + 13] = (byte)r.Quantity;
                W32(d, at + 14, cursor); W16(d, at + 18, names[i].Length); cursor = WriteText(d, cursor, names[i]);
                W32(d, at + 20, cursor); W16(d, at + 24, texts[i].Length);
                W16(d, at + 26, bounded ? WriteBounds(d, at + 28, r) : 0);
                cursor = WriteText(d, cursor, texts[i]);
            }
            return Finish(d, "Follower gift");
        }

        /// <summary>what is "Rule N: field"; the message reads "Rule N: invalid field; use min to max."</summary>
        static void Check(int value, int min, int max, string what)
        {
            if (value >= min && value <= max) return;
            int split = what.IndexOf(": ");
            string where = split < 0 ? "" : what.Substring(0, split + 2), field = split < 0 ? what : what.Substring(split + 2);
            throw new ArgumentException(where + "invalid " + field + "; use " + min + " to " + max + ".");
        }

        static void CheckConditions(FollowerRuleConditions r, string where)
        {
            Check(r.Zone, 0, 0xFFFF, where + "zone");
            if (r.Species != null) Check(r.Species.Value, 1, 1023, where + "species");
            if (r.Form != null) Check(r.Form.Value, 0, 255, where + "form");
            if (r.Form != null && r.Species == null) throw new ArgumentException(where + "a form needs a species.");
            if (r.Type != null) Check(r.Type.Value, 0, 31, where + "type");
            if (r.Hp != null) Check(r.Hp.Value, 1, 5, where + "HP condition");
            if (r.Friendship != null) Check(r.Friendship.Value, 0, 10, where + "friendship condition");
            if (r.Status != null) Check(r.Status.Value, 0, 8, where + "status condition");
            if (r.Facing != null) Check(r.Facing.Value, 0, 4, where + "direction condition");
            foreach (int? v in new[] { r.MinX, r.MaxX, r.MinY, r.MaxY })
                if (v != null) Check(v.Value, -32768, 32767, where + "tile bound");
            if (r.MinX != null && r.MaxX != null && r.MinX > r.MaxX) throw new ArgumentException(where + "Min X must not exceed Max X.");
            if (r.MinY != null && r.MaxY != null && r.MinY > r.MaxY) throw new ArgumentException(where + "Min Y must not exceed Max Y.");
        }

        /// <summary>Text to UTF-16 words with tokens, newline 0xFFFE, ending 0xFFFF; at most 191 words.</summary>
        static int[] EncodeText(string text, bool allowItem, string what)
        {
            if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException(what + " is empty.");
            text = text.Replace("\r\n", "\n");
            string[] tokens = allowItem ? new[] { "{nickname}", "{player}", "{location}", "{item}" } : new[] { "{nickname}", "{player}", "{location}" };
            var words = new List<int>();
            for (int at = 0; at < text.Length;)
            {
                int token = Array.FindIndex(tokens, t => string.CompareOrdinal(text, at, t, 0, t.Length) == 0);
                if (token >= 0) { words.Add(0xFFF0 + token); at += tokens[token].Length; continue; }
                char c = text[at];
                if (char.IsSurrogate(c) || c == 0 || c == 0xFFFF) throw new ArgumentException(what + " contains an unsupported character.");
                words.Add(c == '\n' ? 0xFFFE : c);
                at++;
            }
            if (words.Count + 1 > 192) throw new ArgumentException(what + " exceeds 191 characters.");
            words.Add(0xFFFF);
            return words.ToArray();
        }

        static void WriteHeader(byte[] d, uint magic, int abi, int count)
        {
            W32(d, 0, (int)magic); W16(d, 4, abi); W16(d, 6, count); W32(d, 8, d.Length);
        }

        static void WriteConditions(byte[] d, int at, FollowerRuleConditions r, int formAt, int typeAt, int hpAt)
        {
            W16(d, at, r.Zone); W16(d, at + 2, r.Species ?? 0);
            d[formAt] = (byte)(r.Form ?? 255); d[typeAt] = (byte)(r.Type ?? 255);
            d[hpAt] = (byte)(r.Hp ?? 0); d[hpAt + 1] = (byte)(r.Friendship ?? 0); d[hpAt + 2] = (byte)(r.Status ?? 0); d[hpAt + 3] = (byte)(r.Facing ?? 0);
        }

        static int WriteBounds(byte[] d, int at, FollowerRuleConditions r)
        {
            int mask = 0;
            int?[] values = { r.MinX, r.MaxX, r.MinY, r.MaxY };
            for (int i = 0; i < 4; i++)
            {
                if (values[i] != null) mask |= 1 << i;
                W16(d, at + i * 2, values[i] ?? 0);
            }
            return mask;
        }

        static int WriteText(byte[] d, int at, int[] words)
        {
            foreach (int w in words) { W16(d, at, w); at += 2; }
            return at;
        }

        static byte[] Finish(byte[] d, string what)
        {
            W32(d, 12, (int)FollowingData.Crc32(d, 16, d.Length - 16));
            byte[] narc = FollowingData.PackNarc(d);
            if (narc.Length > MaxBytes) throw new ArgumentException(what + " archive is " + narc.Length + " bytes; the runtime limit is " + MaxBytes + ".");
            return narc;
        }

        static void W16(byte[] d, int at, int v) { d[at] = (byte)v; d[at + 1] = (byte)(v >> 8); }
        static void W32(byte[] d, int at, int v) { W16(d, at, v & 0xFFFF); W16(d, at + 2, (v >> 16) & 0xFFFF); }

        static int CheckHeader(byte[] d, uint magic, int maxAbi)
        {
            if (d.Length < 16 || FollowingData.ReadU32(d, 0) != magic) throw new InvalidDataException("Unrecognized archive contents.");
            int abi = FollowingData.ReadU16(d, 4);
            if (abi < 1 || abi > maxAbi || FollowingData.ReadU32(d, 8) != d.Length || FollowingData.ReadU32(d, 12) != FollowingData.Crc32(d, 16, d.Length - 16))
                throw new InvalidDataException("Archive version, size or checksum is invalid.");
            return abi;
        }

        // Zone and species share the first four bytes; form, type and the four
        // conditions sit at rule-specific offsets (HP, friendship, status, facing follow hp).
        static void ReadConditions(byte[] d, int at, FollowerRuleConditions r, int formAt, int typeAt, int hpAt)
        {
            r.Zone = FollowingData.ReadU16(d, at);
            int species = FollowingData.ReadU16(d, at + 2);
            if (species != 0) r.Species = species;
            if (d[formAt] != 255) r.Form = d[formAt];
            if (d[typeAt] != 255) r.Type = d[typeAt];
            if (d[hpAt] != 0) r.Hp = d[hpAt];
            if (d[hpAt + 1] != 0) r.Friendship = d[hpAt + 1];
            if (d[hpAt + 2] != 0) r.Status = d[hpAt + 2];
            if (d[hpAt + 3] != 0) r.Facing = d[hpAt + 3];
        }

        static void ReadBounds(byte[] d, int at, int mask, FollowerRuleConditions r)
        {
            if ((mask & 1) != 0) r.MinX = FollowingData.ReadS16(d, at);
            if ((mask & 2) != 0) r.MaxX = FollowingData.ReadS16(d, at + 2);
            if ((mask & 4) != 0) r.MinY = FollowingData.ReadS16(d, at + 4);
            if ((mask & 8) != 0) r.MaxY = FollowingData.ReadS16(d, at + 6);
        }

        /// <summary>UTF-16 words ending in 0xFFFF; 0xFFFE is a newline, 0xFFF0–0xFFF3 are tokens.</summary>
        static string ReadText(byte[] d, int at, int words, int floor, bool allowItem)
        {
            if (words < 1 || words > 192 || at % 2 != 0 || at < floor || at + words * 2 > d.Length || FollowingData.ReadU16(d, at + (words - 1) * 2) != 0xFFFF)
                throw new InvalidDataException("Invalid rule text reference.");
            var text = new StringBuilder();
            for (int i = 0; i < words - 1; i++)
            {
                int w = FollowingData.ReadU16(d, at + i * 2);
                if (w == 0xFFFE) text.Append('\n');
                else if (w == 0xFFF0) text.Append("{nickname}");
                else if (w == 0xFFF1) text.Append("{player}");
                else if (w == 0xFFF2) text.Append("{location}");
                else if (w == 0xFFF3 && allowItem) text.Append("{item}");
                else text.Append((char)w);
            }
            return text.ToString();
        }
    }

    /// <summary>
    /// Follower sprite frames: square 4-bit textures with one palette, stored
    /// linearly. Asymmetric sheets have 8 frames (up, down, left, right × idle,
    /// step); mirrored ones have 6 and draw right as a flipped left.
    /// </summary>
    public static class FollowerSprites
    {
        public static readonly string[] Directions = { "up", "down", "left", "right" };

        /// <summary>Pokeweb's followerAnimationFrame: 20-tick walk cycles.</summary>
        public static Bitmap Frame(byte[] btx, bool asymmetric, int direction, int tick, bool walking)
        {
            int phase = walking ? tick % 20 : 0;
            int pose = direction == 1 ? (phase >= 5 && phase < 15 ? 1 : 0) : (phase >= 10 ? 1 : 0);
            bool mirror = direction == 3 && !asymmetric;
            return Decode(btx, (mirror ? 2 : direction) * 2 + pose, mirror);
        }

        public static Bitmap Decode(byte[] b, int textureIndex, bool mirror)
        {
            if (b == null || b.Length < 0x20 || Encoding.ASCII.GetString(b, 0, 4) != "BTX0") return null;
            int blocks = FollowingData.ReadU16(b, 0x0E), tex0 = -1;
            for (int i = 0; i < blocks && 0x10 + i * 4 + 4 <= b.Length; i++)
            {
                int offset = (int)FollowingData.ReadU32(b, 0x10 + i * 4);
                if (offset + 0x3C <= b.Length && Encoding.ASCII.GetString(b, offset, 4) == "TEX0") { tex0 = offset; break; }
            }
            if (tex0 < 0) return null;
            int textureData = tex0 + (int)FollowingData.ReadU32(b, tex0 + 0x14);
            int info = tex0 + FollowingData.ReadU16(b, tex0 + 0x0E);
            int paletteData = tex0 + (int)FollowingData.ReadU32(b, tex0 + 0x38);
            if (info + 8 > b.Length || textureIndex >= b[info + 1]) return null;
            int props = info + 4 + FollowingData.ReadU16(b, info + 6) + textureIndex * 8;
            if (props + 4 > b.Length) return null;
            int image = textureData + FollowingData.ReadU16(b, props) * 8, param = FollowingData.ReadU16(b, props + 2);
            int width = 8 << ((param >> 4) & 7), height = 8 << ((param >> 7) & 7), format = (param >> 10) & 7;
            bool transparent0 = ((param >> 13) & 1) != 0;
            if (format != 3 || image + width * height / 2 > b.Length || paletteData + 32 > b.Length) return null;

            var palette = new int[16];
            for (int i = 0; i < 16; i++)
            {
                int c = FollowingData.ReadU16(b, paletteData + i * 2);
                int r = (c & 31) << 3 | (c & 31) >> 2, g = (c >> 5 & 31) << 3 | (c >> 5 & 31) >> 2, bl = (c >> 10 & 31) << 3 | (c >> 10 & 31) >> 2;
                palette[i] = (transparent0 && i == 0 ? 0 : 0xFF << 24) | r << 16 | g << 8 | bl;
            }
            var pixels = new int[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int value = b[image + (y * width + x) / 2];
                    int index = (x & 1) == 0 ? value & 15 : value >> 4;
                    pixels[y * width + (mirror ? width - 1 - x : x)] = palette[index];
                }
            var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            BitmapData locked = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            for (int y = 0; y < height; y++) Marshal.Copy(pixels, y * width, locked.Scan0 + y * locked.Stride, width);
            bitmap.UnlockBits(locked);
            return bitmap;
        }
    }
}
