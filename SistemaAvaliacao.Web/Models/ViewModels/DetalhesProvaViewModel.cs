namespace SistemaAvaliacao.Web.Models.ViewModels;

using SistemaAvaliacao.Web.Models.Enums;

public class DetalhesProvaViewModel
{
    public Guid Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string? Descricao { get; set; }
    public string? Instituicao { get; set; }
    public DateTime DataInicio { get; set; }
    public DateTime DataFim { get; set; }
    public DateTime CreatedAt { get; set; }
    public TipoAplicacao TipoAplicacao { get; set; } = TipoAplicacao.Virtual;

    public string NomeCriador { get; set; } = string.Empty;

    /// <summary>Link público (o mesmo para todos os alunos).</summary>
    public string LinkPublico { get; set; } = string.Empty;

    /// <summary>Algum aluno já iniciou → edição travada.</summary>
    public bool Bloqueada { get; set; }

    /// <summary>Export de matriz liberado (todos finalizaram OU janela encerrada).</summary>
    public bool PodeExportar { get; set; }
    public int NumeroAlternativas { get; set; } = 4;

    public List<QuestaoResumoItem> Questoes { get; set; } = new();
    public List<AlunoProvaItem> Alunos { get; set; } = new();

    public int NumeroQuestoes => Questoes.Count;
    public int NumeroAlunos => Alunos.Count;    public int AlunosNaoIniciados => Alunos.Count(a => a.Status == "NaoIniciada");
    public int AlunosEmAndamento => Alunos.Count(a => a.Status == "EmAndamento");
    public int AlunosFinalizados => Alunos.Count(a => a.Status == "Finalizada");

    public string Status
    {
        get
        {
            var agora = DateTime.UtcNow;
            if (agora < DataInicio) return "Agendada";
            if (agora > DataFim) return "Encerrada";
            return "Em Andamento";
        }
    }

    public string StatusCss => Status switch
    {
        "Agendada" => "info",
        "Em Andamento" => "success",
        "Encerrada" => "secondary",
        _ => "secondary"
    };
}

public class QuestaoResumoItem
{
    public Guid QuestaoId { get; set; }
    public int Ordem { get; set; }
    public string? Enunciado { get; set; }
    public string Comando { get; set; } = string.Empty;
    public int NumeroAlternativas { get; set; }
    public char AlternativaCorreta { get; set; }
    public bool TemImagem { get; set; }
}

public class AlunoProvaItem
{
    public Guid AlunoId { get; set; }
    public string NomeAluno { get; set; } = string.Empty;
    public string EmailAluno { get; set; } = string.Empty;
    public string CpfAluno { get; set; } = string.Empty;
    public string? MatriculaAluno { get; set; }
    public string Status { get; set; } = "NaoIniciada";
    public DateTime? DataInicio { get; set; }
    public DateTime? DataFim { get; set; }
    public bool JaIniciou { get; set; }
}
