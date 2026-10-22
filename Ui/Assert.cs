namespace _1RM
{
    internal static class Assert
    {
        private const string APP_NAME_RAW = "1Remote";
#if DEBUG
        public const string APP_NAME = $"{APP_NAME_RAW}_Debug";
#if FOR_MICROSOFT_STORE_ONLY
        public const string APP_DISPLAY_NAME = $"{APP_NAME}(Store)_Debug";
#else
        public const string APP_DISPLAY_NAME = APP_NAME + " Preview";
#endif
#else
        public const string APP_NAME = $"{APP_NAME_RAW}";
#if FOR_MICROSOFT_STORE_ONLY
        public const string APP_DISPLAY_NAME = $"{APP_NAME}(Store)";
#else
        public const string APP_DISPLAY_NAME = APP_NAME + " Preview";
#endif
#endif


        public const string MS_APP_CENTER_SECRET = "===REPLACE_ME_WITH_APP_CENTER_SECRET===";
        public const string SENTRY_IO_DEN = "===REPLACE_ME_WITH_SENTRY_IO_DEN===";
        public static readonly string STRING_SALT = ReadBuildCompatibilitySalt();
        private static string ReadBuildCompatibilitySalt()
        {
            using var stream = typeof(Assert).Assembly.GetManifestResourceStream("1Remote.CompatibilitySalt");
            if (stream == null) return "===REPLACE_ME_WITH_SALT===";
            using var reader = new System.IO.StreamReader(stream);
            return reader.ReadToEnd();
        }
        public const string MS_STORE_PATH = "ms-windows-store://review/?productid=9PNMNF92JNFP";
    }
}
