using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SistemaAvaliacao.Web.Data;

namespace SistemaAvaliacao.Web.Services;

// Nota: serviço scoped. O estado compartilhado entre requisições é o IMemoryCache
// (singleton); este serviço apenas o consulta/preenche usando o DbContext scoped.

/// <summary>
/// Conteúdo de UMA questão como é exibido ao aluno. Imutável e idêntico para todos:
/// NÃO contém o gabarito (AlternativaCorreta), que nunca deve chegar ao cliente.
/// </summary>
public sealed record QuestaoExibicao(
    Guid QuestaoId,
    int Ordem,
    string? TextoBase,
    string? Enunciado,
    string? ImagemUrl,
    string? DescricaoAcessibilidade,
    string Comando,
    IReadOnlyList<AlternativaExibicao> Alternativas);

public sealed record AlternativaExibicao(char Letra, string Texto);

/// <summary>
/// Cache em memória do conteúdo das questões de uma prova, por ProvaId.
///
/// As questões são iguais para todos os alunos e, durante a aplicação, imutáveis
/// (a prova trava edição assim que alguém inicia). Então servir cada questão pelo
/// lazy loading não precisa tocar o banco: o conteúdo vem daqui.
///
/// É IMemoryCache (por processo). Com várias instâncias, cada uma mantém sua cópia;
/// como o dado é imutável na janela da prova, isso é seguro. A expiração deslizante
/// é só uma rede de segurança para liberar memória de provas inativas.
/// </summary>
public class QuestoesProvaCache
{
    private readonly IMemoryCache _cache;
    private readonly ApplicationDbContext _context;

    private static readonly TimeSpan Expiracao = TimeSpan.FromMinutes(30);

    public QuestoesProvaCache(IMemoryCache cache, ApplicationDbContext context)
    {
        _cache = cache;
        _context = context;
    }

    private static string Chave(Guid provaId) => $"questoes-prova:{provaId}";

    /// <summary>
    /// Devolve todas as questões da prova (ordenadas), do cache ou do banco na primeira vez.
    /// </summary>
    public async Task<IReadOnlyList<QuestaoExibicao>> ObterAsync(Guid provaId, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(Chave(provaId), out IReadOnlyList<QuestaoExibicao>? cacheado) && cacheado is not null)
            return cacheado;

        var questoes = await CarregarDoBancoAsync(provaId, ct);

        _cache.Set(Chave(provaId), questoes, new MemoryCacheEntryOptions
        {
            SlidingExpiration = Expiracao
        });

        return questoes;
    }

    /// <summary>Devolve uma questão pela ordem (1-based), ou null se não existir.</summary>
    public async Task<QuestaoExibicao?> ObterPorOrdemAsync(Guid provaId, int ordem, CancellationToken ct = default)
    {
        var questoes = await ObterAsync(provaId, ct);
        return questoes.FirstOrDefault(q => q.Ordem == ordem);
    }

    /// <summary>Remove a prova do cache (ex.: ao editar questões antes de iniciar).</summary>
    public void Invalidar(Guid provaId) => _cache.Remove(Chave(provaId));

    private async Task<IReadOnlyList<QuestaoExibicao>> CarregarDoBancoAsync(Guid provaId, CancellationToken ct)
    {
        var itens = await _context.ProvaQuestoes
            .AsNoTracking()
            .Where(pq => pq.ProvaId == provaId)
            .OrderBy(pq => pq.Ordem)
            .Select(pq => new
            {
                pq.Ordem,
                pq.Questao.Id,
                pq.Questao.TextoBase,
                pq.Questao.Enunciado,
                pq.Questao.ImagemUrl,
                pq.Questao.DescricaoAcessibilidade,
                pq.Questao.Comando,
                pq.Questao.NumeroAlternativas,
                pq.Questao.AlternativaA,
                pq.Questao.AlternativaB,
                pq.Questao.AlternativaC,
                pq.Questao.AlternativaD,
                pq.Questao.AlternativaE
            })
            .ToListAsync(ct);

        return itens.Select(q =>
        {
            var alternativas = new List<AlternativaExibicao>
            {
                new('A', q.AlternativaA),
                new('B', q.AlternativaB)
            };
            if (q.NumeroAlternativas >= 3 && !string.IsNullOrEmpty(q.AlternativaC)) alternativas.Add(new('C', q.AlternativaC));
            if (q.NumeroAlternativas >= 4 && !string.IsNullOrEmpty(q.AlternativaD)) alternativas.Add(new('D', q.AlternativaD));
            if (q.NumeroAlternativas >= 5 && !string.IsNullOrEmpty(q.AlternativaE)) alternativas.Add(new('E', q.AlternativaE));

            return new QuestaoExibicao(
                q.Id, q.Ordem, q.TextoBase, q.Enunciado, q.ImagemUrl,
                q.DescricaoAcessibilidade, q.Comando, alternativas);
        }).ToList();
    }
}
