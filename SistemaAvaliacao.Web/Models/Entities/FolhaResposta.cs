using System.ComponentModel.DataAnnotations;
using SistemaAvaliacao.Web.Models.Enums;

namespace SistemaAvaliacao.Web.Models.Entities;

public class FolhaResposta
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid LoteCorrecaoId { get; set; }
    public LoteCorrecao LoteCorrecao { get; set; } = null!;

    [Required]
    public Guid ProvaId { get; set; }
    public Prova Prova { get; set; } = null!;

    [Required]
    public Guid AlunoId { get; set; }
    public Usuario Aluno { get; set; } = null!;

    [Required, StringLength(120)]
    public string TokenIdentificacao { get; set; } = string.Empty;

    public int NumeroPagina { get; set; }
    public bool Processada { get; set; }
    public DateTime? ProcessadaEm { get; set; }

    public ICollection<LeituraResposta> Leituras { get; set; } = new List<LeituraResposta>();
}
