namespace SistemaAvaliacao.Web.Models.ViewModels;

public class ListarProvasViewModel
{
    public List<ProvaListItem> Provas { get; set; } = new();
    public string? BuscaNome { get; set; }
}

public class ProvaListItem
{
    public Guid Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string? Instituicao { get; set; }
    public DateTime DataInicio { get; set; }
    public DateTime DataFim { get; set; }
    public int NumeroQuestoes { get; set; }
    public int NumeroAlunos { get; set; }
    public DateTime CreatedAt { get; set; }

    // Status calculado a partir da janela (UTC).
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

    public string StatusClass => Status switch
    {
        "Agendada" => "info",
        "Em Andamento" => "success",
        "Encerrada" => "secondary",
        _ => "secondary"
    };
}
