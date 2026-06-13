using CScore;
using CScore.Combinations;
using OpenCS.Utilites;
using OpenCS.ViewModels;

using System.Globalization;
using System.Windows;

namespace OpenCS.Views
{
   public partial class ForceSetPropsDialog : Window
   {
      public ForceSetPropsDialog(ForceSet fs)
      {
         InitializeComponent();
         Owner = Application.Current.MainWindow;
         DataContext = new ForceSetPropsVM(fs);
         TitleBox.Focus();
         TitleBox.SelectAll();
      }

      void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
   }
}

namespace OpenCS.ViewModels
{
   public class ForceSetPropsVM : ViewModelBase
   {
      static readonly string[] Codes = ["G", "L", "Q", "A"];

      string _title;
      int    _kindIndex;
      string _gammaFText;
      string _group;

      public ForceSetPropsVM(ForceSet fs)
      {
         CurrentName = fs.Tag ?? "";

         var (lt, title, group, gammaF, _) = SP20Combinations.ParseForceSetName(fs.Tag);
         _title       = title;
         _kindIndex   = lt switch
         {
            NormLoadType.Permanent  => 0,
            NormLoadType.LongTerm   => 1,
            NormLoadType.ShortTerm  => 2,
            NormLoadType.Accidental => 3,
            _                       => 2
         };
         _gammaFText  = gammaF.HasValue ? gammaF.Value.ToString("G", CultureInfo.InvariantCulture) : "";
         _group       = group ?? "";

         KindOptions =
         [
            Loc.S("ForceLoadKindG"),
            Loc.S("ForceLoadKindL"),
            Loc.S("ForceLoadKindQ"),
            Loc.S("ForceLoadKindA"),
         ];
      }

      public string   CurrentName { get; }
      public string[] KindOptions { get; }

      public string Title
      {
         get => _title;
         set { _title = value; OnPropertyChanged(); OnPropertyChanged(nameof(Preview)); }
      }

      public int KindIndex
      {
         get => _kindIndex;
         set { _kindIndex = value; OnPropertyChanged(); OnPropertyChanged(nameof(Preview)); }
      }

      public string GammaFText
      {
         get => _gammaFText;
         set { _gammaFText = value; OnPropertyChanged(); OnPropertyChanged(nameof(Preview)); }
      }

      public string Group
      {
         get => _group;
         set { _group = value; OnPropertyChanged(); OnPropertyChanged(nameof(Preview)); }
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

      /// <summary>Вернуть вычисленное имя (вызывается из команды AppViewModel после DialogResult=true).</summary>
      public string ResultName => Preview;
   }
}
