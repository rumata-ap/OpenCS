using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text.RegularExpressions;
using CScore.Import.Cad;
using Microsoft.Win32;

namespace OpenCS.Services.Cad;

/// <summary>Запущенная САПР с ActiveX API, совместимым с AutoCAD.</summary>
/// <param name="Product">Название продукта (AutoCAD, nanoCAD, …).</param>
/// <param name="ProgId">ProgID, по CLSID которого найдена в ROT.</param>
public sealed record CadInstance(string Product, string ProgId, string Version, string Document)
{
   public override string ToString() => $"{Product} {Version} — {Document}";
}

/// <summary>
/// Подключение к запущенным AutoCAD / nanoCAD / BricsCAD / ZWCAD / GstarCAD через ROT по CLSID всех зарегистрированных
/// версий (позднее связывание, без interop-сборок: работает с любой версией, имеющей ActiveX API, — AutoCAD от 2000).
/// Геометрия читается одним вызовом <c>Document.Export(…,"DXF",…)</c> (AutoCAD выгружает весь чертёж, nanoCAD — выбор)
/// и разбирается в процессе OpenCS; поштучного чтения объектов через COM нет — оно в десятки раз медленнее.
/// Методы блокирующие, вызывать вне UI-потока; объект CAD живёт только внутри вызова.
/// </summary>
public static partial class CadComConnector
{
   const int RPC_E_CALL_REJECTED = unchecked((int)0x80010001);
   const int RPC_E_SERVERCALL_RETRYLATER = unchecked((int)0x8001010A);

   static readonly (string Pattern, string Product)[] Products =
   [
      ("AutoCAD.Application", "AutoCAD"),
      ("nanoCADx64.Application", "nanoCAD"),
      ("nanoCAD.Application", "nanoCAD"),
      ("BricscadApp.AcadApplication", "BricsCAD"),
      ("ZWCAD.Application", "ZWCAD"),
      ("GCAD.Application", "GstarCAD"),
   ];

   [GeneratedRegex(@"^(AutoCAD\.Application|nanoCADx64\.Application|nanoCAD\.Application|BricscadApp\.AcadApplication|ZWCAD\.Application|GCAD\.Application)(\.[\d.]+)?$", RegexOptions.IgnoreCase)]
   private static partial Regex ProgIdRegex();

   [DllImport("ole32.dll")]
   static extern int CreateBindCtx(uint reserved, out IBindCtx ppbc);

   [DllImport("user32.dll")]
   static extern bool SetForegroundWindow(IntPtr hWnd);

   [DllImport("user32.dll")]
   static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

   /// <summary>CLSID всех зарегистрированных версий поддерживаемых САПР → (продукт, ProgID).</summary>
   static Dictionary<Guid, (string Product, string ProgId)> RegisteredClasses()
   {
      var map = new Dictionary<Guid, (string, string)>();
      using var root = Registry.ClassesRoot;
      foreach (var name in root.GetSubKeyNames())
      {
         if (!ProgIdRegex().IsMatch(name)) continue;
         using var clsidKey = root.OpenSubKey(name + @"\CLSID");
         if (clsidKey?.GetValue(null) is string s && Guid.TryParse(s, out var g) && !map.ContainsKey(g))
            map[g] = (Products.First(p => name.StartsWith(p.Pattern, StringComparison.OrdinalIgnoreCase)).Product, name);
      }
      return map;
   }

   /// <summary>Объекты приложений САПР из ROT (без повторов одного процесса).</summary>
   static List<(object App, string Product, string ProgId)> RunningApps()
   {
      var classes = RegisteredClasses();
      var result = new List<(object, string, string)>();
      if (classes.Count == 0) return result;
      if (CreateBindCtx(0, out var ctx) != 0) return result;
      try
      {
         ctx.GetRunningObjectTable(out var rot);
         rot.EnumRunning(out var monikers);
         var m = new IMoniker[1];
         // Один процесс бывает зарегистрирован под несколькими CLSID (версионный и общий ProgID) — повторы
         // отсекаются по COM-идентичности (указатель IUnknown одного объекта одинаков).
         var seen = new HashSet<IntPtr>();
         while (monikers.Next(1, m, IntPtr.Zero) == 0)
         {
            m[0].GetDisplayName(ctx, null, out string name);
            foreach (var (clsid, info) in classes)
            {
               if (!name.Contains(clsid.ToString("B"), StringComparison.OrdinalIgnoreCase)) continue;
               if (rot.GetObject(m[0], out object app) != 0) break;
               IntPtr unk = Marshal.GetIUnknownForObject(app);
               Marshal.Release(unk);
               if (seen.Add(unk)) result.Add((app, info.Product, info.ProgId));
               else Marshal.ReleaseComObject(app);
               break;
            }
         }
      }
      finally { Marshal.ReleaseComObject(ctx); }
      return result;
   }

   /// <summary>Запущенные САПР с открытым документом.</summary>
   public static List<CadInstance> Find()
   {
      var list = new List<CadInstance>();
      foreach (var (app, product, progId) in RunningApps())
      {
         try
         {
            dynamic a = app;
            string version = Retry(() => (string)a.Version);
            string doc = Retry(() => a.Documents.Count > 0 ? (string)a.ActiveDocument.Name : "");
            if (doc.Length > 0) list.Add(new CadInstance(product, progId, version, doc));
         }
         catch (COMException) { }
         finally { Marshal.ReleaseComObject(app); }
      }
      return list;
   }

