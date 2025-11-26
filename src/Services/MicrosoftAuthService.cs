using System.Diagnostics;
using System.Text.Json;
using Microsoft.Playwright;
using ModeusSchedule.MSAuth.BrowserScripts;

namespace ModeusSchedule.MSAuth.Services;

public class MicrosoftAuthService(ILogger<MicrosoftAuthService> logger, IConfiguration configuration)
{
    private static bool _browsersEnsured;
    private static readonly SemaphoreSlim EnsureLock = new(1, 1);
    private static readonly SemaphoreSlim FetchLock = new(1, 1);

    private string? _cachedToken;
    private DateTime _cachedAtUtc;
    private readonly TimeSpan _cacheTtl = TimeSpan.FromMinutes(20);
    private readonly TotpGenerator? _totpGenerator = CreateTotpGenerator(configuration, logger);

    public bool HasFreshToken => _cachedToken != null && DateTime.UtcNow - _cachedAtUtc < _cacheTtl;

    public async Task<string> GetJwtAsync(CancellationToken ct = default)
    {
        logger.LogDebug("Запрошен JWT токен. HasFreshToken=" + HasFreshToken);
        await EnsureBrowsersAsync();

        // Если кэш актуален — вернуть сразу
        if (HasFreshToken)
        {
            logger.LogInformation("Возвращаем закэшированный JWT токен, возраст={AgeSeconds} сек", (DateTime.UtcNow - _cachedAtUtc).TotalSeconds);
            return _cachedToken!;
        }

        // Пытаемся единолично выполнить авторизацию
        if (!await FetchLock.WaitAsync(0, ct))
        {
            // Если кто-то уже выполняет, а кэша нет — просим повторить позже (429)
            throw new MicrosoftAuthInProgressException();
        }

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            // Если запущен отладчик — запускаем окно браузера
            Headless = !Debugger.IsAttached,
            // При debug автоматически открывать DevTools
            // Devtools = Debugger.IsAttached
        });
        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = null
        });
        var page = await context.NewPageAsync();

        try
        {
            logger.LogInformation("Старт авторизации через Microsoft");
            await MicrosoftLoginHelper.LoginMicrosoftAsync(
                page,
                logger,
                configuration["MS_USERNAME"]!,
                configuration["MS_PASSWORD"]!,
                configuration["MODEUS_URL"]!,
                _totpGenerator is null ? null : () => _totpGenerator.Generate());

            var sessionStorageJson = await page.EvaluateAsync<string>("JSON.stringify(sessionStorage)");

            logger.LogDebug("Пробуем извлечь id_token из sessionStorage");
            string? idToken = ExtractIdToken(sessionStorageJson);
            if (string.IsNullOrWhiteSpace(idToken))
            {
                logger.LogError("Не удалось извлечь id_token из sessionStorage");
                throw new Exception("Не удалось извлечь id_token из sessionStorage");
            }

            // Сохраняем в кэш
            _cachedToken = idToken;
            _cachedAtUtc = DateTime.UtcNow;
            logger.LogInformation("Успешно получили и закэшировали id_token");
            return idToken;
        }
        catch (Exception ex) when (ex is not MicrosoftAuthInProgressException)
        {
            logger.LogError(ex, "Ошибка при получении JWT через Microsoft авторизацию");
            throw;
        }
        finally
        {
            await context.CloseAsync();
            await browser.CloseAsync();

            if (FetchLock.CurrentCount == 0) FetchLock.Release();
        }
    }

    private static string? ExtractIdToken(string sessionStorageJson)
    {
        try
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(sessionStorageJson);
            if (dict is null) return null;

            var oidcKey = dict.Keys.FirstOrDefault(k => k.StartsWith("oidc.user:"));
            if (oidcKey is null) return null;

            var oidcValueJson = dict[oidcKey].ToString();
            if (string.IsNullOrWhiteSpace(oidcValueJson)) return null;

            using var doc = JsonDocument.Parse(oidcValueJson);
            if (doc.RootElement.TryGetProperty("id_token", out var tokenEl))
                return tokenEl.GetString();
        }
        catch
        {
            // ignore and return null
        }
        return null;
    }

    private static async Task EnsureBrowsersAsync()
    {
        if (_browsersEnsured) return;
        await EnsureLock.WaitAsync();
        try
        {
            if (_browsersEnsured) return;
            try
            {
                // Устанавливаем Chromium, если не установлен
                Microsoft.Playwright.Program.Main(["install", "chromium"]);
            }
            catch
            {
                // Игнорируем, если установка уже произведена или нет прав — попробуем дальше запустить браузер
            }
            _browsersEnsured = true;
        }
        finally
        {
            EnsureLock.Release();
        }
    }

    /// <summary>
    /// Создает генератор TOTP из переменной окружения, если она настроена.
    /// </summary>
    private static TotpGenerator? CreateTotpGenerator(IConfiguration configuration, ILogger logger)
    {
        var secret = configuration["MS_TOTP_SECRET"];
        if (string.IsNullOrWhiteSpace(secret))
        {
            logger.LogDebug("MS_TOTP_SECRET не задан, MFA по TOTP отключен.");
            return null;
        }

        try
        {
            logger.LogInformation("Инициализация генератора TOTP для Microsoft MFA.");
            return new TotpGenerator(secret);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Не удалось инициализировать TOTP генератор. MFA не будет работать.");
            return null;
        }
    }
}

public class MicrosoftAuthInProgressException : Exception
{
}
