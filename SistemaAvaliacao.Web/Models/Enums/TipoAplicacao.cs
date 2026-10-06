namespace SistemaAvaliacao.Web.Models.Enums;

/// <summary>
/// Define o tipo de aplicação da prova: física (com folhas de resposta) ou virtual (online).
/// </summary>
public enum TipoAplicacao
{
    /// <summary>Prova aplicada fisicamente com folhas de resposta digitalizadas</summary>
    Fisica = 1,
    
    /// <summary>Prova aplicada online através do portal</summary>
    Virtual = 2
}
