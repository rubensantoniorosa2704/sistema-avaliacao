using System.ComponentModel.DataAnnotations;
using SistemaAvaliacao.Web.Models.Enums;

namespace SistemaAvaliacao.Web.Models.Entities;

/// <summary>
/// Prova que pode ser aplicada de duas formas:
/// - Física: com folhas de resposta digitalizadas e processadas via OMR
/// - Virtual: respondida online no portal
/// </summary>
public class Prova
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required(ErrorMessage = "Nome da prova é obrigatório")]
    [StringLength(300)]
    public string Nome { get; set; } = string.Empty;

    [StringLength(1000)]
    public string? Descricao { get; set; }

    /// <summary>Quantidade fixa de alternativas usada na folha de respostas.</summary>
    [Range(4, 5)]
    public int NumeroAlternativas { get; set; } = 4;

    /// <summary>Tipo de aplicação: Física (folhas OMR) ou Virtual (online)</summary>
    [Required]
    public TipoAplicacao TipoAplicacao { get; set; } = TipoAplicacao.Virtual;

    [Required(ErrorMessage = "Data de início é obrigatória")]
    public DateTime DataInicio { get; set; }

    [Required(ErrorMessage = "Data de fim é obrigatória")]
    public DateTime DataFim { get; set; }

    /// <summary>
    /// Token do link público da prova (o mesmo para todos os alunos).
    /// A URL é /p/{TokenPublico}.
    /// Usado apenas para provas Virtual.
    /// </summary>
    public Guid TokenPublico { get; set; } = Guid.NewGuid();

    /// <summary>Admin que criou a prova.</summary>
    [Required]
    public Guid CriadoPorId { get; set; }
    public Usuario CriadoPor { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Navigation properties
    public ICollection<ProvaQuestao> ProvaQuestoes { get; set; } = new List<ProvaQuestao>();
    public ICollection<ProvaAluno> ProvaAlunos { get; set; } = new List<ProvaAluno>();
}
