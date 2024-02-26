using System.Globalization;
using MyApi;
using MyApi.Resources;

var write = () => Console.WriteLine(MyResources.Welcome);

write();

using (UiCultureScope.Begin(new CultureInfo("nb-NO")))
{
    write();
}

write();

using (UiCultureScope.Begin(new CultureInfo("sv-SE")))
{
    Console.WriteLine(MyResources.Goodbye);
}
