using System;
using System.IO;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace Shua.UA.Core.Asset;

public abstract class UnityAsset : IDisposable
{
    protected AssetBundleFile Bundle = new();
    protected bool Disposed;
    protected AssetsFile Info = new();

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected internal virtual void Init(Stream bundleStream)
    {
        var reader = new AssetsFileReader(bundleStream);
        Bundle.Read(reader);

        if (Bundle.DataIsCompressed)
            Bundle = BundleHelper.UnpackBundle(Bundle);

        Bundle.GetFileRange(0, out var offset, out var size);
        var stream = new SegmentStream(Bundle.DataReader.BaseStream, offset, size);
        Info.Read(stream);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            Bundle.Close();
            Info.Close();
        }

        Disposed = true;
    }

    protected AssetTypeValueField? FindAssetField(AssetClassID type)
    {
        foreach (var info in Info.AssetInfos)
            if (info.TypeId == (int)type)
                return Info.GetBaseField(info);
        return null;
    }
}