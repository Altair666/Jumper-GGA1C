using System.Collections.Generic;
using System.Threading.Tasks;
using GGA1C.Sync.Labels;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;

// Пространство имён должно совпадать с тем, что генерирует razor для файла в
// Components/Pages, иначе partial-класс не склеится с компонентом и переопределения
// вроде OnInitializedAsync просто не найдут базовый метод.
namespace GGA1C.Sync.Blazor.Components.Pages;

public partial class Labels
{
    [Inject] private ILabelsAppService LabelsAppService { get; set; } = default!;
    [Inject] private IConfiguration Configuration { get; set; } = default!;

    private List<LabelRecord> Rows { get; set; } = new();

    private string? ApiKey { get; set; }

    private string BufferPath =>
        Configuration["Labels:DatabasePath"] ?? @"C:\GGA1C\buffers\labels.db";

    protected override async Task OnInitializedAsync()
    {
        await LoadAsync();
        ApiKey = await LabelsAppService.GetApiKeyAsync();
    }

    private async Task LoadAsync()
    {
        Rows = await LabelsAppService.GetAllAsync();
    }

    private async Task ClearAsync()
    {
        var confirmed = await Message.Confirm(
            "Удалить все записи из буфера этикеток? Следующая синхронизация из Grist " +
            "наполнит его заново.");

        if (!confirmed)
        {
            return;
        }

        await LabelsAppService.ClearAllAsync();
        await LoadAsync();
        await Notify.Success("Буфер очищен");
    }

    private async Task RegenerateKeyAsync()
    {
        var confirmed = await Message.Confirm(
            "Старый ключ сразу перестанет работать, и синхронизация из виджета будет " +
            "отвечать 401, пока новый ключ не прописан в его настройках. Продолжить?");

        if (!confirmed)
        {
            return;
        }

        ApiKey = await LabelsAppService.RegenerateApiKeyAsync();
        await Notify.Success("Выпущен новый ключ — обновите его в настройках виджета");
    }
}
