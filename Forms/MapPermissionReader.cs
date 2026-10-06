using System;
using System.Collections.Generic;
using NewEditor.Data.NARCTypes;

namespace NewEditor.Forms
{
    /// <summary>
    /// Reads and writes Gen 5 2D movement permissions in a/0/0/8 map containers.
    /// Layout matches BeaterLibrary MapContainer (WB / RD / GC / NG).
    /// </summary>
    public static class MapPermissionReader
    {
        public const int TileSize = 32;

        public const int TypeWalk = 0;
        public const int TypeBlocked = 1;
        public const int TypeShortGrass = 2;
        public const int TypeTallGrass = 4;
        public const int TypeDarkGrass = 6;
        public const int TypeCave = 10;
        public const int TypeSnow = 14;
        public const int TypeSwamp = 28;
        public const int TypeSurfPond = 61;
        public const int TypeSurf = 63;
        public const int TypeSurfEdge = 65;
        public const int TypeLedge = 115;
        public const int TypeLedgeN = 116;
        public const int TypeLedgeS = 117;
        public const int TypeLedgeW = 118;
        public const int TypeLedgeE = 119;

        const ushort MagicWB = 0x4257;
        const ushort MagicGC = 0x4347;
        const ushort MagicNG = 0x474E;
        const ushort MagicRD = 0x4452;

        public class PermSource
        {
            public byte[,] types;
            public int payloadStart;
            public int w, h, layer, bpp;
            public bool interleaved;
        }

        static readonly Dictionary<int, PermSource> cache = new Dictionary<int, PermSource>();

        public static void ClearCache()
        {
            cache.Clear();
        }

        public static byte[,] GetTypes(MapFilesNARC narc, int fileId)
        {
            return GetTypes(narc, fileId, 0);
        }

        public static byte[,] GetTypes(MapFilesNARC narc, int fileId, int block)
        {
            PermSource src = GetSource(narc, fileId, block);
            return src == null ? null : src.types;
        }

        public static PermSource GetSource(MapFilesNARC narc, int fileId)
        {
            return GetSource(narc, fileId, 0);
        }

        public static PermSource GetSource(MapFilesNARC narc, int fileId, int block)
        {
            if (narc == null || narc.files == null || fileId < 0 || fileId >= narc.files.Count)
                return null;
            int key = fileId * 2 + (block == 1 ? 1 : 0);
            PermSource cached;
            if (cache.TryGetValue(key, out cached)) return cached;
            cached = Parse(narc.files[fileId].bytes, block);
            cache[key] = cached;
            return cached;
        }

        static int LayerIndex(PermSource src, int lx, int ly, int layer)
        {
            return src.interleaved
                ? (ly * src.w + lx) * src.bpp + layer
                : layer * (src.w * src.h) + ly * src.w + lx;
        }

        public static bool SetType(MapFilesNARC narc, int fileId, int lx, int ly, byte value)
        {
            return SetType(narc, fileId, lx, ly, value, 0);
        }

