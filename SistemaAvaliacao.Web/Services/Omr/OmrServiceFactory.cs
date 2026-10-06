namespace SistemaAvaliacao.Web.Services.Omr;

/// <summary>
/// Factory para criar instâncias da OmrProcessingService com configuração padrão.
/// Facilita a integração com o resto da aplicação.
/// </summary>
public sealed class OmrServiceFactory
{
    /// <summary>
    /// Cria uma OmrProcessingService totalmente configurada com layout e classificador padrão.
    /// </summary>
    public static OmrProcessingService CreateDefault(int optionCount = 5, int dpi = 200)
    {
        var layout = new SheetLayout();
        var classifier = new AnswerClassifier(layout);
        var reader = new OmrReader(layout, classifier);
        return new OmrProcessingService(layout, reader, optionCount, dpi);
    }

    /// <summary>
    /// Cria uma OmrProcessingService com layout customizado.
    /// </summary>
    public static OmrProcessingService Create(SheetLayout layout, AnswerClassifier classifier, int optionCount = 5, int dpi = 200)
    {
        var reader = new OmrReader(layout, classifier);
        return new OmrProcessingService(layout, reader, optionCount, dpi);
    }
}
