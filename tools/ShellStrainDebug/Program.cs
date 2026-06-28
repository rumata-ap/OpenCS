using CScore;
using OpenCS.Tasks;
using OpenCS.Utilites;

var dbPath = Environment.GetEnvironmentVariable("SHELL_STRAIN_DB")
    ?? @"C:\Users\palex\Downloads\test_prj.db";
const int taskNum = 8;

var db = new DatabaseService(dbPath);
db.LoadAll();

var task = db.CalcTasks.First(t => t.Num == taskNum);
var plate = db.PlateSections.First(s => s.Id == task.SectionId);
var (cDiag, rDiag, layerDiags, _) =
    PlateMaterialResolver.Resolve(plate, db.Materials, task.CalcType);
var concrete = db.Materials.First(m => m.Id == plate.ConcreteMaterialId);
var rebar = db.Materials.First(m => m.Id == plate.RebarMaterialId);
var p = ShellStrainParams.Parse(task.ParamsJson);
double[] target = { p.Nx, p.Ny, p.Nxy, p.Mx, p.My, p.Mxy };

var solver = new ShellStrainSolver(plate, cDiag, rDiag, layerDiags,
    tolRes: p.TolRes, maxIter: p.MaxIter);

Console.WriteLine($"Task {taskNum}: {target[0]} {target[1]} {target[2]} | M {target[3]} {target[4]} {target[5]}");

var e1 = solver.Solve(target);
Print("elastic only", e1);

var r = solver.SolveRobust(target, concrete, rebar, task.CalcType);
Print("robust cascade", r);

static void Print(string label, ShellStrainSolverResult r)
{
    Console.WriteLine($"--- {label} ---");
    Console.WriteLine($"strategy={r.Strategy} conv={r.Converged} iter={r.Iterations} total={r.TotalIterations} resid={r.Residual:G6}");
    Console.WriteLine($"Ny: target vs result = {r.Forces.Ny:F4} (residual vector norm above)");
}
