using System;
using System.Collections.Generic;

namespace NewEditor.Data
{
    /// <summary>
    /// DSi digest table rebuild, matching Pokeweb's dsiDigest.ts.
    /// Locates the HMAC key in decompressed ARM9, validates the source tables,
    /// and rebuilds native sector/block/master hashes for a relocated layout.
    /// </summary>
    public class DsiDigestSource
    {
        public int NtrStart;
        public int TwlStart;
        public int TwlSize;
        public int RegionStart;
        public int RegionSize;
        public int SectorSize;
        public int BlockSectors;
        public byte[] TwlHashes;   // authenticated TWL sector hashes (reused as-is)
        public byte[] TwlRegion;   // complete TWL span (code + padding), copied unchanged
        public HmacSha1 Hmac;
        // Relative offsets of ARM9i/ARM7i within the TWL region, for header update
        public int Arm9iOffsetInRegion = -1;
        public int Arm9iSize;
        public int Arm7iOffsetInRegion = -1;
        public int Arm7iSize;
        public int Modcrypt1OffsetInRegion = -1;
        public int Modcrypt1Size;
        public int Modcrypt2OffsetInRegion = -1;
        public int Modcrypt2Size;
    }

    public class DsiDigestLayout
    {
        public int NtrSize;
        public int SectorOffset;
        public int SectorLength;
        public int BlockOffset;
        public int BlockLength;
        public int End;
    }

    public static class DsiDigest
    {
        const int NtrTwlAlignment = 0x80000;

        static uint ReadU32(byte[] b, int o) =>
            (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24));
        static ushort ReadU16(byte[] b, int o) =>
            (ushort)(b[o] | (b[o + 1] << 8));
        static void WriteU32(byte[] b, int o, uint v)
        {
            b[o] = (byte)v; b[o + 1] = (byte)(v >> 8); b[o + 2] = (byte)(v >> 16); b[o + 3] = (byte)(v >> 24);
        }
        static int Align(int value, int alignment) => (value + alignment - 1) / alignment * alignment;
        static bool PowerOfTwo(int v) => v > 0 && (v & (v - 1)) == 0;
        static bool Span(byte[] src, int start, int length) =>
            start > 0 && length > 0 && (long)start + length <= src.Length;

