using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Util;
using PhiInfo.Core;
using PhiInfo.Core.Type;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using System.Threading.Tasks;
using static Android.Resource;

namespace PhiInfo.Android
{
    [JsonSerializable(typeof(List<SongInfo>))]
    [JsonSerializable(typeof(List<Folder>))]
    [JsonSerializable(typeof(List<Avatar>))]
    [JsonSerializable(typeof(List<string>))]
    [JsonSerializable(typeof(List<ChapterInfo>))]
    public partial class JsonContext : JsonSerializerContext
    {
    }

    [Service(Exported = false, ForegroundServiceType = ForegroundService.TypeDataSync)]
    public class HttpServerService : Service
    {
        private const string TAG = "PhiInfoHttpService";
        private const int NOTIFICATION_ID = 41669;
        private const string CHANNEL_ID = "phiinfo_channel";
        private const string TARGET_PKG = "com.PigeonGames.Phigros";

        private HttpListener _listener;
        private bool _isRunning;
        private readonly JsonContext _jsonContext = new(new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        });

        private PhiInfoAsset _phiAsset;

        private PhiInfo.Core.PhiInfo _phiInfo;

        private ZipArchive _zip;

        public override void OnCreate()
        {
            base.OnCreate();
            CreateNotificationChannel();
        }

        public override StartCommandResult OnStartCommand(Intent intent, StartCommandFlags flags, int startId)
        {
            var notification = new Notification.Builder(this, CHANNEL_ID)
                .SetContentTitle("PhiInfo Server")
                .SetContentText("服务正在监听端口 41669")
                .SetSmallIcon(Drawable.SymDefAppIcon)
                .SetOngoing(true)
                .Build();

            StartForeground(NOTIFICATION_ID, notification, ForegroundService.TypeDataSync);
            Init();
            StartHttpServer();

            return StartCommandResult.Sticky;
        }

        private void Init() {
            var pm = PackageManager;
            var appInfo = pm.GetApplicationInfo(TARGET_PKG, 0);
            var apkPath = appInfo.SourceDir;
            var apkFs = System.IO.File.OpenRead(appInfo.SourceDir);
            _zip = new ZipArchive(apkFs, ZipArchiveMode.Read);

            var catalogEntry = _zip.GetEntry("assets/aa/catalog.json") ?? throw new System.IO.FileNotFoundException("Catalog not found in APK");

            using var catalogStream = catalogEntry.Open();
            var catalogParser = new CatalogParser(catalogStream);

            _phiAsset = new PhiInfoAsset(catalogParser, (bundleName) =>
            {
                lock (_zip) {
                    var entry = _zip.GetEntry("assets/aa/Android/" + bundleName);
                    return ExtractEntryToMemoryStream(entry);
                }
            });

            var files = SetupFiles(_zip);

            Stream cldbStream = Assets.Open("classdata.tpk");

            _phiInfo = new PhiInfo.Core.PhiInfo(
                files.ggm,
                files.level0,
                files.level22,
                files.il2cppBytes,
                files.metadataBytes,
                cldbStream
            );
        }
        private long GetPhigrosVersion() {
            return PackageManager.GetPackageInfo(TARGET_PKG, 0).LongVersionCode;
        }

        public override void OnDestroy()
        {
            StopHttpServer();
            _phiInfo.Dispose();
            _zip.Dispose();
            _zip = null;
            _phiInfo = null;
            _phiAsset = null;
            base.OnDestroy();
        }

        public override IBinder OnBind(Intent intent) => null;

