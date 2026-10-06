using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SistemaAvaliacao.Web.Data;
using SistemaAvaliacao.Web.Models.Entities;
using SistemaAvaliacao.Web.Models.Enums;
using SistemaAvaliacao.Web.Models.ViewModels;

namespace SistemaAvaliacao.Web.Controllers;

[Authorize(Roles = "Admin,Coordenador")]
public class CorrecaoController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IWebHostEnvironment _environment;

    public CorrecaoController(ApplicationDbContext context, IWebHostEnvironment environment)
    {
        _context = context;
        _environment = environment;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var provas = await _context.Provas
            .AsNoTracking()
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new CorrecaoProvaItem
            {
                Id = p.Id,
                Nome = p.Nome,
                NumeroQuestoes = p.ProvaQuestoes.Count,
                NumeroAlunos = p.ProvaAlunos.Count,
                LotesRecebidos = _context.LotesCorrecao.Count(l => l.ProvaId == p.Id),
                UltimoLoteId = _context.LotesCorrecao
                    .Where(l => l.ProvaId == p.Id)
                    .OrderByDescending(l => l.CriadoEm)
                    .Select(l => (Guid?)l.Id)
                    .FirstOrDefault(),
                StatusUltimoLote = _context.LotesCorrecao
                    .Where(l => l.ProvaId == p.Id)
                    .OrderByDescending(l => l.CriadoEm)
                    .Select(l => (StatusProcessamentoCorrecao?)l.Status)
                    .FirstOrDefault()
            })
            .ToListAsync();

        return View(provas);
    }

    [HttpGet]
    public async Task<IActionResult> Enviar(Guid id)
    {
        var prova = await _context.Provas.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
        if (prova == null) return NotFound();

        if (prova.TipoAplicacao == TipoAplicacao.Virtual)
            return BadRequest("Envio de folhas não está disponível para provas virtuais.");

        return View(new EnviarCorrecaoViewModel
        {
            ProvaId = prova.Id,
            NomeProva = prova.Nome
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(100 * 1024 * 1024)]
    public async Task<IActionResult> Enviar(EnviarCorrecaoViewModel model)
    {
        var prova = await _context.Provas.FirstOrDefaultAsync(p => p.Id == model.ProvaId);
        if (prova == null) return NotFound();

        if (prova.TipoAplicacao == TipoAplicacao.Virtual)
            return BadRequest("Envio de folhas não está disponível para provas virtuais.");

        if (model.ArquivoPdf == null || model.ArquivoPdf.Length == 0)
            ModelState.AddModelError(nameof(model.ArquivoPdf), "Selecione um PDF.");
        else if (!Path.GetExtension(model.ArquivoPdf.FileName).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
            ModelState.AddModelError(nameof(model.ArquivoPdf), "Apenas arquivos PDF são aceitos.");
        else if (model.ArquivoPdf.Length > 100 * 1024 * 1024)
            ModelState.AddModelError(nameof(model.ArquivoPdf), "O arquivo deve ter no máximo 100 MB.");

        if (!ModelState.IsValid)
        {
            model.NomeProva = prova.Nome;
            return View(model);
        }

        var existemFolhas = await _context.FolhasResposta.AnyAsync(f => f.ProvaId == prova.Id);
        if (!existemFolhas)
        {
            TempData["Erro"] = "Gere primeiro as folhas de resposta personalizadas para esta prova.";
            return RedirectToAction(nameof(Enviar), new { id = prova.Id });
        }

        var pasta = Path.Combine(_environment.WebRootPath, "uploads", "correcoes");
        Directory.CreateDirectory(pasta);
        var lote = new LoteCorrecao
        {
            ProvaId = prova.Id,
            NomeArquivo = Path.GetFileName(model.ArquivoPdf!.FileName),
            Status = StatusProcessamentoCorrecao.Recebido,
            CriadoEm = DateTime.UtcNow
        };
        _context.LotesCorrecao.Add(lote);
        await _context.SaveChangesAsync();

        var nomeArmazenado = $"{lote.Id:N}.pdf";
        var caminho = Path.Combine(pasta, nomeArmazenado);

        await using (var stream = System.IO.File.Create(caminho))
            await model.ArquivoPdf!.CopyToAsync(stream);

        lote.CaminhoArquivo = caminho;
        await _context.SaveChangesAsync();

        TempData["Sucesso"] = "PDF recebido. Ele ficará disponível para processamento.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Revisar(Guid id)
    {
        var lote = await _context.LotesCorrecao
            .Include(l => l.Prova)
            .Include(l => l.Folhas)
                .ThenInclude(f => f.Aluno)
            .Include(l => l.Folhas)
                .ThenInclude(f => f.Leituras)
                    .ThenInclude(leitura => leitura.Questao)
            .FirstOrDefaultAsync(l => l.Id == id);

        if (lote == null) return NotFound();

        var ordens = await _context.ProvaQuestoes
            .Where(pq => pq.ProvaId == lote.ProvaId)
            .ToDictionaryAsync(pq => pq.QuestaoId, pq => pq.Ordem);

        return View(new RevisaoCorrecaoViewModel
        {
            LoteId = lote.Id,
            NomeProva = lote.Prova.Nome,
            NomeArquivo = lote.NomeArquivo,
            UrlPdf = $"/uploads/correcoes/{Path.GetFileName(lote.CaminhoArquivo)}",
            Folhas = lote.Folhas.Where(f => f.Processada).OrderBy(f => f.NumeroPagina).Select(f => new RevisaoFolhaItem
            {
                FolhaId = f.Id,
                NomeAluno = f.Aluno.Nome,
                NumeroPagina = f.NumeroPagina,
                Leituras = f.Leituras.OrderBy(l => ordens[l.QuestaoId]).Select(l => new RevisaoLeituraItem
                {
                    Id = l.Id,
                    Ordem = ordens[l.QuestaoId],
                    NumeroAlternativas = l.Questao.NumeroAlternativas,
                    Status = l.Status,
                    AlternativaDetectada = l.AlternativaDetectada,
                    AlternativaConfirmada = l.AlternativaConfirmada,
                    Confianca = l.Confianca,
                    Observacao = l.Observacao
                }).ToList()
            }).ToList()
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SalvarRevisao(RevisaoCorrecaoPostViewModel model)
    {
        var lote = await _context.LotesCorrecao
            .Include(l => l.Folhas)
            .FirstOrDefaultAsync(l => l.Id == model.LoteId);

        if (lote == null) return NotFound();
        if (lote.Status != StatusProcessamentoCorrecao.AguardandoRevisao)
        {
            TempData["Erro"] = "Este lote não está disponível para revisão.";
            return RedirectToAction(nameof(Index));
        }

        var idsValidos = lote.Folhas.SelectMany(f => f.Leituras).Select(l => l.Id).ToHashSet();
        var leituras = await _context.LeiturasResposta
            .Where(l => idsValidos.Contains(l.Id))
            .Include(l => l.Questao)
            .ToListAsync();

        foreach (var item in model.Leituras)
        {
            var leitura = leituras.FirstOrDefault(l => l.Id == item.Id);
            if (leitura == null) continue;

            var alternativa = item.AlternativaConfirmada.HasValue
                ? char.ToUpperInvariant(item.AlternativaConfirmada.Value)
                : (char?)null;
            var limite = (char)('A' + Math.Clamp(leitura.Questao.NumeroAlternativas, 2, 5) - 1);

            if (item.Status == StatusResposta.Valida &&
                (!alternativa.HasValue || alternativa.Value < 'A' || alternativa.Value > limite))
            {
                ModelState.AddModelError(string.Empty, $"A questão {leitura.QuestaoId} precisa de uma alternativa válida.");
                continue;
            }

            leitura.Status = item.Status;
            leitura.AlternativaConfirmada = item.Status == StatusResposta.Valida ? alternativa : null;
            leitura.Observacao = string.IsNullOrWhiteSpace(item.Observacao) ? null : item.Observacao.Trim();
            leitura.Revisada = true;
        }

        if (!ModelState.IsValid)
        {
            TempData["Erro"] = "Revise as alternativas válidas antes de salvar.";
            return RedirectToAction(nameof(Revisar), new { id = model.LoteId });
        }

        await _context.SaveChangesAsync();
        TempData["Sucesso"] = "Revisão salva. O lote ainda não foi confirmado.";
        return RedirectToAction(nameof(Revisar), new { id = model.LoteId });
    }

    /// <summary>
    /// Apaga os lotes de UPLOAD (CaminhoArquivo != "") não confirmados de uma prova:
    /// arquivos físicos de PDF, imagens de debug e registros (LoteCorrecao → FolhaResposta → LeituraResposta).
    /// Preserva lotes confirmados E o lote container das FolhasResposta personalizadas
    /// (CaminhoArquivo == ""), que são necessárias para o check do Enviar.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LimparLotes(Guid id)
    {
        // Só apaga lotes que têm um PDF de upload real — jamais o lote container das folhas personalizadas
        var lotes = await _context.LotesCorrecao
            .Include(l => l.Folhas)
                .ThenInclude(f => f.Leituras)
            .Where(l => l.ProvaId == id
                     && l.Status != StatusProcessamentoCorrecao.Confirmado
                     && l.CaminhoArquivo != "")
            .ToListAsync();

        foreach (var lote in lotes)
        {
            // Apaga arquivo PDF físico
            if (System.IO.File.Exists(lote.CaminhoArquivo))
                System.IO.File.Delete(lote.CaminhoArquivo);

            // Apaga pasta de debug OMR, se existir
            if (!string.IsNullOrEmpty(lote.CaminhoDebugOmr) && Directory.Exists(lote.CaminhoDebugOmr))
                Directory.Delete(lote.CaminhoDebugOmr, recursive: true);

            foreach (var folha in lote.Folhas)
                _context.LeiturasResposta.RemoveRange(folha.Leituras);

            _context.FolhasResposta.RemoveRange(lote.Folhas);
            _context.LotesCorrecao.Remove(lote);
        }

        await _context.SaveChangesAsync();

        var total = lotes.Count;
        TempData["Sucesso"] = total > 0
            ? $"{total} lote(s) de upload removido(s). Agora envie a nova folha de teste."
            : "Nenhum lote de upload pendente encontrado para esta prova.";

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Confirmar(Guid id)
    {
        var lote = await _context.LotesCorrecao
            .Include(l => l.Folhas)
                .ThenInclude(f => f.Leituras)
            .FirstOrDefaultAsync(l => l.Id == id);

        if (lote == null) return NotFound();
        if (lote.Status != StatusProcessamentoCorrecao.AguardandoRevisao)
        {
            TempData["Erro"] = "Este lote não está disponível para confirmação.";
            return RedirectToAction(nameof(Index));
        }

        var folhasProcessadas = lote.Folhas.Where(f => f.Processada).ToList();
        if (!folhasProcessadas.Any() || folhasProcessadas.Any(f => f.Leituras.Any(l => !l.Revisada)))
        {
            TempData["Erro"] = "Revise todas as questões das folhas identificadas antes da confirmação.";
            return RedirectToAction(nameof(Revisar), new { id });
        }

        foreach (var folha in folhasProcessadas)
        {
            foreach (var leitura in folha.Leituras)
            {
                var resposta = await _context.Respostas.FirstOrDefaultAsync(r =>
                    r.ProvaId == folha.ProvaId && r.AlunoId == folha.AlunoId && r.QuestaoId == leitura.QuestaoId);

                if (resposta == null)
                {
                    resposta = new Resposta
                    {
                        ProvaId = folha.ProvaId,
                        AlunoId = folha.AlunoId,
                        QuestaoId = leitura.QuestaoId
                    };
                    _context.Respostas.Add(resposta);
                }

                resposta.Status = leitura.Status;
                resposta.AlternativaMarcada = leitura.Status == StatusResposta.Valida
                    ? leitura.AlternativaConfirmada
                    : null;
                resposta.Revisada = true;
                resposta.Observacao = leitura.Observacao;
            }

            var provaAluno = await _context.ProvaAlunos.FirstOrDefaultAsync(pa =>
                pa.ProvaId == folha.ProvaId && pa.AlunoId == folha.AlunoId);
            if (provaAluno != null)
            {
                provaAluno.Status = StatusProvaAluno.Finalizada;
                provaAluno.FinalizadaEm ??= DateTime.UtcNow;
            }
        }

        lote.Status = StatusProcessamentoCorrecao.Confirmado;
        lote.FinalizadoEm = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        TempData["Sucesso"] = "Lote confirmado e respostas gravadas com sucesso.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Salva a revisão e confirma o lote em um único passo — evita a necessidade
    /// de clicar em "Salvar revisão" antes de "Confirmar".
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmarComRevisao(RevisaoCorrecaoPostViewModel model)
    {
        var lote = await _context.LotesCorrecao
            .Include(l => l.Folhas)
                .ThenInclude(f => f.Leituras)
            .FirstOrDefaultAsync(l => l.Id == model.LoteId);

        if (lote == null) return NotFound();
        if (lote.Status != StatusProcessamentoCorrecao.AguardandoRevisao)
        {
            TempData["Erro"] = "Este lote não está disponível para confirmação.";
            return RedirectToAction(nameof(Index));
        }

        // 1) Salvar revisão — marca todas as leituras como Revisada = true
        var idsValidos = lote.Folhas.SelectMany(f => f.Leituras).Select(l => l.Id).ToHashSet();
        var leituras = await _context.LeiturasResposta
            .Where(l => idsValidos.Contains(l.Id))
            .Include(l => l.Questao)
            .ToListAsync();

        foreach (var item in model.Leituras)
        {
            var leitura = leituras.FirstOrDefault(l => l.Id == item.Id);
            if (leitura == null) continue;

            var alternativa = item.AlternativaConfirmada.HasValue
                ? char.ToUpperInvariant(item.AlternativaConfirmada.Value)
                : (char?)null;
            var limite = (char)('A' + Math.Clamp(leitura.Questao.NumeroAlternativas, 2, 5) - 1);

            if (item.Status == StatusResposta.Valida &&
                (!alternativa.HasValue || alternativa.Value < 'A' || alternativa.Value > limite))
            {
                TempData["Erro"] = $"Questão {leitura.QuestaoId} marcada como Válida sem alternativa correta.";
                return RedirectToAction(nameof(Revisar), new { id = model.LoteId });
            }

            leitura.Status = item.Status;
            leitura.AlternativaConfirmada = item.Status == StatusResposta.Valida ? alternativa : null;
            leitura.Observacao = string.IsNullOrWhiteSpace(item.Observacao) ? null : item.Observacao.Trim();
            leitura.Revisada = true;
        }

        await _context.SaveChangesAsync();

        // 2) Confirmar — gravar Respostas e atualizar ProvaAluno
        var folhasProcessadas = lote.Folhas.Where(f => f.Processada).ToList();
        foreach (var folha in folhasProcessadas)
        {
            foreach (var leitura in folha.Leituras)
            {
                var resposta = await _context.Respostas.FirstOrDefaultAsync(r =>
                    r.ProvaId == folha.ProvaId && r.AlunoId == folha.AlunoId && r.QuestaoId == leitura.QuestaoId);

                if (resposta == null)
                {
                    resposta = new Resposta
                    {
                        ProvaId = folha.ProvaId,
                        AlunoId = folha.AlunoId,
                        QuestaoId = leitura.QuestaoId
                    };
                    _context.Respostas.Add(resposta);
                }

                resposta.Status = leitura.Status;
                resposta.AlternativaMarcada = leitura.Status == StatusResposta.Valida
                    ? leitura.AlternativaConfirmada
                    : null;
                resposta.Revisada = true;
                resposta.Observacao = leitura.Observacao;
            }

            var provaAluno = await _context.ProvaAlunos.FirstOrDefaultAsync(pa =>
                pa.ProvaId == folha.ProvaId && pa.AlunoId == folha.AlunoId);
            if (provaAluno != null)
            {
                provaAluno.Status = StatusProvaAluno.Finalizada;
                provaAluno.FinalizadaEm ??= DateTime.UtcNow;
            }
        }

        lote.Status = StatusProcessamentoCorrecao.Confirmado;
        lote.FinalizadoEm = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        TempData["Sucesso"] = "Lote confirmado e respostas gravadas com sucesso.";
        return RedirectToAction(nameof(Index));
    }
}
