using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using SistemaAvaliacao.Web.Data;
using SistemaAvaliacao.Web.Models.Entities;
using SistemaAvaliacao.Web.Models.ViewModels;

namespace SistemaAvaliacao.Web.Services;

public class XlsxProvaImportService
{
    private readonly ApplicationDbContext _context;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<XlsxProvaImportService> _logger;

    public XlsxProvaImportService(ApplicationDbContext context, IWebHostEnvironment environment, ILogger<XlsxProvaImportService> logger)
    {
        _context = context;
        _environment = environment;
        _logger = logger;
    }

    public (List<ImportarQuestaoItem> Itens, List<string> Avisos) PrepararPreview(Stream xlsx)
    {
        var avisos = new List<string>();
        using var workbookStream = SanitizarWorkbookParaClosedXml(xlsx);
        using var workbook = new XLWorkbook(workbookStream);

        var worksheet = workbook.Worksheets.FirstOrDefault(ws => ws.Name.Equals("Itens", StringComparison.OrdinalIgnoreCase))
            ?? workbook.Worksheets.FirstOrDefault();

        if (worksheet == null)
            throw new InvalidOperationException("Nenhuma aba válida foi encontrada na planilha XLSX.");

        var rows = worksheet.RowsUsed().ToList();
        if (rows.Count <= 1)
            throw new InvalidOperationException("A planilha não contém linhas de dados de questões.");

        var cabecalhos = rows[0].Cells().Select(c => NormalizarCabecalho(c.Value.ToString())).ToList();
        var indices = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < cabecalhos.Count; i++)
        {
            var nome = cabecalhos[i];
            if (!string.IsNullOrWhiteSpace(nome))
                indices[nome] = i;
        }

        var obrigatorios = new[] { "numero_questao", "comando", "alternativa_a", "alternativa_b", "gabarito" };
        foreach (var campo in obrigatorios)
        {
            if (!indices.ContainsKey(campo))
                throw new InvalidOperationException($"A planilha está fora do padrão esperado. Falta a coluna '{campo}'.");
        }

        var imagensPorQuestao = ObterImagensPorQuestao(workbook);
        var metadadosPorQuestao = ObterMetadadosDosSuportes(workbook);

        var itens = new List<ImportarQuestaoItem>();
        foreach (var row in rows.Skip(1))
        {
            var cells = row.Cells().ToList();
            var numero = ObterInt(cells, indices, "numero_questao");
            if (numero == null)
            {
                avisos.Add("Linha ignorada porque não possui número de questão válido.");
                continue;
            }

            var titulo = ObterTexto(cells, indices, "titulo");
            var enunciado = ObterTexto(cells, indices, "enunciado");
            var comando = ObterTexto(cells, indices, "comando") ?? string.Empty;
            var alternativaA = LimparAlternativa(ObterTexto(cells, indices, "alternativa_a"));
            var alternativaB = LimparAlternativa(ObterTexto(cells, indices, "alternativa_b"));
            var alternativaC = LimparAlternativa(ObterTexto(cells, indices, "alternativa_c"));
            var alternativaD = LimparAlternativa(ObterTexto(cells, indices, "alternativa_d"));
            var alternativaE = LimparAlternativa(ObterTexto(cells, indices, "alternativa_e"));
            var gabarito = ObterTexto(cells, indices, "gabarito");
            var fonte = ObterTexto(cells, indices, "suporte_legenda_fonte")
                ?? (metadadosPorQuestao.TryGetValue(numero.Value, out var metadados) ? metadados.Fonte : null);
            var descricaoAcessibilidade = metadadosPorQuestao.TryGetValue(numero.Value, out metadados)
                ? metadados.DescricaoAcessibilidade
                : null;
            var imagemUrl = imagensPorQuestao.TryGetValue(numero.Value, out var imagem) ? imagem : null;

            var item = new ImportarQuestaoItem
            {
                Numero = numero.Value,
                Enunciado = MesclarContexto(titulo, enunciado),
                Comando = comando.Trim(),
                ImagemUrl = imagemUrl,
                ImagemEhTabela = false,
                DescricaoAcessibilidade = descricaoAcessibilidade,
                Fonte = string.IsNullOrWhiteSpace(fonte) ? null : fonte.Trim(),
                AlternativaA = alternativaA ?? string.Empty,
                AlternativaB = alternativaB ?? string.Empty,
                AlternativaC = alternativaC,
                AlternativaD = alternativaD,
                AlternativaE = alternativaE,
                NumeroAlternativas = ContarAlternativas(alternativaA, alternativaB, alternativaC, alternativaD, alternativaE),
                AlternativaCorreta = NormalizarGabarito(gabarito),
                GabaritoAutomatico = true,
                Incluir = true
            };

            if (string.IsNullOrWhiteSpace(item.Comando))
                item.Problemas.Add("Comando não informado na planilha.");
            if (string.IsNullOrWhiteSpace(item.AlternativaA) || string.IsNullOrWhiteSpace(item.AlternativaB))
                item.Problemas.Add("Alternativas A e B obrigatórias não foram preenchidas.");
            if (item.AlternativaCorreta == null)
                item.Problemas.Add("Gabarito ausente ou inválido na planilha.");

            itens.Add(item);
        }

