using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SistemaAvaliacao.Web.Data;
using SistemaAvaliacao.Web.Models.Entities;
using SistemaAvaliacao.Web.Models.Enums;
using SistemaAvaliacao.Web.Services;
using SistemaAvaliacao.Web.Services.Omr;

namespace SistemaAvaliacao.CorrecaoWorker;

/// <summary>
/// Serviço de background que processa lotes de PDFs com folhas de resposta.
/// Utiliza OMR (Optical Mark Recognition) para detecção automática de respostas.
/// </summary>
public sealed class OmrBackgroundWorker : BackgroundService
{
    private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
    private readonly ILogger<OmrBackgroundWorker> _logger;
    private readonly string _debugOmrPath;

    public OmrBackgroundWorker(
        IDbContextFactory<ApplicationDbContext> contextFactory,
        ILogger<OmrBackgroundWorker> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
        _debugOmrPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "wwwroot", "uploads", "omr-debug");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Worker de correção iniciado com OMR.");
        Directory.CreateDirectory(_debugOmrPath);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processou = await ProcessarProximoLoteAsync(stoppingToken);

                if (!processou)
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falha no ciclo do worker de correção.");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }
    }

    private async Task<bool> ProcessarProximoLoteAsync(CancellationToken cancellationToken)
    {
        await using var context =
            await _contextFactory.CreateDbContextAsync(cancellationToken);

        var lote = await context.LotesCorrecao
            .Include(l => l.Prova)
            .Where(l =>
                l.Status == StatusProcessamentoCorrecao.Recebido &&
                l.CaminhoArquivo != "")
            .OrderBy(l => l.CriadoEm)
            .FirstOrDefaultAsync(cancellationToken);

        if (lote == null)
            return false;

        lote.Status = StatusProcessamentoCorrecao.Processando;
        lote.Erro = null;

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            context.Entry(lote).Reload();
            lote.Status = StatusProcessamentoCorrecao.Processando;
            lote.Erro = null;
            await context.SaveChangesAsync(cancellationToken);
        }

        try
        {
            if (!File.Exists(lote.CaminhoArquivo))
            {
                throw new FileNotFoundException(
                    "O PDF do lote não foi encontrado.",
                    lote.CaminhoArquivo);
            }

            // Criar instância do serviço OMR
            var omrService = OmrServiceFactory.CreateDefault(
                optionCount: lote.Prova.NumeroAlternativas);

            // Diretório de debug para este lote
            var debugDirLote = Path.Combine(_debugOmrPath, lote.Id.ToString("N"));
            Directory.CreateDirectory(debugDirLote);

            // Processar PDF com OMR
            var folhasProcessadas = await ProcessarPdfComOmrAsync(
                context,
                lote,
                omrService,
                debugDirLote,
                cancellationToken);

            lote.FolhasProcessadas = folhasProcessadas;
            lote.CaminhoDebugOmr = debugDirLote;

            lote.Status = folhasProcessadas == 0
                ? StatusProcessamentoCorrecao.Erro
                : StatusProcessamentoCorrecao.AguardandoRevisao;

            if (folhasProcessadas == 0 && string.IsNullOrWhiteSpace(lote.Erro))
            {
                lote.Erro = "Nenhuma folha foi identificada ou processada.";
            }

            lote.FinalizadoEm = DateTime.UtcNow;
            await context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Lote {LoteId}: {FolhasProcessadas} folha(s) processada(s). Status: {Status}",
                lote.Id,
                folhasProcessadas,
                lote.Status);

            return true;
        }
        catch (Exception ex)
        {
            lote.Status = StatusProcessamentoCorrecao.Erro;
            lote.Erro = ex.Message.Length > 1000
                ? ex.Message[..1000]
                : ex.Message;
            lote.FinalizadoEm = DateTime.UtcNow;

            await context.SaveChangesAsync(cancellationToken);

            _logger.LogError(
                ex,
                "Falha ao processar lote {LoteId}.",
                lote.Id);

            return true;
        }
    }

    private async Task<int> ProcessarPdfComOmrAsync(
        ApplicationDbContext context,
        LoteCorrecao lote,
        OmrProcessingService omrService,
        string debugDir,
        CancellationToken cancellationToken)
    {
        // Carregar folhas esperadas
        var folhasEsperadas = await context.FolhasResposta
            .Where(f => f.ProvaId == lote.ProvaId)
            .ToListAsync(cancellationToken);

        // Questões da prova
        var questoesProva = await context.ProvaQuestoes
            .Where(pq => pq.ProvaId == lote.ProvaId)
            .OrderBy(pq => pq.Ordem)
            .Select(pq => new { pq.QuestaoId, pq.Ordem })
            .ToListAsync(cancellationToken);

        var questaoMap = questoesProva
            .ToDictionary(q => q.Ordem, q => q.QuestaoId);

        var processadas = 0;
        var erros = new List<string>();

        try
        {
            foreach (var (pageNum, resultado) in omrService.ProcessPdf(lote.CaminhoArquivo, debugDir))
            {
                try
                {
                    if (!resultado.Aligned)
                    {
                        erros.Add($"Página {pageNum}: Não alinhada.");
                        _logger.LogWarning("Página {Pagina}: Não alinhada", pageNum);
                        continue;
                    }

                    if (!resultado.StudentId.HasValue)
                    {
                        erros.Add($"Página {pageNum}: QR Code inválido.");
                        _logger.LogWarning("Página {Pagina}: QR Code inválido", pageNum);
                        continue;
                    }

                    var folha = folhasEsperadas.FirstOrDefault(f =>
                        FolhaRespostaPdfService.GuidToStableInt(f.AlunoId) == resultado.StudentId);

                    if (folha == null)
                    {
                        erros.Add($"Página {pageNum}: Aluno {resultado.StudentId} não pertence ao lote.");
                        continue;
                    }

                    await CriarLeiturasAPartirOmrAsync(
                        context,
                        folha,
                        resultado,
                        questaoMap,
                        cancellationToken);

                    folha.LoteCorrecaoId = lote.Id;
                    folha.Processada = true;
                    folha.ProcessadaEm = DateTime.UtcNow;
                    folha.NumeroPagina = pageNum;

                    processadas++;
                }
                catch (Exception ex)
                {
                    erros.Add($"Página {pageNum}: {ex.Message}");
                    _logger.LogWarning(ex, "Erro na página {Pagina}", pageNum);
                }
            }

            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao processar PDF");
            throw;
        }

        if (erros.Any())
        {
            var msg = string.Join(" | ", erros);
            lote.Erro = msg.Length > 1000 ? msg[..1000] : msg;
        }

        return processadas;
    }

    private async Task CriarLeiturasAPartirOmrAsync(
        ApplicationDbContext context,
        FolhaResposta folha,
        PageProcessingResult omrResult,
        Dictionary<int, Guid> questaoMap,
        CancellationToken cancellationToken)
    {
        var leiturasExistentes = await context.LeiturasResposta
            .Where(l => l.FolhaRespostaId == folha.Id)
            .ToListAsync(cancellationToken);

        if (leiturasExistentes.Any())
        {
            context.LeiturasResposta.RemoveRange(leiturasExistentes);
            await context.SaveChangesAsync(cancellationToken);
        }

        var novasLeituras = new List<LeituraResposta>();

        foreach (var questionResult in omrResult.Questions)
        {
            if (!questaoMap.TryGetValue(questionResult.Number, out var questaoId))
                continue;

            var status = MapearStatusOmr(questionResult.Status);

            var leitura = new LeituraResposta
            {
                FolhaRespostaId = folha.Id,
                QuestaoId = questaoId,
                Status = status,
                AlternativaDetectada = questionResult.DetectedOption,
                Confianca = (decimal)questionResult.Confidence,
                Revisada = false,
                Observacao = GerarObservacaoOmr(questionResult)
            };

            novasLeituras.Add(leitura);
        }

        context.LeiturasResposta.AddRange(novasLeituras);
        await context.SaveChangesAsync(cancellationToken);
    }

    private static StatusResposta MapearStatusOmr(QuestionStatus omrStatus)
    {
        return omrStatus switch
        {
            QuestionStatus.Valida => StatusResposta.Valida,
            QuestionStatus.EmBranco => StatusResposta.EmBranco,
            QuestionStatus.Dupla => StatusResposta.Dupla,
            QuestionStatus.RasuraDuvidosa => StatusResposta.RasuraDuvidosa,
            _ => StatusResposta.RasuraDuvidosa
        };
    }

    private static string GerarObservacaoOmr(QuestionResult qr)
    {
        var fillRatios = string.Join(" | ",
            qr.FillRatios.Select((fr, i) =>
                $"{(char)('A' + i)}={fr:P1}"));

        return qr.Status switch
        {
            QuestionStatus.Valida =>
                $"OMR: {qr.DetectedOption}={qr.FillRatios[qr.DetectedOption.Value - 'A']:P1} (conf={qr.Confidence:P0})",
            QuestionStatus.EmBranco =>
                $"OMR: Nenhuma marcação detectada",
            QuestionStatus.Dupla =>
                $"OMR: Múltiplas marcações - {fillRatios}",
            QuestionStatus.RasuraDuvidosa =>
                $"OMR: Ambígua - {fillRatios}",
            _ => $"OMR: {fillRatios}"
        };
    }
}
