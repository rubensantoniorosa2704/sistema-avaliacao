namespace SistemaAvaliacao.Web.Services;

public class ImageUploadService
{
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<ImageUploadService> _logger;
    private const long MaxFileSize = 5 * 1024 * 1024; // 5MB
    private static readonly string[] AllowedExtensions = { ".jpg", ".jpeg", ".png", ".gif", ".webp" };

    public ImageUploadService(IWebHostEnvironment environment, ILogger<ImageUploadService> logger)
    {
        _environment = environment;
        _logger = logger;
    }

    /// <summary>
    /// Faz upload de uma imagem e retorna o caminho relativo
    /// </summary>
    public async Task<(bool Success, string? FilePath, string? ErrorMessage)> UploadImageAsync(IFormFile file)
    {
        try
        {
            // Validar se o arquivo foi enviado
            if (file == null || file.Length == 0)
            {
                return (false, null, "Nenhum arquivo foi enviado.");
            }

            // Validar tamanho
            if (file.Length > MaxFileSize)
            {
                return (false, null, $"O arquivo não pode ter mais de {MaxFileSize / 1024 / 1024}MB.");
            }

            // Validar extensão
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!AllowedExtensions.Contains(extension))
            {
                return (false, null, $"Extensão não permitida. Use: {string.Join(", ", AllowedExtensions)}");
            }

            // Validar tipo MIME
            if (!file.ContentType.StartsWith("image/"))
            {
                return (false, null, "O arquivo não é uma imagem válida.");
            }

            // Validar o CONTEÚDO real do arquivo (magic bytes), não só a extensão/MIME
            // declarados pelo cliente — ambos podem ser forjados.
            if (!await AssinaturaConfereComImagemAsync(file))
            {
                return (false, null, "O conteúdo do arquivo não corresponde a uma imagem válida.");
            }

            // Diretório de uploads na raiz PERSISTENTE (ver CaminhoArmazenamento).
            var uploadsFolder = SistemaAvaliacao.Web.Helpers.CaminhoArmazenamento
                .ResolverSubpasta(_environment.WebRootPath, "questoes");

            // Gerar nome único para o arquivo
            var fileName = $"{Guid.NewGuid()}{extension}";
            var filePath = Path.Combine(uploadsFolder, fileName);

            // Salvar arquivo
            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            // Retornar caminho relativo (usado no banco de dados)
            var relativePath = $"/uploads/questoes/{fileName}";
            
            _logger.LogInformation("Imagem salva com sucesso: {FilePath}", relativePath);
            
            return (true, relativePath, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao fazer upload de imagem");
            return (false, null, "Erro ao salvar a imagem. Tente novamente.");
        }
    }

    /// <summary>
    /// Confere os primeiros bytes do arquivo contra as assinaturas conhecidas dos
    /// formatos de imagem suportados (JPEG, PNG, GIF, WEBP). Impede que um arquivo
    /// com extensão/MIME de imagem, mas conteúdo arbitrário, seja aceito.
    /// </summary>
    private static async Task<bool> AssinaturaConfereComImagemAsync(IFormFile file)
    {
        var buffer = new byte[12];
        int lidos;
        await using (var stream = file.OpenReadStream())
        {
            lidos = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length));
        }
        if (lidos < 12) return false;

        // JPEG: FF D8 FF
        if (buffer[0] == 0xFF && buffer[1] == 0xD8 && buffer[2] == 0xFF)
            return true;

        // PNG: 89 50 4E 47 0D 0A 1A 0A
        if (buffer[0] == 0x89 && buffer[1] == 0x50 && buffer[2] == 0x4E && buffer[3] == 0x47 &&
            buffer[4] == 0x0D && buffer[5] == 0x0A && buffer[6] == 0x1A && buffer[7] == 0x0A)
            return true;

        // GIF: "GIF87a" ou "GIF89a"
        if (buffer[0] == (byte)'G' && buffer[1] == (byte)'I' && buffer[2] == (byte)'F')
            return true;

        // WEBP: "RIFF" .... "WEBP"
        if (buffer[0] == (byte)'R' && buffer[1] == (byte)'I' && buffer[2] == (byte)'F' && buffer[3] == (byte)'F' &&
            buffer[8] == (byte)'W' && buffer[9] == (byte)'E' && buffer[10] == (byte)'B' && buffer[11] == (byte)'P')
            return true;

        return false;
    }

    /// <summary>
    /// Exclui uma imagem do servidor
    /// </summary>
    public bool DeleteImage(string? imagePath)
    {
        try
        {
            if (string.IsNullOrEmpty(imagePath))
            {
                return true; // Não há imagem para excluir
            }

            // O caminho salvo no banco é "/uploads/questoes/arquivo.ext". A raiz física
            // pode ser persistente (/data/uploads) — resolve a partir dela, removendo o
            // prefixo público "/uploads".
            var filePath = ResolverCaminhoFisico(imagePath);

            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                _logger.LogInformation("Imagem excluída: {FilePath}", imagePath);
                return true;
            }

            return true; // Arquivo não existe, considerar sucesso
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao excluir imagem: {ImagePath}", imagePath);
            return false;
        }
    }

    /// <summary>
    /// Valida se uma imagem existe no servidor
    /// </summary>
    public bool ImageExists(string? imagePath)
    {
        if (string.IsNullOrEmpty(imagePath))
        {
            return false;
        }

        var filePath = ResolverCaminhoFisico(imagePath);
        return File.Exists(filePath);
    }

    /// <summary>
    /// Converte um caminho público "/uploads/questoes/arquivo.ext" no caminho físico real,
    /// a partir da raiz persistente de uploads (ver CaminhoArmazenamento). O prefixo público
    /// "/uploads" é removido porque a raiz física já corresponde a ele.
    /// </summary>
    private string ResolverCaminhoFisico(string imagePath)
    {
        var raiz = Helpers.CaminhoArmazenamento.ResolverRaiz(_environment.WebRootPath);
        var relativo = imagePath.TrimStart('/');
        const string prefixo = "uploads/";
        if (relativo.StartsWith(prefixo, StringComparison.OrdinalIgnoreCase))
            relativo = relativo.Substring(prefixo.Length);
        return Path.Combine(raiz, relativo.Replace('/', Path.DirectorySeparatorChar));
    }
}
