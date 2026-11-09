// 提醒提示音：首次播放时用正弦波合成一段两遍“叮咚”的 WAV，再用 SoundPlayer 播放。
// 调用方：ClockWindow.ShowReminder 在“提醒声音”开关打开时调用 <see cref="Play"/>，
// AppController.Dispose 退出时调用 <see cref="DisposePlayer"/> 释放播放器与内存流。
// 约束：不落盘、不读资源文件、不依赖系统提示音；合成结果缓存在单例播放器里，之后每次只重复播放。
// 失败策略：音频设备被占用或系统不支持时静默放弃，绝不把异常抛给提醒界面。

using System;
using System.Collections.Generic;
using System.IO;
using System.Media;

namespace GameClock
{

    /// <summary>
    /// 提示音播放器单例：内部持有一个 <see cref="SoundPlayer"/> 与一段内存 WAV。
    /// 第一次 <see cref="Play"/> 时合成音频并 <c>Load</c> 到内存，之后复用同一个实例；
    /// 所有入口都上锁，允许从任意线程调用（提醒轮询在 UI 线程，锁只防重入合成）。
    /// </summary>
    internal static class AlarmSound
    {
        // ===== 播放器状态 =====

        /// <summary>播放器与合成过程的互斥锁；防止两次快速提醒同时合成 WAV 并覆盖 <see cref="_player"/>。</summary>
        private static readonly object Sync = new object();

        /// <summary>缓存的播放器；null 表示尚未合成，或已被 <see cref="DisposePlayer"/> 释放。</summary>
        private static SoundPlayer _player;

        // ===== 对外入口 =====

        /// <summary>
        /// 播放一次提示音：没有播放器就先合成并加载，再调用 <c>Play</c>（异步，不阻塞调用方）。
        /// 首次调用会有一次合成 + 加载的开销（几十毫秒），之后只走播放分支；
        /// 任何异常（无音频设备、WAV 不被支持等）都在这里吞掉，声音失败不应影响气泡显示。
        /// </summary>
        public static void Play()
        {
            try
            {
                // 整段加锁：合成、加载、播放三步必须串行，避免并发提醒把 _player 换掉。
                lock (Sync)
                {
                    // 懒初始化：只有真正需要声音时才合成，省掉不用声音用户的 CPU 与内存。
                    if (_player == null)
                    {
                        _player = new SoundPlayer(BuildWave());
                        // 先 Load 到内存，避免首次 Play 时边读流边播造成开头被截断。
                        _player.Load();
                    }

                    _player.Play();
                }
            }
            catch (Exception)
            {
                // 静音策略：音频子系统异常只影响声音，提醒仍需照常显示，因此这里不记录也不重试。
            }
        }

        /// <summary>
        /// 释放播放器并清空缓存，进程退出时由 <see cref="AppController.Dispose"/> 调用。
        /// 置 null 是为了让后续 <see cref="Play"/> 能重新合成，而不是拿着已释放的实例去播。
        /// </summary>
        public static void DisposePlayer()
        {
            lock (Sync)
            {
                if (_player != null)
                {
                    // 释放本身也可能抛（设备已移除），吞掉以免拖垮退出流程。
                    try
                    {
                        _player.Dispose();
                    }
                    catch (Exception)
                    {
                    }

                    // 无论释放是否成功都清空引用，避免下次播放访问已释放对象。
                    _player = null;
                }
            }
        }

        // ===== 音频合成 =====

