using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DrawingPixelFormat = System.Drawing.Imaging.PixelFormat;
using MediaPixelFormats = System.Windows.Media.PixelFormats;

namespace OpenCS.Services;

/// <summary>
/// Копирует визуал эпюры разреза в буфер обмена как EMF
/// (WYSIWYG-снимок текущего масштаба и видимостей).
/// </summary>
public static class SectionCutEmfClipboard
{
    const uint CF_ENHMETAFILE = 14;
    const uint CF_BITMAP = 2;

    /// <summary>
    /// Рендерит <paramref name="element"/> и кладёт в буфер EMF (+ bitmap-fallback).
    /// </summary>
    public static bool TryCopy(FrameworkElement element)
    {
        int w = (int)Math.Ceiling(element.ActualWidth);
        int h = (int)Math.Ceiling(element.ActualHeight);
        if (w < 1 || h < 1) return false;

        element.UpdateLayout();
        var rtb = new RenderTargetBitmap(w, h, 96, 96, MediaPixelFormats.Pbgra32);
        rtb.Render(element);

        using var gdiBitmap = ToGdiBitmap(rtb);
        IntPtr hemf = CreateEnhMetafileHandle(gdiBitmap);
        if (hemf == IntPtr.Zero) return false;

        IntPtr hbm = IntPtr.Zero;
        try
        {
            hbm = gdiBitmap.GetHbitmap();

            if (!OpenClipboard(IntPtr.Zero))
                return false;
            try
            {
                EmptyClipboard();

                if (SetClipboardData(CF_ENHMETAFILE, hemf) == IntPtr.Zero)
                    return false;
                hemf = IntPtr.Zero; // ownership → clipboard

                if (hbm != IntPtr.Zero)
                {
                    if (SetClipboardData(CF_BITMAP, hbm) == IntPtr.Zero)
                        DeleteObject(hbm);
                    else
                        hbm = IntPtr.Zero; // ownership → clipboard
                }
            }
            finally
            {
                CloseClipboard();
            }
            return true;
        }
        finally
        {
            if (hemf != IntPtr.Zero) DeleteEnhMetaFile(hemf);
            if (hbm != IntPtr.Zero) DeleteObject(hbm);
        }
    }

    static IntPtr CreateEnhMetafileHandle(Bitmap bmp)
    {
        IntPtr screenDc = GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero) return IntPtr.Zero;
        try
        {
            using var metafile = new Metafile(
                screenDc,
                new Rectangle(0, 0, bmp.Width, bmp.Height),
                MetafileFrameUnit.Pixel,
                EmfType.EmfOnly);
            using (var g = Graphics.FromImage(metafile))
            {
                g.Clear(System.Drawing.Color.White);
                g.DrawImage(bmp, 0, 0, bmp.Width, bmp.Height);
            }

            IntPtr hemf = metafile.GetHenhmetafile();
            if (hemf == IntPtr.Zero) return IntPtr.Zero;

            IntPtr copy = CopyEnhMetaFile(hemf, null);
            DeleteEnhMetaFile(hemf);
            return copy;
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    static Bitmap ToGdiBitmap(RenderTargetBitmap rtb)
    {
        int w = rtb.PixelWidth, h = rtb.PixelHeight;
        var bmp = new Bitmap(w, h, DrawingPixelFormat.Format32bppPArgb);
        var data = bmp.LockBits(
            new Rectangle(0, 0, w, h),
            ImageLockMode.WriteOnly,
            DrawingPixelFormat.Format32bppPArgb);
        try
        {
            rtb.CopyPixels(new Int32Rect(0, 0, w, h), data.Scan0, data.Stride * h, data.Stride);
        }
        finally
        {
            bmp.UnlockBits(data);
        }
        return bmp;
    }

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    static extern IntPtr CopyEnhMetaFile(IntPtr hemfSrc, string? lpszFile);

    [DllImport("gdi32.dll")]
    static extern bool DeleteEnhMetaFile(IntPtr hemf);

    [DllImport("gdi32.dll")]
    static extern bool DeleteObject(IntPtr ho);
}
