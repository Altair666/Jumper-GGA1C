using System.Collections.Generic;
using System.Threading.Tasks;
using GGA1C.Sync.Altium;

namespace GGA1C.Sync.Sync;

/// <summary>
/// Доступ к данным синхронизации. Повторяет набор операций прежней версии сервиса
/// один в один, чтобы перенесённая логика SyncService осталась неизменной — отличие
/// только в том, что операции асинхронные, как принято в репозиториях ABP.
/// </summary>
public interface ISyncStore
{
    Task<ComponentMap?> GetComponentByAltiumIdAsync(string altiumId);

    /// <summary>Вставить или обновить по AltiumId.</summary>
    Task UpsertComponentAsync(ComponentMap c);

    Task UpdateComponentStatusAsync(string altiumId, string status, string? error = null);

    Task<FolderMap?> GetFolderMapByAltiumIdAsync(string altiumFolderId);

    /// <summary>Вставить или обновить по AltiumFolderId.</summary>
    Task UpsertFolderMapAsync(FolderMap f);

    Task<List<ProjectMap>> GetProjectsAsync(string? status = null);

    /// <summary>Вставить или обновить по паре AltiumProjectId + AltiumRevision.</summary>
    Task UpsertProjectAsync(ProjectMap p);

    /// <summary>Добавить запись в журнал синхронизации. Время ставится здесь.</summary>
    Task AddLogAsync(SyncLogEntry entry);

    Task<List<SyncLogEntry>> GetLogAsync(int maxCount = 100);
}
