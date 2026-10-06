using Microsoft.EntityFrameworkCore;
using SistemaAvaliacao.Web.Data;
using SistemaAvaliacao.Web.Models.Entities;
using SistemaAvaliacao.Web.Models.Enums;

namespace SistemaAvaliacao.Web.Services;

/// <summary>
/// Ciclo de vida das tentativas de prova virtual: encerramento manual (o aluno clicou em
/// Finalizar) e consolidação quando a janela da prova termina.
/// </summary>
public class TentativaExameService
{
    private readonly ApplicationDbContext _context;

    public TentativaExameService(ApplicationDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Finaliza a tentativa do aluno. Depois do prazo, o horário registrado é o próprio
    /// <see cref="Prova.DataFim"/>, não o instante da chamada (o cronômetro do navegador
    /// pode disparar segundos após o relógio do servidor). Retorna true se foi após o prazo.
    /// </summary>
    public async Task<bool> FinalizarAsync(ProvaAluno provaAluno, Prova prova, CancellationToken ct = default)
    {
        var agora = DateTime.UtcNow;
        var expirada = agora > prova.DataFim;

        provaAluno.Status = StatusProvaAluno.Finalizada;
        provaAluno.FinalizadaEm = expirada ? prova.DataFim : agora;
        await _context.SaveChangesAsync(ct);

        return expirada;
    }

    /// <summary>
    /// Consolida uma prova cujo prazo já terminou: as tentativas que ficaram EmAndamento
    /// (aluno fechou o navegador, perdeu a conexão) passam a Finalizada, com FinalizadaEm =
    /// DataFim, para que as respostas já salvas entrem nos resultados.
    ///
    /// Chamada ao abrir a tela de resultados. Idempotente e barata: um único UPDATE em
    /// ProvaAlunos (nenhuma resposta é movida ou copiada). Antes do prazo não faz nada.
    /// Retorna quantas tentativas foram consolidadas.
    /// </summary>
    public async Task<int> ConsolidarSeEncerradaAsync(Guid provaId, CancellationToken ct = default)
    {
        var dataFim = await _context.Provas
            .Where(p => p.Id == provaId)
            .Select(p => (DateTime?)p.DataFim)
            .FirstOrDefaultAsync(ct);

        if (dataFim is null || DateTime.UtcNow <= dataFim.Value)
            return 0;

        return await _context.ProvaAlunos
            .Where(pa => pa.ProvaId == provaId && pa.Status == StatusProvaAluno.EmAndamento)
            .ExecuteUpdateAsync(s => s
                .SetProperty(pa => pa.Status, StatusProvaAluno.Finalizada)
                .SetProperty(pa => pa.FinalizadaEm, dataFim.Value), ct);
    }
}
