using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TGDrive.LocalFs;

/// <summary>
/// Вычислитель SHA256-хэша файла с потоковым чтением.
/// Не загружает весь файл в память — читает блоками.
/// </summary>
public static class FileHasher
{
    private const int BufferSize = 64 * 1024; // 64 KB

    /// <summary>
    /// Вычислить SHA256-хэш файла в lowercase hex.
    /// </summary>
    /// <param name="filePath">Абсолютный путь к файлу.</param>
    /// <param name="ct">Токен отмены.</param>
    /// <returns>SHA256 хэш в lowercase hex (64 символа).</returns>
    /// <exception cref="ArgumentException">Путь пустой или невалидный.</exception>
    /// <exception cref="FileNotFoundException">Файл не существует.</exception>
    /// <exception cref="IOException">Файл заблокирован другим процессом или ошибка чтения.</exception>
    public static async Task<string> ComputeSha256Async(string filePath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path cannot be null or whitespace.", nameof(filePath));

        if (!File.Exists(filePath))
            throw new FileNotFoundException("File not found.", filePath);

        using var sha256 = SHA256.Create();
        
        // FileShare.Read — позволяем другим процессам читать файл, но не писать
        // FileOptions.SequentialScan — оптимизация для последовательного чтения больших файлов
        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        var buffer = new byte[BufferSize];
        int bytesRead;
        while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) > 0)
        {
            sha256.TransformBlock(buffer, 0, bytesRead, buffer, 0);
        }
        sha256.TransformFinalBlock(buffer, 0, 0);

        return BytesToHex(sha256.Hash!);
    }

    /// <summary>
    /// Преобразовать байты в lowercase hex.
    /// </summary>
    private static string BytesToHex(byte[] bytes)
    {
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes)
        {
            sb.Append(b.ToString("x2"));
        }
        return sb.ToString();
    }
}