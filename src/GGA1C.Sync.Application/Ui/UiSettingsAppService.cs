using System.Threading.Tasks;
using GGA1C.Sync.Branding;
using GGA1C.Sync.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.SettingManagement;
using Volo.Abp.Settings;

namespace GGA1C.Sync.Ui;

[RemoteService(IsEnabled = false)]
[Authorize(GGA1C.Sync.Permissions.SyncPermissions.Admin)]
public class UiSettingsAppService : SyncAppService, IUiSettingsAppService
{
    private readonly ISettingManager _settingManager;
    private readonly SiteIconStore _siteIcon;

    public UiSettingsAppService(ISettingManager settingManager, SiteIconStore siteIcon)
    {
        _settingManager = settingManager;
        _siteIcon = siteIcon;
    }

    public async Task<string> GetServerNameAsync()
    {
        var name = await SettingProvider.GetOrNullAsync(SyncSettings.Ui.ServerName);
        return string.IsNullOrWhiteSpace(name) ? SyncSettings.Ui.DefaultServerName : name;
    }

    public async Task SetServerNameAsync(string? serverName)
    {
        var name = serverName?.Trim();
        if (name?.Length > SyncSettings.Ui.MaxServerNameLength)
        {
            throw new UserFriendlyException(
                $"Имя сервера длиннее {SyncSettings.Ui.MaxServerNameLength} символов.");
        }

        // null удаляет строку; значение, равное умолчанию, ABP тоже не хранит (наследуется).
        // Кэш настроек ABP SettingManager обновляет сам — новое имя видно после перезагрузки
        // страницы. Важно: кэш у каждого процесса свой, второй экземпляр хаба на той же базе
        // увидит правку только когда истечёт его кэш.
        await _settingManager.SetGlobalAsync(
            SyncSettings.Ui.ServerName, string.IsNullOrEmpty(name) ? null : name);
    }

    public Task<SiteIconInfoDto> GetSiteIconAsync()
    {
        var file = _siteIcon.Find();
        return Task.FromResult(new SiteIconInfoDto
        {
            IsCustom = file != null,
            Url = SiteIcon.Url + "?v=" + (file?.LastWriteUtc.Ticks ?? 0),
        });
    }

    public async Task SetSiteIconAsync(string fileName, byte[] content)
    {
        await _siteIcon.SaveAsync(fileName, content);
        Logger.LogInformation("Значок сайта заменён: {FileName}, {Size} байт", fileName, content.Length);
    }

    public Task ResetSiteIconAsync()
    {
        _siteIcon.Reset();
        Logger.LogInformation("Значок сайта сброшен на значок по умолчанию");
        return Task.CompletedTask;
    }
}
