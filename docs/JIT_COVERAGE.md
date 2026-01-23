# JIT IL Opcode Test Coverage

This document tracks test coverage for every CIL (Common Intermediate Language) opcode.
The goal is comprehensive testing of all opcodes in all applicable scenarios.

## Legend

- **Status**: `[ ]` Not tested, `[~]` Partially tested, `[x]` Fully tested
- **Priority**: `P0` Critical, `P1` High, `P2` Medium, `P3` Low
- **Tests**: Number of tests needed / number implemented

---

## 1. Base Instructions (0x00-0x0D)

### 1.1 nop (0x00)
- **Description**: No operation
- **Stack**: ... → ...
- **Status**: [ ]
- **Priority**: P3
- **Tests Needed**: 3

#### Test Scenarios:
1. **Basic nop execution** - Verify nop doesn't affect program state
2. **Multiple consecutive nops** - Verify sequence of nops executes correctly
3. **Nop between operations** - Verify nop doesn't interfere with surrounding instructions

### 1.2 break (0x01)
- **Description**: Breakpoint instruction
- **Stack**: ... → ...
- **Status**: [ ]
- **Priority**: P3
- **Tests Needed**: 2
- **Notes**: Debugger trap, rarely used

#### Test Scenarios:
1. **Break execution without debugger** - Verify graceful handling when no debugger attached
2. **Break with stack values** - Verify stack is preserved across break instruction

### 1.3 ldarg.0 (0x02)
- **Description**: Load argument 0 onto stack
- **Stack**: ... → ..., value
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 18
- **Notes**: Instance methods: this pointer; Static methods: first parameter

#### Test Scenarios - Static Methods:
1. **Load int8 argument** - Verify sign-extension to int32
2. **Load uint8 argument** - Verify zero-extension to int32
3. **Load int16 argument** - Verify sign-extension to int32
4. **Load uint16 argument** - Verify zero-extension to int32
5. **Load int32 argument** - Verify direct load
6. **Load uint32 argument** - Verify direct load (represented as int32)
7. **Load int64 argument** - Verify 64-bit value preserved
8. **Load uint64 argument** - Verify 64-bit value preserved
9. **Load float32 argument** - Verify float value loaded
10. **Load float64 argument** - Verify double value loaded
11. **Load object reference** - Verify reference loaded correctly
12. **Load null reference** - Verify null loaded as zero
13. **Load value type (struct)** - Verify entire struct copied to stack
14. **Load managed pointer (&)** - Verify byref argument loaded

#### Test Scenarios - Instance Methods:
15. **Load 'this' pointer for class** - Verify object reference loaded
16. **Load 'this' pointer for struct** - Verify managed pointer to struct loaded
17. **Load 'this' for generic class** - Verify generic instance reference
18. **Load 'this' for nullable value type** - Verify Nullable<T> handling

### 1.4 ldarg.1 (0x03)
- **Description**: Load argument 1 onto stack
- **Stack**: ... → ..., value
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 16

#### Test Scenarios:
1. **Load int8 argument** - Verify sign-extension to int32
2. **Load uint8 argument** - Verify zero-extension to int32
3. **Load int16 argument** - Verify sign-extension to int32
4. **Load uint16 argument** - Verify zero-extension to int32
5. **Load int32 argument** - Verify direct load
6. **Load uint32 argument** - Verify direct load
7. **Load int64 argument** - Verify 64-bit value preserved
8. **Load uint64 argument** - Verify 64-bit value preserved
9. **Load float32 argument** - Verify float value loaded
10. **Load float64 argument** - Verify double value loaded
11. **Load object reference** - Verify reference loaded correctly
12. **Load null reference** - Verify null loaded
13. **Load value type (struct)** - Verify entire struct copied
14. **Load managed pointer (&)** - Verify byref loaded
15. **Instance method arg1** - Second parameter after 'this'
16. **Static method arg1** - Second parameter

### 1.5 ldarg.2 (0x04)
- **Description**: Load argument 2 onto stack
- **Stack**: ... → ..., value
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 16

#### Test Scenarios:
1. **Load int8 argument** - Verify sign-extension to int32
2. **Load uint8 argument** - Verify zero-extension to int32
3. **Load int16 argument** - Verify sign-extension to int32
4. **Load uint16 argument** - Verify zero-extension to int32
5. **Load int32 argument** - Verify direct load
6. **Load uint32 argument** - Verify direct load
7. **Load int64 argument** - Verify 64-bit value preserved
8. **Load uint64 argument** - Verify 64-bit value preserved
9. **Load float32 argument** - Verify float value loaded
10. **Load float64 argument** - Verify double value loaded
11. **Load object reference** - Verify reference loaded
12. **Load null reference** - Verify null loaded
13. **Load value type (struct)** - Verify struct copied
14. **Load managed pointer (&)** - Verify byref loaded
15. **Instance method arg2** - Third parameter after 'this'
16. **Static method arg2** - Third parameter

### 1.6 ldarg.3 (0x05)
- **Description**: Load argument 3 onto stack
- **Stack**: ... → ..., value
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 16

#### Test Scenarios:
1. **Load int8 argument** - Verify sign-extension to int32
2. **Load uint8 argument** - Verify zero-extension to int32
3. **Load int16 argument** - Verify sign-extension to int32
4. **Load uint16 argument** - Verify zero-extension to int32
5. **Load int32 argument** - Verify direct load
6. **Load uint32 argument** - Verify direct load
7. **Load int64 argument** - Verify 64-bit value preserved
8. **Load uint64 argument** - Verify 64-bit value preserved
9. **Load float32 argument** - Verify float value loaded
10. **Load float64 argument** - Verify double value loaded
11. **Load object reference** - Verify reference loaded
12. **Load null reference** - Verify null loaded
13. **Load value type (struct)** - Verify struct copied
14. **Load managed pointer (&)** - Verify byref loaded
15. **Instance method arg3** - Fourth parameter after 'this'
16. **Static method arg3** - Fourth parameter

### 1.7 ldloc.0 (0x06)
- **Description**: Load local variable 0 onto stack
- **Stack**: ... → ..., value
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 20

#### Test Scenarios - Primitive Types:
1. **Load int8 local** - Verify sign-extension to int32
2. **Load uint8 local** - Verify zero-extension to int32
3. **Load int16 local** - Verify sign-extension to int32
4. **Load uint16 local** - Verify zero-extension to int32
5. **Load int32 local** - Verify direct load
6. **Load uint32 local** - Verify direct load
7. **Load int64 local** - Verify 64-bit preserved
8. **Load uint64 local** - Verify 64-bit preserved
9. **Load float32 local** - Verify float loaded
10. **Load float64 local** - Verify double loaded
11. **Load bool local** - Verify boolean (as int32 0 or 1)
12. **Load char local** - Verify char (as uint16 zero-extended)

#### Test Scenarios - Reference Types:
13. **Load object reference** - Verify reference loaded
14. **Load null reference** - Verify null loaded
15. **Load array reference** - Verify array reference loaded
16. **Load string reference** - Verify string reference loaded

#### Test Scenarios - Value Types:
17. **Load small struct (≤8 bytes)** - Verify struct copied
18. **Load large struct (>8 bytes)** - Verify large struct copied
19. **Load struct with references** - Verify GC-tracked fields handled

#### Test Scenarios - Pointer Types:
20. **Load managed pointer (&)** - Verify byref local loaded

### 1.8 ldloc.1 (0x07)
- **Description**: Load local variable 1 onto stack
- **Stack**: ... → ..., value
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 14

#### Test Scenarios:
1. **Load int32 local** - Basic integer load
2. **Load int64 local** - 64-bit integer load
3. **Load float32 local** - Float load
4. **Load float64 local** - Double load
5. **Load object reference** - Reference load
6. **Load null reference** - Null load
7. **Load value type** - Struct copy
8. **Load managed pointer** - Byref load
9. **Load after stloc.1** - Verify round-trip
10. **Load uninitialized local** - Verify zero-initialization
11. **Load int8 (sign-extend)** - Verify sign extension
12. **Load uint8 (zero-extend)** - Verify zero extension
13. **Load int16 (sign-extend)** - Verify sign extension
14. **Load uint16 (zero-extend)** - Verify zero extension

### 1.9 ldloc.2 (0x08)
- **Description**: Load local variable 2 onto stack
- **Stack**: ... → ..., value
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 14

#### Test Scenarios:
1. **Load int32 local** - Basic integer load
2. **Load int64 local** - 64-bit integer load
3. **Load float32 local** - Float load
4. **Load float64 local** - Double load
5. **Load object reference** - Reference load
6. **Load null reference** - Null load
7. **Load value type** - Struct copy
8. **Load managed pointer** - Byref load
9. **Load after stloc.2** - Verify round-trip
10. **Load uninitialized local** - Verify zero-initialization
11. **Load int8 (sign-extend)** - Verify sign extension
12. **Load uint8 (zero-extend)** - Verify zero extension
13. **Load int16 (sign-extend)** - Verify sign extension
14. **Load uint16 (zero-extend)** - Verify zero extension

### 1.10 ldloc.3 (0x09)
- **Description**: Load local variable 3 onto stack
- **Stack**: ... → ..., value
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 14

#### Test Scenarios:
1. **Load int32 local** - Basic integer load
2. **Load int64 local** - 64-bit integer load
3. **Load float32 local** - Float load
4. **Load float64 local** - Double load
5. **Load object reference** - Reference load
6. **Load null reference** - Null load
7. **Load value type** - Struct copy
8. **Load managed pointer** - Byref load
9. **Load after stloc.3** - Verify round-trip
10. **Load uninitialized local** - Verify zero-initialization
11. **Load int8 (sign-extend)** - Verify sign extension
12. **Load uint8 (zero-extend)** - Verify zero extension
13. **Load int16 (sign-extend)** - Verify sign extension
14. **Load uint16 (zero-extend)** - Verify zero extension

### 1.11 stloc.0 (0x0A)
- **Description**: Pop value from stack to local variable 0
- **Stack**: ..., value → ...
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 18

#### Test Scenarios - Primitive Types:
1. **Store int32** - Basic integer store
2. **Store int64** - 64-bit integer store
3. **Store float32** - Float store (may involve precision conversion from F)
4. **Store float64** - Double store
5. **Store to int8 local** - Verify truncation to 8 bits
6. **Store to uint8 local** - Verify truncation to 8 bits
7. **Store to int16 local** - Verify truncation to 16 bits
8. **Store to uint16 local** - Verify truncation to 16 bits
9. **Store bool** - Verify boolean storage

#### Test Scenarios - Reference Types:
10. **Store object reference** - Verify reference stored
11. **Store null reference** - Verify null stored
12. **Store array reference** - Verify array reference stored
13. **Store derived type to base local** - Type compatibility

#### Test Scenarios - Value Types:
14. **Store small struct** - Verify struct copied
15. **Store large struct** - Verify large struct copied
16. **Store struct with references** - GC tracking

#### Test Scenarios - Edge Cases:
17. **Store managed pointer** - Byref storage
18. **Multiple stores to same local** - Overwrite behavior

### 1.12 stloc.1 (0x0B)
- **Description**: Pop value from stack to local variable 1
- **Stack**: ..., value → ...
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 12

#### Test Scenarios:
1. **Store int32** - Basic integer store
2. **Store int64** - 64-bit store
3. **Store float32** - Float store with precision handling
4. **Store float64** - Double store
5. **Store object reference** - Reference store
6. **Store null** - Null store
7. **Store value type** - Struct copy
8. **Store managed pointer** - Byref store
9. **Truncation to int8** - Verify truncation
10. **Truncation to int16** - Verify truncation
11. **Store then load round-trip** - Verify value preserved
12. **Overwrite existing value** - Verify replacement

### 1.13 stloc.2 (0x0C)
- **Description**: Pop value from stack to local variable 2
- **Stack**: ..., value → ...
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 12

#### Test Scenarios:
1. **Store int32** - Basic integer store
2. **Store int64** - 64-bit store
3. **Store float32** - Float store
4. **Store float64** - Double store
5. **Store object reference** - Reference store
6. **Store null** - Null store
7. **Store value type** - Struct copy
8. **Store managed pointer** - Byref store
9. **Truncation to int8** - Verify truncation
10. **Truncation to int16** - Verify truncation
11. **Store then load round-trip** - Verify value preserved
12. **Overwrite existing value** - Verify replacement

### 1.14 stloc.3 (0x0D)
- **Description**: Pop value from stack to local variable 3
- **Stack**: ..., value → ...
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 12

#### Test Scenarios:
1. **Store int32** - Basic integer store
2. **Store int64** - 64-bit store
3. **Store float32** - Float store
4. **Store float64** - Double store
5. **Store object reference** - Reference store
6. **Store null** - Null store
7. **Store value type** - Struct copy
8. **Store managed pointer** - Byref store
9. **Truncation to int8** - Verify truncation
10. **Truncation to int16** - Verify truncation
11. **Store then load round-trip** - Verify value preserved
12. **Overwrite existing value** - Verify replacement

---

## 2. Argument/Local Instructions - Short Form (0x0E-0x13)

### 2.1 ldarg.s (0x0E)
- **Description**: Load argument (short form, index 0-255)
- **Stack**: ... → ..., value
- **Operand**: uint8 argument index
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 22

#### Test Scenarios - Index Range:
1. **Load arg index 4** - First arg beyond ldarg.0-3 range
2. **Load arg index 127** - Mid-range index
3. **Load arg index 255** - Maximum short form index

#### Test Scenarios - Type Coverage:
4. **Load int8 argument** - Verify sign-extension to int32
5. **Load uint8 argument** - Verify zero-extension to int32
6. **Load int16 argument** - Verify sign-extension to int32
7. **Load uint16 argument** - Verify zero-extension to int32
8. **Load int32 argument** - Direct load
9. **Load int64 argument** - 64-bit preserved
10. **Load float32 argument** - Float loaded
11. **Load float64 argument** - Double loaded
12. **Load object reference** - Reference loaded
13. **Load null reference** - Null loaded
14. **Load value type (small struct)** - Struct ≤8 bytes
15. **Load value type (large struct)** - Struct >8 bytes
16. **Load managed pointer (&)** - Byref argument

#### Test Scenarios - Method Contexts:
17. **Static method with many params** - No 'this' offset
18. **Instance method with many params** - With 'this' offset
19. **Generic method parameter** - Type parameter handling
20. **Params array** - Loading array created from params

#### Test Scenarios - Edge Cases:
21. **Load same arg multiple times** - Verify non-destructive
22. **Interleave with other operations** - Stack integrity

### 2.2 ldarga.s (0x0F)
- **Description**: Load argument address (short form)
- **Stack**: ... → ..., address
- **Operand**: uint8 argument index
- **Status**: [ ]
- **Priority**: P1
- **Tests Needed**: 18
- **Notes**: Used for ref/out parameters and taking address of value type args

#### Test Scenarios - Basic Addressing:
1. **Address of int32 arg** - Get pointer to int32
2. **Address of int64 arg** - Get pointer to int64
3. **Address of float32 arg** - Get pointer to float
4. **Address of float64 arg** - Get pointer to double
5. **Address of value type arg** - Get pointer to struct
6. **Address of reference arg** - Get pointer to object reference slot

#### Test Scenarios - Index Range:
7. **Address of arg index 4** - First beyond short opcodes
8. **Address of arg index 255** - Maximum short form

#### Test Scenarios - Usage Patterns:
9. **Pass address to method (ref param)** - Common ref pattern
10. **Pass address to method (out param)** - Common out pattern
11. **Modify value through address** - Write via stind.*
12. **Read value through address** - Read via ldind.*
13. **Call method on value type via address** - Constrained call

#### Test Scenarios - Instance Methods:
14. **Address of 'this' (arg 0) in struct method** - Get &this
15. **Address of arg after 'this'** - Offset calculation

#### Test Scenarios - Special Cases:
16. **Address with subsequent ldobj** - Load full struct via address
17. **Address with subsequent stobj** - Store full struct via address
18. **Address as native int for arithmetic** - Pointer math

### 2.3 starg.s (0x10)
- **Description**: Store value to argument (short form)
- **Stack**: ..., value → ...
- **Operand**: uint8 argument index
- **Status**: [ ]
- **Priority**: P1
- **Tests Needed**: 20

#### Test Scenarios - Type Coverage:
1. **Store int32 to arg** - Basic integer store
2. **Store int64 to arg** - 64-bit store
3. **Store float32 to arg** - Float store
4. **Store float64 to arg** - Double store
5. **Store object reference** - Reference store
6. **Store null reference** - Null store
7. **Store value type** - Struct copy
8. **Store to int8 arg** - Truncation to 8 bits
9. **Store to int16 arg** - Truncation to 16 bits

#### Test Scenarios - Index Range:
10. **Store to arg index 4** - First beyond short opcodes
11. **Store to arg index 255** - Maximum short form

#### Test Scenarios - Method Contexts:
12. **Store in static method** - No 'this' offset
13. **Store in instance method** - With 'this' offset
14. **Store to 'this' in struct method** - Modify receiver

#### Test Scenarios - Semantics:
15. **Store then load (round-trip)** - Verify value preserved
16. **Multiple stores to same arg** - Overwrite behavior
17. **Store doesn't affect caller** - Arguments are copies

#### Test Scenarios - Edge Cases:
18. **Store derived type to base arg** - Type compatibility
19. **Store managed pointer** - Byref handling
20. **Interleave stores with other operations** - Stack integrity

### 2.4 ldloc.s (0x11)
- **Description**: Load local variable (short form, index 0-255)
- **Stack**: ... → ..., value
- **Operand**: uint8 local index
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 20

#### Test Scenarios - Index Range:
1. **Load local index 4** - First beyond ldloc.0-3 range
2. **Load local index 127** - Mid-range
3. **Load local index 255** - Maximum short form

#### Test Scenarios - Type Coverage:
4. **Load int8 local** - Sign-extension to int32
5. **Load uint8 local** - Zero-extension to int32
6. **Load int16 local** - Sign-extension to int32
7. **Load uint16 local** - Zero-extension to int32
8. **Load int32 local** - Direct load
9. **Load int64 local** - 64-bit preserved
10. **Load float32 local** - Float loaded
11. **Load float64 local** - Double loaded
12. **Load object reference** - Reference loaded
13. **Load null reference** - Null loaded
14. **Load value type (small)** - Struct ≤8 bytes
15. **Load value type (large)** - Struct >8 bytes
16. **Load managed pointer** - Byref loaded
17. **Load bool local** - Boolean as 0/1 int32
18. **Load char local** - Char as uint16 zero-extended

#### Test Scenarios - Initialization:
19. **Load uninitialized local** - Must be zero/null
20. **Load after store (round-trip)** - Verify preserved

### 2.5 ldloca.s (0x12)
- **Description**: Load local variable address (short form)
- **Stack**: ... → ..., address
- **Operand**: uint8 local index
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 20
- **Notes**: Used for ref parameters and taking address of locals

#### Test Scenarios - Basic Addressing:
1. **Address of int32 local** - Get pointer to int32
2. **Address of int64 local** - Get pointer to int64
3. **Address of float32 local** - Get pointer to float
4. **Address of float64 local** - Get pointer to double
5. **Address of value type local** - Get pointer to struct
6. **Address of reference local** - Get pointer to object reference slot

#### Test Scenarios - Index Range:
7. **Address of local index 4** - First beyond short opcodes
8. **Address of local index 255** - Maximum short form

#### Test Scenarios - Usage Patterns:
9. **Pass to method (ref parameter)** - Common ref pattern
10. **Pass to method (out parameter)** - Common out pattern
11. **Modify value through address (stind)** - Write via stind.*
12. **Read value through address (ldind)** - Read via ldind.*
13. **Call instance method on struct via address** - Constrained call
14. **Initialize via address (initobj)** - Zero-initialize struct

#### Test Scenarios - Struct Operations:
15. **Address with subsequent ldobj** - Load full struct
16. **Address with subsequent stobj** - Store full struct
17. **Address for cpobj source** - Copy source
18. **Address for cpobj destination** - Copy destination

#### Test Scenarios - Special Cases:
19. **Address stability across calls** - Address remains valid
20. **Multiple addresses to different locals** - Distinct pointers

### 2.6 stloc.s (0x13)
- **Description**: Store value to local variable (short form)
- **Stack**: ..., value → ...
- **Operand**: uint8 local index
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 18

#### Test Scenarios - Index Range:
1. **Store to local index 4** - First beyond stloc.0-3
2. **Store to local index 127** - Mid-range
3. **Store to local index 255** - Maximum short form

#### Test Scenarios - Type Coverage:
4. **Store int32** - Basic integer
5. **Store int64** - 64-bit
6. **Store float32** - Float (precision from F)
7. **Store float64** - Double
8. **Store object reference** - Reference
9. **Store null** - Null reference
10. **Store to int8 local** - Truncation to 8 bits
11. **Store to int16 local** - Truncation to 16 bits
12. **Store value type (small)** - Struct ≤8 bytes
13. **Store value type (large)** - Struct >8 bytes
14. **Store managed pointer** - Byref

#### Test Scenarios - Behavior:
15. **Store then load (round-trip)** - Verify preserved
16. **Multiple stores to same local** - Overwrite behavior
17. **Store derived to base-typed local** - Type compatibility
18. **Interleave with other operations** - Stack integrity

---

## 3. Constant Loading Instructions (0x14-0x23)

### 3.1 ldnull (0x14)
- **Description**: Push null reference onto stack
- **Stack**: ... → ..., null
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 10

#### Test Scenarios:
1. **Basic null load** - Verify null pushed as zero bit pattern
2. **Null comparison with ceq** - null == null → 1
3. **Null as method argument** - Pass null to object parameter
4. **Null stored to local** - Store null to reference local
5. **Null stored to field** - Store null to reference field
6. **Null stored to array element** - Store null to object[]
7. **Null for brfalse** - null causes branch (null is false)
8. **Null for brtrue** - null does not branch (null is not true)
9. **Null with isinst** - isinst on null returns null
10. **Null with castclass** - castclass on null returns null (no throw)

### 3.2 ldc.i4.m1 (0x15)
- **Description**: Push -1 (int32) onto stack
- **Stack**: ... → ..., -1
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 8

#### Test Scenarios:
1. **Basic -1 load** - Verify 0xFFFFFFFF loaded
2. **Use in arithmetic** - -1 + 1 = 0
3. **Use as bit pattern** - All bits set (for masks)
4. **Comparison with ceq** - -1 == -1 → 1
5. **Unsigned interpretation** - As uint32 = 4294967295
6. **With bitwise AND** - x & -1 = x
7. **With bitwise OR** - x | -1 = -1
8. **Sign extension to int64** - conv.i8 → 0xFFFFFFFFFFFFFFFF

### 3.3 ldc.i4.0 (0x16)
- **Description**: Push 0 (int32) onto stack
- **Stack**: ... → ..., 0
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 12

#### Test Scenarios:
1. **Basic 0 load** - Verify zero loaded
2. **Zero in arithmetic** - x + 0 = x, x * 0 = 0
3. **Zero for brfalse** - 0 causes branch (is false)
4. **Zero for brtrue** - 0 does not branch (is not true)
5. **Zero comparison** - 0 == 0 → 1
6. **Division by zero** - x / 0 throws DivideByZero
7. **Zero as array index** - First element access
8. **Zero as loop counter initial** - Common pattern
9. **Zero with bitwise ops** - x & 0 = 0, x | 0 = x, x ^ 0 = x
10. **Zero for shift** - x << 0 = x, x >> 0 = x
11. **Zero as null-equivalent for int** - Common sentinel
12. **Zero extension to int64** - conv.i8 → 0L

### 3.4 ldc.i4.1 (0x17)
- **Description**: Push 1 (int32) onto stack
- **Stack**: ... → ..., 1
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 10

#### Test Scenarios:
1. **Basic 1 load** - Verify one loaded
2. **Increment pattern** - x + 1
3. **As true boolean** - 1 is true for brtrue
4. **Multiplication identity** - x * 1 = x
5. **Division identity** - x / 1 = x
6. **As array length** - Single element array
7. **Bit pattern** - Only LSB set
8. **Shift by 1** - x << 1 = x * 2
9. **Comparison result** - ceq returns 1 for true
10. **Extension to int64** - conv.i8 → 1L

### 3.5 ldc.i4.2 (0x18)
- **Description**: Push 2 (int32) onto stack
- **Stack**: ... → ..., 2
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 6

#### Test Scenarios:
1. **Basic 2 load** - Verify value loaded
2. **Power of 2 arithmetic** - x * 2, x / 2
3. **Shift equivalence** - 1 << 1 = 2
4. **Array index** - Third element access
5. **Comparison** - 2 > 1, 2 < 3
6. **Bit pattern** - Second bit set

### 3.6 ldc.i4.3 (0x19)
- **Description**: Push 3 (int32) onto stack
- **Stack**: ... → ..., 3
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 5

#### Test Scenarios:
1. **Basic 3 load** - Verify value loaded
2. **Arithmetic** - 3 + 3 = 6, 3 * 3 = 9
3. **Array index** - Fourth element
4. **Bit pattern** - 0x03 = bits 0 and 1 set
5. **Comparison** - 3 > 2, 3 < 4

### 3.7 ldc.i4.4 (0x1A)
- **Description**: Push 4 (int32) onto stack
- **Stack**: ... → ..., 4
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 6

#### Test Scenarios:
1. **Basic 4 load** - Verify value loaded
2. **Power of 2** - 2^2 = 4
3. **Shift equivalence** - 1 << 2 = 4
4. **Common size** - sizeof(int32)
5. **Array index** - Fifth element
6. **Bit pattern** - Third bit set

### 3.8 ldc.i4.5 (0x1B)
- **Description**: Push 5 (int32) onto stack
- **Stack**: ... → ..., 5
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 4

#### Test Scenarios:
1. **Basic 5 load** - Verify value loaded
2. **Arithmetic** - 5 + 5 = 10
3. **Array index** - Sixth element
4. **Comparison** - 5 > 4, 5 < 6

### 3.9 ldc.i4.6 (0x1C)
- **Description**: Push 6 (int32) onto stack
- **Stack**: ... → ..., 6
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 4

#### Test Scenarios:
1. **Basic 6 load** - Verify value loaded
2. **Arithmetic** - 6 / 2 = 3, 6 / 3 = 2
3. **Array index** - Seventh element
4. **Comparison** - 6 > 5, 6 < 7

### 3.10 ldc.i4.7 (0x1D)
- **Description**: Push 7 (int32) onto stack
- **Stack**: ... → ..., 7
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 5

#### Test Scenarios:
1. **Basic 7 load** - Verify value loaded
2. **Bit mask** - 0x07 = low 3 bits
3. **Arithmetic** - 7 + 1 = 8
4. **Array index** - Eighth element
5. **Comparison** - 7 > 6, 7 < 8

### 3.11 ldc.i4.8 (0x1E)
- **Description**: Push 8 (int32) onto stack
- **Stack**: ... → ..., 8
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 6

#### Test Scenarios:
1. **Basic 8 load** - Verify value loaded
2. **Power of 2** - 2^3 = 8
3. **Shift equivalence** - 1 << 3 = 8
4. **Common size** - sizeof(int64), bits in byte
5. **Array index** - Ninth element
6. **Bit pattern** - Fourth bit set

### 3.12 ldc.i4.s (0x1F)
- **Description**: Push int8 as int32 onto stack
- **Stack**: ... → ..., value
- **Operand**: int8 value (-128 to 127)
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 15

#### Test Scenarios - Positive Values:
1. **Load 9** - First value beyond ldc.i4.8
2. **Load 100** - Mid-range positive
3. **Load 127** - Maximum int8

#### Test Scenarios - Negative Values:
4. **Load -2** - First negative beyond ldc.i4.m1
5. **Load -100** - Mid-range negative
6. **Load -128** - Minimum int8

#### Test Scenarios - Sign Extension:
7. **Verify sign extension of -1** - 0xFF → 0xFFFFFFFF
8. **Verify sign extension of -128** - 0x80 → 0xFFFFFF80
9. **Verify positive not sign-extended** - 127 → 0x0000007F

#### Test Scenarios - Boundary Values:
10. **Load 0 (use ldc.i4.s)** - Compiler might use this
11. **Load 1-8 (use ldc.i4.s)** - Verify equivalence

