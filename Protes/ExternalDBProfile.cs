// ExternalDbProfile.cs
namespace Protes
{
    public class ExternalDbProfile
    {
        public string Name { get; set; } = "Connection";
        public string Host { get; set; } = "localhost";
        public int Port { get; set; } = 3306;
        public string Database { get; set; } = "";
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";

        public string DisplayName => $"{Host}:{Port}/{Database}";
        public string DisplaySubtitle => $"Username: {Username}";
    }
}