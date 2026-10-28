using System;
using System.IO;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using GGA1C.Sync.Blazor.Components;
using GGA1C.Sync.Blazor.Menus;
using GGA1C.Sync.EntityFrameworkCore;
using GGA1C.Sync.Localization;
using GGA1C.Sync.MultiTenancy;
using OpenIddict.Validation.AspNetCore;
using Volo.Abp;
using Volo.Abp.Account.Web;
using Volo.Abp.AspNetCore.Components.Web;
using Volo.Abp.AspNetCore.Components.Server.MudBlazorBasicTheme;
using Volo.Abp.AspNetCore.Components.Server.MudBlazorBasicTheme.Bundling;
using Volo.Abp.AspNetCore.Mvc.UI.Theme.LeptonXLite;
using Volo.Abp.AspNetCore.Mvc.UI.Theme.LeptonXLite.Bundling;
using Volo.Abp.AspNetCore.Components.Web.Theming.MudBlazor.Routing;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.AspNetCore.Mvc.Localization;
using Volo.Abp.AspNetCore.Mvc.UI;
using Volo.Abp.AspNetCore.Mvc.UI.Bootstrap;
using Volo.Abp.AspNetCore.Mvc.UI.Bundling;
using Volo.Abp.AspNetCore.Mvc.UI.MultiTenancy;
using Volo.Abp.AspNetCore.Serilog;
using Volo.Abp.Autofac;
using Volo.Abp.Mapperly;
using Volo.Abp.Identity.Blazor.MudBlazor.Server;
using System.IO;
using System.Threading.Tasks;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.Modularity;
using Volo.Abp.Security.Claims;
using Volo.Abp.SettingManagement.Blazor.MudBlazor.Server;
using Volo.Abp.Swashbuckle;
using Volo.Abp.TenantManagement.Blazor.MudBlazor.Server;
using Volo.Abp.OpenIddict;
using Volo.Abp.UI;
using Volo.Abp.UI.Navigation;
using Volo.Abp.UI.Navigation.Urls;
using Volo.Abp.VirtualFileSystem;

namespace GGA1C.Sync.Blazor;

[DependsOn(
    typeof(SyncApplicationModule),
    typeof(SyncEntityFrameworkCoreModule),
    typeof(SyncHttpApiModule),
    typeof(AbpAutofacModule),
    typeof(AbpSwashbuckleModule),
    typeof(AbpAspNetCoreSerilogModule),
    typeof(AbpAccountWebOpenIddictModule),
    typeof(AbpAspNetCoreComponentsServerMudBlazorBasicThemeModule),
    typeof(AbpAspNetCoreMvcUiLeptonXLiteThemeModule),
    typeof(AbpIdentityBlazorMudBlazorServerModule),
    typeof(AbpTenantManagementBlazorMudBlazorServerModule),
    typeof(AbpSettingManagementBlazorMudBlazorServerModule)
   )]
public class SyncBlazorModule : AbpModule
{
    public override void PreConfigureServices(ServiceConfigurationContext context)
    {
        var hostingEnvironment = context.Services.GetHostingEnvironment();
        var configuration = context.Services.GetConfiguration();

        context.Services.PreConfigure<AbpMvcDataAnnotationsLocalizationOptions>(options =>
        {
            options.AddAssemblyResource(
                typeof(SyncResource),
                typeof(SyncDomainModule).Assembly,
                typeof(SyncDomainSharedModule).Assembly,
                typeof(SyncApplicationModule).Assembly,
                typeof(SyncApplicationContractsModule).Assembly,
                typeof(SyncBlazorModule).Assembly
            );
        });

        PreConfigure<OpenIddictBuilder>(builder =>
        {
            builder.AddValidation(options =>
            {
                options.AddAudiences("Sync");
                options.UseLocalServer();
                options.UseAspNetCore();
            });
        });

        if (!hostingEnvironment.IsDevelopment())
        {
            PreConfigure<AbpOpenIddictAspNetCoreOptions>(options =>
            {
                options.AddDevelopmentEncryptionAndSigningCertificate = false;
            });

            PreConfigure<OpenIddictServerBuilder>(serverBuilder =>
            {
                serverBuilder.AddProductionEncryptionAndSigningCertificate("openiddict.pfx", "973b8cf1-cd61-4dd7-8dba-74c1a3f190cf");
            });
        }

        PreConfigure<AbpAspNetCoreComponentsWebOptions>(options =>
        {
            options.IsBlazorWebApp = true;
        });
    }

    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var hostingEnvironment = context.Services.GetHostingEnvironment();
        var configuration = context.Services.GetConfiguration();

