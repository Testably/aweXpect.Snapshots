# Public API

The package [aweXpect.Snapshots.PublicApi](https://www.nuget.org/packages/aweXpect.Snapshots.PublicApi) verifies the
public API of an assembly against a reviewed snapshot, so that every change of the public API shows up in a pull
request:

```csharp
await Expect.That(typeof(Album).Assembly).MatchesPublicApiSnapshot().UniqueFor.TargetFramework();
```

The public API is written as C# declarations, e.g.

```csharp
namespace MyLibrary
{
    public abstract class Album : System.IComparable<MyLibrary.Album>
    {
        protected Album(string title) { }
        public string Title { get; }
        public abstract System.Collections.Generic.IEnumerable<string> Tracks();
    }
}
```

It usually differs between the target frameworks of a library, so keep a snapshot per target framework of the test
project with `.UniqueFor.TargetFramework()`; each target framework of the test project loads the matching build of the
library. A build that the tests cannot run on, e.g. for `netstandard2.0`, can be loaded from its file and named after
its target framework:

```csharp
await Expect.That(Assembly.LoadFile(path)).MatchesPublicApiSnapshot().Named("netstandard2.0");
```

Otherwise it behaves like [`MatchesSnapshot()` for strings](01-matching-snapshots.md#strings), e.g. for the files and
[accepting changes](02-accepting-snapshots.md).
