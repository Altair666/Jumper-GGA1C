using Volo.Abp.Account;
using Volo.Abp.Mapperly;
using Volo.Abp.FeatureManagement;
using Volo.Abp.Identity;
using Volo.Abp.Modularity;
using Volo.Abp.PermissionManagement;
using Volo.Abp.SettingManagement;
using Volo.Abp.TenantManagement;
using Microsoft.Extensions.DependencyInjection;
using GGA1C.Sync.Printing.Profiles;
using GGA1C.Sync.Printing.Devices;
using Microsoft.Extensions.Logging;

namespace GGA1C.Sync;

[DependsOn(
    typeof(SyncDomainModule),
    typeof(AbpAccountApplicationModule),
    typeof(SyncApplicationContractsModule),
    typeof(AbpIdentityApplicationModule),
    typeof(AbpPermissionManagementApplicationModule),
    typeof(AbpTenantManagementApplicationModule),
    typeof(AbpFeatureManagementApplicationModule),
    typeof(AbpSettingManagementApplicationModule)
    )]
public class SyncApplicationModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddMapperlyObjectMapper<SyncApplicationModule>();
        var folder = context.Services.GetConfiguration()["Printing:ProfilesFolder"] ?? "C:/GGA1C/printer-profiles";
        context.Services.AddSingleton(new FilePrinterProfileStore(folder));
        context.Services.AddSingleton<PrinterProfiles>();
        var dryRun = !bool.TryParse(context.Services.GetConfiguration()["Printing:SettingsDryRun"], out var configured) || configured;
        context.Services.AddSingleton(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<GodexPrinterSettings>>();
            return new GodexPrinterSettings(sp.GetRequiredService<PrinterProfiles>(), dryRun,
                message => logger.LogInformation("{PrinterSettingsMessage}", message));
        });
    }
}
