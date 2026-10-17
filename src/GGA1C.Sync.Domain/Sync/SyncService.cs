using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GGA1C.Sync.Altium;
using GGA1C.Sync.OneC;
using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;

namespace GGA1C.Sync.Sync;

/// <summary>
/// Перенесено из прежней версии сервиса. Логика не менялась: отличие только в том, что
/// доступ к данным идёт через ISyncStore (репозитории ABP) и операции стали асинхронными,
/// а применение настроек вынесено в ConnectionConfigurator.
///
/// В перспективе эти методы станут активностями Elsa, чтобы последовательность шагов,
/// повторы и состояние держал движок, а не этот класс.
/// </summary>
public class SyncService : ITransientDependency
{
    private readonly ISyncStore           _store;
    private readonly AltiumService        _altium;
    private readonly OneCService          _onec;
    private readonly ILogger<SyncService> _log;

    // Пока только чтение из Altium, запись в 1С отключена.
    // Когда будем готовы включить запись → поменять на false
    public static bool ReadOnlyMode = true;

    public SyncService(ISyncStore store, AltiumService altium,
        OneCService onec, ILogger<SyncService> log)
    {
        _store  = store;
        _altium = altium;
        _onec   = onec;
        _log    = log;
    }

    // ── Импорт папок (тегов) из Altium → Sync DB ─────────────────────────────
    // Только читаем из Altium и сохраняем в маппинг-таблицу
    // Запись в 1С — отключена (ReadOnlyMode)

    public async Task SyncFoldersAsync()
    {
        _log.LogInformation("Загрузка тегов из Altium...");
        var altiumFolders = await _altium.GetFoldersAsync();
        _log.LogInformation("Получено {Count} тегов из Altium", altiumFolders.Count);

        int added = 0;
        foreach (var folder in altiumFolders)
        {
            // Сохраняем в маппинг-таблицу если ещё нет
            var existing = await _store.GetFolderMapByAltiumIdAsync(folder.Id);
            if (existing != null) continue;

            await _store.UpsertFolderMapAsync(new FolderMap
            {
                AltiumFolderId   = folder.Id,
                AltiumFolderPath = BuildFolderPath(folder, altiumFolders),
                OnecGroupGuid    = "",   // заполним позже когда включим 1С
                OnecGroupPath    = "",
                AutoCreated      = true,
            });

            await _store.AddLogAsync(new SyncLogEntry
            {
                EntityType = "Folder",
                EntityId   = folder.Id,
                EntityName = folder.Name,
                Action     = "Import",
                Status     = "OK",
                Message    = ReadOnlyMode ? "ReadOnly: сохранено локально" : null,
            });
            added++;
        }

        _log.LogInformation("Теги: {Added} новых добавлено в маппинг", added);
    }

    // ── Импорт компонентов из Altium → Sync DB ────────────────────────────────
    // Только читаем и сохраняем локально. Запись в 1С — отключена.

    public async Task SyncComponentsAsync()
    {
        _log.LogInformation("Загрузка компонентов из Altium...");
        var components = await _altium.GetAllComponentsAsync();
        var filter     = _altium.LifecycleFilter;

        _log.LogInformation(
            "Получено {Count} компонентов. Фильтр lifecycle: '{Filter}'",
            components.Count,
            string.IsNullOrEmpty(filter) ? "(не задан)" : filter);

        int synced = 0, skipped = 0;
        foreach (var comp in components)
        {
            // Фильтр по lifecycle state
            if (!string.IsNullOrEmpty(filter) &&
                !string.Equals(comp.LifecycleState, filter,
                    StringComparison.OrdinalIgnoreCase))
            {
                skipped++;
                continue;
            }

            // Фильтр по параметру Sync_1C = No
            if (comp.Parameters.TryGetValue("Sync_1C", out var flag) &&
                string.Equals(flag, "No", StringComparison.OrdinalIgnoreCase))
            {
                skipped++;
                continue;
            }

            await ImportSingleComponentAsync(comp);
            synced++;
        }

        _log.LogInformation(
            "Компоненты: {Synced} импортировано, {Skipped} пропущено",
            synced, skipped);
    }

