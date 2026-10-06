namespace SistemaAvaliacao.Web.Services.Omr;

/// <summary>
/// Limiares da classificação. Todos relativos ao preenchimento (0..1) das bolhas.
/// Ajuste fino conforme o comportamento do scanner real.
/// </summary>
public sealed class ClassifierThresholds
{
    /// <summary>Abaixo disso, considera-se que a bolha não foi tocada.</summary>
    public double EmptyCeiling { get; init; } = 0.30;

    /// <summary>Acima disso, a bolha é considerada uma marcação forte/clara.</summary>
    public double StrongFill { get; init; } = 0.51;

    /// <summary>
    /// Se a segunda bolha mais preenchida atingir esta fração da primeira,
    /// há ambiguidade (candidata a marcação dupla).
    /// </summary>
    public double SecondRelativeToTop { get; init; } = 0.6;
}

/// <summary>
/// Classifica uma questão a partir do preenchimento medido de cada bolha,
/// usando decisão RELATIVA entre as alternativas da própria questão
/// (robusto a variações globais de iluminação/scanner).
/// </summary>
public sealed class AnswerClassifier
{
    private readonly SheetLayout _layout;
    private readonly ClassifierThresholds _t;

    public AnswerClassifier(SheetLayout layout, ClassifierThresholds? thresholds = null)
    {
        _layout = layout;
        _t = thresholds ?? new ClassifierThresholds();
    }

    public QuestionResult Classify(int questionNumber, double[] fillRatios)
    {
        // Considera apenas alternativas existentes (fill != NaN).
        var valid = new List<(int idx, double fill)>();
        for (int i = 0; i < fillRatios.Length; i++)
        {
            if (!double.IsNaN(fillRatios[i]))
                valid.Add((i, fillRatios[i]));
        }

        if (valid.Count == 0)
        {
            return new QuestionResult
            {
                Number = questionNumber,
                Status = QuestionStatus.EmBranco,
                Confidence = 1.0,
                FillRatios = fillRatios,
                DetectedOption = null
            };
        }

        // Ordena decrescente por preenchimento.
        valid.Sort((a, b) => b.fill.CompareTo(a.fill));
        var top = valid[0];
        var second = valid.Count > 1 ? valid[1] : (idx: -1, fill: 0.0);

        // 1) Em branco: nada passa do teto de "vazio".
        if (top.fill < _t.EmptyCeiling)
        {
            return Build(questionNumber, QuestionStatus.EmBranco, null,
                confidence: Clamp01(1.0 - top.fill / _t.EmptyCeiling), fillRatios);
        }

        double secondRatio = top.fill > 1e-6 ? second.fill / top.fill : 0.0;

        // 2) Dupla: a segunda também é forte (ou próxima da primeira).
        bool secondIsStrong = second.fill >= _t.EmptyCeiling && secondRatio >= _t.SecondRelativeToTop;
        if (secondIsStrong)
        {
            // Quanto mais próximas, mais confiante de que é dupla.
            double conf = Clamp01(secondRatio);
            return Build(questionNumber, QuestionStatus.Dupla, null, conf, fillRatios);
        }

        // 3) Válida: primeira é forte e destacada da segunda.
        if (top.fill >= _t.StrongFill && secondRatio < _t.SecondRelativeToTop)
        {
            // Confiança combina força do preenchimento e separação da 2a.
            double separation = 1.0 - secondRatio;              // 0..1
            double strength = Clamp01((top.fill - _t.EmptyCeiling) / (1.0 - _t.EmptyCeiling));
            double conf = Clamp01(0.5 * strength + 0.5 * separation);
            return Build(questionNumber, QuestionStatus.Valida, _layout.OptionLabels[top.idx], conf, fillRatios);
        }

        // 4) Resto: marcação intermediária/parcial ou ambígua sem ser dupla clara.
        //    Zona cinzenta -> rasura/duvidosa (revisão humana).
        double ambiguity = 1.0 - Math.Abs(top.fill - _t.StrongFill) / Math.Max(_t.StrongFill, 1e-6);
        return Build(questionNumber, QuestionStatus.RasuraDuvidosa,
            null, Clamp01(ambiguity), fillRatios);
    }

    private QuestionResult Build(int number, QuestionStatus status, char? option, double confidence, double[] fills)
        => new()
        {
            Number = number,
            Status = status,
            DetectedOption = option,
            Confidence = Math.Round(confidence, 3),
            FillRatios = fills
        };

    private static double Clamp01(double v) => v < 0 ? 0 : (v > 1 ? 1 : v);
}