        public static bool SetType(MapFilesNARC narc, int fileId, int lx, int ly, byte value, int block)
        {
            PermSource src = GetSource(narc, fileId, block);
            if (src == null || src.types == null) return false;
            if (lx < 0 || ly < 0 || lx >= src.w || ly >= src.h) return false;
            if (narc.files[fileId] == null || narc.files[fileId].bytes == null) return false;
            byte[] data = narc.files[fileId].bytes;

            // Each tile is bpp bytes (usually 8 interleaved planes).
            // Layer 0-3 are height; one of the later planes is movement type.
            // The editor preview only shows the highest-scoring plane, so writing
            // that one alone can leave the game still reading "blocked" elsewhere.
            bool wrote = false;
            int planes = Math.Max(1, src.bpp);
            for (int layer = 0; layer < planes; layer++)
            {
                int off = src.payloadStart + LayerIndex(src, lx, ly, layer);
                if (off < 0 || off >= data.Length) continue;
                byte cur = data[off];
                byte next = cur;
                if (value == TypeWalk)
                {
                    if (IsBlocked(cur)) next = TypeWalk;
                    // Plane 6 in B2W2: 0x80 = passage, 0x81 = no passage.
                    if (cur == 0x81) next = 0x80;
                }
                else if (value == TypeBlocked)
                {
                    if (cur == TypeWalk) next = TypeBlocked;
                    if (cur == 0x80) next = 0x81;
                }
                if (next == cur) continue;
                data[off] = next;
                wrote = true;
            }

            // Keep the preview plane in sync even if it was already 0.
            int vis = src.payloadStart + LayerIndex(src, lx, ly, src.layer);
            if (vis >= 0 && vis < data.Length)
            {
                if (value == TypeWalk && IsBlocked(data[vis])) data[vis] = TypeWalk;
                if (value == TypeBlocked && data[vis] == TypeWalk) data[vis] = TypeBlocked;
                src.types[lx, ly] = data[vis];
                wrote = true;
            }
            return wrote;
        }

        public static string DumpTile(MapFilesNARC narc, int fileId, int lx, int ly)
        {
            return DumpTile(narc, fileId, lx, ly, 0);
        }

        public static string DumpTile(MapFilesNARC narc, int fileId, int lx, int ly, int block)
        {
            PermSource src = GetSource(narc, fileId, block);
            if (src == null || narc == null || narc.files == null) return "";
            if (fileId < 0 || fileId >= narc.files.Count) return "";
            byte[] data = narc.files[fileId].bytes;
            if (data == null) return "";
            var sb = new System.Text.StringBuilder();
            sb.Append(" planes[");
            int planes = Math.Max(1, src.bpp);
            if (planes > 8) planes = 8;
            for (int layer = 0; layer < planes; layer++)
            {
                int off = src.payloadStart + LayerIndex(src, lx, ly, layer);
                if (layer > 0) sb.Append(' ');
                if (off < 0 || off >= data.Length) sb.Append("??");
                else sb.Append(data[off].ToString("X2"));
            }
            sb.Append("] L").Append(src.layer).Append(src.interleaved ? "i" : "p");
            return sb.ToString();
        }

        public static byte[] GetPlanes(MapFilesNARC narc, int fileId, int lx, int ly)
        {
            return GetPlanes(narc, fileId, lx, ly, 0);
        }

        public static byte[] GetPlanes(MapFilesNARC narc, int fileId, int lx, int ly, int block)
        {
            PermSource src = GetSource(narc, fileId, block);
            if (src == null || narc == null || narc.files == null) return null;
            if (fileId < 0 || fileId >= narc.files.Count) return null;
            byte[] data = narc.files[fileId].bytes;
            if (data == null) return null;
            if (lx < 0 || ly < 0 || lx >= src.w || ly >= src.h) return null;
            int n = Math.Max(1, src.bpp);
            if (n > 8) n = 8;
            byte[] planes = new byte[n];
            for (int layer = 0; layer < n; layer++)
            {
                int off = src.payloadStart + LayerIndex(src, lx, ly, layer);
                planes[layer] = (off >= 0 && off < data.Length) ? data[off] : (byte)0;
            }
            return planes;
        }

        public static bool SetPlanes(MapFilesNARC narc, int fileId, int lx, int ly, byte[] planes)
        {
            return SetPlanes(narc, fileId, lx, ly, planes, 0);
        }

