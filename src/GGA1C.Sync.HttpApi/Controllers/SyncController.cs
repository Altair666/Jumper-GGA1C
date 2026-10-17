using GGA1C.Sync.Localization;
using Volo.Abp.AspNetCore.Mvc;

namespace GGA1C.Sync.Controllers;

/* Inherit your controllers from this class.
 */
public abstract class SyncController : AbpControllerBase
{
    protected SyncController()
    {
        LocalizationResource = typeof(SyncResource);
    }
}
