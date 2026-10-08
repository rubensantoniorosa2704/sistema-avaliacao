using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SistemaAvaliacao.Web.Data;

namespace SistemaAvaliacao.CorrecaoWorker;

public class Startup
{
    public static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);

        // Caminho-padrão do arquivo SQLite para o worker (quando a env var não é definida):
        // o mesmo arquivo usado pela aplicação Web.
        var caminhoBancoPadrao = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "SistemaAvaliacao.Web",
            "sistema_avaliacao.db"));

        // Resolução centralizada do provedor de banco — idêntica à da aplicação Web.
        var dbProvider = DatabaseConfiguration.ResolverProvider(
            builder.Configuration[DatabaseConfiguration.ProviderKey]);
        var connectionString = DatabaseConfiguration.MontarConnectionString(dbProvider, caminhoBancoPadrao);

        Console.WriteLine($"Worker: provedor={dbProvider}");

        builder.Services.AddDbContextFactory<ApplicationDbContext>(options =>
            DatabaseConfiguration.Configure(options, dbProvider, connectionString));
        builder.Services.AddHostedService<OmrBackgroundWorker>();

        var host = builder.Build();
        await host.RunAsync();
    }
}
