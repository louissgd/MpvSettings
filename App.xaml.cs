using System;
using System.Linq;
using System.Windows;

namespace MpvSettings;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // --default：恢复默认，无窗口、无对话框，退出码即结果（0 成功 / 1 写入失败 / 2 配置不可用）。
        // 放在建窗之前——这条路一根 WPF 控件都不需要，setting.ahk 与安装包只认退出码。
        if (Services.CliDefault.Wants(e.Args))
        {
            int code = Services.CliDefault.Run(e.Args);
            Shutdown(code);
            return;
        }

        // --export / --import：与 --default 并列，同样无窗口、退出码即结果。
        // setting.ahk / 安装包以后要接单行命令时直接用这两个开关。
        if (Services.CliBackup.Wants(e.Args))
        {
            int code = Services.CliBackup.Run(e.Args);
            Shutdown(code);
            return;
        }

        // 手动建窗：--dump-only / --apply-only / --plan-only 都不弹窗口，
        // 便于自动化断言，也不会在用户桌面上闪一下。
        var window = new MainWindow();
        bool cli = e.Args.Any(a =>
            string.Equals(a, "--dump-only", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a, "--apply-only", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a, "--plan-only", StringComparison.OrdinalIgnoreCase));
        if (cli)
        {
            bool act = e.Args.Any(a =>
                string.Equals(a, "--apply-only", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a, "--plan-only", StringComparison.OrdinalIgnoreCase));
            if (act) window.RunCli(e.Args);   // 纯 --dump-only 保持阶段 ② 的只读行为
            Shutdown();
            return;
        }
        window.Show();
    }
}
