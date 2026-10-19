using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GGA1C.Sync.Labels;
using Microsoft.AspNetCore.Components;
using MudBlazor;

// Пространство имён должно совпадать с тем, что генерирует razor для файла в
// Components/Pages, иначе partial-класс не склеится с компонентом и переопределения
// вроде OnInitializedAsync не найдут базовый метод.
namespace GGA1C.Sync.Blazor.Components.Pages;

public partial class Labels
{
    [Inject] private ILabelClientsAppService ClientsAppService { get; set; } = default!;

    private List<LabelClientDto> Clients { get; set; } = new();

    private List<BufferContentsDto> Buffers { get; set; } = new();

    private string NewName { get; set; } = "";
    private string NewEmail { get; set; } = "";

    // Ключ показываем отдельной плашкой: в таблице его быть не должно, иначе он утечёт
    // в чей-нибудь скриншот при разборе совсем другого вопроса.
    private string? ShownKey { get; set; }

    private string ShownKeyFor { get; set; } = "";

    /// <summary>Открытая карточка учётных данных. Пусто — карточка не показана.</summary>
    private AccountCredentialsDto? Creds { get; set; }

    /// <summary>Одинаковая ширина у кнопок справа от полей — иначе они разной длины.</summary>
    private const string ButtonWidth = "min-width: 210px;";

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
            Buffers = await ClientsAppService.GetBuffersAsync();
        }
        finally
        {
            Busy = false;
        }
    }

    private async Task LoadBuffersAsync()
    {
        Busy = true;
        try
        {
            Buffers = await ClientsAppService.GetBuffersAsync();
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

            // Карточку учётных данных закрываем: она могла быть открыта на удалённой учётке
            if (Creds?.Id == client.Id)
            {
                Creds = null;
            }

            await LoadAsync();
            await Notify.Success("Учётная запись удалена");
        }
        finally
        {
            Busy = false;
        }
    }

    private async Task DeleteFolderAsync(BufferContentsDto buffer)
    {
        var confirmed = await Message.Confirm(
            $"Удалить папку «{buffer.Slug}» вместе с файлом? Учётной записи у неё нет, " +
            $"этикеток внутри: {buffer.RowCount}.");

        if (!confirmed)
        {
            return;
        }

        Busy = true;
        try
        {
            await ClientsAppService.DeleteBufferFolderAsync(buffer.Slug);
            await LoadBuffersAsync();
            await Notify.Success("Папка удалена");
        }
        finally
        {
            Busy = false;
        }
    }

    // ── Учётные данные во внешних системах ────────────────────────────────────

    /// <summary>Виден ли пароль MySQL: его, в отличие от прочих, разрешено смотреть.</summary>
    private bool PrintPasswordShown { get; set; }

    private async Task OpenCredsAsync(LabelClientDto client)
    {
        Busy = true;
        PrintPasswordShown = false;
        try
        {
            Creds = await ClientsAppService.GetCredentialsAsync(client.Id);
        }
        finally
        {
            Busy = false;
        }
    }

    /// <summary>
    /// Показать или снова скрыть пароль MySQL. При показе значение подтягивается с сервера:
    /// в карточку он приходит точками, чтобы не уезжать в браузер без спроса.
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
            Creds.PrintPassword = new string('\u2022', Creds.PrintPassword.Length);
            return;
        }

        Busy = true;
        try
        {
            var password = await ClientsAppService.RevealPrintPasswordAsync(Creds.Id);

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

    /// <summary>
    /// Новый пароль только подставляется в поле — применяется он при сохранении карточки,
    /// чтобы случайное нажатие не отрезало человеку печать.
    /// </summary>
    private void GeneratePrintPassword()
    {
        if (Creds == null)
        {
            return;
        }

        Creds.PrintPassword = ClientsAppService.GeneratePrintPassword();
        PrintPasswordShown = true;
    }

    private async Task SaveCredsAsync()
    {
        if (Creds == null)
        {
            return;
        }

        Busy = true;
        try
        {
            await ClientsAppService.SaveCredentialsAsync(Creds);

            // Перечитываем: пароли в ответе приходят пустыми, зато обновятся признаки
            // «задан», и поля не останутся с набранными значениями.
            Creds = await ClientsAppService.GetCredentialsAsync(Creds.Id);

            PrintPasswordShown = false;
            await LoadAsync();
            await Notify.Success("Учётные данные сохранены");
        }
        finally
        {
            Busy = false;
        }
    }

    // ── Мелочи для разметки ───────────────────────────────────────────────────

    private static Color StateColor(bool has) => has ? Color.Success : Color.Default;

    /// <summary>
    /// Зелёная галочка в поле: значение на месте. Для пароля «на месте» означает, что он
    /// сохранён, — само значение наружу не отдаётся, в поле приходят только точки.
    /// </summary>
    private static Adornment Mark(bool ok) => ok ? Adornment.End : Adornment.None;

    private static bool Filled(string? value) => !string.IsNullOrWhiteSpace(value);

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
