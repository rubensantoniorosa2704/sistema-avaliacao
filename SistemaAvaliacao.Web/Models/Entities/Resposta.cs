using System.ComponentModel.DataAnnotations;
using SistemaAvaliacao.Web.Models.Enums;

namespace SistemaAvaliacao.Web.Models.Entities;

public class Resposta
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    [Required]
    public Guid ProvaId { get; set; }
    
    [Required]
    public Guid AlunoId { get; set; }
    public ProvaAluno ProvaAluno { get; set; } = null!;
    
    [Required]
    public Guid QuestaoId { get; set; }
    public Questao Questao { get; set; } = null!;
    
    [RegularExpression(@"^[A-E]$", ErrorMessage = "Alternativa marcada deve ser A, B, C, D ou E")]
    public char? AlternativaMarcada { get; set; }

    public StatusResposta Status { get; set; } = StatusResposta.Valida;

    public bool Revisada { get; set; }

    [StringLength(500)]
    public string? Observacao { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
