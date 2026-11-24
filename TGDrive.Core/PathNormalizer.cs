using System;
using System.Collections.Generic;
using System.Text;

namespace TGDrive.Core;

public static class PathNormalizer
{
    public const int MaxCaptionLength = 1024;
    public const int MaxPathBudget = 700;

    public static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path cannot be null or whitespace.", nameof(path));

        // 1. Прямые слэши
        var normalized = path.Replace('\\', '/');

        // 2. Без ведущего слэша
        normalized = normalized.TrimStart('/');

        // 3. Разрешение "." и "..", запрет выхода за корень
        var parts = normalized.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        var resultParts = new List<string>(parts.Length);

        foreach (var part in parts)
        {
            if (part == ".") continue;
            if (part == "..")
            {
                if (resultParts.Count == 0)
                    throw new ArgumentException("Path cannot traverse above root (..).", nameof(path));
                resultParts.RemoveAt(resultParts.Count - 1);
            }
            else
            {
                resultParts.Add(part);
            }
        }

        normalized = string.Join("/", resultParts);

        // 4. NFC нормализация
        normalized = normalized.Normalize(NormalizationForm.FormC);

        // 5. R1: бюджет пути ~700 символов
        if (normalized.Length > MaxPathBudget)
            throw new InvalidOperationException("PathTooLongForCaption");

        return normalized;
    }

    public static string GetDbKey(string normalizedPath)
    {
        // R11: ключи путей lowerInvariant в БД (SQLite NOCASE работает корректно только с ASCII)
        return normalizedPath.ToLowerInvariant();
    }
}