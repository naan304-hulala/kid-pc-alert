using System.ServiceProcess;

namespace KidPcAlert;

/// <summary>
/// 화면 없이 뒤에서 도는 Windows 서비스(숨은 경비원).
/// 어떤 계정으로 로그인하든 컴퓨터 전체를 지켜요.
/// </summary>
public sealed class AlertService : ServiceBase
{
    private static readonly TimeSpan QuickSend = TimeSpan.FromSeconds(6);

    private readonly UsageTracker _tracker = new();
    private readonly Notifier _notifier = new();
    private CancellationTokenSource? _cts;

    public AlertService()
    {
        ServiceName = "KidPcAlert";
        CanHandlePowerEvent = true;
        CanHandleSessionChangeEvent = true;
        CanShutdown = true;
        CanStop = true;
        AutoLog = false;
    }

    protected override void OnStart(string[] args)
    {
        Log.Write("서비스 시작");
        _cts = new CancellationTokenSource();
        foreach (var message in _tracker.Startup(DateTimeOffset.Now))
        {
            _notifier.Enqueue(message);
        }

        _ = Task.Run(() => LoopAsync(_cts.Token));
    }

    protected override void OnShutdown()
    {
        Log.Write("윈도우 종료");
        SendNow(_tracker.Off(DateTimeOffset.Now, "종료"));
        _cts?.Cancel();
    }

    protected override void OnStop()
    {
        Log.Write("서비스 중지");
        var now = DateTimeOffset.Now;
        _notifier.Enqueue(_tracker.Stopped(now));
        SendNow(_tracker.Off(now, "감지 중지"));
        _cts?.Cancel();
    }

    protected override bool OnPowerEvent(PowerBroadcastStatus powerStatus)
    {
        Log.Write($"전원 이벤트: {powerStatus}");
        switch (powerStatus)
        {
            case PowerBroadcastStatus.Suspend:
                SendNow(_tracker.Off(DateTimeOffset.Now, "절전"));
                break;
            case PowerBroadcastStatus.ResumeSuspend:
            case PowerBroadcastStatus.ResumeAutomatic:
                var on = _tracker.On(DateTimeOffset.Now, "절전 해제");
                if (on is not null)
                {
                    _notifier.Enqueue(on);
                    // 와이파이가 다시 붙을 시간을 주고 보내요. 실패하면 30초마다 다시 시도해요.
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(TimeSpan.FromSeconds(10));
                        await _notifier.FlushAsync(TimeSpan.FromSeconds(30));
                    });
                }

                break;
        }

        return true;
    }

    protected override void OnSessionChange(SessionChangeDescription changeDescription)
    {
        if (changeDescription.Reason != SessionChangeReason.SessionLogon)
        {
            return;
        }

        var user = WindowsInfo.SessionUser(changeDescription.SessionId);
        Log.Write($"로그인: {user}");
        var message = _tracker.Logon(user, DateTimeOffset.Now);
        if (message is not null)
        {
            _notifier.Enqueue(message);
            _ = _notifier.FlushAsync(TimeSpan.FromSeconds(30));
        }
    }

    private void SendNow(string? message)
    {
        if (message is not null)
        {
            _notifier.Enqueue(message);
        }

        try
        {
            // 꺼지기 직전이라 오래 기다릴 수 없어요. 못 보내면 다음에 켜질 때 보내요.
            _notifier.FlushAsync(QuickSend).Wait(QuickSend + TimeSpan.FromSeconds(1));
        }
        catch (Exception e)
        {
            Log.Write($"즉시 발송 실패: {e.Message}");
        }
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        var lastAccountCheck = DateTimeOffset.MinValue;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var now = DateTimeOffset.Now;
                foreach (var message in _tracker.Heartbeat(now))
                {
                    _notifier.Enqueue(message);
                }

                if (now - lastAccountCheck > TimeSpan.FromMinutes(10))
                {
                    lastAccountCheck = now;
                    foreach (var message in _tracker.CheckAccounts(WindowsInfo.LocalAccounts(), now))
                    {
                        _notifier.Enqueue(message);
                    }
                }

                await _notifier.FlushAsync(TimeSpan.FromSeconds(25));
            }
            catch (Exception e)
            {
                Log.Write($"반복 작업 오류: {e}");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
