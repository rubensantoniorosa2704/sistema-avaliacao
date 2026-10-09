using Microsoft.EntityFrameworkCore;
using SistemaAvaliacao.Web.Data;
using SistemaAvaliacao.Web.Models.Entities;
using SistemaAvaliacao.Web.Models.ViewModels;

namespace SistemaAvaliacao.Web.Services;

/// <summary>
/// Lógica de negócio e acesso a dados das questões vinculadas a uma prova
/// (CRUD manual e reordenação). A importação em lote via planilha permanece em
/// <see cref="XlsxProvaImportService"/>. Upload de imagem (IFormFile) é tratado
/// no controller, que repassa a URL já resolvida para <see cref="SalvarAsync"/>.
/// </summary>
public class QuestaoProvaService(ApplicationDbContext context)
{
    /// <summary>Monta o ViewModel da tela de questões de uma prova. Null se a prova não existe.</summary>
    public async Task<QuestoesProvaViewModel?> ObterTelaAsync(Guid provaId)
    {
        var prova = await context.Provas
            .Include(p => p.ProvaQuestoes).ThenInclude(pq => pq.Questao)
            .Include(p => p.ProvaAlunos)
            .FirstOrDefaultAsync(p => p.Id == provaId);

        if (prova is null) return null;

        return new QuestoesProvaViewModel
        {
            ProvaId = prova.Id,
            NomeProva = prova.Nome,
            Bloqueada = ProvaService.EstaBloqueada(prova),
            Questoes = prova.ProvaQuestoes
                .OrderBy(pq => pq.Ordem)
                .Select(pq => new QuestaoNaProvaItem
                {
                    QuestaoId = pq.QuestaoId,
                    Ordem = pq.Ordem,
                    Enunciado = pq.Questao.Enunciado,
                    Comando = pq.Questao.Comando,
                    ImagemUrl = pq.Questao.ImagemUrl,
                    AlternativaA = pq.Questao.AlternativaA,
                    AlternativaB = pq.Questao.AlternativaB,
                    AlternativaC = pq.Questao.AlternativaC,
                    AlternativaD = pq.Questao.AlternativaD,
                    AlternativaE = pq.Questao.AlternativaE,
                    NumeroAlternativas = pq.Questao.NumeroAlternativas,
                    AlternativaCorreta = pq.Questao.AlternativaCorreta
                }).ToList()
        };
    }

    /// <summary>Carrega uma questão por id (para edição via modal). Null se não existe.</summary>
    public Task<Questao?> ObterQuestaoAsync(Guid id)
        => context.Questoes.FirstOrDefaultAsync(q => q.Id == id);

    public enum SalvarResultado { Criada, Atualizada, ProvaNaoEncontrada, QuestaoNaoEncontrada }

