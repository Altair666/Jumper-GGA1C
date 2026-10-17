using Xunit;

namespace GGA1C.Sync.EntityFrameworkCore;

[CollectionDefinition(SyncTestConsts.CollectionDefinitionName)]
public class SyncEntityFrameworkCoreCollection : ICollectionFixture<SyncEntityFrameworkCoreFixture>
{

}
