# ProtonOS Makefile

# Default target architecture
ARCH ?= x64

# Directories
BUILD_DIR := build/$(ARCH)
KERNEL_DIR := src/kernel
KORLIB_DIR := src/korlib
TEST_DIR := src/FullTest

# Output files
ifeq ($(ARCH),x64)
    EFI_NAME := BOOTX64.EFI
    NASM_FORMAT := win64
else ifeq ($(ARCH),arm64)
    EFI_NAME := BOOTAA64.EFI
    $(error ARM64 not yet implemented)
endif

# Tools
NASM := nasm
LD := lld-link
# Use local bflat build with custom ILCompiler (for testing fixes)
# To use system bflat, change to: BFLAT := bflat
BFLAT := dotnet $(CURDIR)/tools/bflat/src/bflat/bin/Release/net10.0/bflat.dll

# Tool flags
NASM_FLAGS := -f $(NASM_FORMAT)
LD_FLAGS := -subsystem:efi_application -entry:EfiEntry

BFLAT_FLAGS := \
	--os:uefi \
	--arch:$(ARCH) \
	--stdlib:none \
	--no-stacktrace-data \
	--no-globalization \
	--no-reflection \
	--no-exception-messages \
	--emit-eh-info

# Architecture-specific defines
ifeq ($(ARCH),x64)
    BFLAT_FLAGS += -d ARCH_X64 -d BOOT_UEFI
else ifeq ($(ARCH),arm64)
    BFLAT_FLAGS += -d ARCH_ARM64 -d BOOT_UEFI
else ifeq ($(ARCH),apple)
    BFLAT_FLAGS += -d ARCH_ARM64 -d BOOT_M1N1
endif