        /// <summary>
        /// Try to read and authenticate the DSi digest source from a full ROM.
        /// Returns null if the ROM is not DSi-extended or the tables do not authenticate.
        /// </summary>
        public static DsiDigestSource TryRead(byte[] rom)
        {
            if (rom.Length < 0x4000) return null;
            if (rom[0x12] != 2) return null; // not DSi-enhanced

            bool hasDigest = false;
            for (int f = 0x1E0; f <= 0x204; f += 4)
                if (ReadU32(rom, f) != 0) { hasDigest = true; break; }
            if (!hasDigest) return null;

            int ntrStart = (int)ReadU32(rom, 0x1E0);
            int ntrSize = (int)ReadU32(rom, 0x1E4);
            int twlStart = (int)ReadU32(rom, 0x1E8);
            int twlSize = (int)ReadU32(rom, 0x1EC);
            int sectorOffset = (int)ReadU32(rom, 0x1F0);
            int sectorLength = (int)ReadU32(rom, 0x1F4);
            int blockOffset = (int)ReadU32(rom, 0x1F8);
            int blockLength = (int)ReadU32(rom, 0x1FC);
            int sectorSize = (int)ReadU32(rom, 0x200);
            int blockSectors = (int)ReadU32(rom, 0x204);

            if (!PowerOfTwo(sectorSize) || sectorSize < 0x200 || sectorSize > 0x100000) return null;
            if (!PowerOfTwo(blockSectors) || blockSectors > 1024) return null;
            if (!Span(rom, ntrStart, ntrSize) || !Span(rom, twlStart, twlSize)) return null;
            if (!Span(rom, sectorOffset, sectorLength) || !Span(rom, blockOffset, blockLength)) return null;
            if (ntrSize % sectorSize != 0 || twlSize % sectorSize != 0) return null;

            int ntrSectors = ntrSize / sectorSize;
            int twlSectors = twlSize / sectorSize;
            int expectedSectors = Align(ntrSectors + twlSectors, blockSectors) * 20;
            if (sectorLength != expectedSectors || blockLength != expectedSectors / blockSectors) return null;

            int regionStart = ReadU16(rom, 0x92) * NtrTwlAlignment;
            if (regionStart == 0 || regionStart > twlStart) return null;

            // Locate HMAC key in decompressed ARM9
            int arm9Off = (int)ReadU32(rom, 0x20);
            int arm9Len = (int)ReadU32(rom, 0x2C);
            if (!Span(rom, arm9Off, arm9Len)) return null;
            byte[] arm9 = new byte[arm9Len];
            Array.Copy(rom, arm9Off, arm9, 0, arm9Len);
            byte[] decompressed = BLZDecoder.BLZ_DecodePub(arm9) ?? arm9;

            byte[] sectorTable = new byte[sectorLength];
            Array.Copy(rom, sectorOffset, sectorTable, 0, sectorLength);
            byte[] expectedBlock = new byte[20];
            Array.Copy(rom, blockOffset, expectedBlock, 0, 20);

            HmacSha1 hmac = FindHmacKey(decompressed, sectorTable, blockSectors, expectedBlock);
            if (hmac == null) return null;

            // Authenticate master and block table
            byte[] master = hmac.Digest(Slice(rom, blockOffset, blockLength));
            for (int i = 0; i < 20; i++)
                if (master[i] != rom[0x328 + i]) return null;

            byte[] result = new byte[20];
            for (int at = 0; at < sectorLength; at += blockSectors * 20)
            {
                hmac.Digest(Slice(sectorTable, at, blockSectors * 20), result, 0);
                int blockAt = blockOffset + at / blockSectors;
                for (int i = 0; i < 20; i++)
                    if (result[i] != rom[blockAt + i]) return null;
            }

            int regionSize = twlStart + twlSize - regionStart;
            var src = new DsiDigestSource
            {
                NtrStart = ntrStart,
                TwlStart = twlStart,
                TwlSize = twlSize,
                RegionStart = regionStart,
                RegionSize = regionSize,
                SectorSize = sectorSize,
                BlockSectors = blockSectors,
                TwlHashes = Slice(sectorTable, ntrSectors * 20, twlSectors * 20),
                TwlRegion = Slice(rom, regionStart, regionSize),
                Hmac = hmac
            };

            int arm9i = (int)ReadU32(rom, 0x1C0), arm9iSize = (int)ReadU32(rom, 0x1CC);
            int arm7i = (int)ReadU32(rom, 0x1D0), arm7iSize = (int)ReadU32(rom, 0x1DC);
            if (arm9i > 0 && arm9iSize > 0 && arm9i >= regionStart && arm9i + arm9iSize <= regionStart + regionSize)
            {
                src.Arm9iOffsetInRegion = arm9i - regionStart;
                src.Arm9iSize = arm9iSize;
            }
            if (arm7i > 0 && arm7iSize > 0 && arm7i >= regionStart && arm7i + arm7iSize <= regionStart + regionSize)
            {
                src.Arm7iOffsetInRegion = arm7i - regionStart;
                src.Arm7iSize = arm7iSize;
            }
            
            int mc1 = (int)ReadU32(rom, 0x220), mc1s = (int)ReadU32(rom, 0x224);
            int mc2 = (int)ReadU32(rom, 0x228), mc2s = (int)ReadU32(rom, 0x22C);
            if (mc1 > 0 && mc1s > 0 && mc1 >= regionStart && mc1 + mc1s <= regionStart + regionSize)
            { src.Modcrypt1OffsetInRegion = mc1 - regionStart; src.Modcrypt1Size = mc1s; }
            if (mc2 > 0 && mc2s > 0 && mc2 >= regionStart && mc2 + mc2s <= regionStart + regionSize)
            { src.Modcrypt2OffsetInRegion = mc2 - regionStart; src.Modcrypt2Size = mc2s; }
            return src;
        }

        static HmacSha1 FindHmacKey(byte[] arm9, byte[] sectorTable, int blockSectors, byte[] expected)
        {
            byte[] probe = Slice(sectorTable, 0, blockSectors * 20);
            // Known hints for some English B2/W2 codes; scan if they miss
            var hints = new Dictionary<string, int>(); // filled by caller if desired
            for (int at = 0; at + 64 <= arm9.Length; at += 4)
            {
                var h = HmacSha1.FromKey(Slice(arm9, at, 64));
                byte[] got = h.Digest(probe);
                bool match = true;
                for (int i = 0; i < 20; i++) if (got[i] != expected[i]) { match = false; break; }
                if (match) return h;
            }
            return null;
        }

