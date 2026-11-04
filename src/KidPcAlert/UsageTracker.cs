using System.Globalization;

namespace KidPcAlert;

/// <summary>켜짐/꺼짐 상태와 오늘 사용 시간. state.json 에 저장돼서 재부팅해도 이어져요.</summary>
public sealed class UsageState
{
    public DateTimeOffset? SessionStart { get; set; }
    public DateTimeOffset LastHeartbeat { get; set; }
    public DateOnly Day { get; set; }
    public double TodayMinutes { get; set; }
    public DateOnly? NightNoticeKey { get; set; }
    public List<string> SeenAccounts { get; set; } = new();
}

/// <summary>
/// 켜짐/꺼짐을 기록하고 보낼 메시지 문장을 만들어요.
/// 같은 일이 두 번 들어와도(예: 절전 해제 신호가 두 번) 메시지는 한 번만 나가요.
/// </summary>
public sealed class UsageTracker
{
    private static readonly CultureInfo Korean = new("ko-KR");
    private static readonly TimeSpan GapLimit = TimeSpan.FromMinutes(3);

    private readonly object _gate = new();
    private readonly UsageState _state = JsonStore.Load<UsageState>(Paths.State);

    private static AppConfig Config => JsonStore.Load<AppConfig>(Paths.Config);

    /// <summary>서비스가 시작될 때. 지난번에 꺼짐 기록 없이 끝났다면 그것부터 알려요.</summary>
    public List<string> Startup(DateTimeOffset now)
    {
        lock (_gate)
        {
            var messages = new List<string>();
            if (_state.SessionStart is not null)
            {
                AddIfAny(messages, OffCore(_state.LastHeartbeat, "비정상 종료, 마지막 확인 시각 기준"));
            }

            AddIfAny(messages, OnCore(now, "부팅"));
            return messages;
        }
    }

    public string? On(DateTimeOffset now, string reason)
    {
        lock (_gate)
        {
            return OnCore(now, reason);
        }
    }

    public string? Off(DateTimeOffset at, string reason)
    {
        lock (_gate)
        {
            return OffCore(at, reason);
        }
    }

    /// <summary>30초마다 불려요. 오래 소식이 없었다면 그동안 잠들어 있었던 것으로 봐요.</summary>
    public List<string> Heartbeat(DateTimeOffset now)
    {
        lock (_gate)
        {
            var messages = new List<string>();
            if (_state.SessionStart is null)
            {
                return messages;
            }

            if (now - _state.LastHeartbeat > GapLimit)
            {
                AddIfAny(messages, OffCore(_state.LastHeartbeat, "응답 없음, 절전 추정"));
                AddIfAny(messages, OnCore(now, "다시 켜짐"));
            }

            _state.LastHeartbeat = now;
            AddIfAny(messages, NightNotice(now));
            Save();
            return messages;
        }
    }

