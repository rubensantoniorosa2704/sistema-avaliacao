using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using SistemaAvaliacao.Web.Data;
using SistemaAvaliacao.Web.Services;

var builder = WebApplication.CreateBuilder(args);

var caminhoBanco = Environment.GetEnvironmentVariable("SISTEMA_AVALIACAO_DB")
    ?? Path.Combine(builder.Environment.ContentRootPath, "data", "sistema_avaliacao.db");

var diretorioBanco = Path.GetDirectoryName(caminhoBanco);
if (!string.IsNullOrWhiteSpace(diretorioBanco))
    Directory.CreateDirectory(diretorioBanco);

// Add services to the container.
builder.Services.AddControllersWithViews();

var diretorioChavesProtecaoDados = Environment.GetEnvironmentVariable("DATA_PROTECTION_KEYS_PATH")
    ?? Path.Combine(builder.Environment.ContentRootPath, "data", "keys");
Directory.CreateDirectory(diretorioChavesProtecaoDados);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(diretorioChavesProtecaoDados));

// Configurar Entity Framework com SQLite
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite($"Data Source={caminhoBanco}"));

// Registrar serviços
builder.Services.AddScoped<PasswordService>();
builder.Services.AddScoped<ImageUploadService>();
builder.Services.AddScoped<ImportacaoAlunosService>();
builder.Services.AddScoped<XlsxProvaImportService>();
builder.Services.AddScoped<ExportacaoResultadosService>();
builder.Services.AddScoped<ExportacaoXlsxService>();
builder.Services.AddScoped<FolhaRespostaPdfService>();
builder.Services.AddScoped<TentativaExameService>();

// Configurar Cookie Authentication
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax;
    });

builder.Services.AddAuthorization();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

if (!app.Environment.IsDevelopment())
    app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication(); // Adicionar antes de UseAuthorization
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapControllers(); // Para rotas com atributo como [HttpGet("prova/{token}")]

// Inicializar banco com seed data
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<ApplicationDbContext>();
        var passwordService = services.GetRequiredService<PasswordService>();
        var initLogger = services.GetRequiredService<ILogger<Program>>();
        DbInitializer.Initialize(context, passwordService, initLogger);
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "Erro ao inicializar banco de dados");
    }
}

app.Run();
