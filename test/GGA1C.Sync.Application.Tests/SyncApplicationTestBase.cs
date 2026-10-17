using Volo.Abp.Modularity;

namespace GGA1C.Sync;

public abstract class SyncApplicationTestBase<TStartupModule> : SyncTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{

}
