using System.Windows;

namespace OpenCS.Utilites
{
    public static class Loc
    {
        public static string S(string key) =>
            Application.Current?.TryFindResource(key) as string ?? key;
    }
}
