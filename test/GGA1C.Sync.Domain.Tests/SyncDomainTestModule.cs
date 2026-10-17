using Volo.Abp.Modularity;

namespace GGA1C.Sync;

[DependsOn(
    typeof(SyncDomainModule),
    typeof(SyncTestBaseModule)
)]
public class SyncDomainTestModule : AbpModule
{

}
