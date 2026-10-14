using System.Text;
using OpenCS.Cli;

// Кириллица в консоли Windows (кодовая страница по умолчанию — OEM).
Console.OutputEncoding = Encoding.UTF8;
return await CliApp.RunAsync(args, Console.Out, Console.Error);
