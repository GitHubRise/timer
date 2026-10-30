// 提醒提示音模块：运行时在内存里合成一段 880/1174.7 Hz 双音 WAV，交给 System.Media.SoundPlayer 播放。
// 调用方是 ClockWindow 的提醒气泡展示逻辑（SettingsWindow 的「测试提醒」也走同一条链路）。
// 边界：不依赖外部音频文件或第三方库，因此打包时不需要额外资源；播放失败只降级为静音并记 Warn，
// 绝不向提醒展示/计时链路抛异常。SoundPlayer 是进程级单例，退出时由 AppController.Dispose 统一释放。

using System;
using System.Collections.Generic;
using System.IO;
using System.Media;

namespace GameClock
{
    /// <summary>
    /// 提醒声音的合成与播放；首次播放时合成并预加载，之后复用同一个播放器。
    /// </summary>
    internal static class AlarmSound
    {
        /// <summary>播放/释放共用锁：提醒定时器与设置窗口测试按钮可能并发触发播放。</summary>
        private static readonly object Sync = new object();

        /// <summary>缓存的播放器；null 表示尚未合成过 WAV。</summary>
        private static SoundPlayer _player;

        /// <summary>
        /// 播放提醒提示音；首次调用时合成并预加载 WAV，之后复用同一个 SoundPlayer。
        /// 任何失败都只降级为静音，不向调用方抛异常。
        /// </summary>
        public static void Play()
        {
            try
            {
                // 加锁串行化播放与释放，避免释放过程中另一线程正在播放。
                lock (Sync)
                {
                    // 只有首次播放才合成 WAV 并 Load；预加载能让后续 Play 立即出声。
                    if (_player == null)
                    {
                        _player = new SoundPlayer(BuildWave());
                        _player.Load();
                    }
                    // Play 异步播放，不阻塞 UI 线程；重复调用会打断上一段提示音。
                    _player.Play();
                }
            }
            catch (Exception ex)
            {
                // 声卡被独占、音频设备缺失或 WAV 头不被接受都走这里：气泡照常显示，
                // 声音只是增强，失败不得上抛到提醒流程。
                AppLog.Warn("Reminder", "提醒声音播放失败，本次提醒静音：" + ex.Message);
            }
        }

        /// <summary>
        /// 释放 SoundPlayer 及其底层音频句柄；进程退出前调用一次，
        /// 释放后再调用 Play 会重新合成并加载。
        /// </summary>
        public static void DisposePlayer()
        {
            // 与 Play 共用同一把锁，避免释放时另一线程正在播放。
            lock (Sync)
            {
                if (_player != null)
                {
                    try
                    {
                        _player.Dispose();
                    }
                    catch (Exception ex)
                    {
                        // 退出阶段音频设备可能已被移除，Dispose 抛异常不影响进程结束，仅留痕。
                        AppLog.Warn("Reminder", "释放提醒声音播放器失败：" + ex.Message);
                    }
                    _player = null;
                }
            }
        }

        /// <summary>
        /// 在内存里合成一段 PCM WAV：以 880 Hz / 1174.7 Hz 双音交替，总长约 0.9 秒。
        /// 返回的流已倒回起点，可直接交给 SoundPlayer。
        /// </summary>
        private static Stream BuildWave()
        {
            // 44.1 kHz 是 Windows 声卡通用采样率，避免重采样带来的额外延迟。
            const int sampleRate = 44100;
            List<short> samples = new List<short>();

            // 节奏：短音-停顿-高音-稍长停顿，重复两次；停顿逐段加长，形成“提醒”感。
            AddTone(samples, sampleRate, 880.0, 0.16);
            AddSilence(samples, sampleRate, 0.05);
            AddTone(samples, sampleRate, 1174.7, 0.16);
            AddSilence(samples, sampleRate, 0.08);
            AddTone(samples, sampleRate, 880.0, 0.16);
            AddSilence(samples, sampleRate, 0.05);
            AddTone(samples, sampleRate, 1174.7, 0.16);
            AddSilence(samples, sampleRate, 0.10);

            int dataSize = samples.Count * sizeof(short);
            // PCM WAV 头固定 44 字节：RIFF(12) + fmt 块(24) + data 头(8)。
            MemoryStream stream = new MemoryStream(44 + dataSize);
            BinaryWriter writer = new BinaryWriter(stream);
            // RIFF 容器标识；块大小不含前 8 字节，故为 36 + 数据长度。
            writer.Write(new char[] { 'R', 'I', 'F', 'F' });
            writer.Write(36 + dataSize);
            writer.Write(new char[] { 'W', 'A', 'V', 'E' });
            // fmt 子块：16 字节描述 PCM 格式。
            writer.Write(new char[] { 'f', 'm', 't', ' ' });
            writer.Write(16);
            // 格式 1 = PCM 整数采样；声道数 1 = 单声道。
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(sampleRate);
            // 字节率 = 采样率 × 声道数 × 位深/8（16 bit 单声道即 ×2）。
            writer.Write(sampleRate * 2);
            // 块对齐 = 声道数 × 位深/8 = 2 字节。
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write(new char[] { 'd', 'a', 't', 'a' });
            writer.Write(dataSize);
            // 逐个写入 16 bit 小端采样，与 WAV 规范一致。
            for (int i = 0; i < samples.Count; i++)
            {
                writer.Write(samples[i]);
            }
            writer.Flush();
            // 倒回起点，SoundPlayer 从流头读取 RIFF/fmt/data 头。
            stream.Position = 0;
            return stream;
        }

        /// <summary>
        /// 向采样表追加一段正弦波；频率单位 Hz，时长单位秒。
        /// 首尾各做 6 ms 淡入淡出，避免波形突变产生爆音。
        /// </summary>
        private static void AddTone(List<short> samples, int sampleRate, double frequency, double seconds)
        {
            int count = Math.Max(1, (int)Math.Round(sampleRate * seconds));
            // 6 ms 包络：短到听不出音量变化，又足够消除方波式的“咔哒”声。
            int fade = Math.Max(1, (int)(sampleRate * 0.006));
            for (int i = 0; i < count; i++)
            {
                double envelope = 1.0;
                // 淡入段：音量从 0 线性升到 1。
                if (i < fade)
                {
                    envelope = (double)i / fade;
                }
                int remaining = count - i;
                // 淡出段：剩余样本不足 fade 时按比例衰减，防止尾部截断爆音。
                if (remaining < fade)
                {
                    envelope = Math.Min(envelope, (double)remaining / fade);
                }
                double value = Math.Sin(2.0 * Math.PI * frequency * i / sampleRate);
                // 振幅取 24% 满量程：足够醒目又不会在耳机上过于刺耳。
                samples.Add((short)(value * 0.24 * short.MaxValue * envelope));
            }
        }

        /// <summary>
        /// 向采样表追加静音段；时长单位秒，用于分隔相邻提示音。
        /// </summary>
        private static void AddSilence(List<short> samples, int sampleRate, double seconds)
        {
            int count = Math.Max(0, (int)Math.Round(sampleRate * seconds));
            // 静音采样点直接补 0，保持单声道 16 bit 数据格式一致。
            for (int i = 0; i < count; i++)
            {
                samples.Add(0);
            }
        }
    }
}
