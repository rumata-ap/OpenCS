using System.IO;
using System.Runtime.InteropServices;

namespace OpenCS.Services.Scad;

/// <summary>
/// SCADAPIX.dll (SCAD++ API, x64): загрузка по полному пути, выбранному во время работы, и указатели
/// на функции (все WINAPI; APICode — UINT16, BOOL — int, строки — LPCSTR в cp1251). Библиотека
/// загружается в процесс один раз; повторная загрузка из другого каталога — ошибка (нужен перезапуск).
/// </summary>
internal sealed unsafe class ScadApiNative
{
    /// <summary>Имя файла библиотеки.</summary>
    public const string DllName = "SCADAPIX.dll";

    static readonly object Sync = new();
    static ScadApiNative? _loaded;

    /// <summary>
    /// Одна сессия SCAD API в процессе одновременно (потокобезопасность DLL не заявлена): пробу в
    /// диалоге и импорт выполнять под этим семафором.
    /// </summary>
    public static SemaphoreSlim Gate { get; } = new(1, 1);

    /// <summary>Полный путь загруженной библиотеки.</summary>
    public string DllPath { get; }

    public readonly delegate* unmanaged[Stdcall]<nint*, ushort> ApiCreate;
    public readonly delegate* unmanaged[Stdcall]<nint*, ushort> ApiRelease;
    public readonly delegate* unmanaged[Stdcall]<nint, ushort> ApiGetLastError;
    public readonly delegate* unmanaged[Stdcall]<nint, uint> ApiGetQuantityPhrase;
    public readonly delegate* unmanaged[Stdcall]<nint, uint, byte*> ApiGetPhrase;
    public readonly delegate* unmanaged[Stdcall]<nint, byte*, ushort> ApiReadProject;
    public readonly delegate* unmanaged[Stdcall]<nint, byte*> ApiGetUnits;
    public readonly delegate* unmanaged[Stdcall]<nint, uint> ApiGetQuantityNode;
    public readonly delegate* unmanaged[Stdcall]<nint, uint, byte*> ApiGetNode;
    public readonly delegate* unmanaged[Stdcall]<nint, uint, uint> ApiIsNodeDeleted;
    public readonly delegate* unmanaged[Stdcall]<nint, uint> ApiGetElemQuantity;
    public readonly delegate* unmanaged[Stdcall]<nint, uint, uint*, uint*, uint*, uint**, ushort> ApiElemGetData;
    public readonly delegate* unmanaged[Stdcall]<nint, uint, uint> ApiIsElemDeleted;
    public readonly delegate* unmanaged[Stdcall]<nint, uint> ApiGetQuantityRigid;
    public readonly delegate* unmanaged[Stdcall]<nint, uint, byte*, uint, uint*, uint**, ushort> ApiGetRigid;
    public readonly delegate* unmanaged[Stdcall]<nint, uint, byte*> ApiGetRigidName;
    public readonly delegate* unmanaged[Stdcall]<nint, uint> ApiGetQuantityGroupElem;
    public readonly delegate* unmanaged[Stdcall]<nint, uint, uint*, uint*, uint**, byte**, ushort> ApiGetGroupElem;
    public readonly delegate* unmanaged[Stdcall]<nint, uint> ApiGetQuantityBlock;
    public readonly delegate* unmanaged[Stdcall]<nint, uint, uint*, uint**, byte**, uint*, ushort> ApiGetBlock;
    public readonly delegate* unmanaged[Stdcall]<nint, uint> ApiGetQuantitySystemCoordEffors;
    public readonly delegate* unmanaged[Stdcall]<nint, uint, byte*, uint*, double**, uint*, uint**, int> ApiGetSystemCoordEffors;
    public readonly delegate* unmanaged[Stdcall]<nint, uint> ApiGetQuantityConcrete;
    public readonly delegate* unmanaged[Stdcall]<nint, uint, byte**, uint*, uint**, ushort> ApiGetConcrete;
    public readonly delegate* unmanaged[Stdcall]<nint, uint, byte*> ApiGetNameConcrete;
    public readonly delegate* unmanaged[Stdcall]<nint, uint> ApiGetQuantityArmElemPlate;
    public readonly delegate* unmanaged[Stdcall]<nint, uint, byte**, ushort> ApiGetArmElemPlate;
    public readonly delegate* unmanaged[Stdcall]<nint, uint> ApiGetQuantityArmElemRod;
    public readonly delegate* unmanaged[Stdcall]<nint, uint, byte**, ushort> ApiGetArmElemRod;
    public readonly delegate* unmanaged[Stdcall]<nint, uint> ApiGetQuantityLoad;
    public readonly delegate* unmanaged[Stdcall]<nint, byte*, byte*, ushort> ApiInitResult;
    public readonly delegate* unmanaged[Stdcall]<nint, int> ApiYesEffors;
    public readonly delegate* unmanaged[Stdcall]<nint, int> ApiYesRSU;
    public readonly delegate* unmanaged[Stdcall]<nint, uint> ApiGetResultQuantityLoad;
    public readonly delegate* unmanaged[Stdcall]<nint, uint, byte**, byte, ushort> ApiGetEffors;
    public readonly delegate* unmanaged[Stdcall]<nint, uint, byte*, ushort> ApiGetRsu;
    public readonly delegate* unmanaged[Stdcall]<nint, uint, byte*> ApiGetLoadName;
    public readonly delegate* unmanaged[Stdcall]<nint, uint> ApiGetQuantityComb;
    public readonly delegate* unmanaged[Stdcall]<nint, uint, uint, uint, byte*, ushort> ApiGetResultData;

