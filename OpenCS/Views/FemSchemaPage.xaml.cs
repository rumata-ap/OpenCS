using CScore.Fem;
using System.Windows.Controls;

namespace OpenCS.Views;

public partial class FemSchemaPage : UserControl
{
    public FemSchemaPage(FemSchema schema, AppViewModel app)
    {
        InitializeComponent();
        DataContext = new FemSchemaPageVM(schema);
    }
}

public class FemSchemaPageVM
{
    public FemSchema Schema { get; }
    public FemSchemaPageVM(FemSchema schema) => Schema = schema;
}
