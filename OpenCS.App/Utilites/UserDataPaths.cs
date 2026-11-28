namespace OpenCS.Utilites;

/// <summary>
/// Папки данных пользователя вне папки программы: в Windows — <c>%LOCALAPPDATA%\OpenCS</c>, в Linux —
/// <c>~/.local/share/OpenCS</c>. Сюда, а не рядом с исполняемым файлом, по умолчанию пишутся артефакты запусков
/// Gmsh и OpenSees: папка программы может раздаваться пользователям (облачная папка сборки), и чужие рабочие файлы
/// в неё попадать не должны.
/// </summary>
public static class UserDataPaths
{
    /// <summary>Корень данных пользователя; локальной папки приложений нет — временная папка.</summary>
    public static string Root
    {
        get
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(string.IsNullOrEmpty(local) ? Path.GetTempPath() : local, "OpenCS");
        }
    }

    /// <summary>Каталог артефактов Gmsh по умолчанию.</summary>
    public static string GmshArtifacts => Path.Combine(Root, "GmshArtifacts");

    /// <summary>
    /// Каталог артефактов OpenSees по умолчанию. OpenSees не открывает пути с не-ASCII символами (кириллическое имя
    /// пользователя в пути профиля) — тогда общая папка данных программ (<c>%ProgramData%\OpenCS</c>).
    /// </summary>
    public static string OpenSeesArtifacts
    {
        get
        {
            string own = Path.Combine(Root, "OpenSeesArtifacts");
            if (OpenCS.OpenSees.Runtime.WindowsShortPath.IsAsciiSafe(own)) return own;
            string common = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            return string.IsNullOrEmpty(common) ? own : Path.Combine(common, "OpenCS", "OpenSeesArtifacts");
        }
    }
}
