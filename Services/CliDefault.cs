using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using MpvSettings.Models;

namespace MpvSettings.Services;

/// <summary>
/// 命令行入口：<c>MpvSettings.exe --default</c> 把全部设置恢复为出厂值后直接退出，不弹窗口。
///
/// 给谁用：
///   · setting.ahk 的「恢复 MPV 默认配置」——不必再依赖四个 *_default.conf 模板；
///   · 安装包首次配置 / 修复安装——装完就是默认态，不用另带一份配置；
///   · 用户出问题时手工跑一次，把配置拉回已知状态。
///
/// 别名：<c>-default</c>、<c>/default</c>（单横线更符合习惯，Windows 风格也认）。
/// </summary>
public static class CliDefault
{
    private static readonly string[] Flags = { "--default", "-default", "/default" };

    public static bool Wants(string[] args)
        => args.Any(a => Flags.Any(f => string.Equals(a, f, StringComparison.OrdinalIgnoreCase)));

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int processId);

    private const int AttachParentProcess = -1;

    /// <summary>WinExe 默认没有控制台；从 cmd / AHK 里跑时借用父进程的，否则输出会凭空消失。</summary>
    public static void AttachOutput()
    {
        try
        {
            if (!AttachConsole(AttachParentProcess)) return;
            var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };
            var stderr = new StreamWriter(Console.OpenStandardError(), new UTF8Encoding(false)) { AutoFlush = true };
            Console.SetOut(stdout);
            Console.SetError(stderr);
        }
        catch (Exception)
        {
            // 借不到控制台就算了：退出码才是给脚本用的接口
        }
    }

    public static int Run(string[] args)
    {
        AttachOutput();

        var loc = ConfigLocator.Resolve(args);
        if (!loc.IsUsable)
        {
            Console.Error.WriteLine("MPV 简单设置 · 恢复默认：失败");
            Console.Error.WriteLine("  " + loc.Reason);
            return 2;
        }

        Console.WriteLine("MPV 简单设置 · 恢复默认");
        Console.WriteLine("  配置目录：" + loc.ConfigDir);

        bool dryRun = args.Any(a => string.Equals(a, "--dry-run", StringComparison.OrdinalIgnoreCase));

        var snapshot = new ConfigSnapshot(loc);
        var plan = DefaultProfile.Build(snapshot);

        bool keepRegistry = args.Any(a => string.Equals(a, "--keep-registry", StringComparison.OrdinalIgnoreCase));
        if (keepRegistry) plan.Registry.Changes.Clear();

        Dump(args, "--dump-plan=", plan);

        if (dryRun)
        {
            Console.WriteLine("  （--dry-run：只算计划，不写盘）");
            PrintPlan(plan);
            return plan.CanApply ? 0 : 1;
        }

        var result = plan.CanApply ? ConfWriter.Apply(plan) : WriteResult.Blocked(plan);
        Dump(args, "--dump-result=", result);

        foreach (string b in result.Blockers) Console.Error.WriteLine("  阻止：" + b);
        foreach (string d in result.Details) Console.WriteLine("  " + d);
        foreach (string w in result.Warnings) Console.WriteLine("  提示：" + w);

        if (!result.Success)
        {
            Console.Error.WriteLine("  失败：" + result.Error);
            return 1;
        }

        // 菜单路径修正必须排在 Apply 之后：plan 里若出现 menu.conf 的整文件重写
        // （IsWholeFile），会把随包发的打包机路径刷回来，写在前面就白改了。
        // --dry-run 在前面已经 return，走到这里的都是真写盘。
        string menuNote = MenuConfPath.Fix(loc);
        if (menuNote is not null) Console.WriteLine("  " + menuNote);

        Console.WriteLine("  完成：" + (result.Summary.Length > 0 ? result.Summary : plan.Summary));
        return 0;
    }

    public static void PrintPlan(WritePlan plan)
    {
        foreach (var f in plan.Files)
        {
            if (f.IsWholeFile) Console.WriteLine($"  {f.Label}：整文件重写（{f.WholeFileKind}）");
            foreach (var c in f.Changes) Console.WriteLine($"  {f.Label} · {c.Key}  {c.DisplayFrom} → {c.DisplayTo}");
            foreach (var e in f.InputEdits) Console.WriteLine($"  {f.Label} · {e.Action}  {e.DisplayOld} → {e.DisplayNew}");
        }
        foreach (var r in plan.Registry.Changes) Console.WriteLine($"  注册表 · {r.Key}  {r.DisplayFrom} → {r.To}");
    }

    public static void Dump<T>(string[] args, string prefix, T value)
    {
        string path = string.Empty;
        foreach (string a in args)
            if (a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                path = a.Substring(prefix.Length).Trim().Trim('"');

        if (path.Length == 0) return;
        try
        {
            File.WriteAllText(path,
                JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }),
                new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("  写 " + path + " 失败：" + ex.Message);
        }
    }
}
