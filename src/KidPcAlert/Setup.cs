using System.Diagnostics;
using System.Net;
using System.Text;

namespace KidPcAlert;

/// <summary>설치할 때 한 번 실행하는 설정 화면(검은 창). 카카오 로그인까지 여기서 해요.</summary>
public static class Setup
{
    public static async Task<int> RunAsync()
    {
        var config = JsonStore.Load<AppConfig>(Paths.Config);

        Console.WriteLine("=== 아이 컴퓨터 알림 설정 ===");
        Console.WriteLine("[ ] 안의 값이 기본값이에요. 그대로 쓰려면 Enter만 누르세요.");
        Console.WriteLine();

        config.PcName = Ask("이 컴퓨터 이름 (예: 큰애 PC)", config.PcName);
        config.RestApiKey = Ask("카카오 REST API 키", config.RestApiKey);
        var secret = Ask("Client Secret (사용 안 하면 Enter)", config.ClientSecret ?? "");
        config.ClientSecret = string.IsNullOrWhiteSpace(secret) ? null : secret;

        var local = WindowsInfo.LocalAccounts();
        Console.WriteLine($"이 컴퓨터의 계정: {string.Join(", ", local)}");
        var knownDefault = string.Join(", ", config.KnownAccounts.Count > 0 ? config.KnownAccounts : local);
        config.KnownAccounts = Ask("평소 쓰는 계정 (쉼표로 구분, 이 외 계정은 '낯선 계정'으로 알려요)", knownDefault)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        config.NightStart = AskTime("늦은 시간 시작 (시:분)", config.NightStart);
        config.NightEnd = AskTime("늦은 시간 끝 (시:분)", config.NightEnd);

        if (string.IsNullOrWhiteSpace(config.RestApiKey))
        {
            Console.WriteLine("REST API 키가 없으면 카톡을 보낼 수 없어요. 다시 실행해 주세요.");
            return 1;
        }

        JsonStore.Save(Paths.Config, config);

        Console.WriteLine();
        Console.WriteLine("브라우저가 열리면 부모님 카카오 계정으로 로그인하고 '동의하고 계속하기'를 눌러 주세요.");
        var code = await WaitForLoginAsync(config.RestApiKey);
        if (code is null)
        {
            return 1;
        }

        try
        {
            await KakaoClient.ExchangeCodeAsync(config, code);
            JsonStore.Save(Paths.Config, config);
        }
        catch (Exception e)
        {
            Console.WriteLine($"카카오 연결 실패: {e.Message}");
            return 1;
        }

        return await SendTestAsync(config, "설정 완료! 이 메시지가 보이면 성공이에요.");
    }

    public static async Task<int> TestAsync()
    {
        var config = JsonStore.Load<AppConfig>(Paths.Config);
        return await SendTestAsync(config, "테스트 메시지예요.");
    }

    private static async Task<int> SendTestAsync(AppConfig config, string text)
    {
        try
        {
            if (await KakaoClient.SendToMeAsync(config, $"[{config.PcName}] {text}"))
            {
                Console.WriteLine("카카오톡 '나와의 채팅'으로 메시지를 보냈어요. 휴대폰을 확인해 보세요!");
                return 0;
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"오류: {e.Message}");
        }

        Console.WriteLine($"보내지 못했어요. 자세한 내용: {Paths.Log}");
        return 1;
    }

    private static async Task<string?> WaitForLoginAsync(string restApiKey)
    {
        using var listener = new HttpListener();
        listener.Prefixes.Add("http://localhost:8765/");
        listener.Start();

        Process.Start(new ProcessStartInfo(KakaoClient.AuthorizeUrl(restApiKey)) { UseShellExecute = true });

        var contextTask = listener.GetContextAsync();
        if (await Task.WhenAny(contextTask, Task.Delay(TimeSpan.FromMinutes(5))) != contextTask)
        {
            Console.WriteLine("5분 안에 로그인이 끝나지 않았어요. 다시 실행해 주세요.");
            return null;
        }

        var context = await contextTask;
        var code = context.Request.QueryString["code"];
        var error = context.Request.QueryString["error_description"] ?? context.Request.QueryString["error"];
        var page = string.IsNullOrEmpty(code)
            ? $"<h2>연결 실패: {WebUtility.HtmlEncode(error)}</h2>"
            : "<h2>연결 완료! 이 창은 닫아도 돼요.</h2>";
        var bytes = Encoding.UTF8.GetBytes($"<!doctype html><meta charset='utf-8'>{page}");
        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.OutputStream.WriteAsync(bytes);
        context.Response.Close();

        if (string.IsNullOrEmpty(code))
        {
            Console.WriteLine($"카카오 로그인 실패: {error}");
            return null;
        }

        return code;
    }

    private static string Ask(string prompt, string current)
    {
        Console.Write($"{prompt} [{current}]: ");
        var input = Console.ReadLine();
        return string.IsNullOrWhiteSpace(input) ? current : input.Trim();
    }

    private static string AskTime(string prompt, string current)
    {
        while (true)
        {
            var value = Ask(prompt, current);
            if (TimeSpan.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out _))
            {
                return value;
            }

            Console.WriteLine("23:20 처럼 시:분 형식으로 입력해 주세요.");
        }
    }
}
