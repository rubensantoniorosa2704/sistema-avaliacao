using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SistemaAvaliacao.Web.Models.ViewModels;
using SistemaAvaliacao.Web.Services;

namespace SistemaAvaliacao.Web.Controllers;

/// <summary>
/// Questões vinculadas a uma prova: CRUD manual, reordenação e importação via XLSX.
/// Mantém as URLs legadas sob /Admin/* via rota por atributo.
/// Lógica de negócio em <see cref="QuestaoProvaService"/> e <see cref="XlsxProvaImportService"/>;
/// este controller orquestra HTTP (ModelState, upload de imagem, TempData, views, redirects).
/// </summary>
[Authorize(Roles = "Admin")]
[Route("Admin/[action]")]
public class QuestoesController(
    QuestaoProvaService questoes,
    ProvaService provas,
    ImageUploadService imageUpload,
    XlsxProvaImportService xlsxImport) : Controller
{
    private const string QuestoesView = "~/Views/Admin/QuestoesProva.cshtml";
    private const string ImportarView = "~/Views/Admin/ImportarQuestoesXlsx.cshtml";

    private IActionResult VoltarParaQuestoes(Guid provaId)
        => RedirectToAction(nameof(QuestoesProva), new { id = provaId });

    [HttpGet]
    public async Task<IActionResult> QuestoesProva(Guid id)
    {
        var vm = await questoes.ObterTelaAsync(id);
        if (vm is null) return NotFound();
        return View(QuestoesView, vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SalvarQuestaoProva(SalvarQuestaoProvaViewModel model)
    {
        if (await provas.ProvaBloqueadaAsync(model.ProvaId))
        {
            TempData["Erro"] = "A prova já foi iniciada e não pode mais ser editada.";
            return VoltarParaQuestoes(model.ProvaId);
        }

        if (!ModelState.IsValid)
        {
            TempData["Erro"] = "Preencha todos os campos obrigatórios.";
            return VoltarParaQuestoes(model.ProvaId);
        }

        // Upload de imagem é concern de HTTP: resolve a URL aqui e repassa ao serviço.
        string? imagemUrl = null;
        if (model.Imagem is { Length: > 0 })
        {
            var (ok, caminho, erro) = await imageUpload.UploadImageAsync(model.Imagem);
            if (!ok)
            {
                TempData["Erro"] = erro ?? "Falha ao enviar a imagem.";
                return VoltarParaQuestoes(model.ProvaId);
            }
            imagemUrl = caminho;
        }

        var (resultado, ordem) = await questoes.SalvarAsync(model, imagemUrl);
        switch (resultado)
        {
            case QuestaoProvaService.SalvarResultado.ProvaNaoEncontrada:
            case QuestaoProvaService.SalvarResultado.QuestaoNaoEncontrada:
                return NotFound();
            case QuestaoProvaService.SalvarResultado.Atualizada:
                TempData["Sucesso"] = "Questão atualizada com sucesso!";
                break;
            default:
                TempData["Sucesso"] = $"Questão {ordem} adicionada com sucesso!";
                break;
        }

        return VoltarParaQuestoes(model.ProvaId);
    }

    [HttpGet]
    public async Task<IActionResult> ObterQuestao(Guid id)
    {
        var questao = await questoes.ObterQuestaoAsync(id);
        if (questao is null) return NotFound();

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
        if (await provas.ProvaBloqueadaAsync(provaId))
        {
            TempData["Erro"] = "A prova já foi iniciada e não pode mais ser editada.";
            return VoltarParaQuestoes(provaId);
        }

        var removida = await questoes.ExcluirAsync(provaId, questaoId);
        if (!removida) return NotFound();

        TempData["Sucesso"] = "Questão excluída com sucesso!";
        return VoltarParaQuestoes(provaId);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ReordenarQuestaoProva(Guid provaId, Guid questaoId, string direcao)
    {
        if (await provas.ProvaBloqueadaAsync(provaId))
        {
            TempData["Erro"] = "A prova já foi iniciada e não pode mais ser editada.";
            return VoltarParaQuestoes(provaId);
        }

        var resultado = await questoes.ReordenarAsync(provaId, questaoId, direcao);
        if (resultado == QuestaoProvaService.ReordenarResultado.NaoEncontrada)
            return NotFound();

        return VoltarParaQuestoes(provaId);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ImportarQuestoesXlsx(Guid provaId, IFormFile? arquivoXlsx)
    {
        var prova = await provas.ObterDetalhesAsync(provaId);
        if (prova is null) return NotFound();

        if (await provas.ProvaBloqueadaAsync(provaId))
        {
            TempData["Erro"] = "A prova já foi iniciada e não pode mais receber questões.";
            return VoltarParaQuestoes(provaId);
        }

        if (arquivoXlsx is null || arquivoXlsx.Length == 0)
        {
            TempData["Erro"] = "Selecione a planilha (.xlsx) com as questões para importar.";
            return VoltarParaQuestoes(provaId);
        }

        if (!Path.GetExtension(arquivoXlsx.FileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            TempData["Erro"] = "Apenas arquivos .xlsx são aceitos.";
            return VoltarParaQuestoes(provaId);
        }

        List<ImportarQuestaoItem> itens;
        List<string> avisos;
        try
        {
            using var stream = arquivoXlsx.OpenReadStream();
            (itens, avisos) = xlsxImport.PrepararPreview(stream);
        }
        catch (Exception ex)
        {
            TempData["Erro"] = $"Falha ao ler a planilha: {ex.Message}";
            return VoltarParaQuestoes(provaId);
        }

        return View(ImportarView, new ImportarQuestoesXlsxViewModel
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
        var prova = await provas.ObterDetalhesAsync(model.ProvaId);
        if (prova is null) return NotFound();

        if (await provas.ProvaBloqueadaAsync(model.ProvaId))
        {
            TempData["Erro"] = "A prova já foi iniciada e não pode mais receber questões.";
            return VoltarParaQuestoes(model.ProvaId);
        }

        var itensParaGravar = model.Itens.Where(i => i.Incluir).ToList();

        if (itensParaGravar.Count == 0)
        {
            TempData["Erro"] = "Nenhuma questão selecionada para importação.";
            return VoltarParaQuestoes(model.ProvaId);
        }

        var semGabarito = itensParaGravar.Count(i => i.AlternativaCorreta == null);
        var semComando = itensParaGravar.Count(i => string.IsNullOrWhiteSpace(i.Comando));
        var semAlternativasBasicas = itensParaGravar.Count(i =>
            string.IsNullOrWhiteSpace(i.AlternativaA) || string.IsNullOrWhiteSpace(i.AlternativaB));
        // Gabarito que aponta para uma alternativa inexistente (ex.: 'E' numa questão de 4):
        // nenhum aluno acertaria e a correção penalizaria todos. Bloqueia antes de gravar.
        var gabaritoForaDoRange = itensParaGravar.Count(i =>
            i.AlternativaCorreta != null &&
            i.AlternativaCorreta.Value > (char)('A' + Math.Clamp(i.NumeroAlternativas, 2, 5) - 1));

        if (semGabarito > 0 || semComando > 0 || semAlternativasBasicas > 0 || gabaritoForaDoRange > 0)
        {
            var partes = new List<string>();
            if (semGabarito > 0) partes.Add($"{semGabarito} sem alternativa correta");
            if (semComando > 0) partes.Add($"{semComando} sem comando");
            if (semAlternativasBasicas > 0) partes.Add($"{semAlternativasBasicas} sem alternativa A/B");
            if (gabaritoForaDoRange > 0) partes.Add($"{gabaritoForaDoRange} com gabarito apontando para alternativa inexistente");

            TempData["Erro"] = $"Revise antes de confirmar: {string.Join("; ", partes)}.";
            model.NomeProva = prova.Nome;
            return View(ImportarView, model);
        }

        var inseridas = await xlsxImport.ConfirmarAsync(model.ProvaId, itensParaGravar);

        TempData["Sucesso"] = $"{inseridas} questão(ões) importada(s) com sucesso!";
        return VoltarParaQuestoes(model.ProvaId);
    }
}