        public static bool SetPlanes(MapFilesNARC narc, int fileId, int lx, int ly, byte[] planes, int block)
        {
            PermSource src = GetSource(narc, fileId, block);
            if (src == null || planes == null || narc == null || narc.files == null) return false;
            if (fileId < 0 || fileId >= narc.files.Count) return false;
            byte[] data = narc.files[fileId].bytes;
            if (data == null) return false;
            if (lx < 0 || ly < 0 || lx >= src.w || ly >= src.h) return false;
            int n = Math.Min(planes.Length, Math.Max(1, src.bpp));
            bool wrote = false;
            for (int layer = 0; layer < n; layer++)
            {
                int off = src.payloadStart + LayerIndex(src, lx, ly, layer);
                if (off < 0 || off >= data.Length) continue;
                data[off] = planes[layer];
                wrote = true;
            }
            if (src.layer >= 0 && src.layer < n && src.types != null &&
                lx >= 0 && ly >= 0 && lx < src.types.GetLength(0) && ly < src.types.GetLength(1))
                src.types[lx, ly] = planes[src.layer];
            return wrote;
        }

        public static string TypeName(int t)
        {
            switch (t)
            {
                case TypeWalk: return "walk";
                case TypeBlocked: return "blocked";
                case TypeShortGrass: return "short grass";
                case TypeTallGrass: return "tall grass";
                case TypeDarkGrass: return "dark grass";
                case TypeCave: return "cave";
                case TypeSnow: return "snow";
                case TypeSwamp: return "swamp";
                case TypeSurfPond: return "water (pond)";
                case TypeSurf: return "water";
                case TypeSurfEdge: return "water edge";
                case TypeLedge: return "ledge";
                case TypeLedgeN: return "ledge (jump S)";
                case TypeLedgeS: return "ledge (jump N)";
                case TypeLedgeW: return "ledge (jump E)";
                case TypeLedgeE: return "ledge (jump W)";
                default:
                    if (IsLedge(t)) return "ledge";
                    if (IsWater(t)) return "water";
                    if (IsGrass(t)) return "grass";
                    if (IsBlocked(t)) return "blocked";
                    return "type " + t;
            }
        }

        public static bool IsBlocked(int t)
        {
            return t == TypeBlocked || t == 18 || t == 29;
        }

        public static bool IsGrass(int t)
        {
            return t == TypeShortGrass || t == TypeTallGrass || t == TypeDarkGrass || t == 31;
        }

        public static bool IsWater(int t)
        {
            return t == TypeSurfPond || t == TypeSurf || t == TypeSurfEdge || t == TypeSwamp
                || (t >= 61 && t <= 67);
        }

        public static bool IsLedge(int t)
        {
            return t == TypeLedge || t == TypeLedgeN || t == TypeLedgeS || t == TypeLedgeW || t == TypeLedgeE
                || (t >= 115 && t <= 119);
        }

        static PermSource Parse(byte[] data)
        {
            return Parse(data, 0);
        }

        static PermSource Parse(byte[] data, int block)
        {
            if (data == null || data.Length < 16) return null;
            ushort magic = (ushort)(data[0] | (data[1] << 8));
            if (magic != MagicWB && magic != MagicGC && magic != MagicRD && magic != MagicNG)
                return ScanFallback(data);

            int pos = 4;
            ReadU32(data, ref pos);
            uint permOff = 0, perm2Off = 0, bldOff, fileSize;
            if (magic == MagicWB || magic == MagicRD)
                permOff = ReadU32(data, ref pos);
            else if (magic == MagicGC)
            {
                permOff = ReadU32(data, ref pos);
                perm2Off = ReadU32(data, ref pos);
            }
            bldOff = ReadU32(data, ref pos);
            fileSize = ReadU32(data, ref pos);
            if (fileSize == 0 || fileSize > data.Length) fileSize = (uint)data.Length;

            PermSource first = null, second = null;
            int scoreA = int.MinValue, scoreB = int.MinValue;
            if (permOff > 0 && permOff < fileSize)
            {
                uint end = (perm2Off > permOff) ? perm2Off : bldOff;
                if (end <= permOff || end > fileSize) end = Math.Min(fileSize, permOff + 0x6004);
                TryBlock(data, (int)permOff, (int)(end - permOff), ref first, ref scoreA);
            }
            if (perm2Off > 0 && perm2Off < fileSize)
            {
                uint end = bldOff > perm2Off ? bldOff : fileSize;
                TryBlock(data, (int)perm2Off, (int)(end - perm2Off), ref second, ref scoreB);
            }
            if (block == 1 && second != null) return second;
            return first ?? second ?? ScanFallback(data);
        }