#### Test Scenarios - Operations:
12. **Arithmetic with loaded value** - 50 + 50 = 100
13. **Comparison** - 100 > 50
14. **As array index** - arr[50]
15. **Convert to other types** - conv.i8, conv.r8

### 3.13 ldc.i4 (0x20)
- **Description**: Push int32 onto stack
- **Stack**: ... → ..., value
- **Operand**: int32 value
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 20

#### Test Scenarios - Boundary Values:
1. **Load Int32.MaxValue** - 2147483647 (0x7FFFFFFF)
2. **Load Int32.MinValue** - -2147483648 (0x80000000)
3. **Load 128** - First beyond ldc.i4.s positive
4. **Load -129** - First beyond ldc.i4.s negative
5. **Load 256** - 2^8
6. **Load 65536** - 2^16
7. **Load 16777216** - 2^24

#### Test Scenarios - Bit Patterns:
8. **Load 0xFFFFFFFF** - All bits set (same as -1)
9. **Load 0x80000000** - Only sign bit set
10. **Load 0x7FFFFFFF** - All bits except sign
11. **Load 0x55555555** - Alternating bits
12. **Load 0xAAAAAAAA** - Alternating bits inverse
13. **Load 0x0000FFFF** - Low 16 bits
14. **Load 0xFFFF0000** - High 16 bits

#### Test Scenarios - Operations:
15. **Overflow detection** - MaxValue + 1 in add.ovf
16. **Underflow detection** - MinValue - 1 in sub.ovf
17. **Multiplication** - Large values
18. **Division** - MaxValue / 2
19. **Bit operations** - AND, OR, XOR with patterns
20. **Conversion** - To int64, to float64

### 3.14 ldc.i8 (0x21)
- **Description**: Push int64 onto stack
- **Stack**: ... → ..., value
- **Operand**: int64 value
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 22

#### Test Scenarios - Boundary Values:
1. **Load Int64.MaxValue** - 9223372036854775807
2. **Load Int64.MinValue** - -9223372036854775808
3. **Load 0L** - Zero as int64
4. **Load 1L** - One as int64
5. **Load -1L** - All bits set
6. **Load 2147483648L** - Int32.MaxValue + 1
7. **Load -2147483649L** - Int32.MinValue - 1

#### Test Scenarios - Bit Patterns:
8. **Load 0xFFFFFFFFFFFFFFFF** - All 64 bits set
9. **Load 0x8000000000000000** - Only sign bit
10. **Load 0x7FFFFFFFFFFFFFFF** - All except sign bit
11. **Load 0x00000000FFFFFFFF** - Low 32 bits
12. **Load 0xFFFFFFFF00000000** - High 32 bits
13. **Load 0x5555555555555555** - Alternating bits
14. **Load 0xAAAAAAAAAAAAAAAA** - Alternating inverse

#### Test Scenarios - Values Beyond int32:
15. **Load 4294967296** - 2^32 (first beyond uint32)
16. **Load 1099511627776** - 2^40
17. **Load 281474976710656** - 2^48
18. **Load 72057594037927936** - 2^56

#### Test Scenarios - Operations:
19. **64-bit arithmetic** - Add, sub, mul large values
20. **64-bit division** - MaxValue / 2
21. **64-bit shifts** - shl, shr, shr.un
22. **Conversion** - To int32 (truncation), to float64

### 3.15 ldc.r4 (0x22)
- **Description**: Push float32 onto stack
- **Stack**: ... → ..., value
- **Operand**: float32 value
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 25

#### Test Scenarios - Normal Values:
1. **Load 0.0f** - Positive zero
2. **Load -0.0f** - Negative zero (distinct bit pattern)
3. **Load 1.0f** - One
4. **Load -1.0f** - Negative one
5. **Load 0.5f** - Common fraction
6. **Load 3.14159f** - Pi approximation

#### Test Scenarios - Special Values:
7. **Load +Infinity** - Positive infinity
8. **Load -Infinity** - Negative infinity
9. **Load NaN** - Not a number
10. **Load quiet NaN** - Specific NaN pattern
11. **Load signaling NaN** - Specific NaN pattern

#### Test Scenarios - Boundary Values:
12. **Load Float.MaxValue** - ~3.4e38
13. **Load Float.MinValue** - ~-3.4e38
14. **Load Float.Epsilon** - Smallest positive normal
15. **Load smallest subnormal** - Denormalized minimum

#### Test Scenarios - Precision:
16. **Load value requiring all mantissa bits** - 1.99999988f
17. **Load value with rounding** - Values that round in float32

#### Test Scenarios - Bit Patterns:
18. **Load with specific exponent** - Powers of 2
19. **Load 2.0f** - Simple power of 2
20. **Load 0.25f** - Negative power of 2

#### Test Scenarios - Operations (float loaded to F on stack):
21. **Addition** - 1.0f + 2.0f
22. **Multiplication** - 2.0f * 3.0f
23. **Division** - 1.0f / 3.0f
24. **Comparison** - 1.0f < 2.0f
25. **Conversion** - To float64, to int32

### 3.16 ldc.r8 (0x23)
- **Description**: Push float64 onto stack
- **Stack**: ... → ..., value
- **Operand**: float64 value
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 28

#### Test Scenarios - Normal Values:
1. **Load 0.0** - Positive zero
2. **Load -0.0** - Negative zero
3. **Load 1.0** - One
4. **Load -1.0** - Negative one
5. **Load 0.5** - Common fraction
6. **Load 3.141592653589793** - Pi (more precision)
7. **Load 2.718281828459045** - e

#### Test Scenarios - Special Values:
8. **Load +Infinity** - Positive infinity
9. **Load -Infinity** - Negative infinity
10. **Load NaN** - Not a number
11. **Load quiet NaN** - Specific NaN
12. **Load signaling NaN** - Specific NaN

#### Test Scenarios - Boundary Values:
13. **Load Double.MaxValue** - ~1.8e308
14. **Load Double.MinValue** - ~-1.8e308
15. **Load Double.Epsilon** - Smallest positive normal
16. **Load smallest subnormal** - Denormalized minimum

#### Test Scenarios - Values Beyond float32:
17. **Load value > Float.MaxValue** - ~1e100
18. **Load value < Float.MinValue** - ~-1e100
19. **Load value with more precision than float32** - 1.0000000000001

#### Test Scenarios - Bit Patterns:
20. **Load powers of 2** - 2.0, 4.0, 0.5, 0.25
21. **Load value using all mantissa bits** - High precision

#### Test Scenarios - Operations:
22. **Addition** - 1.0 + 2.0
23. **Subtraction** - 2.0 - 1.0
24. **Multiplication** - 2.0 * 3.0
25. **Division** - 1.0 / 3.0
26. **Comparison with NaN** - NaN != NaN
27. **Infinity arithmetic** - Inf + 1 = Inf
28. **Conversion** - To float32 (precision loss), to int64

---

## 4. Stack Manipulation (0x25-0x26)

### 4.1 dup (0x25)
- **Description**: Duplicate top of stack
- **Stack**: ..., value → ..., value, value
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 20

#### Test Scenarios - Primitive Types:
1. **Dup int32** - Duplicate 32-bit integer
2. **Dup int64** - Duplicate 64-bit integer
3. **Dup float32** - Duplicate single-precision float
4. **Dup float64** - Duplicate double-precision float
5. **Dup native int** - Duplicate pointer-sized integer

#### Test Scenarios - Reference Types:
6. **Dup object reference** - Both copies reference same object
7. **Dup null reference** - Duplicate null
8. **Dup array reference** - Both copies reference same array
9. **Dup string reference** - Both copies reference same string

#### Test Scenarios - Value Types:
10. **Dup small struct (≤8 bytes)** - Copy small value type
11. **Dup large struct (>8 bytes)** - Copy large value type
12. **Dup struct with references** - Proper field handling

#### Test Scenarios - Pointer Types:
13. **Dup managed pointer (&)** - Duplicate byref

#### Test Scenarios - Usage Patterns:
14. **Dup for store-and-use** - dup; stloc; use pattern
15. **Dup for compare-and-use** - dup; compare; use pattern
16. **Multiple dups in sequence** - dup; dup; dup
17. **Dup with immediate consumption** - dup; pop

#### Test Scenarios - Verification:
18. **Both copies are independent** - Modify one, check other unchanged
19. **Both copies are equivalent** - ceq on both returns 1
20. **Stack depth increases by 1** - Verify stack tracking

### 4.2 pop (0x26)
- **Description**: Remove top of stack
- **Stack**: ..., value → ...
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 15

#### Test Scenarios - Primitive Types:
1. **Pop int32** - Discard 32-bit integer
2. **Pop int64** - Discard 64-bit integer
3. **Pop float32** - Discard float
4. **Pop float64** - Discard double
5. **Pop native int** - Discard pointer-sized int

#### Test Scenarios - Reference Types:
6. **Pop object reference** - Discard reference (no GC effect)
7. **Pop null reference** - Discard null
8. **Pop array reference** - Discard array ref

#### Test Scenarios - Value Types:
9. **Pop small struct** - Discard small value type
10. **Pop large struct** - Discard large value type

#### Test Scenarios - Pointer Types:
11. **Pop managed pointer** - Discard byref

#### Test Scenarios - Usage Patterns:
12. **Pop void method return** - Call returning void, pop if needed
13. **Pop unused return value** - Call returning int, discard result
14. **Multiple pops** - pop; pop; pop sequence
15. **Pop after dup** - dup; pop leaves original

---

## 5. Control Flow - Unconditional (0x27-0x2A, 0x38)

### 5.1 jmp (0x27)
- **Description**: Jump to method (tail call without return)
- **Stack**: ... → ...
- **Operand**: method token
- **Status**: [ ]
- **Priority**: P3
- **Tests Needed**: 8
- **Notes**: Rarely used, complex semantics. Transfers args and jumps without creating new stack frame.

#### Test Scenarios:
1. **Jump to static method** - Same signature, no args
2. **Jump to static with args** - Args passed through
3. **Jump to instance method** - 'this' passed through
4. **Jump with return value** - Return propagated to original caller
5. **Jump with value type return** - Struct return
6. **Chain of jumps** - A jmps to B jmps to C
7. **Jump to method in different class** - Cross-type jump
8. **Jump with ref/out params** - Byref arguments

### 5.2 call (0x28)
- **Description**: Call method
- **Stack**: ..., arg0, arg1, ... argN → ..., retVal (maybe)
- **Operand**: method token
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 45
- **Notes**: Static calls, instance calls (non-virtual)

#### Test Scenarios - Static Methods:
1. **Call static, no args, void return** - Simplest case
2. **Call static, no args, int32 return** - Return value
3. **Call static, no args, int64 return** - 64-bit return
4. **Call static, no args, float return** - Float return
5. **Call static, no args, object return** - Reference return
6. **Call static, no args, struct return** - Value type return (small)
7. **Call static, no args, large struct return** - Value type return (>8 bytes)
8. **Call static with 1 int arg** - Single argument
9. **Call static with multiple int args** - 2, 3, 4 arguments
10. **Call static with mixed type args** - int, float, object
11. **Call static with struct arg** - Value type passed by value
12. **Call static with ref arg** - Byref parameter
13. **Call static with out arg** - Output parameter

#### Test Scenarios - Instance Methods (Non-Virtual):
14. **Call instance, no args, void** - Simple instance call
15. **Call instance, no args, return value** - With return
16. **Call instance with args** - Parameters after 'this'
17. **Call instance on struct (constrained)** - Value type receiver
18. **Call instance with null check** - Verify null throws NullRef
19. **Call base class method** - Non-virtual base call

#### Test Scenarios - Argument Passing:
20. **Pass int8, verify sign-extension** - Small int handling
21. **Pass uint8, verify zero-extension** - Unsigned small int
22. **Pass int16, verify sign-extension** - 16-bit handling
23. **Pass char (uint16)** - Character handling
24. **Pass bool** - Boolean as int
25. **Pass native int** - Pointer-sized int
26. **Pass managed pointer** - Byref
27. **Pass null reference** - Null as argument

#### Test Scenarios - Return Value Handling:
28. **Return int8, verify sign-extension** - Small return
29. **Return uint8, verify zero-extension** - Unsigned small return
30. **Return bool** - Boolean return
31. **Return null** - Null reference return

#### Test Scenarios - Generic Methods:
32. **Call generic method with int** - Type parameter = int
33. **Call generic method with object** - Type parameter = object
34. **Call generic method with struct** - Type parameter = value type
35. **Call generic on generic type** - Generic type + generic method

#### Test Scenarios - Special Cases:
36. **Call to method with params array** - Varargs handling
37. **Call with optional parameters** - Default values
38. **Recursive call** - Method calls itself
39. **Mutual recursion** - A calls B calls A
40. **Deep call stack** - Many nested calls
41. **Call preserves stack** - Stack correct after call

#### Test Scenarios - Calling Conventions:
42. **Many args (>6)** - Spill to stack on x64
43. **Many float args** - XMM register spilling
44. **Mixed int/float args** - Register allocation
45. **Call with large struct arg** - Stack copy

### 5.3 calli (0x29)
- **Description**: Call method via function pointer
- **Stack**: ..., arg0, arg1, ... argN, ftn → ..., retVal (maybe)
- **Operand**: signature token
- **Status**: [ ]
- **Priority**: P1
- **Tests Needed**: 20

#### Test Scenarios - Basic Calls:
1. **Calli to static method** - Basic function pointer call
2. **Calli void return** - No return value
3. **Calli int32 return** - Integer return
4. **Calli int64 return** - 64-bit return
5. **Calli float64 return** - Float return
6. **Calli object return** - Reference return
7. **Calli struct return** - Value type return

#### Test Scenarios - Arguments:
8. **Calli with 1 arg** - Single parameter
9. **Calli with multiple args** - Several parameters
10. **Calli with ref arg** - Byref parameter
11. **Calli with struct arg** - Value type by value

#### Test Scenarios - Function Pointer Sources:
12. **Calli with ldftn result** - Static method pointer
13. **Calli with ldvirtftn result** - Virtual method pointer
14. **Calli with native pointer** - Interop scenario

#### Test Scenarios - Calling Conventions:
15. **Managed calling convention** - Default .NET
16. **Cdecl calling convention** - C interop
17. **Stdcall calling convention** - Windows API

#### Test Scenarios - Edge Cases:
18. **Calli with null pointer** - Should throw/crash
19. **Calli in loop** - Repeated calls
20. **Calli with generic signature** - Generic function pointer

### 5.4 ret (0x2A)
- **Description**: Return from method
- **Stack**: retVal (maybe) → ...
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 22

#### Test Scenarios - Void Returns:
1. **Return from void method** - Empty stack on ret
2. **Return void after side effects** - Ensure effects complete

#### Test Scenarios - Primitive Returns:
3. **Return int32** - 32-bit integer
4. **Return int64** - 64-bit integer
5. **Return float32** - Single-precision
6. **Return float64** - Double-precision
7. **Return native int** - Pointer-sized
8. **Return int8 (sign-extend)** - Small signed
9. **Return uint8 (zero-extend)** - Small unsigned
10. **Return int16 (sign-extend)** - 16-bit signed
11. **Return uint16 (zero-extend)** - 16-bit unsigned
12. **Return bool** - Boolean as int

#### Test Scenarios - Reference Returns:
13. **Return object reference** - Reference type
14. **Return null reference** - Null
15. **Return array reference** - Array
16. **Return string reference** - String

#### Test Scenarios - Value Type Returns:
17. **Return small struct (≤8 bytes)** - In registers
18. **Return large struct (>8 bytes)** - Hidden pointer
19. **Return struct with references** - GC-tracked

#### Test Scenarios - Special Cases:
20. **Return from nested calls** - Correct unwinding
21. **Return ref (byref return)** - Managed pointer return
22. **Return from try block** - Control flow verification

### 5.5 br.s (0x2B)
- **Description**: Unconditional branch (short form)
- **Stack**: ... → ...
- **Operand**: int8 offset (-128 to +127)
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 12

#### Test Scenarios - Forward Branches:
1. **Branch forward 1 byte** - Minimal forward
2. **Branch forward 127 bytes** - Maximum short forward
3. **Skip over instructions** - Jump past code

#### Test Scenarios - Backward Branches:
4. **Branch backward 1 byte** - Minimal backward (infinite loop potential)
5. **Branch backward 128 bytes** - Maximum short backward
6. **Loop construct** - while(true) pattern

#### Test Scenarios - Offset Calculation:
7. **Branch offset from instruction end** - Verify offset base
8. **Branch to exact target** - Land on specific instruction

#### Test Scenarios - Stack State:
9. **Branch preserves stack** - Stack unchanged across branch
10. **Branch with values on stack** - Values still accessible

#### Test Scenarios - Control Flow:
11. **Branch in if-else** - Conditional skip pattern
12. **Multiple branches** - Branch chain

### 5.6 br (0x38)
- **Description**: Unconditional branch
- **Stack**: ... → ...
- **Operand**: int32 offset
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 12

#### Test Scenarios - Long Branches:
1. **Branch forward 128 bytes** - First beyond short form
2. **Branch forward large offset** - Thousands of bytes
3. **Branch to end of large method** - Near max offset

#### Test Scenarios - Backward Branches:
4. **Branch backward 129 bytes** - First beyond short form
5. **Branch to method start** - Long backward

#### Test Scenarios - Offset Values:
6. **Branch with negative int32 offset** - Large negative
7. **Branch with zero offset** - Self-loop (infinite)
8. **Branch with max int32 offset** - Theoretical limit

#### Test Scenarios - Common Patterns:
9. **Branch at end of then-block** - Skip else
10. **Branch at end of loop body** - Back to condition
11. **Branch for break/continue** - Exit/continue loop
12. **Branch stack preservation** - Stack correct at target

---

## 6. Control Flow - Conditional Boolean (0x2C-0x2D, 0x39-0x3A)

### 6.1 brfalse.s (0x2C)
- **Description**: Branch if false/null/zero (short form)
- **Stack**: ..., value → ...
- **Operand**: int8 offset
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 20
- **Notes**: Also known as brnull.s, brzero.s

#### Test Scenarios - Integer Values:
1. **brfalse with int32 zero** - Branch taken
2. **brfalse with int32 non-zero** - Branch not taken
3. **brfalse with int32 = 1** - Branch not taken
4. **brfalse with int32 = -1** - Branch not taken
5. **brfalse with int32.MaxValue** - Branch not taken
6. **brfalse with int32.MinValue** - Branch not taken

#### Test Scenarios - 64-bit Values:
7. **brfalse with int64 zero** - Branch taken
8. **brfalse with int64 non-zero** - Branch not taken
9. **brfalse with int64 > int32.MaxValue** - High bits non-zero

#### Test Scenarios - Native Int:
10. **brfalse with native int zero** - Branch taken
11. **brfalse with native int non-zero** - Branch not taken

#### Test Scenarios - References:
12. **brfalse with null reference** - Branch taken (null is "false")
13. **brfalse with non-null reference** - Branch not taken
14. **brfalse with null after ldnull** - Branch taken

#### Test Scenarios - Float (treated as int by size):
15. **brfalse with float 0.0** - Bit pattern zero → branch taken
16. **brfalse with float -0.0** - Bit pattern non-zero → branch NOT taken
17. **brfalse with float 1.0** - Branch not taken

#### Test Scenarios - Control Flow:
18. **brfalse forward** - Skip code
19. **brfalse backward** - Loop construct
20. **brfalse in nested conditions** - Complex control flow

### 6.2 brtrue.s (0x2D)
- **Description**: Branch if true/non-null/non-zero (short form)
- **Stack**: ..., value → ...
- **Operand**: int8 offset
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 20
- **Notes**: Also known as brinst.s

#### Test Scenarios - Integer Values:
1. **brtrue with int32 zero** - Branch NOT taken
2. **brtrue with int32 = 1** - Branch taken
3. **brtrue with int32 = -1** - Branch taken (any non-zero)
4. **brtrue with int32.MaxValue** - Branch taken
5. **brtrue with int32.MinValue** - Branch taken
6. **brtrue with any non-zero** - Branch taken

#### Test Scenarios - 64-bit Values:
7. **brtrue with int64 zero** - Branch NOT taken
8. **brtrue with int64 = 1** - Branch taken
9. **brtrue with int64 high bits only** - 0x100000000 → taken
10. **brtrue with int64 = -1** - Branch taken

#### Test Scenarios - Native Int:
11. **brtrue with native int zero** - Branch NOT taken
12. **brtrue with native int = 1** - Branch taken

#### Test Scenarios - References:
13. **brtrue with null reference** - Branch NOT taken
14. **brtrue with non-null reference** - Branch taken
15. **brtrue with valid object** - Branch taken

#### Test Scenarios - Float:
16. **brtrue with float 0.0** - Bit pattern zero → NOT taken
17. **brtrue with float -0.0** - Bit pattern non-zero → taken
18. **brtrue with float 1.0** - Branch taken

#### Test Scenarios - Control Flow:
19. **brtrue forward** - Skip code
20. **brtrue backward** - Loop while non-zero

### 6.3 brfalse (0x39)
- **Description**: Branch if false/null/zero
- **Stack**: ..., value → ...
- **Operand**: int32 offset
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 12
- **Notes**: Also known as brnull, brzero

#### Test Scenarios - Same as brfalse.s but long offsets:
1. **brfalse with zero, long forward jump** - Large positive offset
2. **brfalse with zero, long backward jump** - Large negative offset
3. **brfalse with non-zero, no branch** - Fall through

#### Test Scenarios - Value Types:
4. **brfalse int32 zero** - Branch taken
5. **brfalse int64 zero** - Branch taken
6. **brfalse native int zero** - Branch taken

#### Test Scenarios - References:
7. **brfalse null** - Branch taken
8. **brfalse non-null** - Not taken

#### Test Scenarios - Edge Cases:
9. **brfalse at method boundary** - Large offset
10. **brfalse with result of comparison** - ceq → 0 or 1
11. **brfalse with result of clt** - Comparison chaining
12. **brfalse offset > 127** - First case needing long form

### 6.4 brtrue (0x3A)
- **Description**: Branch if true/non-null/non-zero
- **Stack**: ..., value → ...
- **Operand**: int32 offset
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 12
- **Notes**: Also known as brinst

#### Test Scenarios - Long Offsets:
1. **brtrue with non-zero, long forward** - Large positive offset
2. **brtrue with non-zero, long backward** - Large negative offset
3. **brtrue with zero, no branch** - Fall through

#### Test Scenarios - Value Types:
4. **brtrue int32 non-zero** - Branch taken
5. **brtrue int64 non-zero** - Branch taken
6. **brtrue native int non-zero** - Branch taken

#### Test Scenarios - References:
7. **brtrue non-null** - Branch taken
8. **brtrue null** - Not taken

#### Test Scenarios - Edge Cases:
9. **brtrue at method boundary** - Large offset
10. **brtrue with comparison result** - ceq → 0 or 1
11. **brtrue with cgt result** - Comparison chaining
12. **brtrue offset > 127** - First case needing long form

---

## 7. Control Flow - Conditional Comparison (0x2E-0x37, 0x3B-0x44)

**Note**: All comparison branches share common test patterns. Each opcode tests:
- **Type combinations**: int32/int32, int64/int64, native int variants, float/float
- **Boundary values**: 0, 1, -1, MaxValue, MinValue
- **Signed vs unsigned interpretation**: -1 as 0xFFFFFFFF
- **Float special values**: NaN, +Inf, -Inf, +0, -0

### 7.1 beq.s (0x2E)
- **Description**: Branch if equal (short form)
- **Stack**: ..., value1, value2 → ...
- **Operand**: int8 offset
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 22

#### Test Scenarios - Integer Equality:
1. **beq int32: 0 == 0** - Equal zeros, branch taken
2. **beq int32: 1 == 1** - Equal positives, branch taken
3. **beq int32: -1 == -1** - Equal negatives, branch taken
4. **beq int32: 1 == 2** - Unequal, branch NOT taken
5. **beq int32: -1 == 1** - Unequal signs, NOT taken
6. **beq int32: MaxValue == MaxValue** - Boundary equal
7. **beq int32: MinValue == MinValue** - Boundary equal

#### Test Scenarios - 64-bit:
8. **beq int64: 0L == 0L** - Equal zeros
9. **beq int64: large == large** - Beyond int32 range
10. **beq int64: different** - Unequal, NOT taken

#### Test Scenarios - Native Int:
11. **beq native int: equal** - Pointer-sized comparison
12. **beq int32 vs native int** - Mixed types (valid per spec)

#### Test Scenarios - Floating Point:
13. **beq float: 1.0 == 1.0** - Equal floats
14. **beq float: 1.0 == 2.0** - Unequal, NOT taken
15. **beq float: NaN == NaN** - NaN != NaN, NOT taken
16. **beq float: +0.0 == -0.0** - Equal (same value)
17. **beq float: +Inf == +Inf** - Equal infinities

#### Test Scenarios - References:
18. **beq object: same ref** - Same object, taken
19. **beq object: different refs** - Different objects, NOT taken
20. **beq object: null == null** - Both null, taken

#### Test Scenarios - Control Flow:
21. **beq forward branch** - Skip code on equal
22. **beq backward branch** - Loop until unequal

### 7.2 bge.s (0x2F)
- **Description**: Branch if greater than or equal (signed, short form)
- **Stack**: ..., value1, value2 → ...
- **Operand**: int8 offset
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 18

#### Test Scenarios - Signed Comparison:
1. **bge: 5 >= 3** - Greater, branch taken
2. **bge: 5 >= 5** - Equal, branch taken
3. **bge: 3 >= 5** - Less, branch NOT taken
4. **bge: -1 >= -5** - Negative comparison (−1 > −5), taken
5. **bge: -5 >= -1** - Negative comparison (−5 < −1), NOT taken
6. **bge: 0 >= -1** - Zero vs negative, taken
7. **bge: -1 >= 0** - Negative vs zero, NOT taken
8. **bge: MaxValue >= 0** - Large positive, taken
9. **bge: MinValue >= 0** - Most negative, NOT taken

#### Test Scenarios - 64-bit:
10. **bge int64: large >= small** - 64-bit signed
11. **bge int64: negative >= positive** - Cross-sign

#### Test Scenarios - Float:
12. **bge float: 2.0 >= 1.0** - Greater, taken
13. **bge float: 1.0 >= 1.0** - Equal, taken
14. **bge float: NaN >= 1.0** - NaN unordered, NOT taken
15. **bge float: 1.0 >= NaN** - NaN unordered, NOT taken

#### Test Scenarios - Native Int:
16. **bge native int signed** - Pointer-sized signed

#### Test Scenarios - Control Flow:
17. **bge forward** - Skip on condition
18. **bge backward** - Loop construct

### 7.3 bgt.s (0x30)
- **Description**: Branch if greater than (signed, short form)
- **Stack**: ..., value1, value2 → ...
- **Operand**: int8 offset
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 16

#### Test Scenarios - Signed:
1. **bgt: 5 > 3** - Greater, taken
2. **bgt: 5 > 5** - Equal, NOT taken
3. **bgt: 3 > 5** - Less, NOT taken
4. **bgt: -1 > -5** - Negative (−1 > −5), taken
5. **bgt: 0 > -1** - Zero > negative, taken
6. **bgt: -1 > 0** - Negative > zero, NOT taken
7. **bgt: MaxValue > MinValue** - Extremes, taken
8. **bgt: MinValue > MaxValue** - Extremes, NOT taken

#### Test Scenarios - 64-bit:
9. **bgt int64** - 64-bit signed comparison
10. **bgt int64 equal** - Equal, NOT taken

#### Test Scenarios - Float:
11. **bgt float: 2.0 > 1.0** - Greater, taken
12. **bgt float: 1.0 > 1.0** - Equal, NOT taken
13. **bgt float: NaN > x** - NaN, NOT taken

#### Test Scenarios - Native Int:
14. **bgt native int** - Pointer-sized

#### Test Scenarios - Control Flow:
15. **bgt forward** - Skip code
16. **bgt backward** - Loop

### 7.4 ble.s (0x31)
- **Description**: Branch if less than or equal (signed, short form)
- **Stack**: ..., value1, value2 → ...
- **Operand**: int8 offset
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 16

