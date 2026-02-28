using Android.Content;
using Android.Content.Res;
using Android.Database;
using Android.OS;
using Android.Util;
using Java.IO;
using PhiInfo.Core;
using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using Uri = Android.Net.Uri;

namespace PhiInfo.Android
{
    [ContentProvider(
        new[] { "dev.phigros_labs.PhiInfo.provider" },
        Name = "PhiInfo.Android.PhiInfoProvider",
        Exported = true,
        GrantUriPermissions = true
    )]
    public class PhiInfoProvider : ContentProvider
    {
        private const string TAG = "PhiInfoProvider";
        private const string TARGET_PKG = "com.PigeonGames.Phigros";
        private static PhiInfoAsset _cachedAsset;
        private static ZipArchive _apkZip;
        private static readonly Lock _lock = new();

        public override bool OnCreate() => true;

        static MemoryStream ExtractEntryToMemoryStream(ZipArchiveEntry entry)
        {
            var ms = new MemoryStream();
            using (var entryStream = entry.Open())
            {
                entryStream.CopyTo(ms);
            }
            ms.Position = 0;
            return ms;
        }
        private PhiInfoAsset GetOrInitAsset()
        {
            lock (_lock)
            {
                if (_cachedAsset != null) return _cachedAsset;

                try
                {
                    var appInfo = Context.PackageManager.GetApplicationInfo(TARGET_PKG, 0);
                    var apkFs = System.IO.File.OpenRead(appInfo.SourceDir);
                    _apkZip = new ZipArchive(apkFs, ZipArchiveMode.Read);

                    var catalogEntry = _apkZip.GetEntry("assets/aa/catalog.json")
                        ?? throw new System.IO.FileNotFoundException("Catalog not found in APK");

                    using var catalogStream = catalogEntry.Open();
                    var catalogParser = new CatalogParser(catalogStream);

                    _cachedAsset = new PhiInfoAsset(catalogParser, (bundleName) =>
                    {
                        var entry = _apkZip.GetEntry("assets/aa/Android/" + bundleName);
                        return ExtractEntryToMemoryStream(entry);
                    });

                    return _cachedAsset;
                }
                catch (Exception ex)
                {
                    Log.Error(TAG, $"Initialization failed: {ex.Message}");
                    throw;
                }
            }
        }

        public override ParcelFileDescriptor OpenFile(Uri uri, string mode)
        {
            Log.Debug(TAG, $"OpenFile: {uri}");
            try
            {
                var path = uri.Path ?? "";
                var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (segments.Length < 2) throw new ArgumentException("Invalid URI format");

                var resourceType = segments[0];
                var resourcePath = string.Join("/", segments, 1, segments.Length - 1);

                byte[] data;
                var asset = GetOrInitAsset();

                data = resourceType switch
                {
                    "text" => Encoding.UTF8.GetBytes(asset.GetText(resourcePath).content),
                    "music" => asset.GetMusicRaw(resourcePath).data,
                    "image" => asset.GetImageRaw(resourcePath).WithHeader(),
                    "info" => HandleInfoRequest(resourcePath),
                    _ => throw new ArgumentException($"Unknown type: {resourceType}"),
                };

                if (data == null) throw new System.IO.FileNotFoundException("Resource not found");

                return SystemDataToPfd(data);
            }
            catch (Exception ex)
            {
                Log.Error(TAG, $"OpenFile Exception: {ex}");
                throw new Java.IO.FileNotFoundException(ex.Message);
            }
        }

        private ParcelFileDescriptor SystemDataToPfd(byte[] data)
        {
            var file = new Java.IO.File(Context.CacheDir, Guid.NewGuid().ToString());
            using (var fs = new FileOutputStream(file))
            {
                fs.Write(data);
            }
            return ParcelFileDescriptor.Open(file, ParcelFileMode.ReadOnly);
        }

        private byte[] HandleInfoRequest(string resourcePath)
        {
            if (!resourcePath.Equals("version.txt", StringComparison.OrdinalIgnoreCase) &&
                !resourcePath.Equals("all_info.json", StringComparison.OrdinalIgnoreCase))
            {
                throw new System.IO.FileNotFoundException($"Forbidden info path: {resourcePath}");
            }

            string fullPath = Path.Combine(Context.FilesDir.AbsolutePath, resourcePath);
            if (!System.IO.File.Exists(fullPath))
                throw new System.IO.FileNotFoundException($"File not found: {resourcePath}");

            return System.IO.File.ReadAllBytes(fullPath);
        }
        public override string GetType(Uri uri) => "application/octet-stream";
        public override AssetFileDescriptor OpenAssetFile(Uri uri, string mode)
            => new AssetFileDescriptor(OpenFile(uri, mode), 0, AssetFileDescriptor.UnknownLength);
        public override int Delete(Uri uri, string selection, string[] selectionArgs) => 0;
        public override Uri Insert(Uri uri, ContentValues values) => null;
        public override ICursor Query(Uri uri, string[] proj, string sel, string[] args, string sort) => null;
        public override int Update(Uri uri, ContentValues val, string sel, string[] args) => 0;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _apkZip?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}