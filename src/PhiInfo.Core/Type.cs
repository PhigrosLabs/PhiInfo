#pragma warning disable IDE1006
#pragma warning disable IDE0130

using System.Collections.Generic;

namespace PhiInfo.Core.Type
{
    public struct SongLevel
    {
        public string charter { get; set; }
        public int all_combo_num { get; set; }
        public double difficulty { get; set; }
    }

    public struct SongInfo
    {
        public string id { get; set; }
        public string key { get; set; }
        public string name { get; set; }
        public string composer { get; set; }
        public string illustrator { get; set; }
        public double preview_time { get; set; }
        public double preview_end_time { get; set; }
        public Dictionary<string, SongLevel> levels { get; set; }
    }

    public struct Folder
    {
        public string title { get; set; }
        public string sub_title { get; set; }
        public string cover { get; set; }
        public List<FileItem> files { get; set; }
    }
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

    public struct Avatar
    {
        public string name { get; set; }
        public string addressable_key { get; set; }
    }

    public struct AllInfo
    {
        public List<SongInfo> songs { get; set; }
        public List<Folder> collection { get; set; }
        public List<Avatar> avatars { get; set; }
        public List<string> tips { get; set; }
    }

    public struct Catalog
    {
        public string m_KeyDataString { get; set; }
        public string m_BucketDataString { get; set; }
        public string m_EntryDataString { get; set; }
    }

    public struct Image
    {
        public int width { get; set; }
        public int height { get; set; }
        public byte[] data { get; set; }
    }

    public struct Music
    {
        public float length { get; set; }
        public byte[] data { get; set; }
    }

    public struct Text
    {
        public string content { get; set; }
    }

    public struct SongAsset
    {
        public Dictionary<string, Text> charts { get; set; }
        public Image illustration { get; set; }
        public Image illustration_low_res { get; set; }
        public Image illustration_blur { get; set; }
        public Music music { get; set; }
    }

    public struct AllAssets
    {
        public Dictionary<string, SongAsset> songs { get; set; }
        public Dictionary<string, Image> collection_covers { get; set; }
        public Dictionary<string, Image> avatars { get; set; }
    }

    // Metadata-only versions for tar packing
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
        public ImageMetadata illustration_blur { get; set; }
        public MusicMetadata music { get; set; }
    }

    public struct AllAssetsMetadata
    {
        public Dictionary<string, SongAssetMetadata> songs { get; set; }
        public Dictionary<string, ImageMetadata> collection_covers { get; set; }
        public Dictionary<string, ImageMetadata> avatars { get; set; }
    }
}