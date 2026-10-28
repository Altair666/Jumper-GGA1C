using System;
using Microsoft.Extensions.Localization;
using GGA1C.Sync.Localization;
using GGA1C.Sync.Settings;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Settings;
using Volo.Abp.Threading;
using Volo.Abp.Ui.Branding;

namespace GGA1C.Sync.Blazor;

/// <summary>
/// Имя приложения для всех «заголовочных» надписей: шапка MudBlazor-раскладки и заголовок
/// вкладки (PageHeader), бренд и title страниц входа LeptonX Lite. Берётся из настройки
/// <see cref="SyncSettings.Ui.ServerName"/>, которую меняет администратор.
/// </summary>
[Dependency(ReplaceServices = true)]
public class SyncBrandingProvider : DefaultBrandingProvider
{
    private readonly IStringLocalizer<SyncResource> _localizer;
    private readonly ISettingProvider _settingProvider;
    private readonly Lazy<string> _appName;

    public SyncBrandingProvider(IStringLocalizer<SyncResource> localizer, ISettingProvider settingProvider)
    {
        _localizer = localizer;
        _settingProvider = settingProvider;
        // IBrandingProvider.AppName синхронный, а настройки ABP читаются асинхронно. Значение
        // берётся один раз на экземпляр (он transient): в Blazor раскладка держит свой
        // экземпляр до перезагрузки страницы - новое имя появится после неё.
        _appName = new Lazy<string>(ReadAppName);
    }

    public override string AppName => _appName.Value;

    private string ReadAppName()
    {
        var name = AsyncHelper.RunSync(() => _settingProvider.GetOrNullAsync(SyncSettings.Ui.ServerName));
        return string.IsNullOrWhiteSpace(name) ? _localizer["AppName"] : name;
    }
}
