# FluidScript user manual

FluidScript is a small, statically checked scripting language that runs on a
deterministic virtual machine. Source is UTF-8, names are case-sensitive, and
either newlines or semicolons separate statements. Line comments start with
`//`; block comments use `/* ... */`.

## Your first program

```fluid
dim firstName: string = "Ada"
dim scores = [8, 13, 21]
dim total: int = 0

for index = 0 to 2
    total += scores[index]
end

print("{firstName}: {total}")
```

`print` writes a value to the output supplied by the host. Use `{expression}`
inside a string for interpolation; write `{{` or `}}` for literal braces.

## Values, declarations, and expressions

The built-in value kinds are `null`, `bool`, `int`, `decimal`, `string`,
`datetime`, `guid`, `byte`, arrays, `dict`, functions, declared objects, and
host-registered objects. Type hints are optional (`any` when omitted), but make
errors easier to find:

```fluid
dim count: int = 3
const greeting: string = "Hello"
dim enabled: bool = true
dim price: decimal = 19.95
dim tags: string[] = ["new", "sale"]
dim item = { "name": "book", "quantity": count }

item["quantity"] += 1
print("{greeting}, {item[\"name\"]}")
```

`dim name` initializes `name` to `null`; `const` requires an initializer and
cannot later be assigned. Arrays are mutable and use zero-based integer
indexes. Dictionaries are mutable, are keyed only by strings, and use
`value["key"]` access. A missing dictionary read is a runtime fault.

Integer arithmetic remains `int`; a mixed numeric expression promotes to
`decimal`. There is no implicit truthiness: conditions must be `bool`. `+`
concatenates only two strings, so use interpolation when formatting a number or
another value into text.

## Flow control and errors

```fluid
dim sum = 0
for value = 1 to 10 step 2
    sum += value
end

if sum > 20
    print("large")
else
    print("small")
end

try
    if sum == 25
        throw "unexpected total"
    end
catch error: string
    print("Handled: {error}")
finally
    print("done")
end
```

`for` bounds are inclusive and its `start`, `end`, and optional `step` are
evaluated once. A step cannot be zero. `while`, `break`, `continue`, and
`switch`/`case`/`otherwise` are also available; switch cases do not fall
through. `try` may have `catch`, `finally`, or both. An unhandled `throw` and
other runtime errors are returned to the host with a stable `FSxxxx` code and
source location.

## Functions, lambdas, and types

```fluid
function formatTotal(value: int, label: string = "Total"): string
    return "{label}: {value}"
end

type Person
    dim name: string
    dim visits: int

    function welcome(): string
        return "Welcome, {name}"
    end
end

dim person = Person("Ada", 1)
person.visits += 1
print(person.welcome())
print(formatTotal(person.visits, label = "Visits"))
```

Functions have lexical scope, positional parameters, optional defaults, and
named arguments (after all positional arguments). A function declared with a
non-`any`/non-`null` return type must return on every reachable path. Lambdas
capture surrounding variables by reference:

```fluid
dim multiplier = 3
dim triple = (value: int): int => value * multiplier
print(triple(7))
```

`type` creates a nominal object type. Fields initialize in declaration order;
its constructor accepts positional or named field values. Methods receive an
implicit `self`; fields may be referenced directly inside a method.

## JSON and the standard libraries

The JSON intrinsics work with JSON-native values (`null`, booleans, numbers,
strings, arrays, dictionaries, and declared-object fields):

```fluid
dim record = jsonDeserialize("{{\"name\":\"Ada\",\"active\":true}}")
print(record["name"])
print(jsonSerialize(record))
```

Use `jsonDeserializeAs(TypeName, text)` only when the JSON object has exactly
the declared fields for that FluidScript type. Date/time values, GUIDs, bytes,
functions, captured values, and regular expressions are not JSON-native.

Four libraries are registered automatically: `String`, `RegExp`, `Number`, and
`Math`. They are modeled after familiar JavaScript APIs, but use FluidScript's
finite `int`/`decimal` number model (there is no `NaN` or infinity).

| Library  | Common members                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                |
| -------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `String` | `String(value)`, `String.fromCharCode(...)`, `String.fromCodePoint(...)`, `String.raw(...)`; string members `length`, `at`, `charAt`, `charCodeAt`, `codePointAt`, `concat`, `includes`, `startsWith`, `endsWith`, `indexOf`, `lastIndexOf`, `localeCompare`, `padStart`, `padEnd`, `repeat`, `replace`, `replaceAll`, `search`, `slice`, `split`, `substr`, `substring`, `trim`, `trimStart`, `trimEnd`, `trimLeft`, `trimRight`, `toLowerCase`, `toUpperCase`, `normalize`, `isWellFormed`, `toWellFormed`, `match`, `matchAll`, `valueOf`, and `toString`. |
| `RegExp` | `RegExp(pattern, flags)`, `RegExp.escape(text)`; instance members `source`, `flags`, `lastIndex`, `global`, `ignoreCase`, `multiline`, `dotAll`, `unicode`, `sticky`, `test`, `exec`, and `toString`. Supported flags: `d`, `g`, `i`, `m`, `s`, `u`, `v`, `y`.                                                                                                                                                                                                                                                                                                |
| `Number` | `Number(value)`, `Number.parseInt(text, radix)`, `Number.parseFloat(text)`, `isFinite`, `isInteger`, `isNaN`, `isSafeInteger`, `EPSILON`, `MAX_SAFE_INTEGER`, `MIN_SAFE_INTEGER`; numeric members `toFixed`, `toString`, and `valueOf`.                                                                                                                                                                                                                                                                                                                       |
| `Math`   | Constants `E`, `LN2`, `LN10`, `LOG2E`, `LOG10E`, `PI`, `SQRT1_2`, `SQRT2`; functions `abs`, `ceil`, `floor`, `min`, `max`, `pow`, `random`, `round`, `sign`, `sqrt`, `trunc`, `cbrt`, `exp`, `log`, `log10`, `log2`, `sin`, `cos`, `tan`, `asin`, `acos`, `atan`, `atan2`, `hypot`, `imul`, `clz32`, and `fround`.                                                                                                                                                                                                                                            |

For example:

```fluid
dim title = "  FluidScript  ".trim().toUpperCase()
dim version = Number.parseInt("ff", 16)
dim distance = Math.hypot(3, 4)
dim word = RegExp("fluid", "i")

print(title)
print("hex: {version}; distance: {distance}; match: {word.test(title)}")
```

Library argument or operation failures are runtime fault `FS5018`. Consult the
library member names above rather than assuming every JavaScript behavior is
present.

## Imports: current scope

An import has either a dotted name or a string name:

```fluid
import reporting.sales
import "shared/validation"
```

An embedding application must supply a resolver. Today, imports validate and
compile the resolved dependency and detect missing modules or cycles. They do
**not** yet create an import namespace, execute a module, or make the imported
module's functions/variables callable by the importing script. Explicit
exports, namespaces, runtime module loading, and serialized import metadata
are planned but are not current language features.

## Practical limits

Execution has a host-configurable instruction limit (one million by default),
a 1,024-call-depth limit, and a 100,000-value stack limit. Give recursive
functions a terminating branch. The VM has no ambient file-system, network,
process, or CLR reflection access; an application must explicitly grant any
such behavior through host functions or registered types.
