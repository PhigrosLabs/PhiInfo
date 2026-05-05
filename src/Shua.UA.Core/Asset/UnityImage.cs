#pragma warning disable IDE1006
#pragma warning disable IDE0130

using System;
using System.IO;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace Shua.UA.Core.Asset;

public class UnityImage : UnityAsset
{
    public uint Format { get; private set; }

    public int Width { get; private set; }
    public int Height { get; private set; }
    public Stream Data { get; private set; } = null!;

    protected internal override void Init(Stream bundleStream)
    {
        base.Init(bundleStream);
        var field = FindAssetField(AssetClassID.Texture2D) ??
                    throw new ArgumentException("No Texture2D found.", nameof(bundleStream));

        Width = field["m_Width"].AsInt;
        Height = field["m_Height"].AsInt;
        Format = field["m_TextureFormat"].AsUInt;

        var dataOffset = field["m_StreamData"]["offset"].AsLong;
        var size = field["m_StreamData"]["size"].AsLong;

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