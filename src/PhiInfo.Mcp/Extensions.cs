using System.Collections.Generic;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Text.Unicode;
using Microsoft.Extensions.DependencyInjection;
using PhiInfo.Mcp.Tools;
using PhiInfo.Processing;

namespace PhiInfo.Mcp;

[JsonSerializable(typeof(double?))]
[JsonSerializable(typeof(PhiInfoTool.DifficultyRange))]
[JsonSerializable(typeof(PhiInfoTool.Level?))]
[JsonSerializable(typeof(PhiInfoTool.FileItemField?))]
[JsonSerializable(typeof(int?))]
[JsonSerializable(typeof(List<PhiInfoTool.McpChapterInfo>))]
[JsonSerializable(typeof(List<PhiInfoTool.McpCollectionFolder>))]
[JsonSerializable(typeof(Dictionary<PhiInfoTool.FileItemField,string>))]
[JsonSerializable(typeof(List<PhiInfoTool.McpFileItem>))]
public partial class McpJsonContext : JsonSerializerContext
{
}

public static class Extensions
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        TypeInfoResolver = JsonTypeInfoResolver.Combine(JsonContext.Default, McpJsonContext.Default)
    };

    public static IMcpServerBuilder WithPhiInfoTools(this IMcpServerBuilder mcp)
    {
        return mcp.WithTools<PhiInfoTool>(Options);
    }

    public static bool FuzzyMatch(this string source, string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
            return true;

        if (string.IsNullOrWhiteSpace(source))
            return false;

        source = Normalize(source);
        keyword = Normalize(keyword);

        if (source.Contains(keyword))
            return true;

        if (keyword.Length <= 2)
            return source.Contains(keyword);

        var grams = NGrams(keyword, 3);
        if (grams.Count == 0)
            return false;

        var hit = 0;

        foreach (var g in grams)
            if (source.Contains(g))
                hit++;

        var ratio = (double)hit / grams.Count;

        var threshold = keyword.Length > 10 ? 0.6 : 0.4;

        return ratio >= threshold;
    }

    private static List<string> NGrams(string s, int n)
    {
        var list = new List<string>();

        if (string.IsNullOrEmpty(s) || s.Length < n)
            return list;

        for (var i = 0; i <= s.Length - n; i++) list.Add(s.Substring(i, n));

        return list;
    }

    private static string Normalize(string s)
    {
        if (string.IsNullOrEmpty(s))
            return string.Empty;

        return new string(
            s.ToLowerInvariant()
                .Where(char.IsLetterOrDigit)
                .ToArray()
        );
    }
}