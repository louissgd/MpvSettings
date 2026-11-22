# 1.0.3 色调修复参数 → ffmpeg 压片可行性复核

> 2026-11-20 复核。目的：重新理清 1.0.3 的色调档位，确认能否搬去 `<ffmpeg目录>/ffmpeg.exe`（N-126826-gc0e8b139fd，2026-09-24 nightly，`--enable-libplacebo`）压片。
>
> **结论先行：参数换算能算出来，但不能照搬。本机实测 libplacebo 的 tone-mapping LUT 建不起来，1.0.3 的提亮主力（inverse-tone-mapping）在 ffmpeg 里不生效——0.6 档输出和关闭档逐字节相同。只有 contrast 这类 equalizer 微调生效。**

---

## 一、1.0.3 色调档位（以 11-19 重构定稿为准）

1.0.2 修改记录里有两代值，**第九节的 1/4/10/17 已作废**，别再用：

| 代次 | 位置 | 值 | 状态 |
|---|---|---|---|
| 旧（11-18） | 记录第九节 | contrast 1/4/10/17（对应 0.5/0.6/0.8/1.0），brightness 0 | ❌ 11-19 证伪：截图链路不可靠，全部作废 |
| **新（11-19）** | 记录第十一节 11.3 | 见下表 | ✅ 1.0.3 最终交付 |

最终四档（峰值 2000nits / DisplayHDR 1400 显示器上人眼标定）：

| 档位 | tone-mapping | inverse-tone-mapping | contrast | brightness | saturation | spline_contrast |
|---|---|---|---|---|---|---|
| 0 关闭（默认） | clip | no | 0 | 0 | 0 | 0.5 |
| 1 → 0.6 | spline | yes | **0**（纯 inverse 基线） | 0 | 0 | 0.5 |
| 2 → 0.8 | spline | yes | **6** | 0 | 0 | 0.5 |
| 3 → 1.0 | spline | yes | **12** | 0 | 0 | 0.5 |

源码结论（记录 11.2）：**提亮主力是 `inverse-tone-mapping`**，它解除 libplacebo "输出峰值不得超过源峰值"的限制，让 spline 进入逆映射分支把 PQ 朝显示器峰值拉伸；`contrast` 只是 equalizer 梯度微调，在逆映射下只进 slope 指数 `(1-sc)*0.2`，**不是**强度旋钮。

验证脚本 `_verify/tone_tiers_103_verify.py` 的 18/18 断言就是这四组 `inverse/contrast` 组合。

---

## 二、参数换算（mpv → ffmpeg libplacebo）

mpv 的 `contrast/brightness/saturation` 是 video equalizer（`-100..100`，默认 0，加法式）；ffmpeg libplacebo 是倍数（contrast/saturation 默认 1，范围 `0..16`；brightness 默认 0，范围 `-1..1`）。

换算来自 mpv 源码 `csputils.c: mp_csp_copy_equalizer_values`：

```
ffmpeg.contrast    = (mpv.contrast    + 100) / 100
ffmpeg.brightness  =  mpv.brightness / 100
ffmpeg.saturation  = (mpv.saturation  + 100) / 100
```

| 档位 | ffmpeg 滤镜参数（等价写法） |
|---|---|
| 关闭 | `tonemapping=clip:inverse_tonemapping=0:contrast=1:brightness=0:saturation=1` |
| 0.6 | `tonemapping=spline:inverse_tonemapping=1:contrast=1:brightness=0:saturation=1` |
| 0.8 | `tonemapping=spline:inverse_tonemapping=1:contrast=1.06:brightness=0:saturation=1` |
| 1.0 | `tonemapping=spline:inverse_tonemapping=1:contrast=1.12:brightness=0:saturation=1` |

枚举名已核对本机 ffmpeg：`tonemapping` 有 `clip`(1)、`spline`(6)、`bt.2390`(4)、`reinhard`、`hable`、`mobius` 等；`inverse_tonemapping` 是 boolean。参数名全部对得上，**语法层面完全合法**。

---

## 三、本机实测：滤镜能跑，但 tone mapping 没生效

测试片源：`<测试视频目录>/Luis Fonsi & Demi Lovato - Échame La Culpa.4K.HEVC.HDR10.A.mkv`（HEVC Main10，3840×2160，`bt2020nc/bt2020/smpte2084`，真 HDR10），取第 3 秒起 1 秒。

### 3.1 默认的 libplacebo 路径直接失败

```
-vf "libplacebo=tonemapping=spline:inverse_tonemapping=1:contrast=1.12"
→ Allocation of size 453K failed: VK_ERROR_OUT_OF_DEVICE_MEMORY
```

不是显存不够：`nvidia-smi` 报 RTX 5070 12G 中空闲 9.6G，libplacebo 自己打印的 heap 0 是 11G 空闲。**连 453K / 252K / 16K 都分配失败**，是 Vulkan allocator 的问题，跟 11-18 记录里"显存不足"的判断不同（那次也是同样的报错，根因应归结为 allocator 而非容量）。

