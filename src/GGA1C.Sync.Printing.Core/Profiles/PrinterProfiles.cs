using System.Collections.ObjectModel;
using System.Diagnostics;

namespace GGA1C.Sync.Printing.Profiles;

public sealed record PrinterProfile(PrinterDefinition Definition, long Revision,
    IReadOnlyDictionary<string, double> Values, IReadOnlyList<double> LegacyChoices)
{
    public double Value(string fieldId) => Values[fieldId];
}

internal sealed record PrinterProfileData(long Revision, Dictionary<string, double> Values,
    double[] LegacyValues, bool LegacyConflictPending, bool LegacyChoiceMade)
{
    public static PrinterProfileData Empty() => new(0, new(StringComparer.Ordinal), [], false, false);
}

/// <summary>Server-side profile authority. All writes for the running hub pass through its singleton.</summary>
public sealed class PrinterProfiles(FilePrinterProfileStore store)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private event Action<Guid?, PrinterProfile>? Changed;

    public IDisposable Observe(Guid? tenantId, Action<PrinterProfile> observer)
    {
        void Handler(Guid? owner, PrinterProfile change)
        {
            if (tenantId == owner) observer(change);
        }
        Changed += Handler;
        return new Subscription(() => Changed -= Handler);
    }

    public Task<PrinterProfile> ReadAsync(Guid? tenantId, string printerId) => UpdateAsync(tenantId, printerId, null);

    public Task<PrinterProfile> SaveAsync(Guid? tenantId, string printerId, long revision,
        IReadOnlyDictionary<string, double> changes) => UpdateAsync(tenantId, printerId, (definition, data) =>
    {
        if (data.Revision != revision)
            throw new InvalidOperationException("Профиль уже изменился. Проверьте текущие значения и повторите правку.");
        if (data.LegacyConflictPending)
            throw new InvalidOperationException("Сначала выберите значение для переноса прежней непечатаемой длины.");

        var values = new Dictionary<string, double>(data.Values, StringComparer.Ordinal);
        foreach (var (id, value) in changes)
        {
            var field = definition.Field(id);
            if (field.ReadOnly || !field.Accepts(value))
                throw new ArgumentException($"Недопустимое значение: {field.Caption}.");
            values[id] = value;
        }
        return values.Count == data.Values.Count && values.All(pair => data.Values[pair.Key] == pair.Value)
            ? data : data with { Values = values };
    });

    public Task<PrinterProfile> OfferLegacyLengthsAsync(Guid? tenantId, IReadOnlyList<double> lengths) =>
        UpdateAsync(tenantId, PrinterCatalog.Zx430i, (definition, data) =>
        {
            var field = definition.Field(PrinterCatalog.NonPrintableLength);
            var candidates = data.LegacyValues.Concat(lengths.Where(field.Accepts)).Distinct().Order().ToArray();
            if (candidates.SequenceEqual(data.LegacyValues)) return data;
            var values = data.Values;
            var conflict = data.LegacyConflictPending;
            if (!data.LegacyChoiceMade)
            {
                // Do not overwrite even an explicitly stored zero. Later accounts/browsers
                // can surface a conflict; repeated offers cannot undo a choice or an edit.
                var hasValue = values.TryGetValue(field.Id, out var current);
                conflict |= candidates.Length > 1 || (hasValue && candidates[0] != current);
                if (!hasValue && !conflict)
                    values = new(values, StringComparer.Ordinal) { [field.Id] = candidates[0] };
            }
            return data with { LegacyValues = candidates, Values = values, LegacyConflictPending = conflict };
        });

    public Task<PrinterProfile> ResolveLegacyLengthsAsync(Guid? tenantId, double selected) =>
        UpdateAsync(tenantId, PrinterCatalog.Zx430i, (_, data) =>
        {
            if (!data.LegacyConflictPending) return data; // Another view already saved the choice.
            if (!LegacyChoices(data).Contains(selected))
                throw new ArgumentException("Выберите одно из сохранённых значений.");
            var values = new Dictionary<string, double>(data.Values, StringComparer.Ordinal)
            {
                [PrinterCatalog.NonPrintableLength] = selected,
            };
            return data with { Values = values, LegacyConflictPending = false, LegacyChoiceMade = true };
        });

    private static double[] LegacyChoices(PrinterProfileData data) => !data.LegacyConflictPending ? []
        : data.Values.TryGetValue(PrinterCatalog.NonPrintableLength, out var current)
            ? data.LegacyValues.Append(current).Distinct().Order().ToArray()
            : data.LegacyValues;

    private async Task<PrinterProfile> UpdateAsync(Guid? tenantId, string printerId,
        Func<PrinterDefinition, PrinterProfileData, PrinterProfileData>? update)
    {
        var definition = PrinterCatalog.Get(printerId);
        PrinterProfile snapshot;
        bool changed;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var data = await store.ReadAsync(tenantId, definition.Id).ConfigureAwait(false);
            var next = update?.Invoke(definition, data) ?? data;
            changed = !ReferenceEquals(next, data);
            if (changed)
            {
                next = next with { Revision = checked(data.Revision + 1) };
                await store.WriteAsync(tenantId, definition.Id, next).ConfigureAwait(false);
            }
            var values = definition.Fields.ToDictionary(field => field.Id,
                field => field.ReadOnly ? field.DefaultValue : next.Values.GetValueOrDefault(field.Id, field.DefaultValue),
                StringComparer.Ordinal);
            snapshot = new(definition, next.Revision, new ReadOnlyDictionary<string, double>(values),
                Array.AsReadOnly(LegacyChoices(next)));
        }
        finally { _gate.Release(); }

        // A closed renderer must not make an already successful disk write look like a failure.
        if (changed && Changed is { } observers)
            foreach (Action<Guid?, PrinterProfile> observer in observers.GetInvocationList())
                try { observer(tenantId, snapshot); }
                catch (Exception ex) { Trace.TraceError("Printer profile subscriber: {0}", ex); }
        return snapshot;
    }

    private sealed class Subscription(Action remove) : IDisposable
    {
        private Action? _remove = remove;
        public void Dispose() => Interlocked.Exchange(ref _remove, null)?.Invoke();
    }
}
