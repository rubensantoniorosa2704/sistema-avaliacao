using OpenCvSharp;
using OpenCvSharp.Aruco;
using ZXing;
using ZXing.QrCode;
using ZXing.Common;
using Dictionary = OpenCvSharp.Aruco.Dictionary;

namespace SistemaAvaliacao.Web.Services.Omr;

/// <summary>
/// Dados impressos no cabeçalho do cartão (legíveis por humanos na revisão manual).
/// </summary>
public sealed class StudentCardInfo
{
    public required int ExamId { get; init; }
    public required int StudentId { get; init; }
    public string StudentName { get; init; } = "";
    public string ExamName { get; init; } = "";
    /// <summary>Número de alternativas válidas nesta prova (4 => A-D, 5 => A-E).</summary>
    public int OptionCount { get; init; } = 5;
}

/// <summary>
/// Gera a imagem do cartão-resposta (folha canônica) usando a mesma geometria
/// (<see cref="SheetLayout"/>) que o leitor utiliza. Também permite "pintar"
/// marcações para simular scans de alunos (para testes).
/// </summary>
public sealed class CardGenerator
{
    private readonly SheetLayout _layout;
    private readonly Dictionary _arucoDict;

    public CardGenerator(SheetLayout layout)
    {
        _layout = layout;
        _arucoDict = CvAruco.GetPredefinedDictionary(PredefinedDictionaryName.Dict4X4_50);
    }

    /// <summary>Formato do payload do QR: "exam=&lt;id&gt;;student=&lt;id&gt;;v=1".</summary>
    public static string BuildQrPayload(int examId, int studentId)
        => $"exam={examId};student={studentId};v=1";

    public static (int? examId, int? studentId) ParseQrPayload(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return (null, null);
        int? exam = null, student = null;
        foreach (var part in payload.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var kv = part.Split('=', 2);
            if (kv.Length != 2) continue;
            if (kv[0] == "exam" && int.TryParse(kv[1], out var e)) exam = e;
            else if (kv[0] == "student" && int.TryParse(kv[1], out var s)) student = s;
        }
        return (exam, student);
    }

    /// <summary>
    /// Gera o cartão em branco (o que seria impresso e entregue ao aluno).
    /// </summary>
    public Mat GenerateBlank(StudentCardInfo info)
        => Generate(info, markedAnswers: null);

    /// <summary>
    /// Gera um cartão de teste com padrões de marcação para validar a leitura OMR.
    /// Questões 1-10: Normalmente preenchidas (alternativas A-D)
    /// Questões 11-20: Com marcações duplas (duplicata)
    /// Questões 21-30: Com rasuras (Scribble)
    /// </summary>
    public Mat GenerateTestCard(StudentCardInfo info)
    {
        var marks = new Dictionary<int, SimulatedMark>();
        
        // 1-10: Normal (A, B, C, D em padrão simples)
        for (int q = 0; q < 10; q++)
        {
            marks[q] = SimulatedMark.Single(q % Math.Min(info.OptionCount, 4));
        }
        
        // 11-20: Dupla (detectar duplicata)
        for (int q = 10; q < 20; q++)
        {
            marks[q] = SimulatedMark.Double(0, 1); // A e B
        }
        
        // 21-30: Rasura/Scribble (detectar rasura)
        for (int q = 20; q < 30; q++)
        {
            marks[q] = SimulatedMark.Single(q % Math.Min(info.OptionCount, 4), MarkStyle.Scribble);
        }
        
        return Generate(info, marks);
    }

    /// <summary>
    /// Gera um cartão com marcações simuladas, para teste do leitor.
    /// A chave do dicionário é o índice da questão (0..99); o valor descreve o que pintar.
    /// </summary>
    public Mat Generate(StudentCardInfo info, IReadOnlyDictionary<int, SimulatedMark>? markedAnswers)
    {
        var canvas = new Mat(_layout.CanvasHeight, _layout.CanvasWidth, MatType.CV_8UC3, Scalar.White);

        DrawArucoMarkers(canvas);
        DrawQr(canvas, BuildQrPayload(info.ExamId, info.StudentId));
        DrawHeader(canvas, info);
        DrawGrid(canvas, info.OptionCount);

        if (markedAnswers is not null)
            DrawMarks(canvas, markedAnswers);

        return canvas;
    }

    private void DrawArucoMarkers(Mat canvas)
    {
        var topLefts = _layout.ArucoTopLefts();
        for (int i = 0; i < _layout.ArucoIds.Length; i++)
        {
            using var marker = new Mat();
            _arucoDict.GenerateImageMarker(_layout.ArucoIds[i], _layout.ArucoMarkerPixels, marker, 1);
            // marker é 1 canal (0/255). Converter p/ 3 canais e copiar na região.
            using var marker3 = new Mat();
            Cv2.CvtColor(marker, marker3, ColorConversionCodes.GRAY2BGR);
            var roi = new Rect(topLefts[i].X, topLefts[i].Y, _layout.ArucoMarkerPixels, _layout.ArucoMarkerPixels);
            using var dst = new Mat(canvas, roi);
            marker3.CopyTo(dst);
        }
    }

