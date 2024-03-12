using System.Globalization;
using MyApi;
using MyApi.Resources;

Console.WriteLine(MyResources.Welcome);

using (UiCultureScope.Begin(new CultureInfo("nb-NO")))
{
    Console.WriteLine(MyResources.Welcome);
}

Console.WriteLine(MyResources.Welcome);

using (UiCultureScope.Begin(new CultureInfo("sv-SE")))
{
    Console.WriteLine(MyResources.Welcome);
}
