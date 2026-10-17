using System.Threading.Tasks;
using GGA1C.Sync.Connections;
using Microsoft.AspNetCore.Components;

// Пространство имён должно совпадать с тем, что генерирует razor для файла в
// Components/Pages, иначе partial-класс не склеится с компонентом.
namespace GGA1C.Sync.Blazor.Components.Pages;

public partial class Connections
{
    [Inject] private IConnectionsAppService ConnectionsAppService { get; set; } = default!;

    private ConnectionSettingsDto Model { get; set; } = new();

    private ConnectionTestResultDto? AltiumResult { get; set; }

    private ConnectionTestResultDto? OnecResult { get; set; }

    private bool Busy { get; set; }

    protected override async Task OnInitializedAsync()
    {
        Model = await ConnectionsAppService.GetAsync();
    }

    private static string SecretHint(bool stored)
    {
        return stored
            ? "Значение сохранено. Пустое поле — оставить как есть"
            : "Не задано";
    }

    private async Task SaveAsync()
    {
        Busy = true;

        try
        {
            await ConnectionsAppService.SaveAsync(Model);

            // Перечитываем: секреты в ответе приходят пустыми, зато обновятся флаги
            // «значение сохранено», и поля ввода не останутся с набранным паролем.
            Model = await ConnectionsAppService.GetAsync();

            await Notify.Success("Настройки сохранены");
        }
        finally
        {
            Busy = false;
        }
    }

    private async Task TestAltiumAsync()
    {
        Busy = true;
        AltiumResult = null;

        try
        {
            AltiumResult = await ConnectionsAppService.TestAltiumAsync();
        }
        finally
        {
            Busy = false;
        }
    }

    private async Task TestOneCAsync()
    {
        Busy = true;
        OnecResult = null;

        try
        {
            OnecResult = await ConnectionsAppService.TestOneCAsync();
        }
        finally
        {
            Busy = false;
        }
    }
}
