using Microsoft.EntityFrameworkCore;
using SistemaAvaliacao.Web.Data;
using SistemaAvaliacao.Web.Helpers;
using SistemaAvaliacao.Web.Models.Entities;
using SistemaAvaliacao.Web.Models.Enums;
using SistemaAvaliacao.Web.Models.ViewModels;

namespace SistemaAvaliacao.Web.Services;

/// <summary>
/// Lógica de negócio e acesso a dados do domínio de Provas (CRUD e regras).
/// Extraído do antigo AdminController para separar orquestração HTTP de regra de negócio.
/// Os controllers consomem este serviço e apenas traduzem os resultados em respostas HTTP.
/// </summary>
public class ProvaService(ApplicationDbContext context)
{
    // ───────────────────────────── Regras compartilhadas ──────────────────────────────

    /// <summary>Prova travada para edição/exclusão: algum aluno já iniciou.</summary>
    public static bool EstaBloqueada(Prova prova)
        => prova.ProvaAlunos.Any(pa => pa.IniciadaEm != null);

    /// <summary>Verifica a trava consultando diretamente o banco (sem carregar a coleção).</summary>
    public Task<bool> ProvaBloqueadaAsync(Guid provaId)
        => context.ProvaAlunos.AnyAsync(pa => pa.ProvaId == provaId && pa.IniciadaEm != null);

    /// <summary>
    /// Export liberado quando todos os alunos finalizaram OU a janela da prova
    /// terminou — e desde que haja ao menos um aluno finalizado (dados a exportar).
    /// Requer que ProvaAlunos esteja carregado na entidade.
    /// </summary>
    public static bool PodeExportar(Prova prova)
    {
        var total = prova.ProvaAlunos.Count;
        var finalizados = prova.ProvaAlunos.Count(pa => pa.Status == StatusProvaAluno.Finalizada);
        if (finalizados == 0) return false;
        var todosFinalizaram = total > 0 && finalizados == total;
        var passouFim = DateTime.UtcNow > prova.DataFim;
        return todosFinalizaram || passouFim;
    }

    // ───────────────────────────── Consultas ──────────────────────────────

    /// <summary>Lista de provas para a tela de listagem, com filtro opcional por nome.</summary>
    public async Task<List<ProvaListItem>> ListarAsync(string? buscaNome)
    {
        var query = context.Provas
            .Include(p => p.ProvaQuestoes)
            .Include(p => p.ProvaAlunos)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(buscaNome))
        {
            query = query.Where(p =>
                p.Nome.Contains(buscaNome) ||
                (p.Descricao != null && p.Descricao.Contains(buscaNome)) ||
                (p.Instituicao != null && p.Instituicao.Contains(buscaNome)));
        }

