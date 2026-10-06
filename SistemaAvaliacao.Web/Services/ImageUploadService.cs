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

            // Criar diretório de uploads se não existir
            var uploadsFolder = Path.Combine(_environment.WebRootPath, "uploads", "questoes");
            if (!Directory.Exists(uploadsFolder))
            {
                Directory.CreateDirectory(uploadsFolder);
            }

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

            // Converter caminho relativo para absoluto
            var filePath = Path.Combine(_environment.WebRootPath, imagePath.TrimStart('/'));

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

        var filePath = Path.Combine(_environment.WebRootPath, imagePath.TrimStart('/'));
        return File.Exists(filePath);
    }
}
