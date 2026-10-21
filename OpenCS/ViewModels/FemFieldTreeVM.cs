using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CScore.Fem;
using CScore.Fem.Fields;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>Поле в дереве схемы: имя, число целей со значением (по кэшу) и точек таблицы.</summary>
public sealed class FemFieldItem(FemSchema schema, FemField field, int targets)
{
    public FemSchema Schema { get; } = schema;
    public FemField Field { get; } = field;
    public bool Enabled => Field.Enabled;
    public bool IsSpring => FemFieldQuantities.Get(Field.Quantity).Target == FemFieldTargetKind.MeshNode;
    /// <summary>Целей со значением по кэшу поля.</summary>
    public int TargetCount { get; } = targets;

    /// <summary>«C1 из ГТО — 24 951 КЭ, 8 029 точек».</summary>
    public string Title => string.Format(CultureInfo.CurrentCulture, Loc.S("FemFieldItemTitle"), Field.Name, TargetCount,
        Loc.S(IsSpring ? "FemFieldNodesUnit" : "FemFieldElementsUnit"), Field.Points.Count);
}

/// <summary>Ветка «Поля из таблиц» схемы (спека § 5.1): поля в порядке наложения.</summary>
public sealed class FemFieldsSubNode(FemSchema schema) : FemSubNode, INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public FemSchema Schema { get; } = schema;
    public ObservableCollection<FemFieldItem> Fields { get; } = [];
    public string Title => string.Format(CultureInfo.CurrentCulture, "{0} ({1})", Loc.S("FemFieldsNode"), Fields.Count);

    internal void Reload(DatabaseService db)
    {
        var counts = db.GetFemFieldTargetCounts(Schema.Id);
        Fields.Clear();
        foreach (var f in db.GetFemFields(Schema.Id))
            Fields.Add(new FemFieldItem(Schema, f, counts.GetValueOrDefault(f.Id)));
        PropertyChanged?.Invoke(this, new(nameof(Title)));
    }
}