    // Сохраняем компонент в Sync DB.
    // В ReadOnlyMode — НЕ трогаем 1С, только фиксируем локально.
    public async Task ImportSingleComponentAsync(AltiumComponent comp)
    {
        try
        {
            string folderPath = "";
            if (!string.IsNullOrEmpty(comp.FolderId))
            {
                var fm = await _store.GetFolderMapByAltiumIdAsync(comp.FolderId);
                folderPath = fm?.AltiumFolderPath ?? comp.FolderId;
            }

            var existing = await _store.GetComponentByAltiumIdAsync(comp.Id);

            await _store.UpsertComponentAsync(new ComponentMap
            {
                AltiumId       = comp.Id,
                AltiumItemGuid = comp.ItemGuid    ?? "",
                AltiumName     = comp.Name,
                AltiumPath     = folderPath,
                Article        = comp.Article,
                Description    = comp.Description  ?? "",
                Manufacturer   = comp.Manufacturer ?? "",
                Name1C         = comp.Name1C       ?? "",
                OnecGuid       = existing?.OnecGuid ?? "",
                OnecName       = existing?.OnecName ?? "",
                SyncStatus     = ReadOnlyMode ? "Imported" : "Pending",
                SyncedAt       = DateTime.UtcNow,
            });

            await _store.AddLogAsync(new SyncLogEntry
            {
                EntityType = "Component",
                EntityId   = comp.Id,
                EntityName = comp.Name,
                Action     = existing == null ? "Import" : "Update",
                Status     = "OK",
                Message    = ReadOnlyMode
                    ? $"ReadOnly | {comp.Manufacturer} | {(comp.Description?.Length > 40 ? comp.Description[..40] + "..." : comp.Description)}"
                    : null,
            });

            if (!ReadOnlyMode)
            {
                // TODO: здесь будет логика записи в 1С
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Ошибка импорта компонента {Name}", comp.Name);
            await _store.UpdateComponentStatusAsync(comp.Id, "Error", ex.Message);
            await _store.AddLogAsync(new SyncLogEntry
            {
                EntityType = "Component",
                EntityId   = comp.Id,
                EntityName = comp.Name,
                Action     = "Error",
                Status     = "Error",
                Message    = ex.Message,
            });
        }

        await Task.CompletedTask;
    }

    // ── Проекты / BOM ─────────────────────────────────────────────────────────
    // Тоже только чтение пока ReadOnlyMode = true

    public async Task SyncProjectAsync(ProjectMap project, List<BomLine> bomLines)
    {
        if (ReadOnlyMode)
        {
            _log.LogInformation(
                "ReadOnly: проект {Name} загружен, запись в 1С пропущена",
                project.AltiumProjectName);

            project.SyncStatus  = "Imported";
            project.SyncedAt    = DateTime.UtcNow;
            project.ErrorMessage = null;
            await _store.UpsertProjectAsync(project);

            await _store.AddLogAsync(new SyncLogEntry
            {
                EntityType = "Project",
                EntityId   = project.AltiumProjectId,
                EntityName = project.AltiumProjectName,
                Action     = "Import",
                Status     = "OK",
                Message    = "ReadOnly: данные сохранены локально",
            });
            return;
        }

        // ── Полная логика (включается когда ReadOnlyMode = false) ─────────────
        try
        {
            var resolvedLines = await ResolveBomLinesAsync(bomLines);

            switch (project.SyncAction)
            {
                case "New":
                    await CreateNewNomenclatureWithSpecAsync(project, resolvedLines);
                    break;
                case "Update":
                    await UpdateExistingSpecAsync(project, resolvedLines);
                    break;
                default:
                    _log.LogInformation("Проект {Name} — Skip", project.AltiumProjectName);
                    return;
            }

            project.SyncStatus   = "Synced";
            project.SyncedAt     = DateTime.UtcNow;
            project.ErrorMessage = null;
            await _store.UpsertProjectAsync(project);

            await _store.AddLogAsync(new SyncLogEntry
            {
                EntityType = "Project",
                EntityId   = project.AltiumProjectId,
                EntityName = project.AltiumProjectName,
                Action     = project.SyncAction,
                Status     = "OK",
            });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Ошибка синхронизации проекта {Name}", project.AltiumProjectName);
            project.SyncStatus   = "Error";
            project.ErrorMessage = ex.Message;
            await _store.UpsertProjectAsync(project);
            await _store.AddLogAsync(new SyncLogEntry
            {
                EntityType = "Project",
                EntityId   = project.AltiumProjectId,
                EntityName = project.AltiumProjectName,
                Action     = "Error",
                Status     = "Error",
                Message    = ex.Message,
            });
        }
    }

    // ── Обработка очереди от Add-In ───────────────────────────────────────────

    public async Task ProcessQueueItemAsync(SyncQueueItem item)
    {
        switch (item.Type)
        {
            case "component":
                var comp = new AltiumComponent
                {
                    Id             = item.AltiumId,
                    Name           = item.AltiumName,
                    FolderId       = item.AltiumPath,
                    LifecycleState = "Released",
                    Parameters     = item.Parameters,
                    Article        = item.Parameters.GetValueOrDefault(
                        "Manufacturer Part Number", ""),
                };
                await ImportSingleComponentAsync(comp);
                break;

            case "project":
                var project = new ProjectMap
                {
                    AltiumProjectId   = item.AltiumId,
                    AltiumProjectName = item.AltiumName,
                    AltiumRevision    = item.Parameters.GetValueOrDefault("Revision", ""),
                    SyncAction        = item.SyncAction,
                    SyncStatus        = "Pending",
                };
                var existing = (await _store.GetProjectsAsync())
                    .FirstOrDefault(p => p.AltiumProjectId == item.AltiumId);
                if (existing != null)
                {
                    project.OnecNomenclatureGuid = existing.OnecNomenclatureGuid;
                    project.OnecSpecGuid         = existing.OnecSpecGuid;
                }
                await SyncProjectAsync(project, item.BomLines);
                break;
        }
    }

    // ── Вспомогательные ───────────────────────────────────────────────────────

    // Строим читаемый путь папки: "Passive components / Resistors / SMD"
    private static string BuildFolderPath(AltiumFolder folder, List<AltiumFolder> all)
    {
        var parts = new List<string> { folder.Name };
        var current = folder;
        int guard = 0;
        while (!string.IsNullOrEmpty(current.ParentId) && guard++ < 10)
        {
            var parent = all.FirstOrDefault(f => f.Id == current.ParentId);
            if (parent == null) break;
            parts.Insert(0, parent.Name);
            current = parent;
        }
        return string.Join(" / ", parts);
    }

    private async Task<List<BomLine>> ResolveBomLinesAsync(List<BomLine> lines)
    {
        var resolved = new List<BomLine>();
        foreach (var line in lines)
        {
            var map = await _store.GetComponentByAltiumIdAsync(line.AltiumId);
            if (map == null)
            {
                _log.LogWarning(
                    "Компонент {Id} не найден в маппинге, строка BOM пропущена",
                    line.AltiumId);
                continue;
            }
            resolved.Add(line with { AltiumId = map.OnecGuid });
        }
        return resolved;
    }

    private async Task CreateNewNomenclatureWithSpecAsync(
        ProjectMap project, List<BomLine> lines)
    {
        var nomenclatureGuid = await _onec.CreateNomenclatureAsync(
            project.AltiumProjectName, project.AltiumProjectId);
        var specGuid = await _onec.CreateSpecificationAsync(
            nomenclatureGuid,
            $"{project.AltiumProjectName} rev.{project.AltiumRevision}",
            lines);
        project.OnecNomenclatureGuid = nomenclatureGuid;
        project.OnecSpecGuid         = specGuid;
    }

    private async Task UpdateExistingSpecAsync(ProjectMap project, List<BomLine> lines)
    {
        if (string.IsNullOrEmpty(project.OnecSpecGuid))
            throw new InvalidOperationException(
                "Нет привязанной спецификации для обновления");
        await _onec.UpdateSpecificationAsync(project.OnecSpecGuid, lines);
    }
}
