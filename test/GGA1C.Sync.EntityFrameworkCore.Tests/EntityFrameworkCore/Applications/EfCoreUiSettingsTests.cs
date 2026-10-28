using GGA1C.Sync.Ui;
using Xunit;

namespace GGA1C.Sync.EntityFrameworkCore.Applications;

[Collection(SyncTestConsts.CollectionDefinitionName)]
public class EfCoreUiSettingsTests : UiSettingsTests<SyncEntityFrameworkCoreTestModule>
{
}
