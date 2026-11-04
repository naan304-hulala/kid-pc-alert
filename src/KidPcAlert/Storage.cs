using System.Text.Encodings.Web;
using System.Text.Json;

namespace KidPcAlert;

/// <summary>설정값. C:\ProgramData\KidPcAlert\config.json 에 저장되고 관리자만 읽을 수 있어요.</summary>
public sealed class AppConfig
{
    public string PcName { get; set; } = "아이 PC";
    public string RestApiKey { get; set; } = "";
    public string? ClientSecret { get; set; }
    public string AccessToken { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public DateTimeOffset AccessTokenExpiresAt { get; set; }
    public List<string> KnownAccounts { get; set; } = new();
    public string NightStart { get; set; } = "23:20";
    public string NightEnd { get; set; } = "06:00";
}

public static class Paths
{
    public static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "KidPcAlert");

    public static string Config => Path.Combine(Dir, "config.json");
    public static string State => Path.Combine(Dir, "state.json");
    public static string Outbox => Path.Combine(Dir, "outbox.json");
    public static string Log => Path.Combine(Dir, "log.txt");
}

public static class JsonStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly object Gate = new();

    public static T Load<T>(string path) where T : new()
    {
        lock (Gate)
        {
            try
            {
                if (File.Exists(path))
                {
                    return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) ?? new T();
                }
            }
            catch (Exception e)
            {
                Log.Write($"파일 읽기 실패 {path}: {e.Message}");
            }

            return new T();
        }
    }

    public static void Save<T>(string path, T value)
    {
        lock (Gate)
        {
            Directory.CreateDirectory(Paths.Dir);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(value, Options));
            File.Move(temp, path, overwrite: true);
        }
    }
}

public static class Log
{
    private static readonly object Gate = new();

    public static void Write(string message)
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(Paths.Dir);
                var file = Paths.Log;
                if (File.Exists(file) && new FileInfo(file).Length > 1_000_000)
                {
                    File.Delete(file);
                }

                File.AppendAllText(file, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
            }
            catch
            {
                // 기록 실패는 무시해요. 알림 동작이 더 중요해요.
            }
        }
    }
}
