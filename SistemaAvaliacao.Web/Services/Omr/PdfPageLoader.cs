using OpenCvSharp;
using PDFtoImage;
using SkiaSharp;

namespace SistemaAvaliacao.Web.Services.Omr;

/// <summary>
/// Converte um PDF em imagens OpenCV (uma por página).
/// Usa PDFtoImage (PDFium + SkiaSharp); o SKBitmap é codificado em PNG na memória
/// e decodificado como <see cref="Mat"/> (evita marshaling manual de pixels).
/// </summary>
public static class PdfPageLoader
{
    /// <summary>Renderiza cada página do PDF como um Mat BGR. O chamador deve dar Dispose em cada Mat.</summary>
    public static IEnumerable<Mat> LoadPages(string pdfPath, int dpi = 200)
    {
        using var fs = File.OpenRead(pdfPath);
        var options = new RenderOptions(Dpi: dpi);

        foreach (var sk in Conversion.ToImages(fs, leaveOpen: false, password: null, options: options))
        {
            using (sk)
            {
                yield return SkBitmapToMat(sk);
            }
        }
    }

    /// <summary>Renderiza uma única página (1-based).</summary>
    public static Mat LoadPage(string pdfPath, int pageNumber1Based, int dpi = 200)
    {
        using var fs = File.OpenRead(pdfPath);
        var options = new RenderOptions(Dpi: dpi);
        using var sk = Conversion.ToImage(fs, page: pageNumber1Based - 1, leaveOpen: false, password: null, options: options);
        return SkBitmapToMat(sk);
    }

    public static Mat SkBitmapToMat(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        byte[] bytes = data.ToArray();
        // ImDecode -> BGR (3 canais)
        return Cv2.ImDecode(bytes, ImreadModes.Color);
    }
}
