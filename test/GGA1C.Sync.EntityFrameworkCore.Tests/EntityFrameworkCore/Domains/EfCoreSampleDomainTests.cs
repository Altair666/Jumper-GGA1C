using GGA1C.Sync.Samples;
using Xunit;

namespace GGA1C.Sync.EntityFrameworkCore.Domains;

[Collection(SyncTestConsts.CollectionDefinitionName)]
public class EfCoreSampleDomainTests : SampleDomainTests<SyncEntityFrameworkCoreTestModule>
{

}
