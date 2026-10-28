using GGA1C.Sync.Identity;
using Xunit;

namespace GGA1C.Sync.EntityFrameworkCore.Domains;

[Collection(SyncTestConsts.CollectionDefinitionName)]
public class EfCoreHubUserAccessTests : HubUserAccessTests<SyncEntityFrameworkCoreTestModule>
{
}
