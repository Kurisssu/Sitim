namespace Sitim.Api.Options
{
    public sealed class JwtOptions
    {
        /// <summary>Token issuer (e.g. "SITIM").</summary>
        public string Issuer { get; set; } = "SITIM";

        /// <summary>Token audience (e.g. "SITIM.Client").</summary>
        public string Audience { get; set; } = "SITIM.Client";

        /// <summary>
        /// Symmetric signing key. Use at least 32+ chars for HS256.
        /// In production store it in secrets, not in appsettings.json.
        /// </summary>
        public string SigningKey { get; set; } = "CHANGE_ME_DEV_SIGNING_KEY_32_CHARS_MIN";

        public int AccessTokenMinutes { get; set; } = 60;
    }
}
