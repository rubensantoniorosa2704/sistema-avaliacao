using OpenCvSharp;
using OpenCvSharp.Aruco;
using ZXing;
using ZXing.Common;
using Dictionary = OpenCvSharp.Aruco.Dictionary;
using CvAruco = OpenCvSharp.Aruco.CvAruco;

namespace SistemaAvaliacao.Web.Services.Omr;

/// <summary>
/// Núcleo de OMR. Recebe a imagem de uma página (já rasterizada do PDF),
/// alinha pela geometria dos marcadores ArUco, lê o QR e mede o preenchimento
/// de cada bolha. A classificação é feita por <see cref="AnswerClassifier"/>.
/// </summary>
public sealed class OmrReader
{
    private readonly SheetLayout _layout;
    private readonly AnswerClassifier _classifier;
    private readonly Dictionary _arucoDict;
    private readonly ArucoDetector _detector;

    public OmrReader(SheetLayout layout, AnswerClassifier classifier)
    {
        _layout = layout;
        _classifier = classifier;
        _arucoDict = CvAruco.GetPredefinedDictionary(PredefinedDictionaryType.Dict4X4_50);

        // Parâmetros mais tolerantes: janela adaptativa maior e limites de perímetro
        // amplos ajudam a detectar marcadores mesmo com scan de baixa qualidade/rotação.
        var p = new DetectorParameters
        {
            AdaptiveThreshWinSizeMin = 3,
            AdaptiveThreshWinSizeMax = 43,
            AdaptiveThreshWinSizeStep = 8,
            MinMarkerPerimeterRate = 0.01,
            MaxMarkerPerimeterRate = 4.0,
            PolygonalApproxAccuracyRate = 0.05,
        };
        _detector = new ArucoDetector(_arucoDict, p, new RefineParameters());
    }

    /// <summary>
    /// Lê uma página. <paramref name="warped"/> devolve a folha normalizada
    /// (útil para gerar a imagem de debug em cima do mesmo referencial da geometria).
    /// </summary>
    public SheetReadResult Read(Mat page, int optionCount, out Mat? warped)
    {
        warped = null;
        var warnings = new List<string>();

        // 1) Alinhar pela detecção dos ArUco.
        Mat? aligned = TryAlign(page, warnings);
        if (aligned is null)
        {
            return new SheetReadResult
            {
                Aligned = false,
                Questions = Array.Empty<QuestionResult>(),
                Warnings = warnings
            };
        }
        warped = aligned;

        // 2) Ler QR na folha normalizada.
        string? qr = TryReadQr(aligned, warnings);
        var (examId, studentId) = CardGenerator.ParseQrPayload(qr);

        // 3) Binarizar para medir preenchimento (texto/bolhas escuras -> branco na máscara).
        using var gray = new Mat();
        Cv2.CvtColor(aligned, gray, ColorConversionCodes.BGR2GRAY);
        using var mask = new Mat();
        // Adaptativo é robusto a variação de iluminação do scanner.
        Cv2.AdaptiveThreshold(gray, mask, 255, AdaptiveThresholdTypes.MeanC,
            ThresholdTypes.BinaryInv, 25, 10);

        // 4) Medir cada questão e classificar.
        var results = new List<QuestionResult>(_layout.TotalQuestions);
        for (int q = 0; q < _layout.TotalQuestions; q++)
        {
            var fills = MeasureQuestion(mask, q, optionCount);
            results.Add(_classifier.Classify(q + 1, fills));
        }

        return new SheetReadResult
        {
            Aligned = true,
            QrPayload = qr,
            ExamId = examId,
            StudentId = studentId,
            Questions = results,
            Warnings = warnings
        };
    }

    /// <summary>Detecta os 4 marcadores e aplica warp de perspectiva para a folha canônica.</summary>
    private Mat? TryAlign(Mat page, List<string> warnings)
    {
        using var gray = new Mat();
        Cv2.CvtColor(page, gray, ColorConversionCodes.BGR2GRAY);

        _detector.DetectMarkers(gray, out Point2f[][] corners, out int[] ids, out _);

        if (ids is null || ids.Length < 4)
        {
            warnings.Add($"ArUco insuficientes: encontrados {ids?.Length ?? 0} de 4.");
            return null;
        }

        // Centro de cada marcador detectado, indexado por id.
        var detectedCenter = new System.Collections.Generic.Dictionary<int, Point2f>();
        for (int i = 0; i < ids.Length; i++)
        {
            var c = corners[i];
            var center = new Point2f(
                (c[0].X + c[1].X + c[2].X + c[3].X) / 4f,
                (c[0].Y + c[1].Y + c[2].Y + c[3].Y) / 4f);
            detectedCenter[ids[i]] = center;
        }

        // Precisamos dos 4 ids esperados (0..3).
        foreach (var id in _layout.ArucoIds)
        {
            if (!detectedCenter.ContainsKey(id))
            {
                warnings.Add($"Marcador ArUco id={id} não encontrado.");
                return null;
            }
        }

        var canonicalCenters = _layout.ArucoCenters();
        var src = new[]
        {
            detectedCenter[_layout.ArucoIds[0]],
            detectedCenter[_layout.ArucoIds[1]],
            detectedCenter[_layout.ArucoIds[2]],
            detectedCenter[_layout.ArucoIds[3]],
        };
        var dst = new[]
        {
            canonicalCenters[0], canonicalCenters[1], canonicalCenters[2], canonicalCenters[3],
        };

        using var h = Cv2.GetPerspectiveTransform(src, dst);
        var outMat = new Mat();
        Cv2.WarpPerspective(page, outMat, h, new Size(_layout.CanvasWidth, _layout.CanvasHeight),
            InterpolationFlags.Linear, BorderTypes.Constant, Scalar.White);
        return outMat;
    }

