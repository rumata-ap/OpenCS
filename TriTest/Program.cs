using CSTriangulation;
using System;
using System.Collections.Generic;
using System.Linq;

int passed = 0, failed = 0;

RunGeometryTriangulation("Rectangle 0.3×0.5", BuildRectLoop(0, 0, 0.3, 0.5), null, 0.05, 8.0, 0.3 * 0.5);

RunGeometryTriangulation("L-shape", BuildPolygonLoop(new double[,] {
   {0,0},{0.3,0},{0.3,0.25},{0.15,0.25},{0.15,0.5},{0,0.5}
}), null, 0.05, 8.0, 0.3 * 0.5 - 0.15 * 0.25);

RunGeometryTriangulation("Rect with hole", BuildRectLoop(0, 0, 0.3, 0.5),
   new List<(double x0, double y0, double x1, double y1)> { (0.08, 0.15, 0.22, 0.35) },
   0.04, 8.0, 0.3 * 0.5 - 0.14 * 0.20);

RunGeometryTriangulation("Rect with 2 holes", BuildRectLoop(0, 0, 0.6, 0.4),
   new List<(double x0, double y0, double x1, double y1)> {
      (0.08, 0.12, 0.20, 0.28), (0.40, 0.12, 0.52, 0.28)
   },
   0.04, 8.0, 0.6 * 0.4 - 2 * (0.12 * 0.16));

RunCircle("Circle r=0.2", 0.2, 0.04, 8.0);

RunConstrainedSegment("Constrained segment", 0.3, 0.5, 0.05, 8.0);

RunExpectedException("Self-intersecting contour throws", BuildBowtieLoop());
RunExpectedException("Contour with 2 faces throws", BuildTwoFaceLoop());

ReproRoundHole();

Console.WriteLine($"\n=== {passed}/{passed + failed} PASSED ===");

void ReproRoundHole()
{
   double w = 0.3, h = 0.5, scale = 8.0;
   double hullArea = w * h;
   double avgH = Math.Sqrt(hullArea * 0.01 * 4 / Math.Sqrt(3));
   var outer = ScaleLoop(BuildRectLoop(0, 0, w, h), avgH, scale);

   double radius = 0.09, cx = 0.15, cy = 0.25;
   int nSeg = 32;
   var pts = new double[nSeg, 2];
   for (int i = 0; i < nSeg; i++)
   {
      double phi = 2 * Math.PI * i / nSeg;
      pts[i, 0] = cx + radius * Math.Cos(phi);
      pts[i, 1] = cy + radius * Math.Sin(phi);
   }
   var holeLoopLogical = BuildPolygonLoop(pts);
   var hole = ScaleLoop(new ContourLoop(LoopKind.Hole, holeLoopLogical.Faces), avgH, scale);

   var input = new AdvancingFrontInput { Outer = outer, Holes = new List<ContourLoop> { hole } };
   try
   {
      var result = AdvancingFront.Triangulate(input, 90.0);
      double expectedArea = w * h - Math.PI * radius * radius;
      CheckResult("Round hole d=180mm", result, expectedArea * scale * scale);
   }
   catch (Exception ex)
   {
      Console.WriteLine($"FAIL  Round hole d=180mm: {ex.Message}");
      failed++;
   }
}

// ─────────────────────────────────────────────────────────────────

void RunGeometryTriangulation(string name, ContourLoop outer, List<(double x0,double y0,double x1,double y1)>? holeRects,
   double avgH, double scale, double expectedArea)
{
   var holes = new List<ContourLoop>();
   if (holeRects != null)
      foreach (var (x0, y0, x1, y1) in holeRects)
         holes.Add(BuildRectHoleLoop(x0, y0, x1, y1, avgH, scale));

   var input = new AdvancingFrontInput { Outer = ScaleLoop(outer, avgH, scale), Holes = holes };
   try
   {
      var result = AdvancingFront.Triangulate(input, 90.0);
      CheckResult(name, result, expectedArea * scale * scale);
   }
   catch (Exception ex)
   {
      Console.WriteLine($"FAIL  {name}: {ex.Message}");
      failed++;
   }
}

void RunCircle(string name, double radius, double avgH, double scale)
{
   var face = ContourFace.FullCircle(0, 0, radius * scale, avgH * scale, LoopKind.Hull);
   var loop = new ContourLoop(LoopKind.Hull, new List<ContourFace> { face });
   var input = new AdvancingFrontInput { Outer = loop };
   var result = AdvancingFront.Triangulate(input, 90.0);
   double expectedArea = Math.PI * radius * radius * scale * scale;
   CheckResult(name, result, expectedArea);
}

