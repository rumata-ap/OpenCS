using CScore;
using CScore.Combinations;
using OpenCS.Utilites;

using System.Globalization;

namespace OpenCS.ViewModels
{
   /// <summary>
   /// Редактор имени набора усилий в формате СП20 (префикс, γf, группа).
   /// Обновляет <see cref="ForceSet.Tag"/> при изменении полей.
   /// </summary>
   public class ForceSetNameEditorVM : ViewModelBase
   {
      static readonly string[] Codes = ["G", "L", "Q", "A"];

      readonly ForceSet _model;
      readonly Action _onChanged;

      string _title;
      int    _kindIndex;
      string _gammaFText;
      string _group;

      public ForceSetNameEditorVM(ForceSet model, Action onChanged)
      {
         _model     = model;
         _onChanged = onChanged;

         var (lt, title, group, gammaF, _) = SP20Combinations.ParseForceSetName(model.Tag ?? "");
         _title     = title;
         _kindIndex = lt switch
         {
            NormLoadType.Permanent  => 0,
            NormLoadType.LongTerm   => 1,
            NormLoadType.ShortTerm  => 2,
            NormLoadType.Accidental => 3,
            _                       => 2
         };
         _gammaFText = gammaF.HasValue ? gammaF.Value.ToString("G", CultureInfo.InvariantCulture) : "";
         _group      = group ?? "";

         KindOptions =
         [
            Loc.S("ForceLoadKindG"),
            Loc.S("ForceLoadKindL"),
            Loc.S("ForceLoadKindQ"),
            Loc.S("ForceLoadKindA"),
         ];

         ApplyTag();
      }

      public string[] KindOptions { get; }

      public string Title
      {
         get => _title;
         set { _title = value; OnPropertyChanged(); ApplyTag(); }
      }

      public int KindIndex
      {
         get => _kindIndex;
         set { _kindIndex = value; OnPropertyChanged(); ApplyTag(); }
      }

      public string GammaFText
      {
         get => _gammaFText;
         set { _gammaFText = value; OnPropertyChanged(); ApplyTag(); }
      }

      public string Group
      {
         get => _group;
         set { _group = value; OnPropertyChanged(); ApplyTag(); }
      }

      public string Preview
      {
         get
         {
            string prefix = _kindIndex >= 0 && _kindIndex < Codes.Length ? Codes[_kindIndex] : "Q";
            string gammaText = "";
            string gStr = (_gammaFText ?? "").Trim().Replace(',', '.');
            if (gStr.Length > 0 &&
                double.TryParse(gStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double g) &&
                g > 0.0)
               gammaText = $"(γf={g:G})";
            string grp   = (_group ?? "").Trim();
            string title = (_title ?? "").Trim();
            string name  = $"{prefix}{gammaText}: {title}";
            if (grp.Length > 0) name += $" [{grp}]";
            return name;
         }
      }

      void ApplyTag()
      {
         string tag = Preview;
         if (_model.Tag == tag) return;
         _model.Tag = tag;
         _onChanged();
         OnPropertyChanged(nameof(Preview));
      }
   }
}
