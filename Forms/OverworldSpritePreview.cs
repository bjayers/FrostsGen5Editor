using NewEditor.Data;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace NewEditor.Forms
{
    /// <summary>
    /// First-frame preview of a Gen 5 overworld sprite from a/0/4/8 (B2W2) or a/0/4/9 (BW1).
    /// Decoder follows NSMBe's NSBTX/TEX0 layout (Kazo BWOE files).
    /// NPC sprite ID → file ID + 2. Pokémon bank 4096+ → files 356+.
    /// </summary>
    public static class OverworldSpritePreview
    {
        public const int Bw2NarcId = 48;
        public const int Bw1NarcId = 49;
        const int PokeBankId = 4096;
        const int PokeBankFile = 355;
        const int EmptyFrom = 377;
        const int NpcFileBias = 2;

        static readonly Dictionary<int, Bitmap> cache = new Dictionary<int, Bitmap>();
        static int currentFrame = -1; // -1 = default south idle
        static int currentSprite = int.MinValue;

        // game sprite ID -> a/0/4/8 file index. Missing keys use id + 2.
        // Paste more lines as: { spriteId, fileId },
        // Built from range rules + single overrides. Do not edit by hand;
        // add a line to RangeRules or ExactRules instead.
        static Dictionary<int, int> spriteToFile;

        // "lo-hi = delta" means spinner X in lo..hi shows the art for (X + delta).
        // Example: 5-200 = -4  →  game id 1 uses the file currently previewed at 5.
        static readonly int[][] RangeRules = new int[][]
        {
            new int[] { 5, 109, -4 },
            new int[] { 117, 119, -2 },
            new int[] { 121, 134, 0 },
            new int[] { 143, 151, 4 },
            new int[] { 158, 164, 20 },
            new int[] { 165, 172, 21 },
            new int[] { 173, 179, 22 },
            new int[] { 180, 227, 24 },
            new int[] { 228, 264, 24 },
            new int[] { 265, 352, 24 },
        };

        static readonly int[][] SkipRanges = new int[][]
        {
            new int[] { 135, 142 },
        };

        static readonly int[][] BlankRanges = new int[][]
        {
            new int[] { 156, 177 },
        };

        // { gameId, fileId }  file 0 = blank
        static readonly int[][] ExactRules = new int[][]
        {
            // spinner -> gameId from the 106-119 remap, stored as { gameId, file=spinner+2 }
            new int[] { 110, 108 },
            new int[] { 107, 109 },
            new int[] { 108, 110 },
            new int[] { 105, 111 },
            new int[] { 106, 112 },
            new int[] { 111, 113 },
            new int[] { 108, 114 },
            new int[] { 110, 115 },
            new int[] { 111, 116 },
            new int[] { 113, 117 },
            new int[] { 114, 118 },
            new int[] { 117, 119 },
            new int[] { 116, 120 },
            new int[] { 117, 121 },
            new int[] { 115, 0 },
            new int[] { 120, 0 },
            new int[] { 185, 0 },
            new int[] { 194, 0 },
            new int[] { 202, 0 },
            new int[] { 203, 0 },
        };

        static Dictionary<int, int> BuildMap()
        {
            var map = new Dictionary<int, int>();
            if (RangeRules != null)
            {
                for (int r = 0; r < RangeRules.Length; r++)
                {
                    int lo = RangeRules[r][0], hi = RangeRules[r][1], delta = RangeRules[r][2];
                    for (int spinner = lo; spinner <= hi; spinner++)
                    {
                        int gameId = spinner + delta;
                        if (gameId <= 0 || gameId >= EmptyFrom) continue;
                        map[gameId] = spinner + NpcFileBias;
                    }
                }
            }
            if (SkipRanges != null)
            {
                for (int r = 0; r < SkipRanges.Length; r++)
                {
                    int lo = SkipRanges[r][0], hi = SkipRanges[r][1];
                    for (int id = lo; id <= hi; id++)
                        map.Remove(id);
                }
            }
            if (BlankRanges != null)
            {
                for (int r = 0; r < BlankRanges.Length; r++)
                {
                    int lo = BlankRanges[r][0], hi = BlankRanges[r][1];
                    for (int id = lo; id <= hi; id++)
                        map[id] = 0;
                }
            }
            if (ExactRules != null)
            {
                for (int i = 0; i < ExactRules.Length; i++)
                {
                    int gid = ExactRules[i][0];
                    int file = ExactRules[i][1];
                    map[gid] = file; // 0 / -1 = blank preview
                }
            }
            return map;
        }

        public static void ClearCache()
        {
            foreach (var kv in cache)
                if (kv.Value != null) kv.Value.Dispose();
            cache.Clear();
        }

        public static Bitmap Get(int spriteId)
        {
            Bitmap cached;
            if (cache.TryGetValue(spriteId, out cached)) return cached;
            cached = Decode(spriteId);
            cache[spriteId] = cached;
            return cached;
        }

        public static void Bind(PictureBox box, NumericUpDown spriteBox)
        {
            if (box == null || spriteBox == null) return;
            spriteBox.ValueChanged += (s, e) =>
            {
                currentFrame = -1;
                Show(box, (int)spriteBox.Value);
            };
            box.MouseWheel += (s, e) =>
            {
                if (e.Delta > 0) StepFrame(box, (int)spriteBox.Value, -1);
                else StepFrame(box, (int)spriteBox.Value, 1);
            };
            Show(box, (int)spriteBox.Value);
        }

        public static int FrameCount(int spriteId)
        {
            Bitmap sheet = Get(spriteId);
            if (sheet == null) return 0;
            int side = Math.Min(32, Math.Min(sheet.Width, sheet.Height));
            if (side <= 0) return 1;
            return Math.Max(1, sheet.Height / side);
        }

        public static void StepFrame(PictureBox box, int spriteId, int delta)
        {
            int n = FrameCount(spriteId);
            if (n <= 0) return;
            if (spriteId != currentSprite || currentFrame < 0)
                currentFrame = DefaultFrame(n);
            currentFrame = (currentFrame + delta) % n;
            if (currentFrame < 0) currentFrame += n;
            Show(box, spriteId);
        }

        public static void Show(PictureBox box, int spriteId)
        {
            if (box == null) return;
            Bitmap sheet = Get(spriteId);
            int n = FrameCount(spriteId);
            if (spriteId != currentSprite)
            {
                currentSprite = spriteId;
                if (currentFrame < 0) currentFrame = DefaultFrame(n);
            }
            if (currentFrame < 0) currentFrame = DefaultFrame(n);
            box.Image = CropFrame(sheet, currentFrame);
            box.SizeMode = PictureBoxSizeMode.Zoom;
            box.BackColor = Color.FromArgb(18, 20, 24);
            int file = FileForSprite(spriteId);
            box.Tag = file;
            string tip = "sprite " + spriteId + " -> file " + file;
            if (n > 0) tip += "  frame " + currentFrame + "/" + Math.Max(0, n - 1);
            var tt = new ToolTip();
            tt.SetToolTip(box, tip);
        }

        static int DefaultFrame(int frames)
        {
            if (frames >= 10) return 3;
            if (frames >= 8) return 2;
            if (frames >= 4) return 3;
            if (frames >= 2) return 1;
            return 0;
        }

        static Dictionary<int, int> Map()
        {
            if (spriteToFile == null) spriteToFile = BuildMap();
            return spriteToFile;
        }

        static int FileForSprite(int spriteId)
        {
            if (spriteId <= 0) return -1;
            if (spriteId >= PokeBankId)
                return PokeBankFile + (spriteId - PokeBankId);
            if (spriteId >= EmptyFrom) return -1;
            int file;
            if (Map().TryGetValue(spriteId, out file))
                return file;
            return spriteId + NpcFileBias;
        }

        static Bitmap Decode(int spriteId)
        {
            NARC narc = OwSpriteNarc();
            if (narc == null || narc.numFileEntries <= 0) return null;
            int fileId = FileForSprite(spriteId);
            if (fileId <= 0 || fileId >= narc.numFileEntries) return null;
            List<byte> entry = narc.GetFileEntry(fileId);
            if (entry == null || entry.Count < 16) return null;
            byte[] data = MaybeDecompress(entry.ToArray());
            if (data == null || data.Length < 16) return null;
            return DecodeTex0(data);
        }

        static byte[] MaybeDecompress(byte[] data)
        {
            if (data.Length > 4 && (data[0] == 0x10 || data[0] == 0x11))
            {
                try
                {
                    byte[] dec = DsDecmp.Decompress(data);
                    if (dec != null && dec.Length > 16) return dec;
                }
                catch { }
            }
            return data;
        }

        static NARC OwSpriteNarc()
        {
            var fs = MainEditor.fileSystem;
            if (fs == null || fs.narcs == null) return null;
            int id = MainEditor.RomType == RomType.BW1 ? Bw1NarcId : Bw2NarcId;
            if (id < 0 || id >= fs.narcs.Count) return null;
            return fs.narcs[id];
        }

        static Bitmap CropFrame(Bitmap sheet, int frame)
        {
            if (sheet == null) return null;
            int side = 32;
            if (sheet.Width < side) side = sheet.Width;
            if (sheet.Height < side) side = sheet.Height;
            if (side <= 0) return sheet;
            int frames = Math.Max(1, sheet.Height / side);
            if (frame < 0) frame = 0;
            if (frame >= frames) frame = frames - 1;
            int srcY = frame * side;
            if (srcY + side > sheet.Height) srcY = sheet.Height - side;
            Bitmap bmp = new Bitmap(side, side, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                g.DrawImage(sheet, new Rectangle(0, 0, side, side),
                    new Rectangle(0, srcY, side, side), GraphicsUnit.Pixel);
            }
            return bmp;
        }

        static Bitmap DecodeTex0(byte[] data)
        {
            int tex0 = FindTag(data, 0x54, 0x45, 0x58, 0x30);
            if (tex0 < 0 || tex0 + 0x40 > data.Length) return null;

            int texDataOff = tex0 + ReadS32(data, tex0 + 0x14);
            int palDefRel = ReadS32(data, tex0 + 0x34);
            int palDataRel = ReadS32(data, tex0 + 0x38);
            int palDataOff = tex0 + palDataRel;

            int countPos = tex0 + 0x3D;
            if (countPos >= data.Length) return null;
            int texCount = data[countPos];
            if (texCount <= 0 || texCount > 64) return null;

            int info = countPos + 1 + 0xE + texCount * 4;
            if (info + 8 > data.Length) return null;

            int texOff = texDataOff + 8 * ReadU16(data, info);
            int param = ReadU16(data, info + 2);
            int format = (param >> 10) & 7;
            int width = 8 << ((param >> 4) & 7);
            int height = 8 << ((param >> 7) & 7);
            bool color0 = ((param >> 13) & 1) != 0;

            if (width < 8 || height < 8 || width > 1024 || height > 1024) return null;
            if (format == 0 || format == 5 || format == 7)
            {
                format = 3;
                width = 32;
                if (height < 32) height = 32;
            }

            int palOff = palDataOff;
            int palDict = tex0 + palDefRel;
            if (palDefRel > 0 && palDict + 2 < data.Length)
            {
                int palCount = data[palDict + 1];
                int palInfo = palDict + 1 + 1 + 0xE + palCount * 4;
                if (palCount > 0 && palCount <= 64 && palInfo + 2 <= data.Length)
                    palOff = palDataOff + 8 * ReadU16(data, palInfo);
            }

            int colors = format == 4 ? 256 : format == 2 ? 4 : 16;
            Color[] pal = ReadPalette(data, palOff, colors);
            return Raster(data, texOff, width, height, format, pal, color0);
        }

        static int FindTag(byte[] data, byte a, byte b, byte c, byte d)
        {
            for (int i = 0; i + 4 <= data.Length; i++)
                if (data[i] == a && data[i + 1] == b && data[i + 2] == c && data[i + 3] == d)
                    return i;
            return -1;
        }

        static int ReadS32(byte[] d, int o)
        {
            if (o < 0 || o + 4 > d.Length) return 0;
            return d[o] | (d[o + 1] << 8) | (d[o + 2] << 16) | (d[o + 3] << 24);
        }

        static int ReadU16(byte[] d, int o)
        {
            if (o < 0 || o + 2 > d.Length) return 0;
            return d[o] | (d[o + 1] << 8);
        }

        static Color[] ReadPalette(byte[] data, int offset, int colors)
        {
            Color[] pal = new Color[Math.Max(16, colors)];
            for (int i = 0; i < pal.Length; i++)
            {
                int o = offset + i * 2;
                if (o + 1 >= data.Length)
                {
                    pal[i] = Color.Transparent;
                    continue;
                }
                pal[i] = DsDecmp.Read16BitColor(data[o] | (data[o + 1] << 8));
            }
            return pal;
        }

        static Bitmap Raster(byte[] data, int offset, int width, int height, int format, Color[] pal, bool color0Clear)
        {
            if (offset < 0 || offset >= data.Length) return null;
            int bpp = format == 2 ? 2 : format == 4 ? 8 : 4;
            Bitmap bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            int total = width * height;
            for (int i = 0; i < total; i++)
            {
                int idx = 0;
                if (bpp == 8)
                {
                    int o = offset + i;
                    if (o >= data.Length) break;
                    idx = data[o];
                }
                else if (bpp == 4)
                {
                    int o = offset + i / 2;
                    if (o >= data.Length) break;
                    idx = ((i & 1) == 0) ? (data[o] & 0xF) : (data[o] >> 4);
                }
                else
                {
                    int o = offset + i / 4;
                    if (o >= data.Length) break;
                    idx = (data[o] >> ((i & 3) * 2)) & 3;
                }
                if (idx >= pal.Length) idx = 0;
                Color c = pal[idx];
                if ((color0Clear && idx == 0) || c.A == 0) c = Color.Transparent;
                bmp.SetPixel(i % width, i / width, c);
            }
            return bmp;
        }
    }
}
