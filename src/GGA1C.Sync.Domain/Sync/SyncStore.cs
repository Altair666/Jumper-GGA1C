using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GGA1C.Sync.Altium;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;

namespace GGA1C.Sync.Sync;

/// <summary>
/// Реализация доступа к данным синхронизации над репозиториями ABP.
///
/// Ключи upsert намеренно совпадают с прежней версией сервиса: компонент — по AltiumId,
/// папка — по AltiumFolderId, проект — по паре AltiumProjectId + AltiumRevision.
/// Если сменить ключ, повторная синхронизация начнёт плодить дубликаты вместо обновления.
/// </summary>
public class SyncStore : DomainService, ISyncStore
{
    private readonly IRepository<ComponentMap, int> _components;
    private readonly IRepository<FolderMap, int> _folders;
    private readonly IRepository<ProjectMap, int> _projects;
    private readonly IRepository<SyncLogEntry, int> _log;

    public SyncStore(
        IRepository<ComponentMap, int> components,
        IRepository<FolderMap, int> folders,
        IRepository<ProjectMap, int> projects,
        IRepository<SyncLogEntry, int> log)
    {
        _components = components;
        _folders = folders;
        _projects = projects;
        _log = log;
    }

    public async Task<ComponentMap?> GetComponentByAltiumIdAsync(string altiumId)
    {
        return await _components.FindAsync(x => x.AltiumId == altiumId);
    }

    public async Task UpsertComponentAsync(ComponentMap c)
    {
        var existing = await _components.FindAsync(x => x.AltiumId == c.AltiumId);

        if (existing == null)
        {
            await _components.InsertAsync(c, autoSave: true);
            return;
        }

        existing.AltiumItemGuid = c.AltiumItemGuid;
        existing.AltiumName = c.AltiumName;
        existing.AltiumPath = c.AltiumPath;
        existing.Article = c.Article;
        existing.Description = c.Description;
        existing.Manufacturer = c.Manufacturer;
        existing.Name1C = c.Name1C;
        existing.OnecGuid = c.OnecGuid;
        existing.OnecName = c.OnecName;
        existing.SyncStatus = c.SyncStatus;
        existing.ErrorMessage = c.ErrorMessage;
        existing.SyncedAt = c.SyncedAt;

        await _components.UpdateAsync(existing, autoSave: true);
    }

    public async Task UpdateComponentStatusAsync(string altiumId, string status, string? error = null)
    {
        var existing = await _components.FindAsync(x => x.AltiumId == altiumId);
        if (existing == null)
        {
            return;
        }

        existing.SyncStatus = status;
        existing.ErrorMessage = error;
        existing.SyncedAt = DateTime.UtcNow;

        await _components.UpdateAsync(existing, autoSave: true);
    }

    public async Task<FolderMap?> GetFolderMapByAltiumIdAsync(string altiumFolderId)
    {
        return await _folders.FindAsync(x => x.AltiumFolderId == altiumFolderId);
    }

    public async Task UpsertFolderMapAsync(FolderMap f)
    {
        var existing = await _folders.FindAsync(x => x.AltiumFolderId == f.AltiumFolderId);

        if (existing == null)
        {
            await _folders.InsertAsync(f, autoSave: true);
            return;
        }

        existing.AltiumFolderPath = f.AltiumFolderPath;
        existing.OnecGroupGuid = f.OnecGroupGuid;
        existing.OnecGroupPath = f.OnecGroupPath;
        existing.AutoCreated = f.AutoCreated;

        await _folders.UpdateAsync(existing, autoSave: true);
    }

    public async Task<List<ProjectMap>> GetProjectsAsync(string? status = null)
    {
        var query = await _projects.GetQueryableAsync();

        if (status != null)
        {
            query = query.Where(x => x.SyncStatus == status);
        }

        return query
            .OrderBy(x => x.AltiumProjectName)
            .ThenByDescending(x => x.AltiumRevision)
            .ToList();
    }

    public async Task UpsertProjectAsync(ProjectMap p)
    {
        var existing = await _projects.FindAsync(
            x => x.AltiumProjectId == p.AltiumProjectId && x.AltiumRevision == p.AltiumRevision);

        if (existing == null)
        {
            await _projects.InsertAsync(p, autoSave: true);
            return;
        }

        existing.AltiumProjectName = p.AltiumProjectName;
        existing.OnecNomenclatureGuid = p.OnecNomenclatureGuid;
        existing.OnecSpecGuid = p.OnecSpecGuid;
        existing.SyncAction = p.SyncAction;
        existing.SyncStatus = p.SyncStatus;
        existing.ErrorMessage = p.ErrorMessage;
        existing.SyncedAt = p.SyncedAt;

        await _projects.UpdateAsync(existing, autoSave: true);
    }

    public async Task AddLogAsync(SyncLogEntry entry)
    {
        entry.Timestamp = DateTime.UtcNow;
        await _log.InsertAsync(entry, autoSave: true);
    }

    public async Task<List<SyncLogEntry>> GetLogAsync(int maxCount = 100)
    {
        var query = await _log.GetQueryableAsync();

        return query
            .OrderByDescending(x => x.Timestamp)
            .Take(maxCount)
            .ToList();
    }
}
