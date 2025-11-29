# netos Makefile

# Default target architecture
ARCH ?= x64

# Directories
BUILD_DIR := build/$(ARCH)
KERNEL_DIR := src/kernel
NATIVE_DIR := src/native
NETLIB_DIR := src/netlib

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
BFLAT := bflat

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
NATIVE_SRC := $(wildcard $(NATIVE_DIR)/$(ARCH)/*.asm)
NETLIB_SRC := $(call rwildcard,$(NETLIB_DIR),*.cs)
KERNEL_SRC := $(call rwildcard,$(KERNEL_DIR),*.cs)

# Object files
NATIVE_OBJ := $(BUILD_DIR)/native.obj
KERNEL_OBJ := $(BUILD_DIR)/kernel.obj

# Targets
.PHONY: all clean native kernel image run

all: $(BUILD_DIR)/$(EFI_NAME)

# Create build directories
$(BUILD_DIR):
	mkdir -p $(BUILD_DIR)

# Assemble native code
$(NATIVE_OBJ): $(NATIVE_SRC) | $(BUILD_DIR)
	@echo "NASM $<"
	$(NASM) $(NASM_FLAGS) $< -o $@ -l $(BUILD_DIR)/native.lst

native: $(NATIVE_OBJ)

# Compile kernel (netlib + kernel C# sources together)
$(KERNEL_OBJ): $(NETLIB_SRC) $(KERNEL_SRC) | $(BUILD_DIR)
	@echo "BFLAT kernel"
	$(BFLAT) build $(BFLAT_FLAGS) -c -o $@ $(NETLIB_SRC) $(KERNEL_SRC)

kernel: $(KERNEL_OBJ)

# Link UEFI executable
$(BUILD_DIR)/$(EFI_NAME): $(NATIVE_OBJ) $(KERNEL_OBJ)
	@echo "LINK $@"
	$(LD) $(LD_FLAGS) -out:$@ $^
	@file $@

# Create boot image
image: $(BUILD_DIR)/$(EFI_NAME)
	@echo "Creating boot image..."
	dd if=/dev/zero of=$(BUILD_DIR)/boot.img bs=1M count=64 status=none
	mformat -i $(BUILD_DIR)/boot.img -F -v NETOS ::
	mmd -i $(BUILD_DIR)/boot.img ::/EFI
	mmd -i $(BUILD_DIR)/boot.img ::/EFI/BOOT
	mcopy -i $(BUILD_DIR)/boot.img $(BUILD_DIR)/$(EFI_NAME) ::/EFI/BOOT/$(EFI_NAME)
	@echo "Boot image: $(BUILD_DIR)/boot.img"

clean:
	rm -rf build/

# Run in QEMU
run: image
	./run.sh

# Show configuration
info:
	@echo "ARCH:       $(ARCH)"
	@echo "BUILD_DIR:  $(BUILD_DIR)"
	@echo "NATIVE_SRC: $(NATIVE_SRC)"
	@echo "NETLIB_SRC: $(words $(NETLIB_SRC)) files"
	@echo "KERNEL_SRC: $(words $(KERNEL_SRC)) files"
	@echo "EFI_NAME:   $(EFI_NAME)"
