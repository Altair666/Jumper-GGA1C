using Volo.Abp.Modularity;

namespace GGA1C.Sync;

[DependsOn(
    typeof(SyncApplicationModule),
    typeof(SyncDomainTestModule)
)]
public class SyncApplicationTestModule : AbpModule
{

}
