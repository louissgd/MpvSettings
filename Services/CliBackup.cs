using System;
using System.Linq;

namespace MpvSettings.Services;

/// <summary>
/// 命令行入口（阶段 ⑩b，与 --default 并列、同样在建窗之前处理）：
/// <code>
/// MpvSettings.exe --export[=&lt;dir&gt;] [--config-dir=&lt;pc&gt;] [--include-extra]
/// MpvSettings.exe --import[=&lt;dir&gt;] [--config-dir=&lt;pc&gt;] [--dry-run]
///                 [--keep-registry] [--include-extra] [--no-snapshot]
/// </code>
/// 不写目录时取「记住的文件夹」，首次即桌面\MyMPC-MPV-备份。
/// 退出码沿用 --default：0 成功 / 1 写盘失败 / 2 清单无效或配置不可用。
/// </summary>
public static class CliBackup
{
    public static bool Wants(string[] args)
        => args.Any(a => Match(a, "--export", "-export") || Match(a, "--import", "-import"));

    private static bool Match(string arg, string longName, string shortName)
        => string.Equals(arg, longName, StringComparison.OrdinalIgnoreCase)
           || arg.StartsWith(longName + "=", StringComparison.OrdinalIgnoreCase)
           || string.Equals(arg, shortName, StringComparison.OrdinalIgnoreCase)
           || arg.StartsWith(shortName + "=", StringComparison.OrdinalIgnoreCase);

    /// <summary>取 --xxx= 后面的值；只写开关不带值时返回空串（走默认目录）。</summary>
    private static string Value(string[] args, string longName, string shortName)
    {
        foreach (string a in args)
        {
            if (a.StartsWith(longName + "=", StringComparison.OrdinalIgnoreCase))
                return a.Substring(longName.Length + 1).Trim().Trim('"');
            if (a.StartsWith(shortName + "=", StringComparison.OrdinalIgnoreCase))
                return a.Substring(shortName.Length + 1).Trim().Trim('"');
        }
        return string.Empty;
    }

    private static bool Has(string[] args, string flag)
        => args.Any(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));

    public static int Run(string[] args)
    {
        CliDefault.AttachOutput();

        bool exp = args.Any(a => Match(a, "--export", "-export"));
        bool imp = args.Any(a => Match(a, "--import", "-import"));
        if (exp && imp)
        {
            Console.Error.WriteLine("MPV 简单设置：--export 与 --import 不能同时给");
            return 2;
        }

        string verb = exp ? "导出配置" : "导入配置";
        Console.WriteLine("MPV 简单设置 · " + verb);

        var loc = ConfigLocator.Resolve(args);
        var o = new BackupService.Options
        {
            Dir = exp ? Value(args, "--export", "-export") : Value(args, "--import", "-import"),
            Locator = loc,
            IncludeExtra = Has(args, "--include-extra"),
            DryRun = Has(args, "--dry-run"),
            KeepRegistry = Has(args, "--keep-registry"),
            Snapshot = !Has(args, "--no-snapshot"),
        };

        if (!loc.IsUsable)
        {
            Console.Error.WriteLine("  配置不可用：" + loc.Reason);
            return 2;
        }
        Console.WriteLine("  配置目录：" + loc.ConfigDir);
        Console.WriteLine("  备份目录：" + BackupService.NormalizeDir(o.Dir));

        var r = exp ? BackupService.Export(o) : BackupService.Import(o);

        if (r.Plan is not null)
        {
            CliDefault.Dump(args, "--dump-plan=", r.Plan);
            if (o.DryRun) CliDefault.PrintPlan(r.Plan);
        }
        if (r.Write is not null) CliDefault.Dump(args, "--dump-result=", r.Write);

        foreach (string d in r.Details) Console.WriteLine("  " + d);
        if (!r.Success) Console.Error.WriteLine("  失败：" + r.Message);
        else Console.WriteLine("  " + r.Message);

        return r.Code;
    }
}
