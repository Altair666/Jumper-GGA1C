using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GGA1C.Sync.Labels;
using Microsoft.AspNetCore.Components;

namespace GGA1C.Sync.Blazor.Components.Pages;

public partial class Buffers
{
    [Inject] private ILabelClientsAppService ClientsAppService { get; set; } = default!;

    private List<BufferContentsDto> BufferList { get; set; } = new();
    private bool Busy { get; set; }

    protected override Task OnInitializedAsync() => LoadBuffersAsync();

    private async Task LoadBuffersAsync()
    {
        Busy = true;
        try
        {
            BufferList = await ClientsAppService.GetBuffersAsync();
        }
        finally
        {
            Busy = false;
        }
    }

    private async Task DeleteFolderAsync(BufferContentsDto buffer)
    {
        var confirmed = await Message.Confirm(
            $"Удалить строки «{buffer.Slug}»? Учётной записи у них нет, " +
            $"этикеток: {buffer.RowCount}.");

        if (!confirmed)
        {
            return;
        }

        Busy = true;
        try
        {
            await ClientsAppService.DeleteBufferFolderAsync(buffer.Slug);
            await LoadBuffersAsync();
            await Notify.Success("Строки удалены");
        }
        finally
        {
            Busy = false;
        }
    }

    private static string FormatWhen(DateTime? utc)
    {
        return utc?.ToLocalTime().ToString("dd.MM.yyyy HH:mm") ?? "—";
    }

    private static string Plural(int count)
    {
        // Этикетка / этикетки / этикеток - иначе в заголовке буфера читается коряво
        var last = count % 10;
        var lastTwo = count % 100;

        if (last == 1 && lastTwo != 11)
        {
            return "этикетка";
        }

        if (last is >= 2 and <= 4 && lastTwo is < 12 or > 14)
        {
            return "этикетки";
        }

        return "этикеток";
    }
}
