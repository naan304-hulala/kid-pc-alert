namespace KidPcAlert;

/// <summary>
/// 보낼 메시지를 먼저 기록장(outbox.json)에 적고, 보내는 데 성공하면 지워요.
/// 인터넷이 끊겨도 메시지가 사라지지 않고, 다음에 연결되면 순서대로 보내져요.
/// </summary>
public sealed class Notifier
{
    private readonly object _fileGate = new();
    private readonly SemaphoreSlim _sending = new(1, 1);

    public void Enqueue(string text)
    {
        lock (_fileGate)
        {
            var outbox = JsonStore.Load<List<string>>(Paths.Outbox);
            outbox.Add(text);
            JsonStore.Save(Paths.Outbox, outbox);
        }

        Log.Write($"알림 대기: {text.Replace('\n', ' ')}");
    }

    public async Task FlushAsync(TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await _sending.WaitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            var config = JsonStore.Load<AppConfig>(Paths.Config);
            while (!cts.IsCancellationRequested)
            {
                string next;
                lock (_fileGate)
                {
                    var outbox = JsonStore.Load<List<string>>(Paths.Outbox);
                    if (outbox.Count == 0)
                    {
                        return;
                    }

                    next = outbox[0];
                }

                bool sent;
                try
                {
                    sent = await KakaoClient.SendToMeAsync(config, next, cts.Token);
                }
                catch (Exception e)
                {
                    Log.Write($"발송 오류: {e.Message}");
                    sent = false;
                }

                if (!sent)
                {
                    return;
                }

                lock (_fileGate)
                {
                    var outbox = JsonStore.Load<List<string>>(Paths.Outbox);
                    if (outbox.Count > 0 && outbox[0] == next)
                    {
                        outbox.RemoveAt(0);
                        JsonStore.Save(Paths.Outbox, outbox);
                    }
                }
            }
        }
        finally
        {
            _sending.Release();
        }
    }
}
