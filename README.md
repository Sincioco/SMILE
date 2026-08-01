# SMILE

SMILE is a tiny BASIC-style interpreter written in C#.

The first supported command is:

```basic
Print "Hello World"
```

It translates that command internally to:

```csharp
Console.WriteLine("Hello World");
```

It also supports smart quotes:

```basic
Print “Hello World”
```

## Requirements

- Windows
- .NET SDK 10 or newer
- Optional: GitHub CLI for creating and pushing the repo

## Run

From PowerShell:

```powershell
cd C:\SMILE
dotnet run
```

Then type:

```basic
Print "Hello World"
```

or:

```basic
Print “Hello World”
```

To quit:

```basic
Exit
```

## Current Commands

| SMILE Command | Meaning |
|---|---|
| `Print "text"` | Prints text to the console |

## License

SMILE uses the same licensing as the PMT project: GNU Affero General Public License v3.0. See [LICENSE](LICENSE).

## Future Ideas

Possible next commands:

```basic
Let Name = "Sin"
Print Name
Input Name
If Name = "Sin" Then Print "Hello Sin"
Goto Start
```
