using Volo.Abp.Modularity;

namespace GGA1C.Sync;

/* Inherit from this class for your domain layer tests. */
public abstract class SyncDomainTestBase<TStartupModule> : SyncTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{

}
