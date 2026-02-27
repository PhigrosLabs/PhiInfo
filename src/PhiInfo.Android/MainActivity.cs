using System.IO;
using System.IO.Compression;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using System.Threading.Tasks;
using Android.App;
using Android.OS;
using Android.Widget;

namespace PhiInfo.Android
{
    [JsonSerializable(typeof(Core.Type.AllInfo))]
    public partial class JsonContext : JsonSerializerContext
    {
    }

    [Activity(Label = "PhiInfo.Android", MainLauncher = true)]
    public class MainActivity : Activity
    {
        private static readonly JsonContext context = new(new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
            WriteIndented = false
        });


        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            SetContentView(Resource.Layout.activity_main);

            var text = FindViewById<TextView>(Resource.Id.infoText);
            var updateButton = FindViewById<Button>(Resource.Id.updateButton);


            // 按钮点击事件
            updateButton.Click += (s, e) =>
            {
                updateButton.Enabled = false;
                updateButton.Text = "更新中...";

                Task.Run(() =>
                {
                    try
                    {
                        UpdatePhigrosInfo();
                        RunOnUiThread(async () =>
                        {
                            text.Text = "✓ 信息已更新";
                            await Task.Delay(2000);
                            text.Text = "";
                        });
                    }
                    catch (System.Exception ex)
                    {
                        RunOnUiThread(() =>
                        {
                            text.Text = $"✗ 更新失败: {ex.Message}";
                        });
                    }
                    finally
                    {
                        RunOnUiThread(() =>
                        {
                            updateButton.Enabled = true;
                            updateButton.Text = "更新信息";
                        });
                    }
                });
            };
        }

        private void UpdatePhigrosInfo()
        {
            var pm = PackageManager;
            var appInfo = pm.GetApplicationInfo("com.PigeonGames.Phigros", 0);
            var apkPath = appInfo.SourceDir;
            var apkVersion = pm.GetPackageInfo("com.PigeonGames.Phigros", 0).LongVersionCode;

            var outputDir = this.FilesDir;

            var allInfoPath = Path.Combine(outputDir.AbsolutePath, "all_info.json");
            var versionPath = Path.Combine(outputDir.AbsolutePath, "version.txt");
            using (var apkFs = File.OpenRead(apkPath))
            using (var zip = new ZipArchive(apkFs, ZipArchiveMode.Read))
            {
                var files = SetupFiles(zip);

                Stream cldbStream = Assets.Open("classdata.tpk");

                var phiInfo = new PhiInfo.Core.PhiInfo(
                    files.ggm,
                    files.level0,
                    files.level22,
                    files.il2cppBytes,
                    files.metadataBytes,
                    cldbStream
                );

                var allInfo = phiInfo.ExtractAll();
                var allInfoJson = JsonSerializer.Serialize(allInfo, context.AllInfo);

                File.WriteAllText(allInfoPath, allInfoJson);

                File.WriteAllText(versionPath, apkVersion.ToString());

                phiInfo.Dispose();
                cldbStream.Dispose();
            }
        }

        private struct Files
        {
            public Stream ggm;
            public Stream level0;
            public byte[] il2cppBytes;
            public byte[] metadataBytes;
            public Stream level22;
        }


        private static Files SetupFiles(ZipArchive zip)
        {
            Stream ggm = null;
            Stream level0 = null;
            byte[] il2cppBytes = null;
            byte[] metadataBytes = null;
            var level22Parts = new System.Collections.Generic.List<(int index, byte[] data)>();

            foreach (var entry in zip.Entries)
            {
                switch (entry.FullName)
                {
                    case "assets/bin/Data/globalgamemanagers.assets":
                        ggm = ExtractEntryToMemoryStream(entry);
                        break;
                    case "assets/bin/Data/level0":
                        level0 = ExtractEntryToMemoryStream(entry);
                        break;
                    case "lib/arm64-v8a/libil2cpp.so":
                        il2cppBytes = ExtractEntryToMemory(entry);
                        break;
                    case "assets/bin/Data/Managed/Metadata/global-metadata.dat":
                        metadataBytes = ExtractEntryToMemory(entry);
                        break;
                }

                if (entry.FullName.StartsWith("assets/bin/Data/level22.split"))
                {
                    string suffix = entry.FullName["assets/bin/Data/level22.split".Length..];
                    int index = int.Parse(suffix);
                    level22Parts.Add((index, ExtractEntryToMemory(entry)));
                }
            }

            if (ggm == null || level0 == null || il2cppBytes == null || metadataBytes == null || level22Parts.Count == 0)
                throw new System.IO.FileNotFoundException("Required Unity assets not found in APK");

            level22Parts.Sort((a, b) => a.index.CompareTo(b.index));

            Stream level22 = new MemoryStream();
            foreach (var part in level22Parts)
                level22.Write(part.data, 0, part.data.Length);

            level22.Position = 0;

            var files = new Files
            {
                ggm = ggm,
                level0 = level0,
                il2cppBytes = il2cppBytes,
                metadataBytes = metadataBytes,
                level22 = level22,
            };

            return files;
        }

        private static byte[] ExtractEntryToMemory(ZipArchiveEntry entry)
        {
            using (var ms = new MemoryStream())
            using (var entryStream = entry.Open())
            {
                entryStream.CopyTo(ms);
                return ms.ToArray();
            }
        }

        private static MemoryStream ExtractEntryToMemoryStream(ZipArchiveEntry entry)
        {
            var ms = new MemoryStream();
            using (var entryStream = entry.Open())
            {
                entryStream.CopyTo(ms);
            }
            ms.Position = 0;
            return ms;
        }
    }
}