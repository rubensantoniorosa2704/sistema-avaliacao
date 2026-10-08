using Microsoft.EntityFrameworkCore;

namespace SistemaAvaliacao.Web.Data;

/// <summary>
/// Provedores de banco suportados pela aplicação.
/// </summary>
public enum DatabaseProvider
{
    /// <summary>SQLite (arquivo local). Provedor atual/padrão.</summary>
    Sqlite,

    /// <summary>PostgreSQL (ex.: Neon, Supabase). Reservado para migração futura.</summary>
    Postgres
}

/// <summary>
/// Ponto ÚNICO de configuração do provedor de banco de dados.
///
/// Hoje o sistema usa SQLite. Toda a dependência de provedor está isolada aqui:
/// para migrar para PostgreSQL no futuro, basta (1) adicionar o pacote
/// Npgsql.EntityFrameworkCore.PostgreSQL, (2) descomentar o ramo Postgres em
/// <see cref="Configure"/>, e (3) definir Database:Provider=Postgres na configuração.
/// Nenhum controller, serviço ou entidade precisa mudar.
///
/// Usado tanto pela aplicação Web quanto pelo Worker de correção, garantindo
/// que ambos resolvam a conexão exatamente da mesma forma.
/// </summary>
public static class DatabaseConfiguration
{
    /// <summary>Nome da seção/chave de configuração do provedor.</summary>
    public const string ProviderKey = "Database:Provider";

    /// <summary>Variável de ambiente que define o caminho do arquivo SQLite.</summary>
    public const string SqliteDbPathEnvVar = "SISTEMA_AVALIACAO_DB";

    /// <summary>Variável de ambiente com a connection string (usada por provedores de rede, ex.: Postgres).</summary>
    public const string ConnectionStringEnvVar = "SISTEMA_AVALIACAO_CONNECTION_STRING";

    /// <summary>
    /// Resolve o provedor configurado. Default: <see cref="DatabaseProvider.Sqlite"/>.
    /// Aceita "Sqlite" ou "Postgres" (case-insensitive) via Database:Provider
    /// ou variável de ambiente DATABASE__PROVIDER / DATABASE_PROVIDER.
    /// </summary>
    public static DatabaseProvider ResolverProvider(string? valorConfigurado)
    {
        if (string.IsNullOrWhiteSpace(valorConfigurado))
            return DatabaseProvider.Sqlite;

        return valorConfigurado.Trim().ToLowerInvariant() switch
        {
            "postgres" or "postgresql" or "npgsql" => DatabaseProvider.Postgres,
            _ => DatabaseProvider.Sqlite
        };
    }

    /// <summary>
    /// Configura o <see cref="DbContextOptionsBuilder"/> para o provedor resolvido.
    /// Centraliza a chamada UseSqlite/UseNpgsql num único lugar.
    /// </summary>
    /// <param name="options">Builder a configurar.</param>
    /// <param name="provider">Provedor resolvido.</param>
    /// <param name="connectionString">Connection string já montada para o provedor.</param>
    public static void Configure(
        DbContextOptionsBuilder options,
        DatabaseProvider provider,
        string connectionString)
    {
        switch (provider)
        {
            case DatabaseProvider.Postgres:
                // MIGRAÇÃO FUTURA: adicione o pacote Npgsql.EntityFrameworkCore.PostgreSQL
                // e substitua a linha abaixo por:
                //     options.UseNpgsql(connectionString);
                // Lembre-se de regenerar as migrations para o provedor Npgsql.
                throw new NotSupportedException(
                    "Provedor Postgres ainda não habilitado. Adicione o pacote Npgsql.EntityFrameworkCore.PostgreSQL " +
                    "e habilite options.UseNpgsql em DatabaseConfiguration.Configure.");

            case DatabaseProvider.Sqlite:
            default:
                options.UseSqlite(connectionString);
                break;
        }
    }

    /// <summary>
    /// Monta a connection string para o provedor, a partir das variáveis de ambiente
    /// e de um caminho-padrão para o arquivo SQLite (usado quando a env var não existe).
    /// </summary>
    public static string MontarConnectionString(DatabaseProvider provider, string caminhoPadraoSqlite)
    {
        switch (provider)
        {
            case DatabaseProvider.Postgres:
                var conn = Environment.GetEnvironmentVariable(ConnectionStringEnvVar);
                if (string.IsNullOrWhiteSpace(conn))
                    throw new InvalidOperationException(
                        $"Provedor Postgres selecionado, mas a variável {ConnectionStringEnvVar} não foi definida.");
                return conn;

            case DatabaseProvider.Sqlite:
            default:
                var caminho = Environment.GetEnvironmentVariable(SqliteDbPathEnvVar);
                if (string.IsNullOrWhiteSpace(caminho))
                    caminho = caminhoPadraoSqlite;

                var diretorio = Path.GetDirectoryName(caminho);
                if (!string.IsNullOrWhiteSpace(diretorio))
                    Directory.CreateDirectory(diretorio);

                return $"Data Source={caminho}";
        }
    }

    /// <summary>Indica se o provedor atual é SQLite (para gates de código específico, ex.: PRAGMAs).</summary>
    public static bool EhSqlite(this DatabaseProvider provider) => provider == DatabaseProvider.Sqlite;
}
