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

    private T ProcessAssetBundle<T>(Stream file, Func<AssetBundleFile, AssetsFile, T> processor)
    {
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

    public Image GetImageRaw(Stream file)
    {
        return ProcessAssetBundle(file, (bun, info_file) =>
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

    public Music GetMusicRaw(Stream file)
    {
        return ProcessAssetBundle(file, (bun, info_file) =>
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

    public Text GetText(Stream file)
    {
        return ProcessAssetBundle(file, (bun, info_file) =>
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

    public Dictionary<string, SongAsset> ExtractSongAssets()
    {
        var result = new ConcurrentDictionary<string, SongAsset>();
        var song_info = phiInfo.ExtractSongInfo();

        Parallel.ForEach(song_info, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, song =>
        {
            var path = "Assets/Tracks/" + song.id;
            try
            {
                var music_bundle = getBundle(path + "/music.wav");
                var ill_bundle = getBundle(path + "/Illustration.jpg");
                var ill_low_bundle = getBundle(path + "/IllustrationLowRes.jpg");
                var ill_blur_bundle = getBundle(path + "/IllustrationBlur.jpg");
                var charts = new ConcurrentDictionary<string, Text>();

                Parallel.ForEach(song.levels.Keys, diff =>
                {
                    var chart_bundle = getBundle(path + $"/Chart_{diff}.json");
                    charts.TryAdd(diff, GetText(chart_bundle));
                });

                result.TryAdd(song.id, new SongAsset
                {
                    charts = new Dictionary<string, Text>(charts),
                    music = GetMusicRaw(music_bundle),
                    illustration = GetImageRaw(ill_bundle),
                    illustration_low_res = GetImageRaw(ill_low_bundle),
                    illustration_blur = GetImageRaw(ill_blur_bundle)
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error processing song {song.id}: {ex.Message}");
            }
        });

        return new Dictionary<string, SongAsset>(result);
    }

    public Dictionary<string, Image> ExtractCollectionCovers()
    {
        var result = new ConcurrentDictionary<string, Image>();
        var collection = phiInfo.ExtractCollection();

        Parallel.ForEach(collection, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, folder =>
        {
            try
            {
                var bundle = getBundle(folder.cover);
                result.TryAdd(folder.cover, GetImageRaw(bundle));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error processing collection cover {folder.cover}: {ex.Message}");
            }
        });

        return new Dictionary<string, Image>(result);
    }

    public Dictionary<string, Image> ExtractAvatars()
    {
        var result = new ConcurrentDictionary<string, Image>();
        var avatars = phiInfo.ExtractAvatars();

        Parallel.ForEach(avatars, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, avatar =>
        {
            try
            {
                var bundle = getBundle(avatar.addressable_key);
                result.TryAdd(avatar.addressable_key, GetImageRaw(bundle));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error processing avatar {avatar.addressable_key}: {ex.Message}");
            }
        });

        return new Dictionary<string, Image>(result);
    }

    public AllAssets ExtractAllAssets()
    {
        return new AllAssets
        {
            songs = ExtractSongAssets(),
            collection_covers = ExtractCollectionCovers(),
            avatars = ExtractAvatars()
        };
    }
}