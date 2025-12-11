# Resume: String Interpolation Debugging

## Current Status
String interpolation (`$"Value: {x}"`) returns an empty string (length 0) instead of "Value: 42".

## What Works
- `x.ToString()` returns `'42'` correctly (length 2)
- `string.Concat("Value: ", x.ToString())` returns `'Value: 42'` correctly (length 9)

## What's Broken
- `$"Value: {x}"` returns empty string (length 0)

## Test Output
```
[StrInterp] x.ToString() = '42' len=0x00000002     ← WORKS
[StrInterp] concat = 'Value: 42' len=0x00000009    ← WORKS
[StrInterp] s.Length=0x00000000                     ← BROKEN (should be 9)
```

## How String Interpolation Works

The C# compiler transforms `$"Value: {x}"` into:
```csharp
DefaultInterpolatedStringHandler handler = new DefaultInterpolatedStringHandler(7, 1);
handler.AppendLiteral("Value: ");
handler.AppendFormatted(x);
string s = handler.ToStringAndClear();
```

IL from FullTest.il (lines ~3914-3926):
```
IL_0091: ldloca.s V_3                    // Load address of handler (stack local)
IL_0093: ldc.i4.7                        // literalLength = 7
IL_0094: ldc.i4.1                        // formattedCount = 1
IL_0095: call instance void DefaultInterpolatedStringHandler::.ctor(int32, int32)
IL_009a: ldloca.s V_3
IL_009c: ldstr "Value: "
IL_00a1: call instance void DefaultInterpolatedStringHandler::AppendLiteral(string)
IL_00a6: ldloca.s V_3
IL_00a8: ldloc.0                         // Load x (42)
IL_00a9: call instance void DefaultInterpolatedStringHandler::AppendFormatted<int32>(!!0)
IL_00ae: ldloca.s V_3
IL_00b0: call instance string DefaultInterpolatedStringHandler::ToStringAndClear()
```

## Key Files

### DefaultInterpolatedStringHandler
`/home/shane/protonos/src/SystemRuntime/System/Runtime/CompilerServices/DefaultInterpolatedStringHandler.cs`

```csharp
[InterpolatedStringHandler]
public ref struct DefaultInterpolatedStringHandler
{
    private readonly StringBuilder _builder;  // <-- Field storage is suspect

    public DefaultInterpolatedStringHandler(int literalLength, int formattedCount)
    {
        int estimatedCapacity = literalLength + (formattedCount * 11);
        _builder = new StringBuilder(estimatedCapacity);  // <-- stfld here
    }

    public void AppendLiteral(string value)
    {
        _builder.Append(value);  // <-- ldfld then virtual call
    }

    public void AppendFormatted<T>(T value)
    {
        if (value is not null)
        {
            _builder.Append(value.ToString());
        }
    }

    public string ToStringAndClear()
    {
        string result = _builder.ToString();  // <-- ldfld then virtual call
        _builder.Clear();
        return result;
    }
}
```

## Likely Causes

The issue is likely one of:

1. **stfld for ref struct field** - The constructor creates a StringBuilder but `stfld _builder` may not be storing it correctly. Ref structs live on the stack, so `this` is a managed pointer to the stack location.

2. **ldfld for ref struct field** - When `AppendLiteral` or `ToStringAndClear` loads `_builder`, it may be reading from the wrong offset or getting null.

3. **Stack local addressing** - The `ldloca.s V_3` may not be computing the correct address for the ref struct on the stack.

4. **Field offset calculation** - The field offset for `_builder` in the ref struct may be wrong.

## Debugging Approach

Since we can't add EfiConsole.WriteLine to SystemRuntime (different assembly), we need to:

1. **Add JIT debug output** - Modify ILCompiler.cs to log when compiling DefaultInterpolatedStringHandler methods
2. **Check stfld/ldfld** - Look at `CompileStfld` and `CompileLdfld` in ILCompiler.cs for value type `this` handling
3. **Verify field offsets** - Check how field offsets are calculated for ref structs

## JIT Code Locations

- `CompileStfld` - `/home/shane/protonos/src/kernel/Runtime/JIT/ILCompiler.cs` (search for "case ILOpcode.Stfld")
- `CompileLdfld` - Same file, search for "case ILOpcode.Ldfld"
- Field offset calculation - Look for `GetFieldOffset` or similar

## Test File
`/home/shane/protonos/src/FullTest/Program.cs` - Contains `TestSimpleInterpolation()` method

## Build/Run Commands
```bash
./build.sh 2>&1    # Build (timeout: 120000)
./run.sh 2>&1      # Run in QEMU (timeout: 10000)
./kill.sh          # Kill QEMU after test
cat qemu.log       # View output
```

## Previous Fixes in This Session
- Fixed Int32.ToString() returning '0' instead of '42' by changing Int32Helpers.ToString() to read from `thisPtr` directly instead of `thisPtr + 8`
