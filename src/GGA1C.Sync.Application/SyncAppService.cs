using System;
using System.Collections.Generic;
using System.Text;
using GGA1C.Sync.Localization;
using Volo.Abp.Application.Services;

namespace GGA1C.Sync;

/* Inherit your application services from this class.
 */
public abstract class SyncAppService : ApplicationService
{
    protected SyncAppService()
    {
        LocalizationResource = typeof(SyncResource);
    }
}
