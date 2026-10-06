namespace SistemaAvaliacao.Web.Models.ViewModels;

/// <summary>
/// Dados para o componente de "página de estado" (_PaginaEstado): um card
/// centralizado com ícone, título e mensagens. Reutilizado pelas telas de
/// confirmação/erro do fluxo de exame (prova enviada, encerrada, já iniciada...).
/// </summary>
public class PaginaEstadoViewModel
{
    /// <summary>Variante visual, mapeia para a cor da borda/ícone (success, warning, info, danger, secondary).</summary>
    public string Variante { get; set; } = "info";

    /// <summary>Classe do ícone FontAwesome (ex: "fa-circle-check").</summary>
    public string Icone { get; set; } = "fa-circle-info";

    public string Titulo { get; set; } = string.Empty;

    /// <summary>Linha de apoio logo abaixo do título (texto pequeno). Sempre codificada em HTML.</summary>
    public string? Subtitulo { get; set; }

    /// <summary>
    /// Mensagem principal (texto normal). Sempre codificada em HTML;
    /// use "\n" para quebrar linha.
    /// </summary>
    public string? Mensagem { get; set; }

    /// <summary>Caixa de destaque (alert) com um rótulo e um valor, ex: "Encerrada em: 10/10/2026".</summary>
    public DestaqueEstado? Destaque { get; set; }

    /// <summary>
    /// Parágrafo de detalhe (texto pequeno) renderizado SEM codificação, para permitir
    /// links e negrito. Use somente com texto literal escrito no código; NUNCA
    /// interpole dados vindos do usuário aqui.
    /// </summary>
    public string? DetalheHtml { get; set; }

    /// <summary>Texto do botão de ação. Se null, o botão não é exibido.</summary>
    public string? BotaoTexto { get; set; }

    /// <summary>URL do botão de ação.</summary>
    public string? BotaoUrl { get; set; }
}

/// <summary>Caixa de destaque (alert Bootstrap) do componente de página de estado.</summary>
public class DestaqueEstado
{
    /// <summary>Variante do alert (info, warning, secondary...).</summary>
    public string Variante { get; set; } = "info";

    public string Rotulo { get; set; } = string.Empty;

    public string Texto { get; set; } = string.Empty;
}
