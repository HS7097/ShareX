using ShareX.ScreenCaptureLib;
using SkiaSharp;
using System;
using System.Diagnostics;

int passed = 0;
Run("Continuous scrolling preserves every row", () =>
{
    using SKBitmap page = PatternPage(256, 2400);
    foreach (int step in new[] { 1, 7, 23, 60, 100 })
    {
        using ManualScrollStitcher stitcher = new();
        int last = 0;
        for (int top = 0; top <= 800; top += step)
        {
            using SKBitmap frame = Crop(page, top, 240);
            Require(stitcher.AddFrame(frame) == ManualScrollFrameResult.Appended, $"Step {step}, top {top} was not matched");
            last = top;
        }
        using SKBitmap result = stitcher.BuildResult();
        using SKBitmap expected = Crop(page, 0, last + 240);
        Require(Equal(result, expected), $"Step {step} lost, duplicated or altered rows");
    }
});

Run("Pauses and the end of the page do not append duplicates", () =>
{
    using SKBitmap page = PatternPage(320, 1000);
    using ManualScrollStitcher stitcher = new();
    using SKBitmap first = Crop(page, 0, 240);
    stitcher.AddFrame(first);
    for (int i = 0; i < 100; i++)
    {
        Require(stitcher.AddFrame(first) == ManualScrollFrameResult.Unchanged, "Paused viewport changed");
    }
    using SKBitmap next = Crop(page, 35, 240);
    Require(stitcher.AddFrame(next) == ManualScrollFrameResult.Appended, "Capture did not resume after pause");
    for (int i = 0; i < 20; i++) stitcher.AddFrame(next);
    Require(stitcher.Height == 275 && stitcher.FrameCount == 2, "Paused frames duplicated output");
});

Run("Text content stitches at a steady speed", () =>
{
    using SKBitmap page = TextPage(640, 2400);
    using ManualScrollStitcher stitcher = new();
    for (int top = 0; top <= 600; top += 12)
    {
        using SKBitmap frame = Crop(page, top, 480);
        Require(stitcher.AddFrame(frame) == ManualScrollFrameResult.Appended, $"Text overlap missing at {top}");
    }
    using SKBitmap result = stitcher.BuildResult();
    using SKBitmap expected = Crop(page, 0, 1080);
    Require(Equal(result, expected), "Text output contains a seam or missing rows");
});

Run("Small rasterization differences are tolerated", () =>
{
    using SKBitmap page = PatternPage(256, 1000);
    using ManualScrollStitcher stitcher = new();
    using SKBitmap first = Crop(page, 0, 240);
    stitcher.AddFrame(first);
    using SKBitmap next = Crop(page, 19, 240);
    AddNoise(next);
    Require(stitcher.AddFrame(next) == ManualScrollFrameResult.Appended, "Small color noise rejected the correct overlap");
    Require(stitcher.Height == 259, "Noise changed displacement");
});

Run("Missing overlap retains only verified content and permits recovery", () =>
{
    using SKBitmap page = PatternPage(256, 1600);
    using ManualScrollStitcher stitcher = new();
    using SKBitmap first = Crop(page, 0, 240);
    stitcher.AddFrame(first);
    using SKBitmap gap = Crop(page, 650, 240);
    Require(stitcher.AddFrame(gap) == ManualScrollFrameResult.NoOverlap, "A missing region was silently stitched");
    Require(stitcher.Height == 240, "Unmatched pixels were appended");
    using SKBitmap recovered = Crop(page, 25, 240);
    Require(stitcher.AddFrame(recovered) == ManualScrollFrameResult.Appended, "Reference frame was lost after a mismatch");
    using SKBitmap result = stitcher.BuildResult();
    using SKBitmap expected = Crop(page, 0, 265);
    Require(Equal(result, expected), "Recovery output differs from source");
});

