namespace SistemaAvaliacao.Web.Services.Omr;

/// <summary>Status de classificação de uma questão.</summary>
public enum QuestionStatus
{
    /// <summary>Exatamente uma alternativa marcada com clareza.</summary>
    Valida,
    /// <summary>Nenhuma alternativa marcada.</summary>
    EmBranco,
    /// <summary>Duas ou mais alternativas marcadas.</summary>
    Dupla,
    /// <summary>Marcação fraca/parcial/ambígua — provável rasura. Requer revisão humana.</summary>
    RasuraDuvidosa
}

/// <summary>Medição e classificação de uma única questão.</summary>
public sealed class QuestionResult
{
    /// <summary>Número da questão (1..100).</summary>
    public required int Number { get; init; }

    /// <summary>Alternativa detectada (A..E) quando <see cref="Status"/> == Valida; caso contrário null.</summary>
    public char? DetectedOption { get; init; }

    public required QuestionStatus Status { get; init; }

    /// <summary>Confiança de 0..1 da decisão.</summary>
    public required double Confidence { get; init; }

    /// <summary>Percentual de preenchimento (0..1) de cada bolha A..E, na ordem.</summary>
    public required double[] FillRatios { get; init; }
}

/// <summary>Resultado completo da leitura de uma folha (uma página/aluno).</summary>
public sealed class SheetReadResult
{
    /// <summary>True se os 4 marcadores foram encontrados e a folha foi normalizada.</summary>
    public required bool Aligned { get; init; }

    /// <summary>Conteúdo bruto do QR (ex.: "exam=123;student=456"), ou null se não decodificado.</summary>
    public string? QrPayload { get; init; }

    public int? ExamId { get; init; }
    public int? StudentId { get; init; }

    public required IReadOnlyList<QuestionResult> Questions { get; init; }

    /// <summary>Mensagens de diagnóstico (marcadores faltando, QR ilegível, etc.).</summary>
    public List<string> Warnings { get; init; } = new();
}
