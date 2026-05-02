using System;
using System.CommandLine;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using PhiInfo.Core;
using PhiInfo.Mcp;
using PhiInfo.Processing;
using PhiInfo.Processing.Type;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;

namespace PhiInfo.CLI;

internal static class McpServer
{
    private static readonly Argument<Transport> TransportArgument = new("transport")
    {
        Description = "Transport Mode"
    };

    private static readonly Option<ushort> PortOption = new("--port")
    {
        Description = "HTTP server port",
        DefaultValueFactory = _ => 3000
    };

    private static readonly Option<string> HostOption = new("--host")
    {
        Description = "HTTP server host",
        DefaultValueFactory = _ => "127.0.0.1"
    };

    public static readonly Command Command = new("mcp", "Run MCP server mode")
    {
        Arguments = { TransportArgument },
        Options = { PortOption, HostOption },
        Action = new CommandLineAction(HandleCommand)
    };

    private static int HandleCommand(ParseResult parseResult)
    {
        try
        {
            var transport = parseResult.GetValue(TransportArgument);
            using var context = Program.GetContext(parseResult);
            var formatName = parseResult.GetValue(Program.ImageFormatOption)!;
            var format = Configuration.Default.ImageFormatsManager.FindByName(formatName);

            return transport switch
            {
                Transport.Stdio => RunStdioServer(context, format),
                Transport.HttpServer => RunHttpServer(context, format, parseResult),
                _ => 1
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"MCP server error: {ex.Message}");
            return 1;
        }
    }

    private static int RunStdioServer(PhiInfoContext context, IImageFormat? format)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IPhiInfoRouter>(new PhiInfoRouter(context, "MCP", format));
        services.AddLogging();
        services.AddMcpServer()
            .WithStdioServerTransport()
            .WithPhiInfoTools();

        using var provider = services.BuildServiceProvider();
        var server = provider.GetRequiredService<ModelContextProtocol.Server.McpServer>();

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        server.RunAsync(cts.Token).GetAwaiter().GetResult();
        return 0;
    }

    private static int RunHttpServer(PhiInfoContext context, IImageFormat? format, ParseResult parseResult)
    {
        var port = parseResult.GetValue(PortOption);
        var host = parseResult.GetValue(HostOption)!;
        var url = $"http://{host}:{port}";

        Environment.SetEnvironmentVariable("ASPNETCORE_URLS", url);

        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton<IPhiInfoRouter>(new PhiInfoRouter(context, "MCP", format));
        builder.Services.AddLogging();
        builder.Services.AddMcpServer()
            .WithHttpTransport()
            .WithPhiInfoTools();

        var app = builder.Build();
        app.MapMcp();

        app.Run();
        return 0;
    }

    private enum Transport
    {
        Stdio,
        HttpServer
    }
}