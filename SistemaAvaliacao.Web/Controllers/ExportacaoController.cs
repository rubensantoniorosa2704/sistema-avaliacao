using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SistemaAvaliacao.Web.Services;

namespace SistemaAvaliacao.Web.Controllers;

/// <summary>
/// Exportação de resultados da prova (matriz 0/1 CSV, resultados detalhados CSV e XLSX).
/// Mantém as URLs legadas sob /Admin/* via rota por atributo.
/// A geração dos arquivos fica em <see cref="ExportacaoResultadosService"/> e
/// <see cref="ExportacaoXlsxService"/>; o gate de liberação em <see cref="ProvaService"/>.
/// </summary>
[Authorize(Roles = "Admin")]
[Route("Admin/[action]")]
public class ExportacaoController(
    ProvaService provas,
    ExportacaoResultadosService exportacaoResultados,
    ExportacaoXlsxService exportacaoXlsx) : Controller
{
    private const string _msgBloqueada =
        "A exportação libera quando todos os alunos finalizarem ou quando o prazo da prova terminar.";
    private const string _msgSemDados = "Não há dados para exportar.";

    private IActionResult VoltarParaDetalhes(Guid id)
        => RedirectToAction("DetalhesProva", "Provas", new { id });

    /// <summary>
    /// Verifica o gate de liberação. Retorna null quando liberada; caso contrário,
    /// devolve o IActionResult apropriado (NotFound ou redirect com mensagem).
    /// </summary>
    private async Task<IActionResult?> ChecarLiberacaoAsync(Guid id)
    {
        var liberacao = await provas.VerificarLiberacaoExportacaoAsync(id);
        return liberacao switch
        {
            ProvaService.ExportacaoLiberacao.ProvaNaoEncontrada => NotFound(),
            ProvaService.ExportacaoLiberacao.Bloqueada => BloquearComMensagem(id),
            _ => null
        };
    }

    private IActionResult BloquearComMensagem(Guid id)
    {
        TempData["Erro"] = _msgBloqueada;
        return VoltarParaDetalhes(id);
    }

    [HttpGet]
    public async Task<IActionResult> ExportarMatrizRespostas(Guid id)
    {
        if (await ChecarLiberacaoAsync(id) is { } bloqueio) return bloqueio;

        var matriz = await exportacaoResultados.GerarMatrizCsvAsync(id);
        if (matriz is null) return NotFound();

        if (matriz.TotalQuestoes == 0 || matriz.TotalAlunos == 0)
        {
            TempData["Erro"] = _msgSemDados;
            return VoltarParaDetalhes(id);
        }

        return File(matriz.Conteudo, "text/csv", matriz.NomeArquivo);
    }

    [HttpGet]
    public async Task<IActionResult> ExportarResultadosCsv(Guid id)
    {
        if (await ChecarLiberacaoAsync(id) is { } bloqueio) return bloqueio;

        var resultado = await exportacaoResultados.GerarResultadosDetalhadosCsvAsync(id);
        if (resultado is null)
        {
            TempData["Erro"] = _msgSemDados;
            return VoltarParaDetalhes(id);
        }

        return File(resultado.Conteudo, "text/csv", resultado.NomeArquivo);
    }

    [HttpGet]
    public async Task<IActionResult> ExportarResultadosXlsx(Guid id)
    {
        if (await ChecarLiberacaoAsync(id) is { } bloqueio) return bloqueio;

        var resultado = await exportacaoXlsx.GerarAsync(id);
        if (resultado is null)
        {
            TempData["Erro"] = _msgSemDados;
            return VoltarParaDetalhes(id);
        }

        return File(resultado.Value.Conteudo,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            resultado.Value.NomeArquivo);
    }
}
