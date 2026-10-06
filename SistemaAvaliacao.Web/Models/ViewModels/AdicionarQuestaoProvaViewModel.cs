using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace SistemaAvaliacao.Web.Models.ViewModels;

/// <summary>
/// ViewModel da página de gerenciamento de questões da prova.
/// </summary>
public class QuestoesProvaViewModel
{
    public Guid ProvaId { get; set; }
    public string NomeProva { get; set; } = string.Empty;

    /// <summary>Algum aluno já iniciou → edição travada.</summary>
    public bool Bloqueada { get; set; }

    public List<QuestaoNaProvaItem> Questoes { get; set; } = new();
}

/// <summary>
/// Questão já na prova — dados para exibição no card.
/// </summary>
public class QuestaoNaProvaItem
{
    public Guid QuestaoId { get; set; }
    public int Ordem { get; set; }
    public string? Enunciado { get; set; }
    public string? TextoBase { get; set; }
    public string Comando { get; set; } = string.Empty;
    public string? ImagemUrl { get; set; }
    public string? DescricaoAcessibilidade { get; set; }
    public string AlternativaA { get; set; } = string.Empty;
    public string AlternativaB { get; set; } = string.Empty;
    public string? AlternativaC { get; set; }
    public string? AlternativaD { get; set; }
    public string? AlternativaE { get; set; }
    public int NumeroAlternativas { get; set; }
    public char AlternativaCorreta { get; set; }
}

/// <summary>
/// ViewModel da view de impressão da prova física.
/// </summary>
public class ImprimirProvaViewModel
{
    public Guid ProvaId { get; set; }
    public string NomeProva { get; set; } = string.Empty;
    public string? Descricao { get; set; }
    public int NumeroAlunos { get; set; }
    public List<QuestaoNaProvaItem> Questoes { get; set; } = new();
}

/// <summary>
/// ViewModel para criar ou editar uma questão dentro da prova.
/// </summary>
public class SalvarQuestaoProvaViewModel
{
    public Guid ProvaId { get; set; }

    /// <summary>Null = criação. Preenchido = edição.</summary>
    public Guid? QuestaoId { get; set; }

    [Display(Name = "Enunciado")]
    public string? Enunciado { get; set; }

    [Required(ErrorMessage = "O comando da questão é obrigatório")]
    [Display(Name = "Comando")]
    public string Comando { get; set; } = string.Empty;

    /// <summary>Upload de imagem (opcional). Aceita jpg, png, gif, webp até 5 MB.</summary>
    [Display(Name = "Imagem")]
    public IFormFile? Imagem { get; set; }

    [Display(Name = "Descrição de Acessibilidade")]
    public string? DescricaoAcessibilidade { get; set; }

    [Required(ErrorMessage = "Alternativa A é obrigatória")]
    public string AlternativaA { get; set; } = string.Empty;

    [Required(ErrorMessage = "Alternativa B é obrigatória")]
    public string AlternativaB { get; set; } = string.Empty;

    public string? AlternativaC { get; set; }
    public string? AlternativaD { get; set; }
    public string? AlternativaE { get; set; }

    [Required(ErrorMessage = "Informe o número de alternativas")]
    [Range(2, 5)]
    public int NumeroAlternativas { get; set; } = 5;

    [Required(ErrorMessage = "Informe a alternativa correta")]
    [RegularExpression(@"^[A-E]$")]
    public char AlternativaCorreta { get; set; } = 'A';
}
