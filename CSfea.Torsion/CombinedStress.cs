namespace CSfea.Torsion;

/// <summary>
/// Комбинированные напряжения однородного упругого сечения: σzz от N,Mx,My (простая механика
/// материалов, без депланации/нелинейности — не путать с фибровым методом CScore для
/// нелинейного ЖБ) + τ от T,Vx,Vy (векторно, уже посчитанные компоненты из CSfea.Torsion) →
/// σvm (Мизес), σ11/σ33 (главные, из круга Мора для состояния [[σzz,τzx,τzy],[τzx,0,0],[τzy,0,0]]).
///
/// Знаковая конвенция Mx,My — своя, НЕ как в sectionproperties (см. CLAUDE.md "Sign Conventions"):
/// Mx=∫σ·y·dA, My=∫σ·x·dA (положительный Mx растягивает грань y&gt;0, положительный My — грань
/// x&gt;0, БЕЗ инверсии знака My). Формула ниже выведена заново из этих двух уравнений равновесия
/// (не портирована из sectionproperties, где Myy определён с обратным знаком).
/// </summary>
public static class CombinedStress
{
    /// <summary>
    /// σzz(x,y) от осевой силы N и изгибающих моментов Mx,My (координаты x,y — относительно
    /// центроида xc,yc). area,ixx,iyy,ixy — геометрические характеристики сечения
    /// (см. <see cref="TorsionGeoMoments"/>). Не зависит от E (простая механика материалов).
    /// </summary>
    public static double SigmaZz(double x, double y, double xc, double yc,
        double area, double ixx, double iyy, double ixy, double n, double mx, double my)
    {
        double xr = x - xc, yr = y - yc;
        double denom = ixx * iyy - ixy * ixy;
        double a = (my * ixx - mx * ixy) / denom;
        double b = (iyy * mx - ixy * my) / denom;
        return n / area + a * xr + b * yr;
    }

    /// <summary>
    /// σvm (Мизес) и главные напряжения σ11≥σ33 для состояния [[σzz,τzx,τzy],[τzx,0,0],[τzy,0,0]]
    /// (плоское сечение, σxx=σyy=τxy≈0 — приближение теории тонкостенных стержней).
    /// </summary>
    public static (double sigVm, double sig11, double sig33) Combine(double sigmaZz, double tauZx, double tauZy)
    {
        double tauR2 = tauZx * tauZx + tauZy * tauZy;
        double half = sigmaZz / 2.0;
        double root = Math.Sqrt(half * half + tauR2);
        double sig11 = half + root;
        double sig33 = half - root;
        double sigVm = Math.Sqrt(sigmaZz * sigmaZz + 3.0 * tauR2);
        return (sigVm, sig11, sig33);
    }
}
