using SistemaAvaliacao.Web.Models.Enums;
using SistemaAvaliacao.Web.Services;
using Xunit;

namespace SistemaAvaliacao.Tests;

/// <summary>
/// Testes da regra de recuperação de lote "preso" em Processando — o caso de o worker
/// morrer no meio do processamento (reinício do container, OOM), deixando o lote num
/// estado do qual ele nunca sairia sozinho. A regra devolve o lote a Recebido após um
/// limite de tempo. Ver docs/omr-hardening.md.
/// </summary>
public class LotePresoTests
{
    private static readonly TimeSpan Limite = TimeSpan.FromMinutes(15);
    private static readonly DateTime Agora = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Lote_processando_ha_muito_tempo_esta_preso()
    {
        var iniciadoHa20min = Agora.AddMinutes(-20);
        Assert.True(RecuperacaoLoteCorrecao.EstaPreso(
            StatusProcessamentoCorrecao.Processando, iniciadoHa20min, Agora, Limite));
    }

    [Fact]
    public void Lote_processando_ha_pouco_tempo_NAO_esta_preso()
    {
        // Lote grande ainda legitimamente em andamento não pode ser interrompido.
        var iniciadoHa2min = Agora.AddMinutes(-2);
        Assert.False(RecuperacaoLoteCorrecao.EstaPreso(
            StatusProcessamentoCorrecao.Processando, iniciadoHa2min, Agora, Limite));
    }

    [Fact]
    public void Lote_processando_sem_timestamp_e_tratado_como_preso()
    {
        // Sem início registrado (ex.: dado antigo) + status Processando é um estado órfão:
        // melhor reprocessar do que deixar parado para sempre.
        Assert.True(RecuperacaoLoteCorrecao.EstaPreso(
            StatusProcessamentoCorrecao.Processando, null, Agora, Limite));
    }

    [Theory]
    [InlineData(StatusProcessamentoCorrecao.Recebido)]
    [InlineData(StatusProcessamentoCorrecao.AguardandoRevisao)]
    [InlineData(StatusProcessamentoCorrecao.Confirmado)]
    [InlineData(StatusProcessamentoCorrecao.Erro)]
    public void Lotes_em_outros_estados_nunca_estao_presos(StatusProcessamentoCorrecao status)
    {
        // Mesmo com timestamp antigo, só Processando é candidato a recuperação.
        var antigo = Agora.AddHours(-10);
        Assert.False(RecuperacaoLoteCorrecao.EstaPreso(status, antigo, Agora, Limite));
    }
}
