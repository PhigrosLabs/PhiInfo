using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using PhiInfo.Core;
using PhiInfo.Core.Type;

namespace PhiInfo.CLI;

public struct ImageMetadata
{
    public int width { get; set; }
    public int height { get; set; }
    public int file_id { get; set; }
}

public struct MusicMetadata
{
    public float length { get; set; }
    public int file_id { get; set; }
}

public struct TextMetadata
{
    public int file_id { get; set; }
}

public struct SongAssetMetadata
{
    public Dictionary<string, TextMetadata> charts { get; set; }
    public ImageMetadata illustration { get; set; }
    public ImageMetadata illustration_low_res { get; set; }
    public MusicMetadata music { get; set; }
}

public struct AllAssetsMetadata
{
    public Dictionary<string, SongAssetMetadata> songs { get; set; }
    public Dictionary<string, ImageMetadata> collection_covers { get; set; }
    public Dictionary<string, ImageMetadata> avatars { get; set; }
    public Dictionary<string, ImageMetadata> chapter_covers { get; set; }
}

public sealed class TarPacker
{
    private int _fileIdCounter = -1;

    private readonly ConcurrentDictionary<int, (byte[] Data, string Name)> _files = new();
    private readonly ConcurrentDictionary<string, int> _pathToFileId = new();
    private readonly ConcurrentDictionary<string, (int Width, int Height)> _imageDimensions = new();
    private readonly ConcurrentDictionary<string, float> _musicLengths = new();

    private int AllocateFileId() {
        return Interlocked.Increment(ref _fileIdCounter);
    }

    private (int fileId, int width, int height) AddImageFile(string path, PhiInfoAsset asset)
    {
        if (path == "Assets/Tracks/#ChapterCover/MainStory8.jpg")
        {
            path = "Assets/Tracks/#ChapterCover/MainStory8_2.jpg";
        }

        if (_pathToFileId.TryGetValue(path, out int existingId))
        {
            var dims = _imageDimensions[path];
            return (existingId, dims.Width, dims.Height);
        }

        int id = AllocateFileId();
        var image = asset.GetImageRaw(path);

        _imageDimensions.TryAdd(path, (image.width, image.height));
        _files.TryAdd(id, (image.data, $"files/{id}"));
        _pathToFileId.TryAdd(path, id);

        return (id, image.width, image.height);
    }

    private int AddTextFileFromPath(string path, PhiInfoAsset asset)
    {
        return _pathToFileId.GetOrAdd(path, _ =>
        {
            int id = AllocateFileId();
            var text = asset.GetText(path);
            _files.TryAdd(id, (Encoding.UTF8.GetBytes(text.content), $"files/{id}"));
            return id;
        });
    }

    private (int fileId, float length) AddMusicFile(string path, PhiInfoAsset asset)
    {
        if (_pathToFileId.TryGetValue(path, out int existingId))
        {
            return (existingId, _musicLengths[path]);
        }

        int id = AllocateFileId();
        var music = asset.GetMusicRaw(path);

        _musicLengths.TryAdd(path, music.length);
        _files.TryAdd(id, (music.data, $"files/{id}"));
        _pathToFileId.TryAdd(path, id);

        return (id, music.length);
    }

    public AllAssetsMetadata ConvertToMetadata(AllAssetsPaths assetPaths, PhiInfoAsset asset)
    {
        var songs = new ConcurrentDictionary<string, SongAssetMetadata>();
        var covers = new ConcurrentDictionary<string, ImageMetadata>();
        var avatars = new ConcurrentDictionary<string, ImageMetadata>();
        var chapterCovers = new ConcurrentDictionary<string, ImageMetadata>();

        var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount };

        Parallel.ForEach(assetPaths.songs, parallelOptions, songKvp =>
        {
            try
            {
                var songPath = songKvp.Value;
                var charts = new Dictionary<string, TextMetadata>();

                foreach (var chartKvp in songPath.charts)
                {
                    int fileId = AddTextFileFromPath(chartKvp.Value, asset);
                    charts[chartKvp.Key] = new TextMetadata { file_id = fileId };
                }

                var ill = AddImageFile(songPath.illustration, asset);
                var illLowRes = AddImageFile(songPath.illustration_low_res, asset);
                var music = AddMusicFile(songPath.music, asset);

                songs[songKvp.Key] = new SongAssetMetadata
                {
                    charts = charts,
                    illustration = new ImageMetadata { width = ill.width, height = ill.height, file_id = ill.fileId },
                    illustration_low_res = new ImageMetadata { width = illLowRes.width, height = illLowRes.height, file_id = illLowRes.fileId },
                    music = new MusicMetadata { length = music.length, file_id = music.fileId }
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing song {songKvp.Key}: {ex.Message}");
            }
        });

        Parallel.ForEach(assetPaths.collection_covers, parallelOptions, kvp =>
        {
            try
            {
                var img = AddImageFile(kvp.Value, asset);
                covers[kvp.Key] = new ImageMetadata { width = img.width, height = img.height, file_id = img.fileId };
            }
            catch (Exception ex) { Console.WriteLine($"Error: {kvp.Value} - {ex.Message}"); }
        });

        Parallel.ForEach(assetPaths.avatars, parallelOptions, kvp =>
        {
            try
            {
                var img = AddImageFile(kvp.Value, asset);
                avatars[kvp.Key] = new ImageMetadata { width = img.width, height = img.height, file_id = img.fileId };
            }
            catch (Exception ex) { Console.WriteLine($"Error: {kvp.Value} - {ex.Message}"); }
        });

        Parallel.ForEach(assetPaths.chapter_covers, parallelOptions, kvp =>
        {
            try
            {
                var img = AddImageFile(kvp.Value, asset);
                chapterCovers[kvp.Key] = new ImageMetadata { width = img.width, height = img.height, file_id = img.fileId };
            }
            catch (Exception ex) { Console.WriteLine($"Error: {kvp.Value} - {ex.Message}"); }
        });

        return new AllAssetsMetadata
        {
            songs = new Dictionary<string, SongAssetMetadata>(songs),
            collection_covers = new Dictionary<string, ImageMetadata>(covers),
            avatars = new Dictionary<string, ImageMetadata>(avatars),
            chapter_covers = new Dictionary<string, ImageMetadata>(chapterCovers)
        };
    }

    public void PackToTar(string outputPath, AllAssetsMetadata metadata, JsonSerializerContext context)
    {
        using var fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.SequentialScan);
        using var tarWriter = new TarWriter(fileStream);

        byte[] metadataBytes = JsonSerializer.SerializeToUtf8Bytes(metadata, typeof(AllAssetsMetadata), context);

        var metaEntry = new PaxTarEntry(TarEntryType.RegularFile, "metadata.json")
        {
            DataStream = new MemoryStream(metadataBytes)
        };
        tarWriter.WriteEntry(metaEntry);

        foreach (var kvp in _files)
        {
            var entry = new PaxTarEntry(TarEntryType.RegularFile, kvp.Value.Name)
            {
                DataStream = new MemoryStream(kvp.Value.Data)
            };
            tarWriter.WriteEntry(entry);
        }
    }
}