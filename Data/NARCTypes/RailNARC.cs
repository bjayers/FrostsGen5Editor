using System;
using System.Collections.Generic;
using System.Text;

namespace NewEditor.Data.NARCTypes
{
    /// <summary>
    /// BW2 NARC 78: rail data. First half is rail containers (magic RL, 5 sections),
    /// second half is paired tilemaps (one block per line).
    /// </summary>
    public class RailNARC : NARC
    {
        public List<RailFile> rails = new List<RailFile>();
        public List<RailTilemap> tilemaps = new List<RailTilemap>();

        public override void ReadData()
        {
            base.ReadData();
            rails.Clear();
            tilemaps.Clear();
            int half = numFileEntries / 2;
            for (int i = 0; i < half; i++)
            {
                var raw = GetFileEntry(i);
                rails.Add(raw == null ? new RailFile() : RailFile.Parse(raw.ToArray()));
            }
            for (int i = half; i < numFileEntries; i++)
            {
                var raw = GetFileEntry(i);
                tilemaps.Add(raw == null ? new RailTilemap() : RailTilemap.Parse(raw.ToArray()));
            }
        }

        public override void WriteData()
        {
            // Only files whose bytes changed are written back, so an unedited
            // or unrecognized file stays exactly as it was loaded.
            int half = numFileEntries / 2;
            for (int i = 0; i < rails.Count && i < half; i++)
                ReplaceIfChanged(i, rails[i].ToBytes());
            for (int i = 0; i < tilemaps.Count && i < half; i++)
                ReplaceIfChanged(half + i, tilemaps[i].ToBytes());
            base.WriteData();
        }

        void ReplaceIfChanged(int fileID, byte[] bytes)
        {
            List<byte> current = GetFileEntry(fileID);
            if (current == null || bytes == null) return;
            if (current.Count == bytes.Length)
            {
                bool same = true;
                for (int i = 0; i < bytes.Length && same; i++) same = current[i] == bytes[i];
                if (same) return;
            }
            ReplaceFileEntry(fileID, new List<byte>(bytes));
        }

        public RailFile GetRail(int index)
        {
            if (index < 0 || index >= rails.Count) return null;
            return rails[index];
        }

        public RailTilemap GetTilemap(int index)
        {
            if (index < 0 || index >= tilemaps.Count) return null;
            return tilemaps[index];
        }

        /// <summary>
        /// Whether the rail and its tilemap can be rebuilt on save. Files that
        /// fail this are kept as loaded, so edits to them would not be saved.
        /// </summary>
        public bool CanEdit(int index)
        {
            var rail = GetRail(index);
            var tilemap = GetTilemap(index);
            return rail != null && rail.rebuildable && (tilemap == null || tilemap.rebuildable);
        }
    }

    public class RailFile
    {
        public byte[] raw;
        public List<RailPoint> points = new List<RailPoint>();
        public List<RailLine> lines = new List<RailLine>();
        public List<RailCurve> curves = new List<RailCurve>();
        public int originalLineCount;
        public bool hasSky;
        // Set when the file is the five-section RL container the rebuild in
        // ToBytes understands, with every section an exact number of records.
        // Anything else is saved as its original bytes.
        public bool rebuildable;

        public static RailFile Parse(byte[] data)
        {
            var f = new RailFile { raw = data };
            // GF container: magic "RL", then file count, then offset table, then sections.
            // Section 1 = points (0x70 each), section 2 = lines (0x48 each), section 4 = curves (0x24 each).
            if (data == null || data.Length < 16) return f;
            if (data[0] != (byte)'R' || data[1] != (byte)'L') return f;

            int count = BitConverter.ToUInt16(data, 2);
            if (count < 5 || 4 + count * 4 > data.Length) return f;
            int[] offs = new int[count + 1];
            for (int i = 0; i < count; i++)
                offs[i] = BitConverter.ToInt32(data, 4 + i * 4);
            offs[count] = data.Length;
            for (int i = 0; i < count; i++)
                if (offs[i] < 4 + count * 4 || offs[i] > offs[i + 1]) return f;

            f.points = ParsePoints(data, offs[1], offs[2]);
            f.lines = ParseLines(data, offs[2], offs[3]);
            f.curves = ParseCurves(data, offs[4], offs[5]);
            f.originalLineCount = f.lines.Count;
            f.rebuildable = count == 5 &&
                (offs[2] - offs[1]) % 0x70 == 0 && (offs[3] - offs[2]) % 0x48 == 0 && (offs[5] - offs[4]) % 0x24 == 0;
            return f;
        }

