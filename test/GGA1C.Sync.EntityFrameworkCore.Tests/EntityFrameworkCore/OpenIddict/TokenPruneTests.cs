using System;
using System.Linq;
using System.Threading.Tasks;
using GGA1C.Sync.EntityFrameworkCore.OpenIddict;
using OpenIddict.Abstractions;
using Shouldly;
using Volo.Abp.OpenIddict.Tokens;
using Xunit;

namespace GGA1C.Sync.EntityFrameworkCore.OpenIddict;

/// <summary>
/// БАГ-2: чистка токенов без подзапроса к удаляемой таблице (MySQL 1093). На SQLite тестов
/// ошибка MySQL не воспроизводится — здесь проверяется, что отбор тот же: удаляются старые
/// отозванные и просроченные, живые остаются.
/// </summary>
[Collection(SyncTestConsts.CollectionDefinitionName)]
public class TokenPruneTests : SyncEntityFrameworkCoreTestBase
{
    private readonly IOpenIddictTokenRepository _tokens;

    public TokenPruneTests()
    {
        _tokens = GetRequiredService<IOpenIddictTokenRepository>();
    }

    [Fact]
    public void Replaces_Abp_Repository()
    {
        Volo.Abp.DynamicProxy.ProxyHelper.UnProxy(_tokens).ShouldBeOfType<MySqlOpenIddictTokenRepository>();
    }

    [Fact]
    public async Task Prunes_Old_Revoked_And_Expired_Keeps_Valid()
    {
        var old = DateTime.UtcNow.AddDays(-30);
        var revoked = Guid.NewGuid();
        var expired = Guid.NewGuid();
        var valid = Guid.NewGuid();
        var fresh = Guid.NewGuid();

        await WithUnitOfWorkAsync(async () =>
        {
            await _tokens.InsertAsync(Token(revoked, old, OpenIddictConstants.Statuses.Revoked, DateTime.UtcNow.AddDays(1)));
            await _tokens.InsertAsync(Token(expired, old, OpenIddictConstants.Statuses.Valid, DateTime.UtcNow.AddDays(-1)));
            await _tokens.InsertAsync(Token(valid, old, OpenIddictConstants.Statuses.Valid, DateTime.UtcNow.AddDays(1)));
            await _tokens.InsertAsync(Token(fresh, DateTime.UtcNow, OpenIddictConstants.Statuses.Revoked, DateTime.UtcNow.AddDays(1)));
        });

        long pruned = 0;
        await WithUnitOfWorkAsync(async () =>
        {
            pruned = await _tokens.PruneAsync(DateTime.UtcNow.AddDays(-14));
        });

        pruned.ShouldBe(2);
        await WithUnitOfWorkAsync(async () =>
        {
            var left = (await _tokens.GetListAsync()).Select(t => t.Id).ToList();
            left.ShouldNotContain(revoked);
            left.ShouldNotContain(expired);
            left.ShouldContain(valid);
            left.ShouldContain(fresh);
        });
    }

    private static OpenIddictToken Token(Guid id, DateTime created, string status, DateTime expires) =>
        new(id)
        {
            CreationDate = created,
            Status = status,
            ExpirationDate = expires,
            Type = OpenIddictConstants.TokenTypeHints.RefreshToken,
        };
}
