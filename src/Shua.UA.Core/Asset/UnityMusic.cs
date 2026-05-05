#pragma warning disable IDE1006
#pragma warning disable IDE0130

using System;
using System.IO;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace Shua.UA.Core.Asset;

public class UnityMusic : UnityAsset
{
    public float Length { get; private set; }
    public Stream Data { get; private set; } = null!;

    protected internal override void Init(Stream bundleStream)
    {
        base.Init(bundleStream);

        var field = FindAssetField(AssetClassID.AudioClip)
                    ?? throw new ArgumentException("No AudioClip found.", nameof(bundleStream));

        Length = field["m_Length"].AsFloat;

        var dataOffset = field["m_Resource"]["m_Offset"].AsLong;
        var size = field["m_Resource"]["m_Size"].AsLong;

        Bundle.GetFileRange(1, out var dataFileOffset, out _);
        var offset = dataFileOffset + dataOffset;

        Data = new SegmentStream(Bundle.DataReader.BaseStream, offset, size);
    }

    protected override void Dispose(bool disposing)
    {
        if (Disposed)
            return;

        if (disposing) Data.Dispose();

        base.Dispose(disposing);
    }
}