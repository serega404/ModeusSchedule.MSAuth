using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace ModeusSchedule.MSAuth.BrowserScripts;

public static class MicrosoftLoginHelper
{
    public static async Task LoginMicrosoftAsync(
        IPage page,
        ILogger logger,
        string username,
        string password,
        string loginUrl
        )
    {
        logger.LogInformation("Начало входа в Microsoft. Url: {LoginUrl}, пользователь: {Username}", loginUrl, username);

        await page.GotoAsync(loginUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });

        logger.LogDebug("Страница логина загружена, ожидаем переход на домен Microsoft.");

        await page.WaitForURLAsync(new Regex("login\\.(microsoftonline|live)\\.com", RegexOptions.IgnoreCase), new PageWaitForURLOptions { Timeout = 60_000 });

        var useAnotherAccount = page.Locator("div#otherTile, #otherTileText, div[data-test-id='useAnotherAccount']").First;
        try
        {
            await Assertions.Expect(useAnotherAccount).ToBeVisibleAsync(new() { Timeout = 2000 });
            logger.LogDebug("Обнаружена кнопка 'Использовать другой аккаунт'. Нажимаем.");
            await useAnotherAccount.ClickAsync();
        }
        catch (PlaywrightException ex)
        {
            logger.LogDebug(ex, "Кнопка 'Использовать другой аккаунт' не появилась — пропускаем шаг.");
        }

        var emailInput = page.Locator("input[name='loginfmt'], input#i0116");
        await emailInput.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 30_000 });
        logger.LogDebug("Поле ввода email найдено, вводим логин.");
        await emailInput.FillAsync(username);

        var nextButton = page.Locator("#idSIButton9, input#idSIButton9");
        await nextButton.ClickAsync();

        logger.LogDebug("Нажата кнопка 'Далее' после ввода логина.");

        var passwordInput = page.Locator("input[name='passwd'], input#i0118");
        await passwordInput.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 30_000 });
        logger.LogDebug("Поле ввода пароля найдено, вводим пароль.");
        await passwordInput.FillAsync(password);
        await nextButton.ClickAsync();

        logger.LogDebug("Нажата кнопка входа после ввода пароля.");

        await page.WaitForSelectorAsync("button, input[type='submit'], a", new PageWaitForSelectorOptions { Timeout = 8000 });

        var locator = page.Locator("#idSIButton9, #idBtn_Back").First;
        try
        {
            await Assertions.Expect(locator).ToBeVisibleAsync(new() { Timeout = 3000 });
            logger.LogDebug("Обнаружен экран 'Остаться в системе?'.");
            var noBtn = page.Locator("#idBtn_Back");
            if (await noBtn.IsVisibleAsync())
            {
                logger.LogDebug("Нажимаем кнопку 'Нет'.");
                await noBtn.ClickAsync();
            }
            else
            {
                logger.LogDebug("Кнопка 'Нет' не найдена, нажимаем 'Да'/'Далее'.");
                await page.Locator("#idSIButton9").ClickAsync();
            }
        }
        catch (PlaywrightException ex)
        {
            logger.LogDebug(ex, "Экран 'Остаться в системе?' не появился — пропускаем шаг.");
        }

        logger.LogInformation("Ожидаем завершения редиректов после логина.");
        await page.WaitForURLAsync(url => !Regex.IsMatch(new Uri(url).Host, "login\\.(microsoftonline|live)\\.com", RegexOptions.IgnoreCase), new PageWaitForURLOptions { Timeout = 60_000 });
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var currentHost = new Uri(page.Url).Host;
        if (Regex.IsMatch(currentHost, "login\\.(microsoftonline|live)\\.com", RegexOptions.IgnoreCase))
        {
            logger.LogError("Авторизация не завершена: остались на странице Microsoft Login. Текущий URL: {Url}", page.Url);
            throw new Exception("Авторизация не завершена: остались на странице Microsoft Login");
        }

        logger.LogInformation("Успешный вход в Microsoft. Текущий URL: {Url}", page.Url);
    }
}
