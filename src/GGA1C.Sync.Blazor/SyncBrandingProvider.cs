using Microsoft.Extensions.Localization;
using GGA1C.Sync.Localization;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Ui.Branding;

namespace GGA1C.Sync.Blazor;

[Dependency(ReplaceServices = true)]
public class SyncBrandingProvider : DefaultBrandingProvider
{
    private IStringLocalizer<SyncResource> _localizer;

    public SyncBrandingProvider(IStringLocalizer<SyncResource> localizer)
    {
        _localizer = localizer;
    }

    public override string AppName => _localizer["AppName"];
}
