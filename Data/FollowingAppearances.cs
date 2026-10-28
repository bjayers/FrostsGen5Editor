using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace NewEditor.Data
{
    /// <summary>A replacement sprite waiting for the next appearance save.</summary>
    public class PendingSprite
    {
        public byte[] Btx;
        public bool Asymmetric;
        public bool Placeholder;
    }

    public partial class FollowingData
    {
        public const string LandAnchorsPath = "following/land-anchors.bin";
        public const int DescriptorNarcID = 47; // a/0/4/7
        public const int RetailResources = 975;
        public const int MaxFollowDistance = 12;

        /// <summary>
        /// Save appearance edits into the loaded ROM: heights, follow distances
        /// (all four gaps of a positioning row) and replacement sprites. The
        /// registry, its checksums in native.bin and positioning.narc, the
        /// descriptor rows and the sprite archive change together, as Pokeweb's
        /// install writes them. Throws with the reason, changing nothing, when
        /// the result would not load. Save the ROM to keep it.
        /// </summary>
        public void SaveAppearances(List<FollowerAppearance> draft, byte[] land, Dictionary<string, PendingSprite> imports)
        {
            if (Registry == null || RegistryBytes == null) throw new InvalidOperationException("The follower registry could not be read, so appearances can't be saved.");
            if (ReadFile(fileSystem, LandAnchorsPath) != null)
                throw new InvalidOperationException("This ROM has the riding package, whose rider positions are tied to the registry. Appearance changes can't be saved for it here.");
            if (draft.Count != Registry.Entries.Count) throw new InvalidOperationException("The appearance list no longer matches the registry.");
            NARC resourceNarc = fileSystem.narcs[ResourceNarcID], descriptorNarc = fileSystem.narcs[DescriptorNarcID];
            draft = draft.Select(e => e.Copy()).ToList();

            // Sprites: identical art shares one member; otherwise an appearance's own
            // earlier import is replaced in place, and anything else is appended.
            byte[] resourceBytes = resourceNarc.byteData;
            int resourceCount = Registry.ResourceCount;
            if (imports.Count > 0)
            {
                List<byte[]> members = NarcFile.Read(resourceNarc.byteData, out byte[] names);
                var known = new Dictionary<string, int>();
                string Hash(byte[] b) { using (var sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(b)); }
                for (int i = 0; i < members.Count; i++) { string h = Hash(members[i]); if (!known.ContainsKey(h)) known[h] = i; }
                foreach (FollowerAppearance e in draft)
                {
                    if (!imports.TryGetValue(e.Key, out PendingSprite sprite)) continue;
                    FollowerSprites.Validate(sprite.Btx, sprite.Asymmetric, out int size);
                    string hash = Hash(sprite.Btx);
                    if (known.TryGetValue(hash, out int existing)) e.ResourceId = existing;
                    else if (e.ResourceId >= RetailResources && e.ResourceId < members.Count && draft.Count(o => o.ResourceId == e.ResourceId) == 1)
                    {
                        members[e.ResourceId] = sprite.Btx;
                        known[hash] = e.ResourceId;
                    }
                    else
                    {
                        e.ResourceId = members.Count;
                        members.Add(sprite.Btx);
                        known[hash] = e.ResourceId;
                    }
                    if (members.Count > 65535) throw new InvalidOperationException("The follower sprite archive is full.");
                    e.Size = size;
                    e.Asymmetric = sprite.Asymmetric;
                    e.Placeholder = sprite.Placeholder;
                    e.SideGap = FollowerSprites.SideGap(sprite.Btx, sprite.Asymmetric);
                }
                resourceBytes = NarcFile.Write(members, names);
                resourceCount = members.Count;
            }

            // Descriptor rows: sprite member, quad size and frame layout, as the install normalizes them.
            List<byte> descriptorEntry = descriptorNarc.GetFileEntry(0);
            if (descriptorEntry == null) throw new InvalidOperationException("The descriptor table (a/0/4/7) is missing.");
            byte[] table = descriptorEntry.ToArray();
            foreach (FollowerAppearance e in draft)
            {
                int at = 4 + e.DescriptorRow * 28;
                if (at + 28 > table.Length) throw new InvalidOperationException("Descriptor row " + e.DescriptorRow + " is missing from a/0/4/7.");
                table[at + 16] = (byte)e.ResourceId; table[at + 17] = (byte)(e.ResourceId >> 8);
                table[at + 7] = (byte)(e.Size == 64 ? 2 : 0);
                table[at + 9] = (byte)(e.Asymmetric ? 14 : 13);
            }

            // Registry: patch each record in place; zones and everything else stay as they are.
            byte[] registry = (byte[])RegistryBytes.Clone();
            bool compact = Registry.Version == 2 || Registry.Version == 4;
            foreach (FollowerAppearance e in draft)
            {
                if (e.OffsetY < -128 || e.OffsetY > 127) throw new ArgumentException(e.Key + ": height must be from -128 to 127.");
                int at = e.RecordOffset;
                if (compact)
                {
                    W16(registry, at + 6, e.ResourceId);
                    registry[at + 8] = (byte)((e.Placeholder ? 1 : 0) | (e.Size == 64 ? 2 : 0) | (e.Asymmetric ? 4 : 0) | (Registry.Version == 4 ? Math.Min(6, e.SideGap) << 3 : 0));
                    registry[at + 10] = (byte)(sbyte)e.OffsetY;
                }
                else
                {
                    registry[at + 5] = (byte)(e.Placeholder ? 1 : 0);
                    W16(registry, at + 8, e.ResourceId);
                    registry[at + 10] = (byte)e.Size;
                    registry[at + 11] = (byte)(e.Asymmetric ? 1 : 0);
                    registry[at + 13] = (byte)(sbyte)e.OffsetY;
                    if (Registry.Version == 3) registry[at + 15] = (byte)Math.Min(6, e.SideGap);
                }
            }
            W16(registry, 18, resourceCount);
            registry[24] = registry[25] = registry[26] = registry[27] = 0;
            W32(registry, 24, Crc32(registry, 0, registry.Length));
            uint registryCrc = Crc32(registry, 0, registry.Length);

            byte[] native = (byte[])ReadFile(fileSystem, NativePath).Clone();
            W32(native, 12, (uint)registry.Length);
            W32(native, 20, (uint)resourceCount);
            W32(native, 24, registryCrc);

            byte[] positioning = null;
            byte[] positioningFile = ReadFile(fileSystem, PositioningPath);
            if (positioningFile != null && Positioning != null)
            {
                if (land.Length != Positioning.Land.Length) throw new InvalidOperationException("The follow distance rows no longer match positioning.narc.");
                foreach (byte gap in land.Where((value, i) => i % 12 < 4))
                    if (gap > MaxFollowDistance) throw new ArgumentException("Follow distances must be from 0 to " + MaxFollowDistance + ".");
                List<byte[]> members = NarcFile.Read(positioningFile, out byte[] names);
                byte[] member = (byte[])members[0].Clone();
                Array.Copy(land, 0, member, 24, land.Length);
                W32(member, 12, registryCrc);
                W32(member, 20, Crc32(member, 24, member.Length - 24));
                members[0] = member;
                positioning = NarcFile.Write(members, names);
            }

            // Read everything back the way the runtime will before changing the ROM.
            FollowerRegistry decoded = FollowerRegistry.Decode(registry);
            FollowerPositioning decodedPositioning = positioning == null ? Positioning : FollowerPositioning.Decode(positioning, registry, decoded);
            if (decoded.ResourceCount != NarcFile.Count(resourceBytes)) throw new InvalidOperationException("The sprite archive and the registry disagree.");

            resourceNarc.byteData = resourceBytes;
            descriptorNarc.ReplaceFileEntry(0, new List<byte>(table));
            WriteFile(RegistryPath, registry);
            WriteFile(NativePath, native);
            if (positioning != null) WriteFile(PositioningPath, positioning);
            UpdateReceipt("registrySha256", registry);
            UpdateReceipt("descriptorsSha256", descriptorNarc.byteData);
            if (imports.Count > 0) UpdateReceipt("resourcesSha256", resourceBytes);
            if (positioning != null) UpdateReceipt("positioningSha256", positioning);
            UpdateReceiptNumber("configCrc32", Crc32(native, 0, native.Length));

            RegistryBytes = registry;
            Registry = decoded;
            Positioning = decodedPositioning;
        }

        /// <summary>A sprite for this appearance from a/0/4/8, or the pending replacement.</summary>
        public byte[] Sprite(FollowerAppearance appearance, Dictionary<string, PendingSprite> imports) =>
            imports != null && imports.TryGetValue(appearance.Key, out PendingSprite pending) ? pending.Btx : GetResource(appearance.ResourceId);

        static void W16(byte[] b, int at, int v) { b[at] = (byte)v; b[at + 1] = (byte)(v >> 8); }
        static void W32(byte[] b, int at, uint v) { W16(b, at, (int)(v & 0xFFFF)); W16(b, at + 2, (int)(v >> 16)); }
    }

    /// <summary>Whole-NARC read and write that keeps the name table as it was.</summary>
    public static class NarcFile
    {
        public static int Count(byte[] narc) => FollowingData.ReadU16(narc, 0x18);

        public static List<byte[]> Read(byte[] narc, out byte[] names)
        {
            if (narc == null || narc.Length < 0x1C || Encoding.ASCII.GetString(narc, 0, 4) != "NARC" || Encoding.ASCII.GetString(narc, 0x10, 4) != "BTAF")
                throw new InvalidDataException("Not a NARC archive.");
            int fat = 0x10, fatSize = (int)FollowingData.ReadU32(narc, fat + 4), count = FollowingData.ReadU16(narc, fat + 8);
            int fnt = fat + fatSize, fntSize = (int)FollowingData.ReadU32(narc, fnt + 4), gmif = fnt + fntSize;
            if (Encoding.ASCII.GetString(narc, fnt, 4) != "BTNF" || Encoding.ASCII.GetString(narc, gmif, 4) != "GMIF") throw new InvalidDataException("Damaged NARC archive.");
            names = new byte[fntSize];
            Array.Copy(narc, fnt, names, 0, fntSize);
            var members = new List<byte[]>(count);
            for (int i = 0; i < count; i++)
            {
                int start = (int)FollowingData.ReadU32(narc, fat + 12 + i * 8), end = (int)FollowingData.ReadU32(narc, fat + 16 + i * 8);
                if (end < start || gmif + 8 + end > narc.Length) throw new InvalidDataException("Damaged NARC member " + i + ".");
                var member = new byte[end - start];
                Array.Copy(narc, gmif + 8 + start, member, 0, member.Length);
                members.Add(member);
            }
            return members;
        }

        /// <summary>Members padded to 4 bytes with zeros, as Pokeweb's NARC.save writes them.</summary>
        public static byte[] Write(List<byte[]> members, byte[] names)
        {
            int dataLength = members.Sum(m => (m.Length + 3) & ~3);
            int fatSize = 12 + members.Count * 8, total = 0x10 + fatSize + names.Length + 8 + dataLength;
            var o = new byte[total];
            Encoding.ASCII.GetBytes("NARC").CopyTo(o, 0);
            o[4] = 0xFF; o[5] = 0xFE; o[6] = 1; o[7] = 0;
            W32(o, 8, total); o[12] = 0x10; o[14] = 3;
            int at = 0x10;
            Encoding.ASCII.GetBytes("BTAF").CopyTo(o, at); W32(o, at + 4, fatSize); W32(o, at + 8, members.Count);
            int offset = 0;
            for (int i = 0; i < members.Count; i++)
            {
                W32(o, at + 12 + i * 8, offset);
                W32(o, at + 16 + i * 8, offset + members[i].Length);
                offset += (members[i].Length + 3) & ~3;
            }
            at += fatSize;
            names.CopyTo(o, at);
            at += names.Length;
            Encoding.ASCII.GetBytes("GMIF").CopyTo(o, at); W32(o, at + 4, 8 + dataLength);
            at += 8;
            foreach (byte[] m in members) { m.CopyTo(o, at); at += (m.Length + 3) & ~3; }
            return o;
        }

        static void W32(byte[] b, int at, int v) { b[at] = (byte)v; b[at + 1] = (byte)(v >> 8); b[at + 2] = (byte)(v >> 16); b[at + 3] = (byte)(v >> 24); }
    }

    public static partial class FollowerSprites
    {
        /// <summary>
        /// Pokeweb's validateFollowerResource: a BTX0 with 6 (mirrored) or 8
        /// frames, one palette, square 32- or 64-pixel 4-bit textures with
        /// colour 0 transparent. Throws with the reason.
        /// </summary>
        public static void Validate(byte[] b, bool asymmetric, out int size)
        {
            if (b == null || b.Length < 80 || b.Length > 128 * 1024 || FollowingData.ReadU32(b, 8) != b.Length) throw new InvalidDataException("Invalid sprite file length.");
            Layout layout = Parse(b);
            int frames = asymmetric ? 8 : 6;
            if (layout.Textures.Count != frames || layout.Palettes != 1) throw new InvalidDataException("Expected " + frames + " frames and one palette.");
            size = layout.Textures[0].width;
            if (size != 32 && size != 64) throw new InvalidDataException("Expected 32- or 64-pixel frames.");
            if (layout.PaletteData + 32 > b.Length || layout.PaletteSize < 32) throw new InvalidDataException("Truncated sprite palette.");
            foreach (var t in layout.Textures)
            {
                if (t.width != size || t.height != size || t.format != 3 || !t.transparent0)
                    throw new InvalidDataException("Sprites need equal square 4-bit frames with colour 0 transparent.");
                if (t.offset + size * size / 2 > layout.TextureSize || layout.TextureData + t.offset + size * size / 2 > b.Length)
                    throw new InvalidDataException("Truncated sprite frame.");
            }
        }

        /// <summary>Asymmetric when the sprite has 8 frames, mirrored when it has 6.</summary>
        public static bool IsAsymmetric(byte[] b) => Parse(b).Textures.Count == 8;

        class Layout
        {
            public int TextureData, TextureSize, PaletteData, PaletteSize, Palettes;
            public List<(int offset, int width, int height, int format, bool transparent0)> Textures = new List<(int, int, int, int, bool)>();
        }

        static Layout Parse(byte[] b)
        {
            if (b == null || b.Length < 0x20 || Encoding.ASCII.GetString(b, 0, 4) != "BTX0") throw new InvalidDataException("Not a BTX0 sprite.");
            int tex0 = -1;
            for (int i = 0; i < FollowingData.ReadU16(b, 0x0E) && 0x14 + i * 4 <= b.Length; i++)
            {
                int offset = (int)FollowingData.ReadU32(b, 0x10 + i * 4);
                if (offset + 0x3C <= b.Length && Encoding.ASCII.GetString(b, offset, 4) == "TEX0") { tex0 = offset; break; }
            }
            if (tex0 < 0) throw new InvalidDataException("The sprite has no TEX0 block.");
            var layout = new Layout
            {
                TextureSize = FollowingData.ReadU16(b, tex0 + 0x0C) * 8,
                TextureData = tex0 + (int)FollowingData.ReadU32(b, tex0 + 0x14),
                PaletteSize = (int)FollowingData.ReadU32(b, tex0 + 0x30) * 8,
                PaletteData = tex0 + (int)FollowingData.ReadU32(b, tex0 + 0x38)
            };
            int info = tex0 + FollowingData.ReadU16(b, tex0 + 0x0E), paletteInfo = tex0 + (int)FollowingData.ReadU32(b, tex0 + 0x34);
            if (info + 8 > b.Length || paletteInfo + 2 > b.Length) throw new InvalidDataException("Truncated sprite header.");
            layout.Palettes = b[paletteInfo + 1];
            int count = b[info + 1], props = info + 4 + FollowingData.ReadU16(b, info + 6);
            if (props + count * 8 > b.Length) throw new InvalidDataException("Truncated sprite frame table.");
            for (int i = 0; i < count; i++)
            {
                int param = FollowingData.ReadU16(b, props + i * 8 + 2);
                layout.Textures.Add((FollowingData.ReadU16(b, props + i * 8) * 8, 8 << ((param >> 4) & 7), 8 << ((param >> 7) & 7), (param >> 10) & 7, ((param >> 13) & 1) != 0));
            }
            return layout;
        }

        /// <summary>
        /// Pokeweb's followerSideGap: half of the widest left or right pose
        /// beyond a 16-unit tile, 0–6. Used for the default follow distance.
        /// </summary>
        public static int SideGap(byte[] btx, bool asymmetric)
        {
            int widest = 0;
            foreach (int direction in new[] { 2, 3 })
                foreach (int tick in new[] { 0, 10 })
                    using (Bitmap frame = Frame(btx, asymmetric, direction, tick, true))
                    {
                        if (frame == null) continue;
                        int min = frame.Width, max = -1;
                        for (int y = 0; y < frame.Height; y++)
                            for (int x = 0; x < frame.Width; x++)
                                if (frame.GetPixel(x, y).A != 0) { min = Math.Min(min, x); max = Math.Max(max, x); }
                        widest = Math.Max(widest, max >= min ? max - min + 1 : 0);
                    }
            return Math.Max(0, Math.Min(6, (int)Math.Ceiling((widest - 16) / 2.0)));
        }

        static byte[] Template(int size)
        {
            string name = size == 64 ? "FollowingTemplate64" : "FollowingTemplate32";
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (stream == null) throw new InvalidOperationException("The follower sprite template " + name + " is missing from this build.");
                var bytes = new byte[stream.Length];
                stream.Read(bytes, 0, bytes.Length);
                return bytes;
            }
        }

        /// <summary>
        /// Pokeweb's encodeFollowerFrames for a four-direction sheet: two
        /// columns (idle, step), rows up, down, left, right; 64×128 for 32-pixel
        /// frames or 128×256 for 64-pixel ones. Pixels must be fully opaque or
        /// fully transparent, with at most 15 DS colours; nothing is reduced.
        /// </summary>
        public static byte[] EncodeSheet(Bitmap sheet)
        {
            int size = sheet.Width / 2;
            if ((size != 32 && size != 64) || sheet.Width != size * 2 || sheet.Height != size * 4)
                throw new InvalidDataException("Use a 64×128 or 128×256 PNG: rows up, down, left, right; columns idle and step.");
            var pixels = new int[sheet.Width * sheet.Height];
            using (Bitmap argb = sheet.Clone(new Rectangle(0, 0, sheet.Width, sheet.Height), PixelFormat.Format32bppArgb))
            {
                BitmapData locked = argb.LockBits(new Rectangle(0, 0, argb.Width, argb.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                for (int y = 0; y < argb.Height; y++) Marshal.Copy(locked.Scan0 + y * locked.Stride, pixels, y * argb.Width, argb.Width);
                argb.UnlockBits(locked);
            }
            int Color(int argbValue) => ((argbValue >> 16 & 255) >> 3) | ((argbValue >> 8 & 255) >> 3) << 5 | ((argbValue & 255) >> 3) << 10;
            var colors = new SortedSet<int>();
            foreach (int p in pixels)
            {
                int alpha = (p >> 24) & 255;
                if (alpha != 0 && alpha != 255) throw new InvalidDataException("The sprite has partly transparent pixels; use only fully opaque or fully transparent ones.");
                if (alpha != 0) colors.Add(Color(p));
            }
            if (colors.Count > 15) throw new InvalidDataException("The sprite uses " + colors.Count + " DS colours; the limit is 15 plus transparency.");
            var lookup = new Dictionary<int, int>();
            int index = 1;
            foreach (int c in colors) lookup[c] = index++;

            byte[] bytes = Template(size);
            Layout layout = Parse(bytes);
            Array.Clear(bytes, layout.PaletteData, layout.PaletteSize);
            foreach (var pair in lookup)
            {
                bytes[layout.PaletteData + pair.Value * 2] = (byte)pair.Key;
                bytes[layout.PaletteData + pair.Value * 2 + 1] = (byte)(pair.Key >> 8);
            }
            for (int frame = 0; frame < 8; frame++)
            {
                int at = layout.TextureData + layout.Textures[frame].offset, left = (frame % 2) * size, top = (frame / 2) * size;
                for (int pixel = 0; pixel < size * size; pixel += 2)
                {
                    int Index(int p)
                    {
                        int value = pixels[(top + p / size) * sheet.Width + left + p % size];
                        return ((value >> 24) & 255) != 0 ? lookup[Color(value)] : 0;
                    }
                    bytes[at + pixel / 2] = (byte)(Index(pixel) | Index(pixel + 1) << 4);
                }
            }
            Validate(bytes, true, out _);
            return bytes;
        }
    }
}
