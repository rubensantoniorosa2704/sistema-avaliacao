using System.ComponentModel.DataAnnotations;
using SistemaAvaliacao.Web.Models.Enums;

namespace SistemaAvaliacao.Web.Models.Entities;

public class Usuario
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    [Required(ErrorMessage = "Nome é obrigatório")]
    [StringLength(200)]
    public string Nome { get; set; } = string.Empty;
    
    [Required(ErrorMessage = "Email é obrigatório")]
    [EmailAddress(ErrorMessage = "Email inválido")]
    [StringLength(200)]
    public string Email { get; set; } = string.Empty;
    
    [Required(ErrorMessage = "CPF é obrigatório")]
    [StringLength(14)] // Com formatação: 999.999.999-99
    public string CPF { get; set; } = string.Empty;
    
    /// <summary>
    /// Matrícula do aluno na IES (opcional para Admin).
    /// </summary>
    [StringLength(50)]
    public string? Matricula { get; set; }
    
    [Required]
    public string PasswordHash { get; set; } = string.Empty;
    
    [Required]
    public PerfilUsuario Perfil { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    // Navigation properties
    public ICollection<Prova> ProvasCriadas { get; set; } = new List<Prova>();
    public ICollection<ProvaAluno> ProvasComoAluno { get; set; } = new List<ProvaAluno>();
}
