using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SistemaAvaliacao.Web.Data;
using SistemaAvaliacao.Web.Models.Entities;

namespace SistemaAvaliacao.Web.Services;

/// <summary>
/// Resultado de uma tentativa de autenticação por email/senha.
/// Carrega apenas o necessário para o fluxo de login (sucesso + claims),
/// sem expor a entidade de domínio ao controller.
/// </summary>
public class AuthResult
{
    public bool Succeeded { get; init; }
    public IReadOnlyList<Claim> Claims { get; init; } = Array.Empty<Claim>();

    public static AuthResult Fail() => new() { Succeeded = false };

    public static AuthResult Success(IReadOnlyList<Claim> claims)
        => new() { Succeeded = true, Claims = claims };
}

/// <summary>
/// Autenticação por email/senha (fluxo do administrador).
/// Alunos autenticam por token temporário em outro fluxo (ExameController);
/// este serviço não os atende.
/// Mantém-se livre de dependências de HTTP para permitir testes isolados.
/// </summary>
public class LoginService(ApplicationDbContext context, PasswordService passwordService)
{
    public async Task<AuthResult> AuthenticateAsync(string? email, string? password)
    {
        string emailNormalizado = email?.Trim().ToLowerInvariant() ?? string.Empty;

        var usuario = await context.Usuarios
            .FirstOrDefaultAsync(u => u.Email == emailNormalizado);

        if (usuario is null ||
            !passwordService.VerifyPassword(password ?? string.Empty, usuario.PasswordHash))
        {
            return AuthResult.Fail();
        }

        return AuthResult.Success(BuildClaims(usuario));
    }

    /// <summary>
    /// Monta as claims da identidade autenticada. Ponto único de extensão:
    /// quando o multitenant for introduzido, adicionar aqui a claim de
    /// InstitutionId (ex.: new("InstitutionId", usuario.InstitutionId.ToString()))
    /// para que a autorização por tenant leia do próprio ticket de autenticação.
    /// </summary>
    private static List<Claim> BuildClaims(Usuario usuario) =>
    [
        new(ClaimTypes.NameIdentifier, usuario.Id.ToString()),
        new(ClaimTypes.Name, usuario.Nome),
        new(ClaimTypes.Email, usuario.Email),
        new(ClaimTypes.Role, usuario.Perfil.ToString())
    ];
}
