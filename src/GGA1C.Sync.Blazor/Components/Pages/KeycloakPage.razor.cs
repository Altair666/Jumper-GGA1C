using System.Threading.Tasks;
using GGA1C.Sync.Connections;
using GGA1C.Sync.Keycloak;
using GGA1C.Sync.Labels;
using Microsoft.AspNetCore.Components;

// Пространство имён должно совпадать с тем, что генерирует razor для файла в
// Components/Pages, иначе partial-класс не склеится с компонентом.
namespace GGA1C.Sync.Blazor.Components.Pages;

public partial class KeycloakPage
{
    [Inject] private ILabelClientsAppService ClientsAppService { get; set; } = default!;

    private KeycloakSettingsDto Kc { get; set; } = new();

    private ConnectionTestResultDto? Test { get; set; }

    private KeycloakSyncReport? Report { get; set; }

    private bool Busy { get; set; }

    private string SecretHint => Kc.HasClientSecret
        ? "Сохранён. Пустое поле — оставить как есть"
        : "Не задан";

    protected override async Task OnInitializedAsync()
    {
        Kc = await ClientsAppService.GetKeycloakSettingsAsync();
    }

    private async Task SaveAsync()
    {
        Busy = true;
        try
        {
            await ClientsAppService.SaveKeycloakSettingsAsync(Kc);

            // Перечитываем: секрет в ответе приходит пустым, зато обновится признак
            // «сохранён», и поле не останется с набранным значением.
            Kc = await ClientsAppService.GetKeycloakSettingsAsync();

            await Notify.Success("Настройки Keycloak сохранены");
        }
        finally
        {
            Busy = false;
        }
    }

    private async Task TestAsync()
    {
        Busy = true;
        Test = null;
        Report = null;
        try
        {
            Test = await ClientsAppService.TestKeycloakAsync();
        }
        finally
        {
            Busy = false;
        }
    }

    private async Task SyncAsync()
    {
        Busy = true;
        Test = null;
        Report = null;
        try
        {
            Report = await ClientsAppService.SyncFromKeycloakAsync();
        }
        finally
        {
            Busy = false;
        }
    }
}
