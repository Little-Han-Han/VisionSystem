using System;
using System.Collections.Generic;
using System.Globalization;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace VisionPlatform.Core.Licensing
{
    /// <summary>
    /// 机器指纹采集与机器码计算（对应 LicenseTool README §4.1）。
    /// <para>
    /// 采集 <b>5 项</b>硬件/系统标识，各自取 <c>SHA256(原始值)</c> 前 4 字节转 8 位十六进制
    /// 作为「短哈希」；许可证里存这 5 个短哈希，验签时按 <b>≥N/5 匹配</b> 放行 ——
    /// 换一张网卡或换硬盘不锁死，整机更换必失败。
    /// </para>
    /// <para>
    /// 【必须与签发端算法逐字一致】任何一项的取值方式或拼接顺序变化，都会让已签发的
    /// 许可证全部变成 <see cref="LicenseStatus.MachineMismatch"/>。改动前先看签发侧实现。
    /// </para>
    /// </summary>
    public static class MachineFingerprint
    {
        /// <summary>指纹项数（固定 5 项：主板 / CPU / 系统盘卷序列号 / MachineGuid / 物理网卡 MAC）。</summary>
        public const int SlotCount = 5;

        private static readonly object Sync = new object();
        private static string[] _cachedShortHashes;
        private static string _cachedMachineCode;

        /// <summary>
        /// 5 项短哈希（小写十六进制，8 位）。<strong>采集失败的项为空串</strong> —— 不抛异常，
        /// 也不伪造值（伪造会让匹配计数失真）。
        /// </summary>
        public static string[] GetShortHashes()
        {
            lock (Sync)
            {
                if (_cachedShortHashes == null) _cachedShortHashes = Collect();
                return (string[])_cachedShortHashes.Clone();
            }
        }

        /// <summary>
        /// 机器码（给人看/粘贴）：<c>Base32(SHA256(5 项原始值拼接))</c> 前 16 字符，
        /// 按 4-4-4-4 分组显示，例如 <c>7K3M-9QZP-2XLV-8WDB</c>。
        /// </summary>
        public static string GetMachineCode()
        {
            lock (Sync)
            {
                if (_cachedMachineCode == null)
                {
                    string joined = string.Join("|", CollectRaw());
                    byte[] hash = Sha256(Encoding.UTF8.GetBytes(joined));
                    string b32 = ToBase32(hash);
                    if (b32.Length < 16) b32 = b32.PadRight(16, 'A');

                    string raw = b32.Substring(0, 16);
                    _cachedMachineCode = raw.Substring(0, 4) + "-" + raw.Substring(4, 4) + "-"
                                       + raw.Substring(8, 4) + "-" + raw.Substring(12, 4);
                }
                return _cachedMachineCode;
            }
        }

        /// <summary>指纹是否"降级"（有任一项采集不到）。界面据此提示"指纹降级"。</summary>
        public static bool IsDegraded()
        {
            foreach (string h in GetShortHashes())
            {
                if (string.IsNullOrEmpty(h)) return true;
            }
            return false;
        }

        /// <summary>
        /// 与许可证里的指纹比对，返回匹配项数（只统计"双方都非空"的项）。
        /// </summary>
        public static int CountMatches(IList<string> licensedFingerprints)
        {
            if (licensedFingerprints == null) return 0;

            string[] mine = GetShortHashes();
            int matches = 0;
            int n = Math.Min(SlotCount, Math.Min(mine.Length, licensedFingerprints.Count));
            for (int i = 0; i < n; i++)
            {
                string a = mine[i];
                string b = licensedFingerprints[i];
                if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) continue;   // 采集失败的项不参与比对
                if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) matches++;
            }
            return matches;
        }

        /// <summary>
        /// 匹配是否达到阈值（默认 3/5）。阈值来自配置 <c>System.LicenseMinFingerprintMatch</c>。
        /// </summary>
        public static bool Matches(IList<string> licensedFingerprints, int minMatches)
        {
            if (minMatches <= 0) minMatches = LicenseDefaults.MinFingerprintMatch;
            if (minMatches > SlotCount) minMatches = SlotCount;
            return CountMatches(licensedFingerprints) >= minMatches;
        }

        // =====================================================================
        //  采集
        // =====================================================================

        /// <summary>5 项短哈希（顺序固定：主板 / CPU / 卷序列号 / MachineGuid / MAC）。</summary>
        private static string[] Collect()
        {
            string[] raw = CollectRaw();

            var hashes = new string[SlotCount];
            for (int i = 0; i < SlotCount; i++)
            {
                hashes[i] = string.IsNullOrEmpty(raw[i]) ? "" : ShortHash(raw[i]);
            }
            return hashes;
        }

        /// <summary>5 项原始值（空串 = 该项取不到）。</summary>
        private static string[] CollectRaw()
        {
            return new[]
            {
                TryRead(delegate { return WmiValue("Win32_ComputerSystemProduct", "UUID"); }),
                TryRead(delegate { return WmiValue("Win32_Processor", "ProcessorId"); }),
                TryRead(delegate { return VolumeSerial(ProgramDrive()); }),
                TryRead(delegate { return MachineGuid(); }),
                TryRead(delegate { return FirstPhysicalMac(); })
            };
        }

        private static string TryRead(Func<string> read)
        {
            try
            {
                string v = read();
                return (v ?? "").Trim();
            }
            catch (Exception)
            {
                // 单项采集失败不阻断出码：留空串，界面提示"指纹降级"
                return "";
            }
        }

        /// <summary>WMI 单值查询（主板 UUID / CPU ProcessorId）。</summary>
        private static string WmiValue(string className, string property)
        {
            string query = "SELECT " + property + " FROM " + className;
            using (var searcher = new ManagementObjectSearcher(query))
            using (ManagementObjectCollection results = searcher.Get())
            {
                foreach (ManagementBaseObject item in results)
                {
                    using (item)
                    {
                        object v = item[property];
                        if (v != null) return Convert.ToString(v, CultureInfo.InvariantCulture) ?? "";
                    }
                }
            }
            return "";
        }

        /// <summary>程序所在盘的卷序列号（重装格式化可能变，属中等稳定性）。</summary>
        private static string ProgramDrive()
        {
            string root = System.IO.Path.GetPathRoot(AppDomain.CurrentDomain.BaseDirectory);
            return string.IsNullOrEmpty(root) ? "C:\\" : root;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetVolumeInformation(
            string rootPathName,
            StringBuilder volumeNameBuffer, int volumeNameSize,
            out uint volumeSerialNumber,
            out uint maximumComponentLength,
            out uint fileSystemFlags,
            StringBuilder fileSystemNameBuffer, int fileSystemNameSize);

        private static string VolumeSerial(string root)
        {
            uint serial, maxLen, flags;
            var name = new StringBuilder(256);
            var fs = new StringBuilder(256);

            if (!GetVolumeInformation(root, name, name.Capacity, out serial, out maxLen, out flags,
                                      fs, fs.Capacity))
            {
                return "";
            }
            return serial.ToString("X8", CultureInfo.InvariantCulture);
        }

        /// <summary>系统安装时生成、重装系统才会变的 MachineGuid。</summary>
        private static string MachineGuid()
        {
            using (RegistryKey key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            using (RegistryKey sub = key.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography"))
            {
                if (sub == null) return "";
                object v = sub.GetValue("MachineGuid");
                return v == null ? "" : Convert.ToString(v, CultureInfo.InvariantCulture) ?? "";
            }
        }

        /// <summary>第一块物理网卡的 MAC（排除回环 / 隧道 / 虚拟网卡）。</summary>
        private static string FirstPhysicalMac()
        {
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni == null) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;

                PhysicalAddress pa = ni.GetPhysicalAddress();
                if (pa == null) continue;

                string mac = pa.ToString();
                if (string.IsNullOrEmpty(mac) || mac == "000000000000") continue;

                // 常见虚拟网卡按描述过滤（VMware / VirtualBox / Hyper-V / 回环伪网卡）
                string desc = (ni.Description ?? "").ToLowerInvariant();
                if (desc.Contains("virtual") || desc.Contains("vmware") || desc.Contains("hyper-v")
                    || desc.Contains("loopback") || desc.Contains("tap-") || desc.Contains("vpn"))
                {
                    continue;
                }

                return mac;
            }
            return "";
        }

        // =====================================================================
        //  编码工具
        // =====================================================================

        private static byte[] Sha256(byte[] data)
        {
            using (SHA256 sha = SHA256.Create())
            {
                return sha.ComputeHash(data);
            }
        }

        /// <summary>短哈希：SHA256 前 4 字节 → 8 位小写十六进制。</summary>
        private static string ShortHash(string raw)
        {
            byte[] hash = Sha256(Encoding.UTF8.GetBytes(raw));
            var sb = new StringBuilder(8);
            for (int i = 0; i < 4; i++) sb.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        /// <summary>RFC 4648 Base32（A-Z2-7，不带填充）——机器码要"人手抄写不歧义"。</summary>
        public static string ToBase32(byte[] data)
        {
            const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
            if (data == null || data.Length == 0) return "";

            var sb = new StringBuilder((data.Length * 8 + 4) / 5);
            int buffer = 0;
            int bitsLeft = 0;
            foreach (byte b in data)
            {
                buffer = (buffer << 8) | b;
                bitsLeft += 8;
                while (bitsLeft >= 5)
                {
                    sb.Append(alphabet[(buffer >> (bitsLeft - 5)) & 0x1F]);
                    bitsLeft -= 5;
                }
            }
            if (bitsLeft > 0) sb.Append(alphabet[(buffer << (5 - bitsLeft)) & 0x1F]);
            return sb.ToString();
        }
    }
}
