using System.Collections.Generic;
using System.IO;

namespace PhiInfo.Core;

public interface IInfoDataProvider
{
    /// <summary>
    ///     读取 assets/bin/Data 下的资源文件,自动处理 .split 分片。
    /// </summary>
    /// <param name="name">文件名,如 level0、level22、sharedassets22.assets</param>
    Stream GetDataFile(string name);

    /// <summary>
    ///     列出 assets/bin/Data 下的资源文件名,已合并 .split 分片。
    /// </summary>
    IReadOnlyList<string> GetDataFileNames();
}