   static dynamic Connect(CadInstance target)
   {
      dynamic? found = null;
      foreach (var (app, _, progId) in RunningApps())
      {
         if (found == null && progId == target.ProgId)
         {
            dynamic a = app;
            try
            {
               if (Retry(() => (string)a.ActiveDocument.Name) == target.Document) { found = a; continue; }
            }
            catch (COMException) { }
         }
         Marshal.ReleaseComObject(app);
      }
      return found ?? throw new InvalidOperationException(
         $"{target.Product} с документом «{target.Document}» не найден. Если САПР запущена от имени администратора, " +
         "запустите OpenCS так же (или обе программы без повышения прав).");
   }

   /// <summary>
   /// Снимок активного документа: весь чертёж или объекты, целиком лежащие в рамке <paramref name="window"/>.
   /// </summary>
   public static CadSnapshot Read(CadInstance target, CadArcDiscretization arcs, double tolerance, CadBounds? window = null)
   {
      dynamic app = Connect(target);
      string dir = Path.Combine(Path.GetTempPath(), "OpenCS", "cad-export");
      Directory.CreateDirectory(dir);
      string baseName = Path.Combine(dir, $"sel_{Guid.NewGuid():N}");
      try
      {
         dynamic doc = Retry(() => app.ActiveDocument);
         dynamic ss = Retry(() => NewSelectionSet(doc));
         try
         {
            // nanoCAD выгружает только выбор, AutoCAD — весь чертёж; рамку всё равно применяем у себя.
            if (window is { } w)
               Retry(() => ss.Select(0, new[] { w.MinX, w.MinY, 0.0 }, new[] { w.MaxX, w.MaxY, 0.0 }));
            else
               Retry(() => ss.Select(5));
            Retry(() => doc.Export(baseName, "DXF", ss));
         }
         finally { try { ss.Delete(); } catch (COMException) { } }

         string file = Directory.GetFiles(dir, Path.GetFileName(baseName) + "*").FirstOrDefault()
            ?? throw new InvalidOperationException($"{target.Product} не выгрузил DXF.");
         var snap = DxfSnapshotReader.Read(file, arcs, tolerance);
         snap.Source = $"{target.Product}: {target.Document}";
         return window is { } win ? snap.Within(win) : snap;
      }
      finally
      {
         foreach (var f in Directory.GetFiles(dir, Path.GetFileName(baseName) + "*"))
            try { File.Delete(f); } catch (IOException) { }
         Marshal.ReleaseComObject(app);
      }
   }

   static dynamic NewSelectionSet(dynamic doc)
   {
      const string name = "OPENCS_IMPORT";
      try { doc.SelectionSets.Item(name).Delete(); } catch (Exception e) when (e is COMException or ArgumentException) { } // нет такого набора
      return doc.SelectionSets.Add(name);
   }

   /// <summary>Рамка, указанная пользователем в САПР двумя точками; null — отменено (Esc).</summary>
   public static CadBounds? PickWindow(CadInstance target)
   {
      dynamic app = Connect(target);
      try
      {
         Activate(app);
         dynamic util = app.ActiveDocument.Utility;
         try
         {
            double[] p1 = (double[])util.GetPoint(Type.Missing, "\nOpenCS: первый угол рамки с сечениями: ");
            double[] p2 = (double[])util.GetCorner(p1, "\nOpenCS: противоположный угол: ");
            return new CadBounds(Math.Min(p1[0], p2[0]), Math.Min(p1[1], p2[1]), Math.Max(p1[0], p2[0]), Math.Max(p1[1], p2[1]));
         }
         catch (COMException) { return null; } // Esc или прерванный ввод
      }
      finally { Marshal.ReleaseComObject(app); }
   }

   /// <summary>Показать область чертежа в САПР (зум с полем 10 %).</summary>
   public static void ZoomTo(CadInstance target, CadBounds b, bool activate = false)
   {
      dynamic app = Connect(target);
      try
      {
         var w = b.Inflate(Math.Max(b.Width, b.Height) * 0.1 + 1e-9);
         Retry(() => app.ZoomWindow(new[] { w.MinX, w.MinY, 0.0 }, new[] { w.MaxX, w.MaxY, 0.0 }));
         if (activate) Activate(app);
      }
      finally { Marshal.ReleaseComObject(app); }
   }

   static void Activate(dynamic app)
   {
      try
      {
         var hwnd = new IntPtr(Convert.ToInt64(app.HWND));
         ShowWindow(hwnd, 9); // SW_RESTORE
         SetForegroundWindow(hwnd);
      }
      catch (Exception e) when (e is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) { }
   }

   /// <summary>Повтор вызова, отвергнутого занятой САПР (идёт команда, открыт диалог), — до ~5 с.</summary>
   static T Retry<T>(Func<T> f)
   {
      for (int i = 0; ; i++)
      {
         try { return f(); }
         catch (COMException e) when ((e.HResult == RPC_E_CALL_REJECTED || e.HResult == RPC_E_SERVERCALL_RETRYLATER) && i < 50)
         {
            Thread.Sleep(100);
         }
         catch (COMException e) when (e.HResult == RPC_E_CALL_REJECTED || e.HResult == RPC_E_SERVERCALL_RETRYLATER)
         {
            throw new InvalidOperationException("САПР занята (идёт команда или открыт диалог). Завершите команду (Esc) и повторите.", e);
         }
      }
   }

   static void Retry(Action f) => Retry<object?>(() => { f(); return null; });
}
