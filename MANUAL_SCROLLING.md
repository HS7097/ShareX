# ShareX Manual

This fork is based on ShareX 22.4.0 and identifies itself as **22.4.0.1 Manual**.

## Manual scrolling capture

1. Open **Capture > Scrolling capture**, select the scrolling content, and stop the first capture to open its options if necessary.
2. In the options, select **Manual scrolling** and save. New configurations use this mode by default; existing configurations retain their saved choice.
3. Start a capture and scroll downward at a steady speed. Pauses and reaching the bottom keep the capture running.
4. Press your scrolling-capture hotkey again, or activate the scrolling-capture window, to finish. Review and save the result.

The default sampling interval is 50 ms. Successive frames must retain at least one third of the viewport in common. Exclude fixed headers, sidebars, scrollbars and animated content from the selected region. Image matching cannot reliably infer movement through featureless or repeating content, or recover content skipped between samples. Capture separate sections in those cases.

An uncertain match turns the region border orange-red and logs a warning. Capture waits up to two seconds for a reliable overlap; an unresolved mismatch displays an error and retains only the matched portion for review. Failed captures are not automatically uploaded. A result is limited to 512 MiB of uncompressed pixels.

## Updates

Application update checks, update prompts, update settings and application-update downloads are removed. Installing a new version of this fork is a manual operation. The FFmpeg dependency downloader remains available for recording functionality.

## Build and check (Windows, .NET 10 SDK)

```powershell
dotnet run --project Tests/ManualScrollingCapture -c Release -p:Platform=x64
dotnet publish ShareX/ShareX.csproj -c Release -p:Platform=x64 --self-contained true -o artifacts/publish
```

The checks cover row-for-row stitching at several speeds, pauses, text, rasterization noise, loss and recovery of overlap, repeating patterns, viewport resizing, and HD frames. Back up the installed application and personal ShareX directory before copying a published build over an installation. Retain dependency tools and existing personal data. Language assemblies in the published output belong under `Languages/<culture>/`, matching ShareX's assembly resolver.
