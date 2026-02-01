namespace PhiInfo.Core;

using System;
using System.Diagnostics.SymbolStore;
using System.IO;
using System.Text.Json;
using AssetsTools.NET;
using AssetsTools.NET.Cpp2IL;
using AssetsTools.NET.Extra;

public class PhiInfo
{
    private readonly AssetsManager manager;
    private readonly AssetsFileInstance ggmInst;
    private readonly AssetsFileInstance level0Inst;
    private readonly AssetsFileInstance level22Inst;

    public string lang = "chinese";

    public PhiInfo(
        byte[] globalGameManagers,
        byte[] level0,
        byte[] level22,
        byte[] il2cppSo,
        byte[] globalMetadata
    )
    {
        manager = new AssetsManager();
        manager.LoadClassPackage("classdata.tpk");

        ggmInst = manager.LoadAssetsFile(
            new MemoryStream(globalGameManagers),
            "globalgamemanagers.assets",
            false
        );

        level0Inst = manager.LoadAssetsFile(
            new MemoryStream(level0),
            "level0",
            false
        );

        level22Inst = manager.LoadAssetsFile(
            new MemoryStream(level22),
            "level22",
            false
        );

        string tempIl2cpp = Path.GetTempFileName();
        string tempMetadata = Path.GetTempFileName();
        File.WriteAllBytes(tempIl2cpp, il2cppSo);
        File.WriteAllBytes(tempMetadata, globalMetadata);

        manager.LoadClassDatabaseFromPackage(ggmInst.file.Metadata.UnityVersion);
        manager.MonoTempGenerator = new Cpp2IlTempGenerator(tempMetadata, tempIl2cpp);
    }

    private AssetTypeValueField? FindMonoBehaviour(
        AssetsFileInstance inst,
        string name)
    {
        foreach (AssetFileInfo info in inst.file.AssetInfos)
        {
            if (info.TypeId != (int)AssetClassID.MonoBehaviour)
                continue;

            var Base = manager.GetBaseField(
                inst,
                info,
                AssetReadFlags.SkipMonoBehaviourFields);

            var msId = Base["m_Script"]["m_PathID"].AsInt;
            var msname = manager.GetBaseField(ggmInst, msId)["m_Name"].AsString;

            if (msname == name)
                return manager.GetBaseField(inst, info);
        }

        return null;
    }

    public Dictionary<string, SongInfo> ExtractSongInfo()
    {
        var result = new Dictionary<string, SongInfo>();

        var gameInfoField = FindMonoBehaviour(
            level0Inst,
            "GameInformation"
        ) ?? throw new Exception("GameInformation MonoBehaviour not found");

        var songField = gameInfoField["song"];
        var comboArray = gameInfoField["songAllCombos"]["Array"];

        var comboDict = new Dictionary<string, List<int>>();
        for (int i = 0; i < comboArray.Children.Count; i++)
        {
            var combo = comboArray[i];
            string songId = combo["songsId"].AsString;
            var allComboList = new List<int>();
            var allComboField = combo["allComboNum"]["Array"];
            for (int j = 0; j < allComboField.Children.Count; j++)
                allComboList.Add(allComboField[j].AsInt);
            comboDict[songId] = allComboList;
        }

        for (int i = 0; i < songField.Children.Count; i++)
        {
            var songArray = songField[i]["Array"];
            for (int j = 0; j < songArray.Children.Count; j++)
            {
                var song = songArray[j];
                string songId = song["songsId"].AsString;

                var allComboNum = comboDict.ContainsKey(songId) ? comboDict[songId] : new List<int>();
                var levelsArray = song["levels"]["Array"];
                var chartersArray = song["charter"]["Array"];
                var difficultiesArray = song["difficulty"]["Array"];

                var levelsDict = new Dictionary<string, SongLevel>();

                for (int k = 0; k < difficultiesArray.Children.Count; k++)
                {
                    double diff = difficultiesArray[k].AsDouble;
                    if (diff == 0) continue;

                    string levelName = levelsArray[k].AsString;
                    string charter = chartersArray[k].AsString;
                    int allCombo = k < allComboNum.Count ? allComboNum[k] : 0;

                    levelsDict[levelName] = new SongLevel
                    {
                        charter = charter,
                        all_combo_num = allCombo,
                        difficulty = Math.Round(diff, 1)
                    };
                }

                if (levelsDict.Count == 0) continue;

                result[songId.ToString()] = new SongInfo
                {
                    name = song["songsName"].AsString,
                    composer = song["composer"].AsString,
                    illustrator = song["illustrator"].AsString,
                    preview_time = Math.Round(song["previewTime"].AsDouble, 2),
                    preview_end_time = Math.Round(song["previewEndTime"].AsDouble, 2),
                    levels = levelsDict
                };
            }
        }

        return result;
    }

    public List<Folder> ExtractCollection()
    {
        var result = new List<Folder>();

        var collectionField = FindMonoBehaviour(
            level22Inst,
            "SaturnOSControl"
        ) ?? throw new Exception("SaturnOSControl MonoBehaviour not found");

        var folders = collectionField["folders"]["Array"];

        for (int i = 0; i < folders.Children.Count; i++)
        {
            var folder = folders[i];
            var filesArray = folder["files"]["Array"];
            var files = new List<FileItem>();

            for (int j = 0; j < filesArray.Children.Count; j++)
            {
                var file = filesArray[j];

                files.Add(new FileItem
                {
                    key = file["key"].AsString,
                    sub_index = file["subIndex"].AsInt,
                    name = file["name"][lang].AsString,
                    date = file["date"].AsString,
                    supervisor = file["supervisor"][lang].AsString,
                    category = file["category"].AsString,
                    content = file["content"][lang].AsString,
                    properties = file["properties"][lang].AsString
                });
            }

            result.Add(new Folder
            {
                title = folder["title"][lang].AsString,
                sub_title = folder["subTitle"][lang].AsString,
                cover = folder["cover"].AsString,
                files = files
            });
         }

        return result;
    }
}