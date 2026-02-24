using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using PhiInfo.Core.Type;

namespace PhiInfo.CLI;

public class TarPacker
{
    private int _fileIdCounter = 0;
    private readonly Dictionary<int, (byte[] data, string name)> _files = [];

    public int AllocateFileId()
    {
        return _fileIdCounter++;
    }

    public void AddBinaryFile(int fileId, byte[] data)
    {
        _files[fileId] = (data, "files/" + fileId);
    }

    public void AddTextFile(int fileId, string text)
    {
        _files[fileId] = (System.Text.Encoding.UTF8.GetBytes(text), "files/" + fileId);
    }

    public AllAssetsMetadata ConvertToMetadata(AllAssets assets)
    {
        var result = new AllAssetsMetadata
        {
            songs = [],
            collection_covers = [],
            avatars = []
        };

        foreach (var kvp in assets.songs)
        {
            var songAsset = kvp.Value;
            var charts = new Dictionary<string, TextMetadata>();

            foreach (var chartKvp in songAsset.charts)
            {
                int fileId = AllocateFileId();
                AddTextFile(fileId, chartKvp.Value.content);
                charts[chartKvp.Key] = new TextMetadata { file_id = fileId };
            }

            int illustrationId = AllocateFileId();
            AddBinaryFile(illustrationId, songAsset.illustration.data);

            int illustrationLowResId = AllocateFileId();
            AddBinaryFile(illustrationLowResId, songAsset.illustration_low_res.data);

            int illustrationBlurId = AllocateFileId();
            AddBinaryFile(illustrationBlurId, songAsset.illustration_blur.data);

            int musicId = AllocateFileId();
            AddBinaryFile(musicId, songAsset.music.data);

            result.songs[kvp.Key] = new SongAssetMetadata
            {
                charts = charts,
                illustration = new ImageMetadata
                {
                    width = songAsset.illustration.width,
                    height = songAsset.illustration.height,
                    file_id = illustrationId
                },
                illustration_low_res = new ImageMetadata
                {
                    width = songAsset.illustration_low_res.width,
                    height = songAsset.illustration_low_res.height,
                    file_id = illustrationLowResId
                },
                illustration_blur = new ImageMetadata
                {
                    width = songAsset.illustration_blur.width,
                    height = songAsset.illustration_blur.height,
                    file_id = illustrationBlurId
                },
                music = new MusicMetadata
                {
                    length = songAsset.music.length,
                    file_id = musicId
                }
            };
        }

        foreach (var kvp in assets.collection_covers)
        {
            int fileId = AllocateFileId();
            AddBinaryFile(fileId, kvp.Value.data);
            result.collection_covers[kvp.Key] = new ImageMetadata
            {
                width = kvp.Value.width,
                height = kvp.Value.height,
                file_id = fileId
            };
        }

        foreach (var kvp in assets.avatars)
        {
            int fileId = AllocateFileId();
            AddBinaryFile(fileId, kvp.Value.data);
            result.avatars[kvp.Key] = new ImageMetadata
            {
                width = kvp.Value.width,
                height = kvp.Value.height,
                file_id = fileId
            };
        }

        return result;
    }

    public void PackToTar(string outputPath, AllAssetsMetadata metadata, JsonSerializerOptions jsonOptions, JsonSerializerContext context)
    {
        using (var fileStream = File.Create(outputPath))
        using (var tarWriter = new TarWriter(fileStream))
        {
            var metadataJson = JsonSerializer.Serialize(metadata, typeof(AllAssetsMetadata), context);
            var metadataBytes = System.Text.Encoding.UTF8.GetBytes(metadataJson);

            var metadataEntry = new PaxTarEntry(TarEntryType.RegularFile, "metadata.json")
            {
                DataStream = new MemoryStream(metadataBytes)
            };
            tarWriter.WriteEntry(metadataEntry);

            foreach (var kvp in _files)
            {
                var (data, name) = kvp.Value;
                var fileEntry = new PaxTarEntry(TarEntryType.RegularFile, name)
                {
                    DataStream = new MemoryStream(data)
                };
                tarWriter.WriteEntry(fileEntry);
            }
        }
    }
}
