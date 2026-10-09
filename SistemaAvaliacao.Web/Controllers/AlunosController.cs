using System.Linq;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SistemaAvaliacao.Web.Services;

namespace SistemaAvaliacao.Web.Controllers;

/// <summary>
/// Alunos vinculados a uma prova: importação via CSV, remoção individual e modelo de CSV.
/// Mantém as URLs legadas sob /Admin/* via rota por atributo.
/// Regras e acesso a dados em <see cref="AlunoProvaService"/> e <see cref="ImportacaoAlunosService"/>;
/// este controller orquestra HTTP (validação de arquivo, TempData, redirects).
/// </summary>
[Authorize(Roles = "Admin")]
[Route("Admin/[action]")]
public class AlunosController(
    AlunoProvaService alunos,
    ImportacaoAlunosService importacaoAlunos) : Controller
{
    private RedirectToActionResult VoltarParaDetalhes(Guid provaId)
        => RedirectToAction("DetalhesProva", "Provas", new { id = provaId });

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ImportarAlunosProva(Guid provaId, IFormFile? arquivoCsv)
    {
        if (arquivoCsv is null || arquivoCsv.Length == 0)
        {
            TempData["Erro"] = "Selecione um arquivo CSV.";
            return VoltarParaDetalhes(provaId);
        }

        if (!Path.GetExtension(arquivoCsv.FileName).Equals(".csv", StringComparison.OrdinalIgnoreCase))
        {
            TempData["Erro"] = "Envie um arquivo no formato CSV (.csv).";
            return VoltarParaDetalhes(provaId);
        }

        if (arquivoCsv.Length > 5 * 1024 * 1024)
        {
            TempData["Erro"] = "O arquivo CSV deve ter no máximo 5 MB.";
            return VoltarParaDetalhes(provaId);
        }

        var prova = await alunos.ObterProvaParaImportacaoAsync(provaId);
        if (prova is null) return NotFound();

        if (ProvaService.EstaBloqueada(prova))
        {
            TempData["Erro"] = "A prova já foi iniciada e a lista de alunos não pode mais ser alterada.";
            return VoltarParaDetalhes(provaId);
        }

        using var stream = arquivoCsv.OpenReadStream();
        var resultado = await importacaoAlunos.ImportarAsync(prova, stream);

        TempData["Sucesso"] = resultado.Resumo();

        if (resultado.Erros.Any())
            TempData["Erro"] = $"{resultado.Erros.Count} linha(s) com erro: {string.Join(" | ", resultado.Erros.Take(5))}";

        return VoltarParaDetalhes(provaId);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoverAlunoProva(Guid provaId, Guid alunoId)
    {
        var resultado = await alunos.RemoverAsync(provaId, alunoId);
        switch (resultado)
        {
            case AlunoProvaService.RemocaoResultado.NaoEncontrado:
                return NotFound();
            case AlunoProvaService.RemocaoResultado.JaIniciou:
                TempData["Erro"] = "Não é possível remover um aluno que já iniciou a prova.";
                break;
            default:
                TempData["Sucesso"] = "Aluno removido da prova.";
                break;
        }
        return VoltarParaDetalhes(provaId);
    }

    [HttpGet]
    public IActionResult BaixarModeloCsv()
    {
        var conteudo = "Nome;Matricula;Email;CPF\nJoão da Silva;2024001;joao@email.com;12345678901\nMaria Souza;2024002;maria@email.com;98765432100\n";
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(conteudo)).ToArray();
        return File(bytes, "text/csv", "modelo-alunos.csv");
    }
}
