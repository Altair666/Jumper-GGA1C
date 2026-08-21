using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using AltiumSync.Models;

namespace AltiumSync.Services;

public class AltiumService
{
    private readonly IHttpClientFactory _factory;
    private readonly ILogger<AltiumService> _log;

    private string   _baseUrl  = "";
    private string   _username = "";
    private string   _password = "";

    // Отдельный HttpClient с CookieContainer — нужен для сессии
    private HttpClient?      _session;
    private CookieContainer  _cookies  = new();
    private bool             _loggedIn = false;
    private DateTime         _loginAt  = DateTime.MinValue;

    private readonly SemaphoreSlim _loginLock = new(1, 1);

    public AltiumService(IHttpClientFactory factory, ILogger<AltiumService> log)
    {
        _factory = factory;
        _log     = log;
    }

    private string _apiComponents  = "/components/api/components";
    private string _apiFolders     = "/components/api/tags";
    private string _apiProjects    = "/api/v1/projects";
    private string _lifecycleFilter = "";
    private string _browserCookies  = "";

    // Поля которые запрашивает веб-интерфейс AES
    private const string ComponentFields =
        "fields[]=Id&fields[]=Comment&fields[]=Description" +
        "&fields[]=Manufacturer&fields[]=FootprintName1&fields[]=ItemGUID" +
        "&fields[]=HRID&fields[]=Update+Date&fields[]=Revision+State";

    public void Configure(string baseUrl, string username, string password,
        string apiComponents = "", string apiFolders = "",
        string apiProjects   = "", string lifecycleFilter = "",
        string browserCookies = "")
    {
        var credentialsChanged =
            _baseUrl        != baseUrl.TrimEnd('/') ||
            _username       != username ||
            _password       != password ||
            _browserCookies != browserCookies;

        _baseUrl        = baseUrl.TrimEnd('/');
        _username       = username;
        _password       = password;
        _browserCookies = browserCookies;

        if (!string.IsNullOrWhiteSpace(apiComponents)) _apiComponents = apiComponents;
        if (!string.IsNullOrWhiteSpace(apiFolders))    _apiFolders    = apiFolders;
        if (!string.IsNullOrWhiteSpace(apiProjects))   _apiProjects   = apiProjects;
        _lifecycleFilter = lifecycleFilter;

        if (credentialsChanged)
        {
            _loggedIn = false;
            _loginAt  = DateTime.MinValue;
            _session  = null;
            _cookies  = new CookieContainer();
        }

        // Если заданы куки браузера — считаем себя залогиненными
        if (!string.IsNullOrEmpty(_browserCookies))
        {
            _loggedIn = true;
            _loginAt  = DateTime.UtcNow;
        }
    }

    public List<object> GetSessionCookies()
    {
        if (string.IsNullOrEmpty(_baseUrl)) return new();
        try
        {
            var uri = new Uri(_baseUrl);
            return _cookies.GetCookies(uri)
                .Cast<System.Net.Cookie>()
                .Select(c => (object)new
                {
                    name    = c.Name,
                    value   = c.Value.Length > 40 ? c.Value[..40] + "..." : c.Value,
                    expires = c.Expires.ToString("o"),
                    httpOnly = c.HttpOnly,
                })
                .ToList();
        }
        catch { return new(); }
    }

    public string LifecycleFilter => _lifecycleFilter;

    // Зондирует произвольный путь — для диагностики
    public async Task<(bool ok, int status, string body)> ProbeAsync(string path)
    {
        try
        {
            await EnsureLoggedInAsync();
            var http = GetSession();
            var resp = await http.GetAsync(_baseUrl + path);
            var body = await resp.Content.ReadAsStringAsync();
            return ((int)resp.StatusCode < 400, (int)resp.StatusCode, body);
        }
        catch (Exception ex)
        {
            return (false, 0, ex.Message);
        }
    }

    // ── Авторизация ───────────────────────────────────────────────────────────

