using SistemaAvaliacao.Web.Models.Enums;

namespace SistemaAvaliacao.Web.Services;

/// <summary>
/// Regras de recuperação de lotes de correção presos. Um lote "preso" é aquele que
/// ficou em <see cref="StatusProcessamentoCorrecao.Processando"/> porque o worker morreu
/// no meio do processamento (reinício do container, OOM) — estado do qual ele nunca
/// sairia sozinho. Esta regra é PURA (sem acesso a dados) para ser testável e reutilizável
/// pelo worker. Ver docs/omr-hardening.md.
/// </summary>
public static class RecuperacaoLoteCorrecao
{
    /// <summary>Limite padrão: generoso para não interromper um lote grande ainda em andamento.</summary>
    public static readonly TimeSpan LimitePadrao = TimeSpan.FromMinutes(15);

    /// <summary>
    /// True se o lote deve ser devolvido a Recebido: está em Processando e começou antes
    /// do corte (ou nunca registrou início — estado órfão de dado antigo).
    /// </summary>
    public static bool EstaPreso(
        StatusProcessamentoCorrecao status,
        DateTime? processamentoIniciadoEm,
        DateTime agoraUtc,
        TimeSpan limite)
    {
        if (status != StatusProcessamentoCorrecao.Processando)
            return false;
        return processamentoIniciadoEm == null
            || processamentoIniciadoEm < agoraUtc - limite;
    }
}
