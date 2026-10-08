using System.ComponentModel.DataAnnotations;
using SistemaAvaliacao.Web.Models.Enums;

namespace SistemaAvaliacao.Web.Models.Entities;

public class LoteCorrecao
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid ProvaId { get; set; }
    public Prova Prova { get; set; } = null!;

    [Required, StringLength(260)]
    public string NomeArquivo { get; set; } = string.Empty;

    [Required, StringLength(500)]
    public string CaminhoArquivo { get; set; } = string.Empty;

    public StatusProcessamentoCorrecao Status { get; set; } = StatusProcessamentoCorrecao.Recebido;
    public int TotalFolhas { get; set; }
    public int FolhasProcessadas { get; set; }
    public string? Erro { get; set; }
    
    /// <summary>Confiança média do OMR para este lote (0-1)</summary>
    public decimal? ConfiancaMedia { get; set; }
    
    /// <summary>Diretório contendo imagens anotadas do OMR para debug</summary>
    public string? CaminhoDebugOmr { get; set; }
    
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
    public DateTime? FinalizadoEm { get; set; }

    public ICollection<FolhaResposta> Folhas { get; set; } = [];
}
