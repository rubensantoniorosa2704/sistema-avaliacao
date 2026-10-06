using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using System.Text;
using System.Text.Json;
using SistemaAvaliacao.Web.Data;
using SistemaAvaliacao.Web.Models.Entities;
using SistemaAvaliacao.Web.Models.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace SistemaAvaliacao.Web.Services;

public class DocxGeminiImportService
{
    private readonly ApplicationDbContext _context;
    private readonly GeminiService _gemini;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<DocxGeminiImportService> _logger;

    public DocxGeminiImportService(
        ApplicationDbContext context,
        GeminiService gemini,
        IWebHostEnvironment env,
        ILogger<DocxGeminiImportService> logger)
    {
        _context = context;
        _gemini = gemini;
        _env = env;
        _logger = logger;
    }

    public async Task<ImportarQuestoesDocxViewModel> PrepararPreviewAsync(
        Guid provaId,
        string caminhoDocx,
        string? caminhoGabarito = null)
    {
        var prova = await _context.Provas.FindAsync(provaId)
            ?? throw new InvalidOperationException($"Prova {provaId} não encontrada");

        // Extrai texto bruto do DOCX
        var textoDocx = ExtrairTextoDocx(caminhoDocx);

        // Monta prompt para o Gemini
        var prompt = MontarPrompt(textoDocx);

        // Chama Gemini
        _logger.LogInformation("Enviando {Chars} caracteres para Gemini...", textoDocx.Length);
        var respostaJson = await _gemini.GerarConteudoAsync(prompt);

        // Parse JSON
        var questoes = ParsearRespostaGemini(respostaJson);

        _logger.LogInformation("Gemini retornou {Count} questões", questoes.Count);

        // Carrega gabaritos se fornecido
        Dictionary<int, char>? gabaritos = null;
        if (!string.IsNullOrEmpty(caminhoGabarito))
        {
            gabaritos = CarregarGabaritos(caminhoGabarito);
        }

        // Monta preview
        var itens = new List<ImportarQuestaoItem>();
        foreach (var q in questoes)
        {
            var item = new ImportarQuestaoItem
            {
                Numero = q.Numero,
                Enunciado = q.Enunciado,
                TextoBase = q.TextoBase,
                Comando = q.Comando ?? string.Empty,
                ImagemUrl = q.ImagemUrl,
                ImagemEhTabela = q.ImagemEhTabela,
                AlternativaA = q.Alternativas.ElementAtOrDefault(0) ?? string.Empty,
                AlternativaB = q.Alternativas.ElementAtOrDefault(1) ?? string.Empty,
                AlternativaC = q.Alternativas.ElementAtOrDefault(2),
                AlternativaD = q.Alternativas.ElementAtOrDefault(3),
                AlternativaE = q.Alternativas.ElementAtOrDefault(4),
                NumeroAlternativas = q.Alternativas.Count,
                AlternativaCorreta = gabaritos?.GetValueOrDefault(q.Numero),
                Fonte = q.Fonte,
                DescricaoAcessibilidade = q.DescricaoAcessibilidade,
                Incluir = true
            };

            itens.Add(item);
        }

        return new ImportarQuestoesDocxViewModel
        {
            ProvaId = provaId,
            NomeProva = prova.Nome,
            NomeArquivo = Path.GetFileName(caminhoDocx),
            NomeArquivoGabarito = caminhoGabarito != null ? Path.GetFileName(caminhoGabarito) : null,
            Itens = itens
        };
    }

    private string ExtrairTextoDocx(string caminho)
    {
        var sb = new StringBuilder();

        using var doc = WordprocessingDocument.Open(caminho, false);
        var body = doc.MainDocumentPart?.Document.Body;
        if (body == null) return string.Empty;

        foreach (var paragraph in body.Elements<Paragraph>())
        {
            var texto = paragraph.InnerText;
            if (!string.IsNullOrWhiteSpace(texto))
            {
                sb.AppendLine(texto);
            }
        }

        return sb.ToString();
    }

    private string MontarPrompt(string textoDocx)
    {
        return $@"Você é um especialista em extrair questões de provas do formato ENADE.

TAREFA: Analise o texto abaixo e extraia TODAS as questões em formato JSON.

REGRAS IMPORTANTES:
1. Cada questão tem um número (ex: ""Questão nº 01"", ""QUESTÃO 02"")
2. Algumas questões compartilham um ""Texto-base para as questões X a Y"" - neste caso:
   - Coloque TODO o texto-base no campo ""textoBase"" da PRIMEIRA questão do grupo
   - Deixe ""textoBase"" vazio nas demais questões do grupo
   - O ""enunciado"" individual de cada questão (após o texto-base) vai no campo ""enunciado""
3. Se houver ""Figura X -"" ou ""Fonte:"", inclua como parte do enunciado
4. O ""comando"" é a pergunta final (ex: ""A alternativa correta é"")
5. As alternativas começam com A), B), C), D), E)
6. Extraia TODAS as questões encontradas

