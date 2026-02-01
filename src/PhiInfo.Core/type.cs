#pragma warning disable IDE1006

using System.Diagnostics.CodeAnalysis;

namespace PhiInfo.Core;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)]
public struct SongLevel
{
    public string charter { get; set; }
    public int all_combo_num { get; set; }
    public double difficulty { get; set; }
}

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)]
public struct SongInfo
{
    public string name { get; set; }
    public string composer { get; set; }
    public string illustrator { get; set; }
    public double preview_time { get; set; }
    public double preview_end_time { get; set; }
    public Dictionary<string, SongLevel> levels { get; set; }
}

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)]
public struct Folder
{
    public string title { get; set; }
    public string sub_title { get; set; }
    public string cover { get; set; }
    public List<FileItem> files { get; set; }
}

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)]
public struct FileItem
{
    public string key { get; set; }
    public int sub_index { get; set; }
    public string name { get; set; }
    public string date { get; set; }
    public string supervisor { get; set; }
    public string category { get; set; }
    public string content { get; set; }
    public string properties { get; set; }
}