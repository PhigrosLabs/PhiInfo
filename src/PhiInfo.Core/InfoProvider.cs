using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AssetsTools.NET;
using PhiInfo.Core.Type;
using Shua.UA.Core.Field;

namespace PhiInfo.Core;

public class InfoProvider : IDisposable
{
    // 4.0.0 (code 155) 起收藏品数据被拆成两份:文件夹仍在 SaturnOS 场景里,
    // 条目则迁移到 sharedassets22.assets,且以 getSong 的绝对值作为索引。
    private const uint SharedAssetsCollectionVersion = 155;
    private const string CollectionSceneScript = "SaturnOSControl";
    private const string CollectionSceneFileName = "level22";
    private const string CollectionDatabaseScript = "CollectionDatabase";
    private const string SharedAssetsFileName = "sharedassets22.assets";

    private readonly IInfoDataProvider _dataProvider;
    private readonly FieldProvider _fieldProvider;
    private readonly Lazy<AssetsFile> _level0;
    private readonly Lazy<AssetsFile> _collectionScene;
    private readonly Lazy<AssetsFile> _collectionDatabase;
    private readonly Lazy<PhiVersion> _version;
    private bool _disposed;

    public InfoProvider(IInfoDataProvider dataProvider, FieldProvider fieldProvider)
    {
        _dataProvider = dataProvider;
        _fieldProvider = fieldProvider;
        _level0 = new Lazy<AssetsFile>(() => ReadAssetsFile(dataProvider.GetDataFile("level0")));
        _collectionScene = new Lazy<AssetsFile>(() =>
            FindMonoBehaviourFile(CollectionSceneScript, CollectionSceneFileName));
        _collectionDatabase = new Lazy<AssetsFile>(() =>
            FindMonoBehaviourFile(CollectionDatabaseScript, SharedAssetsFileName));
        _version = new Lazy<PhiVersion>(GetPhiVersion);
    }

    private static AssetsFile ReadAssetsFile(Stream stream)
    {
        var file = new AssetsFile();
        file.Read(new AssetsFileReader(stream));
        return file;
    }

    /// <summary>
    ///     找出包含指定 MonoBehaviour 的资源文件。优先尝试已知文件名,版本变动导致位置变化时按文件名扫描。
    /// </summary>
    private AssetsFile FindMonoBehaviourFile(string scriptName, string preferredFileName)
    {
        var names = new List<string> { preferredFileName };

        foreach (var name in _dataProvider.GetDataFileNames())
        {
            if (name != preferredFileName)
                names.Add(name);
        }

        foreach (var name in names)
        {
            try
            {
                var file = ReadAssetsFile(_dataProvider.GetDataFile(name));

                if (_fieldProvider.TryFindMonoBehaviour(file, scriptName) is not null)
                    return file;

                file.Close();
            }
            catch (Exception)
            {
                // 不是资源文件或读取失败时继续尝试下一个
            }
        }

        throw new InvalidOperationException($"Cannot find {scriptName} in the provided packages.");
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;
        _disposed = true;

        if (disposing)
        {
            foreach (var file in new[] { _level0, _collectionScene, _collectionDatabase })
                if (file.IsValueCreated)
                    file.Value.Close();
        }
    }

    private static Dictionary<Language, string> ExtractMultiLang(AssetTypeValueField field,
        Func<string, string>? hook = null)
    {
        var result = new Dictionary<Language, string>();

        foreach (var child in field.Children)
        {
            if (child.FieldName == "code")
                continue;

            var lang = Extensions.FromString(child.FieldName);
            var value = child.AsString;

            if (hook != null)
                value = hook(value);

            result[lang] = value;
        }

        return result;
    }