        static List<RailPoint> ParsePoints(byte[] data, int start, int end)
        {
            var list = new List<RailPoint>();
            for (int p = start; p + 0x70 <= end; p += 0x70)
            {
                var pt = new RailPoint();
                // position is FX32 x,y,z after 4*3 ints of attachments (line, dir, width) = 48 bytes
                pt.x = BitConverter.ToInt32(data, p + 48);
                pt.y = BitConverter.ToInt32(data, p + 52);
                pt.z = BitConverter.ToInt32(data, p + 56);
                pt.raw = new byte[0x70];
                Array.Copy(data, p, pt.raw, 0, 0x70);
                list.Add(pt);
            }
            return list;
        }

        static List<RailLine> ParseLines(byte[] data, int start, int end)
        {
            var list = new List<RailLine>();
            for (int p = start; p + 0x48 <= end; p += 0x48)
            {
                var ln = new RailLine();
                ln.point1 = BitConverter.ToInt32(data, p);
                ln.point2 = BitConverter.ToInt32(data, p + 4);
                ln.angle = BitConverter.ToInt32(data, p + 8);
                ln.curveId = BitConverter.ToInt32(data, p + 12);
                ln.cameraId = BitConverter.ToInt32(data, p + 16);
                ln.tileLength = BitConverter.ToInt32(data, p + 20);
                ln.name = Encoding.ASCII.GetString(data, p + 24, 0x30).TrimEnd('\0');
                ln.raw = new byte[0x48];
                Array.Copy(data, p, ln.raw, 0, 0x48);
                list.Add(ln);
            }
            return list;
        }

        static List<RailCurve> ParseCurves(byte[] data, int start, int end)
        {
            var list = new List<RailCurve>();
            for (int p = start; p + 0x24 <= end; p += 0x24)
            {
                var c = new RailCurve();
                c.type = BitConverter.ToInt32(data, p);
                c.x = BitConverter.ToInt32(data, p + 4);
                c.y = BitConverter.ToInt32(data, p + 8);
                c.z = BitConverter.ToInt32(data, p + 12);
                c.raw = new byte[0x24];
                Array.Copy(data, p, c.raw, 0, 0x24);
                list.Add(c);
            }
            return list;
        }

        public int groundLineCount;

