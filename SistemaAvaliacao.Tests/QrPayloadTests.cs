using SistemaAvaliacao.Web.Services.Omr;
using Xunit;

namespace SistemaAvaliacao.Tests;

/// <summary>
/// Testes do payload do QR da folha de resposta. A identidade da folha é AUTORITATIVA
/// pelo token (segredo único por folha), não por um id derivado do Guid — isso fecha
/// os vetores de colisão (acidental) e de forja de QR (maliciosa). Ver docs/omr-hardening.md.
/// </summary>
public class QrPayloadTests
{
    [Fact]
    public void Build_e_Parse_fazem_round_trip_do_token()
    {
        const string token = "DEADBEEF00112233445566778899AABB";
        var payload = CardGenerator.BuildQrPayload(examId: 42, token: token);

        var (examId, lido) = CardGenerator.ParseQrPayload(payload);

        Assert.Equal(42, examId);
        Assert.Equal(token, lido);
    }

    [Fact]
    public void Parse_do_payload_vazio_devolve_nulos()
    {
        var (examId, token) = CardGenerator.ParseQrPayload("");
        Assert.Null(examId);
        Assert.Null(token);
    }

    [Fact]
    public void Parse_do_formato_antigo_sem_token_nao_devolve_token()
    {
        // Payload v1 legado (student=<int>, sem token). Não há provas reais com esse
        // formato, mas garantimos que ele NÃO produz um token — o worker então o rejeita
        // em vez de casar uma folha por engano.
        var (examId, token) = CardGenerator.ParseQrPayload("exam=7;student=123;v=1");

        Assert.Equal(7, examId);
        Assert.Null(token);
    }

    [Fact]
    public void Parse_ignora_partes_malformadas_mas_recupera_o_token()
    {
        var (examId, token) = CardGenerator.ParseQrPayload("lixo;exam=9;;token=ABC123;v=2;ruido=x");

        Assert.Equal(9, examId);
        Assert.Equal("ABC123", token);
    }
}
