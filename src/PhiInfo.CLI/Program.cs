namespace PhiInfo.CLI;

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using PhiInfo.Core;

[JsonSerializable(typeof(Core.Type.AllInfo))]
[JsonSerializable(typeof(AllAssetsMetadata))]
public partial class JsonContext : JsonSerializerContext
{
}


struct Files
{
    public Stream ggm;
    public Stream level0;
    public byte[] il2cppBytes;
    public byte[] metadataBytes;
    public Stream level22;
    public Stream catalogJson;
}

class Program
{
    static readonly string dir = "./output/";
    static void Main(string[] args)
    {
        if (args.Length != 3)
        {
            Console.WriteLine("Usage: <apk_path> <format_switch> <asset_switch>");
            return;
        }

        var apkPath = args[0];
        var formatSwitch = args[1] == "true" || args[1] == "1";
        var assetSwitch = args[2] == "true" || args[2] == "1";

        var options = new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
            WriteIndented = formatSwitch,
        };

        var context = new JsonContext(options);

        using (var apkFs = File.OpenRead(apkPath))
        using (var zip = new ZipArchive(apkFs, ZipArchiveMode.Read))
        {
            var files = SetupFiles(zip);

            var phiInfo = new PhiInfo(
                files.ggm,
                files.level0,
                files.level22,
                files.il2cppBytes,
                files.metadataBytes,
                File.OpenRead("classdata.tpk")
            );

            files.catalogJson.Position = 0;
            var catalogParser = new CatalogParser(files.catalogJson);

            Directory.CreateDirectory(dir);

            var allInfo = phiInfo.ExtractAll();
            var allInfoJson = JsonSerializer.Serialize(allInfo, context.AllInfo);
            File.WriteAllText(dir + "all_info.json", allInfoJson);

            if (assetSwitch)
            {
                Func<string, Stream> getBundleStreamFunc = (bundleName) =>
                {
                    lock (zip)
                    {
                        var entry = zip.GetEntry("assets/aa/Android/" + bundleName);
                        if (entry != null)
                        {
                            return ExtractEntryToMemoryStream(entry);
                        }
                        throw new FileNotFoundException($"Bundle not found in APK: {bundleName}");
                    }
                };

                var asset = new PhiInfoAsset(catalogParser, getBundleStreamFunc);
                var assetPaths = asset.ExtractAllAssetsPaths(allInfo);
                var tarPacker = new TarPacker();
                var metadata = tarPacker.ConvertToMetadata(assetPaths, asset);
                tarPacker.PackToTar(dir + "assets.tar", metadata, context);

                Console.WriteLine("资源提取完成!");
                Console.WriteLine($"歌曲数: {assetPaths.songs.Count}");
                Console.WriteLine($"收藏集: {assetPaths.collection_covers.Count}");
                Console.WriteLine($"头像数: {assetPaths.avatars.Count}");
                phiInfo.Dispose();
            }
            else
            {
                phiInfo.Dispose();
                Console.WriteLine("已跳过资源提取");
            }
        }
    }

    static Files SetupFiles(ZipArchive zip)
    {
        Stream? ggm = null;
        Stream? level0 = null;
        byte[]? il2cppBytes = null;
        byte[]? metadataBytes = null;
        Stream? catalogJson = null;
        List<(int index, byte[] data)> level22Parts = new List<(int, byte[])>();

        foreach (var entry in zip.Entries)
        {
            switch (entry.FullName)
            {
                case "assets/bin/Data/globalgamemanagers.assets":
                    ggm = ExtractEntryToMemoryStream(entry);
                    break;
                case "assets/bin/Data/level0":
                    level0 = ExtractEntryToMemoryStream(entry);
                    break;
                case "lib/arm64-v8a/libil2cpp.so":
                    il2cppBytes = ExtractEntryToMemory(entry);
                    break;
                case "assets/bin/Data/Managed/Metadata/global-metadata.dat":
                    metadataBytes = ExtractEntryToMemory(entry);
                    break;
                case "assets/aa/catalog.json":
                    catalogJson = ExtractEntryToMemoryStream(entry);
                    break;
            }

            if (entry.FullName.StartsWith("assets/bin/Data/level22.split"))
            {
                string suffix = entry.FullName["assets/bin/Data/level22.split".Length..];
                int index = int.Parse(suffix);
                level22Parts.Add((index, ExtractEntryToMemory(entry)));
            }
        }

        if (ggm == null || level0 == null || il2cppBytes == null || metadataBytes == null || level22Parts.Count == 0)
            throw new FileNotFoundException("Required Unity assets not found in APK");

        if (catalogJson == null)
            throw new FileNotFoundException("Catalog not found in APK");

        level22Parts.Sort((a, b) => a.index.CompareTo(b.index));

        Stream level22 = new MemoryStream();
        foreach (var part in level22Parts)
            level22.Write(part.data, 0, part.data.Length);

        level22.Position = 0;

        var files = new Files
        {
            ggm = ggm,
            level0 = level0,
            il2cppBytes = il2cppBytes,
            metadataBytes = metadataBytes,
            level22 = level22,
            catalogJson = catalogJson
        };

        return files;
    }

    static byte[] ExtractEntryToMemory(ZipArchiveEntry entry)
    {
        using (var ms = new MemoryStream())
        using (var entryStream = entry.Open())
        {
            entryStream.CopyTo(ms);
            return ms.ToArray();
        }
    }

    static MemoryStream ExtractEntryToMemoryStream(ZipArchiveEntry entry)
    {
        var ms = new MemoryStream();
        using (var entryStream = entry.Open())
        {
            entryStream.CopyTo(ms);
        }
        ms.Position = 0;
        return ms;
    }
}