        public void AddLayer(int yOffsetFx32)
        {
            if (groundLineCount <= 0) groundLineCount = lines.Count;
            int basePoints = points.Count;
            int baseLines = lines.Count;
            int baseCurves = curves.Count;
            // Clone only the ground set (first groundLineCount lines and their points/curves)
            var groundPointIds = new System.Collections.Generic.HashSet<int>();
            var groundCurveIds = new System.Collections.Generic.HashSet<int>();
            for (int i = 0; i < groundLineCount && i < lines.Count; i++)
            {
                groundPointIds.Add(lines[i].point1);
                groundPointIds.Add(lines[i].point2);
                if (lines[i].curveId >= 0) groundCurveIds.Add(lines[i].curveId);
            }
            var pointMap = new System.Collections.Generic.Dictionary<int, int>();
            foreach (int pid in groundPointIds)
            {
                if (pid < 0 || pid >= points.Count) continue;
                var src = points[pid];
                var c = new RailPoint { x = src.x, y = yOffsetFx32, z = src.z, raw = (byte[])src.raw.Clone() };
                BitConverter.GetBytes(c.y).CopyTo(c.raw, 52);
                // Retarget the 4 line-index attachments at the new lines.
                for (int a = 0; a < 4; a++)
                {
                    int lineIdx = BitConverter.ToInt32(c.raw, a * 4);
                    if (lineIdx >= 0 && lineIdx < groundLineCount)
                        BitConverter.GetBytes(lineIdx + baseLines).CopyTo(c.raw, a * 4);
                }
                pointMap[pid] = points.Count;
                points.Add(c);
            }
            var curveMap = new System.Collections.Generic.Dictionary<int, int>();
            foreach (int cid in groundCurveIds)
            {
                if (cid < 0 || cid >= curves.Count) continue;
                var src = curves[cid];
                var c = new RailCurve { type = src.type, x = src.x, y = src.y + yOffsetFx32, z = src.z, raw = (byte[])src.raw.Clone() };
                BitConverter.GetBytes(c.y).CopyTo(c.raw, 8);
                curveMap[cid] = curves.Count;
                curves.Add(c);
            }
            for (int i = 0; i < groundLineCount && i < baseLines; i++)
            {
                var src = lines[i];
                int np1 = pointMap.ContainsKey(src.point1) ? pointMap[src.point1] : src.point1;
                int np2 = pointMap.ContainsKey(src.point2) ? pointMap[src.point2] : src.point2;
                int nc = curveMap.ContainsKey(src.curveId) ? curveMap[src.curveId] : src.curveId;
                var c = new RailLine
                {
                    point1 = np1, point2 = np2, angle = src.angle, curveId = nc,
                    cameraId = src.cameraId, tileLength = src.tileLength,
                    name = src.name + "_L", raw = (byte[])src.raw.Clone()
                };
                BitConverter.GetBytes(c.point1).CopyTo(c.raw, 0);
                BitConverter.GetBytes(c.point2).CopyTo(c.raw, 4);
                BitConverter.GetBytes(c.curveId).CopyTo(c.raw, 12);
                lines.Add(c);
            }
            hasSky = true;
        }

        public int RemoveLayer(decimal heightTiles)
        {
            int target = (int)(heightTiles * 2);
            var dropLines = new System.Collections.Generic.List<int>();
            for (int i = 0; i < lines.Count; i++)
            {
                var ln = lines[i];
                if (ln.point1 < 0 || ln.point1 >= points.Count || ln.point2 < 0 || ln.point2 >= points.Count) continue;
                int y = (points[ln.point1].y + points[ln.point2].y) / 2;
                if (y / 0x8000 == target) dropLines.Add(i);
            }
            for (int i = dropLines.Count - 1; i >= 0; i--)
                lines.RemoveAt(dropLines[i]);
            hasSky = lines.Count > groundLineCount;
            return dropLines.Count;
        }

        public int SetLayerHeight(decimal fromTiles, decimal toTiles)
        {
            int fromHalf = (int)(fromTiles * 2);
            int delta = (int)((toTiles - fromTiles) * 0x10000);
            var seen = new System.Collections.Generic.HashSet<int>();
            int count = 0;
            foreach (var ln in lines)
            {
                if (ln.point1 >= 0 && ln.point1 < points.Count && !seen.Contains(ln.point1))
                {
                    if (points[ln.point1].y / 0x8000 == fromHalf)
                    {
                        points[ln.point1].y += delta;
                        BitConverter.GetBytes(points[ln.point1].y).CopyTo(points[ln.point1].raw, 52);
                        seen.Add(ln.point1);
                        count++;
                    }
                }
                if (ln.point2 >= 0 && ln.point2 < points.Count && !seen.Contains(ln.point2))
                {
                    if (points[ln.point2].y / 0x8000 == fromHalf)
                    {
                        points[ln.point2].y += delta;
                        BitConverter.GetBytes(points[ln.point2].y).CopyTo(points[ln.point2].raw, 52);
                        seen.Add(ln.point2);
                        count++;
                    }
                }
            }
            return count;
        }