FORMATO DE SAÍDA (JSON):
```json
[
  {{
    ""numero"": 1,
    ""textoBase"": """",
    ""enunciado"": ""texto do enunciado"",
    ""comando"": ""texto do comando/pergunta"",
    ""alternativas"": [
      ""texto alternativa A"",
      ""texto alternativa B"",
      ""texto alternativa C"",
      ""texto alternativa D""
    ],
    ""imagemUrl"": null,
    ""imagemEhTabela"": false,
    ""fonte"": null,
    ""descricaoAcessibilidade"": null
  }}
]
```

TEXTO DA PROVA:
{textoDocx}

Retorne APENAS o JSON, sem explicações adicionais.";
    }

    private List<QuestaoGemini> ParsearRespostaGemini(string json)
    {
        try
        {
            // Remove markdown code block se presente
            json = json.Trim();
            if (json.StartsWith("```json"))
            {
                json = json.Substring(7);
            }
            if (json.StartsWith("```"))
            {
                json = json.Substring(3);
            }
            if (json.EndsWith("```"))
            {
                json = json.Substring(0, json.Length - 3);
            }
            json = json.Trim();

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var questoes = JsonSerializer.Deserialize<List<QuestaoGemini>>(json, options);
            return questoes ?? new List<QuestaoGemini>();
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Erro ao parsear JSON do Gemini: {Json}", json);
            throw new InvalidOperationException("Não foi possível interpretar a resposta da IA. Tente novamente.", ex);
        }
    }

    private Dictionary<int, char> CarregarGabaritos(string caminho)
    {
        // Reutiliza lógica existente do DocxProvaImportService
        // Por simplicidade, mantém regex básico aqui
        var gabaritos = new Dictionary<int, char>();
        var texto = ExtrairTextoDocx(caminho);
        var linhas = texto.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var linha in linhas)
        {
            var match = System.Text.RegularExpressions.Regex.Match(linha, @"(\d+)\s*([A-E])", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var numero = int.Parse(match.Groups[1].Value);
                var resposta = char.ToUpper(match.Groups[2].Value[0]);
                gabaritos[numero] = resposta;
            }
        }

        return gabaritos;
    }

    public async Task<int> ConfirmarAsync(Guid provaId, List<ImportarQuestaoItem> itens)
    {
        var prova = await _context.Provas
            .Include(p => p.ProvaQuestoes)
            .FirstOrDefaultAsync(p => p.Id == provaId)
            ?? throw new InvalidOperationException($"Prova {provaId} não encontrada.");

        var proximaOrdem = prova.ProvaQuestoes.Any()
            ? prova.ProvaQuestoes.Max(pq => pq.Ordem) + 1
            : 1;

        int inseridas = 0;
        foreach (var item in itens.Where(i => i.Incluir))
        {
            var questao = new Questao
            {
                Enunciado = string.IsNullOrWhiteSpace(item.Enunciado) ? null : item.Enunciado.Trim(),
                TextoBase = string.IsNullOrWhiteSpace(item.TextoBase) ? null : item.TextoBase.Trim(),
                Comando = item.Comando.Trim(),
                ImagemUrl = string.IsNullOrWhiteSpace(item.ImagemUrl) ? null : item.ImagemUrl,
                DescricaoAcessibilidade = string.IsNullOrWhiteSpace(item.DescricaoAcessibilidade) ? null : item.DescricaoAcessibilidade,
                AlternativaA = item.AlternativaA?.Trim() ?? string.Empty,
                AlternativaB = item.AlternativaB?.Trim() ?? string.Empty,
                AlternativaC = item.AlternativaC?.Trim(),
                AlternativaD = item.AlternativaD?.Trim(),
                AlternativaE = item.AlternativaE?.Trim(),
                NumeroAlternativas = Math.Clamp(item.NumeroAlternativas, 2, 5),
                AlternativaCorreta = item.AlternativaCorreta ?? 'A',
                Fonte = item.Fonte?.Trim(),
                CreatedAt = DateTime.UtcNow
            };

            _context.Questoes.Add(questao);
            await _context.SaveChangesAsync();

            _context.ProvaQuestoes.Add(new ProvaQuestao
            {
                ProvaId = prova.Id,
                QuestaoId = questao.Id,
                Ordem = proximaOrdem++
            });
            inseridas++;
        }

        prova.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return inseridas;
    }

    private class QuestaoGemini
    {
        public int Numero { get; set; }
        public string? TextoBase { get; set; }
        public string? Enunciado { get; set; }
        public string? Comando { get; set; }
        public List<string> Alternativas { get; set; } = new();
        public string? ImagemUrl { get; set; }
        public bool ImagemEhTabela { get; set; }
        public string? Fonte { get; set; }
        public string? DescricaoAcessibilidade { get; set; }
    }
}
