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
using PhiInfo.Core.Type;
using PhiInfo.Processing;
using PhiInfo.Processing.Type;

namespace PhiInfo.Mcp.Tools;

[McpServerToolType]
public sealed partial class PhiInfoTool
{
    // ReSharper disable InconsistentNaming
    [JsonConverter(typeof(JsonStringEnumConverter<Level>))]
    public enum Level
    {
        EZ,
        HD,
        IN,
        AT,
        Legacy
    }
    // ReSharper restore InconsistentNaming

    [McpServerTool(UseStructuredContent = true, ReadOnly = true, OpenWorld = false)]
    [Description("Search Phigros song information. All parameters are filters.")]
    public static async Task<List<SongInfo>> PhiInfoSearchSongs(
        IPhiInfoRouter client,
        [Description("Unique identifiers")] string[]? ids = null,
        [Description("Name")] string? name = null,
        [Description("Composer")] string? composer = null,
        [Description("Illustrator")] string? illustrator = null,
        [Description("Charter")] string? charter = null,
        [Description("Level")] Level? level = null,
        [Description("Minimum difficulty")] double? difficultyMin = null,
        [Description("Maximum difficulty")] double? difficultyMax = null,
        [Description("Maximum number of results")]
        int? limit = null)
    {
        var resp = await client.HandleAsync("/info/songs.json");
        if (resp.code != 200)
            throw new McpException(
                "Server returned an error: " +
                Encoding.UTF8.GetString(resp.data ?? []));

        var songs = JsonSerializer.Deserialize(resp.data, JsonContext.Default.ListSongInfo);
        if (songs is null)
            return [];

        var hasName = !string.IsNullOrWhiteSpace(name);
        var hasComposer = !string.IsNullOrWhiteSpace(composer);
        var hasIllustrator = !string.IsNullOrWhiteSpace(illustrator);
        var hasCharter = !string.IsNullOrWhiteSpace(charter);

        var filterSongs = songs.Where(song =>
        {
            if (ids is { Length: > 0 } && !ids.Contains(song.id)) return false;
            if (hasName && !song.name.FuzzyMatch(name!)) return false;
            if (hasComposer && !song.composer.FuzzyMatch(composer!)) return false;
            if (hasIllustrator && !song.illustrator.FuzzyMatch(illustrator!)) return false;

            if (hasCharter && !song.levels.Values.Any(l => l.charter.FuzzyMatch(charter!)))
                return false;

            if (level.HasValue)
            {
                if (!song.levels.TryGetValue(level.Value.ToString(), out var l))
                    return false;

                if (difficultyMin.HasValue && l.difficulty < difficultyMin.Value)
                    return false;

                if (difficultyMax.HasValue && l.difficulty > difficultyMax.Value)
                    return false;
            }
            else
            {
                if (difficultyMin.HasValue && difficultyMax.HasValue)
                {
                    if (!song.levels.Values.Any(l =>
                            l.difficulty >= difficultyMin.Value &&
                            l.difficulty <= difficultyMax.Value))
                        return false;
                }
                else if (difficultyMin.HasValue)
                {
                    if (!song.levels.Values.Any(l =>
                            l.difficulty >= difficultyMin.Value))
                        return false;
                }
                else if (difficultyMax.HasValue)
                {
                    if (!song.levels.Values.Any(l =>
                            l.difficulty <= difficultyMax.Value))
                        return false;
                }
            }

            return true;
        }).ToList();

        if (limit.HasValue && limit.Value > 0) filterSongs = filterSongs.Take(limit.Value).ToList();

        return filterSongs;
    }

    [McpServerTool(UseStructuredContent = true, ReadOnly = true, OpenWorld = false)]
    [Description("Get Phigros song difficulty range")]
    public static async Task<DifficultyRange> PhiInfoGetDifficultyRange(
        IPhiInfoRouter client)
    {
        var resp = await client.HandleAsync("/info/songs.json");
        if (resp.code != 200)
            throw new McpException("Server error: " + Encoding.UTF8.GetString(resp.data ?? []));

        var songs = JsonSerializer.Deserialize(resp.data, JsonContext.Default.ListSongInfo);
        if (songs is null)
            return new DifficultyRange(0, 0);

        var diffs = songs
            .SelectMany(s => s.levels.Values)
            .Select(l => l.difficulty)
            .ToList();

        if (diffs.Count == 0)
            return new DifficultyRange(0, 0);

        return new DifficultyRange(diffs.Min(), diffs.Max());
    }

    [McpServerTool(UseStructuredContent = true, ReadOnly = true, OpenWorld = false)]
    [Description("Get random Phigros tips")]
    public static async Task<List<string>> PhiInfoGetRandomTip(
        IPhiInfoRouter client,
        Language language,
        int count = 1)
    {
        var resp = await client.HandleAsync("/info/tips.json");

        if (resp.code != 200)
            throw new McpException(
                "Server returned an error: " +
                Encoding.UTF8.GetString(resp.data ?? []));

        var tips = JsonSerializer.Deserialize(
            resp.data,
            JsonContext.Default.DictionaryLanguageListString);

        if (tips == null || tips.Count == 0)
            return [];

        if (!tips.TryGetValue(language, out var list) || list.Count == 0)
        {
            list = tips.Values.FirstOrDefault();
            if (list == null || list.Count == 0)
                return [];
        }

        var random = Random.Shared;

        count = Math.Clamp(count, 1, list.Count);

        var result = list
            .OrderBy(_ => random.Next())
            .Take(count)
            .ToList();

        return result;
    }

    public record struct DifficultyRange(double min, double max);
}