using System.Text;
using System.Text.Json;
using OpenCvSharp;

namespace SistemaAvaliacao.Web.Services.Omr;

/// <summary>
/// Produz a imagem de debug anotada (sobre a folha já normalizada) e o dump JSON
/// com o que foi detectado. É a saída pedida pelo usuário para conferência.
/// </summary>
public static class DebugRenderer
{
    private static Scalar ColorFor(QuestionStatus s) => s switch
    {
        QuestionStatus.Valida => new Scalar(0, 170, 0),        // verde
        QuestionStatus.Dupla => new Scalar(0, 0, 220),         // vermelho
        QuestionStatus.EmBranco => new Scalar(150, 150, 150),  // cinza
        QuestionStatus.RasuraDuvidosa => new Scalar(0, 140, 255), // laranja
        _ => Scalar.Black
    };

    private static string ShortLabel(QuestionStatus s) => s switch
    {
        QuestionStatus.Valida => "OK",
        QuestionStatus.Dupla => "DUP",
        QuestionStatus.EmBranco => "---",
        QuestionStatus.RasuraDuvidosa => "RAS",
        _ => "?"
    };

    /// <summary>
    /// Desenha anotações sobre a folha normalizada. Retorna um novo Mat (não altera o original).
    /// </summary>
    public static Mat Annotate(Mat warped, SheetLayout layout, SheetReadResult result, int optionCount)
    {
        var img = warped.Clone();
        const HersheyFonts font = HersheyFonts.HersheySimplex;

        // Cabeçalho de status
        string head = result.Aligned
            ? $"ALINHADO | QR: {(result.QrPayload ?? "N/A")} | exam={result.ExamId} token={result.Token ?? "N/A"}"
            : "NAO ALINHADO (marcadores nao detectados)";
        Cv2.PutText(img, head, new Point(20, 30), font, 0.55, new Scalar(80, 0, 0), 2);

        foreach (var q in result.Questions)
        {
            int qi = q.Number - 1;
            var color = ColorFor(q.Status);

            for (int opt = 0; opt < optionCount; opt++)
            {
                var c = layout.BubbleCenter(qi, opt);
                var center = new Point((int)Math.Round(c.X), (int)Math.Round(c.Y));

                // Marca a região amostrada
                Cv2.Circle(img, center, layout.BubbleSampleRadius, color, 1, LineTypes.AntiAlias);

                // Destaca a alternativa detectada
                if (q.DetectedOption.HasValue && layout.OptionLabels[opt] == q.DetectedOption.Value)
                    Cv2.Circle(img, center, layout.BubbleSampleRadius + 3, color, 2, LineTypes.AntiAlias);
            }

            // Rótulo de status + confiança à direita da última bolha
            var lastC = layout.BubbleCenter(qi, optionCount - 1);
            var tag = new Point((int)lastC.X + 18, (int)lastC.Y + 4);
            string txt = $"{ShortLabel(q.Status)}";
            if (q.DetectedOption.HasValue) txt += $":{q.DetectedOption}";
            txt += $" {q.Confidence:0.00}";
            Cv2.PutText(img, txt, tag, font, 0.32, color, 1, LineTypes.AntiAlias);
        }

        // Legenda
        DrawLegend(img, layout, font);
        return img;
    }

    private static void DrawLegend(Mat img, SheetLayout layout, HersheyFonts font)
    {
        int y = layout.CanvasHeight - 20;
        var items = new (string label, QuestionStatus s)[]
        {
            ("OK=valida", QuestionStatus.Valida),
            ("DUP=dupla", QuestionStatus.Dupla),
            ("---=branco", QuestionStatus.EmBranco),
            ("RAS=rasura", QuestionStatus.RasuraDuvidosa),
        };
        int x = 20;
        foreach (var it in items)
        {
            Cv2.Circle(img, new Point(x, y - 4), 6, ColorFor(it.s), -1);
            Cv2.PutText(img, it.label, new Point(x + 12, y), font, 0.4, ColorFor(it.s), 1);
            x += 150;
        }
    }

    /// <summary>Serializa o resultado em JSON legível.</summary>
    public static string ToJson(SheetReadResult result)
    {
        var payload = new
        {
            result.Aligned,
            result.QrPayload,
            result.ExamId,
            result.Token,
            result.Warnings,
            Summary = new
            {
                Total = result.Questions.Count,
                Validas = result.Questions.Count(q => q.Status == QuestionStatus.Valida),
                Duplas = result.Questions.Count(q => q.Status == QuestionStatus.Dupla),
                EmBranco = result.Questions.Count(q => q.Status == QuestionStatus.EmBranco),
                Rasuras = result.Questions.Count(q => q.Status == QuestionStatus.RasuraDuvidosa),
            },
            Questions = result.Questions.Select(q => new
            {
                q.Number,
                Status = q.Status.ToString(),
                Option = q.DetectedOption?.ToString(),
                q.Confidence,
                Fills = q.FillRatios.Select(f => double.IsNaN(f) ? (double?)null : Math.Round(f, 3)).ToArray()
            })
        };

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
    }
}
