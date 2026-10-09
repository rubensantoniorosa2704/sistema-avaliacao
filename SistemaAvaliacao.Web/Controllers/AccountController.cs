using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using SistemaAvaliacao.Web.Models.ViewModels;
using SistemaAvaliacao.Web.Services;

namespace SistemaAvaliacao.Web.Controllers;

/// <summary>
/// Autenticação por email/senha. Hoje atende apenas o administrador —
/// único perfil que faz login por credenciais. Alunos entram por token
/// temporário (ExameController) e não passam por aqui; o perfil Coordenador
/// ainda não possui fluxo de login.
///
/// Pontos de crescimento previstos:
/// - Destino pós-login por perfil: ver o redirect no final de Login (POST).
/// - Multitenant (InstitutionId): a claim correspondente é montada em
///   LoginService.BuildClaims; a autorização por tenant deve ler de lá.
/// </summary>
public class AccountController : Controller
{
    private readonly LoginService _loginService;

    public AccountController(LoginService loginService)
    {
        _loginService = loginService;
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction("Index", "Home");
        }

        ViewData["ReturnUrl"] = returnUrl;
        return View();
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        ViewData["ReturnUrl"] = model.ReturnUrl;

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await _loginService.AuthenticateAsync(model.Email, model.Password);

        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, "Email ou senha inválidos");
            return View(model);
        }

        var claimsIdentity = new ClaimsIdentity(result.Claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var authProperties = new AuthenticationProperties
        {
            IsPersistent = model.RememberMe,
            ExpiresUtc = model.RememberMe ? DateTimeOffset.UtcNow.AddDays(30) : DateTimeOffset.UtcNow.AddHours(8)
        };

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(claimsIdentity),
            authProperties);

        // Preserva o destino original (ex.: página protegida que disparou o login).
        // Url.IsLocalUrl evita open redirect para domínios externos.
        if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
        {
            return Redirect(model.ReturnUrl);
        }

        // Hoje apenas o administrador autentica por email/senha, então o destino
        // padrão é a área administrativa. Quando houver outros perfis com login
        // próprio (ex.: Coordenador), introduzir aqui a decisão de destino por perfil.
        return RedirectToAction("Provas", "Provas");
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Login");
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult AccessDenied() => View();
}