void RunConstrainedSegment(string name, double w, double h, double avgH, double scale)
{
   var outer = ScaleLoop(BuildRectLoop(0, 0, w, h), avgH, scale);
   // h сегмента должен быть не меньше его длины (~0.1414), иначе §2.1 разобьёт его на
   // несколько подотрезков и прямого ребра segA-segB в сетке не будет в принципе.
   double segH = 0.2;
   var segA = new ContourNode(0.1 * scale, 0.2 * scale, segH * scale);
   var segB = new ContourNode(0.2 * scale, 0.3 * scale, segH * scale);
   var input = new AdvancingFrontInput
   {
      Outer = outer,
      Constraints = new List<ConstrainedElement> { ConstrainedElement.OfSegment(segA, segB) }
   };
   var result = AdvancingFront.Triangulate(input, 90.0);

   // Точное совпадение ребра сетки с constrained-сегментом и полная гарантия "рёбра его не
   // пересекают" требуют edge recovery (перестроения соседних треугольников после базовой
   // триангуляции) — этого нет ни в спеке/design doc, ни в текущей реализации (случай B.1,
   // §3.2, строит "ухо" без проверок вовсе — известное ограничение, constrained elements пока
   // не используются в CScore/Geo.cs, см. design doc §8/§10). Здесь проверяется то, что
   // реально гарантируется уже сейчас: узлы сегмента присутствуют в сетке как вершины
   // (через Тип 3 в CandidateSearch.FindCandidateT).
   bool nodesPresent = result.Nodes.Any(n => Close(n, segA.X, segA.Y))
      && result.Nodes.Any(n => Close(n, segB.X, segB.Y));

   Console.WriteLine(nodesPresent
      ? $"PASS  {name}: узлы сегмента присутствуют в сетке"
      : $"FAIL  {name}: узлы constrained-сегмента не найдены среди узлов сетки");
   if (nodesPresent) passed++; else failed++;
}

bool Close(double[] node, double x, double y) => Math.Abs(node[0] - x) < 1e-6 && Math.Abs(node[1] - y) < 1e-6;

void RunExpectedException(string name, ContourLoop badLoop)
{
   try
   {
      var input = new AdvancingFrontInput { Outer = badLoop };
      AdvancingFront.Triangulate(input, 90.0);
      Console.WriteLine($"FAIL  {name}: исключение не брошено");
      failed++;
   }
   catch (TriangulationException ex)
   {
      Console.WriteLine($"PASS  {name}: {ex.Message}");
      passed++;
   }
}

void CheckResult(string name, TriangulationResult result, double expectedAreaScaled)
{
   var edgeToTris = new Dictionary<(int, int), List<int>>();
   for (int t = 0; t < result.Triangles.Length; t++)
   {
      var tri = result.Triangles[t];
      for (int k = 0; k < 3; k++)
      {
         int a = tri[k], b = tri[(k + 1) % 3];
         var key = a < b ? (a, b) : (b, a);
         if (!edgeToTris.ContainsKey(key)) edgeToTris[key] = new List<int>();
         edgeToTris[key].Add(t);
      }
   }

   double totalArea = 0;
   foreach (var tri in result.Triangles)
   {
      double ax = result.Nodes[tri[0]][0], ay = result.Nodes[tri[0]][1];
      double bx = result.Nodes[tri[1]][0], by = result.Nodes[tri[1]][1];
      double cx = result.Nodes[tri[2]][0], cy = result.Nodes[tri[2]][1];
      totalArea += Math.Abs((bx - ax) * (cy - ay) - (cx - ax) * (by - ay)) / 2.0;
   }

   int badEdges = edgeToTris.Values.Count(v => v.Count > 2);
   double coverage = totalArea / expectedAreaScaled * 100;
   bool pass = coverage < 101.0 && coverage > 90.0 && badEdges == 0;
   Console.WriteLine($"{(pass ? "PASS" : "FAIL")}  {name}: tri={result.Triangles.Length}  cov={coverage:F1}%  bad={badEdges}");
   if (pass) passed++; else failed++;
}

// ───────────────────────── Построители контуров (в "логических" координатах, без масштаба) ─────────────────────────

ContourLoop BuildRectLoop(double x0, double y0, double x1, double y1)
{
   return BuildPolygonLoop(new double[,] { { x0, y0 }, { x1, y0 }, { x1, y1 }, { x0, y1 } });
}

ContourLoop BuildPolygonLoop(double[,] verts)
{
   int n = verts.GetLength(0);
   var nodes = new ContourNode[n];
   for (int i = 0; i < n; i++) nodes[i] = new ContourNode(verts[i, 0], verts[i, 1], 1.0); // h переопределяется в ScaleLoop
   var faces = new List<ContourFace>(n);
   for (int i = 0; i < n; i++) faces.Add(ContourFace.Linear(nodes[i], nodes[(i + 1) % n]));
   return new ContourLoop(LoopKind.Hull, faces);
}

ContourLoop BuildBowtieLoop()
{
   // Асимметричный самопересекающийся четырёхугольник (ненулевая площадь по формуле
   // шнурка — иначе исключение бросилось бы раньше, на проверке вырожденности площади,
   // а не на целевой проверке самопересечения).
   return BuildPolygonLoop(new double[,] { { 0, 0 }, { 4, 4 }, { 4, 0 }, { 0, 3 } });
}

