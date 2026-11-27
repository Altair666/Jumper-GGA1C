using System;
using System.Threading.Tasks;
using GGA1C.Sync.Printing;
using GGA1C.Sync.Printing.Profiles;
using Microsoft.AspNetCore.Components;

namespace GGA1C.Sync.Blazor.Components.Pages;

public abstract class PrinterProfileComponent : SyncComponentBase
{
    [Inject] protected IPrinterProfilesAppService Profiles { get; set; } = default!;

    [Inject] private PrinterProfileEvents Events { get; set; } = default!;

    protected PrinterProfile? Profile { get; private set; }
    private string? _printerId;
    private IDisposable? _observer;
    private bool _disposed;

    protected async Task TrackProfileAsync(string printerId)
    {
        if (_printerId != printerId)
        {
            _printerId = printerId;
            Profile = null;
        }
        if (_observer == null)
        {
            var observer = await Events.ObserveAsync(OnProfileEvent);
            if (_disposed)
            {
                observer.Dispose();
                return;
            }
            _observer = observer;
        }
        ReceiveProfile(await Profiles.ReadAsync(printerId));
    }

    protected bool ReceiveProfile(PrinterProfile profile)
    {
        if (_disposed || profile.Definition.Id != _printerId || Profile?.Revision >= profile.Revision)
            return false;
        var previous = Profile;
        Profile = profile;
        ProfileReceived(previous, profile);
        return true;
    }

    protected virtual void ProfileReceived(PrinterProfile? previous, PrinterProfile profile) { }

    private void OnProfileEvent(PrinterProfile profile) => _ = InvokeAsync(() =>
    {
        if (ReceiveProfile(profile)) StateHasChanged();
    });

    protected void StopTrackingProfiles()
    {
        _disposed = true;
        _observer?.Dispose();
    }
}
