using CScore;
using OpenCS.Utilites;

var dbPath = Environment.GetEnvironmentVariable("LIMIT_FORCE_DB")
    ?? @"C:\Users\palex\Downloads\test_prj.db";

LimitForceSolverFast.DebugTrace = msg => Console.WriteLine($"  [fast] {msg}");

var db = new DatabaseService(dbPath);
db.LoadAll();

// задача 3: limit_moment, section_id=1
const int taskNum = 3;
var taskRow = db.CalcTasks.FirstOrDefault(t => t.Num == taskNum)
    ?? throw new InvalidOperationException($"Task num={taskNum} not found");

var p = LimitForceParams.Parse(taskRow.ParamsJson);
double n = p.N ?? 0, mx = p.Mx ?? 0, my = p.My ?? 0;

var section = db.CrossSections.First(s => s.Id == taskRow.SectionId);
section.ResolveAndBuildDiagramms();

var adapter = new CrossSectionLimitAdapter(section, taskRow.CalcType);
Console.WriteLine($"DB: {dbPath}");
Console.WriteLine($"Task: {taskRow.Tag} kind={taskRow.Kind} calc={taskRow.CalcType} solver={p.Solver}");
Console.WriteLine($"Section: {section.Tag}, contour={adapter.ContourVertices.Count()}, rebar={adapter.RebarPoints.Count()}, epsCu={adapter.EpsCu}");
Console.WriteLine($"Load: N={n}, Mx={mx}, My={my}");
Console.WriteLine();

Console.WriteLine("=== Fast solver ===");
var fast = new LimitForceSolverFast(section, taskRow.CalcType);
var rf = taskRow.Kind switch
{
    "limit_moment" => fast.MomentFactor(n, mx, my),
    "limit_axial"  => fast.AxialFactor(n, mx, my),
    _              => fast.AllFactor(n, mx, my),
};
Console.WriteLine($"k={rf.Factor:G8} conv={rf.Converged} iter={rf.Iterations} newton={rf.NewtonIterations} gov={rf.Governing}");
Console.WriteLine();

Console.WriteLine("=== Bisection solver ===");
var bisect = LimitForceSolver.ForCrossSection(section, taskRow.CalcType);
var rb = taskRow.Kind switch
{
    "limit_moment" => bisect.MomentFactor(n, mx, my),
    "limit_axial"  => bisect.AxialFactor(n, mx, my),
    _              => bisect.AllFactor(n, mx, my),
};
Console.WriteLine($"k={rb.Factor:G8} conv={rb.Converged} iter={rb.Iterations} newton={rb.NewtonIterations} gov={rb.Governing}");

if (rf.Factor > 0 && rb.Factor > 0)
    Console.WriteLine($"\n|k_fast-k_bisect|/k_bisect = {Math.Abs(rf.Factor - rb.Factor) / rb.Factor:P4}");
