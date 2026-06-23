using CScore.Fem;
using OpenCS.ViewModels;
using System.Windows.Controls;

namespace OpenCS.Views;

public partial class FemSchemaPage : UserControl
{
    public FemSchemaPage(FemSchema schema, AppViewModel app)
    {
        InitializeComponent();
        DataContext         = new FemSchemaPageVM(schema);
        view3D.DataContext  = new Fem3DVM(schema, app.db);
    }
}

public class FemSchemaPageVM
{
    public FemSchema Schema { get; }
    public FemSchemaPageVM(FemSchema schema) => Schema = schema;
}
