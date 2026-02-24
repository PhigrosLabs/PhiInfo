using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using PhiInfo.Core.Type;

namespace PhiInfo.Core;

public class PhiInfoAsset(PhiInfo phiInfo, CatalogParser catalogParser, Func<string, Stream> getBundleStreamFunc) : IDisposable
{
    private bool _disposed = false;

    private readonly CatalogParser _catalogParser = catalogParser;

    private readonly Func<string, Stream> _getBundleStreamFunc = getBundleStreamFunc;

    public readonly PhiInfo phiInfo = phiInfo;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        phiInfo.Dispose();
    }

    static byte[] ReadRangeAsBytes(Stream baseStream, long offset, int size)
    {
        byte[] buffer = new byte[size];

        long oldPos = baseStream.Position;
        try
        {
            baseStream.Seek(offset, SeekOrigin.Begin);

            int readTotal = 0;
            while (readTotal < size)
            {
                int read = baseStream.Read(
                    buffer,
                    readTotal,
                    size - readTotal
                );

                if (read == 0)
                    throw new EndOfStreamException();

                readTotal += read;
            }
        }
        finally
        {
            baseStream.Position = oldPos;
        }

        return buffer;
    }

    private T ProcessAssetBundle<T>(string path, Func<AssetBundleFile, AssetsFile, T> processor)
    {
        var file = getBundle(path);
        var reader = new AssetsFileReader(file);
        AssetBundleFile bun = new();
        bun.Read(reader);
        if (bun.DataIsCompressed)
        {
            bun = BundleHelper.UnpackBundle(bun);
        }

        bun.GetFileRange(0, out long offset, out long size);
        SegmentStream stream = new(bun.DataReader.BaseStream, offset, size);
        AssetsFile info_file = new();
        info_file.Read(new AssetsFileReader(stream));

        try
        {
            return processor(bun, info_file);
        }
        finally
        {
            bun.Close();
            info_file.Close();
        }
    }

    public Image GetImageRaw(string path)
    {
        return ProcessAssetBundle(path, (bun, info_file) =>
        {
            foreach (var info in info_file.AssetInfos)
            {
                if (info.TypeId == (int)AssetClassID.Texture2D)
                {
                    var baseField = phiInfo.GetBaseField(info_file, info, false);
                    var height = baseField["m_Height"].AsInt;
                    var width = baseField["m_Width"].AsInt;
                    var data_offset = baseField["m_StreamData"]["offset"].AsLong;
                    var data_size = baseField["m_StreamData"]["size"].AsLong;
                    bun.GetFileRange(1, out long data_file_offset, out long data_file_size);
                    var data = ReadRangeAsBytes(bun.DataReader.BaseStream, data_file_offset + data_offset, (int)data_size);
                    var image = new Image { width = width, height = height, data = data };
                    return image;
                }
            }
            throw new Exception("No Texture2D found in the asset bundle.");
        });
    }

    public Music GetMusicRaw(string path)
    {
        return ProcessAssetBundle(path, (bun, info_file) =>
        {
            foreach (var info in info_file.AssetInfos)
            {
                if (info.TypeId == (int)AssetClassID.AudioClip)
                {
                    var baseField = phiInfo.GetBaseField(info_file, info, false);
                    var data_offset = baseField["m_Resource"]["m_Offset"].AsLong;
                    var data_size = baseField["m_Resource"]["m_Size"].AsLong;
                    var length = baseField["m_Length"].AsFloat;
                    bun.GetFileRange(1, out long data_file_offset, out long data_file_size);
                    var data = ReadRangeAsBytes(bun.DataReader.BaseStream, data_file_offset + data_offset, (int)data_size);
                    return new Music { data = data, length = length };
                }
            }
            throw new Exception("No AudioClip found in the asset bundle.");
        });
    }

    public Text GetText(string path)
    {
        return ProcessAssetBundle(path, (bun, info_file) =>
        {
            foreach (var info in info_file.AssetInfos)
            {
                if (info.TypeId == (int)AssetClassID.TextAsset)
                {
                    var baseField = phiInfo.GetBaseField(info_file, info, false);
                    var text = baseField["m_Script"].AsString;
                    return new Text { content = text };
                }
            }
            throw new Exception("No TextAsset found in the asset bundle.");
        });
    }

    private Stream getBundle(string path)
    {
        var bundlePath = _catalogParser.Get(path);
        if (bundlePath == null)
            throw new Exception($"Asset {path} not found in catalog.");
        if (bundlePath.Value.ResolvedKey == null)
            throw new Exception($"Asset {path} has no resolved bundle path.");
        if (bundlePath.Value.ResolvedKey.Value.StringValue == null)
            throw new Exception($"Asset {path} has invalid resolved bundle path.");

        return _getBundleStreamFunc(bundlePath.Value.ResolvedKey.Value.StringValue);
    }

    public Dictionary<string, SongAssetPath> ExtractSongAssetPaths()
    {
        var result = new Dictionary<string, SongAssetPath>();
        var song_info = phiInfo.ExtractSongInfo();

        foreach (var song in song_info)
        {
            var path = "Assets/Tracks/" + song.id;
            var charts = new Dictionary<string, string>();

            foreach (var diff in song.levels.Keys)
            {
                charts[diff] = path + $"/Chart_{diff}.json";
            }

            result[song.id] = new SongAssetPath
            {
                charts = charts,
                music = path + "/music.wav",
                illustration = path + "/Illustration.jpg",
                illustration_low_res = path + "/IllustrationLowRes.jpg",
                illustration_blur = path + "/IllustrationBlur.jpg"
            };
        }

        return result;
    }

    public Dictionary<string, string> ExtractCollectionCoverPaths()
    {
        var result = new Dictionary<string, string>();
        var collection = phiInfo.ExtractCollection();

        foreach (var folder in collection)
        {
            result[folder.cover] = folder.cover;
        }

        return result;
    }

    public Dictionary<string, string> ExtractAvatarPaths()
    {
        var result = new Dictionary<string, string>();
        var avatars = phiInfo.ExtractAvatars();

        foreach (var avatar in avatars)
        {
            result[avatar.addressable_key] = avatar.addressable_key;
        }

        return result;
    }

    public Dictionary<string, string> ExtractChapterCoverPaths()
    {
        var result = new Dictionary<string, string>();
        var chapters = phiInfo.ExtractChapters();

        foreach (var chapter in chapters)
        {
            result[chapter.code] = "Assets/Tracks/#ChapterCover/" + chapter.code + ".jpg";
        }

        return result;
    }

    public AllAssetsPaths ExtractAllAssetsPaths()
    {
        return new AllAssetsPaths
        {
            songs = ExtractSongAssetPaths(),
            collection_covers = ExtractCollectionCoverPaths(),
            avatars = ExtractAvatarPaths(),
            chapter_covers = ExtractChapterCoverPaths()
        };
    }
}