using GGA1C.Sync.EntityFrameworkCore;
using Volo.Abp.Autofac;
using Volo.Abp.Modularity;

namespace GGA1C.Sync.DbMigrator;

[DependsOn(
    typeof(AbpAutofacModule),
    typeof(SyncEntityFrameworkCoreModule),
    typeof(SyncApplicationContractsModule)
    )]
public class SyncDbMigratorModule : AbpModule
{
}
