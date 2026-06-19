using CScore.Import;

namespace CSfea.Tests;

static class LiraImportTests
{
   const string LiraWork = @"C:\Users\Public\Documents\LIRA SAPR\LIRA SAPR 2024\Work";

   public static void Run()
   {
      TestLoadCasesBar();
      TestLoadCasesShell();
      TestRsnShell();
      TestRsuShell();
      TestShellIgnoresRz();
      Console.WriteLine("LiraImportTests: OK");
   }

   static void TestLoadCasesBar()
   {
      var path = Path.Combine(LiraWork, "beam_06.htm");
      if (!File.Exists(path)) return;
      var r = LiraImporter.ImportFile(path, LiraImportMode.LoadCases);
      Assert(r.Success, "beam load cases");
      Assert(r.ForceSets.Count == 3, "beam 3 load cases");
      Assert(r.ForceSets.All(f => f.Kind == "bar"), "beam kind");
      Assert(r.ForceSets[0].Tag == "1 - ЗАГРУЖЕНИЕ 1", "beam load case full tag");
      Assert(r.ForceSets[0].Items.Count >= 7, "beam sections");
   }

   static void TestLoadCasesShell()
   {
      var path = Path.Combine(LiraWork, "hostel_06.htm");
      if (!File.Exists(path)) return;
      var r = LiraImporter.ImportFile(path, LiraImportMode.LoadCases);
      Assert(r.Success, "shell load cases");
      Assert(r.ForceSets.Count == 13, "hostel_06 13 load cases");
      Assert(r.ForceSets.All(f => f.Kind == "shell"), "shell kind");
      Assert(r.ForceSets[0].Tag == "1 - СОБСТВЕННЫЙ ВЕС", "shell load case full tag");
   }

   static void TestRsnShell()
   {
      var path = Path.Combine(LiraWork, "hostel_53.htm");
      if (!File.Exists(path)) return;
      var r = LiraImporter.ImportFile(path, LiraImportMode.Rsn);
      Assert(r.Success, "shell RSN");
      Assert(r.ForceSets.Count == 4, "RSN 4 combinations");
      Assert(r.ForceSets.All(f => f.Kind == "shell"), "RSN shell");
      Assert(r.ForceSets[0].Tag == "1 (B1) - Основное.1x", "RSN full tag");
      Assert(r.ForceSets[1].Tag == "2 (B1) - Основное.2x", "RSN full tag 2");
   }

   static void TestRsuShell()
   {
      var path = Path.Combine(LiraWork, "hostel_08.htm");
      if (!File.Exists(path)) return;
      var r = LiraImporter.ImportFile(path, LiraImportMode.Rsu);
      Assert(r.Success, "shell RSU");
      Assert(r.ForceSets.Count == 1, "RSU one set");
      Assert(r.ForceSets[0].Kind == "shell", "RSU shell");
      Assert(r.ForceSets[0].ShellItems.Count > 10, "RSU rows");
   }

   static void TestShellIgnoresRz()
   {
      // hostel_06.htm: у элементов с отпором грунта после QY идёт строка RZ (реактивный отпор)
      var path = Path.Combine(LiraWork, "hostel_06.htm");
      if (!File.Exists(path)) return;
      var r = LiraImporter.ImportFile(path, LiraImportMode.LoadCases);
      Assert(r.Success, "shell with RZ rows");

      var fs = r.ForceSets[0];
      var noSoil = fs.ShellItems.FirstOrDefault(i => i.Label == "11149-C");
      Assert(noSoil != null, "11149-C without soil support");
      Assert(Math.Abs(noSoil!.Nx - 4.326317 * 9.80665) < 0.01, "11149-C Nx");

      var withSoil = fs.ShellItems.FirstOrDefault(i => i.Label == "13241-C");
      Assert(withSoil != null, "13241-C with soil support (RZ row in HTML)");
      Assert(Math.Abs(withSoil!.Nx - 6.365484 * 9.80665) < 0.01, "13241-C Nx ignores RZ");
      Assert(Math.Abs(withSoil.Qy - 3.340709 * 9.80665) < 0.01, "13241-C Qy ignores RZ");
   }

   static void Assert(bool cond, string msg)
   {
      if (!cond) throw new InvalidOperationException($"LiraImportTests failed: {msg}");
   }
}
