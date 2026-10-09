using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using SistemaAvaliacao.Web.Models.ViewModels;
using SistemaAvaliacao.Web.Services;

namespace SistemaAvaliacao.Web.Controllers;

/// <summary>
/// CRUD de provas (área administrativa). Mantém as URLs legadas sob /Admin/*
/// via rota por atributo, de modo que links e redirects existentes continuem válidos.
/// A lógica de negócio e o acesso a dados ficam em <see cref="ProvaService"/>;
/// este controller apenas orquestra HTTP (validação de ModelState, views, redirects, TempData).
/// </summary>
[Authorize(Roles = "Admin")]
[Route("Admin/[action]")]
public class ProvasController(ProvaService provas, TentativaExameService tentativas) : Controller
{
    private Guid GetCurrentUserId()
    {
        var valor = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(valor) || !Guid.TryParse(valor, out var id))
            throw new InvalidOperationException("Usuário autenticado sem identificador válido.");
        return id;
    }

    [HttpGet]
    public async Task<IActionResult> Provas(string? buscaNome)
    {
        var lista = await provas.ListarAsync(buscaNome);
        return View("~/Views/Admin/Provas.cshtml",
            new ListarProvasViewModel { Provas = lista, BuscaNome = buscaNome });
    }

    [HttpGet]
    public IActionResult CriarProva() => View("~/Views/Admin/CriarProva.cshtml", new CriarProvaViewModel());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CriarProva(CriarProvaViewModel model)
    {
        if (!ModelState.IsValid) return View("~/Views/Admin/CriarProva.cshtml", model);

        var (ok, prova, erro) = await provas.CriarAsync(model, GetCurrentUserId());
        if (!ok)
        {
            ModelState.AddModelError("DataFim", erro!);
            return View("~/Views/Admin/CriarProva.cshtml", model);
        }

        TempData["Sucesso"] = "Prova criada! Agora importe o caderno e o gabarito, ou adicione questões.";
        return RedirectToAction("QuestoesProva", "Questoes", new { id = prova!.Id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> EditarProvaModal(EditarProvaViewModel model)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));

        var (resultado, prova, erro) = await provas.EditarAsync(model);
        return resultado switch
        {
            ProvaService.EdicaoResultado.DataInvalida => BadRequest(new[] { erro! }),
            ProvaService.EdicaoResultado.NaoEncontrada => NotFound(),
            ProvaService.EdicaoResultado.Bloqueada => BadRequest(new[] { erro! }),
            _ => Ok(new { nome = prova!.Nome, descricao = prova.Descricao, instituicao = prova.Instituicao })
        };
    }

    [HttpGet]
    public async Task<IActionResult> DetalhesProva(Guid id)
    {
        // Se o prazo já terminou, consolida tentativas abandonadas antes de ler os dados.
        await tentativas.ConsolidarSeEncerradaAsync(id);

        var prova = await provas.ObterDetalhesAsync(id);
        if (prova is null) return NotFound();

        var linkPublico = Url.Action("Identificar", "Exame",
            new { token = prova.TokenPublico }, Request.Scheme) ?? $"/prova/{prova.TokenPublico}";

        return View("~/Views/Admin/DetalhesProva.cshtml", new DetalhesProvaViewModel
        {
            Id = prova.Id,
            Nome = prova.Nome,
            Descricao = prova.Descricao,
            Instituicao = prova.Instituicao,
            DataInicio = prova.DataInicio,
            DataFim = prova.DataFim,
            CreatedAt = prova.CreatedAt,
            TipoAplicacao = prova.TipoAplicacao,
            NomeCriador = prova.CriadoPor.Nome,
            LinkPublico = linkPublico,
            Bloqueada = ProvaService.EstaBloqueada(prova),
            PodeExportar = ProvaService.PodeExportar(prova),
            NumeroAlternativas = prova.NumeroAlternativas,
            Questoes = prova.ProvaQuestoes.Select(_ => new QuestaoResumoItem()).ToList(),
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
        var resultado = await provas.ExcluirAsync(id);
        switch (resultado)
        {
            case ProvaService.ExclusaoResultado.NaoEncontrada:
                return NotFound();
            case ProvaService.ExclusaoResultado.Bloqueada:
                TempData["Erro"] = "Não é possível excluir uma prova já iniciada por um aluno.";
                break;
            default:
                TempData["Sucesso"] = "Prova excluída com sucesso!";
                break;
        }
        return RedirectToAction(nameof(Provas));
    }
}
