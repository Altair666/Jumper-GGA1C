namespace GGA1C.Sync.MultiTenancy;

public static class MultiTenancyConsts
{
    /* Enable/disable multi-tenancy easily in a single point.
     * If you will never need to multi-tenancy, you can remove
     * related modules and code parts, including this file.
     */
    // Выключено: организация одна, арендаторов в базе нет. Заодно со страницы входа
    // (LeptonX Lite) уходит блок «Tenant / switch», а из меню — управление арендаторами.
    // Модули TenantManagement оставлены: их таблицы уже в схеме, убирать — миграция.
    public const bool IsEnabled = false;
}
