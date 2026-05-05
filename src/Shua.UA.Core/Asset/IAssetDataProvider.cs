using System.IO;

namespace Shua.UA.Core.Asset;

public interface IAssetDataProvider
{
    Stream GetCatalog();
    Stream GetBundle(string name);
}