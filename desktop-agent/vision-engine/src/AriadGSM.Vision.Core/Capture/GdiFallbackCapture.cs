using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace AriadGSM.Vision.Capture;

public sealed class GdiFallbackCapture : IScreenCapture
{
    public ValueTask<ScreenFrame> CaptureAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var left = GetSystemMetrics(SystemMetric.VirtualScreenLeft);
        var top = GetSystemMetrics(SystemMetric.VirtualScreenTop);
        var width = GetSystemMetrics(SystemMetric.VirtualScreenWidth);
        var height = GetSystemMetrics(SystemMetric.VirtualScreenHeight);
        if (width <= 0 || height <= 0)
        {
            throw new InvalidOperationException("No pude detectar el tamano de pantalla virtual.");
        }

        var screenDc = GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero)
        {
            ThrowLastWin32("GetDC");
        }

        var memoryDc = IntPtr.Zero;
        var bitmap = IntPtr.Zero;
        var oldObject = IntPtr.Zero;
        try
        {
            memoryDc = CreateCompatibleDC(screenDc);
            if (memoryDc == IntPtr.Zero)
            {
                ThrowLastWin32("CreateCompatibleDC");
            }

            bitmap = CreateCompatibleBitmap(screenDc, width, height);
            if (bitmap == IntPtr.Zero)
            {
                ThrowLastWin32("CreateCompatibleBitmap");
            }

            oldObject = SelectObject(memoryDc, bitmap);
            if (oldObject == IntPtr.Zero)
            {
                ThrowLastWin32("SelectObject");
            }

            if (!BitBlt(memoryDc, 0, 0, width, height, screenDc, left, top, RasterOperation.SourceCopy))
            {
                ThrowLastWin32("BitBlt");
            }

            var bitmapBytes = ReadBitmapBytes(memoryDc, bitmap, width, height);
            var hash = Convert.ToHexString(SHA256.HashData(bitmapBytes)).ToLowerInvariant();
            var capturedAt = DateTimeOffset.UtcNow;
            var frameId = $"gdi-{capturedAt:yyyyMMddHHmmssfff}";
            return ValueTask.FromResult(new ScreenFrame(frameId, capturedAt, width, height, bitmapBytes, hash, "gdi"));
        }
        finally
        {
            if (oldObject != IntPtr.Zero && memoryDc != IntPtr.Zero)
            {
                _ = SelectObject(memoryDc, oldObject);
            }
            if (bitmap != IntPtr.Zero)
            {
                _ = DeleteObject(bitmap);
            }
            if (memoryDc != IntPtr.Zero)
            {
                _ = DeleteDC(memoryDc);
            }
            _ = ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private static byte[] ReadBitmapBytes(IntPtr memoryDc, IntPtr bitmap, int width, int height)
    {
        var stride = width * 4;
        var imageSize = stride * height;
        var pixels = new byte[imageSize];
        var info = new BitmapInfo
        {
            Header = new BitmapInfoHeader
            {
                Size = Marshal.SizeOf<BitmapInfoHeader>(),
                Width = width,
                Height = -height,
                Planes = 1,
                BitCount = 32,
                Compression = 0,
                SizeImage = imageSize
            }
        };

        var lines = GetDIBits(memoryDc, bitmap, 0, (uint)height, pixels, ref info, DibRgbColors);
        if (lines == 0)
        {
            ThrowLastWin32("GetDIBits");
        }

        return BuildBmp(width, height, pixels);
    }

    private static byte[] BuildBmp(int width, int height, byte[] pixels)
    {
        const int fileHeaderSize = 14;
        const int infoHeaderSize = 40;
        var fileSize = fileHeaderSize + infoHeaderSize + pixels.Length;
        var bytes = new byte[fileSize];

        bytes[0] = (byte)'B';
        bytes[1] = (byte)'M';
        WriteInt32(bytes, 2, fileSize);
        WriteInt32(bytes, 10, fileHeaderSize + infoHeaderSize);
        WriteInt32(bytes, 14, infoHeaderSize);
        WriteInt32(bytes, 18, width);
        WriteInt32(bytes, 22, -height);
        WriteInt16(bytes, 26, 1);
        WriteInt16(bytes, 28, 32);
        WriteInt32(bytes, 30, 0);
        WriteInt32(bytes, 34, pixels.Length);
        System.Buffer.BlockCopy(pixels, 0, bytes, fileHeaderSize + infoHeaderSize, pixels.Length);
        return bytes;
    }

    private static void WriteInt16(byte[] bytes, int offset, short value)
    {
        var raw = BitConverter.GetBytes(value);
        System.Buffer.BlockCopy(raw, 0, bytes, offset, raw.Length);
    }

    private static void WriteInt32(byte[] bytes, int offset, int value)
    {
        var raw = BitConverter.GetBytes(value);
        System.Buffer.BlockCopy(raw, 0, bytes, offset, raw.Length);
    }

    private static void ThrowLastWin32(string operation)
    {
        throw new Win32Exception(Marshal.GetLastWin32Error(), $"{operation} fallo.");
    }

    private const int DibRgbColors = 0;

    private enum SystemMetric
    {
        VirtualScreenLeft = 76,
        VirtualScreenTop = 77,
        VirtualScreenWidth = 78,
        VirtualScreenHeight = 79
    }

    private enum RasterOperation : uint
    {
        SourceCopy = 0x00CC0020
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public BitmapInfoHeader Header;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public int Size;
        public int Width;
        public int Height;
        public short Planes;
        public short BitCount;
        public int Compression;
        public int SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public int ClrUsed;
        public int ClrImportant;
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(SystemMetric nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateCompatibleDC(IntPtr hDc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hDc, int width, int height);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool BitBlt(
        IntPtr hdc,
        int x,
        int y,
        int cx,
        int cy,
        IntPtr hdcSrc,
        int x1,
        int y1,
        RasterOperation rop);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern int GetDIBits(
        IntPtr hdc,
        IntPtr hbm,
        uint start,
        uint cLines,
        byte[] lpvBits,
        ref BitmapInfo lpbmi,
        int usage);
}
