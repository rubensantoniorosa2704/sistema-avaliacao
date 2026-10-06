using SistemaAvaliacao.Web.Models.Enums;

namespace SistemaAvaliacao.Web.Models.ViewModels;

// ───────────────────────── Minhas Provas ─────────────────────────

public class MinhasProvasViewModel
{
    public List<ProvaAlunoItem> Provas { get; set; } = new();
}

public class ProvaAlunoItem
{
    public Guid ProvaId { get; set; }
    public string NomeProva { get; set; } = string.Empty;
    public string NomeIES { get; set; } = string.Empty;
    public string? SiglaIES { get; set; }
    public int DuracaoMinutos { get; set; }
    public DateTime DataInicio { get; set; }
    public DateTime DataFim { get; set; }
    public int NumeroQuestoes { get; set; }
    public bool ProvaAtiva { get; set; }
    public StatusProvaAluno Status { get; set; }
    public decimal? NotaFinal { get; set; }

    // DataInicio/DataFim são armazenados e comparados em UTC.
    private bool Agendada => DateTime.UtcNow < DataInicio;
    private bool Encerrada => DateTime.UtcNow > DataFim;

    public bool PodeIniciar => ProvaAtiva && Status == StatusProvaAluno.NaoIniciada && !Agendada && !Encerrada;
    public bool PodeContinuar => ProvaAtiva && Status == StatusProvaAluno.EmAndamento && !Encerrada;
    public bool PodeVerResultado => Status == StatusProvaAluno.Finalizada;

    public string SituacaoLabel
    {
        get
        {
            if (Status == StatusProvaAluno.Finalizada) return "Finalizada";
            if (!ProvaAtiva) return "Indisponível";
            if (Agendada) return "Agendada";
            if (Encerrada) return "Encerrada";
            if (Status == StatusProvaAluno.EmAndamento) return "Em andamento";
            return "Disponível";
        }
    }

    public string SituacaoCss => SituacaoLabel switch
    {
        "Finalizada" => "success",
        "Em andamento" => "warning",
        "Disponível" => "primary",
        "Agendada" => "info",
        "Encerrada" => "secondary",
        _ => "secondary"
    };
}

// ───────────────────────── Fazer Prova ─────────────────────────

public class FazerProvaViewModel
{
    public Guid ProvaId { get; set; }
    public string NomeProva { get; set; } = string.Empty;
    public string? Descricao { get; set; }
    public string NomeIES { get; set; } = string.Empty;
    public string? SiglaIES { get; set; }

    /// <summary>
    /// Segundos restantes calculados no servidor (menor entre prazo e duração).
    /// O cronômetro do front é apenas visual; o servidor é a fonte da verdade.
    /// </summary>
    public int SegundosRestantes { get; set; }

    public List<QuestaoProvaItem> Questoes { get; set; } = new();

    public int TotalQuestoes => Questoes.Count;
    public int TotalRespondidas => Questoes.Count(q => q.RespostaMarcada.HasValue);
}

public class QuestaoProvaItem
{
    public Guid QuestaoId { get; set; }
    public int Ordem { get; set; }
    public string? Enunciado { get; set; }
    public string? ImagemUrl { get; set; }
    public string? DescricaoAcessibilidade { get; set; }
    public string Comando { get; set; } = string.Empty;
    public List<AlternativaItem> Alternativas { get; set; } = new();

    /// <summary>Alternativa já marcada pelo aluno (se houver). Nunca inclui o gabarito.</summary>
    public char? RespostaMarcada { get; set; }
}

public class AlternativaItem
{
    public char Letra { get; set; }
    public string Texto { get; set; } = string.Empty;
}

// ───────────────────────── Resultado ─────────────────────────

public class ResultadoProvaViewModel
{
    public Guid ProvaId { get; set; }
    public string NomeProva { get; set; } = string.Empty;
    public string NomeIES { get; set; } = string.Empty;
    public string? SiglaIES { get; set; }
    public decimal NotaFinal { get; set; }
    public int TotalQuestoes { get; set; }
    public int TotalAcertos { get; set; }
    public int TotalRespondidas { get; set; }
    public DateTime? IniciadaEm { get; set; }
    public DateTime? FinalizadaEm { get; set; }

    public int TotalErros => Math.Max(0, TotalRespondidas - TotalAcertos);
    public int TotalEmBranco => Math.Max(0, TotalQuestoes - TotalRespondidas);

    public TimeSpan? TempoGasto =>
        (IniciadaEm.HasValue && FinalizadaEm.HasValue) ? FinalizadaEm - IniciadaEm : null;

    public string NotaCss => NotaFinal >= 60 ? "success" : NotaFinal >= 40 ? "warning" : "danger";
}
