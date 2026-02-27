namespace PhiInfo.NativeAPI;

using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using Core;

public struct LastError
{
    public string? Error { get; set; }
    public string? StackTrace { get; set; }
}

[JsonSerializable(typeof(LastError))]
[JsonSerializable(typeof(Core.Type.AllInfo))]
public partial class JsonContext : JsonSerializerContext
{
}

public static class PhiInfoNativeApi
{
    private static readonly JsonContext context = new(new JsonSerializerOptions
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        WriteIndented = false
    });

    [ThreadStatic]
    private static LastError? _lastError;
    private static PhiInfo? _phi;

    private static void SetLastError(Exception? ex)
    {
        if (ex == null)
        {
            _lastError = null!;
            return;
        }

        _lastError = new LastError
        {
            Error = ex.Message,
            StackTrace = ex.StackTrace
        };
    }

    private static IntPtr ToUtf8(string str)
    {
        if (str == null) return IntPtr.Zero;
        byte[] bytes = Encoding.UTF8.GetBytes(str + '\0');
        IntPtr ptr = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, ptr, bytes.Length);
        return ptr;
    }

    private static byte[] Copy(IntPtr ptr, int len)
    {
        var buf = new byte[len];
        Marshal.Copy(ptr, buf, 0, len);
        return buf;
    }

    [UnmanagedCallersOnly(EntryPoint = "init", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static bool Init(
        IntPtr ggm, int ggmLen,
        IntPtr level0, int level0Len,
        IntPtr level22, int level22Len,
        IntPtr cldb, int cldbLen,
        IntPtr il2cppSo, int il2cppSoLen,
        IntPtr metaData, int metaDataLen
    )
    {
        try
        {
            _phi?.Dispose();

            _phi = new PhiInfo(
                new MemoryStream(Copy(ggm, ggmLen)),
                new MemoryStream(Copy(level0, level0Len)),
                new MemoryStream(Copy(level22, level22Len)),
                Copy(il2cppSo, il2cppSoLen),
                Copy(metaData, metaDataLen),
                new MemoryStream(Copy(cldb, cldbLen))
            );

            SetLastError(null);
            return true;
        }
        catch (Exception ex)
        {
            SetLastError(ex);
            return false;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "reset", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static bool Reset()
    {
        try
        {
            _phi?.Dispose();
            _phi = null;

            SetLastError(null);
            return true;
        }
        catch (Exception ex)
        {
            SetLastError(ex);
            return false;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "extract_all", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static IntPtr ExtractAll()
    {
        try
        {
            if (_phi == null)
                throw new InvalidOperationException("PhiInfo not created");

            var data = _phi.ExtractAll();
            SetLastError(null);

            string json = JsonSerializer.Serialize(data, context.AllInfo);
            return ToUtf8(json);
        }
        catch (Exception ex)
        {
            SetLastError(ex);
            return IntPtr.Zero;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "free_string", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static bool FreeString(IntPtr ptr)
    {
        try
        {
            if (ptr != IntPtr.Zero)
                Marshal.FreeHGlobal(ptr);

            SetLastError(null);
            return true;
        }
        catch (Exception ex)
        {
            SetLastError(ex);
            return false;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "get_last_error", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static IntPtr GetLastError()
    {
        if (_lastError == null)
            return IntPtr.Zero;

        string json = JsonSerializer.Serialize(_lastError, context.LastError);
        return ToUtf8(json);
    }
}
