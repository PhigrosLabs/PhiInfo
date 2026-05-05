#pragma warning disable IDE1006
// ReSharper disable InconsistentNaming

using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using PhiInfo.Core.Type;
using PhiInfo.Processing;
using PhiInfo.Processing.Type;

namespace PhiInfo.Mcp.Tools;

public sealed partial class PhiInfoTool
{
    [JsonConverter(typeof(JsonStringEnumConverter<FileItemCondition>))]
    public enum FileItemCondition
    {
        folder_index,
        key,
        name,
        date,
        supervisor,
        category,
        content,
        properties
    }

    [McpServerTool(UseStructuredContent = true, ReadOnly = true, OpenWorld = false)]
    [Description("Get the list of Phigros collection folders")]
    public static async Task<List<McpCollectionFolder>> PhiInfoGetCollectionFolders(
        IPhiInfoRouter client,
        [Description("Language")] Language lang)
    {
        var resp = await client.HandleAsync("/info/collection.json");
        if (resp.code != 200)
            throw new McpException(
                "Server returned an error: " +
                Encoding.UTF8.GetString(resp.data ?? []));

        var folders = JsonSerializer.Deserialize(resp.data, JsonContext.Default.ListFolder);
        if (folders is null)
            return [];

        return folders
            .Select((f, i) => new McpCollectionFolder(
                i,
                GetLang(f.title, lang),
                GetLang(f.sub_title, lang),
                f.cover
            ))
            .ToList();
    }

    [McpServerTool(UseStructuredContent = true, ReadOnly = true, OpenWorld = false)]
    [Description("Get files matching conditions from Phigros collection")]
    public static async Task<List<McpFileItem>> PhiInfoGetCollectionFile(
        IPhiInfoRouter client,
        [Description("Language")] Language lang,
        [Description("Filter conditions")] List<FileFilter>? filters = null,
        [Description("Maximum number of results")]
        int? limit = null)
    {
        var resp = await client.HandleAsync("/info/collection.json");
        if (resp.code != 200)
            throw new McpException(
                "Server returned an error: " +
                Encoding.UTF8.GetString(resp.data ?? []));

        var folders = JsonSerializer.Deserialize(resp.data, JsonContext.Default.ListFolder);
        if (folders is null)
            return [];

        IEnumerable<FileItem> files;

        var folderIndexes = filters?
            .Where(f => f.type == FileItemCondition.folder_index)
            .Select(f => f.value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => int.TryParse(v, out var i) ? i : -1)
            .Where(i => i >= 0 && i < folders.Count)
            .ToHashSet();

        if (folderIndexes == null || folderIndexes.Count == 0)
            files = folders.SelectMany(f => f.files);
        else
            files = folders
                .Where((_, idx) => folderIndexes.Contains(idx))
                .SelectMany(f => f.files);

        if (filters is not null)
            filters = filters
                .Where(f => f.type != FileItemCondition.folder_index)
                .ToList();

        var query = files;

        if (filters is not null && filters.Count > 0)
            query = query.Where(f => MatchesFilter(f, filters, lang));

        if (limit.HasValue && limit.Value > 0)
            query = query.Take(limit.Value);

        return query
            .Select(f => f.ToLocalized(lang))
            .ToList();
    }

    [McpServerTool(UseStructuredContent = true, ReadOnly = true, OpenWorld = false)]
    [Description("Get all distinct values of a field from Phigros collection")]
    public static async Task<List<string>> PhiInfoGetCollectionFileConditions(
        IPhiInfoRouter client,
        [Description("Field to query")] FileItemCondition condition,
        [Description("Language")] Language lang,
        [Description("Filter conditions")] List<FileFilter>? filters = null,
        [Description("Maximum number of results")]
        int? limit = null)
    {
        var resp = await client.HandleAsync("/info/collection.json");
        if (resp.code != 200)
            throw new McpException(
                "Server returned an error: " +
                Encoding.UTF8.GetString(resp.data ?? []));

        var folders = JsonSerializer.Deserialize(resp.data, JsonContext.Default.ListFolder);
        if (folders is null)
            return [];

        IEnumerable<FileItem> files;

        var folderIndexes = filters?
            .Where(f => f.type == FileItemCondition.folder_index)
            .Select(f => f.value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => int.TryParse(v, out var i) ? i : -1)
            .Where(i => i >= 0 && i < folders.Count)
            .ToHashSet();

        if (folderIndexes == null || folderIndexes.Count == 0)
            files = folders.SelectMany(f => f.files);
        else
            files = folders
                .Where((_, idx) => folderIndexes.Contains(idx))
                .SelectMany(f => f.files);

        var query = files
            .Select(f => GetFieldValue(f, condition, lang))
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct();

        if (limit.HasValue && limit.Value > 0)
            query = query.Take(limit.Value);

        return query.ToList();
    }

    private static bool MatchesFilter(
        FileItem item,
        List<FileFilter> filters,
        Language lang)
    {
        foreach (var filter in filters)
        {
            var itemValue = GetFieldValue(item, filter.type, lang);

            if (string.IsNullOrWhiteSpace(itemValue))
                return false;

            if (!itemValue.FuzzyMatch(filter.value))
                return false;
        }

        return true;
    }

    private static string GetFieldValue(FileItem item, FileItemCondition condition, Language lang)
    {
        return condition switch
        {
            FileItemCondition.key => item.key,
            FileItemCondition.name => GetLang(item.name, lang),
            FileItemCondition.date => item.date,
            FileItemCondition.supervisor => GetLang(item.supervisor, lang),
            FileItemCondition.category => item.category,
            FileItemCondition.content => GetLang(item.content, lang),
            FileItemCondition.properties => GetLang(item.properties, lang),
            _ => ""
        };
    }

    internal static string GetLang(Dictionary<Language, string> dict, Language lang)
    {
        if (dict.TryGetValue(lang, out var v))
            return v;

        return dict.TryGetValue(Language.zh_cn, out var fallback) ? fallback : "";
    }

    public record McpFileItem(
        string key,
        int sub_index,
        string name,
        string date,
        string supervisor,
        string category,
        string content,
        string properties
    );

    public record McpCollectionFolder(
        int index,
        string title,
        string sub_title,
        string cover
    );

    public record FileFilter(
        FileItemCondition type,
        string value
    );
}

public static class FileItemExtensions
{
    public static PhiInfoTool.McpFileItem ToLocalized(
        this FileItem f,
        Language lang)
    {
        return new PhiInfoTool.McpFileItem(
            f.key,
            f.sub_index,
            PhiInfoTool.GetLang(f.name, lang),
            f.date,
            PhiInfoTool.GetLang(f.supervisor, lang),
            f.category,
            PhiInfoTool.GetLang(f.content, lang),
            PhiInfoTool.GetLang(f.properties, lang)
        );
    }
}