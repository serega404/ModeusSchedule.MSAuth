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
        string loginUrl,
        Func<string?>? totpProvider = null
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

        #region MFA: выбирает TOTP вариант, запрашивает код и вводит его.

        // Пытается выбрать TOTP метод на экране выбора MFA, если он отображен.
        var totpOption = page.Locator("div[data-value='PhoneAppOTP'], div[data-value='OneWayOtp'], div[data-value='PhoneAppOTPRW'], div[data-value='AuthenticatorApp'], div[data-value='OneWayTotp']").First;
        try
        {
            await Assertions.Expect(totpOption).ToBeVisibleAsync(new() { Timeout = 3_000 });
            logger.LogDebug("Обнаружен выбор метода MFA. Выбираем опцию TOTP.");
            await totpOption.ClickAsync();

            var continueButton = page.Locator("#idSubmit_SAOTCS_ProofConfirmation, #idSIButton9, button[type='submit']").First;
            if (await continueButton.IsEnabledAsync())
            {
                await continueButton.ClickAsync();
                logger.LogDebug("Подтвердили выбор метода TOTP.");
            }
        }
        catch (PlaywrightException ex)
        {
            logger.LogDebug(ex, "Экран выбора метода MFA не отображен — продолжаем без выбора.");
        }

        var totpInput = page.Locator("#idTxtBx_SAOTCC_OTC, input[name='otc'], input[name='code']").First;

        if (totpProvider is null)
        {
            if (await totpInput.IsVisibleAsync())
            {
                logger.LogError("Обнаружен запрос на ввод TOTP, но провайдер кода не настроен. Установите переменную MS_TOTP_SECRET.");
                throw new InvalidOperationException("TOTP требуется, но провайдер не настроен");
            }

            logger.LogDebug("TOTP не требуется или провайдер не задан.");
            return;
        }

        try
        {
            await Assertions.Expect(totpInput).ToBeVisibleAsync(new() { Timeout = 10_000 });
        }
        catch (PlaywrightException ex)
        {
            logger.LogDebug(ex, "Поле ввода TOTP не найдено — MFA, вероятно, не требуется.");
            return;
        }

        var totpCode = totpProvider();
        if (string.IsNullOrWhiteSpace(totpCode))
        {
            logger.LogError("TOTP провайдер вернул пустое значение.");
            throw new InvalidOperationException("TOTP провайдер вернул пустой код");
        }

        logger.LogInformation("Обнаружен экран MFA. Вводим TOTP код.");
        await totpInput.FillAsync(totpCode);

        var submit = page.Locator("#idSubmit_SAOTCC_Continue, #idSIButton9, button[type='submit']").First;
        try
        {
            await submit.ClickAsync(new() { Timeout = 5_000 });
            logger.LogDebug("Отправлен TOTP код.");
        }
        catch (PlaywrightException ex)
        {
            logger.LogDebug(ex, "Не удалось автоматически отправить TOTP код — возможно, форма отправилась автоматически.");
        }

        #endregion

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
