using SistemaAvaliacao.Web.Models.Enums;

namespace SistemaAvaliacao.Web.Models.ViewModels;

public class RevisaoCorrecaoViewModel
{
    public Guid LoteId { get; set; }
    public string NomeProva { get; set; } = string.Empty;
    public string NomeArquivo { get; set; } = string.Empty;
    public string UrlPdf { get; set; } = string.Empty;
    public List<RevisaoFolhaItem> Folhas { get; set; } = new();
}

public class RevisaoFolhaItem
{
    public Guid FolhaId { get; set; }
    public string NomeAluno { get; set; } = string.Empty;
    public int NumeroPagina { get; set; }
    public List<RevisaoLeituraItem> Leituras { get; set; } = new();
}

public class RevisaoLeituraItem
{
    public Guid Id { get; set; }
    public int Ordem { get; set; }
    public int NumeroAlternativas { get; set; }
    public StatusResposta Status { get; set; }
    public char? AlternativaDetectada { get; set; }
    public char? AlternativaConfirmada { get; set; }
    public decimal? Confianca { get; set; }
    public string? Observacao { get; set; }
}