        private void StartHttpServer()
        {
            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add("http://127.0.0.1:41669/");
                _listener.Start();
                _isRunning = true;

                Log.Info(TAG, "HTTP Server started on http://127.0.0.1:41669/");

                Task.Run(async () =>
                {
                    while (_isRunning)
                    {
                        try
                        {
                            var context = await _listener.GetContextAsync();
                            _ = Task.Run(() => HandleRequest(context));
                        }
                        catch (HttpListenerException)
                        {
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                Log.Error(TAG, $"Failed to start HTTP server: {ex.Message}");
            }
        }

        private async Task HandleRequest(HttpListenerContext context)
        {
            HttpListenerRequest request = context.Request;
            HttpListenerResponse response = context.Response;

            try
            {
                string path = request.Url.AbsolutePath.ToLower();
                string queryPath = request.QueryString["path"];
                byte[] responseBuffer = System.Array.Empty<byte>();
                string contentType = "application/json";

                switch (path)
                {
                    case "/asset/text":
                        if (string.IsNullOrEmpty(queryPath)) throw new Exception("Missing 'path' parameter");
                        responseBuffer = GetAssetText(queryPath);
                        contentType = "text/plain";
                        break;

                    case "/asset/music":
                        if (string.IsNullOrEmpty(queryPath)) throw new Exception("Missing 'path' parameter");
                        responseBuffer = GetAssetMusic(queryPath);
                        contentType = "application/octet-stream";
                        break;

                    case "/asset/image":
                        if (string.IsNullOrEmpty(queryPath)) throw new Exception("Missing 'path' parameter");
                        responseBuffer = GetAssetImage(queryPath);
                        contentType = "application/octet-stream";
                        break;

                    case "/info/songs":
                        responseBuffer = Encoding.UTF8.GetBytes(GetInfoSongs());
                        break;

                    case "/info/collection":
                        responseBuffer = Encoding.UTF8.GetBytes(GetInfoCollection());
                        break;

                    case "/info/avatars":
                        responseBuffer = Encoding.UTF8.GetBytes(GetInfoAvatars());
                        break;

                    case "/info/tips":
                        responseBuffer = Encoding.UTF8.GetBytes(GetInfoTips());
                        break;

                    case "/info/chapters":
                        responseBuffer = Encoding.UTF8.GetBytes(GetInfoChapters());
                        break;

                    case "/info/version":
                        responseBuffer = Encoding.UTF8.GetBytes(GetInfoVersion());
                        contentType = "text/plain";
                        break;

                    default:
                        response.StatusCode = (int)HttpStatusCode.NotFound;
                        responseBuffer = Encoding.UTF8.GetBytes("Not Found");
                        break;
                }

                response.ContentType = contentType;
                response.ContentLength64 = responseBuffer.Length;
                response.AddHeader("Access-Control-Allow-Origin", "*");
                
                await response.OutputStream.WriteAsync(responseBuffer);
            }
            catch (Exception ex)
            {
                Log.Error(TAG, $"Request error: {ex.Message}");
                response.StatusCode = (int)HttpStatusCode.InternalServerError;
                byte[] errorBuffer = Encoding.UTF8.GetBytes($"500 Internal Server Error: {ex.Message}");
                try { await response.OutputStream.WriteAsync(errorBuffer, 0, errorBuffer.Length); } catch { }
            }
            finally
            {
                response.Close();
            }
        }

        private byte[] GetAssetText(string path)
        {
            var textData = _phiAsset.GetText(path);
            return Encoding.UTF8.GetBytes(textData.content);
        }

        private byte[] GetAssetMusic(string path)
        {
            var musicData = _phiAsset.GetMusicRaw(path);
            return musicData.data;
        }

        private byte[] GetAssetImage(string path)
        {
            var imageData = _phiAsset.GetImageRaw(path);
            return imageData.WithHeader();
        }

        private string GetInfoSongs()
        {
            lock (_phiInfo)
            {
                return JsonSerializer.Serialize(
                    _phiInfo.ExtractSongInfo(),
                    _jsonContext.ListSongInfo
                );
            }
        }

        private string GetInfoCollection()
        {
            lock (_phiInfo)
            {
                return JsonSerializer.Serialize(
                    _phiInfo.ExtractCollection(),
                    _jsonContext.ListFolder
                );
            }
        }

        private string GetInfoAvatars()
        {
            lock (_phiInfo)
            {
                return JsonSerializer.Serialize(
                    _phiInfo.ExtractAvatars(),
                    _jsonContext.ListAvatar
                );
            }
        }

        private string GetInfoTips()
        {
            lock (_phiInfo)
            {
                return JsonSerializer.Serialize(
                    _phiInfo.ExtractTips(),
                    _jsonContext.ListString
                );
            }
        }

        private string GetInfoChapters()
        {
            lock (_phiInfo)
            {
                return JsonSerializer.Serialize(
                    _phiInfo.ExtractChapters(),
                    _jsonContext.ListChapterInfo
                );
            }
        }
        private string GetInfoVersion()
        {
            return GetPhigrosVersion().ToString();
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
            var level22Parts = new List<(int index, byte[] data)>();

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

            return new Files
            {
                ggm = ggm,
                level0 = level0,
                il2cppBytes = il2cppBytes,
                metadataBytes = metadataBytes,
                level22 = level22,
            };
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

        private void StopHttpServer()
        {
            _isRunning = false;
            if (_listener != null)
            {
                _listener.Stop();
                _listener.Close();
                _listener = null;
            }
            Log.Info(TAG, "HTTP Server stopped.");
        }

        private void CreateNotificationChannel()
        {
            var channel = new NotificationChannel(CHANNEL_ID, "HTTP Server 状态", NotificationImportance.Low)
            {
                Description = "显示 PhiInfo 本地 HTTP 服务器的运行状态"
            };
            var manager = (NotificationManager)GetSystemService(NotificationService);
            manager.CreateNotificationChannel(channel);
        }
    }
}