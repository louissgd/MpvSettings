using System;
using System.IO;
using System.Linq;

namespace MpvSettings.Services;

/// <summary>
/// mpv 着色器缓存（gpu-next / libplacebo 自动生成）的统计与清理。
///
/// 与 00.Core\setting.ahk 的 MpvCacheStat / MpvCachePurge 保持同一套语义，
/// 两边显示的数字必须一致，否则用户会以为是两个不同的东西：
///   - 目录：portable_config\cache
///   - 只碰 shader_*（编译后的 DXBC / SPIR-V）与 icc_*（ICC 3D LUT）两类文件
///   - 只扫一层，不递归（AHK 的 Loop, Files 就是非递归）
///   - 删掉只是下次播放重新编译，不改动任何设置
///
/// 别把整个 cache 目录删掉：mpv 自己会重建，但目录里可能有别的用途的文件。
/// </summary>
public static class ShaderCacheService
{
    private static readonly string[] Prefixes = { "shader_", "icc_" };

    public sealed record StatResult(int Files, long Bytes)
    {
        public double Megabytes => Bytes / 1048576.0;
        public bool IsEmpty => Files == 0;
    }

    public sealed record PurgeResult(int Deleted, int Failed, long FreedBytes);

    /// <summary>统计缓存文件数量与总体积。目录不存在时返回 0/0，不抛异常。</summary>
    public static StatResult Stat(string cacheDir)
    {
        if (string.IsNullOrEmpty(cacheDir) || !Directory.Exists(cacheDir))
            return new StatResult(0, 0);

        int count = 0;
        long bytes = 0;
        try
        {
            foreach (var path in EnumerateCacheFiles(cacheDir))
            {
                count++;
                try { bytes += new FileInfo(path).Length; }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        return new StatResult(count, bytes);
    }

    /// <summary>删除缓存文件。被 mpv 占用（正在播放）的文件会算进 Failed，不中断其余删除。</summary>
    public static PurgeResult Purge(string cacheDir)
    {
        if (string.IsNullOrEmpty(cacheDir) || !Directory.Exists(cacheDir))
            return new PurgeResult(0, 0, 0);

        int deleted = 0, failed = 0;
        long freed = 0;
        foreach (var path in EnumerateCacheFiles(cacheDir).ToList())
        {
            long size = 0;
            try { size = new FileInfo(path).Length; }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }

            try
            {
                File.Delete(path);
                deleted++;
                freed += size;
            }
            catch (IOException) { failed++; }              // 被 mpv 占用
            catch (UnauthorizedAccessException) { failed++; }
        }
        return new PurgeResult(deleted, failed, freed);
    }

    /// <summary>行副标题文案：标题已经是「着色器缓存」，这里不再重复前缀。</summary>
    public static string InfoText(StatResult r)
        => r.IsEmpty
            ? "空 —— 播放时自动生成，mpv 上限 128 MB，可随时清理"
            : string.Format("{0} 个文件 / {1:0.#} MB —— 播放时自动生成，可安全清理",
                            r.Files, r.Megabytes);

    private static System.Collections.Generic.IEnumerable<string> EnumerateCacheFiles(string dir)
    {
        // EnumerateFiles 只会返回直接子项，天然就是非递归的
        foreach (var prefix in Prefixes)
            foreach (var path in Directory.EnumerateFiles(dir, prefix + "*", SearchOption.TopDirectoryOnly))
                yield return path;
    }
}
