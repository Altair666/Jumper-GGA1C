using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using GGA1C.Sync.Data;
using Volo.Abp.DependencyInjection;

namespace GGA1C.Sync.EntityFrameworkCore;

public class EntityFrameworkCoreSyncDbSchemaMigrator
    : ISyncDbSchemaMigrator, ITransientDependency
{
    private readonly IServiceProvider _serviceProvider;

    public EntityFrameworkCoreSyncDbSchemaMigrator(
        IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task MigrateAsync()
    {
        /* We intentionally resolve the SyncDbContext
         * from IServiceProvider (instead of directly injecting it)
         * to properly get the connection string of the current tenant in the
         * current scope.
         */

        await _serviceProvider
            .GetRequiredService<SyncDbContext>()
            .Database
            .MigrateAsync();
    }
}