        public void AddSkyClone(int yOffsetFx32)
        {
            if (hasSky) return;
            int basePoints = points.Count;
            int baseLines = lines.Count;
            int baseCurves = curves.Count;

            for (int i = 0; i < basePoints; i++)
            {
                var src = points[i];
                var c = new RailPoint { x = src.x, y = src.y + yOffsetFx32, z = src.z, raw = (byte[])src.raw.Clone() };
                BitConverter.GetBytes(c.y).CopyTo(c.raw, 52);
                // Retarget the 4 line-index attachments (first 16 bytes) at the new lines.
                for (int a = 0; a < 4; a++)
                {
                    int lineIdx = BitConverter.ToInt32(c.raw, a * 4);
                    if (lineIdx >= 0 && lineIdx < baseLines)
                        BitConverter.GetBytes(lineIdx + baseLines).CopyTo(c.raw, a * 4);
                }
                points.Add(c);
            }
            for (int i = 0; i < baseCurves; i++)
            {
                var src = curves[i];
                var c = new RailCurve { type = src.type, x = src.x, y = src.y + yOffsetFx32, z = src.z, raw = (byte[])src.raw.Clone() };
                BitConverter.GetBytes(c.y).CopyTo(c.raw, 8);
                curves.Add(c);
            }
            for (int i = 0; i < baseLines; i++)
            {
                var src = lines[i];
                var c = new RailLine
                {
                    point1 = src.point1 + basePoints,
                    point2 = src.point2 + basePoints,
                    angle = src.angle,
                    curveId = src.curveId + baseCurves,
                    cameraId = src.cameraId,
                    tileLength = src.tileLength,
                    name = src.name + "_sky",
                    raw = (byte[])src.raw.Clone()
                };
                BitConverter.GetBytes(c.point1).CopyTo(c.raw, 0);
                BitConverter.GetBytes(c.point2).CopyTo(c.raw, 4);
                BitConverter.GetBytes(c.curveId).CopyTo(c.raw, 12);
                lines.Add(c);
            }
            hasSky = true;
        }

        public void RemoveSky()
        {
            if (!hasSky) return;
            points.RemoveRange(points.Count / 2, points.Count / 2);
            lines.RemoveRange(lines.Count / 2, lines.Count / 2);
            curves.RemoveRange(curves.Count / 2, curves.Count / 2);
            hasSky = false;
        }

        public void SetSkyOffset(int yOffsetFx32)
        {
            if (!hasSky) return;
            int half = points.Count / 2;
            for (int i = 0; i < half; i++)
            {
                points[half + i].y = points[i].y + yOffsetFx32;
                BitConverter.GetBytes(points[half + i].y).CopyTo(points[half + i].raw, 52);
            }
            half = curves.Count / 2;
            for (int i = 0; i < half; i++)
            {
                curves[half + i].y = curves[i].y + yOffsetFx32;
                BitConverter.GetBytes(curves[half + i].y).CopyTo(curves[half + i].raw, 8);
            }
        }