试过且无效：
- 缩小 LUT（`tonemapping_lut_size=32` / `=2`）——照样 OOM
- 关 peak detect / contrast recovery / dither——照样 OOM
- CUDA hwaccel + `inherit_device=1`——格式转换失败

### 3.2 唯一能跑通的组合：显式 Vulkan hwdevice

```
-init_hw_device vulkan=vk0 -filter_hw_device vk0 \
-vf "hwupload,libplacebo=inherit_device=1:tonemapping=spline:inverse_tonemapping=1:contrast=1.12,hwdownload,format=yuv420p10le"
```

能出满 24 帧。但日志里仍有 `Failed creating LUT texture!`——LUT 没建起来。

### 3.3 决定性证据：输出帧 md5

同一片源同一帧，用 3.2 的命令跑三档，取 framemd5：

| 档位 | 第 1 帧 md5 | 第 2 帧 md5 | 第 3 帧 md5 |
|---|---|---|---|
| 关闭（clip/no/1.0） | `24ec1783...` | `1c7af94d...` | `752946a9...` |
| **0.6（spline/yes/1.0）** | `24ec1783...` | `1c7af94d...` | `752946a9...` |
| 1.0（spline/yes/1.12） | `24ec1783...` | `937becb3...` | `70780085...` |

**关闭档和 0.6 档逐字节完全相同。** 0.6 档的全部效果就是 `inverse-tone-mapping`，LUT 建不起来 → 逆映射没执行 → 等于没开。1.0 档有差异，但差异只来自 `contrast=1.12` 这个 equalizer（不需要 LUT，直接走 shader）。

结论：**ffmpeg 这边实际只有 equalizer（contrast/brightness/saturation）生效，spline 和 inverse 都不生效。** 而 1.0.3 的设计恰恰是"提亮靠 inverse、contrast 只是微调"——搬过去等于把主力丢掉，只留下微调。

---

## 四、即便 LUT 能建起来，也不该照搬

这是记录第九节 9.2 就点明、第十一节进一步确认的设计前提，不是本机环境问题：

1. **作用阶段不同**：mpv 是显示端实时调整（gpu-next 在 PQ 域、真直通输出给 HDR 显示器前），依赖**显示器实际峰值亮度（2000nits）**。压片是把效果烘焙进码流（scene-referred），换显示器/SDR 环境效果就变。
2. **1.0.3 档位是显示器的观感标定**，不是片源级的画面增强。烘焙进文件等于把一个显示器的偏好强加给所有播放环境。
3. **真直通下这套参数本来就该禁用**：mpv 里切到 HDR→SDR 模式时色调档位自动置灰，因为 spline/inverse 只在特定路径生效。

---

## 五、可落地的做法

### 方案 A：只借用 equalizer 微调（能跑，但效果远小于 1.0.3）

如果确实只想要一点点对比度提升，用显式 Vulkan hwdevice 路径，只写 equalizer：

```
ffmpeg -init_hw_device vulkan=vk0 -filter_hw_device vk0 -i 输入 ^
  -vf "hwupload,libplacebo=inherit_device=1:contrast=1.12:brightness=0:saturation=1,hwdownload,format=yuv420p10le" ^
  -c:v libx265 -crf 19 -pix_fmt yuv420p10le 输出.mkv
```

`contrast` 范围 0..16，`1.12` 对应 mpv 的 12。这不是 1.0.3 的 1.0 档，只是其中的微调部分。

### 方案 B：真要 HDR→SDR 压片，用 CPU 滤镜（等价算法不存在）

ffmpeg 的 CPU `tonemap` 滤镜只有 `none/linear/gamma/clip/reinhard/hable/mobius`，**没有 spline，也没有 inverse**，所以无法复现 1.0.3。常规做法：

```
-vf "zscale=t=linear:npl=100,tonemap=tonemap=hable:desat=0,zscale=t=bt709:m=bt709:p=bt709:r=tv,format=yuv420p10le"
```

这是通用 HDR→SDR，不是 1.0.3 的档位。

### 方案 C：用 AviSynth+ 那条线

你项目里本来就有 AviSynth+ 色调修复页（profile 5 不支持那套）。ffmpeg 带 `--enable-avisynth`，可以 `-vf avisynth=脚本.avs`。如果想把观感标定固化，走这条线比硬搬 mpv 的 gpu-next 参数合理。

### 不推荐

在 `libplacebo` 上继续死磕 LUT OOM。已经试过 LUT 缩到 2、关 peak detect、换 CUDA / d3d11va / 显式 Vulkan 设备，都不解决问题。这是这个 ffmpeg nightly + 当前驱动的组合问题，不是参数写法问题。

---

## 六、一句话总结

1.0.3 的四档值是 `inverse + spline + contrast`，换算成 ffmpeg 参数语法上没问题（`1.0 / 1.06 / 1.12` + `tonemapping=spline` + `inverse_tonemapping=1`）。
但本机 ffmpeg 的 libplacebo LUT 建不起来，实测 0.6 档与关闭档输出完全相同——**提亮主力失效，只剩 contrast 微调**。加上"显示端观感标定不该烘焙进文件"这个设计前提，结论是：**不能用于压片，别搬。**
