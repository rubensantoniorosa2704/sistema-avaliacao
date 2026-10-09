using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SistemaAvaliacao.Web.Controllers;

/// <summary>
/// Ponto de entrada da área administrativa. Os domínios de Provas, Questões,
/// Alunos, Folhas/OMR e Exportação foram extraídos para controllers próprios
/// (todos sob /Admin/* via rota por atributo). Resta aqui apenas o redirecionamento
/// da raiz /Admin para a listagem de provas.
/// </summary>
[Authorize(Roles = "Admin")]
public class AdminController : Controller
{
    public IActionResult Index() => RedirectToAction("Provas", "Provas");
}
