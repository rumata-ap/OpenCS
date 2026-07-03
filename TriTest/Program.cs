using CSTriangulation;
using System;
using System.Collections.Generic;

int passed = 0, failed = 0;

RunTest("Rectangle 0.3×0.5", BuildPolygon(new double[,]{
    {0,0},{0.3,0},{0.3,0.5},{0,0.5}
}, 0.05, 8.0), 0.3 * 0.5 * 8 * 8);

RunTest("L-shape", BuildPolygon(new double[,]{
    {0,0},{0.3,0},{0.3,0.25},{0.15,0.25},{0.15,0.5},{0,0.5}
}, 0.05, 8.0), (0.3*0.5 - 0.15*0.25) * 8 * 8);

RunTest("Rect with hole", BuildPolygonWithHole(
    outer: new double[,]{{0,0},{0.3,0},{0.3,0.5},{0,0.5}},
    hole:  new double[,]{{0.08,0.15},{0.22,0.15},{0.22,0.35},{0.08,0.35}},
    avgH: 0.04, scale: 8.0),
    (0.3*0.5 - 0.14*0.20) * 8 * 8);

Console.WriteLine($"\n=== {passed}/{passed+failed} PASSED ===");

// ─────────────────────────────────────────────────────────────────

void RunTest(string name, DiscretizedContour contour, double expectedAreaScaled)
{
    var result = AdvancingFront.Triangulate(contour, 90.0);

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

    int badEdges = 0;
    int shown = 0;
    foreach (var kv in edgeToTris)
    {
        if (kv.Value.Count <= 2) continue;
        badEdges++;
        shown++;
        if (shown <= 3)
        {
            Console.WriteLine($"  BAD edge ({kv.Key.Item1},{kv.Key.Item2}) × {kv.Value.Count} tris:");
            foreach (int ti in kv.Value)
            {
                var tri = result.Triangles[ti];
                Console.WriteLine($"    [{ti}] ({tri[0]},{tri[1]},{tri[2]})");
            }
        }
    }

    double coverage = totalArea / expectedAreaScaled * 100;
    bool pass = coverage < 101.0 && coverage > 90.0 && badEdges == 0;
    string mark = pass ? "PASS" : "FAIL";
    Console.WriteLine($"{mark}  {name}: tri={result.Triangles.Length}  cov={coverage:F1}%  bad={badEdges}");
    if (pass) passed++; else failed++;
}

// Строит контур из полигона (CCW), дискретизируя каждое ребро
DiscretizedContour BuildPolygon(double[,] verts, double avgH, double scale)
{
    int nv = verts.GetLength(0);
    var nodes = new List<double[]>();
    var isBoundary = new List<bool>();
    var hValues = new List<double>();
    var outerIdxs = new List<int>();
    double avgHs = avgH * scale;

    for (int j = 0; j < nv; j++)
    {
        double x0 = verts[j, 0] * scale, y0 = verts[j, 1] * scale;
        double x1 = verts[(j + 1) % nv, 0] * scale, y1 = verts[(j + 1) % nv, 1] * scale;
        double edgeLen = Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
        int nSeg = Math.Max(1, (int)Math.Round(edgeLen / avgHs));
        for (int s = 0; s < nSeg; s++)
        {
            double t = (double)s / nSeg;
            outerIdxs.Add(nodes.Count);
            nodes.Add(new[] { x0 + t * (x1 - x0), y0 + t * (y1 - y0) });
            isBoundary.Add(true);
            hValues.Add(avgHs);
        }
    }

    return new DiscretizedContour
    {
        Nodes = nodes.ToArray(), IsBoundary = isBoundary.ToArray(),
        HValues = hValues.ToArray(), OuterIndices = outerIdxs, HoleIndices = new List<List<int>>()
    };
}

// Строит контур с одним отверстием (hole — CW)
DiscretizedContour BuildPolygonWithHole(double[,] outer, double[,] hole, double avgH, double scale)
{
    int nv = outer.GetLength(0);
    var nodes = new List<double[]>();
    var isBoundary = new List<bool>();
    var hValues = new List<double>();
    var outerIdxs = new List<int>();
    double avgHs = avgH * scale;

    for (int j = 0; j < nv; j++)
    {
        double x0 = outer[j, 0] * scale, y0 = outer[j, 1] * scale;
        double x1 = outer[(j + 1) % nv, 0] * scale, y1 = outer[(j + 1) % nv, 1] * scale;
        double edgeLen = Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
        int nSeg = Math.Max(1, (int)Math.Round(edgeLen / avgHs));
        for (int s = 0; s < nSeg; s++)
        {
            double t = (double)s / nSeg;
            outerIdxs.Add(nodes.Count);
            nodes.Add(new[] { x0 + t * (x1 - x0), y0 + t * (y1 - y0) });
            isBoundary.Add(true);
            hValues.Add(avgHs);
        }
    }

    // hole — обход CW (разворачиваем CCW вершины)
    int nh = hole.GetLength(0);
    var holeIdxs = new List<int>();
    for (int j = nh - 1; j >= 0; j--)
    {
        double x0 = hole[j, 0] * scale, y0 = hole[j, 1] * scale;
        double x1 = hole[(j - 1 + nh) % nh, 0] * scale, y1 = hole[(j - 1 + nh) % nh, 1] * scale;
        double edgeLen = Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
        int nSeg = Math.Max(1, (int)Math.Round(edgeLen / avgHs));
        for (int s = 0; s < nSeg; s++)
        {
            double t = (double)s / nSeg;
            holeIdxs.Add(nodes.Count);
            nodes.Add(new[] { x0 + t * (x1 - x0), y0 + t * (y1 - y0) });
            isBoundary.Add(true);
            hValues.Add(avgHs);
        }
    }

    return new DiscretizedContour
    {
        Nodes = nodes.ToArray(), IsBoundary = isBoundary.ToArray(),
        HValues = hValues.ToArray(), OuterIndices = outerIdxs,
        HoleIndices = new List<List<int>> { holeIdxs }
    };
}