    /// <summary>
    /// Cria ou atualiza uma questão vinculada à prova. Quando <paramref name="imagemUrl"/>
    /// é informado (upload já processado), atualiza/define a imagem. Na criação, calcula
    /// a próxima ordem no fim da lista.
    /// </summary>
    public async Task<(SalvarResultado Resultado, int Ordem)> SalvarAsync(
        SalvarQuestaoProvaViewModel model, string? imagemUrl)
    {
        var prova = await context.Provas
            .Include(p => p.ProvaQuestoes)
            .FirstOrDefaultAsync(p => p.Id == model.ProvaId);

        if (prova is null) return (SalvarResultado.ProvaNaoEncontrada, 0);

        if (model.QuestaoId.HasValue)
        {
            var questao = await context.Questoes.FindAsync(model.QuestaoId.Value);
            if (questao is null) return (SalvarResultado.QuestaoNaoEncontrada, 0);

            questao.Enunciado = model.Enunciado;
            questao.Comando = model.Comando;
            questao.DescricaoAcessibilidade = model.DescricaoAcessibilidade;
            questao.AlternativaA = model.AlternativaA;
            questao.AlternativaB = model.AlternativaB;
            questao.AlternativaC = model.AlternativaC;
            questao.AlternativaD = model.AlternativaD;
            questao.AlternativaE = model.AlternativaE;
            questao.NumeroAlternativas = model.NumeroAlternativas;
            questao.AlternativaCorreta = model.AlternativaCorreta;
            if (imagemUrl != null) questao.ImagemUrl = imagemUrl;

            prova.UpdatedAt = DateTime.UtcNow;
            await context.SaveChangesAsync();
            return (SalvarResultado.Atualizada, 0);
        }

        var nova = new Questao
        {
            Enunciado = model.Enunciado,
            Comando = model.Comando,
            ImagemUrl = imagemUrl,
            DescricaoAcessibilidade = model.DescricaoAcessibilidade,
            AlternativaA = model.AlternativaA,
            AlternativaB = model.AlternativaB,
            AlternativaC = model.AlternativaC,
            AlternativaD = model.AlternativaD,
            AlternativaE = model.AlternativaE,
            NumeroAlternativas = model.NumeroAlternativas,
            AlternativaCorreta = model.AlternativaCorreta,
            CreatedAt = DateTime.UtcNow
        };

        context.Questoes.Add(nova);
        await context.SaveChangesAsync();

        var proximaOrdem = prova.ProvaQuestoes.Any()
            ? prova.ProvaQuestoes.Max(pq => pq.Ordem) + 1
            : 1;

        context.ProvaQuestoes.Add(new ProvaQuestao
        {
            ProvaId = prova.Id,
            QuestaoId = nova.Id,
            Ordem = proximaOrdem
        });

        prova.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync();
        return (SalvarResultado.Criada, proximaOrdem);
    }

    /// <summary>
    /// Remove a questão da prova, reindexa as posteriores e, se a questão não pertencer
    /// a nenhuma outra prova, exclui a questão em si. False se o vínculo não existe.
    /// </summary>
    public async Task<bool> ExcluirAsync(Guid provaId, Guid questaoId)
    {
        var provaQuestao = await context.ProvaQuestoes
            .FirstOrDefaultAsync(pq => pq.ProvaId == provaId && pq.QuestaoId == questaoId);

        if (provaQuestao is null) return false;

        var ordemRemovida = provaQuestao.Ordem;
        context.ProvaQuestoes.Remove(provaQuestao);

        var questao = await context.Questoes.FindAsync(questaoId);
        if (questao is not null)
        {
            var emOutraProva = await context.ProvaQuestoes
                .AnyAsync(pq => pq.QuestaoId == questaoId && pq.ProvaId != provaId);
            if (!emOutraProva)
                context.Questoes.Remove(questao);
        }

        var posteriores = await context.ProvaQuestoes
            .Where(pq => pq.ProvaId == provaId && pq.Ordem > ordemRemovida)
            .ToListAsync();
        foreach (var pq in posteriores) pq.Ordem--;

        var prova = await context.Provas.FindAsync(provaId);
        if (prova is not null) prova.UpdatedAt = DateTime.UtcNow;

        await context.SaveChangesAsync();
        return true;
    }

    public enum ReordenarResultado { Ok, SemMovimento, NaoEncontrada }

    /// <summary>
    /// Move a questão uma posição para cima ou para baixo, trocando a ordem com a vizinha.
    /// SemMovimento quando já está no extremo (não há vizinha na direção pedida).
    /// </summary>
    public async Task<ReordenarResultado> ReordenarAsync(Guid provaId, Guid questaoId, string direcao)
    {
        var atual = await context.ProvaQuestoes
            .FirstOrDefaultAsync(pq => pq.ProvaId == provaId && pq.QuestaoId == questaoId);

        if (atual is null) return ReordenarResultado.NaoEncontrada;

        var novaOrdem = direcao == "cima" ? atual.Ordem - 1 : atual.Ordem + 1;

        var destino = await context.ProvaQuestoes
            .FirstOrDefaultAsync(pq => pq.ProvaId == provaId && pq.Ordem == novaOrdem);

        if (destino is null) return ReordenarResultado.SemMovimento;

        destino.Ordem = atual.Ordem;
        atual.Ordem = novaOrdem;

        var prova = await context.Provas.FindAsync(provaId);
        if (prova is not null) prova.UpdatedAt = DateTime.UtcNow;

        await context.SaveChangesAsync();
        return ReordenarResultado.Ok;
    }
}
