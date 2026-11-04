using System.Management;
using System.Runtime.InteropServices;

namespace KidPcAlert;

/// <summary>지금 로그인한 계정 이름과 컴퓨터의 계정 목록을 알아내요.</summary>
public static class WindowsInfo
{
    private const int WtsUserName = 5;
    private const uint NoSession = 0xFFFFFFFF;

    [DllImport("wtsapi32.dll", EntryPoint = "WTSQuerySessionInformationW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool WTSQuerySessionInformation(IntPtr server, int sessionId, int infoClass, out IntPtr buffer, out int bytes);

    [DllImport("wtsapi32.dll")]
    private static extern void WTSFreeMemory(IntPtr memory);

    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();

    public static string? SessionUser(int sessionId)
    {
        if (!WTSQuerySessionInformation(IntPtr.Zero, sessionId, WtsUserName, out var buffer, out _))
        {
            return null;
        }

        try
        {
            var name = Marshal.PtrToStringUni(buffer);
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
        finally
        {
            WTSFreeMemory(buffer);
        }
    }

    public static string? ActiveUser()
    {
        var id = WTSGetActiveConsoleSessionId();
        return id == NoSession ? null : SessionUser((int)id);
    }

    public static List<string> LocalAccounts()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name FROM Win32_UserAccount WHERE LocalAccount = True AND Disabled = False");
            return searcher.Get()
                .Cast<ManagementObject>()
                .Select(o => o["Name"]?.ToString() ?? "")
                .Where(n => n.Length > 0)
                .ToList();
        }
        catch (Exception e)
        {
            Log.Write($"계정 목록 확인 실패: {e.Message}");
            return new List<string>();
        }
    }
}
