using System;
using AssetsTools.NET;
#if !NET7_0_OR_GREATER
using System.IO;

#else
#endif

namespace Shua.UA.Core;

public static class Extensions
{
    internal static AssetTypeValueField GetBaseField(this AssetsFile file, AssetFileInfo info)
    {
        lock (file.Reader)
        {
            var offset = info.GetAbsoluteByteOffset(file);

            if (!file.Metadata.TypeTreeEnabled)
                throw new Exception($"Failed to build template for type {info.TypeId}");
            var tt = file.Metadata.FindTypeTreeTypeByID(info.TypeId, info.GetScriptIndex(file));
            if (tt == null || tt.Nodes.Count <= 0)
                throw new Exception($"Failed to build template for type {info.TypeId}");
            AssetTypeTemplateField template = new();
            template.FromTypeTree(tt);

            RefTypeManager refMan = new();
            refMan.FromTypeTree(file.Metadata);

            return template.MakeValue(file.Reader, offset, refMan);
        }
    }

#if !NET7_0_OR_GREATER
    public static void ReadExactly(this Stream stream, byte[] buffer, int offset, int count)
    {
        var totalRead = 0;
        while (totalRead < count)
        {
            var read = stream.Read(buffer, offset + totalRead, count - totalRead);
            if (read == 0)
                throw new EndOfStreamException();

            totalRead += read;
        }
    }
#else
#endif
}