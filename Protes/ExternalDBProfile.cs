namespace Protes
{
    public class ExternalDbProfile
    {
        public string Host { get; set; } = "localhost";
        public int Port { get; set; } = 3306;
        public string Database { get; set; } = "";
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";

        public string DisplayName => $"{Host}:{Port}/{Database}";

        // ✅ Add this — auto-generated subtitle
        public string DisplaySubtitle => $"Username: {Username}";

        // Keep DisplayNameWithIndicator as a mutable property for UI
        public string DisplayNameWithIndicator { get; set; }

        public override bool Equals(object obj)
        {
            if (obj is ExternalDbProfile other)
            {
                return Host == other.Host &&
                       Port == other.Port &&
                       Database == other.Database &&
                       Username == other.Username &&
                       Password == other.Password;
            }
            return false;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 23 + (Host?.GetHashCode() ?? 0);
                hash = hash * 23 + Port.GetHashCode();
                hash = hash * 23 + (Database?.GetHashCode() ?? 0);
                hash = hash * 23 + (Username?.GetHashCode() ?? 0);
                hash = hash * 23 + (Password?.GetHashCode() ?? 0);
                return hash;
            }
        }
    }
}