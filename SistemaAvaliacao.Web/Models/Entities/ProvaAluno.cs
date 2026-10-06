using System.ComponentModel.DataAnnotations;
using SistemaAvaliacao.Web.Models.Enums;

namespace SistemaAvaliacao.Web.Models.Entities;

/// <summary>
/// Vínculo entre uma Prova e um Aluno (importado via CSV), com o estado da
/// tentativa. O aluno não faz login: identifica-se por email + CPF no link
/// público e recebe um TokenTentativa ao iniciar.
/// </summary>
public class ProvaAluno
{
    [Required]
    public Guid ProvaId { get; set; }
    public Prova Prova { get; set; } = null!;

    [Required]
    public Guid AlunoId { get; set; }
    public Usuario Aluno { get; set; } = null!;

    [Required]
    public StatusProvaAluno Status { get; set; } = StatusProvaAluno.NaoIniciada;

    public DateTime? IniciadaEm { get; set; }
    public DateTime? FinalizadaEm { get; set; }

    /// <summary>
    /// Gerado quando o aluno clica em "Começar". Amarra o envio das respostas a
    /// esta tentativa e impede refazer (reabrir o link após iniciar é bloqueado).
    /// </summary>
    public Guid? TokenTentativa { get; set; }

    // Navigation properties
    public ICollection<Resposta> Respostas { get; set; } = new List<Resposta>();
}