        public static DsiDigestLayout Plan(DsiDigestSource src, int fileEnd)
        {
            int sectorOffset = Align(fileEnd, src.SectorSize);
            if (sectorOffset <= src.NtrStart) return null;
            int ntrSize = sectorOffset - src.NtrStart;
            int sectors = (ntrSize + src.TwlSize) / src.SectorSize;
            int sectorLength = Align(sectors, src.BlockSectors) * 20;
            int blockOffset = Align(sectorOffset + sectorLength, 0x200);
            int blockLength = sectorLength / src.BlockSectors;
            return new DsiDigestLayout
            {
                NtrSize = ntrSize,
                SectorOffset = sectorOffset,
                SectorLength = sectorLength,
                BlockOffset = blockOffset,
                BlockLength = blockLength,
                End = Align(blockOffset + blockLength, 4)
            };
        }

        /// <summary>
        /// Write rebuilt digest tables into an already-laid-out ROM and update header fields.
        /// twlRegionStart is the absolute offset where the preserved TWL region was written.
        /// </summary>
        public static void Write(byte[] rom, DsiDigestSource src, DsiDigestLayout layout, int twlRegionStart)
        {
            var hash = src.Hmac;
            int entry = layout.SectorOffset;

            // Re-encrypt secure area for hashing if the dump stores the decrypted marker
            byte[] secure = null;
            if (src.NtrStart == 0x4000 && rom.Length >= 0x4800 &&
                ReadU32(rom, 0x4000) == 0xE7FFDEFF && ReadU32(rom, 0x4004) == 0xE7FFDEFF)
            {
                int secureLen = Align(0x800, src.SectorSize);
                secure = new byte[secureLen];
                Array.Copy(rom, 0x4000, secure, 0, Math.Min(secureLen, rom.Length - 0x4000));
                byte[] enc = Key1.EncryptSecureArea(Slice(rom, 0x4000, 0x800), ReadU32(rom, 0x0C));
                Array.Copy(enc, 0, secure, 0, 0x800);
            }

            byte[] sectorBuf = new byte[src.SectorSize];
            for (int at = src.NtrStart; at < src.NtrStart + layout.NtrSize; at += src.SectorSize)
            {
                int index = at - src.NtrStart;
                byte[] chunk;
                if (secure != null && index < secure.Length)
                    chunk = Slice(secure, index, src.SectorSize);
                else
                    chunk = Slice(rom, at, src.SectorSize);
                hash.Digest(chunk, rom, entry);
                entry += 20;
            }

            // Reuse authenticated TWL sector hashes
            Array.Copy(src.TwlHashes, 0, rom, entry, src.TwlHashes.Length);

            byte[] blockProbe = new byte[src.BlockSectors * 20];
            for (int at = 0; at < layout.SectorLength; at += src.BlockSectors * 20)
            {
                Array.Copy(rom, layout.SectorOffset + at, blockProbe, 0, blockProbe.Length);
                hash.Digest(blockProbe, rom, layout.BlockOffset + at / src.BlockSectors);
            }
            hash.Digest(Slice(rom, layout.BlockOffset, layout.BlockLength), rom, 0x328);

            WriteU32(rom, 0x1E0, (uint)src.NtrStart);
            WriteU32(rom, 0x1E4, (uint)layout.NtrSize);
            WriteU32(rom, 0x1E8, (uint)(twlRegionStart + src.TwlStart - src.RegionStart));
            WriteU32(rom, 0x1EC, (uint)src.TwlSize);
            WriteU32(rom, 0x1F0, (uint)layout.SectorOffset);
            WriteU32(rom, 0x1F4, (uint)layout.SectorLength);
            WriteU32(rom, 0x1F8, (uint)layout.BlockOffset);
            WriteU32(rom, 0x1FC, (uint)layout.BlockLength);
        }

        static byte[] Slice(byte[] src, int offset, int length)
        {
            byte[] r = new byte[length];
            int n = Math.Min(length, src.Length - offset);
            if (n > 0) Array.Copy(src, offset, r, 0, n);
            return r;
        }
    }
}