    private void DrawQr(Mat canvas, string payload)
    {
        var writer = new QRCodeWriter();
        var hints = new System.Collections.Generic.Dictionary<EncodeHintType, object>
        {
            { EncodeHintType.MARGIN, 1 },
            { EncodeHintType.ERROR_CORRECTION, ZXing.QrCode.Internal.ErrorCorrectionLevel.M }
        };
        var rect = _layout.QrRect();
        BitMatrix matrix = writer.encode(payload, BarcodeFormat.QR_CODE, rect.Width, rect.Height, hints);

        // Renderiza a BitMatrix diretamente no canvas (preto onde bit set).
        for (int y = 0; y < matrix.Height; y++)
        {
            for (int x = 0; x < matrix.Width; x++)
            {
                if (matrix[x, y])
                {
                    canvas.Set(rect.Y + y, rect.X + x, new Vec3b(0, 0, 0));
                }
            }
        }
    }

    private void DrawHeader(Mat canvas, StudentCardInfo info)
    {
        const HersheyFonts font = HersheyFonts.HersheySimplex;
        var black = Scalar.Black;
        // Começa à direita do marcador superior-esquerdo para não sobrepô-lo.
        int x = _layout.ArucoMargin + _layout.ArucoMarkerPixels + 20;

        Cv2.PutText(canvas, "CARTAO-RESPOSTA", new Point(x, 55), font, 0.9, black, 1, LineTypes.AntiAlias);
        Cv2.PutText(canvas, $"Prova: {Trunc(info.ExamName, 28)} (ID {info.ExamId})", new Point(x, 95), font, 0.55, black, 1, LineTypes.AntiAlias);
        Cv2.PutText(canvas, $"Aluno: {Trunc(info.StudentName, 28)} (ID {info.StudentId})", new Point(x, 122), font, 0.55, black, 1, LineTypes.AntiAlias);
        Cv2.PutText(canvas, $"Alternativas: A-{(char)('A' + info.OptionCount - 1)}", new Point(x, 149), font, 0.55, black, 1, LineTypes.AntiAlias);
        Cv2.PutText(canvas, "Preencha completamente a bolha. Nao rasure.", new Point(x, 176), font, 0.45, black, 1, LineTypes.AntiAlias);
    }

    private static string Trunc(string s, int max)
        => string.IsNullOrEmpty(s) ? "-" : (s.Length <= max ? s : s[..max]);

    private void DrawGrid(Mat canvas, int optionCount)
    {
        const HersheyFonts font = HersheyFonts.HersheySimplex;
        var black = Scalar.Black;

        // Cabeçalho de opções (A B C D E) no topo de cada coluna — fora das bolhas,
        // para NÃO poluir a medição de preenchimento na leitura.
        for (int col = 0; col < _layout.Columns; col++)
        {
            int firstQ = col * _layout.QuestionsPerColumn; // 0,25,50,75
            for (int opt = 0; opt < optionCount; opt++)
            {
                var bc = _layout.BubbleCenter(firstQ, opt);
                var letter = _layout.OptionLabels[opt].ToString();
                var ts = Cv2.GetTextSize(letter, font, 0.5, 1, out _);
                var org = new Point((int)bc.X - ts.Width / 2, _layout.GridTop - 8);
                Cv2.PutText(canvas, letter, org, font, 0.5, black, 1, LineTypes.AntiAlias);
            }
        }

        for (int q = 0; q < _layout.TotalQuestions; q++)
        {
            // Número da questão (todas as linhas), traço fino para não parecer "bold".
            var labelOrigin = _layout.QuestionLabelOrigin(q);
            Cv2.PutText(canvas, (q + 1).ToString("D2"), labelOrigin, font, 0.55, black, 1, LineTypes.AntiAlias);

            for (int opt = 0; opt < optionCount; opt++)
            {
                var c = _layout.BubbleCenter(q, opt);
                var center = new Point((int)Math.Round(c.X), (int)Math.Round(c.Y));

                // Bolhas ativas: círculo preto com contorno fino
                Cv2.Circle(canvas, center, _layout.BubbleRadius, black, 1, LineTypes.AntiAlias);
            }
        }
    }

    private void DrawMarks(Mat canvas, IReadOnlyDictionary<int, SimulatedMark> marks)
    {
        foreach (var (q, mark) in marks)
        {
            foreach (var opt in mark.Options)
            {
                var c = _layout.BubbleCenter(q, opt);
                var center = new Point((int)Math.Round(c.X), (int)Math.Round(c.Y));
                switch (mark.Style)
                {
                    case MarkStyle.Filled:
                        Cv2.Circle(canvas, center, _layout.BubbleRadius - 1, Scalar.Black, -1, LineTypes.AntiAlias);
                        break;
                    case MarkStyle.Light: // rasura leve / preenchimento parcial
                        Cv2.Circle(canvas, center, _layout.BubbleRadius - 3, new Scalar(120, 120, 120), -1, LineTypes.AntiAlias);
                        break;
                    case MarkStyle.Scribble: // rasura: risco irregular por cima
                        Cv2.Line(canvas, new Point(center.X - 10, center.Y - 6), new Point(center.X + 10, center.Y + 6), Scalar.Black, 3);
                        Cv2.Line(canvas, new Point(center.X - 8, center.Y + 7), new Point(center.X + 9, center.Y - 5), Scalar.Black, 2);
                        break;
                }
            }
        }
    }
}

public enum MarkStyle { Filled, Light, Scribble }

public sealed class SimulatedMark
{
    public required int[] Options { get; init; }
    public MarkStyle Style { get; init; } = MarkStyle.Filled;

    public static SimulatedMark Single(int option, MarkStyle style = MarkStyle.Filled)
        => new() { Options = new[] { option }, Style = style };

    public static SimulatedMark Double(int a, int b)
        => new() { Options = new[] { a, b }, Style = MarkStyle.Filled };
}