    ScadApiNative(string path, nint lib)
    {
        DllPath = path;
        nint F(string name) => NativeLibrary.TryGetExport(lib, name, out nint p)
            ? p
            : throw new ScadApiException("ScadApiMissingExport", [name, path]);

        ApiCreate = (delegate* unmanaged[Stdcall]<nint*, ushort>)F(nameof(ApiCreate));
        ApiRelease = (delegate* unmanaged[Stdcall]<nint*, ushort>)F(nameof(ApiRelease));
        ApiGetLastError = (delegate* unmanaged[Stdcall]<nint, ushort>)F(nameof(ApiGetLastError));
        ApiGetQuantityPhrase = (delegate* unmanaged[Stdcall]<nint, uint>)F(nameof(ApiGetQuantityPhrase));
        ApiGetPhrase = (delegate* unmanaged[Stdcall]<nint, uint, byte*>)F(nameof(ApiGetPhrase));
        ApiReadProject = (delegate* unmanaged[Stdcall]<nint, byte*, ushort>)F(nameof(ApiReadProject));
        ApiGetUnits = (delegate* unmanaged[Stdcall]<nint, byte*>)F(nameof(ApiGetUnits));
        ApiGetQuantityNode = (delegate* unmanaged[Stdcall]<nint, uint>)F(nameof(ApiGetQuantityNode));
        ApiGetNode = (delegate* unmanaged[Stdcall]<nint, uint, byte*>)F(nameof(ApiGetNode));
        ApiIsNodeDeleted = (delegate* unmanaged[Stdcall]<nint, uint, uint>)F(nameof(ApiIsNodeDeleted));
        ApiGetElemQuantity = (delegate* unmanaged[Stdcall]<nint, uint>)F(nameof(ApiGetElemQuantity));
        ApiElemGetData = (delegate* unmanaged[Stdcall]<nint, uint, uint*, uint*, uint*, uint**, ushort>)F(nameof(ApiElemGetData));
        ApiIsElemDeleted = (delegate* unmanaged[Stdcall]<nint, uint, uint>)F(nameof(ApiIsElemDeleted));
        ApiGetQuantityRigid = (delegate* unmanaged[Stdcall]<nint, uint>)F(nameof(ApiGetQuantityRigid));
        ApiGetRigid = (delegate* unmanaged[Stdcall]<nint, uint, byte*, uint, uint*, uint**, ushort>)F(nameof(ApiGetRigid));
        ApiGetRigidName = (delegate* unmanaged[Stdcall]<nint, uint, byte*>)F(nameof(ApiGetRigidName));
        ApiGetQuantityGroupElem = (delegate* unmanaged[Stdcall]<nint, uint>)F(nameof(ApiGetQuantityGroupElem));
        ApiGetGroupElem = (delegate* unmanaged[Stdcall]<nint, uint, uint*, uint*, uint**, byte**, ushort>)F(nameof(ApiGetGroupElem));
        ApiGetQuantityBlock = (delegate* unmanaged[Stdcall]<nint, uint>)F(nameof(ApiGetQuantityBlock));
        ApiGetBlock = (delegate* unmanaged[Stdcall]<nint, uint, uint*, uint**, byte**, uint*, ushort>)F(nameof(ApiGetBlock));
        ApiGetQuantitySystemCoordEffors = (delegate* unmanaged[Stdcall]<nint, uint>)F(nameof(ApiGetQuantitySystemCoordEffors));
        ApiGetSystemCoordEffors = (delegate* unmanaged[Stdcall]<nint, uint, byte*, uint*, double**, uint*, uint**, int>)F(nameof(ApiGetSystemCoordEffors));
        ApiGetQuantityConcrete = (delegate* unmanaged[Stdcall]<nint, uint>)F(nameof(ApiGetQuantityConcrete));
        ApiGetConcrete = (delegate* unmanaged[Stdcall]<nint, uint, byte**, uint*, uint**, ushort>)F(nameof(ApiGetConcrete));
        ApiGetNameConcrete = (delegate* unmanaged[Stdcall]<nint, uint, byte*>)F(nameof(ApiGetNameConcrete));
        ApiGetQuantityArmElemPlate = (delegate* unmanaged[Stdcall]<nint, uint>)F(nameof(ApiGetQuantityArmElemPlate));
        ApiGetArmElemPlate = (delegate* unmanaged[Stdcall]<nint, uint, byte**, ushort>)F(nameof(ApiGetArmElemPlate));
        ApiGetQuantityArmElemRod = (delegate* unmanaged[Stdcall]<nint, uint>)F(nameof(ApiGetQuantityArmElemRod));
        ApiGetArmElemRod = (delegate* unmanaged[Stdcall]<nint, uint, byte**, ushort>)F(nameof(ApiGetArmElemRod));
        ApiGetQuantityLoad = (delegate* unmanaged[Stdcall]<nint, uint>)F(nameof(ApiGetQuantityLoad));
        ApiInitResult = (delegate* unmanaged[Stdcall]<nint, byte*, byte*, ushort>)F(nameof(ApiInitResult));
        ApiYesEffors = (delegate* unmanaged[Stdcall]<nint, int>)F(nameof(ApiYesEffors));
        ApiYesRSU = (delegate* unmanaged[Stdcall]<nint, int>)F(nameof(ApiYesRSU));
        ApiGetResultQuantityLoad = (delegate* unmanaged[Stdcall]<nint, uint>)F(nameof(ApiGetResultQuantityLoad));
        ApiGetEffors = (delegate* unmanaged[Stdcall]<nint, uint, byte**, byte, ushort>)F(nameof(ApiGetEffors));
        ApiGetRsu = (delegate* unmanaged[Stdcall]<nint, uint, byte*, ushort>)F(nameof(ApiGetRsu));
        ApiGetLoadName = (delegate* unmanaged[Stdcall]<nint, uint, byte*>)F(nameof(ApiGetLoadName));
        ApiGetQuantityComb = (delegate* unmanaged[Stdcall]<nint, uint>)F(nameof(ApiGetQuantityComb));
        ApiGetResultData = (delegate* unmanaged[Stdcall]<nint, uint, uint, uint, byte*, ushort>)F(nameof(ApiGetResultData));
    }

