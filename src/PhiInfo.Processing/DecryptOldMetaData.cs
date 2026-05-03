// ReSharper disable InconsistentNaming

using System;
using System.Buffers.Binary;

namespace PhiInfo.Processing;

public static class DecryptOldMetaData
{
    private const uint RC4_METADATA_MAGIC = 0xBCFAF088;
    private const uint RC4_KEY_MAGIC = 0x567FE814;
    private const int CHUNK_HDR = 0x10;

    public static byte[] Decrypt(ReadOnlySpan<byte> data)
    {
        var meta = FindChunk(data, RC4_METADATA_MAGIC);
        var key = FindChunk(data, RC4_KEY_MAGIC);

        if (meta == null || key == null)
            throw new InvalidOperationException("Required chunks not found");

        var (_, metaSize, metaDataOff) = meta.Value;
        var (_, keySize, keyDataOff) = key.Value;

        var rc4Key = data.Slice(keyDataOff, keySize).ToArray();
        
        var metadata = data.Slice(metaDataOff, metaSize).ToArray();
        Rc4CryptInPlace(metadata, rc4Key);
        
        var stringDataOffset = BinaryPrimitives.ReadUInt32LittleEndian(metadata.AsSpan(24));
        var stringDataSize   = BinaryPrimitives.ReadUInt32LittleEndian(metadata.AsSpan(28));

        DecryptStrings(metadata, (int)stringDataOffset, (int)stringDataSize);

        return metadata;
    }

    private static (int offset, int size, int dataOffset)? FindChunk(ReadOnlySpan<byte> buf, uint magic)
    {
        var pos = 0;

        while (pos + 8 <= buf.Length)
        {
            var curMagic = BinaryPrimitives.ReadUInt32LittleEndian(buf.Slice(pos, 4));
            var curSize  = BinaryPrimitives.ReadUInt32LittleEndian(buf.Slice(pos + 4, 4));

            if (curMagic == magic)
                return (pos, (int)curSize, pos + CHUNK_HDR);

            pos += CHUNK_HDR + (int)curSize;
        }

        return null;
    }

    private static void Rc4CryptInPlace(Span<byte> data, ReadOnlySpan<byte> key)
    {
        Span<byte> S = stackalloc byte[256];

        for (var i = 0; i < 256; i++)
            S[i] = (byte)i;

        var j = 0;
        for (var i = 0; i < 256; i++)
        {
            j = (j + S[i] + key[i % key.Length]) & 0xFF;
            (S[i], S[j]) = (S[j], S[i]);
        }

        var iIdx = 0;
        j = 0;

        for (var pos = 0; pos < data.Length; pos++)
        {
            iIdx = (iIdx + 1) & 0xFF;
            j = (j + S[iIdx]) & 0xFF;

            (S[iIdx], S[j]) = (S[j], S[iIdx]);

            var k = S[(S[iIdx] + S[j]) & 0xFF];
            data[pos] ^= k;
        }
    }

    private static void DecryptStrings(byte[] metadata, int strOffset, int strSize)
    {
        var pos = 0;

        while (pos < strSize)
        {
            var xorKey = pos % 0xFF;

            while (true)
            {
                var idx = strOffset + pos;
                var enc = metadata[idx];

                xorKey ^= enc;
                metadata[idx] = (byte)(xorKey & 0xFF);

                pos++;

                if ((xorKey & 0xFF) == 0)
                    break;
            }
        }
    }
}