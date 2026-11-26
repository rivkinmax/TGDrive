using System;
using System.Collections.Generic;

namespace TGDrive.LocalFs;

/// <summary>
/// Фильтр исключений для синхронизации.
/// Определяет, какие файлы и папки должны быть пропущены сканером.
/// Согласно REQUIREMENTS.txt раздел 7.
/// </summary>
public sealed class PathFilter
{
    private readonly List<string> _fileNamePatterns = new();
    private readonly HashSet<string> _folderNames = new(StringComparer.OrdinalIgnoreCase);

    public PathFilter()
    {
        // Дефолтные исключения согласно REQUIREMENTS.txt раздел 7
        AddFileNamePattern("*.tmp");
        AddFileNamePattern("*.temp");
        AddFileNamePattern("~*");  // Office temp files (~$document.docx)
        AddFileNamePattern("desktop.ini");
        AddFileNamePattern("Thumbs.db");
        AddFileNamePattern(".DS_Store");
        AddFileNamePattern(".tgdrive-*.part");  // Our partial download files
    }

    /// <summary>
    /// Добавить паттерн исключения по имени файла (поддерживает * и ? как wildcard).
    /// </summary>
    public void AddFileNamePattern(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            throw new ArgumentException("Pattern cannot be null or whitespace.", nameof(pattern));
        _fileNamePatterns.Add(pattern);
    }

    /// <summary>
    /// Добавить имя папки для исключения (вся папка и её содержимое).
    /// </summary>
    public void AddFolderName(string folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName))
            throw new ArgumentException("Folder name cannot be null or whitespace.", nameof(folderName));
        _folderNames.Add(folderName);
    }

    /// <summary>
    /// Убрать паттерн исключения по имени файла.
    /// </summary>
    public bool RemoveFileNamePattern(string pattern)
    {
        return _fileNamePatterns.Remove(pattern);
    }

    /// <summary>
    /// Убрать папку из списка исключений.
    /// </summary>
    public bool RemoveFolderName(string folderName)
    {
        return _folderNames.Remove(folderName);
    }

    /// <summary>
    /// Проверить, должен ли файл быть исключён из синхронизации.
    /// </summary>
    /// <param name="relativePath">Нормализованный относительный путь (прямые слэши, без ведущего слэша).</param>
    /// <returns>true если файл должен быть пропущен, false если должен быть синхронизирован.</returns>
    public bool ShouldExclude(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return true;

        var parts = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return true;

        // Проверка папок в пути
        foreach (var part in parts)
        {
            if (_folderNames.Contains(part))
                return true;
        }

        // Проверка имени файла
        var fileName = parts[parts.Length - 1];
        foreach (var pattern in _fileNamePatterns)
        {
            if (MatchesWildcard(fileName, pattern))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Проверка совпадения строки с wildcard-паттерном (*, ?).
    /// Простая реализация без Regex для производительности.
    /// </summary>
    private static bool MatchesWildcard(string text, string pattern)
    {
        if (string.IsNullOrEmpty(pattern))
            return string.IsNullOrEmpty(text);

        int textIndex = 0;
        int patternIndex = 0;
        int starIndex = -1;
        int matchIndex = 0;

        while (textIndex < text.Length)
        {
            if (patternIndex < pattern.Length && 
                (pattern[patternIndex] == '?' || char.ToLowerInvariant(pattern[patternIndex]) == char.ToLowerInvariant(text[textIndex])))
            {
                textIndex++;
                patternIndex++;
            }
            else if (patternIndex < pattern.Length && pattern[patternIndex] == '*')
            {
                starIndex = patternIndex;
                matchIndex = textIndex;
                patternIndex++;
            }
            else if (starIndex != -1)
            {
                patternIndex = starIndex + 1;
                matchIndex++;
                textIndex = matchIndex;
            }
            else
            {
                return false;
            }
        }

        while (patternIndex < pattern.Length && pattern[patternIndex] == '*')
        {
            patternIndex++;
        }

        return patternIndex == pattern.Length;
    }
}