using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using PhiInfo.Core.Info;
using PhiInfo.Core.Type;
using PhiInfo.Processing;
using PhiInfo.Processing.Type;

namespace PhiInfo.Mcp.Tools;

public sealed partial class PhiInfoTool
{
    [JsonConverter(typeof(JsonStringEnumConverter<FileItemField>))]
    public enum FileItemField
    {
        Key,
        Name,
        Date,
        Supervisor,
        Category,
        Content,
        Properties
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
    [Description("Get files matching conditions from a specified Phigros collection folder")]
    public static async Task<List<McpFileItem>> PhiInfoGetCollectionFile(
        IPhiInfoRouter client,
        [Description("Folder index")] int folderIndex,
        [Description("Language")] Language lang,
        [Description("Filter conditions")]
        Dictionary<FileItemField, string>? filters = null,
        [Description("Maximum number of results")] int? limit = null)
    {
        var resp = await client.HandleAsync("/info/collection.json");
        if (resp.code != 200)
            throw new McpException(
                "Server returned an error: " +
                Encoding.UTF8.GetString(resp.data ?? []));

        var folders = JsonSerializer.Deserialize(resp.data, JsonContext.Default.ListFolder);
        if (folders is null || folderIndex < 0 || folderIndex >= folders.Count)
            return [];

        var files = folders[folderIndex].files;

        IEnumerable<FileItem> query = files;

        if (filters is not null && filters.Count > 0)
            query = query.Where(f => MatchesFilter(f, filters, lang));

        if (limit.HasValue && limit.Value > 0)
            query = query.Take(limit.Value);

        return query
            .Select(f => f.ToLocalized(lang))
            .ToList();
    }

    [McpServerTool(UseStructuredContent = true, ReadOnly = true, OpenWorld = false)]
    [Description("Get all distinct values of a field from a specified Phigros collection folder")]
    public static async Task<List<string>> PhiInfoGetCollectionFileConditions(
        IPhiInfoRouter client,
        [Description("Collection folder index")] int folderIndex,
        [Description("Field to query")] FileItemField field,
        [Description("Language")] Language lang,
        [Description("Maximum number of results")] int? limit = null)
    {
        var resp = await client.HandleAsync("/info/collection.json");
        if (resp.code != 200)
            throw new McpException(
                "Server returned an error: " +
                Encoding.UTF8.GetString(resp.data ?? []));

        var folders = JsonSerializer.Deserialize(resp.data, JsonContext.Default.ListFolder);
        if (folders is null || folderIndex < 0 || folderIndex >= folders.Count)
            return [];

        var query = folders[folderIndex].files
            .Select(f => GetFieldValue(f, field, lang))
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct();

        if (limit.HasValue && limit.Value > 0)
            query = query.Take(limit.Value);

        return query.ToList();
    }
    
    private static bool MatchesFilter(
        FileItem item,
        Dictionary<FileItemField, string> filters,
        Language lang)
    {
        foreach (var (field, value) in filters)
        {
            var itemValue = GetFieldValue(item, field, lang);
            if (string.IsNullOrWhiteSpace(itemValue))
                return false;

            if (!itemValue.Contains(value, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    private static string GetFieldValue(FileItem item, FileItemField field, Language lang)
    {
        return field switch
        {
            FileItemField.Key => item.key,
            FileItemField.Name => GetLang(item.name, lang),
            FileItemField.Date => item.date,
            FileItemField.Supervisor => GetLang(item.supervisor, lang),
            FileItemField.Category => item.category,
            FileItemField.Content => GetLang(item.content, lang),
            FileItemField.Properties => GetLang(item.properties, lang),
            _ => ""
        };
    }

    internal static string GetLang(Dictionary<Language, string> dict, Language lang)
    {
        if (dict.TryGetValue(lang, out var v))
            return v;

        return dict.TryGetValue(Language.zh_cn, out var fallback) ? fallback : "";
    }
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