        /// <summary>
        /// 在内存里合成一段 16 位单声道 44.1kHz 的 WAV：
        /// “880Hz 短音 → 静音 → 1174.7Hz 短音”，整体重复两遍，音高关系接近小三度，听起来像门铃。
        /// 返回定位在 0 的 <see cref="MemoryStream"/>，由 <see cref="SoundPlayer"/> 负责读取与释放。
        /// </summary>
        private static Stream BuildWave()
        {
            // 采样率固定 44.1kHz：常见声卡都原生支持，避免重采样带来的杂音。
            const int sampleRate = 44100;
            List<short> samples = new List<short>();

            // 两遍“高-低”音型，第二遍前留更长的间隔，形成“叮咚……叮咚”的节奏感。
            AddTone(samples, sampleRate, 880.0, 0.16);
            AddSilence(samples, sampleRate, 0.05);
            AddTone(samples, sampleRate, 1174.7, 0.16);
            AddSilence(samples, sampleRate, 0.08);
            AddTone(samples, sampleRate, 880.0, 0.16);
            AddSilence(samples, sampleRate, 0.05);
            AddTone(samples, sampleRate, 1174.7, 0.16);
            AddSilence(samples, sampleRate, 0.10);

            // 每个采样 2 字节；WAV 头固定 44 字节，先按总长度开流，避免扩容拷贝。
            int dataSize = samples.Count * sizeof(short);
            MemoryStream stream = new MemoryStream(44 + dataSize);
            BinaryWriter writer = new BinaryWriter(stream);

            // 下面按 RIFF/WAVE 规范手写 44 字节头：块标识、大小、格式块、数据块。
            writer.Write(new char[] { 'R', 'I', 'F', 'F' });
            writer.Write(36 + dataSize);
            writer.Write(new char[] { 'W', 'A', 'V', 'E' });
            writer.Write(new char[] { 'f', 'm', 't', ' ' });
            writer.Write(16);
            // 音频格式 1 = PCM；声道 1 = 单声道，提示音不需要立体声。
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(sampleRate);
            // 字节率 = 采样率 × 声道数 × 位深/8 = 44100 × 1 × 2。
            writer.Write(sampleRate * 2);
            // 块对齐 = 声道数 × 位深/8；位深 16 bit。
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write(new char[] { 'd', 'a', 't', 'a' });
            writer.Write(dataSize);

            // 逐个写入 PCM 采样；short 小端序，与 WAV 要求一致。
            for (int i = 0; i < samples.Count; i++)
            {
                writer.Write(samples[i]);
            }

            writer.Flush();
            // 回到流开头，交给 SoundPlayer 时才能从头读。
            stream.Position = 0;
            return stream;
        }

        /// <summary>
        /// 追加一段正弦音：按 <paramref name="seconds"/> 计算采样数，并在首尾各加约 6ms 的线性淡入淡出。
        /// 加包络是为了消除波形突然开始时“咔哒”声；振幅只取满量程的 24%，留足余量避免削波。
        /// </summary>
        private static void AddTone(List<short> samples, int sampleRate, double frequency, double seconds)
        {
            // 至少 1 个采样，防止极短时长算出 0 导致空段。
            int count = Math.Max(1, (int)Math.Round(sampleRate * seconds));
            // 淡入淡出长度约 6ms：足够盖住爆音，又不至于让音头明显变软。
            int fade = Math.Max(1, (int)(sampleRate * 0.006));

            for (int i = 0; i < count; i++)
            {
                double envelope = 1.0;

                // 淡入段：第 i 个采样按 i/fade 线性放大，起点为 0。
                if (i < fade)
                {
                    envelope = (double)i / fade;
                }

                // 淡出段：距结尾不足 fade 时按剩余采样数线性减小，取两者较小值。
                int remaining = count - i;
                if (remaining < fade)
                {
                    envelope = Math.Min(envelope, (double)remaining / fade);
                }

                // 标准正弦；相位从 0 开始，因此音头天然接近 0，配合包络不会有跳变。
                double value = Math.Sin(2.0 * Math.PI * frequency * i / sampleRate);
                // 0.24 是主观音量：提醒音要能听见，但不能盖过用户正在听的内容。
                samples.Add((short)(value * 0.24 * short.MaxValue * envelope));
            }
        }

        /// <summary>
        /// 追加一段静音：按秒数换算出采样数并写入 0，用于分隔两个音。
        /// 时长为 0 或负数时算出的长度为 0，循环不执行，等价于不加间隔。
        /// </summary>
        private static void AddSilence(List<short> samples, int sampleRate, double seconds)
        {
            int count = Math.Max(0, (int)Math.Round(sampleRate * seconds));

            // 逐采样补 0；静音也必须是完整采样，否则 WAV 长度与头部声明不一致。
            for (int i = 0; i < count; i++)
            {
                samples.Add(0);
            }
        }
    }
}
