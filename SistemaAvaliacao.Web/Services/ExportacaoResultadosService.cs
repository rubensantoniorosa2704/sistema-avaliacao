using System.Text;
using Microsoft.EntityFrameworkCore;
using SistemaAvaliacao.Web.Data;
using SistemaAvaliacao.Web.Models.Enums;

namespace SistemaAvaliacao.Web.Services;

/// <summary>
/// Exporta a matriz de respostas (0/1) de uma prova concluída.
///
/// O formato reproduz exatamente a planilha consumida pela análise psicométrica
/// externa: uma coluna "ALUNO" seguida de uma coluna por questão (Q.01, Q.02, ...),
/// com 1 para acerto e 0 para erro ou questão não respondida. Nenhuma linha extra
/// (título, totais, comentários) é incluída, para que o arquivo possa ser lido
/// diretamente como matriz de entrada.
/// </summary>
public class ExportacaoResultadosService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<ExportacaoResultadosService> _logger;

    public ExportacaoResultadosService(ApplicationDbContext context, ILogger<ExportacaoResultadosService> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>Resultado da geração da matriz.</summary>
    public sealed record MatrizExportada(
        string NomeArquivo,
        byte[] Conteudo,
        int TotalAlunos,
        int TotalQuestoes,
        int AlunosNaoFinalizados);

    /// <summary>
    /// Gera o CSV da matriz de respostas. Retorna null se a prova não existir.
    /// Somente alunos com a prova <see cref="StatusProvaAluno.Finalizada"/> entram
    /// na matriz — incluir provas em andamento produziria linhas incompletas que
    /// distorceriam a análise.
    /// </summary>
    public async Task<MatrizExportada?> GerarMatrizCsvAsync(Guid provaId)
    {
        var prova = await _context.Provas
            .Include(p => p.ProvaQuestoes).ThenInclude(pq => pq.Questao)
            .Include(p => p.ProvaAlunos).ThenInclude(pa => pa.Aluno)
            .FirstOrDefaultAsync(p => p.Id == provaId);

        if (prova == null) return null;

        var questoes = prova.ProvaQuestoes
            .OrderBy(pq => pq.Ordem)
            .ToList();

        var alunosFinalizados = prova.ProvaAlunos
            .Where(pa => pa.Status == StatusProvaAluno.Finalizada)
            .OrderBy(pa => pa.Aluno.Nome, StringComparer.CurrentCulture)
            .ToList();

        var naoFinalizados = prova.ProvaAlunos.Count - alunosFinalizados.Count;

        // Respostas da prova indexadas por (aluno, questão). O índice único
        // (ProvaId, AlunoId, QuestaoId) garante no máximo uma resposta por par.
        var respostas = await _context.Respostas
            .Where(r => r.ProvaId == provaId)
            .Select(r => new { r.AlunoId, r.QuestaoId, r.AlternativaMarcada })
            .ToListAsync();

        var marcadas = respostas.ToDictionary(
            r => (r.AlunoId, r.QuestaoId),
            r => r.AlternativaMarcada);

        var sb = new StringBuilder();

        // Cabeçalho: ALUNO;Q.01;Q.02;...
        sb.Append("ALUNO");
        for (int i = 0; i < questoes.Count; i++)
            sb.Append(';').Append($"Q.{i + 1:00}");
        sb.Append('\n');

        // Uma linha por aluno finalizado.
        foreach (var pa in alunosFinalizados)
        {
            sb.Append(EscaparCsv(pa.Aluno.Nome));
            foreach (var pq in questoes)
            {
                var acertou = marcadas.TryGetValue((pa.AlunoId, pq.QuestaoId), out var marcada)
                              && marcada == pq.Questao.AlternativaCorreta;
                sb.Append(';').Append(acertou ? '1' : '0');
            }
            sb.Append('\n');
        }

        // BOM UTF-8 para o Excel reconhecer acentuação corretamente.
        var bytes = Encoding.UTF8.GetPreamble()
            .Concat(Encoding.UTF8.GetBytes(sb.ToString()))
            .ToArray();

        var nomeArquivo = $"matriz-respostas-{Slug(prova.Nome)}-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv";

        _logger.LogInformation(
            "Matriz exportada: prova {ProvaId}, {Alunos} aluno(s), {Questoes} questão(ões).",
            provaId, alunosFinalizados.Count, questoes.Count);

        return new MatrizExportada(nomeArquivo, bytes, alunosFinalizados.Count, questoes.Count, naoFinalizados);
    }

    /// <summary>
    /// Gera o arquivo operacional para devolutiva da instituiÃ§Ã£o. Diferente da
    /// matriz psicomÃ©trica, ele preserva os identificadores do aluno e explicita
    /// o resultado de cada questÃ£o como ACERTO, ERRO ou SEM_RESPOSTA.
    /// </summary>
    public async Task<MatrizExportada?> GerarResultadosDetalhadosCsvAsync(Guid provaId)
    {
        var prova = await _context.Provas
            .Include(p => p.ProvaQuestoes).ThenInclude(pq => pq.Questao)
            .Include(p => p.ProvaAlunos).ThenInclude(pa => pa.Aluno)
            .FirstOrDefaultAsync(p => p.Id == provaId);

        if (prova == null) return null;

        var questoes = prova.ProvaQuestoes.OrderBy(pq => pq.Ordem).ToList();
        var alunos = prova.ProvaAlunos
            .Where(pa => pa.Status == StatusProvaAluno.Finalizada)
            .OrderBy(pa => pa.Aluno.Nome, StringComparer.CurrentCulture)
            .ToList();

        if (questoes.Count == 0 || alunos.Count == 0) return null;

        var respostas = await _context.Respostas
            .Where(r => r.ProvaId == provaId)
            .Select(r => new { r.AlunoId, r.QuestaoId, r.AlternativaMarcada })
            .ToListAsync();
        var porAlunoQuestao = respostas.ToDictionary(r => (r.AlunoId, r.QuestaoId), r => r.AlternativaMarcada);

        var sb = new StringBuilder("MATRICULA;ALUNO;CPF");
        foreach (var questao in questoes)
            sb.Append($";Q.{questao.Ordem:00}");
        sb.Append(";ACERTOS;ERROS;SEM_RESPOSTA;PERCENTUAL\n");

        foreach (var pa in alunos)
        {
            sb.Append(EscaparCsv(pa.Aluno.Matricula ?? string.Empty)).Append(';')
                .Append(EscaparCsv(pa.Aluno.Nome)).Append(';')
                .Append(EscaparCsv(pa.Aluno.CPF));

            var acertos = 0;
            var erros = 0;
            var semResposta = 0;
            foreach (var pq in questoes)
            {
                if (!porAlunoQuestao.TryGetValue((pa.AlunoId, pq.QuestaoId), out var marcada) || !marcada.HasValue)
                {
                    sb.Append(";SEM_RESPOSTA");
                    semResposta++;
                }
                else if (marcada == pq.Questao.AlternativaCorreta)
                {
                    sb.Append(";ACERTO");
                    acertos++;
                }
                else
                {
                    sb.Append(";ERRO");
                    erros++;
                }
            }

            var percentual = (double)acertos / questoes.Count;
            sb.Append(';').Append(acertos)
                .Append(';').Append(erros)
                .Append(';').Append(semResposta)
                .Append(';').Append(percentual.ToString("P2", System.Globalization.CultureInfo.InvariantCulture))
                .Append('\n');
        }

        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        var nomeArquivo = $"resultados-{Slug(prova.Nome)}-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv";
        return new MatrizExportada(nomeArquivo, bytes, alunos.Count, questoes.Count, prova.ProvaAlunos.Count - alunos.Count);
    }

    /// <summary>Aspas duplas quando o valor contém separador, aspas ou quebra de linha.</summary>
    private static string EscaparCsv(string valor)
    {
        // Evita que dados informados pela instituiÃ§Ã£o sejam interpretados como
        // fÃ³rmulas ao abrir o CSV no Excel.
        if (!string.IsNullOrEmpty(valor) && "=+-@".Contains(valor[0]))
            valor = "'" + valor;
        if (valor.Contains(';') || valor.Contains('"') || valor.Contains('\n') || valor.Contains('\r'))
            return '"' + valor.Replace("\"", "\"\"") + '"';
        return valor;
    }

    /// <summary>Reduz o nome da prova a um trecho seguro para nome de arquivo.</summary>
    private static string Slug(string texto)
    {
        var limpo = new string(texto
            .Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-')
            .ToArray());

        while (limpo.Contains("--"))
            limpo = limpo.Replace("--", "-");

        limpo = limpo.Trim('-');
        return limpo.Length > 60 ? limpo[..60].Trim('-') : limpo;
    }
}
