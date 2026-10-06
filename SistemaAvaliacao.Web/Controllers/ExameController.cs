using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SistemaAvaliacao.Web.Data;
using SistemaAvaliacao.Web.Models.Entities;
using SistemaAvaliacao.Web.Models.Enums;
using SistemaAvaliacao.Web.Models.ViewModels;
using SistemaAvaliacao.Web.Services;

namespace SistemaAvaliacao.Web.Controllers;

/// <summary>
/// Fluxo público e anônimo de prova. Aluno acessa via `/p/{token}`,
/// identifica-se (email + CPF), e responde ao exame.
/// </summary>
[Route("")]
public class ExameController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly TentativaExameService _tentativas;
    private readonly QuestoesProvaCache _questoesCache;

    public ExameController(
        ApplicationDbContext context,
        TentativaExameService tentativas,
        QuestoesProvaCache questoesCache)
    {
        _context = context;
        _tentativas = tentativas;
        _questoesCache = questoesCache;
    }

    private static QuestaoParaResponderItem MapearItem(QuestaoExibicao q, char? respostaMarcada) => new()
    {
        QuestaoId = q.QuestaoId,
        Ordem = q.Ordem,
        TextoBase = q.TextoBase,
        Enunciado = q.Enunciado,
        ImagemUrl = q.ImagemUrl,
        DescricaoAcessibilidade = q.DescricaoAcessibilidade,
        Comando = q.Comando,
        RespostaMarcada = respostaMarcada,
        Alternativas = q.Alternativas.Select(a => new AlternativaItem { Letra = a.Letra, Texto = a.Texto }).ToList()
    };

    /// <summary>
    /// Tela de identificação (email + CPF) para acessar a prova.
    /// GET /prova/{token}
    /// </summary>
    [HttpGet("prova/{token}")]
    public async Task<IActionResult> Identificar(Guid token)
    {
        var provaEncontrada = await _context.Provas
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.TokenPublico == token);
        
        if (provaEncontrada == null) return NotFound();

        // Provas físicas não têm link público — alunos respondem via folha impressa.
        if (provaEncontrada.TipoAplicacao == TipoAplicacao.Fisica) return NotFound();

        // Validar se a prova está na janela (UTC).
        var agora = DateTime.UtcNow;
        if (agora < provaEncontrada.DataInicio || agora > provaEncontrada.DataFim)
        {
            return View("IdentificacaoFora", new IdentificacaoViewModel
            {
                Token = token,
                NomeProva = provaEncontrada.Nome,
                Status = agora < provaEncontrada.DataInicio ? "AgendadaPara" : "Encerrada",
                DataInicio = provaEncontrada.DataInicio,
                DataFim = provaEncontrada.DataFim
            });
        }

        return View("Identificacao", new IdentificacaoViewModel
        {
            Token = token,
            NomeProva = provaEncontrada.Nome,
            Descricao = provaEncontrada.Descricao
        });
    }

    /// <summary>
    /// POST: identifica o aluno (email + CPF), busca no CSV, gera TokenTentativa.
    /// POST /prova/{token}
    /// </summary>
    [HttpPost("prova/{token}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Identificar(Guid token, IdentificacaoViewModel model)
    {
        var prova = await _context.Provas
            .FirstOrDefaultAsync(p => p.TokenPublico == token);

        if (prova == null) return NotFound();

        // Provas físicas não têm link público.
        if (prova.TipoAplicacao == TipoAplicacao.Fisica) return NotFound();

        var agora = DateTime.UtcNow;
        if (agora < prova.DataInicio || agora > prova.DataFim)
        {
            return View("IdentificacaoFora", new IdentificacaoViewModel
            {
                Token = token,
                NomeProva = prova.Nome,
                Status = agora < prova.DataInicio ? "AgendadaPara" : "Encerrada",
                DataInicio = prova.DataInicio,
                DataFim = prova.DataFim
            });
        }

        if (!ModelState.IsValid)
        {
            return View("Identificacao", model);
        }

        var email = model.EmailForm?.Trim().ToLower() ?? "";
        var cpf = new string(model.CPF?.Where(char.IsDigit).ToArray() ?? Array.Empty<char>());

        var provaAluno = await _context.ProvaAlunos
            .Include(pa => pa.Aluno)
            .FirstOrDefaultAsync(pa =>
                pa.ProvaId == prova.Id &&
                pa.Aluno.Email.ToLower() == email &&
                pa.Aluno.CPF == cpf);

        if (provaAluno == null)
        {
            var possuiAlunos = await _context.ProvaAlunos.AnyAsync(pa => pa.ProvaId == prova.Id);
            ModelState.AddModelError("", possuiAlunos
                ? "E-mail ou CPF não encontrados para esta prova."
                : "Nenhum aluno foi importado para esta prova.");
            return View("Identificacao", model);
        }

        // Se já iniciou, não deixa recomeçar (one-shot).
        if (provaAluno.IniciadaEm.HasValue)
        {
            return View("IdentificacaoJaIniciada", new IdentificacaoViewModel
            {
                Token = token,
                NomeProva = prova.Nome,
                Email = email
            });
        }

        // Gerar TokenTentativa e redirecionar para a prova.
        provaAluno.TokenTentativa = Guid.NewGuid();
        provaAluno.Status = StatusProvaAluno.EmAndamento;
        provaAluno.IniciadaEm = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return RedirectToAction(nameof(Responder), new { token, tentativa = provaAluno.TokenTentativa });
    }

    /// <summary>
    /// Tela de resposta da prova. Valida o TokenTentativa.
    /// GET /prova/{token}/responder?tentativa={tokenTentativa}
    /// </summary>
    [HttpGet("prova/{token}/responder")]
    public async Task<IActionResult> Responder(Guid token, Guid tentativa)
    {
        // Conteúdo das questões vem do cache; aqui só precisamos da prova e das respostas já dadas.
        var prova = await _context.Provas
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.TokenPublico == token);

        if (prova == null) return NotFound();

        var provaAluno = await _context.ProvaAlunos
            .AsNoTracking()
            .Include(pa => pa.Respostas)
            .FirstOrDefaultAsync(pa =>
                pa.ProvaId == prova.Id &&
                pa.TokenTentativa == tentativa &&
                pa.Status == StatusProvaAluno.EmAndamento);

        if (provaAluno == null)
        {
            return View("ResponderInvalido");
        }

        // Validar se a prova ainda está na janela (UTC).
        var agora = DateTime.UtcNow;
        if (agora > prova.DataFim)
        {
            return View("ResponderEncerrada", new ResponderProvaViewModel
            {
                Token = token,
                NomeProva = prova.Nome
            });
        }

        var respostasPorQuestao = provaAluno.Respostas
            .ToDictionary(r => r.QuestaoId, r => r.AlternativaMarcada);

        // Conteúdo das questões vem do cache (sem tocar o banco por navegação).
        var questoes = await _questoesCache.ObterAsync(prova.Id);

        var navegacao = questoes
            .Select(q => new NavegacaoQuestaoItem
            {
                QuestaoId = q.QuestaoId,
                Ordem = q.Ordem,
                Respondida = respostasPorQuestao.TryGetValue(q.QuestaoId, out var a) && a.HasValue
            })
            .ToList();

        var primeira = questoes.FirstOrDefault(q => q.Ordem == 1) ?? questoes.FirstOrDefault();

        return View("Responder", new ResponderProvaViewModel
        {
            Token = token,
            TokenTentativa = tentativa,
            ProvaId = prova.Id,
            AlunoId = provaAluno.AlunoId,
            NomeProva = prova.Nome,
            Descricao = prova.Descricao,
            DataFim = prova.DataFim,
            Navegacao = navegacao,
            PrimeiraQuestao = primeira is null
                ? null
                : MapearItem(primeira, respostasPorQuestao.TryGetValue(primeira.QuestaoId, out var am) ? am : null)
        });
    }

    /// <summary>
    /// Devolve o fragmento HTML de uma questão (lazy loading).
    /// GET /prova/{token}/questao/{ordem}?tentativa={tokenTentativa}
    /// </summary>
    [HttpGet("prova/{token}/questao/{ordem:int}")]
    public async Task<IActionResult> Questao(Guid token, int ordem, Guid tentativa)
    {
        var prova = await _context.Provas
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.TokenPublico == token);
        if (prova == null) return NotFound();

        // A tentativa precisa estar válida e em andamento (mesma regra do SalvarResposta).
        var provaAluno = await _context.ProvaAlunos
            .AsNoTracking()
            .FirstOrDefaultAsync(pa =>
                pa.ProvaId == prova.Id &&
                pa.TokenTentativa == tentativa &&
                pa.Status == StatusProvaAluno.EmAndamento);
        if (provaAluno == null) return NotFound();

        if (DateTime.UtcNow > prova.DataFim) return NotFound();

        var questao = await _questoesCache.ObterPorOrdemAsync(prova.Id, ordem);
        if (questao is null) return NotFound();

        // Resposta já marcada por este aluno nesta questão (para pré-selecionar o rádio).
        var marcada = await _context.Respostas
            .AsNoTracking()
            .Where(r => r.ProvaId == prova.Id && r.AlunoId == provaAluno.AlunoId && r.QuestaoId == questao.QuestaoId)
            .Select(r => r.AlternativaMarcada)
            .FirstOrDefaultAsync();

        ViewData["TotalQuestoes"] = (await _questoesCache.ObterAsync(prova.Id)).Count;
        return PartialView("_Questao", MapearItem(questao, marcada));
    }

    /// <summary>
    /// Salvar resposta individual (AJAX). Valida TokenTentativa.
    /// POST /prova/{token}/salvar-resposta
    /// </summary>
    [HttpPost("prova/{token}/salvar-resposta")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SalvarResposta(Guid token, Guid questaoId, char alternativa, Guid tentativa)
    {
        var prova = await _context.Provas
            .Include(p => p.ProvaQuestoes)
            .FirstOrDefaultAsync(p => p.TokenPublico == token);
        if (prova == null) return Json(new { sucesso = false, erro = "Prova não encontrada" });

        var provaAluno = await _context.ProvaAlunos
            .Include(pa => pa.Respostas)
            .FirstOrDefaultAsync(pa =>
                pa.ProvaId == prova.Id &&
                pa.TokenTentativa == tentativa &&
                pa.Status == StatusProvaAluno.EmAndamento);

        if (provaAluno == null)
            return Json(new { sucesso = false, erro = "Tentativa inválida" });

        var agora = DateTime.UtcNow;
        if (agora > prova.DataFim)
            return Json(new { sucesso = false, erro = "Prova encerrada" });

        var questao = await _context.Questoes.FindAsync(questaoId);
        if (questao == null || !prova.ProvaQuestoes.Any(pq => pq.QuestaoId == questaoId))
            return Json(new { sucesso = false, erro = "Questão inválida" });

        alternativa = char.ToUpperInvariant(alternativa);
        var letraMax = (char)('A' + questao.NumeroAlternativas - 1);
        if (alternativa < 'A' || alternativa > letraMax)
            return Json(new { sucesso = false, erro = "Alternativa inválida" });

        var respostaExistente = provaAluno.Respostas.FirstOrDefault(r => r.QuestaoId == questaoId);
        if (respostaExistente != null)
        {
            respostaExistente.AlternativaMarcada = alternativa;
        }
        else
        {
            _context.Respostas.Add(new Resposta
            {
                ProvaId = prova.Id,
                AlunoId = provaAluno.AlunoId,
                QuestaoId = questaoId,
                AlternativaMarcada = alternativa,
                CreatedAt = DateTime.UtcNow
            });
        }

        await _context.SaveChangesAsync();
        var totalRespondidas = await _context.Respostas.CountAsync(r =>
            r.ProvaId == prova.Id && r.AlunoId == provaAluno.AlunoId);

        return Json(new
        {
            sucesso = true,
            totalRespondidas
        });
    }

    [HttpPost("prova/{token}/limpar-resposta")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LimparResposta(Guid token, Guid questaoId, Guid tentativa)
    {
        var prova = await _context.Provas
            .Include(p => p.ProvaQuestoes)
            .FirstOrDefaultAsync(p => p.TokenPublico == token);

        if (prova == null)
            return Json(new { sucesso = false, erro = "Prova não encontrada" });

        var provaAluno = await _context.ProvaAlunos
            .FirstOrDefaultAsync(pa =>
                pa.ProvaId == prova.Id &&
                pa.TokenTentativa == tentativa &&
                pa.Status == StatusProvaAluno.EmAndamento);

        if (provaAluno == null)
            return Json(new { sucesso = false, erro = "Tentativa inválida" });

        if (DateTime.UtcNow > prova.DataFim)
            return Json(new { sucesso = false, expirado = true, erro = "Prova encerrada" });

        if (!prova.ProvaQuestoes.Any(pq => pq.QuestaoId == questaoId))
            return Json(new { sucesso = false, erro = "Questão inválida" });

        var resposta = await _context.Respostas.FirstOrDefaultAsync(r =>
            r.ProvaId == prova.Id &&
            r.AlunoId == provaAluno.AlunoId &&
            r.QuestaoId == questaoId);

        if (resposta != null)
        {
            _context.Respostas.Remove(resposta);
            await _context.SaveChangesAsync();
        }

        var totalRespondidas = await _context.Respostas.CountAsync(r =>
            r.ProvaId == prova.Id && r.AlunoId == provaAluno.AlunoId);

        return Json(new { sucesso = true, totalRespondidas });
    }

    /// <summary>
    /// Finalizar prova: valida TokenTentativa, marca como Finalizada, grava Respostas.
    /// POST /prova/{token}/finalizar
    /// </summary>
    [HttpPost("prova/{token}/finalizar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Finalizar(Guid token, Guid tentativa)
    {
        var prova = await _context.Provas
            .FirstOrDefaultAsync(p => p.TokenPublico == token);

        if (prova == null) return Json(new { sucesso = false, erro = "Prova não encontrada" });

        var provaAluno = await _context.ProvaAlunos
            .FirstOrDefaultAsync(pa =>
                pa.ProvaId == prova.Id &&
                pa.TokenTentativa == tentativa &&
                pa.Status == StatusProvaAluno.EmAndamento);

        if (provaAluno == null)
            return Json(new { sucesso = false, erro = "Tentativa inválida" });

        // Mesmo fora do prazo a tentativa é fechada (com o horário do DataFim): as respostas
        // já salvas valem e o aluno segue para a tela de confirmação, em vez de ver um erro
        // quando o cronômetro do navegador zera um pouco antes do relógio do servidor.
        var expirada = await _tentativas.FinalizarAsync(provaAluno, prova);

        return Json(new { sucesso = true, expirada });
    }

    /// <summary>
    /// Tela de confirmação exibida após o aluno finalizar a prova virtual.
    /// GET /prova/obrigado
    /// </summary>
    [HttpGet("prova/obrigado")]
    public IActionResult Obrigado() => View();

}
