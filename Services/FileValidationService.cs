namespace RecruiterAI.Services;

public class FileValidationService
{
    private readonly int _maxPdfSizeBytes;
    private readonly int _maxCandidates;
    private readonly int _maxExtractedTextChars;

    public int MaxCandidates => _maxCandidates;

    public FileValidationService(IConfiguration config)
    {
        var maxMb = config.GetValue<int>("FileLimits:MaxPdfSizeMb", 5);
        _maxPdfSizeBytes = maxMb * 1024 * 1024;
        _maxCandidates = config.GetValue<int>("FileLimits:MaxCandidatesPerRequest", 20);
        _maxExtractedTextChars = config.GetValue<int>("FileLimits:MaxExtractedTextChars", 30000);
    }

    public void ValidatePdfBase64(string base64, string candidateName)
    {
        if (string.IsNullOrWhiteSpace(base64))
            throw new ArgumentException($"Falta el PDF de {candidateName}.");

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(base64);
        }
        catch
        {
            throw new ArgumentException($"PDF inválido para {candidateName}.");
        }

        if (bytes.Length > _maxPdfSizeBytes)
        {
            var sizeMb = bytes.Length / 1024.0 / 1024.0;
            var maxMb = _maxPdfSizeBytes / 1024 / 1024;
            throw new ArgumentException(
                $"El PDF de {candidateName} pesa {sizeMb:F1}MB. El máximo permitido es {maxMb}MB.");
        }

        if (bytes.Length < 4 || bytes[0] != 0x25 || bytes[1] != 0x50 ||
            bytes[2] != 0x44 || bytes[3] != 0x46)
        {
            throw new ArgumentException(
                $"El archivo de {candidateName} no es un PDF válido.");
        }
    }

    public void ValidateCandidateCount(int count)
    {
        if (count > _maxCandidates)
            throw new ArgumentException(
                $"Se permiten hasta {_maxCandidates} candidatos por análisis. " +
                $"Recibimos {count}.");
    }

    public string TruncateText(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        return text.Length > _maxExtractedTextChars
            ? text[.._maxExtractedTextChars]
            : text;
    }
}