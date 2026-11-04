using System.ServiceProcess;
using System.Text;
using KidPcAlert;

// 인자 없이 실행되면 Windows 서비스로 동작하고,
// "setup" 또는 "test"를 붙이면 설정용 검은 창으로 동작해요.
if (args.Length == 0)
{
    if (Environment.UserInteractive)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine("사용법: KidPcAlert.exe setup  (처음 설정, 카카오 로그인)");
        Console.WriteLine("        KidPcAlert.exe test   (카톡 테스트 메시지 보내기)");
        return 0;
    }

    ServiceBase.Run(new AlertService());
    return 0;
}

Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.UTF8;

return args[0].ToLowerInvariant() switch
{
    "setup" => await Setup.RunAsync(),
    "test" => await Setup.TestAsync(),
    _ => Usage(),
};

static int Usage()
{
    Console.WriteLine("알 수 없는 명령이에요. setup 또는 test 를 사용해 주세요.");
    return 1;
}
