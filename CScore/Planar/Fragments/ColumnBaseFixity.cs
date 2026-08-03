namespace CScore.Planar.Fragments
{
    /// <summary>Заделка низа многоэтажной колонны (Срез 8.2): только жёсткая или её
    /// отсутствие. Упругие/нелинейные пружины — отдельный следующий срез.</summary>
    public enum ColumnBaseFixity
    {
        /// <summary>Низ колонны свободен — опирание задаётся собственными Boundaries
        /// нижнего уровня (например, фундаментной плиты).</summary>
        None,
        /// <summary>Полная 6-DOF фиксация anchor-узла нижнего уровня.</summary>
        Fixed
    }
}