#### Test Scenarios - Signed:
1. **ble: 3 <= 5** - Less, taken
2. **ble: 5 <= 5** - Equal, taken
3. **ble: 5 <= 3** - Greater, NOT taken
4. **ble: -5 <= -1** - Negative (−5 < −1), taken
5. **ble: -1 <= 0** - Negative <= zero, taken
6. **ble: 0 <= -1** - Zero <= negative, NOT taken
7. **ble: MinValue <= MaxValue** - Extremes, taken
8. **ble: MaxValue <= MinValue** - Extremes, NOT taken

#### Test Scenarios - 64-bit:
9. **ble int64 less** - Less, taken
10. **ble int64 equal** - Equal, taken

#### Test Scenarios - Float:
11. **ble float: 1.0 <= 2.0** - Less, taken
12. **ble float: 1.0 <= 1.0** - Equal, taken
13. **ble float: NaN <= x** - NaN, NOT taken

#### Test Scenarios - Native Int:
14. **ble native int** - Pointer-sized

#### Test Scenarios - Control Flow:
15. **ble forward** - Skip code
16. **ble backward** - Loop

### 7.5 blt.s (0x32)
- **Description**: Branch if less than (signed, short form)
- **Stack**: ..., value1, value2 → ...
- **Operand**: int8 offset
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 16

#### Test Scenarios - Signed:
1. **blt: 3 < 5** - Less, taken
2. **blt: 5 < 5** - Equal, NOT taken
3. **blt: 5 < 3** - Greater, NOT taken
4. **blt: -5 < -1** - Negative (−5 < −1), taken
5. **blt: -1 < 0** - Negative < zero, taken
6. **blt: 0 < -1** - Zero < negative, NOT taken
7. **blt: MinValue < MaxValue** - Extremes, taken
8. **blt: MaxValue < MinValue** - Extremes, NOT taken

#### Test Scenarios - 64-bit:
9. **blt int64 less** - Less, taken
10. **blt int64 equal** - Equal, NOT taken

#### Test Scenarios - Float:
11. **blt float: 1.0 < 2.0** - Less, taken
12. **blt float: 1.0 < 1.0** - Equal, NOT taken
13. **blt float: NaN < x** - NaN, NOT taken

#### Test Scenarios - Native Int:
14. **blt native int** - Pointer-sized

#### Test Scenarios - Control Flow:
15. **blt forward** - Skip code
16. **blt backward** - Loop

### 7.6 bne.un.s (0x33)
- **Description**: Branch if not equal (unsigned/unordered, short form)
- **Stack**: ..., value1, value2 → ...
- **Operand**: int8 offset
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 18

#### Test Scenarios - Unsigned Inequality:
1. **bne.un: 1 != 2** - Unequal, taken
2. **bne.un: 1 != 1** - Equal, NOT taken
3. **bne.un: 0 != 0** - Equal zeros, NOT taken
4. **bne.un: -1 != 1** - Different bit patterns, taken
5. **bne.un: 0xFFFFFFFF != 0** - Different, taken

#### Test Scenarios - 64-bit:
6. **bne.un int64: different** - Unequal, taken
7. **bne.un int64: equal** - Equal, NOT taken

#### Test Scenarios - Float (Unordered):
8. **bne.un float: 1.0 != 2.0** - Different, taken
9. **bne.un float: 1.0 != 1.0** - Equal, NOT taken
10. **bne.un float: NaN != NaN** - NaN unordered, taken
11. **bne.un float: NaN != 1.0** - NaN unordered, taken
12. **bne.un float: 1.0 != NaN** - NaN unordered, taken

#### Test Scenarios - References:
13. **bne.un: same ref** - Equal, NOT taken
14. **bne.un: different refs** - Different, taken
15. **bne.un: null != non-null** - Different, taken

#### Test Scenarios - Native Int:
16. **bne.un native int** - Pointer-sized

#### Test Scenarios - Control Flow:
17. **bne.un forward** - Skip code
18. **bne.un backward** - Loop

### 7.7 bge.un.s (0x34)
- **Description**: Branch if greater than or equal (unsigned, short form)
- **Stack**: ..., value1, value2 → ...
- **Operand**: int8 offset
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 14

#### Test Scenarios - Unsigned:
1. **bge.un: 5 >= 3** - Greater, taken
2. **bge.un: 5 >= 5** - Equal, taken
3. **bge.un: 3 >= 5** - Less, NOT taken
4. **bge.un: 0xFFFFFFFF >= 0** - -1 as max unsigned, taken
5. **bge.un: 0 >= 0xFFFFFFFF** - 0 < max unsigned, NOT taken
6. **bge.un: 0x80000000 >= 0x7FFFFFFF** - MSB set > MSB clear (unsigned), taken

#### Test Scenarios - 64-bit:
7. **bge.un int64 unsigned** - Large unsigned comparison

#### Test Scenarios - Float (Unordered):
8. **bge.un float: NaN >= x** - NaN → taken (unordered)
9. **bge.un float: x >= NaN** - NaN → taken (unordered)
10. **bge.un float: 2.0 >= 1.0** - Greater, taken

#### Test Scenarios - Native Int:
11. **bge.un native int unsigned** - Pointer-sized

#### Test Scenarios - Control Flow:
12. **bge.un forward** - Skip code
13. **bge.un backward** - Loop
14. **bge.un with pointers** - Address comparison

### 7.8 bgt.un.s (0x35)
- **Description**: Branch if greater than (unsigned, short form)
- **Stack**: ..., value1, value2 → ...
- **Operand**: int8 offset
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 14

#### Test Scenarios - Unsigned:
1. **bgt.un: 5 > 3** - Greater, taken
2. **bgt.un: 5 > 5** - Equal, NOT taken
3. **bgt.un: 0xFFFFFFFF > 0** - Max unsigned > 0, taken
4. **bgt.un: 0 > 0xFFFFFFFF** - 0 > max, NOT taken
5. **bgt.un: 0x80000000 > 0x7FFFFFFF** - MSB comparison (unsigned), taken

#### Test Scenarios - 64-bit:
6. **bgt.un int64 unsigned** - Large unsigned
7. **bgt.un int64 equal** - Equal, NOT taken

#### Test Scenarios - Float (Unordered):
8. **bgt.un float: NaN > x** - NaN → taken (unordered)
9. **bgt.un float: x > NaN** - NaN → taken (unordered)
10. **bgt.un float: 2.0 > 1.0** - Greater, taken

#### Test Scenarios - Native Int:
11. **bgt.un native int** - Pointer-sized unsigned

#### Test Scenarios - Control Flow:
12. **bgt.un forward** - Skip code
13. **bgt.un backward** - Loop
14. **bgt.un with pointers** - Address comparison

### 7.9 ble.un.s (0x36)
- **Description**: Branch if less than or equal (unsigned, short form)
- **Stack**: ..., value1, value2 → ...
- **Operand**: int8 offset
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 14

#### Test Scenarios - Unsigned:
1. **ble.un: 3 <= 5** - Less, taken
2. **ble.un: 5 <= 5** - Equal, taken
3. **ble.un: 5 <= 3** - Greater, NOT taken
4. **ble.un: 0 <= 0xFFFFFFFF** - 0 <= max unsigned, taken
5. **ble.un: 0xFFFFFFFF <= 0** - Max unsigned <= 0, NOT taken
6. **ble.un: 0x7FFFFFFF <= 0x80000000** - MSB comparison (unsigned), taken

#### Test Scenarios - 64-bit:
7. **ble.un int64 unsigned** - Large unsigned

#### Test Scenarios - Float (Unordered):
8. **ble.un float: NaN <= x** - NaN → taken (unordered)
9. **ble.un float: 1.0 <= 2.0** - Less, taken
10. **ble.un float: 1.0 <= 1.0** - Equal, taken

#### Test Scenarios - Native Int:
11. **ble.un native int** - Pointer-sized unsigned

#### Test Scenarios - Control Flow:
12. **ble.un forward** - Skip code
13. **ble.un backward** - Loop
14. **ble.un with pointers** - Address comparison

### 7.10 blt.un.s (0x37)
- **Description**: Branch if less than (unsigned, short form)
- **Stack**: ..., value1, value2 → ...
- **Operand**: int8 offset
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 14

#### Test Scenarios - Unsigned:
1. **blt.un: 3 < 5** - Less, taken
2. **blt.un: 5 < 5** - Equal, NOT taken
3. **blt.un: 5 < 3** - Greater, NOT taken
4. **blt.un: 0 < 0xFFFFFFFF** - 0 < max unsigned, taken
5. **blt.un: 0xFFFFFFFF < 0** - Max unsigned < 0, NOT taken
6. **blt.un: 0x7FFFFFFF < 0x80000000** - MSB comparison (unsigned), taken

#### Test Scenarios - 64-bit:
7. **blt.un int64 unsigned** - Large unsigned

#### Test Scenarios - Float (Unordered):
8. **blt.un float: NaN < x** - NaN → taken (unordered)
9. **blt.un float: 1.0 < 2.0** - Less, taken
10. **blt.un float: 1.0 < 1.0** - Equal, NOT taken

#### Test Scenarios - Native Int:
11. **blt.un native int** - Pointer-sized unsigned

#### Test Scenarios - Control Flow:
12. **blt.un forward** - Skip code
13. **blt.un backward** - Loop
14. **blt.un with pointers** - Address comparison

### 7.11 beq (0x3B)
- **Description**: Branch if equal
- **Stack**: ..., value1, value2 → ...
- **Operand**: int32 offset
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 8

#### Test Scenarios (Long form - same logic as beq.s):
1. **beq with long forward offset** - Jump > 127 bytes
2. **beq with long backward offset** - Jump < -128 bytes
3. **beq int32 equal** - Basic equality
4. **beq int64 equal** - 64-bit equality
5. **beq float equal** - Float equality
6. **beq reference equal** - Object reference
7. **beq NaN** - NaN != NaN
8. **beq null** - null == null

### 7.12 bge (0x3C)
- **Description**: Branch if greater than or equal (signed)
- **Stack**: ..., value1, value2 → ...
- **Operand**: int32 offset
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 6

#### Test Scenarios (Long form):
1. **bge with long offset** - Large jump distance
2. **bge int32 signed** - Basic signed comparison
3. **bge int64 signed** - 64-bit signed
4. **bge float** - Float comparison
5. **bge negative numbers** - Signed negative handling
6. **bge NaN** - NaN → NOT taken

### 7.13 bgt (0x3D)
- **Description**: Branch if greater than (signed)
- **Stack**: ..., value1, value2 → ...
- **Operand**: int32 offset
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 6

#### Test Scenarios (Long form):
1. **bgt with long offset** - Large jump distance
2. **bgt int32 signed** - Basic signed comparison
3. **bgt int64 signed** - 64-bit signed
4. **bgt float** - Float comparison
5. **bgt negative numbers** - Signed negative handling
6. **bgt NaN** - NaN → NOT taken

### 7.14 ble (0x3E)
- **Description**: Branch if less than or equal (signed)
- **Stack**: ..., value1, value2 → ...
- **Operand**: int32 offset
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 6

#### Test Scenarios (Long form):
1. **ble with long offset** - Large jump distance
2. **ble int32 signed** - Basic signed comparison
3. **ble int64 signed** - 64-bit signed
4. **ble float** - Float comparison
5. **ble negative numbers** - Signed negative handling
6. **ble NaN** - NaN → NOT taken

### 7.15 blt (0x3F)
- **Description**: Branch if less than (signed)
- **Stack**: ..., value1, value2 → ...
- **Operand**: int32 offset
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 6

#### Test Scenarios (Long form):
1. **blt with long offset** - Large jump distance
2. **blt int32 signed** - Basic signed comparison
3. **blt int64 signed** - 64-bit signed
4. **blt float** - Float comparison
5. **blt negative numbers** - Signed negative handling
6. **blt NaN** - NaN → NOT taken

### 7.16 bne.un (0x40)
- **Description**: Branch if not equal (unsigned/unordered)
- **Stack**: ..., value1, value2 → ...
- **Operand**: int32 offset
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 6

#### Test Scenarios (Long form):
1. **bne.un with long offset** - Large jump distance
2. **bne.un int32** - Basic inequality
3. **bne.un int64** - 64-bit inequality
4. **bne.un float** - Float inequality
5. **bne.un NaN** - NaN unordered → taken
6. **bne.un references** - Object reference inequality

### 7.17 bge.un (0x41)
- **Description**: Branch if greater than or equal (unsigned)
- **Stack**: ..., value1, value2 → ...
- **Operand**: int32 offset
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 6

#### Test Scenarios (Long form):
1. **bge.un with long offset** - Large jump distance
2. **bge.un int32 unsigned** - Basic unsigned comparison
3. **bge.un int64 unsigned** - 64-bit unsigned
4. **bge.un 0xFFFFFFFF >= 0** - -1 as max unsigned
5. **bge.un float NaN** - NaN unordered → taken
6. **bge.un pointers** - Address comparison

### 7.18 bgt.un (0x42)
- **Description**: Branch if greater than (unsigned)
- **Stack**: ..., value1, value2 → ...
- **Operand**: int32 offset
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 6

#### Test Scenarios (Long form):
1. **bgt.un with long offset** - Large jump distance
2. **bgt.un int32 unsigned** - Basic unsigned comparison
3. **bgt.un int64 unsigned** - 64-bit unsigned
4. **bgt.un 0xFFFFFFFF > 0** - Max unsigned > 0
5. **bgt.un float NaN** - NaN unordered → taken
6. **bgt.un pointers** - Address comparison

### 7.19 ble.un (0x43)
- **Description**: Branch if less than or equal (unsigned)
- **Stack**: ..., value1, value2 → ...
- **Operand**: int32 offset
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 6

#### Test Scenarios (Long form):
1. **ble.un with long offset** - Large jump distance
2. **ble.un int32 unsigned** - Basic unsigned comparison
3. **ble.un int64 unsigned** - 64-bit unsigned
4. **ble.un 0 <= 0xFFFFFFFF** - 0 <= max unsigned
5. **ble.un float NaN** - NaN unordered → taken
6. **ble.un pointers** - Address comparison

### 7.20 blt.un (0x44)
- **Description**: Branch if less than (unsigned)
- **Stack**: ..., value1, value2 → ...
- **Operand**: int32 offset
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 6

#### Test Scenarios (Long form):
1. **blt.un with long offset** - Large jump distance
2. **blt.un int32 unsigned** - Basic unsigned comparison
3. **blt.un int64 unsigned** - 64-bit unsigned
4. **blt.un 0 < 0xFFFFFFFF** - 0 < max unsigned
5. **blt.un float NaN** - NaN unordered → taken
6. **blt.un pointers** - Address comparison

---

## 8. Control Flow - Switch (0x45)

### 8.1 switch (0x45)
- **Description**: Jump table switch
- **Stack**: ..., value → ...
- **Operand**: uint32 count, int32[count] targets
- **Status**: [ ]
- **Priority**: P1
- **Tests Needed**: 25

#### Test Scenarios - Basic Switch:
1. **Switch with 2 cases** - Minimal switch table
2. **Switch with 3 cases** - Small table
3. **Switch with 10 cases** - Medium table
4. **Switch with 100 cases** - Large table
5. **Switch with 256 cases** - Very large table

#### Test Scenarios - Index Values:
6. **Switch index = 0** - First case
7. **Switch index = 1** - Second case
8. **Switch index = N-1** - Last valid case
9. **Switch index = N** - Fall through (out of range)
10. **Switch index > N** - Fall through (way out of range)
11. **Switch index = -1** - Negative (unsigned interpretation = large)
12. **Switch index = int32.MaxValue** - Huge index

#### Test Scenarios - Fall-Through:
13. **Fall through to next instruction** - Index out of range
14. **Fall through with code after switch** - Verify execution continues

#### Test Scenarios - Target Offsets:
15. **Forward jump targets** - All cases jump forward
16. **Backward jump targets** - Cases jump backward (loop)
17. **Mixed forward/backward** - Various targets
18. **Target to same location** - Multiple cases same target
19. **Target to next instruction** - Effectively nop

#### Test Scenarios - Edge Cases:
20. **Switch with 0 cases** - Empty table (always fall through)
21. **Switch with 1 case** - Single target
22. **Switch preserves stack** - Values below switch value preserved
23. **Switch at method end** - Boundary conditions

#### Test Scenarios - Type Handling:
24. **Switch with native int index** - 64-bit index on 64-bit
25. **Switch with int64 index** - Should use low 32 bits or specific behavior

---

## 9. Indirect Load (0x46-0x50)

**Common Test Patterns for All ldind.* opcodes:**
- Load from managed pointer (ldloca result)
- Load from native pointer (unmanaged memory)
- Load with unaligned prefix
- Load with volatile prefix
- Round-trip: store then load
- Load from array element address (ldelema)
- Load from field address (ldflda)

### 9.1 ldind.i1 (0x46)
- **Description**: Load int8 from address, sign-extend to int32
- **Stack**: ..., addr → ..., value
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 12

#### Test Scenarios:
1. **Load positive int8 (0x7F = 127)** - Sign-extend to 0x0000007F
2. **Load negative int8 (0x80 = -128)** - Sign-extend to 0xFFFFFF80
3. **Load zero** - 0x00 → 0x00000000
4. **Load -1 (0xFF)** - Sign-extend to 0xFFFFFFFF
5. **Load from local address** - ldloca.s; ldind.i1
6. **Load from argument address** - ldarga.s; ldind.i1
7. **Load from struct field** - Struct member access
8. **Load from array element** - sbyte[] access
9. **Load with unaligned prefix** - Potentially unaligned address
10. **Load with volatile prefix** - Volatile read
11. **Multiple loads same address** - Stability
12. **Load after modification** - Store then load

### 9.2 ldind.u1 (0x47)
- **Description**: Load uint8 from address, zero-extend to int32
- **Stack**: ..., addr → ..., value
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 10

#### Test Scenarios:
1. **Load 0x00** - Zero-extend to 0x00000000
2. **Load 0x7F (127)** - Zero-extend to 0x0000007F
3. **Load 0x80 (128)** - Zero-extend to 0x00000080 (NOT sign-extend!)
4. **Load 0xFF (255)** - Zero-extend to 0x000000FF (NOT 0xFFFFFFFF!)
5. **Load from local address** - byte local
6. **Load from argument address** - byte parameter
7. **Load from array** - byte[] element
8. **Load with unaligned prefix** - Unaligned access
9. **Load with volatile prefix** - Volatile read
10. **Difference from ldind.i1** - 0x80 loads as 128, not -128

### 9.3 ldind.i2 (0x48)
- **Description**: Load int16 from address, sign-extend to int32
- **Stack**: ..., addr → ..., value
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 10

#### Test Scenarios:
1. **Load positive int16 (0x7FFF = 32767)** - Sign-extend to 0x00007FFF
2. **Load negative int16 (0x8000 = -32768)** - Sign-extend to 0xFFFF8000
3. **Load zero** - 0x0000 → 0x00000000
4. **Load -1 (0xFFFF)** - Sign-extend to 0xFFFFFFFF
5. **Load from local address** - short local
6. **Load from array** - short[] element
7. **Load with unaligned prefix** - Potentially unaligned
8. **Load with volatile prefix** - Volatile read
9. **Aligned address** - 2-byte aligned
10. **Verify sign extension** - Bit 15 propagates to bits 16-31

### 9.4 ldind.u2 (0x49)
- **Description**: Load uint16 from address, zero-extend to int32
- **Stack**: ..., addr → ..., value
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 10

#### Test Scenarios:
1. **Load 0x0000** - Zero-extend
2. **Load 0x7FFF (32767)** - Zero-extend
3. **Load 0x8000 (32768)** - Zero-extend to 0x00008000 (not negative!)
4. **Load 0xFFFF (65535)** - Zero-extend to 0x0000FFFF (not -1!)
5. **Load from char local** - char as uint16
6. **Load from ushort array** - ushort[] element
7. **Load with unaligned** - Unaligned access
8. **Load with volatile** - Volatile read
9. **Aligned address** - 2-byte aligned
10. **Difference from ldind.i2** - 0x8000 loads as 32768, not -32768

### 9.5 ldind.i4 (0x4A)
- **Description**: Load int32 from address
- **Stack**: ..., addr → ..., value
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 12

#### Test Scenarios:
1. **Load 0** - Zero value
2. **Load 1** - Positive
3. **Load -1** - All bits set
4. **Load Int32.MaxValue** - 0x7FFFFFFF
5. **Load Int32.MinValue** - 0x80000000
6. **Load from local** - int local access
7. **Load from argument** - int parameter access
8. **Load from array** - int[] element
9. **Load from struct field** - Struct member
10. **Load with volatile** - Volatile read
11. **Load with unaligned** - Unaligned access
12. **Load at 4-byte boundary** - Proper alignment

### 9.6 ldind.u4 (0x4B)
- **Description**: Load uint32 from address
- **Stack**: ..., addr → ..., value
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 8

#### Test Scenarios:
1. **Load 0** - Zero
2. **Load 0xFFFFFFFF** - Max uint32
3. **Load 0x80000000** - High bit set (positive as unsigned)
4. **Load from uint local** - uint access
5. **Load from uint array** - uint[] element
6. **Load with volatile** - Volatile read
7. **Load at aligned address** - 4-byte aligned
8. **Bit pattern preservation** - All 32 bits preserved

### 9.7 ldind.i8 (0x4C)
- **Description**: Load int64 from address
- **Stack**: ..., addr → ..., value
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 12

#### Test Scenarios:
1. **Load 0L** - Zero
2. **Load 1L** - One
3. **Load -1L** - All 64 bits set
4. **Load Int64.MaxValue** - 0x7FFFFFFFFFFFFFFF
5. **Load Int64.MinValue** - 0x8000000000000000
6. **Load value > Int32.MaxValue** - Tests full 64-bit range
7. **Load from long local** - long access
8. **Load from long array** - long[] element
9. **Load with volatile** - Volatile read
10. **Load with unaligned** - 8-byte value at unaligned addr
11. **Load at 8-byte boundary** - Proper alignment
12. **High 32 bits only set** - 0xFFFFFFFF00000000

### 9.8 ldind.i (0x4D)
- **Description**: Load native int from address
- **Stack**: ..., addr → ..., value
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 10

#### Test Scenarios:
1. **Load 0** - Zero native int
2. **Load -1** - All bits set
3. **Load pointer value** - Address as native int
4. **Load from IntPtr local** - IntPtr access
5. **Load from native int field** - Field access
6. **Load with volatile** - Volatile read
7. **Platform-specific size** - 4 bytes on 32-bit, 8 on 64-bit
8. **Load IntPtr.MaxValue** - Platform max
9. **Load IntPtr.MinValue** - Platform min
10. **Load null pointer** - Zero as pointer

### 9.9 ldind.r4 (0x4E)
- **Description**: Load float32 from address
- **Stack**: ..., addr → ..., value
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 14

#### Test Scenarios:
1. **Load 0.0f** - Zero
2. **Load 1.0f** - One
3. **Load -1.0f** - Negative one
4. **Load Float.MaxValue** - Maximum
5. **Load Float.MinValue** - Minimum (most negative)
6. **Load Float.Epsilon** - Smallest positive
7. **Load +Infinity** - Positive infinity
8. **Load -Infinity** - Negative infinity
9. **Load NaN** - Not a number
10. **Load from float local** - float access
11. **Load from float array** - float[] element
12. **Load with volatile** - Volatile read
13. **Load -0.0f** - Negative zero
14. **Load denormalized** - Subnormal value

### 9.10 ldind.r8 (0x4F)
- **Description**: Load float64 from address
- **Stack**: ..., addr → ..., value
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 14

#### Test Scenarios:
1. **Load 0.0** - Zero
2. **Load 1.0** - One
3. **Load -1.0** - Negative one
4. **Load Double.MaxValue** - Maximum
5. **Load Double.MinValue** - Minimum (most negative)
6. **Load Double.Epsilon** - Smallest positive
7. **Load +Infinity** - Positive infinity
8. **Load -Infinity** - Negative infinity
9. **Load NaN** - Not a number
10. **Load from double local** - double access
11. **Load from double array** - double[] element
12. **Load with volatile** - Volatile read
13. **Load -0.0** - Negative zero
14. **Load denormalized** - Subnormal value

### 9.11 ldind.ref (0x50)
- **Description**: Load object reference from address
- **Stack**: ..., addr → ..., obj
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 12

#### Test Scenarios:
1. **Load non-null reference** - Valid object
2. **Load null reference** - Null pointer pattern
3. **Load from ref parameter** - Byref to object
4. **Load from object local address** - Object local
5. **Load from array element** - object[] element
6. **Load from object field** - Reference field
7. **Load derived type reference** - Polymorphic
8. **Load array reference** - Array as object
9. **Load string reference** - String as object
10. **Load with volatile** - Volatile read
11. **Load boxed value type** - Box result
12. **GC safety** - Reference remains valid after GC

---

## 10. Indirect Store (0x51-0x57, 0xDF)

**Common Test Patterns for All stind.* opcodes:**
- Store to managed pointer (ldloca result)
- Store to native pointer
- Store with unaligned prefix
- Store with volatile prefix
- Store then load round-trip
- Store to array element address
- Store to field address

### 10.1 stind.ref (0x51)
- **Description**: Store object reference to address
- **Stack**: ..., addr, obj → ...
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 12

#### Test Scenarios:
1. **Store non-null reference** - Valid object
2. **Store null** - Null reference
3. **Store to ref parameter** - Out parameter
4. **Store to object local address** - Local modification
5. **Store to array element** - object[] element
6. **Store to object field** - Field modification
7. **Store derived to base-typed location** - Polymorphism
8. **Store array reference** - Array as object
9. **Store string reference** - String
10. **Store with volatile** - Volatile write
11. **GC write barrier** - Verify GC tracking
12. **Overwrite existing reference** - Replace value

### 10.2 stind.i1 (0x52)
- **Description**: Store int8 to address (truncates from int32)
- **Stack**: ..., addr, value → ...
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 10

#### Test Scenarios:
1. **Store 0** - Zero byte
2. **Store 127 (0x7F)** - Max positive sbyte
3. **Store -128 (0x80 as sbyte)** - Min negative sbyte
4. **Store 255** - Truncates to 0xFF
5. **Store 256** - Truncates to 0x00 (overflow ignored)
6. **Store to sbyte local** - Modify local
7. **Store to sbyte array** - sbyte[] element
8. **Store with volatile** - Volatile write
9. **Truncation from large int32** - High bits discarded
10. **Round-trip: store then load** - Verify value

### 10.3 stind.i2 (0x53)
- **Description**: Store int16 to address (truncates from int32)
- **Stack**: ..., addr, value → ...
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 10

#### Test Scenarios:
1. **Store 0** - Zero
2. **Store 32767 (0x7FFF)** - Max positive short
3. **Store -32768 (0x8000 as short)** - Min negative short
4. **Store 65535** - Truncates to 0xFFFF
5. **Store 65536** - Truncates to 0x0000 (overflow)
6. **Store to short local** - Modify local
7. **Store to short array** - short[] element
8. **Store with volatile** - Volatile write
9. **Store to char location** - char as uint16
10. **Round-trip verification** - Store then load

### 10.4 stind.i4 (0x54)
- **Description**: Store int32 to address
- **Stack**: ..., addr, value → ...
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 10

#### Test Scenarios:
1. **Store 0** - Zero
2. **Store 1** - One
3. **Store -1** - All bits set
4. **Store Int32.MaxValue** - Maximum
5. **Store Int32.MinValue** - Minimum
6. **Store to int local** - Modify local
7. **Store to int array** - int[] element
8. **Store to struct field** - Field modification
9. **Store with volatile** - Volatile write
10. **Round-trip verification** - Store then load

### 10.5 stind.i8 (0x55)
- **Description**: Store int64 to address
- **Stack**: ..., addr, value → ...
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 10

#### Test Scenarios:
1. **Store 0L** - Zero
2. **Store 1L** - One
3. **Store -1L** - All 64 bits set
4. **Store Int64.MaxValue** - Maximum
5. **Store Int64.MinValue** - Minimum
6. **Store value > Int32.MaxValue** - Full 64-bit range
7. **Store to long local** - Modify local
8. **Store to long array** - long[] element
9. **Store with volatile** - Volatile write
10. **Round-trip verification** - Store then load

### 10.6 stind.r4 (0x56)
- **Description**: Store float32 to address
- **Stack**: ..., addr, value → ...
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 12

