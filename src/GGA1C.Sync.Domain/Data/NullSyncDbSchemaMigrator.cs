using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;

namespace GGA1C.Sync.Data;

/* This is used if database provider does't define
 * ISyncDbSchemaMigrator implementation.
 */
public class NullSyncDbSchemaMigrator : ISyncDbSchemaMigrator, ITransientDependency
{
    public Task MigrateAsync()
    {
        return Task.CompletedTask;
    }
}
