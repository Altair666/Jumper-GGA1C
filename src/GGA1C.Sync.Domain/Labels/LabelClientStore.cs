using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;
using GGA1C.Sync.Secrets;
using Volo.Abp.Security.Encryption;

namespace GGA1C.Sync.Labels;

public class LabelClientStore : DomainService, ILabelClientStore
{
    private readonly IRepository<LabelClient, int> _repo;
    private readonly IStringEncryptionService _encryption;

    public LabelClientStore(
        IRepository<LabelClient, int> repo,
        IStringEncryptionService encryption)
    {
        _repo = repo;
        _encryption = encryption;
    }

    public async Task<LabelClient?> FindByKeyAsync(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        var hash = HashKey(key);
        return await _repo.FindAsync(x => x.KeyHash == hash);
    }

    public async Task<List<LabelClient>> GetAllAsync()
    {
        var query = await _repo.GetQueryableAsync();
        return query.OrderBy(x => x.Name).ToList();
    }

    public async Task<LabelClient?> FindAsync(int id)
    {
        return await _repo.FindAsync(id);
    }

    public async Task<(LabelClient Client, string Key)> CreateAsync(string name, string slug, string? existingKey = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new BusinessException("GGA1C.Sync:EmptyClientName")
                .WithData("message", "Имя учётной записи обязательно");
        }

        var normalizedSlug = (slug ?? "").Trim().ToLowerInvariant();

        if (!IsValidSlug(normalizedSlug))
        {
            throw new BusinessException("GGA1C.Sync:InvalidSlug")
                .WithData("message",
                    "Папка буфера может содержать только латиницу, цифры, дефис и подчёркивание");
        }

        var query = await _repo.GetQueryableAsync();
        if (query.Any(x => x.Slug == normalizedSlug))
        {
            throw new BusinessException("GGA1C.Sync:DuplicateSlug")
                .WithData("message", $"Папка буфера «{normalizedSlug}» уже занята другой учётной записью");
        }

        var key = string.IsNullOrWhiteSpace(existingKey) ? GenerateKey() : existingKey.Trim();

        var client = new LabelClient
        {
            Name = name.Trim(),
            Slug = normalizedSlug,
            KeyHash = HashKey(key),
            KeyPrefix = key[..8],
            KeyEncrypted = _encryption.Encrypt(key),
            IsEnabled = true
        };

        await _repo.InsertAsync(client, autoSave: true);

