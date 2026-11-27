using System.Diagnostics;
using System.IO;

namespace OpenCS.Services.Scad;

/// <summary>
/// Объект SCAD++ API (ApiCreate / ApiRelease) — только чтение проекта. Все вызовы сессии — из
/// одного потока (обязанность вызывающего; в отладке проверяется).
/// </summary>
internal sealed unsafe class ScadApiSession : IDisposable
{
    /// <summary>Код успешного завершения (APICode_OK).</summary>
    const ushort ApiOk = 0;

    readonly int _threadId = Environment.CurrentManagedThreadId;
    nint _handle;

    public ScadApiSession(ScadApiNative native)
    {
        Native = native;
        nint h;
        ushort code;
        using (ScadApiTrace.Step("ApiCreate"))
            code = native.ApiCreate(&h);
        ScadApiTrace.Write($"ApiCreate: код {code}, хэндл 0x{h:X}");
        if (code != ApiOk || h == 0)
            throw new ScadApiException("ScadApiCallFailed", ["ApiCreate", code]);
        _handle = h;
    }

    public ScadApiNative Native { get; }

    /// <summary>Хэндл объекта API.</summary>
    public nint Handle
    {
        get
        {
            Debug.Assert(Environment.CurrentManagedThreadId == _threadId, "Сессия SCAD API используется из другого потока");
            ObjectDisposedException.ThrowIf(_handle == 0, this);
            return _handle;
        }
    }

    /// <summary>Открыть проект .SPR (ApiReadProject).</summary>
    public void Open(string sprPath)
    {
        string full = Path.GetFullPath(sprPath);
        if (!File.Exists(full))
            throw new ScadApiException("ScadApiProjectNotFound", [full]);
        var name = ScadApiLayouts.ToAnsiZ(full)
            ?? throw new ScadApiException("ScadApiPathNotAnsi", [full]);
        ushort code;
        using (ScadApiTrace.Step($"ApiReadProject «{full}»"))
            fixed (byte* p = name)
                code = Native.ApiReadProject(Handle, p);
        ScadApiTrace.Write($"ApiReadProject: код {code}");
        Check(code, "ApiReadProject");
    }

    /// <summary>
    /// Подключить результаты расчёта (ApiInitResult) в единицах «м», «Т». Каталог передаётся явно
    /// (NULL открыл бы диалог SCAD). false — результатов нет или каталог не тот.
    /// </summary>
    public bool TryInitResult(string workDirectory)
    {
        if (string.IsNullOrWhiteSpace(workDirectory) || !Directory.Exists(workDirectory)) return false;
        string dir = Path.GetFullPath(workDirectory);
        if (!dir.EndsWith('\\')) dir += '\\';
        var dirBytes = ScadApiLayouts.ToAnsiZ(dir);
        if (dirBytes == null) return false;

        Span<byte> units = stackalloc byte[ScadApiLayouts.UnitsSize * 2];
        ScadApiLayouts.WriteUnit(units, "m", 1);
        ScadApiLayouts.WriteUnit(units[ScadApiLayouts.UnitsSize..], "T", 1);
        ushort code;
        using (ScadApiTrace.Step($"ApiInitResult «{dir}»"))
            fixed (byte* u = units)
            fixed (byte* d = dirBytes)
                code = Native.ApiInitResult(Handle, u, d);
        ScadApiTrace.Write($"ApiInitResult: код {code}");
        return code == ApiOk;
    }

    /// <summary>Код возврата ≠ APICode_OK → <see cref="ScadApiException"/> с сообщениями DLL.</summary>
    public void Check(ushort code, string what)
    {
        if (code != ApiOk)
            throw new ScadApiException("ScadApiCallFailed", [what, code], Phrases());
    }

    /// <summary>Сообщения DLL (ApiGetPhrase) через перенос строки; null — нет.</summary>
    public string? Phrases()
    {
        nint h = Handle;
        uint n = Native.ApiGetQuantityPhrase(h);
        if (n == 0) return null;
        var lines = new List<string>();
        for (uint i = 1; i <= n && i <= 50; i++)
        {
            byte* p = Native.ApiGetPhrase(h, i);
            if (p != null && Str(p) is { Length: > 0 } s) lines.Add(s);
        }
        return lines.Count > 0 ? string.Join(Environment.NewLine, lines) : null;
    }

    /// <summary>LPCSTR cp1251 → строка (null-указатель → "").</summary>
    public static string Str(byte* p)
    {
        if (p == null) return "";
        int len = 0;
        while (p[len] != 0) len++;
        return ScadApiLayouts.CString(new ReadOnlySpan<byte>(p, len));
    }

    public void Dispose()
    {
        if (_handle == 0) return;
        nint h = _handle;
        _handle = 0;
        using (ScadApiTrace.Step($"ApiRelease 0x{h:X}"))
            Native.ApiRelease(&h);
    }
}
