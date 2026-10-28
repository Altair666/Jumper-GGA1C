using GGA1C.Sync.Accounts;
using Xunit;

namespace GGA1C.Sync.EntityFrameworkCore.Applications;

[Collection(SyncTestConsts.CollectionDefinitionName)]
public class EfCoreMyCredentialsTests : MyCredentialsTests<SyncEntityFrameworkCoreTestModule>
{
}
