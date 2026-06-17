using CScore;
using CSmath;
using OpenCS.Utilites;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;

namespace OpenCS.ViewModels
{
   /// <summary>Одна точка σ(ε)-кривой. Уведомляет об изменении для DataGrid и DiagramCanvas.</summary>
   public class DiagramPoint : ViewModelBase
   {
      double _eps, _sig;

      public double Eps
      {
         get => _eps;
         set { _eps = value; OnPropertyChanged(); OnPropertyChanged(nameof(Branch)); }
      }

      public double Sig
      {
         get => _sig;
         set { _sig = value; OnPropertyChanged(); }
      }

      /// <summary>Ic (ε&lt;0), Origin (ε=0), It (ε&gt;0) — только для отображения.</summary>
      public string Branch => _eps < -1e-15 ? "Ic" : _eps > 1e-15 ? "It" : "Origin";
   }

   /// <summary>
   /// ViewModel редактора пользовательской диаграммы σ(ε).
   /// Коллекция точек, построение сплайнов, CSV-импорт, сохранение в БД.
   /// </summary>
   public class DiagramEditVM : ViewModelBase
   {
      readonly AppViewModel _app;
      readonly Diagramm _diagram;
      readonly bool _isNew;

      public DiagramEditVM(Diagramm diagram, AppViewModel app, bool isNew = false)
      {
         _diagram = diagram;
         _app     = app;
         _isNew   = isNew;
         Points   = LoadPoints(diagram);
      }

      public Diagramm Diagram => _diagram;
      public ObservableCollection<DiagramPoint> Points { get; }

      public string Tag
      {
         get => _diagram.Tag;
         set { _diagram.Tag = value; OnPropertyChanged(); }
      }

      public CalcType CalcType
      {
         get => _diagram.CalcType;
         set { _diagram.CalcType = value; OnPropertyChanged(); }
      }

      public MatType MaterialType
      {
         get => _diagram.MaterialType;
         set { _diagram.MaterialType = value; OnPropertyChanged(); }
      }

      /// <summary>
      /// Строит Ic и It из текущей коллекции Points.
      /// Ic ← точки с Eps ≤ 0, отсортированные по Eps (возрастание).
      /// It ← точки с Eps ≥ 0, отсортированные по Eps (возрастание).
      /// Точка Eps=0 входит в обе ветви.
      /// </summary>
      public void BuildSplines()
      {
         var sorted = Points.OrderBy(p => p.Eps).ToList();

         // Добавить начало координат если его нет
         if (!sorted.Any(p => Math.Abs(p.Eps) < 1e-15))
         {
            int idx = sorted.FindIndex(p => p.Eps > 0);
            if (idx < 0) idx = sorted.Count;
            sorted.Insert(idx, new DiagramPoint { Eps = 0, Sig = 0 });
         }

         var icPts = sorted.Where(p => p.Eps <= 1e-15).ToList();
         var itPts = sorted.Where(p => p.Eps >= -1e-15).ToList();

         if (icPts.Count >= 2)
            _diagram.Ic = new LSpline(
               icPts.Select(p => p.Eps).ToArray(),
               icPts.Select(p => p.Sig).ToArray());

         if (itPts.Count >= 2)
            _diagram.It = new LSpline(
               itPts.Select(p => p.Eps).ToArray(),
               itPts.Select(p => p.Sig).ToArray());
      }

      /// <summary>Вызвать BuildSplines и сохранить диаграмму в БД. Добавить в пул если новая.</summary>
      public void Save()
      {
         BuildSplines();
         _app.db.SaveDiagram(_diagram);
         if (_isNew && !_app.db.Diagrams.Contains(_diagram))
         {
            _app.db.Diagrams.Add(_diagram);
            _app.DiagramsLive.Add(_diagram);
         }
         _app.LogService.Info($"Диаграмма '{_diagram.Tag}' сохранена");
      }

      /// <summary>
      /// Импортирует точки из CSV-файла.
      /// Формат: заголовок (пропускается), затем строки «ε;σ» или «ε,σ».
      /// </summary>
      public void ImportCsv(string path)
      {
         var lines = File.ReadAllLines(path);
         char delim = lines.Any(l => l.Contains(';')) ? ';' : ',';
         var newPoints = new List<DiagramPoint>();

         foreach (var line in lines.Skip(1))
         {
            var parts = line.Split(delim);
            if (parts.Length < 2) continue;
            if (!double.TryParse(parts[0].Trim(), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out double eps)) continue;
            if (!double.TryParse(parts[1].Trim(), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out double sig)) continue;
            newPoints.Add(new DiagramPoint { Eps = eps, Sig = sig });
         }

         Points.Clear();
         foreach (var p in newPoints.OrderBy(p => p.Eps))
            Points.Add(p);
      }

      /// <summary>Добавить новую точку (0, 0) в конец коллекции.</summary>
      public void AddPoint() => Points.Add(new DiagramPoint { Eps = 0, Sig = 0 });

      /// <summary>Удалить точку из коллекции.</summary>
      public void RemovePoint(DiagramPoint p) => Points.Remove(p);

      // ─── helpers ───

      static ObservableCollection<DiagramPoint> LoadPoints(Diagramm d)
      {
         var list   = new List<DiagramPoint>();
         bool seenZero = false;

         void AddBranch(ISpline? sp)
         {
            if (sp?.X == null) return;
            for (int i = 0; i < sp.X.Length; i++)
            {
               bool isZero = Math.Abs(sp.X[i]) < 1e-15 && Math.Abs(sp.Y[i]) < 1e-15;
               if (isZero) { if (seenZero) continue; seenZero = true; }
               list.Add(new DiagramPoint { Eps = sp.X[i], Sig = sp.Y[i] });
            }
         }

         AddBranch(d.Ic);
         AddBranch(d.It);
         return new ObservableCollection<DiagramPoint>(list.OrderBy(p => p.Eps));
      }
   }
}
