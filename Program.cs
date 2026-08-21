using AltiumSync.Data;
using AltiumSync.Services;

var builder = WebApplication.CreateBuilder(args);

// Поддержка запуска как Windows Service
builder.Host.UseWindowsService();

builder.Services.AddControllers();

// HTTP-клиенты — принимаем самоподписанные сертификаты (для on-prem серверов)
builder.Services.AddHttpClient("altium").ConfigurePrimaryHttpMessageHandler(() =>
    new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true });
builder.Services.AddHttpClient("onec").ConfigurePrimaryHttpMessageHandler(() =>
    new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true });

builder.Services.AddSingleton<Database>();
builder.Services.AddSingleton<LabelsDatabase>();
builder.Services.AddSingleton<AltiumService>();
builder.Services.AddSingleton<OneCService>();
builder.Services.AddSingleton<SyncService>();
builder.Services.AddHostedService<AutoSyncBackgroundService>();

var app = builder.Build();

// Инициализируем подключения из сохранённых настроек при старте
var db      = app.Services.GetRequiredService<Database>();
var sync    = app.Services.GetRequiredService<SyncService>();
var saved   = db.GetSettings();
if (!string.IsNullOrEmpty(saved.AltiumServerUrl))
    sync.ApplySettings(saved);

app.UseDefaultFiles();
app.UseStaticFiles();

// UseRouting обязательно ПОСЛЕ статики: иначе catch-all из MapFallbackToFile выбирает
// endpoint раньше, StaticFileMiddleware видит уже выбранный endpoint и пропускает запрос.
// Из-за этого папки без имени файла (/widget/) отдавали index.html приложения вместо
// своего содержимого, а /widget/index.html работал (fallback не матчит пути с расширением).
app.UseRouting();

app.UseMiddleware<AltiumSync.Services.RequestLoggerMiddleware>();
app.MapControllers();
app.MapFallbackToFile("index.html");

app.Run();
