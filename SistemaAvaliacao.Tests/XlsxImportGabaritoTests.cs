using ClosedXML.Excel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SistemaAvaliacao.Web.Data;
using SistemaAvaliacao.Web.Services;
using Xunit;

namespace SistemaAvaliacao.Tests;

/// <summary>
/// Testes do preview de importação de questões por XLSX, focados em DETECTAR gabaritos
/// perigosos antes de gravar — em especial o gabarito que aponta para uma alternativa que a
/// questão não possui (ex.: 'E' numa questão de 4 alternativas), que corromperia a correção
/// de forma silenciosa (nenhum aluno conseguiria acertar).
///
/// O XLSX é construído em memória com ClosedXML, exatamente no formato que PrepararPreview
/// espera (aba "Itens" com as colunas obrigatórias).
/// </summary>
public class XlsxImportGabaritoTests
{
    private static ApplicationDbContext NovoContexto()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    // Um IWebHostEnvironment mínimo: só o WebRootPath é usado (para salvar imagens, que
    // estes testes não exercitam). Aponta para uma pasta temporária.
    private sealed class FakeEnv : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = Path.Combine(Path.GetTempPath(), "sa-tests-" + Guid.NewGuid().ToString("N"));
        public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = null!;
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
        public string EnvironmentName { get; set; } = "Test";
    }

    private static XlsxProvaImportService NovoServico()
    {
        var ctx = NovoContexto();
        return new XlsxProvaImportService(ctx, new FakeEnv(), NullLogger<XlsxProvaImportService>.Instance);
    }

    /// <summary>
    /// Gera um XLSX com uma aba "Itens" contendo UMA questão com as 4 primeiras alternativas
    /// preenchidas (A–D) e o gabarito informado.
    /// </summary>
    private static Stream GerarXlsxUmaQuestao(string gabarito, bool preencherAlternativaE)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Itens");

        var colunas = new[]
        {
            "numero_questao", "comando",
            "alternativa_a", "alternativa_b", "alternativa_c", "alternativa_d", "alternativa_e",
            "gabarito"
        };
        for (var c = 0; c < colunas.Length; c++)
            ws.Cell(1, c + 1).Value = colunas[c];

        ws.Cell(2, 1).Value = 1;                       // numero_questao
        ws.Cell(2, 2).Value = "Qual a resposta?";      // comando
        ws.Cell(2, 3).Value = "Primeira";              // alternativa_a
        ws.Cell(2, 4).Value = "Segunda";               // alternativa_b
        ws.Cell(2, 5).Value = "Terceira";              // alternativa_c
        ws.Cell(2, 6).Value = "Quarta";                // alternativa_d
        if (preencherAlternativaE)
            ws.Cell(2, 7).Value = "Quinta";            // alternativa_e
        ws.Cell(2, 8).Value = gabarito;                // gabarito

        var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;
        return ms;
    }

    [Fact]
    public void Gabarito_fora_do_range_e_sinalizado_como_problema()
    {
        // Questão com 4 alternativas (A–D), gabarito 'E' => aponta para alternativa inexistente.
        var servico = NovoServico();
        using var xlsx = GerarXlsxUmaQuestao(gabarito: "E", preencherAlternativaE: false);

        var (itens, _) = servico.PrepararPreview(xlsx);

        var item = Assert.Single(itens);
        Assert.Equal('E', item.AlternativaCorreta);
        Assert.Equal(4, item.NumeroAlternativas);
        Assert.Contains(item.Problemas, p => p.Contains("não existe"));
    }

    [Fact]
    public void Gabarito_dentro_do_range_nao_gera_problema_de_range()
    {
        // Questão com 4 alternativas (A–D), gabarito 'D' => válido.
        var servico = NovoServico();
        using var xlsx = GerarXlsxUmaQuestao(gabarito: "D", preencherAlternativaE: false);

        var (itens, _) = servico.PrepararPreview(xlsx);

        var item = Assert.Single(itens);
        Assert.Equal('D', item.AlternativaCorreta);
        Assert.DoesNotContain(item.Problemas, p => p.Contains("não existe"));
    }

    [Fact]
    public void Gabarito_E_e_valido_quando_a_questao_tem_5_alternativas()
    {
        // Agora a alternativa E está preenchida => gabarito 'E' cabe.
        var servico = NovoServico();
        using var xlsx = GerarXlsxUmaQuestao(gabarito: "E", preencherAlternativaE: true);

        var (itens, _) = servico.PrepararPreview(xlsx);

        var item = Assert.Single(itens);
        Assert.Equal('E', item.AlternativaCorreta);
        Assert.Equal(5, item.NumeroAlternativas);
        Assert.DoesNotContain(item.Problemas, p => p.Contains("não existe"));
    }
}
