using System.Text;
using Microsoft.EntityFrameworkCore;
using SistemaAvaliacao.Web.Data;
using SistemaAvaliacao.Web.Models.Entities;
using SistemaAvaliacao.Web.Models.Enums;
using SistemaAvaliacao.Web.Services;
using Xunit;

namespace SistemaAvaliacao.Tests;

/// <summary>
/// Testes da importação de alunos por CSV, com foco no tratamento de CPF — em especial o
/// caso clássico do zero à esquerda (CPF que começa com 0). O login do aluno é por
/// email + CPF, então um CPF corrompido na importação = aluno que não entra na prova.
///
/// Alguns testes DOCUMENTAM o comportamento atual (inclusive uma limitação real), para que
/// a correção futura seja uma mudança consciente e visível, não um efeito colateral.
/// </summary>
public class ImportacaoAlunosCpfTests
{
    private static ApplicationDbContext NovoContexto()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            // SQLite in-memory respeita índices únicos e transações (ao contrário do provedor
            // InMemory), o que importa para esta importação que usa BeginTransaction.
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ApplicationDbContext(options);
    }

    private static ImportacaoAlunosService NovoServico(ApplicationDbContext ctx)
        => new(ctx, new PasswordService());

    private static async Task<Prova> SemearProvaAsync(ApplicationDbContext ctx)
    {
        var admin = new Usuario
        {
            Id = Guid.NewGuid(), Nome = "Admin", Email = "admin@teste.com",
            CPF = "99999999999", PasswordHash = "x", Perfil = PerfilUsuario.Admin
        };
        var prova = new Prova
        {
            Id = Guid.NewGuid(), Nome = "Prova", TipoAplicacao = TipoAplicacao.Virtual,
            NumeroAlternativas = 5, DataInicio = DateTime.UtcNow.AddDays(-1),
            DataFim = DateTime.UtcNow.AddDays(1), TokenPublico = Guid.NewGuid(),
            CriadoPorId = admin.Id
        };
        ctx.Usuarios.Add(admin);
        ctx.Provas.Add(prova);
        await ctx.SaveChangesAsync();
        // Recarrega com a coleção que o serviço espera navegar.
        return await ctx.Provas.Include(p => p.ProvaAlunos).FirstAsync(p => p.Id == prova.Id);
    }

    private static Stream CsvDe(string conteudo)
        => new MemoryStream(Encoding.UTF8.GetBytes(conteudo));

    [Fact]
    public async Task Cpf_com_zero_a_esquerda_como_texto_de_11_digitos_e_preservado()
    {
        using var ctx = NovoContexto();
        var prova = await SemearProvaAsync(ctx);
        var servico = NovoServico(ctx);

        var resultado = await servico.ImportarAsync(prova, CsvDe(
            "Nome;Matricula;Email;CPF\n" +
            "Joao Silva;2024001;joao@aluno.com;01234567890\n"));

        Assert.Empty(resultado.Erros);
        Assert.Equal(1, resultado.Vinculados);

        var aluno = await ctx.Usuarios.FirstOrDefaultAsync(u => u.Email == "joao@aluno.com");
        Assert.NotNull(aluno);
        // O ZERO À ESQUERDA foi preservado — é isto que garante o login do aluno.
        Assert.Equal("01234567890", aluno!.CPF);
    }

    [Fact]
    public async Task Cpf_formatado_com_pontos_e_traco_tem_digitos_extraidos_e_zero_preservado()
    {
        using var ctx = NovoContexto();
        var prova = await SemearProvaAsync(ctx);
        var servico = NovoServico(ctx);

        var resultado = await servico.ImportarAsync(prova, CsvDe(
            "Nome;Matricula;Email;CPF\n" +
            "Maria Souza;2024002;maria@aluno.com;012.345.678-90\n"));

        Assert.Empty(resultado.Erros);
        var aluno = await ctx.Usuarios.FirstOrDefaultAsync(u => u.Email == "maria@aluno.com");
        Assert.NotNull(aluno);
        Assert.Equal("01234567890", aluno!.CPF);
    }

    [Fact]
    public async Task Separador_virgula_tambem_funciona()
    {
        using var ctx = NovoContexto();
        var prova = await SemearProvaAsync(ctx);
        var servico = NovoServico(ctx);

        var resultado = await servico.ImportarAsync(prova, CsvDe(
            "Nome,Matricula,Email,CPF\n" +
            "Ana Lima,2024003,ana@aluno.com,01234567890\n"));

        Assert.Empty(resultado.Erros);
        var aluno = await ctx.Usuarios.FirstOrDefaultAsync(u => u.Email == "ana@aluno.com");
        Assert.NotNull(aluno);
        Assert.Equal("01234567890", aluno!.CPF);
    }

    [Fact]
    public async Task Cpf_com_zero_comido_pelo_Excel_10_digitos_e_rejeitado_com_mensagem_explicativa()
    {
        // Cenário real: o Excel formatou o CPF como número e comeu o zero à esquerda,
        // salvando "1234567890" (10 dígitos) no CSV. O aluno continua sendo REJEITADO
        // (não dá para adivinhar o CPF correto), mas agora a mensagem explica a causa
        // provável, para quem importa saber como corrigir o arquivo.
        using var ctx = NovoContexto();
        var prova = await SemearProvaAsync(ctx);
        var servico = NovoServico(ctx);

        var resultado = await servico.ImportarAsync(prova, CsvDe(
            "Nome;Matricula;Email;CPF\n" +
            "Pedro Zero;2024004;pedro@aluno.com;1234567890\n"));

        Assert.Equal(0, resultado.Vinculados);
        Assert.Single(resultado.Erros);
        // A mensagem deve apontar a quantidade de dígitos e a causa provável (zero à esquerda / Excel).
        Assert.Contains("10 dígito", resultado.Erros[0]);
        Assert.Contains("Excel", resultado.Erros[0]);

        var aluno = await ctx.Usuarios.FirstOrDefaultAsync(u => u.Email == "pedro@aluno.com");
        Assert.Null(aluno);
    }

    [Fact]
    public async Task Cpf_com_digitos_a_mais_e_rejeitado()
    {
        using var ctx = NovoContexto();
        var prova = await SemearProvaAsync(ctx);
        var servico = NovoServico(ctx);

        var resultado = await servico.ImportarAsync(prova, CsvDe(
            "Nome;Matricula;Email;CPF\n" +
            "Carlos Erro;2024005;carlos@aluno.com;012345678901\n"));

        Assert.Equal(0, resultado.Vinculados);
        Assert.Single(resultado.Erros);
        Assert.Contains("12 dígitos", resultado.Erros[0]);

        var aluno = await ctx.Usuarios.FirstOrDefaultAsync(u => u.Email == "carlos@aluno.com");
        Assert.Null(aluno);
    }
}
