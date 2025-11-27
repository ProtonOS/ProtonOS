# netos Makefile

# Default target architecture
ARCH ?= x64

# Directories
BUILD_DIR := build/$(ARCH)
NERNEL_DIR := src/nernel
MERNEL_DIR := src/mernel

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
LD_FLAGS := -subsystem:efi_application -entry:EfiMain

# Source files
NERNEL_SRC := $(wildcard $(NERNEL_DIR)/$(ARCH)/*.asm)
MERNEL_SRC := $(wildcard $(MERNEL_DIR)/*.cs)

# Object files
NERNEL_OBJ := $(patsubst $(NERNEL_DIR)/$(ARCH)/%.asm,$(BUILD_DIR)/nernel/%.obj,$(NERNEL_SRC))

# Targets
.PHONY: all clean nernel mernel image run

all: $(BUILD_DIR)/$(EFI_NAME)

# Create build directories
$(BUILD_DIR):
	mkdir -p $(BUILD_DIR)/nernel

# Assemble nernel
$(BUILD_DIR)/nernel/%.obj: $(NERNEL_DIR)/$(ARCH)/%.asm | $(BUILD_DIR)
	@echo "NASM $<"
	@mkdir -p $(dir $@)
	$(NASM) $(NASM_FLAGS) $< -o $@ -l $(BUILD_DIR)/nernel/$*.lst

nernel: $(NERNEL_OBJ)

# Link UEFI executable
$(BUILD_DIR)/$(EFI_NAME): $(NERNEL_OBJ)
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
	@echo "NERNEL_SRC: $(NERNEL_SRC)"
	@echo "NERNEL_OBJ: $(NERNEL_OBJ)"
	@echo "MERNEL_SRC: $(MERNEL_SRC)"
	@echo "EFI_NAME:   $(EFI_NAME)"