        public byte[] ToBytes()
        {
            // Rebuild container: magic, count=5, offsets, then sections.
            // Sections: 0=info (copy from original), 1=points, 2=lines, 3=cameras (copy), 4=curves.
            if (!rebuildable) return raw ?? new byte[0];
            int count = BitConverter.ToUInt16(raw, 2);
            int[] oldOffs = new int[count + 1];
            for (int i = 0; i < count; i++)
                oldOffs[i] = BitConverter.ToInt32(raw, 4 + i * 4);
            oldOffs[count] = raw.Length;

            var info = Slice(raw, oldOffs[0], oldOffs[1]);
            var cameras = Slice(raw, oldOffs[3], oldOffs[4]);

            var pts = new List<byte>();
            foreach (var p in points) pts.AddRange(p.raw);
            var lns = new List<byte>();
            foreach (var l in lines) lns.AddRange(l.raw);
            var crv = new List<byte>();
            foreach (var c in curves) crv.AddRange(c.raw);

            int header = 4 + count * 4;
            int o0 = header;
            int o1 = o0 + info.Length;
            int o2 = o1 + pts.Count;
            int o3 = o2 + lns.Count;
            int o4 = o3 + cameras.Length;

            var outb = new List<byte>();
            outb.Add((byte)'R');
            outb.Add((byte)'L');
            outb.AddRange(BitConverter.GetBytes((ushort)count));
            outb.AddRange(BitConverter.GetBytes(o0));
            outb.AddRange(BitConverter.GetBytes(o1));
            outb.AddRange(BitConverter.GetBytes(o2));
            outb.AddRange(BitConverter.GetBytes(o3));
            outb.AddRange(BitConverter.GetBytes(o4));
            outb.AddRange(info);
            outb.AddRange(pts);
            outb.AddRange(lns);
            outb.AddRange(cameras);
            outb.AddRange(crv);
            return outb.ToArray();
        }

        static byte[] Slice(byte[] data, int start, int end)
        {
            if (start < 0 || end > data.Length || end < start) return new byte[0];
            var b = new byte[end - start];
            Array.Copy(data, start, b, 0, b.Length);
            return b;
        }
    }

    public class RailPoint
    {
        public int x, y, z;
        public byte[] raw;
    }

    public class RailLine
    {
        public int point1, point2, angle, curveId, cameraId, tileLength;
        public string name;
        public byte[] raw;
    }

    public class RailCurve
    {
        public int type, x, y, z;
        public byte[] raw;
    }

    public class RailTilemap
    {
        public List<byte[]> blocks = new List<byte[]>();
        public byte[] raw;
        // Set when every declared block parsed and nothing follows them;
        // otherwise ToBytes returns the original bytes.
        public bool rebuildable;

        public static RailTilemap Parse(byte[] data)
        {
            var t = new RailTilemap { raw = data };
            if (data == null || data.Length < 4) return t;
            int count = BitConverter.ToInt32(data, 0);
            int pos = 4;
            for (int i = 0; i < count && pos + 4 <= data.Length; i++)
            {
                int w = BitConverter.ToInt16(data, pos);
                int h = BitConverter.ToInt16(data, pos + 2);
                if (w < 0 || h < 0) break;
                int size = 4 + w * h * 4;
                if (pos + size > data.Length) break;
                var b = new byte[size];
                Array.Copy(data, pos, b, 0, size);
                t.blocks.Add(b);
                pos += size;
            }
            t.rebuildable = count >= 0 && t.blocks.Count == count && pos == data.Length;
            return t;
        }

        public void AddLayerBlocks(int groundCount)
        {
            int n = Math.Min(groundCount, blocks.Count);
            for (int i = 0; i < n; i++)
            {
                var copy = (byte[])blocks[i].Clone();
                for (int t = 4; t + 3 < copy.Length; t += 4)
                    copy[t + 2] = 0;
                blocks.Add(copy);
            }
        }

        public void AddSkyClone()
        {
            int n = blocks.Count;
            for (int i = 0; i < n; i++)
            {
                var copy = (byte[])blocks[i].Clone();
                // Zero coll byte on every tile so NPCs can move freely. Tile is 4 bytes: type(2), coll(1), shadow(1).
                for (int t = 4; t + 3 < copy.Length; t += 4)
                    copy[t + 2] = 0;
                blocks.Add(copy);
            }
        }

        public void RemoveSky()
        {
            if (blocks.Count < 2) return;
            blocks.RemoveRange(blocks.Count / 2, blocks.Count / 2);
        }

        public byte[] ToBytes()
        {
            if (!rebuildable) return raw ?? new byte[0];
            var outb = new List<byte>();
            outb.AddRange(BitConverter.GetBytes(blocks.Count));
            foreach (var b in blocks) outb.AddRange(b);
            return outb.ToArray();
        }
    }
}
