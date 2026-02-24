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

// =====================
// Metadata structures
// =====================

public struct ImageMetadata
{
#pragma warning disable IDE1006 // 命名样式
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
    public ImageMetadata illustration_blur { get; set; }
    public MusicMetadata music { get; set; }
}

public struct AllAssetsMetadata
{
    public Dictionary<string, SongAssetMetadata> songs { get; set; }
    public Dictionary<string, ImageMetadata> collection_covers { get; set; }
    public Dictionary<string, ImageMetadata> avatars { get; set; }
    public Dictionary<string, ImageMetadata> chapter_covers { get; set; }
}
#pragma warning restore IDE1006 // 命名样式

// =====================
// TarPacker (thread-safe)
// =====================

public class TarPacker
{
    private int _fileIdCounter = -1;

    private readonly ConcurrentDictionary<int, (byte[] data, string name)> _files = new();
    private readonly ConcurrentDictionary<string, int> _pathToFileId = new();
    private readonly ConcurrentDictionary<string, (int width, int height)> _imageDimensions = new();

    private int AllocateFileId()
        => Interlocked.Increment(ref _fileIdCounter);

    private void AddBinaryFile(int fileId, byte[] data)
    {
        _files[fileId] = (data, "files/" + fileId);
    }

    private void AddTextFile(int fileId, string text)
    {
        _files[fileId] = (Encoding.UTF8.GetBytes(text), "files/" + fileId);
    }

    private (int fileId, int width, int height) AddImageFile(string path, PhiInfoAsset asset)
    {
        if (path == "Assets/Tracks/#ChapterCover/MainStory8.jpg") {
            path = "Assets/Tracks/#ChapterCover/MainStory8_2.jpg";
        }
        int fileId = _pathToFileId.GetOrAdd(path, _ =>
        {
            int id = AllocateFileId();
            var image = asset.GetImageRaw(path);

            AddBinaryFile(id, image.data);
            _imageDimensions[path] = (image.width, image.height);

            return id;
        });

        var dims = _imageDimensions[path];
        return (fileId, dims.width, dims.height);
    }

    private int AddTextFileFromPath(string path, PhiInfoAsset asset)
    {
        return _pathToFileId.GetOrAdd(path, _ =>
        {
            int id = AllocateFileId();
            var text = asset.GetText(path);
            AddTextFile(id, text.content);
            return id;
        });
    }

    private (int fileId, float length) AddMusicFile(string path, PhiInfoAsset asset)
    {
        int fileId = _pathToFileId.GetOrAdd(path, _ =>
        {
            int id = AllocateFileId();
            var music = asset.GetMusicRaw(path);
            AddBinaryFile(id, music.data);
            return id;
        });

        var musicData = asset.GetMusicRaw(path);
        return (fileId, musicData.length);
    }

    // =====================
    // Parallel metadata build
    // =====================

    public AllAssetsMetadata ConvertToMetadata(AllAssetsPaths assetPaths, PhiInfoAsset asset)
    {
        var songs = new ConcurrentDictionary<string, SongAssetMetadata>();
        var covers = new ConcurrentDictionary<string, ImageMetadata>();
        var avatars = new ConcurrentDictionary<string, ImageMetadata>();
        var chapterCovers = new ConcurrentDictionary<string, ImageMetadata>();

        Parallel.ForEach(assetPaths.songs, songKvp =>
        {
            try
            {
                var songPath = songKvp.Value;
                var charts = new ConcurrentDictionary<string, TextMetadata>();

                Parallel.ForEach(songPath.charts, chartKvp =>
                {
                    try
                    {
                        int fileId = AddTextFileFromPath(chartKvp.Value, asset);
                        charts[chartKvp.Key] = new TextMetadata { file_id = fileId };
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error processing chart {chartKvp.Value}: {ex.Message}");
                    }
                });

                try
                {
                    var ill = AddImageFile(songPath.illustration, asset);
                    var low = AddImageFile(songPath.illustration_low_res, asset);
                    var blur = AddImageFile(songPath.illustration_blur, asset);
                    var music = AddMusicFile(songPath.music, asset);

                    songs[songKvp.Key] = new SongAssetMetadata
                    {
                        charts = new Dictionary<string, TextMetadata>(charts),
                        illustration = new ImageMetadata { width = ill.width, height = ill.height, file_id = ill.fileId },
                        illustration_low_res = new ImageMetadata { width = low.width, height = low.height, file_id = low.fileId },
                        illustration_blur = new ImageMetadata { width = blur.width, height = blur.height, file_id = blur.fileId },
                        music = new MusicMetadata { length = music.length, file_id = music.fileId }
                    };
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error processing song {songKvp.Key}: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing song {songKvp.Key}: {ex.Message}");
            }
        });

        Parallel.ForEach(assetPaths.collection_covers, kvp =>
        {
            try
            {
                var img = AddImageFile(kvp.Value, asset);
                covers[kvp.Key] = new ImageMetadata
                {
                    width = img.width,
                    height = img.height,
                    file_id = img.fileId
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing collection cover {kvp.Value}: {ex.Message}");
            }
        });

        Parallel.ForEach(assetPaths.avatars, kvp =>
        {
            try
            {
                var img = AddImageFile(kvp.Value, asset);
                avatars[kvp.Key] = new ImageMetadata
                {
                    width = img.width,
                    height = img.height,
                    file_id = img.fileId
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing avatar {kvp.Value}: {ex.Message}");
            }
        });

        Parallel.ForEach(assetPaths.chapter_covers, kvp =>
        {
            try
            {
                var img = AddImageFile(kvp.Value, asset);
                chapterCovers[kvp.Key] = new ImageMetadata
                {
                    width = img.width,
                    height = img.height,
                    file_id = img.fileId
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing chapter cover {kvp.Value}: {ex.Message}");
            }
        });

        return new AllAssetsMetadata
        {
            songs = new Dictionary<string, SongAssetMetadata>(songs),
            collection_covers = new Dictionary<string, ImageMetadata>(covers),
            avatars = new Dictionary<string, ImageMetadata>(avatars),
            chapter_covers = new Dictionary<string, ImageMetadata>(chapterCovers)
        };
    }

    // =====================
    // Tar packing (single-thread)
    // =====================

    public void PackToTar(string outputPath, AllAssetsMetadata metadata, JsonSerializerContext context)
    {
        using var fileStream = File.Create(outputPath);
        using var tarWriter = new TarWriter(fileStream);

        var metadataJson = JsonSerializer.Serialize(metadata, typeof(AllAssetsMetadata), context);
        var metadataBytes = Encoding.UTF8.GetBytes(metadataJson);

        tarWriter.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "metadata.json")
        {
            DataStream = new MemoryStream(metadataBytes)
        });

        foreach (var kvp in _files)
        {
            tarWriter.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, kvp.Value.name)
            {
                DataStream = new MemoryStream(kvp.Value.data)
            });
        }
    }
}