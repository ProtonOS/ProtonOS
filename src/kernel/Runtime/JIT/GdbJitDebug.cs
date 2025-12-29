// GDB JIT Debug Interface
// Implements the GDB JIT interface specification for registering JIT-compiled
// code symbols, allowing GDB to debug JIT code with function names.
//
// Reference: https://sourceware.org/gdb/current/onlinedocs/gdb.html/JIT-Interface.html

using System;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using ProtonOS.Memory;
using ProtonOS.Platform;
using ProtonOS.Threading;

namespace ProtonOS.Runtime.JIT
{
    /// <summary>
    /// GDB JIT action flags
    /// </summary>
    internal enum JitAction : uint
    {
        NoAction = 0,
        RegisterFn = 1,
        UnregisterFn = 2
    }

    /// <summary>
    /// Entry in the linked list of JIT code entries.
    /// Each entry describes one in-memory ELF object.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct JitCodeEntry
    {
        public JitCodeEntry* NextEntry;
        public JitCodeEntry* PrevEntry;
        public byte* SymfileAddr;    // Pointer to in-memory ELF object
        public ulong SymfileSize;    // Size of ELF object
    }

    /// <summary>
    /// Global descriptor that GDB monitors for JIT symbol updates.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct JitDescriptor
    {
        public uint Version;
        public JitAction ActionFlag;
        public JitCodeEntry* RelevantEntry;
        public JitCodeEntry* FirstEntry;
    }

    /// <summary>
    /// GDB JIT Debug registration support.
    /// Creates minimal ELF objects for JIT methods and registers them with GDB.
    /// </summary>
    public static unsafe class GdbJitDebug
    {
        // Import native GDB JIT helper functions
        [RuntimeImport("*", "__jit_debug_register_code")]
        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void NativeJitDebugRegisterCode();

        [RuntimeImport("*", "__jit_set_action")]
        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void NativeSetAction(uint action);

        [RuntimeImport("*", "__jit_set_relevant_entry")]
        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void NativeSetRelevantEntry(JitCodeEntry* entry);

        [RuntimeImport("*", "__jit_set_first_entry")]
        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void NativeSetFirstEntry(JitCodeEntry* entry);

        [RuntimeImport("*", "__jit_get_first_entry")]
        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern JitCodeEntry* NativeGetFirstEntry();

        private static bool _initialized;
        private static SpinLock _lock;

        // ELF constants
        private const byte ELFCLASS64 = 2;
        private const byte ELFDATA2LSB = 1;
        private const byte EV_CURRENT = 1;
        private const ushort ET_EXEC = 2;
        private const ushort EM_X86_64 = 62;

        // Section header types
        private const uint SHT_NULL = 0;
        private const uint SHT_PROGBITS = 1;
        private const uint SHT_SYMTAB = 2;
        private const uint SHT_STRTAB = 3;

        // Section flags
        private const ulong SHF_ALLOC = 0x2;
        private const ulong SHF_EXECINSTR = 0x4;

        // Symbol binding/type
        private const byte STB_GLOBAL = 1;
        private const byte STT_FUNC = 2;

        /// <summary>
        /// Initialize the GDB JIT interface.
        /// Must be called before any JIT code is generated.
        /// </summary>
        public static void Initialize()
        {
            if (_initialized)
                return;

            // Native __jit_debug_descriptor is already initialized in assembly
            // Just set up our managed state
            _lock = new SpinLock();
            _initialized = true;

            DebugConsole.WriteLine("[GdbJit] Debug interface initialized");
        }