        // Add services to the container.
        context.Services.AddRazorComponents()
            .AddInteractiveServerComponents();

        ConfigureAuthentication(context);
        ConfigureUrls(configuration);
        ConfigureBundles();
        ConfigureVirtualFileSystem(hostingEnvironment);
        ConfigureSwaggerServices(context.Services);
        ConfigureAutoApiControllers();
        ConfigureRouter(context);
        ConfigureMenu(context);

        context.Services.AddMapperlyObjectMapper<SyncBlazorModule>();
    }

    private void ConfigureAuthentication(ServiceConfigurationContext context)
    {
        context.Services.ForwardIdentityAuthenticationForBearer(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
        context.Services.Configure<AbpClaimsPrincipalFactoryOptions>(options =>
        {
            options.IsDynamicClaimsEnabled = true;
        });

        // Вход людей через Keycloak (схема добавляется при старте, если настроена).
        GGA1C.Sync.Blazor.Login.KeycloakLoginScheme.AddServices(context.Services);

        // Как часто кука сверяется со штампом безопасности пользователя. Отключили учётку -
        // сверка меняет штамп, и открытая сессия гаснет не позже чем через этот интервал
        // (по умолчанию в ASP.NET Core - 30 минут).
        context.Services.Configure<Microsoft.AspNetCore.Identity.SecurityStampValidatorOptions>(options =>
        {
            options.ValidationInterval = TimeSpan.FromMinutes(2);
        });
    }

    private void ConfigureUrls(IConfiguration configuration)
    {
        Configure<AppUrlOptions>(options =>
        {
            options.Applications["MVC"].RootUrl = configuration["App:SelfUrl"];
            options.RedirectAllowedUrls.AddRange(configuration["App:RedirectAllowedUrls"]?.Split(',') ?? Array.Empty<string>());
        });
    }

    private void ConfigureBundles()
    {
        Configure<AbpBundlingOptions>(options =>
        {
            // MVC UI
            options.StyleBundles.Configure(
                LeptonXLiteThemeBundles.Styles.Global,
                bundle =>
                {
                    bundle.AddFiles("/global-styles.css");
                }
            );

            //BLAZOR UI
            options.StyleBundles.Configure(
                BlazorMudBlazorBasicThemeBundles.Styles.Global,
                bundle =>
                {
                    bundle.AddFiles("/blazor-global-styles.css");
                    //You can remove the following line if you don't use Blazor CSS isolation for components
                    bundle.AddFiles(new BundleFile("/GGA1C.Sync.Blazor.styles.css", true));
                }
            );
        });
    }

    private void ConfigureVirtualFileSystem(IWebHostEnvironment hostingEnvironment)
    {
        if (hostingEnvironment.IsDevelopment())
        {
            Configure<AbpVirtualFileSystemOptions>(options =>
            {
                options.FileSets.ReplaceEmbeddedByPhysical<SyncDomainSharedModule>(Path.Combine(hostingEnvironment.ContentRootPath, $"..{Path.DirectorySeparatorChar}GGA1C.Sync.Domain.Shared"));
                options.FileSets.ReplaceEmbeddedByPhysical<SyncDomainModule>(Path.Combine(hostingEnvironment.ContentRootPath, $"..{Path.DirectorySeparatorChar}GGA1C.Sync.Domain"));
                options.FileSets.ReplaceEmbeddedByPhysical<SyncApplicationContractsModule>(Path.Combine(hostingEnvironment.ContentRootPath, $"..{Path.DirectorySeparatorChar}GGA1C.Sync.Application.Contracts"));
                options.FileSets.ReplaceEmbeddedByPhysical<SyncApplicationModule>(Path.Combine(hostingEnvironment.ContentRootPath, $"..{Path.DirectorySeparatorChar}GGA1C.Sync.Application"));
                options.FileSets.ReplaceEmbeddedByPhysical<SyncBlazorModule>(hostingEnvironment.ContentRootPath);
            });
        }
    }

    private void ConfigureSwaggerServices(IServiceCollection services)
    {
        services.AddAbpSwaggerGen(
            options =>
            {
                options.SwaggerDoc("v1", new OpenApiInfo { Title = "Sync API", Version = "v1" });
                options.DocInclusionPredicate((docName, description) => true);
                options.CustomSchemaIds(type => type.FullName);
            }
        );
    }


    private void ConfigureMenu(ServiceConfigurationContext context)
    {
        Configure<AbpNavigationOptions>(options =>
        {
            options.MenuContributors.Add(new SyncMenuContributor());
        });
    }

    private void ConfigureRouter(ServiceConfigurationContext context)
    {
        Configure<AbpRouterOptions>(options =>
        {
            options.AppAssembly = typeof(SyncBlazorModule).Assembly;
        });
    }

    private void ConfigureAutoApiControllers()
    {
        Configure<AbpAspNetCoreMvcOptions>(options =>
        {
            options.ConventionalControllers.Create(typeof(SyncApplicationModule).Assembly);
        });
    }

    /// <summary>
    /// Без ключа шифрования сервис запускается, но тихо ломает всё, что связано с секретами:
    /// новые значения шифруются другим ключом, старые не расшифровываются. Причём ABP на
    /// настройке не падает, а отдаёт шифротекст как значение — и хаб отправляет его, скажем,
    /// в Keycloak вместо секрета, получая 401 без единой подсказки, что дело в ключе.
    /// Проверено вживую. Поэтому — громкая запись в журнал при старте.
    /// </summary>
    private static void WarnIfEncryptionKeyMissing(
        ApplicationInitializationContext context,
        IConfiguration configuration)
    {
        if (!string.IsNullOrWhiteSpace(configuration["StringEncryption:DefaultPassPhrase"]))
        {
            return;
        }

        context.ServiceProvider
            .GetRequiredService<ILogger<SyncBlazorModule>>()
            .LogError(
                "Не задан StringEncryption:DefaultPassPhrase. Сохранённые пароли и ключи " +
                "не расшифруются, а новые будут зашифрованы ключом, который потеряется " +
                "при следующем запуске. Значение лежит в переменной окружения " +
                @"StringEncryption__DefaultPassPhrase (копия - C:\GGA1C\stringencryption.key).");
    }

    public override void OnApplicationInitialization(ApplicationInitializationContext context)
    {
        var env = context.GetEnvironment();
        var app = context.GetApplicationBuilder();
        var appConfiguration = context.ServiceProvider.GetRequiredService<IConfiguration>();

        WarnIfEncryptionKeyMissing(context, appConfiguration);

        app.UseAbpRequestLocalization();

        if (env.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }
        else
        {
            app.UseExceptionHandler("/Error");
            app.UseHsts();
        }

        app.UseHttpsRedirection();
        app.UseCorrelationId();
        // Копия Grist-виджета отдаётся ОТСЮДА, а не из wwwroot, по двум причинам.
        //
        // Первая: виджет должен жить на том же origin, что и наш API. Иначе браузер
        // требует CORS-предварительный запрос, и синхронизация падает в "Failed to fetch"
        // ещё до ответа сервера - проверено, preflight отдавал 405 без заголовков CORS.
        //
        // Вторая: MapAbpStaticAssets (и MapStaticAssets в .NET 9+) отдаёт только те файлы,
        // что известны на момент сборки, из манифеста. Виджет обновляется задачей
        // планировщика на ходу, поэтому положенный в wwwroot файл просто не отдавался бы
        // без пересборки. Отдельный провайдер читает папку с диска на каждый запрос.
        //
        // Папка намеренно вне репозитория: виджет живёт в своём репозитории и в сборке
        // основного Grist, здесь только отладочная копия.
        // Адрес намеренно повторяет имя репозитория виджета
        // (/widget_report_grist/index.html): он прописан в настройках секции Grist, и по
        // нему должно быть видно, что это именно тот виджет, а не какая-то наша страница.
        var widgetPath = appConfiguration["Widget:LocalPath"];
        if (!string.IsNullOrWhiteSpace(widgetPath) && Directory.Exists(widgetPath))
        {
            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(widgetPath),
                RequestPath = "/widget_report_grist"
            });
        }

        app.MapAbpStaticAssets();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAbpOpenIddictValidation();

        if (MultiTenancyConsts.IsEnabled)
        {
            app.UseMultiTenancy();
        }
        app.UseUnitOfWork();

        // После UseUnitOfWork намеренно: прослойка пишет в базу, и ей нужна
        // единица работы, которую заводит предыдущая строка.
        app.UseMiddleware<GGA1C.Sync.Middleware.RequestLoggerMiddleware>();
        app.UseDynamicClaims();
        app.UseAuthorization();
        app.UseAntiforgery();

        app.UseSwagger();
        app.UseAbpSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/swagger/v1/swagger.json", "Sync API");
        });

        app.UseConfiguredEndpoints(builder =>
        {
            builder.MapRazorComponents<App>()
                .AddInteractiveServerRenderMode()
                .AddAdditionalAssemblies(builder.ServiceProvider.GetRequiredService<IOptions<AbpRouterOptions>>().Value.AdditionalAssemblies.ToArray());
        });
    }
    public override async Task OnPostApplicationInitializationAsync(
        ApplicationInitializationContext context)
    {
        await GGA1C.Sync.Blazor.Login.KeycloakLoginScheme.RegisterAsync(context.ServiceProvider);

        // Второй экземпляр хаба для отладки (свой worktree, общая база) не должен сверять,
        // синхронизировать и прибирать сессии GoLabel параллельно с живым:
        // Workers__Enabled=false.
        var configuration = context.ServiceProvider.GetRequiredService<IConfiguration>();
        if (!configuration.GetValue("Workers:Enabled", true))
        {
            context.ServiceProvider.GetRequiredService<ILogger<SyncBlazorModule>>()
                .LogWarning("Фоновые работники выключены (Workers:Enabled=false)");
            return;
        }

        // Работник сам проверяет на каждом тике, включена ли автосинхронизация и заданы ли
        // адреса, поэтому регистрируем его безусловно - настройка правится в интерфейсе.
        await context.AddBackgroundWorkerAsync<GGA1C.Sync.Sync.AutoSyncWorker>();
        await context.AddBackgroundWorkerAsync<GGA1C.Sync.Keycloak.KeycloakSyncWorker>();

        // GoLabel II на каждое обновление открывает новое подключение и прежнее не
        // закрывает - прибираем за ним, иначе на несколько рабочих мест упрёмся в
        // max_connections.
        await context.AddBackgroundWorkerAsync<GGA1C.Sync.Labels.PrintConnectionWorker>();

        // После перезапуска MySQL GoLabel не входит, пока пароль его логина не в кэше
        // сервера (см. PrintLoginCacheWarmer). Заполняем в фоне: недоступный MySQL не должен
        // задерживать запуск хаба.
        var services = context.ServiceProvider;
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = services.CreateScope();
                await scope.ServiceProvider
                    .GetRequiredService<GGA1C.Sync.Labels.PrintLoginCacheWarmer>()
                    .WarmAllAsync();
            }
            catch (Exception ex)
            {
                services.GetRequiredService<ILogger<SyncBlazorModule>>()
                    .LogWarning(ex, "Не удалось положить в кэш MySQL пароли логинов печати");
            }
        });
    }
}