    public string? Logon(string? user, DateTimeOffset now)
    {
        var config = Config;
        if (user is null || config.KnownAccounts.Count == 0 ||
            config.KnownAccounts.Contains(user, StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        return $"[주의] [{config.PcName}] 낯선 계정 로그인\n계정: {user}\n시각: {Format(now)}";
    }

    public List<string> CheckAccounts(List<string> current, DateTimeOffset now)
    {
        lock (_gate)
        {
            var messages = new List<string>();
            if (current.Count == 0)
            {
                return messages;
            }

            if (_state.SeenAccounts.Count == 0)
            {
                _state.SeenAccounts = current;
                Save();
                return messages;
            }

            var pc = Config.PcName;
            foreach (var name in current.Where(n => !_state.SeenAccounts.Contains(n, StringComparer.OrdinalIgnoreCase)))
            {
                _state.SeenAccounts.Add(name);
                messages.Add($"[주의] [{pc}] 새 계정이 만들어졌어요\n계정: {name}\n확인 시각: {Format(now)}");
            }

            if (messages.Count > 0)
            {
                Save();
            }

            return messages;
        }
    }

    public string Stopped(DateTimeOffset now) =>
        $"[주의] [{Config.PcName}] 감지 프로그램이 중지됐어요\n시각: {Format(now)}";

    private string? OnCore(DateTimeOffset now, string reason)
    {
        if (_state.SessionStart is not null)
        {
            return null;
        }

        var config = Config;
        _state.SessionStart = now;
        _state.LastHeartbeat = now;
        var night = IsNight(now, config);
        if (night)
        {
            // 늦은 시간에 켜졌다는 알림을 이미 보냈으니 "아직 사용 중" 알림은 생략해요.
            _state.NightNoticeKey = NightKey(now, config);
        }

        Save();
        return $"{Prefix(night)}[{config.PcName}] 켜짐 ({reason})\n" +
               $"시각: {Format(now)}\n" +
               $"계정: {WindowsInfo.ActiveUser() ?? "로그인 전"}";
    }

    private string? OffCore(DateTimeOffset at, string reason)
    {
        if (_state.SessionStart is not DateTimeOffset start)
        {
            return null;
        }

        if (at < start)
        {
            at = start;
        }

        var config = Config;
        _state.TodayMinutes = TodayMinutesUntil(at, start);
        _state.Day = DateOnly.FromDateTime(at.Date);
        _state.SessionStart = null;
        Save();

        return $"{Prefix(IsNight(at, config))}[{config.PcName}] 꺼짐 ({reason})\n" +
               $"시각: {Format(at)}\n" +
               $"이번 사용: {Duration(at - start)}\n" +
               $"오늘 총 사용: {Duration(TimeSpan.FromMinutes(_state.TodayMinutes))}";
    }

    private string? NightNotice(DateTimeOffset now)
    {
        var config = Config;
        if (_state.SessionStart is not DateTimeOffset start || !IsNight(now, config))
        {
            return null;
        }

        var key = NightKey(now, config);
        if (_state.NightNoticeKey == key)
        {
            return null;
        }

        _state.NightNoticeKey = key;
        return $"[늦은 시간] [{config.PcName}] 아직 사용 중\n" +
               $"시각: {Format(now)}\n" +
               $"계정: {WindowsInfo.ActiveUser() ?? "로그인 전"}\n" +
               $"오늘 총 사용: {Duration(TimeSpan.FromMinutes(TodayMinutesUntil(now, start)))}";
    }

    /// <summary>오늘 끝난 사용 시간 + 지금 켜져 있는 시간 중 오늘 자정 이후 부분.</summary>
    private double TodayMinutesUntil(DateTimeOffset at, DateTimeOffset sessionStart)
    {
        var today = DateOnly.FromDateTime(at.Date);
        var finished = _state.Day == today ? _state.TodayMinutes : 0;
        var midnight = new DateTimeOffset(at.Date, at.Offset);
        var from = sessionStart > midnight ? sessionStart : midnight;
        return finished + Math.Max(0, (at - from).TotalMinutes);
    }

    private static bool IsNight(DateTimeOffset time, AppConfig config)
    {
        var start = ParseTime(config.NightStart, new TimeSpan(23, 20, 0));
        var end = ParseTime(config.NightEnd, new TimeSpan(6, 0, 0));
        var t = time.TimeOfDay;
        return start <= end ? t >= start && t < end : t >= start || t < end;
    }

    /// <summary>"어느 날 밤"인지. 새벽 1시는 전날 밤으로 쳐요.</summary>
    private static DateOnly NightKey(DateTimeOffset time, AppConfig config)
    {
        var end = ParseTime(config.NightEnd, new TimeSpan(6, 0, 0));
        var date = time.TimeOfDay < end ? time.Date.AddDays(-1) : time.Date;
        return DateOnly.FromDateTime(date);
    }

    private static TimeSpan ParseTime(string text, TimeSpan fallback) =>
        TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out var value) ? value : fallback;

    private static string Prefix(bool night) => night ? "[늦은 시간] " : "";

    private static string Format(DateTimeOffset time) => time.ToString("M월 d일(ddd) tt h:mm", Korean);

    private static string Duration(TimeSpan span) => $"{(int)span.TotalHours}시간 {span.Minutes}분";

    private static void AddIfAny(List<string> list, string? message)
    {
        if (message is not null)
        {
            list.Add(message);
        }
    }

    private void Save() => JsonStore.Save(Paths.State, _state);
}