#### Test Scenarios:
1. **Store 0.0f** - Zero
2. **Store 1.0f** - One
3. **Store -1.0f** - Negative one
4. **Store Float.MaxValue** - Maximum
5. **Store Float.Epsilon** - Smallest positive
6. **Store +Infinity** - Positive infinity
7. **Store -Infinity** - Negative infinity
8. **Store NaN** - Not a number
9. **Store to float local** - Modify local
10. **Store to float array** - float[] element
11. **Store with volatile** - Volatile write
12. **Precision from F type** - Stack F → float32 (may lose precision)

### 10.7 stind.r8 (0x57)
- **Description**: Store float64 to address
- **Stack**: ..., addr, value → ...
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 12

#### Test Scenarios:
1. **Store 0.0** - Zero
2. **Store 1.0** - One
3. **Store -1.0** - Negative one
4. **Store Double.MaxValue** - Maximum
5. **Store Double.Epsilon** - Smallest positive
6. **Store +Infinity** - Positive infinity
7. **Store -Infinity** - Negative infinity
8. **Store NaN** - Not a number
9. **Store to double local** - Modify local
10. **Store to double array** - double[] element
11. **Store with volatile** - Volatile write
12. **Round-trip verification** - Store then load

### 10.8 stind.i (0xDF)
- **Description**: Store native int to address
- **Stack**: ..., addr, value → ...
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 10

#### Test Scenarios:
1. **Store 0** - Zero native int
2. **Store -1** - All bits set
3. **Store pointer value** - Address
4. **Store IntPtr.MaxValue** - Platform max
5. **Store IntPtr.MinValue** - Platform min
6. **Store to IntPtr local** - Modify local
7. **Store to IntPtr field** - Field modification
8. **Store with volatile** - Volatile write
9. **Platform-specific size** - 4 or 8 bytes
10. **Round-trip verification** - Store then load

---

## 11. Arithmetic Operations (0x58-0x5E, 0xD6-0xDB)

**Valid Type Combinations (per ECMA-335 Table III.2):**
- int32 + int32 → int32
- int32 + native int → native int
- int64 + int64 → int64
- native int + int32 → native int
- native int + native int → native int
- F + F → F (float operations)
- Pointer arithmetic: & + int32 → &, & - & → native int

### 11.1 add (0x58)
- **Description**: Add two values
- **Stack**: ..., value1, value2 → ..., result
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 35
- **Notes**: Works on int32, int64, native int, float. Overflow wraps silently.

#### Test Scenarios - int32:
1. **0 + 0** - Zero result
2. **1 + 1** - Simple addition
3. **-1 + 1** - Opposite signs → 0
4. **MaxValue + 0** - Boundary
5. **MaxValue + 1** - Overflow wraps to MinValue
6. **MinValue + (-1)** - Underflow wraps to MaxValue
7. **MinValue + MinValue** - Large overflow

#### Test Scenarios - int64:
8. **0L + 0L** - Zero
9. **1L + 1L** - Simple
10. **Int64.MaxValue + 1L** - Overflow wraps
11. **Int64.MinValue + (-1L)** - Underflow wraps
12. **Large positive + large positive** - Full 64-bit

#### Test Scenarios - native int:
13. **IntPtr + IntPtr** - Platform-sized addition
14. **IntPtr + int32** - Mixed types (valid)
15. **int32 + IntPtr** - Mixed (commutative)

#### Test Scenarios - Float:
16. **1.0f + 2.0f** - Simple float add
17. **1.0 + 2.0** - Simple double add
18. **+Infinity + 1.0** - Infinity preserved
19. **-Infinity + (-1.0)** - Negative infinity
20. **+Infinity + (-Infinity)** - NaN result
21. **NaN + 1.0** - NaN preserved
22. **0.1 + 0.2** - Precision test (≈0.3)
23. **MaxValue + MaxValue** - Infinity result
24. **-0.0 + 0.0** - Zero handling

#### Test Scenarios - Pointer Arithmetic:
25. **& + int32** - Pointer + offset (not verifiable)
26. **int32 + &** - Offset + pointer

#### Test Scenarios - Type Combinations:
27. **int32 + int32** - Result is int32
28. **int64 + int64** - Result is int64
29. **native int + native int** - Result is native int
30. **int32 + native int** - Result is native int
31. **native int + int32** - Result is native int

#### Test Scenarios - Commutative Property:
32. **a + b == b + a** - Verify commutativity (int)
33. **a + b == b + a** - Verify commutativity (float, except NaN)

#### Test Scenarios - Associative Property:
34. **(a + b) + c == a + (b + c)** - Int (exact)
35. **(a + b) + c ≈ a + (b + c)** - Float (approximate)

### 11.2 sub (0x59)
- **Description**: Subtract value2 from value1
- **Stack**: ..., value1, value2 → ..., result
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 30

#### Test Scenarios - int32:
1. **0 - 0** - Zero result
2. **5 - 3** - Simple subtraction
3. **3 - 5** - Negative result
4. **-1 - (-1)** - Zero from negatives
5. **MinValue - 1** - Underflow wraps to MaxValue
6. **MaxValue - (-1)** - Overflow wraps to MinValue
7. **0 - MinValue** - Overflow

#### Test Scenarios - int64:
8. **0L - 0L** - Zero
9. **Int64.MinValue - 1L** - Underflow wraps
10. **Large - small** - Full 64-bit range

#### Test Scenarios - native int:
11. **IntPtr - IntPtr** - Result is native int
12. **IntPtr - int32** - Mixed types

#### Test Scenarios - Float:
13. **3.0 - 1.0** - Simple
14. **1.0 - 3.0** - Negative result
15. **+Infinity - 1.0** - Infinity
16. **+Infinity - (+Infinity)** - NaN result
17. **NaN - 1.0** - NaN preserved
18. **0.0 - (-0.0)** - Zero handling

#### Test Scenarios - Pointer Arithmetic:
19. **& - int32** - Pointer - offset
20. **& - &** - Pointer difference → native int

#### Test Scenarios - Type Combinations:
21. **int32 - int32** - Result int32
22. **int64 - int64** - Result int64
23. **native int - native int** - Result native int
24. **native int - int32** - Result native int

#### Test Scenarios - Edge Cases:
25. **a - a** - Always zero (except NaN)
26. **a - 0** - Identity
27. **0 - a** - Negation
28. **MinValue - MaxValue** - Maximum difference
29. **Anti-commutative** - a - b = -(b - a)
30. **Subtraction vs negative add** - a - b = a + (-b)

### 11.3 mul (0x5A)
- **Description**: Multiply two values
- **Stack**: ..., value1, value2 → ..., result
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 30

#### Test Scenarios - int32:
1. **0 * anything** - Zero result
2. **1 * x** - Identity
3. **-1 * x** - Negation
4. **2 * 3** - Simple multiplication
5. **-2 * 3** - Mixed signs
6. **-2 * -3** - Both negative
7. **MaxValue * 2** - Overflow wraps
8. **MinValue * -1** - Overflow (MinValue has no positive equivalent)

#### Test Scenarios - int64:
9. **0L * x** - Zero
10. **Large * large** - 64-bit overflow
11. **Int32.MaxValue * Int32.MaxValue** - Beyond int32

#### Test Scenarios - native int:
12. **IntPtr * IntPtr** - Platform multiplication

#### Test Scenarios - Float:
13. **2.0 * 3.0** - Simple
14. **-2.0 * 3.0** - Mixed signs
15. **0.0 * Infinity** - NaN result
16. **Infinity * 2.0** - Infinity
17. **Infinity * 0.0** - NaN
18. **NaN * anything** - NaN
19. **Very small * very small** - Underflow to zero
20. **Very large * very large** - Overflow to infinity

#### Test Scenarios - Properties:
21. **Commutative** - a * b = b * a
22. **Associative** - (a * b) * c = a * (b * c)
23. **Distributive** - a * (b + c) = a*b + a*c

#### Test Scenarios - Powers of 2:
24. **x * 2** - Equivalent to x << 1
25. **x * 4** - Equivalent to x << 2
26. **x * 8** - Equivalent to x << 3

#### Test Scenarios - Type Combinations:
27. **int32 * int32** - Result int32
28. **int64 * int64** - Result int64
29. **native int * native int** - Result native int
30. **int32 * native int** - Result native int

### 11.4 div (0x5B)
- **Description**: Divide value1 by value2 (signed)
- **Stack**: ..., value1, value2 → ..., result
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 28
- **Notes**: Throws DivideByZeroException if value2 is zero

#### Test Scenarios - int32:
1. **6 / 2** - Exact division
2. **7 / 2** - Truncates toward zero → 3
3. **-7 / 2** - Truncates toward zero → -3
4. **7 / -2** - Truncates toward zero → -3
5. **-7 / -2** - Positive result → 3
6. **0 / x** - Zero result
7. **x / 1** - Identity
8. **x / -1** - Negation
9. **MinValue / -1** - Overflow! (no positive equivalent)
10. **x / 0** - DivideByZeroException

#### Test Scenarios - int64:
11. **Large / 2** - 64-bit division
12. **Int64.MinValue / -1** - Overflow

#### Test Scenarios - Float:
13. **6.0 / 2.0** - Exact
14. **1.0 / 3.0** - Repeating decimal
15. **1.0 / 0.0** - +Infinity (not exception!)
16. **-1.0 / 0.0** - -Infinity
17. **0.0 / 0.0** - NaN
18. **Infinity / Infinity** - NaN
19. **x / Infinity** - 0.0
20. **Infinity / x** - Infinity (sign depends)
21. **NaN / x** - NaN

#### Test Scenarios - Truncation Direction:
22. **5 / 3 = 1** - Truncate toward zero
23. **-5 / 3 = -1** - Truncate toward zero (not -2)
24. **5 / -3 = -1** - Truncate toward zero
25. **-5 / -3 = 1** - Truncate toward zero

#### Test Scenarios - Type Combinations:
26. **int32 / int32** - Result int32
27. **int64 / int64** - Result int64
28. **native int / native int** - Result native int

### 11.5 div.un (0x5C)
- **Description**: Divide value1 by value2 (unsigned)
- **Stack**: ..., value1, value2 → ..., result
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 14

#### Test Scenarios - Unsigned int32:
1. **6u / 2u** - Simple
2. **7u / 2u** - Truncates → 3
3. **0u / x** - Zero
4. **0xFFFFFFFF / 2** - Large unsigned (2147483647)
5. **0x80000000 / 2** - High bit set (1073741824)
6. **x / 0** - DivideByZeroException

#### Test Scenarios - Unsigned int64:
7. **0xFFFFFFFFFFFFFFFF / 2** - Large unsigned
8. **Large / 1** - Identity

#### Test Scenarios - Interpretation Difference:
9. **-1 treated as 0xFFFFFFFF** - Unsigned interpretation
10. **0x80000000 is 2147483648, not MinValue** - Not negative

#### Test Scenarios - Type Combinations:
11. **int32 / int32 unsigned** - Result int32
12. **int64 / int64 unsigned** - Result int64
13. **native int unsigned** - Result native int
14. **Compare with div (signed)** - Different results for negative bit patterns

### 11.6 rem (0x5D)
- **Description**: Remainder of division (signed)
- **Stack**: ..., value1, value2 → ..., result
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 22

#### Test Scenarios - int32:
1. **7 % 3 = 1** - Basic remainder
2. **6 % 3 = 0** - Exact division
3. **-7 % 3 = -1** - Negative dividend, positive remainder sign
4. **7 % -3 = 1** - Positive dividend, positive remainder
5. **-7 % -3 = -1** - Negative dividend
6. **0 % x = 0** - Zero dividend
7. **x % 1 = 0** - Divisor of 1
8. **x % -1 = 0** - Divisor of -1
9. **x % 0** - DivideByZeroException
10. **MinValue % -1** - Potential overflow (usually 0)

#### Test Scenarios - int64:
11. **Large % small** - 64-bit remainder
12. **Int64.MaxValue % 10** - Remainder of large value

#### Test Scenarios - Float:
13. **7.5 % 2.5 = 0.0** - Exact
14. **7.0 % 2.0 = 1.0** - Basic
15. **-7.0 % 2.0 = -1.0** - Negative dividend
16. **x % 0.0** - NaN (not exception for float)
17. **Infinity % x** - NaN
18. **x % Infinity** - x (the dividend)
19. **NaN % x** - NaN

#### Test Scenarios - Mathematical Identity:
20. **a = (a / b) * b + (a % b)** - Verify for int
21. **a % b has same sign as a** - Sign rule

#### Test Scenarios - Type Combinations:
22. **int32 % int32** - Result int32

### 11.7 rem.un (0x5E)
- **Description**: Remainder of division (unsigned)
- **Stack**: ..., value1, value2 → ..., result
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 12

#### Test Scenarios - Unsigned:
1. **7u % 3u = 1u** - Basic
2. **6u % 3u = 0u** - Exact
3. **0xFFFFFFFF % 10** - Large unsigned
4. **0x80000000 % 3** - High bit set
5. **0u % x = 0u** - Zero dividend
6. **x % 0** - DivideByZeroException

#### Test Scenarios - Interpretation:
7. **-1 as 0xFFFFFFFF** - Unsigned interpretation
8. **Compare with rem (signed)** - Different results

#### Test Scenarios - Type Combinations:
9. **int32 unsigned** - Result int32
10. **int64 unsigned** - Result int64
11. **native int unsigned** - Result native int
12. **Mathematical identity** - a = (a /u b) * b + (a %u b)

### 11.8 add.ovf (0xD6)
- **Description**: Add with overflow check (signed)
- **Stack**: ..., value1, value2 → ..., result
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 16
- **Notes**: Throws OverflowException on overflow

#### Test Scenarios - No Overflow:
1. **0 + 0** - Zero
2. **1 + 1** - Simple
3. **-1 + 1** - Zero result
4. **MaxValue + 0** - Boundary, no overflow
5. **MinValue + 0** - Boundary, no overflow

#### Test Scenarios - Positive Overflow (throws):
6. **MaxValue + 1** - OverflowException
7. **MaxValue + MaxValue** - OverflowException
8. **Large positive + large positive** - OverflowException

#### Test Scenarios - Negative Overflow (throws):
9. **MinValue + (-1)** - OverflowException
10. **MinValue + MinValue** - OverflowException
11. **Large negative + large negative** - OverflowException

#### Test Scenarios - int64:
12. **Int64.MaxValue + 1L** - OverflowException
13. **Int64.MinValue + (-1L)** - OverflowException
14. **Normal 64-bit add** - No exception

#### Test Scenarios - native int:
15. **IntPtr overflow** - Platform-specific behavior

#### Test Scenarios - Mixed:
16. **int32 + native int overflow** - Check overflow detection

### 11.9 add.ovf.un (0xD7)
- **Description**: Add with overflow check (unsigned)
- **Stack**: ..., value1, value2 → ..., result
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 12

#### Test Scenarios - No Overflow:
1. **0 + 0** - Zero
2. **1 + 1** - Simple
3. **0xFFFFFFFE + 1** - Just under max, OK
4. **0 + 0xFFFFFFFF** - Max value, OK

#### Test Scenarios - Overflow (throws):
5. **0xFFFFFFFF + 1** - OverflowException
6. **0xFFFFFFFF + 0xFFFFFFFF** - OverflowException
7. **0x80000000 + 0x80000000** - OverflowException

#### Test Scenarios - int64:
8. **UInt64.MaxValue + 1** - OverflowException
9. **Normal unsigned 64-bit** - No exception

#### Test Scenarios - Interpretation:
10. **Treats values as unsigned** - 0xFFFFFFFF is max, not -1
11. **Compare with add.ovf** - Different overflow conditions

#### Test Scenarios - Pointer:
12. **Pointer + offset overflow** - Address space overflow

### 11.10 mul.ovf (0xD8)
- **Description**: Multiply with overflow check (signed)
- **Stack**: ..., value1, value2 → ..., result
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 14

#### Test Scenarios - No Overflow:
1. **0 * anything** - Zero, no overflow
2. **1 * x** - Identity, no overflow
3. **100 * 100** - 10000, no overflow
4. **-1 * x** - Negation, usually OK

#### Test Scenarios - Overflow (throws):
5. **MaxValue * 2** - OverflowException
6. **MinValue * -1** - OverflowException (no positive equivalent)
7. **MinValue * 2** - OverflowException
8. **Large * large** - OverflowException
9. **46341 * 46341** - Just overflows int32 (2147488281 > MaxValue)

#### Test Scenarios - Near Overflow:
10. **46340 * 46340** - 2147395600, just under MaxValue

#### Test Scenarios - int64:
11. **Int64.MaxValue * 2** - OverflowException
12. **Normal 64-bit multiply** - No exception

#### Test Scenarios - native int:
13. **IntPtr multiply overflow** - Platform behavior

#### Test Scenarios - Edge:
14. **MinValue * 1** - No overflow (identity)

### 11.11 mul.ovf.un (0xD9)
- **Description**: Multiply with overflow check (unsigned)
- **Stack**: ..., value1, value2 → ..., result
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 10

#### Test Scenarios - No Overflow:
1. **0 * x** - Zero
2. **1 * x** - Identity
3. **65535 * 65535** - 4294836225, just under max

#### Test Scenarios - Overflow (throws):
4. **65536 * 65536** - OverflowException (2^32)
5. **0xFFFFFFFF * 2** - OverflowException
6. **0x80000000 * 2** - OverflowException

#### Test Scenarios - int64:
7. **UInt64.MaxValue * 2** - OverflowException
8. **Normal unsigned 64-bit** - No exception

#### Test Scenarios - Interpretation:
9. **Unsigned interpretation** - 0xFFFFFFFF is 4294967295
10. **Compare with mul.ovf** - Different overflow thresholds

### 11.12 sub.ovf (0xDA)
- **Description**: Subtract with overflow check (signed)
- **Stack**: ..., value1, value2 → ..., result
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 14

#### Test Scenarios - No Overflow:
1. **0 - 0** - Zero
2. **5 - 3** - Simple
3. **-5 - (-3)** - Negative subtraction
4. **MaxValue - 0** - Boundary, OK

#### Test Scenarios - Overflow (throws):
5. **MinValue - 1** - OverflowException (underflow)
6. **MaxValue - (-1)** - OverflowException (overflow)
7. **MinValue - MaxValue** - OverflowException
8. **0 - MinValue** - OverflowException (MinValue has no positive)

#### Test Scenarios - Near Overflow:
9. **MinValue - (-1)** - MinValue + 1, OK
10. **MaxValue - 1** - OK

#### Test Scenarios - int64:
11. **Int64.MinValue - 1** - OverflowException
12. **Int64.MaxValue - (-1)** - OverflowException
13. **Normal 64-bit subtract** - No exception

#### Test Scenarios - native int:
14. **IntPtr subtract overflow** - Platform behavior

### 11.13 sub.ovf.un (0xDB)
- **Description**: Subtract with overflow check (unsigned)
- **Stack**: ..., value1, value2 → ..., result
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 10

#### Test Scenarios - No Overflow:
1. **5 - 3** - Simple, OK
2. **0xFFFFFFFF - 0xFFFFFFFE** - 1, OK
3. **x - 0** - Identity, OK

#### Test Scenarios - Underflow (throws):
4. **0 - 1** - OverflowException (would be negative)
5. **5 - 10** - OverflowException
6. **0 - 0xFFFFFFFF** - OverflowException

#### Test Scenarios - int64:
7. **0UL - 1UL** - OverflowException
8. **Normal unsigned 64-bit** - No exception

#### Test Scenarios - Interpretation:
9. **Unsigned interpretation** - Can't go negative
10. **Compare with sub.ovf** - Different underflow conditions

---

## 12. Bitwise Operations (0x5F-0x64)

**Valid Type Combinations (per ECMA-335 Table III.5):**
- int32 op int32 → int32
- int64 op int64 → int64
- native int op native int → native int
- int32 op native int → native int
- native int op int32 → native int

### 12.1 and (0x5F)
- **Description**: Bitwise AND
- **Stack**: ..., value1, value2 → ..., result
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 22

#### Test Scenarios - int32:
1. **0 & 0** - Zero result
2. **0xFFFFFFFF & 0xFFFFFFFF** - All bits preserved
3. **0xFFFFFFFF & 0** - Zero result
4. **0x0F0F0F0F & 0xF0F0F0F0** - No common bits → 0
5. **0xFF00FF00 & 0xFFFF0000** - Partial overlap → 0xFF000000
6. **x & x** - Identity (x & x = x)
7. **x & 0xFFFFFFFF** - Identity (all bits mask)
8. **x & 0** - Zero (zero mask)

#### Test Scenarios - int64:
9. **0L & 0L** - Zero
10. **0xFFFFFFFFFFFFFFFFL & pattern** - 64-bit AND
11. **High bits only** - Upper 32 bits interaction

#### Test Scenarios - native int:
12. **IntPtr & IntPtr** - Platform AND

#### Test Scenarios - Mixed Types:
13. **int32 & native int** - Result is native int
14. **native int & int32** - Result is native int

#### Test Scenarios - Bit Manipulation:
15. **Extract low byte: x & 0xFF** - Mask pattern
16. **Extract high byte: x & 0xFF000000** - High byte mask
17. **Clear bit: x & ~(1 << n)** - Bit clearing
18. **Test bit: (x & (1 << n)) != 0** - Bit testing

#### Test Scenarios - Properties:
19. **Commutative: a & b = b & a** - Order independence
20. **Associative: (a & b) & c = a & (b & c)** - Grouping
21. **Idempotent: a & a = a** - Self-AND
22. **Zero annihilator: a & 0 = 0** - Zero dominates

### 12.2 or (0x60)
- **Description**: Bitwise OR
- **Stack**: ..., value1, value2 → ..., result
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 20

#### Test Scenarios - int32:
1. **0 | 0** - Zero result
2. **0 | x** - Identity (x)
3. **0xFFFFFFFF | 0** - All bits
4. **0xFFFFFFFF | x** - All bits (dominates)
5. **0x0F0F0F0F | 0xF0F0F0F0** - All bits set → 0xFFFFFFFF
6. **0xFF00 | 0x00FF** - Combine bytes → 0xFFFF
7. **x | x** - Identity (x | x = x)

#### Test Scenarios - int64:
8. **0L | pattern** - 64-bit OR
9. **High + low bits** - Full 64-bit coverage

#### Test Scenarios - native int:
10. **IntPtr | IntPtr** - Platform OR

#### Test Scenarios - Mixed Types:
11. **int32 | native int** - Result is native int
12. **native int | int32** - Result is native int

#### Test Scenarios - Bit Manipulation:
13. **Set bit: x | (1 << n)** - Bit setting
14. **Combine flags: flags | FLAG_NEW** - Flag combination
15. **Merge bytes: (high << 8) | low** - Byte packing

#### Test Scenarios - Properties:
16. **Commutative: a | b = b | a** - Order independence
17. **Associative: (a | b) | c = a | (b | c)** - Grouping
18. **Idempotent: a | a = a** - Self-OR
19. **Identity: a | 0 = a** - Zero identity
20. **Domination: a | 0xFFFFFFFF = 0xFFFFFFFF** - All-ones dominates

### 12.3 xor (0x61)
- **Description**: Bitwise XOR
- **Stack**: ..., value1, value2 → ..., result
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 22

#### Test Scenarios - int32:
1. **0 ^ 0** - Zero
2. **x ^ 0** - Identity (x)
3. **x ^ x** - Zero (self-XOR)
4. **0xFFFFFFFF ^ 0xFFFFFFFF** - Zero
5. **0xFFFFFFFF ^ x** - Bitwise NOT of x
6. **0x0F0F0F0F ^ 0xF0F0F0F0** - All bits → 0xFFFFFFFF
7. **0xAAAAAAAA ^ 0x55555555** - Alternating → 0xFFFFFFFF

#### Test Scenarios - int64:
8. **0L ^ pattern** - 64-bit XOR
9. **Large ^ large** - Full 64-bit

#### Test Scenarios - native int:
10. **IntPtr ^ IntPtr** - Platform XOR

#### Test Scenarios - Mixed Types:
11. **int32 ^ native int** - Result is native int

#### Test Scenarios - Bit Manipulation:
12. **Toggle bit: x ^ (1 << n)** - Bit toggling
13. **Swap via XOR: a^=b; b^=a; a^=b** - Classic swap
14. **Encrypt/decrypt: data ^ key** - Simple cipher

#### Test Scenarios - Properties:
15. **Commutative: a ^ b = b ^ a** - Order independence
16. **Associative: (a ^ b) ^ c = a ^ (b ^ c)** - Grouping
17. **Self-inverse: a ^ a = 0** - Cancellation
18. **Identity: a ^ 0 = a** - Zero identity
19. **Double XOR: (a ^ b) ^ b = a** - Reversibility

#### Test Scenarios - Special Patterns:
20. **XOR with -1 equals NOT** - x ^ 0xFFFFFFFF = ~x
21. **Parity calculation** - Reduce to single bit
22. **Checksum/hash** - XOR combining

### 12.4 shl (0x62)
- **Description**: Shift left
- **Stack**: ..., value, shiftAmount → ..., result
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 24
- **Notes**: shiftAmount is masked (& 0x1F for int32, & 0x3F for int64)

#### Test Scenarios - int32:
1. **1 << 0** - No shift → 1
2. **1 << 1** - Simple shift → 2
3. **1 << 31** - Shift to sign bit → 0x80000000
4. **1 << 32** - Masked to 0, so result is 1 (no shift!)
5. **1 << 33** - Masked to 1, so result is 2
6. **0x80000000 << 1** - Shift out sign bit → 0
7. **-1 << 1** - 0xFFFFFFFF << 1 → 0xFFFFFFFE
8. **x << 0** - Identity
9. **0 << n** - Always zero

#### Test Scenarios - int64:
10. **1L << 0** - No shift
11. **1L << 63** - Shift to sign bit
12. **1L << 64** - Masked to 0 (no shift)
13. **1L << 32** - Into upper 32 bits

#### Test Scenarios - native int:
14. **IntPtr << n** - Platform shift

#### Test Scenarios - Shift Amount Type:
15. **Shift by int32** - Normal
16. **Shift by native int** - Also valid

#### Test Scenarios - Equivalences:
17. **x << 1 = x * 2** - Multiply by 2
18. **x << 2 = x * 4** - Multiply by 4
19. **x << n = x * (2^n)** - Power of 2 multiply

#### Test Scenarios - Masking Behavior:
20. **Shift by 32 (int32)** - Actually shifts by 0
21. **Shift by 64 (int64)** - Actually shifts by 0
22. **Shift by negative** - Masked, behaves as large positive

#### Test Scenarios - Edge Cases:
23. **Shift int32 by int64 shift amount** - Truncated to int32 first
24. **All bits shift out** - 1 << 32 wraps, use care

### 12.5 shr (0x63)
- **Description**: Shift right (arithmetic/signed)
- **Stack**: ..., value, shiftAmount → ..., result
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 22

#### Test Scenarios - int32 Positive:
1. **4 >> 1** - Simple → 2
2. **4 >> 2** - Shift by 2 → 1
3. **1 >> 1** - Shift to zero → 0
4. **0x7FFFFFFF >> 1** - Large positive → 0x3FFFFFFF

#### Test Scenarios - int32 Negative (sign extension):
5. **-1 >> 1** - 0xFFFFFFFF >> 1 → 0xFFFFFFFF (sign preserved!)
6. **-2 >> 1** - 0xFFFFFFFE >> 1 → 0xFFFFFFFF (-1)
7. **-4 >> 1** - → -2
8. **0x80000000 >> 1** - MinValue >> 1 → 0xC0000000 (still negative!)
9. **-128 >> 7** - → -1

#### Test Scenarios - int64:
10. **8L >> 1** - Simple 64-bit
11. **-1L >> 1** - Sign extension in 64-bit
12. **Int64.MinValue >> 1** - Sign preserved

#### Test Scenarios - native int:
13. **IntPtr >> n** - Platform arithmetic shift

#### Test Scenarios - Masking:
14. **x >> 32 (int32)** - Masked to 0, no shift
15. **x >> 64 (int64)** - Masked to 0, no shift

#### Test Scenarios - Equivalences:
16. **x >> 1 ≈ x / 2** - For positive (rounds toward -∞)
17. **-7 >> 1 = -4** - Not -3! (rounds down, not toward zero)

#### Test Scenarios - Edge Cases:
18. **0 >> n** - Always zero
19. **x >> 0** - Identity
20. **Shift by negative** - Masked to positive

