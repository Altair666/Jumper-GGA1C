using System;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace GGA1C.Sync.EntityFrameworkCore;

/* This class is needed for EF Core console commands
 * (like Add-Migration and Update-Database commands) */
public class SyncDbContextFactory : IDesignTimeDbContextFactory<SyncDbContext>
{
    public SyncDbContext CreateDbContext(string[] args)
    {
        SyncEfCoreEntityExtensionMappings.Configure();

        var configuration = BuildConfiguration();

        // Провайдер - MySql.EntityFrameworkCore (официальный, от Oracle): именно его
        // тянет Volo.Abp.EntityFrameworkCore.MySQL 10.7. Отсюда UseMySQL, а не UseMySql
        // из Pomelo - того для EF Core 10 пока нет.
        var builder = new DbContextOptionsBuilder<SyncDbContext>()
            .UseMySQL(configuration.GetConnectionString("Default"));

        return new SyncDbContext(builder.Options);
    }

    private static IConfigurationRoot BuildConfiguration()
    {
        // Переменные окружения обязательны: пароль к базе в appsettings.json не лежит,
        // он приходит в ConnectionStrings__Default - иначе уехал бы в репозиторий.
        var builder = new ConfigurationBuilder()
            .SetBasePath(Path.Combine(Directory.GetCurrentDirectory(), "../GGA1C.Sync.DbMigrator/"))
            .AddJsonFile("appsettings.json", optional: false)
            .AddEnvironmentVariables();

        return builder.Build();
    }
}