# Recursive wildcard function
rwildcard=$(foreach d,$(wildcard $(1:=/*)),$(call rwildcard,$d,$2) $(filter $(subst *,%,$2),$d))

# Source files
NATIVE_SRC := $(wildcard $(KERNEL_DIR)/$(ARCH)/*.asm)
# Filter out obj/ and bin/ directories from korlib (dotnet build artifacts)
KORLIB_SRC := $(filter-out %/obj/% %/bin/%,$(call rwildcard,$(KORLIB_DIR),*.cs))
KERNEL_SRC := $(call rwildcard,$(KERNEL_DIR),*.cs)

# Object files
NATIVE_OBJ := $(BUILD_DIR)/native.obj
KERNEL_OBJ := $(BUILD_DIR)/kernel.obj

# Test assembly output
TEST_DLL := $(BUILD_DIR)/FullTest.dll

# korlib IL assembly (for JIT generic instantiation)
KORLIB_DLL := $(BUILD_DIR)/korlib.dll

# TestSupport assembly (cross-assembly test helpers)
TESTSUPPORT_DIR := src/TestSupport
TESTSUPPORT_DLL := $(BUILD_DIR)/TestSupport.dll

# DDK assembly (JIT library)
DDK_DIR := src/ddk
DDK_DLL := $(BUILD_DIR)/ProtonOS.DDK.dll

# Driver directories
DRIVERS_DIR := src/drivers
VIRTIO_DIR := $(DRIVERS_DIR)/shared/virtio
VIRTIO_BLK_DIR := $(DRIVERS_DIR)/shared/storage/virtio-blk
FAT_DIR := $(DRIVERS_DIR)/shared/storage/fat
AHCI_DIR := $(DRIVERS_DIR)/shared/storage/ahci
EXT2_DIR := $(DRIVERS_DIR)/shared/storage/ext2

# Driver DLLs
VIRTIO_DLL := $(BUILD_DIR)/ProtonOS.Drivers.Virtio.dll
VIRTIO_BLK_DLL := $(BUILD_DIR)/ProtonOS.Drivers.VirtioBlk.dll
FAT_DLL := $(BUILD_DIR)/ProtonOS.Drivers.Fat.dll
AHCI_DLL := $(BUILD_DIR)/ProtonOS.Drivers.Ahci.dll
EXT2_DLL := $(BUILD_DIR)/ProtonOS.Drivers.Ext2.dll

# Targets
.PHONY: all clean native kernel test korlibdll testsupport ddk drivers image run deps install-deps check-deps

all: $(BUILD_DIR)/$(EFI_NAME)

# Create build directories
$(BUILD_DIR):
	mkdir -p $(BUILD_DIR)

# Assemble native code
$(NATIVE_OBJ): $(NATIVE_SRC) | $(BUILD_DIR)
	@echo "NASM $<"
	$(NASM) $(NASM_FLAGS) $< -o $@ -l $(BUILD_DIR)/native.lst

native: $(NATIVE_OBJ)

# Compile kernel (korlib + kernel C# sources together)
$(KERNEL_OBJ): $(KORLIB_SRC) $(KERNEL_SRC) | $(BUILD_DIR)
	@echo "BFLAT kernel"
	$(BFLAT) build $(BFLAT_FLAGS) -c -o $@ $(KORLIB_SRC) $(KERNEL_SRC)

kernel: $(KERNEL_OBJ)

# Build test assembly (standard .NET DLL for JIT testing)
$(TEST_DLL): $(TEST_DIR)/Program.cs $(TEST_DIR)/FullTest.csproj | $(BUILD_DIR)
	@echo "DOTNET build FullTest"
	dotnet build $(TEST_DIR)/FullTest.csproj -c Release -o $(BUILD_DIR) --nologo -v q

test: $(TEST_DLL)

# Build korlib IL assembly (for JIT generic instantiation)
$(KORLIB_DLL): $(KORLIB_SRC) $(KORLIB_DIR)/korlib.csproj | $(BUILD_DIR)
	@echo "DOTNET build korlib (IL assembly)"
	dotnet build $(KORLIB_DIR)/korlib.csproj -c Release -o $(BUILD_DIR) --nologo -v q

korlibdll: $(KORLIB_DLL)

# Build TestSupport library (cross-assembly test helpers)
TESTSUPPORT_SRC := $(call rwildcard,$(TESTSUPPORT_DIR),*.cs)
$(TESTSUPPORT_DLL): $(TESTSUPPORT_SRC) $(TESTSUPPORT_DIR)/TestSupport.csproj | $(BUILD_DIR)
	@echo "DOTNET build TestSupport"
	dotnet build $(TESTSUPPORT_DIR)/TestSupport.csproj -c Release -o $(BUILD_DIR) --nologo -v q

testsupport: $(TESTSUPPORT_DLL)

# Build DDK library (JIT-compiled at runtime)
DDK_SRC := $(call rwildcard,$(DDK_DIR),*.cs)
$(DDK_DLL): $(DDK_SRC) $(DDK_DIR)/DDK.csproj | $(BUILD_DIR)
	@echo "DOTNET build ProtonOS.DDK"
	dotnet build $(DDK_DIR)/DDK.csproj -c Release -o $(BUILD_DIR) --nologo -v q

ddk: $(DDK_DLL)

# Build Virtio common library
VIRTIO_SRC := $(call rwildcard,$(VIRTIO_DIR),*.cs)
$(VIRTIO_DLL): $(VIRTIO_SRC) $(VIRTIO_DIR)/Virtio.csproj $(DDK_DLL) | $(BUILD_DIR)
	@echo "DOTNET build ProtonOS.Drivers.Virtio"
	dotnet build $(VIRTIO_DIR)/Virtio.csproj -c Release -o $(BUILD_DIR) --nologo -v q

# Build Virtio-blk driver
VIRTIO_BLK_SRC := $(call rwildcard,$(VIRTIO_BLK_DIR),*.cs)
$(VIRTIO_BLK_DLL): $(VIRTIO_BLK_SRC) $(VIRTIO_BLK_DIR)/VirtioBlk.csproj $(VIRTIO_DLL) | $(BUILD_DIR)
	@echo "DOTNET build ProtonOS.Drivers.VirtioBlk"
	dotnet build $(VIRTIO_BLK_DIR)/VirtioBlk.csproj -c Release -o $(BUILD_DIR) --nologo -v q

# Build FAT filesystem driver
FAT_SRC := $(call rwildcard,$(FAT_DIR),*.cs)
$(FAT_DLL): $(FAT_SRC) $(FAT_DIR)/Fat.csproj $(DDK_DLL) | $(BUILD_DIR)
	@echo "DOTNET build ProtonOS.Drivers.Fat"
	dotnet build $(FAT_DIR)/Fat.csproj -c Release -o $(BUILD_DIR) --nologo -v q

# Build AHCI driver
AHCI_SRC := $(call rwildcard,$(AHCI_DIR),*.cs)
$(AHCI_DLL): $(AHCI_SRC) $(AHCI_DIR)/Ahci.csproj $(DDK_DLL) $(FAT_DLL) | $(BUILD_DIR)
	@echo "DOTNET build ProtonOS.Drivers.Ahci"
	dotnet build $(AHCI_DIR)/Ahci.csproj -c Release -o $(BUILD_DIR) --nologo -v q

# EXT2 filesystem driver
EXT2_SRC := $(call rwildcard,$(EXT2_DIR),*.cs)
$(EXT2_DLL): $(EXT2_SRC) $(EXT2_DIR)/Ext2.csproj $(DDK_DLL) | $(BUILD_DIR)
	@echo "DOTNET build ProtonOS.Drivers.Ext2"
	dotnet build $(EXT2_DIR)/Ext2.csproj -c Release -o $(BUILD_DIR) --nologo -v q

drivers: $(VIRTIO_DLL) $(VIRTIO_BLK_DLL) $(FAT_DLL) $(AHCI_DLL) $(EXT2_DLL)

# Link UEFI executable with debug symbols
$(BUILD_DIR)/$(EFI_NAME): $(NATIVE_OBJ) $(KERNEL_OBJ)
	@echo "LINK $@"
	$(LD) $(LD_FLAGS) -debug -out:$@ $^
	@file $@
	@echo "Generating GDB-compatible symbols from PDB..."
	@python3 tools/gen_elf_syms.py $(BUILD_DIR)/BOOTX64.pdb $(BUILD_DIR)/kernel_syms.elf

# Create boot image
image: $(BUILD_DIR)/$(EFI_NAME) $(TEST_DLL) $(KORLIB_DLL) $(TESTSUPPORT_DLL) $(DDK_DLL) $(VIRTIO_DLL) $(VIRTIO_BLK_DLL) $(FAT_DLL) $(AHCI_DLL) $(EXT2_DLL)
	@echo "Creating boot image..."
	dd if=/dev/zero of=$(BUILD_DIR)/boot.img bs=1M count=64 status=none
	mformat -i $(BUILD_DIR)/boot.img -F -v PROTONOS ::
	mmd -i $(BUILD_DIR)/boot.img ::/EFI
	mmd -i $(BUILD_DIR)/boot.img ::/EFI/BOOT
	mmd -i $(BUILD_DIR)/boot.img ::/drivers
	mcopy -i $(BUILD_DIR)/boot.img $(BUILD_DIR)/$(EFI_NAME) ::/EFI/BOOT/$(EFI_NAME)
	mcopy -i $(BUILD_DIR)/boot.img $(TEST_DLL) ::/FullTest.dll
	mcopy -i $(BUILD_DIR)/boot.img $(KORLIB_DLL) ::/korlib.dll
	mcopy -i $(BUILD_DIR)/boot.img $(TESTSUPPORT_DLL) ::/TestSupport.dll
	mcopy -i $(BUILD_DIR)/boot.img $(DDK_DLL) ::/ProtonOS.DDK.dll
	mcopy -i $(BUILD_DIR)/boot.img $(VIRTIO_DLL) ::/drivers/
	mcopy -i $(BUILD_DIR)/boot.img $(VIRTIO_BLK_DLL) ::/drivers/
	mcopy -i $(BUILD_DIR)/boot.img $(FAT_DLL) ::/drivers/
	mcopy -i $(BUILD_DIR)/boot.img $(AHCI_DLL) ::/drivers/
	mcopy -i $(BUILD_DIR)/boot.img $(EXT2_DLL) ::/drivers/
	@echo "Boot image: $(BUILD_DIR)/boot.img"
	@echo "Contents:"
	@mdir -i $(BUILD_DIR)/boot.img ::/

clean:
	rm -rf build/

# Toolchain directories
RUNTIME_DIR := tools/runtime
BFLAT_DIR := tools/bflat
NUGET_LOCAL := tools/nuget-local
ILC_VERSION := 10.0.0-local.2

# Install system dependencies (requires sudo)
install-deps:
	@tools/install-deps.sh

# Build bflat toolchain (runtime + bflat)
# Run after install-deps, or when runtime/bflat changes
deps: check-deps
	@echo "Building runtime..."
	cd $(RUNTIME_DIR) && TreatWarningsAsErrors=false ./build.sh \
		clr.nativeaotlibs+clr.nativeaotruntime+clr.alljits+clr.tools \
		-c Release -arch x64 /p:GenerateDocumentationFile=false
	@echo "Packing ILCompiler..."
	cd $(RUNTIME_DIR) && ./dotnet.sh pack bflat/pack/ILCompiler.Compiler.nuproj \
		-p:Version=$(ILC_VERSION) \
		-p:IntermediateOutputPath=artifacts/bin/coreclr/linux.x64.Release/ilc/
	@mkdir -p $(NUGET_LOCAL)
	cp $(RUNTIME_DIR)/artifacts/packages/Release/Shipping/BFlat.Compiler.$(ILC_VERSION).nupkg $(NUGET_LOCAL)/
	@echo "Building bflat..."
	cd $(BFLAT_DIR) && dotnet build src/bflat -c Release
	@echo "Dependencies built successfully."

# Quick dependency check (no install, just verify)
check-deps:
	@echo "Checking dependencies..."
	@command -v dotnet >/dev/null || (echo "ERROR: dotnet not found. Run 'make install-deps'" && exit 1)
	@dotnet --list-sdks | grep -q "^10\." || (echo "ERROR: .NET SDK 10.x not found. Run 'make install-deps'" && exit 1)
	@command -v clang >/dev/null || (echo "ERROR: clang not found. Run 'make install-deps'" && exit 1)
	@command -v cmake >/dev/null || (echo "ERROR: cmake not found. Run 'make install-deps'" && exit 1)
	@command -v nasm >/dev/null || (echo "ERROR: nasm not found. Run 'make install-deps'" && exit 1)
	@echo "All dependencies found."

# Run in QEMU
run: image
	./run.sh

# Show configuration
info:
	@echo "ARCH:       $(ARCH)"
	@echo "BUILD_DIR:  $(BUILD_DIR)"
	@echo "NATIVE_SRC: $(NATIVE_SRC)"
	@echo "KORLIB_SRC: $(words $(KORLIB_SRC)) files"
	@echo "KERNEL_SRC: $(words $(KERNEL_SRC)) files"
	@echo "EFI_NAME:   $(EFI_NAME)"
