#pragma warning disable IDE1006
#pragma warning disable IDE0130

using System;
using System.IO;
using AssetsTools.NET.Extra;

namespace Shua.UA.Core.Asset;

public class UnityText : UnityAsset
{
    public string Content { get; private set; } = string.Empty;

    protected internal override void Init(Stream bundleStream)
    {
        base.Init(bundleStream);

        var field = FindAssetField(AssetClassID.TextAsset)
                    ?? throw new ArgumentException("No TextAsset found.", nameof(bundleStream));

        Content = field["m_Script"].AsString;
    }

    protected override void Dispose(bool disposing)
    {
        if (Disposed)
            return;

        base.Dispose(disposing);
    }
}