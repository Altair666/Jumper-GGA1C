using System;
using System.Threading.Tasks;
using GGA1C.Sync.Altium;
using GGA1C.Sync.OneC;
using GGA1C.Sync.Settings;
using GGA1C.Sync.Sync;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.SettingManagement;
using Volo.Abp.Settings;

namespace GGA1C.Sync.Connections;

// Атрибут нужен и на классе: ABP ищет его на типе реализации, пометки на интерфейсе
// недостаточно (проверено на сервисе буфера этикеток - эндпоинт оставался анонимным).
[RemoteService(IsEnabled = false)]
[Authorize]
public class ConnectionsAppService : ApplicationService, IConnectionsAppService
{
    private readonly ISettingManager _settingManager;
    private readonly ConnectionConfigurator _configurator;
    private readonly AltiumService _altium;
    private readonly OneCService _onec;

    public ConnectionsAppService(
        ISettingManager settingManager,
        ConnectionConfigurator configurator,
        AltiumService altium,
        OneCService onec)
    {
        _settingManager = settingManager;
        _configurator = configurator;
        _altium = altium;
        _onec = onec;
    }

    public async Task<ConnectionSettingsDto> GetAsync()
    {
        var altiumPassword = await Get(SyncSettings.Altium.Password);
        var altiumCookies = await Get(SyncSettings.Altium.BrowserCookies);
        var onecPassword = await Get(SyncSettings.OneC.Password);

        return new ConnectionSettingsDto
        {
            AltiumServerUrl = await Get(SyncSettings.Altium.ServerUrl),
            AltiumUser = await Get(SyncSettings.Altium.User),

            // Сами секреты наружу не отдаём - только факт, что они заданы
            AltiumPassword = "",
            HasAltiumPassword = !string.IsNullOrEmpty(altiumPassword),
            AltiumBrowserCookies = "",
            HasAltiumBrowserCookies = !string.IsNullOrEmpty(altiumCookies),

            AltiumApiComponents = await Get(SyncSettings.Altium.ApiComponents),
            AltiumApiFolders = await Get(SyncSettings.Altium.ApiFolders),
            AltiumApiProjects = await Get(SyncSettings.Altium.ApiProjects),
            AltiumLifecycleFilter = await Get(SyncSettings.Altium.LifecycleFilter),

            OnecServerUrl = await Get(SyncSettings.OneC.ServerUrl),
            OnecBaseName = await Get(SyncSettings.OneC.BaseName),
            OnecUser = await Get(SyncSettings.OneC.User),
            OnecPassword = "",
            HasOnecPassword = !string.IsNullOrEmpty(onecPassword),

            AutoSyncEnabled = await SettingProvider.GetAsync<bool>(SyncSettings.AutoSyncEnabled),
            IntervalMinutes = await SettingProvider.GetAsync(SyncSettings.IntervalMinutes, 60),
            DefaultSyncAction = await Get(SyncSettings.DefaultSyncAction)
        };
    }

    public async Task SaveAsync(ConnectionSettingsDto input)
    {
        await Set(SyncSettings.Altium.ServerUrl, input.AltiumServerUrl);
        await Set(SyncSettings.Altium.User, input.AltiumUser);
        await Set(SyncSettings.Altium.ApiComponents, input.AltiumApiComponents);
        await Set(SyncSettings.Altium.ApiFolders, input.AltiumApiFolders);
        await Set(SyncSettings.Altium.ApiProjects, input.AltiumApiProjects);
        await Set(SyncSettings.Altium.LifecycleFilter, input.AltiumLifecycleFilter);

        await Set(SyncSettings.OneC.ServerUrl, input.OnecServerUrl);
        await Set(SyncSettings.OneC.BaseName, input.OnecBaseName);
        await Set(SyncSettings.OneC.User, input.OnecUser);

        await Set(SyncSettings.AutoSyncEnabled, input.AutoSyncEnabled.ToString().ToLowerInvariant());
        await Set(SyncSettings.IntervalMinutes, input.IntervalMinutes.ToString());
        await Set(SyncSettings.DefaultSyncAction, input.DefaultSyncAction);

        // Секреты перезаписываем только если в поле что-то ввели: пустое поле означает
        // «оставить прежнее значение», иначе каждое сохранение формы стирало бы пароли,
        // которых в ней и не было.
        await SetIfFilled(SyncSettings.Altium.Password, input.AltiumPassword);
        await SetIfFilled(SyncSettings.Altium.BrowserCookies, input.AltiumBrowserCookies);
        await SetIfFilled(SyncSettings.OneC.Password, input.OnecPassword);

        // Клиенты держат адреса у себя, поэтому после правки их надо переконфигурировать,
        // иначе проверка связи пойдёт по старому адресу.
        await _configurator.ApplyAllAsync();
    }

    public async Task<ConnectionTestResultDto> TestAltiumAsync()
    {
        await _configurator.ApplyAltiumAsync();

        try
        {
            var (ok, message) = await _altium.TestConnectionAsync();
            return new ConnectionTestResultDto { Ok = ok, Message = message };
        }
        catch (Exception ex)
        {
            return new ConnectionTestResultDto { Ok = false, Message = ex.Message };
        }
    }

    public async Task<ConnectionTestResultDto> TestOneCAsync()
    {
        await _configurator.ApplyOneCAsync();

        try
        {
            var ok = await _onec.TestConnectionAsync();
            return new ConnectionTestResultDto
            {
                Ok = ok,
                Message = ok ? "Подключение успешно" : "1С не ответила ожидаемым образом"
            };
        }
        catch (Exception ex)
        {
            return new ConnectionTestResultDto { Ok = false, Message = ex.Message };
        }
    }

    private async Task<string> Get(string name)
    {
        return await _settingManager.GetOrNullGlobalAsync(name) ?? "";
    }

    private async Task Set(string name, string value)
    {
        await _settingManager.SetGlobalAsync(name, value ?? "");
    }

    private async Task SetIfFilled(string name, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            await _settingManager.SetGlobalAsync(name, value);
        }
    }
}
