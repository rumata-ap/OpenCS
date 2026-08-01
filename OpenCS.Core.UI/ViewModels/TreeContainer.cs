namespace OpenCS.ViewModels
{
   /// <summary>Контейнер группы узлов дерева навигации (замена WPF CompositeCollection/CollectionContainer).
   /// Заголовок — уже локализованная строка, Items — типизированная ObservableCollection доменных объектов.</summary>
   public sealed class TreeContainer
   {
      public string Header { get; }
      public object Items { get; }

      public TreeContainer(string header, object items)
      {
         Header = header;
         Items = items;
      }
   }
}