    public List<SongInfo> ExtractSongs()
    {
        var gameInfo = _fieldProvider.FindMonoBehaviour(_level0.Value, "GameInformation")
                       ?? throw new InvalidOperationException("GameInformation MonoBehaviour not found");

        var songs = gameInfo["song"]
            .Children
            .SelectMany(songGroup => songGroup["Array"].Children)
            .Select(song =>
            {
                var levels = song["levels"]["Array"].Children;
                var charters = song["charter"]["Array"].Children;
                var diffs = song["difficulty"]["Array"].Children;

                var levelDict = diffs
                    .Select((diffNode, i) => new
                    {
                        Diff = diffNode.AsDouble,
                        Level = levels[i].AsString,
                        Charter = charters[i].AsString
                    })
                    .Where(x => x.Diff != 0)
                    .ToDictionary(
                        x => x.Level,
                        x => new SongLevel(x.Charter, Math.Round(x.Diff, 1))
                    );

                return levelDict.Count == 0
                    ? null
                    : new SongInfo(
                        song["songsId"].AsString,
                        song["songsKey"].AsString,
                        song["songsName"].AsString,
                        song["composer"].AsString,
                        song["illustrator"].AsString,
                        Math.Round(song["previewTime"].AsDouble, 2),
                        Math.Round(song["previewEndTime"].AsDouble, 2),
                        levelDict
                    );
            })
            .Where(song => song != null)
            .ToList();

        return songs!;
    }

    public List<Folder> ExtractCollection()
    {
        if (_version.Value.code >= SharedAssetsCollectionVersion)
            return ExtractCollectionFromSharedAssets();

        var control = _fieldProvider.FindMonoBehaviour(_collectionScene.Value, CollectionSceneScript)
                      ?? throw new InvalidOperationException("SaturnOSControl MonoBehaviour not found");

        return control["folders"]["Array"].Children
            .Select(folder =>
            {
                var files = folder["files"]["Array"].Children
                    .Select(ExtractFileItem)
                    .ToList();

                return new Folder(
                    ExtractMultiLang(folder["title"]),
                    ExtractMultiLang(folder["subTitle"]),
                    folder["cover"].AsString,
                    files
                );
            })
            .ToList();
    }

    private List<Folder> ExtractCollectionFromSharedAssets()
    {
        var database = _fieldProvider.FindMonoBehaviour(_collectionDatabase.Value, CollectionDatabaseScript)
                       ?? throw new InvalidOperationException("CollectionDatabase MonoBehaviour not found");

        var items = database["items"]["Array"].Children
            .Select(item => new CollectionEntry(ExtractFileItem(item), Math.Abs(item["getSong"].AsInt)))
            .ToList();

        var control = _fieldProvider.FindMonoBehaviour(_collectionScene.Value, CollectionSceneScript)
                      ?? throw new InvalidOperationException("SaturnOSControl MonoBehaviour not found");

        return control["folders"]["Array"].Children
            .Select(folder => new Folder(
                ExtractMultiLang(folder["title"]),
                ExtractMultiLang(folder["subTitle"]),
                folder["cover"].AsString,
                ExtractFolderFiles(folder, items)
            ))
            .ToList();
    }

    private static List<FileItem> ExtractFolderFiles(AssetTypeValueField folder, List<CollectionEntry> items)
    {
        var start = folder["startIndex"].AsInt;
        var end = folder["endIndex"].AsInt;

        var excluded = folder["excludedFiles"]["Array"].Children
            .Select(range => (Start: range["start"].AsInt, End: range["end"].AsInt))
            .ToList();

        var files = items
            .Where(item => item.Index >= start && item.Index <= end &&
                           !excluded.Any(range => item.Index >= range.Start && item.Index <= range.End))
            .Select(item => item.File)
            .ToList();

        // 索引区间之外的条目由 includedIsolatedFiles 单独指定
        foreach (var reference in folder["includedIsolatedFiles"]["Array"].Children)
        {
            var key = reference["key"].AsString;
            var subIndex = reference["subIndex"].AsInt;

            var item = items.FirstOrDefault(entry => entry.File.key == key && entry.File.sub_index == subIndex);

            if (item != null && !files.Contains(item.File))
                files.Add(item.File);
        }

        return files;
    }

