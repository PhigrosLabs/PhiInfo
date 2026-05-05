using System.IO;

namespace PhiInfo.Core;

public interface IInfoDataProvider
{
    Stream GetLevel0();
    Stream GetLevel22();
}