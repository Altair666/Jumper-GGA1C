using GGA1C.Sync.Samples;
using Xunit;

namespace GGA1C.Sync.EntityFrameworkCore.Applications;

[Collection(SyncTestConsts.CollectionDefinitionName)]
public class EfCoreSampleAppServiceTests : SampleAppServiceTests<SyncEntityFrameworkCoreTestModule>
{

}
