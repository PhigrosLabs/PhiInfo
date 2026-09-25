using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using PhiInfo.Core.Type;
using Shua.Zip;

namespace PhiInfo.Processing.DataProvider;

public class AndroidPackagesDataProvider(IEnumerable<ShuaZip> zips, Stream cldbStream) : IDataProvider
{
    private const string DataPrefix = "assets/bin/Data/";
    private const string RuntimePathPlaceholder = "{UnityEngine.AddressableAssets.Addressables.RuntimePath}";
    private bool _disposed;

    public Stream GetCldb()
    {
        var ms = new MemoryStream();
        cldbStream.CopyTo(ms);
        ms.Position = 0;
        return ms;
    }

    public Stream GetGlobalGameManagers()
    {
        var (zip, entry) = FindEntryInAllZips("assets/bin/Data/globalgamemanagers.assets");
        return EnsureSeekable(zip.OpenFileStream(entry));
    }

    public byte[] GetIl2CppBinary()
    {
        var (zip, entry) = FindEntryInAllZips("lib/arm64-v8a/libil2cpp.so");
        return zip.ReadFile(entry);
    }

    public byte[] GetGlobalMetadata()
    {
        if (TryFindEntryInAllZips("assets/bin/Data/Managed/Metadata/game.dat", out var zip, out var entry))
        {
            var data = zip.ReadFile(entry);
            return DecryptOldMetaData.Decrypt(data);
        }

        var (zip2, entry2) = FindEntryInAllZips("assets/bin/Data/Managed/Metadata/global-metadata.dat");
        return zip2.ReadFile(entry2);
    }

    public Stream GetDataFile(string name)
    {
        if (TryFindEntryInAllZips(DataPrefix + name, out var zip, out var entry))
            return EnsureSeekable(zip.OpenFileStream(entry));

        // 旧版本会把 level 等文件切成 <name>.splitN 分片
        var partPrefix = DataPrefix + name + ".split";
        var parts = new List<(int index, string name, ShuaZip zip)>();

        foreach (var item in zips)
        {
            foreach (var fileEntry in item.Eocd.FileEntries)
            {
                if (!fileEntry.Name.StartsWith(partPrefix, StringComparison.Ordinal))
                    continue;

                var suffix = fileEntry.Name[partPrefix.Length..];
                if (int.TryParse(suffix, out var index))
                    parts.Add((index, fileEntry.Name, item));
            }
        }

        if (parts.Count == 0)
            throw new FileNotFoundException($"Required Unity asset '{DataPrefix}{name}' missing from provided packages.");

        parts.Sort((a, b) => a.index.CompareTo(b.index));

        MemoryStream data = new();

        foreach (var (_, partName, partZip) in parts)
        {
            var part = partZip.ReadFileByName(partName);
            data.Write(part, 0, part.Length);
        }

        data.Position = 0;
        return data;
    }

    public IReadOnlyList<string> GetDataFileNames()
    {
        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var zip in zips)
        {
            foreach (var entry in zip.Eocd.FileEntries)
            {
                if (!entry.Name.StartsWith(DataPrefix, StringComparison.Ordinal))
                    continue;

                var name = entry.Name[DataPrefix.Length..];

                // 跳过子目录与 .resource 资源流
                if (name.Contains('/') || name.EndsWith(".resource", StringComparison.Ordinal))
                    continue;

                var splitIndex = name.IndexOf(".split", StringComparison.Ordinal);
                if (splitIndex > 0)
                    name = name[..splitIndex];

                if (seen.Add(name))
                    names.Add(name);
            }
        }

        return names;
    }

    public Stream GetCatalog()
    {
        var (zip, entry) = FindEntryInAllZips("assets/aa/catalog.json");
        return EnsureSeekable(zip.OpenFileStream(entry));
    }

    public Stream GetBundle(string name)
    {
        var path = name.Replace(RuntimePathPlaceholder, "assets/aa");

        if (TryFindEntryInAllZips(path, out var zip, out var entry))
            return EnsureSeekable(zip.OpenFileStream(entry));

        // 4.0 起 catalog 中的 bundle 名为 <hash1>_<hash2>.bundle,包内实际存放的是 <hash2>.bundle
        var nameIndex = path.LastIndexOf('/') + 1;
        var hashIndex = path.IndexOf('_', nameIndex);

        if (hashIndex > nameIndex)
        {
            var stripped = string.Concat(path.AsSpan(0, nameIndex), path.AsSpan(hashIndex + 1));
            if (TryFindEntryInAllZips(stripped, out var strippedZip, out var strippedEntry))
                return EnsureSeekable(strippedZip.OpenFileStream(strippedEntry));
        }

        throw new FileNotFoundException($"Required Unity asset '{path}' missing from provided packages.");
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private bool TryFindEntryInAllZips(
        string fileName,
        [NotNullWhen(true)] out ShuaZip? zip,
        [NotNullWhen(true)] out FileEntry? entry)
    {
        foreach (var item in zips)
        {
            entry = item.TryFindEntry(fileName);
            if (entry is not null)
            {
                zip = item;
                return true;
            }
        }

        zip = null;
        entry = null;
        return false;
    }

    internal (ShuaZip, FileEntry) FindEntryInAllZips(string fileName)
    {
        if (TryFindEntryInAllZips(fileName, out var zip, out var entry))
            return (zip, entry);

        throw new FileNotFoundException($"Required Unity asset '{fileName}' missing from provided packages.");
    }

    private static Stream EnsureSeekable(Stream stream)
    {
        if (stream.CanSeek)
        {
            stream.Position = 0;
            return stream;
        }

        var ms = new MemoryStream();
        stream.CopyTo(ms);
        ms.Position = 0;

        stream.Dispose();
        return ms;
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            cldbStream.Dispose();
            foreach (var item in zips) item.Dispose();
        }

        _disposed = true;
    }
}
