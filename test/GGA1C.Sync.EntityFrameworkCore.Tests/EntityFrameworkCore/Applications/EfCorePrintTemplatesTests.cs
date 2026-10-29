using GGA1C.Sync.Templates;
using Xunit;

namespace GGA1C.Sync.EntityFrameworkCore.Applications;

[Collection(SyncTestConsts.CollectionDefinitionName)]
public class EfCorePrintTemplatesTests : PrintTemplatesTests<SyncEntityFrameworkCoreTestModule>
{
}
