using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SistemaAvaliacao.Web.Models.ViewModels;
using SistemaAvaliacao.Web.Services;

namespace SistemaAvaliacao.Web.Controllers;

/// <summary>
/// Geração de folhas-resposta (OMR) e impressão da prova física.
/// Mantém as URLs legadas sob /Admin/* via rota por atributo.
/// Geração de PDF em <see cref="FolhaRespostaPdfService"/>; consulta/regra da tela
/// de impressão em <see cref="FolhaProvaService"/>. Este controller orquestra HTTP.
/// </summary>
[Authorize(Roles = "Admin")]
[Route("Admin/[action]")]
public class FolhasController(
    FolhaRespostaPdfService folhaRespostaPdf,
    FolhaProvaService folhas) : Controller
{
    private const string ImprimirView = "~/Views/Admin/ImprimirProva.cshtml";

    private IActionResult VoltarParaDetalhes(Guid id)
        => RedirectToAction("DetalhesProva", "Provas", new { id });

    [HttpGet]
    public async Task<IActionResult> GerarFolhasResposta(Guid id)
    {
        try
        {
            var resultado = await folhaRespostaPdf.GerarAsync(id);
            return File(resultado.Pdf, "application/pdf", resultado.NomeArquivo);
        }
        catch (InvalidOperationException ex)
        {
            TempData["Erro"] = ex.Message;
            return VoltarParaDetalhes(id);
        }
    }

    [HttpGet]
    public async Task<IActionResult> GerarFolhaTesteOmr(Guid id)
    {
        try
        {
            var resultado = await folhaRespostaPdf.GerarTesteAsync(id);
            return File(resultado.Pdf, "application/pdf", resultado.NomeArquivo);
        }
        catch (InvalidOperationException ex)
        {
            TempData["Erro"] = ex.Message;
            return VoltarParaDetalhes(id);
        }
    }

    [HttpGet]
    public async Task<IActionResult> ImprimirProva(Guid id)
    {
        var (resultado, prova) = await folhas.ObterImpressaoAsync(id);

        switch (resultado)
        {
            case FolhaProvaService.ImpressaoResultado.NaoEncontrada:
                return NotFound();
            case FolhaProvaService.ImpressaoResultado.NaoEhFisica:
                return VoltarParaDetalhes(id);
            case FolhaProvaService.ImpressaoResultado.SemQuestoes:
                TempData["Erro"] = "A prova não possui questões para imprimir.";
                return VoltarParaDetalhes(id);
        }

        var vm = new ImprimirProvaViewModel
        {
            ProvaId = prova!.Id,
            NomeProva = prova.Nome,
            Descricao = prova.Descricao,
            NumeroAlunos = prova.ProvaAlunos.Count,
            Questoes = prova.ProvaQuestoes
                .Select(pq => new QuestaoNaProvaItem
                {
                    QuestaoId = pq.QuestaoId,
                    Ordem = pq.Ordem,
                    Enunciado = pq.Questao.Enunciado,
                    TextoBase = pq.Questao.TextoBase,
                    Comando = pq.Questao.Comando,
                    ImagemUrl = pq.Questao.ImagemUrl,
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

        return View(ImprimirView, vm);
    }
}
