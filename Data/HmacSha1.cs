using System;

namespace NewEditor.Data
{
    /// <summary>
    /// SHA1-HMAC matching Pokeweb's src/nds/sha1.ts (big-endian word processing).
    /// Used for DSi digest table rebuilds.
    /// </summary>
    public class HmacSha1
    {
        static readonly uint[] InitialState = { 0x67452301, 0xEFCDAB89, 0x98BADCFE, 0x10325476, 0xC3D2E1F0 };

        readonly uint[] inner = new uint[5];
        readonly uint[] outer = new uint[5];
        readonly uint[] state = new uint[5];
        readonly uint[] words = new uint[80];
        readonly byte[] tail = new byte[128];
        readonly byte[] intermediate = new byte[20];

        HmacSha1() { }

        public static HmacSha1 FromKey(byte[] key)
        {
            var h = new HmacSha1();
            byte[] bytes = key.Length > 64 ? Sha1(key) : key;
            byte[] pad = new byte[64];

            Array.Copy(InitialState, h.inner, 5);
            for (int i = 0; i < 64; i++) pad[i] = (byte)((i < bytes.Length ? bytes[i] : 0) ^ 0x36);
            Compress(h.inner, pad, 0, h.words);

            Array.Copy(InitialState, h.outer, 5);
            for (int i = 0; i < 64; i++) pad[i] = (byte)((i < bytes.Length ? bytes[i] : 0) ^ 0x5C);
            Compress(h.outer, pad, 0, h.words);

            return h;
        }

        public byte[] Digest(byte[] data)
        {
            byte[] outBuf = new byte[20];
            Digest(data, outBuf, 0);
            return outBuf;
        }

        public void Digest(byte[] data, byte[] outBuf, int offset)
        {
            Array.Copy(inner, state, 5);
            Finish(state, data, 64, words, tail, intermediate, 0);
            Array.Copy(outer, state, 5);
            Finish(state, intermediate, 64, words, tail, outBuf, offset);
        }

        public static byte[] Sha1(byte[] data)
        {
            byte[] outBuf = new byte[20];
            uint[] st = (uint[])InitialState.Clone();
            Finish(st, data, 0, new uint[80], new byte[128], outBuf, 0);
            return outBuf;
        }

        static void Compress(uint[] st, byte[] bytes, int offset, uint[] words)
        {
            for (int i = 0; i < 16; i++)
            {
                int at = offset + i * 4;
                words[i] = (uint)((bytes[at] << 24) | (bytes[at + 1] << 16) | (bytes[at + 2] << 8) | bytes[at + 3]);
            }
            for (int i = 16; i < 80; i++)
            {
                uint word = words[i - 3] ^ words[i - 8] ^ words[i - 14] ^ words[i - 16];
                words[i] = (word << 1) | (word >> 31);
            }
            uint a = st[0], b = st[1], c = st[2], d = st[3], e = st[4];
            for (int i = 0; i < 80; i++)
            {
                uint f = i < 20 ? (b & c) | (~b & d)
                       : i < 40 ? b ^ c ^ d
                       : i < 60 ? (b & c) | (b & d) | (c & d)
                       : b ^ c ^ d;
                uint k = i < 20 ? 0x5A827999u
                       : i < 40 ? 0x6ED9EBA1u
                       : i < 60 ? 0x8F1BBCDCu
                       : 0xCA62C1D6u;
                uint next = unchecked((uint)(((a << 5) | (a >> 27)) + f + e + k + words[i]));
                e = d; d = c; c = (b << 30) | (b >> 2); b = a; a = next;
            }
            st[0] += a; st[1] += b; st[2] += c; st[3] += d; st[4] += e;
        }

        static void WriteBigEndian(byte[] outBuf, int offset, uint value)
        {
            outBuf[offset] = (byte)(value >> 24);
            outBuf[offset + 1] = (byte)(value >> 16);
            outBuf[offset + 2] = (byte)(value >> 8);
            outBuf[offset + 3] = (byte)value;
        }

        static void Finish(uint[] st, byte[] data, int prefixLength, uint[] words, byte[] tail, byte[] outBuf, int offset)
        {
            int fullLength = data.Length - data.Length % 64;
            for (int at = 0; at < fullLength; at += 64) Compress(st, data, at, words);
            int remaining = data.Length - fullLength;
            int tailLength = remaining < 56 ? 64 : 128;
            Array.Clear(tail, 0, tail.Length);
            if (remaining > 0) Array.Copy(data, fullLength, tail, 0, remaining);
            tail[remaining] = 0x80;
            ulong bits = (ulong)(prefixLength + data.Length) * 8;
            WriteBigEndian(tail, tailLength - 8, (uint)(bits >> 32));
            WriteBigEndian(tail, tailLength - 4, (uint)bits);
            Compress(st, tail, 0, words);
            if (tailLength == 128) Compress(st, tail, 64, words);
            for (int i = 0; i < 5; i++) WriteBigEndian(outBuf, offset + i * 4, st[i]);
        }
    }
}
