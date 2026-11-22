using System.Text.Json.Serialization;
using StarlightAI.Engine;

namespace StarlightAI;

public sealed record ExecuteRequest([property: JsonPropertyName("code")] string? Code);
public sealed record ExecuteResponse([property: JsonPropertyName("result")] string Result);

public static class Program
{
    // 無資料庫設計：僅於記憶體中保留最後一次執行後的結果
    private static string _lastResult = string.Empty;

    public static void Main(string[] args)
    {
        LoadDotEnvIfExists();

        var builder = WebApplication.CreateBuilder(args);
        builder.Logging.ClearProviders();

        string? portEnv = Environment.GetEnvironmentVariable("PORT");
        int port = int.TryParse(portEnv, out int parsedPort) ? parsedPort : 5000;
        builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

        builder.Services.AddSingleton<GeminiService>();

        var app = builder.Build();

        app.UseDefaultFiles();
        app.UseStaticFiles();

        app.MapGet("/api/execute", () =>
        {
            return Results.Json(new ExecuteResponse(_lastResult));
        });

        app.MapPost("/api/execute", async (HttpContext ctx, GeminiService gemini) =>
        {
            try
            {
                var req = await ctx.Request.ReadFromJsonAsync<ExecuteRequest>(ctx.RequestAborted);
                if (req == null || string.IsNullOrWhiteSpace(req.Code) || req.Code.Length > 131072)
                {
                    _lastResult = "✖";
                    return Results.Json(new ExecuteResponse(_lastResult));
                }

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ctx.RequestAborted);
                cts.CancelAfter(TimeSpan.FromSeconds(50));

                var runtime = new StarlightRuntime(gemini);
                string result = await runtime.ExecuteProgramAsync(req.Code, cts.Token);
                _lastResult = result;
                return Results.Json(new ExecuteResponse(_lastResult));
            }
            catch
            {
                // 嚴格禁止回傳任何 Debug 資訊、堆疊追蹤或內部錯誤訊息
                _lastResult = "✖";
                return Results.Json(new ExecuteResponse(_lastResult));
            }
        });

        Console.WriteLine($"[✦] 星輝服務已啟動 ▸ http://localhost:{port}");
        app.Run();
    }

    private static void LoadDotEnvIfExists()
    {
        string envPath = Path.Combine(Directory.GetCurrentDirectory(), ".env");
        if (!File.Exists(envPath))
            return;

        foreach (string rawLine in File.ReadAllLines(envPath))
        {
            string line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith('#'))
                continue;

            int eqIdx = line.IndexOf('=');
            if (eqIdx <= 0)
                continue;

            string key = line[..eqIdx].Trim();
            string val = line[(eqIdx + 1)..].Trim();

            if ((val.StartsWith('"') && val.EndsWith('"')) ||
                (val.StartsWith('\'') && val.EndsWith('\'')))
            {
                val = val[1..^1];
            }

            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
            {
                Environment.SetEnvironmentVariable(key, val);
            }
        }
    }
}
