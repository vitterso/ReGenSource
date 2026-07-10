# ReGenSource

I hate RESX files.

There. I said it.

I hate them because they are a pain to work with. They are not human-readable, they are not merge-friendly, they are never consistent in tabs/spaces or comments, and they are a pain to manage.

I hate them so much that I decided to write a tool to replace them.

## What is ReGenSource?

ReGenSource is a tool that generates source code with localized resources from a JSON file.

## How can I use this in my project?

Step one is to create a JSON file with the resources you want to localize. The file name must end with `.res.json`.

The JSON file must be added to your project file, along with a reference to the ReGenSource package.

```xml
<ItemGroup>
    <AdditionalFiles Include="Resources.res.json" />
    <PackageReference Include="ReGenSource" Version="0.1.0-ci.17" />
</ItemGroup>
```

The JSON file should be structured like this:

```json
{
   "namespace": "MyApp.Resources",
   "class": "MyResources",
   "resources": [
      {
         "name": "Welcome",
         "default": "Welcome to my application",
         "translations": {
            "pt": "Bem-vindo ao meu aplicativo",
            "nb,nn": "Velkommen til min applikasjon"
         }
      }
   ]
}
```

### Namespace

The namespace is the namespace that the generated class will be placed in.

By default, the namespace is `ReGenSource`.

### Class

The class is the name of the class that will be generated.

By default, the class is `Resources`.

### Resources

The resources is an array of resources that will be generated. Each resource has a name, a default value, and a set of translations. The default value is used when no translation is found for the current culture.

### CultureDefinition

By default the culture is defined by the `Thread.CurrentThread.CurrentUICulture.TwoLetterISOLanguageName` property. This is what is compared with the translation languages.

If you need a different way to define the culture, you can add a `CultureDefinition` property to the JSON file. This property should be a string that will be used as the culture definition in the generated code.

```json
"CultureDefinition": "global::System.Threading.Thread.CurrentThread.CurrentUICulture.TwoLetterISOLanguageName"
```

### ClassAccessModifier

By default the class is generated as a `public` class. It can however be set as `internal` by overriding the `ClassAccessModifier` property:

```json
"ClassAccessModifier": "Internal"
```

### Resources

#### Name

The name of the resource, used as the property name in the generated class.

#### Default

This is the translation to fallback to when no other translation is found for the given language.

#### Translations

Keys support comma-separated values to allow for multiple languages to be used for the same translation.

#### Variables

A translation (or the default value) may contain named placeholders written as `{name}`, where the name is a valid C# identifier. When a resource contains any placeholder, the generated member becomes a **method** that takes one `object?` parameter per distinct placeholder and formats the localized string with `string.Format`:

```json
{
   "name": "Welcome",
   "default": "Welcome to my application, {name}",
   "translations": {
      "nb,nn": "Velkommen til min applikasjon, {name}",
      "sv": "Välkommen till min applikation"
   }
}
```

generates:

```csharp
public static string Welcome(object? @name) => string.Format(/* culture switch */, @name);
```

Notes:

- The parameter list is the union of all placeholder names across the default and every translation, in order of first appearance. A translation that omits a placeholder (like `sv` above) simply ignores the extra argument.
- Resources without any placeholder keep the original property form (e.g. `public static string Welcome`), so this is backward compatible.
- Any literal `{` or `}` that is not part of a placeholder is emitted escaped (`{{` / `}}`) so `string.Format` treats it as a literal brace.
