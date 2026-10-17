using System.Threading.Tasks;

namespace GGA1C.Sync.Data;

public interface ISyncDbSchemaMigrator
{
    Task MigrateAsync();
}
