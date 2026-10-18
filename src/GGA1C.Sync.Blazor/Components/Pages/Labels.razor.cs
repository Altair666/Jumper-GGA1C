using System.Collections.Generic;
using System.Threading.Tasks;
using GGA1C.Sync.Connections;
using GGA1C.Sync.Keycloak;
using GGA1C.Sync.Labels;
using Microsoft.AspNetCore.Components;

// Пространство имён должно совпадать с тем, что генерирует razor для файла в
// Components/Pages, иначе partial-класс не склеится с компонентом и переопределения
// вроде OnInitializedAsync не найдут базовый метод.
namespace GGA1C.Sync.Blazor.Components.Pages;

public partial class Labels
{
    [Inject] private ILabelClientsAppService ClientsAppService { get; set; } = default!;

    private List<LabelClientDto> Clients { get; set; } = new();

    private string NewName { get; set; } = "";
    private string NewEmail { get; set; } = "";

    // Ключ показываем отдельной плашкой: в таблице его быть не должно, иначе он утечёт
    // в чей-нибудь скриншот при разборе совсем другого вопроса.
    private string? ShownKey { get; set; }

    private string ShownKeyFor { get; set; } = "";

    private string SecretHint => Kc.HasClientSecret
        ? "Сохранён. Пустое поле — оставить как есть"
        : "Не задан";

    private KeycloakSettingsDto Kc { get; set; } = new();

    private ConnectionTestResultDto? KcTest { get; set; }

    private KeycloakSyncReport? KcReport { get; set; }

    private bool Busy { get; set; }

    protected override async Task OnInitializedAsync()
    {
        await LoadAsync();
        Kc = await ClientsAppService.GetKeycloakSettingsAsync();
    }

    private async Task LoadAsync()
    {
        Busy = true;
        try
        {
            Clients = await ClientsAppService.GetListAsync();
        }
        finally
        {
            Busy = false;
        }
    }

    private async Task CreateAsync()
    {
        Busy = true;
        try
        {
            var key = await ClientsAppService.CreateAsync(NewName, NewEmail);
            ShownKeyFor = $"Ключ для «{NewName}»";
            ShownKey = key;
            NewName = "";
            NewEmail = "";
            await LoadAsync();
            await Notify.Success("Учётная запись создана");
        }
        finally
        {
            Busy = false;
        }
    }

    private async Task RevealAsync(LabelClientDto client)
    {
        var key = await ClientsAppService.RevealKeyAsync(client.Id);

        if (string.IsNullOrEmpty(key))
        {
            await Notify.Warn("Ключ не сохранён в читаемом виде — выпустите новый");
            return;
        }

        ShownKeyFor = $"Ключ для «{client.Name}»";
        ShownKey = key;
    }

    private async Task RegenerateAsync(LabelClientDto client)
    {
        var confirmed = await Message.Confirm(
            $"Старый ключ «{client.Name}» сразу перестанет работать, и синхронизация из " +
            "его виджета будет отвечать 401, пока новый ключ не прописан в настройках. " +
            "Продолжить?");

        if (!confirmed)
        {
            return;
        }

        Busy = true;
        try
        {
            ShownKey = await ClientsAppService.RegenerateKeyAsync(client.Id);
            ShownKeyFor = $"Новый ключ для «{client.Name}»";
            await LoadAsync();
        }
        finally
        {
            Busy = false;
        }
    }

    private async Task ToggleAsync(LabelClientDto client, bool enabled)
    {
        Busy = true;
        try
        {
            await ClientsAppService.SetEnabledAsync(client.Id, enabled);
            await LoadAsync();
        }
        finally
        {
            Busy = false;
        }
    }

    private async Task ClearBufferAsync(LabelClientDto client)
    {
        var confirmed = await Message.Confirm(
            $"Удалить все этикетки из буфера «{client.Name}»? Следующая синхронизация " +
            "наполнит его заново.");

        if (!confirmed)
        {
            return;
        }

        Busy = true;
        try
        {
            await ClientsAppService.ClearBufferAsync(client.Id);
            await LoadAsync();
            await Notify.Success("Буфер очищен");
        }
        finally
        {
            Busy = false;
        }
    }

    private async Task DeleteAsync(LabelClientDto client)
    {
        var confirmed = await Message.Confirm(
            $"Удалить учётную запись «{client.Name}»? Ключ перестанет работать, а папка " +
            $"буфера будет удалена вместе с содержимым — сейчас в ней этикеток: " +
            $"{client.RowCount}. Если партия ещё не напечатана, сначала напечатайте её.");

        if (!confirmed)
        {
            return;
        }

        Busy = true;
        try
        {
            await ClientsAppService.DeleteAsync(client.Id);
            await LoadAsync();
            await Notify.Success("Учётная запись удалена");
        }
        finally
        {
            Busy = false;
        }
    }

    private async Task SaveKeycloakAsync()
    {
        Busy = true;
        try
        {
            await ClientsAppService.SaveKeycloakSettingsAsync(Kc);

            // Перечитываем: секрет в ответе приходит пустым, зато обновится флаг
            // «сохранён», и поле не останется с набранным значением.
            Kc = await ClientsAppService.GetKeycloakSettingsAsync();

            await Notify.Success("Настройки Keycloak сохранены");
        }
        finally
        {
            Busy = false;
        }
    }

    private async Task TestKeycloakAsync()
    {
        Busy = true;
        KcTest = null;
        KcReport = null;
        try
        {
            KcTest = await ClientsAppService.TestKeycloakAsync();
        }
        finally
        {
            Busy = false;
        }
    }

    private async Task SyncKeycloakAsync()
    {
        Busy = true;
        KcTest = null;
        KcReport = null;
        try
        {
            KcReport = await ClientsAppService.SyncFromKeycloakAsync();
            await LoadAsync();
        }
        finally
        {
            Busy = false;
        }
    }
}