        /// <summary>
        /// Register a JIT-compiled method with GDB.
        /// Creates a minimal ELF object with function symbol.
        /// </summary>
        /// <param name="methodName">Name of the method (will be prefixed with "jit_")</param>
        /// <param name="codeAddress">Address of compiled code</param>
        /// <param name="codeSize">Size of compiled code in bytes</param>
        public static void RegisterMethod(string methodName, ulong codeAddress, uint codeSize)
        {
            if (!_initialized)
                Initialize();

            // Build method name with jit_ prefix
            string fullName = "jit_" + methodName;

            // Create ELF object for this method
            byte* elfData;
            ulong elfSize;
            CreateMinimalElf(fullName, codeAddress, codeSize, out elfData, out elfSize);

            if (elfData == null)
            {
                DebugConsole.WriteLine("[GdbJit] Failed to create ELF for method");
                return;
            }

            // Allocate JIT code entry
            JitCodeEntry* entry = (JitCodeEntry*)HeapAllocator.Alloc((ulong)sizeof(JitCodeEntry));
            if (entry == null)
            {
                // Can't free elfData easily, but this is a rare error path
                DebugConsole.WriteLine("[GdbJit] Failed to allocate JIT code entry");
                return;
            }

            entry->SymfileAddr = elfData;
            entry->SymfileSize = elfSize;
            entry->NextEntry = null;
            entry->PrevEntry = null;

            // Add to linked list and notify GDB
            _lock.Acquire();

            // Insert at head of list - use native helpers to access descriptor
            JitCodeEntry* firstEntry = NativeGetFirstEntry();
            entry->NextEntry = firstEntry;
            if (firstEntry != null)
            {
                firstEntry->PrevEntry = entry;
            }
            NativeSetFirstEntry(entry);

            // Notify GDB via native interface
            NativeSetRelevantEntry(entry);
            NativeSetAction((uint)JitAction.RegisterFn);

            // Call the registration function (GDB sets breakpoint here)
            NativeJitDebugRegisterCode();

            NativeSetAction((uint)JitAction.NoAction);

            _lock.Release();
        }