    private static FileItem ExtractFileItem(AssetTypeValueField file)
    {
        return new FileItem(
            file["key"].AsString,
            file["subIndex"].AsInt,
            ExtractMultiLang(file["name"]),
            file["date"].AsString,
            ExtractMultiLang(file["supervisor"]),
            file["category"].AsString,
            ExtractMultiLang(file["content"], v => v.Replace("\\n", "\n")),
            ExtractMultiLang(file["properties"])
        );
    }

    private sealed record CollectionEntry(FileItem File, int Index);

    public List<Avatar> ExtractAvatars()
    {
        var control = _fieldProvider.FindMonoBehaviour(_level0.Value, "GetCollectionControl")
                      ?? throw new InvalidOperationException("GetCollectionControl MonoBehaviour not found");

        return control["avatars"]["Array"].Children
            .Select(a => new Avatar(
                a["name"].AsString,
                a["addressableKey"].AsString
            ))
            .ToList();
    }

    public Dictionary<Language, List<string>> ExtractTips()
    {
        var provider = _fieldProvider.FindMonoBehaviour(_level0.Value, "TipsProvider")
                       ?? throw new InvalidOperationException("TipsProvider MonoBehaviour not found");

        var result = new Dictionary<Language, List<string>>();

        var array = provider["tips"]["Array"].Children;

        foreach (var entry in array)
        {
            var langValue = entry["language"].AsInt;
            var language = Extensions.FromInt(langValue);

            var tips = entry["tips"]["Array"].Children
                .Select(t => t.AsString)
                .ToList();

            result[language] = tips;
        }

        return result;
    }

    public List<ChapterInfo> ExtractChapters()
    {
        var gameInfo = _fieldProvider.FindMonoBehaviour(_level0.Value, "GameInformation")
                       ?? throw new InvalidOperationException("GameInformation MonoBehaviour not found");

        return gameInfo["chapters"]["Array"].Children
            .Select(chapter =>
            {
                var songInfo = chapter["songInfo"];

                var songs = songInfo["songs"]["Array"].Children
                    .Select(s => s["songsId"].AsString)
                    .ToList();

                return new ChapterInfo(
                    chapter["chapterCode"].AsString,
                    songInfo["banner"].AsString,
                    songs
                );
            })
            .ToList();
    }

    public PhiVersion GetPhiVersion()
    {
        var meta = _fieldProvider.GetMetadata();

        var assembly = meta.AssemblyDefinitions
                           .FirstOrDefault(a => a.AssemblyName.Name == "Assembly-CSharp")
                       ?? throw new InvalidDataException("Cannot find Assembly-CSharp.");

        var type = assembly.Image.Types?
                       .FirstOrDefault(t => t.FullName == "Constants")
                   ?? throw new InvalidDataException("Cannot find Constants class.");

        var codeField = type.Fields?
                            .FirstOrDefault(f => f.Name == "IntVersion")
                        ?? throw new InvalidDataException("Cannot find IntVersion field.");

        var codeDefaultValue = meta.GetFieldDefaultValue(codeField)?.Value
                               ?? throw new InvalidDataException("There is no default value for the IntVersion field.");

        var nameField = type.Fields?
                            .FirstOrDefault(f => f.Name == "Version")
                        ?? throw new InvalidDataException("Cannot find Version field.");

        var nameDefaultValue = meta.GetFieldDefaultValue(nameField)?.Value
                               ?? throw new InvalidDataException("There is no default value for the Version field.");

        if (codeDefaultValue is int intValue && nameDefaultValue is string stringValue)
            return new PhiVersion((uint)intValue, stringValue);

        throw new InvalidDataException(
            $"Invalid version type: {nameDefaultValue.GetType()} and {codeDefaultValue.GetType()}");
    }

    public AllInfo ExtractAllInfo()
    {
        return new AllInfo(GetPhiVersion(), ExtractSongs(), ExtractCollection(), ExtractAvatars(), ExtractTips(),
            ExtractChapters());
    }
}
