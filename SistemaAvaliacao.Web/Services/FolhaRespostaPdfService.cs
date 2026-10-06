using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using OpenCvSharp;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SistemaAvaliacao.Web.Data;
using SistemaAvaliacao.Web.Models.Entities;
using SistemaAvaliacao.Web.Models.Enums;
using SistemaAvaliacao.Web.Services.Omr;

namespace SistemaAvaliacao.Web.Services;

/// <summary>
/// Gera folhas de resposta em PDF usando CardGenerator como ÚNICA fonte de verdade.
/// Cada folha é renderizada por CardGenerator.GenerateBlank() e embutida no PDF.
/// </summary>
public class FolhaRespostaPdfService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<FolhaRespostaPdfService> _logger;

    public FolhaRespostaPdfService(ApplicationDbContext context, ILogger<FolhaRespostaPdfService> logger)
    {
        _context = context;
        _logger = logger;
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public async Task<(byte[] Pdf, string NomeArquivo, int TotalFolhas)> GerarAsync(Guid provaId)
    {
        var prova = await _context.Provas
            .Include(p => p.ProvaAlunos).ThenInclude(pa => pa.Aluno)
            .FirstOrDefaultAsync(p => p.Id == provaId)
            ?? throw new InvalidOperationException("Prova não encontrada.");

        if (prova.ProvaAlunos.Count == 0)
            throw new InvalidOperationException("A prova não possui alunos vinculados.");

        var alunos = prova.ProvaAlunos.OrderBy(pa => pa.Aluno.Nome, StringComparer.CurrentCulture).ToList();
        
        // Buscar ou criar lote de folhas
        var lote = await _context.LotesCorrecao
            .Include(l => l.Folhas).ThenInclude(f => f.Aluno)
            .Where(l => l.ProvaId == provaId && l.CaminhoArquivo == "" && l.Folhas.Any())
            .OrderByDescending(l => l.CriadoEm)
            .FirstOrDefaultAsync();

        var folhas = new List<(FolhaResposta Folha, Usuario Aluno)>();
        
        if (lote == null)
        {
            lote = new LoteCorrecao
            {
                ProvaId = provaId,
                NomeArquivo = $"folhas-resposta-{provaId:N}.pdf",
                Status = StatusProcessamentoCorrecao.Recebido,
                TotalFolhas = alunos.Count
            };
            _context.LotesCorrecao.Add(lote);

            var numeroPagina = 1;
            foreach (var provaAluno in alunos)
            {
                var folha = new FolhaResposta
                {
                    LoteCorrecao = lote,
                    ProvaId = provaId,
                    AlunoId = provaAluno.AlunoId,
                    TokenIdentificacao = CriarTokenIdentificacao(),
                    NumeroPagina = numeroPagina++
                };
                _context.FolhasResposta.Add(folha);
                folhas.Add((folha, provaAluno.Aluno));
            }

            await _context.SaveChangesAsync();
        }
        else
        {
            if (lote.Folhas.Count != alunos.Count)
                throw new InvalidOperationException(
                    "As folhas existentes não correspondem à lista atual de alunos. " +
                    "Gere uma nova prova para emitir outro conjunto de QR Codes.");

            folhas.AddRange(lote.Folhas
                .OrderBy(f => f.NumeroPagina)
                .Select(f => (f, f.Aluno)));
        }

        // Gerar PDF com CardGenerator como única fonte de verdade
        var documento = Document.Create(container =>
        {
            var layout = new SheetLayout();
            var cardGen = new CardGenerator(layout);

            foreach (var (folha, aluno) in folhas)
            {
                // Criar StudentCardInfo com dados da folha
                var cardInfo = new StudentCardInfo
                {
                    ExamId = GuidToStableInt(prova.Id),
                    StudentId = GuidToStableInt(aluno.Id),
                    StudentName = aluno.Nome ?? "---",
                    ExamName = prova.Nome ?? "Prova",
                    OptionCount = Math.Clamp(prova.NumeroAlternativas, 4, 5)
                };

                // Gerar imagem via CardGenerator
                using var cardImage = cardGen.GenerateBlank(cardInfo);
                
                // Converter Mat para PNG bytes
                var pngBytes = cardImage.ImEncode(".png");

                // Adicionar página com a imagem
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(0);
                    page.Content().Image(pngBytes.ToArray());
                });
            }
        });

        using var stream = new MemoryStream();
        documento.GeneratePdf(stream);
        
        _logger.LogInformation(
            "Geradas {TotalFolhas} folhas de resposta (CardGenerator) para a prova {ProvaId}.",
            folhas.Count, 
            provaId);
        
        return (stream.ToArray(), lote.NomeArquivo, folhas.Count);
    }


    /// <summary>
    /// Gera um PDF de teste com bolhas já preenchidas (GenerateTestCard).
    /// Cria as FolhasResposta reais no banco — com os StudentIds corretos —
    /// para que o worker consiga associar cada página ao aluno correspondente.
    /// Questões 1-10: normal | 11-20: dupla | 21-30: rasura.
    /// </summary>
    public async Task<(byte[] Pdf, string NomeArquivo, int TotalFolhas)> GerarTesteAsync(Guid provaId)
    {
        var prova = await _context.Provas
            .Include(p => p.ProvaAlunos).ThenInclude(pa => pa.Aluno)
            .FirstOrDefaultAsync(p => p.Id == provaId)
            ?? throw new InvalidOperationException("Prova não encontrada.");

        if (prova.ProvaAlunos.Count == 0)
            throw new InvalidOperationException("A prova não possui alunos vinculados.");

        var alunos = prova.ProvaAlunos.OrderBy(pa => pa.Aluno.Nome, StringComparer.CurrentCulture).ToList();

        // Reutiliza ou cria o lote/folhas exatamente como GerarAsync faz
        var lote = await _context.LotesCorrecao
            .Include(l => l.Folhas).ThenInclude(f => f.Aluno)
            .Where(l => l.ProvaId == provaId && l.CaminhoArquivo == "" && l.Folhas.Any())
            .OrderByDescending(l => l.CriadoEm)
            .FirstOrDefaultAsync();

        var folhas = new List<(FolhaResposta Folha, Usuario Aluno)>();

        if (lote == null)
        {
            lote = new LoteCorrecao
            {
                ProvaId = provaId,
                NomeArquivo = $"folhas-resposta-{provaId:N}.pdf",
                Status = StatusProcessamentoCorrecao.Recebido,
                TotalFolhas = alunos.Count
            };
            _context.LotesCorrecao.Add(lote);

            var numeroPagina = 1;
            foreach (var provaAluno in alunos)
            {
                var folha = new FolhaResposta
                {
                    LoteCorrecao = lote,
                    ProvaId = provaId,
                    AlunoId = provaAluno.AlunoId,
                    TokenIdentificacao = CriarTokenIdentificacao(),
                    NumeroPagina = numeroPagina++
                };
                _context.FolhasResposta.Add(folha);
                folhas.Add((folha, provaAluno.Aluno));
            }

            await _context.SaveChangesAsync();
        }
        else
        {
            if (lote.Folhas.Count != alunos.Count)
                throw new InvalidOperationException(
                    "As folhas existentes não correspondem à lista atual de alunos. " +
                    "Gere uma nova prova para emitir outro conjunto de QR Codes.");

            folhas.AddRange(lote.Folhas
                .OrderBy(f => f.NumeroPagina)
                .Select(f => (f, f.Aluno)));
        }

        // Gera PDF com GenerateTestCard (bolhas pré-preenchidas para teste)
        var documento = Document.Create(container =>
        {
            var layout = new SheetLayout();
            var cardGen = new CardGenerator(layout);

            foreach (var (folha, aluno) in folhas)
            {
                var cardInfo = new StudentCardInfo
                {
                    ExamId = GuidToStableInt(prova.Id),
                    StudentId = GuidToStableInt(aluno.Id),
                    StudentName = aluno.Nome ?? "---",
                    ExamName = prova.Nome ?? "Prova",
                    OptionCount = Math.Clamp(prova.NumeroAlternativas, 4, 5)
                };

                using var cardImage = cardGen.GenerateTestCard(cardInfo);
                var pngBytes = cardImage.ImEncode(".png");

                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(0);
                    page.Content().Image(pngBytes.ToArray());
                });
            }
        });

        using var stream = new MemoryStream();
        documento.GeneratePdf(stream);

        _logger.LogInformation(
            "Geradas {TotalFolhas} folha(s) de TESTE (GenerateTestCard) para a prova {ProvaId}.",
            folhas.Count,
            provaId);

        return (stream.ToArray(), $"folhas-teste-{provaId:N}.pdf", folhas.Count);
    }

    private static string CriarTokenIdentificacao()
        => Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

    /// <summary>
    /// Converte um Guid para um int positivo de forma determinística entre processos.
    /// Usa os primeiros 4 bytes do Guid (big-endian), garantindo o mesmo valor
    /// no Web e no Worker independentemente do AppDomain ou processo.
    /// NÃO use GetHashCode() — não é estável entre processos no .NET Core.
    /// </summary>
    public static int GuidToStableInt(Guid guid)
    {
        var bytes = guid.ToByteArray();
        int raw = (bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3];
        return Math.Abs(raw == int.MinValue ? 0 : raw);
    }
}

