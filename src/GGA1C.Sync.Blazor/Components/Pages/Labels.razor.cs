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
    private string NewSlug { get; set; } = "";

    /// <summary>Правил ли папку руками. Если да — больше не перебиваем её из имени.</summary>
    private bool SlugTouched { get; set; }

    private string SlugHint => SlugTouched
        ? "Латиница, цифры, дефис, подчёркивание"
        : "Подставляется из фамилии; можно поправить";

    /// <summary>
    /// Папка буфера выводится из фамилии транслитерацией. Кириллица в пути и в имени
    /// сетевой шары для GoLabel2 неуместна, поэтому именно латиница.
    /// </summary>
    private void OnNameChanged(string value)
    {
        NewName = value;

        if (!SlugTouched)
        {
            NewSlug = SlugGenerator.FromName(value);
        }
    }

    private void OnSlugChanged(string value)
    {
        NewSlug = value;

        // Пустое поле означает «снова выводить из имени»: так опечатку можно откатить,
        // просто стерев содержимое.
        SlugTouched = !string.IsNullOrWhiteSpace(value);
    }

    // Ключ показываем отдельной плашкой: в таблице его быть не должно, иначе он утечёт
    // в чей-нибудь скриншот при разборе совсем другого вопроса.
    private string? ShownKey { get; set; }
    private string ShownKeyFor { get; set; } = "";

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
            var key = await ClientsAppService.CreateAsync(NewName, NewSlug);
            ShownKeyFor = $"Ключ для «{NewName}»";
            ShownKey = key;
            NewName = "";
            NewSlug = "";
            SlugTouched = false;
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
