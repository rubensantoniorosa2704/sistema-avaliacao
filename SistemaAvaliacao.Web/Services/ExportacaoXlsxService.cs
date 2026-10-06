using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using SistemaAvaliacao.Web.Data;
using SistemaAvaliacao.Web.Models.Enums;

namespace SistemaAvaliacao.Web.Services;

/// <summary>
/// Exporta resultados de prova (respostas confirmadas) em formato XLSX.
/// Inclui: Nome do aluno, respostas por questão, score e status.
/// </summary>
public class ExportacaoXlsxService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<ExportacaoXlsxService> _logger;

    public ExportacaoXlsxService(ApplicationDbContext context, ILogger<ExportacaoXlsxService> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>Gera planilha XLSX com resultados da prova.</summary>
    public async Task<(byte[] Conteudo, string NomeArquivo)?> GerarAsync(Guid provaId)
    {
        var prova = await _context.Provas
            .Include(p => p.ProvaQuestoes).ThenInclude(pq => pq.Questao)
            .Include(p => p.ProvaAlunos).ThenInclude(pa => pa.Aluno)
            .FirstOrDefaultAsync(p => p.Id == provaId);

        if (prova == null) return null;

        var questoes = prova.ProvaQuestoes
            .OrderBy(pq => pq.Ordem)
            .ToList();

        var alunosFinalizados = prova.ProvaAlunos
            .Where(pa => pa.Status == StatusProvaAluno.Finalizada)
            .OrderBy(pa => pa.Aluno.Nome, StringComparer.CurrentCulture)
            .ToList();

        if (alunosFinalizados.Count == 0)
            return null;

        // Respostas confirmadas
        var respostas = await _context.Respostas
            .Where(r => r.ProvaId == provaId)
            .ToListAsync();

        var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Respostas");

        // ─────────────── Cabeçalho ───────────────
        var headerRow = worksheet.Row(1);
        headerRow.Cell(1).Value = "Matrícula";
        headerRow.Cell(2).Value = "Aluno";
        headerRow.Cell(3).Value = "Email";
        headerRow.Cell(4).Value = "CPF";
        
        // Colunas de questões
        for (int i = 0; i < questoes.Count; i++)
        {
            headerRow.Cell(5 + i).Value = $"Q{questoes[i].Ordem:00}";
        }
        
        headerRow.Cell(5 + questoes.Count).Value = "Acertos";
        headerRow.Cell(6 + questoes.Count).Value = "Score (%)";
        headerRow.Cell(7 + questoes.Count).Value = "Status";

        // Formatar cabeçalho
        for (int col = 1; col <= 7 + questoes.Count; col++)
        {
            headerRow.Cell(col).Style.Font.Bold = true;
            headerRow.Cell(col).Style.Fill.BackgroundColor = XLColor.FromTheme(XLThemeColor.Accent1);
            headerRow.Cell(col).Style.Font.FontColor = XLColor.White;
            headerRow.Cell(col).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            headerRow.Cell(col).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        }

        // ─────────────── Dados ───────────────
        int rowNum = 2;
        foreach (var pa in alunosFinalizados)
        {
            var row = worksheet.Row(rowNum);
            
            row.Cell(1).Value = pa.Aluno.Matricula ?? "—";
            row.Cell(2).Value = pa.Aluno.Nome;
            row.Cell(3).Value = pa.Aluno.Email;
            row.Cell(4).Value = FormatarCpf(pa.Aluno.CPF);

            // Respostas por questão
            int acertos = 0;
            for (int i = 0; i < questoes.Count; i++)
            {
                var pq = questoes[i];
                var resposta = respostas.FirstOrDefault(r =>
                    r.ProvaId == provaId && r.AlunoId == pa.AlunoId && r.QuestaoId == pq.QuestaoId);

                var marcada = resposta?.AlternativaMarcada?.ToString() ?? "—";
                row.Cell(5 + i).Value = marcada;
                row.Cell(5 + i).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                if (resposta?.AlternativaMarcada == pq.Questao.AlternativaCorreta)
                    acertos++;
            }

            // Score
            var score = (double)acertos / questoes.Count;
            row.Cell(5 + questoes.Count).Value = acertos;
            row.Cell(5 + questoes.Count).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            
            row.Cell(6 + questoes.Count).Value = score;
            row.Cell(6 + questoes.Count).Style.NumberFormat.Format = "0.00%";
            row.Cell(6 + questoes.Count).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            row.Cell(7 + questoes.Count).Value = pa.Status.ToString();
            row.Cell(7 + questoes.Count).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            rowNum++;
        }

        // ─────────────── Ajustar larguras ───────────────
        worksheet.Column(1).Width = 12;
        worksheet.Column(2).Width = 20;
        worksheet.Column(3).Width = 20;
        worksheet.Column(4).Width = 15;
        for (int i = 5; i < 5 + questoes.Count; i++)
            worksheet.Column(i).Width = 6;
        worksheet.Column(5 + questoes.Count).Width = 8;
        worksheet.Column(6 + questoes.Count).Width = 12;
        worksheet.Column(7 + questoes.Count).Width = 12;

        // ─────────────── Gerar arquivo ───────────────
        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        var conteudo = stream.ToArray();

        var nomeArquivo = $"respostas-{Slug(prova.Nome)}-{DateTime.UtcNow:yyyyMMdd-HHmm}.xlsx";

        _logger.LogInformation("Planilha XLSX exportada: prova {ProvaId}, {Alunos} aluno(s), {Questoes} questão(ões).",
            provaId, alunosFinalizados.Count, questoes.Count);

        return (conteudo, nomeArquivo);
    }

    private static string FormatarCpf(string cpf)
    {
        if (cpf.Length != 11) return cpf;
        return $"{cpf[..3]}.{cpf[3..6]}.{cpf[6..9]}-{cpf[9..]}";
    }

    private static string Slug(string texto)
    {
        var limpo = new string(texto
            .Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-')
            .ToArray());

        while (limpo.Contains("--"))
            limpo = limpo.Replace("--", "-");

        limpo = limpo.Trim('-');
        return limpo.Length > 60 ? limpo[..60].Trim('-') : limpo;
    }
}
