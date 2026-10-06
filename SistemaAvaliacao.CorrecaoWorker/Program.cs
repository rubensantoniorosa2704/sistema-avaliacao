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

        var databasePath = Environment.GetEnvironmentVariable("SISTEMA_AVALIACAO_DB")
            ?? Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..",
                "SistemaAvaliacao.Web",
                "sistema_avaliacao.db"));

        Console.WriteLine($"Banco do worker: {databasePath}");

        builder.Services.AddDbContextFactory<ApplicationDbContext>(options =>
            options.UseSqlite($"Data Source={databasePath}"));
        builder.Services.AddHostedService<OmrBackgroundWorker>();

        var host = builder.Build();
        await host.RunAsync();
    }
}
