using System.Threading.Tasks;
using GGA1C.Sync.Accounts;
using Microsoft.AspNetCore.Components;

// Пространство имён должно совпадать с тем, что генерирует razor для файла в
// Components/Pages, иначе partial-класс не склеится с компонентом (в шаблоне ABP здесь
// стояло GGA1C.Sync.Blazor.Pages - класс был пустой и ни с чем не склеивался).
namespace GGA1C.Sync.Blazor.Components.Pages;

public partial class Index
{
    [Inject] private IMyAccountAppService MyAccountAppService { get; set; } = default!;

    private MyAccountDto? Me { get; set; }

    protected override async Task OnInitializedAsync()
    {
        if (CurrentUser.IsAuthenticated)
        {
            Me = await MyAccountAppService.GetAsync();
        }
    }
}
