namespace SistemaAvaliacao.Web.Helpers;

/// <summary>
/// Ponto ÚNICO que resolve ONDE os anexos de runtime (imagens de questão, PDFs de folhas
/// de correção) são gravados em disco.
///
/// PROBLEMA que isto resolve: por padrão os uploads iam para <c>wwwroot/uploads</c>, que no
/// container de produção fica em sistema de arquivos EFÊMERO — some a cada redeploy/recriação
/// de máquina, enquanto o banco (em volume persistente) continua apontando para arquivos que
/// não existem mais. Aqui a raiz passa a ser um diretório PERSISTENTE (o mesmo volume do
/// banco), definido pela variável de ambiente <see cref="UploadsRootEnvVar"/>.
///
/// As URLs públicas continuam sendo <c>/uploads/...</c> (ver Program.cs, que serve esta raiz
/// sob esse prefixo), então nenhum caminho já gravado no banco precisa mudar.
/// </summary>
public static class CaminhoArmazenamento
{
    /// <summary>Variável de ambiente que define a raiz persistente de uploads (ex.: /data/uploads).</summary>
    public const string UploadsRootEnvVar = "SISTEMA_AVALIACAO_UPLOADS";

    /// <summary>Prefixo de URL público sob o qual os uploads são servidos.</summary>
    public const string UrlPrefix = "/uploads";

    /// <summary>
    /// Resolve a raiz física de uploads. Em produção, usa <see cref="UploadsRootEnvVar"/>
    /// (apontando para o volume persistente). Em dev, ou se a env var não existir, usa
    /// <c>{webRootPath}/uploads</c> para manter o comportamento local inalterado.
    /// Garante que o diretório exista.
    /// </summary>
    public static string ResolverRaiz(string webRootPath)
    {
        var raiz = Environment.GetEnvironmentVariable(UploadsRootEnvVar);
        if (string.IsNullOrWhiteSpace(raiz))
            raiz = Path.Combine(webRootPath, "uploads");

        Directory.CreateDirectory(raiz);
        return raiz;
    }

    /// <summary>
    /// Resolve (e cria) um subdiretório da raiz de uploads — ex.: "questoes", "correcoes".
    /// </summary>
    public static string ResolverSubpasta(string webRootPath, string subpasta)
    {
        var caminho = Path.Combine(ResolverRaiz(webRootPath), subpasta);
        Directory.CreateDirectory(caminho);
        return caminho;
    }
}