        return await query
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new ProvaListItem
            {
                Id = p.Id,
                Nome = p.Nome,
                Instituicao = p.Instituicao,
                DataInicio = p.DataInicio,
                DataFim = p.DataFim,
                NumeroQuestoes = p.ProvaQuestoes.Count,
                NumeroAlunos = p.ProvaAlunos.Count,
                CreatedAt = p.CreatedAt
            })
            .ToListAsync();
    }

    /// <summary>Carrega a prova com todos os relacionamentos necessários à tela de detalhes.</summary>
    public Task<Prova?> ObterDetalhesAsync(Guid id)
        => context.Provas
            .Include(p => p.CriadoPor)
            .Include(p => p.ProvaQuestoes).ThenInclude(pq => pq.Questao)
            .Include(p => p.ProvaAlunos).ThenInclude(pa => pa.Aluno)
            .FirstOrDefaultAsync(p => p.Id == id);

    public enum ExportacaoLiberacao { Liberada, ProvaNaoEncontrada, Bloqueada }

    /// <summary>
    /// Verifica se a exportação de resultados está liberada para a prova:
    /// carrega os alunos e aplica <see cref="PodeExportar"/>. Centraliza o gate
    /// usado pelas três exportações (matriz, CSV detalhado e XLSX).
    /// </summary>
    public async Task<ExportacaoLiberacao> VerificarLiberacaoExportacaoAsync(Guid provaId)
    {
        var prova = await context.Provas
            .Include(p => p.ProvaAlunos)
            .FirstOrDefaultAsync(p => p.Id == provaId);

        if (prova is null) return ExportacaoLiberacao.ProvaNaoEncontrada;
        return PodeExportar(prova) ? ExportacaoLiberacao.Liberada : ExportacaoLiberacao.Bloqueada;
    }

    // ───────────────────────────── Comandos ──────────────────────────────

    /// <summary>
    /// Cria uma prova. Converte as datas do fuso da aplicação para UTC e associa o criador.
    /// Retorna erro de validação quando a data de fim não é posterior à de início.
    /// </summary>
    public async Task<(bool Ok, Prova? Prova, string? Erro)> CriarAsync(CriarProvaViewModel model, Guid criadoPorId)
    {
        if (model.DataFim <= model.DataInicio)
            return (false, null, "Data de fim deve ser posterior à data de início.");

        var prova = new Prova
        {
            Nome = model.Nome,
            Descricao = model.Descricao,
            Instituicao = model.Instituicao,
            NumeroAlternativas = model.NumeroAlternativas,
            TipoAplicacao = model.TipoAplicacao,
            DataInicio = DataHora.ParaUtc(model.DataInicio),
            DataFim = DataHora.ParaUtc(model.DataFim),
            TokenPublico = Guid.NewGuid(),
            CriadoPorId = criadoPorId,
            CreatedAt = DateTime.UtcNow
        };

        context.Provas.Add(prova);
        await context.SaveChangesAsync();
        return (true, prova, null);
    }

    public enum EdicaoResultado { Ok, DataInvalida, NaoEncontrada, Bloqueada }

    /// <summary>
    /// Valida e aplica a edição de uma prova. Não persiste se houver erro de validação,
    /// prova inexistente, ou prova já iniciada por algum aluno.
    /// </summary>
    public async Task<(EdicaoResultado Resultado, Prova? Prova, string? Erro)> EditarAsync(EditarProvaViewModel model)
    {
        if (model.DataFim <= model.DataInicio)
            return (EdicaoResultado.DataInvalida, null, "Data de fim deve ser posterior à data de início.");

        var prova = await context.Provas
            .Include(p => p.ProvaAlunos)
            .FirstOrDefaultAsync(p => p.Id == model.Id);

        if (prova is null)
            return (EdicaoResultado.NaoEncontrada, null, null);

        if (EstaBloqueada(prova))
            return (EdicaoResultado.Bloqueada, prova, "A prova já foi iniciada e não pode mais ser editada.");

        prova.Nome = model.Nome;
        prova.Descricao = model.Descricao;
        prova.Instituicao = model.Instituicao;
        prova.NumeroAlternativas = model.NumeroAlternativas;
        prova.DataInicio = DataHora.ParaUtc(model.DataInicio);
        prova.DataFim = DataHora.ParaUtc(model.DataFim);
        prova.UpdatedAt = DateTime.UtcNow;

        await context.SaveChangesAsync();
        return (EdicaoResultado.Ok, prova, null);
    }

    public enum ExclusaoResultado { Ok, NaoEncontrada, Bloqueada }

    /// <summary>Exclui uma prova, desde que ainda não tenha sido iniciada por nenhum aluno.</summary>
    public async Task<ExclusaoResultado> ExcluirAsync(Guid id)
    {
        var prova = await context.Provas
            .Include(p => p.ProvaAlunos)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (prova is null)
            return ExclusaoResultado.NaoEncontrada;

        if (EstaBloqueada(prova))
            return ExclusaoResultado.Bloqueada;

        context.Provas.Remove(prova);
        await context.SaveChangesAsync();
        return ExclusaoResultado.Ok;
    }
}
