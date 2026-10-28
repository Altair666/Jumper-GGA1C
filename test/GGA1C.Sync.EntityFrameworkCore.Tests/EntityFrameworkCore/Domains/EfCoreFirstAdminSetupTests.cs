using GGA1C.Sync.Identity;
using Xunit;

namespace GGA1C.Sync.EntityFrameworkCore.Domains;

[Collection(SyncTestConsts.CollectionDefinitionName)]
public class EfCoreFirstAdminSetupTests : FirstAdminSetupTests<SyncEntityFrameworkCoreTestModule>
{
}