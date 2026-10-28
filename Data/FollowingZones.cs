using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NewEditor.Data
{
    public partial class FollowingData
    {
        public const int AreaNarcID = 13; // a/0/1/3
        public const int AreaRecordSize = 10, AreaExteriorOffset = 6, AreaCount = 0x199;
        public const int ZoneLargeSwap = 1, MaxZoneOverrides = 256;

        /// <summary>
        /// Whether an area is outdoors, from its a/0/1/3 record, or null when
        /// the record can't be read. The game reads the file raw, as 10-byte
        /// records, and checks the spring record as the runtime does.
        /// </summary>
        public bool? AreaIsExterior(int areaId)
        {
            if (AreaNarcID >= fileSystem.narcs.Count) return null;
            byte[] areas = fileSystem.narcs[AreaNarcID].byteData;
            if (areas != null && areas.Length >= 4 && Encoding.ASCII.GetString(areas, 0, 4) == "NARC")
                areas = Member(areas, 0, out _);
            if (areaId < 0 || areaId >= AreaCount) areaId = 0;
            int at = areaId * AreaRecordSize + AreaExteriorOffset;
            if (areas == null || at >= areas.Length) return null;
            return areas[at] != 0;
        }

        /// <summary>The zone's override flags in positioning.narc, 0 for none.</summary>
        public int ZoneFlags(int zone)
        {
            if (Positioning == null) return 0;
            foreach (var row in Positioning.ZoneOverrides)
                if (row.zone == zone) return row.flags;
            return 0;
        }

        /// <summary>
        /// Set or clear the zone's indoor/outdoor swap in positioning.narc
        /// member 1, keeping its other flags and every other member. A zone
        /// left with no flags loses its row. Save the ROM to keep the result.
        /// </summary>
        public void SetZoneSwap(int zone, bool swap)
        {
            byte[] file = ReadFile(fileSystem, PositioningPath);
            if (file == null || Positioning == null) throw new InvalidOperationException("This ROM has no usable follower positioning.narc.");
            if (zone < 0 || zone > 0xFFFF) throw new ArgumentOutOfRangeException(nameof(zone));

            var rows = Positioning.ZoneOverrides.Where(r => r.zone != zone).ToList();
            int flags = (ZoneFlags(zone) & ~ZoneLargeSwap) | (swap ? ZoneLargeSwap : 0);
            if (flags != 0) rows.Add((zone, flags));
            rows.Sort((a, b) => a.zone.CompareTo(b.zone));
            if (rows.Count > MaxZoneOverrides) throw new InvalidOperationException("The runtime allows at most " + MaxZoneOverrides + " zone overrides.");

            var member = new byte[8 + rows.Count * 4];
            W32(member, 0, 0x4F5A5746);
            member[4] = 1;
            member[6] = (byte)rows.Count; member[7] = (byte)(rows.Count >> 8);
            for (int i = 0; i < rows.Count; i++)
            {
                member[8 + i * 4] = (byte)rows[i].zone;
                member[9 + i * 4] = (byte)(rows[i].zone >> 8);
                member[10 + i * 4] = (byte)rows[i].flags;
            }

            List<byte[]> members = NarcFile.Read(file, out byte[] names);
            if (members.Count > 1) members[1] = member; else members.Add(member);
            byte[] positioning = NarcFile.Write(members, names);
            FollowerPositioning decoded = FollowerPositioning.Decode(positioning, RegistryBytes, Registry);

            WriteFile(PositioningPath, positioning);
            UpdateReceipt("positioningSha256", positioning);
            Positioning = decoded;
        }
    }
}
