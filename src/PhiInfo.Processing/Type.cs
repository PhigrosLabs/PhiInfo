using System.IO;
using System.Threading.Tasks;

#pragma warning disable IDE1006
#pragma warning disable IDE0130

namespace PhiInfo.Processing.Type;

public record struct Suffix(string image, string text, string music);

public record ApiInfo(string version, string type, Suffix suffix);

public record Response(ushort code, string? mime, byte[]? data);

public interface IOutputWriter
{
    Stream Create(string path, string mime);
}

public interface IPhiInfoRouter
{
    public Task<Response> HandleAsync(string path);
}