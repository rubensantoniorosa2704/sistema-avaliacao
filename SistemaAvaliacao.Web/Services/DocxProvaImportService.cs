using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using SistemaAvaliacao.Web.Data;
using SistemaAvaliacao.Web.Models.Entities;
using SistemaAvaliacao.Web.Models.ViewModels;

namespace SistemaAvaliacao.Web.Services;

/// <summary>
/// Importa questões de um caderno de prova em formato Word (.docx).
///
/// O fluxo é em duas etapas para permitir revisão antes de gravar:
///   1. <see cref="PrepararPreview"/> — lê o .docx, extrai/renderiza as imagens
///      (figuras extraídas + tabelas renderizadas em SVG) para wwwroot/uploads/questoes
///      e devolve as questões estruturadas SEM tocar no banco.
///   2. <see cref="ConfirmarAsync"/> — recebe as questões já revisadas/editadas pelo
///      Admin e grava <see cref="Questao"/> + <see cref="ProvaQuestao"/>, exatamente
///      como a inserção manual (mesmo destino de imagem, mesma ordem sequencial).
///
/// A segmentação do conteúdo é feita por REGEX sobre o texto de cada parágrafo
/// (lido em ordem via OpenXML), sem uso de IA.
/// </summary>
public class DocxProvaImportService
{
    private readonly ApplicationDbContext _context;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<DocxProvaImportService> _logger;

    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace PKG = "http://schemas.openxmlformats.org/package/2006/relationships";

