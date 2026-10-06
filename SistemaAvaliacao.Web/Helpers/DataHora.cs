namespace SistemaAvaliacao.Web.Helpers;

/// <summary>
/// Conversão centralizada de fuso horário.
///
/// Regra do sistema: TODAS as datas são armazenadas e comparadas em UTC.
/// A conversão para o fuso da aplicação (Brasília) acontece apenas nas
/// bordas: ao receber uma data do formulário (local → UTC) e ao exibir
/// uma data para o usuário (UTC → local).
/// </summary>
public static class DataHora
{
    // IANA funciona no Windows e no Linux a partir do .NET 6. Fallbacks por segurança.
    private static readonly TimeZoneInfo FusoApp = ResolverFuso();

    private static TimeZoneInfo ResolverFuso()
    {
        foreach (var id in new[] { "America/Sao_Paulo", "E. South America Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch { /* tenta o próximo */ }
        }
        return TimeZoneInfo.Utc;
    }

    /// <summary>
    /// Converte um horário informado pelo usuário (fuso da aplicação) para UTC,
    /// para ser armazenado. O <see cref="DateTime.Kind"/> é ignorado.
    /// </summary>
    public static DateTime ParaUtc(DateTime local)
    {
        var semKind = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(semKind, FusoApp);
    }

    /// <summary>
    /// Converte um horário UTC (armazenado) para o fuso da aplicação, para exibição
    /// ou para preencher campos de formulário.
    /// </summary>
    public static DateTime ParaLocal(DateTime utc)
    {
        var comKindUtc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(comKindUtc, FusoApp);
    }

    /// <summary>Horário atual no fuso da aplicação.</summary>
    public static DateTime AgoraLocal => ParaLocal(DateTime.UtcNow);
}

public static class DataHoraExtensions
{
    /// <summary>Converte uma data UTC para o fuso da aplicação (Brasília) para exibição.</summary>
    public static DateTime ParaFusoLocal(this DateTime utc) => DataHora.ParaLocal(utc);

    /// <summary>Converte uma data UTC anulável para o fuso da aplicação para exibição.</summary>
    public static DateTime? ParaFusoLocal(this DateTime? utc) => utc.HasValue ? DataHora.ParaLocal(utc.Value) : null;
}
