using CScore;
using OpenCS.Services;
using OpenCS.Utilites;

using System.ComponentModel;
using System.Globalization;

namespace OpenCS.ViewModels
{
   /// <summary>
   /// Редактор имени набора усилий в формате СП 20 (префикс, γf, группа) — правит имя черновика набора. Имя
   /// меняется только при правке полей пользователем: открытие набора с именем не в формате СП 20 («РСУ 1») его не
   /// переписывает. γf — пусто или 0 = из настроек расчёта; иной не числовой или отрицательный ввод — ошибка поля, имя
   /// не меняется.
   /// </summary>
   public class ForceSetNameEditorVM : ViewModelBase, IDataErrorInfo
   {
      static readonly char[] Codes = ['G', 'L', 'Q', 'A'];

      readonly ForceSet _draft;
      readonly Action _onChanged;

      string _title;
      int    _kindIndex;
      string _gammaFText;
      string _group;
      string? _gammaError;

      public ForceSetNameEditorVM(ForceSet draft, Action onChanged)
      {
         _draft     = draft;
         _onChanged = onChanged;

         IsSp20Name = Sp20Name.TryParse(draft.Tag, out var n);
         _title      = IsSp20Name ? n.Title : (draft.Tag ?? "").Trim();
         _kindIndex  = Math.Max(0, Array.IndexOf(Codes, n.Prefix));
         _gammaFText = n.GammaF is double g ? g.ToString("G", CultureInfo.CurrentCulture) : "";
         _group      = n.Group ?? "";

         KindOptions =
         [
            Loc.S("ForceLoadKindG"),
            Loc.S("ForceLoadKindL"),
            Loc.S("ForceLoadKindQ"),
            Loc.S("ForceLoadKindA"),
         ];
      }

      public string[] KindOptions { get; }

      /// <summary>Имя набора — в формате СП 20 (иначе поля показывают имя целиком как название, вид — Q).</summary>
      public bool IsSp20Name { get; private set; }

      /// <summary>Имя не в формате СП 20 — подсказка, что правка полей соберёт имя «Q: …».</summary>
      public bool IsNotSp20Name => !IsSp20Name;

      public string Title
      {
         get => _title;
         set { _title = value ?? ""; OnPropertyChanged(); ApplyTag(); }
      }

      public int KindIndex
      {
         get => _kindIndex;
         set { _kindIndex = value; OnPropertyChanged(); ApplyTag(); }
      }

      public string GammaFText
      {
         get => _gammaFText;
         set { _gammaFText = value ?? ""; OnPropertyChanged(); ApplyTag(); }
      }

      public string Group
      {
         get => _group;
         set { _group = value ?? ""; OnPropertyChanged(); ApplyTag(); }
      }

      /// <summary>Имя набора (как в дереве).</summary>
      public string Preview => _draft.Tag;

      /// <summary>Ошибка поля γf; null — нет.</summary>
      public string? GammaError
      {
         get => _gammaError;
         private set { _gammaError = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasErrors)); }
      }

      /// <summary>Есть ошибки ввода — «Сохранить» откажет.</summary>
      public bool HasErrors => _gammaError != null;

      string IDataErrorInfo.Error => _gammaError ?? "";
      string IDataErrorInfo.this[string column] => column == nameof(GammaFText) ? _gammaError ?? "" : "";

      /// <summary>γf из текста: пусто или 0 — null (из настроек); false — не число или отрицательное.</summary>
      static bool TryGamma(string text, out double? gamma)
      {
         gamma = null;
         string s = (text ?? "").Trim();
         if (s.Length == 0) return true;
         if (!ForceSetAuthoring.TryParseNumber(s, false, out double g) || g < 0) return false;
         if (g > 0) gamma = g;
         return true;
      }

      void ApplyTag()
      {
         if (!TryGamma(_gammaFText, out var gamma))
         {
            GammaError = Loc.S("ForceLoadGammaInvalid");
            return;
         }
         GammaError = null;
         char prefix = _kindIndex >= 0 && _kindIndex < Codes.Length ? Codes[_kindIndex] : 'Q';
         string tag = new Sp20Name(prefix, gamma, _title ?? "", _group).Compose();
         if (!IsSp20Name)
         {
            IsSp20Name = true;
            OnPropertyChanged(nameof(IsSp20Name));
            OnPropertyChanged(nameof(IsNotSp20Name));
         }
         if (_draft.Tag == tag) return;
         _draft.Tag = tag;
         _onChanged();
         OnPropertyChanged(nameof(Preview));
      }
   }
}
