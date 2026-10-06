using System.ComponentModel.DataAnnotations;

namespace SistemaAvaliacao.Web.Models.Entities;

/// <summary>
/// Tabela de junção entre Prova e Questao com ordem de exibição
/// </summary>
public class ProvaQuestao
{
    [Required]
    public Guid ProvaId { get; set; }
    public Prova Prova { get; set; } = null!;
    
    [Required]
    public Guid QuestaoId { get; set; }
    public Questao Questao { get; set; } = null!;
    
    /// <summary>
    /// Ordem de exibição da questão na prova (1-based)
    /// </summary>
    [Required]
    public int Ordem { get; set; }
}