        static void TryBlock(byte[] data, int offset, int length, ref PermSource best, ref int bestScore)
        {
            if (offset < 0 || length < 8 || offset + 4 > data.Length) return;
            int w = data[offset] | (data[offset + 1] << 8);
            int h = data[offset + 2] | (data[offset + 3] << 8);
            if (w <= 0 || h <= 0 || w > 64 || h > 64) return;
            int payload = length - 4;
            if (payload < w * h) return;
            int bpp = payload / (w * h);
            if (bpp < 1 || bpp > 32) return;
            if (offset + 4 + w * h * bpp > data.Length) return;

            for (int layer = 0; layer < bpp; layer++)
                ConsiderLayer(data, offset + 4, w, h, layer, bpp, true, ref best, ref bestScore);
            if (bpp > 1)
            {
                for (int plane = 0; plane < bpp; plane++)
                    ConsiderLayer(data, offset + 4, w, h, plane, bpp, false, ref best, ref bestScore);
            }
        }

        static void ConsiderLayer(byte[] data, int payloadStart, int w, int h, int layer, int bpp, bool interleaved, ref PermSource best, ref int bestScore)
        {
            byte[,] grid = new byte[w, h];
            int score = 0;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int idx = interleaved
                        ? (y * w + x) * bpp + layer
                        : layer * (w * h) + y * w + x;
                    int v = data[payloadStart + idx] & 0xFF;
                    grid[x, y] = (byte)v;
                    score += ScoreValue(v);
                }
            }
            if (score > bestScore)
            {
                bestScore = score;
                best = new PermSource
                {
                    types = grid,
                    payloadStart = payloadStart,
                    w = w,
                    h = h,
                    layer = layer,
                    bpp = bpp,
                    interleaved = interleaved
                };
            }
        }

        static int ScoreValue(int v)
        {
            switch (v)
            {
                case TypeBlocked: return 4;
                case TypeWalk: return 2;
                case TypeShortGrass:
                case TypeTallGrass:
                case TypeDarkGrass: return 6;
                case TypeSurfPond:
                case TypeSurf:
                case TypeSurfEdge:
                case TypeSwamp: return 6;
                case TypeLedge:
                case TypeLedgeN:
                case TypeLedgeS:
                case TypeLedgeW:
                case TypeLedgeE: return 5;
                case TypeCave:
                case TypeSnow:
                case 31: return 3;
                default:
                    if (v > 120) return -3;
                    if (v > 80) return -1;
                    return 0;
            }
        }

        static PermSource ScanFallback(byte[] data)
        {
            PermSource best = null;
            int bestScore = 200;
            for (int i = 0; i + 4 + TileSize * TileSize <= data.Length; i += 2)
            {
                if (data[i] != TileSize || data[i + 1] != 0 || data[i + 2] != TileSize || data[i + 3] != 0)
                    continue;
                int remain = data.Length - (i + 4);
                int bpp = remain / (TileSize * TileSize);
                if (bpp < 1) continue;
                if (bpp > 24) bpp = 8;
                TryBlock(data, i, 4 + TileSize * TileSize * Math.Min(bpp, 24), ref best, ref bestScore);
            }
            return best;
        }

        static uint ReadU32(byte[] data, ref int pos)
        {
            if (pos + 4 > data.Length) return 0;
            uint v = (uint)(data[pos] | (data[pos + 1] << 8) | (data[pos + 2] << 16) | (data[pos + 3] << 24));
            pos += 4;
            return v;
        }
    }
}
