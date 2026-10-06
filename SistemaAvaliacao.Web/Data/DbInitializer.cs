using Microsoft.EntityFrameworkCore;
using SistemaAvaliacao.Web.Models.Entities;
using SistemaAvaliacao.Web.Models.Enums;
using SistemaAvaliacao.Web.Services;

namespace SistemaAvaliacao.Web.Data;

public static class DbInitializer
{
    public static void Initialize(ApplicationDbContext context, PasswordService passwordService, ILogger? logger = null)
    {
        context.Database.Migrate();

        // Otimizações de concorrência para SQLite em produção.
        // WAL permite leituras simultâneas durante escritas.
        // busy_timeout evita "database is locked" em picos de acesso.
        context.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
        context.Database.ExecuteSqlRaw("PRAGMA synchronous=NORMAL;");
        context.Database.ExecuteSqlRaw("PRAGMA busy_timeout=5000;");

        context.Database.ExecuteSqlRaw("UPDATE Provas SET NumeroAlternativas = 4 WHERE NumeroAlternativas NOT IN (4, 5)");

        CriarUsuarioInicialSeNecessario(
            context,
            passwordService,
            PerfilUsuario.Admin,
            "INITIAL_ADMIN",
            obrigatorio: true,
            logger);

        CriarUsuarioInicialSeNecessario(
            context,
            passwordService,
            PerfilUsuario.Admin,
            "INITIAL_ADMIN_2",
            obrigatorio: false,
            logger);

        context.SaveChanges();
        logger?.LogInformation("Usuários iniciais verificados.");
    }

    private static void CriarUsuarioInicialSeNecessario(
        ApplicationDbContext context,
        PasswordService passwordService,
        PerfilUsuario perfil,
        string prefixoVariavel,
        bool obrigatorio,
        ILogger? logger)
    {
        var nome = Environment.GetEnvironmentVariable($"{prefixoVariavel}_NAME");
        var email = Environment.GetEnvironmentVariable($"{prefixoVariavel}_EMAIL");
        var cpf = Environment.GetEnvironmentVariable($"{prefixoVariavel}_CPF");
        var senha = Environment.GetEnvironmentVariable($"{prefixoVariavel}_PASSWORD");

        if (string.IsNullOrWhiteSpace(nome) || string.IsNullOrWhiteSpace(email)
            || string.IsNullOrWhiteSpace(cpf) || string.IsNullOrWhiteSpace(senha))
        {
            if (!obrigatorio)
                return;

            logger?.LogWarning(
                "Nenhum usuário inicial com o perfil {Perfil} foi criado. Defina as variáveis {Prefixo}_NAME, {Prefixo}_EMAIL, {Prefixo}_CPF e {Prefixo}_PASSWORD.",
                perfil,
                prefixoVariavel,
                prefixoVariavel,
                prefixoVariavel,
                prefixoVariavel);
            return;
        }

        var emailNormalizado = email.Trim().ToLowerInvariant();
        var cpfNormalizado = cpf.Trim();
        var usuarioJaExiste = context.Usuarios.Any(usuario =>
            usuario.Email == emailNormalizado || usuario.CPF == cpfNormalizado)
            || context.Usuarios.Local.Any(usuario =>
                usuario.Email == emailNormalizado || usuario.CPF == cpfNormalizado);

        if (usuarioJaExiste)
            return;

        context.Usuarios.Add(new Usuario
        {
            Id = Guid.NewGuid(),
            Nome = nome.Trim(),
            Email = emailNormalizado,
            CPF = cpfNormalizado,
            PasswordHash = passwordService.HashPassword(senha),
            Perfil = perfil,
            CreatedAt = DateTime.UtcNow
        });
    }
}
