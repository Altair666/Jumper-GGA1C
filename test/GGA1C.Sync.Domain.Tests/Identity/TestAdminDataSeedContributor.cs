using System.Threading.Tasks;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;

namespace GGA1C.Sync.Identity;

/// <summary>
/// Штатная сеялка ABP admin больше не заводит (NoDefaultAdminDataSeedContributor), а
/// примеры тестов шаблона на него опираются. Создаём его тем же путём, что страница
/// первого запуска /setup, - заодно это проверка самого пути.
/// </summary>
public class TestAdminDataSeedContributor : IDataSeedContributor, ITransientDependency
{
    private readonly FirstAdminSetup _setup;

    public TestAdminDataSeedContributor(FirstAdminSetup setup)
    {
        _setup = setup;
    }

    public async Task SeedAsync(DataSeedContext context)
    {
        if (await _setup.IsOpenAsync())
        {
            await _setup.CreateAsync("admin", "admin@abp.io", "1q2w3E*");
        }
    }
}