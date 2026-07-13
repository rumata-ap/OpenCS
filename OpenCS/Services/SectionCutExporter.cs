using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OpenCS.Services
{
    /// <summary>Экспорт панели эпюры разреза в файл (PNG/SVG/DXF по расширению пути).</summary>
    public static class SectionCutExporter
    {
        public static void ExportPng(FrameworkElement element, string path)
        {
            int w = (int)element.ActualWidth, h = (int)element.ActualHeight;
            if (w < 1 || h < 1) return;

            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(element);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            using var fs = new FileStream(path, FileMode.Create);
            encoder.Save(fs);
        }
    }
}
