namespace SistemaAvaliacao.Web.Models.Enums;

public enum StatusResposta
{
    /// <summary>Uma alternativa marcada claramente</summary>
    Valida = 1,
    
    /// <summary>Nenhuma alternativa marcada</summary>
    EmBranco = 2,
    
    /// <summary>Duas ou mais alternativas marcadas (múltipla marcação)</summary>
    Dupla = 3,
    
    /// <summary>Marcação indefinida ou ambígua (requer revisão manual)</summary>
    RasuraDuvidosa = 4
}
