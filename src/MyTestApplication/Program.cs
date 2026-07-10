using System.Globalization;
using MyApi;
using MyApi.Resources;

Console.WriteLine(MyResources.Welcome("Benjamin"));
using (UiCultureScope.Begin(new CultureInfo("nb-NO")))
{
    Console.WriteLine(MyResources.Welcome("Benjamin"));
}

Console.WriteLine(MyResources.Welcome("Benjamin"));
using (UiCultureScope.Begin(new CultureInfo("sv-SE")))
{
    Console.WriteLine(MyResources.Welcome("Benjamin"));
}

Console.WriteLine(MyResources.Notice);
Console.WriteLine($"Logo is {MyResources.Logo.Length} bytes");

Console.WriteLine(MyResources.Goodbye);
using (UiCultureScope.Begin(new CultureInfo("nn-NO")))
{
    Console.WriteLine(MyResources.Goodbye);
}
