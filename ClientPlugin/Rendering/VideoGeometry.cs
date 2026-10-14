using VRageMath;
using VRageRender.Messages;

namespace ClientPlugin.Rendering;

internal static class VideoGeometry
{
    // SE 1.210.014 MyVideoPlayer.Draw. Stock computes a crop rectangle
    // but submits sourceRect:null; parity follows the submitted geometry.
    internal static RectangleF Destination(Rectangle rect, int width, int height, MyVideoRectangleFitMode mode)
    {
        if (width <= 0 || height <= 0 || rect.Width <= 0 || rect.Height <= 0) return default;
        var result = rect;
        float aspect = width / (float)height, target = rect.Width / (float)rect.Height;
        if (mode == MyVideoRectangleFitMode.AutoFit) mode = aspect < target ? MyVideoRectangleFitMode.FitHeight : MyVideoRectangleFitMode.FitWidth;
        switch (mode)
        {
            case MyVideoRectangleFitMode.FitWidth: result.Height = System.Math.Min(rect.Height, (int)(rect.Width / (float)width * height)); break;
            case MyVideoRectangleFitMode.FitHeight: result.Width = System.Math.Min(rect.Width, (int)(rect.Height / (float)height * width)); break;
            case MyVideoRectangleFitMode.Fill:
                if (target > aspect) result.Height = (int)(rect.Width / (float)width * height);
                else result.Width = (int)(rect.Height / (float)height * width);
                break;
        }
        result.X = rect.Left + (rect.Width - result.Width) / 2;
        result.Y = rect.Top + (rect.Height - result.Height) / 2;
        return new RectangleF(result.X, result.Y, result.Width, result.Height);
    }
}