    private static readonly Regex RxQuestao = new(@"^\s*Quest[aã]o\s*n[º°o]?\.?\s*0*(\d+)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RxAlternativa = new(@"^\s*([A-E])\)\s*(.*)$", RegexOptions.Compiled);
    private static readonly Regex RxFonte = new(@"^\s*Fonte\s*:\s*(.*)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RxAcessibilidade = new(@"^\s*Descri[çc][aã]o\s+para\s+acessibilidade\s*:\s*(.*)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RxFigura = new(@"^\s*Figura\s+\d+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    
    // Detecta "Texto-base para as questões 02 a 05" ou "Texto-base T1" no início do enunciado
    private static readonly Regex RxTextoBase = new(@"^\s*Texto-base\s+(para\s+as?\s+quest[õo]es?\s+0*(\d+)\s+a\s+0*(\d+)|T\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Cabeçalhos aceitos para a tabela de gabarito no Resumo Técnico. O layout varia entre
    // documentos: "Item | Gab. | Item | Gab. | Item | Gab." (3 pares por linha) ou
    // "Questão | ID | Chave | Dificuldade | Bloom" (1 par por linha, com colunas extras).
    private static readonly Regex RxCabecalhoItem = new(@"^(item|quest[aã]o|q)\.?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RxCabecalhoResposta = new(@"^(gab\.?|gabarito|chave)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RxLetraGabarito = new(@"^\s*([A-E])\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public DocxProvaImportService(
        ApplicationDbContext context,
        IWebHostEnvironment environment,
        ILogger<DocxProvaImportService> logger)
    {
        _context = context;
        _environment = environment;
        _logger = logger;
    }

    // ───────────────────────── Etapa 1: PREVIEW (sem gravar) ─────────────────────────

    /// <summary>
    /// Lê o Caderno de Aplicação (.docx) e o Resumo Técnico (.docx, contém o gabarito
    /// consolidado), materializa as imagens (figuras/tabelas) em wwwroot e devolve os
    /// itens prontos para revisão, já com o gabarito cruzado por número de questão.
    /// Nada é gravado no banco.
    /// </summary>
    public (List<ImportarQuestaoItem> Itens, List<string> Avisos) PrepararPreview(Stream caderno, Stream resumoTecnico)
    {
        var avisos = new List<string>();
        var questoes = Parse(caderno);
        var gabarito = ParseGabarito(resumoTecnico, avisos);

        var itens = new List<ImportarQuestaoItem>();

        foreach (var q in questoes)
        {
            var (imagemUrl, ehTabela) = MaterializarImagem(q, avisos);
            var temGabarito = gabarito.TryGetValue(q.Numero, out var letraGabarito);

            var item = new ImportarQuestaoItem
            {
                Numero = q.Numero,
                Enunciado = string.IsNullOrWhiteSpace(q.Enunciado) ? null : q.Enunciado,
                TextoBase = string.IsNullOrWhiteSpace(q.TextoBase) ? null : q.TextoBase,
                Comando = q.Comando,
                ImagemUrl = imagemUrl,
                ImagemEhTabela = ehTabela,
                DescricaoAcessibilidade = q.DescricaoAcessibilidade,
                Fonte = q.Fonte,
                AlternativaA = Alt(q, 'A'),
                AlternativaB = Alt(q, 'B'),
                AlternativaC = AltOpcional(q, 'C'),
                AlternativaD = AltOpcional(q, 'D'),
                AlternativaE = AltOpcional(q, 'E'),
                NumeroAlternativas = Math.Clamp(q.Alternativas.Count, 2, 5),
                AlternativaCorreta = temGabarito ? letraGabarito : null,
                GabaritoAutomatico = temGabarito
            };

            item.Problemas.AddRange(DetectarProblemas(item, gabarito));
            itens.Add(item);
        }

        ValidarCruzamento(questoes, gabarito, avisos);
        return (itens, avisos);
    }

    private static IEnumerable<string> DetectarProblemas(ImportarQuestaoItem item, Dictionary<int, char> gabarito)
    {
        if (string.IsNullOrWhiteSpace(item.Comando))
            yield return "Comando não identificado.";
        if (string.IsNullOrWhiteSpace(item.AlternativaA) || string.IsNullOrWhiteSpace(item.AlternativaB))
            yield return "Menos de 2 alternativas detectadas.";
        if (!gabarito.ContainsKey(item.Numero))
            yield return "Sem gabarito correspondente no Resumo Técnico — selecione manualmente.";
    }

    /// <summary>
    /// Confere o cruzamento Caderno × Gabarito: item sem gabarito, gabarito sem item
    /// correspondente no Caderno e contagem por letra (A/B/C/D/E), quando o Resumo
    /// Técnico declarar a distribuição esperada.
    /// </summary>
    private static void ValidarCruzamento(List<QuestaoImportada> questoes, Dictionary<int, char> gabarito, List<string> avisos)
    {
        var numerosCaderno = questoes.Select(q => q.Numero).ToHashSet();
        var orfaosGabarito = gabarito.Keys.Where(n => !numerosCaderno.Contains(n)).OrderBy(n => n).ToList();
        if (orfaosGabarito.Count > 0)
            avisos.Add($"O gabarito traz {orfaosGabarito.Count} item(ns) que não têm questão correspondente no Caderno: {string.Join(", ", orfaosGabarito)}.");

        var semGabarito = numerosCaderno.Where(n => !gabarito.ContainsKey(n)).OrderBy(n => n).ToList();
        if (semGabarito.Count > 0)
            avisos.Add($"{semGabarito.Count} questão(ões) do Caderno sem gabarito correspondente: {string.Join(", ", semGabarito)}.");

        if (gabarito.Count > 0)
        {
            var distribuicao = gabarito.Values
                .GroupBy(l => l)
                .OrderBy(g => g.Key)
                .Select(g => $"{g.Key}={g.Count()}")
                .ToList();
            avisos.Add($"Gabarito lido: {gabarito.Count} item(ns) — distribuição {string.Join(", ", distribuicao)}. Confira contra o Resumo Técnico.");
        }
    }

    // ───────────────────────── Parsing do gabarito (Resumo Técnico) ─────────────────────────

    /// <summary>
    /// Lê a tabela "Gabarito consolidado" do Resumo Técnico. A tabela vem em formato
    /// achatado — 3 pares (Item, Gab.) por linha — então cada linha de dados é lida em
    /// grupos de 2 colunas. Retorna Numero da questão → letra do gabarito.
    /// </summary>
    public Dictionary<int, char> ParseGabarito(Stream resumoTecnico, List<string> avisos)
    {
        using var ms = new MemoryStream();
        resumoTecnico.CopyTo(ms);
        ms.Position = 0;

        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        var docEntry = zip.GetEntry("word/document.xml")
            ?? throw new InvalidOperationException("word/document.xml ausente: Resumo Técnico não é um .docx válido.");

        XDocument doc;
        using (var s = docEntry.Open())
            doc = XDocument.Load(s);

        var body = doc.Root?.Element(W + "body")
            ?? throw new InvalidOperationException("Corpo do Resumo Técnico não encontrado.");

        var gabarito = new Dictionary<int, char>();
        bool tabelaEncontrada = false;

        foreach (var tbl in body.Elements(W + "tbl"))
        {
            var linhas = new List<List<string>>();
            foreach (var tr in tbl.Elements(W + "tr"))
            {
                var celulas = tr.Elements(W + "tc")
                    .Select(tc => string.Join(" ", tc.Elements(W + "p").Select(TextoParagrafo)).Trim())
                    .ToList();
                if (celulas.Count > 0)
                    linhas.Add(celulas);
            }

            if (linhas.Count == 0) continue;

            // Identifica todos os pares de colunas (Item, Gab./Chave) pelo cabeçalho.
            // Formato A (Engenharia): "Item | Gab. | Item | Gab. | Item | Gab." — 3 pares por linha.
            // Formato B (Agronomia): "Questão | ID | Chave | Dificuldade | ..." — 1 par, com colunas extras.
            var cab = linhas[0];
            var paresColunas = new List<(int ColItem, int ColResposta)>();
            for (int c = 0; c < cab.Count; c++)
            {
                if (!RxCabecalhoItem.IsMatch(cab[c].Trim())) continue;

                // A coluna de resposta pode não ser a imediatamente seguinte (ex.: "Questão | ID | Chave").
                for (int c2 = c + 1; c2 < cab.Count; c2++)
                {
                    if (RxCabecalhoResposta.IsMatch(cab[c2].Trim()))
                    {
                        paresColunas.Add((c, c2));
                        break;
                    }
                    // Para no próximo "Item"/"Questão": evita casar com a coluna de resposta de outro par.
                    if (RxCabecalhoItem.IsMatch(cab[c2].Trim())) break;
                }
            }

            if (paresColunas.Count == 0) continue;

            tabelaEncontrada = true;
            for (int r = 1; r < linhas.Count; r++)
            {
                var celulas = linhas[r];
                foreach (var (colItem, colResposta) in paresColunas)
                {
                    if (colItem >= celulas.Count || colResposta >= celulas.Count) continue;

                    var itemTexto = celulas[colItem].Trim();
                    var gabTexto = celulas[colResposta].Trim();
                    if (itemTexto.Length == 0 && gabTexto.Length == 0) continue;

                    if (!int.TryParse(itemTexto, out var numero))
                    {
                        avisos.Add($"Gabarito: célula '{itemTexto}' na linha {r + 1} não é um número de item válido — ignorada.");
                        continue;
                    }

                    var mLetra = RxLetraGabarito.Match(gabTexto);
                    if (!mLetra.Success)
                    {
                        avisos.Add($"Gabarito: item {numero} tem valor '{gabTexto}' que não é uma letra A-E válida — ignorado.");
                        continue;
                    }

                    var letra = char.ToUpperInvariant(mLetra.Groups[1].Value[0]);
                    if (!gabarito.TryAdd(numero, letra))
                        avisos.Add($"Gabarito: item {numero} aparece mais de uma vez na tabela — mantida a primeira ocorrência ({gabarito[numero]}).");
                }
            }
        }

        if (!tabelaEncontrada)
            avisos.Add("Não foi possível localizar a tabela de gabarito consolidado no Resumo Técnico (cabeçalho com 'Item'/'Questão' e 'Gab.'/'Chave' não encontrado). Nenhum gabarito foi pré-preenchido.");

        return gabarito;
    }

    // ───────────────────────── Etapa 2: CONFIRMAR (grava) ─────────────────────────

    /// <summary>
    /// Grava as questões revisadas na prova, criando Questao + ProvaQuestao com a
    /// ordem sequencial continuando a numeração já existente na prova.
    /// </summary>
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
        foreach (var item in itens)
        {
            var questao = new Questao
            {
                Enunciado = string.IsNullOrWhiteSpace(item.Enunciado) ? null : item.Enunciado!.Trim(),
                TextoBase = string.IsNullOrWhiteSpace(item.TextoBase) ? null : item.TextoBase!.Trim(),
                Comando = item.Comando.Trim(),
                ImagemUrl = string.IsNullOrWhiteSpace(item.ImagemUrl) ? null : item.ImagemUrl,
                DescricaoAcessibilidade = string.IsNullOrWhiteSpace(item.DescricaoAcessibilidade) ? null : item.DescricaoAcessibilidade,
                AlternativaA = item.AlternativaA?.Trim() ?? string.Empty,
                AlternativaB = item.AlternativaB?.Trim() ?? string.Empty,
                AlternativaC = Limpar(item.AlternativaC),
                AlternativaD = Limpar(item.AlternativaD),
                AlternativaE = Limpar(item.AlternativaE),
                NumeroAlternativas = Math.Clamp(item.NumeroAlternativas, 2, 5),
                AlternativaCorreta = item.AlternativaCorreta ?? 'A',
                Fonte = Limpar(item.Fonte),
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

    // ───────────────────────────── Parsing ─────────────────────────────

    /// <summary>Lê o .docx e devolve as questões estruturadas (via regex sobre o texto).</summary>
    public List<QuestaoImportada> Parse(Stream docx)
    {
        // ZipArchive exige stream com seek; IFormFile pode não ter. Copiamos.
        using var ms = new MemoryStream();
        docx.CopyTo(ms);
        ms.Position = 0;

        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        var blocos = LerBlocos(zip);
        return Segmentar(blocos);
    }

    private List<Bloco> LerBlocos(ZipArchive zip)
    {
        var docEntry = zip.GetEntry("word/document.xml")
            ?? throw new InvalidOperationException("word/document.xml ausente: arquivo não é um .docx válido.");

        XDocument doc;
        using (var s = docEntry.Open())
            doc = XDocument.Load(s);

        var rels = LerRelacionamentos(zip);
        var body = doc.Root?.Element(W + "body")
            ?? throw new InvalidOperationException("Corpo do documento não encontrado.");

        var blocos = new List<Bloco>();

        foreach (var el in body.Elements())
        {
            if (el.Name == W + "p")
            {
                var texto = TextoParagrafo(el);
                var imagens = new List<byte[]>();
                var extensoes = new List<string>();

                foreach (var blip in el.Descendants(A + "blip"))
                {
                    var relId = (string?)blip.Attribute(R + "embed");
                    if (relId != null && rels.TryGetValue(relId, out var alvo))
                    {
                        var bytes = LerEntrada(zip, "word/" + alvo);
                        if (bytes != null)
                        {
                            imagens.Add(bytes);
                            extensoes.Add(Path.GetExtension(alvo).ToLowerInvariant());
                        }
                    }
                }

                blocos.Add(new Bloco
                {
                    Tipo = TipoBloco.Paragrafo,
                    Texto = texto,
                    Imagens = imagens,
                    ImagemExtensoes = extensoes
                });
            }
            else if (el.Name == W + "tbl")
            {
                var linhas = new List<List<string>>();
                foreach (var tr in el.Elements(W + "tr"))
                {
                    var celulas = new List<string>();
                    foreach (var tc in tr.Elements(W + "tc"))
                    {
                        var texto = string.Join(" ", tc.Elements(W + "p").Select(TextoParagrafo)).Trim();
                        celulas.Add(texto);
                    }
                    if (celulas.Count > 0)
                        linhas.Add(celulas);
                }

                if (linhas.Count > 0)
                    blocos.Add(new Bloco { Tipo = TipoBloco.Tabela, Tabela = linhas });
            }
        }

        return blocos;
    }

    private Dictionary<string, string> LerRelacionamentos(ZipArchive zip)
    {
        var map = new Dictionary<string, string>();
        var entry = zip.GetEntry("word/_rels/document.xml.rels");
        if (entry == null) return map;

        XDocument rels;
        using (var s = entry.Open())
            rels = XDocument.Load(s);

        foreach (var rel in rels.Root!.Elements(PKG + "Relationship"))
        {
            var id = (string?)rel.Attribute("Id");
            var target = (string?)rel.Attribute("Target");
            if (id != null && target != null)
                map[id] = target.TrimStart('/');
        }
        return map;
    }

    private static string TextoParagrafo(XElement paragrafo)
    {
        var sb = new StringBuilder();
        foreach (var node in paragrafo.Descendants())
        {
            if (node.Name == W + "t")
                sb.Append(node.Value);
            else if (node.Name == W + "tab")
                sb.Append('\t');
            else if (node.Name == W + "br" || node.Name == W + "cr")
                sb.Append(' ');
        }
        return sb.ToString().Trim();
    }

    private static byte[]? LerEntrada(ZipArchive zip, string caminho)
    {
        var entry = zip.GetEntry(caminho);
        if (entry == null) return null;
        using var s = entry.Open();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    private List<QuestaoImportada> Segmentar(List<Bloco> blocos)
    {
        var questoes = new List<QuestaoImportada>();
        QuestaoImportada? atual = null;
        var buffer = new List<Bloco>();

        void Finalizar()
        {
            if (atual != null)
            {
                PreencherQuestao(atual, buffer);
                questoes.Add(atual);
            }
        }

        foreach (var b in blocos)
        {
            if (b.Tipo == TipoBloco.Paragrafo)
            {
                var m = RxQuestao.Match(b.Texto);
                if (m.Success)
                {
                    Finalizar();
                    atual = new QuestaoImportada { Numero = int.Parse(m.Groups[1].Value) };
                    buffer = new List<Bloco>();
                    continue; // o título da questão não entra no conteúdo
                }
            }

            if (atual != null)
                buffer.Add(b);
        }

        Finalizar();
        
        // Pós-processamento: detectar texto-base compartilhado e replicar para o grupo
        ProcessarTextosBase(questoes);
        
        return questoes;
    }
    
    /// <summary>
    /// Detecta questões com "Texto-base para as questões X a Y" no enunciado.
    /// Move o texto-base completo para TextoBase da primeira questão do grupo
    /// e limpa o enunciado das demais questões do grupo.
    /// </summary>
    private void ProcessarTextosBase(List<QuestaoImportada> questoes)
    {
        foreach (var q in questoes)
        {
            if (string.IsNullOrWhiteSpace(q.Enunciado)) continue;
            
            var match = RxTextoBase.Match(q.Enunciado);
            if (!match.Success) continue;
            
            // Encontrou "Texto-base para as questões X a Y"
            if (match.Groups[2].Success && match.Groups[3].Success)
            {
                int inicio = int.Parse(match.Groups[2].Value);
                int fim = int.Parse(match.Groups[3].Value);
                
                // Garante que a questão atual está no intervalo
                if (q.Numero < inicio || q.Numero > fim) continue;
                
                // Se for a primeira questão do grupo, mantém o enunciado completo
                if (q.Numero == inicio)
                {
                    q.TextoBase = q.Enunciado;
                    continue;
                }
                
                // Se for questão seguinte do grupo, limpa o enunciado
                if (q.Numero > inicio && q.Numero <= fim)
                {
                    q.Enunciado = null;
                }
            }
        }
    }

    private void PreencherQuestao(QuestaoImportada q, List<Bloco> blocos)
    {
        int idxPrimeiraAlt = blocos.FindIndex(b =>
            b.Tipo == TipoBloco.Paragrafo && RxAlternativa.IsMatch(b.Texto));

        int fimCabecalho = idxPrimeiraAlt >= 0 ? idxPrimeiraAlt : blocos.Count;

        // Alternativas: cada uma é um parágrafo isolado começando por "A)".."E)".
        if (idxPrimeiraAlt >= 0)
        {
            for (int i = idxPrimeiraAlt; i < blocos.Count; i++)
            {
                var b = blocos[i];
                if (b.Tipo != TipoBloco.Paragrafo) continue;
                var m = RxAlternativa.Match(b.Texto);
                if (m.Success)
                    q.Alternativas.Add(new AlternativaImportada(m.Groups[1].Value[0], m.Groups[2].Value.Trim()));
            }
        }

        // Comando = último parágrafo de texto (não vazio, não fonte/acessibilidade)
        // antes da primeira alternativa. Na prova, é a linha que termina em "?".
        int idxComando = -1;
        for (int i = fimCabecalho - 1; i >= 0; i--)
        {
            var b = blocos[i];
            if (b.Tipo != TipoBloco.Paragrafo) continue;
            if (string.IsNullOrWhiteSpace(b.Texto)) continue;
            if (RxFonte.IsMatch(b.Texto) || RxAcessibilidade.IsMatch(b.Texto)) continue;
            idxComando = i;
            break;
        }

        if (idxComando >= 0)
            q.Comando = blocos[idxComando].Texto;

        var enunciado = new List<string>();
        for (int i = 0; i < fimCabecalho; i++)
        {
            if (i == idxComando) continue;
            var b = blocos[i];

            if (b.Tipo == TipoBloco.Tabela)
            {
                q.Tabelas.Add(b.Tabela!);
                continue;
            }

            if (b.Imagens.Count > 0)
            {
                for (int k = 0; k < b.Imagens.Count; k++)
                {
                    q.Imagens.Add(b.Imagens[k]);
                    q.ImagemExtensoes.Add(k < b.ImagemExtensoes.Count ? b.ImagemExtensoes[k] : ".png");
                }
            }

            if (string.IsNullOrWhiteSpace(b.Texto)) continue;

            var mFonte = RxFonte.Match(b.Texto);
            if (mFonte.Success) { q.Fonte = Truncar(mFonte.Groups[1].Value.Trim(), 500); continue; }

            var mAcc = RxAcessibilidade.Match(b.Texto);
            if (mAcc.Success) { q.DescricaoAcessibilidade = mAcc.Groups[1].Value.Trim(); continue; }

            // Legenda "Figura N — ..." e texto introdutório entram no enunciado.
            enunciado.Add(b.Texto);
        }

        q.Enunciado = enunciado.Count > 0 ? string.Join("\n\n", enunciado) : null;
    }

    // ───────────────────────── Materialização de imagens ─────────────────────────

    /// <summary>Grava a figura (PNG extraído) ou a tabela (SVG renderizado) em wwwroot.</summary>
    private (string? Url, bool EhTabela) MaterializarImagem(QuestaoImportada q, List<string> avisos)
    {
        var pasta = Path.Combine(_environment.WebRootPath, "uploads", "questoes");
        Directory.CreateDirectory(pasta);

        // Figura (diagrama/gráfico) tem prioridade quando ambas existirem no mesmo item.
        if (q.Imagens.Count > 0)
        {
            var ext = q.ImagemExtensoes.FirstOrDefault() is { Length: > 0 } e ? e : ".png";
            var nome = $"{Guid.NewGuid()}{ext}";
            File.WriteAllBytes(Path.Combine(pasta, nome), q.Imagens[0]);
            if (q.Tabelas.Count > 0)
                avisos.Add($"Questão {q.Numero}: possui figura E tabela; apenas a figura foi anexada (slot único de imagem).");
            return ($"/uploads/questoes/{nome}", false);
        }

        if (q.Tabelas.Count > 0)
        {
            var svg = RenderTabelasSvg(q.Tabelas);
            var nome = $"{Guid.NewGuid()}.svg";
            File.WriteAllText(Path.Combine(pasta, nome), svg, new UTF8Encoding(false));
            return ($"/uploads/questoes/{nome}", true);
        }

        return (null, false);
    }

    // ───────────────────────── Renderização de tabela → SVG ─────────────────────────

    private const double FonteTamanho = 14;
    private const double LarguraCharAprox = 7.5;
    private const int PadX = 10;
    private const int PadY = 7;
    private const int AlturaLinha = 19;
    private const int MaxColChars = 36;

    private static string RenderTabelasSvg(List<List<List<string>>> tabelas)
    {
        var fragmentos = tabelas.Select(RenderTabelaUnica).ToList();

        double largura = fragmentos.Max(f => f.Largura);
        double gap = 16;
        double altura = fragmentos.Sum(f => f.Altura) + gap * (fragmentos.Count - 1);

        var sb = new StringBuilder();
        sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{Arr(largura)}\" height=\"{Arr(altura)}\" ");
        sb.Append($"viewBox=\"0 0 {Arr(largura)} {Arr(altura)}\" font-family=\"'Segoe UI',Arial,sans-serif\" font-size=\"{Arr(FonteTamanho)}\">");
        sb.Append($"<rect x=\"0\" y=\"0\" width=\"{Arr(largura)}\" height=\"{Arr(altura)}\" fill=\"#ffffff\"/>");

        double offsetY = 0;
        foreach (var f in fragmentos)
        {
            sb.Append($"<g transform=\"translate(0,{Arr(offsetY)})\">{f.Svg}</g>");
            offsetY += f.Altura + gap;
        }

        sb.Append("</svg>");
        return sb.ToString();
    }

    private static (string Svg, double Largura, double Altura) RenderTabelaUnica(List<List<string>> linhas)
    {
        int colunas = linhas.Max(l => l.Count);
        foreach (var l in linhas)
            while (l.Count < colunas) l.Add(string.Empty);

        var colChars = new int[colunas];
        for (int c = 0; c < colunas; c++)
        {
            int maxLen = linhas.Max(l => l[c].Length);
            colChars[c] = Math.Clamp(maxLen, 3, MaxColChars);
        }

        var colLargura = new double[colunas];
        for (int c = 0; c < colunas; c++)
            colLargura[c] = colChars[c] * LarguraCharAprox + 2 * PadX;

        var celulasLinhas = new List<List<string>>[linhas.Count];
        var alturaLinha = new double[linhas.Count];
        for (int r = 0; r < linhas.Count; r++)
        {
            celulasLinhas[r] = new List<List<string>>();
            int maxLinhasCelula = 1;
            for (int c = 0; c < colunas; c++)
            {
                var wrap = Quebrar(linhas[r][c], colChars[c]);
                celulasLinhas[r].Add(wrap);
                maxLinhasCelula = Math.Max(maxLinhasCelula, wrap.Count);
            }
            alturaLinha[r] = maxLinhasCelula * AlturaLinha + 2 * PadY;
        }

        double larguraTotal = colLargura.Sum();
        double alturaTotal = alturaLinha.Sum();

        var sb = new StringBuilder();
        double y = 0;
        for (int r = 0; r < linhas.Count; r++)
        {
            double x = 0;
            bool cabecalho = r == 0;
            for (int c = 0; c < colunas; c++)
            {
                var fill = cabecalho ? "#eef2f7" : "#ffffff";
                sb.Append($"<rect x=\"{Arr(x)}\" y=\"{Arr(y)}\" width=\"{Arr(colLargura[c])}\" height=\"{Arr(alturaLinha[r])}\" fill=\"{fill}\" stroke=\"#c9ced6\" stroke-width=\"1\"/>");

                var wrap = celulasLinhas[r][c];
                for (int li = 0; li < wrap.Count; li++)
                {
                    double textY = y + PadY + li * AlturaLinha + FonteTamanho;
                    var peso = cabecalho ? " font-weight=\"600\"" : string.Empty;
                    sb.Append($"<text x=\"{Arr(x + PadX)}\" y=\"{Arr(textY)}\" fill=\"#1f2933\"{peso}>{EscaparXml(wrap[li])}</text>");
                }
                x += colLargura[c];
            }
            y += alturaLinha[r];
        }

        return (sb.ToString(), larguraTotal, alturaTotal);
    }

    private static List<string> Quebrar(string texto, int maxChars)
    {
        texto = texto.Trim();
        if (texto.Length == 0) return new List<string> { string.Empty };
        if (texto.Length <= maxChars) return new List<string> { texto };

        var linhas = new List<string>();
        var atual = new StringBuilder();
        foreach (var palavra in texto.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (atual.Length == 0)
                atual.Append(palavra);
            else if (atual.Length + 1 + palavra.Length <= maxChars)
                atual.Append(' ').Append(palavra);
            else
            {
                linhas.Add(atual.ToString());
                atual.Clear();
                atual.Append(palavra);
            }

            while (atual.Length > maxChars)
            {
                linhas.Add(atual.ToString(0, maxChars));
                var resto = atual.ToString(maxChars, atual.Length - maxChars);
                atual.Clear();
                atual.Append(resto);
            }
        }
        if (atual.Length > 0) linhas.Add(atual.ToString());
        return linhas;
    }

    private static string EscaparXml(string s) => s
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("\"", "&quot;")
        .Replace("'", "&apos;");

    private static string Arr(double v) => Math.Round(v, 1).ToString(System.Globalization.CultureInfo.InvariantCulture);

    // ───────────────────────────── Helpers ─────────────────────────────

    private static string Alt(QuestaoImportada q, char letra)
        => q.Alternativas.FirstOrDefault(a => a.Letra == letra)?.Texto ?? string.Empty;

    private static string? AltOpcional(QuestaoImportada q, char letra)
    {
        var t = q.Alternativas.FirstOrDefault(a => a.Letra == letra)?.Texto;
        return string.IsNullOrWhiteSpace(t) ? null : t;
    }

    private static string? Limpar(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string Truncar(string s, int max) => s.Length <= max ? s : s[..max];

    // ───────────────────────────── Tipos internos ─────────────────────────────

    private enum TipoBloco { Paragrafo, Tabela }

    private sealed class Bloco
    {
        public TipoBloco Tipo { get; set; }
        public string Texto { get; set; } = string.Empty;
        public List<byte[]> Imagens { get; set; } = new();
        public List<string> ImagemExtensoes { get; set; } = new();
        public List<List<string>>? Tabela { get; set; }
    }
}

/// <summary>Alternativa (letra + texto) extraída da questão.</summary>
public sealed record AlternativaImportada(char Letra, string Texto);

/// <summary>Questão estruturada extraída do .docx (antes de virar entidade).</summary>
public sealed class QuestaoImportada
{
    public int Numero { get; set; }
    public string? Enunciado { get; set; }
    public string? TextoBase { get; set; }  // Texto-base compartilhado (apenas na 1ª questão do grupo)
    public string Comando { get; set; } = string.Empty;
    public string? DescricaoAcessibilidade { get; set; }
    public string? Fonte { get; set; }
    public List<AlternativaImportada> Alternativas { get; } = new();
    public List<byte[]> Imagens { get; } = new();
    public List<string> ImagemExtensoes { get; } = new();
    public List<List<List<string>>> Tabelas { get; } = new();
}
