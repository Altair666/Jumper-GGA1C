using System;
using System.Threading.Tasks;
using GGA1C.Sync.Accounts;
using GGA1C.Sync.Labels;
using Microsoft.AspNetCore.Components;
using MudBlazor;

// Пространство имён — как у сгенерированного razor-компонента в Components/Pages, иначе
// partial-класс не склеится (см. Labels.razor.cs).
namespace GGA1C.Sync.Blazor.Components.Pages;

public partial class MyCredentials
{
    [Inject] private IMyAccountAppService MyAccountAppService { get; set; } = default!;

    private AccountCredentialsDto? Creds { get; set; }

    /// <summary>Своей учётки нет (локальный вход или сверка её не завела) — текст вместо карточки.</summary>
    private string? NoAccountMessage { get; set; }

    private bool Loading { get; set; } = true;

    private bool Busy { get; set; }

    /// <summary>Виден ли пароль MySQL: его, в отличие от прочих, разрешено смотреть.</summary>
    private bool PrintPasswordShown { get; set; }

    protected override async Task OnInitializedAsync()
    {
        try
        {
            // Сначала «кто я»: если своей учётки нет, показываем объяснение, а не ошибку.
            var me = await MyAccountAppService.GetAsync();

            if (!me.ViaKeycloak)
            {
                NoAccountMessage = "Вы вошли локальным паролем, а не через Keycloak, — своей " +
                                   "учётки с учётными данными у вас нет. Учётные данные людей " +
                                   "правятся на странице «Grist → GoLabel2».";
                return;
            }

            if (!me.HasAccount)
            {
                NoAccountMessage = "Ваша учётка в хабе не найдена: её заводит сверка с " +
                                   "Keycloak. Обратитесь к администратору хаба.";
                return;
            }

            Creds = await MyAccountAppService.GetCredentialsAsync();
        }
        finally
        {
            Loading = false;
        }
    }

    /// <summary>
    /// Показать или снова скрыть пароль MySQL. При показе значение подтягивается с сервера:
    /// в карточку он приходит точками.
    /// </summary>
    private async Task TogglePrintPasswordAsync()
    {
        if (Creds == null)
        {
            return;
        }

        if (PrintPasswordShown)
        {
            PrintPasswordShown = false;
            Creds.PrintPassword = new string('•', Creds.PrintPassword.Length);
            return;
        }

        Busy = true;
        try
        {
            var password = await MyAccountAppService.RevealPrintPasswordAsync();

            if (string.IsNullOrEmpty(password))
            {
                await Notify.Warn("Пароль ещё не задан — сгенерируйте и сохраните");
                return;
            }

            Creds.PrintPassword = password;
            PrintPasswordShown = true;
        }
        finally
        {
            Busy = false;
        }
    }

    /// <summary>Новый пароль только подставляется в поле — применяется сохранением.</summary>
    private void GeneratePrintPassword()
    {
        if (Creds == null)
        {
            return;
        }

        Creds.PrintPassword = MyAccountAppService.GeneratePrintPassword();
        PrintPasswordShown = true;
    }

    private async Task SaveAsync()
    {
        if (Creds == null)
        {
            return;
        }

        Busy = true;
        try
        {
            await MyAccountAppService.SaveCredentialsAsync(Creds);

            // Перечитываем: обновятся маски и признаки «задан».
            Creds = await MyAccountAppService.GetCredentialsAsync();
            PrintPasswordShown = false;
            await Notify.Success("Учётные данные сохранены");
        }
        finally
        {
            Busy = false;
        }
    }

    /// <summary>Зелёная галочка в поле: значение на месте.</summary>
    private static Adornment Mark(bool ok) => ok ? Adornment.End : Adornment.None;

    private static bool Filled(string? value) => !string.IsNullOrWhiteSpace(value);

    private static string FormatWhen(DateTime? utc)
    {
        return utc?.ToLocalTime().ToString("dd.MM.yyyy HH:mm") ?? "—";
    }
}
