using System.ComponentModel.DataAnnotations;
using SistemaAvaliacao.Web.Models.Enums;

namespace SistemaAvaliacao.Web.Models.ViewModels;

public class CorrecaoProvaItem
{
    public Guid Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public int NumeroQuestoes { get; set; }
    public int NumeroAlunos { get; set; }
    public int LotesRecebidos { get; set; }
    public Guid? UltimoLoteId { get; set; }
    public StatusProcessamentoCorrecao? StatusUltimoLote { get; set; }
}

public class EnviarCorrecaoViewModel
{
    public Guid ProvaId { get; set; }
    public string NomeProva { get; set; } = string.Empty;

    [Required(ErrorMessage = "Selecione um arquivo PDF")]
    public IFormFile? ArquivoPdf { get; set; }
}
