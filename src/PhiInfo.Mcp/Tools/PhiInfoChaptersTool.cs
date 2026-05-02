using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using PhiInfo.Processing;
using PhiInfo.Processing.Type;

namespace PhiInfo.Mcp.Tools;

public sealed partial class PhiInfoTool
{
    [McpServerTool(UseStructuredContent = true, ReadOnly = true, OpenWorld = false)]
    [Description("Get all Phigros chapter information")]
    public static async Task<List<McpChapterInfo>> PhiInfoGetChapters(
        IPhiInfoRouter client)
    {
        var resp = await client.HandleAsync("/info/chapters.json");
        if (resp.code != 200)
            throw new McpException(
                "Server returned an error: " +
                Encoding.UTF8.GetString(resp.data ?? []));

        var chapters = JsonSerializer.Deserialize(resp.data, JsonContext.Default.ListChapterInfo);
        if (chapters is null)
            return [];

        return chapters.Select(c => new McpChapterInfo(c.code, c.banner)).ToList();
    }

    [McpServerTool(UseStructuredContent = true, ReadOnly = true, OpenWorld = false)]
    [Description("Get the list of song IDs in a specified chapter")]
    public static async Task<List<string>> PhiInfoGetChapterSongs(
        IPhiInfoRouter client,
        [Description("Chapter code")] string code)
    {
        var resp = await client.HandleAsync("/info/chapters.json");
        if (resp.code != 200)
            throw new McpException(
                "Server returned an error: " +
                Encoding.UTF8.GetString(resp.data ?? []));

        var chapters = JsonSerializer.Deserialize(resp.data, JsonContext.Default.ListChapterInfo);
        if (chapters is null)
            return [];

        var chapter = chapters.FirstOrDefault(c => c.code == code);
        return chapter?.song_ids ?? [];
    }

    public readonly record struct McpChapterInfo(string code, string banner);
}