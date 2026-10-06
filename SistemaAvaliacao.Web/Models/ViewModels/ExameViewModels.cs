using System.ComponentModel.DataAnnotations;

namespace SistemaAvaliacao.Web.Models.ViewModels;

// ─────────────────── Identificação ───────────────────

public class IdentificacaoViewModel
{
    public Guid Token { get; set; }
    public string NomeProva { get; set; } = string.Empty;
    public string? Descricao { get; set; }

    // Para telas de status (fora da janela, já iniciada, etc).
    public string? Status { get; set; }
    public DateTime DataInicio { get; set; }
    public DateTime DataFim { get; set; }
    public string? Email { get; set; }

    [Required(ErrorMessage = "E-mail é obrigatório")]
    [EmailAddress(ErrorMessage = "E-mail inválido")]
    [Display(Name = "E-mail")]
    public string? EmailForm { get; set; }

    [Required(ErrorMessage = "CPF é obrigatório")]
    [StringLength(14)]
    [Display(Name = "CPF")]
    public string? CPF { get; set; }
}

// ─────────────────── Responder ───────────────────

public class ResponderProvaViewModel
{
    public Guid Token { get; set; }
    public Guid TokenTentativa { get; set; }
    public Guid ProvaId { get; set; }
    public Guid AlunoId { get; set; }
    public string NomeProva { get; set; } = string.Empty;
    public string? Descricao { get; set; }
    public DateTime DataFim { get; set; }
    public List<QuestaoParaResponderItem> Questoes { get; set; } = new();

    public int TotalQuestoes => Questoes.Count;
    public int TotalRespondidas => Questoes.Count(q => q.RespostaMarcada.HasValue);
}

public class QuestaoParaResponderItem
{
    public Guid QuestaoId { get; set; }
    public int Ordem { get; set; }
    public string? TextoBase { get; set; }
    public string? Enunciado { get; set; }
    public string? ImagemUrl { get; set; }
    public string? DescricaoAcessibilidade { get; set; }
    public string Comando { get; set; } = string.Empty;
    public char? RespostaMarcada { get; set; }
    public List<AlternativaItem> Alternativas { get; set; } = new();
}
