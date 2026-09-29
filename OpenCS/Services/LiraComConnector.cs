using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace OpenCS.Services;

/// <summary>
/// Подключение к запущенной ЛИРЕ через COM независимо от продукта и версии.
/// </summary>
/// <remarks>
/// ЛИРА-САПР и ЛИРА-САПФИР регистрируют разные ProgID (<c>LiraSapr.Application[.2024]</c>,
/// <c>LiraSapphire.Application[.2025]</c>), а их библиотеки результатов (LiraResAPI.dll) — разные
/// CLSID класса <c>LiraResultsAccess</c> при одинаковых IID интерфейсов. Поэтому ProgID выбирается
/// по реально запущенному exe (путь из LocalServer32), а CLSID объекта результатов — из библиотеки
/// типов LiraResAPI.dll, лежащей рядом с этим exe (см. <see cref="LiraResultsApi"/>).
/// </remarks>
static class LiraComConnector
{
    /// <summary>Зарегистрированный COM-сервер приложения ЛИРА.</summary>
    sealed record LiraServer(string ProgId, Guid Clsid, string ExePath);

    static readonly Regex AppProgIdPattern =
        new(@"^Lira\w*\.Application(\.\d+)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Возвращает COM-объект приложения запущенной ЛИРЫ. Не запускает новый экземпляр:
    /// без открытой схемы он бесполезен.
    /// </summary>
    public static dynamic ConnectApplication()
    {
        var server = FindRunningServer();
        var appType = Type.GetTypeFromCLSID(server.Clsid, throwOnError: true)!;
        return Activator.CreateInstance(appType)!;
    }

    /// <summary>
    /// Создаёт объект чтения результатов (LiraResultsAccess) из библиотеки той ЛИРЫ, что запущена.
    /// </summary>
    public static LiraResultsApi CreateResultsAccess()
    {
        var server = FindRunningServer();
        string dll = Path.Combine(Path.GetDirectoryName(server.ExePath) ?? "", "LiraResAPI.dll");
        if (!File.Exists(dll))
            throw new InvalidOperationException(
                $"Рядом с {server.ExePath} не найдена библиотека результатов LiraResAPI.dll.");
        return CreateResultsAccess(dll);
    }

    /// <summary>Создаёт объект чтения результатов из указанной LiraResAPI.dll.</summary>
    public static LiraResultsApi CreateResultsAccess(string dllPath)
    {
        var lib = LoadTypeLib(dllPath)
            ?? throw new InvalidOperationException($"Не удалось прочитать библиотеку типов {dllPath}.");
        try
        {
            var clsid = FindCoClassClsid(lib, "LiraResultsAccess")
                ?? throw new InvalidOperationException(
                    $"В библиотеке {dllPath} не найден класс LiraResultsAccess.");
            var requestCodes = ReadEnum(lib, "LiraRequestEnum");

            var type = Type.GetTypeFromCLSID(clsid, throwOnError: false);
            object obj;
            try { obj = Activator.CreateInstance(type!)!; }
            catch (COMException ex)
            {
                throw new InvalidOperationException(
                    $"Библиотека результатов ЛИРЫ ({dllPath}) не зарегистрирована в системе. " +
                    "Переустановите или перерегистрируйте ЛИРУ.", ex);
            }
            return new LiraResultsApi(obj, requestCodes);
        }
        finally { Marshal.ReleaseComObject(lib); }
    }

    // ------------------------------------------------------------------ поиск запущенной ЛИРЫ

    static LiraServer FindRunningServer()
    {
        var servers = RegisteredServers();
        if (servers.Count == 0)
            throw new InvalidOperationException(
                "COM-сервер ЛИРЫ не зарегистрирован (ни LiraSapr.Application, ни LiraSapphire.Application). " +
                "Убедитесь, что ЛИРА установлена и зарегистрирована.");

        foreach (var s in servers)
            if (IsRunning(s.ExePath)) return s;

        throw new InvalidOperationException(
            "ЛИРА не запущена. Откройте расчётную схему в ЛИРА-САПР или ЛИРА-САПФИР и повторите.\n" +
            "Зарегистрированные версии:\n" +
            string.Join("\n", servers.Select(s => $"  {s.ProgId}: {s.ExePath}")));
    }

    /// <summary>
    /// ProgID вида Lira*.Application[.NNNN] с путём к exe. Версионные ProgID идут первыми,
    /// более новые — раньше; дубли по CLSID отбрасываются.
    /// </summary>
    static List<LiraServer> RegisteredServers()
    {
        var result = new List<LiraServer>();
        foreach (var name in Registry.ClassesRoot.GetSubKeyNames()
                     .Where(n => AppProgIdPattern.IsMatch(n))
                     .OrderByDescending(n => n, StringComparer.OrdinalIgnoreCase))
        {
            using var clsidKey = Registry.ClassesRoot.OpenSubKey($@"{name}\CLSID");
            if (!Guid.TryParse(clsidKey?.GetValue(null) as string, out var clsid)) continue;
            if (result.Any(s => s.Clsid == clsid)) continue;

            using var srvKey = Registry.ClassesRoot.OpenSubKey($@"CLSID\{clsid:B}\LocalServer32");
            string? exe = ExtractExePath(srvKey?.GetValue(null) as string);
            if (exe != null) result.Add(new LiraServer(name, clsid, exe));
        }
        return result;
    }

    /// <summary>Путь к exe из строки LocalServer32 (может быть в кавычках и с ключами).</summary>
    static string? ExtractExePath(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        command = command.Trim();
        if (command.StartsWith('"'))
        {
            int end = command.IndexOf('"', 1);
            return end > 1 ? command[1..end] : null;
        }
        int exeEnd = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return exeEnd > 0 ? command[..(exeEnd + 4)] : command;
    }

    static bool IsRunning(string exePath)
    {
        foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exePath)))
        {
            using (p)
            {
                try
                {
                    if (string.Equals(p.MainModule?.FileName, exePath, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                catch
                {
                    // Нет доступа к модулю (другая разрядность/права) — доверяем совпадению имени.
                    return true;
                }
            }
        }
        return false;
    }

    // ------------------------------------------------------------------ библиотека типов

    [DllImport("oleaut32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    static extern int LoadTypeLibEx(string file, int regKind, out ITypeLib typeLib);

    const int RegKindNone = 2;

    static ITypeLib? LoadTypeLib(string dllPath) =>
        LoadTypeLibEx(dllPath, RegKindNone, out var lib) == 0 ? lib : null;

    /// <summary>CLSID coclass с заданным именем.</summary>
    static Guid? FindCoClassClsid(ITypeLib lib, string coClassName)
    {
        Guid? result = null;
        VisitType(lib, coClassName, TYPEKIND.TKIND_COCLASS, (_, attr) => result = attr.guid);
        return result;
    }

    /// <summary>Значения перечисления по именам членов.</summary>
    static Dictionary<string, int> ReadEnum(ITypeLib lib, string enumName)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        VisitType(lib, enumName, TYPEKIND.TKIND_ENUM, (info, attr) =>
        {
            for (int j = 0; j < attr.cVars; j++)
            {
                info.GetVarDesc(j, out var pVar);
                try
                {
                    var desc = Marshal.PtrToStructure<VARDESC>(pVar);
                    info.GetDocumentation(desc.memid, out var member, out _, out _, out _);
                    var value = Marshal.GetObjectForNativeVariant(desc.desc.lpvarValue);
                    if (member != null && value != null) result[member] = Convert.ToInt32(value);
                }
                finally { info.ReleaseVarDesc(pVar); }
            }
        });
        return result;
    }

    static void VisitType(ITypeLib lib, string typeName, TYPEKIND kind, Action<ITypeInfo, TYPEATTR> visit)
    {
        int count = lib.GetTypeInfoCount();
        for (int i = 0; i < count; i++)
        {
            lib.GetDocumentation(i, out var name, out _, out _, out _);
            if (!string.Equals(name, typeName, StringComparison.Ordinal)) continue;

            lib.GetTypeInfo(i, out var info);
            info.GetTypeAttr(out var pAttr);
            try
            {
                var attr = Marshal.PtrToStructure<TYPEATTR>(pAttr);
                if (attr.typekind == kind) { visit(info, attr); return; }
            }
            finally { info.ReleaseTypeAttr(pAttr); }
        }
    }
}

/// <summary>
/// Объект чтения результатов ЛИРЫ с поздним связыванием и номерами запросов из его же библиотеки типов.
/// </summary>
/// <remarks>
/// Typed-интерфейсы Interop.LiraSaprRes использовать нельзя: в ЛИРА-САПФИР 2025 в ILiraResultsAccess
/// и в интерфейсы ответов вставлены новые методы в середину при тех же IID (vtable сдвинут),
/// а в LiraRequestEnum — новый член (РСН 5→6, РСУ 7→8). Вызовы по имени через IDispatch от этого не зависят.
/// </remarks>
sealed class LiraResultsApi(object access, IReadOnlyDictionary<string, int> requestCodes)
{
    /// <summary>COM-объект LiraResultsAccess (вызовы — только через dynamic).</summary>
    public dynamic Access { get; } = access;

    /// <summary>Создаёт запрос по имени члена LiraRequestEnum (например, «kLiraRequest_LoadCaseForces»).</summary>
    public dynamic CreateRequest(string requestName) =>
        requestCodes.TryGetValue(requestName, out int code)
            ? Access.CreateNewRequest(code)
            : throw new InvalidOperationException(
                $"Эта версия ЛИРЫ не поддерживает запрос {requestName}.");
}