Run("Ambiguous repeating patterns do not produce a guessed displacement", () =>
{
    using SKBitmap page = new(256, 1000, SKColorType.Bgra8888, SKAlphaType.Opaque);
    using (SKCanvas canvas = new(page))
    using (SKPaint paint = new())
    {
        for (int y = 0; y < page.Height; y++)
        {
            paint.Color = new SKColor((byte)((y % 40) * 6), 30, 70);
            canvas.DrawRect(0, y, page.Width, 1, paint);
        }
    }
    using ManualScrollStitcher stitcher = new();
    using SKBitmap first = Crop(page, 0, 240);
    using SKBitmap next = Crop(page, 15, 240);
    stitcher.AddFrame(first);
    Require(stitcher.AddFrame(next) == ManualScrollFrameResult.NoOverlap, "Ambiguous pattern guessed an offset");
});

Run("Viewport changes fail explicitly", () =>
{
    using ManualScrollStitcher stitcher = new();
    using SKBitmap first = PatternPage(256, 240);
    using SKBitmap resized = PatternPage(257, 240);
    stitcher.AddFrame(first);
    bool threw = false;
    try { stitcher.AddFrame(resized); }
    catch (InvalidOperationException) { threw = true; }
    Require(threw, "Resize was accepted");
});

Run("HD viewport throughput", () =>
{
    using SKBitmap page = TextPage(1280, 2000);
    using ManualScrollStitcher stitcher = new();
    Stopwatch timer = Stopwatch.StartNew();
    for (int top = 0; top < 200; top += 10)
    {
        using SKBitmap frame = Crop(page, top, 720);
        Require(stitcher.AddFrame(frame) == ManualScrollFrameResult.Appended, $"HD frame missing at {top}");
    }
    Console.WriteLine($"  Average HD frame processing: {timer.Elapsed.TotalMilliseconds / 20:F1} ms");
    Require(stitcher.Height == 910, "HD output height mismatch");
});

Console.WriteLine($"PASS: {passed} manual scrolling capture checks");

void Run(string name, Action test)
{
    try { test(); passed++; Console.WriteLine($"PASS: {name}"); }
    catch (Exception error) { Console.Error.WriteLine($"FAIL: {name}: {error}"); Environment.Exit(1); }
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static SKBitmap Crop(SKBitmap image, int top, int height)
{
    SKBitmap result = new(image.Width, height, SKColorType.Bgra8888, SKAlphaType.Opaque);
    using SKCanvas canvas = new(result);
    using SKPaint paint = new() { BlendMode = SKBlendMode.Src };
    canvas.DrawBitmap(image, 0, -top, paint);
    return result;
}

static unsafe SKBitmap PatternPage(int width, int height)
{
    SKBitmap result = new(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque);
    uint* pixels = (uint*)result.GetPixels();
    Random random = new(4117);
    for (int y = 0; y < height; y++)
    {
        uint color = 0xff000000u | (uint)random.Next(0x101010, 0xeeeeee);
        for (int x = 0; x < width; x++) pixels[y * width + x] = color ^ (uint)((x / 16) * 129);
    }
    return result;
}

static SKBitmap TextPage(int width, int height)
{
    SKBitmap result = new(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque);
    using SKCanvas canvas = new(result);
    canvas.Clear(SKColors.White);
    using SKTypeface typeface = SKTypeface.FromFamilyName("Arial");
    using SKFont font = new(typeface, 20);
    using SKPaint paint = new() { IsAntialias = true };
    Random random = new(67);
    for (int y = 30, row = 1; y < height; y += 35, row++)
    {
        paint.Color = row % 2 == 0 ? SKColors.AliceBlue : SKColors.White;
        canvas.DrawRect(0, y - 24, width, 35, paint);
        paint.Color = SKColors.DarkSlateGray;
        canvas.DrawText($"Row {row:D4}: capture {random.Next():X8} continuous scrolling and pauses", 15, y, font, paint);
    }
    return result;
}

static unsafe void AddNoise(SKBitmap frame)
{
    byte* pixels = (byte*)frame.GetPixels();
    for (int i = 0; i < frame.ByteCount; i++)
    {
        if (i % 4 != 3) pixels[i] = (byte)Math.Clamp(pixels[i] + (i % 3) - 1, 0, 255);
    }
}

static unsafe bool Equal(SKBitmap a, SKBitmap b) =>
    a.Width == b.Width && a.Height == b.Height &&
    new ReadOnlySpan<byte>(a.GetPixels().ToPointer(), a.ByteCount)
        .SequenceEqual(new ReadOnlySpan<byte>(b.GetPixels().ToPointer(), b.ByteCount));
