using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SistemaAvaliacao.Web.Controllers;
using SistemaAvaliacao.Web.Data;
using SistemaAvaliacao.Web.Models.Entities;
using SistemaAvaliacao.Web.Models.Enums;
using SistemaAvaliacao.Web.Services;
using Xunit;

namespace SistemaAvaliacao.Tests;

/// <summary>
/// Testes de comportamento do salvamento de resposta na FRONTEIRA do prazo da prova
/// (o "Buraco A"): uma resposta que o aluno marca nos últimos segundos pode chegar ao
/// servidor logo depois do DataFim e, hoje, ser descartada com "Prova encerrada".
///
/// Estes testes NÃO exigem JavaScript nem timing de navegador: exercem diretamente a
/// regra de domínio em ExameController.SalvarResposta, que é determinística.
///
/// O teste do caso "depois do prazo" documenta o comportamento ATUAL (resposta rejeitada).
/// Quando a correção for aplicada (ex.: tolerância de alguns segundos no servidor), este
/// teste deve ser atualizado para refletir a nova regra — e aí ele vira a prova de que a
/// correção funciona.
/// </summary>
public class SalvarRespostaNaFronteiraDoPrazoTests
{
    private static ApplicationDbContext NovoContexto()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static ExameController NovoController(ApplicationDbContext ctx)
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var questoesCache = new QuestoesProvaCache(cache, ctx);
        var tentativas = new TentativaExameService(ctx);
        return new ExameController(ctx, tentativas, questoesCache);
    }

    /// <summary>
    /// Monta uma prova VIRTUAL com um aluno em andamento e uma questão de 5 alternativas.
    /// A janela da prova é controlada pelos parâmetros para posicionar "agora" antes ou
    /// depois do DataFim.
    /// </summary>
    private static async Task<(Prova prova, ProvaAluno aluno, Questao questao)> SemearAsync(
        ApplicationDbContext ctx, DateTime dataInicio, DateTime dataFim)
    {
        var admin = new Usuario
        {
            Id = Guid.NewGuid(),
            Nome = "Admin",
            Email = "admin@teste.com",
            CPF = "00000000000",
            PasswordHash = "x",
            Perfil = PerfilUsuario.Admin
        };

        var aluno = new Usuario
        {
            Id = Guid.NewGuid(),
            Nome = "Aluno Teste",
            Email = "aluno@teste.com",
            CPF = "11111111111",
            PasswordHash = "x",
            Perfil = PerfilUsuario.Aluno
        };

        var questao = new Questao
        {
            Id = Guid.NewGuid(),
            Comando = "Qual a resposta?",
            AlternativaA = "Alternativa A",
            AlternativaB = "Alternativa B",
            AlternativaC = "Alternativa C",
            AlternativaD = "Alternativa D",
            AlternativaE = "Alternativa E",
            NumeroAlternativas = 5,
            AlternativaCorreta = 'A'
        };

        var prova = new Prova
        {
            Id = Guid.NewGuid(),
            Nome = "Prova Piloto",
            TipoAplicacao = TipoAplicacao.Virtual,
            NumeroAlternativas = 5,
            DataInicio = dataInicio,
            DataFim = dataFim,
            TokenPublico = Guid.NewGuid(),
            CriadoPorId = admin.Id
        };

        prova.ProvaQuestoes.Add(new ProvaQuestao
        {
            ProvaId = prova.Id,
            QuestaoId = questao.Id,
            Ordem = 1
        });

        var provaAluno = new ProvaAluno
        {
            ProvaId = prova.Id,
            AlunoId = aluno.Id,
            Status = StatusProvaAluno.EmAndamento,
            IniciadaEm = dataInicio,
            TokenTentativa = Guid.NewGuid()
        };

        ctx.Usuarios.AddRange(admin, aluno);
        ctx.Questoes.Add(questao);
        ctx.Provas.Add(prova);
        ctx.ProvaAlunos.Add(provaAluno);
        await ctx.SaveChangesAsync();

        return (prova, provaAluno, questao);
    }

    // Lê a propriedade "sucesso" de um JsonResult anônimo retornado pelo controller.
    private static bool LerSucesso(IActionResult resultado)
    {
        var json = Assert.IsType<JsonResult>(resultado);
        var valor = json.Value!;
        var prop = valor.GetType().GetProperty("sucesso", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(prop);
        return (bool)prop!.GetValue(valor)!;
    }

    private static string? LerErro(IActionResult resultado)
    {
        var json = Assert.IsType<JsonResult>(resultado);
        var valor = json.Value!;
        var prop = valor.GetType().GetProperty("erro", BindingFlags.Public | BindingFlags.Instance);
        return prop?.GetValue(valor) as string;
    }

    [Fact]
    public async Task Resposta_marcada_DENTRO_do_prazo_e_aceita_e_persistida()
    {
        // Prazo termina daqui a 1 hora: estamos confortavelmente dentro da janela.
        using var ctx = NovoContexto();
        var (prova, aluno, questao) = await SemearAsync(
            ctx,
            dataInicio: DateTime.UtcNow.AddHours(-1),
            dataFim: DateTime.UtcNow.AddHours(1));

        var controller = NovoController(ctx);

        var resultado = await controller.SalvarResposta(
            prova.TokenPublico, questao.Id, 'A', aluno.TokenTentativa!.Value);

        Assert.True(LerSucesso(resultado), "Resposta dentro do prazo deveria ser aceita.");

        var respostaSalva = await ctx.Respostas
            .FirstOrDefaultAsync(r => r.ProvaId == prova.Id && r.AlunoId == aluno.AlunoId && r.QuestaoId == questao.Id);
        Assert.NotNull(respostaSalva);
        Assert.Equal('A', respostaSalva!.AlternativaMarcada);
    }

    [Fact]
    public async Task Resposta_que_chega_DEPOIS_do_prazo_e_rejeitada_documenta_o_Buraco_A()
    {
        // A prova JÁ encerrou (DataFim no passado): simula a resposta "em voo" que o aluno
        // marcou nos últimos segundos (relógio do navegador) mas que chegou ao servidor
        // logo após o DataFim. Hoje o servidor rejeita e a resposta se perde.
        using var ctx = NovoContexto();
        var (prova, aluno, questao) = await SemearAsync(
            ctx,
            dataInicio: DateTime.UtcNow.AddHours(-2),
            dataFim: DateTime.UtcNow.AddSeconds(-2));

        var controller = NovoController(ctx);

        var resultado = await controller.SalvarResposta(
            prova.TokenPublico, questao.Id, 'A', aluno.TokenTentativa!.Value);

        // Comportamento ATUAL: rejeitada com "Prova encerrada". Esta é a evidência do Buraco A.
        Assert.False(LerSucesso(resultado), "Hoje o servidor rejeita resposta após o DataFim (Buraco A).");
        Assert.Equal("Prova encerrada", LerErro(resultado));

        // E, de fato, nada foi persistido: a resposta do aluno se perdeu.
        var respostaSalva = await ctx.Respostas
            .FirstOrDefaultAsync(r => r.ProvaId == prova.Id && r.AlunoId == aluno.AlunoId && r.QuestaoId == questao.Id);
        Assert.Null(respostaSalva);
    }
}
