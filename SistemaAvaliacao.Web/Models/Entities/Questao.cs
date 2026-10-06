using System.ComponentModel.DataAnnotations;

namespace SistemaAvaliacao.Web.Models.Entities;

public class Questao
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Enunciado da questão (texto introdutório antes da imagem).</summary>
    public string? Enunciado { get; set; }

    /// <summary>Texto-base compartilhado entre questões (ex: "Texto-base para as questões 02 a 05").</summary>
    public string? TextoBase { get; set; }

    /// <summary>Caminho relativo da imagem da questão (figura ou tabela renderizada), opcional.</summary>
    [StringLength(500)]
    public string? ImagemUrl { get; set; }

    /// <summary>Comando da questão (a pergunta em si).</summary>
    [Required(ErrorMessage = "Comando é obrigatório")]
    public string Comando { get; set; } = string.Empty;

    /// <summary>Descrição textual da imagem para acessibilidade.</summary>
    public string? DescricaoAcessibilidade { get; set; }

    [Required(ErrorMessage = "Alternativa A é obrigatória")]
    public string AlternativaA { get; set; } = string.Empty;

    [Required(ErrorMessage = "Alternativa B é obrigatória")]
    public string AlternativaB { get; set; } = string.Empty;

    public string? AlternativaC { get; set; }
    public string? AlternativaD { get; set; }
    public string? AlternativaE { get; set; }

    /// <summary>Número de alternativas da questão (2 a 5).</summary>
    [Required]
    [Range(2, 5, ErrorMessage = "Número de alternativas deve ser entre 2 e 5")]
    public int NumeroAlternativas { get; set; } = 4;

    [Required(ErrorMessage = "Alternativa correta é obrigatória")]
    [RegularExpression(@"^[A-E]$", ErrorMessage = "Alternativa correta deve ser A, B, C, D ou E")]
    public char AlternativaCorreta { get; set; }

    /// <summary>Fonte ou referência da questão.</summary>
    [StringLength(500)]
    public string? Fonte { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public ICollection<ProvaQuestao> ProvaQuestoes { get; set; } = new List<ProvaQuestao>();
    public ICollection<Resposta> Respostas { get; set; } = new List<Resposta>();
}
