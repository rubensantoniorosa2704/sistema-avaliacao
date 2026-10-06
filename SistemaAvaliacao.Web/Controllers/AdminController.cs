using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using SistemaAvaliacao.Web.Data;
using SistemaAvaliacao.Web.Helpers;
using SistemaAvaliacao.Web.Models.Entities;
using SistemaAvaliacao.Web.Models.Enums;
using SistemaAvaliacao.Web.Models.ViewModels;
using SistemaAvaliacao.Web.Services;
using SistemaAvaliacao.Web.Services.Omr;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SistemaAvaliacao.Web.Controllers;

[Authorize(Roles = "Admin")]
public class AdminController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly ImageUploadService _imageUpload;
    private readonly ImportacaoAlunosService _importacaoAlunos;
    private readonly XlsxProvaImportService _xlsxImport;
    private readonly ExportacaoResultadosService _exportacaoResultados;
    private readonly ExportacaoXlsxService _exportacaoXlsx;
    private readonly FolhaRespostaPdfService _folhaRespostaPdf;
    private readonly TentativaExameService _tentativas;

    public AdminController(
        ApplicationDbContext context,
        ImageUploadService imageUpload,
        ImportacaoAlunosService importacaoAlunos,
        XlsxProvaImportService xlsxImport,
        ExportacaoResultadosService exportacaoResultados,
        ExportacaoXlsxService exportacaoXlsx,
        FolhaRespostaPdfService folhaRespostaPdf,
        TentativaExameService tentativas)
    {
        _context = context;
        _imageUpload = imageUpload;
        _importacaoAlunos = importacaoAlunos;
        _xlsxImport = xlsxImport;
        _exportacaoResultados = exportacaoResultados;
        _exportacaoXlsx = exportacaoXlsx;
        _folhaRespostaPdf = folhaRespostaPdf;
        _tentativas = tentativas;
    }

    public IActionResult Index() => RedirectToAction(nameof(Provas));

    // ───────────────────────────── Helpers ────────────────────────────────

    private Guid GetCurrentUserId()
        => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>Prova travada para edição: algum aluno já iniciou.</summary>
    private static bool EstaBloqueada(Prova prova)
        => prova.ProvaAlunos.Any(pa => pa.IniciadaEm != null);

    /// <summary>Verifica a trava carregando os alunos da prova.</summary>
    private async Task<bool> ProvaBloqueadaAsync(Guid provaId)
        => await _context.ProvaAlunos.AnyAsync(pa => pa.ProvaId == provaId && pa.IniciadaEm != null);

    /// <summary>
    /// Export liberado quando todos os alunos finalizaram OU a janela da prova
    /// terminou — e desde que haja ao menos um aluno finalizado (dados a exportar).
    /// Depois do prazo, ConsolidarSeEncerradaAsync já transformou as tentativas
    /// abandonadas em Finalizada, então basta contar os finalizados.
    /// </summary>
    private static bool PodeExportar(Prova prova)
    {
        var total = prova.ProvaAlunos.Count;
        var finalizados = prova.ProvaAlunos.Count(pa => pa.Status == StatusProvaAluno.Finalizada);
        if (finalizados == 0) return false;
        var todosFinalizaram = total > 0 && finalizados == total;
        var passouFim = DateTime.UtcNow > prova.DataFim;
        return todosFinalizaram || passouFim;
    }

    // ───────────────────────────── Provas CRUD ───────────────────────────

    [HttpGet]
    public async Task<IActionResult> Provas(string? buscaNome)
    {
        var query = _context.Provas
            .Include(p => p.ProvaQuestoes)
            .Include(p => p.ProvaAlunos)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(buscaNome))
        {
            query = query.Where(p =>
                p.Nome.Contains(buscaNome) ||
                (p.Descricao != null && p.Descricao.Contains(buscaNome)));
        }

        var provas = await query
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new ProvaListItem
            {
                Id = p.Id,
                Nome = p.Nome,
                DataInicio = p.DataInicio,
                DataFim = p.DataFim,
                NumeroQuestoes = p.ProvaQuestoes.Count,
                NumeroAlunos = p.ProvaAlunos.Count,
                CreatedAt = p.CreatedAt
            })
            .ToListAsync();

        return View(new ListarProvasViewModel
        {
            Provas = provas,
            BuscaNome = buscaNome
        });
    }

    [HttpGet]
    public IActionResult CriarProva() => View(new CriarProvaViewModel());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CriarProva(CriarProvaViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        if (model.DataFim <= model.DataInicio)
        {
            ModelState.AddModelError("DataFim", "Data de fim deve ser posterior à data de início.");
            return View(model);
        }

        var prova = new Prova
        {
            Nome = model.Nome,
            Descricao = model.Descricao,
            NumeroAlternativas = model.NumeroAlternativas,
            TipoAplicacao = model.TipoAplicacao,
            // Datas informadas no fuso da aplicação → armazenadas em UTC.
            DataInicio = DataHora.ParaUtc(model.DataInicio),
            DataFim = DataHora.ParaUtc(model.DataFim),
            TokenPublico = Guid.NewGuid(),
            CriadoPorId = GetCurrentUserId(),
            CreatedAt = DateTime.UtcNow
        };

        _context.Provas.Add(prova);
        await _context.SaveChangesAsync();

        TempData["Sucesso"] = "Prova criada! Agora importe o caderno e o gabarito, ou adicione questões.";
        return RedirectToAction(nameof(QuestoesProva), new { id = prova.Id });
    }

    [HttpGet]
    public async Task<IActionResult> EditarProva(Guid id)
    {
        var prova = await _context.Provas
            .Include(p => p.ProvaQuestoes)
            .Include(p => p.ProvaAlunos)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (prova == null) return NotFound();

        if (EstaBloqueada(prova))
        {
            TempData["Erro"] = "A prova já foi iniciada por um aluno e não pode mais ser editada.";
            return RedirectToAction(nameof(DetalhesProva), new { id });
        }

        return View(new EditarProvaViewModel
        {
            Id = prova.Id,
            Nome = prova.Nome,
            Descricao = prova.Descricao,
            NumeroAlternativas = prova.NumeroAlternativas,
            // Armazenadas em UTC → exibidas no formulário no fuso da aplicação.
            DataInicio = DataHora.ParaLocal(prova.DataInicio),
            DataFim = DataHora.ParaLocal(prova.DataFim),
            NumeroQuestoes = prova.ProvaQuestoes.Count,
            NumeroAlunosAtribuidos = prova.ProvaAlunos.Count
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> EditarProvaModal(EditarProvaViewModel model)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));

        if (model.DataFim <= model.DataInicio)
            return BadRequest(new[] { "Data de fim deve ser posterior à data de início." });

        var prova = await _context.Provas
            .Include(p => p.ProvaAlunos)
            .FirstOrDefaultAsync(p => p.Id == model.Id);

        if (prova == null) return NotFound();

        if (EstaBloqueada(prova))
            return BadRequest(new[] { "A prova já foi iniciada e não pode mais ser editada." });

        prova.Nome = model.Nome;
        prova.Descricao = model.Descricao;
        prova.NumeroAlternativas = model.NumeroAlternativas;
        prova.DataInicio = DataHora.ParaUtc(model.DataInicio);
        prova.DataFim = DataHora.ParaUtc(model.DataFim);
        prova.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return Ok(new { nome = prova.Nome, descricao = prova.Descricao });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> EditarProva(EditarProvaViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        if (model.DataFim <= model.DataInicio)
        {
            ModelState.AddModelError("DataFim", "Data de fim deve ser posterior à data de início.");
            return View(model);
        }

        var prova = await _context.Provas
            .Include(p => p.ProvaAlunos)
            .FirstOrDefaultAsync(p => p.Id == model.Id);
        if (prova == null) return NotFound();

        if (EstaBloqueada(prova))
        {
            TempData["Erro"] = "A prova já foi iniciada por um aluno e não pode mais ser editada.";
            return RedirectToAction(nameof(DetalhesProva), new { id = prova.Id });
        }

        prova.Nome = model.Nome;
        prova.Descricao = model.Descricao;
        prova.NumeroAlternativas = model.NumeroAlternativas;
        prova.DataInicio = DataHora.ParaUtc(model.DataInicio);
        prova.DataFim = DataHora.ParaUtc(model.DataFim);
        prova.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        TempData["Sucesso"] = "Prova atualizada com sucesso!";
        return RedirectToAction(nameof(DetalhesProva), new { id = prova.Id });
    }

    [HttpGet]
    public async Task<IActionResult> DetalhesProva(Guid id)
    {
        // Se o prazo já terminou, consolida as tentativas abandonadas (EmAndamento →
        // Finalizada) antes de ler os dados, para que status e exportações já reflitam
        // o resultado final. Não faz nada antes do prazo.
        await _tentativas.ConsolidarSeEncerradaAsync(id);

        var prova = await _context.Provas
            .Include(p => p.CriadoPor)
            .Include(p => p.ProvaQuestoes).ThenInclude(pq => pq.Questao)
            .Include(p => p.ProvaAlunos).ThenInclude(pa => pa.Aluno)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (prova == null) return NotFound();

        var linkPublico = Url.Action("Identificar", "Exame",
            new { token = prova.TokenPublico }, Request.Scheme) ?? $"/prova/{prova.TokenPublico}";

        return View(new DetalhesProvaViewModel
        {
            Id = prova.Id,
            Nome = prova.Nome,
            Descricao = prova.Descricao,
            DataInicio = prova.DataInicio,
            DataFim = prova.DataFim,
            CreatedAt = prova.CreatedAt,
            TipoAplicacao = prova.TipoAplicacao,
            NomeCriador = prova.CriadoPor.Nome,
            LinkPublico = linkPublico,
            Bloqueada = EstaBloqueada(prova),
            PodeExportar = PodeExportar(prova),
            NumeroAlternativas = prova.NumeroAlternativas,
            Questoes = prova.ProvaQuestoes
                .Select(_ => new QuestaoResumoItem()).ToList(),
            Alunos = prova.ProvaAlunos
                .OrderBy(pa => pa.Aluno.Nome)
                .Select(pa => new AlunoProvaItem
                {
                    AlunoId = pa.AlunoId,
                    NomeAluno = pa.Aluno.Nome,
                    EmailAluno = pa.Aluno.Email,
                    CpfAluno = pa.Aluno.CPF,
                    MatriculaAluno = pa.Aluno.Matricula,
                    Status = pa.Status.ToString(),
                    DataInicio = pa.IniciadaEm,
                    DataFim = pa.FinalizadaEm,
                    JaIniciou = pa.IniciadaEm != null
                }).ToList()
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ExcluirProva(Guid id)
    {
        var prova = await _context.Provas
            .Include(p => p.ProvaAlunos)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (prova == null) return NotFound();

        if (EstaBloqueada(prova))
        {
            TempData["Erro"] = "Não é possível excluir uma prova já iniciada por um aluno.";
            return RedirectToAction(nameof(Provas));
        }

        _context.Provas.Remove(prova);
        await _context.SaveChangesAsync();

        TempData["Sucesso"] = "Prova excluída com sucesso!";
        return RedirectToAction(nameof(Provas));
    }

    // ───────────────────────── Questões da Prova ─────────────────────────

    [HttpGet]
    public async Task<IActionResult> QuestoesProva(Guid id)
    {
        var prova = await _context.Provas
            .Include(p => p.ProvaQuestoes).ThenInclude(pq => pq.Questao)
            .Include(p => p.ProvaAlunos)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (prova == null) return NotFound();

        return View(new QuestoesProvaViewModel
        {
            ProvaId = prova.Id,
            NomeProva = prova.Nome,
            Bloqueada = EstaBloqueada(prova),
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
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SalvarQuestaoProva(SalvarQuestaoProvaViewModel model)
    {
        if (await ProvaBloqueadaAsync(model.ProvaId))
        {
            TempData["Erro"] = "A prova já foi iniciada e não pode mais ser editada.";
            return RedirectToAction(nameof(QuestoesProva), new { id = model.ProvaId });
        }

        if (!ModelState.IsValid)
        {
            TempData["Erro"] = "Preencha todos os campos obrigatórios.";
            return RedirectToAction(nameof(QuestoesProva), new { id = model.ProvaId });
        }

        var prova = await _context.Provas
            .Include(p => p.ProvaQuestoes)
            .FirstOrDefaultAsync(p => p.Id == model.ProvaId);

        if (prova == null) return NotFound();

        string? imagemUrl = null;
        if (model.Imagem is { Length: > 0 })
        {
            var (ok, caminho, erro) = await _imageUpload.UploadImageAsync(model.Imagem);
            if (!ok)
            {
                TempData["Erro"] = erro ?? "Falha ao enviar a imagem.";
                return RedirectToAction(nameof(QuestoesProva), new { id = model.ProvaId });
            }
            imagemUrl = caminho;
        }

        if (model.QuestaoId.HasValue)
        {
            var questao = await _context.Questoes.FindAsync(model.QuestaoId.Value);
            if (questao == null) return NotFound();

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

            if (imagemUrl != null)
                questao.ImagemUrl = imagemUrl;

            TempData["Sucesso"] = "Questão atualizada com sucesso!";
        }
        else
        {
            var questao = new Questao
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

            _context.Questoes.Add(questao);
            await _context.SaveChangesAsync();

            var proximaOrdem = prova.ProvaQuestoes.Any()
                ? prova.ProvaQuestoes.Max(pq => pq.Ordem) + 1
                : 1;

            _context.ProvaQuestoes.Add(new ProvaQuestao
            {
                ProvaId = prova.Id,
                QuestaoId = questao.Id,
                Ordem = proximaOrdem
            });

            TempData["Sucesso"] = $"Questão {proximaOrdem} adicionada com sucesso!";
        }

        prova.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return RedirectToAction(nameof(QuestoesProva), new { id = model.ProvaId });
    }

    [HttpGet]
    public async Task<IActionResult> ObterQuestao(Guid id)
    {
        var questao = await _context.Questoes.FindAsync(id);
        if (questao == null) return NotFound();

        return Json(new
        {
            questao.Id,
            questao.Enunciado,
            questao.Comando,
            questao.ImagemUrl,
            questao.DescricaoAcessibilidade,
            questao.AlternativaA,
            questao.AlternativaB,
            questao.AlternativaC,
            questao.AlternativaD,
            questao.AlternativaE,
            questao.NumeroAlternativas,
            AlternativaCorreta = questao.AlternativaCorreta.ToString()
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ExcluirQuestaoProva(Guid provaId, Guid questaoId)
    {
        if (await ProvaBloqueadaAsync(provaId))
        {
            TempData["Erro"] = "A prova já foi iniciada e não pode mais ser editada.";
            return RedirectToAction(nameof(QuestoesProva), new { id = provaId });
        }

        var provaQuestao = await _context.ProvaQuestoes
            .FirstOrDefaultAsync(pq => pq.ProvaId == provaId && pq.QuestaoId == questaoId);

        if (provaQuestao == null) return NotFound();

        var ordemRemovida = provaQuestao.Ordem;
        _context.ProvaQuestoes.Remove(provaQuestao);

        var questao = await _context.Questoes.FindAsync(questaoId);
        if (questao != null)
        {
            var emOutraProva = await _context.ProvaQuestoes
                .AnyAsync(pq => pq.QuestaoId == questaoId && pq.ProvaId != provaId);
            if (!emOutraProva)
                _context.Questoes.Remove(questao);
        }

        var posteriores = await _context.ProvaQuestoes
            .Where(pq => pq.ProvaId == provaId && pq.Ordem > ordemRemovida)
            .ToListAsync();
        foreach (var pq in posteriores) pq.Ordem--;

        var prova = await _context.Provas.FindAsync(provaId);
        if (prova != null) prova.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        TempData["Sucesso"] = "Questão excluída com sucesso!";
        return RedirectToAction(nameof(QuestoesProva), new { id = provaId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ReordenarQuestaoProva(Guid provaId, Guid questaoId, string direcao)
    {
        if (await ProvaBloqueadaAsync(provaId))
        {
            TempData["Erro"] = "A prova já foi iniciada e não pode mais ser editada.";
            return RedirectToAction(nameof(QuestoesProva), new { id = provaId });
        }

        var questaoAtual = await _context.ProvaQuestoes
            .FirstOrDefaultAsync(pq => pq.ProvaId == provaId && pq.QuestaoId == questaoId);

        if (questaoAtual == null) return NotFound();

        var novaOrdem = direcao == "cima" ? questaoAtual.Ordem - 1 : questaoAtual.Ordem + 1;

        var questaoDestino = await _context.ProvaQuestoes
            .FirstOrDefaultAsync(pq => pq.ProvaId == provaId && pq.Ordem == novaOrdem);

        if (questaoDestino == null)
            return RedirectToAction(nameof(QuestoesProva), new { id = provaId });

        questaoDestino.Ordem = questaoAtual.Ordem;
        questaoAtual.Ordem = novaOrdem;

        var prova = await _context.Provas.FindAsync(provaId);
        if (prova != null) prova.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return RedirectToAction(nameof(QuestoesProva), new { id = provaId });
    }

    // ───────────────────── Importação de questões via XLSX ─────────────────────

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ImportarQuestoesXlsx(Guid provaId, IFormFile? arquivoXlsx)
    {
        var prova = await _context.Provas.FindAsync(provaId);
        if (prova == null) return NotFound();

        if (await ProvaBloqueadaAsync(provaId))
        {
            TempData["Erro"] = "A prova já foi iniciada e não pode mais receber questões.";
            return RedirectToAction(nameof(QuestoesProva), new { id = provaId });
        }

        if (arquivoXlsx == null || arquivoXlsx.Length == 0)
        {
            TempData["Erro"] = "Selecione a planilha (.xlsx) com as questões para importar.";
            return RedirectToAction(nameof(QuestoesProva), new { id = provaId });
        }

        if (!Path.GetExtension(arquivoXlsx.FileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            TempData["Erro"] = "Apenas arquivos .xlsx são aceitos.";
            return RedirectToAction(nameof(QuestoesProva), new { id = provaId });
        }

        List<ImportarQuestaoItem> itens;
        List<string> avisos;
        try
        {
            using var stream = arquivoXlsx.OpenReadStream();
            (itens, avisos) = _xlsxImport.PrepararPreview(stream);
        }
        catch (Exception ex)
        {
            TempData["Erro"] = $"Falha ao ler a planilha: {ex.Message}";
            return RedirectToAction(nameof(QuestoesProva), new { id = provaId });
        }

        return View("ImportarQuestoesXlsx", new ImportarQuestoesXlsxViewModel
        {
            ProvaId = provaId,
            NomeProva = prova.Nome,
            NomeArquivo = arquivoXlsx.FileName,
            NomeArquivoGabarito = string.Empty,
            Avisos = avisos,
            Itens = itens
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmarImportacaoXlsx(ImportarQuestoesXlsxViewModel model)
    {
        var prova = await _context.Provas.FindAsync(model.ProvaId);
        if (prova == null) return NotFound();

        if (await ProvaBloqueadaAsync(model.ProvaId))
        {
            TempData["Erro"] = "A prova já foi iniciada e não pode mais receber questões.";
            return RedirectToAction(nameof(QuestoesProva), new { id = model.ProvaId });
        }

        var itensParaGravar = model.Itens.Where(i => i.Incluir).ToList();

        if (itensParaGravar.Count == 0)
        {
            TempData["Erro"] = "Nenhuma questão selecionada para importação.";
            return RedirectToAction(nameof(QuestoesProva), new { id = model.ProvaId });
        }

        var semGabarito = itensParaGravar.Count(i => i.AlternativaCorreta == null);
        var semComando = itensParaGravar.Count(i => string.IsNullOrWhiteSpace(i.Comando));
        var semAlternativasBasicas = itensParaGravar.Count(i =>
            string.IsNullOrWhiteSpace(i.AlternativaA) || string.IsNullOrWhiteSpace(i.AlternativaB));

        if (semGabarito > 0 || semComando > 0 || semAlternativasBasicas > 0)
        {
            var partes = new List<string>();
            if (semGabarito > 0) partes.Add($"{semGabarito} sem alternativa correta");
            if (semComando > 0) partes.Add($"{semComando} sem comando");
            if (semAlternativasBasicas > 0) partes.Add($"{semAlternativasBasicas} sem alternativa A/B");

            TempData["Erro"] = $"Revise antes de confirmar: {string.Join("; ", partes)}.";
            model.NomeProva = prova.Nome;
            return View("ImportarQuestoesXlsx", model);
        }

        var inseridas = await _xlsxImport.ConfirmarAsync(model.ProvaId, itensParaGravar);

        TempData["Sucesso"] = $"{inseridas} questão(ões) importada(s) com sucesso!";
        return RedirectToAction(nameof(QuestoesProva), new { id = model.ProvaId });
    }

    // ───────────────────────── Alunos da Prova ──────────────────────────

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ImportarAlunosProva(Guid provaId, IFormFile? arquivoCsv)
    {
        if (arquivoCsv == null || arquivoCsv.Length == 0)
        {
            TempData["Erro"] = "Selecione um arquivo CSV.";
            return RedirectToAction(nameof(DetalhesProva), new { id = provaId });
        }

        if (!Path.GetExtension(arquivoCsv.FileName).Equals(".csv", StringComparison.OrdinalIgnoreCase))
        {
            TempData["Erro"] = "Envie um arquivo no formato CSV (.csv).";
            return RedirectToAction(nameof(DetalhesProva), new { id = provaId });
        }

        if (arquivoCsv.Length > 5 * 1024 * 1024)
        {
            TempData["Erro"] = "O arquivo CSV deve ter no mÃ¡ximo 5 MB.";
            return RedirectToAction(nameof(DetalhesProva), new { id = provaId });
        }

        var prova = await _context.Provas
            .Include(p => p.ProvaAlunos)
            .FirstOrDefaultAsync(p => p.Id == provaId);

        if (prova == null) return NotFound();

        if (EstaBloqueada(prova))
        {
            TempData["Erro"] = "A prova já foi iniciada e a lista de alunos não pode mais ser alterada.";
            return RedirectToAction(nameof(DetalhesProva), new { id = provaId });
        }

        using var stream = arquivoCsv.OpenReadStream();
        var resultado = await _importacaoAlunos.ImportarAsync(prova, stream);

        TempData["Sucesso"] = resultado.Resumo();

        if (resultado.Erros.Any())
            TempData["Erro"] = $"{resultado.Erros.Count} linha(s) com erro: {string.Join(" | ", resultado.Erros.Take(5))}";

        return RedirectToAction(nameof(DetalhesProva), new { id = provaId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoverAlunoProva(Guid provaId, Guid alunoId)
    {
        var provaAluno = await _context.ProvaAlunos
            .FirstOrDefaultAsync(pa => pa.ProvaId == provaId && pa.AlunoId == alunoId);

        if (provaAluno == null) return NotFound();

        if (provaAluno.IniciadaEm != null)
        {
            TempData["Erro"] = "Não é possível remover um aluno que já iniciou a prova.";
            return RedirectToAction(nameof(DetalhesProva), new { id = provaId });
        }

        _context.ProvaAlunos.Remove(provaAluno);
        await _context.SaveChangesAsync();

        TempData["Sucesso"] = "Aluno removido da prova.";
        return RedirectToAction(nameof(DetalhesProva), new { id = provaId });
    }

    [HttpGet]
    public IActionResult BaixarModeloCsv()
    {
        var conteudo = "Nome;Matricula;Email;CPF\nJoão da Silva;2024001;joao@email.com;12345678901\nMaria Souza;2024002;maria@email.com;98765432100\n";
        var bytes = System.Text.Encoding.UTF8.GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(conteudo)).ToArray();
        return File(bytes, "text/csv", "modelo-alunos.csv");
    }

    [HttpGet]
    public async Task<IActionResult> GerarFolhasResposta(Guid id)
    {
        try
        {
            var resultado = await _folhaRespostaPdf.GerarAsync(id);
            return File(resultado.Pdf, "application/pdf", resultado.NomeArquivo);
        }
        catch (InvalidOperationException ex)
        {
            TempData["Erro"] = ex.Message;
            return RedirectToAction(nameof(DetalhesProva), new { id });
        }
    }

    /// <summary>
    /// Gera um PDF de teste com bolhas já preenchidas (padrão: normal, dupla, rasura).
    /// Cria as FolhasResposta reais no banco com os StudentIds corretos dos alunos,
    /// para que o worker consiga fazer o match após o envio do PDF na tela de correção.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GerarFolhaTesteOmr(Guid id)
    {
        try
        {
            var resultado = await _folhaRespostaPdf.GerarTesteAsync(id);
            return File(resultado.Pdf, "application/pdf", resultado.NomeArquivo);
        }
        catch (InvalidOperationException ex)
        {
            TempData["Erro"] = ex.Message;
            return RedirectToAction(nameof(DetalhesProva), new { id });
        }
    }

    // ───────────────────── Exportação da matriz de respostas ─────────────────────

    /// <summary>
    /// Abre a view de impressão da prova física com todas as questões.
    /// Só disponível para provas do tipo Física.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> ImprimirProva(Guid id)
    {
        var prova = await _context.Provas
            .Include(p => p.ProvaQuestoes.OrderBy(pq => pq.Ordem))
                .ThenInclude(pq => pq.Questao)
            .Include(p => p.ProvaAlunos)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (prova == null) return NotFound();
        if (prova.TipoAplicacao != Models.Enums.TipoAplicacao.Fisica)
            return RedirectToAction(nameof(DetalhesProva), new { id });

        if (!prova.ProvaQuestoes.Any())
        {
            TempData["Erro"] = "A prova não possui questões para imprimir.";
            return RedirectToAction(nameof(DetalhesProva), new { id });
        }

        var vm = new ImprimirProvaViewModel
        {
            ProvaId = prova.Id,
            NomeProva = prova.Nome,
            Descricao = prova.Descricao,
            NumeroAlunos = prova.ProvaAlunos.Count,
            Questoes = prova.ProvaQuestoes
                .Select(pq => new QuestaoNaProvaItem
                {
                    QuestaoId    = pq.QuestaoId,
                    Ordem        = pq.Ordem,
                    Enunciado    = pq.Questao.Enunciado,
                    TextoBase    = pq.Questao.TextoBase,
                    Comando      = pq.Questao.Comando,
                    ImagemUrl    = pq.Questao.ImagemUrl,
                    DescricaoAcessibilidade = pq.Questao.DescricaoAcessibilidade,
                    AlternativaA = pq.Questao.AlternativaA,
                    AlternativaB = pq.Questao.AlternativaB,
                    AlternativaC = pq.Questao.AlternativaC,
                    AlternativaD = pq.Questao.AlternativaD,
                    AlternativaE = pq.Questao.AlternativaE,
                    NumeroAlternativas = pq.Questao.NumeroAlternativas,
                    AlternativaCorreta = pq.Questao.AlternativaCorreta
                }).ToList()
        };

        return View(vm);
    }

    /// <summary>
    /// Exporta a matriz de respostas (0/1) em CSV (ALUNO;Q.01;Q.02;...).
    /// Liberado só quando todos os alunos finalizaram OU a janela da prova terminou.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> ExportarMatrizRespostas(Guid id)
    {
        var prova = await _context.Provas
            .Include(p => p.ProvaAlunos)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (prova == null) return NotFound();

        if (!PodeExportar(prova))
        {
            TempData["Erro"] = "A exportação libera quando todos os alunos finalizarem ou quando o prazo da prova terminar.";
            return RedirectToAction(nameof(DetalhesProva), new { id });
        }

        var matriz = await _exportacaoResultados.GerarMatrizCsvAsync(id);
        if (matriz == null) return NotFound();

        if (matriz.TotalQuestoes == 0 || matriz.TotalAlunos == 0)
        {
            TempData["Erro"] = "Não há dados para exportar.";
            return RedirectToAction(nameof(DetalhesProva), new { id });
        }

        return File(matriz.Conteudo, "text/csv", matriz.NomeArquivo);
    }

    /// <summary>
    /// Exporta o resultado legÃ­vel pela instituiÃ§Ã£o: uma linha por aluno,
    /// identificadores e ACERTO/ERRO/SEM_RESPOSTA para cada questÃ£o.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> ExportarResultadosCsv(Guid id)
    {
        var prova = await _context.Provas
            .Include(p => p.ProvaAlunos)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (prova == null) return NotFound();
        if (!PodeExportar(prova))
        {
            TempData["Erro"] = "A exportaÃ§Ã£o libera quando todos os alunos finalizarem ou quando o prazo da prova terminar.";
            return RedirectToAction(nameof(DetalhesProva), new { id });
        }

        var resultado = await _exportacaoResultados.GerarResultadosDetalhadosCsvAsync(id);
        if (resultado == null)
        {
            TempData["Erro"] = "NÃ£o hÃ¡ dados para exportar.";
            return RedirectToAction(nameof(DetalhesProva), new { id });
        }

        return File(resultado.Conteudo, "text/csv", resultado.NomeArquivo);
    }

    /// <summary>
    /// Exporta os resultados em planilha XLSX com respostas, acertos e score.
    /// Liberado só quando todos os alunos finalizaram OU a janela da prova terminou.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> ExportarResultadosXlsx(Guid id)
    {
        var prova = await _context.Provas
            .Include(p => p.ProvaAlunos)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (prova == null) return NotFound();

        if (!PodeExportar(prova))
        {
            TempData["Erro"] = "A exportação libera quando todos os alunos finalizarem ou quando o prazo da prova terminar.";
            return RedirectToAction(nameof(DetalhesProva), new { id });
        }

        var resultado = await _exportacaoXlsx.GerarAsync(id);
        if (resultado == null)
        {
            TempData["Erro"] = "Não há dados para exportar.";
            return RedirectToAction(nameof(DetalhesProva), new { id });
        }

        return File(resultado.Value.Conteudo, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", resultado.Value.NomeArquivo);
    }
}