        if (itens.Count == 0)
            throw new InvalidOperationException("Nenhuma questão válida foi encontrada na planilha XLSX.");

        return (itens, avisos);
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
                TextoBase = null,
                Comando = item.Comando.Trim(),
                ImagemUrl = string.IsNullOrWhiteSpace(item.ImagemUrl) ? null : item.ImagemUrl,
                DescricaoAcessibilidade = LimparValor(item.DescricaoAcessibilidade),
                AlternativaA = item.AlternativaA?.Trim() ?? string.Empty,
                AlternativaB = item.AlternativaB?.Trim() ?? string.Empty,
                AlternativaC = LimparValor(item.AlternativaC),
                AlternativaD = LimparValor(item.AlternativaD),
                AlternativaE = LimparValor(item.AlternativaE),
                NumeroAlternativas = Math.Clamp(item.NumeroAlternativas, 2, 5),
                AlternativaCorreta = item.AlternativaCorreta ?? 'A',
                Fonte = LimparValor(item.Fonte),
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

    private static string? MesclarContexto(string? titulo, string? enunciado)
    {
        var valor = !string.IsNullOrWhiteSpace(enunciado) ? enunciado.Trim()
            : !string.IsNullOrWhiteSpace(titulo) ? titulo.Trim()
            : null;

        return valor;
    }

    private static int? ObterInt(List<IXLCell> cells, Dictionary<string, int> indices, string nomeCampo)
    {
        if (!indices.TryGetValue(nomeCampo, out var idx) || idx >= cells.Count)
            return null;

        var valor = cells[idx].GetString().Trim();
        if (string.IsNullOrWhiteSpace(valor))
            return null;

        return int.TryParse(valor, out var numero) ? numero : null;
    }

    private static string? ObterTexto(List<IXLCell> cells, Dictionary<string, int> indices, string nomeCampo)
    {
        if (!indices.TryGetValue(nomeCampo, out var idx) || idx >= cells.Count)
            return null;

        var valor = cells[idx].GetString();
        return string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
    }

