using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GGA1C.Sync.Permissions;
using GGA1C.Sync.Printing.Profiles;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Authorization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.DependencyInjection;
using Volo.Abp.MultiTenancy;

namespace GGA1C.Sync.Printing;

public interface IPrinterProfilesAppService : IApplicationService
{
    Task<PrinterProfile> ReadAsync(string printerId);
    Task<PrinterProfile> SaveAsync(string printerId, long revision, IReadOnlyDictionary<string, double> values);
    Task<PrinterProfile> OfferLegacyLengthsAsync(IReadOnlyList<double> lengths);
    Task<PrinterProfile> ResolveLegacyLengthsAsync(double selected);
}

[RemoteService(IsEnabled = false)]
[Authorize(SyncPermissions.Admin)]
public class PrinterProfilesAppService(PrinterProfiles profiles) : SyncAppService, IPrinterProfilesAppService
{
    public virtual Task<PrinterProfile> ReadAsync(string printerId) => profiles.ReadAsync(CurrentTenant.Id, printerId);

    public virtual Task<PrinterProfile> SaveAsync(string printerId, long revision, IReadOnlyDictionary<string, double> values) =>
        profiles.SaveAsync(CurrentTenant.Id, printerId, revision, values);

    public virtual Task<PrinterProfile> OfferLegacyLengthsAsync(IReadOnlyList<double> lengths) =>
        profiles.OfferLegacyLengthsAsync(CurrentTenant.Id, lengths);

    public virtual Task<PrinterProfile> ResolveLegacyLengthsAsync(double selected) =>
        profiles.ResolveLegacyLengthsAsync(CurrentTenant.Id, selected);
}

// Subscriptions are in-process renderer callbacks, not application commands or DTOs.
public sealed class PrinterProfileEvents(PrinterProfiles profiles, ICurrentTenant tenant, IPermissionChecker permissions)
    : ITransientDependency
{
    public async Task<IDisposable> ObserveAsync(Action<PrinterProfile> observer)
    {
        if (!await permissions.IsGrantedAsync(SyncPermissions.Admin))
            throw new AbpAuthorizationException();
        return profiles.Observe(tenant.Id, observer);
    }
}
