using CScore;
using OpenCS.Services;
using OpenCS.Utilites;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace OpenCS.ViewModels
{
   /// <summary>Одна точка σ(ε)-кривой на странице: ε безразмерная, σ в МПа. Уведомляет DataGrid и DiagramCanvas.</summary>
   public class DiagramPoint : ViewModelBase
   {
      double _eps, _sig;

      public double Eps
      {
         get => _eps;
         set { _eps = value; OnPropertyChanged(); OnPropertyChanged(nameof(Branch)); }
      }

      /// <summary>Напряжение, МПа (в базе и расчёте — кПа).</summary>
      public double Sig
      {
         get => _sig;
         set { _sig = value; OnPropertyChanged(); }
      }

      bool _isCharacteristic;

      /// <summary>Помечена ли точка как характерная (излом) для контурного интеграла.</summary>
      public bool IsCharacteristic
      {
         get => _isCharacteristic;
         set { _isCharacteristic = value; OnPropertyChanged(); }
      }

      /// <summary>Ic (ε&lt;0), Origin (ε=0), It (ε&gt;0) — только для отображения.</summary>
      public string Branch => _eps < -1e-15 ? "Ic" : _eps > 1e-15 ? "It" : "Origin";
   }

   /// <summary>
   /// ViewModel страницы диаграммы пула σ(ε). Правится черновик (обозначение, вид расчёта, тип материала, точки
   /// в МПа); диаграмма пула меняется только по «Сохранить» после проверки <see cref="DiagramAuthoring"/>.
   /// </summary>
   public class DiagramEditVM : ViewModelBase, IUnsavedChanges
   {
      const double KPaPerMPa = 1000;

      readonly AppViewModel _app;
      readonly Diagramm _diagram;
      bool _isNew;
      string _tag = "";
      CalcType _calcType;
      MatType _materialType;
      string _saved = "";

      public DiagramEditVM(Diagramm diagram, AppViewModel app, bool isNew = false)
      {
         _diagram = diagram;
         _app     = app;
         _isNew   = isNew;
         Points   = [];
         Points.CollectionChanged += (_, e) =>
         {
            if (e.NewItems != null) foreach (DiagramPoint p in e.NewItems) p.PropertyChanged += OnPointChanged;
            if (e.OldItems != null) foreach (DiagramPoint p in e.OldItems) p.PropertyChanged -= OnPointChanged;
            Touch();
         };
         Load();
      }

      public Diagramm Diagram => _diagram;
      public ObservableCollection<DiagramPoint> Points { get; }

      /// <summary>Виды расчёта для списка.</summary>
      public IReadOnlyList<CalcType> CalcTypes { get; } = Enum.GetValues<CalcType>();

      /// <summary>Типы материала, для которых диаграмма пула имеет смысл.</summary>
      public IReadOnlyList<MatType> MaterialTypes { get; } = DiagramAuthoring.MaterialTypes;

      /// <summary>Диаграмма ещё не записана в проект.</summary>
      public bool IsNew => _isNew;

      public string Tag
      {
         get => _tag;
         set { _tag = value; OnPropertyChanged(); Touch(); }
      }

      public CalcType CalcType
      {
         get => _calcType;
         set { _calcType = value; OnPropertyChanged(); Touch(); }
      }

      public MatType MaterialType
      {
         get => _materialType;
         set { _materialType = value; OnPropertyChanged(); Touch(); }
      }

      /// <summary>Заголовок страницы: обозначение записанной диаграммы и «*», пока есть несохранённые правки.</summary>
      public string Title => (_isNew ? Loc.S("NewDiagram") : _diagram.Tag) + (IsDirty ? " *" : "");

      public bool IsDirty => Snapshot() != _saved;

      public string DisplayName => string.IsNullOrWhiteSpace(_tag) ? Loc.S("NewDiagram") : _tag;

      void OnPointChanged(object? s, PropertyChangedEventArgs e)
      {
         if (e.PropertyName != nameof(DiagramPoint.Branch)) Touch();
      }

      void Touch()
      {
         OnPropertyChanged(nameof(IsDirty));
         OnPropertyChanged(nameof(Title));
      }

      string Snapshot() => string.Join("|", _tag, _calcType, _materialType,
         string.Join(";", Points.Select(p => FormattableString.Invariant($"{p.Eps:R},{p.Sig:R},{p.IsCharacteristic}"))));

      /// <summary>Заполняет черновик из диаграммы пула.</summary>
      void Load()
      {
         _tag = _diagram.Tag;
         _calcType = _diagram.CalcType;
         _materialType = _diagram.MaterialType;
         var cs = _diagram.CharacteristicStrains ?? [];
         Points.Clear();
         foreach (var p in DiagramAuthoring.ToPoints(_diagram))
            Points.Add(new DiagramPoint
            {
               Eps = p.Eps,
               Sig = p.Sig / KPaPerMPa,
               IsCharacteristic = cs.Any(e => Math.Abs(e - p.Eps) < 1e-12)
            });
         _saved = Snapshot();
         OnPropertyChanged(string.Empty);
      }

      /// <summary>
      /// Проверяет черновик и записывает его в диаграмму пула (новая — добавляется в пул). Ошибки — в журнал,
      /// диаграмма пула не меняется.
      /// </summary>
      public IReadOnlyList<FieldError> Save()
      {
         var pts = Points.Select(p => new DiagramPointData(p.Eps, p.Sig * KPaPerMPa)).ToList();
         var errors = DiagramAuthoring.ValidatePoints(pts).ToList();
         var draft = new Diagramm
         {
            Id = _diagram.Id, Tag = _tag, Type = _diagram.Type, CalcType = _calcType, MaterialType = _materialType
         };
         if (errors.Count == 0)
         {
            DiagramAuthoring.Apply(draft, pts, Points.Where(p => p.IsCharacteristic).Select(p => p.Eps));
            errors.AddRange(DiagramAuthoring.Validate(draft));
         }
         if (errors.Count > 0)
         {
            foreach (var e in errors) _app.LogService.Warning(Describe(e));
            return errors;
         }

         _diagram.Tag = draft.Tag;
         _diagram.CalcType = draft.CalcType;
         _diagram.MaterialType = draft.MaterialType;
         _diagram.Ic = draft.Ic;
         _diagram.It = draft.It;
         _diagram.CharacteristicStrains = draft.CharacteristicStrains;
         var saveErrors = DiagramAuthoring.Save(_app.db, _diagram);
         if (saveErrors.Count > 0)
         {
            foreach (var e in saveErrors) _app.LogService.Warning(Describe(e));
            return saveErrors;
         }
         if (_isNew && !_app.DiagramsLive.Contains(_diagram))
            _app.DiagramsLive.Add(_diagram);
         _isNew = false;
         _saved = Snapshot();
         Touch();
         OnPropertyChanged(nameof(IsNew));
         _app.LogService.Info(string.Format(Loc.S("DiagramSaved"), _diagram.Tag));
         return [];
      }

      /// <summary>Текст ошибки для журнала: «Строка N: …» для точек таблицы.</summary>
      string Describe(FieldError e)
      {
         if (e.Path.StartsWith("points[") && int.TryParse(e.Path[7..^1], out int i) && i >= 0 && i < Points.Count)
            return string.Format(Loc.S("DiagramErrRow"), i + 1, e.Message);
         return e.Message;
      }

      bool IUnsavedChanges.SaveChanges() => Save().Count == 0;

      void IUnsavedChanges.DiscardChanges() => Load();

      /// <summary>
      /// Импортирует точки из CSV: заголовок (пропускается), затем строки «ε;σ» (σ в МПа). Разделитель полей —
      /// «;» или табуляция; «,» — только если их нет (тогда запятая не может быть десятичной). Десятичный
      /// разделитель — «,» или «.». Возвращает число пропущенных строк.
      /// </summary>
      public int ImportCsv(string path)
      {
         var lines = File.ReadAllLines(path);
         char delim = lines.Any(l => l.Contains(';')) ? ';' : lines.Any(l => l.Contains('\t')) ? '\t' : ',';
         var newPoints = new List<DiagramPoint>();
         int skipped = 0;
         foreach (var line in lines.Skip(1))
         {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var parts = line.Split(delim);
            if (parts.Length < 2 || !NumberInput.TryParse(parts[0], out double eps) || !NumberInput.TryParse(parts[1], out double sig))
            {
               skipped++;
               continue;
            }
            newPoints.Add(new DiagramPoint { Eps = eps, Sig = sig });
         }

         Points.Clear();
         foreach (var p in newPoints.OrderBy(p => p.Eps))
            Points.Add(p);
         return skipped;
      }

      /// <summary>Текст CSV для выгрузки: заголовок и строки «ε;σ, МПа», числа с точкой.</summary>
      public string ExportCsv(string delimiter)
      {
         var sb = new StringBuilder();
         sb.AppendLine(string.Join(delimiter, "ε", Loc.S("DiagramSigmaMPa")));
         foreach (var p in Points.OrderBy(p => p.Eps))
            sb.AppendLine($"{p.Eps.ToString(CultureInfo.InvariantCulture)}{delimiter}{p.Sig.ToString(CultureInfo.InvariantCulture)}");
         return sb.ToString();
      }

      /// <summary>Добавить новую точку (0, 0) в конец коллекции.</summary>
      public void AddPoint() => Points.Add(new DiagramPoint { Eps = 0, Sig = 0 });

      /// <summary>Удалить точку из коллекции.</summary>
      public void RemovePoint(DiagramPoint p) => Points.Remove(p);
   }
}
