namespace OpenCS.Utilites
{
    /// <summary>Локализация UI-строк. Резолвер ресурсов регистрируется GUI-проектом.</summary>
    public static class Loc
    {
        /// <summary>Резолвер строковых ресурсов (WPF: Application.Current.TryFindResource; Avalonia: аналогично).</summary>
        public static Func<string, string?>? ResourceResolver { get; set; }

        public static string S(string key) =>
            ResourceResolver?.Invoke(key) ?? key;
    }
}
