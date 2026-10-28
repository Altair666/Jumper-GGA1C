using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using GGA1C.Sync.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.AspNetCore.Mvc.UI.RazorPages;

namespace GGA1C.Sync.Blazor.Pages;

/// <summary>
/// Страница первого запуска (см. <see cref="FirstAdminSetup"/>). Пока открыта — создаёт
/// первого администратора; закрыта — отвечает 404, чтобы снаружи не было видно даже того,
/// что такая страница существует.
/// </summary>
public class SetupModel : AbpPageModel
{
    private readonly FirstAdminSetup _setup;

    [BindProperty]
    public SetupInput Input { get; set; } = new();

    public string? Error { get; set; }

    public SetupModel(FirstAdminSetup setup)
    {
        _setup = setup;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        return await _setup.IsOpenAsync() ? Page() : NotFound();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!await _setup.IsOpenAsync())
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        if (!string.Equals(Input.Password, Input.PasswordRepeat, StringComparison.Ordinal))
        {
            Error = "Пароли не совпадают.";
            return Page();
        }

        try
        {
            var user = await _setup.CreateAsync(Input.UserName, Input.Email, Input.Password);
            Logger.LogWarning("Первый запуск: создан администратор {UserName}", user.UserName);
        }
        catch (Exception ex) when (ex is BusinessException or UserFriendlyException)
        {
            // Ошибки политики паролей и занятые имена приходят отсюда - показываем как есть.
            Error = ex is UserFriendlyException ? ex.Message : GetMessage(ex);
            return Page();
        }

        return Redirect("~/Account/Login");
    }

    private string GetMessage(Exception ex)
    {
        // AbpIdentityResultException кладёт текст ошибок Identity в Message.
        return string.IsNullOrWhiteSpace(ex.Message) ? "Не удалось создать учётку." : ex.Message;
    }

    public class SetupInput
    {
        [Required(ErrorMessage = "Укажите логин")]
        [StringLength(256)]
        public string UserName { get; set; } = "";

        [Required(ErrorMessage = "Укажите почту")]
        [EmailAddress(ErrorMessage = "Похоже, это не почта")]
        [StringLength(256)]
        public string Email { get; set; } = "";

        [Required(ErrorMessage = "Укажите пароль")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = "";

        [Required(ErrorMessage = "Повторите пароль")]
        [DataType(DataType.Password)]
        public string PasswordRepeat { get; set; } = "";
    }
}
