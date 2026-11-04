using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;

namespace KidPcAlert;

/// <summary>카카오톡 "나에게 보내기" API로 부모님 카카오톡에 메시지를 보내요.</summary>
public static class KakaoClient
{
    public const string RedirectUri = "http://localhost:8765/callback";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    public static string AuthorizeUrl(string restApiKey) =>
        "https://kauth.kakao.com/oauth/authorize" +
        $"?client_id={Uri.EscapeDataString(restApiKey)}" +
        $"&redirect_uri={Uri.EscapeDataString(RedirectUri)}" +
        "&response_type=code&scope=talk_message";

    public static async Task ExchangeCodeAsync(AppConfig config, string code)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = config.RestApiKey,
            ["redirect_uri"] = RedirectUri,
            ["code"] = code,
        };
        AddSecret(config, form);
        Apply(config, await PostTokenAsync(form));
    }

    public static async Task<bool> SendToMeAsync(AppConfig config, string text, CancellationToken ct = default)
    {
        if (config.AccessTokenExpiresAt < DateTimeOffset.Now.AddMinutes(5))
        {
            await RefreshAsync(config, ct);
        }

        var template = new JsonObject
        {
            ["object_type"] = "text",
            ["text"] = text,
            ["link"] = new JsonObject(),
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://kapi.kakao.com/v2/api/talk/memo/default/send")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["template_object"] = template.ToJsonString(),
            }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.AccessToken);

        using var response = await Http.SendAsync(request, ct);
        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        Log.Write($"카톡 발송 실패 {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(ct)}");
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            // 다음 시도에서 새 열쇠(토큰)를 받아오도록 만료 처리해요.
            config.AccessTokenExpiresAt = DateTimeOffset.MinValue;
            JsonStore.Save(Paths.Config, config);
        }

        return false;
    }

    private static async Task RefreshAsync(AppConfig config, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(config.RefreshToken))
        {
            throw new InvalidOperationException("카카오 로그인이 필요해요. KidPcAlert.exe setup 을 다시 실행해 주세요.");
        }

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = config.RestApiKey,
            ["refresh_token"] = config.RefreshToken,
        };
        AddSecret(config, form);
        Apply(config, await PostTokenAsync(form, ct));
        JsonStore.Save(Paths.Config, config);
        Log.Write("카카오 토큰 갱신 완료");
    }

    private static void AddSecret(AppConfig config, Dictionary<string, string> form)
    {
        if (!string.IsNullOrWhiteSpace(config.ClientSecret))
        {
            form["client_secret"] = config.ClientSecret;
        }
    }

    private static async Task<JsonObject> PostTokenAsync(Dictionary<string, string> form, CancellationToken ct = default)
    {
        using var response = await Http.PostAsync("https://kauth.kakao.com/oauth/token", new FormUrlEncodedContent(form), ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"카카오 토큰 요청 실패 ({(int)response.StatusCode}): {body}");
        }

        return JsonNode.Parse(body)!.AsObject();
    }

    private static void Apply(AppConfig config, JsonObject json)
    {
        config.AccessToken = json["access_token"]!.GetValue<string>();
        config.AccessTokenExpiresAt = DateTimeOffset.Now.AddSeconds(json["expires_in"]?.GetValue<int>() ?? 3600);

        // 리프레시 토큰은 만료가 가까울 때만 새로 내려와요.
        var refresh = json["refresh_token"]?.GetValue<string>();
        if (!string.IsNullOrEmpty(refresh))
        {
            config.RefreshToken = refresh;
        }
    }
}
