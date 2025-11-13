using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace ModeusSchedule.MSAuth.BrowserScripts;

public static class MicrosoftLoginHelper
{
    public static async Task LoginMicrosoftAsync(IPage page, string username, string password, string loginUrl)
    {
        await page.GotoAsync(loginUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });

        await page.WaitForURLAsync(new Regex("login\\.(microsoftonline|live)\\.com", RegexOptions.IgnoreCase), new PageWaitForURLOptions { Timeout = 60_000 });

        var useAnotherAccount = page.Locator("div#otherTile, #otherTileText, div[data-test-id='useAnotherAccount']").First;
        try
        {
            await Assertions.Expect(useAnotherAccount).ToBeVisibleAsync(new() { Timeout = 2000 });
            await useAnotherAccount.ClickAsync();
        }
        catch (PlaywrightException)
        {
            // Кнопка не появилась — пропускаем
        }

        var emailInput = page.Locator("input[name='loginfmt'], input#i0116");
        await emailInput.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 30_000 });
        await emailInput.FillAsync(username);

        var nextButton = page.Locator("#idSIButton9, input#idSIButton9");
        await nextButton.ClickAsync();

        var passwordInput = page.Locator("input[name='passwd'], input#i0118");
        await passwordInput.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 30_000 });
        await passwordInput.FillAsync(password);
        await nextButton.ClickAsync();

        await page.WaitForSelectorAsync("button, input[type='submit'], a", new PageWaitForSelectorOptions { Timeout = 8000 });

        var locator = page.Locator("#idSIButton9, #idBtn_Back").First;
        try
        {
            await Assertions.Expect(locator).ToBeVisibleAsync(new() { Timeout = 3000 });
            var noBtn = page.Locator("#idBtn_Back");
            if (await noBtn.IsVisibleAsync())
                await noBtn.ClickAsync();
            else
                await page.Locator("#idSIButton9").ClickAsync();
        }
        catch (PlaywrightException)
        {
            // Кнопки не появились — пропускаем этот шаг
        }

        await page.WaitForURLAsync(url => !Regex.IsMatch(new Uri(url).Host, "login\\.(microsoftonline|live)\\.com", RegexOptions.IgnoreCase), new PageWaitForURLOptions { Timeout = 60_000 });
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var currentHost = new Uri(page.Url).Host;
        if (Regex.IsMatch(currentHost, "login\\.(microsoftonline|live)\\.com", RegexOptions.IgnoreCase))
            throw new Exception("Авторизация не завершена: остались на странице Microsoft Login");
    }
}
