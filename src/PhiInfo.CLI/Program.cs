namespace PhiInfo.CLI;

using System.IO.Compression;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using PhiInfo.Core;

[JsonSerializable(typeof(Dictionary<string, SongInfo>))]
[JsonSerializable(typeof(List<Folder>))]
public partial class JsonContext : JsonSerializerContext
{
}


struct Files
{
    public byte[] ggmBytes;
    public byte[] level0Bytes;
    public byte[] il2cppBytes;
    public byte[] metadataBytes;
    public byte[] level22Bytes;
}

class Program
{
    static void Main(string[] args)
    {
        if (args.Length != 1)
        {
            Console.WriteLine("Usage: <apk_path>");
            return;
        }

        var files = SetupFiles(args[0]);

        var options = new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
            WriteIndented = true,
        };

        var context = new JsonContext(options);

        var phiInfo = new PhiInfo(
            files.ggmBytes,
            files.level0Bytes,
            files.level22Bytes,
            files.il2cppBytes,
            files.metadataBytes
        );
        var songInfo = phiInfo.ExtractSongInfo();
        var collectionInfo = phiInfo.ExtractCollection();
        var songInfoJson = JsonSerializer.Serialize(songInfo, context.DictionaryStringSongInfo);
        var collectionInfoJson = JsonSerializer.Serialize(collectionInfo, context.ListFolder);
        File.WriteAllText("song_info.json", songInfoJson);
        File.WriteAllText("collection_info.json", collectionInfoJson);
        Console.WriteLine("Song info extracted to song_info.json");
        Console.WriteLine("Collection info extracted to collection_info.json");
    }

    static Files SetupFiles(string apkPath)
    {
        byte[]? ggmBytes = null;
        byte[]? level0Bytes = null;
        byte[]? il2cppBytes = null;
        byte[]? metadataBytes = null;
        List<(int index, byte[] data)> level22Parts = new List<(int, byte[])>();

        using (var apkFs = File.OpenRead(apkPath))
        using (var zip = new ZipArchive(apkFs, ZipArchiveMode.Read))
        {
            foreach (var entry in zip.Entries)
            {
                switch (entry.FullName)
                {
                    case "assets/bin/Data/globalgamemanagers.assets":
                        ggmBytes = ExtractEntryToMemory(entry);
                        break;
                    case "assets/bin/Data/level0":
                        level0Bytes = ExtractEntryToMemory(entry);
                        break;
                    case "lib/arm64-v8a/libil2cpp.so":
                        il2cppBytes = ExtractEntryToMemory(entry);
                        break;
                    case "assets/bin/Data/Managed/Metadata/global-metadata.dat":
                        metadataBytes = ExtractEntryToMemory(entry);
                        break;
                }
                if (entry.FullName.StartsWith("assets/bin/Data/level22.split"))
                    {
                        string suffix = entry.FullName["assets/bin/Data/level22.split".Length..];
                        int index = int.Parse(suffix);
                        level22Parts.Add((index, ExtractEntryToMemory(entry)));
                    }
            }
        }

        if (ggmBytes == null || level0Bytes == null || il2cppBytes == null || metadataBytes == null || level22Parts.Count == 0)
            throw new FileNotFoundException("Required Unity assets not found in APK");


        level22Parts.Sort((a, b) => a.index.CompareTo(b.index));

        byte[] level22Bytes;
        using (var ms = new MemoryStream())
        {
            foreach (var part in level22Parts)
                ms.Write(part.data, 0, part.data.Length);

            level22Bytes = ms.ToArray();
        }

        var files = new Files
        {
            ggmBytes = ggmBytes,
            level0Bytes = level0Bytes,
            il2cppBytes = il2cppBytes,
            metadataBytes = metadataBytes,
            level22Bytes = level22Bytes
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
}