    /// <summary>Lê o QR recortando a região esperada da folha normalizada.</summary>
    private string? TryReadQr(Mat aligned, List<string> warnings)
    {
        var rect = _layout.QrRect();
        // Amplia um pouco a janela para tolerar pequenos deslocamentos.
        var pad = 12;
        var wide = new Rect(
            Math.Max(0, rect.X - pad),
            Math.Max(0, rect.Y - pad),
            Math.Min(aligned.Width - Math.Max(0, rect.X - pad), rect.Width + 2 * pad),
            Math.Min(aligned.Height - Math.Max(0, rect.Y - pad), rect.Height + 2 * pad));

        using var roi = new Mat(aligned, wide);
        using var grayRoi = new Mat();
        Cv2.CvtColor(roi, grayRoi, ColorConversionCodes.BGR2GRAY);

        // ZXing a partir de buffer de luminância (sem dependência de bitmap).
        int w = grayRoi.Width, hgt = grayRoi.Height;
        byte[] luminance = new byte[w * hgt];
        System.Runtime.InteropServices.Marshal.Copy(grayRoi.Data, luminance, 0, luminance.Length);

        var source = new RGBLuminanceSource(luminance, w, hgt, RGBLuminanceSource.BitmapFormat.Gray8);
        var binarizer = new HybridBinarizer(source);
        var bitmap = new BinaryBitmap(binarizer);

        var reader = new ZXing.QrCode.QRCodeReader();
        var hints = new System.Collections.Generic.Dictionary<DecodeHintType, object>
        {
            { DecodeHintType.TRY_HARDER, true }
        };
        var result = reader.decode(bitmap, hints);
        if (result is null)
        {
            warnings.Add("QR não decodificado.");
            return null;
        }
        return result.Text;
    }

    /// <summary>Mede a fração de pixels preenchidos (0..1) de cada bolha A..E da questão.</summary>
    private double[] MeasureQuestion(Mat mask, int questionIndex, int optionCount)
    {
        var fills = new double[_layout.OptionsPerQuestion];
        int r = _layout.BubbleSampleRadius;

        for (int opt = 0; opt < _layout.OptionsPerQuestion; opt++)
        {
            if (opt >= optionCount)
            {
                fills[opt] = double.NaN; // alternativa inexistente nesta prova
                continue;
            }

            var c = _layout.BubbleCenter(questionIndex, opt);
            int cx = (int)Math.Round(c.X), cy = (int)Math.Round(c.Y);

            var rect = new Rect(cx - r, cy - r, 2 * r, 2 * r);
            rect = ClampRect(rect, mask.Width, mask.Height);
            if (rect.Width <= 0 || rect.Height <= 0) { fills[opt] = 0; continue; }

            using var patch = new Mat(mask, rect);
            using var circleMask = new Mat(patch.Size(), MatType.CV_8UC1, Scalar.All(0));
            Cv2.Circle(circleMask, new Point(patch.Width / 2, patch.Height / 2), r, Scalar.All(255), -1);

            using var masked = new Mat();
            Cv2.BitwiseAnd(patch, circleMask, masked);

            double filled = Cv2.CountNonZero(masked);
            double area = Cv2.CountNonZero(circleMask);
            fills[opt] = area > 0 ? filled / area : 0;
        }
        return fills;
    }

    private static Rect ClampRect(Rect r, int w, int h)
    {
        int x = Math.Max(0, r.X);
        int y = Math.Max(0, r.Y);
        int x2 = Math.Min(w, r.X + r.Width);
        int y2 = Math.Min(h, r.Y + r.Height);
        return new Rect(x, y, Math.Max(0, x2 - x), Math.Max(0, y2 - y));
    }
}
