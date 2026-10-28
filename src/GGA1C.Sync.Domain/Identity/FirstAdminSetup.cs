using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GGA1C.Sync.Settings;
using Microsoft.AspNetCore.Identity;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Guids;
using Volo.Abp.Identity;
using Volo.Abp.SettingManagement;
using Volo.Abp.Uow;
using IdentityRole = Volo.Abp.Identity.IdentityRole;
using IdentityUser = Volo.Abp.Identity.IdentityUser;

namespace GGA1C.Sync.Identity;

/// <summary>
/// Первый запуск, как в Docmost: самостоятельной регистрации нет, но пока в хабе нет ни
/// одного администратора, открыта страница создания его учётки. Создали — страница
/// закрывается навсегда.
///
/// «Навсегда» держится не на наличии администратора (его можно разжаловать или удалить
/// через интерфейс, и страница открылась бы снова для любого в сети), а на отметке
/// <see cref="SyncSettings.Setup.FirstAdminCreatedAt"/>: снять её можно только в базе.
/// На стенде, где <c>admin</c> завели раньше, отметки нет, но администратор есть — закрыто.
/// </summary>
public class FirstAdminSetup : ITransientDependency
{
    public const string AdminRoleName = "admin";

    // Два одновременных запроса не должны оба пройти проверку «администратора нет».
    // Экземпляр хаба один, поэтому достаточно замка в процессе.
    private static readonly SemaphoreSlim Lock = new(1, 1);

    private readonly IdentityUserManager _users;
    private readonly IdentityRoleManager _roles;
    private readonly ISettingManager _settings;
    private readonly IGuidGenerator _guids;
    private readonly IUnitOfWorkManager _uow;

    public FirstAdminSetup(
        IdentityUserManager users,
        IdentityRoleManager roles,
        ISettingManager settings,
        IGuidGenerator guids,
        IUnitOfWorkManager uow)
    {
        _uow = uow;
        _users = users;
        _roles = roles;
        _settings = settings;
        _guids = guids;
    }

    public async Task<bool> IsOpenAsync()
    {
        if (!string.IsNullOrWhiteSpace(
                await _settings.GetOrNullGlobalAsync(SyncSettings.Setup.FirstAdminCreatedAt)))
        {
            return false;
        }

        if (await _roles.FindByNameAsync(AdminRoleName) == null)
        {
            return true;
        }

        return !(await _users.GetUsersInRoleAsync(AdminRoleName)).Any();
    }

    public async Task<IdentityUser> CreateAsync(string userName, string email, string password)
    {
        await Lock.WaitAsync();
        try
        {
            // Своя единица работы, завершённая до снятия замка: иначе второй запрос
            // увидел бы базу без только что созданного администратора.
            using var uow = _uow.Begin(requiresNew: true, isTransactional: true);

            if (!await IsOpenAsync())
            {
                throw new UserFriendlyException("Администратор уже создан — страница закрыта.");
            }

            var role = await _roles.FindByNameAsync(AdminRoleName);
            if (role == null)
            {
                role = new IdentityRole(_guids.Create(), AdminRoleName)
                {
                    IsStatic = true,
                    IsPublic = true
                };
                (await _roles.CreateAsync(role)).CheckErrors();

                // AddToRoleAsync ищет роль запросом к базе - несохранённую он не найдёт.
                await uow.SaveChangesAsync();
            }

            var user = new IdentityUser(_guids.Create(), userName.Trim(), email.Trim());
            (await _users.CreateAsync(user, password)).CheckErrors();
            (await _users.AddToRoleAsync(user, AdminRoleName)).CheckErrors();

            await _settings.SetGlobalAsync(
                SyncSettings.Setup.FirstAdminCreatedAt,
                DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));

            await uow.CompleteAsync();
            return user;
        }
        finally
        {
            Lock.Release();
        }
    }
}
