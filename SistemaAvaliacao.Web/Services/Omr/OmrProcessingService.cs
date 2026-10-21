using OpenCvSharp;

namespace SistemaAvaliacao.Web.Services.Omr;

/// <summary>
/// Resultado do processamento de uma página OMR.
/// </summary>
public sealed class PageProcessingResult
{
    public bool Aligned { get; set; }
    public int? ExamId { get; set; }
    /// <summary>Token de identificação da folha lido do QR (chave autoritativa). Ver OMR hardening.</summary>
    public string? Token { get; set; }
    public string? QrPayload { get; set; }
    public IReadOnlyList<QuestionResult> Questions { get; set; } = Array.Empty<QuestionResult>();
    public string? Warnings { get; set; }
    public string? DebugImagePath { get; set; }
}

/// <summary>
/// Orquestra o processamento de PDFs com OMR: renderiza cada página,
/// executa a leitura OMR, e gera imagens de debug.
/// Agnóstico a persistência - apenas retorna os resultados.
/// </summary>
public sealed class OmrProcessingService
{
    private readonly SheetLayout _layout;
    private readonly OmrReader _reader;
    private readonly int _optionCount;
    private readonly int _dpi;

    public OmrProcessingService(SheetLayout layout, OmrReader reader, int optionCount, int dpi = 200)
    {
        _layout = layout;
        _reader = reader;
        _optionCount = optionCount;
        _dpi = dpi;
    }

    /// <summary>
    /// Processa todas as páginas de um PDF, gerando resultados e imagens de debug.
    /// </summary>
    public IEnumerable<(int pageNumber, PageProcessingResult result)> ProcessPdf(string pdfPath, string debugDir)
    {
        Directory.CreateDirectory(debugDir);
        int pageNumber = 0;

        foreach (var pageMat in PdfPageLoader.LoadPages(pdfPath, _dpi))
        {
            pageNumber++;
            using (pageMat)
            {
                var result = ProcessSinglePage(pageMat, pageNumber, debugDir);
                yield return (pageNumber, result);
            }
        }
    }

    /// <summary>
    /// Processa uma única página já em memória e devolve o resultado.
    /// </summary>
    public PageProcessingResult ProcessSinglePage(Mat pageMat, int pageNumber, string debugDir)
    {
        var readResult = _reader.Read(pageMat, _optionCount, out var warped);

        // Imagem de debug: se alinhou, anota sobre a folha normalizada; senão salva a página crua.
        string debugPath = Path.Combine(debugDir, $"page{pageNumber:D3}_debug.png");
        if (readResult.Aligned && warped is not null)
        {
            using var annotated = DebugRenderer.Annotate(warped, _layout, readResult, _optionCount);
            Cv2.ImWrite(debugPath, annotated);
        }
        else
        {
            Cv2.ImWrite(debugPath, pageMat);
        }

        warped?.Dispose();

        return new PageProcessingResult
        {
            Aligned = readResult.Aligned,
            ExamId = readResult.ExamId,
            Token = readResult.Token,
            QrPayload = readResult.QrPayload,
            Questions = readResult.Questions,
            Warnings = readResult.Warnings.Count > 0 ? string.Join(" | ", readResult.Warnings) : null,
            DebugImagePath = debugPath
        };
    }
}
