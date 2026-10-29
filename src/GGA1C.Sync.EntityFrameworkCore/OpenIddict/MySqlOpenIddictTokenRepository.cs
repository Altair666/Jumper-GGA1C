using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.OpenIddict.Authorizations;
using Volo.Abp.OpenIddict.EntityFrameworkCore;
using Volo.Abp.OpenIddict.Tokens;

namespace GGA1C.Sync.EntityFrameworkCore.OpenIddict;

/// <summary>
/// БАГ-2: чистка токенов OpenIddict (TokenCleanupBackgroundWorker ABP, раз в час) на MySQL
/// падала с «You can't specify target table 'o' for update in FROM clause» (ошибка 1093):
/// штатный PruneAsync строит <c>DELETE FROM OpenIddictTokens WHERE Id IN (SELECT … FROM
/// OpenIddictTokens …)</c>, а MySQL не разрешает подзапрос к той же таблице в DELETE. Токены
/// копились, а чистка авторизаций после неё не запускалась.
///
/// Здесь тот же отбор, но в два шага: сначала номера (обычный SELECT), потом удаление
/// пачками по списку номеров — без подзапроса к удаляемой таблице.
/// </summary>
[Dependency(ReplaceServices = true)]
[ExposeServices(typeof(IOpenIddictTokenRepository), typeof(EfCoreOpenIddictTokenRepository))]
public class MySqlOpenIddictTokenRepository : EfCoreOpenIddictTokenRepository
{
    /// <summary>Сколько номеров в одном DELETE … WHERE Id IN (…).</summary>
    public const int BatchSize = 500;

    public MySqlOpenIddictTokenRepository(IDbContextProvider<IOpenIddictDbContext> dbContextProvider)
        : base(dbContextProvider)
    {
    }

    public override async Task<long> PruneAsync(DateTime date, CancellationToken cancellationToken = default)
    {
        var dbContext = await GetDbContextAsync();
        var tokens = dbContext.Set<OpenIddictToken>();
        var authorizations = dbContext.Set<OpenIddictAuthorization>();
        var now = DateTime.UtcNow;

        // Отбор как у ABP: старше date и (неактивен/невалиден, или его авторизация невалидна,
        // или истёк срок).
        var ids = await (
                from token in tokens.AsNoTracking()
                join authorization in authorizations.AsNoTracking()
                    on token.AuthorizationId equals authorization.Id into joined
                from authorization in joined.DefaultIfEmpty()
                where token.CreationDate < date
                where (token.Status != OpenIddictConstants.Statuses.Inactive &&
                       token.Status != OpenIddictConstants.Statuses.Valid) ||
                      (authorization != null && authorization.Status != OpenIddictConstants.Statuses.Valid) ||
                      token.ExpirationDate < now
                select token.Id)
            .ToListAsync(GetCancellationToken(cancellationToken));

        long deleted = 0;
        foreach (var batch in ids.Chunk(BatchSize))
        {
            deleted += await tokens
                .Where(token => batch.Contains(token.Id))
                .ExecuteDeleteAsync(GetCancellationToken(cancellationToken));
        }

        return deleted;
    }
}
