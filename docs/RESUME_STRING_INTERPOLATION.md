# String Interpolation - FIXED

## Status: RESOLVED

String interpolation (`$"Value: {x}"`) now works correctly!

## Test Output (After Fix)
```
[StrInterp] x.ToString() = '56' len=2
[StrInterp] concat = 'Value: 56' len=9
[StrInterp] result = 'Value: 42' len=9           ← NOW WORKS!
[StrInterp] TestMultipleValues: '10 + 20 = 30' len=12
[StrInterp] TestStringValues: 'Hello, Test!' len=12
```

## Root Cause

The issue was in `CompileNewobj` when handling string constructors like `newobj string(char[], int, int)`:

1. **MemberRef token `0x0A00009E`** referenced `string..ctor(char[], int, int)`
2. The AOT registry had this registered as a factory method with `HasThis=false` and no MethodTable
3. `CompileNewobj` required `ctor.MethodTable != null` to proceed
4. Since the factory method had `MethodTable == null`, it fell back to using the raw token as a MethodTable
5. This caused objects to have the token (0x0A00009E) as their MethodTable instead of a valid one
6. Virtual calls (like `ToString()`) crashed when dereferencing the invalid MethodTable

## Fixes Applied

### 1. Halt on JIT Failure (JitStubs.cs)

Added `CPU.HaltForever()` when JIT compilation fails, so errors are immediately visible instead of crashing later:

```csharp
if (!result.Success)
{
    DebugConsole.Write("[JitStubs] FATAL: Failed to compile ...");
    DebugConsole.WriteLine("!!! SYSTEM HALTED - JIT compilation failure");
    CPU.HaltForever();
}
```

### 2. Remove Dangerous Token Fallback (ILCompiler.cs)

Changed `CompileNewobj` to fail with an error instead of using the raw token as a MethodTable:

```csharp
// OLD (dangerous):
ulong mtAddress = token;  // Fallback: use token as MethodTable!

// NEW (safe):
DebugConsole.Write("[JIT newobj] FAIL: unresolved token 0x");
DebugConsole.WriteHex(token);
return false;
```

### 3. Factory Constructor Support (ILCompiler.cs)

Added special handling for factory-style constructors (like `String..ctor`):

```csharp
// Special case: Factory-style constructors (like String..ctor)
// These have NativeCode but no MethodTable - they allocate and return the object themselves
if (resolved && ctor.IsValid && ctor.NativeCode != null && ctor.MethodTable == null && !ctor.HasThis)
{
    // Factory method: just call it with the args and it returns the new object
    // Pop args, set up calling convention, call factory, push result
    ...
}
```

This correctly handles `newobj string(char[], int, int)` by:
1. Recognizing it's a factory (NativeCode set, MethodTable null, HasThis false)
2. Popping the 3 arguments (char[], startIndex, length)
3. Calling the factory method directly
4. Pushing the returned string onto the eval stack

## Key Files Modified

- `/home/shane/protonos/src/kernel/Runtime/JIT/JitStubs.cs` - Added halt on failure
- `/home/shane/protonos/src/kernel/Runtime/JIT/ILCompiler.cs` - Factory constructor support

## How String Interpolation Works

The C# compiler transforms `$"Value: {x}"` into:
```csharp
DefaultInterpolatedStringHandler handler = new DefaultInterpolatedStringHandler(7, 1);
handler.AppendLiteral("Value: ");
handler.AppendFormatted(x);
string s = handler.ToStringAndClear();
```

Which eventually calls `StringBuilder.ToString()`:
```csharp
public override string ToString()
{
    return new string(_buffer, 0, _length);  // Uses factory constructor
}
```

The `newobj string(char[], int, int)` is now properly handled as a factory call.
