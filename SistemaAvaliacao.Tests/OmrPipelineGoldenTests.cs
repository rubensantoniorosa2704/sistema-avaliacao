using OpenCvSharp;
using SistemaAvaliacao.Web.Services.Omr;
using Xunit;

namespace SistemaAvaliacao.Tests;

/// <summary>
/// Teste de OURO do pipeline OMR: fecha o ciclo geração → leitura → classificação
/// usando a MESMA geometria (SheetLayout) nas duas pontas, inteiramente em memória
/// (sem PDF/PDFium — gera o Mat direto do CardGenerator).
///
/// Por que importa: os limiares do AnswerClassifier (EmptyCeiling/StrongFill/
/// SecondRelativeToTop) são o coração da correção que decide nota de aluno. Sem este
/// teste, mexer num limiar poderia quebrar a detecção sem ninguém perceber até uma
/// prova real sair errada. Aqui, CardGenerator.GenerateTestCard pinta um padrão
/// conhecido e verificamos que o leitor o classifica como esperado:
///   - Questões 1–10 : marcação simples e forte  → Valida
///   - Questões 11–20: duas bolhas (A e B)        → Dupla
///   - Questões 21–30: rabisco concentrado        → Valida (decisão por ÁREA; ver o
///                                                  teste do grupo p/ a limitação conhecida)
///   - Questões 31+  : nada pintado               → EmBranco
///
/// Nota sobre dependências nativas: este teste exercita OpenCvSharp (ArUco, warp,
/// threshold) e ZXing (QR). Roda com os runtimes nativos que o projeto Web já traz.
/// </summary>
public class OmrPipelineGoldenTests
{
    private const int OptionCount = 5;

    private static SheetReadResult LerCartaoDeTeste()
    {
        var layout = new SheetLayout();
        var generator = new CardGenerator(layout);
        var classifier = new AnswerClassifier(layout);
        var reader = new OmrReader(layout, classifier);

        var info = new StudentCardInfo
        {
            ExamId = 123,
            StudentId = 456,
            StudentName = "Aluno de Teste",
            ExamName = "Prova de Teste",
            OptionCount = OptionCount
        };

        using Mat cartao = generator.GenerateTestCard(info);

        var resultado = reader.Read(cartao, OptionCount, out var warped);
        warped?.Dispose();
        return resultado;
    }

    [Fact]
    public void Cartao_de_teste_alinha_e_decodifica_o_QR()
    {
        var r = LerCartaoDeTeste();

        Assert.True(r.Aligned,
            "O cartão gerado deve alinhar pelos 4 marcadores ArUco. Avisos: " +
            string.Join(" | ", r.Warnings));
        // O QR embute exam=123;student=456 — o parse deve recuperar os dois.
        Assert.Equal(123, r.ExamId);
        Assert.Equal(456, r.StudentId);
    }

    [Fact]
    public void Questoes_1_a_10_sao_classificadas_como_Validas()
    {
        var r = LerCartaoDeTeste();
        var porNumero = r.Questions.ToDictionary(q => q.Number);

        for (int n = 1; n <= 10; n++)
        {
            Assert.Equal(QuestionStatus.Valida, porNumero[n].Status);
            // Marcação simples e forte deve devolver a alternativa detectada.
            Assert.NotNull(porNumero[n].DetectedOption);
        }
    }

    [Fact]
    public void Questoes_11_a_20_sao_classificadas_como_Duplas()
    {
        var r = LerCartaoDeTeste();
        var porNumero = r.Questions.ToDictionary(q => q.Number);

        for (int n = 11; n <= 20; n++)
        {
            Assert.Equal(QuestionStatus.Dupla, porNumero[n].Status);
            // Dupla não aponta alternativa — vai para revisão.
            Assert.Null(porNumero[n].DetectedOption);
        }
    }

    [Fact]
    public void Questoes_21_a_30_o_classificador_decide_por_area_preenchida()
    {
        // LIMITAÇÃO CONHECIDA, documentada por este teste (não é bug):
        //
        // O MarkStyle.Scribble do CardGenerator desenha dois riscos cruzando UMA bolha.
        // Na medição, esse rabisco cobre ~56% da área da bolha — acima do StrongFill
        // (0.51) — com as demais bolhas vazias (~1.6%). Para o AnswerClassifier, que
        // decide pela FRAÇÃO DE ÁREA preenchida (e não pela textura/formato da marca),
        // isso é indistinguível de uma marcação forte e limpa. Logo: classifica Valida.
        //
        // Isso é coerente com a regra do produto ("a bolha mais marcada vence, com
        // threshold"). O classificador NÃO distingue "risco" de "preenchimento sólido" —
        // essa distinção exigiria análise de textura e seria uma evolução do algoritmo.
        //
        // Este teste fixa o comportamento ATUAL. Se um dia o classificador passar a
        // tratar rabisco como RasuraDuvidosa, este teste deve ser atualizado — e aí ele
        // vira a prova de que a nova regra funciona.
        var r = LerCartaoDeTeste();
        var porNumero = r.Questions.ToDictionary(q => q.Number);

        for (int n = 21; n <= 30; n++)
        {
            Assert.Equal(QuestionStatus.Valida, porNumero[n].Status);
            Assert.NotNull(porNumero[n].DetectedOption);
        }
    }

    [Fact]
    public void Questoes_nao_marcadas_sao_EmBranco()
    {
        var r = LerCartaoDeTeste();
        var porNumero = r.Questions.ToDictionary(q => q.Number);

        // GenerateTestCard só pinta 1..30. O resto da folha está em branco.
        for (int n = 31; n <= 100; n++)
        {
            Assert.Equal(QuestionStatus.EmBranco, porNumero[n].Status);
        }
    }
}