ContourLoop BuildTwoFaceLoop()
{
   var a = new ContourNode(0, 0, 0.1);
   var b = new ContourNode(1, 0, 0.1);
   return new ContourLoop(LoopKind.Hull, new List<ContourFace> { ContourFace.Linear(a, b), ContourFace.Linear(b, a) });
}

ContourLoop BuildRectHoleLoop(double x0, double y0, double x1, double y1, double avgH, double scale)
{
   // Порядок вершин не важен — ContourValidator.ValidateAndNormalize сам развернёт
   // контур в CW, если знак площади не соответствует LoopKind.Hole (§1.1).
   var loop = BuildPolygonLoop(new double[,] { { x0, y0 }, { x1, y0 }, { x1, y1 }, { x0, y1 } });
   return ScaleLoop(new ContourLoop(LoopKind.Hole, loop.Faces), avgH, scale);
}

// Пересобирает контур с реальным масштабом и шагом h (Build*Loop выше строят "логическую" геометрию с h=1.0-заглушкой).
ContourLoop ScaleLoop(ContourLoop loop, double avgH, double scale)
{
   double h = Math.Max(avgH * scale, 1e-6);
   var faces = new List<ContourFace>(loop.Faces.Count);
   foreach (var f in loop.Faces)
   {
      var a = new ContourNode(f.A.X * scale, f.A.Y * scale, h);
      var b = new ContourNode(f.B.X * scale, f.B.Y * scale, h);
      faces.Add(ContourFace.Linear(a, b));
   }
   return new ContourLoop(loop.Kind, faces);
}

// ───────────────────────── Временная диагностика: серия DXF по шагам каскада B.2 ─────────────────────────

void GenerateCascadeDxfSeries()
{
   var watch = new HashSet<int> { 1, 31, 30, 29, 28, 27, 26, 25, 24, 23, 21, 39, 40 };
   int step = 0;
   string outDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\Downloads";

   FrontTriangulator.DebugWatchQ = watch;
   FrontTriangulator.DebugSnapshot = (qIdx, front, nodes, tris) =>
   {
      step++;
      string path = $"{outDir}\\cascade2_step{step:D2}_q{qIdx}.dxf";
      WriteCascadeDxf(path, front, nodes, tris, qIdx);
      Console.WriteLine($"Written {path} (front={front.Count} tris={tris.Count})");
   };

   var outer = ScaleLoop(BuildRectLoop(0, 0, 0.3, 0.5), 0.05, 8.0);
   var input = new AdvancingFrontInput { Outer = outer };
   AdvancingFront.Triangulate(input, 90.0);

   FrontTriangulator.DebugWatchQ = null;
   FrontTriangulator.DebugSnapshot = null;
}

void WriteCascadeDxf(string path, List<int> front, List<double[]> nodes, List<(int, int, int)> tris, int currentQ)
{
   var doc = new netDxf.DxfDocument();

   // Весь текущий живой фронт — замкнутая полилиния (вся область целиком, не только угол).
   var frontVerts = front.Select(i => new netDxf.Entities.Polyline2DVertex(nodes[i][0], nodes[i][1])).ToList();
   var frontPoly = new netDxf.Entities.Polyline2D(frontVerts, true) { Layer = new netDxf.Tables.Layer("FRONT") { Color = netDxf.AciColor.Blue } };
   doc.Entities.Add(frontPoly);

   // Все уже построенные треугольники — каждый отдельной замкнутой полилинией.
   foreach (var (a, b, c) in tris)
   {
      var verts = new List<netDxf.Entities.Polyline2DVertex>
      {
         new(nodes[a][0], nodes[a][1]), new(nodes[b][0], nodes[b][1]), new(nodes[c][0], nodes[c][1])
      };
      var poly = new netDxf.Entities.Polyline2D(verts, true) { Layer = new netDxf.Tables.Layer("TRIS") { Color = netDxf.AciColor.LightGray } };
      doc.Entities.Add(poly);
   }

   // Подписи узлов текущего фронта.
   foreach (int i in front)
   {
      var text = new netDxf.Entities.Text($"{i}", new netDxf.Vector2(nodes[i][0] + 0.02, nodes[i][1] + 0.02), 0.06)
      { Layer = new netDxf.Tables.Layer("LABELS") { Color = netDxf.AciColor.Yellow } };
      doc.Entities.Add(text);
   }

   // Текущий обрабатываемый узел q — подпись покрупнее.
   var qText = new netDxf.Entities.Text($"q={currentQ}", new netDxf.Vector2(nodes[currentQ][0] + 0.05, nodes[currentQ][1] - 0.08), 0.09)
   { Layer = new netDxf.Tables.Layer("CURRENT") { Color = netDxf.AciColor.Red } };
   doc.Entities.Add(qText);

   doc.Save(path);
}
