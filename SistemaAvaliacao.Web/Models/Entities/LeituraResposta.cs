using System.ComponentModel.DataAnnotations;
using SistemaAvaliacao.Web.Models.Enums;

namespace SistemaAvaliacao.Web.Models.Entities;

public class LeituraResposta
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid FolhaRespostaId { get; set; }
    public FolhaResposta FolhaResposta { get; set; } = null!;

    [Required]
    public Guid QuestaoId { get; set; }
    public Questao Questao { get; set; } = null!;

    public StatusResposta Status { get; set; }
    public char? AlternativaDetectada { get; set; }
    public char? AlternativaConfirmada { get; set; }
    public decimal? Confianca { get; set; }
    public bool Revisada { get; set; }
    public string? Observacao { get; set; }
}
