using System.Threading.Tasks;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Identity;

namespace GGA1C.Sync.Identity;

/// <summary>
/// Отключает штатную сеялку ABP, которая на чистой базе заводит <c>admin</c> с паролем
/// по умолчанию (<c>1q2w3E*</c>) — его знает любой, кто видел шаблон ABP. Вместо неё
/// первого администратора создаёт человек на странице <c>/setup</c> (см.
/// <see cref="FirstAdminSetup"/>). Уже существующих пользователей и ролей эта замена не
/// касается: на стенде <c>admin</c> остаётся как был.
///
/// Права роли <c>admin</c> по-прежнему раздаёт засев ABP (<c>PermissionDataSeedContributor</c>
/// выдаёт их по имени роли, даже если самой роли ещё нет).
/// </summary>
[Dependency(ReplaceServices = true)]
// Себя тоже: засев ABP достаёт каждого участника из DI по его собственному типу.
[ExposeServices(typeof(IdentityDataSeedContributor), typeof(NoDefaultAdminDataSeedContributor))]
public class NoDefaultAdminDataSeedContributor : IdentityDataSeedContributor
{
    public NoDefaultAdminDataSeedContributor(IIdentityDataSeeder identityDataSeeder)
        : base(identityDataSeeder)
    {
    }

    public override Task SeedAsync(DataSeedContext context)
    {
        return Task.CompletedTask;
    }
}