#### Test Scenarios - Type Preservation:
21. **int32 result type** - Result is int32
22. **int64 result type** - Result is int64

### 12.6 shr.un (0x64)
- **Description**: Shift right (logical/unsigned)
- **Stack**: ..., value, shiftAmount → ..., result
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 20

#### Test Scenarios - int32:
1. **4 >> 1** - Simple → 2
2. **0xFFFFFFFF >> 1** - → 0x7FFFFFFF (zero-fill, not sign-extend!)
3. **0x80000000 >> 1** - → 0x40000000 (not 0xC0000000!)
4. **-1 >> 1 (unsigned)** - 0xFFFFFFFF >> 1 → 0x7FFFFFFF
5. **-2 >> 1 (unsigned)** - 0xFFFFFFFE >> 1 → 0x7FFFFFFF

#### Test Scenarios - int64:
6. **0xFFFFFFFFFFFFFFFFL >> 1** - → 0x7FFFFFFFFFFFFFFF
7. **Int64.MinValue >> 1 (unsigned)** - Zero-fill

#### Test Scenarios - native int:
8. **IntPtr >> n (unsigned)** - Platform logical shift

#### Test Scenarios - Masking:
9. **x >>> 32 (int32)** - Masked to 0
10. **x >>> 64 (int64)** - Masked to 0

#### Test Scenarios - Comparison with shr:
11. **Positive same as shr** - No difference for positive
12. **Negative different from shr** - shr.un zero-fills, shr sign-extends
13. **0x80000000 shr vs shr.un** - Different results!

#### Test Scenarios - Equivalences:
14. **x >>> 1 = x / 2 (unsigned)** - Unsigned division by 2
15. **Extracting bits** - (x >>> n) & mask

#### Test Scenarios - Edge Cases:
16. **0 >>> n** - Always zero
17. **x >>> 0** - Identity
18. **Shift by negative** - Masked

#### Test Scenarios - Type:
19. **int32 result** - Result is int32 (interpreted as signed but zero-filled)
20. **int64 result** - Result is int64

---

## 13. Unary Operations (0x65-0x66)

