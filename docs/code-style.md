# C# code style

These rules apply to C# code in the repository, including tests. Mandatory conventions keep the code consistent; recommendations require a judgment about the readability of the specific solution.

## Mandatory conventions

### Files and types

- One independent type per file. This applies to classes, interfaces of any size, records, structs, and enums.
- The file name matches the type name. For a generic type, do not include type parameters in the file name.
- Private nested types are allowed when they are used only by the containing type. Do not widen accessibility only to move a type into its own file.
- `partial` is allowed for a concrete need, for example to separate generated and handwritten parts. Do not use it only to shrink a file.
- Separate data models and DTOs from logic classes by directories inside the relevant functional area, for example `Workspace/Models`. Do not collect unrelated DTOs from the whole project into one shared directory.
- Place a type by its role: a `record` may contain logic, and an ordinary `class` may be a DTO.

### Class member order

Arrange members in this order:

1. Properties, indexers, and events.
2. Constructors.
3. Methods.
4. Private nested types, if needed.
5. Constants and fields.

- Within each group, follow accessibility order: `public`, `internal`, `protected internal`, `protected`, `private protected`, `private`.
- Public and internal properties therefore come at the start of the class, protected fields come before private fields, and private fields end the class.
- Keep overloads of the same method together; accessibility order may be broken for that. Place a static constructor before instance constructors.
- When moving existing fields, keep a semantically significant initializer order. Preserving behavior matters more than mechanical sorting.

### Formatting and naming

- Use a four-space indent and place braces on their own lines.
- Keep braces even for a single-statement body.
- Use `PascalCase` for types, methods, properties, and constants, `camelCase` for parameters and local variables, and `_camelCase` for private and protected fields, including static fields.
- Leave short calls on one line. Break long calls and complex arguments at meaningful boundaries; when the argument list is expanded, put one argument per line.
- The line-length guide is 120 characters. This is not a hard limit: do not split string literals, URLs, and other indivisible fragments only to hit the number.
- Break long call chains at operations. A short chain of simple calls may stay on one line.
- Use `var` when the type is obvious from the expression or the nearest context. Name the type explicitly when that makes the result clear without jumping to the called method's declaration.
- Use expression-bodied members for simple expressions, short conversions, and delegation. Format complex calculations and branching as an ordinary body.
- Give variables meaningful names. `i`, `sb`, and `ex` are acceptable in a short obvious context; expand ambiguous abbreviations such as `tm`, `emIdx`, and `stIdx`.
- Give async methods an `Async` suffix, and put `CancellationToken` last. Exceptions are allowed for names fixed by an external contract. When renaming, keep the external names of MCP tools.
- Read the applicable `.editorconfig` files if they exist, and follow their settings. Do not assume the standard C# formatter will provide meaningful line breaks or enforce the line-length guide.

### Comments and bug fixes

- Comment non-obvious decisions, external-library limitations, important operation order, race conditions, and workarounds.
- Explain why the decision was made and what an obvious simplification would cost. Do not narrate the code, and do not require the reader to know development stages such as "epoch 3".
- If the solution came from investigating a specific issue or document, add a link and briefly state the material constraint. The comment must be understandable without following the link.
- In a bug fix, record the wrong assumption and the failure scenario when the mistake would otherwise be easy to repeat. Do not keep the previous implementation commented out; code history stays in Git.
- Capture a reproducible failure with a regression test when that is practical. The comment explains the constraint; the test checks the behavior.
- Add XML documentation where it states a contract, preconditions, result, or limitations. Avoid documentation that repeats the member name.

For example, a useful comment on test-result handling:

```csharp
// A non-zero exit code can come from a neighboring project with no filter matches.
// To decide the result, also account for the tests that actually ran.
```

## Readability and structure recommendations

### Methods and local functions

- The target method-body length is up to 40 lines. Above 60 lines, reconsider the decomposition. This is a guide for methods of any accessibility, not a mandatory automatic split.
- Judge the body without the signature, attributes, and XML documentation. Local functions remain part of the method's size; extracting them does not bypass this guide.
- Extract self-contained stages. A method that coordinates a scenario should read as a sequence of those stages, without dropping into the detail of each one.
- A coherent linear algorithm may stay longer if splitting it would make it harder to understand. Do not create empty `ProcessPart1` and `ProcessPart2` methods just to hit a line count.
- Local functions are allowed in methods of any accessibility and length when the operation is needed only by that method and is clear in its context.
- Prefer a `static` local function when it does not need to capture outer variables. A large capture of mutable state is a reason to reconsider the function's boundary.
- Prefer early `return` and `continue` over extra nesting when they make the code easier to read.

### Responsibilities, parameters, and state

- Separate computation from presentation of the result. An internal operation returns a structured result, and an MCP handler or formatter turns it into text.
- Move repeated telemetry, result logging, and error handling into a shared mechanism only when the behavior is actually the same. Do not hide different scenarios in order to remove lines that merely look similar.
- Combine related parameters into meaningful types, especially groups of similar strings such as configuration/platform/framework. On the external MCP boundary, parameters may stay flat.
- For non-obvious boolean behavior switches, use named arguments, an enum, or separate operations. Clear flags such as `includePreview` are acceptable.
- Group related state and the results of one operation instead of many independent flags and `_last...` fields, when the values share a lifetime and consistency constraints.
- Keep class responsibilities specific. Do not accumulate unrelated operations in `UtilityTools`, `*Helper`, and `*Manager`; at the same time, do not introduce classes and interfaces only to shrink files.
- Use LINQ for clear collection transformations, and ordinary loops for algorithms with state, several conditions, and side effects.
- Keep cancellation handling consistent, and apply `ConfigureAwait(false)` deliberately with the execution-context dependency in mind. Do not change cancellation or await semantics as part of a purely stylistic edit.

## Applying the rules to existing code

- Follow the rules when creating and changing code. Do not expand an ordinary task into a mass reformat or a reorganization of the repository.
- Separate mass formatting from changes to structure and behavior, so the result is easier to review.
- Use the configured formatter and analyzers for mechanical rules. When checking with `dotnet format --verify-no-changes --include …`, name the specific project or solution and the affected files; the ellipsis stands for the path list, not a literal argument.
- The formatter checks only the rules it supports and that are enabled. Judge method and class boundaries, name quality, and whether comments are useful separately.
