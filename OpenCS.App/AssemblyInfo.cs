using System.Runtime.CompilerServices;

// Многие типы слоя приложения внутренние (internal); WPF-оболочка и тесты пользуются ими напрямую.
// Публичная поверхность — отдельный фасад API (Этап 2), а не этот проект.
[assembly: InternalsVisibleTo("OpenCS")]
[assembly: InternalsVisibleTo("OpenCS.Tests")]
