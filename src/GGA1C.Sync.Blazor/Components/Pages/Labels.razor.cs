using System.Collections.Generic;
using System.Threading.Tasks;
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

    // Ключ показываем отдельной плашкой: в таблице его быть не должно, иначе он утечёт
    // в чей-нибудь скриншот при разборе совсем другого вопроса.
    private string? ShownKey { get; set; }
    private string ShownKeyFor { get; set; } = "";

    private bool Busy { get; set; }

    protected override async Task OnInitializedAsync()
    {
        await LoadAsync();
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
            $"Удалить учётную запись «{client.Name}»? Её ключ перестанет работать. " +
            "Файл буфера останется на диске — в нём может быть неотпечатанная партия.");

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
}