using GGA1C.Sync.Localization;
using Volo.Abp.AspNetCore.Components;

namespace GGA1C.Sync.Blazor;

public abstract class SyncComponentBase : AbpComponentBase
{
    protected SyncComponentBase()
    {
        LocalizationResource = typeof(SyncResource);
    }
}