        return (client, key);
    }

    public async Task<string> RegenerateKeyAsync(int id)
    {
        var client = await _repo.GetAsync(id);

        var key = GenerateKey();

        client.KeyHash = HashKey(key);
        client.KeyPrefix = key[..8];
        client.KeyEncrypted = _encryption.Encrypt(key);

        await _repo.UpdateAsync(client, autoSave: true);

        return key;
    }

    public async Task<string?> RevealKeyAsync(int id)
    {
        var client = await _repo.GetAsync(id);

        if (string.IsNullOrEmpty(client.KeyEncrypted))
        {
            return null;
        }

        return _encryption.Decrypt(client.KeyEncrypted);
    }

    public async Task SetEnabledAsync(int id, bool enabled)
    {
        var client = await _repo.GetAsync(id);
        client.IsEnabled = enabled;
        await _repo.UpdateAsync(client, autoSave: true);
    }

    public async Task DeleteAsync(int id)
    {
        // Здесь удаляется только запись. Файлы буфера сносит LabelClientsAppService:
        // ему доступна фабрика буферов, а хранилищу знать про файловую систему незачем.
        await _repo.DeleteAsync(id, autoSave: true);
    }

    public async Task TouchAsync(int id)
    {
        var client = await _repo.FindAsync(id);
        if (client == null)
        {
            return;
        }

        client.LastUsedAt = DateTime.UtcNow;
        await _repo.UpdateAsync(client, autoSave: true);
    }

    public async Task<LabelClient?> FindBySubAsync(string sub)
    {
        if (string.IsNullOrWhiteSpace(sub))
        {
            return null;
        }

        return await _repo.FindAsync(x => x.KeycloakSub == sub);
    }

    public async Task<LabelClient?> FindByEmailAsync(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var normalized = email.Trim().ToLowerInvariant();
        var query = await _repo.GetQueryableAsync();

        return query.FirstOrDefault(x => x.Email != null && x.Email.ToLower() == normalized);
    }

    public async Task UpdateAsync(LabelClient client)
    {
        await _repo.UpdateAsync(client, autoSave: true);
    }

    public async Task<string> MakeUniqueSlugAsync(string baseSlug)
    {
        var query = await _repo.GetQueryableAsync();
        var taken = query.Select(x => x.Slug).ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!taken.Contains(baseSlug))
        {
            return baseSlug;
        }

        for (var n = 2; n < 1000; n++)
        {
            var candidate = baseSlug + n;
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }

        throw new BusinessException("GGA1C.Sync:CannotMakeUniqueSlug")
            .WithData("message", $"Не удалось подобрать свободное имя папки для «{baseSlug}»");
    }

    public async Task SetCredentialsAsync(int id, AccountCredentials input)
    {
        var client = await _repo.GetAsync(id);

        client.AltiumLogin = Trim(input.AltiumLogin);
        client.PdmLogin = Trim(input.PdmLogin);
        client.OneCLogin = Trim(input.OneCLogin);

        // Пароль приходит маской своей длины (одни точки) - значит поле не трогали и
        // прежнее значение остаётся. Пусто - человек стёр точки, пароль нужно удалить.
        // Иное - новое значение.
        client.AltiumPasswordEncrypted = Apply(client.AltiumPasswordEncrypted, input.AltiumPassword);
        client.PdmPasswordEncrypted = Apply(client.PdmPasswordEncrypted, input.PdmPassword);
        client.OneCPasswordEncrypted = Apply(client.OneCPasswordEncrypted, input.OneCPassword);

        client.CredentialsUpdatedAt = DateTime.UtcNow;

        await _repo.UpdateAsync(client, autoSave: true);
    }

    private string? Apply(string? current, string? entered)
    {
        if (SecretMask.IsMask(entered))
        {
            return current;
        }

        return string.IsNullOrWhiteSpace(entered) ? null : _encryption.Encrypt(entered.Trim());
    }

    public async Task SetPrintCredentialsAsync(int id, string login, string password)
    {
        var client = await _repo.GetAsync(id);

        client.PrintLogin = login;
        client.PrintPasswordEncrypted = _encryption.Encrypt(password);

        await _repo.UpdateAsync(client, autoSave: true);
    }

    public async Task<string?> RevealPrintPasswordAsync(int id)
    {
        var client = await _repo.GetAsync(id);

        return Decrypt(client.PrintPasswordEncrypted);
    }

    public async Task<AccountCredentials> RevealCredentialsAsync(int id)
    {
        var client = await _repo.GetAsync(id);

        return new AccountCredentials(
            client.AltiumLogin,
            Decrypt(client.AltiumPasswordEncrypted),
            client.PdmLogin,
            Decrypt(client.PdmPasswordEncrypted),
            client.OneCLogin,
            Decrypt(client.OneCPasswordEncrypted));
    }

    private string? Decrypt(string? encrypted)
    {
        return string.IsNullOrEmpty(encrypted) ? null : _encryption.Decrypt(encrypted);
    }

    private static string? Trim(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>40 знаков hex — тот же вид, что выдавала прежняя версия сервиса,
    /// чтобы уже прописанные в виджетах ключи не выглядели инородно.</summary>
    private static string GenerateKey()
    {
        return Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N")[..8];
    }

    private static string HashKey(string key)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();
    }

    private static bool IsValidSlug(string slug)
    {
        return !string.IsNullOrWhiteSpace(slug)
            && slug.All(c => char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_');
    }
}
