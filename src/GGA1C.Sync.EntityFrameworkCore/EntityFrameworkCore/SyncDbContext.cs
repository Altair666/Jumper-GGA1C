using GGA1C.Sync.Altium;
using GGA1C.Sync.Diagnostics;
using GGA1C.Sync.Labels;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.AuditLogging.EntityFrameworkCore;
using Volo.Abp.BackgroundJobs.EntityFrameworkCore;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Modeling;
using Volo.Abp.FeatureManagement.EntityFrameworkCore;
using Volo.Abp.Identity;
using Volo.Abp.Identity.EntityFrameworkCore;
using Volo.Abp.OpenIddict.EntityFrameworkCore;
using Volo.Abp.PermissionManagement.EntityFrameworkCore;
using Volo.Abp.SettingManagement.EntityFrameworkCore;
using Volo.Abp.TenantManagement;
using Volo.Abp.TenantManagement.EntityFrameworkCore;

namespace GGA1C.Sync.EntityFrameworkCore;

[ReplaceDbContext(typeof(IIdentityDbContext))]
[ReplaceDbContext(typeof(ITenantManagementDbContext))]
[ConnectionStringName("Default")]
public class SyncDbContext :
    AbpDbContext<SyncDbContext>,
    IIdentityDbContext,
    ITenantManagementDbContext
{
    /* Add DbSet properties for your Aggregate Roots / Entities here. */

    public DbSet<ComponentMap> ComponentMaps { get; set; }
    public DbSet<FolderMap> FolderMaps { get; set; }
    public DbSet<ProjectMap> ProjectMaps { get; set; }
    public DbSet<SyncLogEntry> SyncLog { get; set; }
    public DbSet<RequestLogItem> RequestLog { get; set; }
    public DbSet<LabelClient> LabelClients { get; set; }

    public DbSet<LabelRow> Labels { get; set; }

    #region Entities from the modules

    /* Notice: We only implemented IIdentityDbContext and ITenantManagementDbContext
     * and replaced them for this DbContext. This allows you to perform JOIN
     * queries for the entities of these modules over the repositories easily. You
     * typically don't need that for other modules. But, if you need, you can
     * implement the DbContext interface of the needed module and use ReplaceDbContext
     * attribute just like IIdentityDbContext and ITenantManagementDbContext.
     *
     * More info: Replacing a DbContext of a module ensures that the related module
     * uses this DbContext on runtime. Otherwise, it will use its own DbContext class.
     */

    //Identity
    public DbSet<IdentityUser> Users { get; set; }
    public DbSet<IdentityRole> Roles { get; set; }
    public DbSet<IdentityClaimType> ClaimTypes { get; set; }
    public DbSet<OrganizationUnit> OrganizationUnits { get; set; }
    public DbSet<IdentitySecurityLog> SecurityLogs { get; set; }
    public DbSet<IdentityLinkUser> LinkUsers { get; set; }
    public DbSet<IdentityUserDelegation> UserDelegations { get; set; }
    public DbSet<IdentitySession> Sessions { get; set; }
    // Tenant Management
    public DbSet<Tenant> Tenants { get; set; }
    public DbSet<TenantConnectionString> TenantConnectionStrings { get; set; }

    #endregion

    public SyncDbContext(DbContextOptions<SyncDbContext> options)
        : base(options)
    {

    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        /* Include modules to your migration db context */

        builder.ConfigurePermissionManagement();
        builder.ConfigureSettingManagement();
        builder.ConfigureBackgroundJobs();
        builder.ConfigureAuditLogging();
        builder.ConfigureIdentity();
        builder.ConfigureOpenIddict();
        builder.ConfigureFeatureManagement();
        builder.ConfigureTenantManagement();

        /* Configure your own tables/entities inside here */

        builder.Entity<ComponentMap>(b =>
        {
            b.ToTable(SyncConsts.DbTablePrefix + "ComponentMaps", SyncConsts.DbSchema);
            b.ConfigureByConvention();

            // Ключ сопоставления - AltiumId (вид R_...): именно по нему прежняя версия
            // сервиса делала upsert, и именно он приходит во всех ответах API Altium.
            // ItemGUID есть не всегда, поэтому он просто индексирован, а не уникален.
            b.HasIndex(x => x.AltiumId).IsUnique();
            b.HasIndex(x => x.AltiumItemGuid);
            b.Property(x => x.AltiumId).HasMaxLength(128).IsRequired();
            b.Property(x => x.AltiumItemGuid).HasMaxLength(64);
            b.Property(x => x.AltiumName).HasMaxLength(256);
            b.Property(x => x.AltiumPath).HasMaxLength(512);
            b.Property(x => x.Article).HasMaxLength(256);
            b.Property(x => x.Manufacturer).HasMaxLength(256);
            b.Property(x => x.Name1C).HasMaxLength(512);
            b.Property(x => x.OnecGuid).HasMaxLength(64);
            b.Property(x => x.SyncStatus).HasMaxLength(32).IsRequired();
        });

        builder.Entity<FolderMap>(b =>
        {
            b.ToTable(SyncConsts.DbTablePrefix + "FolderMaps", SyncConsts.DbSchema);
            b.ConfigureByConvention();

            b.HasIndex(x => x.AltiumFolderId).IsUnique();
            b.Property(x => x.AltiumFolderId).HasMaxLength(64).IsRequired();
            b.Property(x => x.AltiumFolderPath).HasMaxLength(512);
            b.Property(x => x.OnecGroupGuid).HasMaxLength(64);
            b.Property(x => x.OnecGroupPath).HasMaxLength(512);
        });

        builder.Entity<ProjectMap>(b =>
        {
            b.ToTable(SyncConsts.DbTablePrefix + "ProjectMaps", SyncConsts.DbSchema);
            b.ConfigureByConvention();

            // Уникальна ПАРА проект+ревизия: у одного проекта живут несколько ревизий
            // одновременно, и каждая синхронизируется отдельно.
            b.HasIndex(x => new { x.AltiumProjectId, x.AltiumRevision }).IsUnique();
            b.Property(x => x.AltiumProjectId).HasMaxLength(128).IsRequired();
            b.Property(x => x.AltiumProjectName).HasMaxLength(256);
            b.Property(x => x.AltiumRevision).HasMaxLength(64);
            b.Property(x => x.SyncAction).HasMaxLength(32).IsRequired();
            b.Property(x => x.SyncStatus).HasMaxLength(32).IsRequired();
        });

        builder.Entity<LabelClient>(b =>
        {
            b.ToTable(SyncConsts.DbTablePrefix + "LabelClients", SyncConsts.DbSchema);
            b.ConfigureByConvention();

            // Поиск учётки идёт по хешу ключа на каждом запросе от виджета - индекс
            // обязателен, и он же гарантирует, что два ключа не совпадут.
            b.HasIndex(x => x.KeyHash).IsUnique();

            // Slug - имя папки буфера на диске, двум учёткам один буфер отдавать нельзя.
            b.HasIndex(x => x.Slug).IsUnique();

            b.Property(x => x.Name).HasMaxLength(128).IsRequired();
            b.Property(x => x.Slug).HasMaxLength(64).IsRequired();
            b.Property(x => x.KeyHash).HasMaxLength(64).IsRequired();
            b.Property(x => x.KeyPrefix).HasMaxLength(16).IsRequired();
            b.Property(x => x.KeyEncrypted).HasMaxLength(512);

            // Связь с Keycloak держится по sub: email и логин человек меняет, sub - нет.
            // Индекс уникальный, но допускает несколько NULL - учётки, заведённые руками,
            // sub не имеют.
            b.HasIndex(x => x.KeycloakSub).IsUnique();
            b.Property(x => x.KeycloakSub).HasMaxLength(64);
            b.Property(x => x.KeycloakUsername).HasMaxLength(128);
            b.Property(x => x.Email).HasMaxLength(256);

            // Учётные данные во внешних системах. Пароли лежат зашифрованными, поэтому
            // поле заметно длиннее самого пароля.
            b.Property(x => x.AltiumLogin).HasMaxLength(128);
            b.Property(x => x.AltiumPasswordEncrypted).HasMaxLength(512);
            b.Property(x => x.PdmLogin).HasMaxLength(128);
            b.Property(x => x.PdmPasswordEncrypted).HasMaxLength(512);

            // Логин печати - имя пользователя MySQL, им подключается GoLabel II.
            // Уникален: двум людям один логин выдавать нельзя.
            b.HasIndex(x => x.PrintLogin).IsUnique();
            b.Property(x => x.PrintLogin).HasMaxLength(32);
        });

        builder.Entity<LabelRow>(b =>
        {
            b.ToTable(SyncConsts.DbTablePrefix + "Labels", SyncConsts.DbSchema);
            b.ConfigureByConvention();

            // Ключ строки уникален внутри учётки: виджет шлёт партию целиком, и upsert
            // опирается именно на эту пару.
            b.HasIndex(x => new { x.Slug, x.ExternalId }).IsUnique();

            // Отбор по учётке идёт на каждом запросе от виджета и в представлении
            // my_labels, которое читает GoLabel II.
            b.HasIndex(x => x.Slug);

            b.Property(x => x.Slug).HasMaxLength(64).IsRequired();
            b.Property(x => x.ExternalId).HasMaxLength(256).IsRequired();
            b.Property(x => x.Date).HasMaxLength(64);
            b.Property(x => x.ProductCode).HasMaxLength(128);
            b.Property(x => x.ModeCode).HasMaxLength(128);
            b.Property(x => x.Article).HasMaxLength(128);
            b.Property(x => x.Name).HasMaxLength(256);
            b.Property(x => x.Manufacturer).HasMaxLength(256);
            b.Property(x => x.Barcode).HasMaxLength(128);
            b.Property(x => x.Qty).HasMaxLength(64);
            b.Property(x => x.Extra1).HasMaxLength(256);
            b.Property(x => x.Extra2).HasMaxLength(256);
            b.Property(x => x.Extra3).HasMaxLength(256);
        });

        builder.Entity<RequestLogItem>(b =>
        {
            b.ToTable(SyncConsts.DbTablePrefix + "RequestLog", SyncConsts.DbSchema);
            b.ConfigureByConvention();

            b.HasIndex(x => x.Id);
            b.Property(x => x.Source).HasMaxLength(32).IsRequired();
            b.Property(x => x.Method).HasMaxLength(16).IsRequired();
            b.Property(x => x.Path).HasMaxLength(2048).IsRequired();
        });

        builder.Entity<SyncLogEntry>(b =>
        {
            b.ToTable(SyncConsts.DbTablePrefix + "SyncLog", SyncConsts.DbSchema);
            b.ConfigureByConvention();

            // Журнал читается почти всегда «последние записи сверху»
            b.HasIndex(x => x.Timestamp);
            b.Property(x => x.EntityType).HasMaxLength(32).IsRequired();
            b.Property(x => x.EntityId).HasMaxLength(128);
            b.Property(x => x.EntityName).HasMaxLength(256);
            b.Property(x => x.Action).HasMaxLength(32).IsRequired();
            b.Property(x => x.Status).HasMaxLength(16).IsRequired();
        });
    }
}
