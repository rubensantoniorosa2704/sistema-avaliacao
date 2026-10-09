using Microsoft.EntityFrameworkCore;
using SistemaAvaliacao.Web.Data;
using SistemaAvaliacao.Web.Models.Entities;
using SistemaAvaliacao.Web.Models.Enums;

namespace SistemaAvaliacao.Web.Services;

/// <summary>
/// Acesso a dados e regras do domínio de folhas/impressão da prova física.
/// A geração dos PDFs de folha-resposta (OMR) permanece em
/// <see cref="FolhaRespostaPdfService"/>; aqui ficam a consulta e as regras da
/// tela de impressão da prova.
/// </summary>
public class FolhaProvaService(ApplicationDbContext context)
{
    public enum ImpressaoResultado { Ok, NaoEncontrada, NaoEhFisica, SemQuestoes }

    /// <summary>
    /// Carrega a prova (com questões ordenadas e alunos) para a tela de impressão,
    /// aplicando as regras: precisa ser do tipo Física e ter ao menos uma questão.
    /// </summary>
    public async Task<(ImpressaoResultado Resultado, Prova? Prova)> ObterImpressaoAsync(Guid provaId)
    {
        var prova = await context.Provas
            .Include(p => p.ProvaQuestoes.OrderBy(pq => pq.Ordem))
                .ThenInclude(pq => pq.Questao)
            .Include(p => p.ProvaAlunos)
            .FirstOrDefaultAsync(p => p.Id == provaId);

        if (prova is null) return (ImpressaoResultado.NaoEncontrada, null);
        if (prova.TipoAplicacao != TipoAplicacao.Fisica) return (ImpressaoResultado.NaoEhFisica, prova);
        if (!prova.ProvaQuestoes.Any()) return (ImpressaoResultado.SemQuestoes, prova);

        return (ImpressaoResultado.Ok, prova);
    }
}
