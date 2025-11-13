using ModeusSchedule.MSAuth.Services;

var builder = WebApplication.CreateBuilder(args);

if (string.IsNullOrWhiteSpace(builder.Configuration["MODEUS_URL"]))
{
    Console.Error.WriteLine("Ошибка: не задан URL для Modeus. Пожалуйста, укажите MODEUS_URL в файле конфигурации или переменных окружения.");
    Environment.Exit(1);
}

Console.WriteLine($"Используется URL Modeus: {builder.Configuration["MODEUS_URL"]}");

if (string.IsNullOrWhiteSpace(builder.Configuration["MS_USERNAME"]) || string.IsNullOrWhiteSpace(builder.Configuration["MS_PASSWORD"]))
{
    Console.Error.WriteLine("Ошибка: не заданы учетные данные для MicrosoftAuth. Пожалуйста, укажите MS_USERNAME и MS_PASSWORD в файле конфигурации или переменных окружения.");
    Environment.Exit(1);
}

var configuredApiKey = builder.Configuration["API_KEY"];

builder.Services.AddSingleton<MicrosoftAuthService>();

var app = builder.Build();

if (!string.IsNullOrWhiteSpace(configuredApiKey))
{
    app.Use(async (context, next) =>
    {
        if (!context.Request.Headers.TryGetValue("X-API-Key", out var providedKey) || !string.Equals(providedKey, configuredApiKey, StringComparison.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Unauthorized");
            return;
        }

        await next();
    });
}

app.MapGet("/auth/ms", async (MicrosoftAuthService mas, CancellationToken ct) =>
    {
        try
        {
            var token = await mas.GetJwtAsync(ct);
            return Results.Json(new { jwt = token });
        }
        catch (MicrosoftAuthInProgressException)
        {
            return Results.StatusCode(StatusCodes.Status429TooManyRequests);
        }
        catch (Exception ex)
        {
            return Results.Problem(ex.Message, statusCode: 500);
        }
    })
    .WithName("GetMsJwt");

app.Run();