    private HttpClient GetSession()
    {
        if (_session != null) return _session;

        var handler = new HttpClientHandler
        {
            CookieContainer = _cookies,
            UseCookies      = true,
            AllowAutoRedirect = true,
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
        };
        _session = new HttpClient(handler);
        _session.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
            "(KHTML, like Gecko) Chrome/148.0.0.0 Safari/537.36");
        return _session;
    }

    private async Task EnsureLoggedInAsync()
    {
        if (_loggedIn && DateTime.UtcNow - _loginAt < TimeSpan.FromHours(23))
            return;

        await _loginLock.WaitAsync();
        try
        {
            if (_loggedIn && DateTime.UtcNow - _loginAt < TimeSpan.FromHours(23))
                return;

            var http = GetSession();
            _log.LogInformation("Авторизация в Altium Enterprise Server...");

            // ── Шаг 1: GET /Account/Login — получаем куки и пробуем найти CSRF ──
            var loginUrl = $"{_baseUrl}/Account/Login";
            var getResp  = await http.GetAsync(loginUrl);
            var html     = await getResp.Content.ReadAsStringAsync();

            // ── Шаг 2: CSRF токен — ищем в HTML, потом в куках ───────────────
            var csrfToken = ExtractCsrfToken(html);

            if (string.IsNullOrEmpty(csrfToken))
            {
                // Страница — SPA, токен в cookie, а не в HTML
                var uri = new Uri(_baseUrl);
                var cookie = _cookies.GetCookies(uri)
                    .Cast<Cookie>()
                    .FirstOrDefault(c =>
                        c.Name.Contains("RequestVerification", StringComparison.OrdinalIgnoreCase) ||
                        c.Name.Contains("CSRF",               StringComparison.OrdinalIgnoreCase) ||
                        c.Name.Contains("csrf",               StringComparison.OrdinalIgnoreCase) ||
                        c.Name.Contains("xsrf",               StringComparison.OrdinalIgnoreCase));

                if (cookie != null)
                {
                    csrfToken = cookie.Value;
                    _log.LogInformation("CSRF токен получен из cookie: {Name}", cookie.Name);
                }
            }

            // ── Шаг 3: POST логин ─────────────────────────────────────────────
            bool success;

            if (!string.IsNullOrEmpty(csrfToken))
            {
                // Вариант А — form-encoded с CSRF (стандартный ASP.NET MVC)
                success = await TryFormLoginAsync(http, loginUrl, csrfToken);
            }
            else
            {
                // Вариант Б — JSON login (некоторые версии AES принимают JSON)
                _log.LogWarning("CSRF токен не найден, пробуем JSON login...");
                success = await TryJsonLoginAsync(http);

                if (!success)
                {
                    // Вариант В — form-encoded БЕЗ CSRF (на случай если он не нужен)
                    _log.LogWarning("JSON login не сработал, пробуем form без CSRF...");
                    success = await TryFormLoginAsync(http, loginUrl, "");
                }
            }

            if (!success)
                throw new InvalidOperationException(
                    "Все методы авторизации не сработали. " +
                    "Проверьте URL, логин и пароль. " +
                    "Откройте /api/diag/login-page для диагностики.");

            _loggedIn = true;
            _loginAt  = DateTime.UtcNow;
            _log.LogInformation("Авторизация в Altium успешна");

            // Прогрев сессии — посещаем страницы как браузер
            // Это устанавливает контекст workspace для последующих API запросов
            await WarmupSessionAsync(http);
        }
        finally
        {
            _loginLock.Release();
        }
    }

    private async Task WarmupSessionAsync(HttpClient http)
    {
        try
        {
            _log.LogInformation("Прогрев сессии...");
            var uri = new Uri(_baseUrl);

            // 1. Главная страница
            await http.GetAsync(_baseUrl + "/");

            // 2. Страница компонентов
            await http.GetAsync(_baseUrl + "/components");

            // 3. Пробуем получить session cookie через librarycomponentsapi
            // Вариант А: view конкретного компонента (первый из известных)
            await http.GetAsync(_baseUrl +
                "/librarycomponentsapi/api/v1/components/view/8f704578-4fd9-4d7f-965d-3686c31b0900");

            // Вариант Б: список компонентов
            await http.GetAsync(_baseUrl + "/librarycomponentsapi/api/v1/components");

            // Вариант В: download endpoint
            await http.GetAsync(_baseUrl + "/librarycomponentsapi/api/v1/download");
            await http.GetAsync(_baseUrl + "/librarycomponentsapi/api/v1/session");

            // 4. Если кука так и не появилась — ставим вручную
            // Сервер может просто проверять наличие куки, не валидируя значение
            var hasCookie = _cookies.GetCookies(uri)
                .Cast<System.Net.Cookie>()
                .Any(c => c.Name.Contains("LIBRARYCOMPONENTS", StringComparison.OrdinalIgnoreCase));

            if (!hasCookie)
            {
                var sessionGuid = Guid.NewGuid().ToString("D").ToUpper();
                _cookies.Add(uri, new System.Net.Cookie(
                    "A365_LIBRARYCOMPONENTSAPI_DOWLOAD_SESSION", sessionGuid));
                _log.LogInformation(
                    "Кука A365_LIBRARYCOMPONENTSAPI_DOWLOAD_SESSION добавлена вручную: {Guid}",
                    sessionGuid);
            }

            // 5. IsUnsupportedBrowser cookie (может нужна для фильтрации)
            var hasUnsupported = _cookies.GetCookies(uri)
                .Cast<System.Net.Cookie>()
                .Any(c => c.Name == "IsUnsupportedBrowser");
            if (!hasUnsupported)
                _cookies.Add(uri, new System.Net.Cookie("IsUnsupportedBrowser", "0"));

            // 6. Инициализируем компонентный API
            await http.GetAsync(_baseUrl + "/components/api/tags");

            var all = _cookies.GetCookies(uri).Cast<System.Net.Cookie>().ToList();
            _log.LogInformation("Прогрев завершён. Кук: {Count}: [{Names}]",
                all.Count, string.Join(", ", all.Select(c => c.Name)));
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Ошибка прогрева (не критично)");
        }
    }

    private async Task<bool> TryFormLoginAsync(HttpClient http, string loginUrl, string csrfToken)
    {
        var fields = new Dictionary<string, string>
        {
            ["UserName"]              = _username,
            ["Password"]              = _password,
            ["RememberMe"]            = "false",
            ["UseWindowsSessionCred"] = "false",
            ["UseExtraOptions"]       = "false",
            ["X-Requested-With"]      = "XMLHttpRequest",
        };
        if (!string.IsNullOrEmpty(csrfToken))
            fields["__RequestVerificationToken"] = csrfToken;

        var resp = await http.PostAsync(loginUrl, new FormUrlEncodedContent(fields));
        return await IsLoggedInAsync(http, resp);
    }

    private async Task<bool> TryJsonLoginAsync(HttpClient http)
    {
        // Пробуем несколько JSON endpoint-ов которые встречаются в AES
        var endpoints = new[]
        {
            "/api/v1/userlogin",
            "/api/userlogin",
            "/Account/LoginJson",
        };

        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            UserName     = _username,
            UserPassword = _password,
        });

        foreach (var ep in endpoints)
        {
            try
            {
                var content = new StringContent(payload,
                    System.Text.Encoding.UTF8, "application/json");
                var resp = await http.PostAsync(_baseUrl + ep, content);
                if (resp.IsSuccessStatusCode) return true;
            }
            catch { /* пробуем следующий */ }
        }
        return false;
    }

    private async Task<bool> IsLoggedInAsync(HttpClient http, HttpResponseMessage lastResp)
    {
        // Проверяем есть ли auth-кука
        var uri = new Uri(_baseUrl);
        var hasAuthCookie = _cookies.GetCookies(uri)
            .Cast<Cookie>()
            .Any(c => c.Name.Contains("ASPXAUTH",  StringComparison.OrdinalIgnoreCase) ||
                      c.Name.Contains("session",   StringComparison.OrdinalIgnoreCase) ||
                      c.Name.Contains(".auth",     StringComparison.OrdinalIgnoreCase) ||
                      c.Name.Contains("auth",      StringComparison.OrdinalIgnoreCase));

        if (hasAuthCookie) return true;

        // Если куки нет — пробуем запрос к защищённому ресурсу
        try
        {
            var check = await http.GetAsync($"{_baseUrl}/components/api/tags");
            return check.IsSuccessStatusCode;
        }
        catch { return false; }
    }


    private static string ExtractCsrfToken(string html)
    {
        // Пробуем все возможные варианты расположения токена в HTML

        // Вариант 1: <input name="__RequestVerificationToken" ... value="TOKEN">
        var m = Regex.Match(html,
            @"<input[^>]+name=[""']__RequestVerificationToken[""'][^>]+value=[""']([^""']+)[""']",
            RegexOptions.IgnoreCase);
        if (m.Success) return m.Groups[1].Value;

        // Вариант 2: <input ... value="TOKEN" ... name="__RequestVerificationToken">
        m = Regex.Match(html,
            @"<input[^>]+value=[""']([^""']+)[""'][^>]+name=[""']__RequestVerificationToken[""']",
            RegexOptions.IgnoreCase);
        if (m.Success) return m.Groups[1].Value;

        // Вариант 3: __RequestVerificationToken ... value="TOKEN" (любой порядок атрибутов)
        m = Regex.Match(html,
            @"__RequestVerificationToken[^>]+value=[""']([^""']+)[""']",
            RegexOptions.IgnoreCase);
        if (m.Success) return m.Groups[1].Value;

        // Вариант 4: value="TOKEN" ... __RequestVerificationToken
        m = Regex.Match(html,
            @"value=[""']([^""']+)[""'][^>]*__RequestVerificationToken",
            RegexOptions.IgnoreCase);
        if (m.Success) return m.Groups[1].Value;

        // Вариант 5: в meta теге (некоторые ASP.NET приложения кладут туда)
        m = Regex.Match(html,
            @"<meta[^>]+name=[""']csrf-token[""'][^>]+content=[""']([^""']+)[""']",
            RegexOptions.IgnoreCase);
        if (m.Success) return m.Groups[1].Value;

        // Вариант 6: в JS переменной
        m = Regex.Match(html,
            @"RequestVerificationToken['""\s:=]+['""]([^'""]{20,})['""]",
            RegexOptions.IgnoreCase);
        if (m.Success) return m.Groups[1].Value;

        return "";
    }

    public async Task<List<AltiumFolder>> GetFoldersAsync()
    {
        try
        {
            await EnsureLoggedInAsync();
            var json = await GetAsync(_apiFolders);
            return ParseFolders(json);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Ошибка получения папок из Altium");
            return new();
        }
    }

    public async Task<List<AltiumComponent>> GetComponentsAsync(string? folderId = null)
    {
        try
        {
            await EnsureLoggedInAsync();
            var url = _apiComponents;
            if (!string.IsNullOrEmpty(folderId))
                url += $"?tagGuid={folderId}";
            var json = await GetAsync(url);
            return ParseComponents(json);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Ошибка получения компонентов из Altium");
            return new();
        }
    }

    // Получить все компоненты — сначала пробуем одним запросом,
    // если есть пагинация — грузим постранично
    public async Task<List<AltiumComponent>> GetAllComponentsAsync()
    {
        try
        {
            await EnsureLoggedInAsync();
            var folders      = await GetFoldersAsync();
            var tagsWithComp = folders.Where(f => f.Count > 0).ToList();
            _log.LogInformation(
                "Тегов: {Tags}, ожидаем ~{Total} компонентов",
                tagsWithComp.Count, tagsWithComp.Sum(f => f.Count));
            var result = await LoadByTagsAsync(tagsWithComp);
            _log.LogInformation("Итого компонентов: {Count}", result.Count);
            return result;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Ошибка GetAllComponentsAsync");
            return new();
        }
    }

    private async Task<List<AltiumComponent>> LoadByTagsAsync(List<AltiumFolder> tags)
    {
        var all = new Dictionary<string, AltiumComponent>();

        foreach (var tag in tags)
        {
            int start = 0;
            int got;
            do
            {
                // Точный формат запроса как в веб-интерфейсе AES 7.2
                var url = $"{_apiComponents}?{ComponentFields}" +
                          $"&text=&tag={tag.Id}&orderby=&start={start}&limit=50";
                var json = await GetAsync(url);
                var page = ParseComponents(json);
                got = page.Count;

                foreach (var c in page)
                    if (!all.ContainsKey(c.Id)) { c.FolderId = tag.Id; all[c.Id] = c; }

                start += got;
                _log.LogInformation(
                    "Тег [{Name}] start={S}: +{Got}, итого={All}",
                    tag.Name, start - got, got, all.Count);

                if (got < 50) break;
            } while (start < tag.Count + 100);

            await Task.Delay(30);
        }

        _log.LogInformation("Итого уникальных: {Count}", all.Count);
        return all.Values.ToList();
    }

    public async Task<AltiumComponent?> GetComponentAsync(string componentId)
    {
        try
        {
            await EnsureLoggedInAsync();
            var json = await GetAsync($"{_apiComponents}/{componentId}");
            using var doc = JsonDocument.Parse(json);
            return MapComponent(doc.RootElement);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Ошибка получения компонента {Id}", componentId);
            return null;
        }
    }

    public async Task<List<AltiumProject>> GetProjectsAsync()
    {
        try
        {
            await EnsureLoggedInAsync();
            var json = await GetAsync(_apiProjects);
            return ParseProjects(json);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Ошибка получения проектов из Altium");
            return new();
        }
    }

    public async Task<List<BomLine>> GetProjectBomAsync(string projectId, string revision)
    {
        try
        {
            await EnsureLoggedInAsync();
            var json = await GetAsync($"{_apiProjects}/{projectId}/revisions/{revision}/bom");
            return ParseBom(json);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Ошибка получения BOM {Id}", projectId);
            return new();
        }
    }

    // ── Тест подключения ──────────────────────────────────────────────────────

    public async Task<(bool ok, string message)> TestConnectionAsync()
    {
        try
        {
            _loggedIn = false; // форсируем свежий логин
            await EnsureLoggedInAsync();
            return (true, "Авторизация успешна");
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Тест подключения не удался");
            return (false, ex.Message);
        }
    }

    // ── Внутренние методы ─────────────────────────────────────────────────────

    private HttpClient? _browserSession; // кешированный клиент для режима browser cookies

    private HttpClient GetBrowserSession()
    {
        if (_browserSession != null) return _browserSession;
        var handler = new HttpClientHandler
        {
            UseCookies = false,
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
        };
        _browserSession = new HttpClient(handler);
        _browserSession.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
            "(KHTML, like Gecko) Chrome/148.0.0.0 Safari/537.36");
        return _browserSession;
    }

    private async Task<string> GetAsync(string path)
    {
        // Режим браузерных кук — используем напрямую без нашей сессии
        if (!string.IsNullOrEmpty(_browserCookies))
        {
            var bHttp    = GetBrowserSession();
            var bRequest = new HttpRequestMessage(HttpMethod.Get, _baseUrl + path);
            bRequest.Headers.Add("Cookie", _browserCookies);
            bRequest.Headers.Add("X-Requested-With", "XMLHttpRequest");
            bRequest.Headers.Add("Accept", "application/json, text/plain, */*");
            bRequest.Headers.Add("Referer", _baseUrl + "/components");
            var bResp = await bHttp.SendAsync(bRequest);
            if (!bResp.IsSuccessStatusCode)
                _log.LogWarning("Browser cookie request failed: {Status} {Path}",
                    (int)bResp.StatusCode, path);
            bResp.EnsureSuccessStatusCode();
            return await bResp.Content.ReadAsStringAsync();
        }

        var http    = GetSession();
        var request = new HttpRequestMessage(HttpMethod.Get, _baseUrl + path);
        request.Headers.Add("Accept", "application/json, text/plain, */*");

        // Referer как в браузере — конкретная страница категории
        // Это важно для корректной работы фильтрации по tag/start
        var referer = path.Contains("/components/api/")
            ? _baseUrl + "/components"
            : _baseUrl + "/";
        request.Headers.Add("Referer", referer);

        // Копируем антифодж токен из куки в заголовок если есть
        var uri = new Uri(_baseUrl);
        var xsrfCookie = _cookies.GetCookies(uri)
            .Cast<System.Net.Cookie>()
            .FirstOrDefault(c =>
                c.Name.Equals("XSRF-TOKEN", StringComparison.OrdinalIgnoreCase) ||
                c.Name.Equals("RequestVerificationToken", StringComparison.OrdinalIgnoreCase) ||
                c.Name.StartsWith("__RequestVerification", StringComparison.OrdinalIgnoreCase));
        if (xsrfCookie != null)
            request.Headers.Add("X-XSRF-TOKEN", xsrfCookie.Value);

        var resp = await http.SendAsync(request);

        if (resp.StatusCode == HttpStatusCode.Unauthorized ||
            resp.StatusCode == HttpStatusCode.Redirect)
        {
            _log.LogWarning("Сессия устарела, повторная авторизация...");
            _loggedIn = false;
            await EnsureLoggedInAsync();
            request = new HttpRequestMessage(HttpMethod.Get, _baseUrl + path);
            request.Headers.Add("X-Requested-With", "XMLHttpRequest");
            request.Headers.Add("Accept", "application/json, text/plain, */*");
            resp = await http.SendAsync(request);
        }

        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync();
    }

    // ── Парсеры ───────────────────────────────────────────────────────────────

    private static List<AltiumFolder> ParseFolders(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // AES 6.0: { "tags": [...], "totalCount": N }
        JsonElement arr;
        if      (root.TryGetProperty("tags",  out arr)) { }
        else if (root.ValueKind == JsonValueKind.Array)  arr = root;
        else throw new InvalidDataException("Неожиданный формат ответа tags");

        var result = new List<AltiumFolder>();
        FlattenTags(arr, result);
        return result;
    }

    // Рекурсивно разворачиваем дерево subTags в плоский список
    private static void FlattenTags(JsonElement arr, List<AltiumFolder> result)
    {
        foreach (var e in arr.EnumerateArray())
        {
            var parentGuid = Str(e, "parentGuid");
            result.Add(new AltiumFolder
            {
                Id       = Str(e, "guid"),
                Name     = Str(e, "name"),
                ParentId = string.IsNullOrEmpty(parentGuid) ? null : parentGuid,
                Count    = e.TryGetProperty("count", out var c) ? c.GetInt32() : 0,
            });
            if (e.TryGetProperty("subTags", out var sub) &&
                sub.ValueKind == JsonValueKind.Array)
                FlattenTags(sub, result);
        }
    }

    private static List<AltiumComponent> ParseComponents(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // AES 7.2: { "components": [...], "allFields": [...] }
        JsonElement items;
        if      (root.TryGetProperty("components", out items)) { }
        else if (root.TryGetProperty("items",      out items)) { }
        else if (root.ValueKind == JsonValueKind.Array)        items = root;
        else throw new InvalidDataException("Unexpected components response");

        return items.EnumerateArray().Select(MapComponent).ToList();
    }

    private static AltiumComponent MapComponent(JsonElement e)
    {
        var comp = new AltiumComponent
        {
            Id          = Str(e, "Id"),
            Name        = Str(e, "Comment"),
            Article     = Str(e, "Comment"),
            ItemGuid    = Str(e, "ItemGUID"),
            Description = Str(e, "Description"),
            Manufacturer = Str(e, "Manufacturer"),
        };

        // Все поля в Parameters для полноты
        foreach (var p in e.EnumerateObject())
            comp.Parameters[p.Name] = p.Value.ValueKind == JsonValueKind.String
                ? p.Value.GetString() ?? ""
                : p.Value.ToString();

        // Кастомные 1С поля которые уже есть в библиотеке
        comp.Name1C  = comp.Parameters.GetValueOrDefault("Наименование 1С");
        comp.NameRu  = comp.Parameters.GetValueOrDefault("Наименование");
        comp.Code1C  = comp.Parameters.GetValueOrDefault("Код");

        // Lifecycle state из allFields не приходит в каждом компоненте напрямую,
        // но может быть в параметрах под разными именами
        comp.LifecycleState = comp.Parameters.GetValueOrDefault("Revision State")
                           ?? comp.Parameters.GetValueOrDefault("LifecycleState");

        return comp;
    }

    private static List<AltiumProject> ParseProjects(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root  = doc.RootElement;
        var items = root.TryGetProperty("items", out var arr) ? arr
                  : root.ValueKind == JsonValueKind.Array    ? root
                  : throw new InvalidDataException("Неожиданный формат ответа projects");
        return items.EnumerateArray().Select(e => new AltiumProject
        {
            Id       = Str(e, "id"),
            Name     = Str(e, "name"),
            Revision = Str(e, "revision"),
        }).ToList();
    }

    private static List<BomLine> ParseBom(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root  = doc.RootElement;
        var items = root.TryGetProperty("items", out var arr) ? arr
                  : root.ValueKind == JsonValueKind.Array    ? root
                  : throw new InvalidDataException("Неожиданный формат ответа BOM");
        return items.EnumerateArray().Select(e => new BomLine
        {
            Designator = Str(e, "designator"),
            AltiumId   = Str(e, "componentId"),
            Name       = Str(e, "name"),
            Quantity   = e.TryGetProperty("quantity", out var q) ? q.GetInt32() : 1,
            Unit       = e.TryGetProperty("unit",     out var u)
                         ? u.GetString() ?? "шт" : "шт",
        }).ToList();
    }

    private static string Str(JsonElement e, string key) =>
        e.TryGetProperty(key, out var v) ? v.GetString() ?? "" : "";
}

// ── DTO ───────────────────────────────────────────────────────────────────────

public class AltiumFolder
{
    public string  Id         { get; set; } = "";   // guid
    public string  Name       { get; set; } = "";
    public string? ParentId   { get; set; }         // parentGuid
    public int     Count      { get; set; }
}

public class AltiumComponent
{
    public string  Id             { get; set; } = "";
    public string  Name           { get; set; } = "";   // Comment (артикул)
    public string  Article        { get; set; } = "";   // Comment
    public string? ItemGuid       { get; set; }         // ItemGUID
    public string? Description    { get; set; }
    public string? Manufacturer   { get; set; }
    public string? Name1C         { get; set; }         // "Наименование 1С"
    public string? NameRu         { get; set; }         // "Наименование"
    public string? Code1C         { get; set; }         // "Код"
    public string? FolderId       { get; set; }
    public string? LifecycleState { get; set; }         // "Revision State"
    public Dictionary<string, string> Parameters { get; set; } = new();
}

public class AltiumProject
{
    public string Id       { get; set; } = "";
    public string Name     { get; set; } = "";
    public string Revision { get; set; } = "";
}
