using System;

namespace NewEditor.Data
{
    /// <summary>
    /// KEY1 (Blowfish-derived) secure-area encryption, matching Pokeweb's key1.ts.
    /// Used so DSi digest hashing sees the cartridge ciphertext even when the
    /// dump stores the decrypted secure-area marker.
    /// </summary>
    public static class Key1
    {
        // Common KEY1 table, base64. Same constant Pokeweb embeds.
        const string EncodedTable =
            "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";

        static uint[] Seed()
        {
            byte[] bytes = Convert.FromBase64String(EncodedTable);
            uint[] table = new uint[1042];
            for (int i = 0; i < 1042; i++)
                table[i] = (uint)(bytes[i * 4] | (bytes[i * 4 + 1] << 8) | (bytes[i * 4 + 2] << 16) | (bytes[i * 4 + 3] << 24));
            return table;
        }

        static uint SwapBytes(uint word) =>
            (word >> 24) | ((word >> 8) & 0xFF00) | ((word << 8) & 0xFF0000) | (word << 24);

        class Cipher
        {
            readonly uint[] table;
            public Cipher(uint[] seed, uint gameCode, int level)
            {
                table = (uint[])seed.Clone();
                uint[] code = { gameCode, gameCode >> 1, gameCode << 1 };
                Expand(code);
                Expand(code);
                if (level == 3)
                {
                    code[1] <<= 1; code[2] >>= 1;
                    Expand(code);
                }
            }
            public void Encrypt(uint first, uint second, out uint outFirst, out uint outSecond)
            {
                uint left = second, right = first;
                for (int round = 0; round < 16; round++)
                {
                    uint word = table[round] ^ left;
                    uint a = unchecked(table[18 + (word >> 24)] + table[274 + ((word >> 16) & 255)]);
                    uint b = unchecked((a ^ table[530 + ((word >> 8) & 255)]) + table[786 + (word & 255)]);
                    left = b ^ right;
                    right = word;
                }
                outFirst = left ^ table[16];
                outSecond = right ^ table[17];
            }
            void Expand(uint[] code)
            {
                Encrypt(code[1], code[2], out uint f, out uint s);
                code[1] = f; code[2] = s;
                Encrypt(code[0], code[1], out f, out s);
                code[0] = f; code[1] = s;
                for (int i = 0; i < 18; i++) table[i] ^= SwapBytes(code[i % 2]);
                uint first = 0, second = 0;
                for (int i = 0; i < table.Length; i += 2)
                {
                    Encrypt(first, second, out f, out s);
                    first = f; second = s;
                    table[i] = second;
                    table[i + 1] = first;
                }
            }
        }

        /// <summary>
        /// The secure-area bytes a native cartridge exposes to DSi integrity reads.
        /// Input must be the 0x800 decrypted secure area (with the decrypted marker).
        /// </summary>
        public static byte[] EncryptSecureArea(byte[] decrypted, uint gameCode)
        {
            if (decrypted.Length != 0x800) throw new ArgumentException("Secure area must be 0x800 bytes");
            byte[] outBuf = (byte[])decrypted.Clone();
            // "encryObj"
            outBuf[0] = (byte)'e'; outBuf[1] = (byte)'n'; outBuf[2] = (byte)'c'; outBuf[3] = (byte)'r';
            outBuf[4] = (byte)'y'; outBuf[5] = (byte)'O'; outBuf[6] = (byte)'b'; outBuf[7] = (byte)'j';
            var seed = Seed();
            var level3 = new Cipher(seed, gameCode, 3);
            for (int at = 0; at < outBuf.Length; at += 8)
            {
                uint first = ReadU32(outBuf, at), second = ReadU32(outBuf, at + 4);
                level3.Encrypt(first, second, out first, out second);
                WriteU32(outBuf, at, first); WriteU32(outBuf, at + 4, second);
            }
            var level2 = new Cipher(seed, gameCode, 2);
            uint f0 = ReadU32(outBuf, 0), s0 = ReadU32(outBuf, 4);
            level2.Encrypt(f0, s0, out f0, out s0);
            WriteU32(outBuf, 0, f0); WriteU32(outBuf, 4, s0);
            return outBuf;
        }

        static uint ReadU32(byte[] b, int o) => (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24));
        static void WriteU32(byte[] b, int o, uint v)
        {
            b[o] = (byte)v; b[o + 1] = (byte)(v >> 8); b[o + 2] = (byte)(v >> 16); b[o + 3] = (byte)(v >> 24);
        }
    }
}