    /// <summary>
    /// Загрузить SCADAPIX.dll из каталога <paramref name="dllDirectory"/> (зависимости ищутся там же).
    /// Повторный вызов с тем же каталогом возвращает уже загруженную библиотеку.
    /// </summary>
    public static ScadApiNative Load(string dllDirectory)
    {
        if (!Environment.Is64BitProcess)
            throw new ScadApiException("ScadApiNot64Bit", []);
        string path = Path.GetFullPath(Path.Combine(dllDirectory, DllName));

        lock (Sync)
        {
            if (_loaded != null)
            {
                return string.Equals(_loaded.DllPath, path, StringComparison.OrdinalIgnoreCase)
                    ? _loaded
                    : throw new ScadApiException("ScadApiOtherDllLoaded", [_loaded.DllPath, path]);
            }
            if (!File.Exists(path))
                throw new ScadApiException("ScadApiDllNotFound", [path]);

            if (!NativeLibrary.TryLoad(path, out nint lib))
            {
                // Зависимости SCAD (библиотеки рядом с DLL) иначе могут не найтись.
                SetDllDirectory(Path.GetDirectoryName(path)!);
                try { lib = NativeLibrary.Load(path); }
                catch (Exception ex) { throw new ScadApiException("ScadApiDllLoadFailed", [path, ex.Message], inner: ex); }
            }
            return _loaded = new ScadApiNative(path, lib);
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool SetDllDirectory(string lpPathName);
}