        /// <summary>
        /// Create a minimal ELF64 object with a single function symbol.
        /// </summary>
        private static void CreateMinimalElf(string methodName, ulong codeAddress, uint codeSize,
            out byte* elfData, out ulong elfSize)
        {
            // Calculate sizes
            int nameLen = methodName.Length + 1; // +1 for null terminator

            // Section string table: \0.text\0.symtab\0.strtab\0.shstrtab\0
            // Offsets:              0  1     7       15      23
            // Size = 33 bytes
            const int shstrtabSize = 33;

            // String table for symbols: \0<methodname>\0
            int strtabSize = 1 + nameLen;

            // Layout:
            // - ELF header: 64 bytes
            // - Section headers: 5 * 64 = 320 bytes (null, .text, .symtab, .strtab, .shstrtab)
            // - Symbol table: 2 * 24 = 48 bytes (null symbol + function symbol)
            // - String table: 1 + nameLen bytes
            // - Section string table: shstrtabSize bytes

            const int ehdrSize = 64;
            const int shdrSize = 64;
            const int numSections = 5;
            const int symEntSize = 24;
            const int numSymbols = 2;

            int shdrsOffset = ehdrSize;
            int symtabOffset = shdrsOffset + (numSections * shdrSize);
            int strtabOffset = symtabOffset + (numSymbols * symEntSize);
            int shstrtabOffset = strtabOffset + strtabSize;
            int totalSize = shstrtabOffset + shstrtabSize;

            // Allocate ELF buffer
            elfData = (byte*)HeapAllocator.Alloc((ulong)totalSize);
            if (elfData == null)
            {
                elfSize = 0;
                return;
            }
            elfSize = (ulong)totalSize;

            // Zero the buffer
            for (int i = 0; i < totalSize; i++)
                elfData[i] = 0;

            // ELF header
            byte* ehdr = elfData;
            ehdr[0] = 0x7f; ehdr[1] = (byte)'E'; ehdr[2] = (byte)'L'; ehdr[3] = (byte)'F';
            ehdr[4] = ELFCLASS64;      // 64-bit
            ehdr[5] = ELFDATA2LSB;     // Little endian
            ehdr[6] = EV_CURRENT;      // Version
            // ehdr[7-15] = 0 (padding)
            *(ushort*)(ehdr + 16) = ET_EXEC;       // e_type
            *(ushort*)(ehdr + 18) = EM_X86_64;     // e_machine
            *(uint*)(ehdr + 20) = 1;               // e_version
            *(ulong*)(ehdr + 24) = codeAddress;    // e_entry
            *(ulong*)(ehdr + 32) = 0;              // e_phoff (no program headers)
            *(ulong*)(ehdr + 40) = (ulong)shdrsOffset; // e_shoff
            *(uint*)(ehdr + 48) = 0;               // e_flags
            *(ushort*)(ehdr + 52) = (ushort)ehdrSize;  // e_ehsize
            *(ushort*)(ehdr + 54) = 0;             // e_phentsize
            *(ushort*)(ehdr + 56) = 0;             // e_phnum
            *(ushort*)(ehdr + 58) = (ushort)shdrSize;  // e_shentsize
            *(ushort*)(ehdr + 60) = numSections;   // e_shnum
            *(ushort*)(ehdr + 62) = 4;             // e_shstrndx (index of .shstrtab)

            // Section headers
            byte* shdrs = elfData + shdrsOffset;

            // Section 0: NULL
            // (already zeroed)

            // Section 1: .text (describes the code region)
            byte* shdrText = shdrs + shdrSize;
            *(uint*)(shdrText + 0) = 1;            // sh_name = ".text" offset
            *(uint*)(shdrText + 4) = SHT_PROGBITS; // sh_type
            *(ulong*)(shdrText + 8) = SHF_ALLOC | SHF_EXECINSTR; // sh_flags
            *(ulong*)(shdrText + 16) = codeAddress; // sh_addr
            *(ulong*)(shdrText + 24) = 0;          // sh_offset (external code)
            *(ulong*)(shdrText + 32) = codeSize;   // sh_size
            *(uint*)(shdrText + 40) = 0;           // sh_link
            *(uint*)(shdrText + 44) = 0;           // sh_info
            *(ulong*)(shdrText + 48) = 16;         // sh_addralign
            *(ulong*)(shdrText + 56) = 0;          // sh_entsize

            // Section 2: .symtab
            byte* shdrSymtab = shdrs + 2 * shdrSize;
            *(uint*)(shdrSymtab + 0) = 7;          // sh_name = ".symtab" offset
            *(uint*)(shdrSymtab + 4) = SHT_SYMTAB; // sh_type
            *(ulong*)(shdrSymtab + 8) = 0;         // sh_flags
            *(ulong*)(shdrSymtab + 16) = 0;        // sh_addr
            *(ulong*)(shdrSymtab + 24) = (ulong)symtabOffset; // sh_offset
            *(ulong*)(shdrSymtab + 32) = (ulong)(numSymbols * symEntSize); // sh_size
            *(uint*)(shdrSymtab + 40) = 3;         // sh_link = .strtab index
            *(uint*)(shdrSymtab + 44) = 1;         // sh_info = first global symbol
            *(ulong*)(shdrSymtab + 48) = 8;        // sh_addralign
            *(ulong*)(shdrSymtab + 56) = (ulong)symEntSize; // sh_entsize

            // Section 3: .strtab
            byte* shdrStrtab = shdrs + 3 * shdrSize;
            *(uint*)(shdrStrtab + 0) = 15;         // sh_name = ".strtab" offset
            *(uint*)(shdrStrtab + 4) = SHT_STRTAB; // sh_type
            *(ulong*)(shdrStrtab + 8) = 0;         // sh_flags
            *(ulong*)(shdrStrtab + 16) = 0;        // sh_addr
            *(ulong*)(shdrStrtab + 24) = (ulong)strtabOffset; // sh_offset
            *(ulong*)(shdrStrtab + 32) = (ulong)strtabSize; // sh_size

            // Section 4: .shstrtab
            byte* shdrShstrtab = shdrs + 4 * shdrSize;
            *(uint*)(shdrShstrtab + 0) = 23;       // sh_name = ".shstrtab" offset
            *(uint*)(shdrShstrtab + 4) = SHT_STRTAB; // sh_type
            *(ulong*)(shdrShstrtab + 8) = 0;       // sh_flags
            *(ulong*)(shdrShstrtab + 16) = 0;      // sh_addr
            *(ulong*)(shdrShstrtab + 24) = (ulong)shstrtabOffset; // sh_offset
            *(ulong*)(shdrShstrtab + 32) = (ulong)shstrtabSize; // sh_size

            // Symbol table
            byte* symtab = elfData + symtabOffset;
            // Symbol 0: NULL (already zeroed)
            // Symbol 1: function
            byte* sym = symtab + symEntSize;
            *(uint*)(sym + 0) = 1;                 // st_name (offset in strtab)
            sym[4] = (byte)((STB_GLOBAL << 4) | STT_FUNC); // st_info
            sym[5] = 0;                            // st_other
            *(ushort*)(sym + 6) = 1;               // st_shndx = .text section
            *(ulong*)(sym + 8) = codeAddress;      // st_value
            *(ulong*)(sym + 16) = codeSize;        // st_size

            // String table
            byte* strtab = elfData + strtabOffset;
            strtab[0] = 0;  // null string
            for (int i = 0; i < methodName.Length; i++)
            {
                strtab[1 + i] = (byte)methodName[i];
            }
            strtab[1 + methodName.Length] = 0;

            // Section string table: write directly
            // Format: \0.text\0.symtab\0.strtab\0.shstrtab\0
            byte* shstrtabData = elfData + shstrtabOffset;
            int idx = 0;
            shstrtabData[idx++] = 0;  // null at offset 0
            // .text at offset 1
            shstrtabData[idx++] = (byte)'.';
            shstrtabData[idx++] = (byte)'t';
            shstrtabData[idx++] = (byte)'e';
            shstrtabData[idx++] = (byte)'x';
            shstrtabData[idx++] = (byte)'t';
            shstrtabData[idx++] = 0;
            // .symtab at offset 7
            shstrtabData[idx++] = (byte)'.';
            shstrtabData[idx++] = (byte)'s';
            shstrtabData[idx++] = (byte)'y';
            shstrtabData[idx++] = (byte)'m';
            shstrtabData[idx++] = (byte)'t';
            shstrtabData[idx++] = (byte)'a';
            shstrtabData[idx++] = (byte)'b';
            shstrtabData[idx++] = 0;
            // .strtab at offset 15
            shstrtabData[idx++] = (byte)'.';
            shstrtabData[idx++] = (byte)'s';
            shstrtabData[idx++] = (byte)'t';
            shstrtabData[idx++] = (byte)'r';
            shstrtabData[idx++] = (byte)'t';
            shstrtabData[idx++] = (byte)'a';
            shstrtabData[idx++] = (byte)'b';
            shstrtabData[idx++] = 0;
            // .shstrtab at offset 23
            shstrtabData[idx++] = (byte)'.';
            shstrtabData[idx++] = (byte)'s';
            shstrtabData[idx++] = (byte)'h';
            shstrtabData[idx++] = (byte)'s';
            shstrtabData[idx++] = (byte)'t';
            shstrtabData[idx++] = (byte)'r';
            shstrtabData[idx++] = (byte)'t';
            shstrtabData[idx++] = (byte)'a';
            shstrtabData[idx++] = (byte)'b';
            shstrtabData[idx++] = 0;
        }

        /// <summary>
        /// Build a mangled method name from type and method info.
        /// Format: TypeName_MethodName
        /// </summary>
        public static string BuildMethodName(string typeName, string methodName)
        {
            // Replace problematic characters
            var sb = new System.Text.StringBuilder(typeName.Length + methodName.Length + 1);

            // Type name - replace . and + with _
            for (int i = 0; i < typeName.Length; i++)
            {
                char c = typeName[i];
                if (c == '.' || c == '+' || c == '<' || c == '>' || c == ',' || c == ' ' || c == '`')
                    sb.Append('_');
                else
                    sb.Append(c);
            }

            sb.Append('_');
            sb.Append('_');  // Double underscore separator like AOT

            // Method name
            for (int i = 0; i < methodName.Length; i++)
            {
                char c = methodName[i];
                if (c == '<' || c == '>' || c == ',' || c == ' ' || c == '.')
                    sb.Append('_');
                else
                    sb.Append(c);
            }

            return sb.ToString();
        }
    }
}
