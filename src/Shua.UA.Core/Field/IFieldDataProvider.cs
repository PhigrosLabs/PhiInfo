using System.IO;

namespace Shua.UA.Core.Field;

public interface IFieldDataProvider
{
    Stream GetCldb();
    Stream GetGlobalGameManagers();
    byte[] GetIl2CppBinary();
    byte[] GetGlobalMetadata();
}