### 13.1 neg (0x65)
- **Description**: Negate value (two's complement for integers)
- **Stack**: ..., value → ..., result
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 20

#### Test Scenarios - int32:
1. **neg 0** - Zero (0 = -0)
2. **neg 1** - → -1
3. **neg -1** - → 1
4. **neg 5** - → -5
5. **neg -5** - → 5
6. **neg MaxValue** - → MinValue + 1 (no exact negative)
7. **neg MinValue** - → MinValue! (overflow, wraps to same value)

#### Test Scenarios - int64:
8. **neg 0L** - Zero
9. **neg 1L** - → -1L
10. **neg Int64.MaxValue** - → Int64.MinValue + 1
11. **neg Int64.MinValue** - → Int64.MinValue (overflow)

#### Test Scenarios - native int:
12. **neg IntPtr** - Platform negation
13. **neg IntPtr.Zero** - Zero

#### Test Scenarios - Float:
14. **neg 0.0** - → -0.0 (negative zero!)
15. **neg -0.0** - → 0.0
16. **neg 1.0** - → -1.0
17. **neg -1.0** - → 1.0
18. **neg +Infinity** - → -Infinity
19. **neg -Infinity** - → +Infinity
20. **neg NaN** - → NaN (still NaN)

### 13.2 not (0x66)
- **Description**: Bitwise complement (one's complement)
- **Stack**: ..., value → ..., result
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 16

#### Test Scenarios - int32:
1. **not 0** - → 0xFFFFFFFF (-1)
2. **not -1** - → 0
3. **not 0x0F0F0F0F** - → 0xF0F0F0F0
4. **not 0xAAAAAAAA** - → 0x55555555
5. **not MaxValue** - → MinValue
6. **not MinValue** - → MaxValue

#### Test Scenarios - int64:
7. **not 0L** - → 0xFFFFFFFFFFFFFFFF
8. **not -1L** - → 0
9. **not pattern** - Full 64-bit complement

#### Test Scenarios - native int:
10. **not IntPtr** - Platform complement

#### Test Scenarios - Properties:
11. **not (not x) = x** - Double complement is identity
12. **not x = -x - 1** - Relationship to negation
13. **not x = x ^ -1** - XOR equivalence

#### Test Scenarios - Bit Manipulation:
14. **Create mask: not 0 = all bits** - Mask creation
15. **Invert mask: not mask** - Mask inversion
16. **Clear bits: x & (not mask)** - Combined with AND

---

## 14. Conversion Operations (0x67-0x6E, 0x76, 0x82-0x8B, 0xB3-0xBA, 0xD1-0xD5, 0xE0)

**Conversion Categories:**
- **Truncating (no overflow check)**: Silently truncates, high bits discarded
- **Overflow-checked (conv.ovf.*)**: Throws OverflowException if value doesn't fit
- **Signed source (.ovf.*)**: Treats source as signed
- **Unsigned source (.ovf.*.un)**: Treats source as unsigned

**Float-to-int behavior**: Truncates toward zero. Overflow/NaN produce unspecified results (no exception for non-ovf).

### 14.1 conv.i1 (0x67)
- **Description**: Convert to int8, push as int32 (sign-extended)
- **Stack**: ..., value → ..., result
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 18

#### Test Scenarios - From int32:
1. **0 → 0** - Zero preserved
2. **127 → 127** - Max int8, preserved
3. **128 → -128** - Wraps (truncates to 0x80, sign-extends)
4. **255 → -1** - Wraps (truncates to 0xFF, sign-extends)
5. **256 → 0** - High byte discarded
6. **-1 → -1** - Negative preserved
7. **-128 → -128** - Min int8, preserved
8. **-129 → 127** - Wraps

#### Test Scenarios - From int64:
9. **0L → 0** - Zero
10. **127L → 127** - In range
11. **Large positive → truncated** - High 56 bits discarded

#### Test Scenarios - From float:
12. **0.0 → 0** - Zero
13. **127.9 → 127** - Truncate toward zero
14. **-128.9 → -128** - Truncate toward zero
15. **200.0 → unspecified** - Out of range (no exception)
16. **NaN → unspecified** - Undefined behavior

#### Test Scenarios - Sign Extension:
17. **Result 0x80 sign-extends to 0xFFFFFF80** - Verify sign extension
18. **Result 0x7F zero-extends (positive)** - Verify no extension needed

### 14.2 conv.i2 (0x68)
- **Description**: Convert to int16, push as int32 (sign-extended)
- **Stack**: ..., value → ..., result
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 14

#### Test Scenarios - From int32:
1. **0 → 0** - Zero
2. **32767 → 32767** - Max int16
3. **32768 → -32768** - Wraps
4. **65535 → -1** - Wraps
5. **65536 → 0** - High 16 bits discarded
6. **-1 → -1** - Negative preserved
7. **-32768 → -32768** - Min int16

#### Test Scenarios - From int64:
8. **Large → truncated** - High 48 bits discarded

#### Test Scenarios - From float:
9. **32767.9 → 32767** - Truncate
10. **-32768.9 → -32768** - Truncate
11. **NaN → unspecified** - Undefined

#### Test Scenarios - Sign Extension:
12. **Result 0x8000 sign-extends** - Verify sign extension
13. **Result 0x7FFF stays positive** - No extension needed
14. **Char to int16** - 0xFFFF → -1 (sign-extended)

### 14.3 conv.i4 (0x69)
- **Description**: Convert to int32
- **Stack**: ..., value → ..., result
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 14

#### Test Scenarios - From int64:
1. **0L → 0** - Zero
2. **MaxInt32 as int64 → MaxInt32** - Fits
3. **MinInt32 as int64 → MinInt32** - Fits
4. **MaxInt32 + 1 → MinInt32** - Overflow wraps
5. **Large positive → truncated** - High 32 bits lost

#### Test Scenarios - From native int (64-bit platform):
6. **IntPtr in int32 range → preserved** - Fits
7. **IntPtr > MaxInt32 → truncated** - High bits lost

#### Test Scenarios - From float:
8. **0.0 → 0** - Zero
9. **1.9 → 1** - Truncate toward zero
10. **-1.9 → -1** - Truncate toward zero
11. **2147483647.9 → 2147483647** - Max
12. **1e20 → unspecified** - Overflow
13. **NaN → unspecified** - Undefined
14. **Infinity → unspecified** - Undefined

### 14.4 conv.i8 (0x6A)
- **Description**: Convert to int64 (sign-extends from smaller types)
- **Stack**: ..., value → ..., result
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 14

#### Test Scenarios - From int32:
1. **0 → 0L** - Zero
2. **1 → 1L** - Positive
3. **-1 → -1L** - Sign-extended to 0xFFFFFFFFFFFFFFFF
4. **MaxInt32 → 2147483647L** - Preserved
5. **MinInt32 → -2147483648L** - Sign-extended

#### Test Scenarios - From native int:
6. **IntPtr → int64** - Platform-dependent sign extension

#### Test Scenarios - From float:
7. **0.0 → 0L** - Zero
8. **1.9 → 1L** - Truncate
9. **-1.9 → -1L** - Truncate toward zero
10. **9223372036854775807.0 → may lose precision** - Large float
11. **1e100 → unspecified** - Overflow
12. **NaN → unspecified** - Undefined

#### Test Scenarios - Edge Cases:
13. **int32 MinValue sign extends** - 0x80000000 → 0xFFFFFFFF80000000
14. **Pointer to int64** - Address as signed

### 14.5 conv.r4 (0x6B)
- **Description**: Convert to float32
- **Stack**: ..., value → ..., result
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 14

#### Test Scenarios - From int32:
1. **0 → 0.0f** - Zero
2. **1 → 1.0f** - Simple
3. **-1 → -1.0f** - Negative
4. **16777216 → exact** - 2^24, last exact int
5. **16777217 → rounds** - Precision loss begins
6. **MaxInt32 → approximate** - Loses precision

#### Test Scenarios - From int64:
7. **0L → 0.0f** - Zero
8. **Large → loses precision** - Float32 has ~7 digits

#### Test Scenarios - From float64:
9. **0.0 → 0.0f** - Zero
10. **1.0 → 1.0f** - Exact
11. **3.141592653589793 → 3.1415927f** - Precision loss
12. **1e100 → Infinity** - Out of float32 range
13. **1e-50 → may underflow** - Denormal or zero
14. **NaN → NaN** - Preserved as NaN

### 14.6 conv.r8 (0x6C)
- **Description**: Convert to float64
- **Stack**: ..., value → ..., result
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 12

#### Test Scenarios - From int32:
1. **0 → 0.0** - Zero
2. **1 → 1.0** - Simple
3. **-1 → -1.0** - Negative
4. **MaxInt32 → exact** - All int32 fit exactly

#### Test Scenarios - From int64:
5. **0L → 0.0** - Zero
6. **Small → exact** - Within 53-bit mantissa
7. **Large → may lose precision** - Beyond 2^53

#### Test Scenarios - From float32:
8. **0.0f → 0.0** - Zero
9. **1.0f → 1.0** - Exact
10. **Float.MaxValue → preserved** - Wider range

#### Test Scenarios - Edge Cases:
11. **Native int to double** - Platform conversion
12. **Pointer to double** - Address as float

### 14.7 conv.u4 (0x6D)
- **Description**: Convert to uint32, push as int32 (no sign extension)
- **Stack**: ..., value → ..., result
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 12

#### Test Scenarios - From int64:
1. **0L → 0** - Zero
2. **0xFFFFFFFF → -1 (as int32)** - Max uint32
3. **0x100000000L → 0** - Truncated (high bits lost)
4. **Large positive → truncated** - Only low 32 bits

#### Test Scenarios - From int32:
5. **-1 → 0xFFFFFFFF** - Reinterpreted (no change in bits)
6. **Positive → unchanged** - Same bits

#### Test Scenarios - From float:
7. **0.0 → 0** - Zero
8. **4294967295.0 → max uint32** - Maximum
9. **1e20 → unspecified** - Overflow
10. **-1.0 → unspecified** - Negative (undefined for unsigned)

#### Test Scenarios - Edge Cases:
11. **Result bit pattern same as conv.i4** - Only interpretation differs
12. **Native int truncation** - Platform-dependent

### 14.8 conv.u8 (0x6E)
- **Description**: Convert to uint64 (zero-extends from smaller types)
- **Stack**: ..., value → ..., result
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 12

#### Test Scenarios - From int32:
1. **0 → 0UL** - Zero
2. **1 → 1UL** - Positive
3. **-1 → 0xFFFFFFFFFFFFFFFF** - Sign-extended then reinterpreted
4. **MaxInt32 → 2147483647UL** - Preserved

#### Test Scenarios - From native int:
5. **IntPtr zero-extends** - Platform behavior

#### Test Scenarios - From float:
6. **0.0 → 0UL** - Zero
7. **1e19 → large uint64** - Large value
8. **-1.0 → unspecified** - Negative undefined
9. **1e30 → unspecified** - Overflow

#### Test Scenarios - Edge Cases:
10. **int32 -1 becomes max uint64** - Sign extension matters
11. **Difference from conv.i8** - Sign extension vs interpretation
12. **Pointer to uint64** - Address as unsigned

### 14.9 conv.r.un (0x76)
- **Description**: Convert unsigned integer to float
- **Stack**: ..., value → ..., result
- **Status**: [ ]
- **Priority**: P1
- **Tests Needed**: 10

#### Test Scenarios:
1. **0 → 0.0** - Zero
2. **1 → 1.0** - Simple
3. **0xFFFFFFFF (uint32) → 4294967295.0** - Max uint32 (not -1!)
4. **0x80000000 → 2147483648.0** - High bit as positive
5. **0xFFFFFFFFFFFFFFFF (uint64) → ~1.8e19** - Max uint64
6. **Compare with conv.r8** - Different for negative bit patterns
7. **Large uint64 → loses precision** - Beyond float64 mantissa
8. **uint32.MaxValue exact in double** - Fits in 53 bits
9. **Native int as unsigned** - Platform interpretation
10. **Sign bit interpreted as value** - Key difference from conv.r8

### 14.10-14.19: conv.ovf.*.un (Unsigned Source, Overflow-Checked)

**Common Pattern**: Treat source as unsigned, throw OverflowException if result doesn't fit target.

### 14.10 conv.ovf.i1.un (0x82)
- **Description**: Convert unsigned to int8 with overflow check
- **Tests Needed**: 8

1. **0 → 0** - OK
2. **127 → 127** - Max int8, OK
3. **128 → OverflowException** - > 127
4. **255 → OverflowException** - Too large
5. **uint64 large → OverflowException** - Way too large
6. **0x80 (as unsigned 128) → throws** - Exceeds signed int8 max

### 14.11 conv.ovf.i2.un (0x83)
- **Description**: Convert unsigned to int16 with overflow check
- **Tests Needed**: 6

1. **0 → 0** - OK
2. **32767 → 32767** - Max int16, OK
3. **32768 → OverflowException** - > 32767
4. **65535 → OverflowException** - Too large
5. **uint64 large → OverflowException** - Way too large

### 14.12 conv.ovf.i4.un (0x84)
- **Description**: Convert unsigned to int32 with overflow check
- **Tests Needed**: 6

1. **0 → 0** - OK
2. **2147483647 → 2147483647** - Max int32, OK
3. **2147483648 → OverflowException** - > MaxInt32
4. **0xFFFFFFFF → OverflowException** - Too large
5. **uint64 large → OverflowException** - Way too large

### 14.13 conv.ovf.i8.un (0x85)
- **Description**: Convert unsigned to int64 with overflow check
- **Tests Needed**: 6

1. **0 → 0** - OK
2. **Int64.MaxValue → Int64.MaxValue** - OK
3. **0x8000000000000000 → OverflowException** - > MaxInt64
4. **UInt64.MaxValue → OverflowException** - Too large

### 14.14 conv.ovf.u1.un (0x86)
- **Description**: Convert unsigned to uint8 with overflow check
- **Tests Needed**: 6

1. **0 → 0** - OK
2. **255 → 255** - Max uint8, OK
3. **256 → OverflowException** - > 255
4. **Large → OverflowException** - Too large

### 14.15 conv.ovf.u2.un (0x87)
- **Description**: Convert unsigned to uint16 with overflow check
- **Tests Needed**: 6

1. **0 → 0** - OK
2. **65535 → 65535** - Max uint16, OK
3. **65536 → OverflowException** - > 65535

### 14.16 conv.ovf.u4.un (0x88)
- **Description**: Convert unsigned to uint32 with overflow check
- **Tests Needed**: 6

1. **0 → 0** - OK
2. **0xFFFFFFFF → 0xFFFFFFFF** - Max uint32, OK
3. **0x100000000 → OverflowException** - > MaxUInt32

### 14.17 conv.ovf.u8.un (0x89)
- **Description**: Convert unsigned to uint64 with overflow check
- **Tests Needed**: 4

1. **0 → 0** - OK
2. **UInt64.MaxValue → MaxValue** - OK (no overflow possible from unsigned)
3. **Any unsigned → OK** - uint64 is largest unsigned

### 14.18 conv.ovf.i.un (0x8A)
- **Description**: Convert unsigned to native int with overflow check
- **Tests Needed**: 6

1. **0 → 0** - OK
2. **IntPtr.MaxValue → OK** - Platform max
3. **> IntPtr.MaxValue → OverflowException** - Too large for signed native int
4. **Platform-dependent thresholds** - Different on 32/64-bit

### 14.19 conv.ovf.u.un (0x8B)
- **Description**: Convert unsigned to native uint with overflow check
- **Tests Needed**: 4

1. **0 → 0** - OK
2. **UIntPtr.MaxValue → OK** - Platform max
3. **> UIntPtr.MaxValue → OverflowException** - On 32-bit, values > 2^32-1

### 14.20-14.27: conv.ovf.* (Signed Source, Overflow-Checked)

**Common Pattern**: Treat source as signed, throw OverflowException if result doesn't fit target.

### 14.20 conv.ovf.i1 (0xB3)
- **Description**: Convert to int8 with overflow check
- **Tests Needed**: 10

1. **0 → 0** - OK
2. **127 → 127** - Max, OK
3. **128 → OverflowException** - Too large
4. **-128 → -128** - Min, OK
5. **-129 → OverflowException** - Too small
6. **int64 in range → OK** - Within [-128, 127]
7. **int64 out of range → throws** - Outside range
8. **float 127.9 → 127** - Truncates, fits
9. **float 128.0 → OverflowException** - Doesn't fit
10. **NaN → OverflowException** - Invalid

### 14.21 conv.ovf.u1 (0xB4)
- **Description**: Convert to uint8 with overflow check
- **Tests Needed**: 8

1. **0 → 0** - OK
2. **255 → 255** - Max, OK
3. **256 → OverflowException** - Too large
4. **-1 → OverflowException** - Negative not allowed!

### 14.22 conv.ovf.i2 (0xB5)
- **Description**: Convert to int16 with overflow check
- **Tests Needed**: 8

1. **0 → 0** - OK
2. **32767 → 32767** - Max, OK
3. **32768 → OverflowException** - Too large
4. **-32768 → -32768** - Min, OK
5. **-32769 → OverflowException** - Too small

### 14.23 conv.ovf.u2 (0xB6)
- **Description**: Convert to uint16 with overflow check
- **Tests Needed**: 6

1. **0 → 0** - OK
2. **65535 → 65535** - Max, OK
3. **65536 → OverflowException** - Too large
4. **-1 → OverflowException** - Negative not allowed

### 14.24 conv.ovf.i4 (0xB7)
- **Description**: Convert to int32 with overflow check
- **Tests Needed**: 8

1. **0L → 0** - OK
2. **Int32.MaxValue → OK** - Max
3. **Int32.MinValue → OK** - Min
4. **(long)Int32.MaxValue + 1 → OverflowException** - Too large
5. **(long)Int32.MinValue - 1 → OverflowException** - Too small

### 14.25 conv.ovf.u4 (0xB8)
- **Description**: Convert to uint32 with overflow check
- **Tests Needed**: 6

1. **0L → 0** - OK
2. **0xFFFFFFFF → OK** - Max
3. **0x100000000 → OverflowException** - Too large
4. **-1L → OverflowException** - Negative not allowed

### 14.26 conv.ovf.i8 (0xB9)
- **Description**: Convert to int64 with overflow check
- **Tests Needed**: 6

1. **0 → 0L** - OK
2. **int32 any → OK** - All int32 fit
3. **float in range → OK** - Truncated, fits
4. **float out of range → OverflowException** - Too large/small
5. **NaN → OverflowException** - Invalid

### 14.27 conv.ovf.u8 (0xBA)
- **Description**: Convert to uint64 with overflow check
- **Tests Needed**: 6

1. **0 → 0UL** - OK
2. **Positive int64 → OK** - Fits
3. **-1 → OverflowException** - Negative not allowed
4. **float positive large → OK if fits** - Within range
5. **float negative → OverflowException** - Negative

### 14.28 conv.u2 (0xD1)
- **Description**: Convert to uint16, push as int32 (zero-extended)
- **Stack**: ..., value → ..., result
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 10

1. **0 → 0** - Zero
2. **65535 → 65535** - Max uint16
3. **65536 → 0** - Truncates
4. **-1 → 65535** - Low 16 bits of 0xFFFFFFFF
5. **0x12345678 → 0x5678** - Low 16 bits
6. **Result zero-extends** - 0xFFFF → 0x0000FFFF (not sign-extended!)
7. **Difference from conv.i2** - conv.i2 sign-extends, conv.u2 zero-extends

### 14.29 conv.u1 (0xD2)
- **Description**: Convert to uint8, push as int32 (zero-extended)
- **Stack**: ..., value → ..., result
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 10

1. **0 → 0** - Zero
2. **255 → 255** - Max uint8
3. **256 → 0** - Truncates
4. **-1 → 255** - Low 8 bits
5. **0x12345678 → 0x78** - Low 8 bits
6. **Result zero-extends** - 0xFF → 0x000000FF (not 0xFFFFFFFF!)
7. **Difference from conv.i1** - conv.i1 sign-extends, conv.u1 zero-extends

### 14.30 conv.i (0xD3)
- **Description**: Convert to native int (sign-extends from smaller)
- **Stack**: ..., value → ..., result
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 10

1. **int32 0 → IntPtr.Zero** - Zero
2. **int32 positive → IntPtr** - Preserved
3. **int32 -1 → IntPtr(-1)** - Sign-extends on 64-bit
4. **int64 → truncates on 32-bit** - Platform-dependent
5. **int64 → preserves on 64-bit** - Full 64 bits
6. **float → truncates** - Toward zero
7. **Pointer round-trip** - ptr → int → ptr
8. **Platform-specific tests** - 32-bit vs 64-bit behavior

### 14.31 conv.ovf.i (0xD4)
- **Description**: Convert to native int with overflow check
- **Stack**: ..., value → ..., result
- **Status**: [ ]
- **Priority**: P1
- **Tests Needed**: 8

1. **0 → IntPtr.Zero** - OK
2. **In range → OK** - Platform range
3. **int64 too large (32-bit) → OverflowException** - Doesn't fit
4. **int64 negative too small → OverflowException** - Doesn't fit
5. **On 64-bit: int64 always fits** - No overflow possible

### 14.32 conv.ovf.u (0xD5)
- **Description**: Convert to native uint with overflow check
- **Stack**: ..., value → ..., result
- **Status**: [ ]
- **Priority**: P1
- **Tests Needed**: 8

#### Test Scenarios:
1. **0 → UIntPtr.Zero** - OK
2. **Positive in range → OK** - Within platform limit
3. **-1 → OverflowException** - Negative not allowed for unsigned
4. **-1000 → OverflowException** - Any negative throws
5. **int64 too large (32-bit) → OverflowException** - Exceeds 32-bit max
6. **int64 positive (64-bit) → OK** - Fits in 64-bit native uint
7. **float negative → OverflowException** - Negative float
8. **Platform threshold tests** - Different on 32-bit vs 64-bit

### 14.33 conv.u (0xE0)
- **Description**: Convert to native uint (zero-extends from smaller)
- **Stack**: ..., value → ..., result
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 10

#### Test Scenarios:
1. **int32 0 → UIntPtr.Zero** - Zero
2. **int32 positive → UIntPtr** - Preserved
3. **int32 -1 → large UIntPtr** - Zero-extends 0xFFFFFFFF
4. **int64 → platform-dependent** - Truncates on 32-bit
5. **int64 large positive → preserved on 64-bit** - Full value
6. **float → truncated** - Toward zero
7. **Native int → reinterpret** - No conversion, just type change
8. **Difference from conv.i** - Zero-extension vs sign-extension
9. **Pointer round-trip** - ptr → uint → ptr
10. **High bit handling** - 0x80000000 stays positive as uint

---

## 15. Object Model - Method Calls (0x6F)

### 15.1 callvirt (0x6F)
- **Description**: Call virtual method
- **Stack**: ..., obj, arg0, arg1, ... argN → ..., retVal (maybe)
- **Operand**: method token
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 45
- **Notes**: Virtual dispatch on class methods, interface dispatch

#### Test Scenarios - Basic Virtual Dispatch:
1. **Call virtual method on base type** - Virtual dispatch works
2. **Call overridden method** - Derived class override called
3. **Call through base reference** - Polymorphic dispatch
4. **Call non-overridden virtual** - Base implementation used
5. **Call virtual on sealed override** - Sealed method behavior
6. **Call new virtual in derived** - 'new' keyword hiding
7. **Deep inheritance chain** - Multiple levels of override

#### Test Scenarios - Interface Dispatch:
8. **Call interface method** - Interface dispatch
9. **Explicit interface implementation** - Private impl
10. **Interface reimplementation** - Derived class re-implements
11. **Multiple interfaces** - Same method in multiple interfaces
12. **Interface default method** - C# 8.0+ default implementations
13. **Generic interface** - IComparable<T> etc.
14. **Covariant interface** - IEnumerable<Derived> → IEnumerable<Base>
15. **Contravariant interface** - IComparer<Base> → IComparer<Derived>

#### Test Scenarios - Generic Methods:
16. **Call generic virtual method** - T Method<T>()
17. **Generic method with constraints** - where T : class
18. **Value type generic argument** - int as T
19. **Reference type generic argument** - string as T
20. **Generic method in generic class** - Nested generics
21. **Generic interface on generic type** - Complex dispatch

#### Test Scenarios - Return Types:
22. **void return** - No return value
23. **int32 return** - Value type return
24. **int64 return** - 64-bit return
25. **float/double return** - Float returns
26. **Object reference return** - Reference return
27. **Struct return (small)** - Fits in registers
28. **Struct return (large)** - Requires return buffer
29. **Nullable<T> return** - Nullable value type

#### Test Scenarios - Parameters:
30. **No parameters** - Just 'this'
31. **Single value parameter** - obj.Method(int)
32. **Multiple parameters** - obj.Method(int, long, string)
33. **Object reference parameter** - Reference argument
34. **Large struct parameter** - Stack vs register
35. **ref/out parameters** - Byref arguments
36. **params array** - Variable arguments
37. **Optional parameters** - Default values

#### Test Scenarios - Null Handling:
38. **Null receiver throws** - NullReferenceException
39. **Null check elimination** - After null check
40. **Null propagation** - ?. operator

#### Test Scenarios - Special Cases:
41. **Call on value type (boxed)** - Virtual on boxed int
42. **Call Object.GetHashCode on struct** - Constrained call
43. **Call Object.ToString on value type** - Constrained virtual
44. **Virtual call to final method** - Devirtualization opportunity
45. **Cross-assembly virtual call** - External type override

---

## 16. Object Model - Object Operations (0x70-0x75, 0x79, 0x7A, 0x8C, 0xA5)

### 16.1 cpobj (0x70)
- **Description**: Copy value type from src to dest
- **Stack**: ..., dest, src → ...
- **Operand**: type token
- **Status**: [ ]
- **Priority**: P1
- **Tests Needed**: 12

#### Test Scenarios:
1. **Copy simple struct (4 bytes)** - Small value type
2. **Copy medium struct (16 bytes)** - Register-sized
3. **Copy large struct (100+ bytes)** - Requires memcpy
4. **Copy struct with reference fields** - GC tracking
5. **Copy to/from local** - Stack addresses
6. **Copy to/from field** - Object field addresses
7. **Copy to/from array element** - Via ldelema
8. **Overlapping source/dest** - Aliasing behavior
9. **Null source pointer** - NullReferenceException
10. **Null dest pointer** - NullReferenceException
11. **Copy generic struct** - Generic<T>
12. **Copy struct with nested struct** - Composite types

### 16.2 ldobj (0x71)
- **Description**: Load value type from address
- **Stack**: ..., addr → ..., value
- **Operand**: type token
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 14

#### Test Scenarios:
1. **Load int32 via address** - Basic load
2. **Load int64 via address** - 64-bit load
3. **Load small struct** - Fits in registers
4. **Load large struct** - Stack copy
5. **Load from local address** - Via ldloca
6. **Load from argument address** - Via ldarga
7. **Load from field address** - Via ldflda
8. **Load from array element** - Via ldelema
9. **Null address** - NullReferenceException
10. **Unaligned address** - Alignment handling
11. **Load generic value type** - Generic<T> as struct
12. **Load Nullable<T>** - Nullable semantics
13. **Load with volatile prefix** - Memory ordering
14. **Load primitive via ldobj** - int32 as value type

### 16.3 ldstr (0x72)
- **Description**: Load string literal
- **Stack**: ... → ..., string
- **Operand**: string token
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 12

#### Test Scenarios:
1. **Empty string ""** - String.Empty
2. **Single character "a"** - Minimal string
3. **ASCII string "Hello"** - Basic ASCII
4. **Unicode string "Hello \u4E16\u754C"** - Multi-byte chars
5. **String with null char** - "foo\0bar" embedded null
6. **Very long string (1000+ chars)** - Large allocation
7. **String with escapes** - "\t\r\n" etc.
8. **String interning** - Same literal = same reference
9. **Multiple different literals** - Different strings
10. **String in loop** - Same reference each iteration
11. **Cross-method string** - Same literal in different methods
12. **String at assembly boundary** - External string

### 16.4 newobj (0x73)
- **Description**: Create new object and call constructor
- **Stack**: ..., arg0, arg1, ... argN → ..., obj
- **Operand**: constructor token
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 30

#### Test Scenarios - Basic Allocation:
1. **New simple class** - No constructor args
2. **New with single arg** - Constructor with int
3. **New with multiple args** - Constructor with int, string
4. **New abstract class** - Should fail
5. **New interface** - Should fail
6. **New static class** - Should fail
7. **New generic class** - List<int>
8. **New nested class** - Outer.Inner

#### Test Scenarios - Struct Construction:
9. **New struct (value type)** - Creates boxed or unboxed
10. **New struct with args** - Parameterized ctor
11. **New Nullable<T>** - Special handling

#### Test Scenarios - Inheritance:
12. **New derived class** - Inheritance chain init
13. **Constructor chaining** - this() / base()
14. **Virtual method in constructor** - Careful behavior
15. **Field initializers** - Before constructor body

#### Test Scenarios - Exception Handling:
16. **Constructor throws** - Exception in ctor
17. **Out of memory** - Allocation failure
18. **Object partially constructed** - Exception mid-construction

#### Test Scenarios - Generic Constructors:
19. **New T where T : new()** - Generic constraint
20. **New with generic arguments** - Constructor args are T
21. **New array of generic** - new T[n] via Activator

#### Test Scenarios - Special Types:
22. **New delegate** - Delegate creation
23. **New array (special)** - Use newarr instead normally
24. **New string** - String constructor
25. **New multidimensional array** - Array.CreateInstance
26. **New with TypedReference** - Special cases

#### Test Scenarios - Constructor Variants:
27. **Default constructor** - Compiler-generated
28. **Private constructor** - Accessibility check
29. **Protected constructor** - Derived class access
30. **Static constructor trigger** - .cctor called

### 16.5 castclass (0x74)
- **Description**: Cast object to type (throws on failure)
- **Stack**: ..., obj → ..., obj
- **Operand**: type token
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 22

#### Test Scenarios - Class Hierarchy:
1. **Cast to same type** - Identity cast
2. **Cast to base class** - Always succeeds
3. **Cast to derived class (valid)** - Runtime type is derived
4. **Cast to derived class (invalid)** - InvalidCastException
5. **Cast to Object** - Always succeeds
6. **Deep hierarchy cast** - Multiple inheritance levels

#### Test Scenarios - Interface:
7. **Cast to implemented interface** - Succeeds
8. **Cast to non-implemented interface** - InvalidCastException
9. **Cast interface to class** - Succeeds if implements
10. **Cast to generic interface** - IEnumerable<T>

#### Test Scenarios - Null:
11. **Null cast to any class** - Returns null (no exception!)
12. **Null cast to interface** - Returns null
13. **Null cast to value type** - NullReferenceException (boxed)

#### Test Scenarios - Generics:
14. **Cast to generic type** - List<int>
15. **Cast with covariance** - IEnumerable<Derived> to IEnumerable<Base>
16. **Cast with contravariance** - IComparer<Base> to IComparer<Derived>
17. **Cast generic to non-generic** - List<int> to IList

#### Test Scenarios - Array:
18. **Cast array covariance** - string[] to object[]
19. **Cast array to IEnumerable** - Array implements IEnumerable
20. **Cast int[] to uint[]** - Not allowed (no covariance)

#### Test Scenarios - Special:
21. **Cast boxed value type** - Boxed int to int
22. **Cast delegate types** - Delegate compatibility

### 16.6 isinst (0x75)
- **Description**: Test if object is instance of type (returns null on failure)
- **Stack**: ..., obj → ..., obj/null
- **Operand**: type token
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 18

#### Test Scenarios - Success Cases (returns obj):
1. **Test same type** - Returns obj
2. **Test base type** - Returns obj
3. **Test implemented interface** - Returns obj
4. **Test null** - Returns null (success case!)

#### Test Scenarios - Failure Cases (returns null):
5. **Test derived type (not match)** - Returns null
6. **Test unrelated class** - Returns null
7. **Test non-implemented interface** - Returns null

#### Test Scenarios - Generics:
8. **Test generic type match** - List<int> is List<int>
9. **Test generic covariance** - IEnumerable<string> is IEnumerable<object>
10. **Test generic contravariance** - IComparer<object> is IComparer<string>

#### Test Scenarios - Arrays:
11. **Test array covariance** - string[] is object[]
12. **Test array to interface** - int[] is IEnumerable
13. **Array element type mismatch** - int[] is string[] → null

#### Test Scenarios - Boxing:
14. **Test boxed int** - boxed int is int → returns
15. **Test boxed int as long** - Returns null
16. **Test boxed enum** - Enum is underlying type?

#### Test Scenarios - Patterns:
17. **Pattern matching usage** - if (x is Type t)
18. **as operator equivalent** - x as Type

### 16.7 unbox (0x79)
- **Description**: Unbox value type (returns pointer to value)
- **Stack**: ..., obj → ..., valuePtr
- **Operand**: type token
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 14

#### Test Scenarios:
1. **Unbox int32** - Basic unbox to pointer
2. **Unbox int64** - 64-bit value
3. **Unbox float** - Float type
4. **Unbox double** - Double type
5. **Unbox struct** - User-defined struct
6. **Unbox enum** - Enum value
7. **Unbox Nullable<T>** - Special nullable handling
8. **Null object** - NullReferenceException
9. **Wrong type** - InvalidCastException
10. **Unbox int as uint** - Same representation, OK?
11. **Unbox to base type** - Enum as underlying int
12. **Read via returned pointer** - ldind.* after unbox
13. **Modify via returned pointer** - stind.* after unbox
14. **Pointer lifetime** - GC considerations

### 16.8 throw (0x7A)
- **Description**: Throw exception
- **Stack**: ..., obj → ...
- **Status**: [x]
- **Priority**: P0
- **Tests Needed**: 16
- **Tests Implemented**: 17 (eh.BasicThrow, eh.BasicThrow2, eh.TryCatch, eh.Nested, eh.Propagation, etc.)

#### Test Scenarios - Basic:
1. **Throw Exception** - Base exception
2. **Throw derived exception** - ArgumentException
3. **Throw null** - NullReferenceException!
4. **Throw SystemException** - System type
5. **Throw custom exception** - User-defined

#### Test Scenarios - Catch Patterns:
6. **Throw and catch same type** - Exact match
7. **Throw and catch base type** - Catch Exception catches all
8. **Throw through multiple frames** - Stack unwinding
9. **Throw in try block** - Immediate catch

#### Test Scenarios - Finally:
10. **Throw with finally** - Finally executes
11. **Throw in finally** - Replaces original exception
12. **Throw in catch** - Re-throws different

#### Test Scenarios - Nested:
13. **Nested try/catch** - Inner vs outer catch
14. **Throw in nested finally** - Complex unwinding

#### Test Scenarios - Special:
15. **Throw preserves stack trace** - Original throw location
16. **Throw affects control flow** - No return after throw

### 16.9 box (0x8C)
- **Description**: Box value type
- **Stack**: ..., value → ..., obj
- **Operand**: type token
- **Status**: [x]
- **Priority**: P0
- **Tests Needed**: 18
- **Tests Implemented**: 43 (box.Int, box.Long, box.Float, box.Double, box.Byte, box.Bool, box.Char, box.UInt.Max, box.Struct, etc.)
- **Notes**: Fixed uint.MaxValue boxing 2026-01-22

#### Test Scenarios - Primitives:
1. **Box int32** - Basic boxing
2. **Box int64** - 64-bit value
3. **Box byte** - Small type
4. **Box float** - Float boxing
5. **Box double** - Double boxing
6. **Box bool** - Boolean boxing
7. **Box char** - Char boxing

#### Test Scenarios - Structs:
8. **Box small struct** - User struct
9. **Box large struct** - Copy semantics
10. **Box struct with references** - GC tracking
11. **Box generic struct** - Generic<int>

#### Test Scenarios - Special Types:
12. **Box enum** - Enum boxing
13. **Box Nullable<int> with value** - Has value
14. **Box Nullable<int> null** - Returns null!
15. **Box IntPtr** - Native int

#### Test Scenarios - Behavior:
16. **Boxed value is copy** - Modifying boxed doesn't affect original
17. **Box same value twice** - Different objects
18. **Box and compare** - Reference equality

### 16.10 unbox.any (0xA5)
- **Description**: Unbox value type (returns value, not pointer)
- **Stack**: ..., obj → ..., value
- **Operand**: type token
- **Status**: [x]
- **Priority**: P0
- **Tests Needed**: 16
- **Tests Implemented**: 35 (unbox.Int32, unbox.Int64, unbox.Float, unbox.Double, unbox.UInt.Max, unbox.Struct, etc.)
- **Notes**: Fixed uint.MaxValue unboxing 2026-01-22 - uses correct 32-bit comparison for int/uint types

#### Test Scenarios - vs unbox:
1. **Unbox.any int32** - Returns value directly
2. **No ldind needed** - Single instruction
3. **Unbox.any struct** - Full struct copied to stack

#### Test Scenarios - Types:
4. **Unbox.any int64** - 64-bit
5. **Unbox.any float/double** - Float types
6. **Unbox.any enum** - Enum value
7. **Unbox.any user struct** - Large struct

#### Test Scenarios - Nullable:
8. **Unbox.any Nullable<int> with value** - Returns Nullable with HasValue=true
9. **Unbox.any null to Nullable<int>** - Returns Nullable with HasValue=false
10. **Unbox.any Nullable to underlying** - int? → int (if has value)

#### Test Scenarios - Error Cases:
11. **Null object (non-nullable)** - NullReferenceException
12. **Wrong type** - InvalidCastException
13. **Unbox.any reference type** - Acts like castclass

#### Test Scenarios - Generic:
14. **Unbox.any T where T : struct** - Generic value type
15. **Unbox.any in generic method** - Type parameter
16. **Unbox.any with Nullable<T>** - Generic nullable

---

## 17. Field Operations (0x7B-0x80)

### 17.1 ldfld (0x7B)
- **Description**: Load instance field
- **Stack**: ..., obj → ..., value
- **Operand**: field token
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 28

#### Test Scenarios - Primitive Fields:
1. **Load int8 field** - Sign-extends to int32
2. **Load uint8 field** - Zero-extends to int32
3. **Load int16 field** - Sign-extends to int32
4. **Load uint16 field** - Zero-extends to int32
5. **Load int32 field** - Direct load
6. **Load uint32 field** - Direct load
7. **Load int64 field** - 64-bit load
8. **Load float field** - Single precision
9. **Load double field** - Double precision
10. **Load bool field** - Boolean (1 byte)
11. **Load char field** - Unicode char

#### Test Scenarios - Reference Fields:
12. **Load object reference field** - Reference type
13. **Load null reference field** - Returns null
14. **Load string field** - String reference
15. **Load array field** - Array reference

#### Test Scenarios - Value Type Fields:
16. **Load struct field (small)** - Embedded struct
17. **Load struct field (large)** - Multi-register struct
18. **Load nested struct** - Struct within struct

#### Test Scenarios - Source Types:
19. **Load from class instance** - Reference receiver
20. **Load from struct (via pointer)** - Value type receiver
21. **Load from boxed value type** - Boxed struct
22. **Null receiver** - NullReferenceException

#### Test Scenarios - Special Cases:
23. **Load from generic class** - Field of T
24. **Load generic field** - Field is T
25. **Load readonly field** - Readonly instance field
26. **Load volatile field** - With volatile prefix
27. **Inherited field** - Field from base class
28. **Field at offset 0** - First field

### 17.2 ldflda (0x7C)
- **Description**: Load instance field address
- **Stack**: ..., obj → ..., addr
- **Operand**: field token
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 16

#### Test Scenarios:
1. **Address of int32 field** - Primitive field address
2. **Address of struct field** - Value type field address
3. **Address of reference field** - Ref to reference slot
4. **Use with ldind.*** - Load via address
5. **Use with stind.*** - Store via address
6. **Use with ldobj** - Load struct via address
7. **Pass to ref parameter** - Byref argument
8. **Null receiver** - NullReferenceException
9. **Address from struct receiver** - Via managed pointer
10. **Fixed/pinned handling** - Pointer stability
11. **Address of generic field** - T field
12. **Address of readonly field** - Readonly semantics
13. **Address within nested struct** - Chained addresses
14. **Address for Interlocked** - Atomic operations
15. **Address in generic type** - Generic<T>.field
16. **Address lifetime** - GC tracking

### 17.3 stfld (0x7D)
- **Description**: Store instance field
- **Stack**: ..., obj, value → ...
- **Operand**: field token
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 24

#### Test Scenarios - Primitive Fields:
1. **Store int8 to field** - Truncates from int32
2. **Store int16 to field** - Truncates from int32
3. **Store int32 to field** - Direct store
4. **Store int64 to field** - 64-bit store
5. **Store float to field** - Single precision
6. **Store double to field** - Double precision
7. **Store bool to field** - Boolean

#### Test Scenarios - Reference Fields:
8. **Store object reference** - With write barrier
9. **Store null** - Clear reference
10. **Store string** - String assignment
11. **Store derived to base field** - Covariance

#### Test Scenarios - Value Type Fields:
12. **Store small struct** - Embedded copy
13. **Store large struct** - Multi-byte copy
14. **Store to struct field** - Via managed pointer

#### Test Scenarios - Error Cases:
15. **Null receiver** - NullReferenceException
16. **Type mismatch** - Verifier catches
17. **Readonly field** - Outside constructor

#### Test Scenarios - Special:
18. **Store to generic field** - Field of type T
19. **Store with volatile prefix** - Memory ordering
20. **Store to inherited field** - Base class field
21. **Store triggers write barrier** - GC tracking
22. **Store in constructor** - Field initialization
23. **Store default value** - Zero/null
24. **Atomic store (int32)** - Naturally atomic

### 17.4 ldsfld (0x7E)
- **Description**: Load static field
- **Stack**: ... → ..., value
- **Operand**: field token
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 20

#### Test Scenarios - Types:
1. **Load static int32** - Primitive
2. **Load static int64** - 64-bit
3. **Load static float/double** - Float types
4. **Load static object reference** - Reference type
5. **Load static string** - String reference
6. **Load static struct** - Value type
7. **Load static array** - Array reference

#### Test Scenarios - Initialization:
8. **First access triggers .cctor** - Static constructor
9. **Thread-safe initialization** - Concurrent access
10. **Already initialized** - No .cctor call
11. **Circular dependency** - Type initialization order

#### Test Scenarios - Modifiers:
12. **Load readonly static** - Readonly field
13. **Load volatile static** - Volatile field
14. **Load const (embedded)** - Compile-time constant
15. **Load static from generic type** - Static per instantiation

#### Test Scenarios - Cross-Assembly:
16. **Load from external assembly** - Extern field
17. **Load from base class** - Inherited static
18. **Load thread-static** - [ThreadStatic]

#### Test Scenarios - Special:
19. **Load from interface** - Interface static (C# 8+)
20. **Performance test** - Hot path access

### 17.5 ldsflda (0x7F)
- **Description**: Load static field address
- **Stack**: ... → ..., addr
- **Operand**: field token
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 12

#### Test Scenarios:
1. **Address of static int32** - Primitive address
2. **Address of static struct** - Value type address
3. **Address of static reference** - Reference slot
4. **Use with Interlocked** - Atomic operations
5. **Use with ldobj/stobj** - Struct operations
6. **Use with ldind/stind** - Indirect access
7. **Pass as ref parameter** - Byref argument
8. **First access triggers .cctor** - Static init
9. **Address of volatile static** - With volatile prefix
10. **Address of generic static** - Per instantiation
11. **Address for fixed statement** - Pinning
12. **Thread-static address** - [ThreadStatic] field

### 17.6 stsfld (0x80)
- **Description**: Store static field
- **Stack**: ..., value → ...
- **Operand**: field token
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 18

#### Test Scenarios - Types:
1. **Store int32 to static** - Primitive
2. **Store int64 to static** - 64-bit
3. **Store float/double to static** - Float types
4. **Store object to static** - With write barrier
5. **Store struct to static** - Value type copy
6. **Store null to static ref** - Clear reference

#### Test Scenarios - Initialization:
7. **First access triggers .cctor** - Static constructor
8. **Store in .cctor** - Static initialization
9. **Thread-safe store** - Concurrent access

#### Test Scenarios - Modifiers:
10. **Store to readonly static (in .cctor)** - Allowed in constructor
11. **Store to volatile static** - Memory ordering
12. **Store to generic static** - Per instantiation

#### Test Scenarios - Write Barrier:
13. **Store reference triggers GC barrier** - GC tracking
14. **Store value type no barrier** - No GC impact

#### Test Scenarios - Special:
15. **Store from different thread** - Threading model
16. **Store to inherited static** - Base class field
17. **Store to interface static** - C# 8+ feature
18. **Atomic store guarantees** - Memory model

---

## 18. Object Model - Store Object (0x81)

### 18.1 stobj (0x81)
- **Description**: Store value type to address
- **Stack**: ..., addr, value → ...
- **Operand**: type token
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 16

#### Test Scenarios - Primitives:
1. **Store int32 via stobj** - Basic store
2. **Store int64 via stobj** - 64-bit store
3. **Store float/double via stobj** - Float types

#### Test Scenarios - Structs:
4. **Store small struct** - Fits in registers
5. **Store large struct** - Multi-byte copy
6. **Store struct with references** - GC tracking
7. **Store nested struct** - Composite type

#### Test Scenarios - Destination:
8. **Store to local address** - Via ldloca
9. **Store to field address** - Via ldflda
10. **Store to array element** - Via ldelema
11. **Store to static address** - Via ldsflda

#### Test Scenarios - Special:
12. **Null destination** - NullReferenceException
13. **Unaligned address** - With unaligned prefix
14. **Volatile store** - With volatile prefix
15. **Generic value type** - Store T where T : struct
16. **Overlapping copy** - Aliased source/dest

---

## 19. Array Operations (0x8D-0xA4)

### 19.1 newarr (0x8D)
- **Description**: Create new single-dimensional array
- **Stack**: ..., numElems → ..., array
- **Operand**: element type token
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 22

#### Test Scenarios - Primitive Element Types:
1. **new int[0]** - Empty array
2. **new int[1]** - Single element
3. **new int[100]** - Medium array
4. **new int[1000000]** - Large array
5. **new byte[n]** - Byte array
6. **new long[n]** - 64-bit elements
7. **new float[n]** - Float array
8. **new double[n]** - Double array
9. **new bool[n]** - Boolean array

#### Test Scenarios - Reference Element Types:
10. **new string[n]** - String array
11. **new object[n]** - Object array
12. **new SomeClass[n]** - Class array

#### Test Scenarios - Value Type Elements:
13. **new SomeStruct[n]** - Struct array (inline storage)
14. **new DateTime[n]** - Framework struct
15. **new Nullable<int>[n]** - Nullable array

#### Test Scenarios - Generic:
16. **new T[n] where T : class** - Generic reference
17. **new T[n] where T : struct** - Generic value type

#### Test Scenarios - Error Cases:
18. **Negative length** - OverflowException
19. **Length too large** - OutOfMemoryException
20. **int.MaxValue length** - Platform limits

#### Test Scenarios - Special:
21. **Array initialized to defaults** - All zeros/nulls
22. **Multi-dimensional via Array.CreateInstance** - Not newarr

### 19.2 ldlen (0x8E)
- **Description**: Load array length
- **Stack**: ..., array → ..., length
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 12

#### Test Scenarios:
1. **Length of empty array** - Returns 0
2. **Length of small array** - Returns correct count
3. **Length of large array** - Large length value
4. **Length as native uint** - Result type is native int
5. **Null array** - NullReferenceException
6. **Length of string array** - Reference type array
7. **Length of struct array** - Value type array
8. **Length used in loop** - Common pattern
9. **Length cached vs reloaded** - Optimization consideration
10. **SZArray vs multi-dimensional** - Different handling
11. **Covariant array length** - string[] as object[]
12. **Generic array T[]** - Generic element type

### 19.3 ldelema (0x8F)
- **Description**: Load address of array element
- **Stack**: ..., array, index → ..., addr
- **Operand**: element type token
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 16

#### Test Scenarios:
1. **Address of int element** - arr[i] address
2. **Address of struct element** - Value type element
3. **Address of first element** - Index 0
4. **Address of last element** - Length - 1
5. **Null array** - NullReferenceException
6. **Index out of range** - IndexOutOfRangeException
7. **Negative index** - IndexOutOfRangeException
8. **Use with ldobj** - Load struct via address
9. **Use with stobj** - Store struct via address
10. **Pass as ref parameter** - Byref argument
11. **Generic element T** - Generic array
12. **Readonly prefix** - For readonly span
13. **Address for Interlocked** - Atomic operations
14. **Covariant array** - string[] as object[] address
15. **ArrayTypeMismatchException** - Wrong element type
16. **Address stability** - GC considerations

### 19.4 ldelem.i1 (0x90)
- **Description**: Load int8 array element (sign-extends to int32)
- **Stack**: ..., array, index → ..., value
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 10

#### Test Scenarios:
1. **Load positive value** - 127 stays 127
2. **Load negative value** - -128 sign-extends to -128
3. **Load zero** - 0 stays 0
4. **Sign extension verified** - 0x80 → 0xFFFFFF80
5. **First element** - Index 0
6. **Last element** - Index length-1
7. **Null array** - NullReferenceException
8. **Index out of range** - IndexOutOfRangeException
9. **Negative index** - IndexOutOfRangeException
10. **sbyte[] array type** - Correct array type

### 19.5 ldelem.u1 (0x91)
- **Description**: Load uint8 array element (zero-extends to int32)
- **Stack**: ..., array, index → ..., value
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 10

#### Test Scenarios:
1. **Load value 0-127** - Small value
2. **Load value 128-255** - High bit set
3. **Zero extension verified** - 0xFF → 0x000000FF (not -1!)
4. **Difference from ldelem.i1** - No sign extension
5. **byte[] array type** - Correct array type
6. **First element** - Index 0
7. **Null array** - NullReferenceException
8. **Index out of range** - IndexOutOfRangeException
9. **Read from char[] as byte[]?** - Type safety
10. **Performance critical path** - Hot loop

### 19.6 ldelem.i2 (0x92)
- **Description**: Load int16 array element (sign-extends to int32)
- **Stack**: ..., array, index → ..., value
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 8

#### Test Scenarios:
1. **Load positive** - 32767 stays positive
2. **Load negative** - -32768 sign-extends
3. **Sign extension** - 0x8000 → 0xFFFF8000
4. **short[] array** - Correct type
5. **Null array** - NullReferenceException
6. **Index out of range** - IndexOutOfRangeException
7. **Bounds check** - First/last element
8. **Alignment** - 2-byte aligned access

### 19.7 ldelem.u2 (0x93)
- **Description**: Load uint16 array element (zero-extends to int32)
- **Stack**: ..., array, index → ..., value
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 8

#### Test Scenarios:
1. **Load 0-32767** - Small value
2. **Load 32768-65535** - High bit set
3. **Zero extension** - 0xFFFF → 0x0000FFFF
4. **ushort[] array** - Correct type
5. **char[] array** - Char is uint16
6. **Null array** - NullReferenceException
7. **Index out of range** - IndexOutOfRangeException
8. **Alignment** - 2-byte aligned

### 19.8 ldelem.i4 (0x94)
- **Description**: Load int32 array element
- **Stack**: ..., array, index → ..., value
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 10

#### Test Scenarios:
1. **Load positive** - MaxValue
2. **Load negative** - MinValue
3. **Load zero** - 0
4. **int[] array** - Correct type
5. **First element** - Index 0
6. **Last element** - Length - 1
7. **Middle element** - Random access
8. **Null array** - NullReferenceException
9. **Index out of range** - IndexOutOfRangeException
10. **Alignment** - 4-byte aligned

### 19.9 ldelem.u4 (0x95)
- **Description**: Load uint32 array element
- **Stack**: ..., array, index → ..., value
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 8

#### Test Scenarios:
1. **Load 0** - Zero
2. **Load MaxValue** - 0xFFFFFFFF
3. **uint[] array** - Correct type
4. **Result as int32** - Same bits, different interpretation
5. **Null array** - NullReferenceException
6. **Index out of range** - IndexOutOfRangeException
7. **Difference from ldelem.i4** - Interpretation only
8. **Alignment** - 4-byte aligned

### 19.10 ldelem.i8 (0x96)
- **Description**: Load int64 array element
- **Stack**: ..., array, index → ..., value
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 8

#### Test Scenarios:
1. **Load positive int64** - Large positive
2. **Load negative int64** - Large negative
3. **Load zero** - 0L
4. **long[] array** - Correct type
5. **ulong[] array** - Same representation
6. **Null array** - NullReferenceException
7. **Index out of range** - IndexOutOfRangeException
8. **Alignment** - 8-byte aligned

### 19.11 ldelem.i (0x97)
- **Description**: Load native int array element
- **Stack**: ..., array, index → ..., value
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 8

#### Test Scenarios:
1. **Load IntPtr value** - Native size
2. **IntPtr[] array** - Correct type
3. **UIntPtr[] array** - Same representation
4. **Platform size** - 32-bit vs 64-bit
5. **Null array** - NullReferenceException
6. **Index out of range** - IndexOutOfRangeException
7. **Pointer array** - Pointer types
8. **Alignment** - Platform aligned

### 19.12 ldelem.r4 (0x98)
- **Description**: Load float32 array element
- **Stack**: ..., array, index → ..., value
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 10

#### Test Scenarios:
1. **Load positive float** - 1.0f
2. **Load negative float** - -1.0f
3. **Load zero** - 0.0f and -0.0f
4. **Load infinity** - +Infinity, -Infinity
5. **Load NaN** - NaN preserved
6. **Load denormal** - Subnormal float
7. **float[] array** - Correct type
8. **Null array** - NullReferenceException
9. **Index out of range** - IndexOutOfRangeException
10. **Alignment** - 4-byte aligned

### 19.13 ldelem.r8 (0x99)
- **Description**: Load float64 array element
- **Stack**: ..., array, index → ..., value
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 10

#### Test Scenarios:
1. **Load positive double** - 1.0
2. **Load negative double** - -1.0
3. **Load zero** - 0.0 and -0.0
4. **Load infinity** - +/-Infinity
5. **Load NaN** - NaN preserved
6. **Load denormal** - Subnormal double
7. **double[] array** - Correct type
8. **Null array** - NullReferenceException
9. **Index out of range** - IndexOutOfRangeException
10. **Alignment** - 8-byte aligned

### 19.14 ldelem.ref (0x9A)
- **Description**: Load object reference array element
- **Stack**: ..., array, index → ..., value
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 14

#### Test Scenarios:
1. **Load object reference** - Non-null ref
2. **Load null element** - Returns null
3. **Load string element** - String array
4. **Load array element** - Array of arrays
5. **object[] array** - Base type array
6. **SomeClass[] array** - Specific type
7. **Interface[] array** - Interface array
8. **Covariant access** - string[] as object[]
9. **Null array** - NullReferenceException
10. **Index out of range** - IndexOutOfRangeException
11. **Generic T[] where T : class** - Generic reference
12. **Variance checking** - Runtime type check
13. **GC tracking** - Reference properly tracked
14. **Hot loop access** - Performance

### 19.15 stelem.i (0x9B)
- **Description**: Store native int to array element
- **Stack**: ..., array, index, value → ...
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 8

#### Test Scenarios:
1. **Store IntPtr value** - Native int
2. **IntPtr[] array** - Correct type
3. **Store to first element** - Index 0
4. **Store to last element** - Length - 1
5. **Null array** - NullReferenceException
6. **Index out of range** - IndexOutOfRangeException
7. **Platform size** - 32-bit vs 64-bit
8. **Alignment** - Platform aligned

### 19.16 stelem.i1 (0x9C)
- **Description**: Store int8 to array element (truncates from int32)
- **Stack**: ..., array, index, value → ...
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 10

#### Test Scenarios:
1. **Store value 0-127** - Positive byte
2. **Store value 128-255 (as int)** - Truncation
3. **Store -128 to -1** - Negative values
4. **Truncation from int32** - High bits discarded
5. **sbyte[] array** - Correct type
6. **byte[] array** - Same underlying
7. **Null array** - NullReferenceException
8. **Index out of range** - IndexOutOfRangeException
9. **Store to first/last** - Bounds
10. **Hot loop store** - Performance

### 19.17 stelem.i2 (0x9D)
- **Description**: Store int16 to array element (truncates from int32)
- **Stack**: ..., array, index, value → ...
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 8

#### Test Scenarios:
1. **Store positive short** - 0-32767
2. **Store negative short** - -32768 to -1
3. **Truncation from int32** - High 16 bits discarded
4. **short[] array** - Correct type
5. **Null array** - NullReferenceException
6. **Index out of range** - IndexOutOfRangeException
7. **Alignment** - 2-byte aligned
8. **ushort[]/char[] same** - Same representation

### 19.18 stelem.i4 (0x9E)
- **Description**: Store int32 to array element
- **Stack**: ..., array, index, value → ...
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 8

#### Test Scenarios:
1. **Store positive int** - MaxValue
2. **Store negative int** - MinValue
3. **Store zero** - 0
4. **int[] array** - Correct type
5. **uint[] array** - Same representation
6. **Null array** - NullReferenceException
7. **Index out of range** - IndexOutOfRangeException
8. **Alignment** - 4-byte aligned

### 19.19 stelem.i8 (0x9F)
- **Description**: Store int64 to array element
- **Stack**: ..., array, index, value → ...
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 8

#### Test Scenarios:
1. **Store positive long** - Large positive
2. **Store negative long** - Large negative
3. **Store zero** - 0L
4. **long[] array** - Correct type
5. **ulong[] array** - Same representation
6. **Null array** - NullReferenceException
7. **Index out of range** - IndexOutOfRangeException
8. **Alignment** - 8-byte aligned

### 19.20 stelem.r4 (0xA0)
- **Description**: Store float32 to array element
- **Stack**: ..., array, index, value → ...
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 10

#### Test Scenarios:
1. **Store positive float** - 1.0f
2. **Store negative float** - -1.0f
3. **Store zero** - 0.0f
4. **Store negative zero** - -0.0f
5. **Store infinity** - +/-Infinity
6. **Store NaN** - NaN preserved
7. **float[] array** - Correct type
8. **Null array** - NullReferenceException
9. **Index out of range** - IndexOutOfRangeException
10. **Alignment** - 4-byte aligned

### 19.21 stelem.r8 (0xA1)
- **Description**: Store float64 to array element
- **Stack**: ..., array, index, value → ...
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 10

#### Test Scenarios:
1. **Store positive double** - 1.0
2. **Store negative double** - -1.0
3. **Store zero** - 0.0
4. **Store negative zero** - -0.0
5. **Store infinity** - +/-Infinity
6. **Store NaN** - NaN preserved
7. **double[] array** - Correct type
8. **Null array** - NullReferenceException
9. **Index out of range** - IndexOutOfRangeException
10. **Alignment** - 8-byte aligned

### 19.22 stelem.ref (0xA2)
- **Description**: Store object reference to array element
- **Stack**: ..., array, index, value → ...
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 16

#### Test Scenarios:
1. **Store object reference** - Non-null
2. **Store null** - Clear element
3. **Store to object[]** - Base type
4. **Store derived to base array** - object[] with string
5. **Store exact type match** - string[] with string
6. **Covariant array store** - Type checking
7. **ArrayTypeMismatchException** - Wrong type stored
8. **Store interface ref** - Interface array
9. **Null array** - NullReferenceException
10. **Index out of range** - IndexOutOfRangeException
11. **Write barrier** - GC tracking
12. **Store array element** - Array of arrays
13. **Generic T[] where T : class** - Generic array
14. **Variance runtime check** - string[] as object[] store
15. **Performance critical** - Hot path
16. **Reference equality after store** - Same reference

### 19.23 ldelem (0xA3)
- **Description**: Load array element (generic)
- **Stack**: ..., array, index → ..., value
- **Operand**: type token
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 14

#### Test Scenarios:
1. **Load int element via ldelem** - Primitive
2. **Load struct element** - Value type
3. **Load reference element** - Reference type
4. **Load generic T element** - Generic array
5. **Type token matches array** - Correct type
6. **Small struct** - Fits registers
7. **Large struct** - Stack copy
8. **Nullable<T> element** - Nullable value type
9. **Null array** - NullReferenceException
10. **Index out of range** - IndexOutOfRangeException
11. **Performance vs typed ldelem** - Comparison
12. **Generic method context** - T[] load
13. **Covariant array** - Type checking
14. **Zero-initialized default** - New array elements

### 19.24 stelem (0xA4)
- **Description**: Store array element (generic)
- **Stack**: ..., array, index, value → ...
- **Operand**: type token
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 14

#### Test Scenarios:
1. **Store int via stelem** - Primitive
2. **Store struct element** - Value type
3. **Store reference element** - Reference type
4. **Store generic T element** - Generic array
5. **Type token matches array** - Correct type
6. **Small struct** - Inline copy
7. **Large struct** - Multi-byte copy
8. **Nullable<T> element** - Nullable value
9. **Null array** - NullReferenceException
10. **Index out of range** - IndexOutOfRangeException
11. **Write barrier for refs** - GC tracking
12. **Generic method context** - T[] store
13. **Covariant array check** - Type safety
14. **Default value store** - Zero/null

---

## 20. TypedReference Operations (0xC2, 0xC6, 0xFE 0x1D)

### 20.1 refanyval (0xC2)
- **Description**: Load address from typed reference
- **Stack**: ..., typedRef → ..., addr
- **Operand**: type token
- **Status**: [ ]
- **Priority**: P2
- **Tests Needed**: 10

#### Test Scenarios:
1. **Extract int address** - Primitive type
2. **Extract struct address** - Value type
3. **Type token matches** - Correct type specified
4. **Type token mismatch** - InvalidCastException
5. **Use with ldobj** - Load value via address
6. **Use with stobj** - Store value via address
7. **Round-trip with mkrefany** - Create then extract
8. **Generic type parameter** - T in generic method
9. **Nested typed reference** - Complex scenarios
10. **Null typed reference** - Invalid state

### 20.2 mkrefany (0xC6)
- **Description**: Create typed reference
- **Stack**: ..., addr → ..., typedRef
- **Operand**: type token
- **Status**: [ ]
- **Priority**: P2
- **Tests Needed**: 10

#### Test Scenarios:
1. **Create from int address** - Primitive
2. **Create from struct address** - Value type
3. **Create from local** - Via ldloca
4. **Create from argument** - Via ldarga
5. **Create from field** - Via ldflda
6. **Type token specified** - Correct type
7. **Pass to method** - __arglist usage
8. **Generic context** - T in generic
9. **Multiple typed refs** - Several in sequence
10. **Lifetime considerations** - Address validity

### 20.3 refanytype (0xFE 0x1D)
- **Description**: Load type from typed reference
- **Stack**: ..., typedRef → ..., typeHandle
- **Status**: [ ]
- **Priority**: P2
- **Tests Needed**: 8

#### Test Scenarios:
1. **Get type of int ref** - Primitive type handle
2. **Get type of struct ref** - Value type handle
3. **Use with Type.GetTypeFromHandle** - Convert to Type
4. **Compare type handles** - Equality check
5. **Generic type parameter** - T handle
6. **Constructed generic type** - List<int> handle
7. **Round-trip verification** - mkrefany → refanytype
8. **Different types** - Multiple typed refs

---

## 21. Floating Point Check (0xC3)

### 21.1 ckfinite (0xC3)
- **Description**: Check for finite float value
- **Stack**: ..., value → ..., value
- **Status**: [ ]
- **Priority**: P2
- **Tests Needed**: 14
- **Notes**: Throws ArithmeticException if NaN or infinity

#### Test Scenarios - Valid (No Throw):
1. **Check 0.0** - Zero is finite
2. **Check -0.0** - Negative zero is finite
3. **Check 1.0** - Normal positive
4. **Check -1.0** - Normal negative
5. **Check very small** - Denormal/subnormal
6. **Check MaxValue** - Largest finite
7. **Check MinValue** - Smallest finite

#### Test Scenarios - Throws ArithmeticException:
8. **Check +Infinity** - Throws
9. **Check -Infinity** - Throws
10. **Check NaN** - Throws
11. **Check quiet NaN** - Throws
12. **Check signaling NaN** - Throws

#### Test Scenarios - Context:
13. **In expression context** - Used in calculations
14. **Double vs float** - Both float64 and float32

---

## 22. Token Loading (0xD0)

### 22.1 ldtoken (0xD0)
- **Description**: Load metadata token as runtime handle
- **Stack**: ... → ..., handle
- **Operand**: metadata token
- **Status**: [ ]
- **Priority**: P1
- **Tests Needed**: 20
- **Notes**: Can be TypeDef, TypeRef, TypeSpec, MethodDef, MethodRef, FieldDef, FieldRef

#### Test Scenarios - Type Tokens:
1. **Load TypeDef token** - typeof(MyClass)
2. **Load TypeRef token** - typeof(string) external
3. **Load TypeSpec token** - typeof(List<int>) generic
4. **Load primitive type** - typeof(int)
5. **Load array type** - typeof(int[])
6. **Load nested type** - typeof(Outer.Inner)
7. **Use with Type.GetTypeFromHandle** - Convert to Type

#### Test Scenarios - Method Tokens:
8. **Load MethodDef token** - Current assembly method
9. **Load MethodRef token** - External method
10. **Load generic method** - Method<T>
11. **Use with MethodBase.GetMethodFromHandle** - Convert

#### Test Scenarios - Field Tokens:
12. **Load FieldDef token** - Instance field
13. **Load static field token** - Static field
14. **Load generic field** - Field in Generic<T>
15. **Use with FieldInfo.GetFieldFromHandle** - Convert

#### Test Scenarios - Generic Context:
16. **Load open generic type** - typeof(List<>)
17. **Load generic parameter T** - typeof(T)
18. **Generic method context** - ldtoken with generic args
19. **Nested generic type** - Dictionary<K, List<V>>
20. **Constructed vs open generic** - Different tokens

---

## 23. Exception Handling (0xDC-0xDE, 0xFE 0x11, 0xFE 0x1A)

### 23.1 endfinally (0xDC)
- **Description**: End finally/fault handler
- **Stack**: ... → ...
- **Status**: [x]
- **Priority**: P0
- **Tests Needed**: 14
- **Tests Implemented**: 17 (eh.TryFinally, eh.TryCatchFinally, eh.Nested, eh.FinallyOnReturn, etc.)
- **Notes**: Also known as endfault. Fully working in JIT.

#### Test Scenarios - Normal Flow:
1. **Finally after try (no exception)** - Normal completion
2. **Finally after caught exception** - Exception handled
3. **Finally after uncaught exception** - Exception propagates
4. **Nested finally blocks** - Multiple levels

#### Test Scenarios - Control Flow:
5. **Finally with return in try** - Return value preserved
6. **Finally with break/continue** - Loop control
7. **Finally modifies local** - Side effects
8. **Finally clears stack** - Stack empty after

#### Test Scenarios - Exception Interaction:
9. **Exception in finally** - Replaces original
10. **Return in finally** - Replaces try's return
11. **Rethrow in finally** - Complex unwinding

#### Test Scenarios - Fault Handler:
12. **Fault only on exception** - Not on normal flow
13. **Fault executes before propagation** - Order
14. **Nested try with fault** - Multiple handlers

### 23.2 leave (0xDD)
- **Description**: Exit protected region
- **Stack**: ... → ...
- **Operand**: int32 offset
- **Status**: [x]
- **Priority**: P0
- **Tests Needed**: 14
- **Tests Implemented**: 17 (all eh.* tests use leave)

#### Test Scenarios - Basic:
1. **Leave try block** - Normal exit
2. **Leave catch block** - After handling
3. **Leave to after try-catch** - Jump target
4. **Leave clears stack** - Stack empty after leave

#### Test Scenarios - Control Flow:
5. **Leave with finally** - Finally executes first
6. **Leave nested try** - Multiple levels
7. **Leave to outer scope** - Deep nesting
8. **Leave forward** - Forward jump
9. **Leave backward** - Backward jump (loop retry)

#### Test Scenarios - Finally Interaction:
10. **Leave triggers finally** - Finally runs
11. **Exception in triggered finally** - Complex flow
12. **Multiple finally blocks** - All execute

#### Test Scenarios - Edge Cases:
13. **Leave is unconditional** - No fall-through
14. **Leave target outside handler** - Valid target

### 23.3 leave.s (0xDE)
- **Description**: Exit protected region (short form)
- **Stack**: ... → ...
- **Operand**: int8 offset
- **Status**: [x]
- **Priority**: P0
- **Tests Needed**: 8
- **Tests Implemented**: Used throughout eh.* tests (compiler chooses short form when possible)

#### Test Scenarios:
1. **Short forward jump** - Within -128 to +127
2. **Short backward jump** - Negative offset
3. **Leave.s try block** - Normal exit
4. **Leave.s catch block** - After handling
5. **Leave.s with finally** - Finally executes
6. **Leave.s clears stack** - Empty after
7. **Boundary offset** - -128, +127
8. **Same as leave** - Identical behavior

### 23.4 endfilter (0xFE 0x11)
- **Description**: End exception filter
- **Stack**: ..., value → ...
- **Status**: [ ]
- **Priority**: P2
- **Tests Needed**: 10

#### Test Scenarios:
1. **Return 1 (handle)** - Accept exception
2. **Return 0 (don't handle)** - Reject exception
3. **Return other value** - Undefined behavior
4. **Filter examines exception** - Type check
5. **Filter examines state** - Context check
6. **Filter with multiple handlers** - First match wins
7. **Filter vs typed catch** - Ordering
8. **Exception during filter** - Complex flow
9. **Filter finally interaction** - Finally execution
10. **Stack behavior** - Consumes int32

### 23.5 rethrow (0xFE 0x1A)
- **Description**: Rethrow current exception
- **Stack**: ... → ...
- **Status**: [x]
- **Priority**: P1
- **Tests Needed**: 12
- **Tests Implemented**: 1 (eh.Rethrow - basic rethrow to outer catch)
- **Notes**: Implemented 2026-01-22. RhpRethrow handler dispatches to outer catch, preserving exception info.

#### Test Scenarios:
1. **Rethrow in catch** - Same exception ✅ (eh.Rethrow)
2. **Preserves stack trace** - Original throw location (stack trace not fully implemented)
3. **Rethrow after partial handling** - Log and rethrow ✅ (eh.Rethrow)
4. **Rethrow vs throw ex** - Stack trace difference (needs throw ex test)
5. **Rethrow in nested catch** - Inner handler (needs more tests)
6. **Rethrow different type** - Must be current exception (enforced by IL)
7. **Only valid in catch** - Not in finally (enforced by IL verifier)
8. **Outer catch catches rethrow** - Handler chain ✅ (eh.Rethrow)
9. **Rethrow in filter** - Complex scenarios (filter not implemented)
10. **Rethrow modifies exception** - No modification (enforced)
11. **Exception.StackTrace after rethrow** - Preserved (needs stack trace impl)
12. **Multiple rethrows** - Chained handlers (needs more tests)

---

## 24. Two-Byte Instructions - Comparison (0xFE 0x01-0x05)

### 24.1 ceq (0xFE 0x01)
- **Description**: Compare equal
- **Stack**: ..., value1, value2 → ..., result (0 or 1)
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 26

#### Test Scenarios - int32:
1. **0 == 0** - Equal zeros
2. **1 == 1** - Equal positives
3. **-1 == -1** - Equal negatives
4. **1 == 2** - Not equal
5. **-1 == 1** - Different signs
6. **MaxValue == MaxValue** - Boundary
7. **MinValue == MinValue** - Boundary

#### Test Scenarios - int64:
8. **0L == 0L** - 64-bit equal
9. **Large == Large** - Large values
10. **Int64.MaxValue** - Boundary

#### Test Scenarios - Float:
11. **0.0 == 0.0** - Float equal
12. **1.0 == 1.0** - Float equal
13. **0.0 == -0.0** - Positive/negative zero equal!
14. **NaN == NaN** - NaN != NaN (returns 0)
15. **NaN == 1.0** - NaN != anything
16. **Infinity == Infinity** - Same infinity equal

#### Test Scenarios - References:
17. **null == null** - Null references equal
18. **Same object reference** - Identity equal
19. **Different objects** - Not equal
20. **String interning** - Same literal equal

#### Test Scenarios - Native Int:
21. **IntPtr zero == zero** - Platform type
22. **Same pointer value** - Equal

#### Test Scenarios - Result:
23. **Result is int32** - 0 or 1
24. **Used in branch** - ceq followed by brfalse
25. **Used in assignment** - bool = (a == b)
26. **Chained comparisons** - (a == b) == (c == d)

### 24.2 cgt (0xFE 0x02)
- **Description**: Compare greater than (signed)
- **Stack**: ..., value1, value2 → ..., result (0 or 1)
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 22

#### Test Scenarios - int32:
1. **2 > 1** - Simple greater
2. **1 > 2** - Not greater
3. **1 > 1** - Equal (returns 0)
4. **0 > -1** - Zero vs negative
5. **-1 > -2** - Negative comparison
6. **MaxValue > MinValue** - Boundaries
7. **MinValue > MaxValue** - Not greater

#### Test Scenarios - int64:
8. **Large > small** - 64-bit comparison
9. **Boundary values** - Max/Min

#### Test Scenarios - Float (Signed):
10. **2.0 > 1.0** - Float greater
11. **1.0 > 2.0** - Not greater
12. **1.0 > 1.0** - Equal
13. **0.0 > -0.0** - Returns 0 (equal)
14. **NaN > 1.0** - Returns 0 (unordered)
15. **1.0 > NaN** - Returns 0 (unordered)
16. **Infinity > 1.0** - Returns 1
17. **1.0 > -Infinity** - Returns 1

#### Test Scenarios - Native Int:
18. **Signed comparison** - Treats as signed
19. **Negative pointer value** - High bit set

#### Test Scenarios - Usage:
20. **Result is 0 or 1** - Int32 result
21. **Used in branch** - cgt + brtrue
22. **Comparison chain** - a > b > c

### 24.3 cgt.un (0xFE 0x03)
- **Description**: Compare greater than (unsigned/unordered)
- **Stack**: ..., value1, value2 → ..., result (0 or 1)
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 22

#### Test Scenarios - int32 (Unsigned):
1. **2u > 1u** - Simple unsigned greater
2. **0xFFFFFFFF > 0** - High bit as positive
3. **0x80000000 > 0x7FFFFFFF** - Unsigned interpretation
4. **0 > 0xFFFFFFFF** - Returns 0

#### Test Scenarios - int64 (Unsigned):
5. **Large unsigned values** - 64-bit unsigned
6. **High bit set values** - Unsigned comparison

#### Test Scenarios - Float (Unordered):
7. **2.0 > 1.0** - Normal comparison
8. **NaN > 1.0** - Returns 1 (unordered = true for cgt.un!)
9. **1.0 > NaN** - Returns 1 (unordered)
10. **NaN > NaN** - Returns 1 (unordered)
11. **Infinity handling** - Same as cgt

#### Test Scenarios - Difference from cgt:
12. **Signed vs unsigned int** - -1 vs 0xFFFFFFFF
13. **NaN behavior opposite** - cgt returns 0, cgt.un returns 1
14. **Object references** - Comparison semantics

#### Test Scenarios - Native Int:
15. **Unsigned pointer comparison** - Address comparison
16. **Platform differences** - 32 vs 64 bit

#### Test Scenarios - Usage:
17. **Null check optimization** - obj != null uses cgt.un
18. **Unsigned loop bounds** - for uint i
19. **Result is 0 or 1** - Int32 result
20. **Array bounds** - Index comparison
21. **Pointer comparison** - Memory addresses
22. **Combined with conversion** - After conv.u*

### 24.4 clt (0xFE 0x04)
- **Description**: Compare less than (signed)
- **Stack**: ..., value1, value2 → ..., result (0 or 1)
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 20

#### Test Scenarios - int32:
1. **1 < 2** - Simple less
2. **2 < 1** - Not less
3. **1 < 1** - Equal (returns 0)
4. **-1 < 0** - Negative vs zero
5. **-2 < -1** - Negative comparison
6. **MinValue < MaxValue** - Boundaries

#### Test Scenarios - int64:
7. **Small < large** - 64-bit
8. **Boundary values** - Max/Min

#### Test Scenarios - Float:
9. **1.0 < 2.0** - Float less
10. **-0.0 < 0.0** - Returns 0 (equal)
11. **NaN < 1.0** - Returns 0 (unordered)
12. **-Infinity < 1.0** - Returns 1
13. **1.0 < Infinity** - Returns 1

#### Test Scenarios - Native Int:
14. **Signed comparison** - Treats as signed
15. **Platform behavior** - 32 vs 64 bit

#### Test Scenarios - Common Patterns:
16. **Loop condition** - i < length
17. **Bounds checking** - Index validation
18. **Sorting comparisons** - Less than for ordering
19. **Result is 0 or 1** - Int32
20. **Chained** - a < b && b < c

### 24.5 clt.un (0xFE 0x05)
- **Description**: Compare less than (unsigned/unordered)
- **Stack**: ..., value1, value2 → ..., result (0 or 1)
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 20

#### Test Scenarios - int32 (Unsigned):
1. **1u < 2u** - Simple unsigned less
2. **0 < 0xFFFFFFFF** - Zero less than max unsigned
3. **0x7FFFFFFF < 0x80000000** - Unsigned interpretation
4. **0xFFFFFFFF < 0** - Returns 0

#### Test Scenarios - int64 (Unsigned):
5. **Unsigned 64-bit** - Large values
6. **High bit set** - Unsigned comparison

#### Test Scenarios - Float (Unordered):
7. **1.0 < 2.0** - Normal comparison
8. **NaN < 1.0** - Returns 1 (unordered)
9. **1.0 < NaN** - Returns 1 (unordered)
10. **NaN < NaN** - Returns 1 (unordered)

#### Test Scenarios - Difference from clt:
11. **Signed vs unsigned** - Different results
12. **NaN behavior opposite** - clt returns 0, clt.un returns 1

#### Test Scenarios - Usage:
13. **Unsigned loop bounds** - for (uint i = 0; i < n; i++)
14. **Array index** - Unsigned index check
15. **Pointer comparison** - Address comparison
16. **Result is 0 or 1** - Int32
17. **Platform size** - 32 vs 64 bit
18. **Combined with conv.u** - After unsigned conversion
19. **Length comparison** - Array.Length (uint)
20. **Bitwise result use** - Boolean operations

---

## 25. Two-Byte Instructions - Function Pointers (0xFE 0x06-0x07)

### 25.1 ldftn (0xFE 0x06)
- **Description**: Load function pointer
- **Stack**: ... → ..., ftn
- **Operand**: method token
- **Status**: [ ]
- **Priority**: P1
- **Tests Needed**: 18

#### Test Scenarios - Static Methods:
1. **Load static method pointer** - Basic function pointer
2. **Load static void method** - No return value
3. **Load static with parameters** - Multiple params
4. **Load static returning value** - Return type
5. **Load static generic method** - Method<T>

#### Test Scenarios - Instance Methods (Non-Virtual):
6. **Load non-virtual instance method** - Specific implementation
7. **Load private method** - Same class
8. **Load sealed method** - Cannot be overridden

#### Test Scenarios - Usage:
9. **Create delegate from ldftn** - Delegate construction
10. **Call via calli** - Indirect call
11. **Store in field** - Function pointer storage
12. **Pass as parameter** - Function pointer argument

#### Test Scenarios - Generic Context:
13. **Load from generic type** - Generic<T>.Method
14. **Load generic method** - Method<T>
15. **Instantiated generic** - Method<int>

#### Test Scenarios - Special:
16. **Load constructor pointer** - .ctor address
17. **External assembly method** - Cross-assembly
18. **Platform invoke** - P/Invoke method

### 25.2 ldvirtftn (0xFE 0x07)
- **Description**: Load virtual function pointer
- **Stack**: ..., obj → ..., ftn
- **Operand**: method token
- **Status**: [ ]
- **Priority**: P1
- **Tests Needed**: 16

#### Test Scenarios - Virtual Methods:
1. **Load virtual method (not overridden)** - Base implementation
2. **Load virtual method (overridden)** - Derived implementation
3. **Load through base reference** - Polymorphic
4. **Load interface method** - Interface dispatch
5. **Load from sealed override** - Final implementation

#### Test Scenarios - Object Types:
6. **Load from class instance** - Reference type
7. **Load from boxed struct** - Boxed value type
8. **Null object reference** - NullReferenceException

#### Test Scenarios - Usage:
9. **Create delegate** - Delegate from virtual
10. **Call via calli** - Indirect virtual call
11. **Capture for later** - Stored pointer

#### Test Scenarios - Generic:
12. **Virtual on generic type** - Generic<T> virtual
13. **Generic virtual method** - Method<T> virtual
14. **Constrained call context** - T.Method where T : interface

#### Test Scenarios - Special:
15. **Interface explicit implementation** - Explicit impl dispatch
16. **Deep inheritance** - Multiple override levels

---

## 26. Two-Byte Instructions - Argument/Local Long Form (0xFE 0x09-0x0E)

### 26.1 ldarg (0xFE 0x09)
- **Description**: Load argument (long form)
- **Stack**: ... → ..., value
- **Operand**: uint16 argument index
- **Status**: [ ]
- **Priority**: P1
- **Tests Needed**: 12

#### Test Scenarios:
1. **Load argument 0** - First argument (or this)
2. **Load argument 256** - Beyond short form range
3. **Load argument 65535** - Maximum index
4. **Load many arguments** - Method with 300+ args
5. **Various types** - int, long, float, object, struct
6. **Instance method args** - After 'this'
7. **Static method args** - No 'this'
8. **Equivalent to ldarg.s** - Same behavior for index < 256
9. **Byref argument** - ref parameter
10. **Value type argument** - Struct copy
11. **Generic parameter T** - Type parameter argument
12. **Vararg after fixed** - __arglist context

### 26.2 ldarga (0xFE 0x0A)
- **Description**: Load argument address (long form)
- **Stack**: ... → ..., addr
- **Operand**: uint16 argument index
- **Status**: [ ]
- **Priority**: P1
- **Tests Needed**: 10

#### Test Scenarios:
1. **Address of argument 0** - First arg address
2. **Address of argument 256+** - Long index
3. **Use with ldind/stind** - Indirect access
4. **Use with ldobj/stobj** - Struct access
5. **Pass as ref parameter** - Byref to byref
6. **Address of 'this' (instance)** - Ref to this
7. **Address of value type arg** - Struct argument
8. **Address for Interlocked** - Atomic ops on arg
9. **Large argument index** - Near max index
10. **Generic argument** - T parameter address

### 26.3 starg (0xFE 0x0B)
- **Description**: Store to argument (long form)
- **Stack**: ..., value → ...
- **Operand**: uint16 argument index
- **Status**: [ ]
- **Priority**: P1
- **Tests Needed**: 10

#### Test Scenarios:
1. **Store to argument 0** - First argument
2. **Store to argument 256+** - Long index
3. **Store int32** - Primitive type
4. **Store int64** - 64-bit type
5. **Store object reference** - Reference type
6. **Store struct** - Value type copy
7. **Store to 'this' (value type)** - Modify struct this
8. **Multiple stores** - Reassign argument
9. **Store then load** - Round trip
10. **Store truncates** - int8/int16 truncation

### 26.4 ldloc (0xFE 0x0C)
- **Description**: Load local variable (long form)
- **Stack**: ... → ..., value
- **Operand**: uint16 local index
- **Status**: [ ]
- **Priority**: P1
- **Tests Needed**: 12

#### Test Scenarios:
1. **Load local 0** - First local
2. **Load local 256** - Beyond short form
3. **Load local 65535** - Maximum index
4. **Many locals** - Method with 300+ locals
5. **Various types** - int, long, float, object, struct
6. **Load after store** - Initialized local
7. **Load uninitialized** - Default value
8. **Equivalent to ldloc.s** - Same for index < 256
9. **Load pinned local** - Fixed/pinned variable
10. **Load struct local** - Value type copy
11. **Generic local** - T type local
12. **Nullable local** - Nullable<int>

### 26.5 ldloca (0xFE 0x0D)
- **Description**: Load local variable address (long form)
- **Stack**: ... → ..., addr
- **Operand**: uint16 local index
- **Status**: [ ]
- **Priority**: P1
- **Tests Needed**: 10

#### Test Scenarios:
1. **Address of local 0** - First local
2. **Address of local 256+** - Long index
3. **Use with ldind/stind** - Indirect access
4. **Use with ldobj/stobj** - Struct operations
5. **Pass as ref parameter** - Byref argument
6. **Address of struct local** - Struct address
7. **Address for initobj** - Zero initialization
8. **Address for Interlocked** - Atomic operations
9. **Pinned local address** - For GC pinning
10. **Generic local address** - T type address

### 26.6 stloc (0xFE 0x0E)
- **Description**: Store to local variable (long form)
- **Stack**: ..., value → ...
- **Operand**: uint16 local index
- **Status**: [ ]
- **Priority**: P1
- **Tests Needed**: 12

#### Test Scenarios:
1. **Store to local 0** - First local
2. **Store to local 256+** - Long index
3. **Store int32** - Primitive
4. **Store int64** - 64-bit
5. **Store float/double** - Float types
6. **Store object reference** - Reference type
7. **Store null** - Clear reference
8. **Store struct** - Value type copy
9. **Multiple stores** - Reassign local
10. **Store truncates** - int8/int16 locals
11. **Equivalent to stloc.s** - Same for index < 256
12. **Store generic T** - Generic type

---

## 27. Two-Byte Instructions - Memory Allocation (0xFE 0x0F)

### 27.1 localloc (0xFE 0x0F)
- **Description**: Allocate space on local heap (stack)
- **Stack**: ..., size → ..., addr
- **Status**: [ ]
- **Priority**: P1
- **Tests Needed**: 16
- **Notes**: Used for stackalloc in C#

#### Test Scenarios - Basic:
1. **Allocate 0 bytes** - Empty allocation
2. **Allocate 4 bytes** - Small allocation
3. **Allocate 100 bytes** - Medium allocation
4. **Allocate 1000 bytes** - Large allocation
5. **Allocate variable size** - Runtime-determined

#### Test Scenarios - Initialization:
6. **Memory is zeroed** - .locals init behavior
7. **Memory not zeroed** - Without .locals init
8. **Write and read back** - Use allocated space

#### Test Scenarios - Alignment:
9. **Result is aligned** - Platform alignment
10. **Size rounding** - Alignment padding

#### Test Scenarios - Error Cases:
11. **Stack overflow** - Very large allocation
12. **Negative size** - Implementation-defined
13. **Only at stack empty** - Must be on empty evaluation stack

#### Test Scenarios - Usage:
14. **stackalloc int[n]** - C# usage
15. **Span<T> from stackalloc** - Modern C# pattern
16. **Lifetime within method** - Stack frame scope

---

## 28. Two-Byte Instructions - Prefix Instructions (0xFE 0x12-0x14, 0xFE 0x16, 0xFE 0x19, 0xFE 0x1E)

### 28.1 unaligned. (0xFE 0x12)
- **Description**: Prefix indicating unaligned pointer
- **Stack**: (prefix)
- **Operand**: uint8 alignment (1, 2, or 4)
- **Status**: [ ]
- **Priority**: P2
- **Tests Needed**: 12

#### Test Scenarios:
1. **Alignment 1 with ldind.i4** - Byte-aligned int32 load
2. **Alignment 2 with ldind.i4** - Word-aligned int32 load
3. **Alignment 1 with stind.i4** - Byte-aligned int32 store
4. **Alignment 1 with ldind.i8** - Byte-aligned int64
5. **Alignment with ldobj** - Unaligned struct load
6. **Alignment with stobj** - Unaligned struct store
7. **Alignment with cpblk** - Unaligned block copy
8. **Alignment with initblk** - Unaligned block init
9. **Prefix applies to next instruction** - Single instruction scope
10. **Interop scenarios** - Packed structs
11. **Platform differences** - x86 vs ARM
12. **Performance impact** - Unaligned access cost

### 28.2 volatile. (0xFE 0x13)
- **Description**: Prefix indicating volatile access
- **Stack**: (prefix)
- **Status**: [ ]
- **Priority**: P1
- **Tests Needed**: 14

#### Test Scenarios - Load:
1. **Volatile load int32** - ldind.i4 with volatile
2. **Volatile load int64** - ldind.i8 with volatile
3. **Volatile load reference** - ldind.ref with volatile
4. **Volatile ldfld** - Instance field
5. **Volatile ldsfld** - Static field

#### Test Scenarios - Store:
6. **Volatile store int32** - stind.i4 with volatile
7. **Volatile store int64** - stind.i8 with volatile
8. **Volatile store reference** - stind.ref with volatile
9. **Volatile stfld** - Instance field
10. **Volatile stsfld** - Static field

#### Test Scenarios - Memory Ordering:
11. **Prevents reordering** - Compiler barrier
12. **Memory fence semantics** - Acquire/release
13. **Cross-thread visibility** - Thread synchronization
14. **With Interlocked** - Combined patterns

### 28.3 tail. (0xFE 0x14)
- **Description**: Prefix indicating tail call
- **Stack**: (prefix)
- **Status**: [ ]
- **Priority**: P2
- **Tests Needed**: 14

#### Test Scenarios - Basic:
1. **Tail call static method** - Simple tail call
2. **Tail call with same signature** - Matching parameters
3. **Tail call with different signature** - Different params
4. **Tail call virtual method** - Polymorphic tail
5. **Tail call interface method** - Interface dispatch

#### Test Scenarios - Requirements:
6. **Must precede call/calli/callvirt** - Prefix scope
7. **Return immediately after** - ret follows call
8. **No exception handlers** - Outside try/catch
9. **Caller returns same type** - Compatible return

#### Test Scenarios - Optimization:
10. **Stack frame reuse** - No stack growth
11. **Recursive tail call** - Self-recursion
12. **Mutual recursion** - A calls B, B calls A

#### Test Scenarios - Edge Cases:
13. **Tail call may be ignored** - JIT decision
14. **Debug mode behavior** - May not optimize

### 28.4 constrained. (0xFE 0x16)
- **Description**: Prefix for constrained virtual call
- **Stack**: (prefix)
- **Operand**: type token
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 20
- **Notes**: Used for calling interface methods on generic type parameters

#### Test Scenarios - Value Types:
1. **T.ToString() where T : struct** - Override on struct
2. **T.GetHashCode() where T : struct** - Override on struct
3. **T.Equals() where T : struct** - Override on struct
4. **Struct without override** - Box and call
5. **Struct with override** - Direct call

#### Test Scenarios - Reference Types:
6. **T.Method() where T : class** - Reference type
7. **Null reference handling** - NullReferenceException
8. **Virtual dispatch** - Normal virtual call

#### Test Scenarios - Interface Constraints:
9. **T.Method() where T : IInterface** - Interface method
10. **Struct implementing interface** - No boxing
11. **Class implementing interface** - Normal dispatch
12. **Default interface method** - C# 8+ feature

#### Test Scenarios - Multiple Constraints:
13. **where T : struct, IComparable** - Combined
14. **where T : class, IDisposable** - Combined
15. **where T : new()** - Constructor constraint

#### Test Scenarios - Generic Context:
16. **Nested generic method** - Complex constraints
17. **Generic type with generic method** - Double generic
18. **Constrained call in loop** - Performance

#### Test Scenarios - Specific Behaviors:
19. **Pointer for value type** - Address passed
20. **Reference for ref type** - Dereferenced

### 28.5 no. (0xFE 0x19)
- **Description**: Prefix indicating no type check/range check/null check
- **Stack**: (prefix)
- **Operand**: uint8 flags (1=typecheck, 2=rangecheck, 4=nullcheck)
- **Status**: [ ]
- **Priority**: P3
- **Tests Needed**: 10

#### Test Scenarios:
1. **no.typecheck with castclass** - Skip type check
2. **no.rangecheck with ldelem** - Skip bounds check
3. **no.nullcheck with ldfld** - Skip null check
4. **Combined flags** - Multiple skips
5. **Verifiable code** - Usually not verifiable
6. **Unsafe context** - Where allowed
7. **Performance critical** - Hot path usage
8. **JIT behavior** - May still check
9. **Debugging considerations** - Harder to debug
10. **When checks would fail** - Undefined behavior

### 28.6 readonly. (0xFE 0x1E)
- **Description**: Prefix for readonly address operation
- **Stack**: (prefix)
- **Status**: [ ]
- **Priority**: P2
- **Tests Needed**: 10
- **Notes**: Used with ldelema for readonly span access

#### Test Scenarios:
1. **readonly ldelema** - Array element address
2. **With value type array** - Struct element
3. **Prevents array covariance check** - Optimization
4. **ReadOnlySpan access** - Modern C# pattern
5. **In parameters** - Readonly reference
6. **Ref readonly return** - Readonly ref return
7. **With struct methods** - Call method on readonly ref
8. **Defense against mutation** - Cannot store via address
9. **Generic element type** - T[] access
10. **Performance benefit** - Skip covariance check

---

## 29. Two-Byte Instructions - Object Initialization (0xFE 0x15)

### 29.1 initobj (0xFE 0x15)
- **Description**: Initialize value type to zero/null
- **Stack**: ..., addr → ...
- **Operand**: type token
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 16

#### Test Scenarios - Primitive Types:
1. **Init int32 to 0** - Primitive zero
2. **Init int64 to 0** - 64-bit zero
3. **Init float to 0.0** - Float zero
4. **Init double to 0.0** - Double zero

#### Test Scenarios - Struct Types:
5. **Init small struct** - All fields zero
6. **Init large struct** - Many fields
7. **Init struct with references** - References set to null
8. **Init nested struct** - Recursive initialization
9. **Init struct with padding** - Padding bytes zeroed

#### Test Scenarios - Generic:
10. **Init T where T : struct** - Generic value type
11. **Init Nullable<T>** - Nullable becomes null

#### Test Scenarios - Address Sources:
12. **Init local via ldloca** - Local variable
13. **Init field via ldflda** - Instance field
14. **Init static via ldsflda** - Static field
15. **Init array element via ldelema** - Array element

#### Test Scenarios - Special:
16. **Init vs default(T)** - Equivalent behavior

---

## 30. Two-Byte Instructions - Block Operations (0xFE 0x17-0x18)

### 30.1 cpblk (0xFE 0x17)
- **Description**: Copy block of memory
- **Stack**: ..., dest, src, size → ...
- **Status**: [ ]
- **Priority**: P1
- **Tests Needed**: 18

#### Test Scenarios - Basic:
1. **Copy 0 bytes** - Empty copy
2. **Copy 1 byte** - Minimal copy
3. **Copy 4 bytes** - Int32 size
4. **Copy 8 bytes** - Int64 size
5. **Copy 100 bytes** - Medium block
6. **Copy 10000 bytes** - Large block

#### Test Scenarios - Alignment:
7. **Aligned source and dest** - Optimal path
8. **Unaligned with prefix** - unaligned. cpblk
9. **Mixed alignment** - One aligned, one not

#### Test Scenarios - Overlap:
10. **Non-overlapping regions** - Normal copy
11. **Overlapping (forward)** - Dest > src
12. **Overlapping (backward)** - Src > dest
13. **Same address** - Source = dest

#### Test Scenarios - Error Cases:
14. **Null destination** - May fault
15. **Null source** - May fault
16. **Invalid memory** - Access violation

#### Test Scenarios - Volatile:
17. **With volatile prefix** - Memory ordering
18. **Performance test** - Memcpy equivalent

### 30.2 initblk (0xFE 0x18)
- **Description**: Initialize block of memory
- **Stack**: ..., addr, value, size → ...
- **Status**: [ ]
- **Priority**: P1
- **Tests Needed**: 14

#### Test Scenarios - Basic:
1. **Init 0 bytes** - Empty init
2. **Init 1 byte to 0** - Single byte zero
3. **Init 4 bytes to 0** - Int32 of zeros
4. **Init 100 bytes to 0** - Medium block zero
5. **Init to 0xFF** - All ones pattern
6. **Init to 0xAB** - Pattern fill

#### Test Scenarios - Value Interpretation:
7. **Value is byte** - Low 8 bits used
8. **Value > 255 truncates** - High bits ignored

#### Test Scenarios - Alignment:
9. **Aligned address** - Optimal path
10. **Unaligned with prefix** - unaligned. initblk

#### Test Scenarios - Error Cases:
11. **Null address** - May fault
12. **Invalid memory** - Access violation

#### Test Scenarios - Volatile:
13. **With volatile prefix** - Memory ordering
14. **Performance test** - Memset equivalent

---

## 31. Two-Byte Instructions - Sizeof (0xFE 0x1C)

### 31.1 sizeof (0xFE 0x1C)
- **Description**: Get size of value type
- **Stack**: ... → ..., size
- **Operand**: type token
- **Status**: [ ]
- **Priority**: P0
- **Tests Needed**: 20

#### Test Scenarios - Primitives:
1. **sizeof(byte)** - 1
2. **sizeof(short)** - 2
3. **sizeof(int)** - 4
4. **sizeof(long)** - 8
5. **sizeof(float)** - 4
6. **sizeof(double)** - 8
7. **sizeof(bool)** - 1
8. **sizeof(char)** - 2

#### Test Scenarios - Platform-Dependent:
9. **sizeof(IntPtr)** - 4 or 8
10. **sizeof(UIntPtr)** - 4 or 8
11. **sizeof(nint)** - Native int size
12. **sizeof(nuint)** - Native uint size

#### Test Scenarios - Structs:
13. **sizeof(empty struct)** - Minimum 1
14. **sizeof(small struct)** - Packed size
15. **sizeof(struct with padding)** - Includes padding
16. **sizeof(nested struct)** - Total size
17. **sizeof(struct with reference)** - Reference field size

#### Test Scenarios - Generic:
18. **sizeof(T) where T : struct** - Generic value type
19. **sizeof(Nullable<int>)** - Nullable size

#### Test Scenarios - Special:
20. **Compile-time constant** - When type is known

---

## 32. Two-Byte Instructions - Arglist (0xFE 0x00)

### 32.1 arglist (0xFE 0x00)
- **Description**: Get argument list handle for vararg method
- **Stack**: ... → ..., argListHandle
- **Status**: [ ]
- **Priority**: P2
- **Tests Needed**: 12
- **Notes**: Used with vararg calling convention

#### Test Scenarios - Basic:
1. **Get arglist in vararg method** - Basic usage
2. **No extra args** - Empty vararg
3. **One extra arg** - Single vararg
4. **Multiple extra args** - Many varargs

#### Test Scenarios - With ArgIterator:
5. **Create ArgIterator from arglist** - Iteration setup
6. **Iterate over args** - GetNextArg
7. **Get remaining count** - GetRemainingCount

#### Test Scenarios - Types:
8. **Varargs of int** - Primitive type
9. **Varargs of mixed types** - Different types
10. **Varargs with objects** - Reference types

#### Test Scenarios - Error Cases:
11. **In non-vararg method** - Invalid usage
12. **After fixed args** - Correct position

---

## Summary

### Total Opcodes: 219

### Test Definition Status: COMPLETE

All 219 IL opcodes now have comprehensive test scenarios defined, covering:
- Type combinations (int32, int64, native int, float32, float64, references, value types)
- Boundary values (min, max, zero, near-boundary)
- Special values (NaN, Infinity, null, negative zero)
- Error conditions (null references, out of range, overflow)
- Platform-dependent behavior (32-bit vs 64-bit)
- Signed vs unsigned interpretations
- Stack behavior and type coercion
- Generic type handling
- Interaction with other instructions (prefixes, control flow)

### Estimated Total Tests: ~2,800+

### By Priority:
- **P0 (Critical)**: ~150 opcodes - Core functionality
- **P1 (High)**: ~40 opcodes - Important features
- **P2 (Medium)**: ~20 opcodes - Less common usage
- **P3 (Low)**: ~9 opcodes - Rarely used

### By Category:
| Category | Count | Tests |
|----------|-------|-------|
| Base Instructions | 14 | ~170 |
| Argument/Local Short | 6 | ~70 |
| Constants | 16 | ~185 |
| Stack Manipulation | 2 | ~18 |
| Control Flow | 27 | ~310 |
| Indirect Load | 11 | ~125 |
| Indirect Store | 8 | ~110 |
| Arithmetic | 13 | ~250 |
| Bitwise | 6 | ~80 |
| Unary | 2 | ~36 |
| Conversion | 33 | ~280 |
| Method Calls | 4 | ~110 |
| Object Model | 10 | ~195 |
| Fields | 6 | ~120 |
| Arrays | 24 | ~260 |
| TypedReference | 3 | ~28 |
| Float Check | 1 | ~14 |
| Token Loading | 1 | ~20 |
| Exception Handling | 5 | ~58 |
| Comparison | 5 | ~110 |
| Function Pointers | 2 | ~34 |
| Argument/Local Long | 6 | ~66 |
| Memory Allocation | 1 | ~16 |
| Prefixes | 6 | ~80 |
| Object Init | 1 | ~16 |
| Block Operations | 2 | ~32 |
| Sizeof | 1 | ~20 |
| Arglist | 1 | ~12 |

---

## Next Steps

1. ~~Determine test scenarios for each opcode (Phase 2)~~ **COMPLETE**
2. ~~Calculate total tests needed~~ **~2,800+ tests defined**
3. Prioritize implementation order - Start with P0 opcodes
4. Implement tests systematically - Begin with Section 1

---

## Implementation Status (Updated 2026-01-22)

### Current Test Count: 2,983 tests passing

All implemented tests are passing. The JIT correctly handles:

### Working Categories:
| Category | Status | Notes |
|----------|--------|-------|
| Base Instructions (ldarg.0-3, ldloc.0-3, stloc.0-3, ldarg.s, starg.s, ldloc.s, stloc.s) | ✅ Working | All short-form argument/local operations |
| Constants (ldc.i4, ldc.i8, ldc.r4, ldc.r8, ldnull, ldstr) | ✅ Working | All constant loading |
| Stack Manipulation (dup, pop) | ✅ Working | |
| Control Flow (br, brfalse, brtrue, beq, bne, bgt, bge, blt, ble, switch, ret) | ✅ Working | All branches and comparisons |
| Indirect Load (ldind.i1-i8, ldind.u1-u4, ldind.r4, ldind.r8, ldind.i, ldind.ref) | ✅ Working | Pointer dereferencing |
| Indirect Store (stind.i1-i8, stind.r4, stind.r8, stind.i, stind.ref) | ✅ Working | Pointer stores |
| Arithmetic (add, sub, mul, div, rem, neg, add.ovf, sub.ovf, mul.ovf) | ✅ Working | Signed/unsigned, checked/unchecked |
| Bitwise (and, or, xor, not, shl, shr, shr.un) | ✅ Working | |
| Conversion (conv.i1-i8, conv.u1-u8, conv.r4, conv.r8, conv.i, conv.u, conv.ovf.*) | ✅ Working | All type conversions |
| Method Calls (call, callvirt, calli) | ✅ Working | Static, virtual, indirect calls |
| Object Model (newobj, newarr, ldlen, ldelem, stelem, box, unbox, castclass, isinst) | ✅ Working | Object creation, arrays, boxing |
| Fields (ldfld, stfld, ldsfld, stsfld, ldflda, ldsflda) | ✅ Working | Instance and static fields |
| Arrays (ldelem.*, stelem.*, ldelema) | ✅ Working | All element types |
| Comparison (ceq, cgt, cgt.un, clt, clt.un) | ✅ Working | All comparison operations |
| Function Pointers (ldftn, ldvirtftn) | ✅ Working | Via delegates (except nested class instance methods) |
| Memory Allocation (localloc) | ✅ Working | stackalloc |
| Prefixes (volatile) | ✅ Working | |
| Object Initialization (initobj) | ✅ Working | default(T) |
| Sizeof | ✅ Working | All primitive types and structs |
| Exception Handling (throw, rethrow, leave, endfinally) | ✅ Working | try/catch/finally, nested handlers, rethrow |
| TypedReference (mkrefany, refanyval, refanytype) | ✅ Working | __makeref, __refvalue, __reftype |
| Token Loading (ldtoken) | ✅ Working | Type.GetTypeFromHandle, typeof() |
| Block Operations (cpblk, initblk) | ✅ Working | Buffer.MemoryCopy patterns |
| Tail Calls (tail.) | ✅ Working | Recursive tail call optimization |

### Known Limitations (JIT-incompatible patterns):

#### 1. Generic newobj (`new T()`)
- **Issue**: Creating instances of generic type parameters requires runtime type information
- **Pattern**: `void CreateInstance<T>() where T : new() { var x = new T(); }`
- **Workaround**: Use `Activator.CreateInstance<T>()` when AOT is available, or pass factory delegates
- **Reason**: JIT cannot resolve the MethodTable for `T` without generic instantiation context being passed through the call chain

#### 2. Lambdas / Closures
- **Issue**: Lambda expressions generate compiler-synthesized closure classes
- **Pattern**: `list.Where(x => x > 5)` or `Func<int> f = () => localVar;`
- **Workaround**: Use explicit delegate instances with static methods
- **Reason**: Closure class types are generated at compile time and not easily resolvable in JIT

#### 3. Nested Class Instance Method Delegates (ldvirtftn)
- **Issue**: Taking a delegate to an instance method of a nested class
- **Pattern**: `Func<int> f = nestedInstance.MethodName;`
- **Workaround**: Use static methods or non-nested classes
- **Reason**: Method token resolution for nested class instance methods through ldvirtftn not implemented

#### 4. Certain BCL Methods Not in AOT
- **Issue**: Some BCL methods require AOT-compiled implementations
- **Examples**: `Type.op_Equality`, `IntPtr.Size` (property), `Buffer.MemoryCopy` (some overloads)
- **Workaround**: Use korlib equivalents or write custom implementations
- **Reason**: These rely on runtime intrinsics or AOT-compiled code paths

#### 5. ldarg.s.LargeStruct / ldloc.s.LargeStruct
- **Location**: `src/JITTest/Tests/ArgumentLocalShortTests.cs`
- **Issue**: Large struct (>8 bytes) at argument/local index 4+ requires multi-slot handling
- **Status**: Commented out pending JIT fix for stack-passed large structs

#### 6. Argument/Local Long Form (indices > 255)
- **Issue**: Hard to test in C# as compiler uses short form when possible
- **Status**: Placeholder test, rarely needed in practice

### Test Breakdown by File:

| Test File | Test Count | Category |
|-----------|------------|----------|
| **IL Opcode Tests** | | |
| ArithmeticTests.cs | ~300 | add, sub, mul, div, rem, neg |
| BitwiseTests.cs | ~150 | and, or, xor, not, shl, shr |
| ComparisonTests.cs | ~200 | ceq, cgt, clt (signed/unsigned) |
| ControlFlowTests.cs | ~200 | br, beq, bne, bgt, bge, blt, ble, switch |
| ConversionTests.cs | ~200 | conv.* instructions |
| FieldOperationTests.cs | ~100 | ldfld, stfld, ldsfld, stsfld |
| IndirectMemoryTests.cs | ~130 | ldind.*, stind.* |
| ObjectModelTests.cs | ~150 | newobj, box, unbox, castclass, isinst |
| BaseInstructionTests.cs | ~150 | ldarg.0-3, ldloc.0-3, stloc.0-3 |
| StackManipulationTests.cs | ~60 | dup, pop |
| SwitchTests.cs | ~100 | switch with various case counts |
| ArgumentLocalShortTests.cs | ~150 | ldarg.s, starg.s, ldloc.s, stloc.s |
| ArrayOperationTests.cs | ~80 | ldelem.*, stelem.*, ldelema, newarr |
| StubTests.cs | ~100 | Exception handling, typedref, ldtoken, tail calls |
| **Feature Tests** | | |
| ForeachTests.cs | ~15 | foreach loops on arrays |
| BoxingTests.cs | ~20 | Boxing/unboxing value types |
| GenericTests.cs | ~25 | Generic types and methods |
| InterfaceTests.cs | ~20 | Interface dispatch |
| NullableTests.cs | ~15 | Nullable<T> operations |
| OverflowTests.cs | ~20 | Checked arithmetic |
| StringTests.cs | ~25 | String operations |
| DelegateTests.cs | ~15 | Delegate invocation |
| MDArrayTests.cs | ~20 | Multi-dimensional arrays |
| DisposableTests.cs | ~10 | using/IDisposable pattern |
| StaticCtorTests.cs | ~10 | Static constructors |
| RecursionTests.cs | ~10 | Recursive calls |
| CalliTests.cs | ~10 | Function pointer calls |
| ParamsTests.cs | ~10 | params keyword |
| IteratorTests.cs | ~10 | Custom IEnumerable |
| **Korlib Tests** | | |
| ListTests.cs | ~25 | List<T> operations |
| DictionaryTests.cs | ~25 | Dictionary<K,V> operations |
| HashSetTests.cs | ~15 | HashSet<T> operations |
| StringBuilderTests.cs | ~15 | StringBuilder operations |
| InterlockedTests.cs | ~15 | Interlocked operations |
| StringFormatTests.cs | ~20 | String formatting |
| UtilityTests.cs | ~30 | BitConverter, TimeSpan, DateTime, Queue, Stack, etc. |
| **Regression Tests** | | |
| JitRegressionTests.cs | ~15 | Tests for fixed JIT bugs |

### Recent Fixes:

- **2026-01-22**: Migrated all tests from FullTest to JITTest with organized structure:
  - Tests/IL/ - IL opcode tests
  - Tests/Features/ - JIT feature tests (boxing, generics, interfaces, delegates, etc.)
  - Tests/Korlib/ - Runtime library API tests (List, Dictionary, HashSet, StringBuilder, etc.)
  - Tests/Regression/ - JIT regression tests
- **2026-01-22**: Fixed object equality comparison for boxed value types
- **2026-01-22**: Implemented `rethrow` instruction - full support for `throw;` in catch handlers
  - Fixed `LeaveTargetOffset` calculation for handlers ending with throw/rethrow
  - Fixed stack layout in rethrow dispatch to correctly position return address
- **2026-01-22**: Fixed `uint.MaxValue` boxing/unboxing - 32-bit comparison was incorrectly using 64-bit width
- **2026-01-22**: Fixed string interning (`ldstr`) - StringPool cache index formula now correctly incorporates assembly ID
- **2026-01-21**: Implemented exception handling for JIT code - try/catch/finally with funclets
- **2026-01-20**: Fixed interface method overload resolution - `FindMethodByName` now matches on parameter count
- **2026-01-20**: Fixed `isinst` for non-implemented interfaces
- **2026-01-20**: Fixed explicit interface method dispatch
- **2026-01-20**: Fixed generic type instantiation for interface dispatch
