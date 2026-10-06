using OpenCvSharp;

namespace SistemaAvaliacao.Web.Services.Omr;

/// <summary>
/// Fonte única de verdade da geometria do cartão-resposta.
///
/// Toda a geometria é definida em uma "folha canônica" de tamanho fixo em pixels
/// (proporção A4 retrato). O gerador desenha nesse canvas; o leitor corrige a
/// perspectiva da foto/scan de volta para exatamente esse mesmo canvas. Assim as
/// coordenadas das bolhas são idênticas na geração e na leitura — é impossível
/// dessincronizar o desenho da leitura.
///
/// Os 4 marcadores ArUco ficam nos cantos da área útil. Eles definem o retângulo
/// de referência usado no warp de perspectiva (resolve scan torto/rotacionado).
/// </summary>
public sealed class SheetLayout
{
    // ---- Dimensões da folha canônica (px). Proporção ~A4 (210x297mm). ----
    public int CanvasWidth { get; } = 1000;
    public int CanvasHeight { get; } = 1414;

    // ---- Estrutura da grade de questões ----
    public int TotalQuestions { get; } = 100;
    public int Columns { get; } = 4;                // 4 colunas
    public int QuestionsPerColumn { get; } = 25;    // 25 por coluna
    public int OptionsPerQuestion { get; } = 5;     // A, B, C, D, E
    public char[] OptionLabels { get; } = { 'A', 'B', 'C', 'D', 'E' };

    // ---- Marcadores ArUco (cantos) ----
    // Dicionário 4x4 com 50 ids é suficiente e robusto para 4 marcadores.
    public int ArucoMarkerPixels { get; } = 40;     // teste de limite
    public int ArucoMargin { get; } = 17;           // distância da borda do canvas
    // Ids fixos: canto sup-esq=0, sup-dir=1, inf-dir=2, inf-esq=3 (sentido horário)
    public int[] ArucoIds { get; } = { 0, 1, 2, 3 };

    // ---- QR code (canto superior direito, conforme especificado pelo usuário) ----
    public int QrSizePx { get; } = 150;

    // ---- Geometria das bolhas ----
    public int BubbleRadius { get; } = 11;          // raio do círculo desenhado (menor p/ caber a folga do numero)
    public int BubbleSampleRadius { get; } = 9;     // raio da região amostrada na leitura (< desenho p/ evitar a borda)

    /// <summary>Folga horizontal à direita das bolhas, para não colar no número da próxima coluna.</summary>
    public int ColumnRightPadding { get; } = 34;

    // Área útil onde ficam as questões (dentro dos marcadores, abaixo do cabeçalho)
    public int GridTop { get; } = 330;              // início vertical da grade (abaixo do cabeçalho + QR)
    public int GridBottom { get; } = 1290;          // fim vertical da grade (acima dos marcadores inferiores)
    public int GridLeft { get; } = 55;              // início horizontal
    public int GridRight { get; } = 945;            // fim horizontal

    // Espaço reservado, dentro de cada coluna, para o rótulo "NN" da questão,
    // antes das bolhas A..E.
    public int QuestionNumberGutter { get; } = 46;

    /// <summary>Centro de cada marcador ArUco na folha canônica, na ordem dos ids 0..3.</summary>
    public Point2f[] ArucoCenters()
    {
        int half = ArucoMarkerPixels / 2;
        int m = ArucoMargin + half;
        return new[]
        {
            new Point2f(m, m),                               // id 0: superior-esquerdo
            new Point2f(CanvasWidth - m, m),                 // id 1: superior-direito
            new Point2f(CanvasWidth - m, CanvasHeight - m),  // id 2: inferior-direito
            new Point2f(m, CanvasHeight - m),                // id 3: inferior-esquerdo
        };
    }

    /// <summary>Canto superior-esquerdo do marcador (para desenho), na ordem dos ids 0..3.</summary>
    public Point[] ArucoTopLefts()
    {
        int m = ArucoMargin;
        int s = ArucoMarkerPixels;
        return new[]
        {
            new Point(m, m),
            new Point(CanvasWidth - m - s, m),
            new Point(CanvasWidth - m - s, CanvasHeight - m - s),
            new Point(m, CanvasHeight - m - s),
        };
    }

    /// <summary>Retângulo do QR (canto superior direito, abaixo do marcador id 1).</summary>
    public Rect QrRect()
    {
        int x = CanvasWidth - ArucoMargin - QrSizePx;
        int y = ArucoMargin + ArucoMarkerPixels + 20;
        return new Rect(x, y, QrSizePx, QrSizePx);
    }

    /// <summary>Largura de cada coluna de questões.</summary>
    public double ColumnWidth => (double)(GridRight - GridLeft) / Columns;

    /// <summary>Espaçamento vertical entre linhas de questão.</summary>
    public double RowPitch => (double)(GridBottom - GridTop) / QuestionsPerColumn;

    /// <summary>
    /// Calcula o centro (px, folha canônica) de uma bolha específica.
    /// </summary>
    /// <param name="questionIndex">0..99</param>
    /// <param name="optionIndex">0..4 (A..E)</param>
    public Point2f BubbleCenter(int questionIndex, int optionIndex)
    {
        if (questionIndex < 0 || questionIndex >= TotalQuestions)
            throw new ArgumentOutOfRangeException(nameof(questionIndex));
        if (optionIndex < 0 || optionIndex >= OptionsPerQuestion)
            throw new ArgumentOutOfRangeException(nameof(optionIndex));

        // Preenchimento por coluna: questões 1..25 na coluna 0, 26..50 na coluna 1, etc.
        int col = questionIndex / QuestionsPerColumn;
        int rowInCol = questionIndex % QuestionsPerColumn;

        double colX = GridLeft + col * ColumnWidth;
        double firstBubbleX = colX + QuestionNumberGutter + BubbleRadius + 4;

        // Espaço horizontal disponível para as 5 bolhas dentro da coluna,
        // reservando ColumnRightPadding à direita para o número da próxima coluna.
        double bubblesSpan = ColumnWidth - QuestionNumberGutter - (BubbleRadius + 4) - ColumnRightPadding;
        double optionPitch = bubblesSpan / (OptionsPerQuestion - 1);

        double x = firstBubbleX + optionIndex * optionPitch;
        double y = GridTop + rowInCol * RowPitch + RowPitch / 2.0;

        return new Point2f((float)x, (float)y);
    }

    /// <summary>Posição (px) do rótulo do número da questão (à esquerda das bolhas).</summary>
    public Point QuestionLabelOrigin(int questionIndex)
    {
        int col = questionIndex / QuestionsPerColumn;
        int rowInCol = questionIndex % QuestionsPerColumn;
        double colX = GridLeft + col * ColumnWidth;
        double y = GridTop + rowInCol * RowPitch + RowPitch / 2.0 + 6;
        return new Point((int)colX, (int)y);
    }
}
