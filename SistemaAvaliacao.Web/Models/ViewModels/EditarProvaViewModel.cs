using System.ComponentModel.DataAnnotations;

namespace SistemaAvaliacao.Web.Models.ViewModels;

public class EditarProvaViewModel
{
    public Guid Id { get; set; }

    [Required(ErrorMessage = "Nome da prova é obrigatório")]
    [StringLength(300, ErrorMessage = "Nome deve ter no máximo 300 caracteres")]
    [Display(Name = "Nome da Prova")]
    public string Nome { get; set; } = string.Empty;

    [StringLength(1000, ErrorMessage = "Descrição deve ter no máximo 1000 caracteres")]
    [Display(Name = "Descrição")]
    [DataType(DataType.MultilineText)]
    public string? Descricao { get; set; }

    [Range(4, 5, ErrorMessage = "A prova deve ter 4 ou 5 alternativas.")]
    [Display(Name = "Alternativas por questão")]
    public int NumeroAlternativas { get; set; } = 4;

    [Required(ErrorMessage = "Data de início é obrigatória")]
    [Display(Name = "Data de Início")]
    [DataType(DataType.DateTime)]
    public DateTime DataInicio { get; set; }

    [Required(ErrorMessage = "Data de fim é obrigatória")]
    [Display(Name = "Data de Fim")]
    [DataType(DataType.DateTime)]
    public DateTime DataFim { get; set; }

    // Estatísticas (exibidas na tela).
    public int NumeroQuestoes { get; set; }
    public int NumeroAlunosAtribuidos { get; set; }
}
