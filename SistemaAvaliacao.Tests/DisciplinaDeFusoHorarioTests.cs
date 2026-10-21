using Xunit;

namespace SistemaAvaliacao.Tests;

/// <summary>
/// Blinda a disciplina de fuso horário do sistema: TODO horário é UTC no banco e nas
/// comparações; a conversão para Brasília acontece só nas bordas, via o helper DataHora.
///
/// A ameaça real não é o código de hoje (que já respeita a regra — zero DateTime.Now),
/// é a erosão futura: alguém escreve "DateTime.Now" por hábito e introduz um bug sutil de
/// prazo que só aparece quando o servidor roda em outro fuso. Este teste varre os fontes
/// da camada Web e falha se esse hábito voltar. É a convenção (§8) virando trava automática.
///
/// Escopo: SistemaAvaliacao.Web, exceto Migrations/ (geradas pelo EF, não editadas à mão)
/// e as pastas de build obj/ e bin/.
/// </summary>
public class DisciplinaDeFusoHorarioTests
{
    [Fact]
    public void Camada_Web_nao_usa_DateTime_Now_nem_Today()
    {
        var raizWeb = LocalizarRaizWeb();

        var arquivos = Directory
            .EnumerateFiles(raizWeb, "*.cs", SearchOption.AllDirectories)
            .Where(caminho => !CaminhoIgnorado(caminho, raizWeb))
            .ToList();

        // Rede de segurança: se a varredura não achou fonte algum, algo está errado com o
        // caminho — melhor falhar do que passar vazio dando falsa sensação de segurança.
        Assert.True(arquivos.Count > 0, $"Nenhum .cs encontrado em {raizWeb}. Verifique a raiz da varredura.");

        var infracoes = new List<string>();

        foreach (var arquivo in arquivos)
        {
            var linhas = File.ReadAllLines(arquivo);
            for (var i = 0; i < linhas.Length; i++)
            {
                var linha = linhas[i];
                if (linha.Contains("DateTime.Now", StringComparison.Ordinal) ||
                    linha.Contains("DateTime.Today", StringComparison.Ordinal))
                {
                    var relativo = Path.GetRelativePath(raizWeb, arquivo);
                    infracoes.Add($"{relativo}:{i + 1}  {linha.Trim()}");
                }
            }
        }

        Assert.True(
            infracoes.Count == 0,
            "Use DateTime.UtcNow (timestamps) ou o helper DataHora (conversão para Brasília), " +
            "nunca DateTime.Now/Today. Veja docs/CONVENTIONS.md §8. Infração(ões):\n" +
            string.Join("\n", infracoes));
    }

    private static bool CaminhoIgnorado(string caminho, string raizWeb)
    {
        var relativo = Path.GetRelativePath(raizWeb, caminho)
            .Replace('\\', '/');

        return relativo.StartsWith("Migrations/", StringComparison.OrdinalIgnoreCase)
            || relativo.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
            || relativo.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
            || relativo.StartsWith("obj/", StringComparison.OrdinalIgnoreCase)
            || relativo.StartsWith("bin/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Sobe a partir do diretório do assembly de teste até encontrar a pasta do projeto
    /// Web (identificada pelo seu .csproj). Independe do caminho absoluto da máquina.
    /// </summary>
    private static string LocalizarRaizWeb()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null)
        {
            var candidata = Path.Combine(dir.FullName, "SistemaAvaliacao.Web");
            if (Directory.Exists(candidata) &&
                File.Exists(Path.Combine(candidata, "SistemaAvaliacao.Web.csproj")))
            {
                return candidata;
            }
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "Não foi possível localizar a pasta SistemaAvaliacao.Web a partir de " +
            AppContext.BaseDirectory);
    }
}
