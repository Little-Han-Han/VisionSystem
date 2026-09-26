using System;
using System.Collections.Generic;

namespace VisionPlatform.Models
{
    /// <summary>登录用户。</summary>
    public class User
    {
        public string Name { get; set; }

        /// <summary>角色：Admin / Operator。</summary>
        public string Role { get; set; }

        public DateTime LoginTime { get; set; }

        public bool IsAdmin
        {
            get { return string.Equals(Role, "Admin", StringComparison.OrdinalIgnoreCase); }
        }

        public override string ToString()
        {
            return Name + " (" + Role + ")";
        }
    }

    /// <summary>
    /// 内置账号校验。
    /// 实际项目应改为数据库或域认证；这里先用内置表，保证软件可独立运行。
    /// </summary>
    public static class UserStore
    {
        private static readonly Dictionary<string, KeyValuePair<string, string>> Accounts =
            new Dictionary<string, KeyValuePair<string, string>>(StringComparer.OrdinalIgnoreCase)
            {
                { "admin",    new KeyValuePair<string, string>("", "Admin") },
                { "operator", new KeyValuePair<string, string>("111111", "Operator") }
            };

        /// <summary>校验账号密码，成功返回用户对象。</summary>
        public static User Validate(string name, string password)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;

            KeyValuePair<string, string> account;
            if (!Accounts.TryGetValue(name.Trim(), out account)) return null;
            if (account.Key != password) return null;

            return new User
            {
                Name = name.Trim(),
                Role = account.Value,
                LoginTime = DateTime.Now
            };
        }

        /// <summary>内置账号列表（用于登录界面提示）。</summary>
        public static IEnumerable<string> DemoAccounts { get { return Accounts.Keys; } }
    }
}
