using Avalonia;
using Avalonia.Media;
using ShareX.ScreenCaptureLib;
using System;
using System.Runtime.ExceptionServices;
using System.Threading;

internal static class WarningBorder
{
    public static void Check()
    {
        ExceptionDispatchInfo failure = null;
        Thread ui = new(() =>
        {
            try
            {
                AppBuilder.Configure<Application>().UsePlatformDetect().SetupWithoutStarting();
                var window = new ScrollingCaptureRegionWindow();
                try
                {
                    var border = (RecordingRegionBorder)window.Content;
                    CheckColor(border, Colors.Lime);
                    window.SetWarning(true);
                    CheckColor(border, Colors.OrangeRed);
                    window.SetWarning(false);
                    CheckColor(border, Colors.Lime);
                }
                finally
                {
                    window.Close();
                }
            }
            catch (Exception ex)
            {
                failure = ExceptionDispatchInfo.Capture(ex);
            }
        });
        ui.SetApartmentState(ApartmentState.STA);
        ui.Start();
        ui.Join();
        failure?.Throw();
    }

    private static void CheckColor(RecordingRegionBorder border, Color expected)
    {
        if (border.AccentBrush is not ISolidColorBrush brush || brush.Color != expected)
            throw new InvalidOperationException($"Expected capture border color {expected}.");
    }
}
