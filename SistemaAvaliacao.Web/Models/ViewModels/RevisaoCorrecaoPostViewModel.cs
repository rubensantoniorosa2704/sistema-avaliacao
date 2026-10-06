using System.ComponentModel.DataAnnotations;
using SistemaAvaliacao.Web.Models.Enums;

namespace SistemaAvaliacao.Web.Models.ViewModels;

public class RevisaoCorrecaoPostViewModel
{
    [Required]
    public Guid LoteId { get; set; }

    public List<RevisaoLeituraPostItem> Leituras { get; set; } = new();
}

public class RevisaoLeituraPostItem
{
    [Required]
    public Guid Id { get; set; }

    public StatusResposta Status { get; set; }
    public char? AlternativaConfirmada { get; set; }
    public string? Observacao { get; set; }
}
