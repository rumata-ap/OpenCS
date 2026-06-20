using CScore.Import;
var path = @"C:\Users\Public\Documents\LIRA SAPR\LIRA SAPR 2024\Work\hostel_06.htm";
var r = LiraImporter.ImportFile(path, LiraImportMode.LoadCases);
Console.WriteLine($"Success={r.Success} Error={r.Error}");
Console.WriteLine($"ForceSets={r.ForceSets.Count}");
foreach (var fs in r.ForceSets.Take(3))
   Console.WriteLine($"  Tag={fs.Tag} items={fs.ShellItems.Count}");
var withRz = r.ForceSets[0].ShellItems.FirstOrDefault(i => i.Label == "13241-C");
if (withRz != null)
   Console.WriteLine($"13241-C Nx={withRz.Nx:F4} (expect ~62.4 kN/m from 6.365484t)");
var first = r.ForceSets[0].ShellItems.FirstOrDefault(i => i.Label == "11149-C");
if (first != null)
   Console.WriteLine($"11149-C Nx={first.Nx:F4} (expect ~42.4 from 4.326317t, no RZ)");
