---
name: runtime-gotchas
description: Cities Skylines runs Mono 2.x with .NET 3.5 profile. Many compile-time-valid .NET 4.0+ APIs throw MissingMethodException at runtime.
metadata:
  type: feedback
---

# Runtime gotchas — Mono 2.x / .NET 3.5

**Rule:** APIs that the compile-time reference assemblies define but the actual Mono 2.x runtime
does NOT have will throw `MissingMethodException` (or similar) at runtime. The compiler does NOT
warn you. Always verify NEW System.Xml.Linq / System.Net.Http / 4.0+ API calls before committing.

**Why:** Cities Skylines is on Unity 5.6.7f1 (frozen since launch — Colossal Order can't upgrade
without breaking every mod). The runtime is Mono 2.0/3.5 profile. `mscorlib v2.0.0.0`,
`runtime v2.0.50727`. Confirmed during Phase 2.2 implementation when `XElement.Parse(string)`
threw `MissingMethodException: 'XmlReaderSettings.set_MaxCharactersFromEntities'` — that setter
is a .NET 4.0 API.

**How to apply:** When tempted to use any of the below, use the workaround instead:

| Forbidden API | Workaround |
| --- | --- |
| `XElement.Parse(string)` | `XElement.Load(XmlReader.Create(new StringReader(xml), new XmlReaderSettings()))` — see `Services\StyleSerializer.cs` |
| `Path.Combine(a, b, c)` 3-arg | nest 2-arg: `Path.Combine(Path.Combine(a, b), c)` |
| `String.IsNullOrWhiteSpace` | `String.IsNullOrEmpty` and trim manually if needed |
| `Tuple<>` (4.0+) | custom struct or `KeyValuePair<>` |
| `IReadOnlyList<>`, `IReadOnlyCollection<>` (4.5) | `IList<>`, `ICollection<>` |
| `HttpClient` (4.5) | `WebClient` / `HttpWebRequest` |
| `async`/`await`/`Task` | IEnumerator coroutines via `Unity.StartCoroutine` |

**Specific danger zones to audit before using:**
- Anything in `System.Xml.Linq` beyond `XElement.Load(XmlReader)` and `XElement.ToString`
- Anything in `System.Net.Http`
- Anything in `System.Threading.Tasks`
- Any method whose docs say "added in .NET 4.0" or later

**Static field initializer / cctor caveat:** if a static initializer throws (even from one of
the above), the whole type becomes permanently unusable (`TypeInitializationException` on every
reference). All initialization should be lazy + try/catch-wrapped — see [[Services\Log.cs]] for
the pattern (lazy `EnsurePath()` instead of static field assignment).
