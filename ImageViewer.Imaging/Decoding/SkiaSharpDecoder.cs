using SkiaSharp;
using ImageViewer.Imaging.Models;

namespace ImageViewer.Imaging.Decoding;

public sealed class SkiaSharpDecoder : IImageDecoder
{
    public async Task<DecodedImage> DecodeAsync(string fullPath, DecodeLimits limits, CancellationToken cancellationToken)
    {
        return await Task.Run(() => DecodeSync(fullPath, limits, cancellationToken), cancellationToken);
    }

    private static DecodedImage DecodeSync(string fullPath, DecodeLimits limits, CancellationToken cancellationToken)
    {
        var canonicalPath = Path.GetFullPath(fullPath);
        var info = new FileInfo(canonicalPath);
        if (!info.Exists)
            throw new FileNotFoundException("Image file not found.", canonicalPath);

        if (info.Length > limits.MaxFileSizeBytes)
            throw new InvalidDataException($"File exceeds max size limit ({limits.MaxFileSizeBytes} bytes).");

        var fileBytes = File.ReadAllBytes(canonicalPath);
        cancellationToken.ThrowIfCancellationRequested();

        using var codec = SKCodec.Create(new MemoryStream(fileBytes));
        if (codec is null)
            throw new InvalidDataException($"SkiaSharp cannot decode '{canonicalPath}'.");

        var skInfo = codec.Info;
        if (skInfo.Width > limits.MaxDimension || skInfo.Height > limits.MaxDimension)
            throw new InvalidDataException("Image dimensions exceed configured limit.");

        if ((long)skInfo.Width * skInfo.Height > limits.MaxPixelArea)
            throw new InvalidDataException("Image pixel area exceeds configured limit.");

        var frameCount = codec.FrameCount;
        if (frameCount <= 0) frameCount = 1;

        if (frameCount > limits.MaxFrameCount)
            throw new InvalidDataException("Frame count exceeds configured limit.");

        var frames = new List<DecodedFrame>(frameCount);

        if (frameCount <= 1)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var frame = DecodeSingleFrame(codec, skInfo);
            frames.Add(frame);
        }
        else
        {
            DecodeAllAnimationFrames(codec, skInfo, frameCount, frames, cancellationToken);
        }

        return new DecodedImage(canonicalPath, frames);
    }

    private static DecodedFrame DecodeSingleFrame(SKCodec codec, SKImageInfo skInfo)
    {
        var targetInfo = new SKImageInfo(skInfo.Width, skInfo.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var bitmap = new SKBitmap(targetInfo);

        var result = codec.GetPixels(targetInfo, bitmap.GetPixels());
        if (result != SKCodecResult.Success && result != SKCodecResult.IncompleteInput)
            throw new InvalidDataException($"SkiaSharp decode failed: {result}");

        return ExtractFrame(bitmap, TimeSpan.FromMilliseconds(100));
    }

    private static void DecodeAllAnimationFrames(SKCodec codec, SKImageInfo skInfo, int frameCount,
        List<DecodedFrame> frames, CancellationToken cancellationToken)
    {
        var targetInfo = new SKImageInfo(skInfo.Width, skInfo.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var frameInfos = codec.FrameInfo;

        using var compositeBitmap = new SKBitmap(targetInfo);
        using var canvas = new SKCanvas(compositeBitmap);

        SKBitmap?[] decodedBitmaps = new SKBitmap?[frameCount];

        try
        {
            for (var i = 0; i < frameCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var duration = i < frameInfos.Length ? frameInfos[i].Duration : 100;
                if (duration <= 0) duration = 100;

                var fi = i < frameInfos.Length ? frameInfos[i] : default;

                var priorFrame = fi.RequiredFrame;
                if (priorFrame >= 0 && priorFrame < i && decodedBitmaps[priorFrame] is { } prior)
                {
                    canvas.Clear(SKColors.Transparent);
                    canvas.DrawBitmap(prior, 0, 0);
                }
                else if (i == 0 || priorFrame < 0)
                {
                    canvas.Clear(SKColors.Transparent);
                }

                using var frameBitmap = new SKBitmap(targetInfo);
                var options = new SKCodecOptions(i);
                var result = codec.GetPixels(targetInfo, frameBitmap.GetPixels(), options);

                if (result != SKCodecResult.Success && result != SKCodecResult.IncompleteInput)
                {
                    if (i == 0)
                        throw new InvalidDataException($"SkiaSharp decode failed for frame {i}: {result}");
                    break;
                }

                canvas.DrawBitmap(frameBitmap, 0, 0);

                var snapshot = new SKBitmap(targetInfo);
                compositeBitmap.CopyTo(snapshot);
                decodedBitmaps[i] = snapshot;

                frames.Add(ExtractFrame(snapshot, TimeSpan.FromMilliseconds(duration)));
            }
        }
        finally
        {
            foreach (var bmp in decodedBitmaps)
                bmp?.Dispose();
        }
    }

    private static DecodedFrame ExtractFrame(SKBitmap bitmap, TimeSpan duration)
    {
        var width = bitmap.Width;
        var height = bitmap.Height;
        var stride = bitmap.RowBytes;
        var byteCount = stride * height;

        var buffer = new byte[byteCount];
        System.Runtime.InteropServices.Marshal.Copy(bitmap.GetPixels(), buffer, 0, byteCount);

        return new DecodedFrame(width, height, stride, buffer, duration);
    }
}
