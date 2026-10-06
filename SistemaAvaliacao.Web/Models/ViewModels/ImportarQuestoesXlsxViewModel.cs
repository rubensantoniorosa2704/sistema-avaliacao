using System.ComponentModel.DataAnnotations;

namespace SistemaAvaliacao.Web.Models.ViewModels;

/// <summary>
/// Uma questão importada a partir da planilha XLSX, pronta para revisão/edição pelo Admin antes
/// da gravação definitiva. Preenchida por XlsxProvaImportService.PrepararPreview
/// e reenviada (com eventuais correções) para XlsxProvaImportService.ConfirmarAsync.
/// </summary>
public class ImportarQuestaoItem
{
    /// <summary>Número da questão no documento original (só para referência na tela).</summary>
    public int Numero { get; set; }

    public string? Enunciado { get; set; }
    
    /// <summary>Texto-base compartilhado entre questões (preenchido apenas na primeira questão do grupo).</summary>
    public string? TextoBase { get; set; }

    [Required(ErrorMessage = "Comando é obrigatório")]
    public string Comando { get; set; } = string.Empty;

    /// <summary>Caminho relativo em wwwroot (figura extraída ou tabela renderizada em SVG).</summary>
    public string? ImagemUrl { get; set; }

    /// <summary>True quando ImagemUrl é uma tabela renderizada (não uma figura do documento).</summary>
    public bool ImagemEhTabela { get; set; }

    public string? DescricaoAcessibilidade { get; set; }

    public string? Fonte { get; set; }

    [Required(ErrorMessage = "Alternativa A é obrigatória")]
    public string AlternativaA { get; set; } = string.Empty;

    [Required(ErrorMessage = "Alternativa B é obrigatória")]
    public string AlternativaB { get; set; } = string.Empty;

    public string? AlternativaC { get; set; }
    public string? AlternativaD { get; set; }
    public string? AlternativaE { get; set; }

    [Range(2, 5)]
    public int NumeroAlternativas { get; set; } = 4;

    /// <summary>Preenchida a partir da planilha (gabarito). Null quando não foi possível casar.</summary>
    [RegularExpression(@"^[A-E]$", ErrorMessage = "Alternativa correta deve ser A, B, C, D ou E")]
    public char? AlternativaCorreta { get; set; }

    /// <summary>True quando AlternativaCorreta veio automaticamente da planilha.</summary>
    public bool GabaritoAutomatico { get; set; }

    /// <summary>Não gravar essa questão na confirmação (Admin pode descartar itens malformados).</summary>
    public bool Incluir { get; set; } = true;

    /// <summary>Problemas detectados automaticamente (exibidos como alerta na tela de revisão).</summary>
    public List<string> Problemas { get; } = new();
}

/// <summary>ViewModel da tela de revisão/edição do preview de importação.</summary>
public class ImportarQuestoesXlsxViewModel
{
    public Guid ProvaId { get; set; }
    public string NomeProva { get; set; } = string.Empty;
    public string NomeArquivo { get; set; } = string.Empty;
    public string NomeArquivoGabarito { get; set; } = string.Empty;

    public List<string> Avisos { get; set; } = new();
    public List<ImportarQuestaoItem> Itens { get; set; } = new();
}