    private static string? LimparAlternativa(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor)) return null;

        var texto = valor.Trim();
        texto = Regex.Replace(texto, @"^\s*[A-E]\s*\)\s*", string.Empty, RegexOptions.IgnoreCase);
        texto = texto.Replace("\r", " ").Replace("\n", " ");
        texto = Regex.Replace(texto, @"\s+", " ").Trim();
        return texto;
    }

    private static string? LimparValor(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor)) return null;
        return Regex.Replace(valor.Trim(), @"\s+", " ");
    }

    private static char? NormalizarGabarito(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor)) return null;

        var texto = valor.Trim();
        var match = Regex.Match(texto, "[A-E]", RegexOptions.IgnoreCase);
        if (!match.Success) return null;

        return char.ToUpperInvariant(match.Value[0]);
    }

    private static int ContarAlternativas(params string?[] alternativas)
    {
        var count = 0;
        foreach (var alt in alternativas)
        {
            if (!string.IsNullOrWhiteSpace(alt))
                count++;
        }

        return Math.Clamp(count, 2, 5);
    }

    private Dictionary<int, string> ObterImagensPorQuestao(XLWorkbook workbook)
    {
        var imagensReconstituidas = ReconstituirImagensBase64(workbook);
        var imagensPorQuestao = new Dictionary<int, string>();

        var sheet = workbook.Worksheets.FirstOrDefault(ws => ws.Name.Equals("Suportes", StringComparison.OrdinalIgnoreCase));
        if (sheet == null)
            return imagensPorQuestao;

        var rows = sheet.RowsUsed().ToList();
        if (rows.Count <= 1)
            return imagensPorQuestao;

        var cabecalhos = rows[0].Cells().Select(c => NormalizarCabecalho(c.Value.ToString())).ToList();
        var indices = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < cabecalhos.Count; i++)
        {
            var nome = cabecalhos[i];
            if (!string.IsNullOrWhiteSpace(nome))
                indices[nome] = i;
        }

        foreach (var row in rows.Skip(1))
        {
            var cells = row.Cells().ToList();
            var numero = ObterInt(cells, indices, "numero_questao");
            if (numero == null)
                continue;

            var imagemId = ObterTexto(cells, indices, "imagem_id");
            if (string.IsNullOrWhiteSpace(imagemId))
                continue;

            if (!imagensReconstituidas.TryGetValue(imagemId, out var bytes) || bytes.Length == 0)
                continue;

            var nomeArquivo = ObterTexto(cells, indices, "arquivo_imagem") ?? $"{imagemId}.png";
            var url = SalvarImagem(bytes, nomeArquivo);
            if (!string.IsNullOrWhiteSpace(url))
                imagensPorQuestao[numero.Value] = url;
        }

        return imagensPorQuestao;
    }

    private static Dictionary<int, (string? DescricaoAcessibilidade, string? Fonte)> ObterMetadadosDosSuportes(XLWorkbook workbook)
    {
        var metadados = new Dictionary<int, (string? DescricaoAcessibilidade, string? Fonte)>();
        var sheet = workbook.Worksheets.FirstOrDefault(ws => ws.Name.Equals("Suportes", StringComparison.OrdinalIgnoreCase));
        if (sheet == null)
            return metadados;

        var rows = sheet.RowsUsed().ToList();
        if (rows.Count <= 1)
            return metadados;

        var cabecalhos = rows[0].Cells().Select(c => NormalizarCabecalho(c.Value.ToString())).ToList();
        var indices = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < cabecalhos.Count; i++)
        {
            var nome = cabecalhos[i];
            if (!string.IsNullOrWhiteSpace(nome))
                indices[nome] = i;
        }

        foreach (var row in rows.Skip(1))
        {
            var cells = row.Cells().ToList();
            var numero = ObterInt(cells, indices, "numero_questao");
            if (numero == null)
                continue;

            var temImagem = !string.IsNullOrWhiteSpace(ObterTexto(cells, indices, "imagem_id"));
            var titulo = ObterTexto(cells, indices, "titulo_suporte");
            var fonte = ObterTexto(cells, indices, "legenda_fonte");

            if (!metadados.TryGetValue(numero.Value, out var atual))
                atual = (null, null);

            if (temImagem && !string.IsNullOrWhiteSpace(titulo))
                atual.DescricaoAcessibilidade = UnirTexto(atual.DescricaoAcessibilidade, titulo);
            if (!string.IsNullOrWhiteSpace(fonte))
                atual.Fonte = UnirTexto(atual.Fonte, fonte);

            metadados[numero.Value] = atual;
        }

        return metadados;
    }

    private static string? UnirTexto(string? atual, string novo)
    {
        if (string.IsNullOrWhiteSpace(atual))
            return novo.Trim();
        if (atual.Contains(novo, StringComparison.OrdinalIgnoreCase))
            return atual;
        return $"{atual}; {novo.Trim()}";
    }

    private Dictionary<string, byte[]> ReconstituirImagensBase64(XLWorkbook workbook)
    {
        var sheet = workbook.Worksheets.FirstOrDefault(ws => ws.Name.Equals("Imagens_Base64", StringComparison.OrdinalIgnoreCase));
        if (sheet == null)
            return new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        var rows = sheet.RowsUsed().ToList();
        if (rows.Count <= 1)
            return new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        var cabecalhos = rows[0].Cells().Select(c => NormalizarCabecalho(c.Value.ToString())).ToList();
        var indices = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < cabecalhos.Count; i++)
        {
            var nome = cabecalhos[i];
            if (!string.IsNullOrWhiteSpace(nome))
                indices[nome] = i;
        }

        var chunksPorImagem = new Dictionary<string, List<(int Parte, string Base64Chunk)>>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows.Skip(1))
        {
            var cells = row.Cells().ToList();
            var imagemId = ObterTexto(cells, indices, "imagem_id");
            var base64Chunk = ObterTexto(cells, indices, "base64_chunk");
            var parte = ObterInt(cells, indices, "parte");
            if (string.IsNullOrWhiteSpace(imagemId) || string.IsNullOrWhiteSpace(base64Chunk) || parte == null)
                continue;

            if (!chunksPorImagem.ContainsKey(imagemId))
                chunksPorImagem[imagemId] = new List<(int Parte, string Base64Chunk)>();

            chunksPorImagem[imagemId].Add((parte.Value, base64Chunk.Trim()));
        }

        var imagens = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in chunksPorImagem)
        {
            var base64Completo = string.Concat(kvp.Value
                .OrderBy(x => x.Parte)
                .Select(x => x.Base64Chunk));

            if (string.IsNullOrWhiteSpace(base64Completo))
                continue;

            try
            {
                imagens[kvp.Key] = Convert.FromBase64String(base64Completo);
            }
            catch (FormatException ex)
            {
                _logger.LogWarning(ex, "Falha ao decodificar imagem {ImagemId} da planilha XLSX.", kvp.Key);
            }
        }

        return imagens;
    }

    private string? SalvarImagem(byte[] bytes, string? nomeArquivo)
    {
        if (bytes.Length == 0)
            return null;

        var pasta = Path.Combine(_environment.WebRootPath, "uploads", "questoes");
        Directory.CreateDirectory(pasta);

        var extensao = ".png";
        if (!string.IsNullOrWhiteSpace(nomeArquivo))
        {
            var extensaoArquivo = Path.GetExtension(nomeArquivo);
            if (!string.IsNullOrWhiteSpace(extensaoArquivo))
                extensao = extensaoArquivo;
        }

        var nome = $"{Guid.NewGuid()}{extensao}";
        var caminho = Path.Combine(pasta, nome);
        File.WriteAllBytes(caminho, bytes);

        return $"/uploads/questoes/{nome}";
    }

    private static MemoryStream SanitizarWorkbookParaClosedXml(Stream source)
    {
        if (!source.CanSeek)
        {
            var seekable = new MemoryStream();
            source.CopyTo(seekable);
            seekable.Position = 0;
            source = seekable;
        }

        source.Position = 0;
        using var archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);

        var output = new MemoryStream();
        using (var newArchive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in archive.Entries)
            {
                var copied = newArchive.CreateEntry(entry.FullName);

                if (entry.FullName.StartsWith("xl/drawings/", StringComparison.OrdinalIgnoreCase) &&
                    entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                {
                    using var sourceEntry = entry.Open();
                    using var reader = new StreamReader(sourceEntry, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
                    var xml = reader.ReadToEnd();

                    var document = XDocument.Parse(xml);
                    var ns = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing");
                    foreach (var cNvPr in document.Descendants(ns + "cNvPr"))
                    {
                        var nomeAtual = (string?)cNvPr.Attribute("name");
                        if (string.IsNullOrWhiteSpace(nomeAtual))
                            continue;

                        cNvPr.SetAttributeValue("name", TruncarNomeImagem(nomeAtual));
                    }

                    using var target = copied.Open();
                    using var writer = new StreamWriter(target, Encoding.UTF8, leaveOpen: true);
                    writer.Write(document.ToString(System.Xml.Linq.SaveOptions.DisableFormatting));
                    writer.Flush();
                    continue;
                }

                using var original = entry.Open();
                using var targetStream = copied.Open();
                original.CopyTo(targetStream);
            }
        }

        output.Position = 0;
        return output;
    }

    private static string TruncarNomeImagem(string nome)
    {
        var valor = Regex.Replace(nome, @"[^A-Za-z0-9_-]", "-");
        valor = valor.Trim('-');
        if (string.IsNullOrWhiteSpace(valor))
            valor = "picture";

        if (valor.Length > 31)
            valor = valor.Substring(0, 31).TrimEnd('-');

        return valor;
    }

    private static string NormalizarCabecalho(string? valor)
        => Regex.Replace((valor ?? string.Empty).Trim(), @"\s+", "_").ToLowerInvariant();
}
