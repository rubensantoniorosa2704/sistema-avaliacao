using System.Reflection;
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
/// Testes do endpoint TempoRestante, que fornece o tempo autoritativo do servidor para o
/// cliente sincronizar o cronômetro. Garante que o relógio da máquina do aluno não pode
/// esticar nem encurtar a prova: a verdade é sempre DataFim - UtcNow no servidor.
/// </summary>
public class TempoRestanteTests
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

    private static async Task<(Prova prova, ProvaAluno aluno)> SemearAsync(
        ApplicationDbContext ctx, DateTime dataInicio, DateTime dataFim,
        StatusProvaAluno status = StatusProvaAluno.EmAndamento)
    {
        var admin = new Usuario
        {
            Id = Guid.NewGuid(), Nome = "Admin", Email = "admin@teste.com",
            CPF = "00000000000", PasswordHash = "x", Perfil = PerfilUsuario.Admin
        };
        var aluno = new Usuario
        {
            Id = Guid.NewGuid(), Nome = "Aluno", Email = "aluno@teste.com",
            CPF = "11111111111", PasswordHash = "x", Perfil = PerfilUsuario.Aluno
        };
        var prova = new Prova
        {
            Id = Guid.NewGuid(), Nome = "Prova Piloto", TipoAplicacao = TipoAplicacao.Virtual,
            NumeroAlternativas = 5, DataInicio = dataInicio, DataFim = dataFim,
            TokenPublico = Guid.NewGuid(), CriadoPorId = admin.Id
        };
        var provaAluno = new ProvaAluno
        {
            ProvaId = prova.Id, AlunoId = aluno.Id, Status = status,
            IniciadaEm = dataInicio, TokenTentativa = Guid.NewGuid()
        };

        ctx.Usuarios.AddRange(admin, aluno);
        ctx.Provas.Add(prova);
        ctx.ProvaAlunos.Add(provaAluno);
        await ctx.SaveChangesAsync();
        return (prova, provaAluno);
    }

    private static T LerProp<T>(IActionResult resultado, string nome)
    {
        var json = Assert.IsType<JsonResult>(resultado);
        var valor = json.Value!;
        var prop = valor.GetType().GetProperty(nome, BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(prop);
        return (T)prop!.GetValue(valor)!;
    }

    [Fact]
    public async Task Dentro_do_prazo_retorna_segundos_positivos_e_nao_encerrada()
    {
        using var ctx = NovoContexto();
        var (prova, aluno) = await SemearAsync(
            ctx,
            dataInicio: DateTime.UtcNow.AddMinutes(-30),
            dataFim: DateTime.UtcNow.AddMinutes(30));

        var controller = NovoController(ctx);
        var resultado = await controller.TempoRestante(prova.TokenPublico, aluno.TokenTentativa!.Value);

        Assert.False(LerProp<bool>(resultado, "encerrada"));
        var segundos = LerProp<long>(resultado, "segundosRestantes");
        // ~30 min = 1800s. Tolerância ampla para o tempo de execução do teste.
        Assert.InRange(segundos, 1700, 1800);
    }

    [Fact]
    public async Task Depois_do_prazo_retorna_encerrada_e_zero()
    {
        using var ctx = NovoContexto();
        var (prova, aluno) = await SemearAsync(
            ctx,
            dataInicio: DateTime.UtcNow.AddHours(-2),
            dataFim: DateTime.UtcNow.AddSeconds(-5));

        var controller = NovoController(ctx);
        var resultado = await controller.TempoRestante(prova.TokenPublico, aluno.TokenTentativa!.Value);

        Assert.True(LerProp<bool>(resultado, "encerrada"));
        Assert.Equal(0L, LerProp<long>(resultado, "segundosRestantes"));
    }

    [Fact]
    public async Task Tentativa_ja_finalizada_retorna_encerrada()
    {
        using var ctx = NovoContexto();
        var (prova, aluno) = await SemearAsync(
            ctx,
            dataInicio: DateTime.UtcNow.AddMinutes(-30),
            dataFim: DateTime.UtcNow.AddMinutes(30),
            status: StatusProvaAluno.Finalizada);

        var controller = NovoController(ctx);
        var resultado = await controller.TempoRestante(prova.TokenPublico, aluno.TokenTentativa!.Value);

        // Mesmo com a janela aberta, uma tentativa já finalizada não tem mais tempo a correr.
        Assert.True(LerProp<bool>(resultado, "encerrada"));
        Assert.Equal(0L, LerProp<long>(resultado, "segundosRestantes"));
    }
}
