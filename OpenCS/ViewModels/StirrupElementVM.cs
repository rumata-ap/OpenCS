using CScore;
using CScore.Sp63Shear;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>Строка таблицы одного элемента поперечного армирования.</summary>
public sealed class StirrupElementVM : ViewModelBase
{
    /// <summary>Создаёт строку таблицы для доменного элемента.</summary>
    public StirrupElementVM(StirrupElement element, int index)
    {
        Element = element ?? throw new ArgumentNullException(nameof(element));
        Index = index;
    }

    /// <summary>Доменный элемент.</summary>
    public StirrupElement Element { get; }

    /// <summary>Порядковый номер элемента в группе.</summary>
    public int Index { get; }

    /// <summary>Тип построения элемента.</summary>
    public string KindText => Element.Source?.Kind switch
    {
        StirrupElementKind.OffsetLoop => Loc.S("StirrupKindOffsetLoop"),
        StirrupElementKind.Cut => Loc.S("StirrupKindCut"),
        _ => Loc.S("StirrupKindManual")
    };

    /// <summary>Диаметр одного стержня, мм.</summary>
    public double DiameterMm => Element.BarDiameterM * 1000.0;

    /// <summary>Отступ, использованный при построении элемента, м.</summary>
    public double? OffsetM => Element.Source?.OffsetM;

    /// <summary>Направление среза.</summary>
    public string DirectionText => Element.Source?.Direction switch
    {
        StirrupCutDirection.Vertical => Loc.S("StirrupDirectionVertical"),
        StirrupCutDirection.Horizontal => Loc.S("StirrupDirectionHorizontal"),
        StirrupCutDirection.TwoPoints => Loc.S("StirrupDirectionTwoPoints"),
        _ => Loc.S("StirrupDirectionNone")
    };

    /// <summary>Положение линии среза, м.</summary>
    public double? PositionM => Element.Source?.Position;

    /// <summary>Длина центральной линии элемента, м.</summary>
    public double LengthM
    {
        get
        {
            var x = Element.CenterlineContour.X;
            var y = Element.CenterlineContour.Y;
            double length = 0.0;
            for (int i = 1; i < Math.Min(x.Count, y.Count); i++)
            {
                double dx = x[i] - x[i - 1];
                double dy = y[i] - y[i - 1];
                length += Math.Sqrt(dx * dx + dy * dy);
            }
            return length;
        }
    }

    /// <summary>Приведённая площадь ветвей в направлении Vy, м².</summary>
    public double AswVy => StirrupResolver.BranchAreas(Element).Vy;

    /// <summary>Приведённая площадь ветвей в направлении Vx, м².</summary>
    public double AswVx => StirrupResolver.BranchAreas(Element).Vx;

    /// <summary>Признак наличия параметрического источника для перестроения.</summary>
    public bool CanRebuild => Element.Source is { Kind: not StirrupElementKind.Manual };
}
