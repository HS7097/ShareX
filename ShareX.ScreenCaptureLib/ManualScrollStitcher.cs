// SPDX-License-Identifier: GPL-3.0-or-later

using SkiaSharp;
using System;
using System.Collections.Generic;

namespace ShareX.ScreenCaptureLib;

internal enum ManualScrollFrameResult
{
    Appended,
    Unchanged,
    NoOverlap
}

/// <summary>
/// Matches adjacent viewport images and retains only newly exposed bottom strips.
/// The caller owns input frames; this object owns its reference frame and strips.
/// </summary>
internal sealed class ManualScrollStitcher : IDisposable
{
    private const int SignatureColumns = 32;
    private const long MaxResultBytes = 512L * 1024 * 1024;
    private readonly List<SKBitmap> strips = new();
    private SKBitmap previous;
    private byte[] previousSignature;

    public int Height { get; private set; }
    public int FrameCount => strips.Count;

    public ManualScrollFrameResult AddFrame(SKBitmap frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.Width < 32 || frame.Height < 64)
        {
            throw new ArgumentException("The scrolling region must be at least 32 by 64 pixels.", nameof(frame));
        }

        using SKBitmap current = Normalize(frame);
        if (previous == null)
        {
            CheckSize(current.Width, current.Height);
            previous = current.Copy();
            previousSignature = CreateSignature(current);
            strips.Add(current.Copy());
            Height = current.Height;
            return ManualScrollFrameResult.Appended;
        }

        if (current.Width != previous.Width || current.Height != previous.Height)
        {
            throw new InvalidOperationException("The scrolling region changed size during capture.");
        }

        if (PixelsEqual(previous, current))
        {
            return ManualScrollFrameResult.Unchanged;
        }

        byte[] signature = CreateSignature(current);
        int offset = FindOffset(previousSignature, signature, current.Height);
        if (offset < 0)
        {
            return ManualScrollFrameResult.NoOverlap;
        }

        if (offset == 0)
        {
            return ManualScrollFrameResult.Unchanged;
        }

        CheckSize(current.Width, checked(Height + offset));
        using SKBitmap strip = new(current.Info.WithSize(current.Width, offset));
        using (SKCanvas canvas = new(strip))
        using (SKPaint paint = new() { BlendMode = SKBlendMode.Src })
        {
            canvas.DrawBitmap(current, 0, offset - current.Height, paint);
        }

        strips.Add(strip.Copy());
        Height += offset;
        previous.Dispose();
        previous = current.Copy();
        previousSignature = signature;
        return ManualScrollFrameResult.Appended;
    }

    public SKBitmap BuildResult()
    {
        if (previous == null)
        {
            return null;
        }

        SKBitmap result = new(previous.Info.WithSize(previous.Width, Height));
        try
        {
            using SKCanvas canvas = new(result);
            using SKPaint paint = new() { BlendMode = SKBlendMode.Src };
            int y = 0;
            foreach (SKBitmap strip in strips)
            {
                canvas.DrawBitmap(strip, 0, y, paint);
                y += strip.Height;
            }
            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }

    private static void CheckSize(int width, int height)
    {
        if ((long)width * height * 4 > MaxResultBytes)
        {
            throw new InvalidOperationException("The capture reached the 512 MiB image limit. Save this part and capture the next section separately.");
        }
    }

    private static SKBitmap Normalize(SKBitmap frame)
    {
        SKBitmap result = new(new SKImageInfo(frame.Width, frame.Height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        using SKCanvas canvas = new(result);
        canvas.Clear(SKColors.White);
        canvas.DrawBitmap(frame, 0, 0);
        return result;
    }

    private static unsafe bool PixelsEqual(SKBitmap left, SKBitmap right)
    {
        return new ReadOnlySpan<byte>(left.GetPixels().ToPointer(), left.ByteCount)
            .SequenceEqual(new ReadOnlySpan<byte>(right.GetPixels().ToPointer(), right.ByteCount));
    }

    private static unsafe byte[] CreateSignature(SKBitmap image)
    {
        byte[] signature = new byte[image.Height * SignatureColumns * 3];
        byte* pixels = (byte*)image.GetPixels();
        int margin = Math.Max(1, image.Width / 20);
        int width = image.Width - margin * 2;
        for (int y = 0; y < image.Height; y++)
        {
            for (int column = 0; column < SignatureColumns; column++)
            {
                int x0 = margin + column * width / SignatureColumns;
                int x1 = Math.Max(x0 + 1, margin + (column + 1) * width / SignatureColumns);
                int b = 0, g = 0, r = 0;
                for (int x = x0; x < x1; x++)
                {
                    byte* pixel = pixels + y * image.RowBytes + x * 4;
                    b += pixel[0];
                    g += pixel[1];
                    r += pixel[2];
                }
                int index = (y * SignatureColumns + column) * 3;
                signature[index] = (byte)(b / (x1 - x0));
                signature[index + 1] = (byte)(g / (x1 - x0));
                signature[index + 2] = (byte)(r / (x1 - x0));
            }
        }
        return signature;
    }

    private static int FindOffset(byte[] before, byte[] after, int height)
    {
        int maxOffset = height - Math.Max(32, height / 3);
        double[] scores = new double[maxOffset + 1];
        int bestOffset = 0;
        double bestScore = double.MaxValue;
        for (int offset = 0; offset <= maxOffset; offset++)
        {
            double score = Score(before, after, height, offset, false);
            scores[offset] = score;
            if (score < bestScore)
            {
                bestScore = score;
                bestOffset = offset;
            }
        }

        // A small amount of rasterization noise is allowed, but a guessed displacement is not.
        if (bestScore > 5 || Score(before, after, height, bestOffset, true) > 5)
        {
            return -1;
        }

        if (bestOffset == 0)
        {
            return 0;
        }

        double nextScore = double.MaxValue;
        for (int offset = 0; offset <= maxOffset; offset++)
        {
            if (Math.Abs(offset - bestOffset) > 2)
            {
                nextScore = Math.Min(nextScore, scores[offset]);
            }
        }

        return nextScore > bestScore + Math.Max(0.5, bestScore * 0.4) ? bestOffset : -1;
    }

    private static double Score(byte[] before, byte[] after, int height, int offset, bool dense)
    {
        int overlap = height - offset;
        int rowStep = dense ? 1 : Math.Max(1, overlap / 48);
        int edge = Math.Min(8, overlap / 8);
        long difference = 0;
        int count = 0;
        for (int y = edge; y < overlap - edge; y += rowStep)
        {
            int a = (y + offset) * SignatureColumns * 3;
            int b = y * SignatureColumns * 3;
            for (int x = 0; x < SignatureColumns * 3; x++)
            {
                difference += Math.Abs(before[a + x] - after[b + x]);
                count++;
            }
        }
        return count == 0 ? double.MaxValue : (double)difference / count;
    }

    public void Dispose()
    {
        previous?.Dispose();
        previous = null;
        previousSignature = null;
        foreach (SKBitmap strip in strips)
        {
            strip.Dispose();
        }
        strips.Clear();
        Height = 0;
    }
}
