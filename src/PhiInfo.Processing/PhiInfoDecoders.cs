using System;
using AssetRipper.TextureDecoder.Etc;
using AssetRipper.TextureDecoder.Rgb.Formats;
using Fmod5Sharp;
using Fmod5Sharp.CodecRebuilders;
using Shua.UA.Core.Asset;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace PhiInfo.Processing;

public static class PhiInfoDecoders
{
    public static byte[] DecoderMusic(UnityMusic raw)
    {
        var bank = FsbLoader.LoadFsbFromStream(raw.Data);
        var music = FmodVorbisRebuilder.RebuildOggFile(bank.Samples[0]);
        raw.Dispose();
        return music;
    }

    public static Image DecoderImage(UnityImage raw)
    {
        var bytes = new byte[raw.Data.Length];
        raw.Data.ReadExactly(bytes, 0, bytes.Length);
        raw.Dispose();
        Image img;
        switch (raw.Format)
        {
            case 3:
                img = Image.LoadPixelData<Rgb24>(bytes, raw.Width, raw.Height);
                break;

            case 4:
                img = Image.LoadPixelData<Rgba32>(bytes, raw.Width, raw.Height);
                break;

            case 34:
            {
                EtcDecoder.DecompressETC<ColorBGRA<byte>, byte>(
                    bytes, raw.Width, raw.Height, out var data);
                img = Image.LoadPixelData<Bgra32>(data, raw.Width, raw.Height);
                break;
            }

            case 47:
            {
                EtcDecoder.DecompressETC2A8<ColorBGRA<byte>, byte>(
                    bytes, raw.Width, raw.Height, out var data);
                img = Image.LoadPixelData<Bgra32>(data, raw.Width, raw.Height);
                break;
            }

            default:
                throw new NotSupportedException($"Unknown format: {raw.Format}");
        }

        img.Mutate(x => x.Flip(FlipMode.Vertical));
        return img;
    }
}