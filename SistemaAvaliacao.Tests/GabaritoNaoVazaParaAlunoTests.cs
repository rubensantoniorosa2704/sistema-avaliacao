using System.Reflection;
using SistemaAvaliacao.Web.Services;
using Xunit;

namespace SistemaAvaliacao.Tests;

/// <summary>
/// Blinda o invariante mais importante do produto: o gabarito (AlternativaCorreta)
/// NUNCA chega ao aluno. O caminho do aluno serve as questões a partir do record
/// <see cref="QuestaoExibicao"/> (preenchido por <see cref="QuestoesProvaCache"/>).
/// Enquanto esse tipo não tiver nenhum membro de gabarito, é IMPOSSÍVEL vazar a
/// resposta correta por acidente — o compilador garante.
///
/// Este teste existe para falhar no dia em que alguém, sem querer, adicionar um campo
/// como "AlternativaCorreta" ao QuestaoExibicao.
/// </summary>
public class GabaritoNaoVazaParaAlunoTests
{
    // Nomes proibidos no tipo que chega ao cliente. Comparação case-insensitive e por
    // "contém", para pegar variações como "LetraCorreta", "GabaritoCorreto" etc.
    private static readonly string[] TermosDeGabarito =
    {
        "correta", "correto", "gabarito"
    };

    [Fact]
    public void QuestaoExibicao_nao_expoe_nenhum_membro_de_gabarito()
    {
        var tipo = typeof(QuestaoExibicao);

        var membros = tipo
            .GetMembers(BindingFlags.Public | BindingFlags.Instance)
            .Select(m => m.Name)
            .ToList();

        var suspeitos = membros
            .Where(nome => TermosDeGabarito.Any(termo =>
                nome.Contains(termo, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        Assert.True(
            suspeitos.Count == 0,
            "QuestaoExibicao (o conteúdo que chega ao aluno) não pode expor o gabarito. " +
            "Membro(s) suspeito(s): " + string.Join(", ", suspeitos));
    }

    [Fact]
    public void AlternativaExibicao_nao_expoe_marca_de_resposta_correta()
    {
        // A alternativa individual também não pode carregar um "IsCorreta"/"Correta".
        var tipo = typeof(AlternativaExibicao);

        var suspeitos = tipo
            .GetMembers(BindingFlags.Public | BindingFlags.Instance)
            .Select(m => m.Name)
            .Where(nome => TermosDeGabarito.Any(termo =>
                nome.Contains(termo, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        Assert.True(
            suspeitos.Count == 0,
            "AlternativaExibicao não pode marcar qual alternativa é a correta. " +
            "Membro(s) suspeito(s): " + string.Join(", ", suspeitos));
    }
}
