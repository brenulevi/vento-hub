using System;
using System.Runtime.InteropServices;

namespace Vento.Sharing.Desktop.Media;

internal static class ScreenShareFrameScaler
{
    public static byte[] ScaleRgba(ReadOnlySpan<byte> source, int sourceWidth, int sourceHeight, int destWidth, int destHeight)
    {
        if (sourceWidth == destWidth && sourceHeight == destHeight)
            return source.ToArray();

        var destination = new byte[checked(destWidth * destHeight * 4)];
        ScaleRgba(source, sourceWidth, sourceHeight, destination, destWidth, destHeight);
        return destination;
    }

    public static void ScaleRgba(
        ReadOnlySpan<byte> source,
        int sourceWidth,
        int sourceHeight,
        Span<byte> destination,
        int destWidth,
        int destHeight)
    {
        if (sourceWidth == destWidth && sourceHeight == destHeight)
        {
            source.CopyTo(destination);
            return;
        }

        unsafe
        {
            fixed (byte* srcBase = source)
            fixed (byte* dstBase = destination)
            {
                ScaleBgra((IntPtr)srcBase, sourceWidth, sourceHeight, sourceWidth * 4, dstBase, destWidth, destHeight);
            }
        }
    }

    public static void ScaleBgra(
        IntPtr source,
        int sourceWidth,
        int sourceHeight,
        int sourceStride,
        byte[] destination,
        int destWidth,
        int destHeight)
    {
        unsafe
        {
            fixed (byte* dstBase = destination)
                ScaleBgra(source, sourceWidth, sourceHeight, sourceStride, dstBase, destWidth, destHeight);
        }
    }

    private static unsafe void ScaleBgra(
        IntPtr source,
        int sourceWidth,
        int sourceHeight,
        int sourceStride,
        byte* dstBase,
        int destWidth,
        int destHeight)
    {
        var src = (byte*)source;
        for (var y = 0; y < destHeight; y++)
        {
            var sy = y * sourceHeight / destHeight;
            var srcRow = src + sy * sourceStride;
            var dstRow = dstBase + y * destWidth * 4;
            for (var x = 0; x < destWidth; x++)
            {
                var sx = x * sourceWidth / destWidth;
                var si = sx * 4;
                var di = x * 4;
                dstRow[di] = srcRow[si];
                dstRow[di + 1] = srcRow[si + 1];
                dstRow[di + 2] = srcRow[si + 2];
                dstRow[di + 3] = srcRow[si + 3];
            }
        }
    }
}
